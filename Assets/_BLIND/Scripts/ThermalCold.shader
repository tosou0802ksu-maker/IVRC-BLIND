Shader "BLIND/ThermalCold"
{
    Properties
    {
        _HeatIntensity ("Heat Intensity", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            float _HeatIntensity;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // 冷気(0〜1)を実機のハンディサーマルカメラ風カラースケールの「寒色帯」だけを
            // 使ってマッピング。信号(NdotV)が最も強い内側(heat=1)が最も冷たい色(濃い紫)になり、
            // 輪郭(heat=0)にかけて相対的に温かいシアン側を経由しながら透明にフェードしていく。
            // 黒を使わないのはサーマル視点で暗闇に埋もれて視認しづらくなるため。
            // 各区間のしきい値(下のBREAKPOINT定数)を変えると、各色が占める面積を調整できる。
            // 最後の区間(darkPurple)のしきい値を高め(0.85〜1.0)にすることで、
            // 最も濃い紫になる範囲を中心付近だけの「小さめ」な領域にしている。
            fixed3 coldRamp(float heat)
            {
                fixed3 cyan       = fixed3(0.0, 0.8, 0.9);
                fixed3 vividBlue  = fixed3(0.0, 0.4, 1.0);
                fixed3 blue       = fixed3(0.0, 0.1, 0.9);
                fixed3 indigo     = fixed3(0.1, 0.05, 0.6);
                fixed3 purple     = fixed3(0.35, 0.0, 0.55);
                fixed3 darkPurple = fixed3(0.22, 0.0, 0.35);

                // heatがどの範囲でどの色に切り替わるか(区間の境界)。
                // 数値を詰めるとその色の範囲が狭く(小さく)、広げると広くなる。
                float t1 = smoothstep(0.00, 0.20, heat); // cyan -> vividBlue
                float t2 = smoothstep(0.20, 0.40, heat); // vividBlue -> blue
                float t3 = smoothstep(0.40, 0.60, heat); // blue -> indigo
                float t4 = smoothstep(0.60, 0.80, heat); // indigo -> purple
                float t5 = smoothstep(0.85, 1.00, heat); // purple -> darkPurple (中心だけの狭い範囲)

                fixed3 col = lerp(cyan, vividBlue, t1);
                col = lerp(col, blue, t2);
                col = lerp(col, indigo, t3);
                col = lerp(col, purple, t4);
                col = lerp(col, darkPurple, t5);

                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                // カメラ正面を向く面ほど1に近く、輪郭(フレネル)ほど0に近い
                float NdotV = saturate(dot(normal, viewDir));

                // 局所的な冷気量
                float heat = saturate(NdotV * _HeatIntensity);

                fixed3 col = coldRamp(heat);

                // 輪郭(最も冷たい/薄い部分)ほど透明にして暗闇に溶け込ませる
                float alpha = smoothstep(0.05, 0.35, heat);

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
