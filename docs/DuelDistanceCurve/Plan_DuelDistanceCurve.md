# Plan — 결투 거리 커브 (DuelDistanceCurve)

근거: `docs/DuelDistanceCurve/Research_DuelDistanceCurve.md`

## 설계 요약

- `Pattern`에 `AnimationCurve duelDistanceCurve` 하나. **키 시간 = 임팩트 기준 상대초**(0 = 임팩트), **값 = 절대 간격(m)**.
- 음수 값 = 플레이어가 적을 지나쳐 뒤로. 회전은 안 건드린다.
- 커브가 비면 기존 `duelDistanceOffset` 경로 그대로(회귀 0).
- **적은 고정, 플레이어만 움직인다** → 간격이 곧 커브값(파생이 아니라 정의).
- 구간 밖 홀드는 `AnimationCurve`의 기본 Clamp가 공짜로 준다.
- 저작·프리뷰는 `Tools/Animation Clip Trimmer`(시간축이 이미 임팩트 기준).

---

## - [x] Step 1 — `Pattern`에 커브와 샘플 함수

`Assets/02. Scripts/Pattern/Pattern.cs`

- `duelDistanceOffset` 바로 아래에 필드 추가:
  ```csharp
  [Tooltip("패턴 진행 중 결투 간격(m). 키 시간 = 임팩트 기준 상대초(0 = 임팩트), 값 = 절대 간격.\n" +
           "음수면 플레이어가 적을 지나쳐 뒤로 간다. 비우면 duelDistanceOffset 경로 그대로.\n" +
           "저작은 Tools/Animation Clip Trimmer.")]
  [SerializeField] private AnimationCurve duelDistanceCurve = new AnimationCurve();
  ```
- 공개 프로퍼티 `DuelDistanceCurve`(툴 프리뷰용)와 **단일 출처 샘플 함수**:
  ```csharp
  /// <summary>이 패턴의 t(임팩트 기준 상대초)에서 둘이 유지할 간격(m).
  /// 런타임과 툴 프리뷰가 같은 함수를 부른다 — 두 그림이 어긋날 코드가 없다.</summary>
  public float DuelGapAt(float relTime, float baseDistance) =>
      duelDistanceCurve != null && duelDistanceCurve.length > 0
          ? duelDistanceCurve.Evaluate(relTime)
          : Mathf.Max(baseDistance + duelDistanceOffset, 0.1f);
  ```
  - 커브 경로에는 `Mathf.Max` 클램프를 걸지 않는다 — 음수가 이 기능의 목적이다.
  - `baseDistance`를 인자로 받는 이유: 씬 앵커 값은 런타임이, 툴은 자기 값을 들고 있어 소유자가 다르다.

## - [x] Step 2 — `EnemyDirector`: 시간 인자와 계획 확장

`Assets/02. Scripts/Enemy/EnemyDirector.cs`

- `DuelDistanceOf(Pattern)` → `DuelDistanceOf(Pattern template, float relTime)`.
  앵커에서 `baseDistance`를 구하는 부분은 그대로, 마지막 줄만 `template.DuelGapAt(relTime, baseDistance)`.
  `template == null`이면 지금처럼 `baseDistance` 폴백.
- 앵커 기준 거리 산출을 `private float DuelBaseDistance()`로 분리(툴이 같은 식을 쓰도록 정적 규칙을 한 곳에).
- `BindReservation`(`:1319`): `float duelDistance = DuelDistanceOf(r.template, arriveTime - r.impactTime);`
  — **도착 시각의 간격**. 표적 선택(`TakeTargetForWindow`)과 `BuildDuelPlan`이 그 값을 쓴다(지금과 동일).
- `DuelPlan`에 필드 3개 추가: `Vector3 Axis`(플레이어→적 단위벡터), `float ImpactTime`, `Pattern Template`.
  `BuildDuelPlan`이 이미 `dir`·`template`을 들고 있으므로 생성자 인자만 늘린다. `impactTime`은 `BindReservation`이 넘긴다.
  - 상대가 없는 디버그 경로에서도 `Axis = Vector3.forward`로 채운다(널 분기 없음).

