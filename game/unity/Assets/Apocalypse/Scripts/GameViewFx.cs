using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        class IncidentFx { public Transform root; public System.Action<float, float> tick; public Vector2 pos; }
        readonly Dictionary<int, IncidentFx> incidentFx = new Dictionary<int, IncidentFx>();

        void UpdateIncidentFx(float t)
        {
            foreach (var inc in Sim.incidents)
            {
                incidentFx.TryGetValue(inc.id, out var fx);
                if (inc.status == "active")
                {
                    if (fx == null) incidentFx[inc.id] = fx = MakeIncidentFx(inc, t);
                    fx.tick?.Invoke(t, inc.deadline - Sim.time);
                }
                else if (fx != null)
                {
                    bool solved = inc.status == "solved";
                    OutcomeBurst(inc, fx.pos, solved);
                    PlayOutcome(inc, fx.pos, solved, t);
                    RetireIncidentProps(fx.root, solved, t);
                    incidentFx.Remove(inc.id);
                }
            }
            foreach (int id in incidentFx.Keys.Where(id => Sim.incidents.All(i => i.id != id)).ToList())
            {
                Destroy(incidentFx[id].root.gameObject);
                incidentFx.Remove(id);
            }
        }

        Vector2 SideSpot(Vector2 p)
        {
            Vector2 best = p + new Vector2(1f, 1f);
            float bestClear = -1f;
            foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            {
                var q = p + d.normalized * 1.35f;
                float c = RoadClearance(q);
                if (c > bestClear) { bestClear = c; best = q; }
            }
            return best;
        }

        SpriteRenderer FxRing(Transform parent, Vector2 pos, Color c, float size, float h = 0.06f)
        {
            var sr = Flat(parent, ringSprite, c, size, 8, h);
            sr.transform.position = W(pos, h);
            return sr;
        }

        SpriteRenderer FxGlow(Transform parent, Vector2 pos, Color c, float size)
        {
            var sr = Flat(parent, soft, c, size, 4, 0.04f);
            sr.transform.position = W(pos, 0.04f);
            return sr;
        }

        static void Ripple(SpriteRenderer[] rings, Color c, float t, float speed, float maxSize)
        {
            for (int i = 0; i < rings.Length; i++)
            {
                float k = (t * speed + i / (float)rings.Length) % 1f;
                rings[i].transform.localScale = Vector3.one * (0.5f + k * maxSize);
                rings[i].color = new Color(c.r, c.g, c.b, c.a * (1f - k));
            }
        }

        IncidentFx MakeIncidentFx(Incident inc, float t0)
        {
            var map = Sim.map;
            var node = map.nodes[inc.node].pos;
            var fx = new IncidentFx { pos = node };
            fx.root = new GameObject("incident" + inc.id).transform;
            fx.root.SetParent(dynRoot, false);
            var root = fx.root;
            switch (inc.kind)
            {
                case "outbreak":
                {
                    Particles(root, W(node, 0.1f), new Color(0.45f, 0.9f, 0.3f, 0.45f), new Color(0.2f, 0.6f, 0.15f, 0.35f), 14f, 2.6f, 0.6f, 0.9f, -0.04f, new Vector3(1.4f, 1.4f, 0.1f), 0.12f);
                    var glow = FxGlow(root, node, new Color(0.4f, 1f, 0.3f, 0.4f), 4f);
                    fx.tick = (t, left) => glow.color = new Color(0.4f, 1f, 0.3f, 0.25f + 0.2f * Mathf.Sin(t * 4f));
                    break;
                }
                case "bridge":
                {
                    Vector2 at = node;
                    if (inc.edge >= 0 && inc.edge < map.edges.Count) { var e = map.edges[inc.edge]; at = (map.nodes[e.a].pos + map.nodes[e.b].pos) / 2f; }
                    fx.pos = at;
                    var g = Group(root, at, 45f);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        Part(g, PrimitiveType.Cube, new Color(1f, 0.55f, 0.1f), new Vector3(i * 0.5f, 0.3f, 0), new Vector3(0.7f, 0.14f, 0.05f), default, true);
                        Part(g, PrimitiveType.Cube, Color.white, new Vector3(i * 0.5f, 0.3f, -0.01f), new Vector3(0.12f, 0.14f, 0.05f), default, true);
                        Part(g, PrimitiveType.Cube, new Color(0.3f, 0.3f, 0.3f), new Vector3(i * 0.5f, 0.12f, 0), new Vector3(0.05f, 0.25f, 0.05f));
                    }
                    var lamp = Part(g, PrimitiveType.Sphere, new Color(1f, 0.85f, 0.2f), new Vector3(0, 0.5f, 0), Vector3.one * 0.14f, default, true).GetComponent<Renderer>();
                    Particles(root, W(at, 0.2f), new Color(1f, 0.9f, 0.4f, 1f), new Color(1f, 0.6f, 0.2f, 1f), 12f, 0.5f, 2.2f, 0.07f, 1.5f, new Vector3(0.3f, 0.3f, 0.05f), 0.6f, 0f, 9);
                    fx.tick = (t, left) => lamp.sharedMaterial = UnlitMat(Mathf.Repeat(t, 0.6f) < 0.3f ? new Color(1f, 0.85f, 0.2f) : new Color(0.3f, 0.25f, 0.05f));
                    break;
                }
                case "airdrop":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    Particles(root, W(spot + new Vector2(0.5f, 0), 0.1f), new Color(1f, 0.3f, 0.25f, 0.6f), new Color(0.8f, 0.2f, 0.2f, 0.4f), 8f, 3f, 0.7f, 0.5f, -0.05f, new Vector3(0.1f, 0.1f, 0.05f), 0.15f);
                    var drop = Group(root, spot, 0f, 6f);
                    Part(drop, PrimitiveType.Cube, new Color(0.45f, 0.35f, 0.2f), new Vector3(0, 0.25f, 0), Vector3.one * 0.5f);
                    Part(drop, PrimitiveType.Cube, new Color(0.3f, 0.5f, 0.3f), new Vector3(0, 0.25f, 0), new Vector3(0.52f, 0.1f, 0.52f));
                    var chute = new GameObject("chute").transform; chute.SetParent(drop, false);
                    Part(chute, PrimitiveType.Sphere, new Color(0.9f, 0.9f, 0.85f), new Vector3(0, 1.4f, 0), new Vector3(1.4f, 0.55f, 1.4f));
                    for (int i = 0; i < 4; i++)
                        Part(chute, PrimitiveType.Cube, new Color(0.7f, 0.7f, 0.7f), new Vector3((i % 2 == 0 ? -0.3f : 0.3f), 0.95f, (i < 2 ? -0.3f : 0.3f)), new Vector3(0.015f, 0.9f, 0.015f));
                    fx.tick = (t, left) =>
                    {
                        float h = Mathf.Max(0f, 6f - (t - t0) * 1.5f);
                        drop.position = W(spot, h) + new Vector3(Mathf.Sin(t * 1.3f) * 0.15f * Mathf.Min(1f, h), 0, 0);
                        chute.gameObject.SetActive(h > 0.05f);
                    };
                    break;
                }
                case "trapped":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    var people = new List<Transform>();
                    Color[] shirts = { new Color(0.85f, 0.75f, 0.6f), new Color(0.6f, 0.75f, 0.9f), new Color(0.9f, 0.6f, 0.6f), new Color(0.7f, 0.85f, 0.6f), new Color(0.9f, 0.85f, 0.5f) };
                    for (int i = 0; i < 5; i++)
                    {
                        var p = Group(root, spot + new Vector2(Mathf.Cos(i * 1.3f), Mathf.Sin(i * 1.3f)) * 0.45f, i * 70f);
                        MiniCapy(p, shirts[i]).transform.localScale = Vector3.one * 1.2f;
                        people.Add(p);
                    }
                    Part(Group(root, spot, 30f), PrimitiveType.Cube, Color.white, new Vector3(0, 0.75f, 0), new Vector3(0.45f, 0.22f, 0.03f), default, true);
                    fx.tick = (t, left) =>
                    {
                        for (int i = 0; i < people.Count; i++)
                            people[i].position = new Vector3(people[i].position.x, Mathf.Abs(Mathf.Sin(t * 5f + i)) * 0.15f, people[i].position.z);
                    };
                    break;
                }
                case "guard":
                {
                    var spot = gm.cfg.escape ? ShelterPos(map.nodes[Sim.extraction]) + new Vector2(0.9f, 0) : SideSpot(node);
                    fx.pos = spot;
                    var g = Group(root, spot, 0f);
                    Part(g, PrimitiveType.Cylinder, new Color(0.3f, 0.3f, 0.32f), new Vector3(0, 0.6f, 0), new Vector3(0.08f, 0.6f, 0.08f));
                    Part(g, PrimitiveType.Sphere, new Color(1f, 0.15f, 0.1f), new Vector3(0, 1.25f, 0), Vector3.one * 0.25f, default, true);
                    var beam = Group(root, spot, 0f, 1.25f);
                    Part(beam, PrimitiveType.Cube, new Color(1f, 0.2f, 0.15f), new Vector3(0, 0, 1.2f), new Vector3(0.1f, 0.05f, 2.4f), default, true);
                    var glow = FxGlow(root, spot, new Color(1f, 0.15f, 0.1f, 0.35f), 5f);
                    fx.tick = (t, left) => { beam.rotation = Quaternion.Euler(0, t * 240f, 0); glow.color = new Color(1f, 0.15f, 0.1f, 0.2f + 0.2f * Mathf.Abs(Mathf.Sin(t * 4f))); };
                    break;
                }
                case "heli":
                {
                    var spot = gm.cfg.escape ? FacilityPos(Sim.extraction) : node;
                    fx.pos = spot;
                    Particles(root, W(spot, 0.9f), new Color(0.15f, 0.15f, 0.15f, 0.7f), new Color(0.3f, 0.28f, 0.26f, 0.5f), 10f, 4f, 0.8f, 0.9f, -0.04f, new Vector3(0.4f, 0.4f, 0.05f), 0.15f);
                    Particles(root, W(spot, 0.8f), new Color(1f, 0.9f, 0.4f, 1f), new Color(1f, 0.5f, 0.1f, 1f), 16f, 0.4f, 2.5f, 0.06f, 1.5f, new Vector3(0.3f, 0.3f, 0.05f), 0.8f, 0f, 9);
                    break;
                }
                case "medevac":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    var ring = FxRing(root, spot, new Color(1f, 1f, 1f, 0.7f), 2.4f);
                    var heliT = Group(root, spot, 30f, 2f);
                    Part(heliT, PrimitiveType.Capsule, new Color(0.92f, 0.92f, 0.94f), new Vector3(0, 0, 0), new Vector3(0.6f, 0.6f, 1.2f), new Vector3(90f, 0, 0));
                    Part(heliT, PrimitiveType.Cube, new Color(0.92f, 0.92f, 0.94f), new Vector3(0, 0.1f, -1f), new Vector3(0.1f, 0.12f, 1.1f));
                    Part(heliT, PrimitiveType.Cube, new Color(0.9f, 0.15f, 0.2f), new Vector3(0.31f, 0.05f, 0), new Vector3(0.02f, 0.3f, 0.1f), default, true);
                    Part(heliT, PrimitiveType.Cube, new Color(0.9f, 0.15f, 0.2f), new Vector3(0.31f, 0.05f, 0), new Vector3(0.02f, 0.1f, 0.3f), default, true);
                    var rotorT = new GameObject("rotor").transform; rotorT.SetParent(heliT, false); rotorT.localPosition = new Vector3(0, 0.38f, 0);
                    Part(rotorT, PrimitiveType.Cube, new Color(0.2f, 0.2f, 0.2f), Vector3.zero, new Vector3(2.2f, 0.03f, 0.12f));
                    var lamp = Part(heliT, PrimitiveType.Sphere, Color.white, new Vector3(0, -0.3f, 0.3f), Vector3.one * 0.12f, default, true).GetComponent<Renderer>();
                    fx.tick = (t, left) =>
                    {
                        heliT.position = W(spot, 2f + 0.12f * Mathf.Sin(t * 2f));
                        rotorT.localRotation = Quaternion.Euler(0, t * 900f % 360f, 0);
                        lamp.sharedMaterial = UnlitMat(Mathf.Repeat(t, 0.8f) < 0.15f ? Color.white : new Color(0.3f, 0.3f, 0.3f));
                        ring.transform.localScale = Vector3.one * (2.3f + 0.2f * Mathf.Sin(t * 3f));
                    };
                    break;
                }
                case "napalm":
                {
                    var spot = SideSpot(node); fx.pos = node;
                    var g = Group(root, spot, 0f);
                    Part(g, PrimitiveType.Cube, new Color(0.35f, 0.35f, 0.38f), new Vector3(0, 0.7f, 0), new Vector3(0.08f, 1.4f, 0.08f));
                    Part(g, PrimitiveType.Cube, new Color(0.35f, 0.35f, 0.38f), new Vector3(0, 1.1f, 0), new Vector3(0.4f, 0.04f, 0.04f));
                    var lamp = Part(g, PrimitiveType.Sphere, new Color(1f, 0.2f, 0.1f), new Vector3(0, 1.45f, 0), Vector3.one * 0.14f, default, true).GetComponent<Renderer>();
                    var radio = new[] { FxRing(root, spot, Color.white, 1f, 1.4f), FxRing(root, spot, Color.white, 1f, 1.4f) };
                    foreach (var r in radio) r.transform.position = W(spot, 1.45f);
                    var target = FxRing(root, node, new Color(1f, 0.35f, 0.1f, 0.9f), 5f);
                    var cross = Group(root, node, 0f, 0.07f);
                    Part(cross, PrimitiveType.Cube, new Color(1f, 0.35f, 0.1f), Vector3.zero, new Vector3(3f, 0.01f, 0.06f), default, true);
                    Part(cross, PrimitiveType.Cube, new Color(1f, 0.35f, 0.1f), Vector3.zero, new Vector3(0.06f, 0.01f, 3f), default, true);
                    fx.tick = (t, left) =>
                    {
                        lamp.sharedMaterial = UnlitMat(Mathf.Repeat(t, 0.7f) < 0.35f ? new Color(1f, 0.2f, 0.1f) : new Color(0.3f, 0.05f, 0.05f));
                        float k = Mathf.Repeat(t * 0.5f, 1f);
                        target.transform.localScale = Vector3.one * Mathf.Lerp(6f, 2.5f, k);
                        cross.rotation = Quaternion.Euler(0, t * 40f, 0);
                        Ripple(radio, new Color(1f, 0.5f, 0.3f, 0.7f), t, 0.8f, 1.5f);
                    };
                    break;
                }
                case "decoy":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    var g = Group(root, spot, 225f);
                    for (int i = 0; i < 3; i++)
                        Part(g, PrimitiveType.Cube, new Color(0.25f, 0.25f, 0.28f), new Vector3(Mathf.Cos(i * 2.1f) * 0.2f, 0.22f, Mathf.Sin(i * 2.1f) * 0.2f), new Vector3(0.03f, 0.45f, 0.03f), new Vector3(Mathf.Sin(i * 2.1f) * 15f, 0, -Mathf.Cos(i * 2.1f) * 15f));
                    Part(g, PrimitiveType.Cube, new Color(0.18f, 0.18f, 0.2f), new Vector3(0, 0.55f, 0), new Vector3(0.45f, 0.35f, 0.3f));
                    var cone = Part(g, PrimitiveType.Cylinder, new Color(0.7f, 0.45f, 1f), new Vector3(0, 0.55f, -0.16f), new Vector3(0.25f, 0.02f, 0.25f), new Vector3(90f, 0, 0), true).transform;
                    var waves = Enumerable.Range(0, 4).Select(_ => FxRing(root, spot, Color.white, 1f)).ToArray();
                    fx.tick = (t, left) =>
                    {
                        Ripple(waves, new Color(0.75f, 0.5f, 1f, 0.8f), t, 0.7f, 6f);
                        cone.localScale = new Vector3(0.25f, 0.02f, 0.25f) * (1f + 0.15f * Mathf.Sin(t * 20f));
                    };
                    break;
                }
                case "chest":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    var g = Group(root, spot, 0f, 0.3f);
                    Part(g, PrimitiveType.Cube, new Color(0.5f, 0.3f, 0.15f), new Vector3(0, 0, 0), new Vector3(0.55f, 0.32f, 0.38f));
                    Part(g, PrimitiveType.Cube, new Color(0.55f, 0.33f, 0.17f), new Vector3(0, 0.2f, 0), new Vector3(0.57f, 0.12f, 0.4f));
                    Part(g, PrimitiveType.Cube, new Color(1f, 0.82f, 0.25f), new Vector3(0, 0.05f, 0), new Vector3(0.59f, 0.05f, 0.42f), default, true);
                    Part(g, PrimitiveType.Cube, new Color(1f, 0.82f, 0.25f), new Vector3(0, 0.08f, -0.2f), new Vector3(0.08f, 0.1f, 0.02f), default, true);
                    Particles(root, W(spot, 0.4f), new Color(1f, 0.9f, 0.4f, 1f), new Color(1f, 0.75f, 0.2f, 1f), 10f, 1.2f, 0.5f, 0.1f, -0.2f, new Vector3(0.6f, 0.6f, 0.2f), 0.2f, 0f, 9);
                    var glow = FxGlow(root, spot, new Color(1f, 0.85f, 0.3f, 0.35f), 2.2f);
                    fx.tick = (t, left) =>
                    {
                        g.position = W(spot, 0.35f + 0.12f * Mathf.Sin(t * 3f));
                        g.rotation = Quaternion.Euler(0, t * 60f, 0);
                        glow.color = new Color(1f, 0.85f, 0.3f, 0.25f + 0.15f * Mathf.Sin(t * 3f));
                    };
                    break;
                }
                case "boss":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    var crack = FxRing(root, spot, new Color(0.1f, 0.05f, 0.05f, 0.8f), 2.4f, 0.03f);
                    var body = Group(root, spot, 225f);
                    var king = ZombieKing(body, 2.3f);
                    Particles(root, W(spot, 0.05f), new Color(0.45f, 0.38f, 0.3f, 0.5f), new Color(0.3f, 0.26f, 0.22f, 0.4f), 8f, 1.5f, 0.4f, 0.7f, 0f, new Vector3(1.4f, 1.4f, 0.05f), 0.3f);
                    fx.tick = (t, left) =>
                    {
                        body.rotation = Quaternion.Euler(0, 225f, Mathf.Sin(t * 2f) * 8f);
                        king.armL.localRotation = Quaternion.Euler(-50f + Mathf.Sin(t * 4f) * 20f, 0, 0);
                        king.armR.localRotation = Quaternion.Euler(-50f - Mathf.Sin(t * 4f) * 20f, 0, 0);
                        body.position = W(spot, Mathf.Abs(Mathf.Sin(t * 2f)) * 0.08f);
                        crack.transform.localScale = Vector3.one * (2.3f + 0.3f * Mathf.Abs(Mathf.Sin(t * 2f)));
                    };
                    break;
                }
                case "herbs":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    var buds = new List<Transform>();
                    for (int i = 0; i < 6; i++)
                    {
                        var p = spot + new Vector2(Mathf.Cos(i * 1.05f), Mathf.Sin(i * 1.05f)) * (0.25f + 0.15f * (i % 2));
                        Part(Group(root, p, 0f), PrimitiveType.Cube, new Color(0.2f, 0.45f, 0.2f), new Vector3(0, 0.15f, 0), new Vector3(0.03f, 0.3f, 0.03f));
                        buds.Add(Part(Group(root, p, 0f), PrimitiveType.Sphere, new Color(0.45f, 1f, 0.75f), new Vector3(0, 0.33f, 0), Vector3.one * 0.12f, default, true).transform);
                    }
                    Particles(root, W(spot, 0.3f), new Color(0.6f, 1f, 0.8f, 1f), new Color(0.4f, 0.95f, 1f, 1f), 9f, 2f, 0.3f, 0.08f, -0.1f, new Vector3(0.7f, 0.7f, 0.1f), 0.1f, 0.2f, 9);
                    var glow = FxGlow(root, spot, new Color(0.4f, 1f, 0.7f, 0.3f), 2f);
                    fx.tick = (t, left) =>
                    {
                        for (int i = 0; i < buds.Count; i++) buds[i].localScale = Vector3.one * (0.1f + 0.04f * Mathf.Sin(t * 3f + i));
                        glow.color = new Color(0.4f, 1f, 0.7f, 0.2f + 0.12f * Mathf.Sin(t * 2f));
                    };
                    break;
                }
                case "gas":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    var g = Group(root, spot, 45f);
                    Part(g, PrimitiveType.Cube, new Color(0.85f, 0.85f, 0.85f), new Vector3(0, 0.75f, 0), new Vector3(1.3f, 0.08f, 0.8f));
                    Part(g, PrimitiveType.Cube, new Color(0.9f, 0.3f, 0.2f), new Vector3(0, 0.82f, -0.41f), new Vector3(1.3f, 0.12f, 0.02f), default, true);
                    Part(g, PrimitiveType.Cube, new Color(0.6f, 0.6f, 0.62f), new Vector3(-0.5f, 0.37f, 0), new Vector3(0.06f, 0.74f, 0.06f));
                    Part(g, PrimitiveType.Cube, new Color(0.6f, 0.6f, 0.62f), new Vector3(0.5f, 0.37f, 0), new Vector3(0.06f, 0.74f, 0.06f));
                    Part(g, PrimitiveType.Cube, new Color(0.85f, 0.2f, 0.15f), new Vector3(-0.2f, 0.22f, 0), new Vector3(0.18f, 0.44f, 0.14f));
                    Part(g, PrimitiveType.Cube, new Color(0.85f, 0.2f, 0.15f), new Vector3(0.2f, 0.22f, 0), new Vector3(0.18f, 0.44f, 0.14f));
                    var puddle = Flat(root, circle, new Color(0.15f, 0.12f, 0.2f, 0.85f), 1.4f, 3, 0.03f);
                    puddle.transform.position = W(spot, 0.03f);
                    var flames = Particles(root, W(spot, 0.1f), new Color(1f, 0.75f, 0.2f, 1f), new Color(1f, 0.3f, 0.05f, 1f), 4f, 0.8f, 0.8f, 0.3f, -0.1f, new Vector3(0.6f, 0.6f, 0.05f), 0f, 0f, 9);
                    var warn = FxGlow(root, spot, new Color(1f, 0.5f, 0.1f, 0.4f), 3f);
                    fx.tick = (t, left) =>
                    {
                        float danger = Mathf.Clamp01(1f - left / 40f);
                        var em = flames.emission; em.rateOverTime = 4f + danger * 30f;
                        float speed = 2f + danger * 8f;
                        warn.color = new Color(1f, 0.5f, 0.1f, Mathf.Repeat(t * speed, 1f) < 0.5f ? 0.15f + danger * 0.3f : 0.05f);
                    };
                    break;
                }
                case "rubble":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    var pieces = new List<Transform>();
                    var rnd = new System.Random(inc.id);
                    for (int i = 0; i < 9; i++)
                    {
                        float s = F(rnd, 0.18f, 0.4f);
                        pieces.Add(Part(Group(root, spot + new Vector2(F(rnd, -0.5f, 0.5f), F(rnd, -0.5f, 0.5f)), F(rnd, 0, 360)), PrimitiveType.Cube,
                                        Vary(new Color(0.42f, 0.38f, 0.33f), rnd, 0.2f), new Vector3(0, s * 0.4f, 0), Vector3.one * s, new Vector3(F(rnd, 0, 30), 0, F(rnd, 0, 30))).transform);
                    }
                    Particles(root, W(spot, 0.2f), new Color(0.6f, 0.55f, 0.48f, 0.5f), new Color(0.45f, 0.4f, 0.35f, 0.4f), 6f, 2f, 0.5f, 0.6f, -0.02f, new Vector3(0.8f, 0.8f, 0.1f), 0.2f);
                    fx.tick = (t, left) =>
                    {
                        for (int i = 0; i < pieces.Count; i++)
                            pieces[i].localRotation = Quaternion.Euler(Mathf.Sin(t * 6f + i) * 4f, 0, Mathf.Cos(t * 5f + i) * 4f);
                    };
                    break;
                }
                case "bell":
                {
                    var spot = SideSpot(node); fx.pos = spot;
                    var g = Group(root, spot, 0f);
                    var stone = new Color(0.55f, 0.52f, 0.48f);
                    for (int i = 0; i < 4; i++)
                        Part(g, PrimitiveType.Cube, stone, new Vector3(i % 2 == 0 ? -0.3f : 0.3f, 0.7f, i < 2 ? -0.3f : 0.3f), new Vector3(0.12f, 1.4f, 0.12f));
                    Part(g, PrimitiveType.Cube, new Color(0.55f, 0.25f, 0.2f), new Vector3(0, 1.55f, 0), new Vector3(0.6f, 0.4f, 0.6f), new Vector3(0, 45f, 0));
                    var bellPivot = Group(root, spot, 0f, 1.3f);
                    Part(bellPivot, PrimitiveType.Cylinder, new Color(0.85f, 0.7f, 0.25f), new Vector3(0, -0.2f, 0), new Vector3(0.32f, 0.18f, 0.32f));
                    Part(bellPivot, PrimitiveType.Sphere, new Color(0.85f, 0.7f, 0.25f), new Vector3(0, -0.05f, 0), Vector3.one * 0.3f);
                    var waves = Enumerable.Range(0, 3).Select(_ => FxRing(root, spot, Color.white, 1f)).ToArray();
                    fx.tick = (t, left) =>
                    {
                        bellPivot.rotation = Quaternion.Euler(Mathf.Sin(t * 3f) * 30f, 0, 0);
                        Ripple(waves, new Color(1f, 0.85f, 0.35f, 0.7f), t, 0.5f, 7f);
                    };
                    break;
                }
            }
            return fx;
        }

        void Burst(Vector3 at, Color c0, Color c1, int count, float speed, float size, float life, float gravity, float radius = 0.3f, bool ring = false)
        {
            if (particleMat == null) particleMat = new Material(lineMat) { mainTexture = soft.texture };
            var go = new GameObject("burst");
            go.transform.SetParent(dynRoot, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            if (ring)
            {
                sh.shapeType = ParticleSystemShapeType.Circle;
                sh.radius = radius;
                sh.radiusThickness = 0f;
                sh.rotation = new Vector3(90f, 0, 0);
            }
            else
            {
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = radius;
            }
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = particleMat;
            pr.sortingOrder = 10;
            ps.Play();
        }

        void Explosion(Vector2 pos, float scale)
        {
            Shake(0.25f * scale);
            Burst(W(pos, 0.4f), new Color(1f, 0.85f, 0.3f, 1f), new Color(1f, 0.35f, 0.05f, 1f), (int)(60 * scale), 3.5f * scale, 0.6f * scale, 0.8f, -0.2f, 0.4f * scale);
            Burst(W(pos, 0.8f), new Color(0.2f, 0.18f, 0.17f, 0.8f), new Color(0.35f, 0.32f, 0.3f, 0.6f), (int)(30 * scale), 1.5f * scale, 1.4f * scale, 2.5f, -0.15f, 0.6f * scale);
        }

        void OutcomeBurst(Incident inc, Vector2 pos, bool solved)
        {
            if (solved)
            {
                switch (inc.kind)
                {
                    case "napalm": Explosion(inc.node >= 0 ? Sim.map.nodes[inc.node].pos : pos, 2f); break;
                    case "decoy": Burst(W(pos, 0.3f), new Color(0.8f, 0.55f, 1f, 1f), new Color(0.6f, 0.4f, 1f, 1f), 80, 6f, 0.25f, 1.2f, 0f, 0.5f, true); break;
                    case "boss":
                        Burst(W(pos, 0.2f), new Color(0.5f, 0.42f, 0.32f, 0.8f), new Color(0.35f, 0.3f, 0.25f, 0.7f), 50, 3f, 0.8f, 1.5f, 0.3f, 0.8f, true);
                        Explosion(pos, 1f);
                        break;
                    case "chest": Burst(W(pos, 0.4f), new Color(1f, 0.9f, 0.35f, 1f), new Color(1f, 0.7f, 0.2f, 1f), 60, 3.5f, 0.18f, 1.2f, 0.6f); break;
                    case "medevac": Burst(W(pos, 1.5f), Color.white, new Color(1f, 0.6f, 0.65f, 1f), 60, 3f, 0.2f, 1.4f, 0.3f, 0.6f); break;
                }
                Burst(W(pos, 0.3f), new Color(0.6f, 1f, 0.75f, 1f), new Color(1f, 1f, 1f, 1f), 40, 2.5f, 0.15f, 1.2f, -0.4f, 0.4f);
                Burst(W(pos, 0.1f), new Color(0.4f, 1f, 0.6f, 0.9f), new Color(0.4f, 1f, 0.6f, 0.6f), 50, 4f, 0.12f, 0.6f, 0f, 0.3f, true);
            }
            else
            {
                Burst(W(pos, 0.4f), new Color(0.25f, 0.05f, 0.05f, 0.8f), new Color(0.1f, 0.1f, 0.1f, 0.7f), 30, 1.2f, 1.0f, 2f, -0.1f, 0.5f);
                Burst(W(pos, 0.1f), new Color(1f, 0.2f, 0.15f, 0.9f), new Color(0.6f, 0.05f, 0.05f, 0.7f), 40, 3f, 0.14f, 0.7f, 0f, 0.3f, true);
            }
        }

        void EventFx(GameEvent e)
        {
            if (e.node < 0 || e.node >= Sim.map.nodes.Count) return;
            var p = Sim.map.nodes[e.node].pos;
            switch (e.type)
            {
                case "down":
                    Burst(W(p, 0.4f), new Color(1f, 0.3f, 0.2f, 1f), new Color(0.6f, 0.05f, 0.05f, 0.8f), 25, 2f, 0.15f, 0.8f, 0.6f, 0.2f);
                    break;
                case "revive":
                    Burst(W(p, 0.2f), new Color(0.5f, 1f, 0.6f, 1f), Color.white, 35, 2.2f, 0.14f, 1.2f, -0.5f, 0.3f);
                    Burst(W(p, 0.05f), new Color(0.4f, 1f, 0.6f, 0.9f), new Color(0.4f, 1f, 0.6f, 0.6f), 30, 2.5f, 0.1f, 0.5f, 0f, 0.2f, true);
                    break;
                case "eliminated":
                    Shake(0.2f);
                    Burst(W(p, 0.5f), new Color(0.12f, 0.12f, 0.12f, 0.8f), new Color(0.3f, 0.1f, 0.1f, 0.7f), 30, 1f, 1.1f, 2.5f, -0.1f, 0.3f);
                    break;
                case "heli":
                    Shake(0.3f);
                    var pad = gm.cfg.escape ? FacilityPos(Sim.extraction) : p;
                    Burst(W(pad, 0.5f), new Color(1f, 0.9f, 0.4f, 1f), new Color(1f, 1f, 1f, 1f), 70, 4f, 0.18f, 1.5f, 0.4f, 0.5f);
                    Burst(W(pad, 0.1f), new Color(1f, 0.85f, 0.3f, 1f), new Color(1f, 0.85f, 0.3f, 0.6f), 60, 5f, 0.14f, 0.8f, 0f, 0.6f, true);
                    break;
                case "fail":
                    if (e.text.Contains("殭屍"))
                        Burst(W(p, 0.3f), new Color(0.4f, 0.8f, 0.3f, 0.6f), new Color(0.2f, 0.5f, 0.15f, 0.5f), 20, 0.8f, 0.8f, 1.8f, -0.1f, 0.3f);
                    break;
            }
        }
    }
}
