using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 札の付いた真鍮の鍵を手続きで焼く。
    ///
    /// 外部素材を使わないのは、ライセンス表記の管理を増やしたくないから。
    /// 鍵は「輪 + 軸 + 刻み + 札」の4部品でできていて、
    /// この程度の形なら拾ってくるより作った方が早いし、
    /// 大きさも手に持つ前提で自由に決められる。
    ///
    /// ⚠️ サブメッシュを2つに分けてある（0=真鍮 / 1=札）。
    ///    1つのマテリアルで塗ると札まで金属になり、
    ///    サーモ視点で「金属＝冷たい」に引きずられて札が読めなくなる。
    ///
    /// 出力: Assets/_BLIND/Art/Models/Key/BrassKey.asset
    /// </summary>
    public static class BlindKeyBaker
    {
        const string Dir = "Assets/_BLIND/Art/Models/Key";
        const string MatDir = "Assets/_BLIND/Art/Materials";

        // 手に持つ前提の寸法(m)。VRで摘まめる大きさにしてある。
        const float BowR = 0.024f;   // 輪の半径
        const float BowT = 0.006f;   // 輪の太さ
        const float ShankR = 0.005f;   // 軸の半径
        const float ShankL = 0.095f;   // 軸の長さ
        const float TagW = 0.052f, TagH = 0.034f, TagT = 0.002f;

        class Build
        {
            public List<Vector3> v = new List<Vector3>();
            public List<Vector3> n = new List<Vector3>();
            public List<Vector2> uv = new List<Vector2>();
            public List<int> t = new List<int>();

            /// <summary>直方体。中心と大きさと回転で置く。</summary>
            public void Box(Vector3 c, Vector3 s, Quaternion rot)
            {
                Vector3 h = s * 0.5f;
                Vector3[] p = {
                    new Vector3(-h.x,-h.y,-h.z), new Vector3( h.x,-h.y,-h.z),
                    new Vector3( h.x, h.y,-h.z), new Vector3(-h.x, h.y,-h.z),
                    new Vector3(-h.x,-h.y, h.z), new Vector3( h.x,-h.y, h.z),
                    new Vector3( h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
                };
                for (int i = 0; i < 8; i++) p[i] = c + rot * p[i];
                int[,] faces = {
                    {0,1,2,3}, {5,4,7,6}, {4,0,3,7}, {1,5,6,2}, {3,2,6,7}, {4,5,1,0}
                };
                for (int f = 0; f < 6; f++)
                {
                    int b = v.Count;
                    Vector3 a = p[faces[f, 0]], bb = p[faces[f, 1]], cc = p[faces[f, 2]], dd = p[faces[f, 3]];
                    Vector3 nr = Vector3.Cross(bb - a, cc - a).normalized;
                    v.Add(a); v.Add(bb); v.Add(cc); v.Add(dd);
                    for (int k = 0; k < 4; k++) n.Add(nr);
                    uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
                    uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                    t.Add(b); t.Add(b + 1); t.Add(b + 2);
                    t.Add(b); t.Add(b + 2); t.Add(b + 3);
                }
            }

            /// <summary>Y軸に沿った角柱。軸や刻みに使う。</summary>
            public void Prism(Vector3 c, float r, float len, int seg)
            {
                float hy = len * 0.5f;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                    Vector3 d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    Vector3 p0 = c + d0 * r + Vector3.down * hy;
                    Vector3 p1 = c + d1 * r + Vector3.down * hy;
                    Vector3 p2 = c + d1 * r + Vector3.up * hy;
                    Vector3 p3 = c + d0 * r + Vector3.up * hy;
                    int b = v.Count;
                    v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                    Vector3 nr = ((d0 + d1) * 0.5f).normalized;
                    for (int k = 0; k < 4; k++) n.Add(nr);
                    uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
                    uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                    t.Add(b); t.Add(b + 1); t.Add(b + 2);
                    t.Add(b); t.Add(b + 2); t.Add(b + 3);
                }
            }

            /// <summary>XY平面の輪。小さい直方体を並べて作る。</summary>
            public void Ring(Vector3 c, float r, float thick, int seg)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a = Mathf.PI * 2f * (i + 0.5f) / seg;
                    var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    // 1コマの長さ。隣とわずかに重ねて隙間を作らない
                    float seglen = 2f * Mathf.PI * r / seg * 1.25f;
                    var rot = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg);
                    Box(c + dir * r, new Vector3(thick, seglen, thick), rot);
                }
            }
        }

        [MenuItem("BLIND/鍵/1. 真鍮の鍵を焼く")]
        public static void Menu() { Debug.Log(Bake()); }

        public static string Bake()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_BLIND/Art/Models"))
                    AssetDatabase.CreateFolder("Assets/_BLIND/Art", "Models");
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Models", "Key");
            }

            // ── 真鍮部（サブメッシュ0）
            var brass = new Build();
            // 輪。原点より上に置き、そこから下へ軸を伸ばす
            brass.Ring(new Vector3(0f, BowR, 0f), BowR, BowT, 14);
            // 軸
            brass.Prism(new Vector3(0f, -ShankL * 0.5f, 0f), ShankR, ShankL, 10);
            // 刻み（先端の歯）。片側に2枚
            brass.Box(new Vector3(0.009f, -ShankL + 0.020f, 0f),
                      new Vector3(0.014f, 0.013f, 0.005f), Quaternion.identity);
            brass.Box(new Vector3(0.008f, -ShankL + 0.004f, 0f),
                      new Vector3(0.012f, 0.009f, 0.005f), Quaternion.identity);
            // 札を吊る小さな輪
            brass.Ring(new Vector3(0.030f, BowR * 1.55f, 0f), 0.009f, 0.003f, 8);

            // ── 札（サブメッシュ1）
            var tag = new Build();
            var tagRot = Quaternion.Euler(0f, 0f, -14f);   // 少し傾けて下げ札らしく
            var tagCenter = new Vector3(0.030f + TagW * 0.42f, BowR * 1.55f - TagH * 0.62f, 0f);
            tag.Box(tagCenter, new Vector3(TagW, TagH, TagT), tagRot);

            // ── メッシュ化
            var mesh = new Mesh { name = "BrassKey" };
            var v = new List<Vector3>(brass.v); v.AddRange(tag.v);
            var n = new List<Vector3>(brass.n); n.AddRange(tag.n);
            var uv = new List<Vector2>(brass.uv); uv.AddRange(tag.uv);
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(brass.t, 0);
            var t2 = new List<int>();
            foreach (var i in tag.t) t2.Add(i + brass.v.Count);
            mesh.SetTriangles(t2, 1);
            mesh.RecalculateBounds();

            var path = Dir + "/BrassKey.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { EditorUtility.CopySerialized(mesh, old); mesh = old; }
            else AssetDatabase.CreateAsset(mesh, path);

            // ── マテリアル
            MakeMat("Key_Brass", new Color(0.72f, 0.55f, 0.22f), 0.72f, 0.85f);
            MakeMat("Key_Tag", new Color(0.80f, 0.76f, 0.64f), 0.18f, 0.00f);

            AssetDatabase.SaveAssets();
            var b = mesh.bounds;
            return "真鍮の鍵を焼いた: " + path
                 + "  三角形=" + (mesh.triangles.Length / 3)
                 + "  大きさ=" + b.size.ToString("F3") + "m"
                 + "（サブメッシュ 0=真鍮 / 1=札）";
        }

        static void MakeMat(string name, Color c, float smooth, float metal)
        {
            string p = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("Standard");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            m.shader = sh;
            m.SetColor("_Color", c);
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
        }
    }
}
