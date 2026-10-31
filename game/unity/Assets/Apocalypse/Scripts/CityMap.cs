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
        public readonly int riverCol;

        public float Scale => nodes.Count / 48f;

        public CityMap(int cols = 8, int rows = 6, int bridges = 3)
        {
            this.cols = Mathf.Max(4, cols);
            this.rows = Mathf.Max(3, rows);
            bridgeCount = Mathf.Clamp(bridges, 1, this.rows);
            riverCol = this.cols / 2 - 1;
        }
        public float RiverX => (riverCol + 0.5f) * spacing;
        public Vector2 Size => new Vector2((cols - 1) * spacing, (rows - 1) * spacing);

        public List<Node> nodes = new List<Node>();
        public List<Edge> edges = new List<Edge>();
        public float[,] dist;
        public int[,] next;
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
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    float jx = (float)(rng.NextDouble() - 0.5) * 1.6f;
                    float jy = (float)(rng.NextDouble() - 0.5) * 1.6f;
                    nodes.Add(new Node { id = r * cols + c, pos = new Vector2(c * spacing + jx, r * spacing + jy) });
                }

            var bridgeRows = Enumerable.Range(0, rows).OrderBy(_ => rng.Next()).Take(bridgeCount).ToList();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int id = r * cols + c;
                    if (c + 1 < cols)
                    {
                        if (c != riverCol) AddEdge(id, id + 1, false);
                        else if (bridgeRows.Contains(r)) AddEdge(id, id + 1, true);
                    }
                    if (r + 1 < rows) AddEdge(id, id + cols, false);
                }

            for (int i = 0; i < cols * rows / 7; i++)
            {
                int c = rng.Next(cols - 1), r = rng.Next(rows - 1);
                if (c == riverCol) continue;
                if (rng.Next(2) == 0) AddEdge(r * cols + c, (r + 1) * cols + c + 1, false);
                else AddEdge(r * cols + c + 1, (r + 1) * cols + c, false);
            }

            var candidates = edges.Where(e => !e.bridge).OrderBy(_ => rng.Next()).Take(edges.Count / 8).ToList();
            foreach (var e in candidates)
            {
                if (nodes[e.a].edges.Count <= 2 || nodes[e.b].edges.Count <= 2) continue;
                e.blocked = true;
                Recompute();
                if (!Connected()) { e.blocked = false; continue; }
                RemoveEdge(e);
            }

            AssignTypes(rng);
            Recompute();
        }

        void AddEdge(int a, int b, bool bridge)
        {
            if (FindEdge(a, b) != null) return;
            var e = new Edge { id = edges.Count, a = a, b = b, bridge = bridge, length = Vector2.Distance(nodes[a].pos, nodes[b].pos) };
            edges.Add(e);
            nodes[a].edges.Add(e.id);
            nodes[b].edges.Add(e.id);
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
            var left = nodes.Where(n => Free(n) && Mathf.RoundToInt(n.id % cols) <= riverCol).OrderBy(_ => rng.Next()).ToList();
            var right = nodes.Where(n => Free(n) && Mathf.RoundToInt(n.id % cols) > riverCol).OrderBy(_ => rng.Next()).ToList();

            void Take(List<Node> pool, NodeType t, int count)
            {
                for (int i = 0; i < count && pool.Count > 0; i++) { pool[0].type = t; pool.RemoveAt(0); }
            }

            int extra = Mathf.Max(0, Mathf.RoundToInt((rows * cols / 48f - 1f) * 2f));
            Take(left, NodeType.Shelter, 2 + extra / 2); Take(left, NodeType.Hospital, 1); Take(left, NodeType.Supply, 2); Take(left, NodeType.Factory, 1 + (extra + 1) / 2); Take(left, NodeType.Generator, 1);
            Take(right, NodeType.Shelter, 1 + extra / 2); Take(right, NodeType.Hospital, 1); Take(right, NodeType.Supply, 1 + extra / 2); Take(right, NodeType.Factory, 2 + (extra + 1) / 2); Take(right, NodeType.Generator, 1);

            foreach (var n in nodes) if (n.type == NodeType.Supply) n.medkits = 3;

            Vector2 center = Size * 0.5f;
            startNode = nodes.Where(n => n.type == NodeType.Shelter).OrderBy(n => Vector2.Distance(n.pos, center)).First().id;
        }

        public bool Connected()
        {
            for (int i = 1; i < nodes.Count; i++) if (dist[0, i] >= Inf) return false;
            return true;
        }

        public void Recompute()
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

        public List<int> Path(int from, int to)
        {
            if (next[from, to] < 0) return null;
            var path = new List<int>();
            int u = from;
            while (u != to) { u = next[u, to]; path.Add(u); }
            return path;
        }
    }
}
