using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        class Follower { public GameObject go; public Vector3 pos, last; public float phase; public Quaternion facing = Quaternion.identity; }
        readonly Dictionary<int, Follower> followers = new Dictionary<int, Follower>();
        readonly HashSet<int> seenFollowers = new HashSet<int>();
        static readonly Color[] FollowerShirts =
        {
            new Color(0.85f, 0.75f, 0.6f), new Color(0.6f, 0.75f, 0.9f), new Color(0.9f, 0.6f, 0.6f), new Color(0.7f, 0.85f, 0.6f), new Color(0.9f, 0.85f, 0.5f),
        };
        const float TrailStep = 0.18f;
        const int TrailGap = 4;

        void UpdateFollowers(PlayerView v, Player p, bool alive, float t, HashSet<int> seen)
        {
            var here = W(p.pos, 0f);
            if (v.trail.Count > 0 && (v.trail[0] - here).sqrMagnitude > 4f) v.trail.Clear();
            if (v.trail.Count == 0 || (v.trail[0] - here).sqrMagnitude > TrailStep * TrailStep)
            {
                v.trail.Insert(0, here);
                if (v.trail.Count > 60) v.trail.RemoveAt(v.trail.Count - 1);
            }
            if (!alive || p.state != PlayerState.Active) return;
            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime) * Mathf.Max(1f, gm.speed);
            int k = 0;
            foreach (int id in p.cargo)
            {
                var task = Sim.TaskById(id);
                if (task == null || task.type != TaskType.Rescue) continue;
                seen.Add(id);
                int idx = (k + 1) * TrailGap;
                Vector3 target = idx < v.trail.Count ? v.trail[idx]
                               : here - v.facing * Vector3.forward * (0.7f * (k + 1));
                if (!followers.TryGetValue(id, out var f))
                {
                    var go = MiniCapy(dynRoot, FollowerShirts[id % FollowerShirts.Length]);
                    followers[id] = f = new Follower { go = go, pos = here, last = here, phase = id * 1.7f };
                    Burst(W(Shown(p), 0.3f), new Color(0.5f, 1f, 0.6f, 1f), Color.white, 12, 1.2f, 0.1f, 0.6f, -0.3f, 0.2f);
                }
                f.pos = Vector3.Lerp(f.pos, target, Mathf.Clamp01(dt * 5f));
                var move = f.pos - f.last;
                float speed = move.magnitude / Mathf.Max(1e-4f, dt);
                bool walking = speed > 0.3f;
                f.last = f.pos;
                var tr = f.go.transform;
                tr.position = f.pos + Vector3.up * (GroundH(new Vector2(f.pos.x, f.pos.z)) + (walking ? Mathf.Abs(Mathf.Sin(t * 11f + f.phase)) * 0.05f : 0f));
                var look = walking ? new Vector3(move.x, 0, move.z) : here - f.pos;
                look.y = 0;
                if (look.sqrMagnitude > 1e-5f)
                {
                    f.facing = Quaternion.Slerp(f.facing, Quaternion.LookRotation(look.normalized), Mathf.Clamp01(dt * 4f));
                    tr.rotation = f.facing * Quaternion.Euler(0, 0, walking ? Mathf.Sin(t * 11f + f.phase) * 2.5f : 0f);
                }
                tr.localScale = Vector3.one * 1.05f;
                k++;
            }
        }

        void RemoveFollowers(HashSet<int> seen, float t)
        {
            foreach (int id in followers.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                var f = followers[id];
                if (Sim.TaskById(id)?.status == TaskStatus.Done)
                    Burst(f.pos + Vector3.up * 0.3f, new Color(0.5f, 1f, 0.6f, 1f), Color.white, 18, 1.5f, 0.12f, 0.8f, -0.4f, 0.2f);
                Destroy(f.go);
                followers.Remove(id);
            }
        }
    }
}
