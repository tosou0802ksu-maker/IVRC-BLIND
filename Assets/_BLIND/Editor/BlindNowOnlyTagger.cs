using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 「現在だけに存在する物」を NowOnly(25) レイヤーに移すツール。
    ///
    /// 過去の人は本来「かつてそこにあった部屋」を見る役だが、
    /// 現状は Default レイヤーの現在の内装をそのまま見ているので、
    /// 現在の障害物も危険も自力で見えてしまい、他の2人に頼る理由が薄い。
    ///
    /// 置き換えたい物を NowOnly に移すと：
    ///   過去の人  … 見えない（PlayerVisionController がマスクから落とす）
    ///   エコロケ  … 見える（BlindVisionBuilder が Echo 層の複製を作る）
    ///   サーモ    … 見える（同じく Thermal 層の複製を作る）
    ///   当たり判定… そのまま残る
    /// となり、「見えないが、ぶつかる物」が生まれる。
    /// これを避けて通るには他の2人に教えてもらうしかない。
    ///
    /// レンダラーだけを移し、Collider の載った親は動かさない。
    /// 親ごと移すと当たり判定まで一緒に動いてしまい、
    /// 「見えないだけで存在はする」という前提が崩れるため。
    /// </summary>
    public static class BlindNowOnlyTagger
    {
        public const int LayerNowOnly = 25;
        const int LayerDefault = 0;
        const int LayerThermal = 22;
        const int LayerEcho = 23;
        public const int LayerMemory = 24;

        static bool Movable(GameObject g)
        {
            // 生成済みのサーモ・エコロケ複製と、過去の人向けの文字類は触らない
            if (g.layer == LayerThermal || g.layer == LayerEcho || g.layer == LayerMemory) return false;
            if (g.name.StartsWith("VisionFX_") || g.name.StartsWith("T_") || g.name.StartsWith("E_")) return false;
            // 遮蔽用の黒い複製は過去の人に見せるためのものなので Default に置いたまま
            if (g.name.StartsWith("Blackout_")) return false;
            return true;
        }

        [MenuItem("BLIND/vision/3. 選択した物を「現在だけ」にする(過去人から隠す)")]
        public static void TagSelected()
        {
            var msg = Apply(LayerNowOnly, LayerDefault);
            EditorUtility.DisplayDialog("BLIND", msg, "OK");
        }

        [MenuItem("BLIND/vision/3b. 選択した物の「現在だけ」を解除する")]
        public static void UntagSelected()
        {
            var msg = Apply(LayerDefault, LayerNowOnly);
            EditorUtility.DisplayDialog("BLIND", msg, "OK");
        }

        /// <summary>ダイアログを出さない版。自動化からはこちらを呼ぶこと。</summary>
        public static string Apply(int to, int from)
        {
            var roots = Selection.gameObjects;
            if (roots == null || roots.Length == 0) return "オブジェクトを選択してから実行してください。";

            int moved = 0;
            var log = new StringBuilder();
            foreach (var root in roots)
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var g = r.gameObject;
                    if (g.layer != from) continue;
                    if (!Movable(g)) continue;
                    Undo.RecordObject(g, "NowOnly");
                    g.layer = to;
                    EditorUtility.SetDirty(g);
                    moved++;
                    if (log.Length < 2000) log.AppendLine("  " + g.name);
                }
            }

            string name = to == LayerNowOnly ? "NowOnly(25)へ移動" : "Default(0)へ戻した";
            return name + ": " + moved + "個\n" + log
                 + "\nこの後 [BLIND]→[vision]→[2. 全部屋にサーモ・エコロケを生成] を実行し、"
                 + "シーンを保存してください。";
        }

        /// <summary>
        /// room16 で、過去の人に見せる物を「人形」と「巨大な手」だけに絞る。
        ///
        /// これまで過去の人は現在の内装をそのまま見ていたので、
        /// 棚も家具も壁も自力で見えてしまい、エコロケ役と役割が重なっていた。
        /// 見える物を人形と手だけにすると、過去の人の視界は
        /// 「暗闇に人影が林立し、その中央に巨大な手がある」だけになる。
        /// 家具や壁は見えないまま当たり判定だけが残るので、
        /// どこを通れるかはエコロケ役に聞くしかない。
        ///
        /// サーモ・エコロケ側の複製は BlindVisionBuilder が
        /// Thermal(22)/Echo(23) 以外の全レイヤーから作るので、
        /// NowOnly に移しても2人の視界からは消えない。
        ///
        /// 何度実行しても同じ結果になる。
        /// </summary>
        [MenuItem("BLIND/vision/4. room16 の過去人を「人形と手だけ」にする")]
        public static void Room16OnlyDollsMenu()
        {
            EditorUtility.DisplayDialog("BLIND", Room16OnlyDolls(), "OK");
        }

        /// <summary>ダイアログを出さない版。自動化からはこちらを呼ぶこと。</summary>
        public static string Room16OnlyDolls()
        {
            var room = Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(t => t.name == "room16" && t.parent != null && t.parent.name == "=== ROOMS ===");
            if (room == null) return "room16 が === ROOMS === の下に見つからない";

            // 過去の人に見せ続けるグループ
            var keep = new[] { "Prop_Dolls", "Prop_GiantHand" };

            int hidden = 0, kept = 0;
            foreach (var r in room.GetComponentsInChildren<Renderer>(true))
            {
                var g = r.gameObject;
                if (!Movable(g)) continue;   // 生成済みの複製には触らない

                bool visible = false;
                for (var tr = g.transform; tr != null && tr != room; tr = tr.parent)
                    if (keep.Contains(tr.name)) { visible = true; break; }

                int want = visible ? LayerDefault : LayerNowOnly;
                if (visible) kept++;
                if (g.layer == want) continue;
                if (g.layer != LayerDefault && g.layer != LayerNowOnly) continue;

                Undo.RecordObject(g, "room16 NowOnly");
                g.layer = want;
                EditorUtility.SetDirty(g);
                if (want == LayerNowOnly) hidden++;
            }

            int shell = BuildBlackoutShell(room);

            int now = room.GetComponentsInChildren<Renderer>(true)
                          .Count(r => r.gameObject.layer == LayerNowOnly);
            return "room16: 過去人から隠した " + hidden + "個（今回の変更分）\n"
                 + "  過去人に見えるまま: " + kept + "個（人形と巨大な手）\n"
                 + "  NowOnly(25) の合計: " + now + "個\n"
                 + "  黒い遮蔽シェル: " + shell + "枚\n"
                 + "この後 [BLIND]→[vision]→[2.] で room16 を作り直すこと。";
        }

        const string BlackoutRoot = "Vision_Blackout";
        const string BlackoutMat = "Assets/_BLIND/Art/Materials/Blackout.mat";

        /// <summary>
        /// 部屋の外殻を真っ黒な複製で覆う。
        ///
        /// 壁や天井を NowOnly に移すと、過去の人のカメラはそれを描かなくなる。
        /// ところが「描かない」は「遮らない」でもあるので、壁の向こうにある
        /// 隣の部屋（room17 の廊下など）がそのまま見えてしまった。
        /// 過去の人の視界を削るつもりが、逆に他の部屋の間取りまで渡してしまう。
        ///
        /// そこで、外殻だけを真っ黒・陰影なしの複製にして Default に置く。
        /// 深度は書くので向こう側は隠れ、色は黒なので何も伝えない。
        /// 当たり判定は元のオブジェクトが持っているので複製からは外す。
        ///
        /// 元の壁の面の向き（内向き）をそのまま使う。箱を1つ置く方式だと
        /// 内側から見たとき背面カリングで消えてしまい遮蔽にならない。
        /// </summary>
        static int BuildBlackoutShell(Transform room)
        {
            var old = room.Find(BlackoutRoot);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var gen = room.Find("GeneratedRoom");
            if (gen == null) return 0;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(BlackoutMat);
            if (mat == null)
            {
                var sh = Shader.Find("Unlit/Color");
                if (sh == null) return 0;
                mat = new Material(sh) { color = Color.black };
                AssetDatabase.CreateAsset(mat, BlackoutMat);
            }
            mat.color = Color.black;

            var root = new GameObject(BlackoutRoot);
            Undo.RegisterCreatedObjectUndo(root, "Blackout shell");
            root.transform.SetParent(room, false);
            root.layer = LayerDefault;

            int n = 0;
            foreach (var src in gen.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = src.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                var go = new GameObject("Blackout_" + src.name);
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                go.transform.localScale = src.transform.lossyScale;
                go.layer = LayerDefault;

                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[src.sharedMaterials.Length == 0 ? 1 : src.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                n++;
            }
            EditorUtility.SetDirty(root);
            return n;
        }

        /// <summary>現在 NowOnly になっている物の数を部屋ごとに数える（検証用）。</summary>
        public static string Report()
        {
            var log = new StringBuilder("NowOnly(25) の内訳\n");
            int total = 0;
            var rooms = Object.FindObjectsOfType<Transform>(true)
                              .Where(t => t.name.StartsWith("room") && t.parent != null)
                              .ToList();
            foreach (var room in rooms)
            {
                int n = room.GetComponentsInChildren<Renderer>(true)
                            .Count(r => r.gameObject.layer == LayerNowOnly);
                if (n == 0) continue;
                log.AppendLine("  " + room.name + ": " + n + "個");
                total += n;
            }
            log.AppendLine("合計 " + total + "個");
            return log.ToString();
        }
    }
}
