using UnityEngine;

namespace Apocalypse
{
    // 流暢模式：關掉光暈和反鋸齒。慢電腦連續幾秒低於 40 fps 會自動開，L 鍵手動切換，-lowfx 一開始就開
    public partial class GameView
    {
        BloomFx bloom;
        bool lowFx;
        float slowFor, frameAvg = 1f / 60f, qualityStart = -1f;
        int defaultAA = -1;

        void InitQuality()
        {
            QualitySettings.vSyncCount = 0;          // 用 targetFrameRate 鎖 60，好電腦不會白跑幾百 fps、筆電不會過熱降頻
            Application.targetFrameRate = 60;
            defaultAA = QualitySettings.antiAliasing;
            bloom = cam.GetComponent<BloomFx>();
            qualityStart = Time.unscaledTime;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-lowfx") >= 0) SetLowFx(true, false);
        }

        void SetLowFx(bool on, bool announce)
        {
            lowFx = on;
            if (bloom) bloom.enabled = !on;
            QualitySettings.antiAliasing = on ? 0 : Mathf.Max(0, defaultAA);
            Debug.Log($"[Apocalypse] 流暢模式 {(on ? "開" : "關")}");
            if (announce)
                banners.Add(new Banner { text = on ? "已切換流暢模式（L 切回）" : "已關閉流暢模式（L 切換）", color = Gold, until = Time.unscaledTime + 2.5f });
        }

        void AutoQuality()
        {
            if (lowFx || galleryMode || Time.captureFramerate > 0 || qualityStart < 0) return;
            float dt = Time.unscaledDeltaTime;
            frameAvg = Mathf.Lerp(frameAvg, dt, 0.05f);
            if (Time.unscaledTime - qualityStart < 6f || !Application.isFocused) { slowFor = 0f; return; }
            slowFor = frameAvg > 1f / 40f ? slowFor + dt : 0f;
            if (slowFor > 3f) SetLowFx(true, true);
        }
    }
}