## - [x] Step 3 — `PlayerCombatMover`: 도착 뒤 커브 구동

`Assets/02. Scripts/Character/PlayerCombatMover.cs`

- `HandleDuelScheduled`에서 계획을 필드에 보관(`curveEnemyPos`, `curveAxis`, `curveImpactTime`, `curveTemplate`, `curveBase`).
  `curveBase`는 계획의 `PlayerPosition`↔`EnemyPosition` 거리(런타임 기준 거리와 같은 값이라 새 참조가 안 생긴다).
  커브가 비어 있으면 구동을 아예 안 켠다(`curveTemplate = null`).
- `Update` 우선순위: `rolling` → **커브 구동** → `turning`/`moving`.
  커브 구동 조건: `!moving && curveTemplate != null`.
  ```csharp
  float rel = actionPlayer.DuelCurveTime;                  // NaN이면 재생 중인 액션이 없다
  if (!float.IsNaN(rel))
  {
      float gap = curveTemplate.DuelGapAt(rel, curveBase);
      Vector3 p = curveEnemyPos - curveAxis * gap;
      p.y = transform.position.y;
      transform.position = p;
  }
  ```
- 종료: 마지막 키 시각을 지나면 값이 상수라 위치도 상수다 → 계속 대입해도 무해하지만,
  다음 `HandleDuelScheduled`가 상태를 덮고 `RollArc`가 `curveTemplate = null`로 끈다(구르기가 위치의 유일한 주인).
- `turning`은 그대로 둔다(회전 미개입 결정).

### Step 3-1 — 시각의 출처는 `CharacterActionPlayer` 하나다

`Time.time − impactTime`을 쓰면 안 된다. 결정 두 개가 같은 곳을 가리킨다:

- **히트스톱**: 정지는 `AttackSpeed = 0`으로만 걸리고 `Time.time`은 계속 흐른다(§7-3).
  월드 시각으로 커브를 굴리면 **캐릭터는 얼었는데 몸만 미끄러진다** = "멈췄다"가 아니라 "렉"으로 읽힌다.
- **배속 압축**: 다음 패턴이 촉박하면 `AttackSpeed`가 최대 `maxAttackSpeed`(2.5)까지 걸린다.
  커브도 같은 배속으로 압축돼야 칼 리치와 간격의 대응이 유지된다(사용자 결정).

둘 다 **재생 헤드를 따라가면 자동으로 성립한다.** 정지 누적과 재생 배속을 동시에 아는 클래스는
`CharacterActionPlayer` 하나뿐이므로 거기서 계산해 내보낸다:

```csharp
/// <summary>지금 재생 중인 액션의 임팩트 기준 시각(초). 저작 배속 기준이라 툴의 t와 같은 단위다.
/// 히트스톱으로 헤드가 얼면 이 값도 얼고, AttackSpeed가 걸리면 같이 압축된다.
/// 재생 중인 액션이 없으면 NaN.</summary>
public float DuelCurveTime { get; }
```

- 산식: `(재생된 클립 시간 − ClipAlignment.ImpactTime) / 저작 speed`.
  재생된 클립 시간은 이미 들고 있는 예약 상태(`pendingScheduleStart`·`playSpeed`·정지 누적)에서 나온다 —
  `Animator` 스테이트 시간을 읽지 않는다(크로스페이드 중에 값이 튄다).
- 액션이 없는 구간(NaN)에서는 커브 구동이 아무것도 안 한다 → 마지막 위치 유지 = 홀드와 같은 결과.
- ⚠ `Attacker.Enemy` 패턴에서는 플레이어 클립이 `playerParry`다. 같은 속성이 그대로 쓰인다(슬롯만 다름).

## - [x] Step 4 — 뒤로 빠질 때 백스텝 클립

