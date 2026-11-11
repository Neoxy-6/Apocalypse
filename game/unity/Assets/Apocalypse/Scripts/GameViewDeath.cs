using System.Collections.Generic;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        static Sprite swirlSprite;

        static Sprite SwirlSprite()
        {
            if (swirlSprite != null) return swirlSprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy) / 30f;
                    float a = Mathf.Atan2(dy, dx) / (2f * Mathf.PI);
                    float band = Mathf.Repeat(a + r * 2.2f, 1f);
                    bool ink = band < 0.42f || r > 0.88f;
                    float alpha = Mathf.Clamp01((1f - r) * 30f);
                    px[y * n + x] = ink ? new Color(0.08f, 0.06f, 0.06f, alpha) : new Color(1f, 1f, 1f, alpha);
                }
            tex.SetPixels(px);
            tex.Apply();
            swirlSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            return swirlSprite;
        }

        (SpriteRenderer, SpriteRenderer) MakeSwirlEyes(Transform head)
        {
            SpriteRenderer Make(bool left)
            {
                var go = new GameObject("swirl");
                go.transform.SetParent(head, false);
                go.transform.localPosition = CapyEye(left) + new Vector3((left ? -1f : 1f) * (Capy().eyeOut + 0.01f), 0, 0);
                go.transform.localRotation = Quaternion.Euler(0, left ? 90f : -90f, 0);
                go.transform.localScale = Vector3.one * 0.13f;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SwirlSprite();
                sr.sortingOrder = 12;
                sr.enabled = false;
                return sr;
            }
            return (Make(true), Make(false));
        }

        static void SetSwirl(Renderer eyeL, Renderer eyeR, SpriteRenderer sl, SpriteRenderer sr, bool on, float t)
        {
            if (eyeL) eyeL.enabled = !on;
            if (eyeR) eyeR.enabled = !on;
            if (sl) { sl.enabled = on; if (on) sl.transform.localRotation = Quaternion.Euler(0, 0, -t * 400f); }
            if (sr) { sr.enabled = on; if (on) sr.transform.localRotation = Quaternion.Euler(0, 0, -t * 400f + 90f); }
        }

        readonly List<(ZombieView v, float start)> dyingZombies = new List<(ZombieView, float)>();

        void StartZombieDeath(ZombieView v)
        {
            if (v.ring) Destroy(v.ring.gameObject);
            if (v.intent) Destroy(v.intent.gameObject);
            if (!v.body) return;
            if (Sim.zoneClearedAt >= 0f && Sim.time - Sim.zoneClearedAt < 0.3f)
            {
                // 被守軍清圈：化成金色光點散開
                var at = v.body.transform.position;
                Burst(at + Vector3.up * 0.4f, new Color(1f, 0.95f, 0.6f, 1f), new Color(1f, 0.8f, 0.3f, 1f), 26, 1.4f, 0.2f, 1.3f, -0.9f, 0.3f);
            }
            Burst(v.body.transform.position + Vector3.up * 0.5f, new Color(0.5f, 0.6f, 0.4f, 0.8f), new Color(0.3f, 0.35f, 0.3f, 0.6f), 14, 1.2f, 0.3f, 0.8f, 0.2f, 0.25f);
            dyingZombies.Add((v, Time.unscaledTime));
        }

        void UpdateDyingZombies(float t)
        {
            float now = Time.unscaledTime;
            for (int i = dyingZombies.Count - 1; i >= 0; i--)
            {
                var (v, start) = dyingZombies[i];
                float k = now - start;
                if (!v.body || k > 1.8f)
                {
                    if (v.body) Destroy(v.body);
                    dyingZombies.RemoveAt(i);
                    continue;
                }
                v.model.localRotation = v.facing * Quaternion.Euler(-Mathf.Min(1f, k / 0.35f) * 88f, 0, 0);
                v.model.localPosition = new Vector3(0, 0.25f * Mathf.Min(1f, k / 0.35f), 0);
                SetSwirl(v.eyeL, v.eyeR, v.swirlL, v.swirlR, true, t);
                if (k > 0.9f)
                {
                    float s = Mathf.Clamp01(1f - (k - 0.9f) / 0.9f);
                    v.body.transform.localScale = Vector3.one * 1.2f * s;
                    var p = v.body.transform.position;
                    v.body.transform.position = new Vector3(p.x, -(1f - s) * 0.4f, p.z);
                }
            }
        }

        bool AnimatePlayerDeath(PlayerView v, Player p, float t)
        {
            float now = Time.unscaledTime;
            if (v.deadAt < 0)
            {
                v.deadAt = now;
                Burst(W(Shown(p), 0.6f), new Color(0.6f, 0.55f, 0.5f, 0.8f), new Color(0.3f, 0.3f, 0.3f, 0.6f), 18, 1.4f, 0.35f, 1f, 0.2f, 0.3f);
            }
            float k = now - v.deadAt;
            if (k > 2.4f) return false;
            v.model.localPosition = new Vector3(0, 0.3f, 0);
            v.model.localRotation = v.facing * Quaternion.Euler(0, 0, 90f);
            v.armL.localRotation = Quaternion.Euler(-40f, 0, 0);
            v.armR.localRotation = Quaternion.Euler(-40f, 0, 0);
            SetSwirl(v.eyeL, v.eyeR, v.swirlL, v.swirlR, true, t);
            float s = k > 1.4f ? Mathf.Clamp01(1f - (k - 1.4f)) : 1f;
            v.body.transform.position = W(Shown(p), GroundH(Shown(p)) - (1f - s) * 0.4f);
            v.body.transform.localScale = Vector3.one * 1.3f * s;
            return true;
        }

        Transform guardRoot, guardModel, guardMuzzle;
        Renderer guardFlash;
        LineRenderer guardTracer;
        Vector2 guardTower, guardAim;
        float guardAimUntil = -1f, guardLastShotSim = -1f, guardLastShotReal = -9f, guardBaseYaw;

        void BuildGuard(Vector2 tower, Vector2 pad)
        {
            guardTower = tower;
            guardLastShotSim = -1f;
            guardRoot = Group(mapRoot, tower, 0f, 1.43f);
            guardModel = new GameObject("soldier").transform;
            guardModel.SetParent(guardRoot, false);
            guardModel.localScale = Vector3.one * 0.8f;
            var olive = new Color(0.36f, 0.42f, 0.28f);
            PlushBody(guardModel, Color.white, olive, false, out var head, out var armL, out var armR, out _, out _, out _, out _);
            var top = CapyHeadTop;
            Part(head, PrimitiveType.Sphere, olive * 0.9f, top + new Vector3(0, -0.02f, -0.02f), new Vector3(0.42f, 0.16f, 0.4f));
            Part(head, PrimitiveType.Cube, olive * 0.8f, top + new Vector3(0, -0.04f, 0.16f), new Vector3(0.34f, 0.03f, 0.16f));
            armL.localRotation = Quaternion.Euler(-40f, 0, 0);
            armR.localRotation = Quaternion.Euler(-40f, 0, 0);
            var gun = Part(guardModel, PrimitiveType.Cube, new Color(0.15f, 0.15f, 0.16f), new Vector3(0, 0.5f, 0.5f), new Vector3(0.08f, 0.1f, 0.6f));
            Part(guardModel, PrimitiveType.Cube, new Color(0.3f, 0.22f, 0.15f), new Vector3(0, 0.42f, 0.28f), new Vector3(0.07f, 0.16f, 0.12f));
            guardMuzzle = new GameObject("muzzle").transform;
            guardMuzzle.SetParent(guardModel, false);
            guardMuzzle.localPosition = new Vector3(0, 0.51f, 0.83f);
            guardFlash = Part(guardMuzzle, PrimitiveType.Sphere, new Color(1f, 0.85f, 0.4f), Vector3.zero, Vector3.one * 0.35f, default, true).GetComponent<Renderer>();
            guardFlash.enabled = false;
            guardTracer = NewLine(mapRoot, 0.05f, 9);
            guardTracer.startColor = new Color(1f, 0.95f, 0.6f, 1f);
            guardTracer.endColor = new Color(1f, 0.7f, 0.3f, 0.6f);
            guardTracer.enabled = false;
            guardTracer.alignment = LineAlignment.View;
            var toPad = pad - tower;
            guardBaseYaw = Mathf.Atan2(toPad.x, toPad.y) * Mathf.Rad2Deg + 180f;
        }

        void UpdateGuard(float t)
        {
            if (guardRoot == null) return;
            float now = Time.unscaledTime;
            if (Sim.time < guardLastShotSim) guardLastShotSim = -1f;
            foreach (var s in Sim.guardShots)
            {
                if (s.z <= guardLastShotSim) continue;
                guardLastShotSim = s.z;
                guardAim = new Vector2(s.x, s.y);
                guardAimUntil = now + 0.9f;
                guardLastShotReal = now;
                Burst(W(guardAim, 0.5f), new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.5f, 0.2f, 0.8f), 12, 2.5f, 0.08f, 0.35f, 0.5f, 0.1f);
            }
            bool down = Sim.time < Sim.guardDownUntil;
            Quaternion want;
            if (now < guardAimUntil)
            {
                var d = guardAim - guardTower;
                want = Quaternion.LookRotation(new Vector3(d.x, 0, d.y));
            }
            else want = Quaternion.Euler(0, guardBaseYaw + Mathf.Sin(t * 0.6f) * 70f, 0);
            guardRoot.rotation = Quaternion.Slerp(guardRoot.rotation, want, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            float since = now - guardLastShotReal;
            guardModel.localPosition = new Vector3(0, 0, -0.08f * Mathf.Clamp01(1f - since * 8f));
            guardModel.localRotation = down ? Quaternion.Euler(70f, 0, 0) : Quaternion.Euler(-6f * Mathf.Clamp01(1f - since * 6f), 0, 0);
            guardFlash.enabled = since < 0.07f;
            guardTracer.enabled = since < 0.1f;
            if (guardTracer.enabled)
            {
                guardTracer.SetPosition(0, guardMuzzle.position);
                guardTracer.SetPosition(1, W(guardAim, 0.5f));
            }
        }
    }
}
