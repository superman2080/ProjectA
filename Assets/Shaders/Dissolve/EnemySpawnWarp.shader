// 적이 균열에서 나올 때만 잠깐 입는 등장 셰이더.
// EnemyDissolve.shader의 쌍둥이다 — 시간축만 반대이고(죽을 때 vs 태어날 때),
// 진행을 _Warp 하나로 <b>바깥에서</b> 민다(그래프가 _Time으로 스스로 돌면
// "언제 시작해 몇 초에 끝나는가"를 EnemyView가 잡을 수 없다).
//
// 늘어남은 정점 변형이다. 진짜 슬릿스캔(과거 프레임 N장 합성)은 스키닝된 포즈 히스토리를
// 프레임마다 떠야 해서 "곡 도중 할당 금지"와 정면으로 충돌한다(docs/EnemySpawnWarp).
//
// ⚠ _Warp = 0이면 변형항이 정확히 0이다 — 도착 프레임의 원형 복원이 분기가 아니라 식으로 보장된다.
Shader "Custom/EnemySpawnWarp"
{
    Properties
    {
        _BaseMap    ("Base Texture", 2D)        = "white" {}
        _BaseColor  ("Base Color", Color)       = (1, 1, 1, 1)
        _Warp       ("Warp", Range(0, 1))       = 0
        _WarpAxisOS ("Warp Axis (Object Space)", Vector) = (0, 0, 1, 0)
        _WarpCenterOS("Warp Center (Object Space)", Vector) = (0, 0, 0, 0)
        _WarpStretch("Warp Stretch (Object Space Length)", Float) = 2
        _WarpSpan   ("Warp Span", Float)        = 1.8
        _EdgeWidth  ("Edge Width", Range(0.001, 1)) = 0.25
        [HDR] _EdgeColor ("Edge Color", Color)  = (2, 2.4, 4, 1)
        _Ambient    ("Ambient", Range(0, 1))    = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _EdgeColor;
                float4 _WarpAxisOS;
                float4 _WarpCenterOS;
                float  _Warp;
                float  _WarpStretch;
                float  _WarpSpan;
                float  _EdgeWidth;
                float  _Ambient;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  scan       : TEXCOORD2; // 스캔 좌표(0 = 앞머리, 1 = 균열 쪽 꼬리)
            };

            // 이 정점이 스캔 순서에서 얼마나 '뒤'인가. 균열 쪽(축과 같은 방향)일수록 1에 가깝다.
            //
            // ⚠ 축·중심·길이는 전부 EnemyView가 <b>렌더러마다</b> 채워 넣는다(그 공간의 바운즈에서 뽑는다).
            // 여기 적힌 기본값은 미리보기용이고, 런타임에서 쓰이는 값이 아니다.
            float ScanCoord(float3 positionOS)
            {
                float3 local = positionOS - _WarpCenterOS.xyz;
                return saturate(0.5 + dot(local, _WarpAxisOS.xyz) / max(_WarpSpan, 1e-4));
            }

            Varyings vert(Attributes input)
            {
                float3 posOS = input.positionOS.xyz;
                float  scan  = ScanCoord(posOS);

                // 뒤쪽일수록 균열 쪽으로 더 끌려간다 -> 꼬리가 길게 늘어난다.
                //
                // ⚠ 길이는 <b>균열까지의 실제 거리</b>다(EnemyView가 매 프레임 채운다). 상수를 쓰면
                // 균열이 9m 떨어졌을 때 줄이 닿지 못해 "그냥 조금 찌그러진 적"이 된다.
                // scan = 1인 정점이 정확히 균열에 앉으므로 <b>줄이 균열에 붙어 있다</b>.
                //
                // ⚠ _Warp를 곱하지 않는다. 그것은 스캔 띠의 자리일 뿐이고, 길이는 거리가 정한다.
                posOS += _WarpAxisOS.xyz * (_WarpStretch * scan);

                Varyings o;
                o.positionCS = TransformObjectToHClip(posOS);
                o.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.scan       = scan;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                // 토스트 셰이딩은 흉내만 낸다 — 1초 남짓 나오는 동안만 입는 옷이다(디졸브와 같은 근거).
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalize(input.normalWS), mainLight.direction)) * 0.5h + 0.5h;
                col.rgb *= lerp(_Ambient, 1.0h, ndotl) * mainLight.color;

                // 스캔 띠. 진행과 함께 몸을 훑고 지나간다(_Warp = 1 -> 꼬리 끝, 0 -> 앞머리).
                half band = 1.0h - saturate(abs(input.scan - _Warp) / max(_EdgeWidth, 1e-4h));
                col.rgb = lerp(col.rgb, _EdgeColor.rgb, band * step(0.0001h, _Warp));
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