`Assets/02. Scripts/Character/CharacterActionPlayer.cs`

**현재 상태**: 뒤로 빠지는 경로에 분기가 없다. `BuildDuelPlan`은 부호를 안 보고 목표점을 내고
(`playerTarget = enemyDest − dir × distance`), `PlayerCombatMover`는 적을 마주본 채 그 점으로 Lerp하며,
`CharacterActionPlayer.HandleDuelScheduled`(`:455`)는 `distance`를 `magnitude`(부호 없음)로만 봐서
**전진 클립으로 뒷걸음질**한다. 거리 커브가 들어오면 뒤로 빠지는 구간이 의도적으로 늘어나므로 여기서 같이 고친다.

- 필드 추가:
  ```csharp
  [Tooltip("뒤로 빠질 때 쓸 대시 클립. 비우면 전진 클립 그대로(예전 동작).")]
  [SerializeField] private AnimationClip quickshiftBackClip;   // Assets/05. Animations/Clip/Move/Quickshift_B.anim
  ```
- **새 애니메이터 스테이트를 만들지 않는다.** 이미 있는 `AnimatorOverrideController`(`:297`)로
  `Quickshift` 스테이트의 클립만 갈아 끼운다 — `overrideController[quickshiftClip] = backward ? quickshiftBackClip : quickshiftClip;`
  (키는 언제나 원본 클립 `quickshiftClip`. 양쪽 분기에서 **매번** 대입해 이전 대여의 상태가 남지 않게 한다.)
  ⚠ 씬의 `Quickshift` 스테이트에 물린 클립이 곧 `quickshiftClip`이어야 키가 맞는다(현재 배선 = `Quickshift_F`).
- **방향 판정**: `HandleDuelScheduled` 안에서
  ```csharp
  Vector3 move = plan.PlayerPosition - transform.position;
  Vector3 facing = plan.EnemyPosition - plan.PlayerPosition;   // 도착 뒤 바라볼 방향
  bool backward = Vector3.Dot(Vector3.ProjectOnPlane(move, Vector3.up),
                              Vector3.ProjectOnPlane(facing, Vector3.up)) < 0f;
  ```
  회전은 `PlayerCombatMover`가 이미 적 쪽으로 걸어 두므로 이 식이 화면의 사실과 같다.
- **뒤로 갈 때는 Sprint 분기를 타지 않는다** — 전진 달리기 루프로 뒤로 미끄러지면 그림이 완전히 깨진다.
  `if (window > dashLength && travelSpeed >= minSprintTravelSpeed && !backward)`. 백스텝은 Quickshift 경로로만.
  배속 역산(`dashLength / window`)은 그대로 — `Quickshift_B` 길이가 `quickshiftClip`과 다르면
  `backward`일 때는 백스텝 클립 길이를 쓴다.
- `LogConverge`에 방향을 함께 찍는다(`Quickshift_B x1.20`).
- **커브 구동 구간(Step 3)에는 로코모션이 없다** — 그 구간은 공격 클립이 상·하체를 다 들고 있어 base가 안 보인다.
  즉 백스텝은 **패턴 사이 수렴**에만 적용된다. 커브로 지나친 뒤 다음 패턴에서 되돌아올 때가 주 사용처다.

## - [x] Step 5 — `Animation Clip Trimmer`: 저작 + 프리뷰

`Assets/02. Scripts/Character/Editor/AnimationClipTrimmerWindow.cs`

- **기준 거리 자동 채움**: 씬에 `EnemyDirector`가 있으면 그 `duelAnchor`에서 런타임과 같은 식으로 `duelBaseDistance`를 읽는다
  (`Object.FindObjectOfType` + `SerializedObject`로 `duelAnchor` 접근, 못 찾으면 지금처럼 수동 입력 유지).
  "씬에서 읽음 / 수동" 상태를 라벨에 적는다.
