using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {

        float shotAge, shakeAmp, lastCut = -10f;
        int shotCount, lastPicked = -1, lastFocusId = -1;
        bool overviewShot;
        Vector2 lastFocusPos, lead;
        Vector3? camBase;

        void NextShot()
        {
            shotAge = 0f; directorSpot = null; lastCut = Time.unscaledTime;
            shotCount++;
            if (shotCount % 3 == 0) { overviewShot = true; directorPick = -1; directorTimer = 4f; return; }
            overviewShot = false;
            directorPick = PickDirectorTarget();
            lastPicked = directorPick;
            directorTimer = directorPick >= 0 ? 7f : 4f;
        }

        void CutTo(Vector2 spot, float hold = 6f)
        {
            directorSpot = spot; directorPick = -1; directorTimer = hold;
            shotAge = 0f; overviewShot = false; lastCut = Time.unscaledTime;
        }

        void Shake(float amount) => shakeAmp = Mathf.Max(shakeAmp, amount);

        float DirectorScore(Player p)
        {
            float s = Sim.zombies.Count(z => Vector2.Distance(z.pos, p.pos) < 6f) * 1.5f;
            if (p.state == PlayerState.Downed) s += 6f;
            if (p.cargo.Count > 0) s += 0.8f;
            if (Sim.incidents.Any(i => i.status == "active" && Vector2.Distance(Sim.map.nodes[i.node].pos, p.pos) < 3f)) s += 2.5f;
            if (p.id == lastPicked) s -= 3f;
            s += Random.value * 1.5f;
            return s;
        }

        Vector2? manualTarget;
        int focusIncident = -1;

        void CycleStudent(int step)
        {
            var ids = Sim.Ranking().Select(t => t.units[0]).Where(u => u.state != PlayerState.Eliminated).Select(u => u.id).ToList();
            if (ids.Count == 0) return;
            int i = ids.IndexOf(follow);
            follow = ids[i < 0 ? (step > 0 ? 0 : ids.Count - 1) : (i + step + ids.Count) % ids.Count];
            director = false; manualTarget = null; focusIncident = -1;
        }

        void CycleIncident(int step)
        {
            var list = ActiveIncidents();
            if (list.Count == 0) return;
            int i = list.FindIndex(x => x.id == focusIncident);
            focusIncident = list[i < 0 ? (step > 0 ? 0 : list.Count - 1) : (i + step + list.Count) % list.Count].id;
            director = false; follow = -1; manualTarget = null;
            lastCut = Time.unscaledTime;
        }
        float manualZoom = 0.37f;
        Vector3 lastMouse;

        void PanCamera()
        {
            if (editingUrl && Sim.status == MatchStatus.Lobby) return;
            float dt = Time.unscaledDeltaTime;
            var dir = Vector2.zero;
            if (Input.GetKey(KeyCode.W)) dir.y += 1f;
            if (Input.GetKey(KeyCode.S)) dir.y -= 1f;
            if (Input.GetKey(KeyCode.A)) dir.x -= 1f;
            if (Input.GetKey(KeyCode.D)) dir.x += 1f;
            var mouse = Input.mousePosition;
            bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            bool pressed = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            Vector2 drag = held && !pressed ? (Vector2)(mouse - lastMouse) : Vector2.zero;
            lastMouse = mouse;
            if (dir == Vector2.zero && drag.sqrMagnitude < 0.01f) return;

            if (!manualTarget.HasValue)
            {
                var look = (camBase ?? cam.transform.position) + cam.transform.forward * 120f;
                manualTarget = new Vector2(look.x, look.z);
                manualZoom = cam.orthographicSize / overviewSize;
            }
            director = false; follow = -1; focusIncident = -1;
            var right = cam.transform.right; var up = cam.transform.up;
            var r2 = new Vector2(right.x, right.z).normalized;
            var u2 = new Vector2(up.x, up.z).normalized;
            float perPixel = cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);
            float tilt = 1f / Mathf.Sin(Pitch * Mathf.Deg2Rad);
            var move = (r2 * dir.normalized.x + u2 * dir.normalized.y * tilt) * cam.orthographicSize * 1.3f * dt
                     - (r2 * drag.x + u2 * drag.y * tilt) * perPixel;
            var size = Sim.map.Size;
            var t = manualTarget.Value + move;
            manualTarget = new Vector2(Mathf.Clamp(t.x, -3f, size.x + 3f), Mathf.Clamp(t.y, -3f, size.y + 3f));
            lastCut = Time.unscaledTime - 1f;
        }

        void ApplyCamera(Vector2 target, float size)
        {
            float dt = Time.unscaledDeltaTime;
            var desired = W(target) - cam.transform.forward * 120f;
            if (!camBase.HasValue) camBase = cam.transform.position;
            float rate = manualTarget.HasValue && follow < 0 && !director ? 12f : Time.unscaledTime - lastCut < 0.8f ? 7f : 2.5f;
            float k = galleryMode ? 1f : 1f - Mathf.Exp(-rate * dt);
            camBase = Vector3.Lerp(camBase.Value, desired, k);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, k);
            shakeAmp *= Mathf.Exp(-3.5f * dt);
            float t = Time.unscaledTime * 22f;
            var shake = (cam.transform.right * (Mathf.PerlinNoise(t, 0.3f) - 0.5f) + cam.transform.up * (Mathf.PerlinNoise(0.7f, t) - 0.5f)) * 2f * shakeAmp;
            cam.transform.position = camBase.Value + shake;
        }

        MeshRenderer zoneWall;
        Mesh zoneMesh;
        ParticleSystem zoneGas;
        const int WallSeg = 96;
        readonly Color[] wallColors = new Color[(WallSeg + 1) * 2];

        void BuildZoneWall()
        {
            var verts = new Vector3[(WallSeg + 1) * 2];
            var tris = new List<int>();
            for (int i = 0; i <= WallSeg; i++)
            {
                float a = i * Mathf.PI * 2f / WallSeg;
                verts[i * 2] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts[i * 2 + 1] = new Vector3(Mathf.Cos(a), 2.4f, Mathf.Sin(a));
                if (i > 0) { int b = (i - 1) * 2; tris.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
            }
            zoneMesh = new Mesh { vertices = verts };
            zoneMesh.SetTriangles(tris, 0);
            zoneMesh.colors = wallColors;
            zoneMesh.RecalculateBounds();
            var go = new GameObject("zoneWall");
            go.transform.SetParent(mapRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = zoneMesh;
            zoneWall = go.AddComponent<MeshRenderer>();
            zoneWall.sharedMaterial = UnlitMat(Color.white);
            zoneWall.sortingOrder = 7;

            zoneGas = Particles(mapRoot, Vector3.zero, new Color(0.75f, 0.2f, 0.35f, 0.45f), new Color(0.45f, 0.15f, 0.5f, 0.35f), 60f, 3.5f, 0.15f, 1.3f, 0f, Vector3.one, 0f, 0.3f, 7);
            var sh = zoneGas.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radiusThickness = 0f;
            sh.rotation = new Vector3(90f, 0, 0);
            sh.scale = Vector3.one;
            var vel = zoneGas.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(0.45f); vel.z = new ParticleSystem.MinMaxCurve(0f);
        }

        void UpdateZoneWall(Vector2 center, float r, bool shrinking, float t)
        {
            if (zoneWall == null) return;
            zoneWall.enabled = shrinking;
            var em = zoneGas.emission;
            em.rateOverTime = shrinking ? Mathf.Min(220f, r * 5f) : 0f;
            if (!shrinking) return;
            zoneWall.transform.position = W(center);
            zoneWall.transform.localScale = new Vector3(r, 1f, r);
            for (int i = 0; i <= WallSeg; i++)
            {
                float band = 0.55f + 0.45f * Mathf.Sin(i * 0.5f - t * 2.2f) * Mathf.Sin(i * 0.17f + t * 0.9f);
                wallColors[i * 2] = new Color(1f, 0.22f, 0.32f, 0.5f * band + 0.15f);
                wallColors[i * 2 + 1] = new Color(0.55f, 0.12f, 0.55f, 0f);
            }
            zoneMesh.colors = wallColors;
            zoneGas.transform.position = W(center, 0.2f);
            var sh = zoneGas.shape;
            sh.radius = r;
        }

        SpriteRenderer rotorDisc;
        ParticleSystem padDust;

        void BuildHeliFx(Vector2 pad)
        {
            rotorDisc = Flat(heli, circle, new Color(0.8f, 0.85f, 0.9f, 0.18f), 3.2f, 6, 1.25f);
            rotorDisc.transform.localPosition = new Vector3(0, 1.25f, 0);
            padDust = Particles(mapRoot, W(pad, 0.1f), new Color(0.6f, 0.55f, 0.48f, 0.5f), new Color(0.45f, 0.42f, 0.38f, 0.4f), 0f, 1.6f, 2.5f, 0.9f, 0f, new Vector3(1.2f, 1.2f, 0.1f), 0.5f, 0f, 8);
            var sh = padDust.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 1.0f;
            sh.radiusThickness = 0.3f;
            sh.rotation = new Vector3(90f, 0, 0);
        }

        void UpdateHeliFx(float t, bool spinning, float lift)
        {
            if (rotorDisc == null) return;
            rotorDisc.enabled = spinning;
            rotorDisc.color = new Color(0.8f, 0.85f, 0.9f, 0.12f + 0.06f * Mathf.Sin(t * 30f));
            var em = padDust.emission;
            bool soon = Sim.departAt >= 0 && Sim.departAt - Sim.time < 12f;
            em.rateOverTime = lift > 0.01f ? Mathf.Lerp(120f, 0f, lift / 6f) : soon ? 40f : spinning ? 6f : 0f;
        }

        List<Vector2> riverLine = new List<Vector2>();

        void BuildRiver(CityMap map, Vector2 size)
        {
            var verts = new List<Vector3>(); var tris = new List<int>();
            for (int i = 0; i < riverLine.Count; i++)
            {
                var t = (riverLine[Mathf.Min(i + 1, riverLine.Count - 1)] - riverLine[Mathf.Max(i - 1, 0)]).normalized;
                var n = new Vector2(t.y, -t.x) * 1.3f;
                var p = riverLine[i];
                verts.Add(new Vector3(p.x - n.x, 0.015f, p.y - n.y)); verts.Add(new Vector3(p.x + n.x, 0.015f, p.y + n.y));
                int b = verts.Count - 4;
                if (b >= 0) tris.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2, b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.SetNormals(verts.Select(_ => Vector3.up).ToList());
            mesh.RecalculateBounds();
            var go = new GameObject("river");
            go.transform.SetParent(mapRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = LitMat(RiverColor);
        }

        void BuildRiverFx(CityMap map, Vector2 size)
        {
            float walked = 0f;
            for (int i = 0; i + 1 < riverLine.Count; i++)
            {
                walked += Vector2.Distance(riverLine[i], riverLine[i + 1]);
                if (walked < 3f) continue;
                walked = 0f;
                var p = riverLine[i];
                Particles(mapRoot, new Vector3(p.x, 0.04f, p.y), new Color(0.65f, 0.85f, 1f, 0.5f), new Color(0.4f, 0.6f, 0.9f, 0.35f),
                          3.6f, 3f, 0.02f, 0.14f, 0f, new Vector3(2.2f, 2.2f, 0.02f), 0.25f, 0f, 2);
            }
        }

        static readonly Color ZombieTint = new Color(0.62f, 0.86f, 0.56f);

        Renderer PlushBody(Transform m, Color tint, Color outfit, bool zombie,
                           out Transform head, out Transform armL, out Transform armR, out Transform legL, out Transform legR, out Renderer eyeL, out Renderer eyeR)
        {
            var rig = BuildCapy(m, tint);
            head = rig.head; armL = rig.armL; armR = rig.armR; legL = rig.legL; legR = rig.legR;
            eyeL = rig.eyeL; eyeR = rig.eyeR;
            if (zombie)
            {
                var glow = UnlitMat(new Color(0.85f, 1f, 0.3f));
                eyeL.sharedMaterial = glow; eyeR.sharedMaterial = glow;
            }
            CapyVest(rig, outfit);
            return rig.bodyRenderer;
        }

        GameObject MiniCapy(Transform parent, Color shirt)
        {
            var g = new GameObject("civilian").transform;
            g.SetParent(parent, false);
            var rig = BuildCapy(g, Color.white, 0.55f);
            CapyVest(rig, shirt);
            return g.gameObject;
        }

        void BuildPlayerModel(PlayerView v, Player p)
        {
            v.body = new GameObject("player" + p.id);
            v.body.transform.SetParent(dynRoot, false);
            v.model = new GameObject("model").transform;
            v.model.SetParent(v.body.transform, false);
            v.furColor = p.color;
            var fur = Vary(Color.white, new System.Random(p.id), 0.06f);
            v.torso = PlushBody(v.model, fur, p.color, false, out v.head, out v.armL, out v.armR, out v.legL, out v.legR, out v.eyeL, out v.eyeR);
            (v.swirlL, v.swirlR) = MakeSwirlEyes(v.head);

            var top = CapyHeadTop;
            v.helmet = Part(v.head, PrimitiveType.Sphere, p.color * 0.9f, top + new Vector3(0, -0.02f, -0.03f), new Vector3(0.36f, 0.14f, 0.34f)).GetComponent<Renderer>();
            Part(v.head, PrimitiveType.Sphere, new Color(0.9f, 0.88f, 0.84f), top + new Vector3(0, 0.05f, -0.03f), Vector3.one * 0.08f);
            v.gun = new GameObject("gun");
            v.gun.transform.SetParent(v.armR, false);
            Part(v.gun.transform, PrimitiveType.Cube, new Color(0.16f, 0.17f, 0.18f), new Vector3(0, 0.08f, 0.26f), new Vector3(0.07f, 0.09f, 0.5f));
            Part(v.gun.transform, PrimitiveType.Cube, new Color(0.35f, 0.24f, 0.14f), new Vector3(0, 0.02f, 0.06f), new Vector3(0.06f, 0.12f, 0.1f));
            v.gun.SetActive(false);
            v.glow = Flat(dynRoot, soft, new Color(p.color.r, p.color.g, p.color.b, 0.35f), 2.0f, 5, 0.03f);
            v.dust = Particles(v.body.transform, Vector3.zero, new Color(0.55f, 0.5f, 0.45f, 0.45f), new Color(0.4f, 0.38f, 0.35f, 0.35f), 0f, 0.7f, 0.4f, 0.25f, -0.05f, new Vector3(0.2f, 0.2f, 0.05f), 0.2f, 0f, 5);
            v.lastPos = p.pos;
            v.parts = v.model.GetComponentsInChildren<MeshRenderer>();
            v.baseColors = new Color[v.parts.Length];
            for (int i = 0; i < v.parts.Length; i++) v.baseColors[i] = v.parts[i].sharedMaterial.color;
            v.lastHp = p.hp;
        }

        Transform Limb(Transform parent, Vector3 pivot, Color c, float w, float len)
        {
            var piv = new GameObject("limb").transform;
            piv.SetParent(parent, false);
            piv.localPosition = pivot;
            Part(piv, PrimitiveType.Capsule, c, new Vector3(0, -len / 2f, 0), new Vector3(w, len / 2f + w / 4f, w));
            return piv;
        }

        static void SetSquash(Transform model, float sy)
        {
            float sx = 1f / Mathf.Sqrt(Mathf.Max(0.5f, sy));
            model.localScale = new Vector3(sx, sy, sx);
        }

        void AnimatePlayer(PlayerView v, Player p, float t)
        {
            float dt = Time.unscaledDeltaTime;
            var delta = p.pos - v.lastPos;
            v.lastPos = p.pos;
            bool downed = p.state == PlayerState.Downed;
            bool moving = !downed && delta.sqrMagnitude > 1e-6f;
            if (v.gun != null && v.gun.activeSelf != (p.ammo > 0)) v.gun.SetActive(p.ammo > 0);
            if (moving)
            {
                v.facing = Quaternion.Slerp(v.facing, Quaternion.LookRotation(new Vector3(delta.x, 0, delta.y)), 1f - Mathf.Exp(-10f * dt));
                v.walk += dt * 9f;
            }
            float w = v.walk;
            if (downed)
            {
                v.model.localPosition = new Vector3(0, 0.3f, 0);
                v.model.localRotation = v.facing * Quaternion.Euler(0, 0, 90f);
                v.armL.localRotation = v.armR.localRotation = Quaternion.Euler(-30f, 0, 0);
                v.legL.localRotation = v.legR.localRotation = Quaternion.identity;
                v.head.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 3f) * 20f, 0);
                SetSquash(v.model, 1f);
            }
            else if (moving)
            {
                float step = Mathf.Sin(w);
                v.model.localPosition = new Vector3(0, Mathf.Abs(Mathf.Cos(w)) * 0.09f, 0);
                v.model.localRotation = v.facing * Quaternion.Euler(-Mathf.Sin(w * 2f) * 4f, 0, step * 6f);
                v.legL.localRotation = v.legR.localRotation = Quaternion.identity;
                v.armL.localRotation = v.armR.localRotation = Quaternion.identity;
                v.head.localRotation = Quaternion.Euler(Mathf.Sin(w * 2f - 1.2f) * 5f, 0, -Mathf.Sin(w - 0.9f) * 8f);
                SetSquash(v.model, 1f + Mathf.Cos(w * 2f) * 0.06f);
            }
            else
            {
                v.model.localPosition = Vector3.zero;
                v.model.localRotation = v.facing * Quaternion.Euler(0, 0, Mathf.Sin(t * 1.6f + p.id) * 4f);
                v.legL.localRotation = Quaternion.identity;
                v.legR.localRotation = Quaternion.identity;
                v.armL.localRotation = v.armR.localRotation = Quaternion.identity;
                v.head.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 0.7f + p.id * 2f) * 25f, Mathf.Sin(t * 1.6f + p.id) * 6f);
                SetSquash(v.model, 1f + Mathf.Sin(t * 3f + p.id) * 0.025f);
            }
            if (!downed && p.cargo.Any(id => Sim.TaskById(id) is GameTask tk && tk.type != TaskType.Rescue))
            {
                v.armL.localRotation = v.armR.localRotation = Quaternion.Euler(-55f, 0, 0);
            }
            v.body.transform.position = W(Shown(p), GroundH(Shown(p)));
            v.body.transform.localScale = Vector3.one * 1.3f;
            TintPlayer(v, p, t);
            var em = v.dust.emission;
            em.rateOverTime = moving ? 9f : 0f;
            v.glow.transform.position = W(Shown(p), 0.03f + GroundH(Shown(p)));
            bool hurt = p.state == PlayerState.Active && (p.hp < 35f || p.infection > 70f);
            bool herbal = Sim.time < Sim.infectionGuardUntil && p.state == PlayerState.Active;
            v.glow.color = hurt && !herbal ? new Color(1f, 0.15f, 0.1f, 0.35f + 0.25f * Mathf.Sin(t * 8f))
                         : downed ? new Color(1f, 0.3f, 0.2f, 0.25f)
                         : herbal ? new Color(0.45f, 1f, 0.55f, 0.4f + 0.2f * Mathf.Sin(t * 5f))
                         : new Color(p.color.r, p.color.g, p.color.b, 0.3f);
            v.glow.enabled = p.state != PlayerState.Eliminated;
        }

        void BuildZombieModel(ZombieView v, Zombie z)
        {
            v.body = new GameObject("zombie" + z.id);
            v.body.transform.SetParent(dynRoot, false);
            v.model = new GameObject("model").transform;
            v.model.SetParent(v.body.transform, false);
            var rnd = new System.Random(unchecked((int)((uint)(z.id + 1) * 2654435761u)));
            var rag = Vary(new Color(0.38f, 0.34f, 0.36f), rnd, 0.25f);
            PlushBody(v.model, Vary(ZombieTint, rnd, 0.1f), rag, true, out v.head, out v.armL, out v.armR, out _, out _, out v.eyeL, out v.eyeR);
            (v.swirlL, v.swirlR) = MakeSwirlEyes(v.head);

            var stitch = new Color(0.16f, 0.12f, 0.12f);
            var top = CapyHeadTop + new Vector3(0.06f, 0.005f, 0f);
            Part(v.head, PrimitiveType.Cube, stitch, top, new Vector3(0.03f, 0.02f, 0.22f), new Vector3(0, 20f, 0));
            for (int i = -1; i <= 1; i++)
                Part(v.head, PrimitiveType.Cube, stitch, top + Quaternion.Euler(0, 20f, 0) * new Vector3(0, 0, i * 0.06f), new Vector3(0.09f, 0.02f, 0.02f), new Vector3(0, 20f, 0));
            v.phase = (float)rnd.NextDouble() * 10f;
            v.tempo = 0.6f + (float)rnd.NextDouble() * 0.9f;
            v.sway = 0.6f + (float)rnd.NextDouble() * 0.8f;
            v.lastPos = z.pos;
            v.facing = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
        }

        void AnimateZombie(ZombieView v, Zombie z, float t)
        {
            float dt = Time.unscaledDeltaTime;
            var delta = z.pos - v.lastPos;
            v.lastPos = z.pos;
            bool moving = delta.sqrMagnitude > 1e-6f;
            if (moving)
                v.facing = Quaternion.Slerp(v.facing, Quaternion.LookRotation(new Vector3(delta.x, 0, delta.y)), 1f - Mathf.Exp(-6f * dt));
            bool stunned = Sim.time < Sim.zombieStunUntil;
            bool chase = z.mode == ZombieMode.Chase;
            float w = t * (chase ? 8f : 4f) * v.tempo + v.phase;
            float step = Mathf.Sin(w);
            if (stunned)
            {
                v.model.localPosition = Vector3.zero;
                v.model.localRotation = v.facing * Quaternion.Euler(0, t * 90f + v.phase * 30f, Mathf.Sin(t * 3f + v.phase) * 14f);
                v.head.localRotation = Quaternion.Euler(Mathf.Sin(t * 5f) * 15f, 0, Mathf.Cos(t * 5f) * 15f);
                v.armL.localRotation = Quaternion.Euler(0, 0, -8f);
                v.armR.localRotation = Quaternion.Euler(0, 0, 8f);
                SetSquash(v.model, 1f);
            }
            else
            {
                float lean = moving ? 14f : 6f;
                v.model.localPosition = new Vector3(0, moving ? Mathf.Abs(Mathf.Cos(w)) * (chase ? 0.07f : 0.03f) : 0f, 0);
                v.model.localRotation = v.facing * Quaternion.Euler(lean, 0, ((moving ? step * 14f : Mathf.Sin(t * 1.3f * v.tempo + v.phase) * 6f) + 6f) * v.sway);
                v.head.localRotation = Quaternion.Euler(10f, 0, 18f + Mathf.Sin(w - 1f) * 8f);
                v.armL.localRotation = Quaternion.Euler(-50f + Mathf.Sin(w) * 10f, 0, 0);
                v.armR.localRotation = Quaternion.Euler(-50f - Mathf.Sin(w) * 10f, 0, 0);
                SetSquash(v.model, 1f + (moving ? Mathf.Cos(w * 2f) * 0.05f : Mathf.Sin(t * 2f * v.tempo + v.phase) * 0.03f) * v.sway);
            }

            Player prey = null;
            float reach = gm.cfg.zombieAttackRange * 1.4f;
            if (!stunned)
                foreach (var p in Sim.players)
                {
                    if (p.state != PlayerState.Active || (p.nextNode < 0 && Sim.IsSafe(p.node))) continue;
                    float d = Vector2.Distance(p.pos, z.pos);
                    if (d < reach) { reach = d; prey = p; }
                }
            if (prey != null)
            {
                var to = Shown(prey) - Shown(z);
                if (to.sqrMagnitude > 1e-4f)
                    v.facing = Quaternion.Slerp(v.facing, Quaternion.LookRotation(new Vector3(to.x, 0, to.y)), 1f - Mathf.Exp(-14f * dt));
                float sw = t * 13f * v.tempo + v.phase;
                v.model.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(sw * 0.5f)) * 0.05f, 0);
                v.model.localRotation = v.facing * Quaternion.Euler(24f + Mathf.Sin(sw) * 7f, 0, Mathf.Sin(sw * 0.5f) * 6f);
                v.head.localRotation = Quaternion.Euler(18f + Mathf.Sin(sw * 2f) * 10f, 0, 0);
                v.armL.localRotation = Quaternion.Euler(-45f + Mathf.Sin(sw) * 40f, 0, 0);
                v.armR.localRotation = Quaternion.Euler(-45f - Mathf.Sin(sw) * 40f, 0, 0);
                SetSquash(v.model, 1f + Mathf.Sin(sw) * 0.06f);
            }
            v.body.transform.position = W(Shown(z), GroundH(Shown(z)));
            v.body.transform.localScale = Vector3.one * 1.2f;
            var eyeColor = stunned ? new Color(0.3f, 0.3f, 0.35f) : chase ? new Color(1f, 0.15f, 0.1f) : new Color(0.85f, 1f, 0.3f);
            var mat = UnlitMat(eyeColor);
            v.eyeL.sharedMaterial = mat;
            v.eyeR.sharedMaterial = mat;
        }
    }
}
