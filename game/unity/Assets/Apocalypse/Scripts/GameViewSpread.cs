using System.Collections.Generic;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        const long ZombieKey = 1_000_000L;
        readonly Dictionary<long, Vector2> shownPos = new Dictionary<long, Vector2>();
        readonly HashSet<long> spreadLive = new HashSet<long>();
        readonly List<long> spreadDead = new List<long>();
        readonly List<Vector2> zombieSpots = new List<Vector2>();

        Vector2 Shown(Player p) => shownPos.TryGetValue(p.id, out var v) ? v : p.pos;
        Vector2 Shown(Zombie z) => shownPos.TryGetValue(ZombieKey + z.id, out var v) ? v : z.pos;

        static float Hash01(long key, int salt)
        {
            unchecked
            {
                ulong h = (ulong)(key * 2654435761L) ^ (ulong)(salt * 40503L + 0x9E3779B9L);
                h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        Vector2 StandSpot(int node, int next, Vector2 pos, long key)
        {
            var map = Sim.map;
            if (next >= 0 && node >= 0)
            {
                var dir = map.nodes[next].pos - map.nodes[node].pos;
                if (dir.sqrMagnitude > 1e-6f)
                {
                    dir.Normalize();
                    float lane = (Hash01(key, 1) - 0.5f) * 0.62f;
                    float ahead = key >= ZombieKey ? (Hash01(key, 4) - 0.5f) * 0.7f : 0f;
                    return pos + new Vector2(-dir.y, dir.x) * lane + dir * ahead;
                }
            }
            float a = Hash01(key, 2) * Mathf.PI * 2f, r = 0.12f + Hash01(key, 3) * 0.5f;
            return pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        void UpdateSpread(float dt)
        {
            float k = 1f - Mathf.Exp(-8f * dt);
            spreadLive.Clear();
            foreach (var p in Sim.players)
            {
                if (p.state == PlayerState.Eliminated) continue;
                Place(p.id, StandSpot(p.node, p.nextNode, p.pos, p.id), p.pos, k);
            }

            zombieSpots.Clear();
            foreach (var z in Sim.zombies) zombieSpots.Add(StandSpot(z.node, z.nextNode, z.pos, ZombieKey + z.id));
            const float minGap = 0.38f;
            for (int i = 0; i < zombieSpots.Count; i++)
                for (int j = i + 1; j < zombieSpots.Count; j++)
                {
                    var d = zombieSpots[j] - zombieSpots[i];
                    float m = d.magnitude;
                    if (m >= minGap) continue;
                    var n = m > 1e-4f ? d / m : new Vector2(Mathf.Cos(i * 2.4f + j), Mathf.Sin(i * 2.4f + j));
                    var push = n * (minGap - m) * 0.5f;
                    zombieSpots[i] -= push; zombieSpots[j] += push;
                }
            float kz = 1f - Mathf.Exp(-4f * dt);
            for (int i = 0; i < Sim.zombies.Count; i++)
            {
                var z = Sim.zombies[i];
                Place(ZombieKey + z.id, zombieSpots[i], z.pos, kz);
            }
            spreadDead.Clear();
            foreach (var key in shownPos.Keys) if (!spreadLive.Contains(key)) spreadDead.Add(key);
            foreach (var key in spreadDead) shownPos.Remove(key);
        }

        void Place(long key, Vector2 spot, Vector2 truePos, float k)
        {
            spreadLive.Add(key);

            shownPos[key] = shownPos.TryGetValue(key, out var prev) && (prev - truePos).sqrMagnitude < 4f ? Vector2.Lerp(prev, spot, k) : spot;
        }
    }
}
