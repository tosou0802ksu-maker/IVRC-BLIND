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

            // 熱量(0〜1)をFLIR風のカラーグラデーションにマッピング
            // 0.0:青 -> 0.25:水色/緑 -> 0.5:黄 -> 0.75:赤 -> 1.0:白
            fixed3 thermalRamp(float heat)
            {
                fixed3 blue   = fixed3(0.0, 0.05, 0.6);
                fixed3 cyan   = fixed3(0.0, 0.8, 0.6);
                fixed3 yellow = fixed3(1.0, 0.95, 0.0);
                fixed3 red    = fixed3(1.0, 0.1, 0.0);
                fixed3 white  = fixed3(1.0, 1.0, 1.0);

                float t1 = smoothstep(0.0, 0.25, heat);
                float t2 = smoothstep(0.25, 0.5, heat);
                float t3 = smoothstep(0.5, 0.75, heat);
                float t4 = smoothstep(0.75, 1.0, heat);

                fixed3 col = lerp(blue, cyan, t1);
                col = lerp(col, yellow, t2);
                col = lerp(col, red, t3);
                col = lerp(col, white, t4);

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
