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

        /// <summary>
        /// 生成した EchoReceiver の点灯時間(秒)。
        /// EchoEmitter の pulseInterval より十分短くしないと、次のパルスが来る前に
        /// 消えず「常に見えている」状態になり、暗闇を手探りする感じが無くなる。
        ///
        /// パルス間隔を 3.5→2.2秒 に詰めた分、ここも 1.2→0.6秒 に削ってある。
        /// 「1回の光がどれだけ長く残るか」ではなく「暗闇が何秒あるか」が体験の核なので、
        /// 間隔を縮めるときは必ずこちらも同じ比率で縮めること。
        /// </summary>
        const float EchoGlowDuration = 0.6f;

        /// <summary>
        /// これを超える三角形数のメッシュは簡易形状に差し替える。
        /// サーモは「その物が何度か」、エコロケは「どこに何があるか」しか伝えないので、
        /// 人形の指1本まで再現しても情報は増えず、ポリゴンだけが3倍になる。
        /// </summary>
        const int MaxPropTris = 180;

        static Mesh _boxProxy, _blobProxy;

        /// <summary>
        /// ギミック側が3層ぶん自分で作っている物か。ここは材料にしてはいけない。
        ///
        /// ⚠️ この判定が無いと落とし穴が塞がる。
        /// `BlindGimmickBuilder` は PitField_Generated の下に
        /// D_（過去人）/ T_（サーモ）/ E_（エコロケ）の3層を**自分で**作る。
        /// D_ は Default レイヤーなので、この関数が無いと vision/2 がそれを
        /// 「まだ複製していない普通の床」とみなして拾い、22℃の床として
        /// サーモ層に複製してしまう。結果、45℃の熱の蓋の上に22℃の床が乗り、
        /// **サーモ視点で穴が完全に消える**（実際にそうなった）。
        ///
        /// ギミック→vision の順で作れば起きないが、順番に依存する仕様は必ず壊れる。
        /// 順番によらず安全になるよう、ここで material として除外する。
        /// </summary>
        static bool IsGimmickOwned(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name == "PitField_Generated") return true;
            return false;
        }

        // ------------------------------------------------------------
        // 床に開いている穴（アヒル部屋のプールなど）
        // ------------------------------------------------------------
        /// <summary>エコロケの床タイルの一辺(m)。穴のある部屋だけこの大きさに割る。</summary>
        const float FloorTile = 1.40f;

        /// <summary>穴の登録。床メッシュとプール本体の名前、深さだけを書く。形はメッシュから取る。</summary>
        class FloorHoleSpec
        {
            public string room;
            public string deckObject;    // 穴が開いた状態で入っている床メッシュの名前
            public string basinObject;   // 穴の底（プールの器）。縁の判定に使う
            public float depth;          // 床面からの深さ(m)
            public string note;
        }

        static readonly FloorHoleSpec[] FloorHoleSpecs =
        {
            // room6（アヒル部屋）。PoolDeck は「穴の開いた床」として入っている(109三角形)。
            // 深さは PoolBasin の実測 2.01m。落ちると出られないので必ず縁を見せる。
            new FloorHoleSpec { room="room6", deckObject="PoolDeck", basinObject="PoolBasin",
                                depth=2.01f, note="アヒル部屋のプール" },
        };

        /// <summary>
        /// 床メッシュから「床がある範囲」と「穴のふち」を取り出したもの。
        ///
        /// ⚠️ 穴の形を矩形で近似しないこと。プールの縁は有機的な曲線で、
        /// バウンズの矩形で抜くと**実際には床がある所まで消え、縁の線も本物のふちから
        /// 何メートルもずれた所に出る**（実際にやった）。元メッシュの境界辺をそのまま使う。
        ///
        /// ⚠️ 「穴＝閉じた輪」だと思わないこと。room6 のプールは部屋の隅まで達していて
        /// **床の外周と地続きの一本の境界線**になっている。輪に分けようとすると
        /// 外周とプールが繋がった1本しか出てこない（実際にそうなった）。
        /// 輪には分けず、境界辺を1本ずつ「外に出た先がプールの器の上か」で判定する。
        /// </summary>
        class FloorCutout
        {
            public float topY;
            public float depth;
            public List<Vector3[]> tris = new List<Vector3[]>();   // 床(world)。XZだけ使う
            public List<Vector3[]> holeEdges = new List<Vector3[]>();  // 穴のふちの線分(world)

            /// <summary>その位置に床があるか（XZ で三角形に含まれるか）。</summary>
            public bool OnDeck(float x, float z) { return Covers(tris, x, z); }

            static bool Covers(List<Vector3[]> tl, float x, float z)
            {
                for (int i = 0; i < tl.Count; i++)
                {
                    var t = tl[i];
                    if (InTri(x, z, t[0], t[1], t[2])) return true;
                }
                return false;
            }

            static bool InTri(float px, float pz, Vector3 a, Vector3 b, Vector3 c)
            {
                float d1 = (px - b.x) * (a.z - b.z) - (a.x - b.x) * (pz - b.z);
                float d2 = (px - c.x) * (b.z - c.z) - (b.x - c.x) * (pz - c.z);
                float d3 = (px - a.x) * (c.z - a.z) - (c.x - a.x) * (pz - a.z);
                bool neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
                bool pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
                return !(neg && pos);
            }

            static List<Vector3[]> WorldTris(Transform room, string objName)
            {
                Transform t = null;
                foreach (var x in room.GetComponentsInChildren<Transform>(true))
                    if (x.name == objName) { t = x; break; }
                if (t == null) return null;
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return null;

                var vs = mf.sharedMesh.vertices;
                var ts = mf.sharedMesh.triangles;
                var w = new Vector3[vs.Length];
                for (int i = 0; i < vs.Length; i++) w[i] = t.TransformPoint(vs[i]);
                var outp = new List<Vector3[]>();
                for (int i = 0; i < ts.Length; i += 3)
                    outp.Add(new[] { w[ts[i]], w[ts[i + 1]], w[ts[i + 2]] });
                return outp;
            }

            public static FloorCutout For(Transform room, string roomName)
            {
                FloorHoleSpec spec = null;
                foreach (var s in FloorHoleSpecs) if (s.room == roomName) { spec = s; break; }
                if (spec == null) return null;

                var deckTris = WorldTris(room, spec.deckObject);
                var basinTris = WorldTris(room, spec.basinObject);
                if (deckTris == null || basinTris == null) return null;

                var cut = new FloorCutout { depth = spec.depth, topY = -9e9f, tris = deckTris };
                foreach (var t in deckTris)
                    cut.topY = Mathf.Max(cut.topY, Mathf.Max(t[0].y, Mathf.Max(t[1].y, t[2].y)));

                // --- 境界辺（三角形1枚にしか使われていない辺）を集める ---
                // 頂点は三角形ごとに複製されているので、位置を丸めた値で数える。
                var count = new Dictionary<long, int>();
                var seg = new Dictionary<long, Vector3[]>();
                System.Func<Vector3, long> key = p =>
                    ((long)Mathf.RoundToInt(p.x * 500f) << 21) ^ (long)Mathf.RoundToInt(p.z * 500f);
                foreach (var t in deckTris)
                    for (int e = 0; e < 3; e++)
                    {
                        var p0 = t[e]; var p1 = t[(e + 1) % 3];
                        long k0 = key(p0), k1 = key(p1);
                        if (k0 == k1) continue;
                        long ek = k0 < k1 ? (k0 * 1000003L + k1) : (k1 * 1000003L + k0);
                        if (!count.ContainsKey(ek)) { count[ek] = 0; seg[ek] = new[] { p0, p1 }; }
                        count[ek]++;
                    }

                // --- 境界辺のうち「外に出た先がプールの器の上」の物だけ拾う ---
                // 部屋の壁際の境界は外に出ると器が無いので落ちる。
                foreach (var kv in count)
                {
                    if (kv.Value != 1) continue;
                    var e = seg[kv.Key];
                    var d = new Vector3(e[1].x - e[0].x, 0f, e[1].z - e[0].z);
                    if (d.sqrMagnitude < 1e-6f) continue;
                    d.Normalize();
                    var mid = (e[0] + e[1]) * 0.5f;
                    var perp = new Vector3(-d.z, 0f, d.x);

                    // 床がある側を先に決める（辺の向きは当てにならない）
                    var inSide = mid + perp * 0.25f;
                    if (!Covers(deckTris, inSide.x, inSide.z)) perp = -perp;
                    var outSide = mid - perp * 0.25f;

                    // 床の外に出て、かつ器の上 ＝ プールのふち
                    if (Covers(deckTris, outSide.x, outSide.z)) continue;
                    if (!Covers(basinTris, outSide.x, outSide.z)) continue;

                    cut.holeEdges.Add(new[] { e[0], e[1], perp });   // perp は床側の向き
                }

                return cut.holeEdges.Count > 0 ? cut : null;
            }
        }

        /// <summary>
        /// 床の穴の縁と深さを描くメッシュ（部屋ローカル空間）。
        ///
        /// 落とし穴部屋と同じ考え方。四角形1枚ごとに 0〜1 の UV を貼るので、
        /// EchoHighlight はその1枚ずつを輪郭として光らせる。
        ///   ・縁の帯   : 床の上に置く平らな枠。遠くからでも「ここで床が終わる」と分かる
        ///   ・内壁の横縞: 深さを段で伝える。落とし穴の PitRings と同じ役目
        /// </summary>
        static Mesh BuildHoleRimMesh(FloorCutout cut, Transform room, string name)
        {
            const float LipWidth = 0.35f;   // 縁の帯の幅(m)
            const int   Rings    = 4;       // 内壁の横縞の数
            float floorTopY = cut.topY;
            float depth = cut.depth;

            var verts = new List<Vector3>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();

            System.Action<Vector3, Vector3, Vector3, Vector3> quad = (a, b, c, d) =>
            {
                int i0 = verts.Count;
                verts.Add(room.InverseTransformPoint(a));
                verts.Add(room.InverseTransformPoint(b));
                verts.Add(room.InverseTransformPoint(c));
                verts.Add(room.InverseTransformPoint(d));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1));
                uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
                // 裏からも見えるようにする。プールの縁は中に落ちてから見上げることもある
                tris.Add(i0 + 2); tris.Add(i0 + 1); tris.Add(i0);
                tris.Add(i0 + 3); tris.Add(i0 + 2); tris.Add(i0);
            };

            float lipY = floorTopY + 0.03f;   // 床板(上面 floorTopY)より上に出す

            // ふちの線分を1本ずつ帯と縞にする。
            // 輪に繋がっていなくても成立するので、外周と地続きの穴でも正しく描ける。
            foreach (var e in cut.holeEdges)
            {
                var a0 = new Vector3(e[0].x, lipY, e[0].z);
                var a1 = new Vector3(e[1].x, lipY, e[1].z);
                if ((a1 - a0).sqrMagnitude < 1e-6f) continue;

                // 少し縮めて1枚ずつ独立した四角にする（隣とくっつくと1本の線に融ける）
                var mid = (a0 + a1) * 0.5f;
                a0 = mid + (a0 - mid) * 0.86f;
                a1 = mid + (a1 - mid) * 0.86f;

                var off = e[2] * LipWidth;   // e[2] は床がある側の向き

                quad(a0, a1, a1 + off, a0 + off);

                for (int r = 0; r < Rings; r++)
                {
                    float y0 = floorTopY - depth * (r + 0.15f) / Rings;
                    float y1 = floorTopY - depth * (r + 0.55f) / Rings;
                    quad(new Vector3(a0.x, y0, a0.z), new Vector3(a1.x, y0, a1.z),
                         new Vector3(a1.x, y1, a1.z), new Vector3(a0.x, y1, a0.z));
                }
            }

            if (verts.Count == 0) return null;

            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();

            // Combine と同じくアセットとして残す。シーンに新規メッシュを持たせると
            // 保存のたびにシーンが太り、作り直しで参照が切れる。
            var path = MeshDir + "/" + name + ".asset";
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (ex != null) { EditorUtility.CopySerialized(m, ex); EditorUtility.SetDirty(ex); return ex; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

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

        /// <summary>
        /// 細長い物・薄い板か。
        ///
        /// 簡易形状はバウンディングボックス（軸に沿った箱）で置き換える。
        /// まっすぐ立った棒なら箱と実物はほぼ同じだが、斜めに走る物だと
        /// 箱が実体より桁違いに大きくなる。
        /// 実例：room14 の斜めのレーザーは太さ5cmしかないのに、
        /// 箱にすると 4.2m×5.0m の板になり、サーモ役の画面を丸ごと橙で塗り潰していた。
        ///
        /// 1m を超える長さがあって、一番細い方向が一番長い方向の 15% 未満なら
        /// 箱で代用してはいけないと判断する。パイプ・手すり・梁・ビームが該当する。
        /// </summary>
        /// <summary>
        /// 什器用のエコロケ材質。建物用との違いは _RimWeight（シルエット発光）だけ。
        ///
        /// 建物は稜線（UVの端）だけを線で描く。箱の集合なのでそれで形が出る。
        /// 置いてある物は椅子や人形のような有機的な形で、稜線が無いので
        /// UVの端だけでは輪郭がほとんど出ず、暗闇に沈んで見えなかった。
        /// 視線に対して縁になっている面を光らせると形が出る。
        /// 反響定位で返ってくるのは物の「外形」なので、こちらが本来の見え方でもある。
        /// </summary>
        static Material EchoPropMaterial(Material baseMat)
        {
            const string path = EchoBigDir + "/EchoMaterial_Prop.mat";
            if (!AssetDatabase.IsValidFolder(EchoBigDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Materials", "Echo");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(baseMat); AssetDatabase.CreateAsset(m, path); }
            m.shader = baseMat.shader;
            m.CopyPropertiesFromMaterial(baseMat);
            m.SetFloat("_RimWeight", 0.45f);
            m.SetFloat("_RimPower", 2.5f);
            m.SetFloat("_GlowIntensity", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>部屋そのものを構成する面か（＝置いてある物ではないか）。</summary>
        static bool IsArchitecture(string key)
        {
            return key == "Wall" || key == "Ceiling" || key == "FloorStone" || key == "Water";
        }

        // -------------------------------------------------------------
        //  動く物の検出
        // -------------------------------------------------------------
        /// <summary>
        /// 「あとで動く物」の Transform 集合を作る。
        ///
        /// なぜ必要か：
        /// 静止物は結合して Vision_Thermal / Vision_Echo にまとめている。これは
        /// ドローコールを Default の 1/8 まで落とすための重要な最適化で、やめたくない。
        /// しかし結合してしまうと、複製は元オブジェクトとは別の場所（部屋直下のバケツ）に
        /// 置かれるため、元が動いても複製は取り残される。
        ///
        /// 実際に踏んだ事故：room3 のガレージシャッターを開くギミックを入れたとき、
        /// T_Garage_Shutter / E_Garage_Shutter が Vision_Thermal / Vision_Echo の下に
        /// いたので一緒に上がらず、サーモ役とエコロケ役にはシャッターが閉じたまま見えていた。
        /// 過去人だけが「開いた」と言い、他の2人には嘘に聞こえる、という最悪のバグになる。
        ///
        /// 対策として、動く物だけは結合せず、複製を元オブジェクトの子として置く。
        /// 子にしておけば元が動けば必ず一緒に動く。構造的に二度とズレない。
        /// 動く物はドアやシャッターのように数が少ないので、結合をやめても損失は小さい。
        ///
        /// 判定材料は2つ：
        ///   1. Animator の支配下にある（ドアの開閉アニメ・歩く人形など）
        ///   2. ギミックスクリプトから Transform / GameObject として参照されている
        ///      （MultiButtonDoor.targetDoor1、DoorQuizManager.door0〜2 など）
        /// 2 はフィールド名を決め打ちにせず、UdonSharpBehaviour の全参照を舐めて集める。
        /// 名前で判定すると新しいギミックを足すたびにここを直す羽目になるため。
        /// </summary>
        static HashSet<Transform> CollectMovable(Transform room)
        {
            var roots = new HashSet<Transform>();

            // 1. Animator の支配下。
            //    ただしコントローラの刺さっていない Animator は動かない。
            //    room9 の配管はインポート時に付いた空の Animator を92個持っていて、
            //    これを「動く物」と誤判定すると配管が全部結合対象から外れ、
            //    サーモ・エコロケのドローコールがそれぞれ +92 される。
            //    マップ全体でコントローラを持つ Animator は3個しかない。
            foreach (var anim in room.GetComponentsInChildren<Animator>(true))
                if (anim != null && anim.runtimeAnimatorController != null) roots.Add(anim.transform);

            // 2. ギミックスクリプトが握っている Transform
            //    シーン全体を見る。room3 のシャッターのように、
            //    マネージャが別の部屋にいる場合があるため。
            foreach (var usb in Object.FindObjectsOfType<UdonSharp.UdonSharpBehaviour>(true))
            {
                if (usb == null) continue;
                var so = new SerializedObject(usb);
                var it = so.GetIterator();
                bool e = it.NextVisible(true);
                while (e)
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                    {
                        Transform t = null;
                        if (it.objectReferenceValue is Transform tr) t = tr;
                        else if (it.objectReferenceValue is GameObject g) t = g.transform;
                        if (t != null && t.IsChildOf(room)) roots.Add(t);
                    }
                    e = it.NextVisible(false);
                }
            }

            // 見つけた根の配下すべてを「動く」とみなす。
            // ドアは板・枠・取っ手が子に分かれていることが多く、根だけ拾っても意味が無い。
            var all = new HashSet<Transform>();
            foreach (var r in roots)
            {
                if (r == null || r == room) continue;      // 部屋そのものを指す参照は無視する

                // 安全弁：大きな入れ物への参照が1つ紛れ込むだけで、その配下が
                // まるごと結合対象から外れてドローコールが跳ね上がる。
                // 動く物（ドア・シャッター）はどれも十数レンダラーで収まるので、
                // それを大きく超える塊は「参照はされているが動く物ではない」と見なす。
                int rc = r.GetComponentsInChildren<MeshRenderer>(true).Length;
                if (rc > MovableRendererCap) continue;

                foreach (var t in r.GetComponentsInChildren<Transform>(true)) all.Add(t);
            }
            return all;
        }

        /// <summary>「動く物」1つが持てるレンダラー数の上限。超えたら結合側に回す（CollectMovable 参照）。</summary>
        const int MovableRendererCap = 40;

        static bool IsThinAndLong(Bounds b)
        {
            float mx = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float mn = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
            if (mx < 1.0f) return false;               // 小さい物は箱にしても実害が無い
            return mn < mx * 0.15f;
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

            // 体温を持つ物は結合しない。1体ずつ独立したレンダラーにする。
            //
            // 結合すると16体が1枚のメッシュになり、MaterialPropertyBlock は
            // レンダラー単位でしか効かないので「16体が完全に同じ呼吸をする」ことしかできない。
            // ThermalBodyDrift で1体ずつ違う周期で体温を揺らし、さらに1体だけを
            // 「暖かい個体」にするには、個体が別々のレンダラーである必要がある。
            //
            // 軽い物でも結合側へ落とさないよう、三角形数の判定より先に置いている。
            // 代償はドローコール +16 程度。サーモ層は元々レンダラーが少ないので許容範囲。
            if (readable && (key == "Body" || key == "Skin" || key == "Burning"))
            {
                standalone = true;
                return false;
            }

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

            // 配管・配線を箱にしてはいけない。
            //
            // room10 の配線束は1個 72,024 三角形あり、上限(180)を大きく超えるので
            // バウンディングボックス（2.5×1.3×2.9m の中身の詰まった箱）に置き換えられていた。
            // それが19個ぶん天井に並んだ結果、**サーモ視点の 74% が巨大な板で埋まり**、
            // 「熱源しか見えない」という前提そのものが壊れていた。
            //
            // 配管はサーモ役にとって数少ない道しるべなので、重くても形を残す。
            // 頂点クラスタリングで落とすと絡まった線がやや団子になるが、
            // 箱になるよりは遥かにましで、天井を走る線として読める。
            //
            // ブラウン管も同じ理由で箱にしてはいけない。
            // 1台が back(7,682) / front(2,428) / top(2,406) の3枚で出来ていて全部上限超え。
            // 箱にすると、いちばん大きい front の箱が他を飲み込んで
            // **「宙に浮いた黄色い板」** にしか見えなくなる（実際にそうなっていた）。
            // 前面ガラス(front)だけ 47℃、筐体(back/top)は 17.5℃ と温度を分けてあるので、
            // 形さえ残れば「積まれた機械のうち、どれがこちらを向いているか」が読める。
            if (readable && (IsDuctKey(key) || IsCrtKey(key)))
            {
                int budget = IsCrtKey(key) ? CrtShapeTris : DuctShapeTris;
                var lite = BlindMeshReducer.SaveLite(src, budget, IsCrtKey(key) ? "_crt" : "_duct");
                ci.mesh = lite != null ? lite : src;
                ci.subMeshIndex = 0;
                ci.transform = room.worldToLocalMatrix * mr.transform.localToWorldMatrix;
                return true;
            }

            // 人体・体温を持つ物だけは箱に潰してはいけない。
            // サーモ役の画面で緑に光る塊が「人の形」なのか「ただの箱」なのかは
            // この部屋の意味そのもの（人形部屋に体温のある人形が混ざっている）で、
            // 箱にした瞬間その情報が消える。重ければ粗くしてでも輪郭を残す。
            bool silhouette = IsSilhouetteProp(mr);
            if (readable && silhouette)
            {
                var lite = BlindMeshReducer.SaveLite(src, silhouette ? SilhouetteTris : HeatShapeTris, "_heat");

                // 元が既に目標より軽いと SaveLite は null を返す。
                // そこで諦めて箱にしてしまうと、軽いだけの物が箱に化けるので素の形を使う。
                ci.mesh = lite != null ? lite : src;
                ci.subMeshIndex = 0;
                ci.transform = room.worldToLocalMatrix * mr.transform.localToWorldMatrix;
                return true;
            }

            // ただし簡易形状にすると部屋を塞いでしまう大物は、素のメッシュを
            // 1枚そのまま置く（結合できないので単独のレンダラーになる）
            if (src != null && (IsBig(b) || IsThinAndLong(b))) { standalone = true; return false; }
            ci.mesh = IsRoundish(key, b) ? BlobProxy() : BoxProxy();
            ci.subMeshIndex = 0;
            var local = room.worldToLocalMatrix * Matrix4x4.TRS(b.center, Quaternion.identity, b.size);
            ci.transform = local;
            return true;
        }

        /// <summary>体温を持つ＝人体プロファイルを効かせる分類キーか。</summary>
        static bool IsBodyKey(string key)
        {
            return key == "Body" || key == "Skin" || key == "Burning";
        }

        /// <summary>配管・配線か。箱に潰してはいけない物の判定。</summary>
        static bool IsDuctKey(string key)
        {
            return key == "Duct" || key == "DuctHot" || key == "DuctWarm" || key == "DuctDead";
        }

        /// <summary>
        /// 配管・配線を減面するときの目標三角形数。
        ///
        /// 元が 72,000 三角形もある配線束を 300 まで落とすと絡まりが団子になって
        /// 「天井に何か塊がある」としか読めなくなる。1,200 残せば線の走り方が分かる。
        /// 19個で 22,800 三角形。マップ全体に対しては誤差。
        /// </summary>
        const int DuctShapeTris = 1200;

        /// <summary>ブラウン管か。前面ガラスと筐体で温度が違うので、形が要る。</summary>
        static bool IsCrtKey(string key)
        {
            return key == "CRTOn" || key == "CRTOff";
        }

        /// <summary>
        /// ブラウン管を減面するときの目標三角形数。
        /// 1台3枚 × 14台 = 42枚。400 なら合計 16,800 三角形で、
        /// 画面の平面と筐体の奥行きが区別できる程度には形が残る。
        /// </summary>
        const int CrtShapeTris = 400;

        /// <summary>エコロケ用に素の形を使うときの上限。これを超えたら簡略版を作る。</summary>
        const int EchoShapeTris = 300;

        /// <summary>
        /// 体温を持つ物（人形・焼けた人）をサーモ層に出すときの粗さ。
        ///
        /// 以前は 300 だった。「人の形をした何か」と分かれば足りるという判断だったが、
        /// 実測したところこれは二重減面になっていた。room16 の人形は Default 層の時点で
        /// 既に BlindMeshReducer が作った `_lite` メッシュ（3,561三角形）で、
        /// それをさらに 300 まで落としていた。頂点クラスタリングは格子スナップなので、
        /// 一度粗くした物をもう一度かけると腕や脚が胴から千切れ、輪郭に穴が空く。
        /// サーモ視点のスクリーンショットで人形の脚が分離して見えていたのはこれが原因。
        ///
        /// サーモ視点の「暗闇に人体だけが浮かぶ」画は作品の看板になる絵なので、
        /// ここを削る意味は薄い。4000 にすると：
        ///   ・人形(_lite 3,561) は予算内なので減面されず、Default 層と同じ形が出る
        ///   ・巨大な腕(GiantArm 4,867) だけが 4,000 に落ちる（ほぼ原形）
        /// 増えるのは room16 全体で3〜4万三角形程度。マップ100万に対して誤差。
        /// サーモ層はレンダラー数が Default の 1/8 しかなく、コストは三角形ではなく
        /// ドローコールに支配されているので、この増加は体感に出ない。
        /// </summary>
        const int HeatShapeTris = 4000;

        /// <summary>
        /// 形そのものが情報になっている物（チェス駒）の粗さ。
        ///
        /// 頂点クラスタリングは格子にスナップする方式なので、
        /// 回転体を削ると台座の輪から先に段々になり、最後は溶けた塊になる。
        /// 実測で比べたところ、ナイトが馬に見えなくなるのが 1500、
        /// ルークの狭間が消えるのが 800、原形をとどめないのが 300 だった。
        /// 2500 なら駒の種類が判別でき、8個で 2万三角形に収まる。
        /// </summary>
        const int SilhouetteTris = 2500;

        /// <summary>
        /// エコロケ層に使う元メッシュを決める。null なら簡易形状（箱）にする。
        ///
        /// 以前は「形が意味を持つ物」だけ実形状にして、他は箱で代用していた。
        /// しかしそれだと room11・room13・room19 のように、床と壁以外が
        /// 全部 12三角形の箱になる部屋が出てしまい、エコロケ役には
        /// 「四角い何かが置いてある」以上のことが一生分からなかった。
        ///
        /// 実測すると箱プロキシ113個の合計はわずか1,356三角形。
        /// 全部を300三角形の実形状に置き換えても増えるのは3万程度で、
        /// マップ全体102万に対して誤差でしかない。
        /// ポリゴンを惜しむ理由が無いので、読める物は全部そのまま形を出す。
        /// </summary>
        static Mesh EchoSource(Renderer mr, string key)
        {
            var mf = mr.GetComponent<MeshFilter>();
            var src = mf != null ? mf.sharedMesh : null;
            if (src == null) return null;

            // 読めないメッシュは三角形を数えることすらできない。
            // インポート設定を書き換えて読めるようにしてから判断する。
            if (!src.isReadable)
            {
                BlindMeshReducer.EnsureReadable(new[] { src });
                if (!src.isReadable) return null;   // それでも駄目なら箱で妥協
            }

            // 形そのものが情報になっている物は、輪郭用でも粗くしすぎない。
            // 300 まで落とすとチェス駒が種類の区別できない塊になる。
            int budget = IsSilhouetteProp(mr) ? SilhouetteTris : EchoShapeTris;

            int tris = src.triangles.Length / 3;
            if (tris <= budget) return src;

            // 表示用の簡略版とは別に、輪郭専用のもっと粗い版を作る
            var lite = BlindMeshReducer.SaveLite(src, budget, "_echo");
            return lite != null ? lite : src;
        }

        // -------------------------------------------------------------
        //  分類：レンダラーの名前とマテリアル名から温度クラスを決める
        // -------------------------------------------------------------
        /// <summary>
        /// room16 で「体温を持っている」人形。
        ///
        /// 16体全部を熱くすると、サーモ役の画面が人型で埋まって
        /// 「どれが動くのか」が読めなくなる。逆に全部冷たいと、
        /// サーモ役はこの部屋で何の役にも立たない。
        /// 数体だけ熱くすると「16体のうち4体だけ体温がある」という状態になり、
        /// サーモ役にしか分からない情報として一番強く効く。
        /// 部屋の四隅に散らして、どこにいても最低1体は視界に入るようにしてある。
        /// </summary>
        /// 【廃止】以前はここに挙げた4体だけを熱くしていた。
        /// 「16体のうち4体だけ体温がある」という設計だったが、実際に見ると
        /// サーモ役の視界には人影が4つ浮かぶだけで、部屋に人形が林立している
        /// という事実そのものが伝わらなかった。今は Prop_Dolls 配下を全部 Body にしている。
        static readonly HashSet<string> HotDolls = new HashSet<string>();

        /// <summary>
        /// 形そのものが情報になっている物。箱やブロブに潰してはいけない。
        ///
        /// room19 のチェス駒がこれにあたる。ナイトなのかルークなのかが分からないと、
        /// 3人が「馬の駒の右」「塔みたいなやつの手前」と言い合えない。
        /// 箱に潰すと駒が全部同じ直方体になり、この部屋で会話が成立しなくなる。
        /// 駒は1個2〜7万tri と重いが、粗くしてでも輪郭を残す価値がある。
        /// </summary>
        static readonly string[] SilhouetteProps =
        {
            "knight", "bishop", "rook", "pawn", "queen", "king",
        };

        /// <summary>
        /// この器具に生きた光源が付いているか。1=点いている 0=消えている -1=Light が無い。
        /// 器具本体（Globe など）から親を数段さかのぼって Light を探す。
        /// 電球と Light コンポーネントは同じ階層に無いことが多いため。
        /// </summary>
        static int LampIsLit(Transform t)
        {
            for (var tr = t; tr != null; tr = tr.parent)
            {
                var lights = tr.GetComponentsInChildren<Light>(true);
                if (lights.Length == 0) continue;
                foreach (var l in lights)
                    if (l.enabled && l.gameObject.activeInHierarchy && l.intensity > 0.01f) return 1;
                return 0;
            }
            return -1;
        }

        /// <summary>
        /// 名前から決まる 0..n-1 の番号。同じ名前なら毎回同じ値になる。
        /// 作り直すたびに天井の模様が変わると、サーモ役が覚えた地形が毎回無効になる。
        /// </summary>
        static int StableIndex(string name, int n)
        {
            unchecked
            {
                int h = 17;
                foreach (var c in name) h = h * 31 + c;
                return ((h % n) + n) % n;
            }
        }

        static bool IsSilhouetteProp(Renderer r)
        {
            for (var tr = r.transform; tr != null; tr = tr.parent)
            {
                string n = tr.name.ToLower();
                foreach (var k in SilhouetteProps)
                    if (n.StartsWith(k)) return true;
                // 部屋やシーンのルートまで行ったら打ち切る
                if (tr.name.StartsWith("room") || tr.name.Contains("ROOMS")) break;
            }
            return false;
        }

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

            // 過去の人の視界を塞ぐためだけに置いた黒い複製。
            // 元の壁が別レイヤーに残っていて、そちらから既にサーモ・エコロケが
            // 作られている。これも拾うと同じ壁が二重になり、面が重なってちらつく。
            if (n.StartsWith("Blackout_")) { echo = false; return null; }

            // --- 体温を持つもの（room16） ---
            // 人形は名前で個体指定する。親をたどって判定するのは、
            // 人形が USDRoot などの中間ノードを挟んでいる場合があるため。
            for (var tr = go.transform; tr != null; tr = tr.parent)
            {
                // room16 の人形は全部が体温を持つ。
                // 以前は4体だけ熱くしていたが、それだと残り12体がサーモ役に見えず、
                // 「人形だらけの部屋」という部屋の姿そのものが伝わらなかった。
                // 全部に体温があれば、サーモ役には人影が林立して見える。
                if (tr.name == "Prop_Dolls") return "Body";

                // 巨大な手。この部屋の主なので、素肌として一番はっきり見せる。
                // 掴まれている人形は服の上からの体温、床を突き破った破片は木材のまま。
                if (tr.name == "Prop_GiantHand")
                {
                    if (n == "GrabbedDoll") return "Body";
                    if (n.StartsWith("Splinter") || n == "Void") break;
                    return "Skin";
                }
            }

            // --- room11 の天井の配線と配管 ---
            // 全部 Duct(38℃) で塗ると天井が一面同じ色になり、線の走り方が読めない。
            // 名前から作った固定の番号で4段階に散らし、
            // 「電気の来ている線」と「死んだ線」が混じった天井にする。
            // 乱数ではなく名前のハッシュを使うのは、作り直すたびに模様が変わらないようにするため。
            if (all.Contains("wire") || all.Contains("tuyaux") || all.Contains("cable"))
            {
                for (var tr = go.transform; tr != null; tr = tr.parent)
                {
                    // ⚠️ 部屋番号は振り直されている（§1.5）。天井が配線だらけの部屋は
                    //    現在 room10。以前ここに書いてあった room11 は今はだるま部屋で、
                    //    配線が1本も無いため4段階の散らしが効かなくなっていた。
                    // room8（ロッカー部屋）も同じ扱いにする。
                    // ここはサーモ視点が点灯0.4%＝ほぼ真っ黒だった部屋で、
                    // 壁と天井に配線を追加して「生きている線と死んだ線が混じった天井」にする。
                    if (tr.name != "room10" && tr.name != "room8") continue;
                    switch (StableIndex(n, 4))
                    {
                        case 0: return "DuctDead";
                        case 1: return "DuctWarm";
                        case 2: return "Duct";
                        default: return "DuctHot";
                    }
                }
            }

            // --- ブラウン管の画面 ---
            // 筐体(back/top)は室温のままにして、前面のガラスだけ熱くする。
            // 画面だけが光っていると、サーモ役には「山積みの機械のうち
            // どれがこちらを向いているか」まで分かる。
            if (n.StartsWith("front_case_low")) return "CRTOn";

            // --- レーザー（room14）: サーモ役だけの領分 ---
            // ビームも天井の発射装置も、まとめてサーモ専用にする。
            // エコロケ役に装置が見えると「天井に何か並んでる」で危険を推理できてしまい、
            // サーモ役が唯一の情報源である状態が崩れる。
            // Default層の実体は別途 NowOnly(25) へ移すので過去人にも見えない。
            // 「何も見えないのに焼かれる」という状況を、サーモ役の一言だけが防ぐ。
            if (all.Contains("laser"))
            {
                echo = false;
                // 発光板(LaserGlow)はビームを太く見せるための板で、
                // 大きい物だと 4.3m×5.0m あり、近くに立つと画面が丸ごと白く覆われる。
                // サーモには芯(LaserBeam)と発射装置だけ出せば「細い線」として読める。
                if (all.Contains("glow")) return null;
                return "Laser";
            }

            // --- 発熱するもの（サーモ役の道しるべになる） ---
            if (n == "Lens" || all.Contains("lamplens")) return "Lamp";
            if (n == "Housing" || all.Contains("lamphousing")) return "Ballast";

            // 取り込んだ照明器具は、名前が LightPanel でないというだけで
            // 全部 19℃ の小物になっていた（room16 の吊り下げ電球、room11 の蛍光灯、
            // room8 の埋込照明、各部屋の typeBlight など）。
            // 停電したビルで唯一まだ電気が来ている場所がサーモ役の道しるべなので、
            // ここが冷たいとサーモ役は暗闇に置き去りになる。
            //
            // "fluoro" は "Fluorescent" に一致しない（fluore と fluoro）。
            // room11 の蛍光灯10本がずっと常温だったのはこれが原因。
            if (n == "Globe" || n == "Shade"
                || all.Contains("fluor") || all.Contains("bulb")
                || all.Contains("lightfixture") || all.Contains("ceilinglight")
                || all.Contains("typeblight") || all.Contains("emergency_light")
                || all.Contains("lightpanel") || all.Contains("light_panel")
                || all.Contains("chess light"))
            {
                if (all.Contains("_off")) return "LampOff";
                if (all.Contains("_dim")) return "LampDim";

                // 名前で分からない器具は、実際に付いている Light を見て判断する。
                // 消えている器具まで熱いと「どこはまだ生きているか」が読めなくなる。
                var lit = LampIsLit(go.transform);
                if (lit == 0) return "LampOff";
                return "Lamp";
            }
            // 天井の照明パネルは On / Dim / Off の3種がある。生きている物だけが熱い＝
            // サーモ役には「どの列の灯りが生きているか」が読める

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
            // kabesitatya = 壁に貼り付いた腰板。room1・room4 に計46枚ある。
            // 什器として扱うとシルエット発光が乗り、浅い角度で見たときに
            // 壁一面が緑に塗り潰されてしまう。壁の一部なので建物側で扱う。
            if (all.Contains("wall") || all.Contains("kabe") || all.Contains("dado") || all.Contains("doorframe")
                || all.Contains("plaster") || all.Contains("trim") || all.Contains("baseboard")
                || all.Contains("cornice") || all.Contains("handrail")) return "Wall";

            // --- 什器 ---
            // 配管系。room9 のロッカー裏を這う配管、room12 の空調、room2・room14 の通気口、room11 の配線・コード。
            // 中を何かが通っている＝生きている設備として、サーモ役にはっきり見せる。
            if (all.Contains("duct") || all.Contains("conduit") || all.Contains("hanger")
                || all.Contains("pipe") || all.Contains("vent") || all.Contains("tube")
                || all.Contains("valve") || all.Contains("plumb")
                || all.Contains("wire") || all.Contains("cable") || all.Contains("cord") || all.Contains("electrical")) return "Duct";
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
        [MenuItem("BLIND/vision/2. 全部屋にサーモ・エコロケを生成")]
        public static string BuildMine()
        {
            // MainWorld に全員の部屋を統合したので、=== ROOMS === の下を全部対象にする。
            // 分類は名前とマテリアル名から自動で決まるので、他メンバーが作った部屋も
            // そのまま通せる（当たらなかった物は Prop 扱いになる）。
            var root = GameObject.Find("=== ROOMS ===");
            if (root == null)
                return Build(new[] { "room2", "room7", "room9", "room12", "room15", "room16" });

            var rooms = new List<string>();
            foreach (Transform t in root.transform)
            {
                if (t.name == "roomtest") continue;         // 動作確認用なので出さない
                if (t.GetComponentsInChildren<MeshRenderer>(true).Length == 0) continue;
                rooms.Add(t.name);
            }
            return Build(rooms.ToArray());
        }

        public static string Build(string[] roomNames)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Models", "VisionMeshes");

            var echoMat = AssetDatabase.LoadAssetAtPath<Material>(EchoMatPath);
            if (echoMat == null) return "EchoMaterial.mat が無い";

            var log = new System.Text.StringBuilder();
            long totalTris = 0; int totalRend = 0; int baked = 0;

            foreach (var rn in roomNames)
            {
                var room = FindRoom(rn);
                if (room == null) { log.AppendLine(rn + ": 見つからない"); continue; }

                // 既存の生成物を消す。
                // ただし Vision_* の中に手で置いた物が紛れ込んでいることがあるので、
                // 消す前に助け出す。room13 のクイズ扉9枚(Doors1〜3)が実際に
                // Vision_Echo の下に入っていて、この掃除で消える寸前だった。
                // 生成物は必ず E_/T_/VisionFX_ で始まるので、それ以外は人が置いた物とみなす。
                foreach (var g in new[] { EchoGroup, ThermalGroup })
                {
                    var old = room.Find(g);
                    if (old == null) continue;
                    var rescued = new List<Transform>();
                    for (int i = old.childCount - 1; i >= 0; i--)
                    {
                        var c = old.GetChild(i);
                        if (c.name.StartsWith("E_") || c.name.StartsWith("T_") || c.name.StartsWith(FxPrefix)) continue;
                        rescued.Add(c);
                    }
                    foreach (var c in rescued) c.SetParent(room, true);   // 見た目を変えずに部屋直下へ退避
                    if (rescued.Count > 0)
                        log.AppendLine("  ⚠ " + rn + "/" + g + " に手置きの物が " + rescued.Count
                                     + " 個あった。消さずに部屋直下へ退避した: "
                                     + string.Join(", ", rescued.ConvertAll(x => x.name).ToArray()));
                    Object.DestroyImmediate(old.gameObject);
                }
                // 動く熱源の複製は元の親の下にぶら下がっているので、名前で探して消す
                var doomedFx = new List<GameObject>();
                foreach (var t in room.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name.StartsWith(FxPrefix)) doomedFx.Add(t.gameObject);
                foreach (var d in doomedFx) if (d != null) Object.DestroyImmediate(d);

                // --- 元になるレンダラーを集めて分類 ---
                var byTemp = new Dictionary<string, List<CombineInstance>>();
                // エコロケのチャンクは「建物」と「置いてある物」で分ける。
                //
                // 什器だけシルエット発光(_RimWeight)を効かせたいため。
                // 全部に効かせると、床・壁・天井は必ずどこかが視線に対して縁になるので
                // 画面全体が緑で埋まり、暗闇そのものが無くなってしまう（実際にそうなった）。
                // 建物は稜線だけ、物は輪郭全体、と分けるとエコロケ役の画面で
                // 「部屋の形」と「そこに在る物」が別の見え方になって読み分けられる。
                var byChunk = new Dictionary<Vector3Int, List<CombineInstance>>();
                var byChunkProp = new Dictionary<Vector3Int, List<CombineInstance>>();
                var temps = new List<Mesh>();   // UV貼り直し用の一時メッシュ。最後に破棄する
                int skipped = 0, used = 0;
                var floorBounds = new Bounds(); bool hasFloor = false;

                int proxied = 0;
                var bigOnes = new List<KeyValuePair<Renderer, string>>();
                // 大きい物は結合せず単体で複製するが、その経路も echo フラグを見ること。
                // 見落とすと「エコロケに出すな」と分類した物（レーザーなど）が
                // 大きいというだけで復活してしまう。実際にレーザーで踏んだ。
                var bigOnesEcho = new List<KeyValuePair<Renderer, string>>();

                // 動く物は結合しない。複製を元の子として置き、必ず一緒に動かす（CollectMovable 参照）
                var movable = CollectMovable(room);
                var movers = new List<KeyValuePair<Renderer, string>>();
                var moversEcho = new List<KeyValuePair<Renderer, string>>();

                // 動く物の中に残っている「前回までの生成物」を消す。
                // バケツの外に居るので上の掃除では拾えない。消さずに作り直すと
                // 同じ位置に複製が重なってZファイティングになる。
                // 消すのは生成物の命名規則(T_/E_/VisionFX_)に合う物だけ。
                // 手で置いた色分けコピー（room13 のクイズ扉など）は人の意図なので触らない。
                {
                    var stale = new List<GameObject>();
                    foreach (var t in movable)
                    {
                        if (t == null) continue;
                        if (t.gameObject.layer != LayerThermal && t.gameObject.layer != LayerEcho) continue;
                        var n2 = t.name;
                        if (n2.StartsWith("T_") || n2.StartsWith("E_") || n2.StartsWith(FxPrefix)) stale.Add(t.gameObject);
                    }
                    foreach (var d in stale) if (d != null) Object.DestroyImmediate(d);
                    if (stale.Count > 0) log.AppendLine("  " + rn + ": 動く物に残っていた旧生成物 " + stale.Count + " 個を削除");
                    if (stale.Count > 0) movable = CollectMovable(room);   // 消した分を反映して取り直す
                }

                foreach (var mr in room.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!mr.gameObject.activeInHierarchy) continue;
                    if (mr.gameObject.layer == LayerThermal || mr.gameObject.layer == LayerEcho) continue;
                    if (mr.name.StartsWith(FxPrefix)) continue;   // 前回の複製を材料にしない
                    if (IsGimmickOwned(mr.transform)) continue;   // 下のコメント参照

                    bool echo;
                    var key = Classify(mr, out echo);
                    if (key == null) { skipped++; continue; }

                    if (movable.Contains(mr.transform))
                    {
                        movers.Add(new KeyValuePair<Renderer, string>(mr, key));
                        if (echo) moversEcho.Add(new KeyValuePair<Renderer, string>(mr, key));
                        used++;
                        continue;
                    }


                    CombineInstance ci; bool standalone;
                    if (!MakeInstance(mr, room, key, out standalone, out ci))
                    {
                        if (standalone)
                        {
                            bigOnes.Add(new KeyValuePair<Renderer, string>(mr, key));
                            if (echo) bigOnesEcho.Add(new KeyValuePair<Renderer, string>(mr, key));
                            used++;
                        }
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
                            var bucket = IsArchitecture(key) ? byChunk : byChunkProp;
                            if (!bucket.ContainsKey(cell)) bucket[cell] = new List<CombineInstance>();

                            // エコロケ層は「UVの端＝輪郭」で線を引くので、UVが0〜1でない
                            // メッシュ（床タイル・手続き生成の棚・FBXの実寸UV）をそのまま渡すと
                            // 面全体が輪郭と判定されてベタ塗りになる。
                            //
                            // ただし全部を箱で代用すると、部屋の内装（回り縁・格天井・
                            // 壁パネル・棚板・照明器具）まで箱に潰れて「倉庫に箱が並んでいる」
                            // だけの部屋になってしまう。エコロケ役が形を伝える係である以上、
                            // ここが潰れるとその部屋の性格が誰にも伝わらない。
                            //
                            // そこで、CPU側でUVを貼り直せる軽いメッシュ（読み取り可・260三角形以下）は
                            // 素の形のまま使う。内装の造作はほぼ全部これに当たるので、
                            // ポリゴンをほとんど増やさずに部屋の中身が出る。
                            var srcE = EchoSource(mr, key);
                            CombineInstance eci;
                            if (srcE != null)
                            {
                                eci = new CombineInstance
                                {
                                    mesh = EchoUv(srcE),
                                    subMeshIndex = 0,
                                    transform = room.worldToLocalMatrix * mr.transform.localToWorldMatrix,
                                };
                            }
                            else
                            {
                                eci = ProxyInstance(mr, room, true, key);
                            }
                            temps.Add(eci.mesh);
                            bucket[cell].Add(eci);
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
                    if (IsBodyKey(kv.Value) && BlindBodyThermalBake.Apply(go)) baked++;
                    tTris += TriCount(kv.Key); tRend++;
                }
                // 動く物は元オブジェクトの子に置く。バケツに入れると開閉ギミックで置き去りになる。
                // 名前を FxPrefix にしておくと、作り直しのときに既存の掃除処理がそのまま拾ってくれる。
                foreach (var kv in movers)
                {
                    var bm = BlindThermalTable.Mat(kv.Value);
                    var go = CloneReal(kv.Key, kv.Key.transform, LayerThermal, bm, FxPrefix + "T_");
                    if (go == null) continue;
                    if (IsBodyKey(kv.Value) && BlindBodyThermalBake.Apply(go)) baked++;
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
                    // ⚠️ 床に開いている穴（アヒル部屋のプールなど）を板でふさがないこと。
                    //   この板は BoxProxy をブロックの大きさに引き伸ばした「ただの一枚板」で、
                    //   元の床メッシュの形は見ていない。プールのように床が抜けている部屋でも
                    //   板は矩形のまま張られるので、**エコロケ視点ではプールが床で埋まり、
                    //   ふちがどこにも出ない**（実際にそうなって落ちた）。
                    //
                    //   ⚠️ 穴の形を矩形で近似してはいけない。プールの縁は有機的な曲線で、
                    //   バウンズの矩形で抜くと**実際には床がある所まで消え、
                    //   縁の線も本物のふちから何メートルもずれた所に出る**（これも実際にやった）。
                    //   元の床メッシュ（穴が開いた状態で入っている）から輪郭を取り出して使う。
                    var cut = FloorCutout.For(room, rn);

                    int nx = Mathf.Max(1, Mathf.RoundToInt(floorBounds.size.x / EchoChunk));
                    int nz = Mathf.Max(1, Mathf.RoundToInt(floorBounds.size.z / EchoChunk));
                    float sx = floorBounds.size.x / nx, sz = floorBounds.size.z / nz;
                    for (int ix = 0; ix < nx; ix++)
                        for (int iz = 0; iz < nz; iz++)
                        {
                            var slab = Object.Instantiate(BoxProxy());
                            var uvSlab = EchoUv(slab);
                            float bx0 = floorBounds.min.x + sx * ix,       bx1 = bx0 + sx;
                            float bz0 = floorBounds.min.z + sz * iz,       bz1 = bz0 + sz;
                            float slabY = floorBounds.max.y - 0.02f;

                            var cis = new List<CombineInstance>();
                            System.Action<float, float, float, float> addBox = (ax0, ax1, az0, az1) =>
                                cis.Add(new CombineInstance
                                {
                                    mesh = uvSlab,
                                    subMeshIndex = 0,
                                    transform = room.worldToLocalMatrix * Matrix4x4.TRS(
                                        new Vector3((ax0 + ax1) * 0.5f, slabY, (az0 + az1) * 0.5f),
                                        Quaternion.identity,
                                        new Vector3(ax1 - ax0, 0.04f, az1 - az0)),
                                });

                            if (cut == null)
                            {
                                addBox(bx0, bx1, bz0, bz1);
                            }
                            else
                            {
                                // 穴のある部屋だけ、板を小さいタイルに割って
                                // 「床が実際にある所」だけ置く。曲がった縁も階段状に追える。
                                int tx = Mathf.Max(1, Mathf.RoundToInt(sx / FloorTile));
                                int tz = Mathf.Max(1, Mathf.RoundToInt(sz / FloorTile));
                                float dx = sx / tx, dz = sz / tz;
                                for (int i = 0; i < tx; i++)
                                    for (int j = 0; j < tz; j++)
                                    {
                                        float cx = bx0 + dx * (i + 0.5f), cz = bz0 + dz * (j + 0.5f);
                                        if (!cut.OnDeck(cx, cz)) continue;
                                        // 少し内側に寄せて1枚ずつ独立した四角にする。
                                        // くっついていると格子1枚に見えて縁が読めない（落とし穴部屋と同じ理由）。
                                        addBox(bx0 + dx * i + 0.05f, bx0 + dx * (i + 1) - 0.05f,
                                               bz0 + dz * j + 0.05f, bz0 + dz * (j + 1) - 0.05f);
                                    }
                            }

                            var fm = cis.Count > 0
                                ? Combine(cis, rn + "_Echo_Floor_" + ix + "_" + iz) : null;
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

                    // 切り抜いた穴の「ふち」と「深さ」を描く。
                    // 板を抜いただけだと、そこは何も描かれない＝暗闇と同じになり、
                    // 「床の続き」なのか「落ちる所」なのか判断できない。
                    // 落とし穴部屋と同じ作りにする：縁に沿った帯＋内壁の横縞。
                    // 帯を細かく区切って1枚ごとに 0〜1 の UV を貼るので、
                    // 輪郭しか描かないエコロケでも点線状の線として確実に出る。
                    var hm = cut != null ? BuildHoleRimMesh(cut, room, rn + "_Echo_HoleRim") : null;
                    if (hm != null)
                    {
                        var hg = new GameObject("E_FloorHole");
                        hg.transform.SetParent(eRoot.transform, false);
                        hg.layer = LayerEcho;
                        hg.AddComponent<MeshFilter>().sharedMesh = hm;
                        var hmr = hg.AddComponent<MeshRenderer>();
                        hmr.sharedMaterial = echoMat;
                        hmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        hmr.receiveShadows = false;
                        hmr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                        hmr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                        Recenter(hm, hg.transform);
                        AddReceiver(hg, hmr);
                        eTris += hm.triangles.Length / 3; eRend++;
                    }
                }
                var propMat = EchoPropMaterial(echoMat);
                foreach (var pair in new[] {
                    new KeyValuePair<Dictionary<Vector3Int, List<CombineInstance>>, Material>(byChunk, echoMat),
                    new KeyValuePair<Dictionary<Vector3Int, List<CombineInstance>>, Material>(byChunkProp, propMat) })
                foreach (var kv in pair.Key)
                {
                    bool isProp = pair.Value == propMat;
                    var mesh = Combine(kv.Value, rn + "_Echo" + (isProp ? "Prop" : "") + "_" + kv.Key.x + "_" + kv.Key.z);
                    if (mesh == null) continue;
                    var go = new GameObject("E_" + (isProp ? "P" : "") + kv.Key.x + "_" + kv.Key.z);
                    go.transform.SetParent(eRoot.transform, false);
                    go.layer = LayerEcho;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr2 = go.AddComponent<MeshRenderer>();
                    mr2.sharedMaterial = pair.Value;
                    mr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr2.receiveShadows = false;
                    mr2.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    mr2.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                    Recenter(mesh, go.transform);
                    AddReceiver(go, mr2);
                    eTris += mesh.triangles.Length / 3; eRend++;
                }
                foreach (var kv in bigOnesEcho)
                {
                    var em = EchoMatFor(kv.Key, rn, echoMat);
                    var go = CloneReal(kv.Key, eRoot.transform, LayerEcho, em, "E_");
                    if (go == null) continue;
                    AddReceiver(go, go.GetComponent<MeshRenderer>());
                    eTris += TriCount(kv.Key); eRend++;
                }
                // 動く物（サーモ側と同じ理由で元オブジェクトの子に置く）
                foreach (var kv in moversEcho)
                {
                    var em = EchoMatFor(kv.Key, rn, echoMat);
                    var go = CloneReal(kv.Key, kv.Key.transform, LayerEcho, em, FxPrefix + "E_");
                    if (go == null) continue;
                    AddReceiver(go, go.GetComponent<MeshRenderer>());
                    eTris += TriCount(kv.Key); eRend++;
                }

                foreach (var tm in temps) if (tm != null) Object.DestroyImmediate(tm);

                // --- アニメーションで動く熱源（燃える男・炎） ---
                var fxLog = new System.Text.StringBuilder();
                int fx = BuildMovingHeat(room, rn, fxLog);

                log.AppendLine(rn.PadRight(8)
                    + " 元 " + used.ToString().PadLeft(4) + " 個(簡易化 " + proxied + " / 除外 " + skipped + ")"
                    + " → サーモ " + tRend + " 枚/" + tTris.ToString("N0") + "tri"
                    + " / エコロケ " + eRend + " 枚/" + eTris.ToString("N0") + "tri"
                    + (fx > 0 ? " / 動く熱源 " + fx + "個" + fxLog : "")
                    + "  温度: " + string.Join(", ", Keys(byTemp)));
                totalTris += tTris + eTris; totalRend += tRend + eRend;
            }

            AssetDatabase.SaveAssets();
            // エコロケ層を作り直した以上、必ず受信機を登録し直す。
            //
            // EchoEmitter は「パルスを届ける相手」を receivers 配列で持っている。
            // この関数はエコロケ層(Vision_Echo)をまるごと作り直すので、
            // 前の EchoReceiver は破棄され、配列には欠損参照だけが残る。
            // 実際にそれが起きて、1部屋を作り直しただけで
            // 「エコロケ視点が全部の部屋で真っ暗」になった（719個中56個が欠損）。
            //
            // 手で [BLIND]→[エコロケ受信機を集め直す] を回す運用にしていたが、
            // 作り直すたびに必要な手順を人間の記憶に預けてはいけない。
            // 部屋を1つだけ作り直したときも、他の部屋の受信機ごと入れ直す。
            log.AppendLine("  " + EchoReceiverCollector.Collect());

            log.AppendLine(WireBodyDrift());
            if (baked > 0)
                log.AppendLine("  肉の厚みを焼き込んだ人体メッシュ " + baked + " 体 / 最後の1体: "
                             + BlindBodyThermalBake.LastReport);
            log.AppendLine("\n合計 " + totalRend + " レンダラー / " + totalTris.ToString("N0") + " tri を追加");
            return log.ToString();
        }

        /// <summary>
        /// 体温を持つレンダラーを部屋ごとに集めて ThermalBodyDrift に繋ぐ。
        ///
        /// 温度が固定だと、サーモ役の画面で人形は「止まった絵」にしかならない。
        /// 一度見れば以降は情報が増えず、二度目からはただの背景になる。
        /// 本物のサーモグラフィが不気味なのは、生きている物の温度が絶えず動くからで、
        /// その「動いている」という手がかりを与えるのがこの処理。
        ///
        /// 「暖かい個体」は部屋ごとに1体だけ選ぶ。16体のうち1体だけ体温が高く、
        /// 他と違うリズムで脈打つ ―― サーモ役だけが気付ける異常として置いている。
        /// どの個体になるかは名前から決まるので、作り直しても入れ替わらない。
        /// </summary>
        static string WireBodyDrift()
        {
            var driftType = System.Type.GetType("ThermalBodyDrift, Assembly-CSharp");
            if (driftType == null) return "  ThermalBodyDrift が未コンパイル。体温の揺らぎは未設定。";

            var rooms = GameObject.Find("=== ROOMS ===");
            if (rooms == null) return "";

            var report = new System.Text.StringBuilder();
            foreach (Transform room in rooms.transform)
            {
                // この部屋のサーモ層にある体温レンダラーを集める
                var bodies = new List<Renderer>();
                foreach (var r in room.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.gameObject.layer != LayerThermal) continue;
                    var m = r.sharedMaterial;
                    if (m == null) continue;
                    var mn = m.name;
                    if (mn == "Thermal_Body" || mn == "Thermal_Skin" || mn == "Thermal_Burning")
                        bodies.Add(r);
                }

                var holderName = "ThermalDrift";
                var existing = room.Find(holderName);
                if (bodies.Count == 0)
                {
                    if (existing != null) Object.DestroyImmediate(existing.gameObject);
                    continue;
                }

                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                var go = new GameObject(holderName);
                go.transform.SetParent(room, false);

                var beh = AddUdonComponent(go, driftType);
                if (beh == null) continue;

                var so = new SerializedObject(beh);
                var arr = so.FindProperty("targets");
                arr.arraySize = bodies.Count;
                for (int i = 0; i < bodies.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];

                // 「暖かい個体」を1体選ぶ。名前から決めるので作り直しても同じ個体が選ばれる。
                int warm = bodies.Count >= 3 ? StableIndex(room.name, bodies.Count) : -1;
                so.FindProperty("warmIndex").intValue = warm;
                so.ApplyModifiedProperties();

                var usb = beh as UdonSharp.UdonSharpBehaviour;
                if (usb != null && UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb) != null)
                    UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);

                report.AppendLine("  " + room.name + ": 体温の揺らぎ " + bodies.Count + "体"
                                + (warm >= 0 ? " (暖かい個体= #" + warm + ")" : ""));
            }
            return report.Length > 0 ? report.ToString().TrimEnd() : "";
        }

        /// <summary>U# の付与。素の AddComponent だと実機で動く UdonBehaviour が作られない。</summary>
        static Component AddUdonComponent(GameObject go, System.Type t)
        {
            var undoType = System.Type.GetType("UdonSharpEditor.UdonSharpUndo, UdonSharp.Editor");
            if (undoType != null)
            {
                var mi = undoType.GetMethod("AddComponent", new[] { typeof(GameObject), typeof(System.Type) });
                if (mi != null) return mi.Invoke(null, new object[] { go, t }) as Component;
            }
            return go.AddComponent(t);
        }

        const string EchoBigDir = "Assets/_BLIND/Art/Materials/Echo";

        /// <summary>動く熱源の複製に付ける接頭辞。作り直すときにこれで探して消す。</summary>
        const string FxPrefix = "VisionFX_";

        // -------------------------------------------------------------
        //  動く熱源（スキンメッシュ・パーティクル）
        // -------------------------------------------------------------
        /// <summary>
        /// アニメーションで動く熱源をサーモ層に出す。
        ///
        /// ここだけはメッシュ結合方式が使えない。room15 の「燃える男」は Animator で
        /// 部屋を1周歩くので、焼き固めた静的メッシュでは本体と一緒に動いてくれない。
        ///
        /// スキンメッシュは、元と同じ bones/rootBone を参照する複製を作れば、
        /// 追加のスクリプトなしで完全に同じ動きをする（描画はボーンの Transform だけを見ていて、
        /// 自分がどこにぶら下がっているかは関係ないため）。
        /// パーティクルは元と同じ親の下に複製を置いて追従させる。
        ///
        /// なお、この複製はエコロケ層には作らない。燃える男の位置を知っているのは
        /// サーモ役だけ、という状態を作るため。危険を伝えられるのがサーモ役しかいない、
        /// という場面が非対称協力のいちばん分かりやすい見せ場になる。
        /// </summary>
        static int BuildMovingHeat(Transform room, string rn, System.Text.StringBuilder log)
        {
            int n = 0;

            // --- 骨で動くメッシュ（人体など） ---
            foreach (var smr in room.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.gameObject.layer == LayerThermal || smr.gameObject.layer == LayerEcho) continue;
                if (smr.sharedMesh == null || smr.bones == null || smr.bones.Length == 0) continue;

                var all = (smr.name + "|" + (smr.transform.parent != null ? smr.transform.parent.name : "")
                         + "|" + (smr.transform.parent != null && smr.transform.parent.parent != null ? smr.transform.parent.parent.name : "")
                         + "|" + (smr.sharedMaterial != null ? smr.sharedMaterial.name : "")).ToLower();
                // 骨で動く＝人間とは限らない。監視カメラのように可動部だけ骨で動く物もあるので、
                // 「人」と分かる名前のときだけ体温を与える。それ以外は室温の小物扱いにする。
                string key;
                if (all.Contains("burn") || all.Contains("fire")) key = "Burning";
                else if (all.Contains("human") || all.Contains("body") || all.Contains("mannequin")
                      || all.Contains("manequin") || all.Contains("doll") || all.Contains("person")
                      || all.Contains("char") || all.Contains("avatar")) key = "Body";
                else key = "Prop";

                var mat = BlindThermalTable.Mat(key);
                if (mat == null) continue;

                var go = new GameObject(FxPrefix + "T_" + smr.name);
                go.transform.SetParent(smr.transform.parent, false);
                go.transform.localPosition = smr.transform.localPosition;
                go.transform.localRotation = smr.transform.localRotation;
                go.transform.localScale = smr.transform.localScale;
                go.layer = LayerThermal;

                var c = go.AddComponent<SkinnedMeshRenderer>();
                c.sharedMesh = smr.sharedMesh;
                c.bones = smr.bones;          // 元のボーンをそのまま参照する＝同じ動きをする
                c.rootBone = smr.rootBone;
                c.localBounds = smr.localBounds;
                c.updateWhenOffscreen = smr.updateWhenOffscreen;
                c.quality = SkinQuality.Bone2;   // 輪郭と温度しか伝えないので2ボーンで足りる
                var mats = new Material[Mathf.Max(smr.sharedMesh.subMeshCount, 1)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                c.sharedMaterials = mats;
                c.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.receiveShadows = false;
                c.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                c.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                log.Append("  " + smr.name + "→" + key + "(" + BlindThermalTable.Get(key).celsius.ToString("0") + "C)");
                n++;
            }

            // --- 炎・煙のパーティクル ---
            foreach (var ps in room.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.gameObject.layer == LayerThermal || ps.gameObject.layer == LayerEcho) continue;
                if (ps.gameObject.name.StartsWith(FxPrefix)) continue;
                var psr = ps.GetComponent<ParticleSystemRenderer>();
                if (psr == null || psr.sharedMaterial == null) continue;

                var mn = (psr.sharedMaterial.name + "|" + ps.gameObject.name).ToLower();
                Color tint;
                if (mn.Contains("fire") || mn.Contains("flame"))
                    tint = new Color(2.2f, 2.0f, 1.7f, 1f);   // 振り切れて真っ白になる
                else if (mn.Contains("smoke"))
                    tint = new Color(0.9f, 0.30f, 0.05f, 1f); // 上がっていく熱気。橙で見える
                else if (mn.Contains("steam") || mn.Contains("vapor"))
                    tint = new Color(0.15f, 0.55f, 0.9f, 1f); // 水蒸気は気化熱で冷たい
                else continue;                                 // 埃などの熱を持たない演出は出さない

                var clone = Object.Instantiate(ps.gameObject, ps.transform.parent);
                clone.name = FxPrefix + "T_" + ps.gameObject.name;
                clone.transform.localPosition = ps.transform.localPosition;
                clone.transform.localRotation = ps.transform.localRotation;
                clone.transform.localScale = ps.transform.localScale;

                // パーティクル以外は全部落とす（Animator や Light が付いていると二重に動く）
                foreach (var comp in clone.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;
                    if (comp is Transform || comp is ParticleSystem || comp is ParticleSystemRenderer) continue;
                    Object.DestroyImmediate(comp);
                }
                foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LayerThermal;
                foreach (var r2 in clone.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    r2.sharedMaterial = FxMat(r2.sharedMaterial, tint, rn);

                log.Append("  " + ps.gameObject.name + "→FX");
                n++;
            }
            return n;
        }

        /// <summary>
        /// パーティクル用のサーモ材質。元の材質（シェーダーもテクスチャも）をそのまま複製して
        /// 色だけ差し替える。炎の形はそのままに、温度だけ塗り替えたいため。
        /// </summary>
        static Material FxMat(Material src, Color tint, string room)
        {
            if (src == null) return null;
            if (!AssetDatabase.IsValidFolder(BlindThermalTable.MatDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Materials", "Thermal");
            var path = BlindThermalTable.MatDir + "/ThermalFX_" + room + "_" + src.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            m.shader = src.shader;
            m.CopyPropertiesFromMaterial(src);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(m);
            return m;
        }

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
            // 数メートルを超える物にシルエット発光を素の強さで掛けると、
            // 視線に対して寝ている面が全部光ってベタ塗りの塊になる。
            // プールの内壁や巨大アヒルがそうなって、**プールのふちがどこか分からなかった**。
            // 大きい物ほど鋭くして、本当に縁になっている所だけ光らせる。
            float span = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            m.SetFloat("_RimPower", span > 5f ? 9f : 3.5f);
            m.SetFloat("_GlowIntensity", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// EchoReceiver を付けて自分自身を対象にする。
        ///
        /// **必ず UdonSharpUndo.AddComponent を使うこと。**
        /// 素の GameObject.AddComponent だと C# 側のプロキシしか作られず、
        /// 対になる UdonBehaviour が生成されない。エディタでは動いているように見えるが、
        /// VRChat 実機で動くのは UdonBehaviour の方なので、アップロードすると
        /// 受信機が1つも反応しない（＝エコロケ役の視界が永久に真っ暗になる）。
        /// </summary>
        static void AddReceiver(GameObject go, Renderer r)
        {
            var rt = System.Type.GetType("EchoReceiver, Assembly-CSharp");
            if (rt == null) return;
            var undoType = System.Type.GetType("UdonSharpEditor.UdonSharpUndo, UdonSharp.Editor");
            Component rec = null;
            if (undoType != null)
            {
                var mi = undoType.GetMethod("AddComponent", new[] { typeof(GameObject), typeof(System.Type) });
                if (mi != null) rec = mi.Invoke(null, new object[] { go, rt }) as Component;
            }
            if (rec == null) rec = go.AddComponent(rt);   // 最後の手段（実機では動かない）
            var so = new SerializedObject(rec);
            var arr = so.FindProperty("targetRenderers");
            if (arr != null) { arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = r; }
            var gd = so.FindProperty("glowDuration");
            if (gd != null) gd.floatValue = EchoGlowDuration;
            so.ApplyModifiedProperties();

            // プロキシ側の値を実体の UdonBehaviour に流し込む（これを忘れると実機に反映されない）
            var usb = rec as UdonSharp.UdonSharpBehaviour;
            if (usb != null && UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(usb) != null)
                UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usb);
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
            var doomed = new List<GameObject>();
            foreach (var g in Object.FindObjectsOfType<GameObject>())
                if (g.name == EchoGroup || g.name == ThermalGroup || g.name.StartsWith(FxPrefix)) doomed.Add(g);
            foreach (var d in doomed) if (d != null) { Object.DestroyImmediate(d); n++; }
            return n + " 個の生成層を削除";
        }
    }
}
