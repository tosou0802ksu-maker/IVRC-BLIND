using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room8（ロッカー部屋）の壁に配管と配線を足す。
    ///
    /// なぜ必要か:
    ///   この部屋はサーモ視点で点灯 0.4%＝ほぼ真っ黒だった。
    ///   ロッカー(16℃)が黒い壁として視界を塞ぎ、熱源が天井灯しか無いため、
    ///   サーモ役は「自分だけ何も見えていない」状態で立ち尽くすことになる。
    ///   壁づたいに配管と配線を通せば、サーモ役には部屋の輪郭が熱の線として浮かび、
    ///   さらに `_FlowStrength`（熱の流れ）が乗るので、建物が動いて見える。
    ///
    /// 何を作るか:
    ///   ・壁に沿った水平の主配管（高さ違いで2〜3本）
    ///   ・そこから床へ降りる縦管
    ///   ・配線束（細い管を数本まとめたもの）
    ///
    /// 設計上の注意:
    ///   ・**高さ 2.2m より下には置かない。** room8 は歩ける面積が 42% しかなく、
    ///     最も狭い所で 0.8m。人の高さに物を足すと通れなくなる。
    ///     縦管だけは床まで降ろすが、壁から 12cm 以内に収めて動線を邪魔しない。
    ///   ・コライダーは付けない。見た目のためだけの物に当たり判定を足すと、
    ///     壁際を歩けなくなるうえ物理の負荷も増える。
    ///   ・名前が分類の入力になる。`Pipe_` は Duct(38℃)、`Cable_` は room8 の
    ///     分岐で DuctDead/Warm/Duct/Hot の4段階に散る（BlindVisionBuilder.Classify）。
    ///   ・Default 層に置くだけ。サーモ／エコロケの複製は vision/2 が作る。
    /// </summary>
    public static class BlindRoom8Pipes
    {
        const string RoomName  = "room8";
        const string GroupName = "Props_WallPipes_Generated";

        /// <summary>部屋の内側の範囲(world)。GeneratedRoom/Walls の実測値。</summary>
        const float X0 = -21.3f, X1 = -9.1f;
        const float Z0 = -47.9f, Z1 = -40.7f;
        const float WallTop = 4.0f;

        /// <summary>壁面からの浮かせ量(m)。0 だと壁と Zファイティングを起こす。</summary>
        const float Offset = 0.16f;

        /// <summary>これより低い位置に水平材を置かない(m)。動線を塞がないため。</summary>
        const float MinRunHeight = 2.2f;

        [MenuItem("BLIND/ギミック/2. room8 の壁に配管と配線を足す")]
        public static void Menu_Build()
        {
            Debug.Log(Build());
        }

        public static string Build()
        {
            var roomGo = GameObject.Find("=== ROOMS ===/" + RoomName);
            if (roomGo == null) return RoomName + " が見つからない。";
            var room = roomGo.transform;

            var old = room.Find(GroupName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(root, "wall pipes");
            root.transform.SetParent(room, false);
            root.transform.position = Vector3.zero;
            root.layer = 0;

            var mat = PipeMaterial();
            var rnd = new System.Random(8081);   // 固定。作り直しても同じ配置になる
            int pipes = 0, cables = 0, drops = 0;

            // 壁は4面。(軸, 固定座標, 始点, 終点) で表す
            // horiz=true … X方向に伸びる壁（Z が固定）
            var walls = new[]
            {
                new WallRun { horiz = true,  fixedCoord = Z0 + Offset, a = X0 + 0.3f, b = X1 - 0.3f, inward =  1f },
                new WallRun { horiz = true,  fixedCoord = Z1 - Offset, a = X0 + 0.3f, b = X1 - 0.3f, inward = -1f },
                new WallRun { horiz = false, fixedCoord = X0 + Offset, a = Z0 + 0.3f, b = Z1 - 0.3f, inward =  1f },
                new WallRun { horiz = false, fixedCoord = X1 - Offset, a = Z0 + 0.3f, b = Z1 - 0.3f, inward = -1f },
            };

            for (int w = 0; w < walls.Length; w++)
            {
                var wall = walls[w];

                // --- 主配管: 高さ違いで2本 ---
                float[] runY = { 3.45f, 2.85f };
                float[] runD = { 0.20f, 0.14f };
                for (int k = 0; k < runY.Length; k++)
                {
                    MakeRun(root.transform, mat, wall, runY[k], runD[k], "Pipe_Wall_" + w + "_" + k);
                    pipes++;
                }

                // --- 配線束: 細い管を3本まとめて1段に ---
                // 3本を少しずつずらして束ねる。名前が別々なので、分類側で
                // 4段階(DuctDead/Warm/Duct/Hot)にばらけて「生きた線と死んだ線」が混じる。
                for (int k = 0; k < 3; k++)
                {
                    float y = 2.42f + k * 0.075f;
                    MakeRun(root.transform, mat, wall, y, 0.055f, "Cable_Wall_" + w + "_" + k);
                    cables++;
                }

                // --- 縦管: 主配管から床へ降ろす ---
                // 壁から Offset しか離れていないので、動線には出てこない。
                float len = Mathf.Abs(wall.b - wall.a);
                int nDrop = Mathf.Max(2, Mathf.RoundToInt(len / 3.4f));
                for (int k = 0; k < nDrop; k++)
                {
                    float t = (k + 0.5f) / nDrop;
                    float pos = Mathf.Lerp(wall.a, wall.b, t) + (float)(rnd.NextDouble() - 0.5) * 0.7f;
                    float bottom = 0.35f + (float)rnd.NextDouble() * 0.5f;
                    MakeDrop(root.transform, mat, wall, pos, bottom, 3.45f, 0.13f,
                             "Pipe_Drop_" + w + "_" + k);
                    drops++;
                }
            }

            // ------------------------------------------------------------
            // 天井を渡す配管網
            // ------------------------------------------------------------
            // 壁だけに付けても、この部屋はロッカーが視界を塞ぐので
            // 通路の中からはほとんど見えなかった（実測で点灯 3.4%）。
            // 天井はロッカーより高く、部屋のどこからでも見える唯一の面なので、
            // ここに網を渡すとサーモ役に「部屋の広がり」と「建物の脈」が同時に伝わる。
            // 実際のロッカー室でも配管は天井を走っているので不自然でもない。
            int ceil = 0;
            {
                // X方向（長辺）に4本
                float[] zs = { -46.4f, -45.0f, -43.6f, -42.2f };
                for (int k = 0; k < zs.Length; k++)
                {
                    var w = new WallRun { horiz = true, fixedCoord = zs[k], a = X0 + 0.2f, b = X1 - 0.2f };
                    MakeRun(root.transform, mat, w, 3.72f, 0.19f, "Pipe_Ceil_X" + k);
                    ceil++;
                    // 配線束を1段下に併走させる
                    for (int c = 0; c < 2; c++)
                    {
                        MakeRun(root.transform, mat, w, 3.44f + c * 0.08f, 0.06f, "Cable_Ceil_X" + k + "_" + c);
                        cables++;
                    }
                }
                // Z方向（短辺）に3本。交差させて「網」にする
                float[] xs = { -19.4f, -15.4f, -11.4f };
                for (int k = 0; k < xs.Length; k++)
                {
                    var w = new WallRun { horiz = false, fixedCoord = xs[k], a = Z0 + 0.2f, b = Z1 - 0.2f };
                    MakeRun(root.transform, mat, w, 3.55f, 0.15f, "Pipe_Ceil_Z" + k);
                    ceil++;
                }
            }

            EditorSceneManagerMarkDirty();
            return "room8 に追加: 壁の主配管 " + pipes + " 本 / 天井の配管 " + ceil + " 本 / 配線 " + cables + " 本 / 縦管 " + drops + " 本\n"
                 + "  すべて高さ " + MinRunHeight + "m 以上（縦管は壁から " + Offset + "m 以内）なので動線に影響しない。\n"
                 + "  → 続けて vision/2 を room8 に実行するとサーモ・エコロケの複製が作られる。";
        }

        class WallRun
        {
            public bool horiz;        // true = X方向に伸びる
            public float fixedCoord;  // horiz なら Z、そうでなければ X
            public float a, b;        // 伸びる方向の始点と終点
            public float inward;      // 部屋の内側はどちら向きか（未使用だが向き調整用に残す）
        }

        static Vector3 PointOn(WallRun w, float along, float y)
        {
            return w.horiz ? new Vector3(along, y, w.fixedCoord)
                           : new Vector3(w.fixedCoord, y, along);
        }

        /// <summary>壁に沿った水平の管を1本置く。</summary>
        static void MakeRun(Transform parent, Material mat, WallRun w, float y, float dia, string name)
        {
            if (y < MinRunHeight) return;   // 動線を塞がないための保険
            var p0 = PointOn(w, w.a, y);
            var p1 = PointOn(w, w.b, y);
            MakeCylinder(parent, mat, p0, p1, dia, name);
        }

        /// <summary>主配管から床へ降ろす縦管。</summary>
        static void MakeDrop(Transform parent, Material mat, WallRun w, float along,
                             float bottomY, float topY, float dia, string name)
        {
            var p0 = PointOn(w, along, bottomY);
            var p1 = PointOn(w, along, topY);
            MakeCylinder(parent, mat, p0, p1, dia, name);
        }

        /// <summary>
        /// 2点を結ぶ円柱を置く。
        /// Unity の Cylinder は高さ2・半径0.5 なので、scale は (径, 長さ/2, 径)。
        /// コライダーは消す（見た目だけの物に当たり判定を足すと壁際を歩けなくなる）。
        /// </summary>
        static void MakeCylinder(Transform parent, Material mat, Vector3 p0, Vector3 p1, float dia, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "pipe");

            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            go.transform.SetParent(parent, false);
            go.transform.position = (p0 + p1) * 0.5f;
            var dir = p1 - p0;
            if (dir.sqrMagnitude > 1e-6f)
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            go.transform.localScale = new Vector3(dia, Mathf.Max(dir.magnitude, 0.02f) * 0.5f, dia);
            go.layer = 0;

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>過去人視点用の見た目。金属らしく暗めにしておく。</summary>
        static Material PipeMaterial()
        {
            const string dir = "Assets/_BLIND/Art/Materials/Gimmick";
            const string path = dir + "/Room8_Pipe.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Materials", "Gimmick");
            m = new Material(Shader.Find("Standard"));
            m.color = new Color(0.32f, 0.31f, 0.29f);
            m.SetFloat("_Metallic", 0.75f);
            m.SetFloat("_Glossiness", 0.35f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void EditorSceneManagerMarkDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
