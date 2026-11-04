using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public interface IBot
    {
        List<PlanStep> Think(GameSim sim, Player p);
    }

    static class BotUtil
    {
        public static PlanStep Goto(int node) => new PlanStep { action = "goto", node = node };
        public static PlanStep Act(string action, int task = -1) => new PlanStep { action = action, task = task };

        public static List<PlanStep> Heal(GameSim sim, Player p)
        {
            if (p.medkits > 0) return new List<PlanStep> { Act("use_medkit") };
            var hospital = sim.map.nodes.Where(n => n.type == NodeType.Hospital)
                .OrderBy(n => sim.map.dist[p.node, n.id]).FirstOrDefault();
            if (hospital == null) return new List<PlanStep>();
            return new List<PlanStep> { Goto(hospital.id), Act("heal") };
        }

        public static bool Useless(GameSim sim, GameTask t) =>
            sim.cfg.escape && ((t.type == TaskType.Part && sim.readyAt >= 0) || (t.type == TaskType.Fuel && sim.fuel >= sim.fuelRequired));

        public static List<PlanStep> DeliverCargo(GameSim sim, Player p)
        {
            var steps = p.cargo.Select(sim.TaskById).Where(t => t != null && Useless(sim, t)).Select(t => Act("drop", t.id)).ToList();
            if (steps.Count > 0) return steps;
            foreach (var t in p.cargo.Select(sim.TaskById).Where(t => t != null).OrderBy(t => sim.map.dist[p.node, t.dest]))
            {
                steps.Add(Goto(t.dest));
                steps.Add(Act("dropoff", t.id));
            }
            return steps;
        }

        public static HashSet<int> TeammateTargets(Player p)
        {
            var set = new HashSet<int>();
            foreach (var mate in p.team.units)
            {
                if (mate == p) continue;
                for (int i = mate.planIndex; i < mate.plan.Count; i++)
                    if (mate.plan[i].action == "pickup") set.Add(mate.plan[i].task);
                if (mate.pending?.action == "pickup") set.Add(mate.pending.task);
            }
            return set;
        }

        public static List<PlanStep> DoTask(GameTask t) => new List<PlanStep>
        {
            Goto(t.origin), Act("pickup", t.id), Goto(t.dest), Act("dropoff", t.id),
        };

        static PlanStep Wait(float s) => new PlanStep { action = "wait", seconds = s };

        public static List<PlanStep> GoHomeIfDeparting(GameSim sim, Player p)
        {
            if (sim.departAt < 0) return null;
            int ex = sim.extraction;
            float eta = sim.map.dist[p.node, ex] / sim.Speed(p);
            if (sim.departAt - sim.time > eta + 15f) return null;
            var steps = new List<PlanStep>();
            if (p.node != ex) steps.Add(Goto(ex));
            foreach (var t in p.cargo.Select(sim.TaskById).Where(t => t != null && t.dest == ex)) steps.Add(Act("dropoff", t.id));
            if (steps.Count == 0) steps.Add(Wait(2));
            return steps;
        }

        public static List<GameTask> OpenTasks(GameSim sim, Player p)
        {
            var taken = TeammateTargets(p);
            return sim.tasks.Where(t => t.status == TaskStatus.Open && sim.CanTake(p, t) && !taken.Contains(t.id)
                                        && sim.Load(p) + t.weight <= sim.cfg.capacity
                                        && (sim.readyAt < 0 || t.type == TaskType.Fuel) && !Useless(sim, t)).ToList();
        }

        public static List<PlanStep> Idle(GameSim sim, Player p) =>
            sim.readyAt >= 0 && p.node != sim.extraction ? new List<PlanStep> { Goto(sim.extraction) } : new List<PlanStep> { Wait(2) };

        public static IEnumerable<Incident> ReachableIncidents(GameSim sim, Player p) =>
            sim.incidents.Where(i => i.status == "active" && sim.map.dist[p.node, i.node] < CityMap.Inf
                                     && sim.time + sim.map.dist[p.node, i.node] / sim.Speed(p) + 3f < i.deadline);

        public static List<PlanStep> Fix(Incident inc) => new List<PlanStep> { Goto(inc.node), new PlanStep { action = "fix", task = inc.id, seconds = 20 } };

        public static List<PlanStep> ReviveNearby(GameSim sim, Player p, float maxDist)
        {
            var q = sim.players.Where(o => o != p && o.state == PlayerState.Downed && sim.map.dist[p.node, o.node] <= maxDist
                                           && sim.map.dist[p.node, o.node] / sim.Speed(p) + 4f < o.downedTimer)
                               .OrderBy(o => sim.map.dist[p.node, o.node]).FirstOrDefault();
            return q == null ? null : new List<PlanStep> { Goto(q.node), new PlanStep { action = "revive", target = q.id } };
        }
    }

    public class RandomBot : IBot
    {
        readonly System.Random rng;
        public RandomBot(int seed) { rng = new System.Random(seed * 7919 + 1); }

        public List<PlanStep> Think(GameSim sim, Player p)
        {
            if (sim.cfg.escape) return ThinkEscape(sim, p);
            if (p.hp < 30 || p.infection > 70) return BotUtil.Heal(sim, p);
            if (p.cargo.Count > 0) return BotUtil.DeliverCargo(sim, p);
            var taken = BotUtil.TeammateTargets(p);
            var open = sim.tasks.Where(t => t.status == TaskStatus.Open && sim.CanTake(p, t) && !taken.Contains(t.id)
                                            && sim.Load(p) + t.weight <= sim.cfg.capacity).ToList();
            if (open.Count == 0) return new List<PlanStep> { new PlanStep { action = "wait", seconds = 2 } };
            return BotUtil.DoTask(open[rng.Next(open.Count)]);
        }

        List<PlanStep> ThinkEscape(GameSim sim, Player p)
        {
            var home = BotUtil.GoHomeIfDeparting(sim, p);
            if (home != null) return home;
            if (p.hp < 30 || p.infection > 70) return BotUtil.Heal(sim, p);
            if (p.cargo.Count > 0) return BotUtil.DeliverCargo(sim, p);
            var incs = BotUtil.ReachableIncidents(sim, p).ToList();
            if (incs.Count > 0 && rng.NextDouble() < 0.3) return BotUtil.Fix(incs[rng.Next(incs.Count)]);
            var open = BotUtil.OpenTasks(sim, p);
            if (open.Count == 0) return incs.Count > 0 ? BotUtil.Fix(incs[rng.Next(incs.Count)]) : BotUtil.Idle(sim, p);
            return BotUtil.DoTask(open[rng.Next(open.Count)]);
        }
    }

    public class GreedyBot : IBot
    {
        public List<PlanStep> Think(GameSim sim, Player p)
        {
            if (sim.cfg.escape) return ThinkEscape(sim, p);
            var map = sim.map;
            if (p.hp < 45 || p.infection > 55) return BotUtil.Heal(sim, p);
            if (p.cargo.Count > 0) return BotUtil.DeliverCargo(sim, p);

            var steps = new List<PlanStep>();
            int from = p.node;
            if (p.medkits == 0)
            {
                var sup = map.nodes.Where(n => n.type == NodeType.Supply && n.medkits > 0)
                    .OrderBy(n => map.dist[p.node, n.id]).FirstOrDefault();
                if (sup != null && map.dist[p.node, sup.id] < 10f)
                {
                    steps.Add(BotUtil.Goto(sup.id));
                    steps.Add(BotUtil.Act("take_medkit"));
                    from = sup.id;
                }
            }

            GameTask best = null;
            float bestValue = 0;
            int load = sim.Load(p) + (steps.Count > 0 ? 1 : 0);
            var taken = BotUtil.TeammateTargets(p);
            foreach (var t in sim.tasks)
            {
                if (t.status != TaskStatus.Open || !sim.CanTake(p, t) || taken.Contains(t.id) || load + t.weight > sim.cfg.capacity) continue;
                float travel = map.dist[from, t.origin] + map.dist[t.origin, t.dest];
                if (travel >= CityMap.Inf) continue;
                float eta = sim.time + travel / sim.cfg.playerSpeed + 2f;
                float gain = t.reward + (t.urgent && eta <= t.deadline ? t.bonus : 0);
                float risk = 1f - 0.5f * map.nodes[t.origin].infection;
                float value = gain * risk / (travel + 5f);
                if (value > bestValue) { bestValue = value; best = t; }
            }
            if (best == null)
            {
                steps.Add(new PlanStep { action = "wait", seconds = 2 });
                return steps;
            }
            steps.AddRange(BotUtil.DoTask(best));
            return steps;
        }

        List<PlanStep> ThinkEscape(GameSim sim, Player p)
        {
            var map = sim.map;
            var home = BotUtil.GoHomeIfDeparting(sim, p);
            if (home != null) return home;
            if (p.hp < 45 || p.infection > 55) return BotUtil.Heal(sim, p);
            if (p.cargo.Count > 0) return BotUtil.DeliverCargo(sim, p);
            var revive = BotUtil.ReviveNearby(sim, p, 4f);
            if (revive != null) return revive;

            var pre = new List<PlanStep>();
            int from = p.node;
            if (p.medkits == 0)
            {
                var sup = map.nodes.Where(n => n.type == NodeType.Supply && n.medkits > 0 && sim.InZone(n.pos))
                    .OrderBy(n => map.dist[p.node, n.id]).FirstOrDefault();
                if (sup != null && map.dist[p.node, sup.id] < 8f)
                {
                    pre.Add(BotUtil.Goto(sup.id));
                    pre.Add(BotUtil.Act("take_medkit"));
                    from = sup.id;
                }
            }

            if (p.ammo == 0 && sim.Phase >= 1 && sim.players.Count(q => q.ammo > 0) < 3 && sim.Load(p) + pre.Count / 2 + sim.cfg.weaponWeight <= sim.cfg.capacity - 2)
            {
                var arm = map.nodes.Where(n => n.type == NodeType.Armory && n.weapons > 0 && sim.InZone(n.pos))
                    .OrderBy(n => map.dist[from, n.id]).FirstOrDefault();
                if (arm != null && map.dist[from, arm.id] < 30f)
                {
                    pre.Add(BotUtil.Goto(arm.id));
                    pre.Add(BotUtil.Act("take_weapon"));
                    from = arm.id;
                }
            }

            float fear = p.hp < 60f ? 0.35f : 0.18f;
            float Danger(int node) => 1f / (1f + fear * sim.zombies.Count(z => Vector2.Distance(z.pos, map.nodes[node].pos) < 3f)
                                               + 0.5f * map.nodes[node].infection);

            float speed = sim.Speed(p);
            List<PlanStep> best = null;
            float bestRate = 0f;
            foreach (var t in BotUtil.OpenTasks(sim, p))
            {
                float travel = map.dist[from, t.origin] + map.dist[t.origin, t.dest];
                if (travel >= CityMap.Inf) continue;
                float secs = travel / speed + 3f;
                float gain = t.reward + (t.urgent && sim.time + secs <= t.deadline ? t.bonus : 0);
                float rate = gain * Danger(t.origin) / secs;
                if (rate > bestRate) { bestRate = rate; best = BotUtil.DoTask(t); }
            }
            foreach (var inc in BotUtil.ReachableIncidents(sim, p))
            {

                float work = Mathf.Max(1f, inc.need - inc.progress) / (1 + inc.workers);
                float secs = map.dist[from, inc.node] / speed + work;
                if (sim.time + secs > inc.deadline) continue;
                float rate = inc.reward * sim.cfg.incidentRewardMul / (1 + inc.workers) / secs * Danger(inc.node);
                if (rate > bestRate) { bestRate = rate; best = BotUtil.Fix(inc); }
            }
            if (best == null) return pre.Count > 0 ? pre : BotUtil.Idle(sim, p);
            pre.AddRange(best);
            return pre;
        }
    }
}
