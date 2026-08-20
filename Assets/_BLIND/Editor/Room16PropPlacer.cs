using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room16 の家具・木箱まわり。room16 ローカル座標（X 0..10, Z 0..14）。
    /// 置いたものの XZ 範囲は Room16DollPlacer が人形の除外領域として読む。
    /// </summary>
    public static class Room16PropPlacer
    {
        const string Sewer = "Assets/Ata Khani/Modular Sewer Props/Prefabs/";
        const string Furni = "Assets/ZNS3D/Prefabs/";

        struct P
        {
            public string path;
            public float x, z, yaw, y;
            public P(string p, float x, float z, float yaw, float y = 0f)
            { path = p; this.x = x; this.z = z; this.yaw = yaw; this.y = y; }
        }

        static readonly P[] Layout =
        {
            // ── 奥の壁（z=14）に本棚を並べる。プレハブの正面は +Z なので 180 度回す
            new P(Furni + "bookshelf_1.prefab", 1.30f, 13.62f, 180f),
            new P(Furni + "bookshelf_3.prefab", 3.20f, 13.62f, 180f),
            new P(Furni + "bookshelf_2.prefab", 5.10f, 13.62f, 180f),
            new P(Furni + "bookshelf_4.prefab", 7.00f, 13.62f, 180f),
            // ── 北の壁（x=10）
            new P(Furni + "bookshelf_2.prefab", 9.63f, 11.60f, 270f),
            new P(Furni + "bookshelf_4.prefab", 9.63f,  9.70f, 270f),
            // ── 南の壁（x=0）
            new P(Furni + "bookshelf_1.prefab", 0.37f, 11.30f,  90f),

            // ── 布のかかった木箱と樽のかたまり（奥の角、部屋に入ると正面奥に見える）
            new P(Sewer + "Barrel_Box_03.prefab", 8.75f, 12.55f, 205f),

            // ── 北の壁ぎわ
            new P(Sewer + "Box_A_01.prefab",  9.30f, 6.90f,  25f),
            new P(Sewer + "Box_B_01.prefab",  9.28f, 6.85f, -12f, 0.89f),   // 上に重ねる
            new P(Sewer + "Box_A_02.prefab",  9.22f, 7.95f, -15f),
            new P(Sewer + "Barrel_B_01.prefab", 9.35f, 8.85f, 40f),

            // ── 南の壁ぎわ
            new P(Sewer + "Box_A_01.prefab",  0.62f, 4.25f,  10f),
            new P(Sewer + "Box_A_02.prefab",  0.62f, 5.35f, -20f),
            new P(Sewer + "Barrel_B_01.prefab", 0.60f, 6.45f, 35f),
            new P(Sewer + "Ladder_A.prefab",  0.72f, 12.75f, 88f),

            // ── 棚の南側の通路（room18 へ抜ける細い道）の、棚に寄せた側
            new P(Sewer + "Box_A_01.prefab",  2.35f, 2.05f,  15f),
            new P(Sewer + "Box_B_01.prefab",  2.30f, 2.00f, -30f, 0.89f),
            new P(Sewer + "Barrel_B_01.prefab", 8.95f, 2.00f, -8f),

            // ── 部屋の中の家具。上にマネキンのパーツを置く土台にもなる
            new P(Furni + "drawer_3.prefab",  4.40f, 12.85f, 175f),
            new P(Furni + "drawer_1.prefab",  8.30f,  4.30f, 250f),
            new P(Furni + "tea_table_1.prefab", 2.15f, 11.95f,  35f),
            new P(Furni + "tea_table_3.prefab", 6.55f,  6.55f, 310f),

            // ── 床に散った板
            new P(Sewer + "Wooden_Plank_01.prefab", 2.70f, 4.55f, 20f),
            new P(Sewer + "Wooden_Plank_05.prefab", 5.90f, 11.30f, 65f),
            new P(Sewer + "Wooden_Plank_03.prefab", 3.55f,  7.05f, 130f),
        };

        [MenuItem("BLIND/room16/Place Props")]
        public static string Place()
        {
            var room = GameObject.Find("room16");
            if (room == null) return "room16 not found";
            var old = room.transform.Find("Prop_Furniture");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var parent = new GameObject("Prop_Furniture");
            parent.transform.SetParent(room.transform, false);

            var log = new System.Text.StringBuilder();
            long tri0 = 0;
            int n = 0;

            foreach (var p in Layout)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(p.path);
                if (pf == null) { log.AppendLine("MISSING " + p.path); continue; }

                var g = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                g.transform.SetParent(parent.transform, false);
                g.transform.localRotation = Quaternion.Euler(0f, p.yaw, 0f);
                g.transform.localScale = Vector3.one;
                g.transform.localPosition = new Vector3(p.x, 0f, p.z);

                var rs = g.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);

                // 床（または指定の高さ）に接地させる
                g.transform.localPosition += new Vector3(0f, (room.transform.position.y + p.y) - b.min.y, 0f);
                b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);

                // コライダーが無いプレハブには箱を足す
                if (g.GetComponentInChildren<Collider>(true) == null)
                {
                    var bc = g.AddComponent<BoxCollider>();
                    bc.center = g.transform.InverseTransformPoint(b.center);
                    var e = g.transform.InverseTransformVector(b.size);
                    bc.size = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
                }

                foreach (var t in g.GetComponentsInChildren<Transform>(true))
                {
                    if (t.GetComponent<Renderer>() == null) continue;
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                        StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                        StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                }

                // LOD0 のみを実コストとして数える
                var lod = g.GetComponentInChildren<LODGroup>(true);
                var counted = new HashSet<Mesh>();
                if (lod != null)
                {
                    foreach (var r in lod.GetLODs()[0].renderers)
                    {
                        var mf = r != null ? r.GetComponent<MeshFilter>() : null;
                        if (mf != null && mf.sharedMesh != null) counted.Add(mf.sharedMesh);
                    }
                }
                else
                {
                    foreach (var r in rs)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) counted.Add(mf.sharedMesh);
                    }
                }
                foreach (var m in counted)
                    for (int s = 0; s < m.subMeshCount; s++) tri0 += m.GetIndexCount(s) / 3;

                n++;
            }

            EditorUtility.SetDirty(parent);
            log.Insert(0, "placed " + n + " props, LOD0 tris=" + tri0 + "\n");
            return log.ToString();
        }

        // ── 家具の上に載せる人形のパーツ ──────────────────────────────

        const string Parts = "Assets/RamsterZ_FreeDoll/Prefabs/";
        static readonly string[] PartNames =
        {
            "KillerDollPartsHandR", "KillerDollPartsHandL", "KillerDollPartsHead01",
            "KillerDollPartsArmR02", "KillerDollPartsArmL02", "KillerDollPartsLegR",
            "KillerDollPartsLegL", "KillerDollPartsTorso02",
        };

        /// <summary>プレハブ名 → 物が載る面の高さ（家具ローカル）と、置ける範囲の半径 x/z。</summary>
        static bool SurfacesFor(string name, out float[] ys, out float halfX, out float halfZ, out float zc)
        {
            zc = 0f;
            if (name.StartsWith("bookshelf")) { ys = new[] { 0.893f, 1.275f, 1.670f }; halfX = 0.58f; halfZ = 0.10f; zc = 0.04f; return true; }
            if (name.StartsWith("drawer")) { ys = new[] { 0.925f }; halfX = 0.42f; halfZ = 0.24f; return true; }
            if (name.StartsWith("tea_table")) { ys = new[] { 0.675f }; halfX = 0.20f; halfZ = 0.20f; return true; }
            if (name.StartsWith("Box_A_01")) { ys = new[] { 0.885f }; halfX = 0.24f; halfZ = 0.24f; return true; }
            if (name.StartsWith("Box_A_02")) { ys = new[] { 0.905f }; halfX = 0.24f; halfZ = 0.24f; return true; }
            ys = null; halfX = halfZ = 0f; return false;
        }

        [MenuItem("BLIND/room16/Place Doll Parts On Furniture")]
        public static string PlaceParts()
        {
            var room = GameObject.Find("room16");
            if (room == null) return "room16 not found";
            var furni = room.transform.Find("Prop_Furniture");
            if (furni == null) return "Prop_Furniture not found — 先に Place Props を実行";

            var old = room.transform.Find("Prop_FurnitureDecor");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var parent = new GameObject("Prop_FurnitureDecor");
            parent.transform.SetParent(room.transform, false);

            var mBody = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Room16_DollBody.mat");
            var mBodyA = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Room16_DollBodyAged.mat");
            var mHead = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Room16_DollHead.mat");

            var rnd = new System.Random(773);
            long tris = 0;
            int n = 0;

            foreach (Transform f in furni)
            {
                float[] ys; float hx, hz, zc;
                var baseName = f.name.Replace("(Clone)", "").Trim();
                if (!SurfacesFor(baseName, out ys, out hx, out hz, out zc)) continue;

                foreach (var y in ys)
                {
                    // 段ごとに 0〜2 個。全部埋めると重いので間引く
                    int count = rnd.Next(0, 3);
                    for (int i = 0; i < count; i++)
                    {
                        var pn = PartNames[rnd.Next(PartNames.Length)];
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Parts + pn + ".prefab");
                        if (pf == null) continue;

                        var g = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                        g.transform.SetParent(parent.transform, false);
                        g.transform.rotation = f.rotation * Quaternion.Euler(
                            (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f);

                        float lx = ((float)rnd.NextDouble() * 2f - 1f) * hx;
                        float lz = zc + ((float)rnd.NextDouble() * 2f - 1f) * hz;
                        g.transform.position = f.TransformPoint(new Vector3(lx, y, lz));

                        var r = g.GetComponentInChildren<Renderer>();
                        if (r == null) { Object.DestroyImmediate(g); continue; }
                        r.sharedMaterial = pn.Contains("Head") ? mHead : (rnd.Next(3) == 0 ? mBodyA : mBody);

                        // 面に接地させる
                        float surfaceY = f.TransformPoint(new Vector3(lx, y, lz)).y;
                        g.transform.position += new Vector3(0f, surfaceY - r.bounds.min.y, 0f);

                        foreach (var t in g.GetComponentsInChildren<Transform>(true))
                        {
                            if (t.GetComponent<Renderer>() == null) continue;
                            GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                            var so = new SerializedObject(t.GetComponent<Renderer>());
                            so.FindProperty("m_ScaleInLightmap").floatValue = 0.5f;
                            so.ApplyModifiedProperties();
                        }

                        var mf2 = g.GetComponentInChildren<MeshFilter>();
                        if (mf2 != null && mf2.sharedMesh != null)
                            for (int s = 0; s < mf2.sharedMesh.subMeshCount; s++) tris += mf2.sharedMesh.GetIndexCount(s) / 3;
                        n++;
                    }
                }
            }

            EditorUtility.SetDirty(parent);
            return "placed " + n + " doll parts on furniture, tris=" + tris;
        }

        /// <summary>
        /// 置いた家具の XZ 範囲（room16 ローカル、minX,minZ,maxX,maxZ）。人形の除外に使う。
        /// 床に散った板のような背の低いものは、人形が上に立てるので除外しない。
        /// </summary>
        public static List<Vector4> OccupiedRects(float minHeight = 0.35f)
        {
            var res = new List<Vector4>();
            var room = GameObject.Find("room16");
            if (room == null) return res;
            var parent = room.transform.Find("Prop_Furniture");
            if (parent == null) return res;
            var o = room.transform.position;

            foreach (Transform g in parent)
            {
                var rs = g.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.y < minHeight) continue;
                res.Add(new Vector4(b.min.x - o.x, b.min.z - o.z, b.max.x - o.x, b.max.z - o.z));
            }
            return res;
        }
    }
}
