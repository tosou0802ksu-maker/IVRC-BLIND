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

        /// <summary>天井配管の元の配置を覚えておく所。</summary>
        const string PipesPath = "Assets/_BLIND/Editor/Room8_PipeOrigins.json";

        /// <summary>配管1本ぶんの長さ(m)。この刻みで敷き直す。</summary>
        const float PipeStep = 1.04f;

        /// <summary>余熱を持たせるロッカーの割合。全部だと熱源の意味が消える。</summary>
        const float WarmShare = 0.30f;

        /// <summary>サーモ役が見る層。</summary>
        const int LayerThermal = 22;

        /// <summary>ロッカーの「中身」に付ける名前。作り直すとき見分けるため。</summary>
        const string ContentsName = "ThermalContents";

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
            log.AppendLine(FixPipes(room));

            // ⚠️ ここで必ず同期する。次の物理がずれた位置で走ってしまう。
            Physics.SyncTransforms();
            log.AppendLine(Settle(room));
            log.AppendLine(AddLockers(room, AddCount));

            // ⚠️ ビジョン層 → 熱、の順。逆にすると差し替える相手がまだ居ない。
            log.AppendLine(BlindVisionBuilder.Build(new[] { "room8" }));
            log.AppendLine(Thermalize(room));

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

            // ⚠️ **控えより実体が少なくても、勝手に復元しないこと。**
            //    作者が動線を確かめるために手で消すことがある。
            //    自動で戻すと、消した本人と毎回けんかになる。控えは
            //    「元はどこにあったか」を思い出すためだけに使う。

            // ⚠️ 並び順で対応づけない。複製した個体は末尾に付くので番号がずれる。
            //    名前ごとの出現順で突き合わせる。
            var pool = new Dictionary<string, Queue<Transform>>();
            foreach (Transform c in group)
            {
                if (c.name.Contains(AddMark)) continue;
                if (!pool.TryGetValue(c.name, out var q)) { q = new Queue<Transform>(); pool[c.name] = q; }
                q.Enqueue(c);
            }

            float xMin = WestOuter + WallT, xMax = EastOuter - WallT;
            float zMin = SouthOuter + WallT, zMax = NorthOuter - WallT;

            int moved = 0, clamped = 0, pushed = 0;
            foreach (var o in org.items)
            {
                if (!pool.TryGetValue(o.name, out var q) || q.Count == 0) continue;
                var d = q.Dequeue();

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
                 + " 倍に広げ直した（角度は元のまま / 控え " + org.items.Count
                 + "件 / 壁際へ寄せた " + clamped + " / 入口前からどけた " + pushed + "）";
        }


        static Origins LoadOrigins()
        {
            if (!System.IO.File.Exists(OriginsPath)) return null;
            return JsonUtility.FromJson<Origins>(System.IO.File.ReadAllText(OriginsPath));
        }

        // ============================================================
        // 天井配管を新しい部屋に合わせ直す
        // ============================================================
        /// <summary>
        /// 天井の太い配管(Props_Pipes)を、広げた部屋の輪郭に張り直す。
        ///
        /// ⚠️ 部屋を広げたとき、ここを直し忘れた。配管は旧部屋の輪郭
        ///    （西 x -20.78 / 南 z -47.38）に貼り付いたままで、新しい部屋では
        ///    **何も無い空中で途切れ、立ち上がり管が部屋の真ん中に突っ立つ**（作者指摘）。
        ///    壁配管(Props_WallPipes_Generated)は生成器が作り直すので問題なかったが、
        ///    こちらは手置きなので誰も動かさない。
        ///
        /// ⚠️ 位置を 1.5 倍するだけでは駄目。配管は 1.04m の部品を並べたもので、
        ///    間隔まで 1.5 倍になると 0.5m ずつ隙間が空く。
        ///    **走りの両端だけを写して、その間を 1.04m 刻みで敷き直す。**
        /// </summary>
        static string FixPipes(Transform room)
        {
            var group = room.Find("Props_Pipes");
            if (group == null) return "天井配管: Props_Pipes が無い";

            var org = LoadPipes(group);
            if (org == null || org.items.Count == 0) return "天井配管: 控えが読めない";

            // 前回足した部品を消す
            var doomed = new List<GameObject>();
            foreach (Transform c in group) if (c.name.Contains(AddMark)) doomed.Add(c.gameObject);
            foreach (var g in doomed) Undo.DestroyObjectImmediate(g);

            // 控えの位置・角度へ戻す
            var kids = new List<Transform>();
            foreach (Transform c in group) kids.Add(c);
            int n = Mathf.Min(kids.Count, org.items.Count);
            for (int i = 0; i < n; i++)
            {
                Undo.RecordObject(kids[i], "fix pipes");
                kids[i].position = org.items[i].pos;
                kids[i].rotation = org.items[i].rot;
            }

            // 走り（同じ向き・同じ高さ・同じ横位置に並ぶ部品の列）にまとめる
            var runsX = new Dictionary<string, List<Transform>>();
            var runsZ = new Dictionary<string, List<Transform>>();
            var loose = new List<Transform>();
            for (int i = 0; i < n; i++)
            {
                var t = kids[i];
                var r = t.GetComponentInChildren<Renderer>(true);
                if (r == null) { loose.Add(t); continue; }
                var s = r.bounds.size;
                if (s.x > s.y && s.x > s.z && s.x > 0.8f)
                    Key(runsX, t.position.y, t.position.z).Add(t);
                else if (s.z > s.y && s.z > s.x && s.z > 0.8f)
                    Key(runsZ, t.position.y, t.position.x).Add(t);
                else
                    loose.Add(t);                      // 立ち上がり管・継手
            }

            int moved = 0, added = 0;
            foreach (var t in loose)
            {
                t.position = Map(t.position);
                EditorUtility.SetDirty(t); moved++;
            }
            foreach (var kv in runsX) Retile(kv.Value, group, true, ref moved, ref added);
            foreach (var kv in runsZ) Retile(kv.Value, group, false, ref moved, ref added);

            Physics.SyncTransforms();
            return "天井配管: 走り " + (runsX.Count + runsZ.Count) + "本を張り直した（動かした "
                 + moved + " / 継ぎ足した " + added + "）";
        }

        static List<Transform> Key(Dictionary<string, List<Transform>> d, float a, float b)
        {
            string k = a.ToString("F2") + "|" + b.ToString("F2");
            if (!d.TryGetValue(k, out var l)) { l = new List<Transform>(); d[k] = l; }
            return l;
        }

        /// <summary>旧部屋の内側を新部屋の内側へ写す。動かさない北東の内角が基準。</summary>
        static Vector3 Map(Vector3 p)
        {
            return new Vector3(AnchorX + (p.x - AnchorX) * Spread,
                               p.y,
                               AnchorZ + (p.z - AnchorZ) * Spread);
        }

        /// <summary>走りの両端を写して、その間を 1.04m 刻みで敷き直す。</summary>
        static void Retile(List<Transform> run, Transform group, bool alongX, ref int moved, ref int added)
        {
            if (run.Count == 0) return;
            var seed = run[0];

            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var t in run)
            {
                float v = alongX ? t.position.x : t.position.z;
                lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            }
            var pLo = Map(alongX ? new Vector3(lo, seed.position.y, seed.position.z)
                                 : new Vector3(seed.position.x, seed.position.y, lo));
            var pHi = Map(alongX ? new Vector3(hi, seed.position.y, seed.position.z)
                                 : new Vector3(seed.position.x, seed.position.y, hi));

            float a = alongX ? pLo.x : pLo.z, b = alongX ? pHi.x : pHi.z;
            int count = Mathf.Max(1, Mathf.RoundToInt((b - a) / PipeStep) + 1);

            for (int i = 0; i < count; i++)
            {
                float v = a + i * PipeStep;
                Transform t;
                if (i < run.Count) { t = run[i]; moved++; }
                else
                {
                    var go = Object.Instantiate(seed.gameObject, group);
                    go.name = seed.name + AddMark;
                    Undo.RegisterCreatedObjectUndo(go, "pipe");
                    t = go.transform; added++;
                }
                t.rotation = seed.rotation;
                t.position = alongX ? new Vector3(v, pLo.y, pLo.z)
                                    : new Vector3(pLo.x, pLo.y, v);
                EditorUtility.SetDirty(t);
            }

            // 余った部品は消さずに列の末尾へ寄せる（他人の置いた物を消さない）
            for (int i = count; i < run.Count; i++)
            {
                float v = a + (count - 1) * PipeStep;
                run[i].position = alongX ? new Vector3(v, pLo.y, pLo.z)
                                         : new Vector3(pLo.x, pLo.y, v);
                EditorUtility.SetDirty(run[i]); moved++;
            }
        }

        static Origins LoadPipes(Transform group)
        {
            if (System.IO.File.Exists(PipesPath))
                return JsonUtility.FromJson<Origins>(System.IO.File.ReadAllText(PipesPath));

            var save = new Origins();
            foreach (Transform c in group)
                save.items.Add(new Origin { name = c.name, pos = c.position, rot = c.rotation });
            System.IO.File.WriteAllText(PipesPath, JsonUtility.ToJson(save, true));
            AssetDatabase.ImportAsset(PipesPath);
            return save;
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

        // ============================================================
        // サーモ役の画面に見るものを作る
        // ============================================================
        /// <summary>
        /// 配管を熱源に、一部のロッカーを余熱にして、時間で脈打たせる。
        ///
        /// room8 は通り抜けるだけの部屋で中身は金属ロッカーばかり。全部が同じ冷たさだと
        /// サーモ役の画面は「灰色の塊が並んだ静止画」で、一度見たら以降は情報が増えない。
        /// 通るだけの部屋こそ、待っている間に眺めるものが要る。
        ///
        /// ⚠️ **ビジョン層を作ったあとに呼ぶこと。** サーモ層の複製が無いと対象が見つからない。
        /// ⚠️ マテリアルは共有アセット。ここでは差し替えるだけで、揺らぎ自体は
        ///    ThermalBodyDrift が MaterialPropertyBlock でレンダラー単位に書く。
        ///    直接書き換えると全部屋に波及する。
        /// </summary>
        static string Thermalize(Transform room)
        {
            var hot  = BlindThermalTable.Mat("PipeHot");
            var warm = BlindThermalTable.Mat("LockerWarm");
            if (hot == null || warm == null) return "熱: 温度段 PipeHot / LockerWarm が無い";

            int nPipe = 0, nLocker = 0;

            // --- 配管を熱源に ---
            foreach (var nm in new[] { "Props_Pipes", "Props_WallPipes_Generated" })
            {
                var g = room.Find(nm);
                if (g == null) continue;
                foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.gameObject.layer != LayerThermal) continue;
                    // 配線(Cable)は電線なので熱くしない。管だけ。
                    if (r.name.Contains("Cable")) continue;
                    r.sharedMaterial = hot;
                    EditorUtility.SetDirty(r); nPipe++;
                }
            }

            // --- 一部のロッカーに「中に何か入っている」熱を作る ---
            //
            // ⚠️ **ロッカーをまるごと暖めてはいけない。** 箱全体が均一に光ると
            //    「熱いロッカー」にしかならず、中身の気配が出ない（作者指摘）。
            //    本物のサーモでは、中の物が接している面だけが温まる。
            //    扉板を余熱にして、中身を扉からわずかに出した塊で示す。
            // ⚠️ 乱数で選ばない。作り直すたびに中身入りが入れ替わると、
            //    サーモ役が覚えた並びが毎回変わって手がかりにならない。名前から決める。
            var body = BlindThermalTable.Mat("Body");
            if (body == null) body = warm;

            var lockers = room.Find("Props_Lockers");
            int nInside = 0;
            if (lockers != null)
            {
                foreach (Transform d in lockers)
                {
                    // 前回置いた中身を消す（これが無いと実行のたびに増える）
                    var old = d.Find(ContentsName);
                    if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

                    // まず全部そのまま（冷たい金属）に戻す
                    foreach (var r in d.GetComponentsInChildren<MeshRenderer>(true))
                        if (r.gameObject.layer == LayerThermal && r.sharedMaterial == warm)
                            r.sharedMaterial = BlindThermalTable.Mat("Metal");

                    uint h = 2166136261u;
                    foreach (char c in d.name) { h ^= c; h *= 16777619u; }
                    if ((h % 1000u) >= (uint)(WarmShare * 1000f)) continue;

                    // 扉板だけ余熱に
                    var door = FindDoor(d);
                    if (door == null) continue;
                    foreach (var r in door.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (r.gameObject.layer != LayerThermal) continue;
                        r.sharedMaterial = warm;
                        EditorUtility.SetDirty(r); nLocker++;
                    }

                    if (MakeContents(d, door, body)) nInside++;
                }
            }

            // 揺らぎを繋ぎ直す（材質を変えたので集め直しが要る）
            string drift = BlindVisionBuilder.WireBodyDrift();

            return "熱: 配管 " + nPipe + "枚を熱源(46℃) / 扉 " + nLocker
                 + "枚に余熱(27.5℃) / 中身 " + nInside + "体\n" + drift;
        }

        /// <summary>ロッカーの扉板を探す。無ければ null。</summary>
        static Transform FindDoor(Transform locker)
        {
            foreach (var t in locker.GetComponentsInChildren<Transform>(true))
            {
                if (t == locker) continue;
                if (t.name.ToLower() != "door") continue;
                if (t.GetComponent<MeshRenderer>() == null) continue;
                return t;
            }
            return null;
        }

        /// <summary>
        /// 扉のすぐ内側に「中身」を置く。扉面からわずかに出しておく。
        ///
        /// ⚠️ サーマルは不透明なので、完全に中へ入れると何も見えない。
        ///    扉板を数cm突き抜けさせて、丸い熱の染みとして読ませる。
        /// ⚠️ 倒れたロッカーもあるので、上下は扉のローカル軸で決める。
        ///    ワールドのY基準にすると、横倒しの個体で中身が横から飛び出す。
        /// </summary>
        static bool MakeContents(Transform locker, Transform door, Material mat)
        {
            var dr = door.GetComponent<MeshRenderer>();
            var lr = locker.GetComponent<MeshRenderer>();
            if (dr == null || lr == null) return false;

            // 扉の外向き＝ロッカー中心から扉中心へ
            var outward = dr.bounds.center - lr.bounds.center;
            if (outward.sqrMagnitude < 1e-6f) return false;
            outward.Normalize();

            // 扉板の面内で長い方／短い方を測る（倒れていても扉のローカル軸で取る）
            var ds = door.localScale;
            var mesh = door.GetComponent<MeshFilter>();
            var local = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.bounds.size : Vector3.one;
            var face = new Vector3(local.x * ds.x, local.y * ds.y, local.z * ds.z);

            // 一番薄い軸が板の厚み。残り2軸が扉面。
            int thin = face.x < face.y ? (face.x < face.z ? 0 : 2) : (face.y < face.z ? 1 : 2);
            float a = thin == 0 ? face.y : face.x;
            float b = thin == 2 ? face.y : face.z;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Undo.RegisterCreatedObjectUndo(go, "locker contents");
            go.name = ContentsName;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.layer = LayerThermal;
            go.transform.SetParent(locker, false);

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // 扉面の 55% ほどの大きさ。潰した楕円にして人の胴らしく見せる。
            float w = Mathf.Min(a, b) * 0.55f;
            float hgt = Mathf.Max(a, b) * 0.42f;
            go.transform.rotation = door.rotation;
            var ls = locker.lossyScale;
            go.transform.localScale = new Vector3(w / Mathf.Max(0.0001f, Mathf.Abs(ls.x)),
                                                  hgt / Mathf.Max(0.0001f, Mathf.Abs(ls.y)),
                                                  w / Mathf.Max(0.0001f, Mathf.Abs(ls.z)));

            // ⚠️ **扉面ではなくロッカーの外形から突き出させること。**
            //    扉は箱に対して引っ込んでいるので、扉から3cm出しただけでは
            //    箱の中に埋もれたままになる。実測で 30個中27個が完全に埋もれて
            //    サーモに何も映っていなかった。
            //    いったん置いてから実際の大きさを測り、外形＋突き出し量まで押し出す。
            const float Protrude = 0.05f;
            go.transform.position = dr.bounds.center;

            var n = outward;
            int ax = 0; float big = Mathf.Abs(n.x);
            if (Mathf.Abs(n.y) > big) { ax = 1; big = Mathf.Abs(n.y); }
            if (Mathf.Abs(n.z) > big) { ax = 2; }
            float sgn = ax == 0 ? Mathf.Sign(n.x) : ax == 1 ? Mathf.Sign(n.y) : Mathf.Sign(n.z);

            var lb = lr.bounds;
            float faceEdge = ax == 0 ? (sgn > 0 ? lb.max.x : lb.min.x)
                       : ax == 1 ? (sgn > 0 ? lb.max.y : lb.min.y)
                                 : (sgn > 0 ? lb.max.z : lb.min.z);

            var sb2 = mr.bounds;                       // 置いた状態の実寸
            float cur = ax == 0 ? (sgn > 0 ? sb2.max.x : sb2.min.x)
                      : ax == 1 ? (sgn > 0 ? sb2.max.y : sb2.min.y)
                                : (sgn > 0 ? sb2.max.z : sb2.min.z);
            float want = faceEdge + sgn * Protrude;
            var shift = Vector3.zero;
            if (ax == 0) shift.x = want - cur; else if (ax == 1) shift.y = want - cur; else shift.z = want - cur;
            go.transform.position += shift;

            EditorUtility.SetDirty(go);
            return true;
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

