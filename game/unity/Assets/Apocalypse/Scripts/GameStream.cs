using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Apocalypse
{
    // 轉播：把遊戲畫面（含介面）縮小、壓成 JPG，給 /stream 和 /frame.jpg 用
    // 沒人在看就完全不截圖，不影響遊戲效能
    public static class StreamHub
    {
        static readonly object gate = new object();
        static byte[] latest;
        static int version;
        public static int viewers;
        static long lastPoll;

        static int lastSeq = -1;

        // 好幾張同時在壓縮，先壓好的不一定是比較新的，舊的就丟掉
        public static void Publish(byte[] jpg, int seq)
        {
            lock (gate)
            {
                if (seq <= lastSeq) return;
                lastSeq = seq; latest = jpg; version++; Monitor.PulseAll(gate);
            }
        }

        // 等下一張新畫面；逾時回傳 null
        public static byte[] Next(ref int seen, int timeoutMs)
        {
            lock (gate)
            {
                while (version == seen)
                    if (!Monitor.Wait(gate, timeoutMs)) return null;
                seen = version;
                return latest;
            }
        }

        public static byte[] Latest(int waitMs)
        {
            Interlocked.Exchange(ref lastPoll, DateTime.UtcNow.Ticks);
            lock (gate)
            {
                if (latest == null) Monitor.Wait(gate, waitMs);
                return latest;
            }
        }

        public static bool Wanted =>
            Volatile.Read(ref viewers) > 0 || DateTime.UtcNow.Ticks - Interlocked.Read(ref lastPoll) < TimeSpan.TicksPerSecond * 3;
    }

    // 轉播畫面固定 16:9（預設 1920×1080），跟遊戲視窗大小無關：
    // GameView 在 LateUpdate 另外把場景畫進 Target，OnGUI 再把介面照 16:9 版面畫上去，畫完這裡讀回來壓 JPG
    public class GameStream : MonoBehaviour
    {
        public RenderTexture Target { get; private set; }
        public bool SceneReady { get; private set; }
        bool guiDone;
        int waited;
        const int MaxInFlight = 3;   // 最多三張同時在讀回、壓縮，才跑得到 60 張
        int inFlight, seq;
        readonly System.Collections.Concurrent.ConcurrentBag<byte[]> pool = new System.Collections.Concurrent.ConcurrentBag<byte[]>();   // 重複用像素記憶體
        float next;
        bool supported;

        IEnumerator Start()
        {
            supported = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && SystemInfo.supportsAsyncGPUReadback;
            if (!supported) yield break;
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                try { Send(); }
                catch (Exception e) { SceneReady = guiDone = false; Debug.LogWarning("[Apocalypse] 轉播截圖失敗：" + e.Message); }
            }
        }

        public bool Due => supported && !SceneReady && Volatile.Read(ref inFlight) < MaxInFlight && StreamHub.Wanted && Time.unscaledTime >= next;

        public void RenderScene(Camera cam)
        {
            var cfg = GameManager.Instance.cfg;
            next = Time.unscaledTime + 1f / Mathf.Clamp(cfg.streamFps, 1, 60) - 0.004f;
            int w = Mathf.Clamp(cfg.streamWidth <= 0 ? 1920 : cfg.streamWidth, 320, 3840) & ~1;
            int h = (w * 9 / 16) & ~1;
            if (Target == null || Target.width != w || Target.height != h)
            {
                if (Target != null) { Target.Release(); Destroy(Target); }
                Target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing) };
                Target.Create();
            }
            var old = cam.targetTexture;
            cam.targetTexture = Target;
            cam.aspect = (float)w / h;
            cam.Render();
            cam.targetTexture = old;
            cam.ResetAspect();
            SceneReady = true; guiDone = false; waited = 0;
        }

        public void GuiDone() { if (SceneReady) guiDone = true; }

        void Send()
        {
            if (!SceneReady) return;
            if (!guiDone) { if (++waited > 3) SceneReady = false; return; }   // 這幀沒畫到介面（例如視窗縮到最小），放棄重來
            SceneReady = guiDone = false;
            var cfg = GameManager.Instance.cfg;
            int w = Target.width, h = Target.height;
            Interlocked.Increment(ref inFlight);
            int quality = Mathf.Clamp(cfg.streamQuality, 10, 100), id = ++seq;
            AsyncGPUReadback.Request(Target, 0, TextureFormat.RGBA32, r =>
            {
                if (r.hasError) { Interlocked.Decrement(ref inFlight); return; }
                var data = r.GetData<byte>();
                if (!pool.TryTake(out var buf) || buf.Length != data.Length) buf = new byte[data.Length];
                data.CopyTo(buf);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { StreamHub.Publish(ImageConversion.EncodeArrayToJPG(buf, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, quality), id); }
                    catch (Exception e) { Debug.LogWarning("[Apocalypse] 轉播壓縮失敗：" + e.Message); }
                    finally { pool.Add(buf); Interlocked.Decrement(ref inFlight); }
                });
            });
        }

        void OnDestroy()
        {
            if (Target != null) Target.Release();
        }
    }
}
