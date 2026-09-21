Shader "WeldingTrainer/Fusion Workpiece"
{
    Properties
    {
        _BaseColor("Metal", Color) = (0.32,0.37,0.42,1)
        _BaseMap("Base map", 2D) = "white" {}
        _Metallic("Metallic", Range(0,1)) = 0.8
        _Smoothness("Smoothness", Range(0,1)) = 0.4
        _CellState("Thermal cells", 2D) = "black" {}
        _HoleRadius("Hole width across each flange", Float) = 0.01
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_CellState); SAMPLER(sampler_CellState);
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _BaseMap_ST;
            float4x4 _WorldToWorkpiece;
            float _HoleRadius;
            half _Metallic, _Smoothness;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return output;
        }
        half2 Cutout(float3 world)
        {
            float3 p = mul(_WorldToWorkpiece, float4(world,1)).xyz;
            half2 state = SAMPLE_TEXTURE2D(_CellState, sampler_CellState, float2(saturate(p.z + 0.5),0.5)).rg;
            // Ragged opening along the corner, through front AND back faces of both plates.
            float edge = _HoleRadius * (0.88 + 0.08*sin(p.z*1700) + 0.04*sin(p.z*3700));
            float distanceToEdge = max(p.x,p.y) - edge;
            if (abs(p.z) <= 0.5001 && state.r > 0.5) clip(distanceToEdge);
            half rim = state.r * (1 - smoothstep(0, 0.004, max(0,distanceToEdge)));
            return half2(rim, state.g);
        }
        half4 Frag(Varyings input, FRONT_FACE_TYPE front : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half2 damage = Cutout(input.positionWS);
            InputData data = (InputData)0;
            data.positionWS = input.positionWS;
            data.normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(front,1,-1);
            data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            data.bakedGI = SampleSH(data.normalWS);
            data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            data.shadowMask = 1;
            SurfaceData surface = (SurfaceData)0;
            surface.albedo = lerp(_BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb,
                half3(0.025,0.018,0.012),damage.x);
            surface.metallic = _Metallic * (1-damage.x);
            surface.smoothness = _Smoothness;
            surface.normalTS = half3(0,0,1);
            surface.occlusion = 1; surface.alpha = 1;
            surface.emission = half3(2.2,0.12,0.005) * damage.x * damage.y * damage.y;
            return UniversalFragmentPBR(data,surface);
        }
        half4 DepthFrag(Varyings input) : SV_Target { Cutout(input.positionWS); return 0; }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings output = Vert(input);
                float3 direction = _LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    direction = normalize(_LightPosition - output.positionWS);
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS,output.normalWS,direction));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #endif
                return output;
            }
            ENDHLSL
        }
    }
}
