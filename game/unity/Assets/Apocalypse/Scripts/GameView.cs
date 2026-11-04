using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView : MonoBehaviour
    {
        class PlayerView
        {
            public GameObject body; public SpriteRenderer ring, glow; public LineRenderer route; public readonly List<GameObject> crates = new List<GameObject>();
            public Renderer torso, helmet; public Transform model, head, legL, legR, armL, armR; public ParticleSystem dust;
            public Vector2 lastPos; public float walk; public Quaternion facing = Quaternion.identity; public Color furColor;
            public Renderer[] parts; public Color[] baseColors; public float lastHp = 100f, flash, lastHitFx = -9f;
            public Renderer eyeL, eyeR; public SpriteRenderer swirlL, swirlR; public float deadAt = -1f;
            public readonly List<Vector3> trail = new List<Vector3>();
            public GameObject gun;
        }
        class ZombieView
        {
            public GameObject body; public SpriteRenderer ring; public LineRenderer intent;
            public Transform model, head, armL, armR; public Renderer eyeL, eyeR; public SpriteRenderer swirlL, swirlR; public Vector2 lastPos; public float phase, tempo = 1f, sway = 1f; public Quaternion facing = Quaternion.identity;
        }
        class Pile { public readonly List<GameObject> items = new List<GameObject>(); public readonly List<GameObject> civilians = new List<GameObject>(); }

        const float Pitch = 35f;
        const float Yaw = 45f;
        static readonly Color Bg = new Color(0.035f, 0.045f, 0.08f);
        static readonly Color GridColor = new Color(0.30f, 0.42f, 0.65f, 0.16f);
        static readonly Color RoadColor = new Color(0.24f, 0.33f, 0.48f);
        static readonly Color BridgeColor = new Color(0.50f, 0.62f, 0.80f);
        static readonly Color BlockedColor = new Color(0.85f, 0.18f, 0.18f);
        static readonly Color RiverColor = new Color(0.05f, 0.11f, 0.22f);

        const string Muted = "#7c8aa5", Accent = "#7fb2ff", Gold = "#ffd54f", Good = "#69f0ae", Bad = "#ff5252", Warn = "#ffab40";
        static readonly Color ZombieColor = new Color(0.36f, 0.62f, 0.25f);
        static readonly Color DeliveryColor = new Color(1.00f, 0.60f, 0.10f);
        static readonly Color RescueColor = new Color(0.40f, 0.85f, 0.45f);
        static readonly Color BatchColor = new Color(1.00f, 0.82f, 0.55f);
        static readonly Color UrgentColor = new Color(1.00f, 0.92f, 0.20f);
        static readonly Color FuelColor = new Color(0.90f, 0.22f, 0.16f);
        static readonly Color InfectionColor = new Color(0.86f, 0.10f, 0.12f);

        GameManager gm;
        GameSim Sim => gm.sim;
        Camera cam;
        Sprite circle, soft, square, ringSprite;
        Material lineMat;
        Transform mapRoot, dynRoot;
        int builtVersion = -1;

        readonly Dictionary<int, SpriteRenderer> heat = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, LineRenderer> roads = new Dictionary<int, LineRenderer>();
        readonly Dictionary<int, Renderer> supplyViews = new Dictionary<int, Renderer>();
        readonly Dictionary<int, PlayerView> playerViews = new Dictionary<int, PlayerView>();
        readonly Dictionary<int, ZombieView> zombieViews = new Dictionary<int, ZombieView>();
        readonly Dictionary<int, SpriteRenderer> noiseViews = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, Pile> piles = new Dictionary<int, Pile>();

        int follow = -1;
        bool director = true;
        int directorPick = -1;
        Vector2? directorSpot;
        float directorTimer, zoomMul = 1f;
        bool clean;

        Font font;
        GUIStyle label, small, bold, big, title, center;
        Texture2D white;
        string addresses = "";

        string camFocus;
        float camFocusSize = 4f;

        void Start()
        {
            gm = GetComponent<GameManager>();
            var args = System.Environment.GetCommandLineArgs();
            int ci = System.Array.IndexOf(args, "-camFocus");
            if (ci >= 0 && ci + 2 < args.Length) { camFocus = args[ci + 1].ToLower(); float.TryParse(args[ci + 2], out camFocusSize); }
            int gi = System.Array.IndexOf(args, "-gallery");
            if (gi >= 0 && gi + 1 < args.Length) StartCoroutine(Gallery(args[gi + 1]));
            cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Bg;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.transform.rotation = Quaternion.Euler(Pitch, Yaw, 0);
            cam.allowHDR = false;
            LoadSavedUrl();
            cam.gameObject.AddComponent<BloomFx>();

            foreach (var l in FindObjectsByType<Light>()) l.enabled = false;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(60f, 20f, 0f);
            sun.color = new Color(0.85f, 0.9f, 1f);
            sun.intensity = 0.75f;
            sun.shadows = LightShadows.None;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.68f);

            circle = MakeSprite(64, d => Mathf.Clamp01((31f - d) * 0.8f));
            soft = MakeSprite(64, d => Mathf.Pow(Mathf.Clamp01(1f - d / 32f), 1.5f));
            square = MakeSprite(16, d => 1f);
            ringSprite = MakeSprite(128, d => Mathf.Clamp01(1f - Mathf.Abs(d - 30.5f) * 1.2f));
            lineMat = new Material(Shader.Find("Sprites/Default"));

            white = Texture2D.whiteTexture;
            try
            {
                addresses = string.Join("  ", Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => $"http://{a}:{gm.cfg.port}"));
            }
            catch { }
            if (addresses == "") addresses = $"http://localhost:{gm.cfg.port}";
        }

        static Sprite MakeSprite(int size, System.Func<float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float c = (size - 1) / 2f, scale = 32f / (size / 2f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color(1, 1, 1, alpha(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) * scale));
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        SpriteRenderer Flat(Transform parent, Sprite s, Color c, float scale, int order, float height = 0.02f)
        {
            var go = new GameObject(s.name);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(90f, 0, 0);
            go.transform.localScale = Vector3.one * scale;
            go.transform.position = new Vector3(0, height, 0);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s; sr.color = c; sr.sortingOrder = order;
            return sr;
        }

        LineRenderer NewLine(Transform parent, float width, int order)
        {
            var go = new GameObject("line");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(90f, 0, 0);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = lineMat;
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.TransformZ;
            lr.widthMultiplier = width;
            lr.sortingOrder = order;
            lr.numCapVertices = 3;
            lr.numCornerVertices = 2;
            lr.positionCount = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        static Material litBase;

        static GameObject Solid(PrimitiveType type, Transform parent, Color c, Vector3 scale)
        {
            if (litBase == null)
                litBase = Resources.Load<Material>("ApocalypseLit") ?? new Material(Shader.Find("Standard"));
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = litBase;
            r.material.color = c;
            return go;
        }

        static void Paint(GameObject go, Color c) => go.GetComponent<Renderer>().material.color = c;

        public static Vector3 W(Vector2 v, float h = 0f) => new Vector3(v.x, h, v.y);

        void BuildMap()
        {
            if (mapRoot) Destroy(mapRoot.gameObject);
            if (dynRoot) Destroy(dynRoot.gameObject);
            heat.Clear(); roads.Clear(); supplyViews.Clear(); incidentViews.Clear(); incidentFx.Clear(); dyingZombies.Clear(); guardRoot = null;
            playerViews.Clear(); zombieViews.Clear(); noiseViews.Clear(); piles.Clear();
            mapRoot = new GameObject("Map").transform;
            dynRoot = new GameObject("Dynamic").transform;
            var map = Sim.map;
            Vector2 size = map.Size;

            riverLine = map.RiverLine();
            BuildGround(map, size);
            BuildRiver(map, size);
            BuildRiverFx(map, size);

            float m = 3f;
            for (float x = -m; x <= size.x + m + 0.01f; x += map.spacing / 2f)
            {
                var g = NewLine(mapRoot, 0.05f, 1);
                g.SetPosition(0, new Vector3(x, 0.005f, -m)); g.SetPosition(1, new Vector3(x, 0.005f, size.y + m));
                g.startColor = g.endColor = GridColor;
            }
            for (float y = -m; y <= size.y + m + 0.01f; y += map.spacing / 2f)
            {
                var g = NewLine(mapRoot, 0.05f, 1);
                g.SetPosition(0, new Vector3(-m, 0.005f, y)); g.SetPosition(1, new Vector3(size.x + m, 0.005f, y));
                g.startColor = g.endColor = GridColor;
            }
            var border = NewLine(mapRoot, 0.12f, 1);
            border.loop = true;
            border.positionCount = 4;
            border.SetPositions(new[] { new Vector3(-m, 0.01f, -m), new Vector3(size.x + m, 0.01f, -m), new Vector3(size.x + m, 0.01f, size.y + m), new Vector3(-m, 0.01f, size.y + m) });
            var bc = GridColor; bc.a = 0.5f;
            border.startColor = border.endColor = bc;

            foreach (var e in map.edges)
            {
                var lr = NewLine(mapRoot, e.bridge ? 0.7f : 0.75f, 2);
                lr.SetPosition(0, W(map.nodes[e.a].pos, 0.01f));
                lr.SetPosition(1, W(map.nodes[e.b].pos, 0.01f));
                roads[e.id] = lr;
            }
            BuildBridges();

            PlaceFacilities();
            foreach (var n in map.nodes)
            {
                var h = Flat(mapRoot, soft, Color.clear, 8f, 1);
                h.transform.position = W(n.pos, 0.03f);
                heat[n.id] = h;
                BuildFacility(n);
            }
            BuildDistricts();
            BuildEscape();
            CollectOccluders();
            builtVersion = Sim.mapVersion;
        }

        LineRenderer zoneLine;
        Transform heli, rotor;

        void BuildEscape()
        {
            zoneLine = null; heli = null;
            if (!gm.cfg.escape) return;
            var ex = FacilityPos(Sim.extraction);
            var pad = Solid(PrimitiveType.Cylinder, mapRoot, new Color(0.25f, 0.27f, 0.3f), new Vector3(2.6f, 0.04f, 2.6f));
            pad.transform.position = W(ex, 0.04f);
            Flat(mapRoot, ringSprite, new Color(1f, 0.85f, 0.2f, 0.9f), 2.5f, 4, 0.09f).transform.position = W(ex, 0.09f);
            heli = new GameObject("heli").transform;
            heli.SetParent(mapRoot, false);
            heli.position = W(ex, 0f);
            EscapeCompound(ex);
            var body = Solid(PrimitiveType.Capsule, heli, new Color(0.35f, 0.42f, 0.3f), new Vector3(0.9f, 0.9f, 1.8f));
            body.transform.localPosition = new Vector3(0, 0.6f, 0);
            body.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            var tail = Solid(PrimitiveType.Cube, heli, new Color(0.35f, 0.42f, 0.3f), new Vector3(0.18f, 0.18f, 1.6f));
            tail.transform.localPosition = new Vector3(0, 0.75f, -1.5f);
            rotor = Solid(PrimitiveType.Cube, heli, new Color(0.15f, 0.15f, 0.15f), new Vector3(3.2f, 0.04f, 0.18f)).transform;
            rotor.localPosition = new Vector3(0, 1.25f, 0);
            zoneLine = NewLine(mapRoot, 0.35f, 6);
            zoneLine.loop = true;
            zoneLine.positionCount = 72;
            zoneLine.startColor = zoneLine.endColor = new Color(1f, 0.25f, 0.2f, 0.9f);
            BuildZoneWall();
            BuildHeliFx(ex);
        }

        readonly Dictionary<int, SpriteRenderer> incidentViews = new Dictionary<int, SpriteRenderer>();

        static Color IncidentColor(string kind)
        {
            switch (kind)
            {
                case "outbreak": return new Color(1f, 0.32f, 0.32f);
                case "bridge": return new Color(0.5f, 0.75f, 1f);
                case "airdrop": return new Color(1f, 0.84f, 0.31f);
                case "trapped": return new Color(0.41f, 0.94f, 0.68f);
                case "medevac": return new Color(0.94f, 0.94f, 0.94f);
                case "napalm": return new Color(1f, 0.45f, 0.1f);
                case "decoy": return new Color(0.7f, 0.55f, 1f);
                case "chest": return new Color(1f, 0.92f, 0.2f);
                case "boss": return new Color(0.75f, 0.1f, 0.1f);
                case "herbs": return new Color(0.5f, 0.9f, 1f);
                case "gas": return new Color(1f, 0.55f, 0.2f);
                case "rubble": return new Color(0.8f, 0.7f, 0.55f);
                case "bell": return new Color(1f, 0.95f, 0.6f);
                case "generator": return new Color(0.3f, 0.85f, 0.95f);
                default: return new Color(1f, 0.67f, 0.25f);
            }
        }

        void UpdateIncidentViews(float t)
        {
            var live = new HashSet<int>();
            foreach (var inc in Sim.incidents)
            {
                if (inc.status != "active") continue;
                live.Add(inc.id);
                if (!incidentViews.TryGetValue(inc.id, out var sr)) { sr = Flat(dynRoot, ringSprite, Color.white, 4f, 7, 0.08f); incidentViews[inc.id] = sr; }
                float left = inc.deadline - Sim.time;
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * (left < 15f ? 12f : 5f));
                sr.transform.position = W(Sim.map.nodes[inc.node].pos, 0.08f);
                sr.transform.localScale = Vector3.one * (3.2f + 0.8f * pulse);
                var c = IncidentColor(inc.kind); c.a = 0.55f + 0.4f * pulse;
                sr.color = c;
            }
            foreach (int id in incidentViews.Keys.Where(id => !live.Contains(id)).ToList())
            {
                Destroy(incidentViews[id].gameObject);
                incidentViews.Remove(id);
            }
        }

        void UpdateEscape(float t)
        {
            UpdateIncidentViews(t);
            UpdateIncidentFx(t);
            UpdateOutcomes(t);
            if (zoneLine == null) return;
            var ex = Sim.map.nodes[Sim.extraction].pos;
            float r = Sim.ZoneRadius;
            bool shrinking = Sim.ZoneRadius < Sim.zoneMax - 0.01f;
            zoneLine.enabled = shrinking;
            for (int i = 0; i < 72; i++)
            {
                float a = i * Mathf.PI * 2f / 72f;
                zoneLine.SetPosition(i, W(ex + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, 0.12f));
            }
            zoneLine.widthMultiplier = 0.3f + 0.1f * Mathf.Sin(t * 4f);
            bool ready = Sim.readyAt >= 0;
            rotor.localRotation = Quaternion.Euler(0, ready ? t * 900f % 360f : 30f, 0);
            float lift = Sim.status == MatchStatus.Ended && Sim.classWon ? Mathf.Min(6f, (t - endedAt) * 2f) : 0f;
            heli.position = W(FacilityPos(Sim.extraction), lift);
            UpdateZoneWall(Sim.map.nodes[Sim.extraction].pos, r, shrinking, t);
            UpdateHeliFx(t, ready, lift);
        }

        float endedAt = -1f;

        static readonly Color[] ThemeGround =
        {
            new Color(0.035f, 0.085f, 0.050f),
            new Color(0.055f, 0.070f, 0.115f),
            new Color(0.085f, 0.068f, 0.058f),
            new Color(0.072f, 0.085f, 0.058f),
        };

        readonly Dictionary<int, Vector2> facilityPos = new Dictionary<int, Vector2>();
        readonly Dictionary<int, Vector2> facilityDir = new Dictionary<int, Vector2>();

        Vector2 FacilityPos(int node) => facilityPos.TryGetValue(node, out var v) ? v : Sim.map.nodes[node].pos;

        Vector2 ShelterPos(Node n) => FacilityPos(n.id) + facilityDir[n.id] * 2.2f;

        static float FacingYaw(Vector2 from, Vector2 to)
        {
            var d = to - from;
            return d.sqrMagnitude < 1e-4f ? 0f : Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + 180f;
        }

        float RoadClearance(Vector2 p)
        {
            var map = Sim.map;
            float best = float.MaxValue;
            foreach (var e in map.edges)
            {
                Vector2 a = map.nodes[e.a].pos, b = map.nodes[e.b].pos, ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        void PlaceFacilities()
        {
            facilityPos.Clear(); facilityDir.Clear();
            facilityAnims.Clear();
            var map = Sim.map;
            var taken = new List<Vector2>();
            var order = map.nodes.Where(n => n.type != NodeType.Road).OrderBy(n => gm.cfg.escape && n.id == Sim.extraction ? 0 : 1).ThenBy(n => n.id);
            foreach (var n in order)
            {
                bool big = gm.cfg.escape && n.id == Sim.extraction;
                int c0 = n.id % map.cols, r0 = n.id / map.cols;
                Vector2 bestPos = n.pos, bestDir = new Vector2(1, 1).normalized;
                float bestScore = float.MinValue;
                for (int dc = -1; dc <= 0; dc++)
                    for (int dr = -1; dr <= 0; dr++)
                    {
                        int c = c0 + dc, r = r0 + dr;
                        if (c < 0 || r < 0 || c >= map.cols - 1 || r >= map.rows - 1 || map.RiverInCell(r, c)) continue;
                        Vector2 center = (map.nodes[r * map.cols + c].pos + map.nodes[r * map.cols + c + 1].pos +
                                          map.nodes[(r + 1) * map.cols + c].pos + map.nodes[(r + 1) * map.cols + c + 1].pos) / 4f;
                        var dir = (center - n.pos).normalized;
                        var pos = n.pos + dir * (big ? 2.0f : 2.2f);
                        float clear = RoadClearance(pos);
                        float crowd = taken.Count == 0 ? 99f : taken.Min(t => Vector2.Distance(t, pos));
                        float score = Mathf.Min(clear, 1.6f) * 2f + Mathf.Min(crowd, 3f);
                        if (score > bestScore) { bestScore = score; bestPos = pos; bestDir = dir; }
                    }
                facilityPos[n.id] = bestPos;
                facilityDir[n.id] = bestDir;
                taken.Add(bestPos);
                if (big) taken.Add(bestPos + bestDir * 2.2f);
            }
        }

        void BuildFacility(Node n)
        {
            var dot = Flat(mapRoot, circle, RoadColor * 1.15f, 1.15f, 3, 0.02f);
            dot.transform.position = W(n.pos, 0.02f);
            if (n.type == NodeType.Road) return;
            var fp = FacilityPos(n.id);
            if (gm.cfg.escape && n.id == Sim.extraction) fp = ShelterPos(n);
            var drive = NewLine(mapRoot, 0.3f, 2);
            drive.SetPosition(0, W(n.pos, 0.012f)); drive.SetPosition(1, W(fp, 0.012f));
            drive.startColor = drive.endColor = RoadColor;
            var root = new GameObject("node" + n.id).transform;
            root.SetParent(mapRoot, false);
            root.position = W(fp);
            root.rotation = Quaternion.Euler(0, FacingYaw(n.pos, fp), 0);
            BuildFacilityArt(n, root);
            if (n.type != NodeType.Shelter)
            {
                root.localScale = Vector3.one * 1.25f;
                Marker(n, fp);
            }
        }

        void LateUpdate()
        {
            if (Sim.mapVersion != builtVersion) BuildMap();
            var map = Sim.map;
            float t = Time.unscaledTime;
            AnimateDecor(t);

            foreach (var n in map.nodes)
            {
                var c = InfectionColor; c.a = n.infection * 0.24f;
                heat[n.id].color = c;
            }
            foreach (var kv in supplyViews)
                if (kv.Value.sharedMaterial != null && kv.Value.sharedMaterial.shader.name != "Hidden/ApocalypseGhost")
                    kv.Value.material.color = map.nodes[kv.Key].medkits > 0 ? new Color(1.0f, 0.84f, 0.31f) : new Color(0.45f, 0.40f, 0.25f);
            foreach (var e in map.edges)
            {
                var c = e.blocked ? BlockedColor : e.bridge ? BridgeColor : RoadColor;
                roads[e.id].startColor = roads[e.id].endColor = c;
            }

            UpdateSpread(Time.unscaledDeltaTime);
            UpdatePlayers(t);
            UpdateZombies();
            UpdateOcclusion();
            UpdateTasks(t);
            if (Sim.status == MatchStatus.Ended) { if (endedAt < 0) endedAt = t; } else endedAt = -1f;
            UpdateEscape(t);
            UpdateBanners();
            UpdateCamera();
        }

        void UpdatePlayers(float t)
        {
            seenFollowers.Clear();
            foreach (var p in Sim.players)
            {
                if (!playerViews.TryGetValue(p.id, out var v))
                {
                    v = new PlayerView
                    {
                        ring = Flat(dynRoot, circle, p.color, 1.2f, 6, 0.04f),
                        route = NewLine(dynRoot, 0.22f, 4),
                    };
                    BuildPlayerModel(v, p);
                    playerViews[p.id] = v;
                }
                bool alive = p.state != PlayerState.Eliminated;
                bool show = alive;
                if (alive) { v.deadAt = -1f; v.body.SetActive(true); AnimatePlayer(v, p, t); SetSwirl(v.eyeL, v.eyeR, v.swirlL, v.swirlR, p.state == PlayerState.Downed, t); }
                else
                {
                    v.glow.enabled = false;
                    if (v.body.activeSelf) show = AnimatePlayerDeath(v, p, t);
                    v.body.SetActive(show);
                }
                v.ring.enabled = alive;
                v.route.enabled = alive && p.state == PlayerState.Active;
                v.ring.transform.position = W(Shown(p), 0.04f + GroundH(Shown(p)));
                var rc = p.color; rc.a = p.id == follow || p.id == directorPick ? 0.9f : 0.45f;
                v.ring.color = rc;
                if (v.route.enabled) DrawRoute(p, v.route);

                var boxes = alive ? p.cargo.Select(Sim.TaskById).Where(tk => tk != null && tk.type != TaskType.Rescue).ToList() : new List<GameTask>();
                while (v.crates.Count < boxes.Count) v.crates.Add(Solid(PrimitiveType.Cube, dynRoot, DeliveryColor, Vector3.one * 0.32f));
                for (int i = 0; i < v.crates.Count; i++)
                {
                    bool on = i < boxes.Count;
                    v.crates[i].SetActive(on);
                    if (!on) continue;
                    var task = boxes[i];
                    v.crates[i].transform.position = W(Shown(p), 1.75f + i * 0.34f + Mathf.Sin(t * 6f + i) * 0.03f);
                    Paint(v.crates[i], task.type == TaskType.Fuel ? FuelColor : task.batch >= 0 ? p.color : DeliveryColor);
                }
                UpdateFollowers(v, p, alive, t, seenFollowers);
            }
            RemoveFollowers(seenFollowers, t);
            foreach (int id in playerViews.Keys.Where(id => Sim.PlayerById(id) == null).ToList())
            {
                var v = playerViews[id];
                Destroy(v.body); Destroy(v.ring.gameObject); Destroy(v.route.gameObject); Destroy(v.glow.gameObject);
                foreach (var c in v.crates) Destroy(c);
                playerViews.Remove(id);
            }
        }

        readonly List<Vector3> routePts = new List<Vector3>();

        void DrawRoute(Player p, LineRenderer lr)
        {
            var map = Sim.map;
            const float h = 0.06f;
            routePts.Clear();
            routePts.Add(W(Shown(p), h));
            int cursor = p.node;
            if (p.nextNode >= 0) { routePts.Add(W(map.nodes[p.nextNode].pos, h)); cursor = p.nextNode; }
            for (int i = p.planIndex; i < p.plan.Count && routePts.Count < 80; i++)
            {
                var s = p.plan[i];
                if (s.node < 0 || s.node >= map.nodes.Count) continue;
                if (s.action == "move") { routePts.Add(W(map.nodes[s.node].pos, h)); cursor = s.node; }
                else if (s.action == "goto")
                {
                    var path = map.Path(cursor, s.node);
                    if (path == null) break;
                    foreach (int n in path) routePts.Add(W(map.nodes[n].pos, h));
                    cursor = s.node;
                }
            }
            lr.positionCount = routePts.Count;
            lr.SetPositions(routePts.ToArray());
            var c = p.color; c.a = 0.6f;
            lr.startColor = c; c.a = 0.25f; lr.endColor = c;
        }

        void UpdateZombies()
        {
            var alive = new HashSet<int>();
            foreach (var z in Sim.zombies)
            {
                alive.Add(z.id);
                if (!zombieViews.TryGetValue(z.id, out var v))
                {
                    var owner = Sim.PlayerById(z.fromPlayer);
                    v = new ZombieView
                    {
                        ring = Flat(dynRoot, circle, owner != null ? owner.color : new Color(0, 0, 0, 0.6f), 0.9f, 5, 0.035f),
                        intent = NewLine(dynRoot, 0.12f, 3),
                    };
                    BuildZombieModel(v, z);
                    zombieViews[z.id] = v;
                }
                AnimateZombie(v, z, Time.unscaledTime);
                v.ring.transform.position = W(Shown(z), 0.035f + GroundH(Shown(z)));

                bool show = !clean || z.mode != ZombieMode.Wander;
                v.intent.enabled = show;
                if (!show) continue;
                routePts.Clear();
                routePts.Add(W(Shown(z), 0.05f));
                foreach (int n in Sim.ZombiePath(z, 4)) routePts.Add(W(Sim.map.nodes[n].pos, 0.05f));
                v.intent.positionCount = routePts.Count;
                v.intent.SetPositions(routePts.ToArray());
                var c = z.mode == ZombieMode.Chase ? new Color(1f, 0.25f, 0.2f, 0.8f)
                      : z.mode == ZombieMode.Attracted ? new Color(1f, 0.85f, 0.3f, 0.6f)
                      : new Color(0.6f, 0.65f, 0.6f, 0.25f);
                v.intent.startColor = c; c.a *= 0.2f; v.intent.endColor = c;
            }
            foreach (int id in zombieViews.Keys.Where(id => !alive.Contains(id)).ToList())
            {
                StartZombieDeath(zombieViews[id]);
                zombieViews.Remove(id);
            }

            var live = new HashSet<int>();
            foreach (var n in Sim.noises)
            {
                live.Add(n.id);
                if (!noiseViews.TryGetValue(n.id, out var sr)) { sr = Flat(dynRoot, ringSprite, Color.white, 1f, 2, 0.05f); noiseViews[n.id] = sr; }
                sr.transform.position = W(n.pos, 0.05f);
                sr.transform.localScale = Vector3.one * n.radius * 2f;

                bool scream = n.source == "scream";
                float a = Mathf.Clamp01((n.until - Sim.time) / 3f) * (scream ? 0.4f + 0.15f * Mathf.Sin(Time.unscaledTime * 8f) : 0.14f);
                sr.color = scream ? new Color(1f, 0.3f, 0.3f, a) : new Color(1f, 0.9f, 0.5f, a);
            }
            foreach (int id in noiseViews.Keys.Where(id => !live.Contains(id)).ToList())
            {
                Destroy(noiseViews[id].gameObject);
                noiseViews.Remove(id);
            }
        }

        Vector2 PileBase(int node)
        {
            var n = Sim.map.nodes[node].pos;
            if (!facilityDir.TryGetValue(node, out var d)) return n + new Vector2(0.55f, -0.55f);
            return n + d * 0.9f + new Vector2(d.y, -d.x) * 0.75f;
        }

        Dictionary<int, List<GameTask>> TasksByNode()
        {
            var byNode = new Dictionary<int, List<GameTask>>();
            foreach (var task in Sim.tasks)
            {
                if (task.status != TaskStatus.Open && task.status != TaskStatus.Announced) continue;
                if (!byNode.TryGetValue(task.origin, out var list)) byNode[task.origin] = list = new List<GameTask>();
                list.Add(task);
            }
            foreach (var list in byNode.Values)
                list.Sort((x, y) => Order(x).CompareTo(Order(y)));
            return byNode;
        }

        static int Order(GameTask t) =>
            t.status == TaskStatus.Announced ? 9 : t.batch >= 0 ? 0 : t.urgent ? 1 : t.type == TaskType.Delivery ? 2 : 3;

        void UpdateTasks(float time)
        {
            var byNode = TasksByNode();
            foreach (var kv in byNode)
            {
                if (!piles.TryGetValue(kv.Key, out var pile)) piles[kv.Key] = pile = new Pile();
                var list = kv.Value;
                while (pile.items.Count < list.Count) pile.items.Add(Solid(PrimitiveType.Cube, dynRoot, Color.white, Vector3.one));
                for (int i = 0; i < pile.items.Count; i++)
                {
                    var go = pile.items[i];
                    bool on = i < list.Count;
                    go.SetActive(on);
                    while (pile.civilians.Count <= i) pile.civilians.Add(null);
                    if (pile.civilians[i] != null) pile.civilians[i].SetActive(false);
                    if (!on) continue;
                    var task = list[i];
                    bool announced = task.status == TaskStatus.Announced;

                    int col = i % 3, layer = i / 3;
                    Vector2 p = PileBase(kv.Key) + new Vector2(col * 0.55f, 0);
                    float s = announced ? 0.32f : 0.45f;
                    float bob = task.urgent && !announced ? 0.12f * Mathf.Sin(time * 6f) : 0f;
                    go.transform.position = W(p, s / 2f + layer * 0.5f + bob);
                    if (task.type == TaskType.Rescue)
                    {
                        if (pile.civilians[i] == null) pile.civilians[i] = MiniCapy(dynRoot, RescueColor);
                        var cv = pile.civilians[i];
                        go.SetActive(false);
                        cv.SetActive(true);
                        cv.transform.position = W(p, layer * 0.5f + bob + Mathf.Abs(Mathf.Sin(time * 5f + i)) * 0.06f);
                        cv.transform.rotation = Quaternion.Euler(0, 225f + Mathf.Sin(time * 2f + i) * 20f, Mathf.Sin(time * 5f + i) * 6f);
                        cv.transform.localScale = Vector3.one * (announced ? 0.75f : 1.05f);
                        continue;
                    }
                    go.transform.localScale = task.type == TaskType.Rescue ? new Vector3(0.3f, s * 0.8f, 0.3f)
                        : task.type == TaskType.Fuel ? new Vector3(0.4f, 0.65f, 0.4f) : Vector3.one * s;
                    var batch = Sim.BatchOf(task);
                    Color c = task.type == TaskType.Fuel ? FuelColor
                            : task.type == TaskType.Rescue ? RescueColor
                            : batch != null ? (batch.owner >= 0 && !batch.failed && Sim.TeamById(batch.owner) is Team owner ? owner.color : BatchColor)
                            : task.urgent ? UrgentColor : DeliveryColor;
                    if (announced) c *= 0.45f;
                    c.a = 1;
                    Paint(go, c);
                }
            }
            foreach (var kv in piles)
                if (!byNode.ContainsKey(kv.Key))
                {
                    foreach (var go in kv.Value.items) go.SetActive(false);
                    foreach (var cv in kv.Value.civilians) if (cv != null) cv.SetActive(false);
                }
        }

        class Banner { public string text, color; public float until; }
        readonly List<Banner> banners = new List<Banner>();
        GameEvent lastSeen;

        void UpdateBanners()
        {
            var events = Sim.events;
            int start = events.Count;
            while (start > 0 && events[start - 1] != lastSeen) start--;
            if (lastSeen == null || !events.Contains(lastSeen)) start = Mathf.Max(0, events.Count - 1);
            for (int i = start; i < events.Count; i++)
            {
                var e = events[i];
                bool major = gm.cfg.escape
                    ? e.type == "heli" || e.type == "incident" || e.type == "solved" || e.type == "incident_fail" || e.type == "eliminated" || e.type == "end"
                    : e.type == "eliminated" || e.type == "down" || e.type == "outbreak" ||
                      (e.type == "batch" && e.text.Contains("準時完成")) || e.type == "end";
                EventFx(e);
                if (!major) continue;

                string text = System.Text.RegularExpressions.Regex.Replace(e.text.TrimStart('✓', '✕', ' '), "（[^）]*）", "");

                int cut = -1;
                foreach (var mark in new[] { "!", "！", "..." })
                {
                    int at = text.IndexOf(mark, System.StringComparison.Ordinal);
                    if (at > 0 && (cut < 0 || at + mark.Length - 1 < cut)) cut = at + mark.Length - 1;
                }
                if (cut < 0) cut = text.IndexOf('：');
                if (cut > 0 && cut < text.Length - 1) text = text.Substring(0, cut + 1);
                banners.Add(new Banner { text = text, color = EventColor(e.type), until = Time.unscaledTime + 4f });
                if (director && e.node >= 0) CutTo(Sim.map.nodes[e.node].pos);
            }
            if (events.Count > 0) lastSeen = events[events.Count - 1];
            Milestones();
            banners.RemoveAll(b => Time.unscaledTime > b.until);
            if (banners.Count > 2) banners.RemoveRange(0, banners.Count - 2);
        }

        int milestoneVersion = -1, partsMilestone;
        bool zoneAnnounced, lastMinuteAnnounced;
        int departMark;

        void Milestones()
        {
            if (!gm.cfg.escape) return;
            if (milestoneVersion != Sim.mapVersion || Sim.status == MatchStatus.Lobby)
            {
                milestoneVersion = Sim.mapVersion; partsMilestone = 0; zoneAnnounced = lastMinuteAnnounced = false; departMark = 999;
            }
            if (Sim.status != MatchStatus.Running || Sim.partsRequired <= 0) return;
            void Add(string text, string color) => banners.Add(new Banner { text = text, color = color, until = Time.unscaledTime + 3.5f });
            int pct = Sim.parts * 100 / Sim.partsRequired;
            foreach (int m in new[] { 25, 50, 75 })
                if (pct >= m && partsMilestone < m && Sim.readyAt < 0) { partsMilestone = m; Add($"零件完成 {m}%！", Gold); }
            if (!zoneAnnounced && Sim.ZoneRadius < Sim.zoneMax - 0.01f) { zoneAnnounced = true; Add("安全區開始縮小！", Bad); }

            if (Sim.departAt >= 0)
                foreach (int s in new[] { 30, 15, 10 })
                    if (Sim.departAt - Sim.time <= s && departMark > s) { departMark = s; Add($"{s} 秒後起飛！還在外面的快回撤離點", s <= 10 ? Bad : Warn); }
            if (!lastMinuteAnnounced && !Sim.ClockStopped && Sim.TimeLeft <= 60f) { lastMinuteAnnounced = true; Add(Sim.departAt >= 0 ? "最後 1 分鐘，快回撤離點！" : "最後 1 分鐘！零件還差 " + (Sim.partsRequired - Sim.parts), Warn); }
        }

        void UpdateCamera()
        {
            var map = Sim.map;
            Vector2 center = map.Size * 0.5f;

            var inv = Quaternion.Inverse(cam.transform.rotation);
            float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
            foreach (var corner in new[] { new Vector2(-3, -3), new Vector2(map.Size.x + 3, -3), new Vector2(-3, map.Size.y + 3), map.Size + new Vector2(3, 3) })
            {
                var v = inv * W(corner);
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            float overview = Mathf.Max((maxY - minY) / 2f * 1.18f, (maxX - minX) / 2f / Mathf.Max(0.5f, cam.aspect) * 1.25f);

            var right = cam.transform.right; var right2 = new Vector2(right.x, right.z).normalized;
            var up = cam.transform.up; var up2 = new Vector2(up.x, up.z).normalized;
            Vector2 target = center + right2 * (overview * cam.aspect * 0.08f) + up2 * (overview * 0.06f);
            float size = overview;

            if (director && Sim.status == MatchStatus.Running)
            {
                directorTimer -= Time.unscaledDeltaTime;
                shotAge += Time.unscaledDeltaTime;
                if (directorTimer <= 0) NextShot();
            }
            else { directorPick = -1; directorSpot = null; overviewShot = false; shotAge = 0f; }

            int focus = follow >= 0 ? follow : directorPick;
            var fp = Sim.PlayerById(focus);
            float push = Mathf.Clamp01(shotAge / 7f);
            if (fp != null && fp.state != PlayerState.Eliminated)
            {

                var vel = focus == lastFocusId ? (fp.pos - lastFocusPos) / Mathf.Max(1e-3f, Time.unscaledDeltaTime) : Vector2.zero;
                lastFocusId = focus; lastFocusPos = fp.pos;
                lead = Vector2.Lerp(lead, Vector2.ClampMagnitude(vel * 0.5f, 1.4f), 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime));
                target = Shown(fp) + lead;
                size = follow >= 0 ? 6f : Mathf.Lerp(6.5f, 4.6f, push);
            }
            else if (follow < 0 && directorSpot.HasValue) { target = directorSpot.Value; size = Mathf.Lerp(6f, 4.4f, push); }
            PanCamera();
            if (manualTarget.HasValue && follow < 0 && !director) { target = manualTarget.Value; size = manualSize; }
            if (focusIncident >= 0 && follow < 0 && !director)
            {
                var fi = Sim.incidents.FirstOrDefault(i => i.id == focusIncident && i.status == "active");
                if (fi != null) { target = Sim.map.nodes[fi.node].pos; size = 6f; }
                else { manualTarget = Sim.map.nodes[Sim.incidents.FirstOrDefault(i => i.id == focusIncident)?.node ?? Sim.extraction].pos; manualSize = 6f; focusIncident = -1; }
            }
            if (camFocus != null)
            {
                var br = camFocus.StartsWith("bridge") ? Sim.map.edges.Where(e => e.bridge).ElementAtOrDefault(camFocus.Length > 6 ? camFocus[6] - '0' : 0) : null;
                if (br != null) target = (Sim.map.nodes[br.a].pos + Sim.map.nodes[br.b].pos) / 2f;
                else if (camFocus == "player") { var pl = Sim.players.FirstOrDefault(); if (pl != null) target = pl.pos; }
                else
                {
                    int id = camFocus == "ex" ? Sim.extraction : Sim.map.nodes.FirstOrDefault(nd => nd.type.ToString().ToLower() == camFocus)?.id ?? Sim.extraction;
                    target = FacilityPos(id);
                }
                size = camFocusSize;
            }
            if (galleryTarget.HasValue) { target = galleryTarget.Value; size = gallerySize; }
            size *= zoomMul;
            ApplyCamera(target, size);
        }

        int PickDirectorTarget()
        {
            int best = -1;
            float bestScore = 2.5f;
            var leaderTeam = Sim.Ranking().FirstOrDefault();
            foreach (var p in Sim.players)
            {
                if (p.state == PlayerState.Eliminated) continue;
                float s = Sim.zombies.Count(z => Vector2.Distance(z.pos, p.pos) < 6f) * 1.5f;
                if (p.state == PlayerState.Downed) s += 6f;
                if (p.team == leaderTeam) s += 1f;
                if (p.cargo.Count > 0) s += 0.5f;
                if (s > bestScore) { bestScore = s; best = p.id; }
            }
            return best;
        }

        void EnsureStyles()
        {
            if (label != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft JhengHei UI", "Microsoft JhengHei", "Microsoft YaHei", "PingFang TC", "Noto Sans CJK TC", "Segoe UI Symbol", "Arial" }, 18);
            GUIStyle Make(int size, FontStyle style, TextAnchor anchor = TextAnchor.MiddleLeft) =>
                new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = style, alignment = anchor, richText = true, normal = { textColor = Color.white }, wordWrap = false, clipping = TextClipping.Overflow };
            label = Make(18, FontStyle.Normal);
            small = Make(14, FontStyle.Normal);
            bold = Make(19, FontStyle.Bold);
            big = Make(30, FontStyle.Bold, TextAnchor.MiddleCenter);
            title = Make(46, FontStyle.Bold, TextAnchor.MiddleCenter);
            center = Make(18, FontStyle.Normal, TextAnchor.MiddleCenter);
            hero = Make(76, FontStyle.Bold, TextAnchor.MiddleCenter);
            tiny = Make(12, FontStyle.Normal);
        }

        GUIStyle hero, tiny;

        static string Spaced(string s) => string.Join("\u2009", s.ToCharArray());

        string JoinUrl => string.IsNullOrEmpty(gm.cfg.publicUrl) ? addresses : gm.cfg.publicUrl;

        void Box(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old; }

        void Bar(Rect r, float v, Color c)
        {
            Box(r, new Color(1, 1, 1, 0.12f));
            Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), c);
        }

        static string Clock(float sec) { sec = Mathf.Max(0, sec); return $"{(int)sec / 60:00}:{(int)sec % 60:00}"; }
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        void OnGUI()
        {
            if (gm == null || Sim == null || cam == null) return;
            EnsureStyles();
            HandleInput(Event.current);
            if (Event.current.type == EventType.Layout) return;

            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = 1080f;

            WorldLabels(s);
            if (gm.cfg.escape)
            {
                if (Sim.status == MatchStatus.Lobby && gm.countdownStart < 0) LobbyMinimal(W, H);
                else WhiteCatHud(W, H);
            }
            else { TopBar(W); Leaderboard(W); }
            if (Sim.status != MatchStatus.Ended) DrawBanners(W);
            if (!(gm.cfg.escape && Sim.status == MatchStatus.Lobby)) EventLog(W, H);
            if (!clean && Sim.status == MatchStatus.Ended) Help(W, H);
            if (Sim.status == MatchStatus.Lobby && !gm.cfg.escape) LobbyPanel(W, H);
            Countdown(W, H);
            if (Sim.status == MatchStatus.Ended)
            {
                float since = endedAt < 0 ? 0f : Time.unscaledTime - endedAt;
                if (since < EndSplashTime) EndSplash(W, H, since);
                else ResultsPanel(W, H);
            }
        }

        void HandleInput(Event e)
        {
            if (e.type == EventType.ScrollWheel)
            {
                zoomMul = Mathf.Clamp(zoomMul * (e.delta.y > 0 ? 1.1f : 0.9f), 0.3f, 2f);
                e.Use();
                return;
            }
            if (e.type != EventType.KeyDown) return;
            if (editingUrl && Sim.status == MatchStatus.Lobby) return;
            var alive = Sim.players.Where(p => p.state != PlayerState.Eliminated).Select(p => p.id).ToList();
            switch (e.keyCode)
            {

                case KeyCode.Space:
                    if (Sim.status == MatchStatus.Lobby) gm.StartMatch();
                    else if (Sim.status == MatchStatus.Ended) { gm.NextRound(); follow = -1; }
                    break;
                case KeyCode.Tab:
                    if (alive.Count == 0) { follow = -1; break; }
                    int i = alive.IndexOf(follow);
                    follow = i + 1 < alive.Count ? alive[i + 1] : -1;
                    director = false; manualTarget = null;
                    break;
                case KeyCode.C: director = !director; follow = -1; manualTarget = null; focusIncident = -1; break;
                case KeyCode.LeftArrow: CycleStudent(-1); break;
                case KeyCode.RightArrow: CycleStudent(1); break;
                case KeyCode.UpArrow: CycleIncident(-1); break;
                case KeyCode.DownArrow: CycleIncident(1); break;
                case KeyCode.H: clean = !clean; break;
                case KeyCode.F11:
                    if (Screen.fullScreen) Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
                    else Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
                    break;
                case KeyCode.Alpha0: case KeyCode.Escape: follow = -1; director = false; zoomMul = 1f; manualTarget = null; focusIncident = -1; break;
                case KeyCode.P: gm.speed = gm.speed == 0 ? 1 : 0; break;
                case KeyCode.Alpha1: gm.speed = 1; break;
                case KeyCode.Alpha2: gm.speed = 2; break;
                case KeyCode.Alpha3: gm.speed = 4; break;
                case KeyCode.Alpha4: gm.speed = 8; break;
                default: return;
            }
            e.Use();
        }

        Vector2 ToGui(Vector2 world, float s, float height = 0f)
        {
            var sp = cam.WorldToScreenPoint(W(world, height));
            return new Vector2(sp.x / s, (Screen.height - sp.y) / s);
        }

        void WorldLabels(float s)
        {
            var map = Sim.map;
            bool close = cam.orthographicSize < 14f;

            var byNode = TasksByNode();
            var style = CenterSmall();
            foreach (var n in map.nodes)
            {
                var lines = new List<string>();
                switch (n.type)
                {
                    case NodeType.Shelter: lines.Add($"<b>{(n.id == map.startNode ? "總部避難所" : "避難所")}</b>"); break;
                    case NodeType.Hospital: lines.Add("<b>醫院</b>"); break;
                    case NodeType.Supply: lines.Add(n.medkits > 0 ? $"<b>補給站</b> 醫療包×{n.medkits}" : "<b>補給站</b> <color=#999>（空）</color>"); break;
                    case NodeType.Factory: lines.Add(Sim.Powered(n) ? "<b>工廠</b>" : "<b>工廠</b> <color=#999>（停電）</color>"); break;
                    case NodeType.Generator: lines.Add(Sim.Powered(n) ? "<b>發電機</b> <color=#9be7b0>運轉中</color>" : "<b>發電機</b> <color=#ff8a75>故障</color>"); break;
                }
                if (gm.cfg.escape && n.id == Sim.extraction) { lines.Clear(); lines.Add("<color=#ffd54f><b>撤離點</b></color>"); }
                if (byNode.TryGetValue(n.id, out var here)) lines.AddRange(PileSummary(here));
                if (!close) lines.RemoveAll(l => l.Contains("公告×"));
                if (gm.cfg.escape && !close)
                    lines.RemoveAll(l => !l.Contains("撤離點") && !l.Contains("油桶×") && !l.Contains("故障"));
                if (!clean && close) lines.Add($"<color=#666>#{n.id}</color>");
                if (lines.Count == 0) continue;

                float h = n.type == NodeType.Road ? 1.0f : 2.4f;
                var g = ToGui(n.type == NodeType.Road ? n.pos : FacilityPos(n.id), s, h);
                float cw = lines.Max(l => style.CalcSize(new GUIContent(l)).x) + 12, ch = 18 * lines.Count + 4;
                var card = new Rect(g.x - cw / 2f, g.y - ch, cw, ch);
                bool onlyId = lines.Count == 1 && lines[0].StartsWith("<color=#666>");
                if (!onlyId) Box(card, new Color(0.02f, 0.03f, 0.06f, 0.4f));
                for (int i = 0; i < lines.Count; i++)
                    GUI.Label(new Rect(card.x, card.y + 2 + 18 * i, cw, 18), $"<color=#eee>{lines[i]}</color>", style);
            }

            foreach (var inc in Sim.incidents)
            {
                if (inc.status != "active") continue;
                var g = ToGui(Sim.map.nodes[inc.node].pos, s, 0f);
                var ic = IncidentColor(inc.kind);
                float left = inc.deadline - Sim.time;
                string people = inc.workers > 0 ? $"  <color={Good}>{inc.workers} 人</color>" : "";
                string when = inc.kind == "generator" || inc.kind == "bridge" ? $"<color={Bad}>待修</color>"
                            : $"<color={(left < 15 ? Bad : "#dfe6f3")}>{Clock(left)}</color>";
                string text = $"<b><color=#{Hex(ic)}>{inc.name}</color></b>  {when}{people}";
                float cw = Mathf.Max(150f, small.CalcSize(new GUIContent(text)).x + 22f);
                var card = new Rect(g.x - cw / 2f, g.y + 18, cw, 46);
                Box(card, new Color(0.02f, 0.03f, 0.06f, 0.82f));
                Box(new Rect(card.x, card.y, 4, card.height), ic);
                GUI.Label(new Rect(card.x + 10, card.y + 2, cw - 12, 20), text, small);
                Bar(new Rect(card.x + 10, card.y + 28, cw - 20, 7), inc.progress / Mathf.Max(0.1f, inc.need), ic);
            }

            foreach (var p in Sim.players)
            {
                if (p.state == PlayerState.Eliminated) continue;
                var g = ToGui(Shown(p), s, 2.0f + 0.34f * p.cargo.Count(id => Sim.TaskById(id)?.type != TaskType.Rescue));
                string extra = p.state == PlayerState.Downed ? $" <color=#ff5252>倒地，{p.downedTimer:0}s 後復活</color>" : "";

                string tag = p.team.units.Count > 1 ? Fit(p.team.name, CenterSmall(), 90) + $"#{p.unit + 1}" : Fit(p.name, CenterSmall(), 110);
                GUI.Label(new Rect(g.x - 100, g.y - 20, 200, 20), $"<b><color=#{Hex(p.color)}>{tag}</color></b>{extra}", CenterSmall());
                if (p.state == PlayerState.Active)
                {
                    const float bw = 46f;
                    var br = new Rect(g.x - bw / 2f, g.y + 1, bw, 5);
                    Box(new Rect(br.x - 1, br.y - 1, bw + 2, p.infection > 1f ? 11 : 7), new Color(0, 0, 0, 0.6f));
                    float hp = Mathf.Clamp01(p.hp / 100f);
                    Box(new Rect(br.x, br.y, bw * hp, 5), Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.35f, 0.9f, 0.4f), hp));
                    if (p.infection > 1f) Box(new Rect(br.x, br.y + 6, bw * Mathf.Clamp01(p.infection / 100f), 3), new Color(0.75f, 0.35f, 1f));
                }
            }
        }

        List<string> PileSummary(List<GameTask> list)
        {
            var lines = new List<string>();
            foreach (var g in list.Where(t => t.batch >= 0 && t.status == TaskStatus.Open).GroupBy(t => t.batch))
            {
                var b = Sim.batches[g.Key];
                lines.Add(b.failed || b.paid ? $"<color=#ffcc80>合約×{g.Count()}</color>"
                    : $"<color=#ffcc80>合約×{g.Count()} 獎金+{b.bonus} 限{Clock(b.deadline - Sim.time)}</color>");
            }
            var urgent = list.Where(t => t.urgent && t.status == TaskStatus.Open).ToList();
            if (urgent.Count > 0)
                lines.Add($"<color=#ffeb3b>限時×{urgent.Count} +{urgent.Max(t => t.reward + t.bonus)} 剩{Clock(urgent.Min(t => t.deadline) - Sim.time)}</color>");
            var fuelList = list.Where(t => t.type == TaskType.Fuel && t.status == TaskStatus.Open).ToList();
            if (fuelList.Count > 0) lines.Add($"<color=#ff7043><b>油桶×{fuelList.Count}</b></color>");
            var partList = list.Where(t => !t.urgent && t.type == TaskType.Part && t.status == TaskStatus.Open).ToList();
            if (partList.Count > 0) lines.Add($"<color=#ffb74d>零件×{partList.Sum(t => t.weight)}</color>");
            var normal = list.Where(t => t.batch < 0 && !t.urgent && t.type == TaskType.Delivery && t.status == TaskStatus.Open).ToList();
            if (normal.Count > 0) lines.Add($"<color=#ffb74d>配送×{normal.Count} 最高+{normal.Max(t => t.reward)}</color>");
            var rescue = list.Where(t => !t.urgent && t.type == TaskType.Rescue && t.status == TaskStatus.Open).ToList();
            if (rescue.Count > 0) lines.Add($"<color=#a5d6a7>救援×{rescue.Count} 最高+{rescue.Max(t => t.reward)}</color>");
            var soon = list.Where(t => t.status == TaskStatus.Announced).ToList();
            if (soon.Count > 0) lines.Add($"<color=#999>公告×{soon.Count}（{soon.Min(t => t.availableAt) - Sim.time:0}s 後開放）</color>");
            return lines;
        }

        void DrawBanners(float W)
        {
            var style = new GUIStyle(big) { fontSize = 24 };
            for (int i = 0; i < banners.Count; i++)
            {
                var b = banners[i];
                float a = Mathf.Clamp01(b.until - Time.unscaledTime);
                float tw = Mathf.Max(320, style.CalcSize(new GUIContent(b.text)).x + (gm.cfg.escape ? 100 : 60));
                ColorUtility.TryParseHtmlString(b.color, out var cc); cc.a = a;
                Rect r;
                if (gm.cfg.escape)
                {
                    EnsureHudStyles();
                    r = new Rect(W / 2f - tw / 2f, 150 + i * 54, tw, 46);
                    Pill(r, new Color(WcDark.r, WcDark.g, WcDark.b, 0.9f * a));
                    Pill(new Rect(r.x + 6, r.y + 6, 34, 34), cc);
                }
                else
                {
                    r = new Rect(W / 2f - tw / 2f, 172 + i * 52, tw, 44);
                    Box(r, new Color(0.02f, 0.03f, 0.06f, 0.8f * a));
                    Box(new Rect(r.x, r.y, 5, r.height), cc);
                }
                GUI.Label(gm.cfg.escape ? new Rect(r.x + 48, r.y, r.width - 64, r.height) : r, $"<color={b.color}>{b.text}</color>", style);
            }
        }

        GUIStyle centerSmall;
        GUIStyle CenterSmall() => centerSmall ?? (centerSmall = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });

        void HeroHud(float W)
        {
            GUI.Label(new Rect(28, 16, 900, 18), $"<color={Muted}>{Spaced("加入遊戲")}</color>", tiny);
            GUI.Label(new Rect(28, 32, 900, 24), $"<color={Accent}>{JoinUrl}</color>", label);
            int wt = Sim.map.nodes[0].district, et = Sim.map.nodes[Sim.map.EastNode].district;
            string area = wt == et ? GameSim.DistrictNames[wt] : $"{GameSim.DistrictNames[wt]} × {GameSim.DistrictNames[et]}";
            GUI.Label(new Rect(28, 58, 600, 18), $"<color={Muted}>{Spaced("地圖種子")}</color>  <b>{gm.cfg.seed}</b>   <color={Muted}>{Spaced("地區")}</color>  <b>{area}</b>", tiny);

            bool fuelPhase = Sim.readyAt >= 0;
            if (fuelPhase)
            {
                GUI.Label(new Rect(W / 2f - 400, 8, 800, 84),
                    $"<color=#ff7043>{Mathf.Min(Sim.fuel, Sim.fuelRequired)}</color><size=34><color={Muted}> / {Sim.fuelRequired} 桶油</color></size>", hero);
                GUI.Label(new Rect(W / 2f - 400, 88, 800, 22),
                    $"<color={Bad}>{Spaced("安全區外倒地就無法復活")}</color>", CenterSmall());
            }
            else
            {
                int shown = Mathf.Min(Sim.parts, Sim.partsRequired);
                GUI.Label(new Rect(W / 2f - 400, 8, 800, 84),
                    $"{shown}<size=34><color={Muted}> / {Sim.partsRequired} 零件</color></size>", hero);
                GUI.Label(new Rect(W / 2f - 400, 88, 800, 22),
                    $"<color={Muted}>{Spaced("修好直升機，一起逃出城市")}</color>", CenterSmall());
            }

            string state;
            if (Sim.status == MatchStatus.Lobby) state = $"<color={Muted}>等待開始</color>";
            else if (Sim.status == MatchStatus.Ended)
                state = Sim.classWon ? $"<color={Good}>直升機起飛！</color>" : $"<color={Bad}>沒能逃出城市</color>";
            else
            {
                string[] phaseColors = { "#81c784", "#ffd54f", "#ff8a65", "#ff5252" };
                int alive = Sim.players.Count(p => p.state != PlayerState.Eliminated);
                string heli = Sim.departAt >= 0 ? $"<color={Good}>起飛倒數 {Clock(Mathf.Max(0f, Sim.departAt - Sim.time))}</color>"
                            : Sim.readyAt >= 0 ? $"<color=#ff7043>加油中</color>  <color={Muted}>存活 {alive}/{Sim.players.Count}</color>"
                            : $"<color={Muted}>直升機修理中</color>";
                bool shrinking = Sim.ZoneRadius < Sim.zoneMax - 0.01f;
                string zone = shrinking ? $"<color={Bad}>安全區縮小中</color>" : $"<color={Muted}>安全區 {Clock(gm.cfg.zoneStart * gm.cfg.matchDuration / 600f - Sim.time)} 後縮小</color>";
                string buffs = "";
                if (Sim.time < Sim.speedBoostUntil) buffs += $"     <color={Good}>全員加速 {Clock(Sim.speedBoostUntil - Sim.time)}</color>";
                if (Sim.time < Sim.zombieStunUntil) buffs += $"     <color=#b39ddb>殭屍呆住 {Clock(Sim.zombieStunUntil - Sim.time)}</color>";
                if (Sim.time < Sim.guardDownUntil) buffs += $"     <color={Bad}>守衛倒下 {Clock(Sim.guardDownUntil - Sim.time)}</color>";
                state = $"<color={phaseColors[Sim.Phase]}>剩餘 <b>{Clock(Sim.TimeLeft)}</b></color>     {heli}{(shrinking && Sim.readyAt < 0 ? $"     {zone}" : "")}{buffs}";
            }
            GUI.Label(new Rect(W / 2f - 500, 112, 1000, 26), state, center);
            float frac = fuelPhase ? (Sim.fuelRequired > 0 ? Mathf.Clamp01((float)Sim.fuel / Sim.fuelRequired) : 1f)
                       : Sim.partsRequired > 0 ? Mathf.Clamp01((float)Sim.parts / Sim.partsRequired) : 0f;
            Bar(new Rect(W / 2f - 160, 142, 320, 6), frac,
                Sim.departAt >= 0 ? new Color(0.41f, 0.94f, 0.68f) : fuelPhase ? FuelColor : new Color(1f, 0.84f, 0.31f));
            Box(new Rect(W / 2f - 160, 151, 320 * Mathf.Clamp01(Sim.time / gm.cfg.matchDuration), 2), new Color(1, 1, 1, 0.35f));
        }

        void TopBar(float W)
        {
            var r = new Rect(W / 2f - 430, 14, 860, 64);
            Box(r, new Color(0, 0, 0, 0.72f));
            int phase = Sim.Phase;
            string[] phaseColors = { "#81c784", "#ffd54f", "#ff8a65", "#ff5252" };
            GUI.Label(new Rect(r.x + 20, r.y + 6, 300, 30), "<b>末日大逃殺</b>  <size=14><color=#999>MVP</color></size>", bold);
            GUI.Label(new Rect(r.x + 20, r.y + 34, 300, 24),
                Sim.status == MatchStatus.Running ? $"<color={phaseColors[phase]}>{GameSim.PhaseNames[phase]}</color>  剩餘 <b>{Clock(Sim.TimeLeft)}</b>"
                : Sim.status == MatchStatus.Lobby ? "等待開始"
                : gm.cfg.escape ? (Sim.classWon ? "<color=#a5d6a7>直升機起飛！</color>" : "<color=#ff5252>沒能逃出</color>")
                : Sim.classWon ? "<color=#a5d6a7>城市守住了！</color>" : "城市已淪陷", label);

            if (gm.cfg.escape)
            {
                string heliText = Sim.departAt >= 0 ? $"<color=#a5d6a7>直升機 {Clock(Mathf.Max(0f, Sim.departAt - Sim.time))} 後起飛！</color>" : "<color=#aaa>修好直升機才能逃</color>";
                GUI.Label(new Rect(r.x + 330, r.y + 6, 400, 24), $"零件  <b>{Mathf.Min(Sim.parts, Sim.partsRequired)} / {Sim.partsRequired}</b>   {heliText}", label);
                Bar(new Rect(r.x + 330, r.y + 38, 380, 14), Sim.partsRequired > 0 ? (float)Sim.parts / Sim.partsRequired : 0f,
                    Sim.departAt >= 0 ? new Color(0.4f, 0.85f, 0.4f) : new Color(1f, 0.75f, 0.2f));
            }
            else
            {
                string goal = gm.cfg.coop ? "  <color=#aaa>目標：撐到 10:00</color>" : "";
                GUI.Label(new Rect(r.x + 330, r.y + 6, 400, 24), $"城市感染程度  <b>{Sim.cityInfection:0}%</b>{goal}", label);
                Bar(new Rect(r.x + 330, r.y + 38, 380, 14), Sim.cityInfection / 100f, Color.Lerp(new Color(1f, 0.75f, 0.2f), InfectionColor, Sim.cityInfection / 100f));
            }

            foreach (float f in new[] { 0.25f, 0.5833f, 0.8333f })
                Box(new Rect(r.x + 330 + 380 * f, r.y + 34, 2, 22), new Color(1, 1, 1, 0.25f));
            Box(new Rect(r.x + 330, r.y + 55, 380 * Mathf.Clamp01(Sim.time / gm.cfg.matchDuration), 3), new Color(1, 1, 1, 0.6f));

            string spd = gm.speed == 0 ? "<color=#ff5252>暫停</color>" : gm.speed > 1 ? $"×{gm.speed:0}" : "";
            string cam = follow >= 0 ? "跟拍" : director ? "導播" : manualTarget.HasValue ? "手動" : "全景";
            GUI.Label(new Rect(r.x + 730, r.y + 6, 120, 24), $"<color=#aaa>{cam}</color> {spd}", label);
            GUI.Label(new Rect(r.x + 730, r.y + 34, 120, 24), $"<color=#7cb342>殭屍 {Sim.zombies.Count}</color>", label);
        }

        void Leaderboard(float W)
        {
            if (gm.cfg.coop && gm.cfg.squadSize <= 1) { ContributionBoard(W); return; }
            var ranking = Sim.Ranking();

            const float nameX = 62, nameW = 168, scoreX = 234, scoreW = 62, barX = 306, barW = 156;
            float rowH = 50f, w = barX + barW + 14;
            var r = new Rect(W - w - 16, 92, w, 44 + rowH * ranking.Count);
            Box(r, new Color(0, 0, 0, 0.72f));
            GUI.Label(new Rect(r.x + 14, r.y + 8, 200, 28), "<b>排行榜</b>", bold);
            GUI.Label(new Rect(r.x + barX, r.y + 8, barW, 28), "<color=#888>生命 / 感染 / 負重</color>", small);
            var scoreStyle = new GUIStyle(bold) { alignment = TextAnchor.MiddleRight };
            for (int i = 0; i < ranking.Count; i++)
            {
                var t = ranking[i];
                int n = t.units.Count;
                float y = r.y + 42 + i * rowH;
                bool dead = t.Alive == 0;
                if (t.units.Any(u => u.id == follow)) Box(new Rect(r.x + 4, y - 2, w - 8, rowH - 2), new Color(1, 1, 1, 0.08f));
                GUI.Label(new Rect(r.x + 12, y, 30, 24), $"<b>{i + 1}</b>", label);
                Box(new Rect(r.x + 40, y + 5, 14, 14), dead ? Color.gray : t.color);
                string shown = Fit(t.name, label, nameW);
                string name = dead ? $"<color=#777>{shown}</color>" : ResultWriter.IsBaseline(t) ? $"<color=#999>{shown}</color>" : shown;
                GUI.Label(new Rect(r.x + nameX, y, nameW, 24), name, label);
                string sub = n == 1 ? StateText(t.units[0])
                    : $"<color=#bbb>存活 {t.Alive}/{n}  配送{t.units.Sum(u => u.delivered)} 救援{t.units.Sum(u => u.rescued)}</color>";
                GUI.Label(new Rect(r.x + nameX, y + 22, nameW + scoreW, 20), sub, small);
                GUI.Label(new Rect(r.x + scoreX, y, scoreW, 24), $"<b>{t.Score}</b>", scoreStyle);

                float cw = barW / n;
                for (int k = 0; k < n; k++)
                {
                    var u = t.units[k];
                    float x = r.x + barX + k * cw, bw = cw - (n > 1 ? 4 : 0);
                    if (u.state == PlayerState.Eliminated) { Box(new Rect(x, y + 4, bw, 27), new Color(1, 1, 1, 0.05f)); continue; }
                    Bar(new Rect(x, y + 4, bw, 7), u.hp / 100f, u.state == PlayerState.Downed ? new Color(1f, 0.5f, 0.3f) : new Color(0.4f, 0.85f, 0.4f));
                    Bar(new Rect(x, y + 15, bw, 7), u.infection / 100f, new Color(0.7f, 0.3f, 0.9f));
                    Bar(new Rect(x, y + 26, bw, 5), Sim.Load(u) / (float)gm.cfg.capacity, new Color(0.9f, 0.7f, 0.3f));
                }
            }
        }

        void ContributionBoard(float W)
        {
            var ranking = Sim.Ranking();
            bool esc = gm.cfg.escape;
            const float w = 330, rowH = 26;
            float x = W - w - 24, y0 = 18;
            Box(new Rect(x - 12, y0 - 6, w + 24, 34 + rowH * ranking.Count), new Color(0.02f, 0.03f, 0.06f, 0.45f));
            GUI.Label(new Rect(x, y0, 200, 18), $"<color={Muted}>{Spaced(esc ? "倖存者" : "貢獻榜")}</color>", tiny);
            GUI.Label(new Rect(x + w - 100, y0, 100, 18), $"<color={Muted}>{Spaced("分數")}</color>", new GUIStyle(tiny) { alignment = TextAnchor.MiddleRight });
            var scoreStyle = new GUIStyle(bold) { alignment = TextAnchor.MiddleRight, fontSize = 17 };
            var name = new GUIStyle(label) { fontSize = 16 };
            for (int i = 0; i < ranking.Count; i++)
            {
                var t = ranking[i];
                var u = t.units[0];
                float y = y0 + 24 + i * rowH;
                if (u.id == follow) Box(new Rect(x - 8, y, w + 16, rowH - 2), new Color(1, 1, 1, 0.07f));
                bool gone = u.state == PlayerState.Eliminated;
                Box(new Rect(x, y + 9, 8, 8), u.state != PlayerState.Active ? new Color(1, 1, 1, 0.25f) : t.color);
                GUI.Label(new Rect(x + 16, y, 130, 24), u.state != PlayerState.Active ? $"<color={Muted}>{Fit(t.name, name, 125)}</color>" : Fit(t.name, name, 125), name);
                string stat = gone ? $"<color={Bad}>倒下了，沒能逃出</color>"
                    : u.state == PlayerState.Downed ? $"<color={Bad}>復活 {u.downedTimer:0}s</color>"
                    : esc ? (Sim.ComboOf(u) > 0 ? $"<color={Gold}><b>連續得分 ×{Sim.ComboOf(u) + 1}</b></color>" : "")
                    : $"<color={Muted}>-{u.cityReduced:0.0}%  倒 {u.downs}</color>";
                GUI.Label(new Rect(x + 150, y + 2, 170, 22), stat, tiny);
                GUI.Label(new Rect(x + w - 80, y, 80, 24), $"<color={Gold}>{t.Score}</color>", scoreStyle);
            }
        }

        static string Fit(string text, GUIStyle style, float width)
        {
            if (style.CalcSize(new GUIContent(text)).x <= width) return text;
            for (int len = text.Length - 1; len > 0; len--)
            {
                string s = text.Substring(0, len) + "…";
                if (style.CalcSize(new GUIContent(s)).x <= width) return s;
            }
            return "…";
        }

        string StateText(Player p)
        {
            switch (p.state)
            {
                case PlayerState.Eliminated: return $"<color=#ff5252>淘汰</color> <color=#777>{Clock(p.eliminatedAt)}</color>";
                case PlayerState.Downed: return $"<color=#ff8a65>復活 {p.downedTimer:0}s</color>";
            }
            string act = p.pending != null ? ActionName(p.pending.action)
                       : p.nextNode >= 0 ? "移動中"
                       : p.planIndex < p.plan.Count ? "執行中" : "<color=#888>待命</color>";
            return $"<color=#bbb>{act}  配送{p.delivered} 救援{p.rescued}</color>";
        }

        static string ActionName(string a)
        {
            switch (a)
            {
                case "pickup": return "領取中";
                case "dropoff": return "交付中";
                case "heal": return "治療中";
                case "use_medkit": return "使用醫療包";
                case "take_medkit": return "拿取醫療包";
                case "revive": return "救援隊友";
                case "wait": return "等待";
                default: return a;
            }
        }

        void EventLog(float W, float H)
        {
            if (gm.cfg.escape) { EventLogWc(W, H); return; }
            var shown = gm.cfg.escape ? Sim.events.Where(e => e.type != "done" && e.type != "respawn" && e.type != "titles" && e.type != "fail" && e.type != "urgent").ToList() : Sim.events;
            var list = shown.Skip(Mathf.Max(0, shown.Count - 5)).ToList();
            const float rowH = 26, w = 560;
            var r = new Rect(gm.cfg.escape ? W - 24 - w : 24, H - 24 - 16 - rowH * list.Count, w, 16 + rowH * list.Count);
            if (gm.cfg.escape) { EnsureHudStyles(); Pill(r, WcDark, roundBox); }
            else Box(r, new Color(0.02f, 0.03f, 0.06f, 0.55f));
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                float y = r.y + 8 + i * rowH;
                string c = EventColor(e.type);
                ColorUtility.TryParseHtmlString(c, out var cc);
                Box(new Rect(r.x, y + 3, 3, rowH - 6), cc);
                GUI.Label(new Rect(r.x + 12, y, 24, rowH), $"<color={c}>{EventIcon(e.type)}</color>", small);
                string text = System.Text.RegularExpressions.Regex.Replace(e.text.TrimStart('✓', '✕', ' '), @" ?#\d+", "");
                GUI.Label(new Rect(r.x + 34, y, w - 40, rowH), $"<color=#dfe6f3>{Fit(text, small, w - 46)}</color>", small);
            }
        }

        static string EventIcon(string type)
        {
            switch (type)
            {
                case "done": case "heli": case "batch": case "revive": case "respawn": case "solved": return "✓";
                case "combo": return "★";
                case "urgent": case "supply": return "★";
                case "down": case "fail": case "eliminated": case "incident_fail": return "✕";
                case "outbreak": case "failure": case "incident": case "roadblock": return "▲";
                default: return "·";
            }
        }

        static string EventColor(string type)
        {
            switch (type)
            {
                case "urgent": return "#ffeb3b";
                case "outbreak": case "eliminated": case "fail": return "#ff5252";
                case "down": return "#ff8a65";
                case "failure": return "#ffab40";
                case "done": case "revive": return "#a5d6a7";
                case "supply": case "repair": return "#81d4fa";
                case "batch": return "#ffcc80";
                case "incident": return "#ffab40";
                case "solved": return "#69f0ae";
                case "combo": return "#ffd54f";
                case "incident_fail": return "#ff5252";
                case "heli": return "#69f0ae";
                default: return "#e0e0e0";
            }
        }

        void Help(float W, float H)
        {
            GUI.Label(new Rect(W - 760, H - 34, 744, 24),
                "<color=#777>Space 開始／結束後回準備畫面　WASD／拖曳 移動鏡頭　←→ 看學員　↑↓ 看事件　滾輪 縮放　C 自動導播　0 全景　1-4 速度　P 暫停　H 乾淨轉播模式　F11 全螢幕</color>",
                new GUIStyle(small) { alignment = TextAnchor.MiddleRight });
        }

        void LobbyPanel(float W, float H)
        {
            var humans = Sim.teams.Where(t => !t.IsBot).ToList();
            var r = new Rect(W / 2f - 380, H / 2f - 230, 760, 400 + 26 * Mathf.Clamp(humans.Count - 4, 0, 4));
            Box(r, new Color(0.02f, 0.03f, 0.06f, 0.88f));
            GUI.Label(new Rect(r.x, r.y + 24, r.width, 60), "《末日大逃殺》", title);
            string mode = gm.cfg.escape ? "逃脫模式：全班修好直升機，一起逃出城市" : gm.cfg.coop ? "合作模式：全班一起撐過 10 分鐘" : gm.cfg.squadSize > 1 ? $"小隊模式（每隊 {gm.cfg.squadSize} 人）" : "單人模式";
            GUI.Label(new Rect(r.x, r.y + 84, r.width, 30), $"<color=#aaa>{mode}　|　種子 {gm.cfg.seed}　|　{gm.cfg.matchDuration / 60f:0.#} 分鐘　|　基準 Bot：隨機×{gm.cfg.randomBots} 貪婪×{gm.cfg.greedyBots}</color>", center);
            GUI.Label(new Rect(r.x, r.y + 120, r.width, 26), $"<color={Accent}>加入遊戲：{JoinUrl}</color>", center);
            GUI.Label(new Rect(r.x, r.y + 146, r.width, 24), "<color=#777>Colab 請透過 ngrok / cloudflared 公開網址連線</color>", center);
            GUI.Label(new Rect(r.x + 60, r.y + 184, 400, 28), $"<b>已加入的玩家（{humans.Count}）</b>", bold);
            for (int i = 0; i < humans.Count; i++)
            {
                string role = humans[i].units[0].role;
                float x = r.x + 80 + (i >= 8 ? 320 : 0), y = r.y + 216 + (i % 8) * 26;
                GUI.Label(new Rect(x, y, 300, 24), $"<color=#{Hex(humans[i].color)}>●</color> {humans[i].name}{(role != "" ? $"  <color=#ffcc80>{role}</color>" : "")}", label);
            }
            if (humans.Count == 0) GUI.Label(new Rect(r.x + 80, r.y + 216, 500, 24), "<color=#777>等待玩家呼叫 POST /join …</color>", label);
            GUI.Label(new Rect(r.x, r.yMax - 52, r.width, 32), "<b>按 Space 開始比賽</b>", big);
        }

        void Countdown(float W, float H)
        {
            float now = Time.unscaledTime;
            string text = null;
            float k = 0f;
            if (gm.countdownStart >= 0)
            {
                float t = now - gm.countdownStart;
                int n = Mathf.Clamp(3 - Mathf.FloorToInt(t), 1, 3);
                text = n.ToString();
                k = t - Mathf.Floor(t);
            }
            else if (now - gm.startedAt < 0.9f)
            {
                text = "Start!";
                k = (now - gm.startedAt) / 0.9f;
            }
            else if (gm.cfg.escape && Sim.status == MatchStatus.Running && Sim.departAt >= 0 && Sim.departAt - Sim.time <= 3f && Sim.departAt > Sim.time)
            {
                float left = Sim.departAt - Sim.time;
                int n = Mathf.CeilToInt(left);
                text = n.ToString();
                k = Mathf.Clamp01(n - left);
            }
            if (text == null) return;
            EnsureHudStyles();
            float pop = k < 0.18f ? Mathf.Lerp(1.9f, 0.95f, k / 0.18f) : k < 0.3f ? Mathf.Lerp(0.95f, 1f, (k - 0.18f) / 0.12f) : 1f;
            float alpha = k > 0.7f ? Mathf.Clamp01(1f - (k - 0.7f) / 0.3f) : 1f;
            var style = new GUIStyle(wcTitle) { fontSize = text == "Start!" ? 130 : 180 };
            var old = GUI.matrix;
            var pivot = new Vector2(W / 2f, H / 2f - 40f);
            GUI.matrix = old * Matrix4x4.TRS(pivot, Quaternion.identity, new Vector3(pop, pop, 1)) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
            var c = text == "Start!" ? new Color(0.87f, 0.80f, 0.75f, alpha) : new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(pivot.x - 500, pivot.y - 130, 1000, 260), $"<color=#{ColorUtility.ToHtmlStringRGBA(c)}>{text}</color>", style);
            GUI.matrix = old;
        }

        const float EndSplashTime = 5f;

        void EndSplash(float W, float H, float since)
        {
            Box(new Rect(0, 0, W, H), new Color(0, 0, 0, Mathf.Clamp01(since / 0.4f) * 0.55f));
            float pop = since < 0.25f ? Mathf.Lerp(2.6f, 0.92f, since / 0.25f) : since < 0.4f ? Mathf.Lerp(0.92f, 1f, (since - 0.25f) / 0.15f) : 1f;
            float shake = since < 0.6f ? Mathf.Sin(since * 70f) * 8f * (1f - since / 0.6f) : 0f;
            var style = new GUIStyle(title) { fontSize = 140 };
            var old = GUI.matrix;
            var pivot = new Vector2(W / 2f + shake, H / 2f - 40f);
            GUI.matrix = old * Matrix4x4.TRS(pivot, Quaternion.identity, new Vector3(pop, pop, 1)) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
            string color = !gm.cfg.escape ? "#ffffff" : Sim.classWon ? "#a5d6a7" : "#ff6e6e";
            GUI.Label(new Rect(pivot.x - 600, pivot.y - 100, 1200, 200), $"<color={color}>結束！</color>", style);
            GUI.matrix = old;
            if (since > 0.5f)
            {
                string sub = !gm.cfg.escape ? Sim.endReason
                    : Sim.classWon ? $"直升機起飛！{Sim.players.Count(p => p.escaped)}/{Sim.players.Count} 人逃出" : "沒能逃出城市";
                var c = new Color(1, 1, 1, Mathf.Clamp01((since - 0.5f) / 0.4f));
                GUI.Label(new Rect(0, H / 2f + 70, W, 50), $"<color=#{ColorUtility.ToHtmlStringRGBA(c)}>{sub}</color>", new GUIStyle(big) { fontSize = 36 });
            }
        }

        void ResultsPanel(float W, float H)
        {
            if (gm.cfg.coop && gm.cfg.squadSize <= 1) { CoopResultsPanel(W, H); return; }
            var ranking = Sim.Ranking();
            int rb = ResultWriter.BestBotScore(Sim, "random"), gb = ResultWriter.BestBotScore(Sim, "greedy");
            var r = new Rect(W / 2f - 420, H / 2f - 90 - 18 * ranking.Count, 840, 200 + 36 * ranking.Count);
            Box(r, new Color(0.02f, 0.03f, 0.06f, 0.9f));
            GUI.Label(new Rect(r.x, r.y + 18, r.width, 50), "城市淪陷・最終結算", title);
            GUI.Label(new Rect(r.x, r.y + 70, r.width, 26), $"<color=#aaa>{Sim.endReason}（{Clock(Sim.time)}）</color>", center);
            string[] cols = { "名次", "玩家", "分數", "配送", "救援", "狀態", "段位" };
            float[] xs = { 30, 100, 380, 470, 550, 630, 730 };
            for (int c = 0; c < cols.Length; c++) GUI.Label(new Rect(r.x + xs[c], r.y + 108, 120, 24), $"<color=#888>{cols[c]}</color>", label);
            for (int i = 0; i < ranking.Count; i++)
            {
                var t = ranking[i];
                float y = r.y + 140 + i * 36;
                string tier = ResultWriter.IsBaseline(t) ? "<color=#777>基準</color>" : TierName(ResultWriter.Tier(t.Score, rb, gb, ResultWriter.BenchmarkScore(Sim)));
                int alive = t.units.Count(u => u.state == PlayerState.Active);
                string state = alive == 0 ? "<color=#ff5252>全滅</color>"
                    : t.units.Count > 1 ? $"<color=#a5d6a7>存活 {alive}/{t.units.Count}</color>" : "<color=#a5d6a7>存活</color>";
                string[] vals =
                {
                    $"<b>{i + 1}</b>", $"<color=#{Hex(t.color)}>●</color> {t.name}", $"<b>{t.Score}</b>",
                    t.units.Sum(u => u.delivered).ToString(), t.units.Sum(u => u.rescued).ToString(), state, tier,
                };
                for (int c = 0; c < vals.Length; c++) GUI.Label(new Rect(r.x + xs[c], y, 300, 30), vals[c], label);
            }
            GUI.Label(new Rect(r.x, r.yMax - 44, r.width, 30), "<color=#aaa>按 Space 回到準備畫面（玩家保留）</color>", center);
        }

        void CoopResultsPanel(float W, float H)
        {
            var ranking = Sim.Ranking();
            float rowH = 30f;
            var r = new Rect(W / 2f - 460, H / 2f - 110 - rowH / 2f * ranking.Count, 920, 220 + rowH * ranking.Count);
            Box(r, new Color(0.02f, 0.03f, 0.06f, 0.9f));
            bool esc = gm.cfg.escape;
            string head = esc ? (Sim.classWon ? $"<color=#a5d6a7>直升機起飛！{Sim.players.Count(p => p.escaped)}/{Sim.players.Count} 人逃出</color>" : "<color=#ff5252>直升機沒修好</color>")
                              : Sim.classWon ? "<color=#a5d6a7>全班撐過了！城市守住了</color>" : "城市淪陷";
            GUI.Label(new Rect(r.x, r.y + 16, r.width, 50), head, title);
            string sub = esc
                ? (Sim.classWon ? $"比賽還剩 {Clock(gm.cfg.matchDuration - Sim.time)} 時起飛，搭上直升機的人 +{gm.cfg.escapeBonus} 分" : $"零件 {Sim.parts}/{Sim.partsRequired}，還差 {Sim.partsRequired - Sim.parts} 個，全班被困在城市裡")
                : Sim.classWon ? $"最後城市感染 {Sim.cityInfection:0}%，每人 +{gm.cfg.classBonus} 分"
                : $"在 {Clock(Sim.time)} 淪陷（再撐 {Clock(gm.cfg.matchDuration - Sim.time)} 就成功了）";
            GUI.Label(new Rect(r.x, r.y + 68, r.width, 26), $"<color=#aaa>{sub}</color>", center);
            string[] cols = esc ? new[] { "名次", "玩家", "分數", "零件", "救援", "事件", "油桶", "救人", "倒地", "逃出" }
                                : new[] { "名次", "玩家", "分工", "分數", "壓低感染", "配送", "救援", "倒地", "撤離" };
            float[] xs = esc ? new float[] { 24, 84, 320, 410, 495, 580, 660, 740, 815, 870 } : new float[] { 24, 84, 330, 420, 500, 610, 680, 750, 820 };

            var all = ranking.Select(t => t.units[0]).ToList();
            string Star(int v, System.Func<Player, int> stat) =>
                v > 0 && all.Count > 1 && v == all.Max(stat) ? $"{v}<color=#ffd54f> ★</color>" : v.ToString();
            if (!esc && ranking.All(t => t.units[0].role == "")) cols[2] = "";
            for (int c = 0; c < cols.Length; c++) GUI.Label(new Rect(r.x + xs[c], r.y + 104, 120, 24), $"<color=#888>{cols[c]}</color>", label);
            for (int i = 0; i < ranking.Count; i++)
            {
                var t = ranking[i];
                var u = t.units[0];
                float y = r.y + 134 + i * rowH;
                string[] vals = esc
                    ? new[]
                    {
                        $"<b>{i + 1}</b>", $"<color=#{Hex(t.color)}>●</color> {Fit(t.name, label, 220)}", $"<b>{t.Score}</b>",
                        Star(u.parts, q => q.parts), Star(u.rescued, q => q.rescued), Star(u.fixedCount, q => q.fixedCount),
                        Star(u.fuel, q => q.fuel), Star(u.revives, q => q.revives), u.downs.ToString(),
                        u.escaped ? "<color=#a5d6a7>✓</color>" : "<color=#777>-</color>",
                    }
                    : new[]
                    {
                        $"<b>{i + 1}</b>", $"<color=#{Hex(t.color)}>●</color> {Fit(t.name, label, 220)}", $"<color=#ffcc80>{u.role}</color>",
                        $"<b>{t.Score}</b>", $"<color=#a5d6a7>-{u.cityReduced:0.0}%</color>", u.delivered.ToString(), u.rescued.ToString(),
                        u.downs.ToString(), u.evacuated ? "<color=#a5d6a7>✓</color>" : "<color=#777>-</color>",
                    };
                for (int c = 0; c < vals.Length; c++) GUI.Label(new Rect(r.x + xs[c], y, 260, 28), vals[c], label);
            }
            GUI.Label(new Rect(r.x, r.yMax - 40, r.width, 30), "<color=#aaa>按 Space 回到準備畫面（玩家保留）</color>", center);
        }

        static string TierName(string tier)
        {
            switch (tier)
            {
                case "diamond": return "<color=#80deea>鑽石</color>";
                case "gold": return "<color=#ffd54f>黃金</color>";
                case "silver": return "<color=#cfd8dc>白銀</color>";
                case "bronze": return "<color=#bcaaa4>青銅</color>";
                default: return "<color=#777>未定級</color>";
            }
        }
    }
}
