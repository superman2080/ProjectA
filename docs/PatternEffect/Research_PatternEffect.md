# Research — 패턴별 월드 이펙트 (PatternEffect)

## 요구사항 (확정)
1. **패턴 에셋별 전용 이펙트** — `Pattern`이 이펙트를 소유한다(`SuccessAnimationClip`·`sliceTarget`과 같은 결).
2. **월드 공간** — Canvas가 아니라 3D. 칼·적 위치에 뜬다.
3. **여러 개를 리스트로** — 한 패턴에 이펙트가 몇 개든 붙는다. 임팩트 순간만이 아니라 **패턴 진행 중 어느 시점, 어느 부착 지점**(플레이어·칼·적·씬 앵커)이든 지정할 수 있어야 한다.
4. **적 반응에 따라 갈린다** — 막는 애니메이션이면 스파크, 뒷구르기면 무연출.
5. **이펙트 저장/타이밍 툴** — 큐 목록을 편집하고 패턴 진행 타임라인 위에서 시각을 맞추는 에디터 창(`Tools/Animation Clip Trimmer`와 같은 결).

---

## 1. 지금 이펙트 시스템이 하는 일 — 그리고 안 하는 일

### `EffectManager` (`Assets/02. Scripts/Effect/EffectManager.cs`)
- Canvas 이펙트의 **유일 관리 지점**. `PatternHandler`의 확장 이벤트만 구독(`OnFocusRingResolved`/`OnNodeConnected`/`OnPatternComplete`) + `EnemyDirector.OnEnemyKilled`.
- 키는 **`EffectTrigger` enum**(Perfect/Good/Miss/PatternCompleteFull/PatternComplete/NodeConnected/Parried/EnemyEvaded/EnemyKilled/ProjectileSliced).
- 즉 **매핑 단위가 "판정 사건"이지 "패턴"이 아니다.** 어떤 패턴을 성공해도 `PatternCompleteFull` 하나로 수렴한다 → 요구사항 1을 지금 구조로는 표현할 수 없다.
- 풀링: 프리팹별 `Queue<CanvasEffectView>` + `maxSizes` 상한. `overlayLayer`/`ambientLayer` 아래에만 인스턴스가 산다.
- 좌표: `WorldToLocal()`로 **월드→Canvas 로컬**로 눌러 담는다. `EnemyKilled`조차 적의 월드 좌표를 스크린으로 투영해 UI 평면에 그린다 → **월드 이펙트가 아니다**(원근·가림·카메라 앵글 회전과 무관하게 납작하게 뜬다).

### `CanvasEffectView` (`Effect/CanvasEffectView.cs`)
- `UIParticle`(Coffee.UIExtensions) 전제. `OnSpawn`/`OnDespawn`, 파티클 전멸 감지 후 `OnFinished` 발행 → 매니저가 회수. `maxLifetime` 안전장치 8초.
- **`UIParticle` 의존이 Canvas 전용의 근거다.** 월드 파티클에 그대로 못 쓴다(=`ParticleSystem`만 있으면 충분).

### 월드 이펙트의 현 상태 — `SliceTargetDirector`
```csharp
private void PlayEffect(GameObject prefab, Vector3 position)
{
    if (prefab == null) return; // 비우면 무연출
    Instantiate(prefab, position, Quaternion.identity);
}
```
- `sliceEffectPrefab` / `crushEffectPrefab` 둘뿐이고 **풀링도 회수도 없다.** 곡 도중 `Instantiate`는 CLAUDE.md §5가 명시한 히치 원인(판정 손실)이며, 파괴되지 않아 씬에 누적된다.
- **결론: 월드 이펙트를 위한 인프라는 사실상 없다.** 새로 만들어야 하는 것은 "패턴별 매핑"만이 아니라 **월드 풀링/수명 관리**까지다. 다만 두 프리팹은 새 시스템으로 흡수시킬 수 있는 자연스러운 후보다.

---

## 2. 시각(타이밍)의 진실 — 어디에 붙일 수 있나

프로젝트 전체가 **하나의 절대 시각**을 공유한다:

```
impactTime = Deadline + Pattern.ImpactOffset
           = LastNodeTime + PatternHandler.GoodWindow + Pattern.ImpactOffset
```

이 식을 읽는 곳(전부 같은 식을 각자 계산):
| 소비자 | 파일 | 용도 |
|---|---|---|
| `CharacterActionPlayer` | Character/CharacterActionPlayer.cs | 플레이어 칼 임팩트 프레임 정렬 |
| `EnemyView` / `EnemyDirector` | Enemy/ | 적 칼·사망 클립·절단(`burstTime`) |
| `SliceTargetDirector` | Slice/ | 표적 도착·절단 |
| `CameraDirector` | Camera/ | 쉐이크·펀치 큐 |
| `HitStopDirector` | HitStop/ | 정지 창 발사 |

