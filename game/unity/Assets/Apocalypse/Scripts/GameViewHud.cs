using System.Linq;
using UnityEngine;

namespace Apocalypse
{

    public partial class GameView
    {
        static readonly Color WcDark = new Color(0.16f, 0.14f, 0.15f, 0.78f);
        static readonly Color WcTaupe = new Color(0.43f, 0.38f, 0.39f, 0.92f);
        static readonly Color WcBeige = new Color(0.87f, 0.80f, 0.75f, 1f);
        static readonly Color WcFuel = new Color(0.95f, 0.62f, 0.45f, 1f);
        const string WcBeigeHex = "#ddcdc0", WcSoftHex = "#a8999b";

        GUIStyle pill, roundBox, wcTime, wcTag, wcName, wcScore, wcRank, wcSmall, wcTitle;
        Texture2D dotTex, circleTex;

        static Texture2D RoundTex(int size, float radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(radius - d + 0.5f));
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        void EnsureHudStyles()
        {
            if (pill != null) return;
            pill = new GUIStyle { normal = { background = RoundTex(64, 31.5f) }, border = new RectOffset(31, 31, 31, 31) };
            roundBox = new GUIStyle { normal = { background = RoundTex(48, 14f) }, border = new RectOffset(16, 16, 16, 16) };
            dotTex = RoundTex(16, 8f);
            circleTex = RoundTex(128, 64f);
            GUIStyle Make(int size, FontStyle fs, TextAnchor a) =>
                new GUIStyle(label) { fontSize = size, fontStyle = fs, alignment = a, richText = true, clipping = TextClipping.Overflow, wordWrap = false };
            wcTime = Make(72, FontStyle.Bold, TextAnchor.UpperRight);
            wcTag = Make(17, FontStyle.Bold, TextAnchor.MiddleCenter);
            wcName = Make(17, FontStyle.Bold, TextAnchor.MiddleLeft);
            wcScore = Make(19, FontStyle.Bold, TextAnchor.MiddleRight);
            wcRank = Make(15, FontStyle.Bold, TextAnchor.MiddleCenter);
            wcSmall = Make(18, FontStyle.Bold, TextAnchor.MiddleLeft);
            wcTitle = Make(84, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        readonly System.Collections.Generic.Dictionary<int, GUIStyle> pillStyles = new System.Collections.Generic.Dictionary<int, GUIStyle>();

        GUIStyle PillStyle(float height)
        {
            int k = Mathf.Max(4, Mathf.RoundToInt(height));
            if (pillStyles.TryGetValue(k, out var st)) return st;
            int half = (k + 1) / 2, w = half * 2 + 1;
            float rad = k / 2f;
            var tex = new Texture2D(w, k, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * k];
            for (int y = 0; y < k; y++)
                for (int x = 0; x < w; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, rad, w - rad), cy = k / 2f;
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    px[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(rad - d + 0.5f));
                }
            tex.SetPixels(px);
            tex.Apply();
            st = new GUIStyle { normal = { background = tex }, border = new RectOffset(half, half, 0, 0) };
            pillStyles[k] = st;
            return st;
        }

        void Pill(Rect r, Color c, GUIStyle st = null)
        {
            if (Event.current.type != EventType.Repaint || r.width <= 0 || r.height <= 0) return;
            var old = GUI.color; GUI.color = c;
            if (st != null) st.Draw(r, false, false, false, false);
            else if (r.width <= r.height + 0.5f) GUI.DrawTexture(r, circleTex);
            else PillStyle(r.height).Draw(r, false, false, false, false);
            GUI.color = old;
        }

        static string Hx(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        void WhiteCatHud(float W, float H)
        {
            EnsureHudStyles();
            bool fuelPhase = Sim.readyAt >= 0;
            float frac = fuelPhase ? (Sim.fuelRequired > 0 ? Mathf.Clamp01((float)Sim.fuel / Sim.fuelRequired) : 1f)
                                   : Sim.partsRequired > 0 ? Mathf.Clamp01((float)Sim.parts / Sim.partsRequired) : 0f;
            var fill = fuelPhase ? WcFuel : WcBeige;

            var av = new Rect(16, 14, 96, 96);
            Pill(av, WcTaupe, roundBox);
            Pill(new Rect(av.x + 5, av.y + 5, av.width - 10, av.height - 10), WcDark, roundBox);
            GUI.Label(new Rect(av.x, av.y + 8, av.width, 54), $"<size=34><color={Hx(fill)}>{Mathf.FloorToInt(frac * 100)}</color></size><size=18><color={WcSoftHex}>%</color></size>", wcTag);
            GUI.Label(new Rect(av.x, av.y + 58, av.width, 26), $"<color={WcSoftHex}>{(fuelPhase ? "油桶" : "零件")}</color>", wcTag);

            var bar = new Rect(124, 28, 560, 38);
            Pill(bar, WcTaupe);
            float fw = Mathf.Max(bar.height, bar.width * frac);
            if (frac > 0.001f) Pill(new Rect(bar.x, bar.y, fw, bar.height), fill);
            if (fw > bar.height + 4) Pill(new Rect(bar.x + 12, bar.y + 6, fw - 24, 6), new Color(1, 1, 1, 0.22f));

            string tag = fuelPhase ? $"油桶 {Mathf.Min(Sim.fuel, Sim.fuelRequired)}/{Sim.fuelRequired}"
                       : Sim.parts >= Sim.partsRequired && Sim.partsRequired > 0 ? $"直升機組裝中 {Clock(Mathf.Max(0f, Sim.RepairGate - Sim.time))}"
                       : $"零件 {Mathf.Min(Sim.parts, Sim.partsRequired)}/{Sim.partsRequired}";
            float tw = wcTag.CalcSize(new GUIContent(tag)).x + 36;
            var tagR = new Rect(bar.x + 26, bar.yMax + 8, tw, 32);
            Pill(tagR, WcBeige);
            GUI.Label(tagR, $"<color=#3a3233>{tag}</color>", wcTag);
            if (fuelPhase)
                GUI.Label(new Rect(tagR.x, tagR.yMax + 10, 400, 28), "<color=#ff8a75>安全區外倒地無人救援將無法復活</color>", wcSmall);

            string clock = Clock(Sim.TimeLeft);
            int lead = 0;
            while (lead < clock.Length - 1 && (clock[lead] == '0' || clock[lead] == ':')) lead++;
            string digits = Sim.ClockStopped ? "#9be7b0" : "#ffffff";
            string timeText = $"<color={WcSoftHex}>{clock.Substring(0, lead)}</color><color={digits}>{clock.Substring(lead)}</color>";
            GUI.Label(new Rect(W - 424, 0, 400, 86), timeText, wcTime);

            float clockW = wcTime.CalcSize(new GUIContent(clock)).x;
            InfectionDial(new Rect(W - 24 - clockW - 92, 10, 70, 70), Sim.cityInfection / 100f);

            GUI.Label(new Rect(W - 600, 90, 582, 34),
                      $"<color={WcBeigeHex}>{GameSim.PhaseNames[Sim.Phase]}</color><color={WcSoftHex}> · </color><color=#c9e3a8>現存殭屍 {Sim.zombies.Count}</color>",
                      new GUIStyle(wcSmall) { alignment = TextAnchor.MiddleRight, fontSize = 20 });
            GoalBar(new Vector2(tagR.xMax + 12, tagR.center.y - 22), fuelPhase, frac);
            string buffs = "";
            if (Sim.time < Sim.speedBoostUntil) buffs += $"<color=#9be7b0>全員加速 {Clock(Sim.speedBoostUntil - Sim.time)}</color>   ";
            if (Sim.time < Sim.zombieStunUntil) buffs += $"<color=#c7b3ff>殭屍僵直 {Clock(Sim.zombieStunUntil - Sim.time)}</color>   ";
            if (Sim.time < Sim.infectionGuardUntil) buffs += $"<color=#9be7b0>感染抑制 {Clock(Sim.infectionGuardUntil - Sim.time)}</color>   ";
            if (Sim.time < Sim.guardDownUntil) buffs += $"<color=#ff8a75>守衛倒下 {Clock(Sim.guardDownUntil - Sim.time)}</color>   ";
            if (buffs != "") GUI.Label(new Rect(W - 600, 128, 582, 28), buffs, new GUIStyle(wcSmall) { alignment = TextAnchor.MiddleRight, fontSize = 16 });

            SurvivorList(H);
            IncidentList(W, H);
        }

        void GoalBar(Vector2 at, bool fuelPhase, float frac)
        {
            string goal = Sim.departAt >= 0 ? $"<color=#9be7b0>起飛倒數 {Clock(Mathf.Max(0f, Sim.departAt - Sim.time))}</color>"
                        : fuelPhase ? $"<color=#ffb59c>收集油桶 {Mathf.Min(Sim.fuel, Sim.fuelRequired)}/{Sim.fuelRequired}</color>"
                        : $"<color=#f6f1ee>直升機修理進度 {Mathf.FloorToInt(frac * 100)}%</color>";
            if (!fuelPhase)
            {
                var gens = Sim.Generators.ToList();
                int on = gens.Count(g => g.powered);
                if (gens.Count > 0)
                    goal += on == gens.Count ? $"<color={WcSoftHex}>　·　</color><color=#9be7b0>發電機 {on}/{gens.Count} 運轉</color>"
                          : $"<color={WcSoftHex}>　·　</color><color=#ff8a75>發電機 {on}/{gens.Count} 運轉，故障區工廠已停止生產零件</color>";
            }
            var st = new GUIStyle(wcName) { fontSize = 18 };
            var tagSt = new GUIStyle(wcTag) { fontSize = 14 };
            float tw = tagSt.CalcSize(new GUIContent("目標")).x + 22;
            float w = tw + 40 + st.CalcSize(new GUIContent(goal)).x;
            var r = new Rect(at.x, at.y, w, 44);
            Pill(r, WcDark);
            var tagR = new Rect(r.x + 10, r.y + 10, tw, 24);
            Pill(tagR, WcBeige);
            GUI.Label(tagR, "<color=#3a3233>目標</color>", tagSt);
            GUI.Label(new Rect(tagR.xMax + 12, r.y, w, r.height), goal, st);
        }

        void InfectionDial(Rect r, float frac)
        {
            frac = Mathf.Clamp01(frac);
            const int n = 40;
            var c = r.center;
            float rad = r.width / 2f - 5f;
            var hot = frac < 0.5f ? Color.Lerp(new Color(0.55f, 0.85f, 0.45f), new Color(1f, 0.8f, 0.35f), frac * 2f)
                                  : Color.Lerp(new Color(1f, 0.8f, 0.35f), new Color(0.95f, 0.35f, 0.5f), (frac - 0.5f) * 2f);
            Pill(new Rect(r.x, r.y, r.width, r.height), WcDark);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * rad;
                bool on = i < Mathf.RoundToInt(frac * n);
                float d = on ? 7f : 5f;
                Pill(new Rect(p.x - d / 2f, p.y - d / 2f, d, d), on ? hot : WcTaupe);
            }
            GUI.Label(new Rect(r.x, r.y + 14, r.width, 30), $"<color=#{ColorUtility.ToHtmlStringRGB(hot)}>{Mathf.RoundToInt(frac * 100)}%</color>", new GUIStyle(wcTag) { fontSize = 19 });
            GUI.Label(new Rect(r.x, r.y + 38, r.width, 20), $"<color={WcSoftHex}>感染</color>", new GUIStyle(wcTag) { fontSize = 12, fontStyle = FontStyle.Normal });
        }

        void SurvivorList(float H)
        {
            var ranking = Sim.Ranking();
            const float x = 16, w = 390, rowH = 36, top = 190, bottom = 40;
            float maxRows = Mathf.Floor((H - bottom - top) / rowH);
            int rows = (int)Mathf.Min(ranking.Count, maxRows);
            float y0 = Mathf.Max(top, (H - rows * rowH) / 2f + 50f);
            y0 = Mathf.Min(y0, H - bottom - rows * rowH);
            for (int i = 0; i < ranking.Count && i < maxRows; i++)
            {
                var t = ranking[i];
                var u = t.units[0];
                float y = y0 + i * rowH;

                bool gone = u.state == PlayerState.Eliminated || (Sim.status == MatchStatus.Ended && !u.escaped),
                     downed = !gone && u.state == PlayerState.Downed;
                Pill(new Rect(x, y, w, rowH - 6), u.id == follow ? new Color(0.30f, 0.26f, 0.27f, 0.9f) : WcDark);
                Pill(new Rect(x + 4, y + 4, 34, rowH - 14), i < 3 ? WcBeige : WcTaupe);
                GUI.Label(new Rect(x + 4, y + 4, 34, rowH - 14), $"<color={(i < 3 ? "#3a3233" : "#f2ebe6")}>{i + 1}</color>", wcRank);
                var dot = gone || downed ? new Color(1, 1, 1, 0.25f) : t.color;
                Pill(new Rect(x + 46, y + 10, 11, 11), dot);
                string stat = gone ? "<color=#ff8a75>沒能逃出</color>"
                    : downed ? $"<color=#ff8a75>倒地等待救援 {u.downedTimer:0}s</color>"
                    : Sim.ComboOf(u) > 0 ? $"<color={WcBeigeHex}>×{Sim.ComboOf(u) + 1}</color>" : "";
                string nm = Fit(t.name, wcName, stat.Length > 0 ? 120 : 160);
                GUI.Label(new Rect(x + 62, y, 170, rowH - 6), gone || downed ? $"<color={WcSoftHex}>{nm}</color>" : $"<color=#f6f1ee>{nm}</color>", wcName);
                // 狀態靠右、貼在分數左邊，字再長也不會蓋到分數
                GUI.Label(new Rect(x + w - 92 - 126, y, 120, rowH - 6), stat, new GUIStyle(wcSmall) { fontSize = 13, alignment = TextAnchor.MiddleRight });
                GUI.Label(new Rect(x + w - 92, y, 80, rowH - 6), $"<color={WcBeigeHex}>{t.Score}</color>", wcScore);
            }
        }

        System.Collections.Generic.List<Incident> ActiveIncidents() =>
            Sim.incidents.Where(i => i.status == "active").OrderBy(i => i.deadline).ThenBy(i => i.id).ToList();

        void IncidentList(float W, float H)
        {
            var list = ActiveIncidents();
            if (list.Count == 0) return;
            const float w = 330, rowH = 50;
            float x = W - w - 16, top = 170, bottom = 230;
            int rows = (int)Mathf.Min(list.Count, Mathf.Floor((H - top - bottom) / rowH));
            float y0 = Mathf.Max(top, (H - rows * rowH) / 2f);
            for (int i = 0; i < rows; i++)
            {
                var inc = list[i];
                float y = y0 + i * rowH;
                var c = IncidentColor(inc.kind);
                float left = inc.deadline - Sim.time;
                Pill(new Rect(x, y, w, rowH - 8), inc.id == focusIncident ? new Color(0.30f, 0.26f, 0.27f, 0.92f) : WcDark);
                Pill(new Rect(x + 14, y + 15, 12, 12), c);
                GUI.Label(new Rect(x + 34, y, 170, rowH - 18), $"<color=#f6f1ee>{inc.name}</color>", wcName);
                string ppl = inc.workers > 0 ? $"<color=#9be7b0>{inc.workers} 人</color>  " : "";
                string when = inc.kind == "generator" || inc.kind == "bridge" ? "<color=#ff8a75>待修</color>" : $"<color={(left < 15 ? "#ff8a75" : WcBeigeHex)}>{Clock(Mathf.Max(0, left))}</color>";
                GUI.Label(new Rect(x + w - 150, y, 136, rowH - 18), $"{ppl}{when}",
                          new GUIStyle(wcSmall) { fontSize = 16, alignment = TextAnchor.MiddleRight });
                var bar = new Rect(x + 34, y + rowH - 20, w - 52, 6);
                Pill(bar, WcTaupe);
                float f = Mathf.Clamp01(inc.progress / Mathf.Max(0.1f, inc.need));
                if (f > 0.01f) Pill(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * f), bar.height), c);
            }
        }

