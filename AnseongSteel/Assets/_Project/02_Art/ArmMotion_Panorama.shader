Shader "AnseongSteel/Fixed Robot Panorama"
{
    Properties
    {
        _LeftFeed("Left sector", 2D) = "black" {}
        _CenterFeed("Center sector", 2D) = "black" {}
        _RightFeed("Right sector", 2D) = "black" {}
        _TanHalfHorizontal("Horizontal projection", Float) = .7088
        _TanHalfVertical("Vertical projection", Float) = .7002
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_LeftFeed); SAMPLER(sampler_LeftFeed);
            TEXTURE2D(_CenterFeed); SAMPLER(sampler_CenterFeed);
            TEXTURE2D(_RightFeed); SAMPLER(sampler_RightFeed);
            CBUFFER_START(UnityPerMaterial)
                float _TanHalfHorizontal, _TanHalfVertical;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz); output.uv = input.uv; return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Cockpit V02's spherical UVs: azimuth -106..106, elevation -18..27.
                // Reproject each lens into angular UV space instead of stretching a
                // rectilinear image over the dome. Robot view stays fixed as HMD turns.
                float azimuth = (input.uv.x - .5) * 212;
                int sector = azimuth < -212.0/6.0 ? -1 : azimuth > 212.0/6.0 ? 1 : 0;
                float a = radians(azimuth - sector * (212.0/3.0));
                float e = radians(lerp(-18, 27, input.uv.y));
                float2 uv = .5 + .5 * float2(tan(a) / _TanHalfHorizontal, tan(e) / (cos(a) * _TanHalfVertical));
                if (sector < 0) return SAMPLE_TEXTURE2D(_LeftFeed, sampler_LeftFeed, uv);
                if (sector > 0) return SAMPLE_TEXTURE2D(_RightFeed, sampler_RightFeed, uv);
                return SAMPLE_TEXTURE2D(_CenterFeed, sampler_CenterFeed, uv);
            }
            ENDHLSL
        }
    }
}
