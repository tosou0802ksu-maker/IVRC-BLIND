using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room6（プール部屋）の入口に足場を作る。
    ///
    /// ---------------------------------------------------------------
    /// 何が起きていたか
    /// ---------------------------------------------------------------
    /// room5 から room6 へ入る扉(x -14.30..-14.10 / z -17.50..-16.30)の
    /// **真下がプールだった**。実測すると、扉をまたいだ瞬間に床が y=-0.99 まで落ちる。
    ///
    ///   x=-14.00 : FloorTile(0.00)      ← room5 側。ここまでは床がある
    ///   x=-14.25 : PoolBasin(-0.99)     ← 扉の中。もう水の上
    ///   x=-14.50 : SmallDuck_13(0.58)   ← アヒルの背中に乗っている
    ///
    /// つまり**一歩目からアヒルの上**で、踏み外すとプールへ落ちる。
    ///
    /// ---------------------------------------------------------------
    /// 直し方
    /// ---------------------------------------------------------------
    /// 扉の位置は動かさない（作者の指定）。代わりにプールサイドを扉の前まで延ばす。
    ///
    /// ⚠️ **迂回路を新しく作っているわけではない。** デッキの縁は扉のわずか
    ///    北 1.0m(z=-15.35)にあり、そこから南の出口(x -20.30..-19.10 / z=-20.4)
    ///    までは**もともと陸続き**で歩いて行ける（BFS で確認。到達2000マス、
    ///    扉に一番近い到達点は (-14.75, -15.15) ＝ わずか 1.77m）。
    ///    塞がっていたのは扉とデッキの縁のあいだの 1m だけ。
    ///    アヒル渡りは元から「近道」であって唯一の道ではない。
    ///
    /// ⚠️ 足場に埋まるアヒルは**消さずにプールへ寄せる**。部屋の持ち味なので減らさない。
    ///
    /// ⚠️ アヒルを動かしたら **必ず vision を作り直すこと**。
    ///    room6 のサーモ・エコロケはアヒルを1枚のメッシュに**結合**して持っている
    ///    (`T_Prop` / `E_P*`)。本体だけ動かすと、過去人には動いて見えるのに
    ///    サーモとエコロケには元の位置に残ったままになる。
    ///    このツールは最後に `BlindVisionBuilder.Build(new[]{"room6"})` を呼ぶ。
    /// </summary>
    public static class Room6PoolLedge
    {
        const string LedgeName = "PoolLedge_Generated";
        const int LayerDefault = 0;
        const int LayerThermal = 22;
        const int LayerEcho    = 23;

        /// <summary>足場の幅(m)。人がすれ違わなくてよいので 1 人ぶん + 余裕。</summary>
        const float Width = 1.45f;

        /// <summary>扉の南端より手前へ出す量(m)。扉の角で足を踏み外さないための余白。</summary>
        const float SouthMargin = 0.12f;

        /// <summary>足場の上面。デッキ(y=0)より 4mm 下げて、同一平面のちらつきを避ける。</summary>
        const float TopY = -0.004f;

        [MenuItem("BLIND/部屋修正/6. room6の入口に足場を作る")]
        public static void Menu()
        {
            Debug.Log(Build());
        }

        public static string Build()
        {
            var rooms = GameObject.Find("=== ROOMS ===");
            if (rooms == null) return "room6: === ROOMS === が無い";

            Transform room = null;
            foreach (Transform r in rooms.transform)
                if (r.name == "room6") { room = r; break; }
            if (room == null) return "room6: 部屋が見つからない";

            var old = room.Find(LedgeName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // --- 東の扉を実測する（座標は書かない）---
            Bounds door = new Bounds();
            bool hasDoor = false;
            foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.name != "FramePiece") continue;
                if (mr.bounds.center.x < -18f) continue;      // 南の扉は除く
                if (!hasDoor) { door = mr.bounds; hasDoor = true; } else door.Encapsulate(mr.bounds);
            }
            if (!hasDoor) return "room6: 東の扉枠(FramePiece)が見つからない";

            float doorZ = door.center.z;
            float innerX = door.min.x;                        // 壁の部屋側の面

            // --- デッキの縁を探す（扉の中央から北へ舐めて、y≒0 の床が出る所）---
            float edgeZ = doorZ;
            bool foundEdge = false;
            for (float z = door.max.z; z <= door.max.z + 4.0f; z += 0.02f)
            {
                if (IsDeck(innerX - 0.30f, z) && IsDeck(innerX - Width + 0.20f, z))
                {
                    edgeZ = z; foundEdge = true; break;
                }
            }
            if (!foundEdge) return "room6: 扉の北 4m 以内にデッキの縁が見つからない";

            float z0 = door.min.z - SouthMargin;              // 南端
            float z1 = edgeZ;                                 // 北端＝既存デッキ
            float x1 = door.max.x;                            // 壁の中まで伸ばして扉の下を埋める
            float x0 = innerX - Width;

            // 底はプールの底まで落とす。板が水に浮いていると「浮いている板」に見える。
            float bottomY = -1.30f;
            var basin = room.Find("PoolBasin");
            if (basin != null)
            {
                var bc = basin.GetComponent<Renderer>();
                if (bc != null) bottomY = bc.bounds.min.y + 0.02f;
            }

            var center = new Vector3((x0 + x1) * 0.5f, (bottomY + TopY) * 0.5f, (z0 + z1) * 0.5f);
            var size   = new Vector3(x1 - x0, TopY - bottomY, z1 - z0);

            var rootGo = new GameObject(LedgeName);
            Undo.RegisterCreatedObjectUndo(rootGo, "pool ledge");
            rootGo.transform.SetParent(room, false);
            rootGo.transform.position = Vector3.zero;
            rootGo.transform.rotation = Quaternion.identity;
            rootGo.layer = LayerDefault;

            var deckMat = MatOf(room, "PoolDeck");

            // ⚠️ サーモは **デッキと同じ段** を使うこと。`FloorStone` にしてはいけない。
            //    room6 のデッキは名前に "Pool" が入るせいで `Water`(11℃ dim 0.24) に
            //    分類されている。一方 `FloorStone` は dim 0.022 ＝ 2.3m 先から消える。
            //    混ぜると、周りの床は見えているのに**足場だけ真っ黒な穴**になり、
            //    サーモ役には「そこが抜けている」としか読めない（＝プールへ歩いて落ちる）。
            //    部屋の分類が正しいかどうかは別の話で、ここで勝手に直さない。
            var tMat = BlindThermalTable.Mat("Water");
            var eMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/EchoMaterial.mat");

            MakeBox(rootGo.transform, "D_Ledge", LayerDefault, center, size, deckMat, false);
            MakeBox(rootGo.transform, "T_Ledge", LayerThermal, center, size, tMat, false);
            MakeBox(rootGo.transform, "E_Ledge", LayerEcho,    center, size, eMat, true);

            var col = rootGo.AddComponent<BoxCollider>();
            col.center = center;
            col.size = size;

            string moved = MoveDucksOut(room, new Bounds(center, size));
            Physics.SyncTransforms();
            string steps = BuildSteps(room, rootGo.transform, deckMat, tMat, eMat);

            Physics.SyncTransforms();

            // ⚠️ アヒルを動かしたので、結合済みのサーモ・エコロケを作り直す。
            string vision = BlindVisionBuilder.Build(new[] { "room6" });

            return "room6 の足場: x " + x0.ToString("F2") + "〜" + x1.ToString("F2")
                 + " / z " + z0.ToString("F2") + "〜" + z1.ToString("F2")
                 + "（幅 " + (x1 - x0).ToString("F2") + "m × 奥行 " + (z1 - z0).ToString("F2") + "m"
                 + " / 深さ " + (TopY - bottomY).ToString("F2") + "m）\n"
                 + "  " + moved + "\n"
                 + "  " + steps + "\n"
                 + "  " + vision.Split('\n')[0];
        }

        /// <summary>
        /// プールから上がるための階段を作る。
        ///
        /// ⚠️ プールは深さ 1〜2m で壁が垂直。**落ちると自力では二度と上がれない。**
        ///    運ぶアヒルは全部陸に置いてあるので詰みはしないが、
        ///    誤って飛び込んだ人がそこで待つしかなくなる（3人協力なので全員が止まる）。
        ///    プールの縁に何箇所か段を付けて、いつでも戻れるようにする。
        ///
        /// ⚠️ 段は**プール側に張り出させる**こと。デッキを削って作ると
        ///    歩ける床が減って、通路が切れる恐れがある。
        /// </summary>
        static string BuildSteps(Transform room, Transform parent, Material d, Material t, Material e)
        {
            const int N = 4;                 // 段数
            const float Run = 0.34f;         // 1段の奥行き
            const float Wide = 1.30f;        // 階段の幅
            const float Grid = 0.25f;

            // ⚠️ 場所を座標で決め打ちしない。最初そうしたら3箇所のうち1箇所が
            //    デッキの真上で「水底が見つからない」になった。
            //    デッキと水の境目を実際に走査して選ぶ。
            var edges = new List<(Vector3 at, Vector3 dir, float floorY)>();
            for (float x = -29.8f; x <= -14.4f; x += Grid)
                for (float z = -20.2f; z <= -6.6f; z += Grid)
                {
                    if (!IsDeck(x, z)) continue;
                    foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
                    {
                        // 隣が水で、その先も水なら「縁」。角に作ると階段が壁にめり込む。
                        var n1 = new Vector3(x, 0f, z) + dir * Grid;
                        var n2 = new Vector3(x, 0f, z) + dir * (Run * N + 0.4f);
                        if (IsDeck(n1.x, n1.z) || IsDeck(n2.x, n2.z)) continue;
                        float fy = PoolFloor(n2.x, n2.z);
                        if (float.IsNaN(fy)) continue;
                        // 階段の幅ぶん、縁が真っ直ぐ続いていること
                        var side = new Vector3(dir.z, 0f, dir.x);
                        bool straight = true;
                        for (float w = -Wide * 0.5f; w <= Wide * 0.5f; w += Grid)
                        {
                            var q = new Vector3(x, 0f, z) + side * w;
                            if (!IsDeck(q.x, q.z)) { straight = false; break; }
                        }
                        if (!straight) continue;
                        edges.Add((new Vector3(x, 0f, z), dir, fy));
                        break;
                    }
                }

            // 互いに 6m 以上離れた所を選ぶ。プールのどこに落ちても近くに1つある状態にする。
            var picked = new List<(Vector3 at, Vector3 dir, float floorY)>();
            foreach (var cand in edges)
            {
                bool far = true;
                foreach (var q in picked)
                    if (Vector3.Distance(q.at, cand.at) < 6.0f) { far = false; break; }
                if (far) picked.Add(cand);
            }

            int made = 0;
            foreach (var sp in picked)
            {
                var go = new GameObject("Steps_" + made);
                Undo.RegisterCreatedObjectUndo(go, "pool steps");
                go.transform.SetParent(parent, false);
                go.transform.position = Vector3.zero;
                go.layer = LayerDefault;

                float rise = (0f - sp.floorY) / N;
                bool alongX = Mathf.Abs(sp.dir.x) > 0.5f;
                for (int k = 0; k < N; k++)
                {
                    // k=0 が一番上（縁のすぐ内側）。下へ行くほどプールの奥へ出る。
                    float top = -rise * k - 0.004f;
                    var c = sp.at + sp.dir * (Run * (k + 0.5f));
                    c.y = (top + sp.floorY) * 0.5f;
                    var size = alongX
                        ? new Vector3(Run, top - sp.floorY, Wide)
                        : new Vector3(Wide, top - sp.floorY, Run);
                    MakeBox(go.transform, "D_Step" + k, LayerDefault, c, size, d, false);
                    MakeBox(go.transform, "T_Step" + k, LayerThermal, c, size, t, false);
                    MakeBox(go.transform, "E_Step" + k, LayerEcho, c, size, e, true);
                    var col = go.AddComponent<BoxCollider>();
                    col.center = c; col.size = size;
                }
                made++;
            }
            return "プールの階段: " + made + "箇所（1段 " + (1f / N).ToString("F2")
                 + "×深さ・幅 " + Wide.ToString("F2") + "m / 候補 " + edges.Count + "）落ちても上がれる";
        }

        /// <summary>そこがプールなら水底の高さ。プールでなければ NaN。</summary>
        static float PoolFloor(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 5f, z), Vector3.down, 12f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var n = h.collider.name;
                if (n.Contains("Duck")) continue;
                if (n == "PoolBasin") return 5f - h.distance;
                return float.NaN;
            }
            return float.NaN;
        }

        /// <summary>その真上から線を落として、y≒0 のデッキが出るか。</summary>
        static bool IsDeck(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 4f, z), Vector3.down, 9f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                float y = 4f - h.distance;
                var n = h.collider.name;
                if (n == "PoolDeck" || n == "FloorTile") return Mathf.Abs(y) < 0.15f;
                if (n == "PoolBasin" || n.Contains("Duck")) return false;
            }
            return false;
        }

        /// <summary>
        /// 通路にかかるアヒルをプールの空いている所へ寄せる。**1体も消さない。**
        /// 高さ(y)は変えない。浮かんでいる高さは作者が付けた表情なので触らない。
        /// </summary>
        static string MoveDucksOut(Transform room, Bounds ledge)
        {
            var group = room.Find("Props_SmallDucks");
            if (group == null) return "アヒル: Props_SmallDucks が無い";

            var basin = room.Find("PoolBasin");
            if (basin == null) return "アヒル: PoolBasin が無い";
            var water = basin.GetComponent<Renderer>().bounds;

            // 先に全部の位置を控える。動かしながら測ると自分の新しい位置と比べてしまう。
            var all = new List<Transform>();
            var box = new Dictionary<Transform, Bounds>();
            foreach (Transform d in group)
            {
                if (!TryBounds(d, out var b)) continue;
                all.Add(d); box[d] = b;
            }

            // 空けておく所＝新しい足場 ＋ **両方の扉の前**。
            //
            // ⚠️ 扉の前を入れ忘れると、どかしたアヒルが別の扉を塞ぐ。
            //    実際、足場から寄せた1体が南の出口(room7 行き)の真ん前に着地して、
            //    今度はそこが通れなくなった（BFS で到達不能）。
            //    「どかす」は「どこかへ置く」ことなので、置いてよい場所を先に決める。
            var zones = new List<Bounds> { Expand(ledge, 0.20f) };
            foreach (var b in DoorZones(room)) zones.Add(b);

            // ⚠️ 判定は **真上から見た重なりだけ**。高さは見ない。
            //    板に沈んでいる物だけ避けたところ、板の上に浮いていたアヒル1体が
            //    落ちてきて**扉の真正面に座り、通路を完全に塞いだ**（BFS で到達不能）。
            //    幅 1.65m の通路に置ける物は無いと思ってよい。板の上は空ける。
            int n = 0;
            var log = new System.Text.StringBuilder();
            foreach (var d in all)
            {
                var b = box[d];
                if (!HitsAny(b, zones)) continue;

                Vector3 best = Vector3.zero;
                float bestScore = float.MaxValue;
                for (float dx = -0.4f; dx >= -5.0f; dx -= 0.20f)
                    for (float dz = -2.6f; dz <= 2.6f; dz += 0.20f)
                    {
                        var off = new Vector3(dx, 0f, dz);
                        var nb = new Bounds(b.center + off, b.size);
                        if (HitsAny(nb, zones)) continue;
                        if (nb.min.x < water.min.x + 0.15f) continue;
                        if (nb.min.z < water.min.z + 0.15f) continue;
                        if (nb.max.z > water.max.z - 0.15f) continue;

                        // 他のアヒルとの重なりが少ない所を選ぶ。近いほど良い。
                        float overlap = 0f;
                        foreach (var o in all)
                        {
                            if (o == d) continue;
                            var ob = box[o];
                            if (!nb.Intersects(ob)) continue;
                            overlap += Mathf.Min(nb.max.x, ob.max.x) - Mathf.Max(nb.min.x, ob.min.x)
                                     + Mathf.Min(nb.max.z, ob.max.z) - Mathf.Max(nb.min.z, ob.min.z);
                        }
                        float score = overlap * 3f + off.magnitude;
                        if (score < bestScore) { bestScore = score; best = off; }
                    }

                if (bestScore == float.MaxValue) { log.Append(" " + d.name + "(寄せ先なし)"); continue; }

                Undo.RecordObject(d, "move duck");
                d.position += best;
                box[d] = new Bounds(b.center + best, b.size);
                EditorUtility.SetDirty(d);
                n++;
            }

            return "アヒル: 通路にかかる " + n + "体をプールへ寄せた（削除は無し）" + log;
        }

        static Bounds Expand(Bounds b, float m)
        {
            var r = b; r.Expand(new Vector3(m * 2f, 0f, m * 2f)); return r;
        }

        /// <summary>真上から見た重なりだけを見る。高さは無視する。</summary>
        static bool HitsAny(Bounds b, List<Bounds> zones)
        {
            foreach (var z in zones)
            {
                if (b.max.x <= z.min.x || b.min.x >= z.max.x) continue;
                if (b.max.z <= z.min.z || b.min.z >= z.max.z) continue;
                return true;
            }
            return false;
        }

        /// <summary>扉ごとに「部屋の中へ 1.8m」の通行帯を作る。物を置いてはいけない所。</summary>
        static List<Bounds> DoorZones(Transform room)
        {
            const float Reach = 1.8f;
            var groups = new List<Bounds>();
            foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.name != "FramePiece") continue;
                bool merged = false;
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = groups[i]; var e = g; e.Expand(1.0f);
                    if (e.Intersects(mr.bounds)) { g.Encapsulate(mr.bounds); groups[i] = g; merged = true; break; }
                }
                if (!merged) groups.Add(mr.bounds);
            }

            var center = room.GetComponentInChildren<MeshRenderer>() != null ? RoomCenter(room) : Vector3.zero;
            var result = new List<Bounds>();
            foreach (var g in groups)
            {
                // 薄い方の軸が扉の向き。そちら側へ部屋の中心に向かって伸ばす。
                var b = g;
                if (g.size.x < g.size.z)
                {
                    float dir = center.x > g.center.x ? 1f : -1f;
                    b.Encapsulate(new Vector3(g.center.x + dir * Reach, g.center.y, g.min.z));
                    b.Encapsulate(new Vector3(g.center.x + dir * Reach, g.center.y, g.max.z));
                }
                else
                {
                    float dir = center.z > g.center.z ? 1f : -1f;
                    b.Encapsulate(new Vector3(g.min.x, g.center.y, g.center.z + dir * Reach));
                    b.Encapsulate(new Vector3(g.max.x, g.center.y, g.center.z + dir * Reach));
                }
                result.Add(b);
            }
            return result;
        }

        static Vector3 RoomCenter(Transform room)
        {
            Bounds b = new Bounds(); bool has = false;
            foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.name != "PoolDeck" && mr.name != "FloorTile") continue;
                if (!has) { b = mr.bounds; has = true; } else b.Encapsulate(mr.bounds);
            }
            return has ? b.center : room.position;
        }

        static bool TryBounds(Transform t, out Bounds b)
        {
            b = new Bounds();
            bool has = false;
            foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.gameObject.layer != LayerDefault) continue;
                if (!has) { b = mr.bounds; has = true; } else b.Encapsulate(mr.bounds);
            }
            return has;
        }

        static Material MatOf(Transform room, string child)
        {
            var t = room.Find(child);
            if (t == null) return null;
            var r = t.GetComponent<Renderer>();
            return r != null ? r.sharedMaterial : null;
        }

        static void MakeBox(Transform parent, string name, int layer, Vector3 pos,
                            Vector3 size, Material mat, bool echo)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(go, "ledge box");
            go.name = name;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.layer = layer;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (!echo) return;

            var t = System.Type.GetType("EchoReceiver, Assembly-CSharp");
            if (t == null) return;
            var undoType = System.Type.GetType("UdonSharpEditor.UdonSharpUndo, UdonSharp.Editor");
            Component rec = null;
            if (undoType != null)
            {
                var mi = undoType.GetMethod("AddComponent", new[] { typeof(GameObject), typeof(System.Type) });
                if (mi != null) rec = mi.Invoke(null, new object[] { go, t }) as Component;
            }
            if (rec == null) return;
            var so = new SerializedObject(rec);
            var arr = so.FindProperty("targetRenderers");
            if (arr != null) { arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = mr; }
            so.ApplyModifiedProperties();
            var usb = rec as UdonSharp.UdonSharpBehaviour;
            if (usb != null && UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb) != null)
                UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
        }
    }
}