        void EventLogWc(float W, float H)
        {
            EnsureHudStyles();
            var shown = Sim.events.Where(e => e.type != "done" && e.type != "respawn" && e.type != "titles" && e.type != "fail" && e.type != "urgent").ToList();
            var list = shown.Skip(Mathf.Max(0, shown.Count - 5)).ToList();
            const float rowH = 40, gap = 6, w = 560;
            var st = new GUIStyle(wcName) { fontSize = 16 };
            var tagSt = new GUIStyle(wcTag) { fontSize = 13 };
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                float a = Mathf.Lerp(0.55f, 1f, (i + 1f) / list.Count);
                float y = H - 24 - (list.Count - i) * (rowH + gap) + gap;
                var r = new Rect(W - 24 - w, y, w, rowH);
                Pill(r, new Color(WcDark.r, WcDark.g, WcDark.b, WcDark.a * a));
                var (label, col) = EventKind(e.type);
                Pill(new Rect(r.x + 15, r.y + 14, 12, 12), new Color(col.r, col.g, col.b, a));
                float tw = tagSt.CalcSize(new GUIContent(label)).x + 20;
                var tagR = new Rect(r.x + 36, r.y + 9, tw, 22);
                Pill(tagR, new Color(WcBeige.r, WcBeige.g, WcBeige.b, a));
                GUI.Label(tagR, $"<color=#3a3233>{label}</color>", tagSt);
                string text = System.Text.RegularExpressions.Regex.Replace(e.text.TrimStart('✓', '✕', ' '), @" ?#\d+", "");
                float tx = tagR.xMax + 10;
                GUI.Label(new Rect(tx, r.y, r.xMax - tx - 16, rowH), $"<color=#f6f1ee{(int)(a * 255):x2}>{Fit(text, st, r.xMax - tx - 20)}</color>", st);
            }
        }

