using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{
    // 加 -perf 啟動時，每 5 秒把各段每幀平均花的毫秒數寫進 log，找卡頓用
    public static class Perf
    {
        public static readonly bool On = System.Environment.GetCommandLineArgs().Contains("-perf");
        static readonly Dictionary<string, long> ticks = new Dictionary<string, long>();
        static readonly Dictionary<string, long> bytes = new Dictionary<string, long>();
        static int frames;
        static float nextLog = 5f;
        static readonly Stopwatch clock = Stopwatch.StartNew();

        public struct Scope : System.IDisposable
        {
            readonly string name; readonly long start, mem;
            public Scope(string n) { name = n; start = On ? clock.ElapsedTicks : 0; mem = On ? UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() : 0; }
            public void Dispose()
            {
                if (!On) return;
                ticks.TryGetValue(name, out var t);
                ticks[name] = t + clock.ElapsedTicks - start;
                bytes.TryGetValue(name, out var b);
                bytes[name] = b + System.Math.Max(0L, UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - mem);
            }
        }

        public static Scope S(string name) => new Scope(name);

        static long allocAt; static double gpuMs, cpuMs; static int timed, gcCount; static long memAt;
        static readonly FrameTiming[] ft = new FrameTiming[1];

        public static void Frame()
        {
            if (!On) return;
            frames++;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0) { gpuMs += ft[0].gpuFrameTime; cpuMs += ft[0].cpuFrameTime; timed++; }
            if (Time.unscaledTime < nextLog) return;
            float ms(long t) => (float)(t * 1000.0 / Stopwatch.Frequency / frames);
            var parts = string.Join("  ", ticks.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={ms(kv.Value):0.00}/{bytes[kv.Key] / 1024.0 / frames:0.0}KB"));
            long total = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            parts += $"  總配置={(total - allocAt) / 1024.0 / frames:0.0}KB/幀"; allocAt = total;
            var rs = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            string Root(Transform t) { var path = t.name; while (t.parent != null && t.parent.parent != null) { t = t.parent; } return (t.parent != null ? t.parent.name + "/" : "") + t.name; }
            var byRoot = string.Join(" ", rs.GroupBy(r => Root(r.transform) + (r.isPartOfStaticBatch ? "(合併)" : "")).OrderByDescending(g => g.Count()).Take(12).Select(g => $"{g.Key}:{g.Count()}"));
            UnityEngine.Debug.Log($"[Perf] 物件分布 {byRoot}");
            int mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
            UnityEngine.Debug.Log($"[Perf] fps={frames / 5f:0.0} gpu={(timed > 0 ? gpuMs / timed : -1):0.00} cpu={(timed > 0 ? cpuMs / timed : -1):0.00} renderers={rs.Count} mats={mats} gc={System.GC.CollectionCount(0) - gcCount} allocKB/frame={(UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - memAt) / 1024.0 / frames:0.0} {Screen.width}x{Screen.height} 每幀毫秒 {parts}");
            gpuMs = cpuMs = 0; timed = 0; gcCount = System.GC.CollectionCount(0); memAt = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            ticks.Clear(); bytes.Clear(); frames = 0; nextLog = Time.unscaledTime + 5f;
        }
    }
}
