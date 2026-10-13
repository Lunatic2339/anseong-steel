Shader "MechaVR/Arm Energy Shield"
{
    Properties
    {
        _Open("Deployment", Range(0,1)) = 0
        _Emitter("Armor seam light", Float) = 0
        [HDR] _EnergyColor("Energy", Color) = (0.04,0.5,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _EnergyColor;
            float _Open, _Emitter;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 plane:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.plane=v.positionOS.xy; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(_Open-0.001);
                if(_Emitter>0.5) return half4(0.25,1.5,2.5,saturate(_Open*5));
                float2 p=abs(i.plane);
                // Grow above and below the forearm's light seams. No central housing or cutout.
                float height=lerp(0.035,0.5,_Open);
                float width=lerp(0.12,0.2645,smoothstep(0,0.65,_Open));
                float reveal=min(width-p.x,height-p.y);
                clip(reveal);
                float outline=min(min(0.2645-p.x,0.5-p.y),(0.6645-p.x-p.y)*0.7071);
                float rim=1-smoothstep(0.001,0.006,outline);
                float movingRim=(1-smoothstep(0.001,0.008,reveal))*(1-step(0.999,_Open));
                float2 grid=i.plane/0.019;
                float2 period=float2(1,1.7320508);
                float2 a=grid-period*floor(grid/period)-period*.5;
                float2 b=grid-period*.5; b=b-period*floor(b/period)-period*.5;
                float2 cell=dot(a,a)<dot(b,b)?a:b;
                cell=abs(cell);
                float edgeDistance=max(dot(cell,float2(.5,.8660254)),cell.x);
                float aa=max(fwidth(edgeDistance),.003);
                float hex=smoothstep(.475-aa,.49+aa,edgeDistance);
                float edge=max(rim,movingRim);
                half3 color=_EnergyColor.rgb*(.65+hex*.8)+half3(.15,1.1,1.8)*edge;
                half alpha=(.22+hex*.18+edge*.57)*saturate(_Open*6);
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}