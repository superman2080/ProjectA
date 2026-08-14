# Plan — 미사용 코드 정리 · 리팩토링

근거: `docs/Refactoring/Research_Refactoring.md`

원칙 셋:
1. **삭제가 기본**이다. "나중에 쓸지도"는 git이 기억한다.
2. **삭제와 이동을 한 Step에 섞지 않는다** — 되돌릴 때 뒤엉킨다.
3. **의미가 있는 미사용은 삭제 전에 판단을 먼저 받는다**(Step 0). 기능 유실인지 잔해인지는 코드가 답하지 못한다.

Step 순서는 위험도 오름차순이다. 1~3은 씬/에셋을 건드리지 않아 되돌리기가 공짜고, 6~7은 씬 재직렬화를 부른다.

---

## Step 0 — 판단 3건, **전부 확정됨** (남은 대기 없음)

| # | 대상 | 결정 | 작업 위치 |
|---|---|---|---|
| 0-1 | `Pattern.sliceTarget` / `sliceTargetOffset` | **삭제** — 구 연출 잔해. 인프라는 살아 있어 부활은 호출부 3줄 | Step 2-4 · [부록 A](#부록-a--패턴-소유-표적-부활-레시피) |
| 0-2 | `WeaponTrailController` | **삭제** — 트레일 on/off는 이제 **애니메이션 클립 이벤트**가 한다. 이 컴포넌트는 대체된 구현이다 | Step 1-8 |
| 0-3 | `PlayerHealth.OnDamaged` / `OnDepleted` | **유지** — `OnDepleted`는 향후 **플레이어 사망 연출의 진입점**이다. 구독자가 없는 것은 미완성이지 사망이 아니다 | Step 1-9 (문서 표기만) |

---

## Step 1 — 참조 0 삭제 (씬 영향 없음)

- [x] 1-1. `Assets/02. Scripts/Statemachine/` 폴더 통째로 삭제(`.meta` 포함, 359줄). CLAUDE.md §10에서 해당 항목 제거.
- [x] 1-2. 미사용 public 멤버 삭제:
  - `EnemyDirector.cs:1223` `DuelAnchorPosition()`
  - `EnemyView.cs:348` `ReadyBy()` / `:87` `MoveSpeed`
  - `EnemyDefinition.cs:40` `DisplayName`
  - `MeshSliceBaker.cs:70` `Validate(Mesh)` (호환용 shim)
  - `CorpseView.cs:36,39` `Age`, `RootPiece`
  - `SliceTargetView.cs:161` `ImpactPosition`
  - `DodgePointView.cs:90` `IsShowing`
  - `CameraAngleSwitcher.cs:49` `CurrentIndex`
  - `PatternTemplateLibrary.cs:43` `MaxNodeCount`
  - ~~`MeshSliceBakerWindow.cs:29` `const PieceMeshPrefix`~~ — **오탐이라 되돌림**(보간 문자열 안에서 6곳이 쓰고 있었다)
- [x] 1-3. 미사용 private 필드 삭제: `CharacterActionPlayer.cs:203,205` `playStartTime`/`playDur`.
      같은 파일 `:201-202`의 "히트스톱 캐치업이 남은 길이를 구한다" 주석도 함께 삭제(밀기 모델로 바뀌어 거짓이 됐다).
- [x] 1-4. 구독자 없는 이벤트 삭제: `PatternLineRenderer.cs:36,38,39`(`OnSegmentPointAdded`/`OnFadeStarted`/`OnFadeCompleted`)와 각 `?.Invoke` 지점.
- [x] 1-5. `PatternHandler`의 이벤트 셋(`OnJudged:195` / `OnFocusRingSpawned:211` / `OnFocusRingMissedArrival:215`)은
      **CLAUDE.md가 확장 포인트로 공표한 API**다. 삭제하지 말고 **문서에서 "현재 구독자 없음"으로 표기**만 한다.
      단 CLAUDE.md의 "`OnJudged` — EffectManager가 구독"은 사실이 아니므로 정정한다(실제로는 `OnFocusRingResolved`).
- [x] 1-6. 불필요한 `using` 제거: `SliceTargetView.cs`(`System`), `SliceGeometry.cs`(`System.Collections.Generic`), `PatternLineRenderer.cs`(`System` — 이벤트 삭제로 새로 불필요해짐).
      ~~`ChartGen/Tests/*` 4파일, `DuetTimelineTests.cs`~~ — **오탐이라 되돌림**(네임스페이스 접두사 없이 타입을 쓰고 있었다).
- [x] 1-7. `CharacterActionPlayer.cs:400-404` 중복 `<summary>` 블록 제거(앞쪽 = `SwitchBaseState`용 잔해).
- [x] 1-8. **`Character/WeaponTrailController.cs` 삭제**(64줄, `.meta` 포함). 트레일 on/off는 애니메이션 클립 이벤트가 대신한다.
  - ⚠ **`CharacterActionPlayer`의 `OnSwingBegan`/`OnSwingEnded`(`:140,146`)와 `RaiseSwingBegan`/`RaiseSwingEnded`는 지우지 않는다.**
    `swingActive` 플래그가 `ApplyHitStop:737`의 가드("스윙 중일 때만 멈춘다")로 쓰이고 있어, Raise 경로를 걷어내면 **히트스톱이 조용히 안 걸린다.**
    이벤트 둘만 남는 비용은 4줄이고, 1-5와 같은 취급(공표된 확장 포인트, 현재 구독자 없음)으로 둔다.
  - CLAUDE.md §6의 `WeaponTrailController` 문단 통째로 교체 — 지금 서술("유일한 관리 지점", 배선 주의, 안쪽 칼날 노드)이 **전부 사실이 아니다**.
    대체 서술: 트레일은 애니메이션 클립 이벤트로 제어하며, `OnSwingBegan/Ended`는 구독자 없는 확장 포인트로 남아 있다.
- [x] 1-9. `PlayerHealth`는 **손대지 않는다.** CLAUDE.md에 한 줄만 추가한다 —
  *"목숨은 실제로 깎이지만 `OnDepleted` 구독자가 아직 없다(사망 연출 미구현). 이 이벤트가 그 진입점이다."*
  없으면 다음 사람이 "죽어도 아무 일 없다"를 버그로 보고 추적하게 된다.

**검증**: 컴파일 통과 + 기존 유닛테스트(`ChartGen`/`Slice`/`Enemy`/`Pattern` 4 asmdef) 전부 green.

---

## Step 2 — 인스펙터 노브 정리 (씬 값이 사라지므로 Step 1과 분리)

- [x] 2-1. `CharacterActionPlayer.cs:90` `sprintClip` 삭제. 툴팁이 주장하는 "길이를 읽어 배속 역산"은 실제로 `sprintReferenceSpeed`가 한다.
- [x] 2-2. `EnemyView.cs:82` `wanderArriveDistance` 삭제(읽는 코드 없음). 배회 정지 판정이 실제로 무엇을 보는지 `TickWander`(:415) 주석에 한 줄로 남긴다.
- [x] 2-3. `SongSelectManager.cs:8` `availableCharts` 삭제.
- [x] 2-4. **패턴 소유 표적 필드 삭제** — `Pattern.cs`의 네 줄:
  - `:64-65` `sliceTarget`(SliceSet) + `:138` `SliceTarget`
  - `:67-68` `sliceTargetOffset` + `:141` `SliceTargetOffset`

  ⚠ **`duelDistanceOffset`(`:76`, `DuelDistanceOffset:155`)은 건드리지 않는다.** 이름이 비슷하지만 전혀 다른 값이고
  살아 있다 — `EnemyDirector.DuelDistanceOf:965-974`(결투 거리 = `duelBaseDistance + 보정`) ·
  `MeshSliceBakerWindow.cs:75`(칼 평면 유도) · `PatternChartWindow.cs:848`이 읽는다.

  ⚠ **`SliceTargetDirector.Reserve`의 `Vector2 offset` 인자(`:123,149,155,156`)는 남긴다.**
  지금 호출부가 `Vector2.zero`를 넘길 뿐 죽은 코드가 아니고, 부록 A의 부활 경로가 이 인자를 그대로 쓴다.
  지웠다 되살리면 공개 서명이 두 번 흔들린다.
- [x] 2-5. CLAUDE.md §11 정정. 현재 서술이 **사실이 아니다**:
  - ~~"`Pattern.sliceTarget`이 이 에셋을 직접 참조한다"~~ → 실제 표적 소스는 `EnemyCue.projectile`(채보 엔트리 소유)
  - ~~"표적은 패턴당 하나"~~ → 토큰당 다중 예약이 가능하다(`SetOutcome:185-192`가 같은 토큰을 전부 순회한다)
  - ~~"배치는 `sliceTargetOffset`이 정한다"~~ → 발사 지점은 `Reserve`의 `spawnOverride`(쏘는 적의 링 위치)가 정한다
  - `SliceTargetDirector`가 이제 **투사체 디렉터**임을 명시(`EnemyDirector`의 필드명도 `projectileDirector`)
- [x] 2-6. 부록 A(부활 레시피)를 `docs/SliceTarget/`에 옮겨 적는다. **이 Plan은 언젠가 닫히지만 그 문서는 남는다.**

**검증**: Unity에서 `BattleScene`·`SongSelectScene` 열어 콘솔 경고 0. 직렬화 필드 삭제는 경고 없이 값만 사라진다 — **다른 노브가 리셋되지 않았는지 눈으로 확인**한다.
2-4는 패턴 에셋 전수(`04. Datas/Patterns/Templates/*.asset`)가 재직렬화된다 — diff에서 **`sliceTarget`/`sliceTargetOffset` 두 줄만** 빠졌는지 확인한다.

---

## Step 3 — `EffectTrigger` 죽은 값 정리 (단일 커밋, 씬 diff 0줄이 목표)

### 전제 — 데이터가 어디 사는가
**`EffectCatalog.cs`는 enum + struct 정의 파일일 뿐 ScriptableObject가 아니다.**
실제 매핑은 `EffectManager.catalog`(`EffectManager.cs:24`, `List<EffectEntry>`)에 **인라인 직렬화**되고,
그 컴포넌트 인스턴스는 **`BattleScene.unity` 한 곳뿐**이다(GUID `eed5329e7bd7bf942afed144331c57ca` 전체 검색 결과 씬 1건).
→ 위험 대상은 에셋이 아니라 **씬 파일**이다.

### 현재 씬 데이터 (`BattleScene.unity:1099-1120`)
```yaml
catalog:
- trigger: 0   # Perfect             프리팹 O (4/8)
- trigger: 1   # Good                프리팹 O (4/8)
- trigger: 2   # Miss                프리팹 O (3/6)
- trigger: 5   # NodeConnected       프리팹 O (8/16)
- trigger: 3   # PatternCompleteFull 프리팹 O (2/4)
```
**직렬화된 값이 전부 5 이하다. 6·7·8·9는 한 줄도 없다.**
그래서 `Parried`(6)·`EnemyEvaded`(7)를 지워 `EnemyKilled`가 8→6으로 밀려도 **가리킬 데이터가 애초에 없다** →
당초 계획했던 "명시 값 부여 커밋 → 삭제 커밋" 2단 분리는 이 데이터에서 불필요하다.

### 절차
- [x] 3-0. **작업 직전 재확인**(`BattleScene.unity`가 이미 `M` 상태다 — 그 사이 엔트리가 추가됐을 수 있다):
  ```bash
  grep -n -A3 "^  catalog:" "Assets/01. Scenes/BattleScene.unity"
  ```
  값 ≥6이 하나라도 있으면 **여기서 멈추고** 그 엔트리부터 처리한다.
- [x] 3-1. `Effect/EffectCatalog.cs`에서 `Parried` · `EnemyEvaded` · `ProjectileSliced` 삭제.
- [x] 3-2. **남는 값에 명시 정수를 박는다.** 삭제보다 이쪽이 본질적인 방어다 — 앞으로 값을 넣고 빼도 씬이 안 흔들린다:
  ```csharp
  public enum EffectTrigger
  {
      Perfect = 0,
      Good = 1,
      Miss = 2,
      PatternCompleteFull = 3,
      PatternComplete = 4,
      NodeConnected = 5,
      EnemyKilled = 8,   // 6,7은 삭제된 Parried/EnemyEvaded 자리 — 재사용 금지
  }
  ```
  ⚠ `EnemyKilled`를 6으로 **당기지 않는다**. 지금 씬은 어느 쪽이든 안 깨지지만, 8을 쓰는 브랜치를 나중에 머지할 때 어긋난다.
- [x] 3-3. **씬을 열지 않는다.** 지운 트리거를 쓰는 엔트리가 없으므로 인스펙터에서 정리할 대상이 없다.
      (씬을 열어 저장하면 관계없는 직렬화 노이즈만 diff에 낀다.)

### 검증
```bash
git diff --stat "Assets/01. Scenes/BattleScene.unity"   # 0줄이어야 한다
```
컴파일 통과 + 곡 1회 재생 → 판정 이펙트 3종(Perfect/Good/Miss) · `NodeConnected` · 패턴 완성 이펙트가 그대로 뜨는지 확인.

### 범위 밖 — 배선 안 된 트리거 2개 (이번에 건드리지 않는다)
씬 카탈로그에 프리팹이 없어 **오늘 무연출**인 값들. 카탈로그 규율("프리팹 비면 무연출")대로의 안전한 상태라 삭제 대상이 아니다.
- **`EnemyKilled`(8)** — `EffectManager.HandleEnemyKilled:80`이 구독·호출까지 하는데 프리팹이 없다.
  ⚠ 의도일 수 있다 — §7-4 월드 이펙트가 그 자리를 맡고, 처치 순간엔 히트스톱·`OnEnemyBurst` 쉐이크가 이미 있다. **잊힌 거라면 프리팹 한 줄로 살아난다.**
- **`PatternComplete`(4, 부분 완성)** — `PatternCompleteFull`(3)만 배선. 실패 패턴에 완성 이펙트가 없는 건 자연스러운 설계로 보인다.

---

## Step 4 — 프리팹 풀 일원화 (가장 큰 순삭, ~150줄)

`Pattern/Core`처럼 순수 C#으로 `Util/PrefabPool.cs` 하나를 신설한다(`MonoBehaviour` 아님).

```
public sealed class PrefabPool                      // 소유자가 필드로 하나씩 든다
{
    public PrefabPool(Transform root, bool reparentOnRent);
    public GameObject Rent(GameObject prefab, int maxSize);
    public void Release(GameObject instance);        // 마커에서 원본 프리팹을 읽는다
    public void Prewarm(GameObject prefab, int count, int maxSize);
}
```

- [x] 4-1. `PrefabPool` + 마커 컴포넌트 `PooledInstance`(필드 `SourcePrefab` 하나) 작성.
      `EnemyPooledInstance`(`EnemyDirector.cs:1906`) / `SlicePooledInstance`(`SliceTargetDirector.cs:477`)를 이걸로 통합한다.
- [x] 4-2. `SliceTargetDirector`를 먼저 이관한다(셋 중 가장 단순, `:328-395` 삭제).
- [x] 4-3. `EnemyDirector` 이관(`:1700-1770` 삭제). ⚠ 이쪽만 `Rent` 시 `SetParent(null)`을 한다 → 생성자 플래그 `reparentOnRent`로 흡수.
- [x] 4-4. `PatternEffectDirector`(`:369-430`)는 **반환 타입이 `CanvasEffectView`이고 `OnDespawn()` 훅이 붙는다.**
      제네릭으로 억지로 합치지 말고, `PrefabPool`을 내부에 두고 얇게 감싸는 형태로만 이관한다.
      *(합치는 비용 > 이득이라 판단되면 4-4는 건너뛰고 4-2·4-3만 한다 — 그래도 100줄이 준다.)*
- [x] 4-5. `Pool.cs`(`PoolKey` 싱글톤)는 **그대로 둔다.** `FocusRing` 한 키뿐이지만 `IPoolable` 계약이 다르고, 건드리면 판정 경로가 흔들린다.

**검증**: 곡 하나 완주. 적 사망 N회 후 `poolRoot` 자식 수가 상한 안에 머무는지, 표적 조각이 재사용되는지 하이라라키에서 확인.

---

## Step 5 — 임팩트 시각 식 일원화

- [x] 5-1. `PatternCompletionInfo`에 `Deadline` 필드 추가. `PatternHandler.CompletePattern`(`:664`)에서 `pattern.Deadline`을 그대로 실어 보낸다
      (`ActivePattern`이 이미 들고 있다 — 새 계산이 아니다).
- [x] 5-2. 세 payload struct에 공통 확장 하나를 둔다: `float ImpactTime => Deadline + (Template != null ? Template.ImpactOffset : 0f);`
      → `Pattern/PatternInfoExtensions.cs`(또는 각 struct의 프로퍼티).
- [x] 5-3. 호출부 치환:
  - `CameraDirector.cs:385-386` → `info.ImpactTime`
  - `CameraDirector.cs:429` → `info.ImpactTime`
  - `HitStopDirector.cs:94-95` → `info.ImpactTime`
  - `CharacterActionPlayer.cs:661` → `info.ImpactTime`
  - `EnemyDirector.cs:1096` → `info.ImpactTime`
- [x] 5-4. 그 결과 `CameraDirector`·`HitStopDirector`가 `handler.GoodWindow`를 참조할 이유가 사라진다 — 남은 참조를 확인하고 필드 배선도 정리.
- [x] 5-5. `PatternChartWindow.cs:883` `GoodWindowApprox = 0.1f` 상수 복제는 **그대로 둔다**(에디터 툴은 씬 `PatternHandler` 없이 도는 경로가 있다). 대신 "왜 복제인가" 주석 한 줄 추가.
- [x] 5-6. CLAUDE.md의 *"`Deadline`은 페이로드에 없다 — 직접 만든다"* 문장 삭제.

**검증**: `Pattern/Tests/PatternEffectCueTests.ImpactIsDeadlinePlusPatternImpactOffset`가 이미 이 식을 검증한다 → green 유지.
추가로 히트스톱·표적 절단·칼날 임팩트가 같은 프레임인지 육안 확인(한 프레임이라도 어긋나면 이 Step이 원인이다).

---

## Step 6 — 명명 통일 (순수 rename, 기능 변화 0)

- [x] 6-1. `PatternCompletionInfo.Pattern` → `Template`. `JudgeTargetInfo`·`PatternQueuedInfo`와 같은 이름이 된다.
      호출부: `CameraDirector.cs:385`, `HitStopDirector.cs:94`, `PatternEffectDirector.cs:178~`, `EnemyDirector.cs:1226~` 등.
- [x] 6-2. `PatternHandler.ComputeFallDuration(int pointIndex, float exposureDuration)`(`:409`) —
      본문이 `return exposureDuration;`뿐이고 인자 하나는 미사용이다. 유일 호출자인 `PatternChartWindow`에서
      **호출을 제거하고 `exposureDuration`을 직접 쓰게 한 뒤 메서드를 삭제**한다.
      (CLAUDE.md §4가 "시그니처만 유지"라고 적어 둔 이유가 이 호출자 하나다 — 그 호출자를 고치면 이유가 사라진다.)
- [x] 6-3. `PatternHandler.OnPointReleased(int index)`(`:458`)의 미사용 인자 제거 — `Point.OnPointUp` 시그니처가 `Action<int>`라면 `_`로 남긴다.

---

## Step 7 — `EnemyDirector` 분해 (1,910줄 → ~1,600줄)

**클래스를 쪼개지 않는다. 파일만 쪼갠다**(`partial`). 상태 필드가 서로 얽혀 있어 진짜 분리는 이 Plan의 범위를 넘고,
얻는 것은 "한 파일을 스크롤하지 않는다"뿐이라 그 값이면 partial로 충분하다.

- [x] 7-1. `EnemyDirector.Gizmos.cs` — `:1775-1900`(이미 전부 `#if UNITY_EDITOR`). ~125줄.
- [x] 7-2. `EnemyDirector.Recycle.cs` — `RecycleCorpses`/`ReleaseCorpse`/`RecycleDissolved`/`RecycleDebris`/`AllSettled`/`RecycleDebrisEntry`/`EnforcePieceBudget`(`:1585-1690`). ~105줄.
- [x] 7-3. Step 4가 끝났다면 풀 코드는 이미 사라졌다. 남아 있으면 `EnemyDirector.Pool.cs`로.
- [x] 7-4. 남은 본체가 여전히 1,600줄 이상이면 **거기서 멈춘다.** 무리·예약·처치는 서로의 상태를 직접 읽으므로 분리 비용이 이득을 넘는다.

**검증**: 컴파일 + 곡 1회 완주. partial 분리는 동작이 바뀌면 안 된다 — 바뀌었다면 이동 중 코드를 건드린 것이다.

---

## Step 8 — 저위험 잡정리

- [ ] 8-1. `Assets/_Recovery/0 (1).unity`(+`.meta`) 정리 — **보류.** 씬 복구본이라 지우는 판단은 사용자 몫이다(되돌릴 수 없다).
- [ ] 8-2. 외부 에셋 `.unitypackage.meta` 2건 커밋 or ignore 결정 — **보류.** 코드에 영향이 없고 커밋 정책 문제다.
- [x] 8-3. `clusterEnabled == false` 폴백(`ringCount:41` + `EnemyRing.PickStagePosition`) —
      **이번에는 지우지 않는다.** `EnemyRingTests` 4건이 이 경로를 검증하고 있어 코드·테스트를 함께 지워야 하며,
      §11-6이 아직 "폴백으로 남긴다"고 명시하고 있다. 무리 배치가 확정되면 그때 별도 작업으로 뺀다.

---

## 순서 요약

```
Step 0 (판단)  →  1 (삭제) → 2 (노브) → 3 (enum) → 4 (풀) → 5 (임팩트 식) → 6 (rename) → 7 (partial) → 8 (잡정리)
                  └ 여기까지는 되돌리기 공짜 ┘   └ 씬/에셋 재직렬화 구간 ┘
```

각 Step은 **독립 커밋 1개**로 만든다. 4·5·7은 실패 시 되돌릴 지점이 명확해야 한다.

---

## 부록 A — 패턴 소유 표적 부활 레시피

Step 2-4가 지우는 것은 **인프라가 아니라 배선 한 곳**이다. "날아와서 베이는 표적"을 다시 넣고 싶어지면 아래 세 줄이면 된다.

### 지우지 않는 것 (전부 그대로 산다)
| 자산 | 역할 |
|---|---|
| `Slice/SliceTargetDirector.cs` (481줄) | 예약 · 스폰 · 접근 · 임팩트 판정 · 풀 · 조각 예산 · 기즈모 |
| `Slice/SliceTargetView.cs` · `SlicePiece.cs` | 표적 하나의 이동/절단, 조각 운동 |
| `Slice/SliceSet.cs` · `Core/MeshSliceBaker.cs` · `Editor/MeshSliceBakerWindow.cs` | 굽기 산출물과 굽기 툴 |
| `SliceTargetDirector.Reserve`의 `Vector2 offset` 인자 | 아래 레시피가 그대로 쓴다 |

즉 지워지는 것은 `Pattern`의 필드 2개 + 그 프로퍼티 2개뿐이다.

### 부활 절차
1. `Pattern.cs`에 필드 둘을 되돌린다(`sliceTarget` / `sliceTargetOffset` + 프로퍼티).
2. `EnemyDirector.BindReservation`의 투사체 블록(`:1178-1182`) **옆에** 한 블록 더 놓는다:

```csharp
// 투사체(채보 엔트리 소유) — 쏘는 적 위치에서 날아온다
if (r.cue.projectile != null && projectileDirector != null)
{
    Vector3 origin = opponent != null ? opponent.RingPosition : Center;
    projectileDirector.Reserve(r.token, r.cue.projectile, r.startTime, r.impactTime, Vector2.zero, origin);
}

// 패턴 소유 표적 — spawnOverride를 주지 않으므로 씬의 spawnAnchor에서 날아온다
if (r.template?.SliceTarget != null && projectileDirector != null)
    projectileDirector.Reserve(r.token, r.template.SliceTarget, r.startTime, r.impactTime,
                               r.template.SliceTargetOffset);
```

### 왜 이걸로 끝인가 (구조가 이미 열려 있는 근거)
- **토큰당 다중 예약이 이미 성립한다.** `SetOutcome:185-192`가 `reservations`·`active` 양쪽에서 같은 토큰을 **전부 순회**해 확정한다. 투사체와 패턴 표적이 한 패턴에 동시에 떠도 같은 성패로 갈린다.
- **`spawnOverride`를 안 주면 `spawnAnchor`에서 날아온다**(`SpawnBase:178`). 그게 옛 패턴 표적의 경로 그대로다.
- 접근시간 클램프(`Reserve:145`), 프리팹 풀, 조각 예산(`EnforcePieceBudget`), 기즈모는 호출부와 무관하게 돈다.
- 임팩트 시각은 `r.impactTime` 하나를 공유하므로 §6 정렬(칼날 임팩트 = 절단)이 자동으로 맞는다.

### 함정 하나
`Reserve:126-134`가 **휴머노이드(스킨드) 세트를 거부한다.** 시체 프리팹이 산출물이라 조각 배열이 비어 있기 때문.
표적용 `SliceSet`은 **일반 메쉬 모드로 구워야 한다** — 안 그러면 경고 한 줄 찍고 무연출이 된다.

---

## 실행 결과 (2026-08-10)

**전 Step 구현 완료.** 검증: Unity 컴파일 **에러 0 · 경고 0**, EditMode 테스트 **120/120 green**.

### 계획에서 벗어난 것 3가지

1. **오탐 2건을 되돌렸다** — `PieceMeshPrefix`(1-2)와 테스트 5파일의 `using`(1-6).
   원인은 같다: 조사 스크립트가 참조 수를 셀 때 **문자열·주석을 제거**했는데, 그 안에서 쓰이거나(보간 문자열 `$"{PieceMeshPrefix}…"`)
   네임스페이스 없이 타입을 참조하던 경우를 놓쳤다. 컴파일이 즉시 잡아 되돌렸다.
2. **Step 4-4를 건너뛰지 않고 했다.** `PatternEffectDirector`뿐 아니라 **`EffectManager`도 같은 뷰 풀을 복제**하고 있어
   (조사 당시 B-1에 안 잡혔다) 둘을 `Effect/CanvasEffectPool.cs` 하나로 합쳤다. `PrefabPool`과 나눈 이유는 반납 훅(`OnDespawn`)과
   `SourcePrefab` 규약이 다르기 때문이다.
3. **Step 6-2가 굽기 툴의 씬 의존을 통째로 걷어냈다.** `ComputeFallDuration` 호출을 없애자
   `PatternChartWindow.referenceHandler`(씬 `PatternHandler` 참조)를 요구할 이유가 사라졌다 — 필드·경고·저장 잠금 4곳을 함께 제거했다.
   **이제 씬을 열지 않고도 채보를 굽고 저장할 수 있다.**

### 부수 성과
- `PatternHandler.GoodWindow` 제거 — `Deadline`이 페이로드로 나가면서 외부에서 식을 재조립할 이유가 사라졌다(5-4).
- `CorpseView`의 `Pieces`/`Bones`/`RootPieceIndex`, `SliceSet.RootPieceIndex` 등 조사에서 놓친 미사용 getter 추가 제거.
- `EnemyDirector` 1,910줄 → **1,581줄** (+ `.Recycle.cs` 123줄, `.Gizmos.cs` 154줄).

### 남은 것
- Step 8-1 · 8-2 (보류, 위 참조)
- Step 8-3 (의도적으로 안 함)
- **플레이 검증**: 곡 1회 완주로 이펙트·풀 재사용·히트스톱을 눈으로 확인하는 것은 아직 안 했다.
