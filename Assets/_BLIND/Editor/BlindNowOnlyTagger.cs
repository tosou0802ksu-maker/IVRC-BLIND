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
