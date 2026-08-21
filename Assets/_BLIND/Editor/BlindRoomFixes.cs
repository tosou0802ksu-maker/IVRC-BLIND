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

        // -------------------------------------------------------------

        [MenuItem("BLIND/部屋修正/3. room9に「過去人だけ塞がって見える」通路を作る")]
        public static void FixRoom9Menu()
        {
            EditorUtility.DisplayDialog("BLIND", FixRoom9FakeWall(), "OK");
        }

        /// <summary>
        /// room9（ロッカー部屋）の東側の開口(2m×3.25m)に、
        /// 過去人にだけ見える「壁」を重ねる。
        ///
        /// Memory(24) は過去人にしか映らないレイヤーなので、
        ///   過去人      … 行き止まりの壁に見える
        ///   エコロケ    … 開口として見える（Echo層には何も置かない）
        ///   サーモ      … 同上
        /// になる。コライダーは付けないので、実際には歩いて通れる。
        ///
        /// 過去人が「そっちは行き止まりだ」と言い、エコロケが「いや空いてる」と返す。
        /// 過去人が見ているのは"かつてこの部屋がどうだったか"なので、
        /// 嘘をついているわけではなく、後から壊された壁を見ている、という理屈になる。
        /// </summary>
        public static string FixRoom9FakeWall()
        {
            var room9 = Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == "room9");
            if (room9 == null) return "room9 が見つかりません。";

            const string objName = "Memory_SealedDoorway_East";
            var old = room9.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == objName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // 東の壁 x=-9.2、開口は z -46.30〜-44.30 / y 0〜3.25
            var center = new Vector3(-9.20f, 1.625f, -45.30f);
            var size = new Vector3(0.20f, 3.25f, 2.00f);

            // 同じ部屋の壁の材質をそのまま使う。過去人の目には
            // 他の壁と地続きに見えないと「壁」として通用しない。
            var wallMat = room9.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.gameObject.layer == 0 && r.gameObject.name == "WallSegment")
                .Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objName;
            go.transform.SetParent(room9, true);
            go.transform.position = center;
            go.transform.localScale = size;
            go.layer = BlindNowOnlyTagger.LayerMemory;
            if (wallMat != null) go.GetComponent<MeshRenderer>().sharedMaterial = wallMat;

            // ここが肝。当たり判定を消さないと本当に塞がってしまう。
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(go, "room9 fake wall");

            EditorSceneManagerSetDirty(room9);
            return "room9 東の開口に Memory(24) の壁を設置しました。\n"
                 + "  位置 " + center.ToString("F2") + " / 大きさ " + size.ToString("F2") + "\n"
                 + "  材質 " + (wallMat != null ? wallMat.name : "(見つからず・既定)") + "\n"
                 + "  コライダー無し＝通り抜けられる\n"
                 + "過去人だけが行き止まりに見え、エコロケ役には開口として見えます。";
        }

        // -------------------------------------------------------------

        [MenuItem("BLIND/部屋修正/4. room16(人形部屋)を暗くする")]
        public static void FixRoom16Menu()
        {
            EditorUtility.DisplayDialog("BLIND", DarkenRoom16(), "OK");
        }

        /// <summary>
        /// room16 の照明を落とす。
        ///
        /// 数体の人形と巨大な手にだけ体温を持たせた（BlindVisionBuilder.HotDolls）ので、
        /// 過去人の視界が明るいままだと「見えている人形」と「熱い人形」の対応が
        /// 過去人の側だけで完結してしまい、サーモ役の情報が要らなくなる。
        /// 部屋を暗くして、過去人にも人形が何体あるのか分からない状態にする。
        ///
        /// 完全な暗闇にはしない。過去人は文字と看板を読む役なので、
        /// 手元が見える程度は残す（既存比 35%）。
        /// </summary>
        public static string DarkenRoom16()
        {
            var room16 = Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == "room16");
            if (room16 == null) return "room16 が見つかりません。";

            // 「今の値に0.35を掛ける」書き方にすると、二度実行しただけで
            // 0.1225倍まで落ちて部屋が完全な暗闇になる（実際にそうなった）。
            // 何度実行しても同じ結果になるよう、元の明るさを表に持って絶対値で入れる。
            // Original は改変前にシーンから読み取った実測値。
            float[] original = { 9.041876f, 2f, 3f, 0f, 2f, 7f, 3f };
            const float scale = 0.35f;

            var log = new StringBuilder("room16 の照明を元の " + (scale * 100) + "% に設定しました\n");
            int n = 0, idx = 0;
            foreach (var l in room16.GetComponentsInChildren<Light>(true))
            {
                string nm = l.gameObject.name;
                // 穴から差す光は道しるべなので残す
                if (nm.StartsWith("HoleLight")) { log.AppendLine("  " + nm + " : 1.50 据え置き(道しるべ)"); continue; }

                float target;
                if (nm.StartsWith("CoolFill")) target = 0.85f * scale;      // 元 0.85
                else if (idx < original.Length) target = original[idx++] * scale;
                else continue;

                if (target <= 0.001f) continue;
                Undo.RecordObject(l, "darken room16");
                float before = l.intensity;
                l.intensity = target;
                EditorUtility.SetDirty(l);
                log.AppendLine("  " + nm + " : " + before.ToString("F2") + " → " + target.ToString("F2"));
                n++;
            }
            log.AppendLine("変更 " + n + "灯");
            EditorSceneManagerSetDirty(room16);
            return log.ToString();
        }

        static void EditorSceneManagerSetDirty(Transform t)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
    }
}
