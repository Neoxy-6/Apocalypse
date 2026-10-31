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

            IEnumerator Shot(string name, Vector2 at, float size, float wait = 2.2f)
            {
                galleryTarget = at; gallerySize = size;
                yield return new WaitForSecondsRealtime(wait);
                GameManager.CaptureTo(Path.Combine(dir, name + ".png"));
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

            galleryNoFade = true;
            yield return Shot("extraction", FacilityPos(Sim.extraction), 4.2f);
            yield return Shot("guard", FacilityPos(Sim.extraction) + new Vector2(1.4f, 1.4f), 2.2f);
            foreach (var (name, type) in new[] { ("hospital", NodeType.Hospital), ("supply", NodeType.Supply), ("factory", NodeType.Factory) })
            {
                var n = map.nodes.FirstOrDefault(x => x.type == type);
                if (n != null) yield return Shot(name, FacilityPos(n.id), 2.8f);
            }
            var gen = Sim.Generators.FirstOrDefault();
            if (gen != null)
            {
                gen.powered = false;
                yield return Shot("generator_broken", FacilityPos(gen.id), 2.4f);
                foreach (var g in Sim.Generators) g.powered = true;
                yield return Shot("generator_on", FacilityPos(gen.id), 2.4f, 3f);
                var f = map.nodes.FirstOrDefault(x => x.type == NodeType.Factory);
                if (f != null) yield return Shot("factory_on", FacilityPos(f.id), 2.8f, 3f);
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
                yield return Shot("incident_" + kind, (at + map.nodes[inc.node].pos) / 2f, 3f, 2.8f);
                Sim.incidents.Remove(inc);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
