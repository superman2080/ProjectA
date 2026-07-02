# Task 4 Report: CelShaderUpgrader.cs 생성

## 완료 내용

### 생성된 파일
- **경로**: `Assets/Shaders/Editor/CelShaderUpgrader.cs`
- **크기**: 1.2KB
- **클래스명**: `CelShaderUpgrader`

### 구현 사항

#### 메뉴 항목
- **메뉴 경로**: `Tools/Cel Shader/Upgrade School Katana Girl Materials`
- **기능**: EditorWindow를 통해 School Katana Girl의 머티리얼을 일괄 업그레이드

#### 핵심 로직
1. `Shader.Find("Custom/CelShader")`로 타겟 셰이더 검색
2. 셰이더를 찾지 못하면 에러 로그 출력 후 반환
3. `Assets/99. External Assets/CombatGirlsCharacterPack/School_Katana_Girl/Materials` 폴더의 모든 머티리얼 검색
4. 각 머티리얼에 대해:
   - `_MainTex` 텍스처 추출
   - 셰이더를 `Custom/CelShader`로 교체
   - `_MainTex`를 `_BaseMap`에 복사 (프로퍼티 존재 시)
   - `EditorUtility.SetDirty()`로 변경 표시
5. `AssetDatabase.SaveAssets()`로 모든 변경사항 저장
6. 처리 결과 로그 출력 (업그레이드된 머티리얼 수)

#### 완료 기준 검증
- ✓ 파일이 정확한 경로에 존재
- ✓ MenuItem 경로가 정확하게 구현됨
- ✓ `Shader.Find("Custom/CelShader")` 실패 시 에러 처리 로직 포함
- ✓ `_MainTex` → `_BaseMap` 텍스처 복사 로직 구현
- ✓ `AssetDatabase.SaveAssets()` 호출로 변경사항 저장
- ✓ 코드가 브리프의 사양과 정확히 일치

### 사용 방법
1. Unity Editor에서 메뉴: `Tools → Cel Shader → Upgrade School Katana Girl Materials` 클릭
2. 콘솔에서 진행 상황 및 최종 결과 확인

---

**STATUS: DONE**
