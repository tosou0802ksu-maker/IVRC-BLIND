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
        /// room16 で、過去の人から「部屋の中に置いてある物」だけを隠す。
        ///
        /// 壁・床・天井・腰壁（＝部屋そのもの）は見せたままにする。
        /// 一度これも隠してみたが、視界を削るどころか逆効果だった。
        /// 壁を描かないということは壁が遮らないということでもあるので、
        /// 隣の room17 の廊下が丸ごと透けて見え、他の部屋の間取りまで
        /// 過去の人に渡してしまっていた。
        ///
        /// 隠すのは棚・家具・小物だけ。当たり判定は残るので、
        /// 「部屋の形は見えるのに、そこに何が置いてあるかは分からない」
        /// という状態になる。ぶつかる物の在り処はエコロケ役に聞くしかない。
        ///
        /// 人形と巨大な手は、この部屋の主役なので見せたままにする。
        ///
        /// サーモ・エコロケ側の複製は BlindVisionBuilder が
        /// Thermal(22)/Echo(23) 以外の全レイヤーから作るので、
        /// NowOnly に移しても2人の視界からは消えない。
        ///
        /// 何度実行しても同じ結果になる。
        /// </summary>
        [MenuItem("BLIND/vision/4. room16 の過去人から「中の物」を隠す")]
        public static void Room16OnlyDollsMenu()
        {
            EditorUtility.DisplayDialog("BLIND", Room16OnlyDolls(), "OK");
        }

        /// <summary>過去の人から隠すグループ。ここに挙げた物だけが NowOnly に移る。</summary>
        static readonly string[] Room16Hidden =
        {
            "Shelves",             // 書架
            "Prop_ShelfDecor",     // 棚の上の小物・マネキン頭部
            "Prop_Furniture",      // 机・箱・樽・梯子など
            "Prop_FurnitureDecor", // 人形の部位など家具まわりの小物
        };

        /// <summary>ダイアログを出さない版。自動化からはこちらを呼ぶこと。</summary>
        public static string Room16OnlyDolls()
        {
            var room = Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(t => t.name == "room16" && t.parent != null && t.parent.name == "=== ROOMS ===");
            if (room == null) return "room16 が === ROOMS === の下に見つからない";

            // 以前の版が作った黒い遮蔽シェルは、壁を見せる方式では要らない
            var shell = room.Find(BlackoutRoot);
            if (shell != null) Undo.DestroyObjectImmediate(shell.gameObject);

            int hidden = 0, shown = 0;
            foreach (var r in room.GetComponentsInChildren<Renderer>(true))
            {
                var g = r.gameObject;
                if (!Movable(g)) continue;   // 生成済みの複製には触らない

                bool hide = false;
                for (var tr = g.transform; tr != null && tr != room; tr = tr.parent)
                    if (Room16Hidden.Contains(tr.name)) { hide = true; break; }

                int want = hide ? LayerNowOnly : LayerDefault;
                if (g.layer != LayerDefault && g.layer != LayerNowOnly) continue;
                if (g.layer == want) { if (hide) hidden++; else shown++; continue; }

                Undo.RecordObject(g, "room16 NowOnly");
                g.layer = want;
                EditorUtility.SetDirty(g);
                if (hide) hidden++; else shown++;
            }

            return "room16: 過去人から隠した " + hidden + "個（棚・家具・小物）\n"
                 + "  過去人に見えるまま: " + shown + "個（壁・床・天井・腰壁・照明・人形・巨大な手）\n"
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