        static (string label, Color color) EventKind(string type)
        {
            switch (type)
            {
                case "solved": case "heli": return ("完成", new Color(0.55f, 0.85f, 0.65f));
                case "revive": return ("救援", new Color(0.55f, 0.85f, 0.65f));
                case "incident": return ("事件", new Color(0.98f, 0.72f, 0.45f));
                case "incident_fail": return ("失敗", new Color(1f, 0.50f, 0.45f));
                case "down": return ("倒地", new Color(1f, 0.60f, 0.48f));
                case "eliminated": return ("淘汰", new Color(1f, 0.45f, 0.42f));
                case "outbreak": case "failure": return ("警報", new Color(1f, 0.55f, 0.42f));
                case "roadblock": return ("封路", new Color(1f, 0.55f, 0.42f));
                case "combo": return ("連續得分", new Color(0.98f, 0.85f, 0.50f));
                case "supply": case "repair": case "batch": return ("物資", WcBeige);
                case "join": return ("加入", new Color(0.62f, 0.78f, 0.95f));
                default: return ("消息", new Color(0.66f, 0.60f, 0.61f));
            }
        }

        const string UrlControl = "lobbyUrl";
        bool editingUrl;
        Rect urlRect;

        void LoadSavedUrl()
        {
            if (string.IsNullOrEmpty(gm.cfg.publicUrl)) gm.cfg.publicUrl = PlayerPrefs.GetString("publicUrl", "");
        }

