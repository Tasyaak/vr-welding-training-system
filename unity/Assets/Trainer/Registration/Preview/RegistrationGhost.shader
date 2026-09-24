Shader "WeldingTrainer/RegistrationGhost"
{
    Properties { _Color ("Color", Color) = (0.1,0.9,1,0.28) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct input { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct output { float4 position:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Color;
            output vert(input v)
            {
                output o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(output,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.position=UnityObjectToClipPos(v.vertex); return o;
            }
            float4 frag(output i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return _Color; }
            ENDHLSL
        }
    }
}
