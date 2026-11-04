using System.Collections.Generic;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        const float DeckH = 0.25f, RampLen = 0.9f;
        readonly Dictionary<int, GameObject> bridgeGaps = new Dictionary<int, GameObject>();
        readonly List<(Vector2 mid, Vector2 dir, float half)> bridgeSpans = new List<(Vector2, Vector2, float)>();

        float GroundH(Vector2 p)
        {
            float best = 0f;
            foreach (var (mid, dir, half) in bridgeSpans)
            {
                var d = p - mid;
                float along = Mathf.Abs(Vector2.Dot(d, dir)), side = Mathf.Abs(d.x * dir.y - d.y * dir.x);
                if (side > 0.75f || along > half + RampLen) continue;
                best = Mathf.Max(best, along <= half ? DeckH : DeckH * (1f - (along - half) / RampLen));
            }
            return best;
        }

        void BuildBridges()
        {
            bridgeGaps.Clear();
            bridgeSpans.Clear();
            var map = Sim.map;
            foreach (var e in map.edges)
            {
                if (!e.bridge) continue;
                Vector2 a = map.nodes[e.a].pos, b = map.nodes[e.b].pos;
                if (a.x > b.x) (a, b) = (b, a);
                var dir = (b - a).normalized;
                float total = (b - a).magnitude;
                Vector2 mid = Vector2.Lerp(a, b, CityMap.CrossT(riverLine, a, b, out var flow));
                float across = Mathf.Abs(dir.x * flow.y - dir.y * flow.x);
                float len = Mathf.Min(3.8f / Mathf.Max(0.5f, across), total - 2f * RampLen - 0.6f);
                float half = len / 2f;
                bridgeSpans.Add((mid, dir, half));

                var lr = roads[e.id];
                lr.positionCount = 6;
                lr.SetPositions(new[]
                {
                    W(a, 0.01f), W(mid - dir * (half + RampLen), 0.01f), W(mid - dir * half, DeckH + 0.01f),
                    W(mid + dir * half, DeckH + 0.01f), W(mid + dir * (half + RampLen), 0.01f), W(b, 0.01f),
                });

                var g = Group(mapRoot, mid, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg);
                int style = Mathf.Abs(gm.cfg.seed * 7 + e.id * 13) % 3;
                Color deck = style == 0 ? new Color(0.52f, 0.49f, 0.44f) : new Color(0.36f, 0.38f, 0.42f);
                Color rail = style == 1 ? new Color(0.75f, 0.32f, 0.22f) : style == 2 ? new Color(0.55f, 0.6f, 0.68f) : new Color(0.6f, 0.57f, 0.52f);

                float seg = len / 3f;
                var gap = new GameObject("gap").transform;
                gap.SetParent(g, false);
                for (int i = -1; i <= 1; i++)
                {
                    var parent = i == 0 ? gap : g;
                    Part(parent, PrimitiveType.Cube, deck, new Vector3(0, DeckH - 0.05f, i * seg), new Vector3(0.95f, 0.1f, seg + 0.01f));
                    for (int s = -1; s <= 1; s += 2)
                        Part(parent, PrimitiveType.Cube, rail, new Vector3(s * 0.46f, DeckH + 0.12f, i * seg), new Vector3(0.04f, 0.04f, seg + 0.01f));
                }
                bridgeGaps[e.id] = gap.gameObject;
                for (float z = -half; z <= half + 0.01f; z += 0.45f)
                    for (int s = -1; s <= 1; s += 2)
                        Part(Mathf.Abs(z) < seg / 2f ? gap : g, PrimitiveType.Cube, rail, new Vector3(s * 0.46f, DeckH + 0.06f, z), new Vector3(0.04f, 0.12f, 0.04f));

                float rampLen = Mathf.Sqrt(RampLen * RampLen + DeckH * DeckH), pitch = Mathf.Atan2(DeckH, RampLen) * Mathf.Rad2Deg;
                Part(g, PrimitiveType.Cube, deck, new Vector3(0, DeckH / 2f - 0.04f, -half - RampLen / 2f), new Vector3(0.95f, 0.08f, rampLen), new Vector3(-pitch, 0, 0));
                Part(g, PrimitiveType.Cube, deck, new Vector3(0, DeckH / 2f - 0.04f, half + RampLen / 2f), new Vector3(0.95f, 0.08f, rampLen), new Vector3(pitch, 0, 0));

                for (int s = -1; s <= 1; s += 2)
                    for (int end = -1; end <= 1; end += 2)
                    {
                        var p = new Vector3(s * 0.55f, 0, end * (half + 0.05f));
                        Part(g, PrimitiveType.Cube, new Color(0.3f, 0.3f, 0.32f), p + new Vector3(0, DeckH + 0.3f, 0), new Vector3(0.04f, 0.6f, 0.04f));
                        Part(g, PrimitiveType.Sphere, new Color(1f, 0.88f, 0.6f), p + new Vector3(0, DeckH + 0.62f, 0), Vector3.one * 0.1f, default, true);
                    }

                switch (style)
                {
                    case 0:
                        for (int i = -1; i <= 1; i++)
                            Part(g, PrimitiveType.Cube, deck * 0.85f, new Vector3(0, DeckH / 2f - 0.05f, i * len * 0.3f), new Vector3(1.0f, DeckH, 0.28f));
                        for (int i = -1; i <= 1; i += 2)
                            Part(g, PrimitiveType.Cylinder, deck * 0.7f, new Vector3(0, 0.02f, i * len * 0.15f), new Vector3(0.12f, 0.48f, len * 0.22f), new Vector3(0, 0, 90f));
                        break;
                    case 1:
                    {
                        float top = DeckH + 0.6f, span = len * 0.9f;
                        int bays = Mathf.Max(3, Mathf.RoundToInt(span / 0.6f));
                        for (int s = -1; s <= 1; s += 2)
                        {
                            Part(g, PrimitiveType.Cube, rail, new Vector3(s * 0.46f, top, 0), new Vector3(0.05f, 0.05f, span));
                            for (int k = 0; k <= bays; k++)
                            {
                                float z = -span / 2f + span * k / bays;
                                Part(g, PrimitiveType.Cube, rail, new Vector3(s * 0.46f, (DeckH + top) / 2f, z), new Vector3(0.04f, top - DeckH, 0.04f));
                                if (k == bays) continue;
                                float bz = span / bays, diag = Mathf.Sqrt(bz * bz + (top - DeckH) * (top - DeckH));
                                float ang = Mathf.Atan2(top - DeckH, bz) * Mathf.Rad2Deg * (k % 2 == 0 ? 1 : -1);
                                Part(g, PrimitiveType.Cube, rail, new Vector3(s * 0.46f, (DeckH + top) / 2f, z + bz / 2f), new Vector3(0.03f, 0.03f, diag), new Vector3(ang, 0, 0));
                            }
                        }
                        for (int k = 0; k <= 2; k++)
                            Part(g, PrimitiveType.Cube, rail, new Vector3(0, top, -span / 2f + span * k / 2f), new Vector3(0.95f, 0.04f, 0.04f));
                        for (int i = -1; i <= 1; i += 2)
                            Part(g, PrimitiveType.Cube, new Color(0.4f, 0.4f, 0.42f), new Vector3(0, DeckH / 2f - 0.05f, i * len * 0.33f), new Vector3(0.6f, DeckH, 0.25f));
                        break;
                    }
                    default:
                    {
                        float towerZ = len * 0.3f, towerH = DeckH + 1.3f;
                        for (int i = -1; i <= 1; i += 2)
                        {
                            for (int s = -1; s <= 1; s += 2)
                                Part(g, PrimitiveType.Cube, rail, new Vector3(s * 0.5f, towerH / 2f, i * towerZ), new Vector3(0.1f, towerH, 0.12f));
                            Part(g, PrimitiveType.Cube, rail, new Vector3(0, towerH - 0.1f, i * towerZ), new Vector3(1.1f, 0.08f, 0.1f));
                            Part(g, PrimitiveType.Cube, rail, new Vector3(0, DeckH + 0.55f, i * towerZ), new Vector3(1.1f, 0.06f, 0.08f));
                        }
                        var cable = new Color(0.8f, 0.82f, 0.85f);

                        System.Func<float, float> sag = z => DeckH + 0.35f + (towerH - DeckH - 0.45f) * (z * z) / (towerZ * towerZ);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            const int n = 6;
                            for (int k = 0; k < n; k++)
                            {
                                float z0 = -towerZ + 2f * towerZ * k / n, z1 = -towerZ + 2f * towerZ * (k + 1) / n;
                                CableSeg(g, cable, new Vector3(s * 0.5f, sag(z0) - 0.1f, z0), new Vector3(s * 0.5f, sag(z1) - 0.1f, z1));
                                if (k > 0) Part(g, PrimitiveType.Cube, cable, new Vector3(s * 0.5f, (sag(z0) - 0.1f + DeckH) / 2f, z0), new Vector3(0.015f, sag(z0) - 0.1f - DeckH, 0.015f));
                            }
                            for (int i = -1; i <= 1; i += 2)
                                CableSeg(g, cable, new Vector3(s * 0.5f, towerH - 0.1f, i * towerZ), new Vector3(s * 0.5f, DeckH, i * half));
                        }
                        break;
                    }
                }
            }
        }

        void CableSeg(Transform g, Color c, Vector3 p0, Vector3 p1)
        {
            var d = p1 - p0;
            var go = Part(g, PrimitiveType.Cube, c, (p0 + p1) / 2f, new Vector3(0.025f, 0.025f, d.magnitude));
            go.transform.localRotation = Quaternion.LookRotation(d);
        }

        void UpdateBridges()
        {
            foreach (var kv in bridgeGaps)
                if (kv.Value && kv.Key < Sim.map.edges.Count)
                {
                    bool broken = Sim.map.edges[kv.Key].blocked;
                    if (kv.Value.activeSelf == broken) kv.Value.SetActive(!broken);
                }
        }
    }
}