        void LobbyMinimal(float W, float H)
        {
            EnsureHudStyles();
            Box(new Rect(0, 0, W, H), new Color(0.05f, 0.04f, 0.05f, 0.55f));
            GUI.Label(new Rect(0, H / 2f - 230, W, 110), "<color=#f6f1ee>末日大逃殺</color>", wcTitle);

            editingUrl = false;
            float listTop = H / 2f - 90;

            var humans = Sim.teams.Where(t => !t.IsBot).ToList();
            GUI.Label(new Rect(0, listTop, W, 34), $"<color=#f6f1ee>已加入 {humans.Count} 人</color>", new GUIStyle(wcTag) { fontSize = 24 });
            var chip = new GUIStyle(wcName) { fontSize = 18 };
            const float chipH = 38, gap = 10;
            float maxW = Mathf.Min(1400, W - 120), y = listTop + 50;
            var row = new System.Collections.Generic.List<(string name, Color c, float w)>();
            void Flush()
            {
                float total = row.Sum(r => r.w) + gap * Mathf.Max(0, row.Count - 1), x = W / 2f - total / 2f;
                foreach (var (name, c, w) in row)
                {
                    Pill(new Rect(x, y, w, chipH), WcDark);
                    Pill(new Rect(x + 14, y + 13, 12, 12), c);
                    GUI.Label(new Rect(x + 34, y, w - 40, chipH), $"<color=#f6f1ee>{name}</color>", chip);
                    x += w + gap;
                }
                row.Clear();
                y += chipH + gap;
            }
            float rowW = 0;
            foreach (var t in humans)
            {
                string name = Fit(t.name, chip, 200);
                float w = chip.CalcSize(new GUIContent(name)).x + 52;
                if (row.Count > 0 && rowW + gap + w > maxW) { Flush(); rowW = 0; }
                row.Add((name, t.color, w));
                rowW += (row.Count > 1 ? gap : 0) + w;
            }
            if (row.Count > 0) Flush();
            if (humans.Count == 0)
                GUI.Label(new Rect(0, y, W, 30), $"<color={WcSoftHex}>等待學員加入…</color>", new GUIStyle(wcTag) { fontSize = 18, fontStyle = FontStyle.Normal });
        }
    }
}
