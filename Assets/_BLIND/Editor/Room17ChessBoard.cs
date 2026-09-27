using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room17 を 16m 角の正方形にして、床を 2m タイル 8×8 のチェス盤に敷き直す。
    ///
    /// ・扉のある西の壁(x=0)と南の壁(z=0)は動かさず、東と北へ広げる
    /// ・床タイル(yukaarutokoros の64枚)は今の並び順(a〜h・1〜8、穴の位置)を保ったまま
    ///   2.0m 角・厚み 12.4cm・上面の高さ統一で敷き詰める
    /// ・ライトパネル(chess_lightpanel の64枚)は、今いちばん近いタイルの真上へ位置だけ移す
    /// ・扉・扉枠・天井・駒・家具・ボタン・照明は触らない
    ///
    /// サーモ(T_)／エコロケ(E_)の複製はタイル・パネル・壁の子なので一緒に変わる。
    /// ⚠️ room17/Vision_Echo の E_Floor_* (9枚) は子ではないのでついてこない。
    ///    天井を直した後に room17 のサーモ・エコロケを作り直すこと。
    ///
    /// Ctrl+Z 1回で丸ごと元に戻せる。
    /// </summary>
    public static class Room17ChessBoard
    {
        const int N = 8;
        const float Tile = 2.0f;
        const float TileThickness = 0.124f;
        const float WallThickness = 0.2f;

        /// <summary>内寸 16m。壁の中心どうしの距離は 16.2m。</summary>
        const float Inner = Tile * N;
        const float WallCenter = Inner + WallThickness;          // 東・北の壁の中心
        const float FullLength = Inner + WallThickness * 2f;     // 角で重なる長さ
        const float InnerStart = WallThickness * 0.5f;           // 西・南の壁の内面

        [MenuItem("BLIND/部屋修正/room17をチェス盤に整える")]
        public static void Menu()
        {
            var msg = Build();
            Debug.Log(msg);
            EditorUtility.DisplayDialog("BLIND", msg, "OK");
        }

        public static string Build()
        {
            var rooms = GameObject.Find("=== ROOMS ===");
            if (rooms == null) return "room17: === ROOMS === が無い";
            var room = rooms.transform.Find("room17");
            if (room == null) return "room17 が無い";

            var tileRoot = room.Find("room17items/floor/yukaarutokoros");
            var panelRoot = room.Find("room17items/chess/chess_lightpanel");
            var walls = room.Find("GeneratedRoom/Walls");
            if (tileRoot == null) return "room17: room17items/floor/yukaarutokoros が無い";
            if (panelRoot == null) return "room17: room17items/chess/chess_lightpanel が無い";
            if (walls == null) return "room17: GeneratedRoom/Walls が無い";

            var tiles = Children(tileRoot);
            var panels = Children(panelRoot);
            if (tiles.Count != N * N) return "room17: 床タイルが " + tiles.Count + " 枚（64枚のはず）。何もしていない";
            if (panels.Count != N * N) return "room17: ライトパネルが " + panels.Count + " 枚（64枚のはず）。何もしていない";

            // ⚠️ 大きさは localScale で決めるので、親が拡大・回転していると狂う
            if (!IsPlain(tileRoot, room)) return "room17: yukaarutokoros が回転か拡大されている。何もしていない";

            // --- タイルを 8列×8行 に振り分ける（x の順に8枚ずつ、列の中は z の順）---
            var byX = tiles.OrderBy(t => Local(room, Center(t)).x).ToList();
            var grid = new Transform[N, N];
            for (int c = 0; c < N; c++)
            {
                var col = byX.Skip(c * N).Take(N).OrderBy(t => Local(room, Center(t)).z).ToList();
                float spread = col.Max(t => Local(room, Center(t)).x) - col.Min(t => Local(room, Center(t)).x);
                if (spread > Tile * 0.5f) return "room17: タイルが8列にきれいに並んでいない（列 " + c + " の幅 " + spread.ToString("F2") + "m）。何もしていない";
                for (int r = 0; r < N; r++) grid[c, r] = col[r];
            }

            // --- パネルを「今いちばん近いタイル」に対応させる（1対1でなければ止める）---
            var panelOf = new Dictionary<Transform, Transform>();
            foreach (var p in panels)
            {
                var pp = Local(room, p.position);
                Transform best = null; float bd = float.MaxValue;
                foreach (var t in tiles)
                {
                    var tp = Local(room, Center(t));
                    float d = (pp.x - tp.x) * (pp.x - tp.x) + (pp.z - tp.z) * (pp.z - tp.z);
                    if (d < bd) { bd = d; best = t; }
                }
                if (panelOf.ContainsKey(best))
                    return "room17: ライトパネル " + p.name + " と " + panelOf[best].name + " が同じタイル " + best.name + " の上にある。何もしていない";
                panelOf[best] = p;
            }

            // --- 上面の高さ：いちばん多い高さにそろえる ---
            float top = tiles.Select(t => Mathf.Round(Local(room, new Vector3(0f, Top(t), 0f)).y * 1000f) / 1000f)
                             .GroupBy(y => y).OrderByDescending(g => g.Count()).First().Key;

            // --- 壁の広げ幅は、今の東・北の壁の位置から出す ---
            var segs = Children(walls).Where(w => w.name != "DoorFrame").ToList();
            var east = segs.Where(w => w.localScale.y >= 3f && w.localScale.z > 10f).OrderByDescending(w => w.localPosition.x).FirstOrDefault();
            var north = segs.Where(w => w.localScale.y >= 3f && w.localScale.x > 10f).OrderByDescending(w => w.localPosition.z).FirstOrDefault();
            if (east == null || north == null) return "room17: 東か北の壁が見つからない。何もしていない";
            float dx = WallCenter - east.localPosition.x;
            float dz = WallCenter - north.localPosition.z;
            float midX = east.localPosition.x * 0.5f;
            float midZ = north.localPosition.z * 0.5f;

            // ===== ここから書き換え（Ctrl+Z 1回で戻る）=====
            Undo.SetCurrentGroupName("room17 チェス盤");
            int group = Undo.GetCurrentGroup();
            var log = new StringBuilder("room17 をチェス盤に整えた（Ctrl+Z で戻せる）\n");

            // 床タイル
            for (int c = 0; c < N; c++)
            for (int r = 0; r < N; r++)
            {
                var t = grid[c, r];
                var mf = t.GetComponent<MeshFilter>();
                var mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                var target = new Vector3(InnerStart + Tile * (c + 0.5f), top - TileThickness * 0.5f, InnerStart + Tile * (r + 0.5f));

                Undo.RecordObject(t, "room17 tile");
                var s = new Vector3(Tile / mb.size.x, TileThickness / mb.size.y, Tile / mb.size.z);
                t.localScale = s;
                t.position = room.TransformPoint(target) - t.rotation * Vector3.Scale(mb.center, s);

                if (panelOf.TryGetValue(t, out var p))
                {
                    Undo.RecordObject(p, "room17 panel");
                    var w = room.TransformPoint(target);
                    p.position = new Vector3(w.x, p.position.y, w.z);
                }
            }
            log.AppendLine("  床タイル 64枚: " + Tile + "m角・厚み " + TileThickness + "m・上面 y=" + top.ToString("F3") + " に敷き直した");
            log.AppendLine("  ライトパネル 64枚: 対応するタイルの真上へ移した");

            // 壁
            int moved = 0;
            foreach (var w in segs)
            {
                var p = w.localPosition; var s = w.localScale;
                bool alongX = s.x > 10f && s.z <= 0.3f;
                bool alongZ = s.z > 10f && s.x <= 0.3f;
                bool westAfterDoor = !alongZ && s.x <= 0.3f && Mathf.Abs(p.x) < 0.3f && s.z > 3f && p.z - s.z * 0.5f > 3f;

                Undo.RecordObject(w, "room17 wall");
                if (alongX)
                {
                    p.x = FullLength * 0.5f - WallThickness * 0.5f;
                    s.x = FullLength;
                    if (p.z > midZ) p.z += dz;
                }
                else if (alongZ)
                {
                    p.z = FullLength * 0.5f - WallThickness * 0.5f;
                    s.z = FullLength;
                    if (p.x > midX) p.x += dx;
                }
                else if (westAfterDoor)
                {
                    // 扉より北側の西の壁。扉側の端はそのままで、北の端だけ延ばす
                    float z0 = p.z - s.z * 0.5f;
                    float z1 = WallCenter + WallThickness * 0.5f;
                    p.z = (z0 + z1) * 0.5f;
                    s.z = z1 - z0;
                }
                else continue;

                w.localPosition = p; w.localScale = s;
                moved++;
            }
            log.AppendLine("  壁 " + moved + "枚: 内寸 " + Inner + "m 角になるよう延ばした／移した（扉まわりは触っていない）");

            // RoomBuilder3 の記録を実物に合わせる（誰かが BuildRoom を押しても壊れないように）
            var rb = room.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "RoomBuilder3");
            if (rb != null)
            {
                var so = new SerializedObject(rb);
                SetFloat(so, "roomDepthX", WallCenter);
                SetFloat(so, "roomWidthZ", WallCenter);
                SetFloat(so, "tileDepthX", Tile);
                SetFloat(so, "tileWidthZ", Tile);
                so.ApplyModifiedProperties();
                log.AppendLine("  RoomBuilder3: 部屋 " + WallCenter + "m・タイル " + Tile + "m に合わせた");
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(room.gameObject.scene);

            log.AppendLine("触っていない: 扉・扉枠・天井・駒・家具・ボタン・照明");
            log.AppendLine("天井を直したら room17 のサーモ・エコロケを作り直すこと（Vision_Echo の E_Floor_* は古い大きさのまま）");
            return log.ToString();
        }

        /// <summary>
        /// GeneratedRoom/Ceiling の天井板を、今の壁の外側いっぱいに合わせる。
        /// 高さと厚みは変えない。T_/E_ の複製は子なので一緒に変わる。
        /// 壁の位置から測るので、チェス盤メニューの前後どちらで押しても合う。
        /// </summary>
        [MenuItem("BLIND/部屋修正/room17の天井を部屋に合わせる")]
        public static void FitCeilingMenu()
        {
            var msg = FitCeiling();
            Debug.Log(msg);
            EditorUtility.DisplayDialog("BLIND", msg, "OK");
        }

        public static string FitCeiling()
        {
            var rooms = GameObject.Find("=== ROOMS ===");
            var room = rooms != null ? rooms.transform.Find("room17") : null;
            if (room == null) return "room17 が無い";
            var walls = room.Find("GeneratedRoom/Walls");
            var ceiling = room.Find("GeneratedRoom/Ceiling");
            if (walls == null || ceiling == null) return "room17: GeneratedRoom の Walls か Ceiling が無い";

            var segs = Children(walls).Where(w => w.name != "DoorFrame" && w.localScale.y >= 3f).ToList();
            var east = segs.Where(w => w.localScale.z > 10f).OrderByDescending(w => w.localPosition.x).FirstOrDefault();
            var west = segs.Where(w => w.localScale.x <= 0.3f).OrderBy(w => w.localPosition.x).FirstOrDefault();
            var north = segs.Where(w => w.localScale.x > 10f).OrderByDescending(w => w.localPosition.z).FirstOrDefault();
            var south = segs.Where(w => w.localScale.x > 10f).OrderBy(w => w.localPosition.z).FirstOrDefault();
            if (east == null || west == null || north == null || south == null) return "room17: 四方の壁が見つからない。何もしていない";

            // 壁の外面から外面まで
            float x0 = west.localPosition.x - west.localScale.x * 0.5f;
            float x1 = east.localPosition.x + east.localScale.x * 0.5f;
            float z0 = south.localPosition.z - south.localScale.z * 0.5f;
            float z1 = north.localPosition.z + north.localScale.z * 0.5f;

            Undo.SetCurrentGroupName("room17 天井");
            int group = Undo.GetCurrentGroup();
            int n = 0;
            foreach (var c in Children(ceiling))
            {
                Undo.RecordObject(c, "room17 ceiling");
                var p = c.localPosition; var s = c.localScale;
                p.x = (x0 + x1) * 0.5f; p.z = (z0 + z1) * 0.5f;
                s.x = x1 - x0; s.z = z1 - z0;
                c.localPosition = p; c.localScale = s;
                n++;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(room.gameObject.scene);

            return "room17 の天井 " + n + "枚を " + (x1 - x0).ToString("F2") + " × " + (z1 - z0).ToString("F2")
                 + "m（壁の外側いっぱい）に合わせた（Ctrl+Z で戻せる）\n"
                 + "このあと room17 のサーモ・エコロケを作り直すこと（Vision_Echo の E_Floor_* は古い大きさのまま）";
        }

        /// <summary>
        /// room17 だけサーモ・エコロケを作り直す。
        /// vision/2 は全部屋を作り直すので、差分が大きくなり他の部屋にも触ってしまう。
        /// </summary>
        [MenuItem("BLIND/部屋修正/room17のサーモ・エコロケだけ作り直す")]
        public static void RebuildVisionMenu()
        {
            var msg = BlindVisionBuilder.Build(new[] { "room17" });
            Debug.Log(msg);
            EditorUtility.DisplayDialog("BLIND", msg, "OK");
        }

        static List<Transform> Children(Transform t)
        {
            var list = new List<Transform>();
            foreach (Transform c in t) list.Add(c);
            return list;
        }

        static bool IsPlain(Transform t, Transform room)
        {
            for (var p = t; p != null && p != room.parent; p = p.parent)
            {
                if (Quaternion.Angle(p.localRotation, Quaternion.identity) > 0.1f) return false;
                if ((p.localScale - Vector3.one).sqrMagnitude > 1e-6f) return false;
            }
            return true;
        }

        static Vector3 Local(Transform room, Vector3 world) { return room.InverseTransformPoint(world); }

        static Vector3 Center(Transform t)
        {
            var r = t.GetComponent<Renderer>();
            return r != null ? r.bounds.center : t.position;
        }

        static float Top(Transform t)
        {
            var r = t.GetComponent<Renderer>();
            return r != null ? r.bounds.max.y : t.position.y;
        }

        static void SetFloat(SerializedObject so, string field, float value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.floatValue = value;
        }
    }
}
