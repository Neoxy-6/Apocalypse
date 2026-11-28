using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{
    // 當天多場比賽的總計分（給學員看 /scoreboard）
    // 每打完一場就記進 Results/scoreboard_<port>.json，重開 exe（換地圖）也不會掉
    // 名單在 Results/roster.txt，一行一個名字；清空總分在遊戲視窗連按兩下 K，或開 exe 時加 -resetscores 1
    public static class Scoreboard
    {
        [Serializable] public class Db { public List<MatchResult> matches = new List<MatchResult>(); public List<string> roster = new List<string>(); }

        static Db db;
        static int port;
        static DateTime rosterTime;

        static string FilePath => Path.Combine(ResultWriter.Folder, $"scoreboard_{port}.json");
        public static string RosterPath => Path.Combine(ResultWriter.Folder, "roster.txt");
        public static int MatchCount => db?.matches.Count ?? 0;

        public static void Init(int serverPort, bool reset)
        {
            port = serverPort;
            db = new Db();
            try { if (!reset && File.Exists(FilePath)) db = JsonUtility.FromJson<Db>(File.ReadAllText(FilePath)) ?? new Db(); }
            catch (Exception e) { Debug.LogWarning("[Apocalypse] 計分檔讀不了，從空的開始：" + e.Message); }
            if (reset) Save();
            try
            {
                Directory.CreateDirectory(ResultWriter.Folder);
                if (!File.Exists(RosterPath))
                    File.WriteAllText(RosterPath, "# 總排名的名單：一行一個名字，存檔後網頁會自動更新\n# 沒有名字時顯示所有玩家（伺服器自己加的 bot 不算）\n# # 開頭的行不算\n");
            }
            catch { }
            LoadRoster();
            Debug.Log($"[Apocalypse] 總排名 {FilePath}（{db.matches.Count} 場），名單 {RosterPath}");
        }

        static void LoadRoster()
        {
            try
            {
                var t = File.Exists(RosterPath) ? File.GetLastWriteTimeUtc(RosterPath) : DateTime.MinValue;
                if (t == rosterTime) return;
                rosterTime = t;
                db.roster = t == DateTime.MinValue ? new List<string>()
                    : File.ReadAllLines(RosterPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).Distinct().ToList();
            }
            catch { }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(ResultWriter.Folder);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(new Db { matches = db.matches }, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e) { Debug.LogWarning("[Apocalypse] 計分檔存不了：" + e.Message); }
        }

        static string Key(MatchResult m) => m.finished_at + "|" + m.seed;

        public static void Add(MatchResult m)
        {
            if (db == null || m?.ranking == null || m.ranking.Length == 0) return;
            if (db.matches.Exists(x => Key(x) == Key(m))) return;
            db.matches.Add(m);
            Save();
        }

        public static void ResetScores()
        {
            if (db == null) return;
            db.matches.Clear();
            Save();
            Debug.Log("[Apocalypse] 總排名已清空");
        }

        public static string Json()
        {
            if (db == null) return "{\"matches\":[],\"roster\":[]}";
            LoadRoster();
            return JsonUtility.ToJson(db);
        }
    }
}
