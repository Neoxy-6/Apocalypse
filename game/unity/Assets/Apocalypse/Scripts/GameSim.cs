using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public class GameSim
    {
        public static readonly string[] PhaseNames = { "前期", "中期", "後期", "終局" };
        public static readonly string[] PhaseKeys = { "early", "mid", "late", "final" };
        static readonly float[] PhaseEnds = { 0.25f, 0.5833f, 0.8333f, 1f };

        static readonly Color[] Palette =
        {
            new Color(0.30f, 0.76f, 0.97f), new Color(1.00f, 0.44f, 0.26f), new Color(0.73f, 0.41f, 0.78f),
            new Color(1.00f, 0.84f, 0.31f), new Color(0.40f, 0.73f, 0.42f), new Color(0.94f, 0.38f, 0.57f),
            new Color(0.31f, 0.82f, 0.77f), new Color(0.98f, 0.62f, 0.13f), new Color(0.62f, 0.66f, 1.00f),
            new Color(0.80f, 0.86f, 0.22f),
        };

        public readonly GameConfig cfg;
        public CityMap map;
        public System.Random rng;
        public MatchStatus status;
        public float time;
        public float cityInfection;
        public string endReason = "";
        public int mapVersion;
        public string lastResultJson = "";
        public bool classWon;

        public int extraction;
        public int parts, partsRequired;
        public float readyAt = -1f, departAt = -1f;
        public float zoneMax;
        public int guardKills;
        float guardCooldown;

        public readonly List<Vector3> guardShots = new List<Vector3>();
        public int evacuated;
        public float speedBoostUntil = -1f, guardDownUntil = -1f;
        public float zombieStunUntil = -1f;
        float medevacTimer, medevacNext;
        int medevacRolls;
        public bool heliBoost;
        public int fuel, fuelRequired;
        int fuelDiscount;

        public bool NoRespawn => cfg.escape && readyAt >= 0;
        public readonly List<Incident> incidents = new List<Incident>();
        int nextIncidentId;
        int lastPhase;
        float lastGeneratorBreak;
        float lastDt;
        float incidentTimer;

        static readonly Dictionary<string, (string name, float need, int minPeople, float limit, int reward)> IncidentTypes =
            new Dictionary<string, (string, float, int, float, int)>
            {
                ["outbreak"] = ("感染爆發", 8f, 1, 60f, 60),
                ["bridge"] = ("橋樑損毀", 10f, 1, 60f, 50),
                ["airdrop"] = ("物資空投", 4f, 1, 45f, 40),
                ["trapped"] = ("受困人群", 20f, 2, 75f, 120),
                ["guard"] = ("撤離點告急", 8f, 1, 50f, 60),
                ["heli"] = ("直升機故障", 24f, 2, 60f, 80),
                ["medevac"] = ("醫療隊抵達", 6f, 1, 50f, 80),
                ["napalm"] = ("呼叫空襲", 8f, 1, 50f, 60),
                ["decoy"] = ("聲波誘餌", 20f, 2, 60f, 90),
                ["chest"] = ("神秘寶箱", 3f, 1, 40f, 150),
                ["boss"] = ("殭屍王", 30f, 3, 75f, 200),

                ["herbs"] = ("草藥採集", 6f, 1, 50f, 70),
                ["gas"] = ("加油站", 5f, 1, 60f, 80),
                ["rubble"] = ("瓦礫堆", 20f, 2, 70f, 100),
                ["bell"] = ("教堂鐘聲", 8f, 1, 50f, 70),

                ["generator"] = ("發電機故障", 15f, 1, 9999f, 60),
            };

        static readonly Dictionary<string, (string ok, string fail)> IncidentText = new Dictionary<string, (string, string)>
        {
            ["outbreak"] = ("壓制了感染爆發!", "感染爆發失控了..."),
            ["bridge"] = ("修好了斷掉的橋!", "橋沒修好..."),
            ["airdrop"] = ("搶到了空投物資!", "空投物資被搶走了..."),
            ["trapped"] = ("救出了受困的民眾!", "沒能救出受困的民眾..."),
            ["guard"] = ("守住了撤離點!", "撤離點失守了!"),
            ["heli"] = ("排除了直升機故障!", "直升機故障沒排除!"),
            ["medevac"] = ("接應到醫療隊!", "沒接應到醫療隊..."),
            ["napalm"] = ("呼叫的空襲命中了!", "空襲沒呼叫成功..."),
            ["decoy"] = ("啟動了聲波誘餌!", "聲波誘餌沒啟動..."),
            ["chest"] = ("打開了神秘寶箱!", "沒搶到神秘寶箱..."),
            ["boss"] = ("打倒了殭屍王!", "殭屍王沒被打倒..."),
            ["herbs"] = ("採到了草藥!", "草藥沒採集到..."),
            ["gas"] = ("裝滿了油桶!", "加油站的油被搶光了..."),
            ["rubble"] = ("挖開了瓦礫堆!", "瓦礫堆沒挖開!"),
            ["bell"] = ("敲響了教堂的鐘!", "教堂的鐘沒敲響!"),
            ["generator"] = ("修好了發電機!", "發電機沒修好"),
        };

        public static readonly string[] Districts = { "jungle", "city", "ruins", "town" };
        public static readonly string[] DistrictNames = { "雨林", "城市", "廢墟", "小城鎮" };
        static readonly string[] DistrictEvent = { "herbs", "gas", "rubble", "bell" };

        public readonly List<Player> players = new List<Player>();
        public readonly List<GameTask> tasks = new List<GameTask>();
        public readonly List<Zombie> zombies = new List<Zombie>();
        public readonly List<GameEvent> events = new List<GameEvent>();

        public readonly Dictionary<int, Batch> batches = new Dictionary<int, Batch>();
        int nextTaskId, nextZombieId, nextPlayerId, nextBatchId;
        float taskTimer, spreadTimer, zombieTimer, eventTimer, restockTimer, botTimer;

        const int StreamTask = 1, StreamEvent = 2, StreamSpread = 3, StreamZombie = 4;
        int taskSlot, eventIndex, spreadTick, zombieSpawnIndex;

        void UseStream(int stream, int index)
        {
            unchecked { rng = new System.Random(cfg.seed * 73856093 ^ stream * 19349663 ^ (index + 1) * 83492791); }
        }

        public GameSim(GameConfig cfg)
        {
            this.cfg = cfg;
            ResetWorld();
        }

        public int Phase
        {
            get
            {
                float f = time / cfg.matchDuration;
                for (int i = 0; i < PhaseEnds.Length; i++) if (f < PhaseEnds[i]) return i;
                return 3;
            }
        }

        public float TimeLeft => Mathf.Max(0f, cfg.matchDuration - (ClockStopped ? departAt - cfg.boardTime : time));
        public bool ClockStopped => cfg.escape && departAt >= 0;

        public void ResetWorld()
        {
            rng = new System.Random(cfg.seed);
            map = new CityMap(cfg.mapCols, cfg.mapRows, cfg.bridges);
            map.Generate(rng);
            mapVersion++;
            status = MatchStatus.Lobby;
            time = 0; cityInfection = 0; endReason = ""; classWon = false;

            if (cfg.escape)
                foreach (var n in map.nodes)
                    if (n.type == NodeType.Shelter && n.id != map.startNode) n.type = NodeType.Road;
            extraction = map.startNode;
            var drng = new System.Random(unchecked(cfg.seed * 7919 + 17));
            int west = drng.Next(4), east = drng.Next(4);
            foreach (var n in map.nodes) n.district = n.id % map.cols <= map.riverCol ? west : east;
            var ex = map.nodes[extraction].pos;
            zoneMax = map.nodes.Max(n => Vector2.Distance(n.pos, ex)) + 1f;
            parts = partsRequired = 0; readyAt = departAt = -1f; guardKills = 0; guardCooldown = 0f; guardShots.Clear();
            incidents.Clear(); evacuated = 0; speedBoostUntil = guardDownUntil = -1f; heliBoost = false;
            nextIncidentId = 0; incidentTimer = 25f; lastPhase = 0; lastGeneratorBreak = -999f;
            zombieStunUntil = -1f; medevacTimer = 5f; medevacNext = 0f; medevacRolls = 0;
            fuel = fuelRequired = fuelDiscount = 0;
            tasks.Clear(); zombies.Clear(); events.Clear(); batches.Clear(); noises.Clear();
            players.RemoveAll(p => p.team.IsBot);
            teams.RemoveAll(t => t.IsBot);
            foreach (var t in teams) t.rank = 0;
            nextTaskId = nextZombieId = nextBatchId = 0;
            taskSlot = eventIndex = spreadTick = zombieSpawnIndex = 0;
            taskTimer = spreadTimer = zombieTimer = restockTimer = botTimer = 0;
            eventTimer = 20f;
            foreach (var p in players) ResetPlayer(p);
            Log("system", $"大廳開放中，地圖種子 {cfg.seed}");
        }

        void ResetPlayer(Player p)
        {
            p.node = map.startNode; p.nextNode = -1; p.progress = 0;
            p.pos = map.nodes[p.node].pos;
            p.hp = 100; p.infection = 0; p.cargo.Clear(); p.medkits = 0;
            p.state = PlayerState.Active; p.downedTimer = 0; p.eliminatedAt = -1; p.eliminatedReason = "";
            p.score = p.delivered = p.rescued = p.revives = 0;
            p.plan.Clear(); p.planIndex = 0; p.pending = null; p.actionTimer = 0;
            p.lastError = ""; p.lastPlanTime = -999; p.idleSince = -1; p.rank = 0;
            p.noiseTimer = 0; p.lastScream = -999;
            p.downs = 0; p.evacuated = false; p.cityReduced = 0; p.parts = 0; p.escaped = false; p.fixedCount = 0; p.fuel = 0; p.combo = 0; p.lastScoreAt = -999f; p.protectUntil = -1f; p.revivedAt = -999f;
        }

        public readonly List<Team> teams = new List<Team>();
        int nextTeamId;

        public Team AddTeam(string name, Func<int, IBot> botFactory = null, string botKind = "")
        {
            var team = new Team
            {
                id = nextTeamId++,
                name = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim(),
                token = Guid.NewGuid().ToString("N"),
                botKind = botKind,
            };
            team.color = Palette[team.id % Palette.Length];
            int n = Mathf.Max(1, cfg.squadSize);
            for (int i = 0; i < n; i++)
            {
                var p = new Player
                {
                    id = nextPlayerId++,
                    name = n > 1 ? $"{team.name}#{i + 1}" : team.name,
                    token = team.token, color = team.color,
                    bot = botFactory?.Invoke(i), botKind = botKind,
                    team = team, unit = i,
                };
                ResetPlayer(p);
                players.Add(p);
                team.units.Add(p);
            }
            teams.Add(team);
            if (cfg.escape && status == MatchStatus.Running)
                partsRequired = Mathf.Max(partsRequired, PartsNeeded(players.Count));
            Log("join", n > 1 ? $"{team.name} 小隊（{n} 名傭兵）加入比賽" : $"{team.name} 加入比賽");
            return team;
        }

        public Team FindByToken(string token) =>
            string.IsNullOrEmpty(token) ? null : teams.FirstOrDefault(t => t.token == token);

        public Team TeamById(int id) => teams.FirstOrDefault(t => t.id == id);

        float SquadTaskScale => cfg.coop
            ? Mathf.Max(1f, players.Count / cfg.coopTaskPlayers)
            : 1f + 0.5f * (Mathf.Max(1, cfg.squadSize) - 1);

        public void StartMatch()
        {
            if (status != MatchStatus.Lobby) return;
            for (int i = 0; i < cfg.randomBots; i++) { int k = i; AddTeam($"Bot-Random-{i + 1}", u => new RandomBot(cfg.seed + k * 31 + u), "random"); }
            for (int i = 0; i < cfg.greedyBots; i++) AddTeam($"Bot-Greedy-{i + 1}", u => new GreedyBot(), "greedy");
            status = MatchStatus.Running;
            if (cfg.escape)
            {
                partsRequired = PartsNeeded(players.Count);
                foreach (var g in Generators) BreakGenerator(g);
                SpawnCivilians();
                Log("incident", "發電機都沒電！先修好發電機，工廠才會生產零件", Generators.FirstOrDefault()?.id ?? -1);
            }
            for (int i = 0; i < Mathf.RoundToInt(6 * map.Scale * SquadTaskScale); i++) { UseStream(StreamTask, taskSlot++); SpawnTask(false, 0f); }
            for (int i = 0; i < Mathf.RoundToInt(2 * map.Scale); i++) { UseStream(StreamZombie, zombieSpawnIndex++); SpawnZombieAtInfected(); }
            Log("system", cfg.escape ? $"比賽開始! 共 {players.Count} 位玩家，{cfg.matchDuration / 60f:0.#} 分鐘內修好直升機逃出去!"
                                    : $"比賽開始！共 {players.Count} 位玩家，城市將在 {cfg.matchDuration / 60f:0.#} 分鐘後全面淪陷");
        }

        public void Log(string type, string text, int node = -1)
        {
            events.Add(new GameEvent { time = time, type = type, text = text, node = node });
            if (events.Count > 200) events.RemoveAt(0);
        }

        public void Tick(float dt)
        {
            if (status != MatchStatus.Running || dt <= 0) return;
            time += dt;
            lastDt = dt;

            UpdateRoads();
            UpdateInfection(dt);
            UpdateTasks(dt);
            UpdateBatches();
            UpdateZombieSpawns(dt);
            if (cfg.escape) { CheckMedevac(dt); UpdateGenerators(); SpawnIncidents(dt); } else UpdateEvents(dt);
            UpdateRestock(dt);
            UpdateBots(dt);

            foreach (var p in players) UpdatePlayer(p, dt);
            if (cfg.escape) UpdateIncidents();
            UpdateNoises(dt);
            if (time >= zombieStunUntil)
                foreach (var z in zombies) UpdateZombie(z, dt);
            if (cfg.escape && time >= guardDownUntil)
            {
                guardKills += zombies.RemoveAll(z => z.node == extraction && z.nextNode < 0);

                guardCooldown -= dt;
                if (guardCooldown <= 0)
                {
                    var expos = map.nodes[extraction].pos;
                    Zombie best = null;
                    float bd = cfg.guardRange;
                    foreach (var z in zombies)
                    {
                        float d = Vector2.Distance(z.pos, expos);
                        if (d <= bd) { best = z; bd = d; }
                    }
                    if (best != null)
                    {
                        zombies.Remove(best);
                        guardKills++;
                        guardCooldown = cfg.guardFireInterval;
                        guardShots.Add(new Vector3(best.pos.x, best.pos.y, time));
                        if (guardShots.Count > 20) guardShots.RemoveAt(0);
                    }
                }
            }
            ApplyDamage(dt);

            if (cfg.escape) UpdateHelicopter();
            else if (cityInfection >= 100f) EndMatch("城市感染程度達到上限，提前淪陷");
            else if (time >= cfg.matchDuration) EndMatch("時間到，城市全面淪陷");
            else if (players.Count > 0 && players.All(p => p.state == PlayerState.Eliminated)) EndMatch("所有玩家都被淘汰了");
        }

        int PartsNeeded(int n)
        {
            n = Mathf.Min(n, 30);
            return Mathf.Max(5, Mathf.RoundToInt((cfg.partsBase + cfg.partsPerPlayer * n - cfg.partsCrowding * n * n) * cfg.difficulty * DScale));
        }

        float DScale => cfg.matchDuration / 600f;

        void UpdateHelicopter()
        {
            if (readyAt < 0 && parts >= partsRequired)
            {
                readyAt = time;
                foreach (var p in players)
                    if (p.state == PlayerState.Active) { p.hp = 100f; p.infection = Mathf.Min(p.infection, 30f); }
                int before = zombies.Count;
                zombies.RemoveAll(z => InZone(z.pos));
                SpawnFuel();
                Log("heli", $"直升機修好了！守軍清掉圈內 {before - zombies.Count} 隻殭屍，去撿回 {fuelRequired} 桶油。現在倒地就無法復活！", extraction);
            }
            if (readyAt >= 0 && departAt < 0 && fuel >= fuelRequired)
            {
                departAt = time + cfg.boardTime;
                Log("heli", $"油加滿了！倒數暫停，{cfg.boardTime:0} 秒後起飛，快回撤離點！", extraction);
            }
            if (departAt >= 0 && time >= departAt) EndMatch("直升機起飛");
            else if (departAt < 0 && time >= cfg.matchDuration) EndMatch(readyAt < 0 ? "時間到，直升機沒修好" : "時間到，油不夠，直升機飛不起來");
            else if (players.Count > 0 && players.All(p => p.state == PlayerState.Eliminated)) EndMatch("所有人都倒下了");
        }

        void SpawnFuel()
        {
            UseStream(StreamEvent, 100000);
            int n = Mathf.Max(1, players.Count);
            fuelRequired = Mathf.Max(2, Mathf.RoundToInt(n * cfg.fuelPerPlayer) - fuelDiscount);
            var far = map.nodes.Where(nd => nd.type == NodeType.Road && map.dist[extraction, nd.id] >= 6f && map.dist[extraction, nd.id] <= 20f && InZone(nd.pos)).ToList();
            if (far.Count == 0) far = map.nodes.Where(nd => nd.type == NodeType.Road && map.dist[extraction, nd.id] >= 6f).ToList();
            if (far.Count == 0) far = map.nodes.Where(nd => nd.type == NodeType.Road).ToList();
            int reward = Mathf.RoundToInt(cfg.fuelReward * (1f + 0.25f * Phase));
            for (int i = 0; i < fuelRequired + cfg.fuelSpare + (int)(n * cfg.fuelSparePerPlayer); i++)
            {
                int node = far[rng.Next(far.Count)].id;
                tasks.Add(new GameTask
                {
                    id = nextTaskId++, type = TaskType.Fuel, origin = node, dest = extraction, weight = 2, reward = reward,
                    availableAt = time, expiresAt = -1f, status = TaskStatus.Open,
                });
            }
        }

        public float ZoneRadius
        {
            get
            {
                float t = readyAt < 0 ? time : Mathf.Min(time, readyAt);
                float z0 = cfg.zoneStart * DScale, z1 = cfg.zoneEnd * DScale;
                if (!cfg.escape || t <= z0) return zoneMax;
                float f = Mathf.Min(1f, (t - z0) / Mathf.Max(1f, z1 - z0));
                return zoneMax + (cfg.zoneMinRadius - zoneMax) * f;
            }
        }

        public bool InZone(Vector2 pos) => Vector2.Distance(pos, map.nodes[extraction].pos) <= ZoneRadius;

        public bool IsSafe(int node) =>
            !(cfg.escape && node == extraction && time < guardDownUntil) && map.nodes[node].Safe;

        public Incident IncidentById(int id) => incidents.FirstOrDefault(i => i.id == id);

        void SpawnIncidents(float dt)
        {
            incidentTimer -= dt;
            if (incidentTimer > 0 || readyAt >= 0) return;
            int n = Mathf.Max(1, players.Count);
            incidentTimer = cfg.incidentInterval * DScale / (1f + 0.2f * Phase) / Mathf.Max(1f, Mathf.Sqrt(n / 6f));
            UseStream(StreamEvent, eventIndex++);
            if (incidents.Count(i => i.status == "active" && i.kind != "generator") >= 1 + n / 4) return;
            int ph = Phase;
            var kinds = new List<(string, float)> { ("outbreak", 0.3f), ("airdrop", 0.25f), ("trapped", 0.2f), ("bridge", 0.15f), ("chest", 0.15f) };
            if (ph >= 1) kinds.AddRange(new[] { ("guard", 0.15f), ("napalm", 0.15f), ("decoy", 0.12f) });
            if (ph >= 2) kinds.AddRange(new[] { ("heli", 0.15f), ("boss", 0.12f) });
            var themes = map.nodes.Select(nd => nd.district).Distinct().OrderBy(d => d).ToList();
            foreach (int d in themes) kinds.Add((DistrictEvent[d], 0.24f / themes.Count));
            kinds.Add(("generator", cfg.generatorWeight));
            double roll = rng.NextDouble() * kinds.Sum(k => (double)k.Item2);
            string kind = kinds[kinds.Count - 1].Item1;
            foreach (var (k, w) in kinds)
            {
                if (roll < w) { kind = k; break; }
                roll -= w;
            }
            var roads = map.nodes.Where(nd => nd.type == NodeType.Road && nd.id != extraction).ToList();
            int node;
            Edge edge = null;
            int theme = System.Array.IndexOf(DistrictEvent, kind);
            if (theme >= 0)
            {
                var pool = roads.Where(nd => nd.district == theme).ToList();
                if (pool.Count == 0) pool = roads;
                node = pool[rng.Next(pool.Count)].id;
            }
            else if (kind == "generator")
            {
                var gens = Generators.ToList();
                if (gens.Count == 0) return;
                var g = gens[rng.Next(gens.Count)];
                if (!g.powered || time - lastGeneratorBreak < cfg.generatorCooldown) return;
                BreakGenerator(g);
                Log("incident", $"突發事件：{IncidentTypes["generator"].name}! 工廠停工了!", g.id);
                return;
            }
            else if (kind == "guard" || kind == "heli") node = extraction;
            else if (kind == "trapped")
            {
                var pool = roads.Where(nd => nd.infection >= 0.3f).ToList();
                if (pool.Count == 0) pool = roads;
                node = pool[rng.Next(pool.Count)].id;
            }
            else if (kind == "bridge")
            {
                var open = map.edges.Where(e => !e.blocked).ToList();
                var brs = open.Where(e => e.bridge).ToList();
                if (brs.Count == 0) brs = open;
                edge = brs[rng.Next(brs.Count)];
                edge.blocked = true;
                map.Recompute();
                if (!map.Connected()) { edge.blocked = false; map.Recompute(); return; }
                node = edge.a;
            }
            else node = roads[rng.Next(roads.Count)].id;

            var def = IncidentTypes[kind];
            var inc = new Incident
            {
                id = nextIncidentId++, kind = kind, name = def.name, node = node, need = def.need,
                minPeople = def.minPeople, deadline = time + def.limit, reward = def.reward,
            };
            if (edge != null) { inc.edge = edge.id; edge.blockedUntil = inc.deadline + 120f; }
            if (kind == "outbreak")
            {
                map.nodes[node].infection = 1f;
                for (int i = 0; i < 2 + ph; i++) SpawnZombie(node, -1);
            }
            if (kind == "boss") for (int i = 0; i < 2; i++) SpawnZombie(node, -1);
            incidents.Add(inc);
            Log("incident", $"突發事件：{inc.name}!", node);
        }

        public IEnumerable<Node> Generators => map.nodes.Where(n => n.type == NodeType.Generator);

        bool West(int node) => node % map.cols <= map.riverCol;

        IEnumerable<Node> GeneratorsFor(Node factory)
        {
            var same = Generators.Where(g => West(g.id) == West(factory.id)).ToList();
            return same.Count > 0 ? same : Generators;
        }

        public bool Powered(Node n) =>
            n.type == NodeType.Generator ? n.powered
            : n.type == NodeType.Factory && (!cfg.escape || GeneratorsFor(n).Any(g => g.powered));

        void BreakGenerator(Node g)
        {
            g.powered = false;
            lastGeneratorBreak = time;
            var def = IncidentTypes["generator"];
            incidents.Add(new Incident
            {
                id = nextIncidentId++, kind = "generator", name = def.name, node = g.id, need = def.need,
                minPeople = def.minPeople, deadline = time + def.limit, reward = def.reward,
            });
        }

        void UpdateGenerators()
        {
            int ph = Phase;
            if (ph == lastPhase) return;
            lastPhase = ph;
            if (ph < 1 || ph > 2 || readyAt >= 0) return;
            var gens = Generators.ToList();
            if (gens.Count == 0) return;
            var g = gens[(ph - 1) % gens.Count];
            if (!g.powered) return;
            BreakGenerator(g);
            Log("incident", $"突發事件：{IncidentTypes["generator"].name}! 工廠停工了!", g.id);
        }

        public Incident DebugIncident(string kind, int node)
        {
            if (!IncidentTypes.TryGetValue(kind, out var def)) return null;
            Edge edge = null;
            if (kind == "bridge") { edge = map.edges.FirstOrDefault(e => e.bridge); if (edge != null) node = edge.a; }
            if (kind == "guard" || kind == "heli") node = extraction;
            var inc = new Incident { id = nextIncidentId++, kind = kind, name = def.name, node = node, need = def.need, minPeople = def.minPeople, deadline = time + 9999f, reward = def.reward };
            if (edge != null) inc.edge = edge.id;
            incidents.Add(inc);
            return inc;
        }

        void SpawnCivilians()
        {
            UseStream(StreamTask, 300000);
            var pool = map.nodes.Where(n => n.type == NodeType.Road && n.id != extraction && map.dist[extraction, n.id] >= 4f && map.dist[extraction, n.id] < CityMap.Inf).ToList();
            if (pool.Count == 0) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(players.Count * cfg.civiliansPerPlayer), 6, 60);
            for (int i = 0; i < count; i++) AddCivilian(pool[rng.Next(pool.Count)].id);
        }

        void AddCivilian(int origin)
        {
            float d = map.dist[origin, extraction];
            if (d >= CityMap.Inf) return;
            float baseReward = (18f + d * 0.5f + 20f * map.nodes[origin].infection) * cfg.rescueRewardMul * (1f + 0.25f * Phase);
            tasks.Add(new GameTask
            {
                id = nextTaskId++, type = TaskType.Rescue, origin = origin, dest = extraction, weight = 4,
                reward = Mathf.RoundToInt(baseReward), availableAt = time, expiresAt = -1f, status = TaskStatus.Open,
            });
        }

        int RestartFactories(Node g)
        {
            int k = 0;
            foreach (var f in map.nodes.Where(n => n.type == NodeType.Factory && GeneratorsFor(n).Contains(g)))
            {
                float d = map.dist[f.id, extraction];
                if (d >= CityMap.Inf) continue;
                const int weight = 3;
                float baseReward = (10f + d * 0.6f + weight * cfg.partWeightReward) * (1f + 0.25f * Phase);
                tasks.Add(new GameTask
                {
                    id = nextTaskId++, type = TaskType.Part, origin = f.id, dest = extraction, weight = weight,
                    reward = Mathf.RoundToInt(baseReward), availableAt = time, expiresAt = time + 90f, status = TaskStatus.Open,
                });
                k++;
            }
            return k;
        }

        void CheckMedevac(float dt)
        {
            medevacTimer -= dt;
            if (medevacTimer > 0) return;
            medevacTimer = 5f;
            int n = players.Count;
            int down = players.Count(p => p.state != PlayerState.Active);
            if (down < Mathf.Max(2, (n + 3) / 4) || time < medevacNext || incidents.Any(i => i.kind == "medevac" && i.status == "active")) return;
            UseStream(StreamEvent, 200000 + medevacRolls); medevacRolls++;
            if (rng.NextDouble() >= 0.5) return;
            var pool = map.nodes.Where(nd => nd.type == NodeType.Road && map.dist[extraction, nd.id] >= 4f && map.dist[extraction, nd.id] <= 15f && InZone(nd.pos)).ToList();
            if (pool.Count == 0) pool = map.nodes.Where(nd => nd.type == NodeType.Road && nd.id != extraction).ToList();
            int node = pool[rng.Next(pool.Count)].id;
            var def = IncidentTypes["medevac"];
            var inc = new Incident
            {
                id = nextIncidentId++, kind = "medevac", name = def.name, node = node, need = def.need,
                minPeople = def.minPeople, deadline = time + def.limit, reward = def.reward,
            };
            incidents.Add(inc);
            medevacNext = inc.deadline + 60f;
            Log("incident", $"突發事件：{inc.name}! 接應到就讓倒下的人全部復活!", node);
        }

        void UpdateIncidents()
        {
            float dt = lastDt;
            foreach (var inc in incidents)
            {
                if (inc.status != "active") continue;
                var workers = players.Where(p => p.state == PlayerState.Active && p.nextNode < 0 && p.node == inc.node &&
                                                 p.pending != null && p.pending.action == "fix" && p.pending.task == inc.id).ToList();
                inc.workers = workers.Count;
                if (workers.Count > 0)
                    foreach (var p in workers)
                    {
                        inc.progress += dt;
                        inc.work[p.id] = (inc.work.TryGetValue(p.id, out float w) ? w : 0f) + dt;
                    }
                if (inc.progress >= inc.need) SolveIncident(inc, workers);
                else if (time >= inc.deadline) FailIncident(inc);
            }
            incidents.RemoveAll(i => i.status != "active" && time - i.deadline >= 10f);
        }

        int ComboGain(Player p, int gain)
        {
            int prev = p.combo;
            float gap = time - p.lastScoreAt;
            if (gap > cfg.comboWindow) p.combo = 0;
            else if (gap >= 2f) p.combo = Mathf.Min(cfg.comboMax, p.combo + 1);
            p.lastScoreAt = time;
            if (p.combo != prev && (p.combo == 2 || p.combo == cfg.comboMax))
                Log("combo", $"{p.name} 連續得分 {p.combo + 1} 次！", p.node);
            return gain + gain * p.combo * cfg.comboStep / 100;
        }

        string SolverNames(Incident inc)
        {
            var names = inc.work.OrderByDescending(kv => kv.Value).Select(kv => PlayerById(kv.Key)?.name).Where(n => n != null).ToList();
            if (names.Count == 0) return "大家";
            if (names.Count == 1) return names[0];
            if (names.Count <= 3) return string.Join("、", names.Take(names.Count - 1)) + " 和 " + names[names.Count - 1];
            return $"{names[0]}、{names[1]} 等 {names.Count} 人";
        }

        public int ComboOf(Player p) => time - p.lastScoreAt <= cfg.comboWindow ? p.combo : 0;

        void SolveIncident(Incident inc, List<Player> workers)
        {
            inc.status = "solved";
            float total = Mathf.Max(1f, inc.work.Values.Sum());
            int reward = Mathf.RoundToInt(inc.reward * (1f + 0.25f * Phase) * cfg.incidentRewardMul);
            foreach (var kv in inc.work)
            {
                var q = PlayerById(kv.Key);
                if (q == null) continue;
                q.score += ComboGain(q, Mathf.RoundToInt(reward * kv.Value / total));
                q.fixedCount++;
            }
            foreach (var p in workers) { p.pending = null; p.actionTimer = 0; }
            var pos = map.nodes[inc.node].pos;
            string effect;
            switch (inc.kind)
            {
                case "outbreak":
                    zombies.RemoveAll(z => Vector2.Distance(z.pos, pos) <= 4f);
                    map.nodes[inc.node].infection = 0f;
                    effect = "當地殭屍清除、感染歸零!";
                    break;
                case "bridge":
                    if (inc.edge >= 0 && inc.edge < map.edges.Count) map.edges[inc.edge].blockedUntil = time;
                    speedBoostUntil = time + cfg.speedBoostTime;
                    effect = $"全員加速 {cfg.speedBoostTime:0} 秒!";
                    break;
                case "airdrop":
                    foreach (var p in players) if (p.state == PlayerState.Active) p.hp = 100f;
                    foreach (var n in map.nodes) if (n.type == NodeType.Supply) n.medkits = Mathf.Max(n.medkits, 5);
                    effect = "全員生命回滿、補給站醫療包補滿!";
                    break;
                case "trapped":
                    for (int i = 0; i < cfg.trappedCivilians; i++) AddCivilian(inc.node);
                    effect = $"{cfg.trappedCivilians} 位民眾等著被帶回撤離點!";
                    break;
                case "guard":
                    zombies.RemoveAll(z => Vector2.Distance(z.pos, pos) <= 6f);
                    effect = "守衛擊退了撤離點附近的殭屍!";
                    break;
                case "medevac":
                {
                    var back = players.Where(q => q.state != PlayerState.Active).ToList();
                    foreach (var q in back) Respawn(q, true);
                    effect = $"{back.Count} 位倒下的同學全部復活!";
                    break;
                }
                case "napalm":
                    effect = $"炸掉了附近 {zombies.RemoveAll(z => Vector2.Distance(z.pos, pos) <= 8f)} 隻殭屍!";
                    break;
                case "decoy":
                    zombieStunUntil = time + cfg.stunTime;
                    effect = $"全城殭屍暫停 {cfg.stunTime:0} 秒!";
                    break;
                case "herbs":
                    foreach (var q in players)
                        if (q.state == PlayerState.Active) { q.medkits++; q.hp = Mathf.Min(100f, q.hp + 30f); }
                    effect = "全員多一個醫療包、生命 +30!";
                    break;
                case "gas":
                    if (readyAt >= 0) fuelRequired = Mathf.Max(1, fuelRequired - 1);
                    else fuelDiscount++;
                    effect = "之後少要 1 桶油!";
                    break;
                case "rubble":
                {
                    int extra = Mathf.Max(1, Mathf.RoundToInt(partsRequired * 0.05f));
                    parts += extra;
                    effect = $"挖出 {extra} 個零件!";
                    break;
                }
                case "generator":
                {
                    map.nodes[inc.node].powered = true;
                    inc.deadline = time;
                    int k = RestartFactories(map.nodes[inc.node]);
                    effect = $"工廠恢復生產零件!";
                    break;
                }
                case "bell":
                {
                    var town = tasks.Where(t => t.type == TaskType.Rescue && (t.status == TaskStatus.Open || t.status == TaskStatus.Announced)
                                                && map.nodes[t.origin].district == 3).ToList();
                    foreach (var t in town) { t.status = TaskStatus.Done; t.expiresAt = time; }
                    evacuated += town.Count;
                    effect = $"鎮民聽到鐘聲! {town.Count} 名平民逃到避難所";
                    break;
                }
                case "chest":
                {
                    int extra = Mathf.Max(1, Mathf.RoundToInt(partsRequired * 0.04f));
                    parts += extra;
                    effect = $"寶箱裡有 {extra} 個零件和 {reward} 分!";
                    break;
                }
                case "boss":
                    effect = $"殭屍王倒下! 附近 {zombies.RemoveAll(z => Vector2.Distance(z.pos, pos) <= 6f)} 隻殭屍一同陣亡!";
                    break;
                default:
                    heliBoost = true;
                    if (readyAt >= 0) { fuelRequired = Mathf.Max(1, fuelRequired - 1); effect = "直升機少需要 1 桶油!"; }
                    else
                    {
                        int less = Mathf.Max(1, Mathf.RoundToInt(partsRequired * 0.05f));
                        partsRequired = Mathf.Max(parts + 1, partsRequired - less);
                        effect = $"零件需求 -{less}!";
                    }
                    break;
            }
            string ok = IncidentText.TryGetValue(inc.kind, out var tx) ? tx.ok : $"解決了{inc.name}!";
            Log("solved", $"{SolverNames(inc)} {ok} {effect}", inc.node);
        }

        void FailIncident(Incident inc)
        {
            inc.status = "failed";
            string effect;
            switch (inc.kind)
            {
                case "outbreak":
                    for (int i = 0; i < 3; i++) SpawnZombie(inc.node, -1);
                    effect = "殭屍又多了 3 隻!";
                    break;
                case "bridge":
                    effect = "橋要再斷 2 分鐘!";
                    break;
                case "airdrop":
                    for (int i = 0; i < 2; i++) SpawnZombie(inc.node, -1);
                    effect = "物資被殭屍佔領!";
                    break;
                case "trapped":
                    for (int i = 0; i < 4; i++) SpawnZombie(inc.node, -1);
                    effect = "受困的民眾變成了 4 隻殭屍!";
                    break;
                case "guard":
                    guardDownUntil = time + cfg.guardDownTime;
                    effect = $"撤離點守衛倒下! {cfg.guardDownTime:0} 秒內無法攻擊殭屍!";
                    break;
                case "medevac":
                    effect = "醫療隊撤走了!";
                    break;
                case "napalm":
                case "decoy":
                    for (int i = 0; i < 2; i++) SpawnZombie(inc.node, -1);
                    effect = "訊號引來 2 隻殭屍!";
                    break;
                case "chest":
                    effect = "寶箱被殭屍叼走了!";
                    break;
                case "herbs":
                    for (int i = 0; i < 2; i++) SpawnZombie(inc.node, -1);
                    effect = "草叢裡竄出 2 隻殭屍!";
                    break;
                case "gas":
                    effect = "這次沒裝到油!";
                    break;
                case "rubble":
                    for (int i = 0; i < 2; i++) SpawnZombie(inc.node, -1);
                    effect = "瓦礫裡爬出 2 隻殭屍!";
                    break;
                case "bell":
                    for (int i = 0; i < 3; i++) SpawnZombie(inc.node, -1);
                    effect = "鐘聲引來 3 隻殭屍!";
                    break;
                case "boss":
                    for (int i = 0; i < 5; i++) SpawnZombie(inc.node, -1);
                    effect = "殭屍王帶來 5 隻殭屍!";
                    break;
                default:
                    if (readyAt >= 0)
                    {
                        fuelRequired++;
                        effect = "油料外洩，要多撿 1 桶油!";
                    }
                    else
                    {
                        int extra = Mathf.Max(1, Mathf.RoundToInt(partsRequired * 0.1f));
                        partsRequired += extra;
                        effect = $"直升機故障! 零件需求 +{extra}!";
                    }
                    break;
            }
            string bad = IncidentText.TryGetValue(inc.kind, out var tx) ? tx.fail : $"{inc.name}沒有處理!";
            Log("incident_fail", $"✕ {bad} {effect}", inc.node);
        }

        void UpdateRoads()
        {
            bool changed = false;
            foreach (var e in map.edges)
                if (e.blocked && time >= e.blockedUntil) { e.blocked = false; changed = true; Log("repair", $"道路 {e.a}-{e.b} 恢復通行"); }
            if (changed) map.Recompute();
        }

        void UpdateInfection(float dt)
        {
            float avg = map.nodes.Average(n => n.infection);
            float rate = cfg.coop && !cfg.escape ? cfg.coopCityRate + cfg.cityPerPlayer * players.Count : cfg.cityRate;
            cityInfection += (100f / cfg.matchDuration) * rate * (0.6f + avg) * dt;
            if (cfg.escape) cityInfection = Mathf.Min(100f, cityInfection);

            spreadTimer -= dt;
            if (spreadTimer > 0) return;
            spreadTimer = 4f;
            UseStream(StreamSpread, spreadTick++);
            float chance = 0.10f + 0.35f * cityInfection / 100f;
            var add = new float[map.nodes.Count];
            foreach (var n in map.nodes)
            {
                if (n.infection < 0.6f) continue;
                foreach (int ei in n.edges)
                {
                    var e = map.edges[ei];
                    if (!e.blocked && rng.NextDouble() < chance) add[e.Other(n.id)] += cfg.spreadAmount;
                }
            }
            foreach (var n in map.nodes)
            {
                float cap = n.Safe ? 0.4f : 1f;
                float grow = n.infection > 0 ? cfg.spreadGrow : 0f;
                n.infection = Mathf.Min(cap, n.infection + add[n.id] + grow);
            }
        }

        void UpdateTasks(float dt)
        {
            foreach (var t in tasks)
            {
                if (t.status == TaskStatus.Announced && time >= t.availableAt) t.status = TaskStatus.Open;
                if (t.status == TaskStatus.Open && t.expiresAt > 0 && time >= t.expiresAt)
                {
                    t.status = TaskStatus.Expired;
                    if (cfg.escape)
                    {
                        if (t.type == TaskType.Rescue)
                        {
                            for (int i = 0; i < cfg.civilianZombies; i++) SpawnZombie(t.origin, -1);
                            Log("fail", $"倖存者 #{t.id} 沒人救，變成了殭屍", t.origin);
                        }
                    }
                    else if (cfg.coop && t.urgent) { cityInfection += cfg.urgentFailCity; Log("fail", $"緊急委託 #{t.id} 沒人處理，城市感染 +{cfg.urgentFailCity:0}%", t.origin); }
                    else if (t.type == TaskType.Rescue)
                    {
                        cityInfection += cfg.coop ? cfg.rescueFailCity : 1f;
                        Log("fail", $"倖存者 #{t.id} 未獲救援，城市感染上升", t.origin);
                    }
                }
            }
            tasks.RemoveAll(t => (t.status == TaskStatus.Done || t.status == TaskStatus.Expired) && time - t.expiresAt > 30f && t.carrier < 0);

            taskTimer -= dt;
            if (taskTimer > 0 || NoRespawn) return;
            taskTimer = cfg.taskInterval / (map.Scale * SquadTaskScale * (1f + 0.35f * Phase));
            UseStream(StreamTask, taskSlot++);
            SpawnTask(rng.NextDouble() < 0.15 + 0.07 * Phase, cfg.announceLead);
        }

        public GameTask SpawnTask(bool urgent, float lead)
        {
            float availableAt = time + lead;
            bool rescue = !cfg.escape && rng.NextDouble() < 0.25 + 0.1 * Phase;
            int origin, dest, weight;
            if (rescue)
            {
                var pool = map.nodes.Where(n => n.type == NodeType.Road && n.infection >= 0.3f).ToList();
                if (pool.Count == 0) pool = map.nodes.Where(n => n.type == NodeType.Road).ToList();
                origin = pool[rng.Next(pool.Count)].id;
                if (cfg.escape) dest = extraction;
                else
                {
                    var safe = map.nodes.Where(n => n.Safe).ToList();
                    dest = safe[rng.Next(safe.Count)].id;
                }
                weight = 4;
            }
            else
            {

                var src = cfg.escape ? map.nodes.Where(n => n.type == NodeType.Factory && Powered(n)).ToList()
                                     : map.nodes.Where(n => n.type == NodeType.Factory || n.type == NodeType.Supply).ToList();
                if (src.Count == 0) return null;
                origin = src[rng.Next(src.Count)].id;
                if (cfg.escape)
                {
                    dest = extraction;
                    weight = 1 + rng.Next(3);
                }
                else
                {
                    if (!urgent && rng.NextDouble() < cfg.batchChance)
                    {
                        var first = SpawnBatch(origin, availableAt);
                        if (first != null) return first;
                    }
                    var dst = map.nodes.Where(n => n.id != origin && n.type != NodeType.Factory && map.dist[origin, n.id] >= 8f).ToList();
                    if (dst.Count == 0) return null;
                    dest = dst[rng.Next(dst.Count)].id;
                    weight = 1 + rng.Next(4);
                }
            }
            float d = map.dist[origin, dest];
            if (d >= CityMap.Inf) return null;

            float baseReward = rescue
                ? (18f + d * 0.5f + 20f * map.nodes[origin].infection) * cfg.rescueRewardMul
                : cfg.escape ? 10f + d * 0.6f + weight * cfg.partWeightReward
                : 8f + d * 0.5f + weight * 2f;
            baseReward *= 1f + 0.25f * Phase;

            var t = new GameTask
            {
                id = nextTaskId++,
                type = rescue ? TaskType.Rescue : cfg.escape ? TaskType.Part : TaskType.Delivery,
                origin = origin, dest = dest, weight = weight,
                reward = Mathf.RoundToInt(baseReward),
                availableAt = availableAt,
                expiresAt = availableAt + 90f,
                status = lead > 0 ? TaskStatus.Announced : TaskStatus.Open,
            };
            if (urgent)
            {
                t.urgent = true;
                t.bonus = Mathf.RoundToInt(baseReward * 0.6f);
                t.deadline = availableAt + (d + 15f) / cfg.playerSpeed * 1.5f + 25f;
                string when = lead > 0 ? $"{lead:0} 秒後開放，" : "";
                Log("urgent", $"緊急委託 #{t.id}：{(rescue ? "救援" : cfg.escape ? "零件" : "配送")} {origin}→{dest}，{when}期限 {Clock(t.deadline)}，獎金 +{t.bonus}");
            }
            tasks.Add(t);
            return t;
        }

        GameTask SpawnBatch(int origin, float availableAt)
        {
            var centers = map.nodes.Where(n => n.id != origin && map.dist[origin, n.id] >= 12f && map.dist[origin, n.id] < CityMap.Inf).ToList();
            if (centers.Count == 0) return null;
            var center = centers[rng.Next(centers.Count)];
            var area = map.nodes.Where(n => n.id != origin && n.type != NodeType.Factory && map.dist[center.id, n.id] <= 9f)
                                .OrderBy(_ => rng.Next()).ToList();
            int k = Mathf.Min(area.Count, cfg.batchMin + rng.Next(cfg.batchMax - cfg.batchMin + 1));
            if (k < 2) return null;

            var dests = area.Take(k).Select(n => n.id).ToList();
            float tour = 0; int at = origin;
            var left = new List<int>(dests);
            while (left.Count > 0)
            {
                int nxt = left.OrderBy(n => map.dist[at, n]).First();
                tour += map.dist[at, nxt]; at = nxt; left.Remove(nxt);
            }
            var batch = new Batch
            {
                id = nextBatchId++, origin = origin,
                deadline = availableAt + 25f + 1.4f * tour / cfg.playerSpeed + 2f * k,
            };

            GameTask first = null;
            int total = 0;
            foreach (int dest in dests)
            {
                int weight = 1 + rng.Next(2);
                float d = map.dist[origin, dest];
                var t = new GameTask
                {
                    id = nextTaskId++, type = TaskType.Delivery, origin = origin, dest = dest, weight = weight,
                    reward = Mathf.RoundToInt((6f + d * 0.45f + weight * 2f) * (1f + 0.25f * Phase)),
                    availableAt = availableAt, expiresAt = availableAt + 120f,
                    status = availableAt > time ? TaskStatus.Announced : TaskStatus.Open, batch = batch.id, deadline = batch.deadline,
                };
                tasks.Add(t);
                batch.tasks.Add(t.id);
                total += t.reward;
                first = first ?? t;
            }
            batch.bonus = Mathf.RoundToInt(total * cfg.batchBonusRatio);
            batches[batch.id] = batch;
            string when = availableAt > time ? $"{availableAt - time:0} 秒後開放，" : "";
            Log("batch", $"批次合約 B{batch.id}：工廠 {origin} 有 {k} 件包裹送往路口 {center.id} 一帶，{when}{Clock(batch.deadline)} 前全送達獎金 +{batch.bonus}");
            return first;
        }

        public Batch BatchOf(GameTask t) => t != null && t.batch >= 0 && batches.TryGetValue(t.batch, out var b) ? b : null;

        public bool CanTake(Player p, GameTask t)
        {
            if (cfg.coop) return true;
            var b = BatchOf(t);
            return b == null || b.owner < 0 || b.owner == p.team.id;
        }

        void UpdateBatches()
        {
            foreach (var b in batches.Values)
            {
                if (b.paid || b.failed || time <= b.deadline) continue;
                b.failed = true;
                if (b.owner >= 0) Log("fail", $"批次合約 B{b.id} 逾期，{TeamById(b.owner)?.name} 失去合約獎金");
            }
        }

        static string Clock(float t) => $"{(int)t / 60:00}:{(int)t % 60:00}";

        int MaxZombies => Mathf.RoundToInt((3 + 21f * time / cfg.matchDuration) * map.Scale);

        void UpdateZombieSpawns(float dt)
        {
            zombieTimer -= dt;
            if (zombieTimer > 0) return;
            zombieTimer = (6f - Phase) / map.Scale;
            UseStream(StreamZombie, zombieSpawnIndex++);
            if (zombies.Count(z => z.fromPlayer < 0) < MaxZombies) SpawnZombieAtInfected();
        }

        void SpawnZombieAtInfected()
        {
            var pool = map.nodes.Where(n => n.infection >= 0.5f && !n.Safe).ToList();
            if (pool.Count == 0) return;
            SpawnZombie(pool[rng.Next(pool.Count)].id, -1);
        }

        Zombie SpawnZombie(int node, int fromPlayer)
        {
            var z = new Zombie { id = nextZombieId++, node = node, pos = map.nodes[node].pos, fromPlayer = fromPlayer, wait = 1f, rng = new System.Random(rng.Next()) };
            zombies.Add(z);
            return z;
        }

        void UpdateEvents(float dt)
        {
            eventTimer -= dt;
            if (eventTimer > 0) return;
            eventTimer = 25f - 3f * Phase;
            UseStream(StreamEvent, eventIndex++);
            double roll = rng.NextDouble();
            int phase = Phase;

            if (roll < 0.3) { SpawnTask(true, 0f); return; }
            if (roll < 0.5)
            {
                var sup = map.nodes.Where(n => n.type == NodeType.Supply).ToList();
                var s = sup[rng.Next(sup.Count)];
                s.medkits = Mathf.Min(8, s.medkits + 3);
                Log("supply", $"物資空投：補給站 {s.id} 新增醫療包（剩 {s.medkits}）");
                return;
            }
            if (phase == 0) { SpawnTask(true, 0f); return; }
            if (roll < 0.75)
            {
                var pool = map.nodes.Where(n => n.type == NodeType.Road && n.id != map.startNode).ToList();
                var n = pool[rng.Next(pool.Count)];
                n.infection = 1f;
                int count = 2 + phase;
                for (int i = 0; i < count; i++) SpawnZombie(n.id, -1);
                cityInfection += 2f;
                Log("outbreak", $"感染爆發！出現 {count} 隻殭屍", n.id);
                return;
            }

            var open = map.edges.Where(e => !e.blocked).ToList();
            var bridges = open.Where(e => e.bridge).ToList();
            var edge = bridges.Count > 0 && rng.NextDouble() < 0.5 ? bridges[rng.Next(bridges.Count)] : open[rng.Next(open.Count)];
            edge.blocked = true;
            map.Recompute();
            if (!map.Connected()) { edge.blocked = false; map.Recompute(); return; }
            edge.blockedUntil = time + 45f + (float)rng.NextDouble() * 45f;
            Log("failure", $"有一段{(edge.bridge ? "橋" : "道路")}壞了，{edge.blockedUntil - time:0} 秒後修好");
        }

        void UpdateRestock(float dt)
        {
            restockTimer -= dt;
            if (restockTimer > 0) return;
            restockTimer = 45f;
            foreach (var n in map.nodes)
                if (n.type == NodeType.Supply && n.medkits < 5) n.medkits++;
        }

        void UpdateBots(float dt)
        {
            botTimer -= dt;
            if (botTimer > 0) return;
            botTimer = 0.25f;
            foreach (var p in players)
            {
                if (!p.IsBot || p.state != PlayerState.Active) continue;
                bool idle = p.planIndex >= p.plan.Count && p.pending == null && p.nextNode < 0;
                if (!idle) { p.idleSince = -1f; continue; }
                if (p.idleSince < 0) p.idleSince = time;

                if (time - p.idleSince < cfg.botReactionTime * Mathf.Max(1f, cfg.timeScale)) continue;
                if (time - p.lastPlanTime >= cfg.planCooldown) SubmitPlan(p, p.bot.Think(this, p), false);
                else
                {
                    var steps = p.bot.Think(this, p);
                    if (IsEmergencyPlan(steps)) SubmitPlan(p, steps, false);
                }
            }
        }

        public int Load(Player p) => p.cargo.Sum(id => TaskById(id)?.weight ?? 0) + p.medkits;

        public float Speed(Player p)
        {
            float s = cfg.playerSpeed * (1f - cfg.loadSpeedPenalty * Load(p));
            if (p.hp < 30f) s *= 0.8f;
            if (time < speedBoostUntil) s *= cfg.speedBoost;
            return Mathf.Max(0.5f, s);
        }

        public GameTask TaskById(int id) => tasks.FirstOrDefault(t => t.id == id);
        public Player PlayerById(int id) => players.FirstOrDefault(p => p.id == id);

        static readonly HashSet<string> Actions = new HashSet<string>
            { "move", "goto", "pickup", "dropoff", "take_medkit", "use_medkit", "heal", "revive", "wait", "fix" };

        public string SubmitPlan(Player p, List<PlanStep> steps, bool append, int startNode = -1)
        {
            if (status != MatchStatus.Running) return "比賽尚未開始或已結束";
            if (p.state != PlayerState.Active) return "角色目前無法行動（" + p.state + "）";
            if (steps == null) steps = new List<PlanStep>();
            foreach (var s in steps)
                if (s == null || !Actions.Contains(s.action)) return "未知的 action：" + s?.action;
            bool emergency = !append && IsEmergencyPlan(steps);
            if (!emergency && time - p.lastPlanTime < cfg.planCooldown)
                return $"送出太頻繁，請在 {cfg.planCooldown - (time - p.lastPlanTime):0.0} 秒後再試（緊急撤退 / 治療指令不受限制）";

            if (append)
            {
                p.plan.RemoveRange(0, Mathf.Min(p.planIndex, p.plan.Count));
                p.plan.AddRange(steps);
            }
            else
            {
                p.plan = new List<PlanStep>(steps);
                int here = p.nextNode >= 0 ? p.nextNode : p.node;
                bool needsStart = steps.Count > 0 && steps[0].action == "move";
                if (needsStart && startNode >= 0 && startNode < map.nodes.Count && startNode != here)
                    p.plan.Insert(0, new PlanStep { action = "goto", node = startNode });
            }
            if (p.plan.Count > cfg.maxPlanSteps) p.plan.RemoveRange(cfg.maxPlanSteps, p.plan.Count - cfg.maxPlanSteps);
            p.planIndex = 0;
            if (!emergency) p.lastPlanTime = time;
            p.lastError = "";
            return null;
        }

        static readonly HashSet<string> EmergencyActions = new HashSet<string> { "move", "goto", "wait", "use_medkit", "heal" };

        bool IsEmergencyPlan(List<PlanStep> steps)
        {
            if (steps.Count == 0 || !steps.All(s => EmergencyActions.Contains(s.action))) return false;
            if (steps.Any(s => s.action == "use_medkit" || s.action == "heal")) return true;
            var last = steps.LastOrDefault(s => s.action == "move" || s.action == "goto");
            return last != null && last.node >= 0 && last.node < map.nodes.Count && map.nodes[last.node].Safe;
        }

        void UpdatePlayer(Player p, float dt)
        {
            if (p.state == PlayerState.Eliminated) return;
            if (p.state == PlayerState.Downed)
            {
                p.downedTimer -= dt;
                if (p.downedTimer <= 0)
                {
                    if (NoRespawn) Eliminate(p, p.eliminatedReason == "" ? "沒人來救" : p.eliminatedReason);
                    else Respawn(p);
                }
                return;
            }

            if (p.nextNode >= 0)
            {
                if (Advance(ref p.node, ref p.nextNode, ref p.progress, Speed(p), dt)) p.pos = map.nodes[p.node].pos;
                else p.pos = Vector2.Lerp(map.nodes[p.node].pos, map.nodes[p.nextNode].pos, p.progress);
                return;
            }

            if (p.pending != null)
            {
                p.actionTimer -= dt;
                if (p.actionTimer <= 0)
                {
                    var s = p.pending;
                    p.pending = null;
                    string err = s.action == "fix" ? null : Validate(p, s);
                    if (err != null) Fail(p, s, err, true);
                    else Complete(p, s);
                }
                return;
            }

            for (int guard = 0; guard < 8 && p.planIndex < p.plan.Count && p.nextNode < 0 && p.pending == null; guard++)
                StartStep(p, p.plan[p.planIndex]);
        }

        bool Advance(ref int node, ref int nextNode, ref float progress, float speed, float dt)
        {
            var e = map.FindEdge(node, nextNode);
            progress += speed * dt / Mathf.Max(0.01f, e.length);
            if (progress < 1f) return false;
            node = nextNode; nextNode = -1; progress = 0;
            return true;
        }

        void StartStep(Player p, PlanStep s)
        {
            switch (s.action)
            {
                case "move":
                {
                    if (s.node == p.node) { p.planIndex++; return; }
                    if (s.node < 0 || s.node >= map.nodes.Count) { Fail(p, s, $"move：節點 {s.node} 不存在"); return; }
                    var e = map.FindEdge(p.node, s.node);
                    if (e == null) { Fail(p, s, $"move：{p.node} 與 {s.node} 之間沒有道路"); return; }
                    if (e.blocked) { Fail(p, s, $"move：道路 {p.node}-{s.node} 已封鎖"); return; }
                    p.nextNode = s.node; p.progress = 0; p.planIndex++;
                    return;
                }
                case "goto":
                {
                    if (s.node < 0 || s.node >= map.nodes.Count) { Fail(p, s, $"goto：節點 {s.node} 不存在"); return; }
                    if (s.node == p.node) { p.planIndex++; return; }
                    int hop = map.next[p.node, s.node];
                    if (hop < 0) { Fail(p, s, $"goto：無法抵達節點 {s.node}"); return; }
                    p.nextNode = hop; p.progress = 0;
                    return;
                }
                default:
                {

                    var done = s.task >= 0 ? TaskById(s.task) : null;
                    if ((s.action == "pickup" && p.cargo.Contains(s.task)) ||
                        (s.action == "dropoff" && done != null && done.completedBy == p.id))
                    {
                        p.planIndex++;
                        return;
                    }

                    if (s.action == "pickup" && done != null && done.status == TaskStatus.Announced &&
                        done.origin == p.node && CanTake(p, done))
                        return;
                    string err = Validate(p, s);
                    if (err != null) { Fail(p, s, err); return; }
                    p.pending = s;
                    p.actionTimer = Duration(s);
                    p.planIndex++;
                    return;
                }
            }
        }

        static float Duration(PlanStep s)
        {
            switch (s.action)
            {
                case "heal": return 4f;
                case "revive": return 3f;
                case "use_medkit": return 1.5f;
                case "wait": return Mathf.Clamp(s.seconds, 0.1f, 30f);
                case "fix": return Mathf.Clamp(s.seconds > 0 ? s.seconds : 10f, 1f, 30f);
                default: return 1f;
            }
        }

        string Validate(Player p, PlanStep s)
        {
            var node = map.nodes[p.node];
            switch (s.action)
            {
                case "pickup":
                {
                    var t = TaskById(s.task);
                    if (t == null || t.status != TaskStatus.Open) return $"pickup：委託 #{s.task} 已不可領取";
                    if (t.origin != p.node) return $"pickup：委託 #{s.task} 在節點 {t.origin}，你在 {p.node}";
                    if (!CanTake(p, t)) return $"pickup：批次合約 B{t.batch} 已由 {TeamById(BatchOf(t).owner)?.name} 負責";
                    if (Load(p) + t.weight > cfg.capacity) return $"pickup：背包容量不足（{Load(p)}+{t.weight}>{cfg.capacity}）";
                    return null;
                }
                case "dropoff":
                {
                    var t = TaskById(s.task);
                    if (t == null || !p.cargo.Contains(s.task)) return $"dropoff：你沒有攜帶委託 #{s.task}";
                    if (t.dest != p.node) return $"dropoff：委託 #{s.task} 目的地是 {t.dest}，你在 {p.node}";
                    return null;
                }
                case "take_medkit":
                    if (node.type != NodeType.Supply) return "take_medkit：這裡不是補給站";
                    if (node.medkits <= 0) return "take_medkit：補給站已無醫療包";
                    if (Load(p) + 1 > cfg.capacity) return "take_medkit：背包容量不足";
                    return null;
                case "use_medkit":
                    return p.medkits > 0 ? null : "use_medkit：沒有醫療包";
                case "heal":
                    return node.type == NodeType.Hospital ? null : "heal：這裡不是醫院";
                case "fix":
                {
                    var inc = IncidentById(s.task);
                    if (inc == null || inc.status != "active") return $"fix：事件 #{s.task} 已經結束";
                    if (inc.node != p.node) return $"fix：事件 #{s.task} 在節點 {inc.node}，你在 {p.node}";
                    return null;
                }
                case "revive":
                {
                    var t = PlayerById(s.target);
                    if (t == null || t.state != PlayerState.Downed) return $"revive：玩家 {s.target} 不是倒地狀態";
                    if (t.node != p.node) return $"revive：玩家 {s.target} 在節點 {t.node}";
                    return null;
                }
                default: return null;
            }
        }

        void Complete(Player p, PlanStep s)
        {
            switch (s.action)
            {
                case "pickup":
                {
                    var t = TaskById(s.task);
                    t.status = TaskStatus.Carried; t.carrier = p.id;
                    p.cargo.Add(t.id);
                    MakeNoise(p.node, cfg.actionNoiseRadius, cfg.actionNoiseDuration, "pickup");
                    var b = BatchOf(t);
                    if (b != null && b.owner < 0 && !cfg.coop)
                    {
                        b.owner = p.team.id;

                        foreach (int id in b.tasks) { var bt = TaskById(id); if (bt != null) bt.expiresAt = Mathf.Max(bt.expiresAt, b.deadline + 60f); }
                        Log("batch", b.deadline > time
                            ? $"{p.team.name} 承接批次合約 B{b.id}（{b.tasks.Count} 件，剩 {b.deadline - time:0} 秒）"
                            : $"{p.team.name} 承接批次合約 B{b.id}（{b.tasks.Count} 件，已逾期，無合約獎金）");
                    }
                    break;
                }
                case "dropoff":
                {
                    var t = TaskById(s.task);
                    p.cargo.Remove(t.id);
                    if (!(cfg.escape && p.node == extraction))
                        MakeNoise(p.node, cfg.actionNoiseRadius, cfg.actionNoiseDuration, "dropoff");
                    int gain = t.reward;
                    bool onTime = t.urgent && time <= t.deadline;
                    if (onTime) gain += t.bonus;
                    if (cfg.escape && t.type == TaskType.Part && cfg.partDecay > 0)
                        gain = Mathf.RoundToInt(gain * cfg.partDecay / (cfg.partDecay + p.parts));
                    if (cfg.escape) gain = ComboGain(p, gain);
                    p.score += gain;
                    t.status = TaskStatus.Done; t.carrier = -1; t.completedBy = p.id; t.expiresAt = time;
                    if (cfg.coop && !cfg.escape) ReduceCity(p, gain);
                    if (t.type == TaskType.Part) { parts += t.weight; p.parts += t.weight; }
                    else if (t.type == TaskType.Fuel) { fuel++; p.fuel++; }
                    if (t.type == TaskType.Rescue) { p.rescued++; evacuated++; if (!cfg.coop) cityInfection = Mathf.Max(0, cityInfection - 1.5f); }
                    else if (t.type != TaskType.Part && t.type != TaskType.Fuel) p.delivered++;
                    Log("done", $"{p.name} 完成{(t.type == TaskType.Rescue ? "救援" : t.type == TaskType.Part ? "零件" : t.type == TaskType.Fuel ? "油桶" : "配送")} #{t.id} +{gain}{(onTime ? "（限時獎勵）" : "")}{(cfg.escape && p.combo > 0 ? $"（連續得分 {p.combo + 1} 次）" : "")}");
                    var b = BatchOf(t);
                    if (b != null && cfg.coop)
                    {
                        b.delivered++;
                        b.by[p.id] = (b.by.TryGetValue(p.id, out int n) ? n : 0) + 1;
                        if (!b.paid && !b.failed && b.delivered >= b.tasks.Count)
                        {
                            b.paid = true;
                            foreach (var kv in b.by)
                            {
                                var q = PlayerById(kv.Key);
                                int share = Mathf.RoundToInt((float)b.bonus * kv.Value / b.tasks.Count);
                                q.score += share;
                                ReduceCity(q, share);
                            }
                            Log("batch", $"批次合約 B{b.id} 準時完成！合約獎金 +{b.bonus}（{b.by.Count} 人合力）", p.node);
                        }
                        break;
                    }
                    if (b != null && b.owner == p.team.id) b.delivered++;
                    if (b != null && !b.paid && !b.failed && b.owner == p.team.id && b.delivered >= b.tasks.Count)
                    {
                        b.paid = true;
                        p.score += b.bonus;
                        Log("batch", $"{p.team.name} 準時完成批次合約 B{b.id}，合約獎金 +{b.bonus}", p.node);
                    }
                    break;
                }
                case "take_medkit":
                    map.nodes[p.node].medkits--; p.medkits++;
                    break;
                case "use_medkit":
                    p.medkits--;
                    p.hp = Mathf.Min(100, p.hp + 40); p.infection = Mathf.Max(0, p.infection - 25);
                    break;
                case "heal":
                    p.hp = 100; p.infection = Mathf.Max(0, p.infection - 40);
                    break;
                case "revive":
                {
                    var t = PlayerById(s.target);
                    t.state = PlayerState.Active; t.hp = 40; t.eliminatedReason = "";
                    t.protectUntil = time + 5f;
                    bool fresh = time - t.revivedAt >= 30f;
                    t.revivedAt = time;
                    int bonus = fresh ? cfg.reviveBonus : 0;
                    p.score += cfg.escape && fresh ? ComboGain(p, bonus) : bonus;
                    if (fresh) p.revives++;
                    Log("revive", $"{p.name} 救起了 {t.name}" + (fresh ? $" +{bonus}" : ""), p.node);
                    break;
                }
            }
        }

        void Fail(Player p, PlanStep s, string msg, bool advanced = false)
        {
            p.lastError = $"[{time:0}s] {msg}（已略過此步）";
            if (s.action == "move" && !advanced)
            {
                int end = p.planIndex;
                while (end + 1 < p.plan.Count && p.plan[end + 1].action == "move") end++;
                int target = p.plan[end].node;
                p.plan.RemoveRange(p.planIndex, end - p.planIndex + 1);
                if (target >= 0 && target < map.nodes.Count && target != p.node && map.next[p.node, target] >= 0)
                    p.plan.Insert(p.planIndex, new PlanStep { action = "goto", node = target });
                return;
            }
            if (!advanced) p.planIndex++;
            if (s.action == "pickup")
                for (int k = p.plan.Count - 1; k >= p.planIndex; k--)
                    if (p.plan[k].action == "dropoff" && p.plan[k].task == s.task) p.plan.RemoveAt(k);
        }

        void Down(Player p, string reason)
        {
            if (p.nextNode >= 0 && p.progress > 0.5f) p.node = p.nextNode;
            p.nextNode = -1; p.progress = 0;
            p.pos = map.nodes[p.node].pos;
            p.pending = null; p.plan.Clear(); p.planIndex = 0;
            p.state = PlayerState.Downed;
            p.downedTimer = cfg.downedTime;
            p.hp = 0;
            foreach (int id in p.cargo)
            {
                var t = TaskById(id);
                if (t == null) continue;
                t.status = TaskStatus.Open; t.carrier = -1; t.origin = p.node;
                t.expiresAt = t.type == TaskType.Fuel ? -1f : time + 60f;
            }
            p.cargo.Clear();
            p.downs++;
            int penalty = cfg.respawnPenalty * (cfg.escape ? Mathf.Min(p.downs, 3) : 1);
            p.score -= penalty;
            p.combo = 0; p.lastScoreAt = -999f;
            cityInfection += 1f;
            p.eliminatedReason = reason;
            if (NoRespawn) Log("down", $"{p.name} 倒地了！{cfg.downedTime:0} 秒內沒人來救就逃不掉了", p.node);
            else Log("down", $"{p.name} 倒地（{reason}），掉落貨物、扣 {penalty} 分，{cfg.downedTime:0} 秒內有人來救 +{cfg.reviveBonus}，不然回撤離點復活", p.node);
        }

        void Eliminate(Player p, string reason)
        {
            if (p.nextNode >= 0 && p.progress > 0.5f) p.node = p.nextNode;
            p.nextNode = -1; p.progress = 0;
            p.pos = map.nodes[p.node].pos;
            p.state = PlayerState.Eliminated;
            p.eliminatedAt = time;
            p.eliminatedReason = reason;
            p.plan.Clear(); p.pending = null;
            foreach (int id in p.cargo)
            {
                var t = TaskById(id);
                if (t == null) continue;
                t.status = TaskStatus.Open; t.carrier = -1; t.origin = p.node;
                t.expiresAt = t.type == TaskType.Fuel ? -1f : time + 60f;
            }
            p.cargo.Clear();
            Log("eliminated", $"{p.name} 倒下了（{reason}），沒能逃出", p.node);
        }

        void Respawn(Player p, bool quiet = false)
        {
            p.node = cfg.escape ? extraction : map.startNode; p.nextNode = -1; p.progress = 0;
            p.pos = map.nodes[p.node].pos;
            p.state = PlayerState.Active;
            p.hp = cfg.respawnHp;
            p.infection = Mathf.Min(p.infection, 30f);
            p.protectUntil = time + 5f;
            p.lastPlanTime = -999f;
            p.eliminatedAt = -1f; p.eliminatedReason = "";
            p.plan.Clear(); p.planIndex = 0; p.pending = null;
            if (quiet) return;
            Log("respawn", $"{p.name} 在{(cfg.escape ? "撤離點" : "避難所")}復活", p.node);
        }

        public float ZombieSpeed => cfg.zombieSpeed + cfg.zombieSpeedPerPhase * Phase;

        void UpdateZombie(Zombie z, float dt)
        {
            if (z.nextNode >= 0)
            {
                if (Advance(ref z.node, ref z.nextNode, ref z.progress, ZombieSpeed, dt)) z.pos = map.nodes[z.node].pos;
                else z.pos = Vector2.Lerp(map.nodes[z.node].pos, map.nodes[z.nextNode].pos, z.progress);
                return;
            }
            if (z.wait > 0) { z.wait -= dt; if (FindPrey(z) == null) return; }

            DecideZombie(z);
            if (z.target >= 0 && z.target != z.node)
            {
                int hop = map.next[z.node, z.target];
                if (hop >= 0) { z.nextNode = hop; z.progress = 0; }
                else z.target = -1;
            }
        }

        void DecideZombie(Zombie z)
        {
            var prey = FindPrey(z);
            if (prey != null)
            {
                z.mode = ZombieMode.Chase; z.chasing = prey.id;
                z.target = prey.nextNode >= 0 ? prey.nextNode : prey.node;
                return;
            }
            z.chasing = -1;
            var noise = noises.Where(n => Vector2.Distance(z.pos, n.pos) <= n.radius)
                              .OrderBy(n => Vector2.Distance(z.pos, n.pos)).FirstOrDefault();
            if (noise != null) { z.mode = ZombieMode.Attracted; z.target = noise.node; return; }

            if (z.mode != ZombieMode.Wander || z.target < 0 || z.target == z.node)
            {
                z.mode = ZombieMode.Wander;
                var near = map.nodes.Where(n => !n.Safe && map.dist[z.node, n.id] >= 6f && map.dist[z.node, n.id] <= 18f).ToList();
                if (near.Count == 0) near = map.nodes.Where(n => !n.Safe && n.id != z.node).ToList();
                z.target = near.Count > 0 ? near[z.rng.Next(near.Count)].id : -1;
            }
        }

        public List<int> ZombiePath(Zombie z, int maxLen = 10)
        {
            var path = new List<int>();
            int from = z.node;
            if (z.nextNode >= 0) { path.Add(z.nextNode); from = z.nextNode; }
            if (z.target >= 0 && z.target != from)
            {
                var rest = map.Path(from, z.target);
                if (rest != null) path.AddRange(rest);
            }
            if (path.Count > maxLen) path.RemoveRange(maxLen, path.Count - maxLen);
            return path;
        }

        Player FindPrey(Zombie z)
        {
            Player prey = null;
            float best = cfg.zombieSight;
            foreach (var p in players)
            {
                if (p.state != PlayerState.Active || (p.nextNode < 0 && IsSafe(p.node))) continue;
                float d = Vector2.Distance(z.pos, p.pos);
                if (d < best) { best = d; prey = p; }
            }
            return prey;
        }

        public readonly List<Noise> noises = new List<Noise>();
        int nextNoiseId;

        void MakeNoise(int node, float radius, float duration, string source)
        {
            var n = noises.FirstOrDefault(x => x.node == node);
            if (n == null)
            {
                noises.Add(new Noise { id = nextNoiseId++, node = node, pos = map.nodes[node].pos, radius = radius, until = time + duration, source = source });
                return;
            }
            n.radius = Mathf.Max(n.radius, radius);
            if (time + duration > n.until) { n.until = time + duration; n.source = source; }
        }

        void UpdateNoises(float dt)
        {
            noises.RemoveAll(n => time >= n.until);
            foreach (var p in players)
            {
                if (p.state != PlayerState.Active) continue;

                p.noiseTimer -= dt;
                if (p.noiseTimer <= 0 && p.cargo.Any(id => TaskById(id)?.type == TaskType.Rescue))
                {
                    p.noiseTimer = 1f;
                    MakeNoise(p.nextNode >= 0 && p.progress > 0.5f ? p.nextNode : p.node, cfg.survivorNoiseRadius, 2f, "survivor");
                }
            }
        }

        void ApplyDamage(float dt)
        {
            float scale = 0.7f + 0.15f * Phase;
            foreach (var p in players)
            {
                if (p.state != PlayerState.Active || time < p.protectUntil) continue;
                var here = map.nodes[p.node];
                if (cfg.escape && !InZone(p.pos))
                {
                    p.hp -= cfg.zoneDamage * dt;
                    p.infection += cfg.zoneInfection * dt;
                }
                bool inSafeZone = p.nextNode < 0 && IsSafe(p.node);
                if (!inSafeZone && time >= zombieStunUntil)
                {
                    bool hit = false;
                    foreach (var z in zombies)
                    {
                        if (Vector2.Distance(z.pos, p.pos) > cfg.zombieAttackRange) continue;
                        p.hp -= cfg.zombieDamage * scale * dt;
                        p.infection += cfg.zombieInfection * scale * dt;
                        hit = true;
                    }

                    if (hit && time - p.lastScream > 3f)
                    {
                        p.lastScream = time;
                        MakeNoise(p.nextNode >= 0 && p.progress > 0.5f ? p.nextNode : p.node, cfg.screamRadius, 6f, "scream");
                    }
                    if (p.nextNode < 0 && here.infection > 0.7f) p.infection += 0.5f * dt;
                }
                if (p.infection >= 100f) { p.infection = 100f; Down(p, "感染值達到上限"); }
                else if (p.hp <= 0f) Down(p, "被殭屍咬倒");
            }
        }

        void ReduceCity(Player p, int points)
        {
            float d = Mathf.Min(cityInfection, points * cfg.cityPerPoint);
            cityInfection -= d;
            p.cityReduced += d;
        }

        public List<int> Fixing(Player p)
        {
            var list = new List<int>();
            for (int i = p.planIndex; i < p.plan.Count; i++)
                if (p.plan[i].action == "fix") list.Add(p.plan[i].task);
            if (p.pending?.action == "fix") list.Add(p.pending.task);
            return list;
        }

        public List<int> Targets(Player p)
        {
            var list = new List<int>();
            for (int i = p.planIndex; i < p.plan.Count; i++)
                if (p.plan[i].action == "pickup") list.Add(p.plan[i].task);
            if (p.pending?.action == "pickup") list.Add(p.pending.task);
            return list;
        }

        void EndMatch(string reason)
        {
            status = MatchStatus.Ended;
            endReason = reason;
            if (cfg.escape)
            {
                classWon = departAt >= 0 && time >= departAt;
                if (classWon)
                {
                    foreach (var p in players)
                        if (p.state == PlayerState.Active && p.nextNode < 0 && p.node == extraction)
                        {
                            p.escaped = true;
                            p.score += cfg.escapeBonus + Mathf.RoundToInt(TimeLeft * cfg.earlyBonusPerSec);
                        }
                    endReason = $"直升機起飛！{players.Count(p => p.escaped)}/{players.Count} 人逃出城市";
                }
                else if (readyAt < 0) endReason = $"直升機沒修好（零件 {parts}/{partsRequired}），全班被困在城市裡";
                else if (players.Count > 0 && players.All(p => p.state == PlayerState.Eliminated)) endReason = "所有人都倒下了，直升機沒能起飛";
                else endReason = $"油不夠（{fuel}/{fuelRequired} 桶），直升機飛不起來";
            }
            else
            {
                classWon = cfg.coop && cityInfection < 100f;
                if (classWon)
                {
                    endReason = "全班撐過了！城市守住了";
                    foreach (var p in players) p.score += cfg.classBonus;
                }
                foreach (var p in players)
                    if (p.state == PlayerState.Active && p.nextNode < 0 && map.nodes[p.node].Safe)
                    {
                        p.score += cfg.survivalBonus;
                        p.evacuated = true;
                    }
            }
            var ranking = Ranking();
            for (int i = 0; i < ranking.Count; i++) ranking[i].rank = i + 1;
            Log("end", "比賽結束!");
            lastResultJson = ResultWriter.Save(this);
        }

        public List<Team> Ranking() =>
            teams.OrderByDescending(t => t.Score)
                 .ThenByDescending(t => t.units.Count(u => u.state == PlayerState.Active))
                 .ThenByDescending(t => t.units.Max(u => u.eliminatedAt < 0 ? float.MaxValue : u.eliminatedAt))
                 .ThenBy(t => t.id).ToList();
    }
}
