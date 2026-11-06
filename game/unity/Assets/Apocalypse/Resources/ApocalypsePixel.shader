Shader "Hidden/ApocalypsePixel"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Levels ("Color levels", Float) = 12
        _Dither ("Dither", Float) = 0.6
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: 顏色減色 + 有序抖動
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Levels, _Dither;

            static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                int2 p = int2(i.uv * _MainTex_TexelSize.zw) & 3;
                float d = (bayer[p.y * 4 + p.x] / 16.0 - 0.5) * _Dither;
                c.rgb = floor(c.rgb * _Levels + 0.5 + d) / _Levels;
                return saturate(c);
            }
            ENDCG
        }
    }
}
