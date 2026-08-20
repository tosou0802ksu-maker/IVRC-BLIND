using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room12「だるまの資料室」の什器を手続きで作る。
    ///
    /// 木製のアンティーク本棚（Mix Furniture）を仮置きして確かめたところ、
    /// 彫刻付きの洋館の書棚で、テラゾー床＋蛍光灯の現代の資料室からは完全に浮いた。
    /// スチール書架は直線だけの形なので、拾ってくるより作った方が確実で、
    /// しかも 1台 132 三角形（木製本棚は 5,392）で済む。48台置いても 6千。
    /// 事務机・椅子・文書保存箱も同じ理由でここで作る。
    /// </summary>
    public static class Room12Kit
    {
        public const string KitDir = "Assets/_BLIND/Art/Models/Room12Kit";
        const string MatDir = "Assets/_BLIND/Art/Materials";
        const string TexDir = "Assets/_BLIND/Art/Textures";

        // --- スチール書架の寸法（実物の軽量ラックに合わせている） ---
        public const float RackW = 0.95f;   // 間口
        public const float RackD = 0.55f;   // 奥行き
        public const float RackH = 2.10f;   // 高さ
        /// <summary>棚板の上面。天板(1番上)の上は天井まで開いている。</summary>
        public static readonly float[] RackShelfY = { 0.15f, 0.62f, 1.09f, 1.56f, 2.03f };
        public const float RackClear = 0.47f;             // 棚板どうしの間隔
        public const float RackUsableHalfW = 0.385f;      // 支柱の内側

        public const float DeskW = 1.40f, DeskD = 0.70f, DeskH = 0.73f;
        public const float BoxW = 0.40f, BoxH = 0.30f, BoxD = 0.32f;

        // ---------------------------------------------------------------
        //  箱を積んでメッシュを組む小さなビルダー
        // ---------------------------------------------------------------
        public class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();
            /// <summary>UV は面ごとの平面投影。テクスチャ 1 枚 = 何メートルか。</summary>
            public float UvMeters = 1.0f;

            public void AddBox(Vector3 center, Vector3 size) { AddBox(center, size, Quaternion.identity); }

            public void AddBox(Vector3 center, Vector3 size, Quaternion rot)
            {
                var h = size * 0.5f;
                // 面ごとに (法線, 面内の右, 面内の上)
                Vector3[] nrm = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                Vector3[] rgt = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
                Vector3[] upv = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };
                for (int f = 0; f < 6; f++)
                {
                    var no = nrm[f]; var ri = rgt[f]; var up = upv[f];
                    float he = Vector3.Dot(new Vector3(Mathf.Abs(ri.x), Mathf.Abs(ri.y), Mathf.Abs(ri.z)), h);
                    float hu = Vector3.Dot(new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z)), h);
                    float hn = Vector3.Dot(new Vector3(Mathf.Abs(no.x), Mathf.Abs(no.y), Mathf.Abs(no.z)), h);
                    var org = no * hn;
                    int b = v.Count;
                    Vector3[] corner = {
                        org - ri * he - up * hu, org + ri * he - up * hu,
                        org + ri * he + up * hu, org - ri * he + up * hu };
                    Vector2[] uvs = {
                        new Vector2(-he, -hu) / UvMeters, new Vector2(he, -hu) / UvMeters,
                        new Vector2(he, hu) / UvMeters,   new Vector2(-he, hu) / UvMeters };
                    for (int i = 0; i < 4; i++)
                    {
                        v.Add(center + rot * corner[i]);
                        n.Add(rot * no);
                        uv.Add(uvs[i]);
                    }
                    t.Add(b); t.Add(b + 2); t.Add(b + 1);
                    t.Add(b); t.Add(b + 3); t.Add(b + 2);
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateTangents();
                m.RecalculateBounds();
                Unwrapping.GenerateSecondaryUVSet(m);
                return m;
            }

            public int TriCount { get { return t.Count / 3; } }
        }

        // ---------------------------------------------------------------
        //  什器のメッシュ
        // ---------------------------------------------------------------

        /// <summary>スチール書架。原点は床の中心、開いている面は +Z。</summary>
        static Mesh MakeRack()
        {
            var b = new MeshBuilder { UvMeters = 0.9f };
            const float post = 0.045f;
            float px = RackW * 0.5f - post * 0.5f;
            float pz = RackD * 0.5f - post * 0.5f;
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    b.AddBox(new Vector3(sx * px, RackH * 0.5f, sz * pz), new Vector3(post, RackH, post));

            foreach (var y in RackShelfY)
                b.AddBox(new Vector3(0f, y - 0.015f, 0f), new Vector3(RackW - post, 0.03f, RackD - post));

            // 背面の筋交い。これがないとただの棚の絵になる
            float diag = Mathf.Sqrt(RackW * RackW + (RackH - 0.3f) * (RackH - 0.3f));
            float ang = Mathf.Atan2(RackH - 0.3f, RackW) * Mathf.Rad2Deg;
            foreach (var s in new[] { 1f, -1f })
                b.AddBox(new Vector3(0f, RackH * 0.5f, -pz),
                         new Vector3(0.025f, diag, 0.012f),
                         Quaternion.Euler(0f, 0f, s * (90f - ang)));
            return b.ToMesh("Room12_SteelRack");
        }

        /// <summary>事務机。原点は床の中心、座る側（引き出しの正面）は +Z。</summary>
        static Mesh MakeDesk()
        {
            var b = new MeshBuilder { UvMeters = 0.9f };
            b.AddBox(new Vector3(0f, DeskH - 0.015f, 0f), new Vector3(DeskW, 0.03f, DeskD));           // 天板
            b.AddBox(new Vector3(0.46f, 0.35f, -0.01f), new Vector3(0.42f, 0.68f, DeskD - 0.05f));      // 引き出し箱
            for (int i = 0; i < 3; i++)                                                                 // 引き出しの面
                b.AddBox(new Vector3(0.46f, 0.16f + i * 0.21f, DeskD * 0.5f - 0.02f), new Vector3(0.36f, 0.17f, 0.02f));
            b.AddBox(new Vector3(-DeskW * 0.5f + 0.02f, 0.35f, -0.01f), new Vector3(0.03f, 0.68f, DeskD - 0.05f)); // 左の側板
            b.AddBox(new Vector3(-0.22f, 0.45f, -DeskD * 0.5f + 0.03f), new Vector3(0.92f, 0.42f, 0.02f));         // 幕板
            b.AddBox(new Vector3(-0.22f, 0.06f, -DeskD * 0.5f + 0.06f), new Vector3(0.92f, 0.04f, 0.03f));         // 足元の桟
            return b.ToMesh("Room12_Desk");
        }

        /// <summary>事務椅子。原点は床の中心、背もたれは -Z。</summary>
        static Mesh MakeChair()
        {
            var b = new MeshBuilder { UvMeters = 0.6f };
            b.AddBox(new Vector3(0f, 0.44f, 0f), new Vector3(0.44f, 0.05f, 0.42f));
            b.AddBox(new Vector3(0f, 0.68f, -0.19f), new Vector3(0.42f, 0.40f, 0.04f));
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    b.AddBox(new Vector3(sx * 0.18f, 0.21f, sz * 0.17f), new Vector3(0.032f, 0.42f, 0.032f));
            // 背もたれの支柱
            foreach (var sx in new[] { -1f, 1f })
                b.AddBox(new Vector3(sx * 0.18f, 0.56f, -0.19f), new Vector3(0.032f, 0.24f, 0.032f));
            return b.ToMesh("Room12_Chair");
        }

        /// <summary>文書保存箱。ふた付きの段ボール。原点は床の中心。</summary>
        static Mesh MakeArchiveBox()
        {
            var b = new MeshBuilder { UvMeters = 0.5f };
            b.AddBox(new Vector3(0f, BoxH * 0.5f, 0f), new Vector3(BoxW, BoxH, BoxD));
            b.AddBox(new Vector3(0f, BoxH + 0.015f, 0f), new Vector3(BoxW + 0.02f, 0.03f, BoxD + 0.02f)); // ふた
            return b.ToMesh("Room12_ArchiveBox");
        }

        // ---------------------------------------------------------------
        [MenuItem("BLIND/room12/1. Build Kit")]
        public static string BuildKit()
        {
            if (!AssetDatabase.IsValidFolder(KitDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Models", "Room12Kit");

            var log = new System.Text.StringBuilder();
            var items = new (string name, Mesh mesh)[]
            {
                ("Room12_SteelRack",   MakeRack()),
                ("Room12_Desk",        MakeDesk()),
                ("Room12_Chair",       MakeChair()),
                ("Room12_ArchiveBox",  MakeArchiveBox()),
            };
            foreach (var it in items)
            {
                var path = KitDir + "/" + it.name + ".asset";
                var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (ex != null) { EditorUtility.CopySerialized(it.mesh, ex); EditorUtility.SetDirty(ex); }
                else AssetDatabase.CreateAsset(it.mesh, path);
                log.AppendLine(it.name + "  tris=" + (it.mesh.triangles.Length / 3) + "  size=" + it.mesh.bounds.size.ToString("F3"));
            }

            BuildMaterials(log);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return log.ToString();
        }

        static void BuildMaterials(System.Text.StringBuilder log)
        {
            var col = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Metal032/Metal032_Color.jpg");
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/PaintedMetal001/PaintedMetal001_NormalGL.jpg");
            if (nrm != null)
            {
                var ip = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(nrm));
                if (ip != null && ip.textureType != TextureImporterType.NormalMap)
                { ip.textureType = TextureImporterType.NormalMap; ip.SaveAndReimport(); }
            }

            // 資料室の什器は塗装鋼。灰緑がかった役所の色
            Steel("Room12_Steel",     new Color(0.600f, 0.610f, 0.575f), col, nrm, 0.30f, 0.40f, log);
            Steel("Room12_SteelDesk", new Color(0.372f, 0.380f, 0.365f), col, nrm, 0.32f, 0.44f, log);
            Steel("Room12_SteelRust", new Color(0.395f, 0.318f, 0.258f), col, nrm, 0.22f, 0.28f, log);
            // 段ボールはてかりも金属味もない
            var cbCol = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Cardboard004/Cardboard004_Color.jpg");
            var cbNrm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Cardboard004/Cardboard004_NormalGL.jpg");
            if (cbNrm != null)
            {
                var ip = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(cbNrm));
                if (ip != null && ip.textureType != TextureImporterType.NormalMap)
                { ip.textureType = TextureImporterType.NormalMap; ip.SaveAndReimport(); }
            }
            // 素の Cardboard004 は明るい黄土色なので、埃をかぶった資料室の色まで落とす
            Steel("Room12_Cardboard",     new Color(0.700f, 0.680f, 0.640f), cbCol, cbNrm, 0f, 0.06f, log);
            Steel("Room12_CardboardOld",  new Color(0.470f, 0.440f, 0.405f), cbCol, cbNrm, 0f, 0.05f, log);
        }

        static void Steel(string name, Color tint, Texture2D col, Texture2D nrm, float metal, float gloss,
                          System.Text.StringBuilder log)
        {
            var path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Standard");
            m.SetTexture("_MainTex", col);
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 0.6f); m.EnableKeyword("_NORMALMAP"); }
            m.SetColor("_Color", tint);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Glossiness", gloss);
            EditorUtility.SetDirty(m);
            log.AppendLine("mat " + name);
        }
    }
}
