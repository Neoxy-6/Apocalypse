using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace Apocalypse
{
    // /state 的快取：主執行緒每 0.1 秒組一份大家共用的狀態，每個人的 you 另外組好，
    // 網路執行緒收到 /state 直接拼起來回，不用排隊等主執行緒的下一幀，也不用每個請求重組一次 150 KB 的 JSON。
    // 內容跟 BuildState(sim, viewer) 一模一樣，最多舊 0.1 秒（學生每秒抓一次）。
    public static class StateCache
    {
        const long EveryMs = 100;    // 多久重組一次
        const long StaleMs = 500;    // 超過這麼久沒更新（主執行緒卡住、暫停中），就退回原本排隊的做法
        const long IdleMs = 3000;    // 這麼久沒人抓 /state 就不組，省主執行緒

        sealed class Snapshot
        {
            public string prefix, suffix, noViewer;
            public Dictionary<string, string> you;
            public long builtAt;
        }

        static readonly Stopwatch clock = Stopwatch.StartNew();
        static volatile Snapshot snap;
        static long lastAsk = -IdleMs * 2;
        const string Slot = "__apocalypse_you_slot__";
        // 開 exe 時加 -nostatecache 就關掉，退回每個請求都排隊重組（出問題時的備案、比較效能用）
        public static readonly bool Enabled = !System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a.ToLowerInvariant() == "-nostatecache");

        // 網路執行緒呼叫：有新鮮的快取就直接給，回傳 false 代表要走原本的排隊流程
        public static bool TryServe(string token, out string json) => TryServe(token, out json, out _);

        // why：hit / none（還沒組）/ stale（太舊）/ unknown（快取裡沒有這個 token）
        public static bool TryServe(string token, out string json, out string why)
        {
            long now = clock.ElapsedMilliseconds;
            Interlocked.Exchange(ref lastAsk, now);
            json = null;
            if (!Enabled) { why = "off"; return false; }
            var s = snap;
            why = s == null ? "none" : now - s.builtAt > StaleMs ? "stale" : null;
            if (why != null) return false;
            string you;
            if (string.IsNullOrEmpty(token)) you = s.noViewer;
            else if (!s.you.TryGetValue(token, out you)) { why = "unknown"; return false; }   // 剛加入、快取還沒有他：走排隊，免得回一個 id = -1 被當成 token 失效
            json = s.prefix + you + s.suffix;
            why = "hit";
            return true;
        }

        public static void Invalidate() => snap = null;

        // 主執行緒每幀呼叫
        public static void Update(GameSim sim)
        {
            if (!Enabled) return;
            long now = clock.ElapsedMilliseconds;
            if (now - Interlocked.Read(ref lastAsk) > IdleMs) { snap = null; return; }
            var old = snap;
            if (old != null && now - old.builtAt < EveryMs) return;

            var state = Protocol.BuildState(sim, null);
            var slot = new YouDto { name = Slot };
            state.you = slot;
            string full = JsonUtility.ToJson(state), slotJson = JsonUtility.ToJson(slot);
            int i = full.IndexOf(slotJson, System.StringComparison.Ordinal);
            if (i < 0) { snap = null; return; }

            var you = new Dictionary<string, string>(sim.teams.Count);
            foreach (var t in sim.teams)
                if (!string.IsNullOrEmpty(t.token) && t.units.Count > 0)
                    you[t.token] = JsonUtility.ToJson(Protocol.YouOf(sim, t));
            snap = new Snapshot
            {
                prefix = full.Substring(0, i), suffix = full.Substring(i + slotJson.Length),
                noViewer = JsonUtility.ToJson(new YouDto()), you = you, builtAt = clock.ElapsedMilliseconds,
            };
        }
    }
}
