using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        Vector2? galleryTarget;
        float gallerySize = 3f;
        bool galleryMode;
        bool galleryNoFade;
        Vector2? galleryFocus;

        IEnumerator Gallery(string dir)
        {
            galleryMode = true;
            Directory.CreateDirectory(dir);
            gm.cfg.randomBots = 3;
            gm.StartMatch();
            while (Sim.status != MatchStatus.Running) yield return null;
            yield return new WaitForSecondsRealtime(1.5f);
            gm.speed = 0f;
            var map = Sim.map;
            var roads = map.nodes.Where(n => n.type == NodeType.Road && n.id != Sim.extraction && map.dist[Sim.extraction, n.id] >= 4f).ToList();
            Node Far(params int[] avoid) => roads.OrderByDescending(n => avoid.Length == 0 ? n.id : avoid.Min(a => Vector2.Distance(n.pos, map.nodes[a].pos))).First();

            Node Empty() => roads.OrderByDescending(n => Sim.players.Select(q => q.pos).Concat(Sim.zombies.Select(q => q.pos)).Append(map.nodes[Sim.extraction].pos)
                                                              .Min(q => Vector2.Distance(n.pos, q))).First();

            IEnumerator Shot(string name, Vector2 at, float size, float wait = 2.2f, Vector2? focus = null)
            {
                galleryTarget = at; gallerySize = size; galleryFocus = focus;
                yield return new WaitForSecondsRealtime(wait);
                GameManager.CaptureTo(Path.Combine(dir, name + ".png"));
                galleryFocus = null;
                yield return null;
            }

            var ps = Sim.players;
            if (ps.Count >= 3)
            {
                void Put(Player p, Node n) { p.node = n.id; p.nextNode = -1; p.pos = n.pos; p.plan.Clear(); p.pending = null; }
                var nA = Far(Sim.extraction); var nB = Far(Sim.extraction, nA.id); var nC = Far(Sim.extraction, nA.id, nB.id);
                Put(ps[0], nA); Put(ps[1], nB); Put(ps[2], nC);
                ps[1].state = PlayerState.Downed; ps[1].downedTimer = 15f;
                foreach (var t in Sim.tasks.Where(t => t.type == TaskType.Rescue && t.status == TaskStatus.Open).Take(2).ToList())
                {
                    t.status = TaskStatus.Carried; t.carrier = ps[2].id; ps[2].cargo.Add(t.id);
                }
                yield return new WaitForSecondsRealtime(1f);
                Debug.Log($"[Gallery] followers cargo={ps[2].cargo.Count} rescueOpen={Sim.tasks.Count(t => t.type == TaskType.Rescue)}");
                yield return Shot("player", nA.pos, 1.8f);
                yield return Shot("downed", nB.pos, 1.8f);
                yield return Shot("followers", nC.pos, 2.4f);
            }
            var z = Sim.zombies.FirstOrDefault();
            if (z != null) yield return Shot("zombie", Shown(z), 1.8f);
            var civ = Sim.tasks.FirstOrDefault(t => t.type == TaskType.Rescue && t.status == TaskStatus.Open);
            if (civ != null) yield return Shot("civilian", PileBase(civ.origin) + new Vector2(0.5f, 0), 2f);
            var part = Sim.tasks.FirstOrDefault(t => t.type == TaskType.Part && t.status == TaskStatus.Open);
            if (part != null) yield return Shot("parts", PileBase(part.origin) + new Vector2(0.5f, 0), 2f);
            var fuelNode = Empty();
            Sim.tasks.Add(new GameTask { id = 99999, type = TaskType.Fuel, origin = fuelNode.id, dest = Sim.extraction, weight = 2, reward = 80, availableAt = 0, expiresAt = -1, status = TaskStatus.Open });
            yield return Shot("fuel", PileBase(fuelNode.id) + new Vector2(0.5f, 0), 2f);
            Sim.tasks.RemoveAll(t => t.id == 99999);

            Vector2 Lift(Vector2 p, float h) =>
                p + new Vector2(Mathf.Sin(Yaw * Mathf.Deg2Rad), Mathf.Cos(Yaw * Mathf.Deg2Rad)) * (h / Mathf.Tan(Pitch * Mathf.Deg2Rad));
            var facilities = map.nodes.Where(x => x.type != NodeType.Road).ToList();
            Node Lonely(NodeType type) => map.nodes.Where(x => x.type == type)
                .OrderByDescending(x => facilities.Where(o => o != x).Select(o => Vector2.Distance(FacilityPos(o.id), FacilityPos(x.id))).DefaultIfEmpty(99f).Min())
                .FirstOrDefault();

            galleryNoFade = true;
            galleryHide = true;
            yield return Shot("extraction", Lift(FacilityPos(Sim.extraction), 1f), 3.8f, 2.2f, FacilityPos(Sim.extraction));
            galleryHide = false;
            yield return Shot("guard", Lift(guardTower, 1.4f), 2.4f, 2.2f, guardTower);
            foreach (var (name, type) in new[] { ("hospital", NodeType.Hospital), ("supply", NodeType.Supply), ("factory", NodeType.Factory), ("armory", NodeType.Armory) })
            {
                var n = Lonely(type);
                if (n != null) yield return Shot(name, Lift(FacilityPos(n.id), 1.8f), 3.4f, 2.2f, FacilityPos(n.id));
            }
            var gen = Lonely(NodeType.Generator);
            if (gen != null)
            {
                gen.powered = false;
                yield return Shot("generator_broken", Lift(FacilityPos(gen.id), 1.8f), 3.4f, 2.2f, FacilityPos(gen.id));
                foreach (var g in Sim.Generators) g.powered = true;
                yield return Shot("generator_on", Lift(FacilityPos(gen.id), 1.8f), 3.4f, 3f, FacilityPos(gen.id));
                var f = Lonely(NodeType.Factory);
                if (f != null) yield return Shot("factory_on", Lift(FacilityPos(f.id), 1.8f), 3.4f, 3f, FacilityPos(f.id));
            }

            galleryNoFade = false;
            Sim.incidents.Clear();
            var spot = Empty();
            yield return null;
            foreach (var kind in new[] { "outbreak", "bridge", "airdrop", "trapped", "guard", "heli", "medevac", "napalm", "decoy", "chest", "boss",
                                         "herbs", "gas", "rubble", "bell" })
            {
                var inc = Sim.DebugIncident(kind, spot.id);
                if (inc == null) continue;
                yield return null;
                var at = incidentFx.TryGetValue(inc.id, out var fx) ? fx.pos : map.nodes[inc.node].pos;
                yield return Shot("incident_" + kind, Lift((at + map.nodes[inc.node].pos) / 2f, 0.6f), 3f, 2.8f);
                Sim.incidents.Remove(inc);
                yield return null;
            }
            if (System.Environment.GetCommandLineArgs().Contains("-galleryOutcomes"))
            {
                // 事件結果的特效（只有加 -galleryOutcomes 才拍，README 的圖不受影響）
                float whole = Mathf.Max(map.Size.x, map.Size.y) * 0.62f;
                foreach (var (kind, solved, size, wait) in new[] { ("napalm", true, whole, 1.4f), ("herbs", true, whole, 0.9f), ("bell", true, whole, 1.0f),
                                                                  ("gas", true, 6f, 0.9f), ("gas", false, 6f, 0.45f) })
                {
                    var inc = Sim.DebugIncident(kind, spot.id);
                    if (inc == null) continue;
                    yield return new WaitForSecondsRealtime(0.4f);
                    galleryTarget = kind == "napalm" || kind == "herbs" || kind == "bell" ? map.Size * 0.5f : map.nodes[inc.node].pos;
                    gallerySize = size;
                    yield return new WaitForSecondsRealtime(0.4f);
                    Sim.DebugResolve(inc, solved);
                    yield return new WaitForSecondsRealtime(wait);
                    GameManager.CaptureTo(Path.Combine(dir, $"outcome_{kind}_{(solved ? "ok" : "fail")}.png"));
                    yield return new WaitForSecondsRealtime(2.5f);
                    Sim.incidents.Remove(inc);
                }
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
