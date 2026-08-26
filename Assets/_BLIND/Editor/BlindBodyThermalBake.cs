using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 人体メッシュに「芯からの遠さ」を頂点カラーとして焼き込む。
    ///
    /// なぜ高さ基準ではだめなのか:
    ///   最初は world Y（床からの高さ）を体の軸の代わりに使っていた。立っている人形なら
    ///   胴・頭・足先が分かれるので一見それらしく見えるが、実際の人体の温度分布は
    ///   高さでは決まらない。横に伸ばした腕は胴と同じ高さにあるのに手先は 10℃ 近く低く、
    ///   逆さまに倒れている人形は足のほうが高い位置に来る。room16 には
    ///   寝ている人形も逆立ちしている人形もいるので、高さ基準だと
    ///   そういう個体が丸ごと一色になってしまう。
    ///
    /// 何で決まるのか:
    ///   体表の温度は、その場所の「肉の厚み」でほぼ決まる。
    ///   体積あたりの発熱に対して放熱するのは表面なので、細い所ほど冷え、
    ///   太い所ほど芯の温度を保つ。血流も太い部位ほど多い。
    ///   実測でだいたい 胴 22cm / 太もも 13cm / 上腕 9cm / 前腕 6cm / 手 2cm で、
    ///   これは体温分布図の 赤(胴) → 黄緑(太もも) → 水色(手足先) の並びとそのまま一致する。
    ///
    /// やること:
    ///   頂点ごとに法線の逆向きへレイを撃って肉の厚みを測り、
    ///   0〜1 に正規化して頂点カラー r に入れる。ポーズにも向きにも依存しない。
    ///   焼いたメッシュは VisionMeshes に .asset として保存する。元のインポート
    ///   アセットには絶対に書き込まない（過去人視点にも波及してしまう）。
    ///
    /// 頂点カラー a = 0.5 を「焼き込み済み」の目印にしている。
    /// 頂点カラーを持たないメッシュには GPU が (1,1,1,1) を渡すので、
    /// シェーダー側は a を見るだけで焼き込みの有無を判別でき、
    /// 焼けなかったメッシュは従来の高さ基準へ自動的に落ちる。
    /// </summary>
    public static class BlindBodyThermalBake
    {
        const string MeshDir = "Assets/_BLIND/Art/Models/VisionMeshes";

        // 厚みのしきい値は絶対値(m)ではなく、その体の「全長に対する比」で持つ。
        //
        // room16 の巨大な腕は天井から生えている 8m の腕で、実寸で測ると
        // どこを取っても 18cm より遥かに太く、絶対値で判定すると全体が
        // 「芯」＝真っ赤な一色になってしまう。人体として見せたいのは
        // 実際の温度ではなく体の構造なので、比で持てば大きさに関係なく
        // 手先が冷えて胴が熱い、という同じ絵が出る。
        //
        // 係数は身長 1.9m の人形での実測から決めてある：
        //   手先 2cm = 全長の 1.1% / 胴 18cm = 全長の 9.5%

        /// <summary>これより細い所は完全な末端（手先・指・足先）とみなす。全長比。</summary>
        const float ThinRatio = 0.011f;

        /// <summary>これより太い所は完全な芯（胴）とみなす。全長比。</summary>
        const float ThickRatio = 0.095f;

        /// <summary>厚み測定レイの最大長。全長比。これを超えたら「太い」側に振る。</summary>
        const float ProbeRatio = 0.32f;

        /// <summary>
        /// 厚みに対する「重心への近さ」の混ぜ具合。0 なら厚みだけ。
        ///
        /// 最初は 0.25 混ぜていた。頭蓋は厚いので厚みだけだと胴と同じ温度になり、
        /// それを補正するつもりだった。しかし実測すると逆効果だった：
        /// 立っている人形の重心は腰のあたりにあるので、いちばん熱くしたい胸が
        /// 重心から遠く、この項が胸の温度を下げてしまう（胴の coreness が
        /// 1.0 ではなく 0.87 で頭打ちになり、赤が一度も出なかった）。
        ///
        /// そもそも実際の体温分布図でも頭部は胴と同じくらい熱い。
        /// 補正する必要が無かったので 0 にしてある。
        /// 値そのものは頂点カラー g に残してあるので、後から使いたくなれば拾える。
        /// </summary>
        const float CentralWeight = 0f;

        /// <summary>厚みをならす格子の一辺。全長比。レイ1本の測定は折り目で暴れるのでぼかす。</summary>
        const float SmoothRatio = 0.013f;

        /// <summary>ならす回数。減面済みメッシュは頂点が粗いので2回かけないと面が見える。</summary>
        const int SmoothPasses = 2;

        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>直近の焼き込み結果の要約。ビルドログに出して数値で確認するため。</summary>
        public static string LastReport = "";

        /// <summary>
        /// レンダラーのメッシュを、芯からの遠さを焼き込んだ版に差し替える。
        /// 焼けなかった場合は何もしない（シェーダー側が高さ基準に落ちる）。
        /// </summary>
        public static bool Apply(GameObject go)
        {
            if (go == null) return false;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;

            var baked = Bake(mf.sharedMesh, go.transform.lossyScale);
            if (baked == null) return false;
            mf.sharedMesh = baked;
            return true;
        }

        public static Mesh Bake(Mesh src, Vector3 scale)
        {
            if (src == null || !src.isReadable) return null;

            // 体の全長(m)。しきい値は全部これに対する比で決まる。
            var bb = src.bounds;
            var wsz = new Vector3(bb.size.x * Mathf.Abs(scale.x),
                                  bb.size.y * Mathf.Abs(scale.y),
                                  bb.size.z * Mathf.Abs(scale.z));
            float span = Mathf.Max(wsz.x, Mathf.Max(wsz.y, wsz.z));
            if (span < 1e-3f) return null;

            // 同じメッシュでも全長が違えば厚みの絶対値が変わる（巨大な腕がこれ）。
            // 全長を名前に含めて別アセットにする。10cm 刻みで丸めているので、
            // 人形の個体差(±5%)で無駄なコピーが増えることはない。
            string name = Sanitize(src.name) + "_core" + Mathf.RoundToInt(span * 10f);
            string path = MeshDir + "/" + name + ".asset";

            Mesh cached;
            if (Cache.TryGetValue(path, out cached) && cached != null) return cached;

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null && existing.vertexCount == src.vertexCount)
            {
                Cache[path] = existing;
                return existing;
            }

            var verts = src.vertices;
            var norms = src.normals;
            if (verts.Length == 0) return null;
            if (norms == null || norms.Length != verts.Length)
            {
                src.RecalculateNormals();
                norms = src.normals;
            }

            var thickness = Probe(src, verts, norms, scale, span * ProbeRatio);
            if (thickness == null) return null;

            // 格子でならす。レイ1本の測定は、腕と胴のすき間や服の折り目をかすめると
            // 実際より薄く出る。そのまま色にすると胴に冷たい斑点が散って
            // 「傷んだ肉」みたいな汚い絵になるので、周囲 3x3x3 マスで平均する。
            // すき間をまたいで少しにじむが、熱伝導としてはむしろ正しい。
            //
            // 2回かけている。人形は全身で 1,441 頂点しかない減面済みメッシュなので、
            // 頂点カラーはそのまま面ごとの平坦な色として出る。1回だけだと
            // 胴に角ばったオレンジの継ぎ目が見えた（頂点補間の三角形がそのまま出た）。
            // もう1回ならすと、面の境目が消えて連続した濃淡になる。
            for (int pass = 0; pass < SmoothPasses; pass++)
                Smooth(verts, scale, thickness, span * SmoothRatio);

            // 重心への近さ。厚みだけだと頭蓋（厚い）が胴と同じ温度になりがちなので、
            // 補助として少しだけ混ぜる。ローカル空間で測るのでポーズに依存しない。
            float maxR = Mathf.Max(bb.extents.magnitude, 1e-4f);
            float thin = span * ThinRatio, thick0 = span * ThickRatio;

            // 実測した厚みの分布に合わせて目盛りを取り直す（オートレンジ）。
            //
            // 比で決めたしきい値だけだと、胴の測定値がしきい値に届かず
            // 「いちばん熱い所でも黄色止まり」になる。ならしの平均化で
            // 山が削れるうえ、人形の胴は本物の人間より薄いのが原因。
            // そこで実際に測れた厚みの 3%〜92% 点を目盛りの両端に割り当てる。
            // 実機のサーマルカメラが「映っている範囲に合わせてレンジを詰める」のと
            // 同じ考え方で、どの人形も手足＝水色、胴＝赤 まで必ず振れるようになる。
            //
            // ただし厚みがほとんど一様な物（板や壁に化けた誤検出）にこれを掛けると
            // 測定誤差だけが引き伸ばされて汚くなるので、分布に幅が無いときは
            // 比のしきい値をそのまま使う。
            {
                var sorted = (float[])thickness.Clone();
                System.Array.Sort(sorted);
                // 上端は 82% 点。100% 点（＝いちばん厚い1点）に合わせると、
                // 胴の大半がそこに届かず「胴の中心の一点だけ赤くて、あとは橙」になる。
                // 体温分布図でも赤いのは胴ぜんぶなので、上位2割はまとめて振り切らせる。
                float lo = sorted[Mathf.Clamp((int)(sorted.Length * 0.03f), 0, sorted.Length - 1)];
                float hi = sorted[Mathf.Clamp((int)(sorted.Length * 0.82f), 0, sorted.Length - 1)];
                if (hi - lo > span * 0.02f) { thin = lo; thick0 = hi; }
            }

            var colors = new Color32[verts.Length];
            float tMin = float.MaxValue, tMax = 0f; double tSum = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                float th = thickness[i];
                tMin = Mathf.Min(tMin, th); tMax = Mathf.Max(tMax, th); tSum += th;

                float thickT = Mathf.InverseLerp(thin, thick0, th);
                float central = 1f - Mathf.Clamp01((verts[i] - bb.center).magnitude / maxR);
                float core = Mathf.Clamp01(thickT * (1f - CentralWeight) + central * CentralWeight);

                colors[i] = new Color32(
                    (byte)Mathf.RoundToInt(core * 255f),
                    (byte)Mathf.RoundToInt(central * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(th / (span * ProbeRatio)) * 255f),
                    128);   // a=0.5 が「焼き込み済み」の目印
            }

            var mesh = Object.Instantiate(src);
            mesh.name = name;
            mesh.colors32 = colors;

            if (!AssetDatabase.IsValidFolder(MeshDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Models", "VisionMeshes");
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            LastReport = name + ": 厚み " + (tMin * 100f).ToString("0.0") + "〜"
                       + (tMax * 100f).ToString("0.0") + "cm (平均 "
                       + (tSum / verts.Length * 100f).ToString("0.0") + "cm)";
            Cache[path] = mesh;
            return mesh;
        }

        /// <summary>
        /// 頂点ごとに法線の逆向きへレイを撃って肉の厚みを測る。
        /// 裏面に当たらせる必要があるので queriesHitBackfaces を一時的に立てる。
        /// </summary>
        static float[] Probe(Mesh src, Vector3[] verts, Vector3[] norms, Vector3 scale, float maxProbe)
        {
            var holder = new GameObject("__ThermalProbe");
            holder.hideFlags = HideFlags.HideAndDontSave;
            var t = holder.transform;
            t.position = Vector3.zero;
            t.rotation = Quaternion.identity;
            t.localScale = scale;

            var col = holder.AddComponent<MeshCollider>();
            col.sharedMesh = src;

            bool prevBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();

            var result = new float[verts.Length];
            var miss = new List<int>();
            try
            {
                float eps = maxProbe * 0.005f;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 p = t.TransformPoint(verts[i]);
                    Vector3 n = t.TransformDirection(norms[i]).normalized;
                    RaycastHit hit;
                    // 自分自身の面をすぐ拾わないよう、わずかに内側から撃つ
                    if (col.Raycast(new Ray(p - n * eps, -n), out hit, maxProbe))
                        result[i] = hit.distance + eps;
                    else
                    {
                        result[i] = -1f;   // 穴の空いたメッシュ。あとで周囲から埋める
                        miss.Add(i);
                    }
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = prevBack;
                Object.DestroyImmediate(holder);
            }

            // 貫通した頂点は、当たった頂点の中央値で埋める。
            // 「太い」に倒すと閉じていないメッシュが丸ごと真っ赤になり、
            // 「細い」に倒すと丸ごと真っ青になる。どちらも嘘なので中庸に置く。
            if (miss.Count > 0)
            {
                if (miss.Count == verts.Length) return null;   // 一度も当たらない＝面が裏返っている
                var hits = new List<float>(verts.Length - miss.Count);
                for (int i = 0; i < result.Length; i++) if (result[i] >= 0f) hits.Add(result[i]);
                hits.Sort();
                float med = hits[hits.Count / 2];
                foreach (var i in miss) result[i] = med;
            }
            return result;
        }

        /// <summary>厚みを格子で平均してならす。頂点が割れていても位置で拾うので継ぎ目が出ない。</summary>
        static void Smooth(Vector3[] verts, Vector3 scale, float[] value, float cellSize)
        {
            float SmoothCell = Mathf.Max(cellSize, 1e-4f);
            var sum = new Dictionary<Vector3Int, float>();
            var cnt = new Dictionary<Vector3Int, int>();
            var cell = new Vector3Int[verts.Length];

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = Vector3.Scale(verts[i], scale);
                var c = new Vector3Int(
                    Mathf.FloorToInt(p.x / SmoothCell),
                    Mathf.FloorToInt(p.y / SmoothCell),
                    Mathf.FloorToInt(p.z / SmoothCell));
                cell[i] = c;
                float sv; int sc;
                sum[c] = (sum.TryGetValue(c, out sv) ? sv : 0f) + value[i];
                cnt[c] = (cnt.TryGetValue(c, out sc) ? sc : 0) + 1;
            }

            var outv = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var c = cell[i];
                float acc = 0f; int n = 0;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var k = new Vector3Int(c.x + dx, c.y + dy, c.z + dz);
                            float sv;
                            if (!sum.TryGetValue(k, out sv)) continue;
                            acc += sv; n += cnt[k];
                        }
                outv[i] = n > 0 ? acc / n : value[i];
            }
            System.Array.Copy(outv, value, verts.Length);
        }

        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Mesh";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }
    }
}
