Shader "WeldingTrainer/Fusion Graph"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 vertex:POSITION; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 vertex:SV_POSITION; half4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A input)
            {
                V output=(V)0; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex=TransformObjectToHClip(input.vertex.xyz); output.color=input.color; return output;
            }
            half4 Frag(V input):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); return input.color; }
            ENDHLSL
        }
    }
}
