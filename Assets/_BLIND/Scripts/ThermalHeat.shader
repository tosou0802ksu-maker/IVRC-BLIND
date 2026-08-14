Shader "BLIND/ThermalHeat"
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

            // 熱量(0〜1)を実機のハンディサーマルカメラ風のカラーグラデーションにマッピング
            // 0.0:紫 -> 1/6:青 -> 2/6:水色 -> 3/6:緑 -> 4/6:黄 -> 5/6:橙 -> 1.0:赤
            fixed3 thermalRamp(float heat)
            {
                fixed3 purple = fixed3(0.35, 0.0, 0.55);
                fixed3 blue   = fixed3(0.0, 0.1, 0.9);
                fixed3 cyan   = fixed3(0.0, 0.8, 0.9);
                fixed3 green  = fixed3(0.0, 0.85, 0.1);
                fixed3 yellow = fixed3(1.0, 0.95, 0.0);
                fixed3 orange = fixed3(1.0, 0.5, 0.0);
                fixed3 red    = fixed3(0.9, 0.0, 0.0);

                float t1 = smoothstep(0.0 / 6.0, 1.0 / 6.0, heat);
                float t2 = smoothstep(1.0 / 6.0, 2.0 / 6.0, heat);
                float t3 = smoothstep(2.0 / 6.0, 3.0 / 6.0, heat);
                float t4 = smoothstep(3.0 / 6.0, 4.0 / 6.0, heat);
                float t5 = smoothstep(4.0 / 6.0, 5.0 / 6.0, heat);
                float t6 = smoothstep(5.0 / 6.0, 6.0 / 6.0, heat);

                fixed3 col = lerp(purple, blue, t1);
                col = lerp(col, cyan, t2);
                col = lerp(col, green, t3);
                col = lerp(col, yellow, t4);
                col = lerp(col, orange, t5);
                col = lerp(col, red, t6);

                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                // カメラ正面を向く面ほど1に近く、輪郭(フレネル)ほど0に近い
                float NdotV = saturate(dot(normal, viewDir));

                // 局所的な熱量
                float heat = saturate(NdotV * _HeatIntensity);

                fixed3 col = thermalRamp(heat);

                // 輪郭(冷たい部分)ほど透明にして暗闇に溶け込ませる
                float alpha = smoothstep(0.05, 0.35, heat);

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
