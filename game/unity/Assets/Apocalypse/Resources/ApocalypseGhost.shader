Shader "Hidden/ApocalypseGhost"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 0.3)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+2" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float l = 0.55 + 0.45 * saturate(dot(normalize(i.n), normalize(float3(0.35, 0.85, 0.4))));
                return fixed4(_Color.rgb * l * 1.15, _Color.a);
            }
            ENDCG
        }
    }
}
