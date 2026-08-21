using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 部屋そのものの作りを直す修正。シーンを作り直しても再適用できるよう
    /// 手作業ではなくコードにしてある（今日シーンが壊れて巻き戻した経緯があるため）。
    /// 何度実行しても結果が同じ（冪等）になるように書くこと。
    /// </summary>
    public static class BlindRoomFixes
    {
        [MenuItem("BLIND/部屋修正/1. room6とroom7の扉を繋ぐ")]
        public static void FixRoom67Menu()
        {
            EditorUtility.DisplayDialog("BLIND", FixRoom67(), "OK");
        }

        /// <summary>
        /// room6 と room7 の扉が繋がっていない問題。
        ///
        /// 両方の壁は x=-14 付近で接していて隙間は無い。繋がらない原因は
        /// 扉の位置が z 方向に 2.2m ずれていること。
        ///   room6 の扉  : world z = -16.9（room6 の道路の正面）
        ///   room7 の扉  : world z = -14.7
        /// room6 の扉は道路の突き当たりにあるので動かせない。room7 側を合わせる。
        ///
        /// room7 の原点は (-30.2, 0, -20.4) で回転なし。よって local z = world z + 20.4。
        /// 扉の中心を local 5.70 → 3.50 に動かし、両脇の壁の長さを詰め直す。
        /// </summary>
        public static string FixRoom67()
        {
            var room7 = Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == "room7");
            if (room7 == null) return "room7 が見つかりません。";

            const float doorZ = 3.50f;      // 新しい扉の中心(local z)。world -16.9
            const float halfW = 0.60f;      // 扉の幅 1.2m の半分
            var log = new StringBuilder("room7 の扉を world z=-14.7 → -16.9 へ移動\n");

            int moved = 0;
            foreach (var t in room7.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer != 0) continue;
                var lp = t.localPosition;
                var ls = t.localScale;

                // --- 扉の枠と、扉の上の壁（まぐさ）--- z=5.70 にあるものを 3.50 へ
                if (t.name == "FramePiece" || (t.name == "WallSegment" && Mathf.Abs(ls.z - 1.20f) < 0.01f))
                {
                    if (Mathf.Abs(lp.z - 5.70f) < 0.01f) { Undo.RecordObject(t, "room67"); lp.z = doorZ; t.localPosition = lp; moved++; }
                    // 縦枠は扉の中心から±0.575 の位置にある
                    else if (Mathf.Abs(lp.z - 5.13f) < 0.01f) { Undo.RecordObject(t, "room67"); lp.z = doorZ - 0.57f; t.localPosition = lp; moved++; }
                    else if (Mathf.Abs(lp.z - 6.28f) < 0.01f) { Undo.RecordObject(t, "room67"); lp.z = doorZ + 0.58f; t.localPosition = lp; moved++; }
                    continue;
                }

                // --- 扉の両脇の壁 --- 端は動かさず、扉側の端だけを詰める
                if (t.name != "WallSegment") continue;
                if (Mathf.Abs(ls.x - 0.20f) > 0.01f) continue;      // x=-14 の壁だけ（厚み0.2）
                if (Mathf.Abs(lp.x - 16.00f) > 0.01f) continue;

                float min = lp.z - ls.z * 0.5f;
                float max = lp.z + ls.z * 0.5f;

                // 壁の「端」ではなく「中心」がどちら側かで判定すること。
                // 端で判定すると、新しい扉の位置をまたいでいる壁（移動前の状態）が
                // どちらの条件にも当てはまらず、素通りしてしまう。
                if (lp.z < doorZ)                                    // 南側の壁：奥端は据え置き、扉側を詰める
                {
                    float newMax = doorZ - halfW;
                    Undo.RecordObject(t, "room67");
                    t.localScale = new Vector3(ls.x, ls.y, newMax - min);
                    t.localPosition = new Vector3(lp.x, lp.y, (min + newMax) * 0.5f);
                    log.AppendLine("  南の壁  local z " + min.ToString("F2") + "〜" + newMax.ToString("F2"));
                    moved++;
                }
                else                                                 // 北側の壁：扉側を伸ばす
                {
                    float newMin = doorZ + halfW;
                    Undo.RecordObject(t, "room67");
                    t.localScale = new Vector3(ls.x, ls.y, max - newMin);
                    t.localPosition = new Vector3(lp.x, lp.y, (newMin + max) * 0.5f);
                    log.AppendLine("  北の壁  local z " + newMin.ToString("F2") + "〜" + max.ToString("F2"));
                    moved++;
                }
            }

            log.AppendLine("移動・変形したオブジェクト: " + moved + "個");
            log.AppendLine("扉の開口: world z -17.5〜-16.3（room6 と一致）");
            EditorSceneManagerSetDirty(room7);
            return log.ToString();
        }

        // -------------------------------------------------------------

        [MenuItem("BLIND/部屋修正/2. room14のレーザーをサーモ専用にする")]
        public static void FixRoom14LaserMenu()
        {
            EditorUtility.DisplayDialog("BLIND", FixRoom14Laser(), "OK");
        }

        /// <summary>
        /// room14 のレーザーを、サーモ役以外には一切見えなくする。
        ///
        /// Default(0) にある実体を NowOnly(25) へ移すと、
        /// 過去人のカリングマスクから外れて見えなくなる（PlayerVisionController が落とす）。
        /// エコロケ層の複製は Classify() が echo=false を返すので作られない。
        /// 残るのは Thermal(22) の 88℃ の複製だけ。
        ///
        /// コライダーはそのまま残るので、当たり判定と HazardZone は今までどおり効く。
        /// 「見えないのに焼かれる」状態になり、それを防げるのはサーモ役の一言だけになる。
        /// </summary>
        public static string FixRoom14Laser()
        {
            var room14 = Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == "room14");
            if (room14 == null) return "room14 が見つかりません。";

            int moved = 0, already = 0;
            foreach (var r in room14.GetComponentsInChildren<Renderer>(true))
            {
                var go = r.gameObject;
                string all = (go.name + "|" + (go.transform.parent != null ? go.transform.parent.name : "")).ToLower();
                if (!all.Contains("laser")) continue;
                if (go.layer == BlindNowOnlyTagger.LayerNowOnly) { already++; continue; }
                if (go.layer != 0) continue;                        // 生成済みの Thermal 複製は触らない
                Undo.RecordObject(go, "laser NowOnly");
                go.layer = BlindNowOnlyTagger.LayerNowOnly;
                EditorUtility.SetDirty(go);
                moved++;
            }

            EditorSceneManagerSetDirty(room14);
            return "room14 レーザー: NowOnly(25)へ移動 " + moved + "個（既に移動済み " + already + "個）\n"
                 + "この後 [BLIND]→[vision]→[2. 全部屋にサーモ・エコロケを生成] を実行してください。\n"
                 + "エコロケ層の複製が消え、サーモ層が 88℃ で作り直されます。";
        }

        static void EditorSceneManagerSetDirty(Transform t)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
    }
}
