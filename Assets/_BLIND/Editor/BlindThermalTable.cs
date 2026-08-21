using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// サーモ役に見せる温度の設計表。
    ///
    /// 値は実際のサーモグラフィで観測される温度を基準にしている。ただし現実の室内は
    /// 壁も床も棚も 17〜19℃ の範囲にほぼ収まってしまい、そのまま入れるとサーモ役の視界は
    /// 一色の紫で塗り潰されて何も分からない。そこで「順序と理由は現実どおり、
    /// 差は読めるところまで開く」方針で振ってある。どれがどちらの理由かは備考に書いた。
    ///
    /// 表示レンジは 12〜70℃、ガンマ 0.65。室温帯(15〜21℃)が紫〜青、
    /// 人肌(36℃)が緑〜黄、蛍光灯(52℃)が黄〜橙、安定器(68℃)が赤になる。
    /// </summary>
    public static class BlindThermalTable
    {
        public const string MatDir = "Assets/_BLIND/Art/Materials/Thermal";

        public struct Temp
        {
            public string key;
            public float celsius;
            public float dim;      // 1=そのまま見える 0=真っ黒（遮蔽はする）
            public string note;

            /// <summary>
            /// この材質だけ表示レンジの上限を変える。0 なら共通の DisplayMax を使う。
            ///
            /// 実機のサーマルカメラは映っている範囲に合わせて自動でレンジを詰める。
            /// 12〜70℃ は「炎がある部屋」に合わせた目盛りなので、
            /// 炎の無い部屋で人体(34〜36℃)を映すと目盛りの真ん中＝緑にしかならず、
            /// 「冷たい物体」に見えてしまう。人体しか熱源が無い部屋では
            /// カメラは人体の帯まで目盛りを詰めるので、上限を下げるのが実機に近い。
            /// </summary>
            public float displayMax;

            public Temp(string k, float c, float d, string n)
            { key = k; celsius = c; dim = d; note = n; displayMax = 0f; }

            public Temp(string k, float c, float d, float dmax, string n)
            { key = k; celsius = c; dim = d; note = n; displayMax = dmax; }
        }

        /// <summary>
        /// dim の考え方：
        /// 床・壁・天井までサーモで見えると、サーモ役だけで空間の形が読めてしまい、
        /// 「エコロケ役が形を伝える」という非対称協力の前提が崩れる。
        /// そこで建物側は 0.02〜0.03 の「言われれば分かる」程度まで落とし、
        /// 什器は 0.05〜0.09 の気配だけ、熱源だけを 1.0 で見せる。
        /// 不透明のままなので壁の遮蔽は効いたまま。
        ///
        /// dim はシェーダー側で距離の効きにも使っている。dim が小さいものほど
        /// 手前でしか見えず、5m も離れれば背景に溶ける（ThermalSurface の _FadeNear）。
        /// 減光だけだと「近寄れば結局全部読める」ので、その穴をここで塞いでいる。
        /// </summary>
        public static readonly Temp[] All =
        {
            // --- 建物（ほぼ黒。エコロケ役の領分） ---
            // ここは意図的にほとんど上げていない（+10%程度）。
            // 床・壁・天井が読めるようになった瞬間にサーモ役は間取りを自力で把握でき、
            // 「エコロケ役に形を教えてもらう」という本作の背骨が折れる。
            // 画面が寂しいからといって触っていいのはこの3行ではなく、下の什器帯。
            new Temp("FloorStone",  15.0f, 0.022f, "コンクリ・テラゾー床。最も冷たい。形はエコロケ役の担当なのでほぼ黒"),
            new Temp("Wall",        18.0f, 0.028f, "塗装壁・石膏。室温そのもの。ほぼ黒だが遮蔽はする"),
            new Temp("Ceiling",     21.5f, 0.034f, "吊り天井。暖気が溜まりわずかに温かい"),

            // --- 什器（気配だけ。手を伸ばす距離で「何かある」と分かる程度） ---
            // 建物より一段上げ幅を大きくしてある（+50%前後）。
            // 「そこに物がある」はサーモ役が伝えてよい情報（＝ぶつかる危険の予告）で、
            // 「部屋がどういう形か」とは別物。ここを上げると画面が空でなくなる一方、
            // 建物が黒いままなので空間の形は依然として読めない。
            // 上げすぎの目安：什器が2〜3m先で判別できるようになったら行き過ぎ。
            new Temp("Metal",       16.0f, 0.075f, "スチール書架。金属は放射率が低く実際より冷たく写る（実測どおりの現象）"),
            new Temp("Prop",        19.0f, 0.095f, "だるま・人形など張り子や樹脂の小物"),
            new Temp("Wood",        20.0f, 0.105f, "木箱・木製家具。断熱性が高く室温よりわずかに高い"),
            new Temp("Cardboard",   20.5f, 0.105f, "段ボール。木材と同じ理由でやや高い"),
            new Temp("CRTOff",      17.5f, 0.100f, "電源の切れたCRT。ただの箱なので室温"),
            new Temp("LampOff",     19.0f, 0.115f, "切れている照明パネル。樹脂板が室温のまま。どれが死んでいるかが分かる"),
            new Temp("Fabric",      22.0f, 0.130f, "布・カーペット。放熱しにくいぶん高め"),
            new Temp("Water",       11.0f, 0.240f, "溜まり水。気化熱で室温より数℃低い。落ちると危ないので例外的に読ませる"),

            // --- 熱源（サーモ役の領分。ここだけ全開） ---
            // 配管は「生きている設備」としてはっきり読ませる。
            // 長い管なので天井の一本線で間取りがある程度読めてしまう副作用はあるが、
            // 通っている配管をサーモ役が追えること自体を道しるべとして使う判断。
            new Temp("Duct",        38.0f, 0.550f, "配管・空調ダクト。中を温水や排気が通っているぶん室温よりはっきり高い。" +
                                                   "サーモ役には天井や壁を這う線として見え、部屋から部屋への繋がりを追える"),
            // Body / Skin は room16 でしか使っていない。
            // room16 の最高温度は肌の 36℃ で、蛍光灯も炎も無い。
            // それを 12〜70℃ の目盛りで映すと人体は目盛りの中央＝緑になり、
            // 「体温がある」はずの人形が一番冷たそうな色で出てしまっていた。
            // 実機なら映っている範囲に合わせて目盛りが 40℃ 付近まで詰まるので、
            // 上限を 40 にして人体を橙〜赤に置く。この2行だけの局所的な変更で、
            // 他の部屋の見え方（蛍光灯や炎の白飛び）には影響しない。
            new Temp("Body",        34.0f, 1.00f, 40.0f, "人体の衣服表面。素肌は33〜36℃、服の上からだとこのくらい"),
            new Temp("Skin",        36.0f, 1.00f, 40.0f, "人体の露出した肌"),
            new Temp("LampDim",     32.0f, 0.85f, "ちらついている・調光された照明パネル。生きてはいるが出力が落ちている"),
            new Temp("CRTOn",       47.0f, 1.00f, "通電中のCRTの筐体。ブラウン管の排熱で 45〜50℃"),
            // 生きている照明は白く振り切れさせる。
            // 人体を 40℃ 目盛りに移した結果、52℃ の照明と 34℃ の人体が
            // どちらも橙になり、ぱっと見で区別がつかなくなった。
            // 「白＝まだ電気が来ている物 / 橙＝体温のある物」と色で分けておくと、
            // サーモ役は形を確かめる前に、それがどちらの種類かを言える。
            new Temp("Lamp",        52.0f, 1.00f, 53.0f, "蛍光管そのもの。管壁は 40〜60℃"),
            new Temp("Burning",     66.0f, 1.00f, "全身が燃えている人体。実際は炎と同じく振り切れるが、炎の中で" +
                                                  "「人の形」だけは輪郭として読めてほしいので、炎より一段低く置いてある"),
            new Temp("Ballast",     68.0f, 1.00f, "照明器具の本体・安定器。蛍光灯で最も熱い部位"),
            // 表示レンジの上限が70℃なので、それを超える温度は全部 h=1.0 に張り付いて
            // 同じ真っ白になる。88℃ にしていた時は炎(95℃)と区別が付かず、
            // さらにレーザーの発光板が画面を覆って一面白飛びしていた。
            // 62℃ ならレンジ内に収まり、赤〜橙のはっきりした線として出る。
            new Temp("Laser",       62.0f, 1.00f, "room14 の警備レーザー。白飛びさせず赤い線として見せる。" +
                                                  "炎(95℃/真っ白)とは色で区別が付く。" +
                                                  "エコロケ役にも過去人にも一切見えないので、この線を伝えられるのはサーモ役だけ"),
            new Temp("Fire",        95.0f, 1.00f, "炎・強い熱源。レンジ上限で振り切れて真っ白になる"),
        };

        static Dictionary<string, Temp> _map;
        public static Temp Get(string key)
        {
            if (_map == null) { _map = new Dictionary<string, Temp>(); foreach (var t in All) _map[t.key] = t; }
            return _map.ContainsKey(key) ? _map[key] : _map["Wall"];
        }

        public const float DisplayMin = 12f;
        public const float DisplayMax = 70f;
        public const float Gamma = 0.65f;

        /// <summary>dim=0 の物が見える距離(m)。建物はこの距離で背景に溶ける。</summary>
        public const float FadeNear = 5f;
        /// <summary>dim=1 の物が見える距離(m)。熱源は部屋の端からでも見える。</summary>
        public const float FadeFar = 200f;

        /// <summary>温度ごとのマテリアルを作る。同じ温度は同じマテリアルを共有する。</summary>
        [MenuItem("BLIND/vision/1. サーマル材質を作り直す")]
        public static string BuildMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/_BLIND/Art/Materials", "Thermal");

            var sh = Shader.Find("BLIND/ThermalSurface");
            if (sh == null) return "BLIND/ThermalSurface が見つからない（コンパイル待ち？）";

            var log = new System.Text.StringBuilder("温度  減光  マテリアル                 備考\n");
            foreach (var t in All)
            {
                var path = MatDir + "/Thermal_" + t.key + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.shader = sh;
                m.SetFloat("_TempC", t.celsius);
                m.SetFloat("_TempMin", DisplayMin);
                m.SetFloat("_TempMax", t.displayMax > 0f ? t.displayMax : DisplayMax);
                m.SetFloat("_TempGamma", Gamma);
                m.SetFloat("_HeatIntensity", 1f);
                // ざらつきは控えめに。暗く落とした面ほど 8bit の階調が粗くなるので、
                // ムラを強く入れるとブロック状のノイズ（ガビガビ）として目立ってしまう。
                m.SetFloat("_EdgeCool", 0.10f);
                m.SetFloat("_Noise", 0.015f);
                m.SetFloat("_Grain", t.celsius > 30f ? 0.15f : 0.05f);
                m.SetFloat("_Dim", t.dim);
                m.SetFloat("_FadeNear", FadeNear);
                m.SetFloat("_FadeFar", FadeFar);
                EditorUtility.SetDirty(m);
                log.AppendLine(t.celsius.ToString("00.0").PadRight(6) + t.dim.ToString("0.00").PadRight(6)
                             + ("Thermal_" + t.key).PadRight(26) + t.note);
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        public static Material Mat(string key)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Thermal_" + key + ".mat");
        }
    }
}
