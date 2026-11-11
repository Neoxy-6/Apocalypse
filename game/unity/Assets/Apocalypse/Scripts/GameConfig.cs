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

        public int mapCols = 12;
        public int mapRows = 10;
        public int bridges = 2;

        public float batchChance = 0.6f;
        public int batchMin = 3, batchMax = 5;
        public float batchBonusRatio = 1.0f;
        public float taskInterval = 9f;
        public float announceLead = 60f;

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

        public float fuelPerPlayer = 0.5f, fuelSparePerPlayer = 0.3f;
        public int fuelSpare = 4, fuelReward = 80;
        public float boardTime = 45f;

        public float stunTime = 40f;
        public float herbGuardTime = 30f, herbCityDrop = 20f;
        public int gasFuelBonus = 3;
        public float gasBlastRadius = 5f, gasBlastDamage = 60f;

        public float partWeightReward = 12f, rescueRewardMul = 2f, incidentRewardMul = 1f;
        public float partDecay = 0f;
        public float urgentChance = 0f, urgentChancePerPhase = 0f, urgentBonusRatio = 1.2f;
        // 零件的期限：出現後 partDueSlack 秒 + 來回路程（距離 × 2 ÷ 速度）；過期每分鐘少 lateDecayPerMin 份 reward，最低剩 lateFloor
        public float partDueSlack = 15f, partDueTripMul = 2f, lateDecayPerMin = 1f, lateFloor = 0.3f;
        // 套件：零件有 setChance 的機率和另外 1–2 件組成一套；同一趟一起交多拿整套分數的 setBonusRatio
        public float setChance = 0.35f, setBonusRatio = 1f, setRadius = 40f;   // 同一套的零件來自第一件 setRadius 格內的工廠
        public int setMin = 3, setMax = 5;
        public bool setCrossRiver = true;     // 套件至少有一件在河的另一邊
        // 加工：procChance 的零件要先送到另一間工廠加工，送到後 procDelay 秒才能撿去撤離點
        public float procChance = 0.35f, procDelay = 8f;
        // 全班還缺越多零件，新出現的零件越值錢：最多多 teamNeedBonus
        public float teamNeedBonus = 0.5f;
        public float comboWindow = 5f;
        public int comboStep = 0, comboMax = 8;
        public float difficulty = 1.8f;
        public float earliestDepart = 480f;
        public float departDelay = 60f;
        public int escapeBonus = 100;
        public float earlyBonusPerSec = 0.5f;
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

        public float planCooldown = 30f;
        public int maxPlanSteps = 200;
        public int capacity = 20;
        public float playerSpeed = 3f;
        public float loadSpeedPenalty = 0.035f;
        public int fuelWeight = 2;
        public int weaponWeight = 1, weaponShots = 20, weaponStock = 4, killReward = 8;
        public float weaponRange = 4.5f, weaponInterval = 1.2f;
        public float downedTime = 15f;
        public int respawnPenalty = 15;
        public float respawnHp = 70f;
        public int survivalBonus = 120;
        public int reviveBonus = 60;

        public float zombieSpeed = 1.6f;
        public float zombieSpeedPerPhase = 0.2f;
        public bool fuelNear = true;          // 油桶放在安全區中段，不放在地圖邊緣
        public float zombieSight = 5.5f;
        public float zombieLoseMul = 1.5f;     // 追的人跑出視線 × 這個倍數就放棄，回巡邏路線
        public float dangerHorizon = 60f;     // st.danger 預測未來幾秒
        public float zombieAttackRange = 0.9f;
        public float zombieDamage = 8f;
        public float zombieInfection = 2.5f;
        public float zombieChaseMul = 1.25f;
        public float zombieCapBase = 4f, zombieCapGrowth = 15f, zombieSpawnMul = 1.6f;

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
