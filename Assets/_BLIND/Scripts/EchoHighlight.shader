Shader "BLIND/EchoHighlight"
{
    Properties
    {
        _EdgeColor ("Edge Color", Color) = (0, 1, 0.5, 1)
        _EdgeThickness ("Edge Thickness", Range(0.5, 8)) = 2
        _EdgeIntensity ("Edge Intensity", Range(0, 10)) = 3
        [Space(10)]
        _GlowIntensity ("Glow Intensity (0=Off)", Float) = 0
    }

    // エコロケ視点用。
    //
    // 物体そのものは完全な黒(=真っ暗な視界に溶けて見えない)で、
    // パルスが当たっている間だけ「輪郭線」だけが光って形が分かる。
    // ライティングは一切受けない(Unlit)ので、シーンのライトや環境光の
    // 影響で物体が薄っすら見えてしまうことがない。
    //
    // 不透明のまま描画するので、光っていない時も奥にあるものを正しく遮る
    // (=手前の壁越しに奥の物が透けて見えることはない)。
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _EdgeColor;
            float _EdgeThickness;
            float _EdgeIntensity;
            float _GlowIntensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // UVの端(0または1)に近いほど1に近づく = 面の輪郭
            float EdgeFromUV(float2 uv, float thickness)
            {
                float2 aa = max(fwidth(uv) * thickness, 1e-5);
                float2 distToBorder = min(uv, 1.0 - uv);
                float2 edge2 = 1.0 - saturate(distToBorder / aa);
                return max(edge2.x, edge2.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float edge = EdgeFromUV(i.uv, _EdgeThickness);
                float3 col = _EdgeColor.rgb * edge * _EdgeIntensity * _GlowIntensity;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    FallBack Off
}
