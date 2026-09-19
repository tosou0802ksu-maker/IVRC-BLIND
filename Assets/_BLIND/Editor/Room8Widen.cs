using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// room8 を広げる。
    ///
    /// 実測で room8 は内寸 11.8 x 6.8m（84㎡）ありながら、
    /// **人が立てるのは 29.9㎡（36%）だけ**で、立てる場所の 76% が幅1.0m未満だった。
    /// 倒れたロッカーが床一面に散らばっているせい。幅1mは1人がやっと通れる幅で、
    /// 3人協力では1人が止まると後ろ2人が詰まる。
    ///
    /// ⚠️ **ロッカーを間引いてはいけない。** 散らかり具合が room8 の見せ場だと作者が明言。
    ///    部屋そのものを広げ、ロッカーは**位置だけ**同じ倍率で外へ散らす。
    ///    大きさも向きも変えないので、乱雑さはそのままに隙間だけが広がる。
    ///
    /// ⚠️ 北壁と東壁は動かせない。
    ///    北壁には room7 からの入口（x -19.8..-18.6）、
    ///    東壁には room9 側の封鎖扉 Memory_SealedDoorway_East がある。
    ///    広げられるのは西と南だけ（全部屋の座標を調べて、どちらも完全に空きと確認済み）。
    ///
    /// ⚠️ 南へ伸ばしても room10 とは干渉しない。
    ///    room10 の西壁は x -9.10..-8.90、room8 の東壁は x -9.30..-9.10 で、
    ///    ちょうど隣り合うだけで重ならない（実測）。切り欠きは要らない。
    /// </summary>
    public static class Room8Widen
    {
        // --- 動かさない側（外面）---
        const float EastOuter  = -9.1f;
        const float NorthOuter = -40.7f;

        // --- 広げる側（外面）---
        const float WestOuter  = -27.2f;
        const float SouthOuter = -51.3f;

        const float WallT = 0.2f;
        const float WallH = 4.0f;
        const float FloorY = -0.05f, FloorT = 0.1f;
        const float CeilY = 4.05f, CeilT = 0.1f;

        /// <summary>ロッカーを散らす倍率。1.0 で元のまま。</summary>
        const float Spread = 1.5f;

        /// <summary>足す上限。実際は下の MinStand に当たった時点で止まる。</summary>
        const int AddCount = 40;

        /// <summary>
        /// 足し終わった時点で残す「人が立てる面積」の割合。
        /// ⚠️ 台数で決めてはいけない。35台と決め打ちしたら 103.9→68.2㎡ まで落ちて、
        ///    幅1m未満が 68% に戻った（広げる前が 76%）。台数ではなく結果で止める。
        /// ⚠️ 絶対値(㎡)で書くのも駄目。測り方（升の粗さ・半径）を変えると基準がずれ、
        ///    85㎡ と書いたら開始時点で既に下回っていて 1台も置けなかった。割合で持つ。
        /// </summary>
        const float KeepStand = 0.80f;

        /// <summary>足した個体に付ける目印。作り直すとき見分けるため。</summary>
        const string AddMark = "_add";

        /// <summary>散らす基準点＝動かさない北東の内角。ここから見て外へ広がる。</summary>
        const float AnchorX = -9.3f, AnchorZ = -40.9f;

        /// <summary>入口の前は空けておく（x範囲, zの奥行き）。</summary>
        const float DoorX0 = -20.3f, DoorX1 = -18.1f, DoorClear = 2.2f;

        /// <summary>元の位置を覚えておく所。倍率を変えて何度でもやり直せるようにする。</summary>
        const string OriginsPath = "Assets/_BLIND/Editor/Room8_PropOrigins.json";

        [MenuItem("BLIND/部屋修正/8. room8 を広げる")]
        public static void Menu_Build()
        {
            var log = Build();
            Debug.Log(log);
            EditorUtility.DisplayDialog("room8 を広げる", log, "OK");
        }

        [System.Serializable] public class Origin { public string name; public Vector3 pos; public Quaternion rot; }
        [System.Serializable] public class Origins { public List<Origin> items = new List<Origin>(); }

        public static string Build()
        {
            var rooms = GameObject.Find("=== ROOMS ===");
            if (rooms == null) return "room8: === ROOMS === が無い";
            Transform room = null;
            foreach (Transform t in rooms.transform) if (t.name == "room8") room = t;
            if (room == null) return "room8: 部屋が無い";

            var log = new System.Text.StringBuilder();
            log.AppendLine(Shell(room));
            log.AppendLine(ClearAdded(room));
            log.AppendLine(SpreadProps(room));
            log.AppendLine(Lighting(room));

            // ⚠️ ここで必ず同期する。次の物理がずれた位置で走ってしまう。
            Physics.SyncTransforms();
            log.AppendLine(Settle(room));
            log.AppendLine(AddLockers(room, AddCount));

            // ⚠️ 忘れると、動かした壁も床もロッカーも Raycast に映らないままになる。
            //    Unity は既定で autoSyncTransforms が切れているため。
            //    これを忘れて計測したら、広げる前とまったく同じ数字が出た。
            Physics.SyncTransforms();
            return log.ToString();
        }

        // ============================================================
        // 床・天井・壁
        // ============================================================
        static string Shell(Transform room)
        {
            float w = EastOuter - WestOuter;          // 18.1
            float d = NorthOuter - SouthOuter;        // 10.6
            float cx = (EastOuter + WestOuter) * 0.5f;
            float cz = (NorthOuter + SouthOuter) * 0.5f;

            int n = 0;

            // --- 床 ---
            var floor = room.Find("GeneratedRoom/Floor/FloorTile");
            if (floor != null)
            {
                Put(floor, new Vector3(cx, FloorY, cz), new Vector3(w, FloorT, d)); n++;
            }

            // --- 天井 ---
            var ceil = room.Find("Ceiling");
            if (ceil != null && ceil.GetComponent<Renderer>() != null)
            {
                Put(ceil, new Vector3(cx, CeilY, cz), new Vector3(w, CeilT, d)); n++;
            }

            // --- 壁 ---
            var walls = room.Find("GeneratedRoom/Walls");
            if (walls == null) return "room8: Walls が無い（床/天井 " + n + " 件だけ直した）";

            foreach (Transform seg in walls)
            {
                var r = seg.GetComponent<Renderer>();
                if (r == null) continue;                       // DoorFrame はそのまま
                var b = r.bounds;

                // 西壁：まるごと西へ移して、南北いっぱいに伸ばす
                if (b.size.x < 0.5f && b.center.x < -20.0f)
                {
                    Put(seg, new Vector3(WestOuter + WallT * 0.5f, WallH * 0.5f, cz),
                             new Vector3(WallT, WallH, d)); n++;
                    continue;
                }

                // 南壁：南へ移して、東西いっぱいに伸ばす
                if (b.size.z < 0.5f && b.center.z < -47.0f)
                {
                    Put(seg, new Vector3(cx, WallH * 0.5f, SouthOuter + WallT * 0.5f),
                             new Vector3(w, WallH, WallT)); n++;
                    continue;
                }

                // 北壁の西寄りの一枚：入口の西端から新しい西壁まで伸ばす
                if (b.size.z < 0.5f && b.center.z > -41.5f && b.max.y > 3.5f && b.center.x < -19.9f)
                {
                    float x1 = b.max.x;                        // 入口の西端（動かさない）
                    Put(seg, new Vector3((WestOuter + x1) * 0.5f, WallH * 0.5f, NorthOuter - WallT * 0.5f),
                             new Vector3(x1 - WestOuter, WallH, WallT)); n++;
                    continue;
                }

                // 東壁の南寄りの一枚：封鎖扉の南端から新しい南壁まで伸ばす
                if (b.size.x < 0.5f && b.center.x > -9.5f && b.max.y > 3.5f && b.center.z < -46.5f)
                {
                    float z1 = b.max.z;                        // 封鎖扉の南端（動かさない）
                    Put(seg, new Vector3(EastOuter - WallT * 0.5f, WallH * 0.5f, (SouthOuter + z1) * 0.5f),
                             new Vector3(WallT, WallH, z1 - SouthOuter)); n++;
                    continue;
                }
            }

            return "room8 の殻: " + n + " 枚を作り直した（内寸 "
                 + (w - WallT * 2f).ToString("F1") + " x " + (d - WallT * 2f).ToString("F1") + " m）";
        }

        static void Put(Transform t, Vector3 worldCenter, Vector3 worldSize)
        {
            Undo.RecordObject(t, "room8 widen");
            var p = t.parent;
            var ls = p != null ? p.lossyScale : Vector3.one;
            t.position = worldCenter;
            t.localScale = new Vector3(worldSize.x / Mathf.Max(0.0001f, ls.x),
                                       worldSize.y / Mathf.Max(0.0001f, ls.y),
                                       worldSize.z / Mathf.Max(0.0001f, ls.z));
            EditorUtility.SetDirty(t);
        }

        // ============================================================
        // ロッカーを外へ散らす
        // ============================================================
        /// <summary>
        /// 控えてある「元の位置と角度」を基準に、位置だけ Spread 倍へ広げ直す。
        ///
        /// ⚠️ **角度は必ず控えの値へ戻すこと。** 倒れ方の妙な角度が room8 の見せ場で、
        ///    作者が残したいと明言している。位置合わせのついでに物理で転がすと平らに寝て、
        ///    その奇妙さが消える（実際に一度消してしまい、git から拾い直した）。
        /// ⚠️ 今の位置を基準にすると、実行するたびに 1.5 倍ずつ離れていく。控えを使う。
        /// </summary>
        static string SpreadProps(Transform room)
        {
            var group = room.Find("Props_Lockers");
            if (group == null) return "ロッカー: Props_Lockers が無い";

            var org = LoadOrigins();
            if (org == null || org.items == null || org.items.Count == 0)
                return "ロッカー: 控え " + OriginsPath + " が読めない";

            float xMin = WestOuter + WallT, xMax = EastOuter - WallT;
            float zMin = SouthOuter + WallT, zMax = NorthOuter - WallT;

            int moved = 0, clamped = 0, pushed = 0;
            int n = Mathf.Min(group.childCount, org.items.Count);
            for (int i = 0; i < n; i++)
            {
                var d = group.GetChild(i);
                var o = org.items[i];

                Undo.RecordObject(d, "spread lockers");
                d.localRotation = o.rot;
                d.localPosition = o.pos;                       // いったん元へ戻してから広げる

                var w0 = d.position;
                float nx = AnchorX + (w0.x - AnchorX) * Spread;
                float nz = AnchorZ + (w0.z - AnchorZ) * Spread;

                var r = d.GetComponentInChildren<Renderer>(true);
                float rad = r != null ? Mathf.Max(r.bounds.size.x, r.bounds.size.z) * 0.5f : 0.5f;
                float cx = Mathf.Clamp(nx, xMin + rad, xMax - rad);
                float cz = Mathf.Clamp(nz, zMin + rad, zMax - rad);
                if (!Mathf.Approximately(cx, nx) || !Mathf.Approximately(cz, nz)) clamped++;

                // ⚠️ 入口の前だけは必ず空ける。ここが塞がると部屋に入れない。
                if (cx > DoorX0 - rad && cx < DoorX1 + rad && cz > NorthOuter - DoorClear - rad)
                {
                    cz = NorthOuter - DoorClear - rad;
                    pushed++;
                }

                d.position = new Vector3(cx, w0.y, cz);
                EditorUtility.SetDirty(d);
                moved++;
            }

            return "ロッカー: " + moved + "台を " + Spread.ToString("F2")
                 + " 倍に広げ直した（角度は元のまま / 壁際へ寄せた " + clamped
                 + " / 入口前からどけた " + pushed + "）";
        }

        static Origins LoadOrigins()
        {
            if (!System.IO.File.Exists(OriginsPath)) return null;
            return JsonUtility.FromJson<Origins>(System.IO.File.ReadAllText(OriginsPath));
        }

        /// <summary>前回足したロッカーを消す。これが無いと実行のたびに増え続ける。</summary>
        static string ClearAdded(Transform room)
        {
            var group = room.Find("Props_Lockers");
            if (group == null) return "追加分の掃除: Props_Lockers が無い";

            var doomed = new List<GameObject>();
            foreach (Transform d in group) if (d.name.Contains(AddMark)) doomed.Add(d.gameObject);
            foreach (var g in doomed) Undo.DestroyObjectImmediate(g);
            Physics.SyncTransforms();
            return "追加分の掃除: " + doomed.Count + "台を消した";
        }

        // ============================================================
        // 広がったぶんロッカーを増やす
        // ============================================================
        /// <summary>
        /// 床が 2.2 倍になったぶん、ロッカーを足して散らかり具合を戻す。
        ///
        /// ⚠️ **足しすぎると元の木阿弥。** 広げた目的は 3人がすれ違えるようにすること。
        ///    1台置くごとに「幅1.1m以上で歩ける範囲」を測り直し、
        ///    その台が占める面積より多く削ったら置き直す。
        /// ⚠️ 角度は元からある55台の中から借りる。乱数で作ると倒れ方の癖が変わる。
        /// </summary>
        static string AddLockers(Transform room, int want)
        {
            var group = room.Find("Props_Lockers");
            if (group == null || group.childCount == 0) return "追加: Props_Lockers が無い";

            var seeds = new List<Transform>();
            foreach (Transform d in group) seeds.Add(d);

            var rng = new System.Random(8080);
            float xMin = WestOuter + WallT, xMax = EastOuter - WallT;
            float zMin = SouthOuter + WallT, zMax = NorthOuter - WallT;

            Physics.SyncTransforms();
            int before = WalkCells();

            // ⚠️ **下限を決めておくこと。** 1台ごとの差分だけで判定したら、
            //    35台すべてが通って歩ける面積が 103.9→66.6㎡ まで落ちた。
            //    広げた意味が無くなるので、歩ける網の 8割は必ず残す。
            int floor0 = Mathf.RoundToInt(before * 0.80f);
            float minStand = StandArea() * KeepStand;

            int added = 0, rejected = 0;
            for (int k = 0; k < want; k++)
            {
                // ⚠️ 試行を増やしすぎない。1回ごとに部屋全体を物理で走査するので、
                //    60台×60回にしたら Unity が数分固まった。粗い格子＋少ない試行で足りる。
                bool ok = false;
                for (int t = 0; t < 12 && !ok; t++)
                {
                    var src = seeds[rng.Next(seeds.Count)];
                    var go = Object.Instantiate(src.gameObject, group);
                    go.name = src.name + AddMark + k;
                    Undo.RegisterCreatedObjectUndo(go, "add locker");

                    // 角度は別の個体から借りる（倒れ方の癖をそろえる）
                    go.transform.rotation = seeds[rng.Next(seeds.Count)].rotation;

                    var r = go.GetComponentInChildren<Renderer>(true);
                    float rad = r != null ? Mathf.Max(r.bounds.size.x, r.bounds.size.z) * 0.5f : 0.6f;
                    float cx = Mathf.Lerp(xMin + rad, xMax - rad, (float)rng.NextDouble());
                    float cz = Mathf.Lerp(zMin + rad, zMax - rad, (float)rng.NextDouble());

                    // 入口の前は空ける
                    if (cx > DoorX0 - rad && cx < DoorX1 + rad && cz > NorthOuter - DoorClear - rad)
                    { Undo.DestroyObjectImmediate(go); continue; }

                    go.transform.position = new Vector3(cx, 0.6f, cz);
                    Physics.SyncTransforms();
                    Drop(go.transform);
                    Physics.SyncTransforms();

                    int after = WalkCells();
                    // その台が実際に覆う面積より 8升(0.5㎡)以上よけいに削ったら却下
                    float fx = r != null ? r.bounds.size.x : 1f, fz = r != null ? r.bounds.size.z : 1f;
                    int foot = Mathf.CeilToInt(fx * fz / (0.40f * 0.40f));
                    if (after < floor0 || after < before - foot - 3 || StandArea() < minStand)
                    {
                        Undo.DestroyObjectImmediate(go);
                        Physics.SyncTransforms();
                        rejected++;
                        continue;
                    }

                    before = after; added++; ok = true;
                    EditorUtility.SetDirty(go);
                }
            }

            // 足した個体が別の個体の上に乗って浮くことがあるので、最後にもう一度下ろす
            Physics.SyncTransforms();
            foreach (Transform d in group) Drop(d);
            Physics.SyncTransforms();

            return "追加: ロッカーを " + added + "台足した（却下 " + rejected
                 + " / 合計 " + group.childCount + "台）";
        }

        /// <summary>人が立てる面積(㎡)。プレイヤーの半径 0.23m ＋ 少し余裕を見た 0.30m で測る。</summary>
        static float StandArea()
        {
            const float step = 0.40f, R = 0.30f;
            float x0 = WestOuter, z0 = SouthOuter, x1 = EastOuter, z1 = NorthOuter;
            int nx = (int)((x1 - x0) / step) + 1, nz = (int)((z1 - z0) / step) + 1;
            int n = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float wx = x0 + i * step, wz = z0 + j * step;
                    if (!Physics.Raycast(new Vector3(wx, 3.5f, wz), Vector3.down, out var h, 8f,
                                         ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (h.point.y < -0.6f || h.point.y > 0.6f) continue;
                    if (Physics.CheckCapsule(new Vector3(wx, h.point.y + R + 0.05f, wz),
                                             new Vector3(wx, h.point.y + 1.60f, wz),
                                             R, ~0, QueryTriggerInteraction.Ignore)) continue;
                    n++;
                }
            return n * step * step;
        }

        /// <summary>幅1.1m以上を保ったまま入口から歩ける升の数。狭さの物差し。</summary>
        static int WalkCells()
        {
            const float step = 0.40f;
            float x0 = WestOuter, z0 = SouthOuter, x1 = EastOuter, z1 = NorthOuter;
            int nx = (int)((x1 - x0) / step) + 1, nz = (int)((z1 - z0) / step) + 1;
            var open = new bool[nx, nz];

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float wx = x0 + i * step, wz = z0 + j * step;
                    if (!Physics.Raycast(new Vector3(wx, 3.5f, wz), Vector3.down, out var h, 8f,
                                         ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (h.point.y < -0.6f || h.point.y > 0.6f) continue;
                    // 半径0.55m ＝ 通路幅1.1m ぶんの余裕があるか。
                    // ⚠️ カプセルの端は必ず半径ぶん床から離すこと。
                    //    下端を 0.30 にしたら球の底が床下 0.25m に出て、全升が「塞がっている」
                    //    と判定され、この関数が常に 0 を返していた。結果、置き過ぎの判定が
                    //    まったく効かず 35台すべてが通った。
                    const float R = 0.55f;
                    if (Physics.CheckCapsule(new Vector3(wx, h.point.y + R + 0.05f, wz),
                                             new Vector3(wx, h.point.y + 1.60f, wz),
                                             R, ~0, QueryTriggerInteraction.Ignore)) continue;
                    open[i, j] = true;
                }

            // ⚠️ 起点を入口の真下に決め打ちしてはいけない。そこが塞がると 0 が返り、
            //    「置いても何も減っていない」と誤判定して 35台すべてを通してしまった。
            //    入口に一番近い空き升から数える。
            int si = -1, sj = -1;
            {
                float best = float.MaxValue;
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                    {
                        if (!open[i, j]) continue;
                        float wx = x0 + i * step, wz = z0 + j * step;
                        float dd = (wx + 19.2f) * (wx + 19.2f) + (wz + 41.8f) * (wz + 41.8f);
                        if (dd < best) { best = dd; si = i; sj = j; }
                    }
            }
            if (si < 0) return 0;

            var seen = new bool[nx, nz];
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
                    if (a < 0 || b < 0 || a >= nx || b >= nz || seen[a, b] || !open[a, b]) continue;
                    seen[a, b] = true; n++; q.Enqueue(new Vector2Int(a, b));
                }
            }
            return n;
        }

        // ============================================================
        // 浮いた個体を落とす
        // ============================================================
        /// <summary>
        /// 広げたせいで下の支えが消え、宙に浮いた個体を真下の面へ落とす。
        ///
        /// ⚠️ **物理シミュレーションで転がしてはいけない。** 一度それをやったら 55台すべてが
        ///    平らに寝てしまい、倒れ方の妙な角度＝room8 の見せ場が消えた。
        ///    角度は変えず、まっすぐ下ろすだけにする。
        /// </summary>
        static string Settle(Transform room)
        {
            var group = room.Find("Props_Lockers");
            if (group == null) return "着地: Props_Lockers が無い";

            Physics.SyncTransforms();
            int dropped = 0;
            foreach (Transform d in group) if (Drop(d)) dropped++;
            Physics.SyncTransforms();

            return "着地: 浮いていた " + dropped + "台を下ろした（角度は変えていない）";
        }

        /// <summary>真下にある面へ着地させる。浮いていなければ何もしない。</summary>
        static bool Drop(Transform d)
        {
            var r = d.GetComponentInChildren<Renderer>(true);
            if (r == null) return false;
            var b = r.bounds;

            float sup = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(b.center.x, b.min.y + 0.02f, b.center.z),
                                                 Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(d)) continue;
                if (h.point.y > sup) sup = h.point.y;
            }
            if (float.IsNegativeInfinity(sup)) return false;

            float gap = b.min.y - sup;
            if (gap <= 0.40f) return false;

            Undo.RecordObject(d, "drop locker");
            d.position = new Vector3(d.position.x, d.position.y - gap, d.position.z);
            EditorUtility.SetDirty(d);
            return true;
        }

        // ============================================================
        // 照明を広げた床に合わせて足す
        // ============================================================
        static string Lighting(Transform room)
        {
            var panels = room.Find("LightPanels");
            var lights = room.Find("Lights");
            if (panels == null || lights == null || panels.childCount == 0)
                return "照明: LightPanels / Lights が無い";

            // 元の並びの間隔をそのまま使う（4.0m x 3.2m）。引き伸ばすと暗い所ができる。
            var seedPanel = panels.GetChild(0);
            var seedLight = lights.childCount > 0 ? lights.GetChild(0) : null;

            var xs = new List<float>(); var zs = new List<float>();
            for (float x = -11.2f; x > WestOuter + 1.0f; x -= 4.0f) xs.Add(x);
            for (float z = -42.7f; z > SouthOuter + 1.0f; z -= 3.2f) zs.Add(z);

            int added = 0;
            foreach (float x in xs)
                foreach (float z in zs)
                {
                    if (Occupied(panels, x, z)) continue;

                    var p = Object.Instantiate(seedPanel.gameObject, panels);
                    p.name = "LightPanel_x" + x.ToString("F0") + "_z" + z.ToString("F0");
                    p.transform.position = new Vector3(x, seedPanel.position.y, z);
                    Undo.RegisterCreatedObjectUndo(p, "light panel");

                    if (seedLight != null)
                    {
                        var l = Object.Instantiate(seedLight.gameObject, lights);
                        l.name = "RoomLight_x" + x.ToString("F0") + "_z" + z.ToString("F0");
                        l.transform.position = new Vector3(x, seedLight.position.y, z);
                        Undo.RegisterCreatedObjectUndo(l, "room light");
                    }
                    added++;
                }

            return "照明: " + added + " 組を追加（合計 " + panels.childCount + " 組）";
        }

        static bool Occupied(Transform group, float x, float z)
        {
            foreach (Transform c in group)
                if (Mathf.Abs(c.position.x - x) < 1.0f && Mathf.Abs(c.position.z - z) < 1.0f) return true;
            return false;
        }
    }
}
