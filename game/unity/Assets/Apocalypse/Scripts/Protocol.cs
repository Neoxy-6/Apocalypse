using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    [Serializable] public class NodeDto { public int id; public string type; public float x, y, infection; public int medkits, weapons; public bool safe, powered; public string district; }
    [Serializable] public class EdgeDto { public int id, a, b; public float length; public bool blocked, bridge; public float blocked_until; }
    [Serializable] public class TaskDto { public int id; public string type; public int origin, dest, weight, reward, bonus, batch; public bool urgent; public float deadline, expires_at, available_at; public string status; public int carrier; }
    [Serializable] public class BatchDto { public int id, origin, owner, bonus, delivered; public float deadline; public bool paid, failed; public int[] tasks; }
    [Serializable] public class PlayerDto { public int id; public string name, color, state, bot; public int node, next_node, score, load, delivered, rescued, downs; public float progress, x, y, hp, infection, downed_timer, city_reduced; public string role; public int[] targets; public int parts; public bool escaped; public int[] fixing; public int @fixed, fuel, combo, ammo, kills; }
    [Serializable] public class ZombieDto { public int id, node, next_node, from_player, target, chasing; public string mode; public float x, y, progress; public int[] path; }
    [Serializable] public class NoiseDto { public int id, node; public float x, y, radius, until; public string source; }
    [Serializable] public class EventDto { public float time; public string type, text; }
    [Serializable] public class StepDto { public string action; public int node = -1, task = -1, target = -1; public float seconds; }

    [Serializable]
    public class UnitDto
    {
        public int id = -1;
        public string name = "", state = "", busy = "", last_error = "";
        public int node, next_node, score, load, capacity, medkits, plan_index, ammo;
        public float hp, infection, speed, next_plan_at;
        public int[] cargo = new int[0];
        public StepDto[] plan = new StepDto[0];
    }

    [Serializable]
    public class YouDto : UnitDto
    {
    }

    [Serializable]
    public class RulesDto
    {
        public int capacity, max_plan_steps, survival_bonus, respawn_penalty;
        public float player_speed, load_speed_penalty, plan_cooldown, downed_time;
        public float zombie_speed, zombie_speed_per_phase, zombie_sight, zombie_attack_range, zombie_damage, zombie_infection, zombie_chase_mul;
        public float action_noise_radius, action_noise_duration, survivor_noise_radius, scream_radius;
        public int weapon_weight, weapon_shots, kill_reward, fuel_weight;
        public float weapon_range, weapon_interval;
    }

    [Serializable]
    public class EscapeDto
    {
        public bool enabled;
        public int extraction, parts, parts_required, escape_bonus;
        public float ready_at, depart_at, zone_x, zone_y, zone_radius, zone_damage;
        public int evacuated;
        public float speed_boost_until, guard_down_until, zombie_stun_until;
        public bool heli_boost;
        public int fuel, fuel_required;
    }

    [Serializable] public class IncidentDto { public int id, node, min_people, workers, reward; public string type, name, status; public float deadline, progress, need; }

    [Serializable]
    public class StateDto
    {
        public string status, phase_name, end_reason;
        public bool coop, class_won;
        public EscapeDto escape;
        public IncidentDto[] incidents;
        public int phase, seed, start_node, match_id;
        public float time, duration, time_left, city_infection;
        public YouDto you;
        public RulesDto rules;
        public NodeDto[] nodes;
        public EdgeDto[] edges;
        public TaskDto[] tasks;
        public BatchDto[] batches;
        public PlayerDto[] players;
        public ZombieDto[] zombies;
        public NoiseDto[] noises;
        public float zombie_speed;
        public EventDto[] events;
    }

    [Serializable] public class JoinRequest { public string name, role; }
    [Serializable] public class JoinResponse { public bool ok; public string error, token; public int player_id; }
    [Serializable] public class PlanRequest { public string token; public StepDto[] steps; public bool append; public int start_node = -1, unit; }
    [Serializable] public class AdminRequest { public string key; }
    [Serializable] public class OkResponse { public bool ok; public string error; }

    [Serializable] public class ResultRow { public int rank, player_id, score, delivered, rescued, revives, downs, evacuated; public string name, bot, state, tier, role; public float survived, city_reduced; public int parts; public bool escaped; }
    [Serializable] public class MatchResult { public string finished_at, end_reason; public bool coop, class_won, escape; public int parts, parts_required; public int seed, match_id; public float duration, ended_at, city_infection; public ResultRow[] ranking; }

    public static class Protocol
    {
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        static string F2(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        static string Esc(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        public static string ReplayHeader(GameSim sim, float every)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"every\":").Append(F2(every)).Append(",\"extraction\":").Append(sim.extraction)
              .Append(",\"duration\":").Append(F2(sim.cfg.matchDuration)).Append(",\"nodes\":[");
            for (int i = 0; i < sim.map.nodes.Count; i++)
            {
                var n = sim.map.nodes[i];
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(F2(n.pos.x)).Append(',').Append(F2(n.pos.y)).Append(',').Append(Esc(Lower(n.type))).Append(']');
            }
            sb.Append("],\"edges\":[");
            for (int i = 0; i < sim.map.edges.Count; i++)
            {
                var e = sim.map.edges[i];
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(e.a).Append(',').Append(e.b).Append(',').Append(e.bridge ? 1 : 0).Append(']');
            }
            sb.Append("],\"players\":{");
            for (int i = 0; i < sim.players.Count; i++)
            {
                var p = sim.players[i];
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(p.id).Append("\":[").Append(Esc(p.name)).Append(',').Append(Esc(Hex(p.color))).Append(']');
            }
            return sb.Append("}}").ToString();
        }

        public static string ReplayFrame(GameSim sim)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"t\":").Append(F2(sim.time)).Append(",\"z\":").Append(F2(sim.ZoneRadius))
              .Append(",\"parts\":").Append(sim.parts).Append(",\"preq\":").Append(sim.partsRequired)
              .Append(",\"fuel\":").Append(sim.fuel).Append(",\"freq\":").Append(sim.fuelRequired)
              .Append(",\"ready\":").Append(F2(sim.readyAt)).Append(",\"depart\":").Append(F2(sim.departAt)).Append(",\"p\":[");
            bool first = true;
            foreach (var p in sim.players)
            {
                if (!first) sb.Append(','); first = false;
                int s = p.state == PlayerState.Active ? 0 : p.state == PlayerState.Downed ? 1 : 2;
                sb.Append('[').Append(p.id).Append(',').Append(F2(p.pos.x)).Append(',').Append(F2(p.pos.y)).Append(',').Append(s)
                  .Append(',').Append(p.score).Append(',').Append(Mathf.RoundToInt(p.hp)).Append(']');
            }
            sb.Append("],\"zb\":[");
            first = true;
            foreach (var z in sim.zombies)
            {
                if (!first) sb.Append(','); first = false;
                sb.Append('[').Append(F2(z.pos.x)).Append(',').Append(F2(z.pos.y)).Append(',').Append(z.mode == ZombieMode.Chase ? 1 : 0).Append(']');
            }
            sb.Append("],\"k\":[");
            first = true;
            foreach (var t in sim.tasks)
            {
                if (t.status != TaskStatus.Open && t.status != TaskStatus.Announced) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append('[').Append(t.origin).Append(",\"").Append(Lower(t.type)[0]).Append("\",").Append(t.status == TaskStatus.Announced ? 1 : 0).Append(']');
            }
            sb.Append("],\"i\":[");
            first = true;
            foreach (var inc in sim.incidents)
            {
                if (inc.status != "active") continue;
                if (!first) sb.Append(','); first = false;
                sb.Append('[').Append(inc.node).Append(',').Append(Esc(inc.kind)).Append(']');
            }
            sb.Append("],\"b\":[");
            first = true;
            for (int i = 0; i < sim.map.edges.Count; i++)
            {
                if (!sim.map.edges[i].blocked) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append(i);
            }
            sb.Append("],\"g\":[");
            first = true;
            foreach (var n in sim.map.nodes)
            {
                if (n.type != NodeType.Generator || !n.powered) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append(n.id);
            }
            return sb.Append("]}").ToString();
        }

        static string Lower(object e) => e.ToString().ToLowerInvariant();

        public static StateDto BuildState(GameSim sim, Team viewer)
        {
            var cfg = sim.cfg;
            var s = new StateDto
            {
                status = Lower(sim.status), phase = sim.Phase, phase_name = GameSim.PhaseKeys[sim.Phase],
                end_reason = sim.endReason, coop = cfg.coop, class_won = sim.classWon, seed = cfg.seed,
                escape = new EscapeDto
                {
                    enabled = cfg.escape, extraction = sim.extraction, parts = sim.parts, parts_required = sim.partsRequired,
                    ready_at = sim.readyAt, depart_at = sim.departAt, zone_x = sim.map.nodes[sim.extraction].pos.x,
                    zone_y = sim.map.nodes[sim.extraction].pos.y, zone_radius = sim.ZoneRadius, zone_damage = cfg.zoneDamage,
                    escape_bonus = cfg.escapeBonus, evacuated = sim.evacuated, speed_boost_until = sim.speedBoostUntil,
                    guard_down_until = sim.guardDownUntil, heli_boost = sim.heliBoost, zombie_stun_until = sim.zombieStunUntil,
                    fuel = sim.fuel, fuel_required = sim.fuelRequired,
                },
                incidents = sim.incidents.Select(i => new IncidentDto
                {
                    id = i.id, type = i.kind, name = i.name, node = i.node, deadline = i.deadline, progress = i.progress, need = i.need,
                    min_people = i.minPeople, workers = i.workers, status = i.status,
                    reward = Mathf.RoundToInt(i.reward * (1f + 0.25f * sim.Phase)),
                }).ToArray(), start_node = sim.map.startNode, match_id = sim.mapVersion,
                time = sim.time, duration = cfg.matchDuration, time_left = sim.TimeLeft, city_infection = sim.cityInfection,
                rules = new RulesDto
                {
                    capacity = cfg.capacity, max_plan_steps = cfg.maxPlanSteps, survival_bonus = cfg.survivalBonus, respawn_penalty = cfg.respawnPenalty,
                    player_speed = cfg.playerSpeed, load_speed_penalty = cfg.loadSpeedPenalty, plan_cooldown = cfg.planCooldown,
                    downed_time = cfg.downedTime, zombie_speed = cfg.zombieSpeed, zombie_speed_per_phase = cfg.zombieSpeedPerPhase,
                    zombie_sight = cfg.zombieSight, zombie_attack_range = cfg.zombieAttackRange,
                    zombie_damage = cfg.zombieDamage, zombie_infection = cfg.zombieInfection,
                    zombie_chase_mul = cfg.zombieChaseMul,
                    action_noise_radius = cfg.actionNoiseRadius, action_noise_duration = cfg.actionNoiseDuration,
                    survivor_noise_radius = cfg.survivorNoiseRadius, scream_radius = cfg.screamRadius,
                    weapon_weight = cfg.weaponWeight, weapon_shots = cfg.weaponShots, kill_reward = cfg.killReward, fuel_weight = cfg.fuelWeight,
                    weapon_range = cfg.weaponRange, weapon_interval = cfg.weaponInterval,
                },
                nodes = sim.map.nodes.Select(n => new NodeDto
                {
                    id = n.id, type = Lower(n.type), x = n.pos.x, y = n.pos.y, infection = n.infection, medkits = n.medkits, weapons = n.weapons, safe = n.Safe, powered = sim.Powered(n), district = GameSim.Districts[n.district],
                }).ToArray(),
                edges = sim.map.edges.Select(e => new EdgeDto
                {
                    id = e.id, a = e.a, b = e.b, length = e.length, blocked = e.blocked, bridge = e.bridge,
                    blocked_until = e.blocked ? e.blockedUntil : 0,
                }).ToArray(),
                tasks = sim.tasks.Where(t => t.status == TaskStatus.Announced || t.status == TaskStatus.Open || t.status == TaskStatus.Carried).Select(t => new TaskDto
                {
                    id = t.id, type = Lower(t.type), origin = t.origin, dest = t.dest, weight = t.weight, reward = t.reward, bonus = t.bonus,
                    urgent = t.urgent, deadline = t.deadline, expires_at = t.expiresAt, status = Lower(t.status), carrier = t.carrier,
                    batch = t.batch, available_at = t.availableAt,
                }).ToArray(),
                batches = sim.batches.Values.Where(b => b.tasks.Any(id => sim.TaskById(id) is GameTask bt &&
                        bt.status <= TaskStatus.Carried)).Select(b => new BatchDto
                {
                    id = b.id, origin = b.origin, owner = b.owner, bonus = b.bonus, delivered = b.delivered,
                    deadline = b.deadline, paid = b.paid, failed = b.failed, tasks = b.tasks.ToArray(),
                }).ToArray(),
                players = sim.players.Select(p => new PlayerDto
                {
                    id = p.id, name = p.name, color = Hex(p.color), state = Lower(p.state), bot = p.botKind,
                    node = p.node, next_node = p.nextNode, progress = p.progress, x = p.pos.x, y = p.pos.y,
                    hp = p.hp, infection = p.infection, downed_timer = p.downedTimer,
                    score = p.score, load = sim.Load(p), delivered = p.delivered, rescued = p.rescued, downs = p.downs, city_reduced = p.cityReduced, role = p.role, targets = sim.Targets(p).ToArray(), parts = p.parts, escaped = p.escaped, fixing = sim.Fixing(p).ToArray(), @fixed = p.fixedCount, fuel = p.fuel, combo = sim.ComboOf(p), ammo = p.ammo, kills = p.kills,
                }).ToArray(),
                zombies = sim.zombies.Select(z => new ZombieDto
                {
                    id = z.id, node = z.node, next_node = z.nextNode, x = z.pos.x, y = z.pos.y, from_player = z.fromPlayer,
                    progress = z.progress, mode = Lower(z.mode), target = z.target, chasing = z.chasing,
                    path = sim.ZombiePath(z).ToArray(),
                }).ToArray(),
                noises = sim.noises.Select(n => new NoiseDto
                {
                    id = n.id, node = n.node, x = n.pos.x, y = n.pos.y, radius = n.radius, until = n.until, source = n.source,
                }).ToArray(),
                zombie_speed = sim.ZombieSpeed,
                events = sim.events.Skip(Math.Max(0, sim.events.Count - 20))
                    .Select(e => new EventDto { time = e.time, type = e.type, text = e.text }).ToArray(),
                you = new YouDto(),
            };

            if (viewer != null)
            {
                var you = new YouDto();
                Fill(you, sim, viewer.units[0]);
                s.you = you;
            }
            return s;
        }

        static UnitDto Fill(UnitDto d, GameSim sim, Player u)
        {
            d.id = u.id; d.name = u.name; d.state = Lower(u.state);
            d.node = u.node; d.next_node = u.nextNode; d.hp = u.hp; d.infection = u.infection;
            d.score = u.score; d.load = sim.Load(u); d.capacity = sim.cfg.capacity; d.medkits = u.medkits; d.ammo = u.ammo;
            d.speed = sim.Speed(u); d.cargo = u.cargo.ToArray();
            d.plan = u.plan.Select(ToDto).ToArray(); d.plan_index = u.planIndex;
            d.busy = u.pending != null ? u.pending.action : (u.nextNode >= 0 ? "moving" : "");
            d.last_error = u.lastError;
            d.next_plan_at = u.lastPlanTime + sim.cfg.planCooldown;
            return d;
        }

        public static StepDto ToDto(PlanStep s) => new StepDto { action = s.action, node = s.node, task = s.task, target = s.target, seconds = s.seconds };
        public static PlanStep FromDto(StepDto s) => s == null ? null : new PlanStep
        {
            action = (s.action ?? "").Trim().ToLowerInvariant(), node = s.node, task = s.task, target = s.target, seconds = s.seconds,
        };
    }

    public static class ResultWriter
    {
        public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Results"));

        public static string Save(GameSim sim)
        {
            var ranking = sim.Ranking();
            int randomBest = BestBotScore(sim, "random"), greedyBest = BestBotScore(sim, "greedy"), benchBest = BenchmarkScore(sim);

            var result = new MatchResult
            {
                finished_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), match_id = sim.mapVersion,
                end_reason = sim.endReason, seed = sim.cfg.seed, duration = sim.cfg.matchDuration,
                ended_at = sim.time, city_infection = sim.cityInfection, coop = sim.cfg.coop, class_won = sim.classWon, escape = sim.cfg.escape,
                parts = sim.parts, parts_required = sim.partsRequired,
                ranking = ranking.Select(t => new ResultRow
                {
                    rank = t.rank, player_id = t.id, name = t.name, bot = t.botKind, state = TeamState(t),
                    score = t.Score, delivered = t.units.Sum(u => u.delivered), rescued = t.units.Sum(u => u.rescued),
                    revives = t.units.Sum(u => u.revives), downs = t.units.Sum(u => u.downs), evacuated = t.units.Count(u => u.evacuated),
                    survived = t.units.Max(u => u.eliminatedAt < 0 ? sim.time : u.eliminatedAt),
                    tier = IsBaseline(t) || sim.cfg.coop ? "" : Tier(t.Score, randomBest, greedyBest, benchBest),
                    role = t.units[0].role, city_reduced = t.units.Sum(u => u.cityReduced),
                    parts = t.units.Sum(u => u.parts), escaped = t.units.Any(u => u.escaped),
                }).ToArray(),
            };
            string json = JsonUtility.ToJson(result, true);
            try
            {
                Directory.CreateDirectory(Folder);
                string file = Path.Combine(Folder, $"match_{DateTime.Now:yyyyMMdd_HHmmss}_seed{sim.cfg.seed}.json");
                File.WriteAllText(file, json);
                Debug.Log("[Apocalypse] 結果已儲存：" + file);
            }
            catch (Exception e) { Debug.LogWarning("[Apocalypse] 結果儲存失敗：" + e.Message); }
            return json;
        }

        public static int BestBotScore(GameSim sim, string kind) =>
            sim.teams.Where(t => t.botKind == kind).Select(t => t.Score).DefaultIfEmpty(-1).Max();

        public const string BenchmarkPrefix = "Benchmark-";
        public static bool IsBaseline(Team t) => t.IsBot || t.name.StartsWith(BenchmarkPrefix);
        public static int BenchmarkScore(GameSim sim) =>
            sim.teams.Where(t => t.name.StartsWith(BenchmarkPrefix)).Select(t => t.Score).DefaultIfEmpty(-1).Max();

        public static string TeamState(Team t) =>
            t.units.Any(u => u.state == PlayerState.Active) ? "active" :
            t.units.Any(u => u.state == PlayerState.Downed) ? "downed" : "eliminated";

        public static string Tier(int score, int randomBest, int greedyBest, int benchBest = -1)
        {
            if (benchBest >= 0 ? score > benchBest : greedyBest >= 0 && score > greedyBest * 1.3f) return "diamond";
            if (greedyBest >= 0 && score > greedyBest) return "gold";
            if (randomBest >= 0 && score > randomBest) return "silver";
            return score > 0 ? "bronze" : "unranked";
        }
    }
}
