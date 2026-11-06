using System.Collections.Generic;
using UnityEngine;

namespace Apocalypse
{
    // 像素風物件：圓的東西改成方塊堆出來的形狀，材質換成有像素格子的貼圖。
    public partial class GameView
    {
        static Material pixelBase;
        static readonly Dictionary<PrimitiveType, Mesh> voxelMeshes = new Dictionary<PrimitiveType, Mesh>();

        static Material PixelBase()
        {
            if (pixelBase != null) return pixelBase;
            var sh = Shader.Find("Apocalypse/PixelLit");
            if (sh == null || !sh.isSupported)
                return pixelBase = Resources.Load<Material>("ApocalypseLit") ?? new Material(Shader.Find("Standard"));
            pixelBase = new Material(sh) { mainTexture = PixelNoise() };
            pixelBase.SetFloat("_Density", 10f);
            return pixelBase;
        }

        // 16x16 的灰階雜訊：每格亮度差一點，偶爾有比較暗的點，看起來像手繪像素
        static Texture2D PixelNoise()
        {
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var rnd = new System.Random(1234);
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++)
            {
                float v = 0.9f + (float)rnd.NextDouble() * 0.16f;
                if (rnd.NextDouble() < 0.06) v = 0.76f;
                px[i] = new Color(v, v, v, 1f);
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // 方塊版的球、圓柱、膠囊：用幾個重疊的長方體拼出階梯狀的輪廓，大小跟 Unity 內建的一樣
        static Mesh VoxelMesh(PrimitiveType type)
        {
            if (voxelMeshes.TryGetValue(type, out var m) && m != null) return m;
            const float a = 0.6f, b = 0.84f;
            (Vector3 size, Vector3 center)[] boxes;
            switch (type)
            {
                case PrimitiveType.Sphere:
                    boxes = new[] { (new Vector3(1, a, a), Vector3.zero), (new Vector3(a, 1, a), Vector3.zero), (new Vector3(a, a, 1), Vector3.zero), (new Vector3(b, b, b), Vector3.zero) };
                    break;
                case PrimitiveType.Cylinder:
                    boxes = new[] { (new Vector3(1, 2, a), Vector3.zero), (new Vector3(a, 2, 1), Vector3.zero), (new Vector3(b, 2, b), Vector3.zero) };
                    break;
                case PrimitiveType.Capsule:
                    boxes = new[] { (new Vector3(1, 1.2f, a), Vector3.zero), (new Vector3(a, 1.2f, 1), Vector3.zero), (new Vector3(b, 1.68f, b), Vector3.zero), (new Vector3(a, 2, a), Vector3.zero) };
                    break;
                default:
                    return null;
            }
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var (size, center) in boxes)
            {
                var h = size / 2f;
                foreach (var d in dirs)
                {
                    var u = d.y != 0 ? Vector3.right : Vector3.up;
                    var v = Vector3.Cross(d, u);
                    int s = verts.Count;
                    foreach (var (x, y) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                    {
                        var p = d + u * x + v * y;
                        verts.Add(center + Vector3.Scale(p, h));
                        norms.Add(d);
                    }
                    tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                }
            }
            m = new Mesh { name = "voxel_" + type };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            voxelMeshes[type] = m;
            return m;
        }

        // 取代 GameObject.CreatePrimitive：方塊照舊，圓的換成方塊版
        static GameObject PixelPrimitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type == PrimitiveType.Sphere || type == PrimitiveType.Cylinder || type == PrimitiveType.Capsule ? PrimitiveType.Cube : type);
            Object.Destroy(go.GetComponent<Collider>());
            var mesh = VoxelMesh(type);
            if (mesh != null) go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.name = type.ToString();
            return go;
        }
    }
}
