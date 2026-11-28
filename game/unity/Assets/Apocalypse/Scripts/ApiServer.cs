using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Apocalypse
{

    public class ApiServer
    {
        public class Request
        {
            public string method, path, body;
            public NameValueCollection query;
            public int code = 200;
            public string contentType = "application/json; charset=utf-8";
            public string response = "{}";
            public readonly ManualResetEventSlim done = new ManualResetEventSlim(false);
        }

        HttpListener listener;
        Thread thread;
        volatile bool running;
        readonly ConcurrentQueue<Request> queue = new ConcurrentQueue<Request>();
        public string Prefix { get; private set; } = "";

        public bool Start(int port)
        {
            foreach (string prefix in new[] { $"http://*:{port}/", $"http://localhost:{port}/" })
            {
                try
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add(prefix);
                    listener.Start();
                    Prefix = prefix;
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Apocalypse] 無法監聽 {prefix}：{e.Message}");
                    listener = null;
                }
            }
            if (listener == null) return false;
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "ApocalypseApi" };
            thread.Start();
            Debug.Log("[Apocalypse] API 伺服器啟動：" + Prefix);
            return true;
        }

        public void Stop()
        {
            running = false;
            try { listener?.Stop(); listener?.Close(); } catch { }
            listener = null;
        }

        void Loop()
        {
            while (running)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { if (!running) return; continue; }
                // 轉播是一直開著的連線，給它自己的執行緒，不佔用處理 API 的執行緒
                if (ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant() == "/stream")
                    new Thread(() => Stream(ctx)) { IsBackground = true, Name = "ApocalypseStream" }.Start();
                else ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        const string WatchPage = @"<!doctype html>
<html lang=""zh-Hant""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>末日大逃殺 轉播</title>
<style>
html,body{margin:0;height:100%;background:#0d0b0d;color:#cfc6c0;font-family:system-ui,""Microsoft JhengHei"",sans-serif}
#v{position:fixed;inset:0;width:100%;height:100%;object-fit:contain;display:block}
#msg{position:fixed;left:0;right:0;bottom:16px;text-align:center;font-size:15px;opacity:.8;pointer-events:none}
#fs{position:fixed;right:12px;top:12px;background:#2a2326cc;color:#f6f1ee;border:0;border-radius:8px;padding:8px 12px;font-size:14px;cursor:pointer}
</style></head><body>
<img id=""v"" alt="""">
<div id=""msg"">連線中…</div>
<button id=""fs"">全螢幕</button>
<script>
const v=document.getElementById('v'),msg=document.getElementById('msg');
let got=false,polling=false;
function stream(){got=false;v.src='stream?t='+Date.now();setTimeout(()=>{if(!got&&!polling){polling=true;poll();}},6000);}
v.onload=()=>{got=true;msg.textContent='';};
v.onerror=()=>{if(polling)return;msg.textContent='連不上遊戲，重試中…';setTimeout(stream,2000);};
// 有些網路通道不支援連續串流，改成一張一張抓
async function poll(){
  msg.textContent='';
  while(true){
    try{const r=await fetch('frame.jpg?t='+Date.now(),{cache:'no-store'});
      if(!r.ok)throw 0;const u=URL.createObjectURL(await r.blob());
      await new Promise(ok=>{v.onload=ok;v.onerror=ok;v.src=u;});URL.revokeObjectURL(u);msg.textContent='';
    }catch(e){msg.textContent='連不上遊戲，重試中…';await new Promise(ok=>setTimeout(ok,2000));}
    await new Promise(ok=>setTimeout(ok,120));
  }
}
document.getElementById('fs').onclick=()=>{const e=document.documentElement;(document.fullscreenElement?document.exitFullscreen():(e.requestFullscreen||e.webkitRequestFullscreen).call(e));};
stream();
</script></body></html>";

        static void Cors(HttpListenerResponse res)
        {
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Cache-Control", "no-cache, no-store");
        }

        // MJPEG：瀏覽器的 <img> 會一直收新的 JPG 換上去
        void Stream(HttpListenerContext ctx)
        {
            var res = ctx.Response;
            Interlocked.Increment(ref StreamHub.viewers);
            try
            {
                Cors(res);
                res.ContentType = "multipart/x-mixed-replace; boundary=frame";
                res.SendChunked = true;
                var os = res.OutputStream;
                var crlf = new byte[] { 13, 10 };
                int seen = -1;
                while (running)
                {
                    var jpg = StreamHub.Next(ref seen, 2000);
                    if (jpg == null) continue;
                    var head = Encoding.ASCII.GetBytes("--frame\r\nContent-Type: image/jpeg\r\nContent-Length: " + jpg.Length + "\r\n\r\n");
                    os.Write(head, 0, head.Length);
                    os.Write(jpg, 0, jpg.Length);
                    os.Write(crlf, 0, 2);
                    os.Flush();
                }
            }
            catch { }   // 學員關掉網頁
            finally
            {
                Interlocked.Decrement(ref StreamHub.viewers);
                try { res.Abort(); } catch { }
            }
        }

        static bool ServeStatic(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            var res = ctx.Response;
            byte[] bytes;
            if (path == "/watch")
            {
                bytes = Encoding.UTF8.GetBytes(WatchPage);
                res.ContentType = "text/html; charset=utf-8";
            }
            else if (path == "/frame.jpg")
            {
                bytes = StreamHub.Latest(3000);
                if (bytes == null) { res.StatusCode = 503; res.Close(); return true; }
                res.ContentType = "image/jpeg";
            }
            else return false;
            Cors(res);
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
            return true;
        }

        void Handle(HttpListenerContext ctx)
        {
            try
            {
                var res = ctx.Response;
                res.AddHeader("Access-Control-Allow-Origin", "*");
                res.AddHeader("Access-Control-Allow-Headers", "*");
                res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                if (ctx.Request.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }
                if (ctx.Request.HttpMethod == "GET" && ServeStatic(ctx)) return;

                string body = "";
                if (ctx.Request.HasEntityBody)
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();

                var req = new Request
                {
                    method = ctx.Request.HttpMethod,
                    path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant(),
                    query = ctx.Request.QueryString,
                    body = body,
                };
                if (req.path == "") req.path = "/";
                // /state 有快取就直接回，不用等主執行緒（見 StateCache）
                string cacheWhy = null;
                if (req.method == "GET" && req.path == "/state" && StateCache.TryServe(req.query["token"], out var cached, out cacheWhy))
                    req.response = cached;
                else
                {
                    queue.Enqueue(req);
                    if (!req.done.Wait(5000)) { req.code = 503; req.response = "{\"ok\":false,\"error\":\"server busy\"}"; }
                }

                byte[] bytes = Encoding.UTF8.GetBytes(req.response);
                // 狀態很大（幾十 KB），學員從 Colab 經過通道連進來時先壓縮，傳輸量少八成左右
                string accept = ctx.Request.Headers["Accept-Encoding"] ?? "";
                if (bytes.Length > 1024 && accept.IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    using (var ms = new MemoryStream())
                    {
                        using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, true)) gz.Write(bytes, 0, bytes.Length);
                        bytes = ms.ToArray();
                    }
                    res.AddHeader("Content-Encoding", "gzip");
                }
                if (cacheWhy != null) res.AddHeader("X-State-Cache", cacheWhy);
                res.StatusCode = req.code;
                res.ContentType = req.contentType;
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
                res.Close();
            }
            catch (Exception e)
            {
                try { ctx.Response.Abort(); } catch { }
                Debug.LogWarning("[Apocalypse] HTTP 錯誤：" + e.Message);
            }
        }

        public void Pump(Action<Request> handler)
        {
            while (queue.TryDequeue(out var req))
            {
                try { if (Perf.On) using (Perf.S("req " + req.method + " " + req.path)) handler(req); else handler(req); }
                catch (Exception e)
                {
                    req.code = 500;
                    req.response = JsonUtility.ToJson(new OkResponse { ok = false, error = e.Message });
                    Debug.LogException(e);
                }
                req.done.Set();
            }
        }
    }
}
