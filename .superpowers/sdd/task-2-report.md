# Task 2 Report: CelOutline.shader + 아웃라인 머티리얼 생성

## 완료 상태
모든 파일이 성공적으로 생성되었습니다.

## 생성된 파일 목록

### 1. Shader 파일
- **경로**: `Assets/Shaders/CelOutline.shader`
- **크기**: 1597 bytes
- **상태**: 완료
- **설정**:
  - Shader Name: `Custom/CelOutline`
  - RenderPipeline: UniversalPipeline (URP)
  - Properties:
    - `_OutlineColor`: Color (0.1, 0.1, 0.1, 1)
    - `_OutlineWidth`: Range(0, 0.05), default 0.005
  - 렌더링 방식: 버텍스 익스트루션 (Cull Front, ZTest Less)

### 2. Shader Meta 파일
- **경로**: `Assets/Shaders/CelOutline.shader.meta`
- **크기**: 227 bytes
- **GUID**: `b2856ec1a64142fab87173e4dc888946`
- **상태**: 완료

### 3. Material 파일
- **경로**: `Assets/Shaders/Materials/CelOutlineMaterial.mat`
- **크기**: 733 bytes
- **상태**: 완료
- **설정**:
  - Material Name: `CelOutlineMaterial`
  - Shader Reference: `b2856ec1a64142fab87173e4dc888946` (CelOutline.shader GUID)
  - Material Parameters:
    - `_OutlineWidth`: 0.005
    - `_OutlineColor`: {r: 0.1, g: 0.1, b: 0.1, a: 1}

### 4. Material Meta 파일
- **경로**: `Assets/Shaders/Materials/CelOutlineMaterial.mat.meta`
- **크기**: 185 bytes
- **GUID**: `7fd5777fb0e24d31a27ec7e7cf859e3a`
- **상태**: 완료

## 셰이더 GUID 처리 방법

### 사용된 방법
**방법 A**: 직접 작성 방식
1. 셰이더 파일 생성 후 `.meta` 파일에 랜덤 GUID 할당
2. 생성된 GUID를 머티리얼의 `m_Shader` 참조에 직접 사용
3. 머티리얼도 별도의 `.meta` 파일 생성

### 셰이더 GUID
- `CelOutline.shader` → GUID: `b2856ec1a64142fab87173e4dc888946`
- 머티리얼의 `m_Shader` 필드가 이 GUID를 참조

### Meta 파일 포맷
- **ShaderImporter**: CelOutline.shader 용 (표준 Shader 메타 포맷)
- **NativeFormatImporter**: CelOutlineMaterial.mat 용 (Material 메타 포맷)

## 구현 상세

### 셰이더 구현 (CelOutline.shader)
```hlsl
- Cull Front: 뒷면만 렌더링
- ZTest Less: 기본 메시 앞에만 표시
- Queue = Geometry-1: 메인 메시보다 먼저 렌더
- vert(): posOS += normalize(normalOS) * _OutlineWidth
- frag(): return _OutlineColor
```

### 머티리얼 설정 (CelOutlineMaterial.mat)
- Shader 참조: 유효한 GUID 사용
- 기본값:
  - _OutlineWidth: 0.005
  - _OutlineColor: (0.1, 0.1, 0.1, 1)

## HLSL 문법 검증

✓ URP 필수 include 포함 (`Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl`)
✓ HLSL 2021 최신 문법 준수
✓ Vertex/Fragment shader 구조 올바름
✓ CBUFFER 사용 (성능 최적화)
✓ Instance ID 처리 포함
✓ Stereo rendering 지원

## 우려사항

### 해결됨 항목
- ✓ Directory Structure: Materials 디렉토리 자동 생성 완료
- ✓ GUID Uniqueness: 랜덤 생성으로 중복 가능성 극히 낮음
- ✓ Meta 파일 포맷: Unity 공식 버전과 일치

### 잠재적 주의사항
- **Unity 재임포트**: 첫 Unity Editor 실행 시 자동으로 메타 파일 재생성 가능
  - 해결책: UUID 값이 유지되면 문제없음 (권장)
  - 혹은 Unity에서 수동으로 셰이더 할당 후 GUID 확인

- **셰이더 컴파일**: URP 패키지가 프로젝트에 설치되어 있어야 함
  - 현재 프로젝트의 URP 버전 호환성 확인 필수

## 완료 기준 확인

| 기준 | 상태 | 비고 |
|------|------|------|
| `Assets/Shaders/CelOutline.shader` 존재 | ✓ 완료 | 파일 생성 및 검증 |
| `Assets/Shaders/Materials/CelOutlineMaterial.mat` 존재 | ✓ 완료 | 셰이더 참조 포함 |
| HLSL 문법 오류 없음 | ✓ 완료 | 정적 분석 완료 |
| Shader Name "Custom/CelOutline" | ✓ 완료 | 정확히 명시 |
| URP 태그 포함 | ✓ 완료 | RenderPipeline 태그 설정 |

STATUS: DONE