- **커브 필드**: 거리 행 아래 `EditorGUILayout.CurveField("결투 거리 커브", curve)` 한 줄.
  지점 찍기·끌기·탄젠트는 Unity 커브 에디터가 전부 처리한다(새 UI 코드 없음).
  - 비어 있으면 "커브 없음 — 상수 {DuelDistance:0.00}m" 안내, `+ 커브 만들기` 버튼이 현재 상수값 키 2개
    (`t = 첫 클립 시작`, `t = 0`)로 초기화.
- **프리뷰가 실제 거리를 쓴다**: `DuelDistance` 프로퍼티를 `GapAt(t)`로 대체.
  `curve.length > 0 ? curve.Evaluate(t) : duelBaseDistance + duelDistanceOffset`
  — Step 1의 `DuelGapAt`과 같은 식이며, 대상 패턴이 있으면 `targetPattern.DuelGapAt(t, duelBaseDistance)`를
  직접 불러 **에셋 저장 전 창의 커브**를 쓰도록 창 로컬 커브를 우선한다.
  - **런타임과 같이 플레이어를 움직인다**(사용자 결정) — `PlaceActors`(`:368`)에서
    `enemy.placement = Vector3.forward * GapAt(0f)`(임팩트 시점 자리에 **고정**),
    `player.placement = Vector3.forward * (GapAt(0f) − GapAt(t))`.
    `t = 0`에서 플레이어가 원점이라 기존 구도가 그대로고, 스크럽하면 플레이어가 축을 따라 미끄러진다.
    간격이 음수가 되는 구간에서는 플레이어가 적을 지나쳐 뒤로 나간다 — 화면이 런타임과 같아지고,
    칼날 경로·바운드도 실제 궤적이 된다.
  - `player.facing`은 계속 `Quaternion.identity`(회전 미개입 결정과 같음).
  - `CombinedBounds`(`:483`)는 두 배우 인스턴스 렌더러를 이미 합치므로 폴백 중심만
    `Vector3.forward * (GapAt(0f) * 0.5f)`로 바꾼다(카메라가 스크럽마다 튀지 않게 `t`에 의존하지 않는 값).
  - 현재 `t`의 간격을 숫자로도 표시(`간격 t=+0.00s : 1.20m`).
- **저장/동기**: `ApplyToPattern`에서 `duelDistanceCurve` 프로퍼티에 `animationCurveValue`로 쓰고,
  `DrawSyncState`의 비교에 커브 키 개수·시각·값 비교를 추가(`LoadFromPattern`에서도 읽어 온다).

## - [x] Step 6 — 검증

`Assets/02. Scripts/Enemy/Tests/`(기존 asmdef)에 `DuelGapTests` 하나:

- 빈 커브 → `base + offset`, 그리고 0.1m 하한이 걸린다.
- 키 2개(−0.5 → 1.2m, 0 → −0.8m) → 중간값 보간, `t = −5`에서 1.2, `t = +5`에서 −0.8 (앞뒤 홀드).
- 커브가 음수를 그대로 돌려준다(클램프 없음).

`Pattern`이 `EnemySpace` 테스트 asmdef에서 참조 가능한지 확인하고, 아니면 `Pattern/Tests` asmdef를 새로 만들지 않고
기존 `Slice/Tests`·`ChartGen/Tests` 중 `PatternSpace`를 이미 참조하는 곳에 붙인다(새 asmdef 추가 금지).

## - [x] Step 7 — 문서

- `docs/!Guides/Guide_PatternActionEditor.md`(있으면)에 커브 사용 절 추가, 없으면 이 Plan 옆에 짧은 사용 절.
- `CLAUDE.md` §11-2에 한 문단: "결투 간격은 상수가 아니라 패턴이 든 시간 함수다.
  적은 고정이고 플레이어가 커브를 따른다. 음수 = 지나치기. 커브가 비면 예전 상수 경로.
  뒤로 빠지는 수렴은 `Quickshift_B`로 클립만 갈아 끼운다(새 스테이트 없음)."

---

---

## 구현 결과 (2026-08-13)

