// 1960~70년대 필름 영사의 세로 긁힘 — 화면 외곽에 얇은 세로선이 몇 프레임만 떴다 사라진다.
// FullScreenPassRendererFeature(URP 내장)가 AfterRenderingPostProcessing에 찍는다.
//
// 이 셰이더는 화면을 읽지 않는다. 선만 그려 하드웨어 블렌드로 얹으므로
// 피처의 Fetch Color Buffer를 끌 수 있고, 그러면 전체 화면 복사 패스가 통째로 사라진다.
//
// 간헐성에 C#이 필요 없는 이유: "가끔씩"은 시간의 함수이고 _Time.y가 이미 그 시간이다.
// 슬롯(_FlickerRate로 나눈 시간 칸)과 열 번호를 해시해 그 슬롯 동안 보일 열을 고른다 —
// 난수 생성기도, 코루틴도, 상태 필드도 없다.
//
// 주의: 히트스톱(docs/HitStop)에 안 언다. _Time은 timeScale 밖이고,
// 필름 긁힘은 화면(영사기)의 사건이지 게임 세계의 사건이 아니라 안 어는 것이 맞다.
Shader "Hidden/AnimeFilmScratch"
{
    Properties
    {
        [HDR] _LineColor ("Line Color", Color)            = (0.92, 0.92, 0.88, 1)
        _Columns      ("Columns", Float)                  = 220
        _LineWidth    ("Line Width (column ratio)", Range(0.02, 1)) = 0.25
        _FlickerRate  ("Flicker Rate (slots/sec)", Float) = 12
        _ScratchesPerSecond ("Scratches Per Second", Range(0, 20)) = 0.6
        _EdgeStart    ("Edge Start", Range(0, 1))         = 0.62
        _Intensity    ("Intensity", Range(0, 1))          = 0.35
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "AnimeFilmScratch"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LineColor;
                float _Columns;
                float _LineWidth;
                float _FlickerRate;
                float _ScratchesPerSecond;
                float _EdgeStart;
                float _Intensity;
            CBUFFER_END

            // 주의: 마지막이 곱이면 안 된다. 균일한 두 값의 곱은 0 근처에 몰려서
            // (P(xy < t) = t(1 - ln t)) 작은 문턱에서 7배쯤 자주 걸린다 - 그러면
            // _ScratchesPerSecond가 말하는 값과 실제 빈도가 어긋난다. 실측으로 확인한 지점이다.
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // 외곽 마스크. 화면 중앙은 0이라 패턴인풋 주변에는 절대 안 뜬다.
                float edge = smoothstep(_EdgeStart, 1.0, abs(uv.x * 2.0 - 1.0));

                // 열 선택. 칸 대비 비율로 굵기를 잡아 해상도가 바뀌어도 선이 따라온다.
                float columns   = max(_Columns, 1.0);
                float columnPos = uv.x * columns;
                float column    = floor(columnPos);
                float offset    = abs(frac(columnPos) - 0.5);
                float stroke    = 1.0 - smoothstep(0.0, max(_LineWidth, 0.001) * 0.5, offset);

                // 간헐성. 이 시간 칸에서 뽑힌 열만 보인다.
                // 확률이 아니라 초당 줄 수로 저작한다 - 확률로 두면 쓸 만한 값이
                // 열 수와 슬롯 수에 묻혀 0.0002대가 되어 슬라이더로 손댈 수가 없다.
                float rate    = max(_FlickerRate, 0.001);
                float slot    = floor(_Time.y * rate);
                float density = saturate(_ScratchesPerSecond / (columns * rate));
                float alive   = step(Hash21(float2(column, slot)), density);

                // 선마다 밝기를 조금 흔든다 — 전부 같은 밝기면 긁힘이 아니라 격자로 읽힌다.
                float bright = 0.6 + 0.4 * Hash21(float2(column + 7.1, slot + 3.7));

                float alpha = edge * stroke * alive * bright * _Intensity;
                return half4(_LineColor.rgb, alpha * _LineColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
