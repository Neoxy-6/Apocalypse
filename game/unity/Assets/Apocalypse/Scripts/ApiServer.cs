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
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
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
                queue.Enqueue(req);
                if (!req.done.Wait(5000)) { req.code = 503; req.response = "{\"ok\":false,\"error\":\"server busy\"}"; }

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
                try { handler(req); }
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
