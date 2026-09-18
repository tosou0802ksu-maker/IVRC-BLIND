using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room6（プール部屋）のアヒルギミック。
    ///
    /// ---------------------------------------------------------------
    /// 遊び方
    /// ---------------------------------------------------------------
    ///   ・部屋には小さいアヒルが61体ちらばっている。
    ///   ・そのうち **3体だけが熱を持っている**。見分けられるのはサーモ役だけ。
    ///   ・過去人だけに読める看板が南の壁に出ていて、そこに「3体運べ」と書いてある。
    ///   ・過去人がその3体を拾って台の受け皿に置くと、room7 へ抜ける扉が開く。
    ///   ・**一度開いたら二度と閉まらない**（ItemAltar.placedMask を戻さない）。
    ///
    /// 3役の役割がここで噛み合う:
    ///   過去人   … 指示を読める / 物を運べる
    ///   サーモ   … どのアヒルが熱いか分かる（過去人には全部同じに見える）
    ///   エコロケ … 部屋の形と台の位置が分かる
    ///
    /// ---------------------------------------------------------------
    /// ⚠️ 運ぶアヒルは必ず陸から手が届く所に置く
    /// ---------------------------------------------------------------
    /// プールは深さ1〜2mで、落ちると自力では上がれない（壁が垂直）。
    /// 水の中に置くと「取りに行ったら詰む」ギミックになる。
    /// 歩ける床を BFS で出して、そこから ReachFromLand(m) 以内にだけ置く。
    /// </summary>
    public static class Room6DuckGimmick
    {
        const string RootName = "DuckGimmick_Generated";
        const int LayerDefault = 0;
        const int LayerThermal = 22;
        const int LayerEcho    = 23;

        /// <summary>運ぶアヒルの数。増やすなら ItemAltar の受け皿も自動で増える。</summary>
        const int CarryCount = 3;

        /// <summary>歩ける床からこの距離までに運ぶアヒルを置く(m)。腕を伸ばせば届く範囲。</summary>
        const float ReachFromLand = 1.10f;

        /// <summary>プールサイドに散らす数。全部プールの中だと味気ない（作者指摘）。</summary>
        const int LandCount = 12;

        /// <summary>山の高さの上限(m)。これを超えると「山」ではなく塔に見えるし、上の個体に手が届かない。</summary>
        const float MaxPile = 2.20f;

        /// <summary>どこにも入らなかった個体だけに許す上限(m)。⚠️ ここを無制限にすると塔になる。</summary>
        const float RelaxPile = 3.20f;

        /// <summary>台の上面(m)。デッキ(0)より一段高い「高くなっている所」。</summary>
        const float DeckTop = 0.25f;

        /// <summary>
        /// 受け皿の内寸と囲いの高さ(m)。囲いが低いとアヒルが転がって落ちる。
        ///
        /// ⚠️ 内寸は**実際に運ぶアヒルより大きくすること**。
        ///    0.70m で作ったが、選ばれた3体は 0.73／0.80／0.76m あって
        ///    どれも囲いに乗り上げてしまった（＝転がり止めとして働かない）。
        ///    運ぶ個体の上限(0.95m)＋指が入る余裕で決める。
        /// </summary>
        const float SlotInner = 0.90f, SlotWall = 0.06f, SlotLip = 0.15f;

        const string GenMatDir = "Assets/_BLIND/Art/Materials/Gimmick";

        [MenuItem("BLIND/部屋修正/7. room6のアヒルギミックを作る")]
        public static void Menu()
        {
            Debug.Log(Build());
        }

        public static string Build()
        {
            var rooms = GameObject.Find("=== ROOMS ===");
            if (rooms == null) return "アヒル: === ROOMS === が無い";
            Transform room = null;
            foreach (Transform r in rooms.transform) if (r.name == "room6") room = r;
            if (room == null) return "アヒル: room6 が無い";

            var log = new System.Text.StringBuilder();

            // ⚠️ 作り直す前に、**全部のアヒルを素の状態へ戻す**。
            //    掴む仕掛けは 61体すべてに付けているので、Carry フォルダだけ見ていると
            //    前回ぶんが残って二重に付く。
            var pool0 = room.Find("Props_SmallDucks");
            if (pool0 != null)
            {
                var back = new List<Transform>();
                foreach (Transform d in pool0) back.Add(d);
                var oldRoot = room.Find(RootName);
                if (oldRoot != null)
                {
                    var carry = oldRoot.Find("Carry");
                    if (carry != null) foreach (Transform d in carry) back.Add(d);
                }
                foreach (var d in back)
                {
                    var vis = d.Find("DuckVision_Generated");
                    if (vis != null) Undo.DestroyObjectImmediate(vis.gameObject);
                    StripGenerated(d.gameObject);
                    if (d.parent != pool0) Undo.SetTransformParent(d, pool0, "restore duck");
                }
            }
            var old = room.Find(RootName);
            if (old != null)
            {
                Undo.DestroyObjectImmediate(old.gameObject);
                Physics.SyncTransforms();
            }

            var rootGo = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(rootGo, "duck gimmick");
            rootGo.transform.SetParent(room, false);
            rootGo.transform.position = Vector3.zero;
            rootGo.transform.rotation = Quaternion.identity;
            rootGo.layer = LayerDefault;

            log.AppendLine(MoveWetFloorSign(room));

            // --- 台 ---
            var altar = BuildAltarDeck(room, rootGo.transform, out var slots,
                                       out var slotD, out var slotT, out var altarCenter,
                                       out var altarEcho);
            log.AppendLine(altar);

            // --- 看板（過去人だけ）---
            log.AppendLine(BuildSign(room, rootGo.transform));

            // ⚠️ ここで必ず物理を同期すること。
            //    Unity は既定で autoSyncTransforms が切れているので、
            //    **今作ったばかりの台は Raycast に映らない**。
            //    同期を忘れると、床を調べる処理が台のある/なしをその時の運で拾い、
            //    実行するたびにアヒルの配り方と「運ぶ3体」が変わった（実測：1回目と2回目で別の3体）。
            Physics.SyncTransforms();

            // --- 床の地図を焼く（以降 Raycast は撃たない）---
            var map = BakeFloor(room, BlockedZones(room));

            // --- 台の上に乗っている大きいアヒルをどける ---
            log.AppendLine(ClearAltar(room, map));

            // --- 61体を配り直す。数は変えない ---
            log.AppendLine(Scatter(room, map, out var carriers, out _));

            // --- 重力で落ち着かせる ---
            log.AppendLine(SettlePhysics(room));

            // --- 散らした結果、宙に浮いた大きいアヒルを座らせる ---
            log.AppendLine(SettleBigDucks(room, map));

            // --- 3体を「運べる熱いアヒル」にする ---
            log.AppendLine(MakeCarriers(room, rootGo.transform, carriers, out var carryItems));

            // --- 施錠扉 ---
            log.AppendLine(BuildGate(room, rootGo.transform, carryItems, slots, slotD, slotT,
                                     altarCenter, altarEcho));

            Physics.SyncTransforms();
            log.AppendLine("  " + BlindVisionBuilder.Build(new[] { "room6" }).Split('\n')[0]);
            return log.ToString();
        }

        // ============================================================
        // 邪魔な看板をどける
        // ============================================================
        static string MoveWetFloorSign(Transform room)
        {
            var sign = room.Find("Props_WetFloorSign_Sample");
            if (sign == null) return "看板: Props_WetFloorSign_Sample が無い（もう動かしてある？）";

            // 台の上ではなく、プールの反対側（東寄りの床）へ寄せる。
            // ⚠️ y をそのまま持ってこない。元はプールの上に 1.88m で浮かせてあり、
            //    デッキへ移すと**宙に浮いた看板**になる（実際そうなった）。
            //    床に着けること。
            var to = new Vector3(-22.6f, sign.position.y, -8.6f);
            Bounds sb2 = new Bounds(); bool sh = false;
            foreach (var mr in sign.GetComponentsInChildren<MeshRenderer>(true))
            { if (!sh) { sb2 = mr.bounds; sh = true; } else sb2.Encapsulate(mr.bounds); }
            if (sh)
            {
                var probe = Physics.RaycastAll(new Vector3(to.x, 5f, to.z), Vector3.down, 12f);
                System.Array.Sort(probe, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in probe)
                {
                    if (h.collider.name.Contains("Duck")) continue;
                    to.y = sign.position.y + ((5f - h.distance) - sb2.min.y);
                    break;
                }
            }
            if (Vector3.Distance(sign.position, to) < 0.05f) return "看板: すでに寄せてある";
            Undo.RecordObject(sign, "move sign");
            var from = sign.position;
            sign.position = to;
            EditorUtility.SetDirty(sign);
            return "看板: 「足元注意」を " + from.ToString("F1") + " → " + to.ToString("F1") + " へ寄せた（ギミックの場所を空ける）";
        }

        // ============================================================
        // 台（一段高い置き場）
        // ============================================================
        static string BuildAltarDeck(Transform room, Transform parent, out Transform[] slots,
                                     out MeshRenderer[] slotD, out MeshRenderer[] slotT,
                                     out Transform center, out List<Object> echoes)
        {
            slots = new Transform[CarryCount];
            slotD = new MeshRenderer[CarryCount];
            slotT = new MeshRenderer[CarryCount];
            echoes = new List<Object>();

            // ⚠️ 置き場所は**時計の前**（作者指定：部屋で一番開けている所）。
            //    最初はプールの南東の角に作ったが、そこは巨大アヒルの尻尾と
            //    看板が取り合う狭い隅で、台も看板も削らないと入らなかった。
            //    時計の下の西側は幅 8m の平らなデッキで、何も削らずに置ける。
            Physics.SyncTransforms();
            var clock = room.Find("basic_clock_ver1.0");
            if (clock == null) { center = null; return "台: 時計(basic_clock_ver1.0)が見つからない"; }

            // 時計が掛かっている壁の、部屋側の面を実測する
            float wallFace = 0f; int axis = 0; float inward = 1f;
            float bestD = float.MaxValue;
            foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.name.StartsWith("Wall")) continue;
                var wb = mr.bounds;
                if (wb.size.y < 3f) continue;
                // 薄い方の軸が壁の厚み
                int ax = wb.size.x < wb.size.z ? 0 : 2;
                float face = ax == 0 ? wb.max.x : wb.max.z;
                float far  = ax == 0 ? wb.min.x : wb.min.z;
                float cp = ax == 0 ? clock.position.x : clock.position.z;
                float d1 = Mathf.Abs(face - cp), d2 = Mathf.Abs(far - cp);
                float d = Mathf.Min(d1, d2);
                if (d > bestD || d > 1.0f) continue;
                bestD = d; axis = ax;
                // 部屋の中心へ向く方を「内側」にする
                var fc = FloorCenter(room);
                float mid = ax == 0 ? wb.center.x : wb.center.z;
                float toCenter = (ax == 0 ? fc.x : fc.z) - mid;
                inward = Mathf.Sign(toCenter);
                wallFace = inward > 0 ? Mathf.Max(face, far) : Mathf.Min(face, far);
            }
            if (bestD == float.MaxValue) { center = null; return "台: 時計の掛かっている壁が特定できない"; }

            const float Depth = 2.40f;   // 壁から部屋の中へ
            const float Width = 2.90f;   // 壁に沿って
            float alongC = axis == 0 ? clock.position.z : clock.position.x;

            float x0, x1, z0, z1;
            if (axis == 0)
            {
                x0 = Mathf.Min(wallFace, wallFace + inward * Depth);
                x1 = Mathf.Max(wallFace, wallFace + inward * Depth);
                z0 = alongC - Width * 0.5f; z1 = alongC + Width * 0.5f;
            }
            else
            {
                z0 = Mathf.Min(wallFace, wallFace + inward * Depth);
                z1 = Mathf.Max(wallFace, wallFace + inward * Depth);
                x0 = alongC - Width * 0.5f; x1 = alongC + Width * 0.5f;
            }

            var go = new GameObject("Altar");
            Undo.RegisterCreatedObjectUndo(go, "altar");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.layer = LayerDefault;

            // ⚠️ 台を床と同じタイルで作ると、**一段高いことが全く読めない**。
            //    三面投影なので継ぎ目まで揃ってしまい、床の模様がそのまま乗る。
            //    壁と同じコンクリートにすると、青いタイルの床から生えた
            //    灰色の台としてはっきり分かれる。
            var deckMat = MatOf(room, "PoolBasin");
            var wallMat = MatOf(room, "Walls/WallSegment");
            if (wallMat != null) deckMat = wallMat;
            var eMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/EchoMaterial.mat");
            var eProp = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Echo/EchoMaterial_Prop.mat");
            if (eProp == null) eProp = eMat;
            var tDeck = BlindThermalTable.Mat("Water");     // 周りの床と同じ段。9.14 を参照
            var tSlot = BlindThermalTable.Mat("DuckSlot");

            // 平らなデッキの上に置くので、底は床のすぐ下でよい。
            float bottom = -0.10f;

            var body = new Vector3((x0 + x1) * 0.5f, (bottom + DeckTop) * 0.5f, (z0 + z1) * 0.5f);
            var bodySize = new Vector3(x1 - x0, DeckTop - bottom, z1 - z0);
            MakeBox(go.transform, "D_Deck", LayerDefault, body, bodySize, deckMat, false, null);
            MakeBox(go.transform, "T_Deck", LayerThermal, body, bodySize, tDeck, false, null);
            MakeBox(go.transform, "E_Deck", LayerEcho, body, bodySize, eMat, true, echoes);

            var col = go.AddComponent<BoxCollider>();
            col.center = body; col.size = bodySize;

            // ⚠️ 台の縁に立ち上がりは付けない。今は平らなデッキの上なので
            //    落ちる先が無い。転がり止めは受け皿の囲いが受け持つ。

            // --- 受け皿 ---
            var markMat = MakeMat("Duck_Slot", new Color(0.86f, 0.80f, 0.18f), new Color(0.45f, 0.40f, 0.05f));
            // ⚠️ 3個を1列に並べると入らない。
            //    受け皿の外寸は 1.02m、台は 2.30(x) × 2.88(z) しかないので、
            //    1列に3個 = 3.06m ではみ出す。手前に2個・奥に1個の三角に置く。
            float outer = SlotInner + SlotWall * 2f;
            float cx = (x0 + x1) * 0.5f;
            float cz = (z0 + z1) * 0.5f;
            var offs = new Vector2[]
            {
                new Vector2(-(outer * 0.52f),  outer * 0.62f),
                new Vector2( (outer * 0.52f),  outer * 0.62f),
                new Vector2( 0f,              -outer * 0.62f),
            };
            var centerGo = new GameObject("Center");
            Undo.RegisterCreatedObjectUndo(centerGo, "altar center");
            centerGo.transform.SetParent(go.transform, false);
            centerGo.transform.position = new Vector3(cx, DeckTop + 0.25f, cz);
            center = centerGo.transform;

            for (int i = 0; i < CarryCount; i++)
            {
                var off = offs[i % offs.Length];
                float sx = cx + off.x;
                float sz = cz + off.y;
                var sgo = new GameObject("Slot_" + i);
                Undo.RegisterCreatedObjectUndo(sgo, "slot");
                sgo.transform.SetParent(go.transform, false);
                sgo.transform.position = new Vector3(sx, DeckTop + 0.02f, sz);
                slots[i] = sgo.transform;

                // 囲い4枚。低いとアヒルが転がって落ちる。
                float o = SlotInner * 0.5f + SlotWall * 0.5f;
                float wy = DeckTop + SlotLip * 0.5f;
                float len = SlotInner + SlotWall * 2f;
                MakeSolid(go.transform, "SlotWall_" + i + "_a", new Vector3(sx - o, wy, sz),
                          new Vector3(SlotWall, SlotLip, len), markMat, tSlot, eProp, echoes);
                MakeSolid(go.transform, "SlotWall_" + i + "_b", new Vector3(sx + o, wy, sz),
                          new Vector3(SlotWall, SlotLip, len), markMat, tSlot, eProp, echoes);
                MakeSolid(go.transform, "SlotWall_" + i + "_c", new Vector3(sx, wy, sz - o),
                          new Vector3(len, SlotLip, SlotWall), markMat, tSlot, eProp, echoes);
                MakeSolid(go.transform, "SlotWall_" + i + "_d", new Vector3(sx, wy, sz + o),
                          new Vector3(len, SlotLip, SlotWall), markMat, tSlot, eProp, echoes);

                // 中の板。埋まったら色が変わる「何個入ったか」の表示。
                var padPos = new Vector3(sx, DeckTop + 0.012f, sz);
                var padSize = new Vector3(SlotInner, 0.024f, SlotInner);
                slotD[i] = MakeBox(go.transform, "D_Pad_" + i, LayerDefault, padPos, padSize, markMat, false, null);
                slotT[i] = MakeBox(go.transform, "T_Pad_" + i, LayerThermal, padPos, padSize, tSlot, false, null);
            }

            return "台: x " + x0.ToString("F2") + "〜" + x1.ToString("F2")
                 + " / z " + z0.ToString("F2") + "〜" + z1.ToString("F2")
                 + " 上面 " + DeckTop.ToString("F2") + "m（足場から一段上がる）"
                 + " / 受け皿 " + CarryCount + "個（内寸 " + SlotInner.ToString("F2")
                 + "m・囲い " + SlotLip.ToString("F2") + "m）";
        }

        // ============================================================
        // 看板（過去人だけに見える）
        // ============================================================
        static string BuildSign(Transform room, Transform parent)
        {
            var tex = BlindSignBaker.Bake(
                new[] { "熱を持ったアヒルを", CarryCount + "体 運べ" },
                "Assets/_BLIND/Art/Textures/Sign/Sign_DuckOrder.png",
                1024, 512,
                new Color(0.86f, 0.80f, 0.18f), new Color(0.06f, 0.05f, 0.04f), 0.13f);
            if (tex == null) return "看板: 文字を焼けなかった";

            var mat = MakeMat("Sign_DuckOrder", Color.white, new Color(0.30f, 0.28f, 0.10f));
            mat.mainTexture = tex;
            // ⚠️ Cube を 180° 回して部屋へ向けると、その面の UV は**上下も左右も逆**になる。
            //    そのまま貼ると文字が鏡のうえ行が入れ替わって、まったく読めない
            //    （最初は左右だけ直して出し、まだ裏返ったままだった）。
            //    絵の側で両方向とも反転して打ち消す。
            //    offset を 1 にしないと Clamp で端の1列が引き伸ばされる。
            mat.mainTextureScale = new Vector2(-1f, -1f);
            mat.mainTextureOffset = new Vector2(1f, 1f);

            // ⚠️ 看板は**自分で光らせる**こと。西の壁は暗く、素の材質だと
            //    黄色い板に黒い文字がほとんど沈んで読めなかった。
            //    発光色を一様にしても地も文字も同じだけ持ち上がって意味が無い。
            //    **同じ絵を _EmissionMap に入れる**と、地だけが光って文字は黒のまま残り、
            //    暗い部屋でもはっきり読める（懐中電灯を入れた後も効く）。
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetTexture("_EmissionMap", tex);
            mat.SetTextureScale("_EmissionMap", new Vector2(-1f, -1f));
            mat.SetTextureOffset("_EmissionMap", new Vector2(1f, 1f));
            mat.SetColor("_EmissionColor", new Color(0.95f, 0.90f, 0.35f));
            EditorUtility.SetDirty(mat);
            EditorUtility.SetDirty(mat);

            // ⚠️ 看板は**台と同じ壁**に出す。台と看板が離れていると、
            //    指示を読んだ場所と置く場所が結び付かない。
            var altarT = room.Find(RootName + "/Altar");
            if (altarT == null) return "看板: 先に台を作ること";
            var ab = altarT.GetComponent<BoxCollider>().bounds;
            var clock = room.Find("basic_clock_ver1.0");

            // 台の「壁に接している辺」を探す＝そこが看板を出す面
            bool alongZ = ab.size.z > ab.size.x;
            const float W = 2.60f, H = 1.30f;      // 絵は 2:1。壁が広いのでさっきより大きい
            float cy = 2.00f;

            var go = new GameObject("Sign");
            Undo.RegisterCreatedObjectUndo(go, "sign");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.layer = LayerDefault;

            Vector3 faceCenter, faceSize, frameSize, inDir;
            Quaternion faceRot;
            var fc = FloorCenter(room);
            if (alongZ)
            {
                // 壁は x 一定。台の、部屋の中心から遠い側の面が壁。
                float wallX = Mathf.Abs(ab.min.x - fc.x) > Mathf.Abs(ab.max.x - fc.x) ? ab.min.x : ab.max.x;
                float dir = Mathf.Sign(fc.x - wallX);
                faceCenter = new Vector3(wallX + dir * 0.075f, cy, ab.center.z);
                // ⚠️ 大きさは**回す前**の形で指定する。板は Cube なので、
                //    先に (0.01, H, W) と置いてから 90° 回すと厚みと幅が入れ替わり、
                //    **壁から生えた薄い板**になって絵が一切見えなくなる（実際そうなった）。
                //    常に (W, H, 0.01) で作って、絵のある面(-Z)を部屋へ向ける。
                faceSize = new Vector3(W, H, 0.01f);
                frameSize = new Vector3(0.07f, H + 0.14f, W + 0.14f);
                faceRot = Quaternion.Euler(0f, dir > 0 ? -90f : 90f, 0f);
                inDir = new Vector3(dir, 0f, 0f);
            }
            else
            {
                float wallZ = Mathf.Abs(ab.min.z - fc.z) > Mathf.Abs(ab.max.z - fc.z) ? ab.min.z : ab.max.z;
                float dir = Mathf.Sign(fc.z - wallZ);
                faceCenter = new Vector3(ab.center.x, cy, wallZ + dir * 0.075f);
                faceSize = new Vector3(W, H, 0.01f);   // 回す前の形（上のコメント参照）
                frameSize = new Vector3(W + 0.14f, H + 0.14f, 0.07f);
                faceRot = Quaternion.Euler(0f, dir > 0 ? 180f : 0f, 0f);
                inDir = new Vector3(0f, 0f, dir);
            }

            // ⚠️ 枠は**絵より壁側**に置くこと。部屋側に置くと枠が絵を覆って
            //    真っ黒な板にしか見えない（実際そうなった）。
            var frameCenter = faceCenter - inDir * 0.04f;

            MakeBox(go.transform, "Frame", LayerDefault, frameCenter, frameSize,
                    MakeMat("Sign_Frame", new Color(0.16f, 0.16f, 0.17f), Color.black), false, null);

            // ⚠️ **この板はレイヤー0だけ**。サーモ・エコロケ用の複製を作らない。
            //    「過去人にしか読めない指示」がこのギミックの肝で、
            //    3役が口で伝え合うことが遊びになる。複製を作ると台無し。
            var face = MakeBox(go.transform, "D_Face", LayerDefault, faceCenter, faceSize, mat, false, null);
            face.transform.localRotation = faceRot;

            return "看板: " + faceCenter.ToString("F2") + " に " + W.ToString("F1") + "×" + H.ToString("F1")
                 + "m（時計の下・台と同じ壁 / 過去人だけに見える／レイヤー0のみ）"
                 + (clock != null ? " 時計から " + Vector3.Distance(faceCenter, clock.position).ToString("F1") + "m" : "");
        }

        // ============================================================
        // 床の地図（一度だけ作って、以降はこれだけを見る）
        // ============================================================
        //
        // ⚠️ 配置の途中で Raycast を撃ってはいけない。
        //    Unity は既定で autoSyncTransforms が切れているので、直前に作った台や
        //    動かしたアヒルが物理側に反映されているかどうかがその時の運で決まる。
        //    実際、同じ処理を3回流して**毎回ちがうアヒルが「運ぶ3体」に選ばれた**。
        //    先に地図を焼いて、あとは地図だけで決めれば必ず同じ結果になる。
        class FloorMap
        {
            public float x0, z0, step;
            public int nx, nz;
            public float[,] y;        // その升の地面の高さ
            public bool[,] land;      // 歩ける床か（デッキ・足場・台）
            public bool[,] free;      // 物を置いてよいか（扉の前と台の上を除く）
            public bool[,] reach;     // 入口から歩いて行けるか

            public bool Cell(float wx, float wz, out int i, out int j)
            {
                i = Mathf.RoundToInt((wx - x0) / step);
                j = Mathf.RoundToInt((wz - z0) / step);
                return i >= 0 && j >= 0 && i < nx && j < nz;
            }
            public Vector2 World(int i, int j) { return new Vector2(x0 + i * step, z0 + j * step); }
        }

        static FloorMap BakeFloor(Transform room, List<Bounds> block)
        {
            var m = new FloorMap { step = 0.25f, x0 = -29.9f, z0 = -20.1f };
            float x1 = -14.4f, z1 = -6.8f;
            m.nx = (int)((x1 - m.x0) / m.step) + 1;
            m.nz = (int)((z1 - m.z0) / m.step) + 1;
            m.y = new float[m.nx, m.nz];
            m.land = new bool[m.nx, m.nz];
            m.free = new bool[m.nx, m.nz];

            for (int i = 0; i < m.nx; i++)
                for (int j = 0; j < m.nz; j++)
                {
                    var w = m.World(i, j);
                    var hits = Physics.RaycastAll(new Vector3(w.x, 5f, w.y), Vector3.down, 12f);
                    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    bool got = false;
                    foreach (var h in hits)
                    {
                        var n = h.collider.name;
                        if (n.Contains("Duck")) continue;              // アヒルの上には積まない
                        float hy = 5f - h.distance;
                        m.y[i, j] = hy;
                        m.land[i, j] = (n == "PoolDeck" || n == "FloorTile"
                                     || n == "PoolLedge_Generated" || n == "Altar") && hy > -0.15f;
                        got = true;
                        break;
                    }
                    if (!got) continue;

                    bool ok = true;
                    var cell = new Bounds(new Vector3(w.x, 0f, w.y), new Vector3(m.step, 10f, m.step));
                    foreach (var b in block) if (Flat(cell, b)) { ok = false; break; }
                    m.free[i, j] = ok;
                }

            // ⚠️ 「陸である」と「行ける」は別物。
            //    room6 の南の帯(x -23〜-19.5 / z≒-19.4)はデッキだが、
            //    西の端を回らないと入れない袋小路で、途中がアヒル1体で塞がるだけで
            //    丸ごと孤立する。実際そうなって、運ぶアヒルの1体が
            //    **陸から 2.42m ＝ 取りに行けない**場所に置かれた。
            //    入口から繋がっている升だけを候補にする。
            m.reach = new bool[m.nx, m.nz];
            if (m.Cell(-14.45f, -16.90f, out int si, out int sj) && m.land[si, sj])
            {
                var q = new Queue<Vector2Int>();
                m.reach[si, sj] = true; q.Enqueue(new Vector2Int(si, sj));
                int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    for (int k = 0; k < 4; k++)
                    {
                        int a = c.x + dx[k], b = c.y + dz[k];
                        if (a < 0 || b < 0 || a >= m.nx || b >= m.nz) continue;
                        if (m.reach[a, b] || !m.land[a, b]) continue;
                        if (Mathf.Abs(m.y[a, b] - m.y[c.x, c.y]) > 0.35f) continue;   // 段差
                        m.reach[a, b] = true; q.Enqueue(new Vector2Int(a, b));
                    }
                }
            }
            return m;
        }

        static List<Bounds> BlockedZones(Transform room)
        {
            var block = new List<Bounds>();
            var altar = room.Find(RootName + "/Altar");
            if (altar != null) block.Add(Grow(altar.GetComponent<BoxCollider>().bounds, 0.35f));
            var ledge = room.Find("PoolLedge_Generated");
            if (ledge != null) block.Add(Grow(ledge.GetComponent<BoxCollider>().bounds, 0.30f));
            foreach (var b in DoorZones(room)) block.Add(b);

            // ⚠️ プールの階段のまわりにはアヒルを置かない。
            //    階段の降り口が1体でも塞がると、落ちた人がそこから上がれない。
            var ledgeRoot = room.Find("PoolLedge_Generated");
            if (ledgeRoot != null)
                foreach (Transform st in ledgeRoot)
                {
                    if (!st.name.StartsWith("Steps_")) continue;
                    Bounds sb = new Bounds(); bool has = false;
                    foreach (var c in st.GetComponentsInChildren<BoxCollider>())
                    { if (!has) { sb = c.bounds; has = true; } else sb.Encapsulate(c.bounds); }
                    // ⚠️ 余白を取りすぎない。1.2m にしたら6箇所ぶんで
                    //    **水面の 7割が立入禁止になり、61体中39体が置けなくなった**
                    //    （実測：置ける水面 1247升 → 377升）。
                    //    降り口さえ空いていればよいので 0.5m で足りる。
                    if (has) block.Add(Grow(sb, 0.50f));
                }
            return block;
        }

        /// <summary>
        /// 台の上に乗ってしまった大きいアヒル(Props_Ducks)を水へどける。**消さない。**
        ///
        /// ⚠️ 小さいアヒルだけ気にしていたら、Props_Ducks の3体が受け皿の上に
        ///    座ったままだった。大きいので台が完全に隠れる。
        /// ⚠️ 高い所に浮いている個体(1.0m 以上)は触らない。巨大アヒルの背に乗せてある
        ///    配置で、部屋の見せ場。台の上とはいえ頭上なので邪魔にならない。
        /// </summary>
        static string ClearAltar(Transform room, FloorMap map)
        {
            var group = room.Find("Props_Ducks");
            var altarT = room.Find(RootName + "/Altar");
            if (group == null || altarT == null) return "大きいアヒル: 対象なし";
            var ab = altarT.GetComponent<BoxCollider>().bounds;

            int n = 0;
            var log = new System.Text.StringBuilder();
            foreach (Transform d in group)
            {
                if (!TryBounds(d, out var b)) continue;
                if (b.min.y > 1.0f) continue;                       // 頭上に浮いている物は触らない
                if (b.size.x > 4f || b.size.z > 4f) continue;       // 部屋の主役の巨大アヒルは動かさない
                if (b.max.x <= ab.min.x || b.min.x >= ab.max.x) continue;
                if (b.max.z <= ab.min.z || b.min.z >= ab.max.z) continue;

                // 台から一番近い、空いている水面へ。順に見るので毎回同じ結果になる。
                float best = float.MaxValue; Vector3 to = d.position; bool found = false;
                for (int i = 0; i < map.nx; i++)
                    for (int j = 0; j < map.nz; j++)
                    {
                        if (!map.free[i, j] || map.land[i, j]) continue;
                        var w = map.World(i, j);
                        var nb = new Bounds(new Vector3(w.x, 0f, w.y), b.size);
                        if (nb.max.x > ab.min.x && nb.min.x < ab.max.x
                         && nb.max.z > ab.min.z && nb.min.z < ab.max.z) continue;
                        if (OverlapsWalk(map, nb, 0.12f)) continue;
                        float dd = (new Vector2(w.x, w.y) - new Vector2(d.position.x, d.position.z)).sqrMagnitude;
                        if (dd < best) { best = dd; to = new Vector3(w.x, map.y[i, j] + (d.position.y - b.min.y), w.y); found = true; }
                    }
                if (!found) { log.Append(" " + d.name + "(寄せ先なし)"); continue; }

                Undo.RecordObject(d, "clear altar");
                d.position = to;
                EditorUtility.SetDirty(d);
                n++;
            }
            return "大きいアヒル: 台に乗っていた " + n + "体を水へどけた（削除は無し）" + log;
        }

        // ============================================================
        // 61体を配り直す（数は変えない）
        // ============================================================
        static string Scatter(Transform room, FloorMap map, out List<Transform> carriers, out List<Lump> placed)
        {
            carriers = new List<Transform>();
            placed = new List<Lump>();
            var group = room.Find("Props_SmallDucks");
            if (group == null) return "アヒル: Props_SmallDucks が無い";

            var ducks = new List<Transform>();
            foreach (Transform d in group) ducks.Add(d);
            ducks.Sort((a, b) => string.CompareOrdinal(a.name, b.name));   // 実行するたび同じ順番に

            var size = new Dictionary<Transform, Bounds>();
            foreach (var d in ducks) { if (TryBounds(d, out var b)) size[d] = b; }

            // ⚠️ **置いた個体を1体ずつ覚えておく。** 下に何か居ればその上に乗せる。
            //    これなら浮きもせず、めり込みもせず、「山」になる。
            //
            // ⚠️ これを「升目の高さマップ」でやってはいけない。
            //    足跡ぶんの四角を最大値で塗りつぶす書き方にしたところ、実寸 1〜2m の
            //    アヒルでは 2m 四方が塗られ、**1.5m 離れた隣まで「下に居る」と誤認**して
            //    高さが伝染し、61体が連鎖して **y=18m** まで積み上がった（実測）。
            //    円で持って、本当に重なっている相手の上にだけ乗せる。
            var lumps = placed;

            // 山の中心。ほとんどはプールの中に置く。
            var piles = new[]
            {
                new Vector2(-18.6f, -18.2f), new Vector2(-22.4f, -19.2f),
                new Vector2(-25.8f, -16.4f), new Vector2(-20.8f, -13.4f),
                new Vector2(-17.2f, -12.2f), new Vector2(-24.6f, -11.0f),
                new Vector2(-19.4f, -10.2f), new Vector2(-16.0f, -16.0f),
            };

            // --- 運ぶ3体をどこに隠すか ---
            //
            // ⚠️ 3体とも陸の上に置くと、熱を見なくても「陸に転がっている3体」で当たる。
            //    サーモ役の出番が消えるので、**2体は山の中に埋める**。
            //    プールには階段を付けたので、水の中へ取りに行っても戻ってこられる。
            //    1体だけプールサイドに置いて、最初の1体は見つけやすくしてある。
            var small = new List<Transform>();
            foreach (var d in ducks)
            {
                if (!size.ContainsKey(d)) continue;
                var sz = size[d].size;
                if (sz.x <= 0.84f && sz.z <= 0.84f) small.Add(d);
            }
            if (small.Count < CarryCount) return "アヒル: 受け皿に入る大きさの個体が足りない";
            for (int i = 0; i < CarryCount; i++) carriers.Add(small[i]);

            // ⚠️ 隠す山は**必ず水の中**から選ぶ。決め打ちにしたら片方がデッキの上で、
            //    熱い3体のうち2体が陸に転がる形になった。
            //    狙いの点に一番近い「水の升」を取る。
            var hidePiles = new List<Vector2>();
            foreach (var want in new[] { new Vector2(-20.8f, -13.4f), new Vector2(-18.4f, -17.6f) })
            {
                float best = float.MaxValue; Vector2 got = want; bool found = false;
                for (int i = 0; i < map.nx; i++)
                    for (int j = 0; j < map.nz; j++)
                    {
                        if (!map.free[i, j] || map.land[i, j]) continue;
                        var w = map.World(i, j);
                        float dd = (w - want).sqrMagnitude;
                        if (dd < best) { best = dd; got = w; found = true; }
                    }
                if (found) hidePiles.Add(got);
            }
            if (hidePiles.Count == 0) hidePiles.Add(piles[0]);

            var rng = new System.Random(6061);
            int moved = 0, idx = 0, stranded = 0;

            // 1体目：プールサイド（歩ける床）
            var landSpots = PickLandSpots(map, 1);
            if (landSpots.Count > 0 && size.ContainsKey(carriers[0]))
            {
                var b0 = size[carriers[0]];
                PlaceOn(carriers[0], b0, map, lumps,
                        new Vector3(landSpots[0].x, 0f, landSpots[0].z), true, true);
                moved++;
            }

            // --- プールサイドにも程よく散らす ---
            //
            // ⚠️ 以前は「陸は1体だけ」にしていた。無制限に許すと 61体中52体が
            //    プールサイドに並んだからだが、全部プールの中だと味気ない（作者指摘）。
            // ⚠️ デッキは幅2.6mしかない輪。無造作に置くと部屋が通れなくなるので、
            //    1体置くごとに BFS で歩ける範囲を測り直し、**そのアヒルが占める面積より
            //    多く孤立させたら置き直す**。これで通路は必ず残る。
            var occ = new bool[map.nx, map.nz];
            var landPlaced = new List<Vector2>();
            int onLand = 0;

            // 小さい個体ほど通路を塞ぎにくいので、小さい順に選ぶ
            var landPick = new List<Transform>();
            foreach (var d in ducks) if (!carriers.Contains(d) && size.ContainsKey(d)) landPick.Add(d);
            landPick.Sort((a, b) =>
            {
                float fa = size[a].size.x * size[a].size.z, fb = size[b].size.x * size[b].size.z;
                int c = fa.CompareTo(fb);
                return c != 0 ? c : string.CompareOrdinal(a.name, b.name);   // 毎回同じ順番に
            });

            var landDucks = new HashSet<Transform>();
            for (int k = 0; k < LandCount && k < landPick.Count; k++)
            {
                var d = landPick[k];
                if (!TryPlaceLand(d, size[d], map, lumps, occ, landPlaced, rng)) continue;
                landDucks.Add(d);
                onLand++; moved++;
            }

            // --- 残りを山へ ---
            var rest = new List<Transform>();
            foreach (var d in ducks)
                if (!carriers.Contains(d) && !landDucks.Contains(d) && size.ContainsKey(d)) rest.Add(d);

            // ⚠️ 隠す2体を**先に**置くと山の一番下に埋まる。実測で底(-1.3m)に沈み、
            //    上に 2m ぶんアヒルが積まれて、掘り出すのが苦行になっていた。
            //    山を6割ほど作ってから差し込むと**山の中腹**に収まる。
            //    そこなら角度によってサーモ役の目に入り、掘る手間も数体で済む。
            int insertAt = Mathf.RoundToInt(rest.Count * 0.6f);
            int hidden = 0;

            for (int n2 = 0; n2 < rest.Count; n2++)
            {
                if (n2 == insertAt)
                {
                    for (int k = 1; k < carriers.Count; k++)
                    {
                        var hd = carriers[k];
                        var aim2 = hidePiles[(k - 1) % hidePiles.Count];
                        if (TryPlaceNear(hd, size[hd], map, lumps, aim2, 0f, 0.6f, rng)) { moved++; hidden++; }
                        else stranded++;
                    }
                }

                var d = rest[n2];
                var b = size[d];
                var aim = piles[idx % piles.Length]; idx++;
                // ⚠️ 山の半径は個体の実寸（直径1〜2m）から決めること。1.6m にしていたとき、
                //    1つの山に8体ぶんを押し込む形になって高さが足りなくなり、大半が
                //    下の逃げ道へ落ちていた。2.2m なら1段に4体ほど並ぶ。
                if (TryPlaceNear(d, b, map, lumps, aim, 0.2f, 2.2f, rng)) { moved++; continue; }
                // どうしても入らないときは、いま一番低い所へ必ず置く
                if (PlaceLowest(d, b, map, lumps)) moved++; else stranded++;
            }
            if (hidden == 0)
                for (int k = 1; k < carriers.Count; k++)
                {
                    var hd = carriers[k];
                    var aim2 = hidePiles[(k - 1) % hidePiles.Count];
                    if (TryPlaceNear(hd, size[hd], map, lumps, aim2, 0f, 0.8f, rng)) moved++; else stranded++;
                }

            Physics.SyncTransforms();
            return "アヒル: " + ducks.Count + "体を配り直した（プールサイド " + (onLand + 1)
                 + "体 / 水の中 " + piles.Length + "つの山 / 動かした "
                 + moved + "体 / 削除も追加も無し）"
                 + (stranded > 0 ? " ⚠ 置けなかった " + stranded + "体" : "");
        }

        /// <summary>
        /// 入口から歩いて行ける陸升の数。occ に印が付いた升はアヒルで塞がっているとみなす。
        /// </summary>
        static int ReachCount(FloorMap map, bool[,] occ)
        {
            if (!map.Cell(-14.45f, -16.90f, out int si, out int sj)) return 0;
            if (!map.land[si, sj] || occ[si, sj]) return 0;

            var seen = new bool[map.nx, map.nz];
            var q = new Queue<Vector2Int>();
            seen[si, sj] = true; q.Enqueue(new Vector2Int(si, sj));
            int n = 1;
            int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int a = c.x + dx[k], b = c.y + dz[k];
                    if (a < 0 || b < 0 || a >= map.nx || b >= map.nz) continue;
                    if (seen[a, b] || !map.land[a, b] || occ[a, b]) continue;
                    if (Mathf.Abs(map.y[a, b] - map.y[c.x, c.y]) > 0.35f) continue;
                    seen[a, b] = true; n++; q.Enqueue(new Vector2Int(a, b));
                }
            }
            return n;
        }

        /// <summary>
        /// プールサイドに1体置く。**通路を殺さない場所にしか置かない。**
        /// 半分は既に置いた個体の近くを狙って、ぽつぽつと小さな群れになるようにする。
        /// </summary>
        static bool TryPlaceLand(Transform d, Bounds b, FloorMap map, List<Lump> lumps,
                                 bool[,] occ, List<Vector2> already, System.Random rng)
        {
            int r = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(b.size.x, b.size.z) * 0.5f / map.step), 0, 8);
            int before = ReachCount(map, occ);       // 失敗しても occ は戻すので1回でよい

            for (int t = 0; t < 250; t++)
            {
                int i, j;
                if (already.Count > 0 && (t & 1) == 0)
                {
                    var q = already[rng.Next(already.Count)];
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rad = 0.9f + (float)rng.NextDouble() * 0.9f;
                    if (!map.Cell(q.x + Mathf.Cos(ang) * rad, q.y + Mathf.Sin(ang) * rad, out i, out j)) continue;
                }
                else
                {
                    i = rng.Next(map.nx); j = rng.Next(map.nz);
                }

                if (!map.land[i, j] || !map.free[i, j] || !map.reach[i, j] || occ[i, j]) continue;

                var touched = new List<Vector2Int>();
                int touchedLand = 0;
                for (int a = Mathf.Max(0, i - r); a <= Mathf.Min(map.nx - 1, i + r); a++)
                    for (int c = Mathf.Max(0, j - r); c <= Mathf.Min(map.nz - 1, j + r); c++)
                        if (!occ[a, c])
                        {
                            occ[a, c] = true;
                            touched.Add(new Vector2Int(a, c));
                            if (map.land[a, c]) touchedLand++;
                        }

                // 自分が占めるぶんより 12升(約0.75㎡)以上よけいに孤立させたら却下。
                int after = ReachCount(map, occ);
                bool kills = after < before - touchedLand - 12;

                var w = map.World(i, j);
                if (kills || !PlaceOn(d, b, map, lumps, new Vector3(w.x, 0f, w.y), true, true))
                {
                    foreach (var p in touched) occ[p.x, p.y] = false;
                    continue;
                }

                already.Add(w);
                return true;
            }
            return false;
        }

        /// <summary>山の近くに置き場所を探す。見つかれば置いて true。</summary>
        static bool TryPlaceNear(Transform d, Bounds b, FloorMap map, List<Lump> lumps,
                                 Vector2 aim, float rMin, float rMax, System.Random rng)
        {
            for (int tryN = 0; tryN < 240; tryN++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = rMin + (float)rng.NextDouble() * (rMax - rMin);
                var at = new Vector3(aim.x + Mathf.Cos(ang) * rad, 0f, aim.y + Mathf.Sin(ang) * rad);
                if (PlaceOn(d, b, map, lumps, at, false, false)) return true;
            }
            return false;
        }

        /// <summary>
        /// 山にも入らなかったときの最後の手段。**いま一番低い所**に置く。
        /// ⚠️ 「升を順に見て最初に空いていた所へ relax で置く」と書いてはいけない。
        ///    毎回まったく同じ升が選ばれ、そこへ積み上がって **y=48m まで伸びる塔**になった（実測）。
        ///    毎回いちばん低い所を選べば、置くたびに最低点が移るので塔にならない。
        /// </summary>
        static bool PlaceLowest(Transform d, Bounds b, FloorMap map, List<Lump> lumps)
        {
            var at = new List<Vector3>();
            var h = new List<float>();
            for (int i = 0; i < map.nx; i++)
                for (int j = 0; j < map.nz; j++)
                {
                    if (!map.free[i, j] || map.land[i, j]) continue;
                    var w = map.World(i, j);
                    at.Add(new Vector3(w.x, 0f, w.y));
                    h.Add(TopAt(lumps, w, Mathf.Max(b.size.x, b.size.z) * 0.5f, map.y[i, j]) - map.y[i, j]);
                }

            var order = new int[at.Count];
            for (int k = 0; k < order.Length; k++) order[k] = k;
            System.Array.Sort(order, (a, c) => h[a].CompareTo(h[c]));

            for (int k = 0; k < order.Length; k++)
                if (PlaceOn(d, b, map, lumps, at[order[k]], true, false)) return true;
            return false;
        }

        /// <summary>
        /// 真下にある物（床か、先に置いたアヒル）の上に載せる。
        /// ⚠️ 浮かせない・めり込ませない。高さは必ず下にある物から決める。
        /// </summary>
        static bool PlaceOn(Transform d, Bounds b, FloorMap map, List<Lump> lumps, Vector3 at, bool relax, bool allowLand)
        {
            if (!map.Cell(at.x, at.z, out int ci, out int cj)) return false;
            if (!map.free[ci, cj]) return false;

            // ⚠️ 判定を footprint 全体に広げすぎない。最初は半径いっぱいの升が
            //    すべて空いていることを求めたが、2m のアヒルだと 2.25m 四方が
            //    まるごと空いている必要があり、**61体中46体が置けなかった**。
            //    通れなくなるかどうかは OverlapsWalk で見れば足りる。
            // ⚠️ **陸に置くのは指定した1体だけ。** 「中心が陸なら許す」という書き方をしたら
            //    山の中心が岸際にあるせいで **61体中52体がプールサイドに並んだ**。
            //    作者の指示は「もっとプールの中や山の中に分散、プールサイドは1体くらい」。
            bool onLand = map.land[ci, cj];
            if (onLand && !allowLand) return false;
            var foot = new Bounds(new Vector3(at.x, 0f, at.z), b.size);
            if (!allowLand && OverlapsWalk(map, foot, 0.12f)) return false;   // 通路を塞がない

            // 下にある物（床か、先に置いたアヒル）の高さ。
            float r = Mathf.Max(b.size.x, b.size.z) * 0.5f;
            var xz = new Vector2(at.x, at.z);
            float floorY = map.y[ci, cj];
            float top = TopAt(lumps, xz, r, floorY);

            // 高く積みすぎない。塔になると「山」に見えないし、上の個体に手が届かない。
            // 元の部屋もアヒルが胸の高さまで積んであったので、そのくらいまでは許す。
            // ⚠️ relax でも上限を外さないこと。外したら y=48m の塔ができた（実測）。
            if (top - floorY > (relax ? RelaxPile : MaxPile)) return false;

            // ⚠️ アヒルの**原点は中心ではない**。`position.y = top + 高さの半分` と書くと、
            //    原点が足元にある個体はそのぶん浮く（実測で 61体中29体、最大 2.11m）。
            //    「いまの見た目の箱」を測り直して、そこからの差分で動かす。原点がどこでも合う。
            if (!TryBounds(d, out var cur)) return false;
            var p = d.position;
            d.position = new Vector3(p.x + (at.x - cur.center.x),
                                     p.y + (top - cur.min.y),
                                     p.z + (at.z - cur.center.z));
            EditorUtility.SetDirty(d);

            lumps.Add(new Lump { xz = xz, r = r, bottom = top, top = top + cur.size.y });
            return true;
        }

        /// <summary>
        /// 散らし終えた小さいアヒルを、**本物の重力で**落ち着かせる。
        ///
        /// 計算で高さを決める方式は、アヒルの形（ドーム型・原点が中心にない・個体差が
        /// 1.0〜2.0m）を近似しきれず、浮きとめり込みが何度も出た。物理に任せれば
        /// 「山積み」も「転がって散らばる」も勝手に正しくなる。
        ///
        /// ⚠️ **シーン中の他の Rigidbody を必ず止めてから回すこと。**
        ///    他の部屋の持ち運べる物（鍵など）も一緒に落下して、部屋が壊れる。
        /// ⚠️ Rigidbody は非凸の MeshCollider を受け付けない。回すあいだだけ凸にする。
        /// </summary>
        static string SettlePhysics(Transform room)
        {
            var group = room.Find("Props_SmallDucks");
            if (group == null) return "物理: 対象なし";

            // --- 他の Rigidbody を止める ---
            var frozen = new List<Rigidbody>();
            foreach (var rb in Object.FindObjectsOfType<Rigidbody>())
                if (!rb.isKinematic) { rb.isKinematic = true; frozen.Add(rb); }

            var ducks = new List<Transform>();
            foreach (Transform d in group) ducks.Add(d);

            var startPos = new Dictionary<Transform, Vector3>();
            var startRot = new Dictionary<Transform, Quaternion>();
            var mine = new List<Rigidbody>();
            var temps = new List<Collider>();
            var wasOn = new Dictionary<Collider, bool>();

            foreach (var d in ducks)
            {
                startPos[d] = d.position;
                startRot[d] = d.rotation;

                // ⚠️ **元の当たり判定を当てにしない。**
                //    アヒルの MeshCollider は無効のまま置かれていることがあり、
                //    「有効な物だけ凸にする」と書いたら当たり判定ゼロで回してしまい、
                //    61体中48体が床をすり抜けて落ちた（実測：底 -2.44m）。
                //    元は全部いったん切って、回すあいだ用の凸コライダーを必ず1つ作る。
                foreach (var c in d.GetComponentsInChildren<Collider>(true))
                {
                    wasOn[c] = c.enabled;
                    c.enabled = false;
                }

                var mf = d.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null)
                {
                    var tmc = Undo.AddComponent<MeshCollider>(mf.gameObject);
                    tmc.sharedMesh = mf.sharedMesh;
                    tmc.convex = true;
                    temps.Add(tmc);
                }
                else
                {
                    var rend = d.GetComponentInChildren<Renderer>(true);
                    if (rend == null) continue;
                    var bc = Undo.AddComponent<BoxCollider>(d.gameObject);
                    bc.center = d.InverseTransformPoint(rend.bounds.center);
                    bc.size = new Vector3(rend.bounds.size.x / Mathf.Max(0.0001f, d.lossyScale.x),
                                          rend.bounds.size.y / Mathf.Max(0.0001f, d.lossyScale.y),
                                          rend.bounds.size.z / Mathf.Max(0.0001f, d.lossyScale.z));
                    temps.Add(bc);
                }

                var rb = d.GetComponent<Rigidbody>();
                if (rb == null) rb = Undo.AddComponent<Rigidbody>(d.gameObject);
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.mass = 1f;
                rb.drag = 0.30f;
                rb.angularDrag = 1.20f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                // ⚠️ 少しめり込んだ状態から始まるので、押し戻す速度を抑えないと山が吹き飛ぶ。
                rb.maxDepenetrationVelocity = 1.0f;
                mine.Add(rb);
            }

            var mode = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Physics.SyncTransforms();
                for (int i = 0; i < 400; i++) Physics.Simulate(0.02f);   // 8秒ぶん
            }
            finally
            {
                Physics.simulationMode = mode;
            }

            // --- 後片付け ---
            foreach (var rb in mine) Undo.DestroyObjectImmediate(rb);
            foreach (var c in temps) Undo.DestroyObjectImmediate(c);
            foreach (var kv in wasOn) if (kv.Key != null) kv.Key.enabled = kv.Value;
            foreach (var rb in frozen) if (rb != null) rb.isKinematic = false;

            // ⚠️ 穴から落ちた個体や、転がって部屋の外まで出た個体は元へ戻す。
            //    物理に任せる以上、想定外へ行くことはある。
            int back = 0;
            foreach (var d in ducks)
            {
                var p = d.position;
                var s = startPos[d];
                bool lost = p.y < s.y - 4.0f
                         || new Vector2(p.x - s.x, p.z - s.z).magnitude > 6.0f;
                if (!lost) continue;
                d.position = s;
                d.rotation = startRot[d];
                back++;
            }
            foreach (var d in ducks) EditorUtility.SetDirty(d);
            Physics.SyncTransforms();

            return "物理: " + ducks.Count + "体を重力で落ち着かせた"
                 + (back > 0 ? "（"+ back + "体は想定外へ行ったので元へ戻した）" : "");
        }

        /// <summary>
        /// 散らした結果、下の支えが無くなって宙に浮いた大きいアヒル(Props_Ducks)を座らせる。
        ///
        /// ⚠️ **下げるだけ。持ち上げない。** 巨大アヒルの背に乗せてある個体は部屋の見せ場で、
        ///    作者が意図して高い所に置いている。持ち上げる処理を入れるとそれを壊す。
        /// ⚠️ 部屋の主役の巨大アヒル(4m超)には触らない。
        /// </summary>
        static string SettleBigDucks(Transform room, FloorMap map)
        {
            var group = room.Find("Props_Ducks");
            if (group == null) return "大きいアヒル: 座らせる対象なし";

            int n = 0;
            var log = new System.Text.StringBuilder();
            foreach (Transform d in group)
            {
                if (!TryBounds(d, out var b)) continue;
                if (b.size.x > 4f || b.size.z > 4f) continue;
                if (!map.Cell(b.center.x, b.center.z, out int ci, out int cj)) continue;

                // ⚠️ 支えは Raycast で見る。焼いた床の高さ(map.y)だけで決めてはいけない。
                //    プールの底に段差や穴がある所で、実際の接地面より下まで沈める。
                //    Scatter の最後に Physics.SyncTransforms() を呼んであるので、
                //    散らし終えた小さいアヒルもここで正しく当たる。
                float support = float.NegativeInfinity;
                var hits = Physics.RaycastAll(new Vector3(b.center.x, b.min.y + 0.02f, b.center.z),
                                              Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(d)) continue;     // 自分は支えではない
                    if (h.point.y > support) support = h.point.y;
                }
                if (float.IsNegativeInfinity(support)) continue;         // 下に何も無い所は触らない

                float gap = b.min.y - support;
                if (gap <= 0.25f) continue;                 // 接しているとみなす

                Undo.RecordObject(d, "settle duck");
                d.position = new Vector3(d.position.x, d.position.y - gap, d.position.z);
                EditorUtility.SetDirty(d);
                n++;
                log.Append(" " + d.name + "(" + gap.ToString("F2") + "m下げた)");
            }
            return "大きいアヒル: 浮いていた " + n + "体を座らせた" + log;
        }

        /// <summary>置いた1体。山の高さはこの一覧からだけ決める。</summary>
        class Lump
        {
            public Vector2 xz;    // 水平の中心
            public float r;       // 水平の半径
            public float bottom;  // 底の高さ
            public float top;     // 上面の高さ
        }

        /// <summary>
        /// その場所に置いたとき、下にくる物の上面。何も無ければ床。
        /// ⚠️ 「水平に重なっている相手」だけを見る。升目を四角く塗る方式に戻してはいけない。
        /// </summary>
        static float TopAt(List<Lump> lumps, Vector2 xz, float r, float floorY)
        {
            // ⚠️ アヒルを**円柱**として扱ってはいけない。
            //    「重なっていれば相手の一番上に乗せる」と書いたところ、端にかすっただけの
            //    個体まで相手の全高ぶん持ち上がり、**61体中31体が 0.45m 以上浮いた**（実測）。
            //    アヒルはドーム型なので、中心から外れるほど背が低い。放物線で近似する。
            float top = floorY;
            for (int i = 0; i < lumps.Count; i++)
            {
                var L = lumps[i];
                float reach = L.r + r;
                float dd = (L.xz - xz).sqrMagnitude;
                if (dd >= reach * reach) continue;

                float u = Mathf.Sqrt(dd) / reach;                       // 0=真上 1=かすっただけ
                float h = L.bottom + (L.top - L.bottom) * (1f - u * u);
                if (h > top) top = h;
            }
            return top;
        }

        /// <summary>その物が「歩いて行ける床」に少しでも掛かっているか。</summary>
        static bool OverlapsWalk(FloorMap map, Bounds b, float margin)
        {
            if (!map.Cell(b.min.x - margin, b.min.z - margin, out int i0, out int j0)) { i0 = 0; j0 = 0; }
            if (!map.Cell(b.max.x + margin, b.max.z + margin, out int i1, out int j1)) { i1 = map.nx - 1; j1 = map.nz - 1; }
            i0 = Mathf.Clamp(i0, 0, map.nx - 1); i1 = Mathf.Clamp(i1, 0, map.nx - 1);
            j0 = Mathf.Clamp(j0, 0, map.nz - 1); j1 = Mathf.Clamp(j1, 0, map.nz - 1);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    if (map.reach[i, j]) return true;
            return false;
        }

        /// <summary>
        /// プールサイドに置く1体ぶんの場所を選ぶ。
        /// ⚠️ 「歩ける升を端から」では駄目。西の壁ぎわに一列に並んだ。
        ///    部屋の離れた点を先に決めて、そこに一番近い歩ける升を取る。
        /// </summary>
        static List<Vector3> PickLandSpots(FloorMap map, int n)
        {
            var targets = new[]
            {
                new Vector2(-16.8f,  -8.6f),   // 北東（入口から歩いてすぐ）
                new Vector2(-28.4f, -12.4f),
                new Vector2(-21.5f, -19.4f),
            };

            var picked = new List<Vector3>();
            for (int k = 0; k < n; k++)
            {
                var want = targets[k % targets.Length];
                float best = float.MaxValue; Vector3 bestP = Vector3.zero; bool found = false;
                for (int i = 1; i < map.nx - 1; i++)
                    for (int j = 1; j < map.nz - 1; j++)
                    {
                        if (!map.reach[i, j] || !map.free[i, j]) continue;
                        if (!map.reach[i - 1, j] || !map.reach[i + 1, j]
                         || !map.reach[i, j - 1] || !map.reach[i, j + 1]) continue;
                        var w = map.World(i, j);
                        bool tooNear = false;
                        foreach (var q in picked)
                            if ((new Vector2(q.x, q.z) - w).sqrMagnitude < 4.0f * 4.0f) { tooNear = true; break; }
                        if (tooNear) continue;
                        float d = (w - want).sqrMagnitude;
                        if (d < best) { best = d; bestP = new Vector3(w.x, map.y[i, j], w.y); found = true; }
                    }
                if (found) picked.Add(bestP);
            }
            return picked;
        }

        // ============================================================
        // 掴めるようにする（61体すべて）
        // ============================================================
        /// <summary>
        /// ⚠️ **掴めるのを熱い3体だけにしてはいけない。**
        ///    そうすると過去人が片っ端から触るだけで正解が分かってしまい、
        ///    サーモ役に聞く必要が消える（作者指摘）。61体すべてを掴めるようにして、
        ///    「持てるかどうか」を手掛かりにできなくする。
        ///
        /// ⚠️ VRCPickup を付けると SDK がその GameObject を layer 13(Pickup) へ移す。
        ///    アヒルは本体に MeshRenderer が付いているので、そのまま付けると
        ///    **絵ごと 13 へ行って3役全員から消える**
        ///    （過去人のカメラは 0/6/7/9/10/21/24 しか映さない）。
        ///    本体は「掴む当たり判定」だけにして、見た目は子へ移す。
        ///
        /// ⚠️ アヒルの当たり判定は凸でない MeshCollider。そのまま Rigidbody を付けると
        ///    Unity に弾かれる。元のは止めて掴む用の箱を足し、作り直すときは必ず元へ戻す。
        ///
        /// 代償：61体ぶんの VRCObjectSync と、3層×61体＝183 レンダラー。
        /// この部屋は元から軽いので許容するが、動きが重いと感じたら
        /// まずここ（同期する物の数）を疑うこと。
        /// </summary>
        static string MakeCarriers(Transform room, Transform parent, List<Transform> carriers,
                                   out List<Component> items)
        {
            items = new List<Component>();
            var group = room.Find("Props_SmallDucks");
            if (group == null) return "掴める化: Props_SmallDucks が無い";

            var tHot  = BlindThermalTable.Mat("DuckHot");
            var tCold = BlindThermalTable.Mat("Prop");
            var eProp = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/Echo/EchoMaterial_Prop.mat");
            if (eProp == null) eProp = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/EchoMaterial.mat");

            int n = 0, skipped = 0;
            var carrySet = new HashSet<Transform>(carriers);
            var order = new List<Transform>();
            foreach (Transform d in group) order.Add(d);
            order.Sort((x, y) => string.CompareOrdinal(x.name, y.name));

            foreach (var d in order)
            {
                var mf = d.GetComponent<MeshFilter>();
                var mr = d.GetComponent<MeshRenderer>();
                if (mf == null || mf.sharedMesh == null || mr == null) { skipped++; continue; }
                bool hot = carrySet.Contains(d);

                Undo.RecordObject(mr, "hide duck body");
                mr.enabled = false;

                var mc = d.GetComponent<MeshCollider>();
                if (mc != null) { Undo.RecordObject(mc, "off mesh collider"); mc.enabled = false; }
                var bc = d.GetComponent<BoxCollider>();
                if (bc == null) bc = Undo.AddComponent<BoxCollider>(d.gameObject);
                var mb = mf.sharedMesh.bounds;
                bc.center = mb.center; bc.size = mb.size;
                bc.isTrigger = false;

                var vis = new GameObject("DuckVision_Generated");
                Undo.RegisterCreatedObjectUndo(vis, "duck vision");
                vis.transform.SetParent(d, false);
                vis.transform.localPosition = Vector3.zero;
                vis.transform.localRotation = Quaternion.identity;
                vis.transform.localScale = Vector3.one;
                MakeMeshChild(vis.transform, "D_Body", LayerDefault, mf.sharedMesh, mr.sharedMaterial, false, null);
                MakeMeshChild(vis.transform, "T_Body", LayerThermal, mf.sharedMesh, hot ? tHot : tCold, false, null);
                MakeMeshChild(vis.transform, "E_Body", LayerEcho, mf.sharedMesh, eProp, true, null);

                var rb = d.GetComponent<Rigidbody>();
                if (rb == null) rb = Undo.AddComponent<Rigidbody>(d.gameObject);
                rb.mass = 0.4f;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.isKinematic = false;

                var pick = AddVrc(d.gameObject, "VRC.SDK3.Components.VRCPickup");
                var sync = AddVrc(d.gameObject, "VRC.SDK3.Components.VRCObjectSync");
                var carry = AddUdon(d.gameObject, "CarryableItem");
                if (carry != null)
                {
                    if (sync != null) SetObj(carry, "objectSync", sync);
                    if (pick != null) SetObj(carry, "pickup", pick);
                    SetSyncMode(carry, "None");
                    PushUdon(carry);
                    if (hot) items.Add(carry);
                }
                n++;
            }

            var sb = new System.Text.StringBuilder("掴める化: " + n + "体すべてに VRCPickup+ObjectSync"
                   + (skipped > 0 ? "（メッシュ無しで除外 " + skipped + "体）" : "")
                   + " / うち熱い " + items.Count + "体: ");
            foreach (var c in carriers) sb.Append(c.name + c.position.ToString("F1") + " ");
            return sb.ToString();
        }

        static string world(Transform t) { return t.position.ToString("F1"); }

        /// <summary>
        /// 前回付けた掴む仕掛けを剥がして、元のアヒルに戻す。**本体は消さない。**
        /// 他メンバーが置いた物なので、絵・当たり判定・レイヤーは必ず元へ返す。
        /// </summary>
        static void StripGenerated(GameObject go)
        {
            var t = System.Type.GetType("CarryableItem, Assembly-CSharp");
            if (t != null)
            {
                var c = go.GetComponent(t);
                if (c != null) Undo.DestroyObjectImmediate(c);
            }
            foreach (var n in new[] { "VRC.SDK3.Components.VRCObjectSync", "VRC.SDK3.Components.VRCPickup" })
            {
                var c = FindVrc(go, n);
                if (c != null) Undo.DestroyObjectImmediate(c);
            }
            foreach (var ub in go.GetComponents<Component>())
                if (ub != null && ub.GetType().FullName == "VRC.Udon.UdonBehaviour")
                    Undo.DestroyObjectImmediate(ub);

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) Undo.DestroyObjectImmediate(rb);
            var bc = go.GetComponent<BoxCollider>();
            if (bc != null) Undo.DestroyObjectImmediate(bc);       // これは足した物

            // ⚠️ 元からある物は必ず戻す。忘れると作り直すたびにアヒルが1体ずつ壊れる。
            var mc = go.GetComponent<MeshCollider>();
            if (mc != null) { Undo.RecordObject(mc, "restore"); mc.enabled = true; }
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) { Undo.RecordObject(mr, "restore"); mr.enabled = true; }
            go.layer = LayerDefault;                                // VRCPickup が 13 にしている
        }

        // ============================================================
        // 施錠扉（room7 へ抜ける南の扉）
        // ============================================================
        static string BuildGate(Transform room, Transform parent, List<Component> items,
                                Transform[] slots, MeshRenderer[] slotD, MeshRenderer[] slotT,
                                Transform center, List<Object> altarEcho)
        {
            // 南の扉枠を実測する
            Bounds frame = new Bounds(); bool has = false;
            foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.name != "FramePiece") continue;
                if (mr.bounds.center.z > -20.0f) continue;          // 東の入口は除く
                if (!has) { frame = mr.bounds; has = true; } else frame.Encapsulate(mr.bounds);
            }
            if (!has) return "施錠扉: 南の扉枠が見つからない";

            const float Jamb = 0.05f;
            float leafH = (frame.size.y - Jamb * 2f) - 0.02f;
            float leafW = (frame.size.x - Jamb * 2f) - 0.02f;
            float leafT = frame.size.z - 0.04f;

            var go = new GameObject("Gate");
            Undo.RegisterCreatedObjectUndo(go, "duck gate");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.layer = LayerDefault;

            var leaf = new GameObject("Leaf");
            Undo.RegisterCreatedObjectUndo(leaf, "leaf");
            leaf.transform.SetParent(go.transform, false);
            leaf.transform.position = new Vector3(frame.center.x, frame.min.y, frame.center.z);
            var closed = leaf.transform.localPosition;

            var steel = MakeMat("DuckGate_Leaf", new Color(0.30f, 0.31f, 0.33f), Color.black);
            var tGate = BlindThermalTable.Mat("Gate");
            var eMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_BLIND/Art/Materials/EchoMaterial.mat");
            var lp = new Vector3(0f, frame.min.y + Jamb + leafH * 0.5f - frame.min.y, 0f);
            var ls = new Vector3(leafW, leafH, leafT);
            var echoes = new List<Object>();
            MakeBox(leaf.transform, "D_Leaf", LayerDefault, leaf.transform.position + lp, ls, steel, false, null);
            MakeBox(leaf.transform, "T_Leaf", LayerThermal, leaf.transform.position + lp, ls, tGate, false, null);
            MakeBox(leaf.transform, "E_Leaf", LayerEcho, leaf.transform.position + lp, ls, eMat, true, echoes);

            var lc = leaf.AddComponent<BoxCollider>();
            lc.center = lp; lc.size = ls;

            // ⚠️ 扉の**東**へ滑らせる。西は x -30.3〜-20.3 も壁だが、
            //    東(-19.1〜-14.1)の方が 5m あって確実に隠れる。
            var open = closed + new Vector3(frame.size.x + 0.08f, 0f, 0f);

            var beh = AddUdon(go, "ItemAltar");
            if (beh == null) return "施錠扉: ItemAltar の付与に失敗";
            SetObjArray(beh, "items", items.ConvertAll(x => (Object)x).ToArray());
            SetObjArray(beh, "slots", System.Array.ConvertAll(slots, x => (Object)x));
            SetObj(beh, "altarCenter", center);
            SetObj(beh, "doorLeaf", leaf.transform);
            var so = new SerializedObject(beh);
            so.FindProperty("closedLocalPos").vector3Value = closed;
            so.FindProperty("openLocalPos").vector3Value = open;
            so.ApplyModifiedProperties();
            SetObjArray(beh, "slotMarksDefault", System.Array.ConvertAll(slotD, x => (Object)x));
            SetObjArray(beh, "slotMarksThermal", System.Array.ConvertAll(slotT, x => (Object)x));
            SetObj(beh, "filledDefault", MakeMat("Duck_SlotOn", new Color(0.10f, 0.70f, 0.22f), new Color(0.10f, 1.1f, 0.25f)));
            SetObj(beh, "filledThermal", BlindThermalTable.Mat("DuckSlotOn"));
            var all = new List<Object>(altarEcho); all.AddRange(echoes);
            SetObjArray(beh, "echoes", all.ToArray());
            SetSyncMode(beh, "Manual");
            PushUdon(beh);

            return "施錠扉: room7 への扉を施錠（扉 " + ls.ToString("F2")
                 + " / 開くと東へ " + (open.x - closed.x).ToString("F2") + "m 壁の中へ）"
                 + " / そろえる数 " + items.Count;
        }

        // ============================================================
        // 道具
        // ============================================================
        static Vector3 FloorCenter(Transform room)
        {
            Bounds b = new Bounds(); bool has = false;
            foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.name != "PoolDeck" && mr.name != "FloorTile") continue;
                if (!has) { b = mr.bounds; has = true; } else b.Encapsulate(mr.bounds);
            }
            return has ? b.center : room.position;
        }

        static Bounds Grow(Bounds b, float m) { var r = b; r.Expand(new Vector3(m * 2, 0, m * 2)); return r; }

        static bool Flat(Bounds a, Bounds b)
        {
            if (a.max.x <= b.min.x || a.min.x >= b.max.x) return false;
            if (a.max.z <= b.min.z || a.min.z >= b.max.z) return false;
            return true;
        }

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
            var result = new List<Bounds>();
            foreach (var g in groups)
            {
                var b = g;
                if (g.size.x < g.size.z) { b.Encapsulate(new Vector3(g.center.x - Reach, g.center.y, g.min.z)); b.Encapsulate(new Vector3(g.center.x - Reach, g.center.y, g.max.z)); }
                else { b.Encapsulate(new Vector3(g.min.x, g.center.y, g.center.z + Reach)); b.Encapsulate(new Vector3(g.max.x, g.center.y, g.center.z + Reach)); }
                result.Add(b);
            }
            return result;
        }

        static bool TryBounds(Transform t, out Bounds b)
        {
            b = new Bounds(); bool has = false;
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
            if (t == null)
            {
                foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.name == child.Substring(child.LastIndexOf('/') + 1)) return mr.sharedMaterial;
                return null;
            }
            var r = t.GetComponent<Renderer>(); return r != null ? r.sharedMaterial : null;
        }

        static Material MakeMat(string name, Color c, Color emission)
        {
            if (!AssetDatabase.IsValidFolder(GenMatDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Materials", "Gimmick");
            string path = GenMatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.color = c;
            m.SetFloat("_Glossiness", 0.35f);
            if (emission.maxColorComponent > 0.01f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", emission);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void MakeSolid(Transform parent, string name, Vector3 pos, Vector3 size,
                              Material d, Material t, Material e, List<Object> echoes)
        {
            MakeBox(parent, "D_" + name, LayerDefault, pos, size, d, false, null);
            MakeBox(parent, "T_" + name, LayerThermal, pos, size, t, false, null);
            MakeBox(parent, "E_" + name, LayerEcho, pos, size, e, true, echoes);
            var col = new GameObject("C_" + name);
            Undo.RegisterCreatedObjectUndo(col, "solid");
            col.transform.SetParent(parent, false);
            col.transform.position = pos;
            col.layer = LayerDefault;
            var bc = col.AddComponent<BoxCollider>();
            bc.size = size;
        }

        static MeshRenderer MakeBox(Transform parent, string name, int layer, Vector3 pos,
                                    Vector3 size, Material mat, bool echo, List<Object> echoes)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(go, "box");
            go.name = name;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.layer = layer;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (echo) AttachEcho(go, mr, echoes);
            return mr;
        }

        static GameObject MakeMeshChild(Transform parent, string name, int layer,
                                        Mesh mesh, Material mat, bool echo, List<Object> echoes)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "mesh child");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (echo) AttachEcho(go, mr, echoes);
            return go;
        }

        static void AttachEcho(GameObject go, MeshRenderer mr, List<Object> echoes)
        {
            var rec = AddUdon(go, "EchoReceiver");
            if (rec == null) return;
            var so = new SerializedObject(rec);
            var arr = so.FindProperty("targetRenderers");
            if (arr != null) { arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = mr; }
            so.ApplyModifiedProperties();
            PushUdon(rec);
            if (echoes != null) echoes.Add(rec);
        }

        static Component AddUdon(GameObject go, string typeName)
        {
            var t = System.Type.GetType(typeName + ", Assembly-CSharp");
            if (t == null) { Debug.LogError("BLIND: 型が無い " + typeName); return null; }
            var undoType = System.Type.GetType("UdonSharpEditor.UdonSharpUndo, UdonSharp.Editor");
            Component c = null;
            if (undoType != null)
            {
                var mi = undoType.GetMethod("AddComponent", new[] { typeof(GameObject), typeof(System.Type) });
                if (mi != null) c = mi.Invoke(null, new object[] { go, t }) as Component;
            }
            if (c == null) c = go.AddComponent(t);
            return c;
        }

        static Component AddVrc(GameObject go, string fullName)
        {
            var t = VrcType(fullName);
            if (t == null) { Debug.LogError("BLIND: VRC の型が無い " + fullName); return null; }
            var c = go.GetComponent(t);
            if (c == null) c = Undo.AddComponent(go, t);
            return c;
        }

        static Component FindVrc(GameObject go, string fullName)
        {
            var t = VrcType(fullName);
            return t == null ? null : go.GetComponent(t);
        }

        static System.Type VrcType(string fullName)
        {
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        static void SetSyncMode(Component c, string mode)
        {
            var usb = c as UdonSharp.UdonSharpBehaviour; if (usb == null) return;
            var ub = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb); if (ub == null) return;
            var so = new SerializedObject(ub);
            var p = so.FindProperty("_syncMethod"); if (p == null) return;
            int idx = System.Array.IndexOf(p.enumNames, mode);
            if (idx < 0) return;
            p.enumValueIndex = idx;
            so.ApplyModifiedProperties();
        }

        static void PushUdon(Component c)
        {
            var usb = c as UdonSharp.UdonSharpBehaviour; if (usb == null) return;
            if (UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb) == null) return;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
        }

        static void SetObj(Component c, string field, Object value)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError("BLIND: フィールドが無い " + field); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        static void SetObjArray(Component c, string field, Object[] values)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError("BLIND: フィールドが無い " + field); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedProperties();
        }
    }
}
