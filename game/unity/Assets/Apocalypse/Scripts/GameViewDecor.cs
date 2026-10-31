using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        Transform decorStatic, decorAnim;
        readonly List<(Transform t, float speed)> spinners = new List<(Transform, float)>();
        readonly List<(Renderer r, Material on, Material off, float phase)> neons = new List<(Renderer, Material, Material, float)>();
        readonly List<(SpriteRenderer sr, Color c, float phase)> glows = new List<(SpriteRenderer, Color, float)>();
        static readonly Dictionary<int, Material> litCache = new Dictionary<int, Material>();
        static readonly Dictionary<int, Material> unlitCache = new Dictionary<int, Material>();
        Material particleMat;

        static int ColorKey(Color c) =>
            (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 63) << 12) | (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 63) << 6) | Mathf.RoundToInt(Mathf.Clamp01(c.b) * 63);

        static Material LitMat(Color c)
        {
            int k = ColorKey(c);
            if (litCache.TryGetValue(k, out var m) && m != null) return m;
            if (litBase == null) litBase = Resources.Load<Material>("ApocalypseLit") ?? new Material(Shader.Find("Standard"));
            m = new Material(litBase) { color = c, enableInstancing = true };
            litCache[k] = m;
            return m;
        }

        Material UnlitMat(Color c)
        {
            int k = ColorKey(c);
            if (unlitCache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(lineMat) { color = c, enableInstancing = true };
            unlitCache[k] = m;
            return m;
        }

        GameObject Part(Transform parent, PrimitiveType type, Color c, Vector3 localPos, Vector3 scale, Vector3 euler = default, bool unlit = false)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = unlit ? UnlitMat(c) : LitMat(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        Transform Group(Transform parent, Vector2 pos, float yaw, float h = 0f)
        {
            var g = new GameObject("g").transform;
            g.SetParent(parent, false);
            g.position = W(pos, h);
            g.rotation = Quaternion.Euler(0, yaw, 0);
            return g;
        }

        static float F(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static T Pick<T>(System.Random r, T[] arr) => arr[r.Next(arr.Length)];
        static Color Vary(Color c, System.Random rnd, float amount = 0.1f)
        {
            float k = 1f + F(rnd, -amount, amount);
            return new Color(c.r * k, c.g * k, c.b * k);
        }

        ParticleSystem Particles(Transform parent, Vector3 pos, Color c0, Color c1, float rate, float life, float speed, float size,
                                 float gravity, Vector3 box, float drift = 0f, float noise = 0f, int order = 8)
        {
            if (particleMat == null) particleMat = new Material(lineMat) { mainTexture = soft.texture };
            var go = new GameObject("fx");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = box;
            sh.rotation = new Vector3(-90f, 0, 0);
            if (drift > 0)
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-drift, drift);
                vel.y = new ParticleSystem.MinMaxCurve(-drift * 0.2f, drift * 0.2f);
                vel.z = new ParticleSystem.MinMaxCurve(-drift, drift);
            }
            if (noise > 0)
            {
                var nz = ps.noise;
                nz.enabled = true;
                nz.strength = noise;
                nz.frequency = 0.4f;
            }
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = particleMat;
            pr.sortingOrder = order;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        SpriteRenderer Glow(Vector2 pos, Color c, float size)
        {
            var sr = Flat(decorAnim, soft, c, size, 3, 0.03f);
            sr.transform.position = W(pos, 0.03f);
            glows.Add((sr, c, pos.x * 1.7f + pos.y));
            return sr;
        }

        void BuildDistricts()
        {
            var map = Sim.map;
            spinners.Clear(); neons.Clear(); glows.Clear();
            decorStatic = new GameObject("Districts").transform;
            decorStatic.SetParent(mapRoot, false);
            decorAnim = new GameObject("DistrictsAnim").transform;
            decorAnim.SetParent(mapRoot, false);
            var occupied = facilityPos.Values.ToList();
            if (gm.cfg.escape) occupied.Add(ShelterPos(map.nodes[Sim.extraction]));

            bool Free(Vector2 p, float road, float fac) => RoadClearance(p) >= road && occupied.All(o => Vector2.Distance(o, p) >= fac);

            for (int r = 0; r < map.rows - 1; r++)
                for (int c = 0; c < map.cols - 1; c++)
                {
                    if (c == map.riverCol) continue;
                    int theme = map.nodes[r * map.cols + c].district;

                    int wTheme = map.nodes[0].district, eTheme = map.nodes[map.cols - 1].district;
                    if (wTheme != eTheme)
                    {
                        int fromRiver = c < map.riverCol ? map.riverCol - 1 - c : c - map.riverCol - 1;
                        float mix = fromRiver == 0 ? 0.45f : fromRiver == 1 ? 0.2f : 0f;
                        if (new System.Random(gm.cfg.seed * 31 + r * 7 + c).NextDouble() < mix) theme = c < map.riverCol ? eTheme : wTheme;
                    }
                    Vector2 center = (map.nodes[r * map.cols + c].pos + map.nodes[r * map.cols + c + 1].pos +
                                      map.nodes[(r + 1) * map.cols + c].pos + map.nodes[(r + 1) * map.cols + c + 1].pos) / 4f;
                    var rnd = new System.Random(gm.cfg.seed * 1009 + r * 97 + c * 13 + theme);
                    bool wholeBlock = Free(center, 2.2f, 3.4f);
                    switch (theme)
                    {
                        case 0: JungleBlock(center, rnd, wholeBlock, Free); break;
                        case 1: CityBlock(center, rnd, Free); break;
                        case 2: RuinsBlock(center, rnd, Free); break;
                        default: TownBlock(center, rnd, wholeBlock, Free); break;
                    }
                }

            Vector2 size = map.Size;
            int west = map.nodes[0].district, east = map.nodes[map.cols - 1].district;
            SideFx(west, -3f, map.RiverX - 1.3f, size);
            SideFx(east, map.RiverX + 1.3f, size.x + 3f, size);

        }

        void SideFx(int theme, float x0, float x1, Vector2 size)
        {
            float cx = (x0 + x1) / 2f, w = x1 - x0, d = size.y + 6f;
            var at = new Vector3(cx, 0, size.y / 2f);
            switch (theme)
            {
                case 0:
                    Particles(decorAnim, at + Vector3.up * 0.4f, new Color(0.75f, 0.95f, 0.8f, 0.07f), new Color(0.6f, 0.85f, 0.7f, 0.05f),
                              w * d / 18f, 14f, 0.05f, 3.2f, 0f, new Vector3(w, d, 0.3f), 0.08f);
                    Particles(decorAnim, at + Vector3.up * 0.8f, new Color(0.85f, 1f, 0.4f, 0.9f), new Color(1f, 0.9f, 0.3f, 0.8f),
                              w * d / 60f, 4f, 0.1f, 0.12f, 0f, new Vector3(w, d, 0.8f), 0.15f, 0.4f, 9);
                    break;
                case 2:
                    Particles(decorAnim, at + Vector3.up * 3.5f, new Color(0.75f, 0.72f, 0.7f, 0.8f), new Color(0.45f, 0.42f, 0.4f, 0.7f),
                              w * d / 14f, 10f, 0.05f, 0.09f, 0.02f, new Vector3(w, d, 0.5f), 0.25f, 0.3f, 9);
                    break;
            }
        }

        void BuildGround(CityMap map, Vector2 size)
        {
            int west = map.nodes[0].district, east = map.nodes[map.cols - 1].district;
            float x0 = -3f, x1 = size.x + 3f, z0 = -3f, z1 = size.y + 3f, band = map.spacing * 1.6f;
            var lit = new Color(1.1f, 1.17f, 1.33f);
            const int steps = 60;
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            for (int i = 0; i <= steps; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)steps);
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(map.RiverX - band, map.RiverX + band, x));
                var c = Color.Lerp(ThemeGround[west], ThemeGround[east], k);
                c = new Color(c.r * lit.r, c.g * lit.g, c.b * lit.b, 1f);
                verts.Add(new Vector3(x, 0f, z0)); verts.Add(new Vector3(x, 0f, z1));
                cols.Add(c); cols.Add(c);
                if (i > 0) { int b = (i - 1) * 2; tris.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("ground");
            go.transform.SetParent(mapRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = UnlitMat(Color.white);
            mr.sortingOrder = -10;
            var slab = Solid(PrimitiveType.Cube, mapRoot, new Color(0.03f, 0.035f, 0.05f), new Vector3(x1 - x0, 0.2f, z1 - z0));
            slab.transform.position = new Vector3((x0 + x1) / 2f, -0.105f, (z0 + z1) / 2f);
        }

        static readonly Color[] JungleGreens =
        {
            new Color(0.10f, 0.32f, 0.16f), new Color(0.14f, 0.40f, 0.18f), new Color(0.08f, 0.26f, 0.15f),
            new Color(0.20f, 0.42f, 0.14f), new Color(0.12f, 0.35f, 0.24f),
        };
        static readonly Color Stone = new Color(0.42f, 0.42f, 0.36f), Moss = new Color(0.22f, 0.38f, 0.2f);

        void JungleBlock(Vector2 center, System.Random rnd, bool whole, System.Func<Vector2, float, float, bool> free)
        {
            double block = rnd.NextDouble();
            if (whole && block < 0.16) { Temple(center, rnd); return; }
            if (whole && block < 0.28) { Pond(center, rnd); }
            for (int i = 0; i < 9; i++)
            {
                var pos = center + new Vector2((i % 3 - 1) * 1.35f, (i / 3 - 1) * 1.35f) + new Vector2(F(rnd, -0.35f, 0.35f), F(rnd, -0.35f, 0.35f));
                if (!free(pos, 0.95f, 1.8f)) continue;
                if (whole && block >= 0.16 && block < 0.28 && Vector2.Distance(pos, center) < 1.6f) continue;
                double roll = rnd.NextDouble();
                if (roll < 0.34) JungleTree(pos, rnd);
                else if (roll < 0.48) Palm(pos, rnd);
                else if (roll < 0.68) Ferns(pos, rnd);
                else if (roll < 0.76) Pillars(pos, rnd);
                else if (roll < 0.80) StoneHead(pos, rnd);
            }
        }

        void JungleTree(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, F(rnd, 0, 360));
            float h = F(rnd, 0.5f, 1.3f);
            Part(g, PrimitiveType.Cylinder, new Color(0.28f, 0.2f, 0.14f), new Vector3(0, h / 2f, 0), new Vector3(0.12f, h / 2f, 0.12f));
            int blobs = 2 + rnd.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                float s = F(rnd, 0.6f, 1.2f);
                Part(g, PrimitiveType.Sphere, Pick(rnd, JungleGreens), new Vector3(F(rnd, -0.35f, 0.35f), h + F(rnd, 0, 0.35f), F(rnd, -0.35f, 0.35f)),
                     new Vector3(s, s * F(rnd, 0.55f, 0.75f), s));
            }
            if (rnd.NextDouble() < 0.3)
                Part(g, PrimitiveType.Cube, new Color(0.15f, 0.35f, 0.15f), new Vector3(0.3f, h * 0.6f, 0.1f), new Vector3(0.04f, h * 0.8f, 0.04f), new Vector3(0, 0, 8f));
        }

        void Palm(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, F(rnd, 0, 360));
            float h = F(rnd, 1.0f, 1.6f), lean = F(rnd, 5f, 15f);
            var trunk = Part(g, PrimitiveType.Cylinder, new Color(0.40f, 0.32f, 0.22f), new Vector3(0, h / 2f, 0), new Vector3(0.09f, h / 2f, 0.09f), new Vector3(lean, 0, 0));
            var top = new Vector3(0, h, Mathf.Sin(lean * Mathf.Deg2Rad) * h * 0.5f);
            int fronds = 6 + rnd.Next(3);
            var green = Pick(rnd, JungleGreens);
            for (int i = 0; i < fronds; i++)
            {
                float a = i * 360f / fronds + F(rnd, -10, 10);
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                Part(g, PrimitiveType.Cube, Vary(green, rnd), top + dir * 0.32f + Vector3.down * 0.06f, new Vector3(0.16f, 0.03f, 0.7f), new Vector3(22f, a, 0));
            }
        }

        void Ferns(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, 0);
            int n = 3 + rnd.Next(4);
            for (int i = 0; i < n; i++)
            {
                float s = F(rnd, 0.25f, 0.5f);
                var p = new Vector3(F(rnd, -0.5f, 0.5f), s * 0.25f, F(rnd, -0.5f, 0.5f));
                Part(g, PrimitiveType.Sphere, Vary(Pick(rnd, JungleGreens), rnd, 0.2f), p, new Vector3(s, s * 0.5f, s));
                if (rnd.NextDouble() < 0.25)
                    Part(g, PrimitiveType.Sphere, Pick(rnd, new[] { new Color(0.95f, 0.3f, 0.35f), new Color(1f, 0.8f, 0.2f), new Color(0.9f, 0.5f, 0.9f) }),
                         p + Vector3.up * s * 0.25f, Vector3.one * 0.1f);
            }
        }

        void Pillars(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, F(rnd, 0, 360));
            int n = 2 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3((i - (n - 1) / 2f) * 0.45f, 0, 0);
                if (rnd.NextDouble() < 0.3)
                {
                    Part(g, PrimitiveType.Cylinder, Vary(Stone, rnd), p + new Vector3(0, 0.1f, 0.35f), new Vector3(0.2f, 0.4f, 0.2f), new Vector3(90f, F(rnd, -30, 30), 0));
                    continue;
                }
                float h = F(rnd, 0.35f, 1.1f);
                Part(g, PrimitiveType.Cylinder, Vary(Stone, rnd), p + new Vector3(0, h / 2f, 0), new Vector3(0.2f, h / 2f, 0.2f));
                Part(g, PrimitiveType.Cube, Moss, p + new Vector3(0, h + 0.03f, 0), new Vector3(0.24f, 0.06f, 0.24f));
            }
            if (rnd.NextDouble() < 0.5)
                Part(g, PrimitiveType.Cube, Vary(Stone, rnd), new Vector3(0, 1.0f, 0), new Vector3(0.45f * n, 0.14f, 0.24f), new Vector3(0, 0, F(rnd, -8, 8)));
        }

        void StoneHead(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, F(rnd, -60, 30));
            Part(g, PrimitiveType.Cube, Vary(Stone, rnd), new Vector3(0, 0.35f, 0), new Vector3(0.55f, 0.7f, 0.5f));
            Part(g, PrimitiveType.Cube, Moss, new Vector3(0, 0.72f, 0), new Vector3(0.6f, 0.08f, 0.55f));
            Part(g, PrimitiveType.Cube, new Color(0.18f, 0.18f, 0.16f), new Vector3(-0.12f, 0.45f, -0.26f), new Vector3(0.1f, 0.06f, 0.03f));
            Part(g, PrimitiveType.Cube, new Color(0.18f, 0.18f, 0.16f), new Vector3(0.12f, 0.45f, -0.26f), new Vector3(0.1f, 0.06f, 0.03f));
            Part(g, PrimitiveType.Cube, new Color(0.18f, 0.18f, 0.16f), new Vector3(0, 0.22f, -0.26f), new Vector3(0.22f, 0.05f, 0.03f));
        }

        void Temple(Vector2 center, System.Random rnd)
        {
            var g = Group(decorStatic, center, Mathf.Round(F(rnd, 0, 4)) * 90f);
            float[] sizes = { 3.0f, 2.3f, 1.65f, 1.0f };
            float y = 0;
            for (int i = 0; i < sizes.Length; i++)
            {
                Part(g, PrimitiveType.Cube, Vary(Stone, rnd, 0.06f), new Vector3(0, y + 0.18f, 0), new Vector3(sizes[i], 0.36f, sizes[i]));
                Part(g, PrimitiveType.Cube, Moss, new Vector3(0, y + 0.37f, 0), new Vector3(sizes[i] * 0.98f, 0.03f, sizes[i] * 0.98f));
                y += 0.36f;
            }
            Part(g, PrimitiveType.Cube, Vary(Stone, rnd), new Vector3(0, y + 0.25f, 0), new Vector3(0.6f, 0.5f, 0.6f));
            Part(g, PrimitiveType.Cube, new Color(0.1f, 0.1f, 0.09f), new Vector3(0, y + 0.2f, -0.31f), new Vector3(0.25f, 0.35f, 0.02f));
            Part(g, PrimitiveType.Cube, new Color(0.36f, 0.36f, 0.31f), new Vector3(0, y / 2f, -1.25f), new Vector3(0.6f, 0.1f, 1.6f), new Vector3(-26f, 0, 0));
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2 == 0 ? -1.75f : 1.75f, i < 2 ? -1.75f : 1.75f);
                var p = center + corner;
                if (RoadClearance(p) > 0.9f) Ferns(p, rnd);
            }
        }

        void Pond(Vector2 center, System.Random rnd)
        {
            var water = Flat(decorStatic, circle, new Color(0.08f, 0.25f, 0.3f, 0.9f), 1f, 2, 0.02f);
            water.transform.position = W(center, 0.02f);
            water.transform.localScale = new Vector3(F(rnd, 2.2f, 2.8f), F(rnd, 1.6f, 2.2f), 1f);
            for (int i = 0; i < 6; i++)
            {
                var pad = Flat(decorStatic, circle, new Color(0.25f, 0.55f, 0.25f, 1f), F(rnd, 0.18f, 0.3f), 3, 0.025f);
                pad.transform.position = W(center + new Vector2(F(rnd, -0.8f, 0.8f), F(rnd, -0.6f, 0.6f)), 0.025f);
            }
            for (int i = 0; i < 5; i++)
            {
                float a = F(rnd, 0, Mathf.PI * 2);
                var p = center + new Vector2(Mathf.Cos(a) * 1.25f, Mathf.Sin(a) * 0.95f);
                Part(decorStatic, PrimitiveType.Cube, new Color(0.35f, 0.45f, 0.2f), W(p, 0.25f), new Vector3(0.03f, 0.5f, 0.03f), new Vector3(F(rnd, -10, 10), 0, F(rnd, -10, 10)));
            }
        }

        static readonly Color[] TowerBodies =
        {
            new Color(0.30f, 0.38f, 0.48f), new Color(0.48f, 0.50f, 0.53f), new Color(0.26f, 0.29f, 0.34f), new Color(0.56f, 0.54f, 0.50f),
        };
        static readonly Color GlassBand = new Color(0.55f, 0.66f, 0.78f), WarmLight = new Color(1f, 0.85f, 0.55f);

        void CityBlock(Vector2 center, System.Random rnd, System.Func<Vector2, float, float, bool> free)
        {
            for (int i = 0; i < 4; i++)
            {
                var pos = center + new Vector2(i % 2 == 0 ? -1.3f : 1.3f, i < 2 ? -1.3f : 1.3f) + new Vector2(F(rnd, -0.15f, 0.15f), F(rnd, -0.15f, 0.15f));
                if (free(pos, 1.25f, 2.0f)) { Office(pos, rnd); continue; }
                if (free(pos, 0.9f, 1.6f)) { if (rnd.NextDouble() < 0.6) Office(pos, rnd, 0.7f); else StreetTrees(pos, rnd); }
            }
        }

        void Office(Vector2 pos, System.Random rnd, float scale = 1f)
        {
            var g = Group(decorStatic, pos, 0);
            float w = F(rnd, 1.2f, 1.7f) * scale, d = F(rnd, 1.2f, 1.7f) * scale, h = F(rnd, 1.5f, 3.6f);
            var body = Pick(rnd, TowerBodies);
            int style = rnd.Next(3);
            Part(g, PrimitiveType.Cube, body, new Vector3(0, h / 2f, 0), new Vector3(w, h, d));
            if (style == 0)
            {
                for (float y = 0.35f; y < h - 0.1f; y += 0.3f)
                    Part(g, PrimitiveType.Cube, GlassBand, new Vector3(0, y, 0), new Vector3(w + 0.02f, 0.12f, d + 0.02f));
            }
            else if (style == 1)
            {
                int cols = Mathf.Max(2, Mathf.RoundToInt(w / 0.3f));
                for (int k = 0; k < cols; k++)
                {
                    float x = -w / 2f + w * (k + 0.5f) / cols;
                    Part(g, PrimitiveType.Cube, body * 0.75f, new Vector3(x, h / 2f + 0.1f, -d / 2f - 0.01f), new Vector3(0.06f, h - 0.4f, 0.02f));
                    Part(g, PrimitiveType.Cube, body * 0.75f, new Vector3(-w / 2f - 0.01f, h / 2f + 0.1f, x * d / w), new Vector3(0.02f, h - 0.4f, 0.06f));
                }
            }
            else
            {
                float h2 = F(rnd, 0.6f, 1.2f);
                Part(g, PrimitiveType.Cube, body, new Vector3(0, h + h2 / 2f, 0), new Vector3(w * 0.65f, h2, d * 0.65f));
                for (float y = 0.35f; y < h + h2 - 0.1f; y += 0.45f)
                {
                    float ww = y > h ? w * 0.65f : w, dd = y > h ? d * 0.65f : d;
                    Part(g, PrimitiveType.Cube, GlassBand, new Vector3(0, y, 0), new Vector3(ww + 0.02f, 0.08f, dd + 0.02f));
                }
                h += h2;
            }

            int lit = rnd.Next(4);
            for (int k = 0; k < lit; k++)
            {
                bool face = rnd.NextDouble() < 0.5;
                float y = F(rnd, 0.4f, h * 0.85f);
                Part(g, PrimitiveType.Cube, WarmLight, face ? new Vector3(F(rnd, -w / 3f, w / 3f), y, -d / 2f - 0.02f) : new Vector3(-w / 2f - 0.02f, y, F(rnd, -d / 3f, d / 3f)),
                     face ? new Vector3(0.18f, 0.09f, 0.02f) : new Vector3(0.02f, 0.09f, 0.18f), default, true);
            }

            Part(g, PrimitiveType.Cube, body * 0.8f, new Vector3(F(rnd, -w / 6f, w / 6f), h + 0.1f, F(rnd, -d / 6f, d / 6f)), new Vector3(w * 0.35f, 0.2f, d * 0.3f));
            if (rnd.NextDouble() < 0.3)
                Part(g, PrimitiveType.Cube, new Color(0.3f, 0.3f, 0.32f), new Vector3(w / 4f, h + 0.4f, d / 4f), new Vector3(0.03f, 0.8f, 0.03f));

            Part(g, PrimitiveType.Cube, new Color(0.2f, 0.22f, 0.26f), new Vector3(0, 0.15f, -d / 2f - 0.02f), new Vector3(w * 0.35f, 0.3f, 0.03f));
        }

        void StreetTrees(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, 0);
            for (int i = 0; i < 2; i++)
            {
                var p = new Vector3(i == 0 ? -0.3f : 0.3f, 0, F(rnd, -0.2f, 0.2f));
                Part(g, PrimitiveType.Cylinder, new Color(0.28f, 0.22f, 0.16f), p + new Vector3(0, 0.15f, 0), new Vector3(0.06f, 0.15f, 0.06f));
                Part(g, PrimitiveType.Sphere, new Color(0.2f, 0.36f, 0.24f), p + new Vector3(0, 0.42f, 0), Vector3.one * 0.38f);
            }
        }

        static readonly Color[] RuinWalls = { new Color(0.36f, 0.33f, 0.30f), new Color(0.30f, 0.28f, 0.27f), new Color(0.40f, 0.36f, 0.31f), new Color(0.27f, 0.27f, 0.28f) };

        void RuinsBlock(Vector2 center, System.Random rnd, System.Func<Vector2, float, float, bool> free)
        {
            for (int i = 0; i < 9; i++)
            {
                var pos = center + new Vector2((i % 3 - 1) * 1.4f, (i / 3 - 1) * 1.4f) + new Vector2(F(rnd, -0.3f, 0.3f), F(rnd, -0.3f, 0.3f));
                if (!free(pos, 0.9f, 1.7f)) continue;
                double roll = rnd.NextDouble();
                if (roll < 0.30) Shell(pos, rnd);
                else if (roll < 0.48) RubblePile(pos, rnd);
                else if (roll < 0.58) BurntCar(pos, rnd);
                else if (roll < 0.70) Fire(pos, rnd);
                else if (roll < 0.77) LeakingPipe(pos, rnd);
                else if (roll < 0.84) Crater(pos, rnd);
            }
        }

        void Shell(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, F(rnd, 0, 360));
            float w = F(rnd, 0.9f, 1.3f), d = F(rnd, 0.8f, 1.2f), h = F(rnd, 0.6f, 1.8f);
            var col = Pick(rnd, RuinWalls);
            Part(g, PrimitiveType.Cube, col * 0.8f, new Vector3(0, 0.03f, 0), new Vector3(w, 0.06f, d));

            int walls = 2 + rnd.Next(2);
            for (int i = 0; i < walls; i++)
            {
                bool x = i % 2 == 0;
                float side = i < 2 ? -1f : 1f;
                float len = x ? w : d;
                int pieces = 2 + rnd.Next(2);
                for (int k = 0; k < pieces; k++)
                {
                    float ph = h * F(rnd, 0.35f, 1f), seg = len / pieces, o = -len / 2f + seg * (k + 0.5f);
                    var lp = x ? new Vector3(o, ph / 2f, side * d / 2f) : new Vector3(side * w / 2f, ph / 2f, o);
                    var sc = x ? new Vector3(seg * 0.95f, ph, 0.1f) : new Vector3(0.1f, ph, seg * 0.95f);
                    Part(g, PrimitiveType.Cube, Vary(col, rnd), lp, sc);
                    if (rnd.NextDouble() < 0.3)
                        Part(g, PrimitiveType.Cube, new Color(0.06f, 0.06f, 0.06f), lp + new Vector3(0, ph * 0.15f, 0), x ? new Vector3(seg * 0.3f, 0.18f, 0.11f) : new Vector3(0.11f, 0.18f, seg * 0.3f));
                }
            }
            if (h > 1.1f)
                Part(g, PrimitiveType.Cube, col * 0.85f, new Vector3(0.1f, h * 0.5f, 0), new Vector3(w * 0.7f, 0.06f, d * 0.8f), new Vector3(F(rnd, 10, 25), 0, F(rnd, -10, 10)));
            int bars = rnd.Next(4);
            for (int i = 0; i < bars; i++)
                Part(g, PrimitiveType.Cube, new Color(0.35f, 0.22f, 0.15f), new Vector3(F(rnd, -w / 2f, w / 2f), h * 0.9f, -d / 2f), new Vector3(0.02f, F(rnd, 0.3f, 0.6f), 0.02f),
                     new Vector3(F(rnd, -25, 25), 0, F(rnd, -25, 25)));
        }

        void RubblePile(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, 0);
            int n = 6 + rnd.Next(6);
            for (int i = 0; i < n; i++)
            {
                float s = F(rnd, 0.12f, 0.35f);
                Part(g, PrimitiveType.Cube, Vary(Pick(rnd, RuinWalls), rnd, 0.2f), new Vector3(F(rnd, -0.55f, 0.55f), s * 0.35f, F(rnd, -0.55f, 0.55f)),
                     new Vector3(s, s * F(rnd, 0.5f, 1f), s * F(rnd, 0.6f, 1.2f)), new Vector3(F(rnd, 0, 40), F(rnd, 0, 360), F(rnd, 0, 40)));
            }
        }

        void BurntCar(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, F(rnd, 0, 360));
            var body = new Color(0.16f, 0.13f, 0.12f);
            Part(g, PrimitiveType.Cube, body, new Vector3(0, 0.16f, 0), new Vector3(0.45f, 0.2f, 0.9f), new Vector3(0, 0, F(rnd, -6, 6)));
            Part(g, PrimitiveType.Cube, body * 0.8f, new Vector3(0, 0.33f, -0.05f), new Vector3(0.38f, 0.16f, 0.45f));
            Part(g, PrimitiveType.Cube, new Color(0.45f, 0.2f, 0.1f), new Vector3(0, 0.27f, 0.3f), new Vector3(0.4f, 0.04f, 0.25f));
            var scorch = Flat(decorStatic, soft, new Color(0, 0, 0, 0.5f), 1.6f, 1, 0.015f);
            scorch.transform.position = W(pos, 0.015f);
        }

        void Fire(Vector2 pos, System.Random rnd)
        {
            RubblePile(pos, rnd);
            var at = W(pos, 0.15f);
            Particles(decorAnim, at, new Color(1f, 0.75f, 0.2f, 1f), new Color(1f, 0.3f, 0.05f, 1f), 26f, 0.9f, 0.9f, 0.32f, -0.1f, new Vector3(0.45f, 0.45f, 0.05f), 0f, 0f, 9);
            Particles(decorAnim, at + Vector3.up * 0.7f, new Color(0.25f, 0.23f, 0.22f, 0.55f), new Color(0.12f, 0.12f, 0.12f, 0.45f), 5f, 5f, 0.6f, 0.9f, -0.03f, new Vector3(0.3f, 0.3f, 0.05f), 0.15f, 0.2f, 8);
            Glow(pos, new Color(1f, 0.45f, 0.1f, 0.4f), 2.4f);
        }

        void LeakingPipe(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, F(rnd, 0, 360));
            var pipe = new Color(0.4f, 0.42f, 0.44f);
            Part(g, PrimitiveType.Cylinder, pipe, new Vector3(0, 0.55f, 0), new Vector3(0.12f, 0.5f, 0.12f), new Vector3(0, 0, 90f));
            Part(g, PrimitiveType.Cube, new Color(0.3f, 0.28f, 0.27f), new Vector3(-0.35f, 0.27f, 0), new Vector3(0.08f, 0.55f, 0.08f));
            Part(g, PrimitiveType.Cube, new Color(0.3f, 0.28f, 0.27f), new Vector3(0.2f, 0.27f, 0), new Vector3(0.08f, 0.55f, 0.08f));
            var end = g.TransformPoint(new Vector3(0.5f, 0.52f, 0));
            Particles(decorAnim, end, new Color(0.5f, 0.75f, 1f, 0.9f), new Color(0.35f, 0.6f, 0.95f, 0.8f), 14f, 0.5f, 0.05f, 0.07f, 1.2f, new Vector3(0.05f, 0.05f, 0.02f), 0f, 0f, 9);
            var puddle = Flat(decorStatic, circle, new Color(0.15f, 0.3f, 0.45f, 0.75f), 1f, 2, 0.02f);
            puddle.transform.position = new Vector3(end.x, 0.02f, end.z);
            puddle.transform.localScale = new Vector3(F(rnd, 0.7f, 1.1f), F(rnd, 0.5f, 0.8f), 1f);
        }

        void Crater(Vector2 pos, System.Random rnd)
        {
            var hole = Flat(decorStatic, soft, new Color(0, 0, 0, 0.7f), F(rnd, 1.4f, 2f), 1, 0.015f);
            hole.transform.position = W(pos, 0.015f);
            var rim = Flat(decorStatic, ringSprite, new Color(0.35f, 0.3f, 0.26f, 0.8f), F(rnd, 1.1f, 1.5f), 2, 0.018f);
            rim.transform.position = W(pos, 0.018f);
        }

        static readonly Color[] TownWalls =
        {
            new Color(0.78f, 0.72f, 0.60f), new Color(0.62f, 0.68f, 0.70f), new Color(0.74f, 0.62f, 0.52f), new Color(0.80f, 0.78f, 0.72f),
            new Color(0.58f, 0.64f, 0.52f), new Color(0.70f, 0.58f, 0.60f), new Color(0.55f, 0.45f, 0.36f), new Color(0.66f, 0.70f, 0.78f),
        };
        static readonly Color[] TownRoofs =
        {
            new Color(0.62f, 0.24f, 0.18f), new Color(0.30f, 0.33f, 0.40f), new Color(0.45f, 0.30f, 0.20f), new Color(0.22f, 0.40f, 0.42f),
            new Color(0.55f, 0.42f, 0.22f), new Color(0.40f, 0.20f, 0.25f), new Color(0.25f, 0.28f, 0.25f),
        };

        void TownBlock(Vector2 center, System.Random rnd, bool whole, System.Func<Vector2, float, float, bool> free)
        {
            if (whole && rnd.NextDouble() < 0.38) { Farmland(center, rnd); return; }
            for (int i = 0; i < 9; i++)
            {
                var pos = center + new Vector2((i % 3 - 1) * 1.35f, (i / 3 - 1) * 1.35f) + new Vector2(F(rnd, -0.25f, 0.25f), F(rnd, -0.25f, 0.25f));
                if (!free(pos, 0.95f, 1.7f)) continue;
                double roll = rnd.NextDouble();
                if (roll < 0.40) TownHouse(pos, rnd);
                else if (roll < 0.47) Barn(pos, rnd);
                else if (roll < 0.51) Windmill(pos, rnd);
                else if (roll < 0.63) Orchard(pos, rnd);
                else if (roll < 0.70) Hay(pos, rnd);
                else if (roll < 0.74) WaterTower(pos, rnd);
            }
        }

        void TownHouse(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, Mathf.Round(F(rnd, 0, 4)) * 90f + F(rnd, -8, 8));
            float w = F(rnd, 0.75f, 1.2f), d = F(rnd, 0.6f, 0.9f), h = F(rnd, 0.4f, 0.6f) * (rnd.NextDouble() < 0.3 ? 1.7f : 1f);
            var wall = Pick(rnd, TownWalls);
            var roof = Pick(rnd, TownRoofs);
            Part(g, PrimitiveType.Cube, Vary(wall, rnd, 0.05f), new Vector3(0, h / 2f, 0), new Vector3(w, h, d));
            int style = rnd.Next(3);
            if (style == 0)
                Part(g, PrimitiveType.Cube, roof, new Vector3(0, h, 0), new Vector3(w + 0.1f, d * 0.72f, d * 0.72f), new Vector3(45f, 0, 0));
            else if (style == 1)
                Part(g, PrimitiveType.Cube, roof, new Vector3(0, h + 0.05f, 0), new Vector3(Mathf.Min(w, d) * 0.8f, 0.28f, Mathf.Min(w, d) * 0.8f), new Vector3(0, 45f, 0));
            else
            {
                Part(g, PrimitiveType.Cube, roof, new Vector3(0, h + 0.02f, 0), new Vector3(w + 0.06f, 0.05f, d + 0.06f));
                Part(g, PrimitiveType.Cube, Vary(wall, rnd), new Vector3(0, h + 0.08f, -d / 2f), new Vector3(w + 0.06f, 0.1f, 0.04f));
            }
            Part(g, PrimitiveType.Cube, new Color(0.3f, 0.2f, 0.15f), new Vector3(F(rnd, -w / 4f, w / 4f), 0.15f, -d / 2f - 0.01f), new Vector3(0.14f, 0.3f, 0.02f));
            int windows = 1 + rnd.Next(3);
            for (int i = 0; i < windows; i++)
            {
                bool lit = rnd.NextDouble() < 0.3;
                Part(g, PrimitiveType.Cube, lit ? new Color(1f, 0.85f, 0.5f) : new Color(0.15f, 0.2f, 0.28f),
                     new Vector3(-w / 2f - 0.01f, h * F(rnd, 0.45f, 0.7f), F(rnd, -d / 3f, d / 3f)), new Vector3(0.02f, 0.1f, 0.12f), default, lit);
            }
            if (rnd.NextDouble() < 0.4)
                Part(g, PrimitiveType.Cube, new Color(0.38f, 0.32f, 0.3f), new Vector3(w / 3f, h + 0.3f, d / 6f), new Vector3(0.1f, 0.35f, 0.1f));
            if (rnd.NextDouble() < 0.35)
            {
                float ww = w * 0.5f, wd = d * 0.6f;
                Part(g, PrimitiveType.Cube, Vary(wall, rnd, 0.05f), new Vector3(w / 2f + ww / 2f - 0.05f, h * 0.4f, d / 4f), new Vector3(ww, h * 0.8f, wd));
                Part(g, PrimitiveType.Cube, roof, new Vector3(w / 2f + ww / 2f - 0.05f, h * 0.82f, d / 4f), new Vector3(ww + 0.06f, 0.05f, wd + 0.06f));
            }
            if (rnd.NextDouble() < 0.35) Fence(g, w + 0.5f, d + 0.5f, rnd);
        }

        void Fence(Transform g, float w, float d, System.Random rnd)
        {
            var c = new Color(0.55f, 0.45f, 0.32f);
            Part(g, PrimitiveType.Cube, c, new Vector3(0, 0.1f, -d / 2f), new Vector3(w, 0.03f, 0.02f));
            Part(g, PrimitiveType.Cube, c, new Vector3(0, 0.1f, d / 2f), new Vector3(w, 0.03f, 0.02f));
            Part(g, PrimitiveType.Cube, c, new Vector3(-w / 2f, 0.1f, 0), new Vector3(0.02f, 0.03f, d));
            Part(g, PrimitiveType.Cube, c, new Vector3(w / 2f, 0.1f, 0), new Vector3(0.02f, 0.03f, d));
            for (int i = 0; i < 4; i++)
                Part(g, PrimitiveType.Cube, c, new Vector3(i % 2 == 0 ? -w / 2f : w / 2f, 0.09f, i < 2 ? -d / 2f : d / 2f), new Vector3(0.04f, 0.18f, 0.04f));
        }

        void Farmland(Vector2 center, System.Random rnd)
        {
            var g = Group(decorStatic, center, Mathf.Round(F(rnd, 0, 2)) * 90f);
            float w = 3.4f, d = 3.0f;
            int crop = rnd.Next(3);
            Color soil = crop == 2 ? new Color(0.14f, 0.24f, 0.24f) : new Color(0.30f, 0.22f, 0.15f);
            Part(g, PrimitiveType.Cube, soil, new Vector3(0, 0.01f, 0), new Vector3(w, 0.02f, d));
            int rows = crop == 2 ? 10 : 8;
            for (int i = 0; i < rows; i++)
            {
                float z = -d / 2f + d * (i + 0.5f) / rows;
                if (crop == 0)
                    Part(g, PrimitiveType.Cube, Vary(new Color(0.78f, 0.66f, 0.30f), rnd, 0.06f), new Vector3(0, 0.08f, z), new Vector3(w * 0.94f, 0.14f, d / rows * 0.6f));
                else if (crop == 1)
                    for (int k = 0; k < 9; k++)
                        Part(g, PrimitiveType.Sphere, Vary(new Color(0.30f, 0.55f, 0.25f), rnd, 0.15f), new Vector3(-w / 2f + w * (k + 0.5f) / 9f, 0.07f, z), Vector3.one * 0.16f);
                else
                    Part(g, PrimitiveType.Cube, new Color(0.45f, 0.7f, 0.35f), new Vector3(0, 0.04f, z), new Vector3(w * 0.94f, 0.06f, 0.05f));
            }
            Fence(g, w + 0.1f, d + 0.1f, rnd);
            if (rnd.NextDouble() < 0.6)
            {
                var p = new Vector3(F(rnd, -1f, 1f), 0, F(rnd, -1f, 1f));
                Part(g, PrimitiveType.Cube, new Color(0.45f, 0.35f, 0.25f), p + new Vector3(0, 0.3f, 0), new Vector3(0.04f, 0.6f, 0.04f));
                Part(g, PrimitiveType.Cube, new Color(0.45f, 0.35f, 0.25f), p + new Vector3(0, 0.45f, 0), new Vector3(0.4f, 0.04f, 0.04f));
                Part(g, PrimitiveType.Sphere, new Color(0.8f, 0.7f, 0.4f), p + new Vector3(0, 0.65f, 0), Vector3.one * 0.14f);
            }
        }

        void Barn(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, Mathf.Round(F(rnd, 0, 4)) * 90f);
            var red = Vary(new Color(0.58f, 0.18f, 0.14f), rnd, 0.08f);
            Part(g, PrimitiveType.Cube, red, new Vector3(0, 0.35f, 0), new Vector3(0.9f, 0.7f, 1.1f));
            Part(g, PrimitiveType.Cube, new Color(0.25f, 0.22f, 0.22f), new Vector3(0, 0.72f, 0), new Vector3(0.66f, 0.66f, 1.14f), new Vector3(0, 0, 45f));
            Part(g, PrimitiveType.Cube, new Color(0.9f, 0.88f, 0.82f), new Vector3(0, 0.3f, -0.56f), new Vector3(0.4f, 0.5f, 0.02f));
            Part(g, PrimitiveType.Cylinder, new Color(0.7f, 0.72f, 0.74f), new Vector3(0.7f, 0.6f, 0.3f), new Vector3(0.38f, 0.6f, 0.38f));
            Part(g, PrimitiveType.Sphere, new Color(0.55f, 0.58f, 0.6f), new Vector3(0.7f, 1.2f, 0.3f), Vector3.one * 0.38f);
        }

        void Windmill(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, F(rnd, 0, 360));
            Part(g, PrimitiveType.Cylinder, new Color(0.82f, 0.8f, 0.74f), new Vector3(0, 0.6f, 0), new Vector3(0.45f, 0.6f, 0.45f));
            Part(g, PrimitiveType.Cube, new Color(0.4f, 0.25f, 0.2f), new Vector3(0, 1.3f, 0), new Vector3(0.4f, 0.25f, 0.5f), new Vector3(0, 45f, 0));
            var hub = Group(decorAnim, pos, 0, 1.25f);
            hub.position = g.TransformPoint(new Vector3(0, 1.25f, -0.3f));
            hub.rotation = g.rotation;
            var spin = new GameObject("blades").transform;
            spin.SetParent(hub, false);
            for (int i = 0; i < 4; i++)
                Part(spin, PrimitiveType.Cube, new Color(0.85f, 0.82f, 0.75f), Quaternion.Euler(0, 0, i * 90f) * new Vector3(0, 0.45f, 0), new Vector3(0.14f, 0.9f, 0.02f), new Vector3(0, 0, i * 90f));
            spinners.Add((spin, F(rnd, 40f, 70f)));
        }

        void Orchard(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, Mathf.Round(F(rnd, 0, 4)) * 90f);
            var fruit = Pick(rnd, new[] { new Color(0.9f, 0.3f, 0.2f), new Color(1f, 0.6f, 0.15f), new Color(0.95f, 0.85f, 0.3f) });
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(i % 2 == 0 ? -0.35f : 0.35f, 0, i < 2 ? -0.35f : 0.35f);
                Part(g, PrimitiveType.Cylinder, new Color(0.3f, 0.22f, 0.15f), p + new Vector3(0, 0.15f, 0), new Vector3(0.06f, 0.15f, 0.06f));
                Part(g, PrimitiveType.Sphere, Vary(new Color(0.2f, 0.42f, 0.2f), rnd), p + new Vector3(0, 0.42f, 0), Vector3.one * 0.42f);
                Part(g, PrimitiveType.Sphere, fruit, p + new Vector3(0.12f, 0.45f, -0.15f), Vector3.one * 0.07f);
            }
        }

        void Hay(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, F(rnd, 0, 360));
            int n = 2 + rnd.Next(3);
            for (int i = 0; i < n; i++)
                Part(g, PrimitiveType.Cylinder, Vary(new Color(0.78f, 0.66f, 0.35f), rnd), new Vector3(i * 0.32f - 0.3f, 0.14f, F(rnd, -0.2f, 0.2f)), new Vector3(0.28f, 0.14f, 0.28f), new Vector3(0, 0, 90f));
        }

        void WaterTower(Vector2 pos, System.Random rnd)
        {
            var g = Group(decorStatic, pos, 0);
            for (int i = 0; i < 4; i++)
                Part(g, PrimitiveType.Cube, new Color(0.35f, 0.3f, 0.28f), new Vector3(i % 2 == 0 ? -0.2f : 0.2f, 0.45f, i < 2 ? -0.2f : 0.2f), new Vector3(0.05f, 0.9f, 0.05f));
            Part(g, PrimitiveType.Cylinder, new Color(0.5f, 0.38f, 0.28f), new Vector3(0, 1.05f, 0), new Vector3(0.6f, 0.2f, 0.6f));
            Part(g, PrimitiveType.Cube, new Color(0.35f, 0.25f, 0.2f), new Vector3(0, 1.3f, 0), new Vector3(0.45f, 0.1f, 0.45f), new Vector3(0, 45f, 0));
        }

        void AnimateDecor(float t)
        {
            foreach (var f in facilityAnims) f(t);
            UpdateBridges();
            UpdateGuard(t);
            UpdateDyingZombies(t);
            foreach (var (tr, speed) in spinners)
                if (tr) tr.localRotation = Quaternion.Euler(0, 0, t * speed);
            foreach (var (r, on, off, phase) in neons)
                if (r) r.sharedMaterial = Mathf.PerlinNoise(t * 2.5f + phase, phase) < 0.3f ? off : on;
            foreach (var (sr, c, phase) in glows)
            {
                if (!sr) continue;
                float k = 0.7f + 0.5f * Mathf.PerlinNoise(t * 3f + phase, 0.5f);
                sr.color = new Color(c.r, c.g, c.b, c.a * k);
            }
        }
    }
}
