using System.Collections.Generic;
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

        /// <summary>過去の人に何を見せるかの部屋ごとの指定。</summary>
        class Rule
        {
            public string room;
            /// <summary>丸ごと隠すグループ名（そのオブジェクト以下すべて）。</summary>
            public string[] hide = new string[0];
            /// <summary>1つおきに隠すグループ名。半分だけ見える状態を作る。</summary>
            public string[] hideHalf = new string[0];
            /// <summary>指定するとこれ以外を全部隠す（建物は常に残る）。名前の先頭一致。</summary>
            public string[] showOnly = null;
            public string note;
        }

        /// <summary>
        /// 過去の人の視界は「部屋の形は見えるが、中に何があるかは分からない」を狙う。
        /// 床・壁紙・天井はどの部屋でも必ず見せる（showOnly でも建物は除外しない）。
        /// 隠した物も当たり判定は残るので、見えない障害物としてそこに在り続ける。
        /// </summary>
        static readonly Rule[] Rules =
        {
            new Rule { room = "room6",
                hide = new[] { "cumos", "roadblocks", "fire_hydrant_1k" },
                note = "背の高い壁状の塊と車止めを消す。コーン・道路標識・草・路面は残す。"
                     + "標識を残すのは、文字と矢印が過去の人だけの情報源だから。" },

            new Rule { room = "room9",
                hideHalf = new[] { "Props_Lockers" },
                note = "ロッカー55台を1台おきに消す。見えているロッカーの間に"
                     + "見えないロッカーが挟まっているので、列の隙間を信用できなくなる。" },

            new Rule { room = "room11",
                showOnly = new[] { "CRT_final (4)", "Wire" },
                note = "真ん中のテレビの山と、床を這う配線だけ。天井の配線束・配管・"
                     + "蛍光灯・ロッカー・器具はすべて消える。"
                     + "Wire は先頭一致。小文字の wires（天井束）には当たらない。" },

            new Rule { room = "room12",
                hide = new[] { "Prop_MonitorPile" },
                hideHalf = new[] { "Prop_Daruma" },
                note = "真ん中のモニタの山を消し、だるまは1体おきに消す。" },

            new Rule { room = "room15",
                hide = new[] { "Prop_BurningMannequin", "Fire_Ring" },
                note = "燃えている人を過去の人から消す。サーモ役だけが見つけられる存在にする。"
                     + "動く熱源の複製は Thermal(22) 側に別に作られているので影響しない。" },

            new Rule { room = "room16",
                hide = new[] { "Shelves", "Prop_ShelfDecor", "Prop_Furniture", "Prop_FurnitureDecor" },
                note = "棚・家具・小物を消す。壁紙・床・天井・照明・人形・巨大な手は残す。" },
        };

        /// <summary>
        /// 建物そのもの。どの部屋でも過去の人に見せ続ける。
        /// showOnly を指定した部屋でも、ここに当たる物は隠さない。
        /// </summary>
        static bool IsArchitecture(GameObject g, Transform room)
        {
            for (var tr = g.transform; tr != null && tr != room; tr = tr.parent)
            {
                var n = tr.name;
                if (n == "GeneratedRoom" || n == "Ceiling" || n == "Panelling"
                    || n.EndsWith("_Dado") || n.EndsWith("_Ceiling")) return true;
            }
            var rn = g.name;
            return rn.StartsWith("WallSegment") || rn.StartsWith("FloorTile")
                || rn.StartsWith("FloorSlab") || rn.StartsWith("CeilingPanel")
                || rn.StartsWith("FramePiece") || rn.StartsWith("Cornice")
                || rn.StartsWith("Baseboard") || rn.StartsWith("CofferPanel")
                || rn.StartsWith("Stile") || rn.StartsWith("Rail")
                || rn.StartsWith("BeamX") || rn.StartsWith("BeamZ");
        }

        [MenuItem("BLIND/vision/5. 全部屋の過去人の視界を表どおりにする")]
        public static void ApplyPastPersonRulesMenu()
        {
            EditorUtility.DisplayDialog("BLIND", ApplyPastPersonRules(), "OK");
        }

        /// <summary>ダイアログを出さない版。自動化からはこちらを呼ぶこと。</summary>
        public static string ApplyPastPersonRules()
        {
            var log = new StringBuilder();
            foreach (var rule in Rules) log.AppendLine(ApplyRule(rule));
            log.AppendLine("この後 [BLIND]→[vision]→[2.] で作り直すこと。");
            return log.ToString();
        }

        static string ApplyRule(Rule rule)
        {
            var room = Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(t => t.name == rule.room && t.parent != null && t.parent.name == "=== ROOMS ===");
            if (room == null) return rule.room + ": 見つからない";

            // 以前の版が作った黒い遮蔽シェルは、壁を見せる方式では要らない
            var shell = room.Find(BlackoutRoot);
            if (shell != null) Undo.DestroyObjectImmediate(shell.gameObject);

            // 1つおきに隠す指定は、グループの子の順番で決める。
            // レンダラー単位で数えるとロッカー1台が本体と扉で割れてしまうため。
            var halfHidden = new HashSet<Transform>();
            foreach (var gn in rule.hideHalf)
            {
                var grp = room.Find(gn);
                if (grp == null) continue;
                int i = 0;
                foreach (Transform child in grp)
                {
                    if (i % 2 == 1) halfHidden.Add(child);
                    i++;
                }
            }

            int hidden = 0, shown = 0;
            foreach (var r in room.GetComponentsInChildren<Renderer>(true))
            {
                var g = r.gameObject;
                if (!Movable(g)) continue;
                if (g.layer != LayerDefault && g.layer != LayerNowOnly) continue;

                bool hide = false;
                if (!IsArchitecture(g, room))
                {
                    for (var tr = g.transform; tr != null && tr != room; tr = tr.parent)
                    {
                        if (rule.hide.Contains(tr.name)) { hide = true; break; }
                        if (halfHidden.Contains(tr)) { hide = true; break; }
                    }
                    if (!hide && rule.showOnly != null)
                    {
                        bool keep = false;
                        for (var tr = g.transform; tr != null && tr != room && !keep; tr = tr.parent)
                            foreach (var k in rule.showOnly)
                                if (tr.name == k || tr.name.StartsWith(k)) { keep = true; break; }
                        hide = !keep;
                    }
                }

                int want = hide ? LayerNowOnly : LayerDefault;
                if (hide) hidden++; else shown++;
                if (g.layer == want) continue;

                Undo.RecordObject(g, "past-person visibility");
                g.layer = want;
                EditorUtility.SetDirty(g);
            }
            return string.Format("{0,-7}: 隠す {1,4}個 / 見せる {2,4}個   {3}",
                                 rule.room, hidden, shown, rule.note);
        }

        /// <summary>旧名。room16 だけを処理する。</summary>
        public static string Room16OnlyDolls()
        {
            return ApplyRule(Rules.First(r => r.room == "room16"));
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
