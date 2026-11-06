using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        class Outcome { public Transform root; public float t0, dur; public System.Action<float, float> tick; }
        readonly List<Outcome> outcomes = new List<Outcome>();

        void UpdateOutcomes(float t)
        {
            for (int i = outcomes.Count - 1; i >= 0; i--)
            {
                var o = outcomes[i];
                float k = (t - o.t0) / o.dur;
                if (k >= 1f || o.root == null)
                {
                    if (o.root != null) Destroy(o.root.gameObject);
                    outcomes.RemoveAt(i);
                    continue;
                }
                o.tick?.Invoke(Mathf.Clamp01(k), t);
            }
        }

        Outcome NewOutcome(string name, float dur, float t)
        {
            var root = new GameObject(name).transform;
            root.SetParent(dynRoot, false);
            var o = new Outcome { root = root, t0 = t, dur = dur };
            outcomes.Add(o);
            return o;
        }

        void RetireIncidentProps(Transform props, bool solved, float t)
        {
            foreach (var ps in props.GetComponentsInChildren<ParticleSystem>()) { var em = ps.emission; em.enabled = false; }
            var o = new Outcome { root = props, t0 = t, dur = solved ? 0.6f : 1.4f };
            var start = props.localScale;
            var pos0 = props.position;
            var rends = props.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
            o.tick = (k, now) =>
            {
                if (solved) props.localScale = start * (k < 0.3f ? 1f + k : Mathf.Lerp(1.3f, 0f, (k - 0.3f) / 0.7f));
                else
                {
                    props.position = pos0 + Vector3.down * k * 1.2f + new Vector3(Mathf.Sin(now * 40f) * 0.03f * (1f - k), 0, 0);
                    if (k > 0.05f) foreach (var r in rends) if (r is MeshRenderer) r.sharedMaterial = LitMat(new Color(0.18f, 0.16f, 0.16f));
                }
            };
            outcomes.Add(o);
        }

        static Vector3 Arc(Vector3 a, Vector3 b, float h, float k) => Vector3.Lerp(a, b, k) + Vector3.up * (4f * h * k * (1f - k));

        static void Wave(SpriteRenderer sr, Color c, float k, float size0, float size1)
        {
            sr.transform.localScale = Vector3.one * Mathf.Lerp(size0, size1, k);
            sr.color = new Color(c.r, c.g, c.b, c.a * (1f - k));
        }

        static void PaintAll(GameObject go, Color c)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = LitMat(c);
        }

        LineRenderer Bolt(Transform parent, Color c, float width)
        {
            var go = new GameObject("bolt");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = UnlitMat(c);
            lr.widthMultiplier = width;
            lr.positionCount = 12;
            lr.numCapVertices = 2;
            lr.sortingOrder = 12;
            return lr;
        }

        static void Zap(LineRenderer lr, Vector3 a, Vector3 b, float jitter, System.Random rnd)
        {
            int n = lr.positionCount;
            for (int i = 0; i < n; i++)
            {
                float k = i / (n - 1f);
                var p = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;
                if (i > 0 && i < n - 1) p += new Vector3(F(rnd, -jitter, jitter), F(rnd, -jitter, jitter), F(rnd, -jitter, jitter));
                lr.SetPosition(i, p);
            }
        }

        static void RunCivilian(Transform cv, Vector3 a, Vector3 b, float k, float phase)
        {
            var dir = b - a; dir.y = 0;
            cv.position = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, k)) + Vector3.up * Mathf.Abs(Mathf.Sin(k * 40f + phase)) * 0.18f;
            if (dir.sqrMagnitude > 1e-4f) cv.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(0, 0, Mathf.Sin(k * 40f + phase) * 10f);
            cv.localScale = Vector3.one * 1.1f * (k > 0.85f ? (1f - k) / 0.15f : 1f);
        }

        Transform MiniHeli(Transform parent, Color body, out Transform rotor)
        {
            var h = new GameObject("heli").transform;
            h.SetParent(parent, false);
            Part(h, PrimitiveType.Sphere, body, new Vector3(0, 0.35f, 0), new Vector3(0.7f, 0.5f, 1.0f));
            Part(h, PrimitiveType.Cube, body, new Vector3(0, 0.45f, -0.8f), new Vector3(0.12f, 0.12f, 0.9f));
            Part(h, PrimitiveType.Cube, new Color(0.9f, 0.15f, 0.2f), new Vector3(0, 0.45f, 0.1f), new Vector3(0.72f, 0.12f, 0.12f), default, true);
            Part(h, PrimitiveType.Sphere, new Color(0.5f, 0.75f, 0.9f), new Vector3(0, 0.42f, 0.38f), new Vector3(0.45f, 0.3f, 0.3f));
            Part(h, PrimitiveType.Cube, new Color(0.3f, 0.3f, 0.3f), new Vector3(-0.3f, 0.05f, 0), new Vector3(0.05f, 0.05f, 0.9f));
            Part(h, PrimitiveType.Cube, new Color(0.3f, 0.3f, 0.3f), new Vector3(0.3f, 0.05f, 0), new Vector3(0.05f, 0.05f, 0.9f));
            rotor = new GameObject("rotor").transform;
            rotor.SetParent(h, false);
            rotor.localPosition = new Vector3(0, 0.66f, 0);
            Part(rotor, PrimitiveType.Cube, new Color(0.2f, 0.2f, 0.2f), Vector3.zero, new Vector3(1.8f, 0.02f, 0.1f));
            Part(rotor, PrimitiveType.Cube, new Color(0.2f, 0.2f, 0.2f), Vector3.zero, new Vector3(0.1f, 0.02f, 1.8f));
            return h;
        }

        void PlayOutcome(Incident inc, Vector2 spot, bool solved, float t)
        {
            var map = Sim.map;
            var node = map.nodes[inc.node].pos;
            var ex = FacilityPos(Sim.extraction);
            var rnd = new System.Random(inc.id * 31 + (solved ? 1 : 2));
            var o = NewOutcome((solved ? "solved_" : "failed_") + inc.kind, 2f, t);
            var root = o.root;
            Vector3 At(Vector2 p, float h = 0f) => W(p, h);

            if (solved)
                switch (inc.kind)
                {
                    case "outbreak":
                    {
                        var rings = Enumerable.Range(0, 3).Select(_ => FxRing(root, node, Color.white, 1f, 0.08f)).ToArray();
                        var souls = Enumerable.Range(0, 10).Select(_ =>
                        {
                            var s = Part(root, PrimitiveType.Sphere, Color.white, Vector3.zero, Vector3.one * 0.15f, default, true).transform;
                            s.position = At(node + new Vector2(F(rnd, -3.5f, 3.5f), F(rnd, -3.5f, 3.5f)), 0.3f);
                            return s;
                        }).ToArray();
                        var starts = souls.Select(s => s.position).ToArray();
                        o.tick = (k, now) =>
                        {
                            for (int i = 0; i < rings.Length; i++) Wave(rings[i], new Color(0.6f, 1f, 0.7f, 0.9f), Mathf.Clamp01(k * 1.6f - i * 0.25f), 1f, 18f);
                            for (int i = 0; i < souls.Length; i++)
                            {
                                souls[i].position = starts[i] + Vector3.up * k * 3f + new Vector3(Mathf.Sin(now * 3f + i) * 0.2f, 0, 0);
                                souls[i].localScale = Vector3.one * 0.15f * (1f - k);
                            }
                        };
                        break;
                    }
                    case "bridge":
                    {
                        Vector2 mid = node, a = node, b = node;
                        if (inc.edge >= 0 && inc.edge < map.edges.Count) { var e = map.edges[inc.edge]; a = map.nodes[e.a].pos; b = map.nodes[e.b].pos; mid = (a + b) / 2f; }
                        var dir = (b - a).normalized;
                        var planks = new List<(Transform tr, Vector3 from, Vector3 to, float delay)>();
                        for (int i = 0; i < 6; i++)
                        {
                            var tr = Part(root, PrimitiveType.Cube, new Color(0.6f, 0.42f, 0.25f), Vector3.zero, new Vector3(0.9f, 0.06f, 0.22f)).transform;
                            float side = i % 2 == 0 ? -1f : 1f;
                            var land = mid + dir * ((i - 2.5f) * 0.28f);
                            planks.Add((tr, At(mid + dir * side * 4f, 0.2f), At(land, 0.35f), i * 0.09f));
                            tr.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y)) * Quaternion.Euler(0, 90f, 0);
                        }
                        var boost = FxRing(root, mid, new Color(0.5f, 0.85f, 1f, 0.9f), 1f, 0.1f);
                        o.dur = 2.2f;
                        o.tick = (k, now) =>
                        {
                            foreach (var (tr, from, to, delay) in planks)
                            {
                                float q = Mathf.Clamp01((k - delay) / 0.4f);
                                tr.position = Arc(from, to, 1.5f, q);
                                tr.gameObject.SetActive(k >= delay);
                            }
                            Wave(boost, new Color(0.5f, 0.85f, 1f, 0.9f), Mathf.Clamp01((k - 0.6f) / 0.4f), 1f, 14f);
                        };
                        break;
                    }
                    case "airdrop":
                    {
                        var crate = Part(root, PrimitiveType.Cube, new Color(0.45f, 0.35f, 0.2f), Vector3.zero, Vector3.one * 0.5f).transform;
                        crate.position = At(spot, 0.25f);
                        var lid = Part(root, PrimitiveType.Cube, new Color(0.3f, 0.5f, 0.3f), Vector3.zero, new Vector3(0.54f, 0.08f, 0.54f)).transform;
                        var items = new List<(Transform tr, Vector3 to)>();
                        for (int i = 0; i < 6; i++)
                        {
                            var it = Part(root, PrimitiveType.Cube, i % 2 == 0 ? Color.white : new Color(1f, 0.85f, 0.3f), Vector3.zero, Vector3.one * 0.18f).transform;
                            Part(it, PrimitiveType.Cube, new Color(0.9f, 0.15f, 0.2f), new Vector3(0, 0, -0.51f), new Vector3(0.6f, 0.18f, 0.02f), default, true);
                            float ang = i * Mathf.PI / 3f;
                            items.Add((it, At(spot + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 2.2f, 0.1f)));
                        }
                        var hearts = Enumerable.Range(0, 6).Select(i => Part(root, PrimitiveType.Sphere, new Color(1f, 0.5f, 0.65f), Vector3.zero, Vector3.one * 0.14f, default, true).transform).ToArray();
                        o.tick = (k, now) =>
                        {
                            lid.position = At(spot, 0.5f) + Vector3.up * Mathf.Clamp01(k * 3f) * 1.4f;
                            lid.rotation = Quaternion.Euler(k * 300f, k * 120f, 0);
                            foreach (var (tr, to) in items) { tr.position = Arc(At(spot, 0.5f), to, 1.6f, Mathf.Clamp01(k * 1.5f)); tr.rotation = Quaternion.Euler(k * 400f, k * 200f, 0); }
                            for (int i = 0; i < hearts.Length; i++)
                            {
                                hearts[i].position = At(spot + new Vector2(Mathf.Cos(i) * 0.6f, Mathf.Sin(i) * 0.6f), 0.6f + k * 2.5f + i * 0.1f);
                                hearts[i].localScale = Vector3.one * 0.18f * Mathf.Sin(k * Mathf.PI);
                            }
                            crate.localScale = Vector3.one * 0.5f * (k > 0.7f ? (1f - k) / 0.3f : 1f);
                        };
                        break;
                    }
                    case "trapped":
                    {
                        Color[] shirts = { new Color(0.85f, 0.75f, 0.6f), new Color(0.6f, 0.75f, 0.9f), new Color(0.9f, 0.6f, 0.6f), new Color(0.7f, 0.85f, 0.6f), new Color(0.9f, 0.85f, 0.5f), new Color(0.75f, 0.65f, 0.9f) };
                        var to = node + Vector2.ClampMagnitude(ex - node, 7f);
                        var people = Enumerable.Range(0, 6).Select(i => (MiniCapy(root, shirts[i]).transform, At(spot + new Vector2(Mathf.Cos(i * 1.1f), Mathf.Sin(i * 1.1f)) * 0.5f),
                                                                         At(to + new Vector2(Mathf.Cos(i * 1.7f), Mathf.Sin(i * 1.7f)) * 0.6f), i * 0.05f)).ToArray();
                        o.dur = 4f;
                        o.tick = (k, now) => { foreach (var (tr, a, b, d) in people) RunCivilian(tr, a, b, Mathf.Clamp01((k - d) / (1f - d)), d * 20f); };
                        break;
                    }
                    case "guard":
                    {
                        var tower = At(ex, 1.6f);
                        var shots = Enumerable.Range(0, 10).Select(i =>
                        {
                            float ang = F(rnd, 0, Mathf.PI * 2f);
                            var hit = At(ex + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * F(rnd, 2.5f, 6f), 0.2f);
                            var lr = Bolt(root, new Color(1f, 0.9f, 0.5f), 0.05f);
                            lr.positionCount = 2;
                            return (lr, hit, i * 0.08f);
                        }).ToArray();
                        o.dur = 1.6f;
                        o.tick = (k, now) =>
                        {
                            foreach (var (lr, hit, d) in shots)
                            {
                                bool on = k >= d && k < d + 0.06f;
                                lr.enabled = on;
                                if (on) { lr.SetPosition(0, tower); lr.SetPosition(1, hit); }
                                if (k >= d + 0.06f && lr.positionCount == 2 && lr.gameObject.name == "bolt")
                                {
                                    lr.gameObject.name = "done";
                                    Burst(hit, new Color(0.7f, 0.65f, 0.6f, 0.8f), new Color(0.4f, 0.4f, 0.4f, 0.6f), 10, 1f, 0.3f, 0.8f, -0.1f, 0.1f);
                                }
                            }
                        };
                        break;
                    }
                    case "medevac":
                    {
                        var heli = MiniHeli(root, Color.white, out var rotor);
                        var pillars = Sim.players.Select(p =>
                        {
                            var c = Part(root, PrimitiveType.Cylinder, new Color(0.5f, 1f, 0.6f), Vector3.zero, new Vector3(0.6f, 3f, 0.6f), default, true).transform;
                            c.position = At(Shown(p), 3f);
                            return c;
                        }).ToArray();
                        o.dur = 3f;
                        o.tick = (k, now) =>
                        {
                            float h = k < 0.5f ? Mathf.Lerp(7f, 0f, k / 0.5f) : (k > 0.8f ? (k - 0.8f) * 30f : 0f);
                            heli.position = At(spot, h);
                            rotor.localRotation = Quaternion.Euler(0, now * 1500f, 0);
                            float w = Mathf.Sin(Mathf.Clamp01((k - 0.4f) / 0.6f) * Mathf.PI) * 0.6f;
                            foreach (var c in pillars) c.localScale = new Vector3(w, 3f, w);
                        };
                        break;
                    }
                    case "napalm":
                    {
                        var dir = new Vector2(1f, -0.4f).normalized;
                        var jet = new GameObject("jet").transform; jet.SetParent(root, false);
                        Part(jet, PrimitiveType.Cube, new Color(0.45f, 0.5f, 0.5f), Vector3.zero, new Vector3(0.3f, 0.2f, 1.4f));
                        Part(jet, PrimitiveType.Cube, new Color(0.4f, 0.45f, 0.45f), new Vector3(0, 0, -0.1f), new Vector3(1.6f, 0.05f, 0.5f));
                        Part(jet, PrimitiveType.Cube, new Color(0.4f, 0.45f, 0.45f), new Vector3(0, 0.25f, -0.6f), new Vector3(0.05f, 0.4f, 0.3f));
                        jet.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
                        bool[] boomed = new bool[3];
                        o.tick = (k, now) =>
                        {
                            jet.position = At(node + dir * Mathf.Lerp(-14f, 14f, k), 5f);
                            for (int i = 0; i < 3; i++)
                                if (!boomed[i] && k > 0.42f + i * 0.08f) { boomed[i] = true; Explosion(node + dir * ((i - 1) * 2.2f), 1.3f); }
                        };
                        break;
                    }
                    case "decoy":
                    {
                        var spk = Part(root, PrimitiveType.Cylinder, new Color(0.5f, 0.4f, 0.8f), Vector3.zero, new Vector3(0.5f, 0.3f, 0.5f)).transform;
                        var rings = Enumerable.Range(0, 4).Select(_ => FxRing(root, spot, new Color(0.8f, 0.55f, 1f), 1f, 0.1f)).ToArray();
                        o.dur = 2.5f;
                        o.tick = (k, now) =>
                        {
                            spk.position = At(spot, 0.2f + Mathf.Abs(Mathf.Sin(now * 14f)) * 0.15f);
                            spk.localScale = new Vector3(0.5f, 0.3f, 0.5f) * (1f + Mathf.Abs(Mathf.Sin(now * 14f)) * 0.3f) * (k > 0.8f ? (1f - k) / 0.2f : 1f);
                            for (int i = 0; i < rings.Length; i++) Wave(rings[i], new Color(0.8f, 0.55f, 1f, 0.85f), Mathf.Repeat(k * 1.6f - i * 0.2f, 1f), 1f, 50f);
                        };
                        break;
                    }
                    case "chest":
                    {
                        var box = Part(root, PrimitiveType.Cube, new Color(0.55f, 0.32f, 0.15f), Vector3.zero, new Vector3(0.6f, 0.35f, 0.4f)).transform;
                        box.position = At(spot, 0.18f);
                        var lidPivot = new GameObject("lid").transform; lidPivot.SetParent(root, false);
                        lidPivot.position = At(spot + new Vector2(0, 0.2f), 0.36f);
                        Part(lidPivot, PrimitiveType.Cube, new Color(1f, 0.8f, 0.2f), new Vector3(0, 0.05f, -0.2f), new Vector3(0.62f, 0.1f, 0.42f));
                        var coins = Enumerable.Range(0, 14).Select(i =>
                        {
                            var c = Part(root, PrimitiveType.Cylinder, new Color(1f, 0.85f, 0.2f), Vector3.zero, new Vector3(0.16f, 0.02f, 0.16f), default, true).transform;
                            float ang = F(rnd, 0, Mathf.PI * 2f);
                            return (c, At(spot + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * F(rnd, 0.6f, 1.8f), 0.05f), F(rnd, 0.8f, 2f));
                        }).ToArray();
                        var glow = FxGlow(root, spot, new Color(1f, 0.85f, 0.3f, 0.6f), 3f);
                        o.tick = (k, now) =>
                        {
                            lidPivot.localRotation = Quaternion.Euler(-Mathf.Clamp01(k * 4f) * 110f, 0, 0);
                            foreach (var (c, to, h) in coins) { c.position = Arc(At(spot, 0.4f), to, h, Mathf.Clamp01(k * 1.4f)); c.rotation = Quaternion.Euler(now * 600f, 0, now * 300f); }
                            glow.color = new Color(1f, 0.85f, 0.3f, 0.6f * (1f - k));
                            float s = k > 0.8f ? (1f - k) / 0.2f : 1f;
                            box.localScale = new Vector3(0.6f, 0.35f, 0.4f) * s; lidPivot.localScale = Vector3.one * s;
                        };
                        break;
                    }
                    case "boss":
                    {
                        var body = new GameObject("boss").transform; body.SetParent(root, false);
                        body.position = At(spot, 0f);
                        ZombieKing(body, 2.4f, false);
                        var crown = new GameObject("crown").transform; crown.SetParent(root, false);
                        for (int i = 0; i < 5; i++)
                            Part(crown, PrimitiveType.Cube, new Color(1f, 0.82f, 0.2f), new Vector3(Mathf.Cos(i * 1.256f) * 0.25f, 0, Mathf.Sin(i * 1.256f) * 0.25f), new Vector3(0.1f, 0.25f, 0.1f), default, true);
                        o.dur = 2.4f;
                        o.tick = (k, now) =>
                        {
                            float fall = Mathf.Clamp01(k * 2f);
                            body.rotation = Quaternion.Euler(fall * fall * 90f, 0, 0);
                            body.position = At(spot, -Mathf.Clamp01((k - 0.5f) * 2f) * 1.5f);
                            if (k > 0.25f && k < 0.27f) Burst(At(spot, 0.2f), new Color(0.55f, 0.48f, 0.38f, 0.8f), new Color(0.4f, 0.35f, 0.3f, 0.6f), 40, 3f, 0.6f, 1.2f, 0.2f, 0.8f, true);
                            crown.position = Arc(At(spot, 2.4f), At(spot + new Vector2(1.2f, 0.4f), 0.1f), 2f, Mathf.Clamp01(k * 1.3f));
                            crown.rotation = Quaternion.Euler(now * 200f, now * 360f, 0);
                        };
                        break;
                    }
                    case "herbs":
                    {
                        var leaves = Enumerable.Range(0, 16).Select(_ => Part(root, PrimitiveType.Sphere, new Color(0.4f, 0.85f, 0.35f), Vector3.zero, new Vector3(0.22f, 0.04f, 0.12f), default, true).transform).ToArray();
                        o.tick = (k, now) =>
                        {
                            for (int i = 0; i < leaves.Length; i++)
                            {
                                float a = i * 0.39f + k * 9f, r = 0.4f + k * 1.6f;
                                leaves[i].position = At(spot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, 0.2f + k * 2.5f + i * 0.04f);
                                leaves[i].rotation = Quaternion.Euler(now * 200f + i * 20f, a * Mathf.Rad2Deg, 0);
                                leaves[i].localScale = new Vector3(0.22f, 0.04f, 0.12f) * (1f - k * k);
                            }
                        };
                        break;
                    }
                    case "gas":
                    {
                        var barrel = Part(root, PrimitiveType.Cylinder, new Color(0.85f, 0.2f, 0.15f), Vector3.zero, new Vector3(0.35f, 0.25f, 0.35f)).transform;
                        var trail = Particles(barrel, barrel.position, new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.6f, 0.2f, 0.8f), 40f, 0.6f, 0.2f, 0.15f, 0f, new Vector3(0.1f, 0.1f, 0.1f), 0f, 0f, 9);
                        o.tick = (k, now) =>
                        {
                            barrel.position = Arc(At(spot, 0.3f), At(ex, 0.4f), 5f, k);
                            barrel.rotation = Quaternion.Euler(now * 300f, 0, now * 150f);
                            trail.transform.position = barrel.position;
                        };
                        break;
                    }
                    case "rubble":
                    {
                        var rocks = Enumerable.Range(0, 12).Select(_ =>
                        {
                            var r = Part(root, PrimitiveType.Cube, new Color(0.5f, 0.48f, 0.45f), Vector3.zero, Vector3.one * F(rnd, 0.15f, 0.32f)).transform;
                            float ang = F(rnd, 0, Mathf.PI * 2f);
                            return (r, At(spot + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * F(rnd, 1.2f, 2.5f), 0.08f));
                        }).ToArray();
                        var gears = Enumerable.Range(0, 3).Select(i => Part(root, PrimitiveType.Cylinder, new Color(0.87f, 0.8f, 0.7f), Vector3.zero, new Vector3(0.3f, 0.05f, 0.3f), default, true).transform).ToArray();
                        var to = node + Vector2.ClampMagnitude(ex - node, 6f);
                        o.dur = 2.2f;
                        o.tick = (k, now) =>
                        {
                            foreach (var (r, end) in rocks) { r.position = Arc(At(spot, 0.3f), end, 1.2f, Mathf.Clamp01(k * 2.5f)); r.rotation = Quaternion.Euler(k * 500f, k * 300f, 0); }
                            for (int i = 0; i < gears.Length; i++)
                            {
                                float q = Mathf.Clamp01((k - 0.25f - i * 0.08f) / 0.6f);
                                gears[i].position = Arc(At(spot, 0.4f), At(to + new Vector2(i * 0.4f, 0), 0.3f), 2.5f, q);
                                gears[i].rotation = Quaternion.Euler(90f, now * 400f, 0);
                                gears[i].localScale = new Vector3(0.3f, 0.05f, 0.3f) * (q >= 1f ? 0f : 1f);
                            }
                        };
                        break;
                    }
                    case "bell":
                    {
                        var rings = Enumerable.Range(0, 4).Select(_ => FxRing(root, spot, new Color(1f, 0.9f, 0.5f), 1f, 1.5f)).ToArray();
                        var people = Enumerable.Range(0, 4).Select(i =>
                        {
                            var dir = new Vector2(Mathf.Cos(i * 1.57f + 0.4f), Mathf.Sin(i * 1.57f + 0.4f));
                            return (MiniCapy(root, new Color(0.9f, 0.8f, 0.55f)).transform, At(node + dir * 0.6f), At(node + dir * 4.5f));
                        }).ToArray();
                        o.dur = 3f;
                        o.tick = (k, now) =>
                        {
                            for (int i = 0; i < rings.Length; i++) Wave(rings[i], new Color(1f, 0.9f, 0.5f, 0.9f), Mathf.Repeat(k * 2f - i * 0.25f, 1f), 1f, 12f);
                            for (int i = 0; i < people.Length; i++) RunCivilian(people[i].Item1, people[i].Item2, people[i].Item3, k, i);
                        };
                        break;
                    }
                    case "generator":
                    {
                        var gen = map.nodes[inc.node];
                        var targets = map.nodes.Where(n => n.type == NodeType.Factory && Sim.Powered(n)).Select(n => FacilityPos(n.id)).ToList();
                        var bolts = targets.Select(_ => Bolt(root, new Color(0.6f, 0.95f, 1f), 0.08f)).ToArray();
                        var flashes = targets.Select(p => FxGlow(root, p, new Color(0.6f, 0.95f, 1f, 0.8f), 3.5f)).ToArray();
                        var from = At(FacilityPos(gen.id), 1.2f);
                        o.dur = 1.6f;
                        o.tick = (k, now) =>
                        {
                            for (int i = 0; i < bolts.Length; i++)
                            {
                                bolts[i].enabled = k < 0.75f && Mathf.Repeat(now * 12f + i, 1f) < 0.7f;
                                Zap(bolts[i], from, At(targets[i], 1f), 0.35f, rnd);
                                flashes[i].color = new Color(0.6f, 0.95f, 1f, 0.8f * Mathf.Sin(k * Mathf.PI));
                            }
                        };
                        break;
                    }
                    default:
                    {
                        var gear = new GameObject("gear").transform; gear.SetParent(root, false);
                        Part(gear, PrimitiveType.Cylinder, new Color(0.75f, 0.8f, 0.85f), Vector3.zero, new Vector3(0.7f, 0.06f, 0.7f), default, true);
                        for (int i = 0; i < 8; i++)
                            Part(gear, PrimitiveType.Cube, new Color(0.75f, 0.8f, 0.85f), new Vector3(Mathf.Cos(i * 0.785f) * 0.4f, 0, Mathf.Sin(i * 0.785f) * 0.4f), new Vector3(0.14f, 0.06f, 0.14f), new Vector3(0, -i * 45f, 0), true);
                        Burst(At(ex, 0.8f), new Color(0.5f, 0.85f, 1f, 1f), Color.white, 50, 3f, 0.1f, 0.9f, 0.5f, 0.4f);
                        o.tick = (k, now) =>
                        {
                            gear.position = At(ex, 1f + k * 2.5f);
                            gear.rotation = Quaternion.Euler(70f, now * 300f, 0);
                            gear.localScale = Vector3.one * Mathf.Sin(k * Mathf.PI) * 1.3f;
                        };
                        break;
                    }
                }
            else
                switch (inc.kind)
                {
                    case "outbreak":
                    {
                        var crack = FxGlow(root, node, new Color(0.05f, 0.08f, 0.03f, 0.9f), 1f);
                        var ring = FxRing(root, node, new Color(0.4f, 1f, 0.3f), 1f, 0.08f);
                        Particles(root, At(node, 0.1f), new Color(0.45f, 1f, 0.3f, 0.7f), new Color(0.2f, 0.6f, 0.15f, 0.5f), 40f, 1.5f, 2.5f, 0.6f, -0.1f, new Vector3(0.8f, 0.8f, 0.1f), 0.2f, 0.3f);
                        o.tick = (k, now) =>
                        {
                            crack.transform.localScale = Vector3.one * Mathf.Lerp(1f, 6f, Mathf.Clamp01(k * 2f));
                            crack.color = new Color(0.05f, 0.08f, 0.03f, 0.9f * (1f - k));
                            Wave(ring, new Color(0.4f, 1f, 0.3f, 0.9f), k, 1f, 10f);
                        };
                        break;
                    }
                    case "bridge":
                    {
                        Vector2 mid = node;
                        if (inc.edge >= 0 && inc.edge < map.edges.Count) { var e = map.edges[inc.edge]; mid = (map.nodes[e.a].pos + map.nodes[e.b].pos) / 2f; }
                        var chunks = Enumerable.Range(0, 7).Select(i =>
                        {
                            var c = Part(root, PrimitiveType.Cube, i % 2 == 0 ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.55f, 0.4f, 0.25f), Vector3.zero, new Vector3(F(rnd, 0.3f, 0.6f), 0.12f, F(rnd, 0.2f, 0.4f))).transform;
                            return (c, At(mid + new Vector2(F(rnd, -0.6f, 0.6f), F(rnd, -0.6f, 0.6f)), 0.4f), F(rnd, 0f, 0.3f));
                        }).ToArray();
                        bool splashed = false;
                        o.tick = (k, now) =>
                        {
                            foreach (var (c, from, d) in chunks)
                            {
                                float q = Mathf.Clamp01((k - d) / 0.6f);
                                c.position = from + Vector3.down * q * q * 2.5f;
                                c.rotation = Quaternion.Euler(q * 200f, d * 300f, q * 120f);
                                c.gameObject.SetActive(q < 1f);
                            }
                            if (!splashed && k > 0.45f)
                            {
                                splashed = true;
                                Burst(At(mid, 0.05f), new Color(0.6f, 0.8f, 1f, 1f), new Color(0.3f, 0.55f, 0.9f, 0.8f), 50, 3f, 0.15f, 0.9f, 1.2f, 0.4f);
                            }
                        };
                        break;
                    }
                    case "airdrop":
                    {
                        var crate = Part(root, PrimitiveType.Cube, new Color(0.45f, 0.35f, 0.2f), Vector3.zero, Vector3.one * 0.5f).transform;
                        var hands = Enumerable.Range(0, 4).Select(i => Part(root, PrimitiveType.Capsule, new Color(0.45f, 0.65f, 0.35f), Vector3.zero, new Vector3(0.1f, 0.3f, 0.1f)).transform).ToArray();
                        Particles(root, At(spot, 0.1f), new Color(1f, 0.3f, 0.25f, 0.6f), new Color(0.6f, 0.1f, 0.1f, 0.4f), 15f, 2f, 0.8f, 0.5f, -0.05f, new Vector3(0.3f, 0.3f, 0.05f), 0.15f);
                        o.tick = (k, now) =>
                        {
                            crate.position = At(spot, 0.25f - Mathf.Clamp01((k - 0.3f) / 0.6f) * 0.8f) + new Vector3(Mathf.Sin(now * 50f) * 0.05f, 0, 0);
                            for (int i = 0; i < hands.Length; i++)
                            {
                                float a = i * 1.57f + 0.78f;
                                hands[i].position = At(spot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.35f, -0.3f + Mathf.Sin(Mathf.Clamp01(k * 1.2f) * Mathf.PI) * 0.6f);
                                hands[i].rotation = Quaternion.Euler(Mathf.Sin(now * 20f + i) * 20f, 0, 0);
                            }
                        };
                        break;
                    }
                    case "trapped":
                    {
                        var people = Enumerable.Range(0, 5).Select(i =>
                        {
                            var cv = MiniCapy(root, new Color(0.85f, 0.75f, 0.6f));
                            cv.transform.position = At(spot + new Vector2(Mathf.Cos(i * 1.3f), Mathf.Sin(i * 1.3f)) * 0.45f);
                            cv.transform.localScale = Vector3.one * 1.2f;
                            return cv;
                        }).ToArray();
                        bool green = false;
                        o.tick = (k, now) =>
                        {
                            if (!green && k > 0.35f) { green = true; foreach (var cv in people) PaintAll(cv, new Color(0.45f, 0.7f, 0.35f)); }
                            for (int i = 0; i < people.Length; i++)
                            {
                                var tr = people[i].transform;
                                tr.rotation = Quaternion.Euler(Mathf.Clamp01((k - 0.5f) * 3f) * 85f, i * 70f, Mathf.Sin(now * 40f + i) * 8f * (1f - k));
                                tr.position = At(spot + new Vector2(Mathf.Cos(i * 1.3f), Mathf.Sin(i * 1.3f)) * 0.45f, -Mathf.Clamp01((k - 0.75f) * 4f) * 0.6f);
                            }
                        };
                        break;
                    }
                    case "guard":
                    {
                        var beams = Enumerable.Range(0, 2).Select(i => Part(root, PrimitiveType.Cube, new Color(1f, 0.15f, 0.1f), Vector3.zero, new Vector3(0.15f, 0.02f, 6f), default, true).transform).ToArray();
                        var ring = FxRing(root, ex, new Color(1f, 0.2f, 0.15f), 6f, 0.08f);
                        o.dur = 5f;
                        o.tick = (k, now) =>
                        {
                            for (int i = 0; i < beams.Length; i++)
                            {
                                beams[i].position = At(ex, 1.8f);
                                beams[i].rotation = Quaternion.Euler(0, now * 240f + i * 180f, 0);
                                beams[i].localScale = new Vector3(0.15f, 0.02f, 6f) * (k > 0.9f ? (1f - k) * 10f : 1f);
                            }
                            ring.color = new Color(1f, 0.2f, 0.15f, (0.4f + 0.4f * Mathf.Sin(now * 10f)) * (1f - k));
                        };
                        break;
                    }
                    case "medevac":
                    {
                        var heli = MiniHeli(root, Color.white, out var rotor);
                        o.dur = 2.5f;
                        o.tick = (k, now) =>
                        {
                            heli.position = At(spot + new Vector2(k * k * 14f, -k * k * 6f), k * 7f);
                            heli.rotation = Quaternion.Euler(k * 15f, 60f, 0);
                            rotor.localRotation = Quaternion.Euler(0, now * 1500f, 0);
                        };
                        break;
                    }
                    case "napalm":
                    case "decoy":
                    {
                        var flare = Part(root, PrimitiveType.Sphere, new Color(1f, 0.3f, 0.2f), Vector3.zero, Vector3.one * 0.25f, default, true).transform;
                        bool puffed = false;
                        o.dur = 1.8f;
                        o.tick = (k, now) =>
                        {
                            flare.position = Arc(At(spot, 0.3f), At(spot + new Vector2(0.6f, 0), 0.1f), 3f, k);
                            flare.localScale = Vector3.one * 0.25f * (k < 0.5f ? 1f + Mathf.Sin(now * 30f) * 0.2f : Mathf.Lerp(1f, 0.3f, (k - 0.5f) * 2f));
                            if (k > 0.5f) flare.GetComponent<Renderer>().sharedMaterial = UnlitMat(new Color(0.3f, 0.25f, 0.25f));
                            if (!puffed && k > 0.95f) { puffed = true; Burst(At(spot, 0.2f), new Color(0.5f, 0.5f, 0.5f, 0.7f), new Color(0.3f, 0.3f, 0.3f, 0.5f), 15, 0.8f, 0.4f, 1f, -0.1f, 0.2f); }
                        };
                        break;
                    }
                    case "chest":
                    {
                        var box = Part(root, PrimitiveType.Cube, new Color(0.55f, 0.32f, 0.15f), Vector3.zero, new Vector3(0.6f, 0.45f, 0.4f)).transform;
                        Particles(root, At(spot, 0.1f), new Color(0.15f, 0.15f, 0.15f, 0.6f), new Color(0.3f, 0.3f, 0.3f, 0.4f), 12f, 1.5f, 0.6f, 0.4f, -0.05f, new Vector3(0.4f, 0.4f, 0.05f), 0.1f);
                        o.dur = 1.6f;
                        o.tick = (k, now) =>
                        {
                            box.position = At(spot, 0.22f - k * 0.7f) + new Vector3(Mathf.Sin(now * 45f) * 0.06f, 0, 0);
                            box.rotation = Quaternion.Euler(0, Mathf.Sin(now * 30f) * 10f, Mathf.Sin(now * 37f) * 8f);
                        };
                        break;
                    }
                    case "herbs":
                    {
                        var bushes = Enumerable.Range(0, 4).Select(i =>
                        {
                            var b = Part(root, PrimitiveType.Sphere, new Color(0.25f, 0.5f, 0.2f), Vector3.zero, new Vector3(0.6f, 0.45f, 0.6f)).transform;
                            b.position = At(spot + new Vector2(Mathf.Cos(i * 1.57f), Mathf.Sin(i * 1.57f)) * 0.5f, 0.2f);
                            return b;
                        }).ToArray();
                        bool popped = false;
                        o.dur = 1.6f;
                        o.tick = (k, now) =>
                        {
                            foreach (var b in bushes) b.rotation = Quaternion.Euler(Mathf.Sin(now * 35f) * 20f, 0, Mathf.Cos(now * 31f) * 20f);
                            if (!popped && k > 0.6f) { popped = true; Burst(At(spot, 0.3f), new Color(0.3f, 0.7f, 0.25f, 1f), new Color(0.2f, 0.4f, 0.15f, 0.8f), 40, 3f, 0.15f, 0.9f, 0.6f, 0.4f); }
                        };
                        break;
                    }
                    case "gas":
                    {
                        var drum = Part(root, PrimitiveType.Cylinder, new Color(0.45f, 0.42f, 0.4f), Vector3.zero, new Vector3(0.3f, 0.22f, 0.3f)).transform;
                        Burst(At(spot, 0.3f), new Color(0.5f, 0.5f, 0.5f, 0.7f), new Color(0.3f, 0.3f, 0.3f, 0.5f), 20, 1f, 0.4f, 1.2f, -0.1f, 0.3f);
                        o.dur = 1.8f;
                        o.tick = (k, now) =>
                        {
                            drum.position = At(spot + new Vector2(k * 1.2f, 0), 0.15f);
                            drum.rotation = Quaternion.Euler(0, 0, 90f) * Quaternion.Euler(0, -k * 600f, 0);
                            drum.localScale = new Vector3(0.3f, 0.22f, 0.3f) * (k > 0.8f ? (1f - k) * 5f : 1f);
                        };
                        break;
                    }
                    case "rubble":
                    {
                        var rocks = Enumerable.Range(0, 10).Select(_ =>
                        {
                            var r = Part(root, PrimitiveType.Cube, new Color(0.45f, 0.43f, 0.4f), Vector3.zero, Vector3.one * F(rnd, 0.2f, 0.4f)).transform;
                            return (r, At(spot + new Vector2(F(rnd, -0.5f, 0.5f), F(rnd, -0.5f, 0.5f)), F(rnd, 0.1f, 0.5f)), F(rnd, 0f, 0.4f));
                        }).ToArray();
                        bool dusted = false;
                        o.dur = 1.8f;
                        o.tick = (k, now) =>
                        {
                            foreach (var (r, land, d) in rocks)
                            {
                                float q = Mathf.Clamp01((k - d) / 0.35f);
                                r.position = land + Vector3.up * (1f - q * q) * 4f;
                                r.rotation = Quaternion.Euler(q * 180f, d * 400f, 0);
                            }
                            if (!dusted && k > 0.4f) { dusted = true; Burst(At(spot, 0.2f), new Color(0.6f, 0.55f, 0.5f, 0.8f), new Color(0.45f, 0.42f, 0.4f, 0.6f), 35, 2f, 0.6f, 1.4f, 0.1f, 0.6f, true); }
                        };
                        break;
                    }
                    case "bell":
                    {
                        var bell = Part(root, PrimitiveType.Cylinder, new Color(0.85f, 0.65f, 0.2f), Vector3.zero, new Vector3(0.5f, 0.3f, 0.5f)).transform;
                        var ring = FxRing(root, spot, new Color(0.3f, 0.25f, 0.2f), 1f, 0.06f);
                        o.dur = 1.8f;
                        o.tick = (k, now) =>
                        {
                            float q = Mathf.Clamp01(k * 2.2f);
                            bell.position = At(spot, 3f * (1f - q * q) + 0.3f);
                            bell.rotation = Quaternion.Euler(q * 70f, 0, q * 30f);
                            Wave(ring, new Color(0.3f, 0.25f, 0.2f, 0.9f), Mathf.Clamp01((k - 0.45f) / 0.55f), 1f, 8f);
                        };
                        break;
                    }
                    case "boss":
                    {
                        var body = new GameObject("boss").transform; body.SetParent(root, false);
                        ZombieKing(body, 2.4f);
                        var rings = Enumerable.Range(0, 3).Select(_ => FxRing(root, spot, new Color(1f, 0.2f, 0.15f), 1f, 0.08f)).ToArray();
                        Shake(0.35f);
                        o.dur = 2.2f;
                        o.tick = (k, now) =>
                        {
                            float grow = k < 0.6f ? 1f + k * 0.8f : Mathf.Lerp(1.48f, 0f, (k - 0.6f) / 0.4f);
                            body.position = At(spot, 0f);
                            body.localScale = Vector3.one * grow;
                            body.rotation = Quaternion.Euler(0, Mathf.Sin(now * 25f) * 8f, 0);
                            for (int i = 0; i < rings.Length; i++) Wave(rings[i], new Color(1f, 0.2f, 0.15f, 0.9f), Mathf.Repeat(k * 2f - i * 0.3f, 1f), 1f, 12f);
                        };
                        break;
                    }
                    default:
                    {
                        var oil = FxGlow(root, ex, new Color(0.05f, 0.05f, 0.08f, 0.85f), 0.5f);
                        Particles(root, At(ex, 0.6f), new Color(0.12f, 0.12f, 0.12f, 0.7f), new Color(0.05f, 0.05f, 0.05f, 0.6f), 25f, 3f, 1.2f, 0.8f, -0.05f, new Vector3(0.4f, 0.4f, 0.05f), 0.2f, 0.2f);
                        o.dur = 3.5f;
                        o.tick = (k, now) =>
                        {
                            oil.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 3.5f, Mathf.Clamp01(k * 1.5f));
                            oil.color = new Color(0.05f, 0.05f, 0.08f, 0.85f * (k > 0.8f ? (1f - k) * 5f : 1f));
                        };
                        break;
                    }
                }
        }
    }
}
