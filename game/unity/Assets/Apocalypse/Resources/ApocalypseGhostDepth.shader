Shader "Hidden/ApocalypseGhostDepth"
{
    SubShader
    {
        Tags { "Queue" = "Transparent+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite On
            ColorMask 0
            Cull Back
        }
    }
}
