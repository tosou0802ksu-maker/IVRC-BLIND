// 実際のサーモグラフィの見え方に寄せた、面用のサーマルシェーダー。
//
// 既存の BLIND/ThermalHeat / ThermalCold は色を「カメラとの角度(NdotV)」から作っていたため、
// 同じ物でも見る向きで温度が変わって見えてしまい、「この机は何度」という設計ができなかった。
// こちらは _TempC に摂氏をそのまま入れる。色は温度だけで決まり、角度は
// 実機のサーマルカメラで起きる「かすめる角度ほど放射率が下がって低く写る」ぶんの
// わずかな補正にしか使わない。
//
// 表示レンジ(_TempMin.._TempMax)はカメラ側の設定に相当する。
// ガンマを 1 未満にしてあるので、室温付近(15〜21℃)の 1℃差でも色が動き、
// 蛍光灯のような高温源は上の方で潰れる。実機のオートレンジに近い挙動。
//
// 不透明。壁の向こうが透けないので、サーモ役にも部屋という空間が成立する。
Shader "BLIND/ThermalSurface"
{
    Properties
    {
        _TempC        ("Temperature (celsius)", Float) = 18.0
        _TempMin      ("Display Min (celsius)", Float) = 12.0
        _TempMax      ("Display Max (celsius)", Float) = 70.0
        _TempGamma    ("Ramp Gamma", Range(0.2, 2.0)) = 0.65
        _HeatIntensity("Heat Intensity (DynamicThermalObject用の倍率)", Float) = 1.0
        _EdgeCool     ("Edge Emissivity Falloff", Range(0.0, 0.5)) = 0.18
        _Noise        ("Sensor Noise", Range(0.0, 0.15)) = 0.035
        _Grain        ("Surface Variation", Range(0.0, 0.5)) = 0.12
        _Dim          ("Dim (1=そのまま 0=真っ黒)", Range(0.0, 1.0)) = 1.0
        _FadeNear     ("Fade Range at Dim=0 (m)", Float) = 5.0
        _FadeFar      ("Fade Range at Dim=1 (m)", Float) = 200.0

        // --- 熱の流れ（配管・配線用。0 なら完全に無効で従来どおり） ---
        _FlowStrength ("Heat Flow Amplitude (C)", Float) = 0.0
        _FlowSpeed    ("Flow Speed (m/s)", Float) = 1.2
        _FlowLength   ("Flow Wavelength (m)", Float) = 2.5

        // --- 人体プロファイル（Body / Skin 用。0 なら完全に無効で従来どおり） ---
        _BodyProfile  ("Body Profile (0=off 1=on)", Range(0.0, 1.0)) = 0.0
        _BodyCoreness ("Use Baked Vertex Coreness (0=height)", Range(0.0, 1.0)) = 0.0
        _BodyPow      ("Coreness Falloff Curve", Range(0.4, 3.0)) = 1.0
        _BodyCoreY    ("Body Core Height (world Y, m)", Float) = 1.15
        _BodySpread   ("Body Falloff (m)", Float) = 0.85
        _BodyDrop     ("Extremity Temp Drop (C)", Float) = 7.0
        _BodyMottle   ("Mottling (C)", Float) = 1.6
        _BodyVein     ("Vein/Hot-spot Strength (C)", Float) = 2.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 vcol : TEXCOORD2;
            };

            float _TempC, _TempMin, _TempMax, _TempGamma;
            float _HeatIntensity, _EdgeCool, _Noise, _Grain, _Dim;
            float _FadeNear, _FadeFar;
            float _FlowStrength, _FlowSpeed, _FlowLength;
            float _BodyProfile, _BodyCoreness, _BodyPow;
            float _BodyCoreY, _BodySpread, _BodyDrop, _BodyMottle, _BodyVein;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                // 頂点カラーを持たないメッシュには GPU が (1,1,1,1) を入れる。
                // 焼き込み済みのメッシュは a=0.5 にしてあるので、a で見分けられる。
                o.vcol = v.color;
                return o;
            }

            // 実機のハンディサーマルカメラの標準的な色順(アイアン系ではなくレインボー系)
            // 0.0:紫 -> 1/6:青 -> 2/6:水色 -> 3/6:緑 -> 4/6:黄 -> 5/6:橙 -> 1.0:赤
            //
            // レンジ上限に張り付く高温(蛍光灯・安定器・炎)は白飛びさせる。
            // 実機でも振り切れた画素は白になるし、暗い画面の中で「そこが熱源だ」と
            // 一目で分かる目印になる。サーモ役の道しるべはこれしかない。
            fixed3 thermalRamp(float h)
            {
                fixed3 purple = fixed3(0.30, 0.00, 0.48);
                fixed3 blue   = fixed3(0.00, 0.10, 0.90);
                fixed3 cyan   = fixed3(0.00, 0.80, 0.90);
                fixed3 green  = fixed3(0.00, 0.85, 0.10);
                fixed3 yellow = fixed3(1.00, 0.95, 0.00);
                fixed3 orange = fixed3(1.00, 0.50, 0.00);
                fixed3 red    = fixed3(0.95, 0.05, 0.00);

                fixed3 col = lerp(purple, blue,   smoothstep(0.0/6.0, 1.0/6.0, h));
                col = lerp(col, cyan,   smoothstep(1.0/6.0, 2.0/6.0, h));
                col = lerp(col, green,  smoothstep(2.0/6.0, 3.0/6.0, h));
                col = lerp(col, yellow, smoothstep(3.0/6.0, 4.0/6.0, h));
                col = lerp(col, orange, smoothstep(4.0/6.0, 5.0/6.0, h));
                col = lerp(col, red,    smoothstep(5.0/6.0, 6.0/6.0, h));
                // 振り切れた所は白熱させる。
                // ただし白へ寄せるのを 0.93 から、色も純白ではなく温かい象牙色にしてある。
                // 純白に早く飛ばすと、熱い物がのっぺりした白い塊になって
                // 「何が熱いのか」も「どのくらい熱いのか」も読めなくなる。
                // 赤〜橙の帯を最後まで残したほうが、熱源の形と強さが伝わる。
                col = lerp(col, fixed3(1.0, 0.93, 0.82), smoothstep(0.93, 1.0, h));
                return col;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            // ワールド空間の細かい模様は、遠くや浅い角度だと 1ピクセルより細かくなる。
            // すると隣り合うピクセルが無関係な値を拾い、カメラがわずかに動くだけで
            // 全部が入れ替わって激しくチラつく（床が縞模様に波打って見えるのがこれ）。
            //
            // fp = 1ピクセルがワールドで何m を覆っているか。模様のマス目が
            // それより細かくなる手前でなめらかに 0 へ落とす。ミップマップと同じ考え方。
            //
            // 時間で動くノイズは入れない。VRだと左右の目に別々のノイズが出て
            // 立体視が壊れ、ちらつきと酔いの直接の原因になる。実機のサーマルの
            // ざらつきは「固定パターンノイズ」なので、止まっていても嘘ではない。
            float NoiseOct(float3 p, float cells, float fp)
            {
                float fade = saturate(1.0 - fp * cells * 2.0);
                if (fade <= 0.0) return 0.0;
                return (hash13(floor(p * cells)) - 0.5) * fade;
            }

            // 上のノイズはマス目ごとに値が一定なので、面に当てると立方体の面が
            // そのまま出る（体に 8cm 角の市松模様が浮いて、圧縮ノイズのように見えた）。
            // センサーの固定パターンノイズはそれで正しいが、体表の温度ムラは
            // 血流と発汗の分布なので、境目のない滑らかな濃淡でなければならない。
            // 8隅を補間して滑らかにした版。人体プロファイルでだけ使う。
            float ValueNoise(float3 p)
            {
                float3 c = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);   // なめらかに繋ぐ

                float n000 = hash13(c + float3(0,0,0)), n100 = hash13(c + float3(1,0,0));
                float n010 = hash13(c + float3(0,1,0)), n110 = hash13(c + float3(1,1,0));
                float n001 = hash13(c + float3(0,0,1)), n101 = hash13(c + float3(1,0,1));
                float n011 = hash13(c + float3(0,1,1)), n111 = hash13(c + float3(1,1,1));

                float x00 = lerp(n000, n100, f.x), x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x), x11 = lerp(n011, n111, f.x);
                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            float NoiseSmooth(float3 p, float cells, float fp)
            {
                float fade = saturate(1.0 - fp * cells * 2.0);
                if (fade <= 0.0) return 0.0;
                return (ValueNoise(p * cells) - 0.5) * fade;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float NdotV = saturate(dot(n, v));

                // 1ピクセルがワールドで何m を覆っているか。模様のチラつき止めに使う
                float3 dpx = ddx(i.worldPos), dpy = ddy(i.worldPos);
                float fp = max(length(dpx), length(dpy));

                // 実機でも、かすめる角度の面は放射率が落ちて数℃低く写る
                float tempC = _TempC * _HeatIntensity;
                tempC -= (1.0 - NdotV) * _EdgeCool * max(tempC - _TempMin, 0.0);

                // 同じ材質でも表面には温度ムラがある（16cm マス）
                tempC += NoiseOct(i.worldPos, 6.0, fp) * _Grain;

                // --- 熱の流れ ---
                //
                // 配管や配線を一様な温度で塗ると、サーモ役の画面では「線が引いてあるだけ」で
                // 建物が止まって見える。実際の配管は中を通る温水や排気で温度が波打つので、
                // ゆっくり流れる波を足すと、建物そのものが動いている＝生きているように見える。
                //
                // _FlowStrength が 0 のマテリアル（壁・床・人体など）は
                // この行を通っても値が変わらないので、従来の見え方のまま。
                //
                // ⚠️ 時間で動かすのはここだけ。ノイズ（NoiseOct / NoiseSmooth）は
                // 絶対に時間で動かしてはいけない（VRで左右の目に別々の粒が出て立体視が壊れる）。
                // これはワールド座標のなめらかな正弦波なので、左右の目で完全に同じ値になり、
                // その問題は起きない。周波数も低いので画素単位のちらつきも出ない。
                if (_FlowStrength > 0.0)
                {
                    // 斜めの軸にしてあるのは、縦・横・奥行きどの向きの配管にも
                    // 波が乗るようにするため。軸に平行な管だけ波が止まって見えるのを避ける。
                    float3 axis = normalize(float3(1.0, 0.35, 0.6));
                    float phase = (dot(i.worldPos, axis) - _Time.y * _FlowSpeed) / max(_FlowLength, 0.05);
                    tempC += sin(phase * 6.2831853) * _FlowStrength;
                }

                // --- 人体プロファイル ---
                //
                // 人体を一様な温度で塗ると、サーモ視点では「黄色い人型のシール」にしか
                // 見えない。実際のサーモグラフィで人が不気味に写るのは、体の中に
                // 温度の構造があるからで、平面ではなく塊として立ち上がって見える。
                //
                // 現実の人体の分布：
                //   胴と頭が最も高く(36℃前後)、手足の先は血流が細いので 28〜30℃まで落ちる。
                //   表面は汗腺・血管・服の張り付きでまだらになり、頸動脈や
                //   脇のあたりに局所的な高温点が出る。
                //
                // 体表の温度は、その場所の「肉の厚み」でほぼ決まる。発熱するのは体積で
                // 放熱するのは表面なので、細い所ほど冷える。実測でだいたい
                // 胴 22cm / 太もも 13cm / 上腕 9cm / 前腕 6cm / 手 2cm、
                // これが体温分布図の 赤(胴) → 黄緑(太もも) → 水色(手足先) の並びと一致する。
                // その厚みを BlindBodyThermalBake がメッシュに焼き込み、頂点カラー r に
                // 「芯からの遠さ」として入れてある。向きにもポーズにも依存しないので、
                // 寝ている人形も逆立ちしている人形も正しく色が分かれる。
                //
                // 焼き込みのないメッシュ（頂点カラー無し＝GPU が a=1 を渡す）は、
                // 従来どおり高さ(world Y)を体の軸の代わりに使う方式へ自動的に落ちる。
                //
                // _BodyProfile が 0 のマテリアル（壁・床・配管など）は
                // この行を通っても値が変わらないので、従来の見え方のまま。
                if (_BodyProfile > 0.0)
                {
                    // --- 高さ基準（焼き込みが無いとき用のたね）---
                    // 人体の温度分布は胴を中心に上下対称ではない。頭部は
                    // 血流が多く体で最も高温(36〜37℃)になる一方、足先は
                    // 心臓から遠く血流が細いので 28℃ 前後まで落ちる。
                    // 対称に落とすと頭が足と同じ色になり「顔だけ冷たい人形」という
                    // 現実にはあり得ない絵になる（実際そう見えていた）。
                    // 上方向は落ち幅を 2.4 倍緩くして、頭を胴と同じ帯に残す。
                    float dy = i.worldPos.y - _BodyCoreY;
                    float spread = max(_BodySpread, 0.01);
                    float d = (dy < 0.0) ? (-dy / spread) : (dy / (spread * 2.4));
                    float heightFall = saturate(d * d);        // 二乗＝足先だけ急に冷える

                    // --- 焼き込み基準 ---
                    // vcol.a が 0.5 なら焼き込み済み。頂点カラーの無いメッシュには
                    // GPU が (1,1,1,1) を渡すので、a=1 を見て自動的に高さ基準へ落ちる。
                    float baked = _BodyCoreness * (1.0 - step(0.75, i.vcol.a));
                    float coreFall = pow(saturate(1.0 - i.vcol.r), _BodyPow);

                    coreFall = lerp(heightFall, coreFall, baked);

                    // 表面のまだら。10cm 相当。血管や汗の分布に相当する。
                    // マス目のままだと体に市松模様が出るので、補間したノイズを使う。
                    float mottle = NoiseSmooth(i.worldPos, 10.0, fp) * 2.0;

                    // 局所的な高温点（頸動脈・脇など）。まばらに強く出したいので
                    // 粗いノイズを一方向に振ってから正の側だけ拾う。
                    float vein = saturate(NoiseSmooth(i.worldPos, 2.5, fp) * 2.0 + 0.30);
                    vein = vein * vein;

                    float bodyDelta = -coreFall * _BodyDrop
                                    + mottle * _BodyMottle
                                    + vein * _BodyVein;

                    tempC += bodyDelta * _BodyProfile;
                }

                float h = saturate((tempC - _TempMin) / max(_TempMax - _TempMin, 0.001));
                h = pow(h, _TempGamma);

                fixed3 col = thermalRamp(h);

                // センサーの固定パターンノイズ（2.5cm マス）
                col += NoiseOct(i.worldPos, 40.0, fp) * _Noise;

                // 床・壁・天井まではっきり見えると、サーモ役だけで空間が読めてしまい
                // エコロケ役の役割が無くなる。建物側は _Dim を下げてほぼ黒く落とし、
                // 「熱源しか見えない」状態にする。不透明のままなので遮蔽は効く
                // （＝壁の向こうは見えない）。
                // _Dim は「見た目でどのくらいの明るさにしたいか」で指定する。
                // プロジェクトが Linear 色空間なので、出力値をそのまま掛けても
                // 体感の明るさは落ちない（0.05倍にしても見た目は 26% までしか落ちない）。
                // ここで知覚量→リニア量に直しておかないと、表の数字と実際の見え方がずれる。
                #ifdef UNITY_COLORSPACE_GAMMA
                    col *= _Dim;
                #else
                    col *= pow(max(_Dim, 0.0), 2.2);
                #endif

                // さらに、温度差の小さい物は「近くでしか読めない」ようにする。
                //
                // 実機でも、距離が延びるほど大気の透過と背景放射で温度差が埋もれる。
                // 室温の壁は数mで背景に溶けるが、50℃の蛍光管は部屋の端からでも見える。
                // ゲーム上は「サーモ役が入口に立っただけで間取りを読んでしまう」のを
                // 防ぐ役割も持つ。形を伝えるのはエコロケ役の仕事なので、
                // サーモ役には自分の足元より先の建物が見えていてはいけない。
                //
                // 黒く落ちても不透明のままなので遮蔽は効いたまま
                //（＝壁の向こうの熱源は見えない）。
                float dist = distance(_WorldSpaceCameraPos, i.worldPos);
                float d2 = saturate(_Dim) * saturate(_Dim);
                float range = lerp(_FadeNear, _FadeFar, d2);
                col *= 1.0 - smoothstep(range * 0.45, range, dist);

                return fixed4(saturate(col), 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