**`HitStopDirector`가 가장 가까운 선례다** — 예약 하나(`hasPending`/`pendingFireTime`), `OnPatternComplete`에서 시각 계산, `Update`에서 발사, `OnAllPatternsCleared`에서 취소. 40줄짜리 골격. 이펙트 디렉터도 같은 골격이면 새 수학이 필요 없다.

### 붙을 수 있는 다른 시각들
- `PatternHandler.OnPatternQueued` — 큐 투입(등장에 시간이 걸리는 연출용). ⚠ 이 시점엔 상대 적이 정해지지 않는다(§11-1) → **적에 붙는 이펙트는 여기서 예약하면 안 된다.**
- `CharacterActionPlayer.OnSwingBegan` / `OnSwingEnded` — 스윙 트림 구간. **성공 베기에서만** 발행, 인터럽트에서도 종료가 나온다. 칼에 붙는 지속형(트레일 계열) 연출의 정석 구독처(`WeaponTrailController` 선례).
- `CharacterActionPlayer.OnPlayerHit` — 적 칼이 닿는 순간.
- `EnemyDirector.OnEnemyBurst` — 절단 = 임팩트(화면 사건). `OnEnemyKilled`는 확정이라 **화면엔 아직 아무 일도 없다**(카메라 쉐이크가 `OnEnemyBurst`를 듣는 이유와 동일).

### "적이 막았다"는 지금 어디서 정해지나 (실패 연출의 근거)
- `EnemyView.Resolve()`(`Enemy/EnemyView.cs:440~`)의 `parried` 분기:
  ```csharp
  bool parried = !playerSucceeded && attacker != Attacker.Enemy && distance <= 0f;
  ```
  → **실패 + `Attacker.Player` + 후퇴거리 0**일 때만 "막았다". 후퇴거리가 양수면 **회피**(`evadeStateName`)라 칼이 만나지 않는다.
- 그 거리를 정하는 곳은 `EnemyDirector.ResolveRetreatDistance()`(`:835`)이고, 판단 기준은 **다음 패턴의 창**이다 — 창이 넉넉하면 물러나고(회피), 짧으면 제자리에서 막는다(패링). **즉 같은 실패라도 음악이 연출을 고른다.**
- ⚠ **패링/회피를 알리는 이벤트가 없다.** `EffectTrigger`에 `Parried`·`EnemyEvaded` 값은 있지만 `EffectManager`가 구독하는 이벤트 중 이를 발행하는 곳이 없어 **죽은 enum 값**이다.
- ⚠ **확정 시각 ≠ 화면 사건 시각.** `ResolveReservation`은 마지막 노드 입력 시점에 돌고, 칼이 만나는 순간은 `impactTime`(= Deadline + ImpactOffset)이라 그보다 늦다. `OnEnemyKilled`(확정) / `OnEnemyBurst`(임팩트)가 갈려 있는 것과 같은 구조 — **막는 이펙트도 확정이 아니라 임팩트에 떠야 한다.**
- 참고로 반대 방향의 "막음"도 있다: `Attacker.Enemy` + **성공** = 플레이어가 받아쳐 적이 밀려난다(`knockBackStateName`, `EnemyView.cs:455`).

### ⚠ `Time.timeScale`·히트스톱과의 관계
- 히트스톱은 `Animator`의 Speed Multiplier만 멈춘다. **`ParticleSystem`은 안 멈춘다** — 현재 이음매 목록(`SlicePiece`)과 같은 부류의 알려진 구멍이다. 정지 창이 0.1초라 임팩트 이펙트도 그 동안 계속 재생된다.

---

## 3. `Pattern`이 이미 소유한 것들 — 새 슬롯의 선례

`Pattern.cs`(전역 `PatternSpace`)는 이미 "모양에 종속된 정적 데이터"를 여럿 소유한다:

| 필드 | 타입 | 성격 |
|---|---|---|
| `successAnimationClip` + `animationStartOffset`/`Duration`/`ImpactTime`/`Speed` | 개별 필드(구형) | 플레이어 베기 |
| `playerAttack` / `playerParry` / `enemyAttack` / `enemyFeint` / `enemyDeath` | **`ClipAlignment`** | 클립 5슬롯 |
| `sliceTarget` + `sliceTargetOffset` | `SliceSet` + Vector2 | 표적 |
| `impactOffset` | float | **공통 앵커 보정**(5개 소비자가 전부 읽음) |
| `duelDistanceOffset` | float | 결투 거리 보정 |
| `attacker` | enum | 역할 |

**`ClipAlignment`가 정확한 선례다** — `[Serializable]` 값 묶음 하나를 `Pattern`이 슬롯으로 여러 개 들고, `Tools/Animation Clip Trimmer`가 그 슬롯을 선택해 저장한다. 이펙트 슬롯도 같은 모양이면:
- 인스펙터에 그대로 뜬다(커스텀 에디터 불필요)
- `OnValidate` 검증 훅 자리가 이미 있다(`WarnUnusedSlots` 옆)
- 툴이 `SerializedProperty` 경로(`"enemyFeint"` 같은 문자열)로 슬롯을 집는 기존 방식을 그대로 쓴다

