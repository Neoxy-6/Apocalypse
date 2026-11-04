using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public class CityMap
    {
        public const float Inf = 1e9f;
        public readonly int cols, rows, bridgeCount;
        public readonly float spacing = 6f;
        public readonly List<Vector2Int> river = new List<Vector2Int>();
        readonly HashSet<Vector2Int> riverSet = new HashSet<Vector2Int>();
        readonly HashSet<int> cuts = new HashSet<int>();
        bool[] sideA = new bool[0];

        public float Scale => nodes.Count / 48f;

        public CityMap(int cols = 8, int rows = 6, int bridges = 3)
        {
            this.cols = Mathf.Max(4, cols);
            this.rows = Mathf.Max(3, rows);
            bridgeCount = Mathf.Clamp(bridges, 1, this.rows);
        }

        public bool West(int node) => sideA[node];
        public bool RiverInCell(int r, int c) => riverSet.Contains(new Vector2Int(c + 1, r + 1));
        int Key(int a, int b) => Mathf.Min(a, b) * 10000 + Mathf.Max(a, b);
        bool Cut(int a, int b) => cuts.Contains(Key(a, b));

        public bool WestAt(Vector2 p)
        {
            Node best = nodes[0];
            foreach (var n in nodes) if ((n.pos - p).sqrMagnitude < (best.pos - p).sqrMagnitude) best = n;
            return sideA[best.id];
        }

        public List<Vector2> RiverLine()
        {
            var pts = river.Select(d => new Vector2(d.x - 0.5f, d.y - 0.5f) * spacing + Warp(d.x - 0.5f, d.y - 0.5f)).ToList();
            for (int k = 0; k < 3; k++)
            {
                var sm = new List<Vector2> { pts[0] };
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    sm.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.25f));
                    sm.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.75f));
                }
                sm.Add(pts[pts.Count - 1]);
                pts = sm;
            }
            return pts;
        }

        public static float SegDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        public static float RiverDistance(List<Vector2> line, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++) best = Mathf.Min(best, SegDistance(p, line[i], line[i + 1]));
            return best;
        }

        public static float CrossT(List<Vector2> line, Vector2 a, Vector2 b, out Vector2 riverDir)
        {
            riverDir = (b - a).normalized;
            riverDir = new Vector2(-riverDir.y, riverDir.x);
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector2 c = line[i], d = line[i + 1], r = b - a, s = d - c;
                float den = r.x * s.y - r.y * s.x;
                if (Mathf.Abs(den) < 1e-6f) continue;
                float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / den, u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / den;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) { riverDir = s.normalized; return t; }
            }
            return 0.5f;
        }

        public int EastNode => nodes.First(n => !sideA[n.id]).id;

        public Vector2 Size => new Vector2((cols - 1) * spacing, (rows - 1) * spacing);

        public List<Node> nodes = new List<Node>();
        public List<Edge> edges = new List<Edge>();
        public float[,] dist;
        public int[,] next;
        float[] warpX = new float[0], warpY = new float[0];
        int warpW, warpH;

        public Vector2 Warp(float gx, float gy)
        {
            float u = gx / 3f, v = gy / 3f;
            int i = Mathf.Clamp(Mathf.FloorToInt(u), 0, warpW - 2), j = Mathf.Clamp(Mathf.FloorToInt(v), 0, warpH - 2);
            float fu = u - i, fv = v - j;
            float Lerp2(float[] w)
            {
                float a = w[j * warpW + i] + (w[j * warpW + i + 1] - w[j * warpW + i]) * fu;
                float b = w[(j + 1) * warpW + i] + (w[(j + 1) * warpW + i + 1] - w[(j + 1) * warpW + i]) * fu;
                return a + (b - a) * fv;
            }
            return new Vector2(Lerp2(warpX), Lerp2(warpY));
        }
        public int startNode;

        public Edge FindEdge(int a, int b)
        {
            foreach (int e in nodes[a].edges)
                if (edges[e].Other(a) == b) return edges[e];
            return null;
        }

        public void Generate(System.Random rng)
        {
            nodes.Clear(); edges.Clear();
            warpW = cols / 3 + 2; warpH = rows / 3 + 2;
            warpX = new float[warpW * warpH]; warpY = new float[warpW * warpH];
            for (int k = 0; k < warpW * warpH; k++)
            {
                warpX[k] = (float)(rng.NextDouble() - 0.5) * 5f;
                warpY[k] = (float)(rng.NextDouble() - 0.5) * 5f;
            }
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    float jx = (float)(rng.NextDouble() - 0.5) * 2.2f;
                    float jy = (float)(rng.NextDouble() - 0.5) * 2.2f;
                    var w = Warp(c, r);
                    float x = Mathf.Clamp(c * spacing + jx + w.x, -2.5f, Size.x + 2.5f);
                    float y = Mathf.Clamp(r * spacing + jy + w.y, -2.5f, Size.y + 2.5f);
                    nodes.Add(new Node { id = r * cols + c, pos = new Vector2(x, y) });
                }

            GenerateRiver(rng);
            var steps = new List<int>();
            for (int i = 0; i + 1 < river.Count; i++) steps.Add(StepEdge(river[i], river[i + 1]));
            int third = Mathf.Max(1, steps.Count / 3);
            var bridgeKeys = new HashSet<int> { steps[rng.Next(third)], steps[steps.Count - 1 - rng.Next(third)] };
            if (bridgeCount > 2)
                foreach (int k in steps.Where(k => !bridgeKeys.Contains(k)).OrderBy(_ => rng.Next()).Take(bridgeCount - 2).ToList()) bridgeKeys.Add(k);

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int id = r * cols + c;
                    if (c + 1 < cols)
                    {
                        if (!Cut(id, id + 1)) AddEdge(id, id + 1, false);
                        else if (bridgeKeys.Contains(Key(id, id + 1))) AddEdge(id, id + 1, true);
                    }
                    if (r + 1 < rows)
                    {
                        if (!Cut(id, id + cols)) AddEdge(id, id + cols, false);
                        else if (bridgeKeys.Contains(Key(id, id + cols))) AddEdge(id, id + cols, true);
                    }
                }

            for (int i = 0; i < cols * rows / 14; i++)
            {
                int c = rng.Next(cols - 1), r = rng.Next(rows - 1);
                bool down = rng.Next(2) == 0;
                if (RiverInCell(r, c)) continue;
                if (down) AddEdge(r * cols + c, (r + 1) * cols + c + 1, false);
                else AddEdge(r * cols + c + 1, (r + 1) * cols + c, false);
            }


            var candidates = edges.Where(e => !e.bridge).OrderBy(_ => rng.Next()).Take(edges.Count).ToList();
            foreach (var e in candidates)
            {
                if (nodes[e.a].edges.Count <= 2 || nodes[e.b].edges.Count <= 2) continue;
                e.blocked = true;
                if (!Reachable()) { e.blocked = false; continue; }
                RemoveEdge(e);
            }

            AssignTypes(rng);
            Recompute();
        }

        Vector2Int Entry(int side, System.Random rng)
        {
            switch (side)
            {
                case 0: return new Vector2Int(2 + rng.Next(cols - 3), 0);
                case 1: return new Vector2Int(cols, 2 + rng.Next(rows - 3));
                case 2: return new Vector2Int(2 + rng.Next(cols - 3), rows);
                default: return new Vector2Int(0, 2 + rng.Next(rows - 3));
            }
        }

        Vector2Int Inward(Vector2Int p) => new Vector2Int(Mathf.Clamp(p.x, 1, cols - 1), Mathf.Clamp(p.y, 1, rows - 1));

        static void Stair(List<Vector2Int> path, Vector2Int to, System.Random rng)
        {
            var p = path[path.Count - 1];
            while (p != to)
            {
                int dx = System.Math.Sign(to.x - p.x), dy = System.Math.Sign(to.y - p.y);
                if (dx != 0 && dy != 0) { if (rng.Next(2) == 0) dy = 0; else dx = 0; }
                p = new Vector2Int(p.x + dx, p.y + dy);
                path.Add(p);
            }
        }

        int StepEdge(Vector2Int p, Vector2Int q)
        {
            if (p.y == q.y) { int x = Mathf.Min(p.x, q.x); return Key((p.y - 1) * cols + x, p.y * cols + x); }
            int y = Mathf.Min(p.y, q.y);
            return Key(y * cols + p.x - 1, y * cols + p.x);
        }

        void Split(List<Vector2Int> path)
        {
            cuts.Clear();
            for (int i = 0; i + 1 < path.Count; i++) cuts.Add(StepEdge(path[i], path[i + 1]));
            sideA = new bool[rows * cols];
            var queue = new Queue<int>();
            sideA[0] = true; queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue(), r = u / cols, c = u % cols;
                foreach (int v in new[] { c > 0 ? u - 1 : -1, c + 1 < cols ? u + 1 : -1, r > 0 ? u - cols : -1, r + 1 < rows ? u + cols : -1 })
                    if (v >= 0 && !sideA[v] && !Cut(u, v)) { sideA[v] = true; queue.Enqueue(v); }
            }
        }

        void GenerateRiver(System.Random rng)
        {
            int n = rows * cols, bestGap = int.MaxValue;
            var best = Enumerable.Range(0, rows + 1).Select(y => new Vector2Int(cols / 2, y)).ToList();
            for (int attempt = 0; attempt < 80; attempt++)
            {
                int s0 = rng.Next(4), s1 = (s0 + 1 + rng.Next(3)) % 4;
                var a = Entry(s0, rng);
                var b = Entry(s1, rng);
                var mid = new Vector2Int(2 + rng.Next(cols - 3), 2 + rng.Next(rows - 3));
                var path = new List<Vector2Int> { a, Inward(a) };
                Stair(path, mid, rng);
                Stair(path, Inward(b), rng);
                path.Add(b);
                if (path.Distinct().Count() != path.Count) continue;
                Split(path);
                int gap = Mathf.Abs(sideA.Count(x => x) * 2 - n);
                if (gap < bestGap) { bestGap = gap; best = path; }
                if (gap * 10 <= n) break;
            }
            Split(best);
            river.Clear(); river.AddRange(best);
            riverSet.Clear(); foreach (var d in best) riverSet.Add(d);
        }

        Edge AddEdge(int a, int b, bool bridge)
        {
            var old = FindEdge(a, b);
            if (old != null) return old;
            var e = new Edge { id = edges.Count, a = a, b = b, bridge = bridge, length = Vector2.Distance(nodes[a].pos, nodes[b].pos) };
            edges.Add(e);
            nodes[a].edges.Add(e.id);
            nodes[b].edges.Add(e.id);
            return e;
        }

        void RemoveEdge(Edge e)
        {
            nodes[e.a].edges.Remove(e.id);
            nodes[e.b].edges.Remove(e.id);
            edges.Remove(e);

            foreach (var n in nodes) n.edges.Clear();
            for (int i = 0; i < edges.Count; i++)
            {
                edges[i].id = i;
                nodes[edges[i].a].edges.Add(i);
                nodes[edges[i].b].edges.Add(i);
            }
        }

        void AssignTypes(System.Random rng)
        {
            int Id(int r, int c) => r * cols + c;

            var seeds = new[] { Id(rows - 1, 0), Id(0, cols - 1) };
            foreach (int s in seeds)
            {
                nodes[s].infection = 1f;
                foreach (int e in nodes[s].edges) nodes[edges[e].Other(s)].infection = 0.5f;
            }

            bool Free(Node n) => n.type == NodeType.Road && n.infection == 0f;
            var left = nodes.Where(n => Free(n) && West(n.id)).OrderBy(_ => rng.Next()).ToList();
            var right = nodes.Where(n => Free(n) && !West(n.id)).OrderBy(_ => rng.Next()).ToList();

            void Take(List<Node> pool, NodeType t, int count)
            {
                for (int i = 0; i < count && pool.Count > 0; i++) { pool[0].type = t; pool.RemoveAt(0); }
            }

            int extra = Mathf.Max(0, Mathf.RoundToInt((rows * cols / 48f - 1f) * 2f));
            Take(left, NodeType.Shelter, 2 + extra / 2); Take(left, NodeType.Hospital, 1); Take(left, NodeType.Supply, 2); Take(left, NodeType.Factory, 1 + (extra + 1) / 2); Take(left, NodeType.Generator, 1);
            Take(right, NodeType.Shelter, 1 + extra / 2); Take(right, NodeType.Hospital, 1); Take(right, NodeType.Supply, 1 + extra / 2); Take(right, NodeType.Factory, 2 + (extra + 1) / 2); Take(right, NodeType.Generator, 1);
            Take(rng.Next(2) == 0 ? left : right, NodeType.Armory, 1);

            foreach (var n in nodes) if (n.type == NodeType.Supply) n.medkits = 3;

            Vector2 center = Size * 0.5f;
            startNode = nodes.Where(n => n.type == NodeType.Shelter).OrderBy(n => Vector2.Distance(n.pos, center)).First().id;
        }

        bool Reachable()
        {
            var seen = new bool[nodes.Count];
            var queue = new Queue<int>();
            seen[0] = true; queue.Enqueue(0);
            int count = 1;
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                foreach (int id in nodes[u].edges)
                {
                    var e = edges[id];
                    if (e.blocked) continue;
                    int v = e.Other(u);
                    if (seen[v]) continue;
                    seen[v] = true; count++; queue.Enqueue(v);
                }
            }
            return count == nodes.Count;
        }

        public bool Connected()
        {
            for (int i = 1; i < nodes.Count; i++) if (dist[0, i] >= Inf) return false;
            return true;
        }

        public void Recompute()
        {
            Floyd(out dist, out next);
        }

        void Floyd(out float[,] dist, out int[,] next)
        {
            int n = nodes.Count;
            dist = new float[n, n];
            next = new int[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) { dist[i, j] = i == j ? 0 : Inf; next[i, j] = i == j ? i : -1; }
            foreach (var e in edges)
            {
                if (e.blocked) continue;
                dist[e.a, e.b] = dist[e.b, e.a] = e.length;
                next[e.a, e.b] = e.b; next[e.b, e.a] = e.a;
            }
            for (int k = 0; k < n; k++)
                for (int i = 0; i < n; i++)
                {
                    if (dist[i, k] >= Inf) continue;
                    for (int j = 0; j < n; j++)
                    {
                        float d = dist[i, k] + dist[k, j];
                        if (d < dist[i, j]) { dist[i, j] = d; next[i, j] = next[i, k]; }
                    }
                }
        }

        public List<int> Path(int from, int to) => Walk(next, from, to);

        static List<int> Walk(int[,] next, int from, int to)
        {
            if (next[from, to] < 0) return null;
            var path = new List<int>();
            int u = from;
            while (u != to) { u = next[u, to]; path.Add(u); }
            return path;
        }
    }
}

