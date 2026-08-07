Shader "BLIND/OutlineHighlight"
{
    Properties
    {
        _MainTex ("Base Texture", 2D) = "white" {}
        _Color ("Base Color", Color) = (1, 1, 1, 1)

        [Space(10)]
        _EdgeColor ("Edge Highlight Color", Color) = (1, 0, 0, 1)
        _EdgeThickness ("Edge Thickness", Range(0.5, 8)) = 2
        _EdgeIntensity ("Edge Emission Intensity", Range(0, 10)) = 2

        [Space(10)]
        [Toggle] _HighlightActive ("Highlight Active (0=Off, 1=On)", Float) = 0
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
        float _HighlightActive;

        // Lights up fragments close to a UV shell border (u or v near 0 or 1).
        // On a standard per-face-unwrapped cube each face occupies its own
        // 0-1 UV square, so a UV border coincides with a geometric edge of
        // the mesh. This is view-independent (unlike Fresnel/rim), so a flat
        // face never glows across its whole surface at grazing angles -
        // only the strip near each edge does. Corners naturally read
        // brighter because two borders (u and v) overlap there.
        float EdgeFromUV(float2 uv, float thickness)
        {
            float2 aa = max(fwidth(uv) * thickness, 1e-5);
            float2 distToBorder = min(uv, 1.0 - uv);
            float2 edge2 = 1.0 - saturate(distToBorder / aa);
            return max(edge2.x, edge2.y);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 tex = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = tex.rgb;
            o.Alpha = tex.a;

            float edge = EdgeFromUV(IN.uv_MainTex, _EdgeThickness);
            o.Emission = _EdgeColor.rgb * edge * _EdgeIntensity * _HighlightActive;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
