Shader "WeldingTrainer/Fusion Metal"
{
    Properties
    {
        _ColdColor("Cooled metal", Color) = (0.27,0.29,0.31,1)
        _CoolingSeconds("Cooling seconds", Float) = 6
        _FormationSeconds("Formation seconds", Float) = 0.12
        _DemoTime("Demo clock", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ColdColor;
                float _CoolingSeconds, _FormationSeconds, _DemoTime;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 times : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float heat : TEXCOORD2;
                float poor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float grow = smoothstep(0, max(0.01, _FormationSeconds), _DemoTime - input.times.y);
                // Authored workpiece positions must never scale toward the local origin.
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.poor = input.times.y;
                output.heat = saturate(1 - (_DemoTime - input.times.x) / max(0.1, _CoolingSeconds));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half heat = input.heat * input.heat;
                InputData data = (InputData)0;
                data.positionWS = input.positionWS;
                data.normalWS = normalize(input.normalWS);
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                data.bakedGI = SampleSH(data.normalWS);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                data.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(lerp(_ColdColor.rgb, half3(0.55,0.25,0.04), input.poor), half3(0.45,0.025,0.008), heat);
                surface.metallic = lerp(0.8, 0.25, heat);
                surface.smoothness = lerp(0.48, 0.78, heat);
                surface.normalTS = half3(0,0,1);
                surface.occlusion = 1;
                surface.alpha = 1;
                surface.emission = half3(2.8, 0.09, 0.008) * heat;
                return UniversalFragmentPBR(data, surface);
            }
            ENDHLSL
        }
    }
}
