// Unlit colour drawn on top of the model (like the web viewer's flow arrows), so the conceptual
// refrigerant-flow markers stay visible inside pipes, coils and the compressor shell.
// No LightMode tag -> rendered by URP's SRPDefaultUnlit pass (and by the built-in pipeline).
Shader "VCR/FlowArrowOnTop"
{
    Properties { _BaseColor ("Color", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZTest Always
        ZWrite Off
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _BaseColor;
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(_BaseColor.rgb, 0.95); }
            ENDCG
        }
    }
}
