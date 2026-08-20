Shader "BLIND/EchoHighlight"
{
    Properties
    {
        _EdgeColor ("Edge Color", Color) = (0, 1, 0.5, 1)
        _EdgeThickness ("Edge Thickness", Range(0.5, 8)) = 2
        _EdgeIntensity ("Edge Intensity", Range(0, 10)) = 3
        _MaxEdgeUV ("Max Edge Width (UV)", Range(0.005, 0.5)) = 0.05
        [Space(10)]
        // 元メッシュのUVが 0〜1 でない（実寸UV・タイリングUV）と面全体が輪郭判定になる。
        // Read/Write が off で CPU から貼り直せないメッシュ用に、シェーダー側で
        // オブジェクト空間の箱投影に切り替える。_ObjMin/_ObjSize は mesh.bounds を入れる。
        [Toggle] _UseObjectUv ("Object-space Box UV", Float) = 0
        _ObjMin  ("Mesh Bounds Min", Vector) = (0,0,0,0)
        _ObjSize ("Mesh Bounds Size", Vector) = (1,1,1,0)
        [Space(10)]
        // 箱投影は箱の稜線しかUVの端を作らないので、アヒルのような有機的な形だと
        // 輪郭がほとんど出ない。視線に対して縁になっている所を光らせて補う。
        // 反響定位で返ってくるのは物の「外形」なので、これがむしろ本来の見え方。
        _RimWeight ("Silhouette Edge", Range(0, 1)) = 0
        _RimPower  ("Silhouette Sharpness", Range(0.5, 12)) = 4
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
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            fixed4 _EdgeColor;
            float _EdgeThickness;
            float _EdgeIntensity;
            float _GlowIntensity;
            float _MaxEdgeUV;
            float _UseObjectUv;
            float4 _ObjMin, _ObjSize;
            float _RimWeight, _RimPower;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                if (_UseObjectUv > 0.5)
                {
                    // その物自身のバウンディングボックスに対する 0〜1 に置き直し、
                    // 法線の一番強い軸を面の向きとみなして残り2軸をUVにする（箱投影）。
                    float3 p = (v.vertex.xyz - _ObjMin.xyz) / max(_ObjSize.xyz, 1e-4);
                    float3 an = abs(v.normal);
                    if (an.x >= an.y && an.x >= an.z)      o.uv = float2(p.z, p.y);
                    else if (an.y >= an.z)                 o.uv = float2(p.x, p.z);
                    else                                   o.uv = float2(p.x, p.y);
                }
                else o.uv = v.uv;

                return o;
            }

            // UVの端(0または1)に近いほど1に近づく = 面の輪郭
            //
            // fwidth は「1ピクセルあたりUVがどれだけ動くか」なので、床や壁のように
            // 視線とほぼ平行な面では発散する。素のままだと輪郭が面いっぱいまで
            // 太って、ベタ塗りに見えてしまう。_MaxEdgeUV で上限を掛けて防ぐ。
            float EdgeFromUV(float2 uv, float thickness)
            {
                float2 aa = clamp(fwidth(uv) * thickness, 1e-5, _MaxEdgeUV);
                float2 distToBorder = min(uv, 1.0 - uv);
                float2 edge2 = 1.0 - saturate(distToBorder / aa);
                return max(edge2.x, edge2.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float edge = EdgeFromUV(i.uv, _EdgeThickness);

                if (_RimWeight > 0.001)
                {
                    float3 n = normalize(i.worldNormal);
                    float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                    float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                    edge = max(edge, rim * _RimWeight);
                }

                float3 col = _EdgeColor.rgb * edge * _EdgeIntensity * _GlowIntensity;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    FallBack Off
}
