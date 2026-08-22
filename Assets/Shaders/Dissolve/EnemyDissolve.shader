// 적이 사라질 때만 잠깐 입는 소멸 셰이더.
// Assets/Shaders/Dissolve/Dissolve.shadergraph의 룩(노이즈 클립 + 타는 테두리)을 따르되,
// 진행을 _Dissolve 하나로 <b>바깥에서</b> 민다 — 그래프 쪽은 _Time으로 스스로 돌아서
// "언제 시작해 몇 초에 끝나는가"를 EnemyView가 잡을 수 없다.
Shader "Custom/EnemyDissolve"
{
    Properties
    {
        _BaseMap    ("Base Texture", 2D)        = "white" {}
        _BaseColor  ("Base Color", Color)       = (1, 1, 1, 1)
        _NoiseTex   ("Dissolve Noise", 2D)      = "gray" {}
        _Dissolve   ("Dissolve", Range(0, 1))   = 0
        _EdgeWidth  ("Edge Width", Range(0.001, 0.4)) = 0.08
        [HDR] _EdgeColor ("Edge Color", Color)  = (4, 1.2, 0.2, 1)
        _Ambient    ("Ambient", Range(0, 1))    = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "AlphaTest"
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

            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _NoiseTex_ST;
                float4 _BaseColor;
                float4 _EdgeColor;
                float  _Dissolve;
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
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.uv * _NoiseTex_ST.xy + _NoiseTex_ST.zw).r;

                // ⚠ 0..1을 그대로 문턱으로 쓰면 _Dissolve=0에서 noise==0인 픽셀이 이미 잘린다.
                // 범위를 살짝 넘겨 잡아야 "0이면 온전, 1이면 전멸"이 정확히 성립한다.
                half threshold = lerp(-0.01h, 1.01h, saturate(_Dissolve));
                clip(noise - threshold);

                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                // 토스트 셰이딩은 흉내만 낸다 — 1초 남짓 타 없어지는 동안만 입는 옷이다.
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalize(input.normalWS), mainLight.direction)) * 0.5h + 0.5h;
                col.rgb *= lerp(_Ambient, 1.0h, ndotl) * mainLight.color;

                // 잘리기 직전 띠가 탄다.
                half edge = 1.0h - saturate((noise - threshold) / max(_EdgeWidth, 1e-4h));
                col.rgb = lerp(col.rgb, _EdgeColor.rgb, edge * step(0.0001h, _Dissolve));
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
