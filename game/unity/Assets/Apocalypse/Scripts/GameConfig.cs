using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    [Serializable]
    public class GameConfig
    {

        public int port = 8080;
        public string adminKey = "admin";
        public string publicUrl = "";
        public string mapFile = "";

        public int mapCols = 10;
        public int mapRows = 8;
        public int bridges = 2;

        public float batchChance = 0.6f;
        public int batchMin = 3, batchMax = 5;
        public float batchBonusRatio = 1.0f;
        public float taskInterval = 9f;
        public float announceLead = 30f;

        public float cityRate = 0.55f;
        public float spreadAmount = 0.15f;
        public float spreadGrow = 0.02f;

        public bool coop = true;
        public float coopCityRate = 1.35f;
        public float cityPerPlayer = 0.02f;
        public float cityPerPoint = 0.03f;
        public int classBonus = 200;
        public float urgentFailCity = 3f;
        public float rescueFailCity = 2f;
        public float coopTaskPlayers = 3f;

        public bool escape = true;
        public float partsBase = 0f;
        public float partsPerPlayer = 7f;
        public float partsCrowding = 0.15f;

        public float incidentInterval = 30f;
        public float roadblockInterval = 45f, roadblockTime = 60f;
        public int civilianZombies = 1;
        public float civiliansPerPlayer = 2.5f;
        public int trappedCivilians = 4;
        public float generatorWeight = 0.04f, generatorCooldown = 90f;
        public float speedBoost = 1.2f, speedBoostTime = 60f;
        public float guardDownTime = 60f;
        public float guardRange = 5f, guardFireInterval = 1f;

        public float fuelPerPlayer = 0.6f, fuelSparePerPlayer = 0.3f;
        public int fuelSpare = 4, fuelReward = 80;
        public float boardTime = 45f;

        public float stunTime = 40f;

        public float partWeightReward = 12f, rescueRewardMul = 2f, incidentRewardMul = 2f;
        public float partDecay = 80f;
        public float comboWindow = 25f;
        public int comboStep = 10, comboMax = 5;
        public float difficulty = 1.2f;
        public float earliestDepart = 480f;
        public float departDelay = 60f;
        public int escapeBonus = 200;
        public float earlyBonusPerSec = 1f;
        public float zoneStart = 90f, zoneEnd = 540f, zoneMinRadius = 7f;
        public float zoneDamage = 3f, zoneInfection = 1f;

        public int squadSize = 1;
        public int seed = 42;
        public float matchDuration = 600f;
        public float timeScale = 1f;
        public int randomBots = 0;
        public int greedyBots = 0;
        public float botReactionTime = 1f;
        public bool autoStart = false;
        public int autoStartPlayers = 1;
        public bool allowLateJoin = true;

        public float autoLoopDelay = 0f;
        public int[] practiceSeeds = new int[0];

        public float planCooldown = 15f;
        public int maxPlanSteps = 200;
        public int capacity = 10;
        public float playerSpeed = 3f;
        public float loadSpeedPenalty = 0.05f;
        public int fuelWeight = 4;
        public int weaponWeight = 4, weaponShots = 8, weaponStock = 2, killReward = 5;
        public float weaponRange = 3.5f, weaponInterval = 1.2f;
        public float downedTime = 15f;
        public int respawnPenalty = 50;
        public float respawnHp = 70f;
        public int survivalBonus = 120;
        public int reviveBonus = 60;

        public float zombieSpeed = 1.4f;
        public float zombieSpeedPerPhase = 0.2f;
        public float zombieSight = 5.5f;
        public float zombieAttackRange = 0.9f;
        public float zombieDamage = 5.5f;
        public float zombieInfection = 2.5f;
        public float zombieChaseMul = 1.25f;
        public float zombieCapBase = 6f, zombieCapGrowth = 40f, zombieSpawnMul = 1.6f;

        public float actionNoiseRadius = 10f;
        public float actionNoiseDuration = 12f;
        public float survivorNoiseRadius = 8f;
        public float screamRadius = 12f;

        public static GameConfig Load()
        {
            var cfg = new GameConfig();
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "config.json");
                if (File.Exists(path)) JsonUtility.FromJsonOverwrite(File.ReadAllText(path), cfg);
            }
            catch (Exception e) { Debug.LogWarning("[Apocalypse] config.json 讀取失敗：" + e.Message); }

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                string v = args[i + 1];
                switch (args[i].ToLowerInvariant())
                {
                    case "-port": int.TryParse(v, out cfg.port); break;
                    case "-seed": int.TryParse(v, out cfg.seed); break;
                    case "-map": cfg.mapFile = v; break;
                    case "-duration": float.TryParse(v, out cfg.matchDuration); break;
                    case "-timescale": float.TryParse(v, out cfg.timeScale); break;
                    case "-adminkey": cfg.adminKey = v; break;
                    case "-publicurl": cfg.publicUrl = v; break;
                    case "-randombots": int.TryParse(v, out cfg.randomBots); break;
                    case "-greedybots": int.TryParse(v, out cfg.greedyBots); break;
                    case "-squad": int.TryParse(v, out cfg.squadSize); break;
                    case "-autoloop": float.TryParse(v, out cfg.autoLoopDelay); break;
                    case "-latejoin": cfg.allowLateJoin = v != "0"; break;
                    case "-coop": cfg.coop = v != "0"; break;
                    case "-escape": cfg.escape = v != "0"; break;
                    case "-difficulty": float.TryParse(v, out cfg.difficulty); break;
                    case "-seeds":
                        cfg.practiceSeeds = v.Split(',').Select(x => int.TryParse(x, out int s) ? s : -1).Where(s => s >= 0).ToArray();
                        if (cfg.practiceSeeds.Length > 0) cfg.seed = cfg.practiceSeeds[0];
                        break;
                    case "-autostart":
                        cfg.autoStart = true;
                        int.TryParse(v, out cfg.autoStartPlayers);
                        break;
                }
            }
            return cfg;
        }
    }
}
