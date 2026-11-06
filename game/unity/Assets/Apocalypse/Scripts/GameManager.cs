using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        public GameConfig cfg;
        public GameSim sim;
        public ApiServer server;
        public float speed = 1f;

        public const float CountdownTime = 3f;
        public float countdownStart = -1f, startedAt = -9f;
        float endedFor;
        int loopIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("Apocalypse");
            DontDestroyOnLoad(go);
            go.AddComponent<GameManager>();
        }

        void Awake()
        {
            Instance = this;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            cfg = GameConfig.Load();
            sim = new GameSim(cfg);
            server = new ApiServer();
            server.Start(cfg.port);

            var argv = System.Environment.GetCommandLineArgs();
            bool wantsShots = argv.Contains("-capture") || argv.Contains("-captureEvery") || argv.Contains("-gallery") || argv.Contains("-record");
            if (!Application.isBatchMode || (wantsShots && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null))
                gameObject.AddComponent<GameView>();
        }

        string capturePath;
        float captureAt = -1f;

        string captureDir;
        float captureEvery = -1f, nextCapture;
        bool capturedEnd;
        float endSeenAt = -1f;

        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-capture");
            if (i >= 0 && i + 2 < args.Length && float.TryParse(args[i + 2], out float sec))
            {
                capturePath = args[i + 1];
                captureAt = sec;
            }
            i = System.Array.IndexOf(args, "-captureEvery");
            if (i >= 0 && i + 2 < args.Length && float.TryParse(args[i + 2], out float every))
            {
                captureDir = args[i + 1];
                captureEvery = every;
                Directory.CreateDirectory(captureDir);
            }
            i = System.Array.IndexOf(args, "-record");
            if (i >= 0 && i + 1 < args.Length)
            {
                int fps = 30;
                if (i + 2 < args.Length) int.TryParse(args[i + 2], out fps);
                StartCoroutine(Record(args[i + 1], Mathf.Clamp(fps, 1, 60)));
            }
        }

        // 錄影：鎖定每幀的時間步長，不管存檔多慢，畫面都照固定 fps 前進；每幀（含 UI）存成一張 JPG
        System.Collections.IEnumerator Record(string dir, int fps)
        {
            Directory.CreateDirectory(dir);
            Time.captureFramerate = fps;
            var eof = new WaitForEndOfFrame();
            int frame = 0, endFrame = -1;
            while (true)
            {
                yield return eof;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"f{frame:00000}.jpg"), tex.EncodeToJPG(88));
                Destroy(tex);
                frame++;
                if (sim.status == MatchStatus.Ended && endFrame < 0) endFrame = frame;
                if (endFrame >= 0 && frame - endFrame > fps * 9) { Application.Quit(); yield break; }
            }
        }

        public static void CaptureTo(string path) => Capture(path);

        static void Capture(string path)
        {
            if (!Application.isBatchMode) { ScreenCapture.CaptureScreenshot(path); return; }
            var cam = Camera.main;
            if (cam == null) return;
            const int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24);
            var old = cam.targetTexture;
            float oldAspect = cam.aspect;
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = old;
            cam.aspect = oldAspect;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        void CaptureTick()
        {
            if (captureEvery <= 0) return;
            if (sim.status == MatchStatus.Running && sim.time >= nextCapture)
            {
                Capture(Path.Combine(captureDir, $"t{(int)sim.time:000}.png"));
                nextCapture = sim.time + captureEvery;
                capturedEnd = false;
            }
            else if (sim.status == MatchStatus.Ended && !capturedEnd)
            {
                if (endSeenAt < 0) endSeenAt = Time.unscaledTime;
                if (Time.unscaledTime - endSeenAt < 5.7f) return;
                endSeenAt = -1f;
                Capture(Path.Combine(captureDir, "end.png"));
                capturedEnd = true;
                nextCapture = 0;
            }
        }

        void OnDestroy() => server?.Stop();
        void OnApplicationQuit() => server?.Stop();

        void Update()
        {
            server.Pump(Handle);

            if (sim.status == MatchStatus.Lobby && cfg.autoStart && sim.teams.Count(t => !t.IsBot) >= Mathf.Max(1, cfg.autoStartPlayers))
                StartMatch();
            if (countdownStart >= 0 && Time.unscaledTime - countdownStart >= CountdownTime)
            {
                countdownStart = -1f;
                if (sim.status == MatchStatus.Lobby) { sim.StartMatch(); startedAt = Time.unscaledTime; }
            }

            if (sim.status == MatchStatus.Ended && cfg.autoLoopDelay > 0)
            {
                endedFor += Time.unscaledDeltaTime;
                if (endedFor >= cfg.autoLoopDelay)
                {
                    endedFor = 0;
                    NextRound();
                    if (sim.teams.Count(t => !t.IsBot) > 0) StartMatch();
                }
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f) * cfg.timeScale * speed;
            int steps = Mathf.CeilToInt(dt / 0.05f);
            for (int i = 0; i < steps; i++) sim.Tick(dt / steps);

            RecordReplay();
            CaptureTick();
            if (captureAt > 0 && Time.unscaledTime >= captureAt)
            {
                Capture(capturePath);
                captureAt = -1f;
                Invoke(nameof(Quit), 1.5f);
            }
        }

        void Quit() => Application.Quit();

        public const float ReplayEvery = 0.5f;
        readonly List<string> replayFrames = new List<string>();
        float nextReplayAt;
        bool replayEnded, replayStale;
        string replayHeader;
        int replayPlayers = -1;

        void RecordReplay()
        {
            if (sim.status == MatchStatus.Running)
            {
                if (replayStale) { replayFrames.Clear(); nextReplayAt = 0f; replayEnded = false; replayStale = false; replayPlayers = -1; }
                if (replayPlayers != sim.players.Count) { replayHeader = Protocol.ReplayHeader(sim, ReplayEvery); replayPlayers = sim.players.Count; }
                if (sim.time >= nextReplayAt)
                {
                    replayFrames.Add(Protocol.ReplayFrame(sim));
                    nextReplayAt = sim.time + ReplayEvery;
                }
            }
            else if (sim.status == MatchStatus.Ended && !replayEnded && replayFrames.Count > 0)
            {
                replayFrames.Add(Protocol.ReplayFrame(sim));
                replayEnded = true;
            }
            else if (sim.status == MatchStatus.Lobby) replayStale = true;
        }

        public void StartMatch()
        {
            if (sim.status == MatchStatus.Lobby && countdownStart < 0) countdownStart = Time.unscaledTime;
        }

        public void ResetMatch() { speed = 1f; countdownStart = -1f; sim.ResetWorld(); }

        public void NextRound()
        {
            if (cfg.practiceSeeds.Length > 0) cfg.seed = cfg.practiceSeeds[++loopIndex % cfg.practiceSeeds.Length];
            ResetMatch();
        }

        void Handle(ApiServer.Request req)
        {
            switch (req.method + " " + req.path)
            {
                case "GET /":
                    req.response = $"{{\"ok\":true,\"game\":\"apocalypse-mvp\",\"status\":\"{sim.status.ToString().ToLowerInvariant()}\",\"players\":{sim.players.Count}}}";
                    return;

                case "GET /state":
                    req.response = JsonUtility.ToJson(Protocol.BuildState(sim, sim.FindByToken(req.query["token"])));
                    return;

                case "POST /join":
                {
                    var body = Parse<JoinRequest>(req.body);
                    if (sim.status != MatchStatus.Lobby && !cfg.allowLateJoin) { Reply(req, 409, "比賽已開始，無法加入"); return; }
                    string name = body?.name ?? "";
                    if (name.Length > 24) name = name.Substring(0, 24);
                    var team = sim.AddTeam(name);
                    string role = (body?.role ?? "").Trim();
                    foreach (var u in team.units) u.role = role.Length > 6 ? role.Substring(0, 6) : role;
                    req.response = JsonUtility.ToJson(new JoinResponse
                    {
                        ok = true, player_id = team.units[0].id, token = team.token,
                    });
                    return;
                }

                case "POST /plan":
                {
                    var body = Parse<PlanRequest>(req.body);
                    var team = sim.FindByToken(body?.token);
                    if (team == null) { Reply(req, 401, "token 無效"); return; }
                    if (body.unit < 0 || body.unit >= team.units.Count) { Reply(req, 400, $"unit 必須在 0..{team.units.Count - 1}"); return; }
                    var p = team.units[body.unit];
                    var steps = (body.steps ?? new StepDto[0]).Select(Protocol.FromDto).ToList();
                    string err = sim.SubmitPlan(p, steps, body.append, body.start_node);
                    if (err != null) Reply(req, 400, err);
                    else req.response = JsonUtility.ToJson(new OkResponse { ok = true, error = "" });
                    return;
                }

                case "POST /admin/start":
                case "POST /admin/reset":
                {
                    var body = Parse<AdminRequest>(req.body);
                    if (body?.key != cfg.adminKey) { Reply(req, 403, "admin key 錯誤"); return; }
                    if (req.path.EndsWith("start")) StartMatch(); else ResetMatch();
                    req.response = JsonUtility.ToJson(new OkResponse { ok = true, error = "" });
                    return;
                }

                case "GET /replay":
                    req.response = replayFrames.Count == 0 ? "{\"ok\":false,\"error\":\"還沒有可以重播的比賽\"}"
                        : "{\"ok\":true,\"header\":" + replayHeader + ",\"frames\":[" + string.Join(",", replayFrames) + "]}";
                    return;

                case "GET /map":
                    req.response = string.IsNullOrEmpty(sim.mapJson) ? "{\"ok\":false,\"error\":\"這場用的是隨機地圖\"}" : sim.mapJson;
                    return;

                case "GET /results":
                    req.response = string.IsNullOrEmpty(sim.lastResultJson) ? "{\"ok\":false,\"error\":\"尚無結果\"}" : sim.lastResultJson;
                    return;
            }

            if (req.method == "GET" && req.path.StartsWith("/sdk/"))
            {
                string file = Path.GetFileName(req.path);
                string full = Path.Combine(Application.streamingAssetsPath, "sdk", file);
                if (file.EndsWith(".py") && File.Exists(full))
                {
                    req.contentType = "text/x-python; charset=utf-8";
                    req.response = File.ReadAllText(full);
                    return;
                }
            }
            Reply(req, 404, "找不到 " + req.method + " " + req.path);
        }

        static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<T>(json); } catch { return null; }
        }

        static void Reply(ApiServer.Request req, int code, string error)
        {
            req.code = code;
            req.response = JsonUtility.ToJson(new OkResponse { ok = false, error = error });
        }
    }
}
