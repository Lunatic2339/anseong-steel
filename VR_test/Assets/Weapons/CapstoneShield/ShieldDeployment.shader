Shader "MechaVR/Shield Deployment"
{
    Properties
    {
        _BaseMap("Original shield texture", 2D) = "white" {}
        _Open("Deployment", Range(0,1)) = 0
        _Core("Metal emitter pass", Float) = 0
        _Link("Energy connection", Float) = 0
        _ZWrite("Depth write", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float _Open, _Core, _ZWrite, _Link;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float2 plane:TEXCOORD1; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 plane:TEXCOORD1; float3 normalWS:TEXCOORD2; half metal:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.uv=v.uv; o.plane=v.plane; o.metal=v.color.r; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_Link > .5)
                {
                    clip(_Open - .001);
                    float2 grid = i.uv * float2(12,5);
                    grid.x += step(1, fmod(floor(grid.y),2)) * .5;
                    float2 cell = abs(frac(grid) - .5);
                    float hex = smoothstep(.38,.48,max(cell.x,cell.y*.866+cell.x*.5));
                    float pulse = pow(.5+.5*sin(i.uv.y*24-_Time.y*5),8);
                    half3 blue = half3(.04,.55,1.0)*(1+hex*.6+pulse*.25);
                    return half4(blue,(.19+hex*.17+pulse*.10)*smoothstep(0,.3,_Open));
                }
                half3 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                // Vertex colors mark the original metal (red=1) and planar energy (red=0).
                float2 p=abs(i.plane);
                if (_Core > 0.5)
                {
                    clip(i.metal-0.5);
                    Light light=GetMainLight();
                    half diffuse=0.45+0.55*abs(dot(normalize(i.normalWS),light.direction));
                    half blue=saturate((tex.b-tex.r-0.035)/0.125);
                    return half4(tex*(diffuse+blue*0.6),1);
                }
                clip(0.5-i.metal);
                clip(_Open-0.001);
                float radius=max(p.x/0.2645,p.y/0.5);
                clip(_Open-radius);
                float edge=(1-smoothstep(0.0,0.045,_Open-radius))*(1-step(0.999,_Open));
                half3 color=tex*1.35+half3(0.12,0.7,1.0)*edge*1.8;
                half alpha=saturate(0.30+max(tex.r,max(tex.g,tex.b))*0.28+edge*0.35);
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}