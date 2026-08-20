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

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            float _TempC, _TempMin, _TempMax, _TempGamma;
            float _HeatIntensity, _EdgeCool, _Noise, _Grain, _Dim;
            float _FadeNear, _FadeFar;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
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
                // 振り切れた所は白熱させる（h=0.86 あたり＝実温度 50℃ 前後から）
                col = lerp(col, fixed3(1.0, 1.0, 0.92), smoothstep(0.86, 1.0, h));
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
