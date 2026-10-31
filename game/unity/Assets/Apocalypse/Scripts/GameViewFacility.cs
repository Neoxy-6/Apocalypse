using System.Collections.Generic;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        readonly List<System.Action<float>> facilityAnims = new List<System.Action<float>>();

        void BuildFacilityArt(Node n, Transform root)
        {
            switch (n.type)
            {
                case NodeType.Shelter: Bunker(root, n.id == Sim.map.startNode); break;
                case NodeType.Hospital: Hospital(root); break;
                case NodeType.Supply: SupplyDepot(root, n); break;
                case NodeType.Factory: Factory(root, n); break;
                case NodeType.Generator: Generator(root, n); break;
            }
        }

        void Hospital(Transform root)
        {
            var white = new Color(0.9f, 0.92f, 0.94f);
            var red = new Color(0.92f, 0.15f, 0.2f);
            var glass = new Color(0.55f, 0.75f, 0.9f);

            Part(root, PrimitiveType.Cube, white, new Vector3(0, 0.8f, 0.1f), new Vector3(1.6f, 1.6f, 1.15f));
            for (int i = 0; i < 3; i++)
                Part(root, PrimitiveType.Cube, glass, new Vector3(0, 0.38f + i * 0.45f, 0.1f), new Vector3(1.62f, 0.13f, 1.17f), default, true);
            Part(root, PrimitiveType.Cube, white * 0.92f, new Vector3(0.55f, 0.35f, -0.35f), new Vector3(0.5f, 0.7f, 0.4f));
            Part(root, PrimitiveType.Cube, red, new Vector3(0.55f, 0.72f, -0.35f), new Vector3(0.52f, 0.04f, 0.42f), default, true);
            Part(root, PrimitiveType.Cube, white, new Vector3(-0.3f, 0.38f, -0.62f), new Vector3(0.7f, 0.05f, 0.35f));
            Part(root, PrimitiveType.Cube, red, new Vector3(-0.3f, 0.41f, -0.62f), new Vector3(0.72f, 0.03f, 0.37f), default, true);
            Part(root, PrimitiveType.Cube, new Color(0.3f, 0.35f, 0.4f), new Vector3(-0.3f, 0.17f, -0.48f), new Vector3(0.35f, 0.34f, 0.02f));
            for (int f = 0; f < 4; f++)
            {
                var face = Quaternion.Euler(0, f * 90f, 0);
                float half = f % 2 == 0 ? 0.59f : 0.82f;
                var at = new Vector3(0, 1.38f, 0.1f) + face * new Vector3(0, 0, -half);
                Part(root, PrimitiveType.Cube, red, at, new Vector3(0.3f, 0.09f, 0.02f), new Vector3(0, f * 90f, 0), true);
                Part(root, PrimitiveType.Cube, red, at, new Vector3(0.09f, 0.3f, 0.02f), new Vector3(0, f * 90f, 0), true);
            }
            Part(root, PrimitiveType.Cube, red, new Vector3(-0.35f, 1.62f, 0.1f), new Vector3(0.7f, 0.04f, 0.2f), default, true);
            Part(root, PrimitiveType.Cube, red, new Vector3(-0.35f, 1.62f, 0.1f), new Vector3(0.2f, 0.04f, 0.7f), default, true);

            Part(root, PrimitiveType.Cylinder, new Color(0.35f, 0.37f, 0.4f), new Vector3(0.45f, 1.62f, 0.3f), new Vector3(0.55f, 0.02f, 0.55f));
            Part(root, PrimitiveType.Cube, Color.white, new Vector3(0.45f, 1.645f, 0.3f), new Vector3(0.04f, 0.01f, 0.25f), default, true);

            var amb = Group(root, Vector2.zero, 0f);
            amb.SetParent(root, false);
            amb.localPosition = new Vector3(0.95f, 0, -0.85f);
            amb.localRotation = Quaternion.Euler(0, 15f, 0);
            Part(amb, PrimitiveType.Cube, white, new Vector3(0, 0.15f, 0), new Vector3(0.28f, 0.24f, 0.55f));
            Part(amb, PrimitiveType.Cube, red, new Vector3(0, 0.15f, 0), new Vector3(0.29f, 0.05f, 0.56f), default, true);
            var lightA = Part(amb, PrimitiveType.Cube, red, new Vector3(-0.06f, 0.29f, -0.15f), new Vector3(0.08f, 0.05f, 0.06f), default, true).GetComponent<Renderer>();
            var lightB = Part(amb, PrimitiveType.Cube, new Color(0.2f, 0.4f, 1f), new Vector3(0.06f, 0.29f, -0.15f), new Vector3(0.08f, 0.05f, 0.06f), default, true).GetComponent<Renderer>();
            Material ra = UnlitMat(red), rb = UnlitMat(new Color(0.2f, 0.4f, 1f)), dark = UnlitMat(new Color(0.15f, 0.15f, 0.18f));
            facilityAnims.Add(t =>
            {
                bool a = Mathf.Repeat(t, 0.5f) < 0.25f;
                lightA.sharedMaterial = a ? ra : dark;
                lightB.sharedMaterial = a ? dark : rb;
            });
        }

        void Bunker(Transform root, bool main)
        {
            var olive = new Color(0.33f, 0.38f, 0.28f);
            var sand = new Color(0.62f, 0.55f, 0.4f);

            Part(root, PrimitiveType.Cylinder, olive, new Vector3(0, 0, 0.1f), new Vector3(1.2f, 0.7f, 1.2f), new Vector3(90f, 0, 0));
            Part(root, PrimitiveType.Cube, olive * 0.8f, new Vector3(0, 0.27f, -0.6f), new Vector3(0.95f, 0.55f, 0.04f));
            Part(root, PrimitiveType.Cube, new Color(0.2f, 0.22f, 0.18f), new Vector3(0, 0.2f, -0.63f), new Vector3(0.3f, 0.4f, 0.02f));
            Part(root, PrimitiveType.Cube, new Color(1f, 0.85f, 0.3f), new Vector3(0, 0.47f, -0.63f), new Vector3(0.5f, 0.08f, 0.02f), default, true);

            for (int i = 0; i < 7; i++)
            {
                float x = -0.75f + i * 0.25f;
                Part(root, PrimitiveType.Cube, Vary(sand, new System.Random(i), 0.08f), new Vector3(x, 0.08f, -0.95f), new Vector3(0.24f, 0.15f, 0.16f));
                if (i % 2 == 0) Part(root, PrimitiveType.Cube, Vary(sand, new System.Random(i + 9), 0.08f), new Vector3(x + 0.12f, 0.22f, -0.95f), new Vector3(0.24f, 0.13f, 0.16f));
            }
            if (!main) return;

            Part(root, PrimitiveType.Cylinder, new Color(0.75f, 0.75f, 0.78f), new Vector3(0.75f, 0.8f, -0.4f), new Vector3(0.04f, 0.8f, 0.04f));
            var flagPivot = new GameObject("flag").transform;
            flagPivot.SetParent(root, false);
            flagPivot.localPosition = new Vector3(0.75f, 1.45f, -0.4f);
            Part(flagPivot, PrimitiveType.Cube, new Color(0.3f, 0.8f, 0.95f), new Vector3(0.22f, 0, 0), new Vector3(0.42f, 0.24f, 0.02f), default, true);
            facilityAnims.Add(t => flagPivot.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 2.2f) * 25f, Mathf.Sin(t * 3.1f) * 4f));
        }

        void SupplyDepot(Transform root, Node n)
        {
            var body = Part(root, PrimitiveType.Cube, new Color(1.0f, 0.84f, 0.31f), new Vector3(0, 0.38f, 0.15f), new Vector3(1.4f, 0.75f, 0.7f));
            supplyViews[n.id] = body.GetComponent<Renderer>();
            for (int i = 0; i < 6; i++)
                Part(root, PrimitiveType.Cube, new Color(0.75f, 0.6f, 0.2f), new Vector3(-0.6f + i * 0.24f, 0.38f, -0.21f), new Vector3(0.04f, 0.7f, 0.02f));
            var green = new Color(0.2f, 0.85f, 0.4f);
            Part(root, PrimitiveType.Cube, Color.white, new Vector3(0.35f, 0.45f, -0.22f), new Vector3(0.36f, 0.36f, 0.02f), default, true);
            Part(root, PrimitiveType.Cube, green, new Vector3(0.35f, 0.45f, -0.235f), new Vector3(0.26f, 0.08f, 0.02f), default, true);
            Part(root, PrimitiveType.Cube, green, new Vector3(0.35f, 0.45f, -0.235f), new Vector3(0.08f, 0.26f, 0.02f), default, true);

            var rnd = new System.Random(n.id);
            for (int i = 0; i < 5; i++)
            {
                float s = F(rnd, 0.18f, 0.26f);
                Part(root, PrimitiveType.Cube, Vary(new Color(0.55f, 0.45f, 0.3f), rnd), new Vector3(-0.55f + (i % 3) * 0.28f, s / 2f + (i / 3) * 0.24f, -0.5f), Vector3.one * s, new Vector3(0, F(rnd, -15, 15), 0));
            }
            Part(root, PrimitiveType.Cube, new Color(0.25f, 0.4f, 0.55f), new Vector3(0.45f, 0.18f, -0.5f), new Vector3(0.45f, 0.35f, 0.3f), new Vector3(0, 0, 6f));
        }

        void Factory(Transform root, Node n)
        {
            var wall = new Color(0.52f, 0.47f, 0.42f);
            var roof = new Color(0.45f, 0.5f, 0.55f);
            Part(root, PrimitiveType.Cube, wall, new Vector3(0, 0.4f, 0.1f), new Vector3(1.8f, 0.8f, 1.2f));
            Part(root, PrimitiveType.Cylinder, roof, new Vector3(0, 0.8f, 0.1f), new Vector3(1.2f, 0.92f, 0.5f), new Vector3(0, 0, 90f));
            for (int i = 0; i < 4; i++)
                Part(root, PrimitiveType.Cube, roof * 0.8f, new Vector3(-0.68f + i * 0.45f, 1.06f, 0.1f), new Vector3(0.03f, 0.04f, 1.22f));

            Part(root, PrimitiveType.Cube, new Color(0.35f, 0.33f, 0.32f), new Vector3(-0.3f, 0.3f, -0.51f), new Vector3(0.75f, 0.6f, 0.02f));
            for (int i = 0; i < 4; i++)
                Part(root, PrimitiveType.Cube, new Color(0.45f, 0.43f, 0.42f), new Vector3(-0.3f, 0.1f + i * 0.14f, -0.525f), new Vector3(0.75f, 0.03f, 0.01f));
            Part(root, PrimitiveType.Cube, new Color(1f, 0.8f, 0.15f), new Vector3(-0.3f, 0.63f, -0.52f), new Vector3(0.8f, 0.05f, 0.02f), default, true);

            Part(root, PrimitiveType.Cube, new Color(0.5f, 0.38f, 0.25f), new Vector3(0.55f, 0.03f, -0.75f), new Vector3(0.45f, 0.06f, 0.4f));
            Part(root, PrimitiveType.Cube, new Color(0.62f, 0.5f, 0.32f), new Vector3(0.55f, 0.2f, -0.75f), new Vector3(0.4f, 0.28f, 0.34f));
            var fl = new GameObject("forklift").transform;
            fl.SetParent(root, false);
            fl.localPosition = new Vector3(-0.85f, 0, -0.8f);
            fl.localRotation = Quaternion.Euler(0, 30f, 0);
            Part(fl, PrimitiveType.Cube, new Color(0.95f, 0.7f, 0.1f), new Vector3(0, 0.12f, 0), new Vector3(0.22f, 0.18f, 0.3f));
            Part(fl, PrimitiveType.Cube, new Color(0.2f, 0.2f, 0.2f), new Vector3(0, 0.28f, -0.17f), new Vector3(0.2f, 0.4f, 0.03f));
            Part(fl, PrimitiveType.Cube, new Color(0.2f, 0.2f, 0.2f), new Vector3(0, 0.05f, -0.3f), new Vector3(0.18f, 0.02f, 0.22f));

            var brick = new Color(0.62f, 0.3f, 0.25f);
            Part(root, PrimitiveType.Cylinder, brick, new Vector3(0.62f, 1.05f, 0.45f), new Vector3(0.22f, 0.65f, 0.22f));
            Part(root, PrimitiveType.Cylinder, Color.white, new Vector3(0.62f, 1.55f, 0.45f), new Vector3(0.235f, 0.06f, 0.235f));
            var smoke = Particles(root, root.TransformPoint(new Vector3(0.62f, 1.75f, 0.45f)), new Color(0.75f, 0.73f, 0.7f, 0.6f), new Color(0.5f, 0.5f, 0.5f, 0.4f),
                                  6f, 4f, 0.5f, 0.55f, -0.04f, new Vector3(0.12f, 0.12f, 0.05f), 0.12f, 0.2f, 8);
            var windows = new Renderer[5];
            for (int i = 0; i < windows.Length; i++)
                windows[i] = Part(root, PrimitiveType.Cube, Color.white, new Vector3(-0.8f + i * 0.4f, 0.62f, 0.71f), new Vector3(0.24f, 0.12f, 0.02f), default, true).GetComponent<Renderer>();
            Part(root, PrimitiveType.Cube, new Color(0.22f, 0.22f, 0.24f), new Vector3(0.45f, 0.1f, -0.95f), new Vector3(0.9f, 0.05f, 0.22f));
            var box = Part(root, PrimitiveType.Cube, new Color(0.85f, 0.6f, 0.3f), new Vector3(0.45f, 0.19f, -0.95f), Vector3.one * 0.13f).transform;
            Material lit = UnlitMat(new Color(1f, 0.85f, 0.45f)), dark = UnlitMat(new Color(0.12f, 0.12f, 0.15f));
            bool was = true;
            facilityAnims.Add(t =>
            {
                bool on = Sim.Powered(n);
                if (on != was)
                {
                    was = on;
                    var em = smoke.emission; em.enabled = on;
                    foreach (var w in windows) w.sharedMaterial = on ? lit : dark;
                }
                if (on) box.localPosition = new Vector3(0.05f + Mathf.Repeat(t * 0.25f, 0.8f), 0.19f, -0.95f);
            });
        }

        void Generator(Transform root, Node n)
        {
            var concrete = new Color(0.55f, 0.55f, 0.53f);
            var body = new Color(0.32f, 0.48f, 0.42f);
            Part(root, PrimitiveType.Cube, concrete, new Vector3(0, 0.04f, 0.15f), new Vector3(1.5f, 0.08f, 1.1f));
            Part(root, PrimitiveType.Cube, body, new Vector3(-0.1f, 0.38f, 0.2f), new Vector3(1.0f, 0.6f, 0.7f));
            Part(root, PrimitiveType.Cube, body * 0.85f, new Vector3(-0.1f, 0.7f, 0.2f), new Vector3(1.04f, 0.05f, 0.74f));
            for (int i = 0; i < 5; i++)
                Part(root, PrimitiveType.Cube, body * 0.6f, new Vector3(-0.45f + i * 0.12f, 0.4f, -0.155f), new Vector3(0.05f, 0.35f, 0.02f));

            for (int i = 0; i < 6; i++)
                Part(root, PrimitiveType.Cube, i % 2 == 0 ? new Color(1f, 0.8f, 0.1f) : new Color(0.1f, 0.1f, 0.1f),
                     new Vector3(-0.55f + i * 0.18f, 0.1f, -0.16f), new Vector3(0.18f, 0.05f, 0.02f), default, true);

            Part(root, PrimitiveType.Cylinder, new Color(0.2f, 0.22f, 0.22f), new Vector3(0.25f, 0.42f, -0.16f), new Vector3(0.38f, 0.02f, 0.38f), new Vector3(90f, 0, 0));
            var fan = new GameObject("fan").transform;
            fan.SetParent(root, false);
            fan.localPosition = new Vector3(0.25f, 0.42f, -0.18f);
            for (int i = 0; i < 3; i++)
                Part(fan, PrimitiveType.Cube, new Color(0.75f, 0.77f, 0.78f), Vector3.zero, new Vector3(0.05f, 0.32f, 0.01f), new Vector3(0, 0, i * 60f));

            Part(root, PrimitiveType.Cylinder, new Color(0.35f, 0.33f, 0.32f), new Vector3(-0.45f, 0.9f, 0.35f), new Vector3(0.08f, 0.22f, 0.08f));
            var exhaust = Particles(root, root.TransformPoint(new Vector3(-0.45f, 1.15f, 0.35f)), new Color(0.7f, 0.7f, 0.68f, 0.45f), new Color(0.5f, 0.5f, 0.5f, 0.3f),
                                    5f, 2.5f, 0.4f, 0.3f, -0.03f, new Vector3(0.05f, 0.05f, 0.05f), 0.08f, 0.15f, 8);

            var sparks = Particles(root, root.TransformPoint(new Vector3(0.1f, 0.72f, 0.0f)), new Color(1f, 0.95f, 0.5f, 1f), new Color(1f, 0.6f, 0.1f, 1f),
                                   22f, 0.45f, 1.6f, 0.06f, 1.5f, new Vector3(0.3f, 0.2f, 0.05f), 0.6f, 0f, 9);
            var black = Particles(root, root.TransformPoint(new Vector3(-0.1f, 0.8f, 0.2f)), new Color(0.15f, 0.14f, 0.14f, 0.6f), new Color(0.08f, 0.08f, 0.08f, 0.5f),
                                  6f, 3.5f, 0.5f, 0.6f, -0.04f, new Vector3(0.3f, 0.3f, 0.05f), 0.15f, 0.2f, 8);

            var lamp = Part(root, PrimitiveType.Sphere, Color.white, new Vector3(-0.45f, 0.8f, 0.0f), Vector3.one * 0.12f, default, true).GetComponent<Renderer>();

            var pole = new Color(0.4f, 0.3f, 0.2f);
            Part(root, PrimitiveType.Cylinder, pole, new Vector3(0.8f, 0.7f, 0.55f), new Vector3(0.06f, 0.7f, 0.06f));
            Part(root, PrimitiveType.Cube, pole, new Vector3(0.8f, 1.3f, 0.55f), new Vector3(0.45f, 0.04f, 0.04f));
            Part(root, PrimitiveType.Cylinder, new Color(0.1f, 0.1f, 0.1f), new Vector3(0.4f, 0.98f, 0.38f), new Vector3(0.015f, 0.42f, 0.015f), new Vector3(0, 0, 58f));
            Material green = UnlitMat(new Color(0.3f, 1f, 0.45f)), red = UnlitMat(new Color(1f, 0.2f, 0.15f)), off = UnlitMat(new Color(0.25f, 0.08f, 0.06f));
            bool was = true;
            facilityAnims.Add(t =>
            {
                bool on = Sim.Powered(n);
                if (on != was)
                {
                    was = on;
                    var e1 = exhaust.emission; e1.enabled = on;
                    var e2 = sparks.emission; e2.enabled = !on;
                    var e3 = black.emission; e3.enabled = !on;
                }
                if (on) fan.localRotation = Quaternion.Euler(0, 0, t * 540f);
                else fan.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 3f) * 8f);
                lamp.sharedMaterial = on ? green : (Mathf.Repeat(t, 0.6f) < 0.3f ? red : off);
            });
        }

        void EscapeCompound(Vector2 pad)
        {
            var root = new GameObject("compound").transform;
            root.SetParent(mapRoot, false);
            root.position = W(pad);
            var white = Color.white;
            Part(root, PrimitiveType.Cube, white, new Vector3(-0.32f, 0.066f, 0), new Vector3(0.1f, 0.01f, 0.8f), default, true);
            Part(root, PrimitiveType.Cube, white, new Vector3(0.32f, 0.066f, 0), new Vector3(0.1f, 0.01f, 0.8f), default, true);
            Part(root, PrimitiveType.Cube, white, new Vector3(0, 0.066f, 0), new Vector3(0.6f, 0.01f, 0.1f), default, true);

            const int n = 12;
            var lights = new Renderer[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                lights[i] = Part(root, PrimitiveType.Sphere, new Color(1f, 0.85f, 0.3f), new Vector3(Mathf.Cos(a) * 1.3f, 0.08f, Mathf.Sin(a) * 1.3f), Vector3.one * 0.08f, default, true).GetComponent<Renderer>();
            }
            Material on = UnlitMat(new Color(1f, 0.9f, 0.4f)), off = UnlitMat(new Color(0.35f, 0.28f, 0.1f));
            facilityAnims.Add(t =>
            {
                int k = (int)(t * 8f) % n;
                for (int i = 0; i < n; i++) lights[i].sharedMaterial = (i == k || i == (k + n / 2) % n) ? on : off;
            });

            var sand = new Color(0.62f, 0.55f, 0.4f);
            var rnd = new System.Random(gm.cfg.seed + 5);
            for (int i = 0; i < 28; i++)
            {
                float a = i * Mathf.PI * 2f / 28f;
                var p = pad + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.85f;
                if (RoadClearance(p) < 0.55f) continue;
                Part(mapRoot, PrimitiveType.Cube, Vary(sand, rnd, 0.08f), W(p, 0.08f), new Vector3(0.38f, 0.16f, 0.2f), new Vector3(0, -a * Mathf.Rad2Deg + 90f, 0));
            }

            Vector2 tower = pad + new Vector2(1.6f, 1.6f);
            float best = -1f;
            foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            {
                var q = pad + d.normalized * 2.3f;
                float c = RoadClearance(q);
                if (c > best) { best = c; tower = q; }
            }
            var tw = Group(mapRoot, tower, 0f);
            var wood = new Color(0.42f, 0.33f, 0.24f);
            for (int i = 0; i < 4; i++)
                Part(tw, PrimitiveType.Cube, wood, new Vector3(i % 2 == 0 ? -0.22f : 0.22f, 0.7f, i < 2 ? -0.22f : 0.22f), new Vector3(0.06f, 1.4f, 0.06f));
            Part(tw, PrimitiveType.Cube, wood * 1.1f, new Vector3(0, 1.4f, 0), new Vector3(0.6f, 0.06f, 0.6f));
            Part(tw, PrimitiveType.Cube, sand, new Vector3(0, 1.52f, 0), new Vector3(0.62f, 0.18f, 0.62f));
            BuildGuard(tower, pad);
            var lamp = Group(mapRoot, tower + new Vector2(0.26f, -0.26f), 0f, 1.68f);
            Part(lamp, PrimitiveType.Cylinder, new Color(0.25f, 0.25f, 0.27f), new Vector3(0, 0, 0.12f), new Vector3(0.16f, 0.1f, 0.16f), new Vector3(90f, 0, 0));
            Part(lamp, PrimitiveType.Cylinder, new Color(1f, 0.95f, 0.75f), new Vector3(0, 0, 0.23f), new Vector3(0.13f, 0.01f, 0.13f), new Vector3(90f, 0, 0), true);
            var spot = Flat(mapRoot, soft, new Color(1f, 0.95f, 0.7f, 0.22f), 2.2f, 4, 0.05f);
            facilityAnims.Add(t =>
            {
                float a = Mathf.Sin(t * 0.6f) * 1.4f + 0.8f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var target = tower + dir * 3.2f;
                spot.transform.position = W(target, 0.05f);
                lamp.rotation = Quaternion.LookRotation(W(target, 0f) - lamp.position);
            });

            for (int i = 0; i < 2; i++)
            {
                float a = (i == 0 ? 0.6f : 3.7f);
                var p = pad + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.55f;
                if (RoadClearance(p) < 0.4f) continue;
                Part(mapRoot, PrimitiveType.Cube, new Color(0.3f, 0.3f, 0.32f), W(p, 0.55f), new Vector3(0.05f, 1.1f, 0.05f));
                Part(mapRoot, PrimitiveType.Cube, new Color(1f, 0.95f, 0.8f), W(p, 1.12f), new Vector3(0.2f, 0.08f, 0.12f), default, true);
                var g = Flat(mapRoot, soft, new Color(1f, 0.95f, 0.75f, 0.12f), 2.4f, 4, 0.04f);
                g.transform.position = W(p, 0.04f);
            }
        }
    }
}
