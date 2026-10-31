Shader "Hidden/ApocalypseBloom"
{
    Properties
    {
        _MainTex ("", 2D) = "white" {}
        _BloomTex ("", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    float _Threshold;
    float _Intensity;
    float _Vignette;
    float _Saturation;

    half3 Box(float2 uv, float d)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-d, -d, d, d);
        return (tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb +
                tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb) * 0.25;
    }

    half4 FragPrefilter(v2f_img i) : SV_Target
    {
        half3 c = Box(i.uv, 1);
        half br = max(c.r, max(c.g, c.b));
        half soft = saturate((br - _Threshold + 0.1) / 0.2);
        half k = max(0, br - _Threshold) / max(br, 1e-4) + soft * 0.15;
        return half4(c * k, 1);
    }

    half4 FragDown(v2f_img i) : SV_Target { return half4(Box(i.uv, 1), 1); }
    half4 FragUp(v2f_img i) : SV_Target { return half4(Box(i.uv, 0.5), 1); }

    half4 FragComposite(v2f_img i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb + tex2D(_BloomTex, i.uv).rgb * _Intensity;
        half l = dot(c, half3(0.299, 0.587, 0.114));
        c = lerp(l.xxx, c, _Saturation);
        float2 d = i.uv - 0.5;
        c *= saturate(1 - dot(d, d) * _Vignette);
        return half4(c, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragPrefilter
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragDown
            ENDCG
        }
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragUp
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragComposite
            ENDCG
        }
    }
}