Plan과 달라진 곳 셋. 전부 이유가 있다:

1. **간격 계산이 `Pattern`이 아니라 `Pattern/Core/DuelGap`에 산다.** `Pattern`은 predefined assembly
   (`Assembly-CSharp`)라 어느 테스트 asmdef에서도 참조할 수 없다 — Step 6이 성립하지 않는다.
   `DuetTimeline`이 이미 쓰는 규율("산술은 asmdef 안에, 에셋은 위임")을 그대로 따랐다.
   `Pattern.DuelGapAt`은 `DuelGap.At`에 위임하는 한 줄이다.
2. **`MeshSliceBakerWindow.DuelDistance`도 `DuelGapAt(0f, …)`로 바꿨다**(한 줄). 굽는 포즈가 임팩트 순간이라
   `t = 0`이 정확히 그 시점이고, 안 바꾸면 커브를 쓰는 패턴에서 **절단 평면 유도가 옛 상수로 계산된다**.
3. **`PlayerCombatMover.actionPlayer` 필드를 추가했다.** 커브 시각의 출처가 `CharacterActionPlayer`이므로
   참조가 필요하다. 비면 커브 구동이 꺼진다(기존 "배선 누락 = 조용히 비활성" 규율).

검증: `Pattern.Tests` EditMode **48/48 통과**(`DuelGapTests` 6건 포함), 컴파일 에러·경고 0.

### 남은 씬 배선 2개 (안 하면 조용히 예전 동작)

- `CharacterActionPlayer.quickshiftBackClip` ← `Assets/05. Animations/Clip/Move/Quickshift_B.anim`
- `PlayerCombatMover.actionPlayer` ← 같은 오브젝트의 `CharacterActionPlayer`

⚠ 백스텝 교체의 키는 `quickshiftClip`이다 — 애니메이터 `Quickshift` 스테이트에 **실제로 물린 그 클립**이어야
교체가 먹는다(키가 다르면 조용히 무시된다).

## - [x] Step 8 — 축 뒤집힘 수정 (2026-08-13, 후속)

**증상**: 물러나야 하는 구간에서 플레이어가 갑자기 적을 관통해 뒤로 건너갔다. 반대로 커브가 음수인데도
관통하지 않는 패턴이 섞였다.

**원인**: 축(`DuelPlan.Axis`)을 **그 순간 플레이어 위치**에서 파생시켰다
(`BuildDuelPlan`의 `toEnemy = opponent.Destination − player`). 커브의 목적이 관통이라 플레이어는
정상적으로 적 뒤에 서 있고(마지막 키가 음수면 클립이 끝나도 그 자리에 홀드된다 — 앞으로 되돌리는 경로가 없다),
그 자리에서 다시 재면 축이 **180° 뒤집힌다**. 그러면 다음 패턴부터 커브 전체가 거울로 돌아
간격 증가 = 적 뒤로 건너가기, 음수 = 앞으로 복귀가 된다. 매 패턴 부호가 번갈아 뒤집혔다.

`PlayerPosition`을 앵커 부모로 고친 것(§11-2)은 같은 부호 뒤집기의 **다른 트리거** 하나만 막았다 —
커브가 실제로 만드는 관통은 그대로 남아 있었다.

**수정**: 축을 순간 위치가 아니라 **교전의 성질**로 둔다.
- `DuelGap.ResolveAxis(toEnemy, previousAxis)` (순수 함수, 테스트됨) — 새로 잰 방향이 직전 축과
  부호가 반대면 직전 축을 유지한다. 부호가 반대일 때만 개입하므로 **상대가 바뀌어 축을 새로 잡는 경로는 안 막는다**.
- `EnemyDirector`가 `duelAxis`/`duelAxisOwner`를 든다 — 축은 상대와 수명을 같이하고(사슬 내내 유지),
  상대가 바뀌면 그 자리에서 새로 잡힌다. 디버그 경로(`opponent == null`)는 주인을 비운다.

