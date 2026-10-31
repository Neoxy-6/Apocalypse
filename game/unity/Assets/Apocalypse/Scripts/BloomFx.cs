using UnityEngine;

namespace Apocalypse
{

    public class BloomFx : MonoBehaviour
    {
        public float threshold = 0.84f, intensity = 0.75f, vignette = 1.0f, saturation = 1.12f;
        const int Iterations = 6;
        Material mat;
        readonly RenderTexture[] levels = new RenderTexture[Iterations];

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null)
            {
                var sh = Shader.Find("Hidden/ApocalypseBloom");
                if (sh == null || !sh.isSupported) { Graphics.Blit(src, dst); return; }
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            mat.SetFloat("_Threshold", threshold);
            mat.SetFloat("_Intensity", intensity);
            mat.SetFloat("_Vignette", vignette);
            mat.SetFloat("_Saturation", saturation);

            int w = src.width / 2, h = src.height / 2, n = 1;
            levels[0] = RenderTexture.GetTemporary(w, h, 0, src.format);
            Graphics.Blit(src, levels[0], mat, 0);
            for (; n < Iterations; n++)
            {
                w /= 2; h /= 2;
                if (w < 4 || h < 4) break;
                levels[n] = RenderTexture.GetTemporary(w, h, 0, src.format);
                Graphics.Blit(levels[n - 1], levels[n], mat, 1);
            }
            for (int k = n - 2; k >= 0; k--)
            {
                Graphics.Blit(levels[k + 1], levels[k], mat, 2);
                RenderTexture.ReleaseTemporary(levels[k + 1]);
                levels[k + 1] = null;
            }
            mat.SetTexture("_BloomTex", levels[0]);
            Graphics.Blit(src, dst, mat, 3);
            RenderTexture.ReleaseTemporary(levels[0]);
            levels[0] = null;
        }
    }
}
