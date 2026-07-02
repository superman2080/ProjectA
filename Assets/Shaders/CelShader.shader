Shader "Custom/CelShader"
{
    Properties
    {
        _BaseMap        ("Base Texture", 2D)          = "white" {}
        _BaseColor      ("Base Color",   Color)       = (1, 1, 1, 1)
        _Step1Threshold ("Step 1 Threshold", Range(0, 1)) = 0.5
        _Step2Threshold ("Step 2 Threshold", Range(0, 1)) = 0.2
        _ShadowColor1   ("Shadow Color 1",   Color)   = (0.75, 0.75, 0.8, 1)
        _ShadowColor2   ("Shadow Color 2",   Color)   = (0.55, 0.55, 0.65, 1)
        _RimColor       ("Rim Color",        Color)   = (1, 1, 1, 1)
        _RimPower       ("Rim Power",    Range(0.1, 8)) = 3.0
        _RimIntensity   ("Rim Intensity", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
        }

        // ──────────────────────────────────────────
        // Pass 1: ForwardLit
        // ──────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   CelVert
            #pragma fragment CelFrag

            // URP keyword variants
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── Textures & Samplers ──────────────────
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // ── Constant Buffer ──────────────────────
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Step1Threshold;
                half   _Step2Threshold;
                half4  _ShadowColor1;
                half4  _ShadowColor2;
                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
            CBUFFER_END

            // ── Vertex Input / Output ────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float  fogCoord    : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Vertex Shader ────────────────────────
            Varyings CelVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs  = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = normInputs.normalWS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogCoord   = ComputeFogFactor(posInputs.positionCS.z);

                return OUT;
            }

            // ── Fragment Shader ──────────────────────
            half4 CelFrag(Varyings IN) : SV_Target
            {
                // Base color
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // Lighting
                half3 normalWS  = normalize(IN.normalWS);
                half3 viewDir   = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half  NdotL     = dot(normalWS, normalize(mainLight.direction)) * mainLight.shadowAttenuation;

                // 3-step cel shading
                half3 shadedColor;
                if (NdotL >= _Step1Threshold)
                    shadedColor = texColor.rgb;
                else if (NdotL >= _Step2Threshold)
                    shadedColor = texColor.rgb * _ShadowColor1.rgb;
                else
                    shadedColor = texColor.rgb * _ShadowColor2.rgb;

                // Rim light (Fresnel)
                half rim = pow(1.0h - saturate(dot(normalWS, viewDir)), _RimPower) * _RimIntensity;
                half3 finalColor = shadedColor + _RimColor.rgb * rim;

                // Fog
                finalColor = MixFog(finalColor, IN.fogCoord);

                return half4(finalColor, texColor.a);
            }
            ENDHLSL
        }

        // ──────────────────────────────────────────
        // Pass 2: ShadowCaster (self-contained)
        // ──────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Step1Threshold;
                half   _Step2Threshold;
                half4  _ShadowColor1;
                half4  _ShadowColor2;
                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
            CBUFFER_END

            // _MainLightPosition is declared in Shadows.hlsl / Core URP globals.
            // We reference it directly without re-declaring.

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);

                // Apply shadow bias to remove self-shadowing artefacts
                float3 lightDir = normalize(_MainLightPosition.xyz);
                posWS = ApplyShadowBias(posWS, normWS, lightDir);

                OUT.positionCS = TransformWorldToHClip(posWS);

                // Clamp to near-plane to avoid shadow holes on OpenGL-style APIs
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
