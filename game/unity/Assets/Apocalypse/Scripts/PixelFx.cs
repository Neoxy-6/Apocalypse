using UnityEngine;

namespace Apocalypse
{
    // 把 3D 畫面縮成低解析度、減色後再放大，做出像素風。UI 是之後才畫的，不受影響。
    public class PixelFx : MonoBehaviour
    {
        public int height = 216;
        public float levels = 10f, dither = 0.6f;
        Material mat;

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null)
            {
                var sh = Shader.Find("Hidden/ApocalypsePixel");
                if (sh == null || !sh.isSupported) { Graphics.Blit(src, dst); return; }
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            int h = Mathf.Clamp(height, 64, src.height);
            int w = Mathf.Max(1, Mathf.RoundToInt(h * (float)src.width / src.height));

            // 一半一半縮小，每次平均 2x2，避免直接取樣造成的閃爍
            var cur = src;
            RenderTexture tmp = null;
            while (cur.height / 2 >= h * 2)
            {
                var next = RenderTexture.GetTemporary(cur.width / 2, cur.height / 2, 0, src.format);
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(cur, next);
                if (tmp != null) RenderTexture.ReleaseTemporary(tmp);
                tmp = next; cur = next;
            }
            var small = RenderTexture.GetTemporary(w, h, 0, src.format);
            small.filterMode = FilterMode.Point;
            mat.SetFloat("_Levels", levels);
            mat.SetFloat("_Dither", dither);
            if (cur != src) cur.filterMode = FilterMode.Bilinear;
            var reduced = RenderTexture.GetTemporary(w, h, 0, src.format);
            reduced.filterMode = FilterMode.Point;
            Graphics.Blit(cur, small);
            Graphics.Blit(small, reduced, mat, 0);
            Graphics.Blit(reduced, dst);
            RenderTexture.ReleaseTemporary(small);
            RenderTexture.ReleaseTemporary(reduced);
            if (tmp != null) RenderTexture.ReleaseTemporary(tmp);
        }
    }
}
