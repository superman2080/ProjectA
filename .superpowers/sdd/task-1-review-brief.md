# Task 1 Review Brief

## 리뷰 대상
`C:\Unity\ProjectA\Assets\Shaders\CelShader.shader`

## 스펙 요구사항 (확인 항목)
1. 셰이더 이름이 정확히 `Custom/CelShader`인가
2. 다음 9개 Properties가 모두 올바른 이름으로 존재하는가:
   `_BaseMap`, `_BaseColor`, `_Step1Threshold`, `_Step2Threshold`,
   `_ShadowColor1`, `_ShadowColor2`, `_RimColor`, `_RimPower`, `_RimIntensity`
3. `_BaseMap_ST`가 CBUFFER에 포함되어 있는가 (TRANSFORM_TEX 필요)
4. ForwardLit 패스에 `LightMode = UniversalForward` 태그가 있는가
5. 3단계 셀 셰이딩이 구현되었는가 (NdotL >= Step1 / NdotL >= Step2 / else)
6. Fresnel 림라이트가 구현되었는가 (`pow(1 - dot(normal, viewDir), power) * intensity`)
7. ShadowCaster 패스가 존재하며 `ShadowCasterPass.hlsl` include 없이 자체 구현인가
8. `RenderPipeline = UniversalPipeline` 태그가 있는가

## 코드 품질 확인 항목
- TEXTURE2D + SAMPLER 분리 선언 (SRP Batcher 요건)
- 두 패스 모두 동일한 CBUFFER 구조 사용 (URP 요건)
- URP include 경로 정확성
- YAGNI 위반 없음 (불필요한 기능 추가 없음)

## 보고 형식
아래 형식으로 보고:
```
스펙 준수: ✅ / ❌
항목별:
1. [PASS/FAIL] ...
...
코드 품질: Approved / Issues Found
발견사항:
- [Critical/Important/Minor] ...
```
