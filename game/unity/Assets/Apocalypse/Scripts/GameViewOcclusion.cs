using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        class OccGroup { public Renderer[] rs; public Material[][] orig, ghost; public Color[] ghostColor; public Transform root; public int[] order; public Bounds b; public Vector2 xz; public bool faded, decor, hidden; public float until, alpha = 1f; }
        readonly List<OccGroup> occGroups = new List<OccGroup>();
        static readonly Dictionary<int, Material> ghostCache = new Dictionary<int, Material>();
        static Shader ghostShader;
        static Material ghostDepth;
        static MaterialPropertyBlock mpb;
        readonly List<Vector3> occlusionTargets = new List<Vector3>();

        Material GhostMat(Color c)
        {
            int k = ColorKey(c);
            if (ghostCache.TryGetValue(k, out var m) && m != null) return m;
            if (ghostShader == null) ghostShader = Shader.Find("Hidden/ApocalypseGhost");
            if (ghostDepth == null)
            {
                var ds = Shader.Find("Hidden/ApocalypseGhostDepth");
                if (ds != null && ds.isSupported) ghostDepth = new Material(ds);
            }
            var lit = Color.Lerp(c, new Color(0.78f, 0.86f, 1f), 0.45f);
            var tint = new Color(lit.r, lit.g, lit.b, 0.3f);
            m = ghostShader != null && ghostShader.isSupported ? new Material(ghostShader) { color = tint } : new Material(lineMat) { color = tint };
            ghostCache[k] = m;
            return m;
        }

        Transform GroupRoot(Transform t)
        {
            while (t.parent != null && t.parent != mapRoot && t.parent != decorStatic && t.parent != decorAnim) t = t.parent;
            return t;
        }

        void CollectOccluders()
        {
            occGroups.Clear();
            var seen = new HashSet<Transform>();
            foreach (var r in mapRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.bounds.max.y < 0.75f || r.sharedMaterial == null) continue;
                var root = GroupRoot(r.transform);
                if (root.name == "zoneWall" || !seen.Add(root)) continue;
                var rs = root.GetComponentsInChildren<MeshRenderer>(true).Where(x => x.sharedMaterial != null).ToArray();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var x in rs) b.Encapsulate(x.bounds);
                occGroups.Add(new OccGroup
                {
                    rs = rs, orig = rs.Select(x => x.sharedMaterials).ToArray(), order = rs.Select(x => x.sortingOrder).ToArray(),

                    ghost = rs.Select(x => { var gm = GhostMat(x.sharedMaterial.color); return ghostDepth != null ? new[] { ghostDepth, gm } : new[] { gm }; }).ToArray(),
                    ghostColor = rs.Select(x => GhostMat(x.sharedMaterial.color).color).ToArray(),
                    b = b, xz = new Vector2(b.center.x, b.center.z),
                    decor = root.parent == decorStatic || root.parent == decorAnim, root = root,
                });
            }
        }

        // 鏡頭拉越近，一般建築越透明，近到一定程度就完全看不到。設施、橋、直升機坪不受影響。
        const float FadeOpaqueSize = 15f, FadeGoneSize = 6f;

        void UpdateOcclusion()
        {
            if (galleryMode) { UpdateGalleryOcclusion(); return; }
            float a = galleryNoFade ? 1f : Mathf.InverseLerp(FadeGoneSize, FadeOpaqueSize, cam.orthographicSize);
            if (mpb == null) mpb = new MaterialPropertyBlock();
            foreach (var g in occGroups)
            {
                float want = g.decor ? a : 1f;
                bool ghost = want < 0.999f, hidden = want < 0.02f;
                if (ghost != g.faded)
                {
                    for (int i = 0; i < g.rs.Length; i++)
                        if (g.rs[i] != null)
                        {
                            g.rs[i].sharedMaterials = ghost ? g.ghost[i] : g.orig[i];
                            g.rs[i].sortingOrder = ghost ? 100 : g.order[i];
                            if (!ghost) g.rs[i].SetPropertyBlock(null);
                        }
                    g.faded = ghost;
                    g.alpha = -1f;
                }
                if (hidden != g.hidden)
                {
                    foreach (var r in g.rs) if (r != null) r.forceRenderingOff = hidden;
                    g.hidden = hidden;
                }
                if (ghost && !hidden && Mathf.Abs(want - g.alpha) > 0.01f)
                {
                    g.alpha = want;
                    for (int i = 0; i < g.rs.Length; i++)
                    {
                        if (g.rs[i] == null) continue;
                        var c = g.ghostColor[i];
                        mpb.Clear();
                        mpb.SetColor("_Color", new Color(c.r, c.g, c.b, Mathf.Lerp(0.05f, 0.85f, want)));
                        g.rs[i].SetPropertyBlock(mpb);
                    }
                }
            }
        }

        void UpdateGalleryOcclusion()
        {
            float now = Time.unscaledTime;
            bool close = !galleryNoFade && cam.orthographicSize < 8.5f;
            occlusionTargets.Clear();
            var back = -cam.transform.forward;
            if (galleryFocus.HasValue)
            {
                close = true;
                var f = galleryFocus.Value;
                for (int i = 0; i < 6; i++)
                    for (float h = 0.3f; h <= 2.4f; h += 0.7f)
                        occlusionTargets.Add(W(f + new Vector2(Mathf.Cos(i * 1.05f), Mathf.Sin(i * 1.05f)) * 0.9f, h));
            }
            else if (close)
            {

                var fwd = cam.transform.forward;
                var center = cam.transform.position + fwd * (-cam.transform.position.y / Mathf.Min(-0.01f, fwd.y));
                var right = cam.transform.right; var up = cam.transform.up;
                var r2 = new Vector3(right.x, 0, right.z).normalized;
                var u2 = new Vector3(up.x, 0, up.z).normalized;
                float hw = cam.orthographicSize * cam.aspect * 0.7f, hh = cam.orthographicSize * 0.7f / Mathf.Sin(Pitch * Mathf.Deg2Rad);
                const int nx = 9, ny = 7;
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                    {
                        float fx = i / (nx - 1f) * 2f - 1f, fy = j / (ny - 1f) * 2f - 1f;
                        occlusionTargets.Add(center + r2 * (fx * hw) + u2 * (fy * hh) + Vector3.up * 0.3f);
                    }
            }
            foreach (var g in occGroups)
            {
                if (close && !(galleryFocus.HasValue && Vector2.Distance(g.xz, galleryFocus.Value) < 1.6f))
                    foreach (var tp in occlusionTargets)
                    {
                        float dx = g.xz.x - tp.x, dz = g.xz.y - tp.z;
                        if (dx * dx + dz * dz > 49f || dx * back.x + dz * back.z < -1.5f) continue;
                        if (g.b.IntersectRay(new Ray(tp, back))) { g.until = now + 0.35f; break; }
                    }
                bool want = close && now < g.until;
                // 拍撤離點時：擋住的樹、房子、別的設施直接藏起來，撤離點自己的東西不動
                bool hide = want && galleryHide;
                if (galleryHide && !GalleryHideable(g)) want = hide = false;
                bool ghost = want && !hide;
                if (ghost != g.faded)
                {
                    for (int i = 0; i < g.rs.Length; i++)
                        if (g.rs[i] != null)
                        {
                            g.rs[i].sharedMaterials = ghost ? g.ghost[i] : g.orig[i];
                            g.rs[i].sortingOrder = ghost ? 100 : g.order[i];
                        }
                    g.faded = ghost;
                }
                if (hide != g.hidden)
                {
                    foreach (var r in g.rs) if (r != null) r.forceRenderingOff = hide;
                    g.hidden = hide;
                }
            }
        }

        bool galleryHide;

        bool GalleryHideable(OccGroup g)
        {
            if (g.decor || g.root == null) return true;
            string n = g.root.name;
            return n == "marker" || (n.StartsWith("node") && n != "node" + Sim.extraction);
        }

        void TintPlayer(PlayerView v, Player p, float t)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            float dt = Time.unscaledDeltaTime;
            if (p.state == PlayerState.Active && p.hp < v.lastHp - 0.01f)
            {
                v.flash = 1f;
                if (t - v.lastHitFx > 0.6f)
                {
                    v.lastHitFx = t;
                    Burst(W(Shown(p), 0.8f), new Color(1f, 0.2f, 0.15f, 1f), new Color(0.7f, 0.05f, 0.05f, 0.8f), 10, 1.6f, 0.1f, 0.5f, 0.8f, 0.2f);
                }
            }
            v.lastHp = p.hp;
            v.flash = Mathf.Max(0f, v.flash - dt * 3f);
            bool downedBlink = p.state == PlayerState.Downed && Mathf.Sin(t * 10f) < 0;
            for (int i = 0; i < v.parts.Length; i++)
            {
                if (v.parts[i] == null) continue;
                var c = v.baseColors[i];
                if (downedBlink) c = Color.Lerp(c, Color.white, 0.7f);
                else if (v.flash > 0f) c = Color.Lerp(c, new Color(1f, 0.12f, 0.1f), v.flash * 0.85f);
                v.parts[i].GetPropertyBlock(mpb);
                mpb.SetColor("_Color", c);
                v.parts[i].SetPropertyBlock(mpb);
            }
        }
    }
}