검증: `DuelGapTests` **9/9 통과**(축 3건 추가), 컴파일 에러 0.

⚠ 남는 그림 하나: 관통 상태에서 다음 패턴의 목표 자리는 적 **앞**이라 접근 이동이 적을 뚫고 돌아온다.
저작 규약으로 막는 게 싸다 — **커브 마지막 키를 다시 양수로 돌려 놓는다**(잔심 후 복귀). 코드 0줄.

## - [x] Step 9 — 임팩트 이후 구간 살리기 (2026-08-13, 후속)

**증상**: 커브 값이 음수인데 관통하지 않는다.

**원인**: **다음 패턴의 결투 계획이 이번 임팩트보다 먼저 온다.** 완료(= 마지막 노드 입력)에서
`ResolveReservation → BindNextReservation → OnDuelScheduled`가 나가는데(`EnemyDirector.cs:1460, 1522`),
임팩트는 그보다 `goodWindow(0.1) + impactOffset`만큼 뒤다. `HandleDuelScheduled`가 그 자리에서
`curveTemplate`을 갈아끼우고 `moving = true`로 만들어 위치를 접근 lerp에 넘기므로,
**이번 커브의 유효 구동 창이 `t ≈ −0.1`에서 끝난다.** `t > 0`(관통) 키는 한 번도 재생되지 않는 죽은 데이터였다.

**수정**: 인수인계를 **계획 도착이 아니라 커브 끝**에 묶는다.
- `DuelGap.EndTime` / `Pattern.DuelCurveEndTime` — 마지막 키 시각이 "이 패턴이 위치를 소유하는 끝"이다.
- `PlayerCombatMover.pendingPlan` — 커브가 아직 자기 클립을 타고 마지막 키 이전이면(`IsCurveDriving`)
  새 계획을 보류만 하고, 끝나는 즉시 `ApplyPlan`으로 푼다. 이동·회전도 함께 미뤄져
  **관통 중에는 다음 상대 쪽으로 돌지 않는다**(마주보지 않는 그림이 유지된다).
- **도착 마감은 절대 안 넘긴다** — `Time.time >= plan.PlayerArriveTime`이면 보류하지 않는다.
  위치 소유권보다 칼이 맞는 시각이 우선이다.
- 구르기 진입은 커브 구동을 끄므로(`RollArc`) 보류가 그 프레임에 자동으로 풀린다.

검증: `DuelGapTests` **10/10 통과**, 컴파일 에러 0.

⚠ **꼬리 길이는 저작 예산이다.** 마지막 키를 임팩트 +0.35초에 찍으면 다음 접근 이동이 그만큼 늦게 시작한다
(엔트리 간 최소 간격 0.4초). 길게 찍을수록 다음 대시가 압축된다.

⚠ 값 단위는 **미터**다. `t < 0` 구간을 4~5로 찍으면 스윙 내내 4~5m 떨어져 서 있어 칼이 안 닿는다
(`Attacker.Player`는 `arriveTime = impactTime`이라 배치 거리도 `curve.Evaluate(0)`이다).
대략 `t=−0.5 → 1.2` / `t=0 → 0.3~0` / `t=+0.35 → −1.5` 규모가 화면에 맞는다.

## - [x] Step 10 — 커브 시작 위치로 미리 가 있기 (2026-08-13, 후속)

**증상**: 접근은 예전 상수 간격까지만 하고, 패턴이 시작하는 순간 커브 첫 키 값으로 **순간이동**한다.

**원인**: 배치 거리를 **도착 시각**(`arriveTime − impactTime`, `Attacker.Player`면 `t = 0`)에서 뽑았는데,
커브 구동은 **클립 시작**(`t = 첫 키 ≈ −0.5`)에서 열린다. 두 시각이 다르니 두 값도 다르고,
그 차이가 커브가 열리는 프레임에 한 번에 적용됐다. 게다가 `PlayerArriveTime`이 임팩트라
그때까지는 접근 lerp가 위치를 쥐고 있어 스냅이 임팩트로 밀리기도 했다.

