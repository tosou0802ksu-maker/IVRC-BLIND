Shader "Custom/UdonOutline"
{
    Properties
    {
        // UdonSharpから制御するプロパティ名。以前と同じなのでスクリプト修正不要
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Range (0.0, 0.1)) = 0.01

        // 元のモデルを描画するためのメインテクスチャと色
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Color ("Base Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        // ========================================================
        // PASS 1: アウトラインの描画 (前のシェーダーとほぼ同じ)
        // ========================================================
        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode" = "Always" }
            Cull Front // 前面をカリング（裏面だけ描画）して、中身が見えるようにする
            ZWrite On
            ColorMask RGB

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
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            uniform float _OutlineWidth;
            uniform float4 _OutlineColor;

            v2f vert(appdata v)
            {
                v2f o;
                
                // 頂点を法線方向に少し拡大する（アウトラインの仕組み）
                float4 pos = v.vertex;
                pos.xyz += v.normal * _OutlineWidth;
                
                o.pos = UnityObjectToClipPos(pos);
                o.color = _OutlineColor;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // アウトラインの色だけを返す
                return i.color;
            }
            ENDCG
        }

        // ========================================================
        // PASS 2: 元のモデルの描画 (通常通り描画)
        // ========================================================
        Pass
        {
            Name "BASE"
            Cull Back // 通常通り前面を描画（裏面をカリング）

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // テクスチャの色に、インスペクターで設定した色を乗算
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}