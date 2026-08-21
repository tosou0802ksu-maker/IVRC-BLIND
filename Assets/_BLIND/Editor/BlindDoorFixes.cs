using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// soshi から取り込んだ 5部屋（room8/11/13/17/19）が「入れない」問題の修正。
    ///
    /// 実測して分かった原因は4つで、どれも別々の問題：
    ///
    ///   1. 開口の高さが 2.00m しかない
    ///      既存18部屋はすべて 2.30m で建っている。取り込んだ5部屋だけ 2.00m。
    ///      接続部で 2.30 の穴と 2.00 の穴が突き合わさり、段差として見える。
    ///
    ///   2. room17 の南の開口が room16 の北の開口と 2.0m ずれている
    ///      room16 の北開口は z -18.00..-16.80、room17 の南開口は z -16.00..-14.80。
    ///      重なりがゼロなので room17 から先へは物理的に到達できない。
    ///      取り込み時に room17 を +2.1m ずらしたのが原因。
    ///
    ///   3. room19 に床が無い（generateFloor = false）
    ///
    ///   4. room13 の内部間仕切り3枚のドア9枚が閉まったまま
    ///      羽根の BoxCollider だけが通路を塞いでいる。奥の room14 まで到達不能。
    ///
    /// BuildRoom() は原則呼ばない。room8/11/13/17 の GeneratedRoom には
    /// 手で足した柱・梁・床帯が大量に入っており、BuildRoom() はその親ごと
    /// 作り直すので、それらが消える。代わりに、まぐさ（開口上部の板）と
    /// ドア枠だけを直接動かして開口を広げる。
    /// room19 だけは中身が完全に自動生成のみなので BuildRoom() してよい。
    ///
    /// 何度実行しても同じ結果になるよう、すべて絶対値で指定している。
    /// </summary>
    public static class BlindDoorFixes
    {
        /// <summary>世界の標準の開口高さ。既存18部屋がすべてこの値で建っている。</summary>
        const float StandardDoorHeight = 2.30f;

        /// <summary>取り込んだ5部屋の開口高さ。これを StandardDoorHeight まで引き上げる。</summary>
        const float ImportedDoorHeight = 2.00f;

        /// <summary>room17/room19 を room16 の開口に合わせるための Z 座標。</summary>
        const float Room17And19Z = -19.90f;

        static readonly string[] ImportedRooms = { "room8", "room11", "room13", "room17", "room19" };

        [MenuItem("BLIND/部屋合体/2. 取り込んだ部屋の扉を直す(高さ・位置・床・内部ドア)")]
        public static void FixAllMenu()
        {
            EditorUtility.DisplayDialog("BLIND", FixAll(), "OK");
        }

        /// <summary>ダイアログを出さない版。自動化からはこちらを呼ぶこと。</summary>
        public static string FixAll()
        {
            var log = new StringBuilder();
            log.AppendLine(SyncRoom16Config());
            log.AppendLine(AlignRoom17And19());
            log.AppendLine(RebuildRoom19WithFloor());
            log.AppendLine(RaiseDoorways());
            log.AppendLine(OpenRoom13InnerDoors());

            // Physics.autoSyncTransforms は 2022 では既定で false。
            // Transform を動かしただけでは当たり判定の位置は古いままで、
            // 直後に Raycast や OverlapCapsule で確かめると「まだ塞がっている」
            // という嘘の結果が返る。移動したら必ずここで同期すること。
            Physics.SyncTransforms();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return log.ToString();
        }

        static Transform Room(string name)
        {
            return Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(t => t.name == name && t.parent != null && t.parent.name == "=== ROOMS ===");
        }

        static Component Builder(string name)
        {
            var r = Room(name);
            if (r == null) return null;
            return r.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "RoomBuilder3");
        }

        // ------------------------------------------------------------------
        // 1. room16 の Inspector を実物に合わせる（形は一切変えない）
        // ------------------------------------------------------------------

        /// <summary>
        /// room16 は Inspector の値と実際に建っている形が食い違っている。
        ///   Inspector: 北 H2.0 off0 / 南 H2.0 off0 / 西 H2.0 off0
        ///   実  物  : 北 H2.3 off-2.0 / 南 H2.3 off+1.8 / 西 H2.3 off+2.6
        /// この状態で誰かが room16 の BuildRoom() を押すと開口が全部動き、
        /// room15・room17・room18 との接続が同時に壊れる。
        /// 形には触れず、データだけを実物に合わせて地雷を抜いておく。
        /// </summary>
        static string SyncRoom16Config()
        {
            var rb = Builder("room16");
            if (rb == null) return "room16: RoomBuilder3 が無い（スキップ）";

            var t = rb.GetType();
            Undo.RecordObject(rb, "Sync room16 config");

            int n = 0;
            n += SetWall(rb, t, "northWall", 1.2f, 2.30f, -2.0f);
            n += SetWall(rb, t, "southWall", 1.2f, 2.30f, 1.8f);
            n += SetWall(rb, t, "westWall", 1.2f, 2.30f, 2.6f);

            EditorUtility.SetDirty(rb);
            return "room16: Inspector を実物に合わせた（変更 " + n + " 項目 / 形は不変）";
        }

        static int SetWall(Component rb, System.Type t, string wallName, float w, float h, float off)
        {
            var f = t.GetField(wallName);
            if (f == null) return 0;
            var cfg = f.GetValue(rb);
            if (cfg == null) return 0;
            var ct = cfg.GetType();

            int n = 0;
            n += SetIfDiff(ct.GetField("doorWidth"), cfg, w);
            n += SetIfDiff(ct.GetField("doorHeight"), cfg, h);
            n += SetIfDiff(ct.GetField("doorOffset"), cfg, off);
            return n;
        }

        static int SetIfDiff(System.Reflection.FieldInfo f, object target, float v)
        {
            if (f == null) return 0;
            if (Mathf.Approximately((float)f.GetValue(target), v)) return 0;
            f.SetValue(target, v);
            return 1;
        }

        // ------------------------------------------------------------------
        // 2. room17 / room19 を room16 の開口に合わせる
        // ------------------------------------------------------------------

        /// <summary>
        /// room16 の北開口の中心は z = -17.40。
        /// room17 は南開口の中心が原点 +2.5 の位置に建つので、
        /// room17 の原点 z は -17.40 - 2.50 = -19.90 でなければならない。
        /// room19 は room17 の北開口に接続しているので、同じ量だけ一緒に動かす。
        /// </summary>
        static string AlignRoom17And19()
        {
            var log = new StringBuilder();
            foreach (var name in new[] { "room17", "room19" })
            {
                var r = Room(name);
                if (r == null) { log.AppendLine(name + ": 見つからない"); continue; }

                var p = r.localPosition;
                if (Mathf.Abs(p.z - Room17And19Z) < 0.001f)
                {
                    log.AppendLine(name + ": 位置は既に正しい (z=" + Room17And19Z + ")");
                    continue;
                }
                Undo.RecordObject(r, "Align " + name);
                r.localPosition = new Vector3(p.x, p.y, Room17And19Z);
                EditorUtility.SetDirty(r);
                log.AppendLine(name + ": z " + p.z.ToString("F2") + " -> " + Room17And19Z.ToString("F2"));
            }
            return log.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------
        // 3. room19 に床を付ける
        // ------------------------------------------------------------------

        /// <summary>
        /// room19 は generateFloor が false で、床が1枚も無い。
        /// この部屋の GeneratedRoom は WallSegment / FramePiece / CeilingPanel
        /// しか入っておらず、手作業で足した物が無いので BuildRoom() してよい。
        /// ついでに開口高さも標準の 2.30 にしてから建て直す。
        /// </summary>
        static string RebuildRoom19WithFloor()
        {
            var rb = Builder("room19");
            if (rb == null) return "room19: RoomBuilder3 が無い（スキップ）";

            var t = rb.GetType();
            var root = Room("room19");

            // 手で足した物が紛れ込んでいたら建て直さない（消してしまうため）
            var gen = root.Find("GeneratedRoom");
            if (gen != null)
            {
                var names = gen.GetComponentsInChildren<Renderer>(true).Select(r => r.name).ToList();
                var foreign = names.Where(n =>
                    !n.StartsWith("WallSegment") && !n.StartsWith("FramePiece") &&
                    !n.StartsWith("CeilingPanel") && !n.StartsWith("FloorTile") &&
                    !n.StartsWith("FloorSlab")).ToList();
                if (foreign.Count > 0)
                    return "room19: GeneratedRoom に自動生成でない物が " + foreign.Count
                         + " 個ある（" + string.Join(", ", foreign.Take(3)) + "）。"
                         + "建て直すと消えるので中止した。";
            }

            Undo.RecordObject(rb, "Rebuild room19");
            int n = 0;
            var gf = t.GetField("generateFloor");
            if (gf != null && !(bool)gf.GetValue(rb)) { gf.SetValue(rb, true); n++; }
            n += SetWall(rb, t, "southWall", 1.2f, StandardDoorHeight, -3.0f);

            if (n == 0) return "room19: 既に床あり・高さ 2.30（変更なし）";

            t.GetMethod("BuildRoom").Invoke(rb, null);
            EditorUtility.SetDirty(rb);

            var floors = root.GetComponentsInChildren<Renderer>(true).Count(r => r.name.StartsWith("Floor"));
            return "room19: 床を生成して建て直した（床 " + floors + " 枚 / 開口高さ 2.30）";
        }

        // ------------------------------------------------------------------
        // 4. 開口の高さを 2.00 -> 2.30 に上げる
        // ------------------------------------------------------------------

        /// <summary>
        /// まぐさ（開口の上をふさぐ板）の下端を 0.30m 持ち上げ、
        /// ドア枠の上枠と柱もそれに合わせて伸ばす。
        /// BuildRoom() を使わないので、手で足した柱や梁は残る。
        /// </summary>
        static string RaiseDoorways()
        {
            var log = new StringBuilder();
            float delta = StandardDoorHeight - ImportedDoorHeight;

            foreach (var name in ImportedRooms)
            {
                var root = Room(name);
                if (root == null) { log.AppendLine(name + ": 見つからない"); continue; }

                var walls = root.Find("GeneratedRoom/Walls");
                if (walls == null) { log.AppendLine(name + ": Walls が無い"); continue; }

                int lintels = 0, rails = 0, posts = 0;

                foreach (var r in walls.GetComponentsInChildren<Renderer>(true))
                {
                    var b = r.bounds;
                    float h = b.max.y - b.min.y;

                    // まぐさ: 床から浮いていて天井まで届く板。下端が 2.00 付近の物だけ。
                    if (r.name.StartsWith("WallSegment") &&
                        Mathf.Abs(b.min.y - ImportedDoorHeight) < 0.06f && h > 1.0f)
                    { Retarget(r.transform, b.min.y + delta, b.max.y); lintels++; continue; }

                    if (!r.name.StartsWith("FramePiece")) continue;

                    // 上枠: 薄い板で、上端が 2.00 付近。厚みを保ったまま 0.30 持ち上げる。
                    if (h < 0.12f && Mathf.Abs(b.max.y - ImportedDoorHeight) < 0.06f && b.min.y > 0.5f)
                    { Retarget(r.transform, b.min.y + delta, b.max.y + delta); rails++; continue; }

                    // 柱: 床から立ち上がって上枠の下まで届く縦材。上端だけ 0.30 伸ばす。
                    if (h > 1.0f && b.min.y < 0.5f &&
                        Mathf.Abs(b.max.y - (ImportedDoorHeight - 0.05f)) < 0.08f)
                    { Retarget(r.transform, b.min.y, b.max.y + delta); posts++; continue; }
                }

                // Inspector の値も合わせておく（次に誰かが建て直したとき用）
                var rb = Builder(name);
                if (rb != null)
                {
                    var t = rb.GetType();
                    Undo.RecordObject(rb, "Raise door height");
                    foreach (var wn in new[] { "northWall", "southWall", "eastWall", "westWall" })
                    {
                        var f = t.GetField(wn);
                        if (f == null) continue;
                        var cfg = f.GetValue(rb);
                        var ct = cfg.GetType();
                        if (!(bool)ct.GetField("hasDoor").GetValue(cfg)) continue;
                        SetIfDiff(ct.GetField("doorHeight"), cfg, StandardDoorHeight);
                    }
                    EditorUtility.SetDirty(rb);
                }

                log.AppendLine(string.Format("{0}: まぐさ{1} 上枠{2} 柱{3} を 2.00 -> 2.30 に",
                    name, lintels, rails, posts));
            }
            return log.ToString().TrimEnd();
        }

        /// <summary>
        /// 立方体の上下の端をワールド座標で指定し直す。
        /// 中心が原点の Cube なので、縦倍率と中心の高さだけで決まる。
        /// 部屋の回転は Y 軸まわりだけなので、縦方向はこの計算で正しい。
        /// </summary>
        static void Retarget(Transform t, float newMinY, float newMaxY)
        {
            var r = t.GetComponent<Renderer>();
            if (r == null) return;
            var b = r.bounds;
            float oldH = b.max.y - b.min.y;
            float newH = newMaxY - newMinY;
            if (oldH < 0.0001f || newH <= 0f) return;
            if (Mathf.Abs(oldH - newH) < 0.0005f &&
                Mathf.Abs(b.min.y - newMinY) < 0.0005f) return;   // 既に済み

            Undo.RecordObject(t, "Retarget wall piece");
            var s = t.localScale;
            t.localScale = new Vector3(s.x, s.y * (newH / oldH), s.z);
            var p = t.position;
            t.position = new Vector3(p.x, (newMinY + newMaxY) * 0.5f, p.z);
            EditorUtility.SetDirty(t);
        }

        // ------------------------------------------------------------------
        // 5. room13 の内部ドアを開ける
        // ------------------------------------------------------------------

        /// <summary>
        /// room13 は間仕切り3枚で4つの区画に割られていて、その9枚のドアが
        /// 閉まったままになっている。通路を塞いでいるのは羽根の BoxCollider
        /// だけなので、蝶番の位置を軸にして 90度 開く。
        /// 判定を消すのではなく実際に開くのは、閉じて見えるドアをすり抜けると
        /// 「見えている物が嘘」になり、この作品の視界ギミックの前提が崩れるため。
        /// </summary>
        static string OpenRoom13InnerDoors()
        {
            var root = Room("room13");
            if (root == null) return "room13: 見つからない";

            var doors = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Door_0"))
                .Where(t => t.GetComponentsInChildren<Collider>(true).Length > 0)
                .ToList();

            int opened = 0, already = 0, skipped = 0;
            foreach (var d in doors)
            {
                var r = d.GetComponentInChildren<Renderer>();
                if (r == null) { skipped++; continue; }
                var b = r.bounds;

                // 閉まっている羽根は Z 方向に幅を持ち、X 方向は板の厚みしかない
                float spanZ = b.max.z - b.min.z, spanX = b.max.x - b.min.x;
                if (spanZ < 0.6f || spanX > 0.3f) { already++; continue; }

                // 蝶番は -Z 側の端。そこを軸に +90度 回すと羽根は +X 側へ開く
                var hinge = new Vector3(b.center.x, d.position.y, b.min.z);
                var d2 = d.position - hinge;
                var moved = new Vector3(d2.z, d2.y, -d2.x);   // Y軸まわり +90度

                Undo.RecordObject(d, "Open room13 door");
                d.position = hinge + moved;
                d.rotation = Quaternion.Euler(0f, 90f, 0f) * d.rotation;
                EditorUtility.SetDirty(d);
                opened++;
            }
            return "room13: 内部ドアを開いた " + opened + "枚（開済 " + already + " / 対象外 " + skipped + "）";
        }
    }
}
