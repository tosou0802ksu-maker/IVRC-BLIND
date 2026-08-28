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

            /// <summary>
            /// この材質だけ表示レンジの下限を変える。Unset なら共通の DisplayMin を使う。
            ///
            /// 冷たい物を「冷たい色」で強く見せたいときに使う。共通レンジ(12〜70℃)だと
            /// 12℃以下は全部いちばん暗い紫に潰れてしまい、**冷たさが情報にならない**。
            /// 下限を下げると、その材質だけ紫〜青の帯を使い切れる。
            /// </summary>
            public float displayMin;
            public const float Unset = -999f;

            public Temp(string k, float c, float d, string n)
            { key = k; celsius = c; dim = d; note = n; displayMax = 0f; displayMin = Unset; }

            public Temp(string k, float c, float d, float dmax, string n)
            { key = k; celsius = c; dim = d; note = n; displayMax = dmax; displayMin = Unset; }

            public Temp(string k, float c, float d, float dmin, float dmax, string n)
            { key = k; celsius = c; dim = d; note = n; displayMax = dmax; displayMin = dmin; }
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

            // --- 落とし穴フロア（room5 / room10 / room18）---
            // 通常の床(FloorStone)は dim=0.022 でほぼ真っ黒にしてある。「空間の形は
            // エコロケ役の担当」という切り分けのためで、それ自体は正しい。
            // だが落とし穴フロアだけは「サーモ役にしか見えない穴」を成立させる必要があり、
            // 床そのものが読めないと穴の欠けも読めない。そこでこの2つだけ例外的に上げる。
            // 設定上は「金属の踏み板が空調で温まっている / 穴の底だけ外気で冷えている」。
            // 踏み板(24℃)と穴の底(11℃)を13℃離してあるので、穴は青黒い矩形として抜ける。
            // 踏み板は周囲の床と同じ温度・暗さにし、穴の中だけが異常な熱（または冷気）を発しているようにする。
            // これにより、サーモ役には「穴だけがくっきりと色付きで見える」ようになる。
            new Temp("PitDeck",     15.0f, 0.022f, "落とし穴の安全な床。周囲の床(FloorStone)と完全に同化させる"),
            // 落とし穴は「冷たい」で見せる。
            //
            // 45℃の熱源にしていたときは、CRT(47℃)・温水管(46℃)と同じ黄色に並んでしまい、
            // **サーモ視点が黄色一色**になっていた（穴も機械も配管も同じ色）。
            // 5m の縦坑は熱源が無く下から冷気が上がってくるので、
            // 室内でいちばん冷たい面になるのが本来。表示レンジの下限だけ 0℃ に下げて、
            // 誰も使っていない紫〜青の帯を穴に割り当てる。
            // 暖色の物と色相で完全に分かれるので、遠くからでも穴だと分かる。
            new Temp("PitVoid",      3.0f, 1.000f, 0f, DisplayMax,
                     "落とし穴の内側。冷気の溜まった縦坑。青紫で暖色の熱源と色相を分ける"),

            // --- 赤・青・緑のゲートボタン ---
            // 通電している操作盤。サーモ役にも「押すべき物」として見えないと
            // 3人のうち1人だけがボタンを探せない、という事故になる。
            new Temp("Button",      33.0f, 0.900f, "赤/青/緑ゲートボタンの筐体。通電した操作盤としてはっきり読ませる"),
            new Temp("ButtonLit",   58.0f, 1.000f, "押した後のゲートボタン。点灯して発熱し、押済みかどうかが一目で分かる"),

            // --- 熱源（サーモ役の領分。ここだけ全開） ---
            // 配管は「生きている設備」としてはっきり読ませる。
            // 長い管なので天井の一本線で間取りがある程度読めてしまう副作用はあるが、
            // 通っている配管をサーモ役が追えること自体を道しるべとして使う判断。
            // room11 の天井は配線束と配管がびっしり通っているが、全部 Duct(38℃) だったため
            // 一面がのっぺりした同じ色になり、どれが1本の線なのかも読めなかった。
            // 実際の天井裏は、電流が流れている線だけが熱く、死んだ線は室温のまま。
            // 4段階に散らすと、天井が「温度のまだら模様」になって線の走り方が見える。
            new Temp("DuctDead",    21.0f, 0.300f, "電気の来ていない配線。室温のまま。room11 の天井をまだらにするための一番冷たい帯"),
            new Temp("DuctWarm",    29.0f, 0.420f, "軽く電流の流れている配線。ほんのり温かい"),
            new Temp("DuctHot",     46.0f, 0.700f, "負荷のかかっている配線・温水管。触ると熱いくらい"),
            new Temp("Duct",        38.0f, 0.550f, "配管・空調ダクト。中を温水や排気が通っているぶん室温よりはっきり高い。" +
                                                   "サーモ役には天井や壁を這う線として見え、部屋から部屋への繋がりを追える"),
            // Body / Skin は room16 でしか使っていない。
            // room16 の最高温度は肌の 36℃ で、蛍光灯も炎も無い。
            // それを 12〜70℃ の目盛りで映すと人体は目盛りの中央＝緑になり、
            // 「体温がある」はずの人形が一番冷たそうな色で出てしまっていた。
            // 実機なら映っている範囲に合わせて目盛りが 40℃ 付近まで詰まるので、
            // 上限を 40 にして人体を橙〜赤に置く。この2行だけの局所的な変更で、
            // 他の部屋の見え方（蛍光灯や炎の白飛び）には影響しない。
            // 上限を 40→43 に上げてある。人体プロファイル(_BodyVein/_BodyMottle)で
            // 胴の局所高温が 34+1.6+2.2≒37.8℃ まで上がるので、上限40 のままだと
            // そこが白飛びの帯(h>0.93)に入り、胴が真っ白に潰れて構造が消えた。
            // 43 にすると 37.8℃ が h≒0.86 に収まり、赤〜橙のまま階調が残る。
            // 芯（胴の中心）の温度をそのまま入れる。手足の先までの落差は
            // シェーダー側の人体プロファイルが肉の厚みから作るので、ここは
            // 「体温分布図でいちばん赤い所」の数字を書けばよい。
            new Temp("Body",        36.5f, 1.00f, 37.8f, "人体の胴の芯。手足の先は肉の厚みに応じて26.5℃まで落ちる"),
            new Temp("Skin",        36.6f, 1.00f, 37.8f, "人体の露出した肌。芯は37℃弱、末端は肉の厚み次第で下がる"),
            new Temp("LampDim",     32.0f, 0.85f, "ちらついている・調光された照明パネル。生きてはいるが出力が落ちている"),
            // ブラウン管の画面は4段階に散らす。全部 47℃ にしていたら
            // 山積みのテレビが「同じ黄色い板の群れ」にしかならなかった。
            // 停電したビルで何台か生き残っている、という絵にしたほうが情報量が多く、
            // サーモ役が「どれが生きているか」を伝えられる。
            new Temp("CRTWarm",     30.0f, 1.00f, "かろうじて通電している画面。緑。ついさっきまで映っていた程度"),
            new Temp("CRTOn",       47.0f, 1.00f, "通電中のCRTの画面。ブラウン管の排熱で 45〜50℃"),
            new Temp("CRTHot",      60.0f, 1.00f, "長時間つけっぱなしの画面。橙〜赤。この部屋でいちばん熱い機械"),
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

                // 人体だけは表示レンジの「下限」も体温帯まで持ち上げる。
                //
                // 共通の下限 12℃ は室温(15〜21℃)を見せるための目盛りで、
                // これを人体に使うと 21〜38℃ の体内の温度差がランプの上 1/3 に
                // 押し込まれ、体全体が黄〜橙一色になる（実際そう見えていた）。
                //
                // 25.5〜37.8℃ にすると、体温分布図の目盛り(27〜37℃)が
                // ランプの水色〜赤にちょうど重なる。
                //
                // 幅の決め方：ランプのガンマは 0.65（室温付近の 1℃差を見せるための
                // 設定）で上が圧縮されるので、温度を等間隔に置いても色は上に詰まる。
                // 胴(36.5℃)がランプの 0.92（＝白飛びが始まる 0.93 の直前＝はっきり赤）、
                // 手足先(26.5℃)が 0.20（＝青と水色の境）に来るよう逆算した幅がこれ。
                // 上限を 42℃ に取ると胴が黄色止まりになり、赤が一度も出ない。
                //   手足先 26.5℃ 水色 / 前腕・脛 29℃ 緑 / 太もも 31.5℃ 黄
                //   腰 34℃ 橙 / 胴 36.5℃ 赤 / 局所高温点 37.4℃ 白の手前
                //
                // 実機のサーマルカメラも映っている範囲に合わせて自動でレンジを詰めるので、
                // 「人体しか熱源が無い部屋ではカメラが体温帯に合わせる」という解釈で
                // 現実とも矛盾しない。displayMax を分けているのと同じ考え方。
                bool bodyKey = t.key == "Body" || t.key == "Skin";
                m.SetFloat("_TempMin", bodyKey ? 25.5f
                                     : (t.displayMin > Temp.Unset + 1f ? t.displayMin : DisplayMin));
                m.SetFloat("_TempMax", t.displayMax > 0f ? t.displayMax : DisplayMax);
                m.SetFloat("_TempGamma", Gamma);
                m.SetFloat("_HeatIntensity", 1f);
                // かすめる角度の面が低く写るぶん。人体は全面が曲面なので、
                // 建物と同じ 0.10 を掛けると胴のほとんどが斜めに当たって
                // 常に 1℃ 低く出る（＝いちばん熱いはずの胴が赤に届かない）。
                // 輪郭が少し冷たく落ちる効果は残したいので、半分にして効かせる。
                m.SetFloat("_EdgeCool", bodyKey ? 0.05f : 0.10f);
                // ざらつきは控えめに。暗く落とした面ほど 8bit の階調が粗くなるので、
                // ムラを強く入れるとブロック状のノイズ（ガビガビ）として目立ってしまう。
                m.SetFloat("_Noise", 0.015f);
                m.SetFloat("_Grain", t.celsius > 30f ? 0.15f : 0.05f);
                m.SetFloat("_Dim", t.dim);
                m.SetFloat("_FadeNear", FadeNear);
                m.SetFloat("_FadeFar", FadeFar);

                // 人体だけは体内の温度構造を出す。
                // 一様な温度で塗ると「黄色い人型のシール」にしかならず、
                // 実際のサーモグラフィで人が不気味に見える理由（体が塊として
                // 立ち上がって見えること）が丸ごと失われる。
                // 芯の高さ・落ち幅は人形の背丈(約1.9m)に合わせてある。
                // 配管・配線には「熱が流れている」波を乗せる。
                //
                // 一様な温度で塗ると、サーモ役の画面では線が引いてあるだけで
                // 建物が止まって見える。ゆっくり流れる波を足すと、
                // 建物そのものが動いている＝生きているように見える。
                //
                // 電気の来ていない配線(DuctDead)だけは波を乗せない。
                // 「動いている管」と「死んだ管」が混じることで、
                // 動いていること自体が情報になる。
                float flow = 0f, flowLen = 2.5f, flowSpd = 1.2f;
                if (t.key == "Duct")     { flow = 2.6f; flowLen = 2.8f; flowSpd = 1.1f; }
                if (t.key == "DuctWarm") { flow = 1.6f; flowLen = 3.6f; flowSpd = 0.7f; }
                if (t.key == "DuctHot")  { flow = 3.4f; flowLen = 2.0f; flowSpd = 1.8f; }
                m.SetFloat("_FlowStrength", flow);
                m.SetFloat("_FlowLength", flowLen);
                m.SetFloat("_FlowSpeed", flowSpd);

                bool isBody = t.key == "Body" || t.key == "Skin" || t.key == "Burning";
                m.SetFloat("_BodyProfile", isBody ? 1f : 0f);
                if (isBody)
                {
                    // 芯から末端までの落差。
                    //
                    // 現実の体温分布図どおり 10℃（胴 36.5℃ → 手足の先 26.5℃）。
                    // ガンマで上が詰まるぶんは表示レンジの幅（_TempMin/_TempMax）で
                    // 吸収してあるので、落差そのものを誇張する必要は無い。
                    // 12℃ まで開けた版も試したが、指先が青を通り越して紫に沈み、
                    // 手の形が読めなくなった。
                    m.SetFloat("_BodyDrop", t.key == "Burning" ? 16f : 10f);

                    // 「芯からの遠さ」を頂点カラーから読む。
                    // 焼き込みの無いメッシュはシェーダー側が高さ基準に落ちるので、
                    // 全部の人体メッシュが焼けていなくても壊れない。
                    m.SetFloat("_BodyCoreness", 1f);

                    // 落ち方の曲線。1.0＝厚みにそのまま比例。
                    // 焼き込み側で厚みの分布に合わせて目盛りを取り直しているので、
                    // ここで曲げる必要はもう無い。1 より大きくすると胴だけが
                    // 広く赤くなり、体の中の階調が潰れる。
                    m.SetFloat("_BodyPow", 1.0f);

                    // まだらと局所高温点。肉の厚みが構造を作るようになったぶん、
                    // ノイズ側は弱めてよい。強いままだと構造の上に砂が乗って濁る。
                    // 表示レンジを胴＝ほぼ上限まで詰めたので、ここを強くすると
                    // 胴が白い斑点だらけになる。両方の山が重なって 37.8℃ に
                    // 届いたところだけが白く飛ぶ＝まれ、という強さにしてある。
                    m.SetFloat("_BodyMottle", t.key == "Burning" ? 2.0f : 0.6f);
                    m.SetFloat("_BodyVein", t.key == "Burning" ? 4f : 0.7f);

                    if (t.key == "Skin")
                    {
                        // room16 の巨大な腕。天井から生えていて縦に長いので、
                        // 立っている人間を前提にした高さ基準の芯は当てはまらない。
                        // 芯を高くして落ち幅も緩くし、まだらと局所高温だけを効かせる。
                        m.SetFloat("_BodyCoreY", 2.6f);
                        m.SetFloat("_BodySpread", 3.0f);
                    }
                    else
                    {
                        m.SetFloat("_BodyCoreY", 1.15f);   // 胴のあたりが一番熱い
                        m.SetFloat("_BodySpread", 0.85f);  // そこから上下へ落ちる
                    }
                }
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
