Shader "BLIND/EchoHighlight"
{
    Properties
    {
        _MainTex ("Base Texture", 2D) = "white" {}
        _Color ("Base Color", Color) = (1, 1, 1, 1)
        [Space(10)]
        _EdgeColor ("Edge Color", Color) = (0, 1, 0.5, 1)
        _EdgeThickness ("Edge Thickness", Range(0.5, 8)) = 2
        _EdgeIntensity ("Edge Intensity", Range(0, 10)) = 3
        [Space(10)]
        _GlowIntensity ("Glow Intensity (0=Off)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        struct Input
        {
            float2 uv_MainTex;
        };

        fixed4 _Color;
        fixed4 _EdgeColor;
        float _EdgeThickness;
        float _EdgeIntensity;
        float _GlowIntensity;

        float EdgeFromUV(float2 uv, float thickness)
        {
            float2 aa = max(fwidth(uv) * thickness, 1e-5);
            float2 distToBorder = min(uv, 1.0 - uv);
            float2 edge2 = 1.0 - saturate(distToBorder / aa);
            return max(edge2.x, edge2.y);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 tex = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = tex.rgb;
            o.Alpha = tex.a;
            float edge = EdgeFromUV(IN.uv_MainTex, _EdgeThickness);
            o.Emission = _EdgeColor.rgb * edge * _EdgeIntensity * _GlowIntensity;
        }
        ENDCG
    }
    FallBack "Diffuse"
}