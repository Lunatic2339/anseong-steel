Shader "AnseongSteel/HudUiText"
{
    Properties { [PerRendererData] _MainTex ("Font Atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.color=v.color; o.uv=v.uv; return o; }
            half4 frag(V v):SV_Target { half4 c=v.color; c.a*=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv).a; return c; }
            ENDHLSL
        }
    }
}