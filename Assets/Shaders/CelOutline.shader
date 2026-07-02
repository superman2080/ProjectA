Shader "Custom/CelOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.1, 0.1, 0.1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.05)) = 0.005
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-1" }

        Pass
        {
            Name "Outline"

            Cull Front
            ZTest Less

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _OutlineColor;
            float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posOS = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionHCS = TransformObjectToHClip(posOS);

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
