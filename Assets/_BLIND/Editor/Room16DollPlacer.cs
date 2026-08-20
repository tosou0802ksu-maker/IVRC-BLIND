using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room16 の人形配置。room16 ローカル座標（X 0..10, Z 0..14）。
    /// 部屋全体にまんべんなく散らしたいので、ジッタ付きグリッドから
    /// 動線・棚・小道具に当たるものを落として決めている。
    /// </summary>
    public static class Room16DollPlacer
    {
        const string MeshDir = "Assets/_BLIND/Art/Models/Room16Dolls/Room16Doll_";
        static readonly string[] PoseName = { "Pose0_Stand", "Pose1_StandTilt", "Pose2_Reach", "Pose3_Bowed", "Pose4_Sprawl", "Pose5_Curl", "Pose6_Hanging" };

        /// <summary>歩ける道。ここから ClearPath 以内には人形を置かない。</summary>
        static readonly Vector2[][] Paths =
        {
            // room15 の扉 → 部屋を横切って room17 の扉へ
            new[] { new Vector2(0.4f, 8.80f), new Vector2(3.0f, 8.50f), new Vector2(5.2f, 7.40f),
                    new Vector2(7.4f, 5.90f), new Vector2(9.6f, 5.00f) },
            // 途中から西側を回って room18 の扉へ
            new[] { new Vector2(2.8f, 8.60f), new Vector2(1.9f, 7.40f), new Vector2(1.9f, 4.20f),
                    new Vector2(1.8f, 1.60f), new Vector2(4.5f, 1.10f), new Vector2(7.6f, 0.40f) },
        };

        const float ClearPath = 0.88f;

        /// <summary>置いてはいけない矩形（XZ, minX/minZ/maxX/maxZ）。棚と天井の手。</summary>
        static readonly Vector4[] FixedBlockers =
        {
            new Vector4(1.60f, 2.15f, 10.10f, 3.25f),  // 元からある本棚の列
            new Vector4(6.05f, 8.05f,  7.95f, 9.95f),  // 天井から降りてくる腕と掴まれた人形
        };

        /// <summary>固定分＋シーンに置いた家具。家具は Room16PropPlacer から実測で拾う。</summary>
        static List<Vector4> AllBlockers()
        {
            var l = new List<Vector4>();
            // 固定分はざっくり書いた矩形なので広めに膨らませる
            foreach (var b in FixedBlockers)
                l.Add(new Vector4(b.x - 0.55f, b.y - 0.55f, b.z + 0.55f, b.w + 0.55f));
            // 家具は実測のバウンズなので、人形の胴まわりぶんだけ足す
            foreach (var b in Room16PropPlacer.OccupiedRects())
                l.Add(new Vector4(b.x - 0.28f, b.y - 0.28f, b.z + 0.28f, b.w + 0.28f));
            return l;
        }

        static List<Vector4> _blockers;

        const float Margin = 0.75f;   // 壁からの余裕

        struct D
        {
            public float x, z, yaw, spin;
            public int pose;
            public bool aged, lying;
        }

        static float DistToPaths(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var path in Paths)
                for (int i = 0; i + 1 < path.Length; i++)
                {
                    var a = path[i]; var b = path[i + 1];
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
                }
            return best;
        }

        static bool InBlocker(Vector2 p, float pad)
        {
            foreach (var b in _blockers)
                if (p.x > b.x - pad && p.x < b.z + pad && p.y > b.y - pad && p.y < b.w + pad) return true;
            return false;
        }

        static List<D> BuildLayout()
        {
            _blockers = AllBlockers();
            var rnd = new System.Random(20260820);
            var picked = new List<D>();
            var pts = new List<Vector2>();

            // 部屋全体を覆うジッタ付きグリッド。棚の南側の細い通路も含める。
            for (float gx = 1.05f; gx <= 9.3f; gx += 1.09f)
                for (float gz = 0.85f; gz <= 13.6f; gz += 1.06f)
                {
                    var p = new Vector2(
                        gx + ((float)rnd.NextDouble() - 0.5f) * 0.66f,
                        gz + ((float)rnd.NextDouble() - 0.5f) * 0.66f);

                    if (p.x < Margin || p.x > 10f - Margin || p.y < 0.55f || p.y > 14f - Margin) continue;
                    if (DistToPaths(p) < ClearPath) continue;
                    if (InBlocker(p, 0f)) continue;

                    bool tooClose = false;
                    foreach (var q in pts) if (Vector2.Distance(p, q) < 0.94f) { tooClose = true; break; }
                    if (tooClose) continue;

                    pts.Add(p);
                }

            // 3.6体に1体くらいを倒れている状態にして、立ちポーズは順に散らす
            int i2 = 0;
            foreach (var p in pts)
            {
                bool lying = (i2 % 7 == 2) || (i2 % 7 == 5);
                var d = new D
                {
                    x = p.x,
                    z = p.y,
                    yaw = (float)rnd.NextDouble() * 360f,
                    pose = lying ? (4 + (i2 % 2)) : (i2 * 3 + 1) % 4,
                    spin = (float)rnd.NextDouble() * 360f,
                    aged = (i2 % 3 == 1),
                    lying = lying,
                };
                picked.Add(d);
                i2++;
            }
            return picked;
        }

        [MenuItem("BLIND/room16/Place Dolls")]
        public static string Place()
        {
            var room = GameObject.Find("room16");
            if (room == null) return "room16 not found";
            var old = room.transform.Find("Prop_Dolls");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var parent = new GameObject("Prop_Dolls");
            parent.transform.SetParent(room.transform, false);

            var mBody = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Room16_DollBody.mat");
            var mBodyA = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Room16_DollBodyAged.mat");
            var mHead = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Room16_DollHead.mat");
            var mHeadA = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Room16_DollHeadAged.mat");
            var mEyes = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Room16_DollEyes.mat");

            var meshes = new Dictionary<int, Mesh>();
            for (int i = 0; i < PoseName.Length; i++)
                meshes[i] = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + PoseName[i] + ".asset");

            var layout = BuildLayout();
            var rnd = new System.Random(9161);
            long tris = 0;
            int nStand = 0, nLie = 0;

            for (int i = 0; i < layout.Count; i++)
            {
                var d = layout[i];
                var g = new GameObject((d.lying ? "Doll_Fallen_" : "Doll_") + i.ToString("00"));
                g.transform.SetParent(parent.transform, false);

                var q = Quaternion.Euler(0f, d.yaw, 0f);
                if (d.lying) q = q * Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, d.spin, 0f);
                g.transform.localRotation = q;
                float s = d.lying ? 0.97f + (float)rnd.NextDouble() * 0.07f
                                  : 0.94f + (float)rnd.NextDouble() * 0.12f;
                g.transform.localScale = new Vector3(s, s, s);
                g.transform.localPosition = new Vector3(d.x, 0f, d.z);

                var mesh = meshes[d.pose];
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { d.aged ? mBodyA : mBody, d.aged ? mHeadA : mHead, mEyes };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                // 床に接地させる
                var b = mr.bounds;
                g.transform.localPosition += new Vector3(0f, (room.transform.position.y + 0.015f) - b.min.y, 0f);

                // 当たり判定はプリミティブで（メッシュコライダーは重すぎる）
                b = mr.bounds;
                if (d.lying)
                {
                    var bc = g.AddComponent<BoxCollider>();
                    bc.center = g.transform.InverseTransformPoint(b.center);
                    var e = g.transform.InverseTransformVector(b.size);
                    bc.size = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 0.82f;
                    nLie++;
                }
                else
                {
                    var cc = g.AddComponent<CapsuleCollider>();
                    cc.direction = 1;
                    float h = b.size.y / s;
                    cc.height = h; cc.radius = 0.20f;
                    cc.center = new Vector3(0f, h * 0.5f, 0f);
                    nStand++;
                }

                GameObjectUtility.SetStaticEditorFlags(g,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                var so = new SerializedObject(mr);
                so.FindProperty("m_ScaleInLightmap").floatValue = d.lying ? 0.85f : 0.75f;
                so.ApplyModifiedProperties();

                for (int sm = 0; sm < mesh.subMeshCount; sm++) tris += mesh.GetIndexCount(sm) / 3;
            }

            EditorUtility.SetDirty(parent);
            return "placed " + layout.Count + " dolls (stand " + nStand + " / fallen " + nLie + "), tris=" + tris;
        }
    }
}