⚠ `Pattern.OnValidate`의 `WarnUnusedSlots()`는 **역할(`attacker`)에 따라 재생되지 않을 슬롯을 경고**한다. 이펙트 슬롯이 역할별로 갈리면 여기도 같이 손봐야 한다.

---

## 4. 툴 선례 — `AnimationClipTrimmerWindow` (804줄)

`Assets/02. Scripts/Character/Editor/AnimationClipTrimmerWindow.cs`
- `[MenuItem("Tools/Animation Clip Trimmer")]`, `EditorWindow`.
- 내부 `class Actor` 하나가 슬롯 하나를 대표하고(`label`, `slotPath` 문자열), `attacker`에 따라 `slotPath`를 갈아끼운다(`feint.slotPath = enemyIsAttacker ? null : "enemyFeint"`).
- 구조: `DrawInputs` → `DrawPreview`(씬 프리뷰 재생) → `DrawReadouts` → `DrawTimeline`/`DrawTimelineMarkers` → `DrawActorBlock` → `DrawApply`/`DrawSyncState`. `OnEditorUpdate`로 프리뷰를 매 프레임 굴린다.
- **이미 두 배우의 합주를 임팩트 기준 타임라인에 그린다** → 이펙트 트랙을 얹을 자리가 이미 있는 셈이지만, 사용자 요구는 **전용 창**이다.

다른 툴 선례: `PatternChartWindow`(엔트리 표 + 일괄 도구 + 읽기 전용 회색 표시), `MeshSliceBakerWindow`(씬 뷰 상호작용 + 제자리 재굽기로 GUID 유지).

---

## 5. 제약·함정 정리

1. **패턴이 겹친다**(`exposureDuration` 0.5초 리드타임). 같은 템플릿 두 개가 동시에 살아 있을 수 있으므로 **이펙트 예약도 인스턴스 단위 토큰이 필요**하거나, `HitStopDirector`처럼 "예약 최대 하나" 근거(완료가 순차적, 최소 0.4초 간격)에 기대야 한다.
2. **상대 적은 큐 시점에 정해지지 않는다**(§11-1). 적에 붙는 이펙트는 `OnPatternComplete` 이후(=상대 확정 후)에만 위치를 물어야 한다.
3. **곡 도중 `Instantiate` 금지**(히치=판정 손실). 프리웜/풀링 필수. `EnemyDirector.PrepareStage`가 `countdownDuration`(≥3초) 안에 프리웜하는 선례.
4. **카메라 앵글이 런타임에 바뀐다**(§7-5). 월드 이펙트는 어느 앵글에서도 읽혀야 한다 — 빌보드/스케일 고려 대상.
5. **히트스톱이 파티클을 안 멈춘다**(위 §2).
6. **네이밍 규칙**: 코드 식별자는 영문 PascalCase(밑줄 없음), 문서/주석은 한국어.
7. **`Attacker.Player` 실패엔 피격이 없다** — 그 실패의 화면상 사실은 "적이 막았다" 하나뿐(§6). 실패용 이펙트 슬롯을 만든다면 이 분기를 따라야 한다.

---

## 6. 미결 → Plan에서 결정된 것

| 미결이던 것 | 결론 | 근거 |
|---|---|---|
| 슬롯 개수 | 슬롯 없음 — **큐 리스트 하나** | 개수·시점이 만들면서 정해진다 (Plan D0) |
| 조건 구분 | 적 **반응 단위** 넷(Always/Success/Parry/Evade) | 반응 클립이 셋뿐이고, 화면 사실이 거기서 갈린다 (D1) |
| 부착 지점 | **앵커 enum 넷 + `follow`** | 임팩트 앵커·플레이어·칼날·상대 (D2) |
| 시각 | **패턴 진행 기준점 다섯 ± 오프셋** | 전부 `PatternQueuedInfo`에서 나온다, 새 시계 없음 (D3) |
| 재생 소유자 | 새 `PatternEffectDirector` | Canvas 전제와 섞이지 않는다 (D4) |
| 반응 통지 | `EnemyDirector.OnEnemyReacted` 신규 | 패링/회피는 기존 이벤트로 알 수 없다 (D6) |
| `SliceTargetDirector` 이펙트 흡수 | **안 한다** | 요구 밖. 새 풀이 검증되면 이관 (D5) |
| 툴 프리뷰 | **한다** — 큐 하나씩 + 타임라인 전체 재생 | (Plan Step 7) |

여전히 열려 있는 것:
- 히트스톱 창 동안 파티클을 멈출지(현재 안 멈춤 — `SlicePiece`와 같은 부류의 알려진 구멍).
- 임의 씬 오브젝트를 앵커로 지정하는 `Custom` 값(다섯 번째가 실제로 필요해지면).
