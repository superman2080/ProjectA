# Task 1 Brief: CelShader.shader 생성

## 목표
URP 호환 커스텀 Cel 셰이더를 `Assets/Shaders/CelShader.shader`에 생성한다.
이 파일은 3단계 셰이딩(NdotL 기반)과 Fresnel 림라이트를 ForwardLit 패스로 구현하고, ShadowCaster 패스도 포함한다.

## 글로벌 제약
- 렌더 파이프라인: URP (Universal Render Pipeline)
- 셰이더 이름: `Custom/CelShader` (정확히 이 이름이어야 Task 4의 Shader.Find가 작동)
- 저장 경로: `Assets/Shaders/CelShader.shader`
- Unity URP 14+ 호환 HLSL

## 생성할 파일
- `Assets/Shaders/CelShader.shader`

## 셰이더 명세

### Properties (정확한 이름 필수)
| Property | Type | Default |
|---|---|---|
| `_BaseMap` | 2D | "white" |
| `_BaseColor` | Color | (1,1,1,1) |
| `_Step1Threshold` | Range(0,1) | 0.5 |
| `_Step2Threshold` | Range(0,1) | 0.2 |
| `_ShadowColor1` | Color | (0.75,0.75,0.8,1) |
| `_ShadowColor2` | Color | (0.55,0.55,0.65,1) |
| `_RimColor` | Color | (1,1,1,1) |
| `_RimPower` | Range(0.1,8) | 3.0 |
| `_RimIntensity` | Range(0,1) | 0.4 |

### ForwardLit 패스 로직
1. `NdotL = dot(normalize(normalWS), normalize(mainLight.direction))`
2. `NdotL >= _Step1Threshold` → `texColor.rgb`
3. `NdotL >= _Step2Threshold` → `texColor.rgb * _ShadowColor1.rgb`
4. else → `texColor.rgb * _ShadowColor2.rgb`
5. 림라이트: `rim = pow(1 - saturate(dot(normalWS, viewDir)), _RimPower) * _RimIntensity`
6. 최종: `shadedColor + _RimColor.rgb * rim`

### ShadowCaster 패스
- `ApplyShadowBias(posWS, normWS, _MainLightPosition.xyz)` 사용
- 자체 vert/frag 구현 (ShadowCasterPass.hlsl include 없이)

## MCP 도구 사용
1. `Assets/Shaders/` 폴더 생성 확인 후 `create_script`로 파일 작성
2. `mcpforunity://editor/state` 읽어 컴파일 완료 대기
3. `read_console(types=["error"], count=10)` 에러 확인
4. 에러 있으면 수정 후 재업로드

## 보고서 파일
작업 완료 후 `.superpowers/sdd/task-1-report.md`에 보고서 작성:
- 생성된 파일 경로
- 컴파일 결과 (에러 없음 확인)
- 우려사항 (있는 경우)

## 상태 반환
마지막 줄에 반드시 다음 중 하나:
- `STATUS: DONE`
- `STATUS: DONE_WITH_CONCERNS`
- `STATUS: NEEDS_CONTEXT`
- `STATUS: BLOCKED`
