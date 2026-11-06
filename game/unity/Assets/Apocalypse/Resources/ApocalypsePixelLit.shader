// 物件用的像素貼圖材質：每個面依物件座標貼一張 16x16 的像素雜訊，格子大小固定（跟物件縮放無關），
// 光照跟原本的 Standard 一樣只用太陽光 + 環境光，所以顏色不會跑掉。
Shader "Apocalypse/PixelLit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Pixel noise", 2D) = "white" {}
        _Density ("Texels per unit", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Density;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 p : TEXCOORD0;
                float3 on : TEXCOORD1;
                float3 wn : TEXCOORD2;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 scale = float3(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21), length(unity_ObjectToWorld._m02_m12_m22));
                o.p = v.vertex.xyz * scale;
                o.on = v.normal;
                o.wn = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = abs(i.on);
                float2 uv = n.x > n.y && n.x > n.z ? i.p.zy : (n.y > n.z ? i.p.xz : i.p.xy);
                fixed tex = tex2D(_MainTex, uv * _Density * _MainTex_TexelSize.xy).r;
                float ndl = max(0, dot(normalize(i.wn), _WorldSpaceLightPos0.xyz));
                fixed3 light = UNITY_LIGHTMODEL_AMBIENT.rgb + _LightColor0.rgb * ndl;
                return fixed4(_Color.rgb * tex * light, 1);
            }
            ENDCG
        }
    }
    Fallback "Legacy Shaders/VertexLit"
}
