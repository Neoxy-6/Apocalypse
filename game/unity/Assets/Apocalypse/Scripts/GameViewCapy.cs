using System.Collections.Generic;
using UnityEngine;

namespace Apocalypse
{
    public partial class GameView
    {
        [System.Serializable] class CapyGroup { public string name; public float[] pivot, v, n, uv; public int[] t; }
        [System.Serializable] class CapyData
        {
            public string texture; public CapyGroup[] groups; public float height, eyeOut;
            public float[] headTop, eyeL, eyeR, bodyCenter, bodySize, snout;
        }

        class CapyRig
        {
            public Transform root, head, body, armL, armR, legL, legR;
            public Renderer eyeL, eyeR, bodyRenderer;
        }

        static CapyData capyData;
        static Texture2D capyTex;
        static readonly Dictionary<string, Mesh> capyMeshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<int, Material> capyMats = new Dictionary<int, Material>();

        static Vector3 V3(float[] a) => new Vector3(a[0], a[1], a[2]);

        static CapyData Capy()
        {
            if (capyData != null) return capyData;
            capyData = JsonUtility.FromJson<CapyData>(Resources.Load<TextAsset>("capybara").text);
            capyTex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            capyTex.LoadImage(System.Convert.FromBase64String(capyData.texture));
            capyTex.filterMode = FilterMode.Point;
            foreach (var g in capyData.groups)
            {
                int n = g.v.Length / 3;
                var verts = new Vector3[n]; var norms = new Vector3[n]; var uvs = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    verts[i] = new Vector3(g.v[i * 3], g.v[i * 3 + 1], g.v[i * 3 + 2]);
                    norms[i] = new Vector3(g.n[i * 3], g.n[i * 3 + 1], g.n[i * 3 + 2]);
                    uvs[i] = new Vector2(g.uv[i * 2], g.uv[i * 2 + 1]);
                }
                var mesh = new Mesh { name = "capy_" + g.name, vertices = verts, normals = norms, uv = uvs, triangles = g.t };
                mesh.RecalculateBounds();
                capyMeshes[g.name] = mesh;
            }
            return capyData;
        }

        static Material CapyMat(Color tint)
        {
            int k = ColorKey(tint);
            if (capyMats.TryGetValue(k, out var m) && m != null) return m;
            Capy();
            if (litBase == null) litBase = Resources.Load<Material>("ApocalypseLit") ?? new Material(Shader.Find("Standard"));
            m = new Material(litBase) { color = tint, mainTexture = capyTex, enableInstancing = true };
            capyMats[k] = m;
            return m;
        }

        static Renderer CapyPart(Transform parent, string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = capyMeshes[name];
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        CapyRig BuildCapy(Transform parent, Color tint, float scale = 1f)
        {
            var data = Capy();
            var mat = CapyMat(tint);
            var rig = new CapyRig { root = new GameObject("capybara").transform };
            rig.root.SetParent(parent, false);
            rig.root.localScale = Vector3.one * scale;
            Transform Pivot(string name)
            {
                var g = System.Array.Find(data.groups, x => x.name == name);
                var t = new GameObject(name + "Pivot").transform;
                t.SetParent(rig.root, false);
                t.localPosition = V3(g.pivot);
                return t;
            }
            rig.body = Pivot("body"); rig.bodyRenderer = CapyPart(rig.body, "body", mat);
            rig.head = Pivot("head"); CapyPart(rig.head, "head", mat);
            rig.eyeL = CapyPart(rig.head, "eyeL", mat);
            rig.eyeR = CapyPart(rig.head, "eyeR", mat);
            rig.armL = Pivot("armL"); CapyPart(rig.armL, "armL", mat);
            rig.armR = Pivot("armR"); CapyPart(rig.armR, "armR", mat);
            rig.legL = Pivot("legL"); CapyPart(rig.legL, "legL", mat);
            rig.legR = Pivot("legR"); CapyPart(rig.legR, "legR", mat);
            return rig;
        }

        // 繞在身上的一圈衣服，用來分辨隊伍或身分
        void CapyVest(CapyRig rig, Color c)
        {
            var d = Capy();
            var center = V3(d.bodyCenter) - V3(capyData.groups[System.Array.FindIndex(d.groups, g => g.name == "body")].pivot);
            var size = V3(d.bodySize);
            Part(rig.body, PrimitiveType.Cube, c, center + new Vector3(0, -0.07f, 0.02f), new Vector3(size.x * 1.05f, size.y * 0.22f, size.z * 0.94f));
        }

        // 殭屍王：放大的綠色卡皮巴拉，紅眼睛、頭上一頂金色王冠
        CapyRig ZombieKing(Transform parent, float scale, bool crown = true)
        {
            var rig = BuildCapy(parent, ZombieTint * 0.85f, scale);
            var red = UnlitMat(new Color(1f, 0.12f, 0.06f));
            rig.eyeL.sharedMaterial = red; rig.eyeR.sharedMaterial = red;
            CapyVest(rig, new Color(0.3f, 0.12f, 0.14f));
            rig.armL.localRotation = rig.armR.localRotation = Quaternion.Euler(-50f, 0, 0);
            if (crown)
            {
                var gold = new Color(1f, 0.82f, 0.2f);
                var top = CapyHeadTop;
                Part(rig.head, PrimitiveType.Cube, gold, top + new Vector3(0, 0.03f, -0.02f), new Vector3(0.3f, 0.06f, 0.3f), default, true);
                for (int i = 0; i < 5; i++)
                    Part(rig.head, PrimitiveType.Cube, gold, top + new Vector3(Mathf.Cos(i * 1.256f) * 0.12f, 0.1f, Mathf.Sin(i * 1.256f) * 0.12f - 0.02f), new Vector3(0.05f, 0.1f, 0.05f), default, true);
            }
            return rig;
        }

        static Vector3 CapyHeadTop => V3(Capy().headTop);
        static Vector3 CapyEye(bool left) => V3(left ? Capy().eyeL : Capy().eyeR);
    }
}
