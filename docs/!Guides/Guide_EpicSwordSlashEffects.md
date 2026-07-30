# Guide: Epic Sword Slash Effects System (Hovl Studio)

에셋 위치: `Assets/99. External Assets/Hovl Studio/Epic Sword Slash Effects System/`
스크립트 위치: `Assets/99. External Assets/Hovl Studio/HSFiles/Scripts/`

## 구성

| 파일 | 역할 |
|---|---|
| `HSFiles/Scripts/HS_SwordMeshTrail.cs` | 실제 트레일 생성. **칼 메쉬 오브젝트에 붙인다** |
| `HSFiles/Scripts/HS_SwordTrailAnimationEvents.cs` | Animation Event 수신 후 등록된 트레일들에 전달. **Animator 오브젝트에 붙는다** |
| `HSFiles/Scripts/HS_SwordTrailPreset.cs` | 룩 프리셋 SO (`Sword slash presets/*.asset` 22종) |

## 기본 사용법

1. 칼 메쉬 노드에 `HS_SwordMeshTrail` Add Component.
   - 이 프로젝트 경로: `root/add_weapon_r/Weapon_Katana_01_Blade/Weapon_Katana_01_Blade` (안쪽 메쉬 노드. 바깥쪽 아님)
2. `Preset` 슬롯에 프리셋 에셋 드롭 (예: `Slash blue.asset`). 드롭하면 `OnValidate`가 자동으로 `ApplyTrailPreset()` 호출 → 값 복사 + 머티리얼 레이어 생성 + Point A 이펙트 프리팹 인스턴스화.
   - 프리셋 편집 후엔 컴포넌트 우클릭 → **Apply Trail Preset** 다시 실행.
   - 프리셋 안 쓰면 `Trail Material` 직접 지정.
3. `Trail Point A/B` 자식 두 개가 자동 생성·자동 배치된다 (`Longest` 축 = 메쉬 bounds 최장축). 칼끝/칼밑이 안 맞으면:
   - `Automatic Axis`를 `LocalX/Y/Z`로 고정
   - `Point A Inset` / `Point B Inset` / `Endpoint Padding`으로 길이 조정
   - 수동으로 옮겼으면 `Recalculate Points On Awake` **끄기** (안 끄면 Awake에서 덮어씀)
4. Add Component 하면 `Reset()`이 부모 Animator를 찾아 `HS_SwordTrailAnimationEvents`도 자동 생성·등록한다. Animator 없으면 `Work Without Animation` 자동 on = 상시 발광.

### 주요 값

- `Trail Lifetime` 0.25~0.35 — 잔상 길이
- `Minimum Section Distance` 0.015 — 이보다 덜 움직이면 섹션 안 만듦
- `Lines Along Trail` — 폭 방향 분할. UV는 항상 0-1 정사각 유지
- `Smooth Low FPS` — Catmull-Rom 보간. 저프레임에서 직선 꺾임 방지
- `Maximum Dissolve` + `Dissolve Property Name`(`_Dissolve`) — `StopTrail` 후 `Trail Lifetime` 동안 디졸브. 0이면 머티리얼 손 안 댐

## 애니메이션 클립에 시작/끝 넣기 (Animation Event 방식)

`HS_SwordTrailAnimationEvents`의 public 수신 함수 3개:

| 함수명 | 동작 |
|---|---|
| `StartSwordTrail` | 발생 시작 |
| `StopSwordTrail` | 신규 섹션 중단. 기존 잔상은 Lifetime대로 사라짐 |
| `ClearSwordTrail` | 즉시 전부 삭제 (컷/텔레포트용) |

넣는 방법:
1. 클립 선택 → Animation 창 열기 (읽기전용 FBX 클립이면 Ctrl+D로 복제 후 진행)
2. 타임라인 상단 이벤트 바에서 우클릭 → **Add Animation Event**, 또는 Inspector 클립 → Events 섹션
3. Function 드롭다운에서 `StartSwordTrail` 선택 (드롭다운에 뜨려면 Animator 오브젝트에 `HS_SwordTrailAnimationEvents`가 붙어있어야 함)
4. 칼 휘두르기 시작 프레임에 Start, 끝 프레임에 Stop

데모 클립 `Demo scene/SlashCombo.anim`이 정확히 이 패턴 — `time: 0.0667`에 `StartSwordTrail` 이벤트.

## 이 프로젝트에서 실제 사용하는 방법 — Animation Clip Trimmer로 클립에 직접 기록

`WeaponTrailController`는 `Tiny.Trail`(별도 외부 에셋) 전용이라 HS 트레일 제어에는 못 쓴다. 대신 `CharacterActionPlayer`가 재생하는 클립 자체에 Animation Event를 심는다.

`Tools/Animation Clip Trimmer`(`AnimationClipTrimmerWindow.cs`)에 **Sword Trail Events** 섹션이 있다:
- **Write StartSwordTrail(Start) / StopSwordTrail(End) to Clip** 버튼을 누르면, 창에서 잡은 **Start 마크 시각**에 `StartSwordTrail`, **End 마크 시각**에 `StopSwordTrail` AnimationEvent를 클립에 직접 기록한다.
- 기존에 같은 이름으로 찍힌 이벤트는 먼저 지우고 새 마크 위치로 다시 찍는다 — 여러 번 눌러도 중복 안 쌓임.
- 클립에 붙는 이벤트라 `CharacterActionPlayer`의 트림/배속 압축과 무관하게 항상 트림 구간의 Start/End에서 정확히 호출된다.
- 전제조건: Animator 오브젝트에 `HS_SwordTrailAnimationEvents`가 붙어 있어야 함수 호출이 실제로 트레일에 전달된다(칼 메쉬의 `HS_SwordMeshTrail`이 자동 등록).
- **인터럽트(연계·미스)로 클립이 끊기는 경우 `StopSwordTrail`이 못 불릴 수 있다** — 클립 이벤트 방식의 구조적 한계. 트레일이 계속 켜진 채 남으면 다음 스윙의 `StartTrail()`이 `clearPreviousTrailOnStart`로 정리해준다(기본값 on).

원문 가이드: `Epic Sword Slash Effects System/Demo scene/Sword_Mesh_Trail_System_User_Guide.docx.pdf`
