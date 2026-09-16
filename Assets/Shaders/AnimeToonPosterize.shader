// 툰 단색화 - 이미 계산된 화면의 밝기를 N단계로 잘라 그라데이션을 계단으로 만든다.
// FullScreenPassRendererFeature(URP 내장)가 AfterRenderingPostProcessing에 찍는다.
//
// 조명을 다시 계산하지 않는다. 그래서 포인트 라이트 12개도, 베이크 GI도, 에셋팩 셰이더도
// 전부 그대로 살아 있고 계단만 얹힌다 - 머티리얼 ~140개를 이관하지 않는 근거가 이것이다.
//
// 주의: 이 패스는 화면을 읽는다. 피처의 Fetch Color Buffer를 켜야 하며
// 세로선(AnimeFilmScratch)과 반대다. 그리고 리스트에서 세로선보다 앞이어야 한다 -
// 뒤에 두면 필름 긁힘까지 계단화돼 선이 밴드에 먹힌다(docs/AnimeFilmLook).
Shader "Hidden/AnimeToonPosterize"
{
    Properties
    {
        _Steps      ("Steps", Range(2, 16))         = 8
        _Softness   ("Band Softness", Range(0, 0.5)) = 0.02
        _Strength   ("Strength", Range(0, 1))       = 1
        [Toggle(_POSTERIZE_RGB)] _PosterizeRGB ("Posterize RGB (off = luminance only)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "AnimeToonPosterize"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _POSTERIZE_RGB

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Steps;
                float _Softness;
                float _Strength;
                float _PosterizeRGB;
            CBUFFER_END

            // 주의: 이 시점의 화면 값은 선형(linear)이다. sRGB 변환은 마지막 블릿이 한다.
            // 균등 간격으로 그냥 자르면 눈에 보이는 중간 밝기가 실제로는 0.01~0.05라
            // 전부 최하위 밴드로 눌려 화면이 통째로 검어진다. 그래서 지각 공간으로 옮겨
            // 자르고 되돌린다 - 밴드가 눈에 보이는 대로 고르게 퍼진다.
            // 단계 수만큼의 '레벨'로 반올림한다. floor가 아니라 round인 것이 중요하다 -
            // floor면 모든 픽셀이 반 밴드만큼 어두워져 화면 전체가 가라앉는다.
            // 분모가 steps가 아니라 steps - 1이라 최상단 레벨이 정확히 1.0에 닿는다.
            float Quantize(float x)
            {
                float levels = max(_Steps, 2.0) - 1.0;
                float scaled = LinearToSRGB(saturate(x)) * levels;
                float lower  = floor(scaled);
                float t      = scaled - lower;

                // _Softness = 0이면 완전한 계단. 키우면 경계만 아주 조금 무르게 한다 -
                // 하늘·안개처럼 넓은 그라데이션에서 계단이 압축 오류처럼 보이는 것을 막는다.
                float blend = _Softness <= 0.0
                    ? step(0.5, t)
                    : smoothstep(0.5 - _Softness, 0.5 + _Softness, t);

                return SRGBToLinear((lower + blend) / levels);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half3 posterized;

            #if defined(_POSTERIZE_RGB)
                // 채널마다 자른다. 색 수 자체가 줄어 인쇄물 느낌이 난다.
                posterized = half3(Quantize(source.r), Quantize(source.g), Quantize(source.b));
            #else
                // 밝기만 자른다. 색조·채도가 보존된 채 음영만 끊겨 셀 애니메이션에 가깝다.
                float lum = max(Luminance(source.rgb), 1e-3);
                posterized = source.rgb * (Quantize(lum) / lum);
            #endif

                half3 result = lerp(source.rgb, saturate(posterized), saturate(_Strength));
                return half4(result, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