**수정**: **커브가 시작 시각과 시작 위치를 같이 소유한다.**
- `DuelGap.StartTime` / `Pattern.DuelCurveStartTime` — 첫 키 시각.
- `BuildDuelPlan`: 커브가 있으면 배치 거리를 `DuelGapAt(첫 키 시각)`으로 잡고,
  `playerArriveTime`을 `impactTime + 첫 키 시각`으로 **당긴다**(앞당기기만 하므로 안전).
  커브가 없으면 예전 식 그대로 — 회귀 없음.

결과: 접근이 끝난 자리 = 커브 첫 값 = 커브가 열릴 때의 값. 이음매가 정의상 연속이라 스냅이 사라진다.

검증: `DuelGapTests` **11/11 통과**, 컴파일 에러 0.

⚠ **첫 키는 클립 시작(트림 시작)에 찍는다.** 그보다 늦게 찍으면 그 사이 구간이 `Evaluate` 클램프로
첫 값 홀드가 되어 커브 시작 전 이동이 그만큼 일찍 끝난 채 서 있는다(스냅은 없다).
그보다 이르게 찍으면 접근 마감이 그만큼 앞당겨져 대시가 빨라진다.

## - [x] Step 11 — 기습 회피가 커브를 죽이던 것 (2026-08-13, 후속)

**증상**: 기습 회피(Space)를 한 패턴만 관통이 안 난다.

**원인**: `PlayerCombatMover.RollArc`가 `curveTemplate = null`로 커브를 **버렸다**.
기습 회피는 §11-8대로 **플레이어 애니메이션 공백**, 즉 이번 패턴의 계획은 이미 적용됐고
스윙 클립은 아직 시작 전인 구간에서 일어난다. 그 자리에서 버리면 **그 패턴의 커브가 한 번도 돌지 않는다** —
클립이 시작해도 위치를 아무도 안 건드리니 예전 상수 동작이 된다.
(진단 로그로 확인한 잘림 — `t 0.224에서 끊겨 gap 최소 0.00m` — 이 경로였다.)

**수정**: **버리지 않고 재운다**(`curveSuspended`).
- `RollArc`는 구동만 재우고 커브·적·임팩트는 그대로 둔다.
- 구르기가 끝나면 `ResumeCurveAfterRoll` — 원호를 돈 만큼 어긋난 **축만 다시 잡고**(`DuelGap.ResolveAxis`),
  기습자를 보던 시선을 상대 쪽으로 되돌리고, **커브 시작 위치로 되돌리는 짧은 이동**을 건다
  (마감 = `임팩트 + 첫 키 시각` — Step 10과 같은 이음매 규율. 안 하면 클립 시작 프레임에 구르기 변위만큼 순간이동).
- 새 계획이 오면(`ApplyPlan`) 재워 둔 것을 되살리지 않는다 — 커브가 통째로 갈리므로.

검증: `Pattern.Tests` **53/53 통과**, 컴파일 에러 0.

### 진단 로그

`PlayerCombatMover.logDuelCurve`(기본 꺼짐) — 한 구동이 끝날 때 도달한 `t`·간격 극값과
차단 사유(gate/NaN/moving)를 한 줄로 찍는다. 커브가 "어디까지 그려졌나"는 프레임 로그로는 안 보인다.

## 범위 밖(의도적으로 안 함)

- `Pattern Effect Tool`의 `duelDistance = 1f` 하드코딩 — 같은 헬퍼로 나중에 고치면 된다.
- 커브 시간축의 창 길이 정규화 — 구동 구간이 클립 트림 길이라 절대초로 충분하다(Research §6).
- 지나친 뒤 회전/복귀, 타임라인의 간격 그래프 스트립, 적 쪽 이동 분담(`playerShare` 개입).
- `Attacker.Enemy`에서의 칼 정렬 보정 — 허용하되 저작자 책임(툴 프리뷰가 어긋남을 보여 준다).
