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
        _SphereCenterOS("Sphere Center (Object Space)", Vector) = (0, 0, 0, 0)
        _SphereRadiusOS("Sphere Radius (Object Space)", Float) = 0.35
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
                float4 _SphereCenterOS;
                float  _SphereRadiusOS;
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
                float  scan       : TEXCOORD2; // 형태가 정해지는 자리까지의 거리(0 = 지금 정해지는 중)
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

            // 형태가 정해지는 자리. <b>_Warp = 1이면 1 + _EdgeWidth</b>라 몸 전체가 구이고,
            // <b>_Warp = 0이면 0</b>이라 정확히 아무것도 안 남는다 — 양 끝이 식으로 포화된다.
            // 범위를 [0, 1]로 두면 늘어나는 내내 꼬리 _EdgeWidth 구간이 사람 형태로 남는다.
            float ResolveFront()
            {
                return _Warp * (1.0 + max(_EdgeWidth, 1e-4));
            }

            // 앞이 아직 지나가지 않은 정점은 제 자리가 아니라 <b>구 위</b>에 있다.
            // 앞은 꼬리(균열)에서 머리로 쓸려 오므로, 구 덩어리로 나와 꼬리를 노끈처럼 끌고 오다가
            // 앞이 훑고 지나가며 제 모양이 된다 — 분기가 아니라 식이다.
            //
            // ⚠ 기준은 원본 positionOS다. 구로 옮긴 좌표로 스캔을 재면 좌표 범위가 구 지름으로
            // 무너져 줄이 균열에 안 닿는다.
            float SphereAmount(float scan, float front)
            {
                return saturate((front - scan) / max(_EdgeWidth, 1e-4));
            }

            Varyings vert(Attributes input)
            {
                float3 posOS = input.positionOS.xyz;
                float  scan  = ScanCoord(posOS);

                // 구는 몸 중심에 앉는다(_WarpCenterOS는 축 위의 점이라 발밑이 된다 — 여기 쓰면 안 된다).
                // ⚠ 중심·반지름은 EnemyView가 <b>월드 기준 하나</b>를 잡아 렌더러마다 옮겨 넣는다 —
                // 렌더러마다 따로 재면 몸과 무기가 각각 구가 되어 <b>구가 둘</b>이 된다.
                float  front    = ResolveFront();
                float3 toCenter = posOS - _SphereCenterOS.xyz;
                float  dist     = max(length(toCenter), 1e-4);
                float3 sphereOS = _SphereCenterOS.xyz + toCenter * (_SphereRadiusOS / dist);

                float sphere = SphereAmount(scan, front);
                posOS = lerp(posOS, sphereOS, sphere);

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
                o.normalWS   = TransformObjectToWorldNormal(normalize(lerp(input.normalOS, toCenter / dist, sphere)));
                o.scan       = scan - front; // 띠는 형태가 정해지는 자리에 앉는다(프래그먼트는 거리만 본다)
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                // 토스트 셰이딩은 흉내만 낸다 — 1초 남짓 나오는 동안만 입는 옷이다(디졸브와 같은 근거).
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalize(input.normalWS), mainLight.direction)) * 0.5h + 0.5h;
                col.rgb *= lerp(_Ambient, 1.0h, ndotl) * mainLight.color;

                // 스캔 띠. <b>형태가 정해지는 자리</b>에 앉아 꼬리에서 머리로 쓸려 온다 —
                // 빛나는 곳이 곧 구에서 제 모양으로 돌아오는 경계다(버텍스가 이미 거리를 넘겨준다).
                half band = 1.0h - saturate(abs(input.scan) / max(_EdgeWidth, 1e-4h));
                col.rgb = lerp(col.rgb, _EdgeColor.rgb, band * step(0.0001h, _Warp));
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
