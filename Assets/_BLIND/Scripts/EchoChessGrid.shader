Shader "BLIND/EchoChessGrid"
{
    Properties
    {
        _EdgeColor ("Edge Color", Color) = (0, 1, 0.5, 1)
        _EdgeThickness ("Outer Edge Thickness", Range(0.5, 8)) = 2
        _EdgeIntensity ("Edge Intensity", Range(0, 10)) = 3
        [Space(10)]
        _GridSize ("Grid Squares Per Side", Float) = 8
        _GridLineThickness ("Grid Line Thickness", Range(0.2, 6)) = 1.5
        [Space(10)]
        _GlowIntensity ("Glow Intensity (0=Off)", Float) = 0
    }

    // チェス盤床のエコロケ用オーバーレイ。
    //
    // EchoHighlight と同じ「物体は真っ黒、パルスが当たった時だけ輪郭が光る」
    // 仕組みに、床の外周だけでなく「マス目の溝」も光る処理を追加したもの。
    // _GlowIntensity は EchoReceiver がそのまま制御できる(プロパティ名を
    // 揃えてあるため、EchoReceiver側の変更は不要)。
    //
    // 床本体(Default層)の上に、同じ位置・同じ大きさでこのマテリアルを使った
    // Echo層の板を重ねて使う(3つの世界パターン)。実際の床タイル分割を
    // 増やす必要はない。1枚板のUV(0-1)を _GridSize 分割してマス目線を描く。
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
            float _GridSize;
            float _GridLineThickness;
            float _GlowIntensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // UVの端(0または1)に近いほど1に近づく = 板全体の輪郭
            float EdgeFromUV(float2 uv, float thickness)
            {
                float2 aa = max(fwidth(uv) * thickness, 1e-5);
                float2 distToBorder = min(uv, 1.0 - uv);
                float2 edge2 = 1.0 - saturate(distToBorder / aa);
                return max(edge2.x, edge2.y);
            }

            // UVをマス目状に分割し、マスの境界に近いほど1に近づく = 溝の線
            float GridEdge(float2 uv, float gridSize, float thickness)
            {
                float2 guv = uv * gridSize;
                float2 aa = max(fwidth(guv) * thickness, 1e-5);
                float2 gridFrac = frac(guv);
                float2 distToLine = min(gridFrac, 1.0 - gridFrac);
                float2 line2 = 1.0 - saturate(distToLine / aa);
                return max(line2.x, line2.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float outer = EdgeFromUV(i.uv, _EdgeThickness);
                float grid = GridEdge(i.uv, _GridSize, _GridLineThickness);
                float edge = max(outer, grid);

                float3 col = _EdgeColor.rgb * edge * _EdgeIntensity * _GlowIntensity;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    FallBack Off
}
