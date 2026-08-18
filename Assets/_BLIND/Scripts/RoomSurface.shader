Shader "BLIND/RoomSurface"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        [Space(10)]
        _TextureScale ("Texture Size (meters)", Float) = 1
        [Space(10)]
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0
        [Space(10)]
        _Saturation ("Saturation (0=完全グレー)", Range(0, 1)) = 1
        _AmbientBoost ("Ambient Boost (暗くなり防止)", Range(0, 2)) = 0.35
    }

    // 部屋(床・壁・天井)専用のトライプラナー・マテリアル。
    //
    // 【何を解決するシェーダーか】
    // RoomBuilder3が生成するCubeにテクスチャを普通に貼ると、次の問題が起きる:
    //   ・オブジェクトの大きさによってテクスチャの縦横比が変わる
    //   ・壁の向き(北南 / 東西)でテクスチャが90度回って見え、床の目地と線が合わない
    //   ・入り口をくぐる時、壁の切断面にテクスチャが引き伸ばされて筋(境目)が出る
    //
    // このシェーダーはUVを一切使わず、【ワールド座標】から直接テクスチャを貼る。
    //   ・面の法線を見て、床なら(X,Z)、東西の壁なら(X,Y)、南北の壁なら(Z,Y)を自動で選ぶ
    //   ・どの面でも必ず「横=水平方向・縦=Y(高さ)」になるので、向きが揃う
    //   ・_TextureScale はメートル単位。1なら1mごとに1回繰り返す(実寸)
    //     → 拡大縮小しても模様の大きさが変わらず、隣り合う面の目地が必ず繋がる
    //
    // 部屋のジオメトリは軸に沿ったCubeなので、法線は必ずX/Y/Zのどれかを向く。
    // よってブレンドは実質発生せず、にじみのない綺麗な貼り方になる。
    //
    // _AmbientBoost は「テクスチャを貼ると本来の色より暗くなる」対策。
    // 密閉された部屋は環境光がほとんど届かず physically based では暗く落ちるため、
    // アルベドを弱く自己発光させて下限の明るさを底上げする(0で無効)。
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        fixed4 _Color;
        float _TextureScale;
        half _Glossiness;
        half _Metallic;
        half _AmbientBoost;
        half _NormalStrength;
        half _Saturation;

        struct Input
        {
            float3 worldPos;
            float3 geoNormal;
        };

        // 法線マップで o.Normal を書き換えると IN.worldNormal が
        // 揺らいでしまうため、投影軸の判定用に「元の面の法線」を別途持ち回る。
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.geoNormal = UnityObjectToWorldNormal(v.normal);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // 面がどの軸を向いているかで重み付け。
            // 8乗して尖らせることで、軸に沿った面ではほぼ1つの投影だけが選ばれる。
            float3 blend = pow(abs(IN.geoNormal), 8) + 1e-5;
            blend /= (blend.x + blend.y + blend.z);

            float3 wp = IN.worldPos / max(_TextureScale, 0.0001);

            // X向きの面(南北の壁) = 横にZ・縦にY
            // Y向きの面(床・天井)  = 横にX・縦にZ
            // Z向きの面(東西の壁) = 横にX・縦にY
            float2 uvX = wp.zy;
            float2 uvY = wp.xz;
            float2 uvZ = wp.xy;

            fixed4 c = tex2D(_MainTex, uvX) * blend.x
                     + tex2D(_MainTex, uvY) * blend.y
                     + tex2D(_MainTex, uvZ) * blend.z;

            // 彩度調整。素材そのものが色付き(例: コンクリのテクスチャがベージュ寄り)でも
            // ここでグレーに寄せられる。Tintの掛け算では元の色味が残ってしまうため、
            // 「灰色のコンクリにしたい」といった調整はこちらで行う。
            float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
            c.rgb = lerp(lum.xxx, c.rgb, _Saturation);

            c *= _Color;

            float3 n = UnpackNormal(tex2D(_BumpMap, uvX)) * blend.x
                     + UnpackNormal(tex2D(_BumpMap, uvY)) * blend.y
                     + UnpackNormal(tex2D(_BumpMap, uvZ)) * blend.z;
            n.xy *= _NormalStrength;

            o.Albedo = c.rgb;
            o.Normal = normalize(n);
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = c.rgb * _AmbientBoost;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
