using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 部屋の Default レイヤーの見た目から、サーモ役(22)とエコロケ役(23)用の
    /// 見た目レイヤーを生成する。
    ///
    /// 配置手順.md のとおり、この3役は互いに相手のレイヤーが一切見えない。
    /// つまり部屋を作っただけではサーモ役とエコロケ役には何も見えていない。
    /// ここで実体と同じ形のメッシュを 22 / 22 の2枚焼き足す。
    ///
    /// 1レンダラーにつき1個ずつ複製すると room12 だけで 600 個増えるので、
    /// 温度クラスごと・空間ブロックごとにメッシュを結合してから置く。
    /// エコロケはブロック単位で光る方が、パルスが広がっていく感じが出て都合もよい。
    /// </summary>
    public static class BlindVisionBuilder
    {
        public const int LayerThermal = 22;
        public const int LayerEcho = 23;

        const string EchoMatPath = "Assets/_BLIND/Art/Materials/EchoMaterial.mat";
        const string EchoGroup = "Vision_Echo";
        const string ThermalGroup = "Vision_Thermal";
        const string MeshDir = "Assets/_BLIND/Art/Models/VisionMeshes";

        /// <summary>エコロケのブロックの大きさ(m)。小さいほどパルスが細かく広がる。</summary>
        const float EchoChunk = 5f;

        /// <summary>生成した EchoReceiver の点灯時間(秒)。</summary>
        const float EchoGlowDuration = 2.5f;

        /// <summary>
        /// これを超える三角形数のメッシュは簡易形状に差し替える。
        /// サーモは「その物が何度か」、エコロケは「どこに何があるか」しか伝えないので、
        /// 人形の指1本まで再現しても情報は増えず、ポリゴンだけが3倍になる。
        /// </summary>
        const int MaxPropTris = 260;

        static Mesh _boxProxy, _blobProxy;

        /// <summary>
        /// エコロケ用にUVを貼り直したメッシュを作る。
        ///
        /// EchoHighlight は「UVが0か1に近い所＝輪郭」として光らせる。ところが床タイルや
        /// 手続き生成した書架のUVは実寸スケール(0〜1に収まらない)なので、そのまま渡すと
        /// 面全体が輪郭と判定されてベタ塗りになる。
        /// ここで各頂点を、その物自身のバウンディングボックスに対する 0〜1 に投影し直す。
        /// 法線の一番強い軸を面の向きとみなして、残り2軸をUVに使う（箱投影）。
        /// 結果として「物ごとの外形が線で出る」という狙いどおりの見え方になる。
        /// </summary>
        static Mesh EchoUv(Mesh src)
        {
            var m = Object.Instantiate(src);
            m.name = src.name + "_echoUV";
            var v = m.vertices;
            var nr = m.normals;
            var b = m.bounds;
            var size = new Vector3(
                Mathf.Max(b.size.x, 1e-4f), Mathf.Max(b.size.y, 1e-4f), Mathf.Max(b.size.z, 1e-4f));
            var uv = new Vector2[v.Length];
            bool hasN = nr != null && nr.Length == v.Length;
            for (int i = 0; i < v.Length; i++)
            {
                var p = new Vector3((v[i].x - b.min.x) / size.x, (v[i].y - b.min.y) / size.y, (v[i].z - b.min.z) / size.z);
                var n = hasN ? nr[i] : Vector3.up;
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                if (ax >= ay && ax >= az) uv[i] = new Vector2(p.z, p.y);       // X向きの面
                else if (ay >= az) uv[i] = new Vector2(p.x, p.z);              // Y向きの面（床・天井）
                else uv[i] = new Vector2(p.x, p.y);                            // Z向きの面
            }
            m.uv = uv;
            return m;
        }

        /// <summary>単位立方体（原点中心、1m角）。角ばった物の代用。</summary>
        static Mesh BoxProxy()
        {
            if (_boxProxy != null) return _boxProxy;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boxProxy = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            _boxProxy.name = "VisionProxy_Box";
            Object.DestroyImmediate(go);
            return _boxProxy;
        }

        /// <summary>低ポリの球（原点中心、直径1m）。だるま・人形など丸い物の代用。</summary>
        static Mesh BlobProxy()
        {
            if (_blobProxy != null) return _blobProxy;
            const int seg = 10, ring = 6;
            var v = new List<Vector3>(); var tri = new List<int>(); var uv = new List<Vector2>();
            for (int y = 0; y <= ring; y++)
            {
                float phi = Mathf.PI * y / ring;
                for (int x = 0; x <= seg; x++)
                {
                    float th = 2f * Mathf.PI * x / seg;
                    v.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)) * 0.5f);
                    uv.Add(new Vector2((float)x / seg, (float)y / ring));
                }
            }
            for (int y = 0; y < ring; y++)
                for (int x = 0; x < seg; x++)
                {
                    int a = y * (seg + 1) + x, b = a + seg + 1;
                    tri.Add(a); tri.Add(b); tri.Add(a + 1);
                    tri.Add(a + 1); tri.Add(b); tri.Add(b + 1);
                }
            _blobProxy = new Mesh { name = "VisionProxy_Blob" };
            _blobProxy.SetVertices(v); _blobProxy.SetUVs(0, uv); _blobProxy.SetTriangles(tri, 0);
            _blobProxy.RecalculateNormals(); _blobProxy.RecalculateBounds();
            return _blobProxy;
        }

        /// <summary>
        /// 実測のバウンズに合わせた簡易形状(箱 or 楕円体)の CombineInstance を返す。
        /// echoUv=true なら UV を 0〜1 に貼り直した複製を使う（呼び側で破棄すること）。
        /// </summary>
        /// <summary>
        /// 楕円体で代用してよいか。縦横比だけで決めると立方体に近い箱（CRTモニタなど）まで
        /// 球にされてしまうので、まず「丸い物として設計されたクラスか」で振り分ける。
        /// </summary>
        static bool IsRoundish(string key, Bounds b)
        {
            if (key != "Prop" && key != "Body" && key != "Skin") return false;
            float mx = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float mn = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
            if (mx > 1.5f) return false;               // 大きい物を球で近似すると誤差が数mになる
            return mn / Mathf.Max(mx, 1e-4f) > 0.6f;   // だるまは丸い、立っている人形は細長いので箱
        }

        /// <summary>
        /// 簡易形状で置き換えてはいけない大きさか。
        ///
        /// 簡易形状の誤差は物の寸法に比例する。棚(0.6m厚)を箱にしても誤差は数cmだが、
        /// room7 の巨大アヒル(8.8×7.5×8.0m)を球にすると、直径8mの球が
        /// プール室のほぼ全体を埋めてしまう。サーモ・エコロケとも不透明なので、
        /// 中に入った側からは「胸の高さから上が何も見えない」状態になる。
        /// この大きさの物は素のメッシュをそのまま使う（1体につきドローコール1個）。
        /// </summary>
        static bool IsBig(Bounds b)
        {
            float mx = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float mn = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
            // 2.5m = プレイヤーが「中に入れてしまう」大きさ。2m のアヒルを球にしても
            // 部屋は塞がらないので、そこまでは今までどおり簡易形状のままにする。
            return mn >= 2.5f || mx >= 5.0f;
        }

        static CombineInstance ProxyInstance(Renderer mr, Transform room, bool echoUv, string key)
        {
            var b = mr.bounds;
            var src = IsRoundish(key, b) ? BlobProxy() : BoxProxy();
            var ci = new CombineInstance
            {
                mesh = echoUv ? EchoUv(src) : src,
                subMeshIndex = 0,
                transform = room.worldToLocalMatrix * Matrix4x4.TRS(
                    b.center, Quaternion.identity,
                    new Vector3(Mathf.Max(b.size.x, 0.02f), Mathf.Max(b.size.y, 0.02f), Mathf.Max(b.size.z, 0.02f))),
            };
            return ci;
        }

        /// <summary>
        /// 元のレンダラーを、そのまま使うか簡易形状に置き換えるかを決めて CombineInstance を返す。
        /// 読み取り不可(Read/Write Enabled が off)のメッシュもここで簡易形状に逃がす。
        /// </summary>
        static bool MakeInstance(Renderer mr, Transform room, string key, out bool standalone, out CombineInstance ci)
        {
            ci = default;
            standalone = false;
            var mf = mr.GetComponent<MeshFilter>();
            var src = mf != null ? mf.sharedMesh : null;
            bool readable = src != null && src.isReadable;
            int tris = readable ? src.triangles.Length / 3 : int.MaxValue;

            if (readable && tris <= MaxPropTris)
            {
                ci.mesh = src;
                ci.subMeshIndex = 0;
                ci.transform = room.worldToLocalMatrix * mr.transform.localToWorldMatrix;
                return true;
            }

            // 実測のバウンズに合わせて簡易形状を置く
            var b = mr.bounds;
            if (b.size.sqrMagnitude < 1e-6f) return false;

            // ただし簡易形状にすると部屋を塞いでしまう大物は、素のメッシュを
            // 1枚そのまま置く（結合できないので単独のレンダラーになる）
            if (src != null && IsBig(b)) { standalone = true; return false; }
            ci.mesh = IsRoundish(key, b) ? BlobProxy() : BoxProxy();
            ci.subMeshIndex = 0;
            var local = room.worldToLocalMatrix * Matrix4x4.TRS(b.center, Quaternion.identity, b.size);
            ci.transform = local;
            return true;
        }

        // -------------------------------------------------------------
        //  分類：レンダラーの名前とマテリアル名から温度クラスを決める
        // -------------------------------------------------------------
        /// <summary>戻り値が null ならその物はサーモ／エコロケに出さない。</summary>
        static string Classify(Renderer r, out bool echo)
        {
            echo = true;
            var go = r.gameObject;
            string n = go.name;
            string p = go.transform.parent != null ? go.transform.parent.name : "";
            string mat = r.sharedMaterial != null ? r.sharedMaterial.name : "";
            string all = (n + "|" + p + "|" + mat).ToLower();

            // --- 焼け跡・汚れなどの貼り付け表現は出さない ---
            // 床に密着したゼロ厚のポリゴンなので、そのまま複製すると床と同じ高さに
            // 2枚重なって Zファイティング（チラつき）を起こす。汚れには温度も反響も無い。
            if (all.Contains("decal") || all.Contains("stain") || all.Contains("soot")) { echo = false; return null; }

            // --- 発熱するもの（サーモ役の道しるべになる） ---
            if (n == "Lens" || all.Contains("lamplens")) return "Lamp";
            if (n == "Housing" || all.Contains("lamphousing")) return "Ballast";
            if (all.Contains("fluoro")) return "Lamp";
            // 天井の照明パネルは On / Dim / Off の3種がある。生きている物だけが熱い＝
            // サーモ役には「どの列の灯りが生きているか」が読める
            if (all.Contains("lightpanel") || all.Contains("light_panel"))
            {
                if (all.Contains("_off")) return "LampOff";
                if (all.Contains("_dim")) return "LampDim";
                return "Lamp";
            }

            // --- CRT モニタ ---
            if (all.Contains("crt") || all.Contains("screenshards") || all.Contains("screenoff") || all.Contains("screenon"))
                return "CRTOff";   // このワールドは停電しているので通電中の熱は出ない

            // --- 部屋の面 ---
            // 「濡れ注意」の看板は floor を含むが床ではないので先に除ける
            if (all.Contains("sign")) return "Prop";
            // room7 の PoolDeck は名前に反して「水面」そのもの。実機のサーマルでは
            // まとまった水は蒸発熱で室温より数℃低く、画面の中で一番はっきりした冷たい面になる
            if (all.Contains("pooldeck") || all.Contains("water")) return "Water";
            if (all.Contains("basin")) return "FloorStone";
            if (all.Contains("floortile") || n.StartsWith("Floor") || all.Contains("floor")) return "FloorStone";
            if (all.Contains("ceiling") || all.Contains("plenum")) return "Ceiling";
            if (all.Contains("wall") || all.Contains("dado") || all.Contains("doorframe")
                || all.Contains("plaster") || all.Contains("trim")) return "Wall";

            // --- 什器 ---
            if (all.Contains("duct") || all.Contains("conduit") || all.Contains("hanger")) return "Duct";
            if (all.Contains("rack") || all.Contains("steel") || all.Contains("desk") || all.Contains("chair")
                || all.Contains("locker") || all.Contains("scaffold") || all.Contains("ladder")) return "Metal";
            if (all.Contains("box") || all.Contains("cardboard") || all.Contains("archivebox")) return "Cardboard";
            if (all.Contains("barrel") || all.Contains("plank") || all.Contains("wood") || all.Contains("bookshelf")
                || all.Contains("drawer") || all.Contains("table") || all.Contains("crate")) return "Wood";
            if (all.Contains("rug") || all.Contains("carpet") || all.Contains("cloth") || all.Contains("fabric")) return "Fabric";

            // --- 小物 ---
            if (all.Contains("daruma") || all.Contains("doll") || all.Contains("mannequin") || all.Contains("killerdoll"))
                return "Prop";

            return "Prop";
        }

        // -------------------------------------------------------------
        [MenuItem("BLIND/vision/2. 担当部屋にサーモ・エコロケを生成")]
        public static string BuildMine()
        {
            return Build(new[] { "room2", "room7", "room9", "room12", "room15", "room16" });
        }

        public static string Build(string[] roomNames)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Models", "VisionMeshes");

            var echoMat = AssetDatabase.LoadAssetAtPath<Material>(EchoMatPath);
            if (echoMat == null) return "EchoMaterial.mat が無い";

            var log = new System.Text.StringBuilder();
            long totalTris = 0; int totalRend = 0;

            foreach (var rn in roomNames)
            {
                var room = FindRoom(rn);
                if (room == null) { log.AppendLine(rn + ": 見つからない"); continue; }

                // 既存の生成物を消す
                foreach (var g in new[] { EchoGroup, ThermalGroup })
                { var old = room.Find(g); if (old != null) Object.DestroyImmediate(old.gameObject); }

                // --- 元になるレンダラーを集めて分類 ---
                var byTemp = new Dictionary<string, List<CombineInstance>>();
                var byChunk = new Dictionary<Vector3Int, List<CombineInstance>>();
                var temps = new List<Mesh>();   // UV貼り直し用の一時メッシュ。最後に破棄する
                int skipped = 0, used = 0;
                var floorBounds = new Bounds(); bool hasFloor = false;

                int proxied = 0;
                var bigOnes = new List<KeyValuePair<Renderer, string>>();
                foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!mr.gameObject.activeInHierarchy) continue;
                    if (mr.gameObject.layer == LayerThermal || mr.gameObject.layer == LayerEcho) continue;

                    bool echo;
                    var key = Classify(mr, out echo);
                    if (key == null) { skipped++; continue; }

                    CombineInstance ci; bool standalone;
                    if (!MakeInstance(mr, room, key, out standalone, out ci))
                    {
                        if (standalone) { bigOnes.Add(new KeyValuePair<Renderer, string>(mr, key)); used++; }
                        else skipped++;
                        continue;
                    }
                    if (ci.mesh == _boxProxy || ci.mesh == _blobProxy) proxied++;

                    if (!byTemp.ContainsKey(key)) byTemp[key] = new List<CombineInstance>();
                    byTemp[key].Add(ci);

                    if (echo)
                    {
                        // 床は 2m角のタイルが何十枚も並んでいる。EchoHighlight は
                        // 小さい面ほど輪郭が太く出る(fwidth基準)ので、タイルのままだと
                        // 一枚ごとに全面が輪郭判定になって床がベタ塗りになる。
                        // 配置手順.md のとおり、床は部屋につき大きな板1枚にまとめる。
                        // 水面もエコロケ上は「そこが床の高さ」を意味するので床板にまとめる
                        if (key == "FloorStone" || key == "Water")
                        {
                            if (!hasFloor) { floorBounds = mr.bounds; hasFloor = true; }
                            else floorBounds.Encapsulate(mr.bounds);
                        }
                        else
                        {
                            var lp = room.InverseTransformPoint(mr.bounds.center);
                            var cell = new Vector3Int(Mathf.FloorToInt(lp.x / EchoChunk), 0, Mathf.FloorToInt(lp.z / EchoChunk));
                            if (!byChunk.ContainsKey(cell)) byChunk[cell] = new List<CombineInstance>();

                            // エコロケ層は必ず簡易形状で組む。
                            // EchoHighlight は「UVの端＝輪郭」で線を引くため、元メッシュのUVが
                            // 0〜1でない（床タイル・手続き生成の書架・FBXの実寸UVなど）と
                            // 面全体が輪郭と判定されてベタ塗りになる。箱と楕円体なら
                            // 必ず面ごとに0〜1が入るので、どんな元データでも輪郭が出る。
                            // 反響定位はもともと形の輪郭しか分からない設定なので情報も失われない。
                            var eci = ProxyInstance(mr, room, true, key);
                            temps.Add(eci.mesh);
                            byChunk[cell].Add(eci);
                        }
                    }
                    used++;
                }

                // --- サーモ層 ---
                var tRoot = new GameObject(ThermalGroup);
                tRoot.transform.SetParent(room, false);
                long tTris = 0; int tRend = 0;
                foreach (var kv in byTemp)
                {
                    var mesh = Combine(kv.Value, rn + "_Thermal_" + kv.Key);
                    if (mesh == null) continue;
                    var mat = BlindThermalTable.Mat(kv.Key);
                    if (mat == null) continue;
                    var go = new GameObject("T_" + kv.Key + " (" + BlindThermalTable.Get(kv.Key).celsius.ToString("0.0") + "C)");
                    go.transform.SetParent(tRoot.transform, false);
                    go.layer = LayerThermal;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr2 = go.AddComponent<MeshRenderer>();
                    mr2.sharedMaterial = mat;
                    mr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr2.receiveShadows = false;
                    mr2.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    mr2.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    Recenter(mesh, go.transform);
                    tTris += mesh.triangles.Length / 3; tRend++;
                }
                // 簡易形状にすると部屋を塞ぐ大物は、素のメッシュを1枚だけ置く
                foreach (var kv in bigOnes)
                {
                    var bm = BlindThermalTable.Mat(kv.Value);
                    var go = CloneReal(kv.Key, tRoot.transform, LayerThermal, bm, "T_");
                    if (go == null) continue;
                    tTris += TriCount(kv.Key); tRend++;
                }

                // --- エコロケ層 ---
                var eRoot = new GameObject(EchoGroup);
                eRoot.transform.SetParent(room, false);
                long eTris = 0; int eRend = 0;

                // 床は薄い板にする。ただし部屋に1枚だとパルスで床全体が一度に光ってしまい、
                // 「反響が広がっていく」感じが出ないので、ブロックと同じ大きさに割る。
                if (hasFloor)
                {
                    int nx = Mathf.Max(1, Mathf.RoundToInt(floorBounds.size.x / EchoChunk));
                    int nz = Mathf.Max(1, Mathf.RoundToInt(floorBounds.size.z / EchoChunk));
                    float sx = floorBounds.size.x / nx, sz = floorBounds.size.z / nz;
                    for (int ix = 0; ix < nx; ix++)
                        for (int iz = 0; iz < nz; iz++)
                        {
                            var slab = Object.Instantiate(BoxProxy());
                            var uvSlab = EchoUv(slab);
                            var center = new Vector3(
                                floorBounds.min.x + sx * (ix + 0.5f),
                                floorBounds.max.y - 0.02f,
                                floorBounds.min.z + sz * (iz + 0.5f));
                            var ci2 = new CombineInstance
                            {
                                mesh = uvSlab,
                                subMeshIndex = 0,
                                transform = room.worldToLocalMatrix * Matrix4x4.TRS(
                                    center, Quaternion.identity, new Vector3(sx, 0.04f, sz)),
                            };
                            var fm = Combine(new List<CombineInstance> { ci2 }, rn + "_Echo_Floor_" + ix + "_" + iz);
                            Object.DestroyImmediate(slab);
                            Object.DestroyImmediate(uvSlab);
                            if (fm == null) continue;

                            var fg = new GameObject("E_Floor_" + ix + "_" + iz);
                            fg.transform.SetParent(eRoot.transform, false);
                            fg.layer = LayerEcho;
                            fg.AddComponent<MeshFilter>().sharedMesh = fm;
                            var fmr = fg.AddComponent<MeshRenderer>();
                            fmr.sharedMaterial = echoMat;
                            fmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            fmr.receiveShadows = false;
                            fmr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                            fmr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                            Recenter(fm, fg.transform);
                            AddReceiver(fg, fmr);
                            eTris += fm.triangles.Length / 3; eRend++;
                        }
                }
                foreach (var kv in byChunk)
                {
                    var mesh = Combine(kv.Value, rn + "_Echo_" + kv.Key.x + "_" + kv.Key.z);
                    if (mesh == null) continue;
                    var go = new GameObject("E_" + kv.Key.x + "_" + kv.Key.z);
                    go.transform.SetParent(eRoot.transform, false);
                    go.layer = LayerEcho;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr2 = go.AddComponent<MeshRenderer>();
                    mr2.sharedMaterial = echoMat;
                    mr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr2.receiveShadows = false;
                    mr2.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    mr2.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                    Recenter(mesh, go.transform);
                    AddReceiver(go, mr2);
                    eTris += mesh.triangles.Length / 3; eRend++;
                }
                foreach (var kv in bigOnes)
                {
                    var em = EchoMatFor(kv.Key, rn, echoMat);
                    var go = CloneReal(kv.Key, eRoot.transform, LayerEcho, em, "E_");
                    if (go == null) continue;
                    AddReceiver(go, go.GetComponent<MeshRenderer>());
                    eTris += TriCount(kv.Key); eRend++;
                }

                foreach (var tm in temps) if (tm != null) Object.DestroyImmediate(tm);

                log.AppendLine(rn.PadRight(8)
                    + " 元 " + used.ToString().PadLeft(4) + " 個(簡易化 " + proxied + " / 除外 " + skipped + ")"
                    + " → サーモ " + tRend + " 枚/" + tTris.ToString("N0") + "tri"
                    + " / エコロケ " + eRend + " 枚/" + eTris.ToString("N0") + "tri"
                    + "  温度: " + string.Join(", ", Keys(byTemp)));
                totalTris += tTris + eTris; totalRend += tRend + eRend;
            }

            AssetDatabase.SaveAssets();
            log.AppendLine("\n合計 " + totalRend + " レンダラー / " + totalTris.ToString("N0") + " tri を追加");
            return log.ToString();
        }

        const string EchoBigDir = "Assets/_BLIND/Art/Materials/Echo";

        /// <summary>
        /// 大物を「素のメッシュのまま」視覚レイヤーに1枚置く。結合はしない。
        /// 元の親の下に一度作ってローカルTRSをそのまま写してから付け替えるので、
        /// 親側に回転やスケールが掛かっていても位置がずれない。
        /// </summary>
        static GameObject CloneReal(Renderer src, Transform parent, int layer, Material mat, string prefix)
        {
            var mf = src.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mat == null) return null;

            var go = new GameObject(prefix + src.name);
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition;
            go.transform.localRotation = src.transform.localRotation;
            go.transform.localScale = src.transform.localScale;
            go.transform.SetParent(parent, true);
            go.layer = layer;

            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(mf.sharedMesh.subMeshCount, 1)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>読めないメッシュは三角形数を数えられないので頂点数で概算する。</summary>
        static long TriCount(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            var m = mf != null ? mf.sharedMesh : null;
            if (m == null) return 0;
            return m.isReadable ? m.triangles.Length / 3 : m.vertexCount;
        }

        /// <summary>
        /// 大物用のエコロケ材質。
        /// 素のメッシュのUVは 0〜1 に収まっていないので、そのまま EchoHighlight に渡すと
        /// 面全体が輪郭と判定されてベタ塗りになる。Read/Write が off だと CPU で
        /// 貼り直せないため、シェーダー側のオブジェクト空間箱投影に切り替える。
        /// 投影に使うバウンズはメッシュごとに決まるので、マテリアルもメッシュ単位で作る。
        /// </summary>
        static Material EchoMatFor(Renderer src, string room, Material baseMat)
        {
            var mf = src.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return null;
            if (!AssetDatabase.IsValidFolder(EchoBigDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Materials", "Echo");

            var mesh = mf.sharedMesh;
            var safe = mesh.name;
            foreach (var c in new[] { '/', '\\', ' ', '(', ')', ':', '*', '?', '"', '<', '>', '|' })
                safe = safe.Replace(c, '_');
            var path = EchoBigDir + "/Echo_" + room + "_" + safe + ".mat";

            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(baseMat); AssetDatabase.CreateAsset(m, path); }
            m.shader = baseMat.shader;
            m.CopyPropertiesFromMaterial(baseMat);
            var b = mesh.bounds;   // bounds は Read/Write が off でも読める
            m.SetFloat("_UseObjectUv", 1f);
            m.SetVector("_ObjMin", b.min);
            m.SetVector("_ObjSize", b.size);
            // 箱投影だけだと有機的な形（アヒル・腕）にはほとんど線が出ないので
            // シルエットを足す。反響定位で返るのは外形なので理屈にも合う。
            m.SetFloat("_RimWeight", 1f);
            m.SetFloat("_RimPower", 3.5f);
            m.SetFloat("_GlowIntensity", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>EchoReceiver を付けて自分自身を対象にする。</summary>
        static void AddReceiver(GameObject go, Renderer r)
        {
            var rt = System.Type.GetType("EchoReceiver, Assembly-CSharp");
            if (rt == null) return;
            var rec = go.AddComponent(rt);
            var so = new SerializedObject(rec);
            var arr = so.FindProperty("targetRenderers");
            if (arr != null) { arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = r; }
            var gd = so.FindProperty("glowDuration");
            if (gd != null) gd.floatValue = EchoGlowDuration;
            so.ApplyModifiedProperties();
        }

        /// <summary>
        /// メッシュを自分の重心へ寄せ直し、その分を Transform に持たせる。
        ///
        /// 結合したメッシュは部屋のローカル原点を基準に作られるので、そのまま置くと
        /// GameObject の座標が全ブロック「部屋の原点」になってしまう。
        /// EchoEmitter は receiver.transform.position までの距離と角度でパルスの
        /// 当たり判定をしているため、これだと部屋の中の位置関係が完全に失われ、
        /// 「部屋ごと全部光る」か「1つも光らない」かの二択になる。
        /// （視界が真っ暗になっていた原因はこれ。）
        /// </summary>
        static void Recenter(Mesh mesh, Transform t)
        {
            if (mesh == null || !mesh.isReadable) return;
            var c = mesh.bounds.center;
            if (c.sqrMagnitude < 1e-8f) return;
            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] -= c;
            mesh.vertices = v;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            t.localPosition = c;
        }

        static string[] Keys(Dictionary<string, List<CombineInstance>> d)
        {
            var l = new List<string>();
            foreach (var kv in d) l.Add(kv.Key + "×" + kv.Value.Count);
            l.Sort();
            return l.ToArray();
        }

        static Mesh Combine(List<CombineInstance> cis, string name)
        {
            if (cis.Count == 0) return null;
            var mesh = new Mesh { name = name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.CombineMeshes(cis.ToArray(), true, true);
            mesh.RecalculateBounds();
            var path = MeshDir + "/" + name + ".asset";
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (ex != null) { EditorUtility.CopySerialized(mesh, ex); EditorUtility.SetDirty(ex); return ex; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Transform FindRoom(string n)
        {
            foreach (var g in Object.FindObjectsOfType<GameObject>())
                if (g.name == n && g.GetComponentsInChildren<Renderer>().Length > 0) return g.transform;
            return null;
        }

        /// <summary>生成した層を全部消す（作り直す前に使う）。</summary>
        [MenuItem("BLIND/vision/9. 生成したサーモ・エコロケ層を消す")]
        public static string Clear()
        {
            int n = 0;
            foreach (var g in Object.FindObjectsOfType<GameObject>())
                if (g.name == EchoGroup || g.name == ThermalGroup) { Object.DestroyImmediate(g); n++; }
            return n + " 個の生成層を削除";
        }
    }
}
