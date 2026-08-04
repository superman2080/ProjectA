# Plan — EnemyFeint (표적이 된 순간부터 임팩트까지 적이 하는 동작)

근거: `docs/EnemyFeint/Research_EnemyFeint.md`

## 전제가 바뀌었다 — 적은 이제 마중 나오지 않는다

초안은 "적이 도착한 뒤부터"였다. 실측해 보니 그 창이 **너무 짧다**(아래 표). 원인은 적이 간격의 15%를 걸어오는 데
그 이동이 창에 비례해 커지기 때문이고, 그래서 **창이 길어져도 여유가 안 생긴다**(남는 시간이 언제나 `0.735 × W`로 고정).

**`EnemyDirector.playerShare`를 1.0으로 올려 적을 아예 세운다.** `BuildDuelPlan`의 적 이동량이
`(1 − playerShare) × (간격 − duelDistance)`이므로 share가 1이면 **정확히 0**이다 — 적 목적지 = 지금 서 있는 자리.

- 견제 창이 **W 전체**가 된다.
- 이동이 없으니 **미끄러짐도 0**이고, `wantsLocomotion`도 false(`minMoveDistance` 0.3)라 제자리 Run도 안 난다.
- **플레이어는 더 미끄러지지 않는다.** `TakeTargetForWindow`가 목표 거리를 `cruiseSpeed × W / playerShare`로 잡으므로
  share가 오르면 목표 거리가 오히려 **줄어든다**(5.29W → 4.5W). 플레이어 속도는 어느 쪽이든 `cruiseSpeed`로 같다.
- ⚠ **§11-2가 share를 1로 두지 않은 이유("적이 정지 표적으로 읽힌다")를 이 기능이 대체한다.**
  1m 걸어나오는 것보다 제자리에서 칼을 고쳐잡는 편이 교전으로 더 잘 읽힌다. 정지가 아니게 되므로 그 근거가 소멸한다.

### 실측 (`Dreamer_Lv10`, 87엔트리, 150 BPM, **86개 전부 `Attacker.Player`** = 전부 견제 대상)

`W` = 표적이 되는 순간(직전 패턴 완료) → 임팩트

| | min | p10 | p50 | avg | p90 | max |
|---|---|---|---|---|---|---|
| **W (share 1.0에서의 견제 창)** | **0.50** | 0.90 | **1.30** | 1.48 | 2.10 | 2.10 |
| 도착 후만 (share 0.85, 폐기안) | 0.37 | 0.66 | 0.96 | 1.09 | 1.54 | 1.54 |

배속 없이 클립이 통째로 들어가는 비율:

| 트림 길이 | **share 1.0 (채택)** | share 0.85 · 도착 후 (폐기) |
|---|---|---|
| 0.6s | 97% | 97% |
| **0.8s** | **97%** | 77% |
| 1.0s | 77% | 49% |
| 1.5s | 49% | 23% |
| 2.0s | 23% | 0% |

→ **권장 트림 0.8초.** 97%가 배속 없이 들어간다.

## 설계 요약

```
전제   : EnemyDirector.playerShare = 1.0  (적은 제자리에 선다)
대상   : 교전 상대 하나 (군중은 별도 플랜)
조건   : Pattern.Attacker == Player  &&  EnemyFeint.IsUsable  &&  창 >= minFeintWindow
시작   : 표적이 되는 순간(BindReservation). ResolveScheduleStart(impactTime, earliest: Time.time)
끝     : 임팩트 (클립에 임팩트를 안 찍으면 트림 끝이 임팩트로 폴백 = 클립 끝이 임팩트에 붙는다)
슬롯   : 기존 Attack 스테이트 재사용 (Attacker.Player에서는 비어 있다)
```

- **새 정렬 수학 없음.** `ClipAlignment`가 시작·배속 역산을 이미 한다.
- **새 애니메이터 스테이트 없음.** `Attacker.Player` 패턴에서 `Attack` 슬롯은 아무도 안 쓴다.
- **새 이벤트 없음.** `EnemyDirector.BindReservation`이 상대·임팩트·템플릿을 한자리에서 안다.
- 비어 있으면 완전 무연출 — 기존 채보는 하나도 안 바뀐다.

## 단계

- [x] **Step 0 — 적을 세운다** (`EnemyDirector`, 인스펙터 + 문서)
  - 씬의 `playerShare` 0.85 → **1.0**. 코드 수정 없음(`[Range(0.5f, 1f)]`이 이미 1을 허용한다).
  - `playerShare` 툴팁의 "1로 두면 적이 정지 표적으로 보인다" 문구를 **견제가 그 자리를 대신한다**로 고친다.
  - `CLAUDE.md` §11-2의 같은 문장도 고친다. **안 고치면 다음에 누가 근거를 보고 0.85로 되돌린다.**
  - ⚠ 부수 영향: `ResolveRetreatDistance`의 `travel = failRetreatDistance × playerShare / cruiseSpeed`가 커져
    `needed`가 0.05초쯤 늘고 **패링 비율이 살짝 오른다**. `retreatWindowMargin`이 흡수하는 범위이므로 값은 건드리지 않는다.
  - ⚠ `minTargetDistance`(2m) 확인: 최소 창 0.5초에서 목표 거리 `4.5×0.5 + 1 = 3.25m` > 2m — 안 걸린다.

- [x] **Step 1 — 패턴 슬롯 추가** (`Pattern.cs`)
  - `[SerializeField] private ClipAlignment enemyFeint = new ClipAlignment();` + `public ClipAlignment EnemyFeint => enemyFeint;`
  - 기존 슬롯 4개 옆에 둔다. 툴팁에 **"임팩트 프레임을 찍지 마라 — 클립 끝이 임팩트에 붙는 것이 기본"**과
    **"트림 0.8초 이하 권장(실측 97%가 배속 없이 들어간다)"**을 명시한다.
  - `OnValidate`: 임팩트 검증(`ValidateImpactTime`)만 걸고 **미배선 경고는 넣지 않는다**(선택 슬롯).
  - ⚠ `Attacker.Enemy` 패턴에 이 슬롯이 채워져 있으면 **경고**한다 — 재생되지 않으므로 조용히 무시되면 저작자가 헤맨다.

- [x] **Step 2 — 뷰의 배정 경로** (`EnemyView.cs`)
  - `public void AssignFeint(ClipAlignment feint, float impactTime, Vector3 duelPosition, Vector3 faceTarget, float maxSpeed)`
  - `AssignAttack`과 거의 같다. 다른 점은 **`earliest`가 `Time.time`이라는 것뿐**이다(도착을 기다리지 않는다 —
    share 1.0에서 이동이 0이므로 기다릴 도착이 없다).
  - 순서: `Current = Phase.Engage` → `ApproachDuel(duelPosition, faceTarget, impactTime)`(이동량 0이지만
    **호출은 유지한다** — 바라보기(`ScheduleFace`)와 후퇴 인수인계가 여기 얹혀 있다) → `pending*` 채우고 `TryStartAttack`에 맡긴다.
  - **기존 `pendingAttack` 필드를 그대로 쓴다.** `Attacker.Player`에서는 진짜 공격 예약이 없어 충돌하지 않고,
    `Resolve`(`:401`)·`MarkDying`(`:462`)이 이미 `hasPendingAttack`을 내리므로 **정리 경로가 공짜로 따라온다.**
  - 클립이 없으면(`!IsUsable`) 지금과 같이 `ApproachDuel`만 하고 끝낸다.

- [x] **Step 3 — 디렉터 배선** (`EnemyDirector.BindReservation`)
  ```
  var feint = AttackerOf(r.template) == Attacker.Player ? r.template?.EnemyFeint : null;
  bool roomy = (r.impactTime - Time.time) >= minFeintWindow;
  if (feint != null && feint.IsUsable && roomy)
      opponent.AssignFeint(feint, r.impactTime, plan.EnemyPosition, plan.PlayerPosition, maxAttackSpeed);
  else
      opponent.AssignAttack(r.attack, r.impactTime, plan.EnemyPosition, plan.PlayerPosition, maxAttackSpeed);
  ```
  - `Reservation.attack`은 `Attacker.Enemy`일 때만 채워지므로(`:612`) **두 경로는 배타적**이라 겹칠 수 없다.
  - **`minFeintWindow`**(신규 인스펙터 값, 기본 **0.35초**): 이보다 짧으면 아예 재생하지 않는다.
    실측 최소 창이 0.50초라 이 곡에서는 안 걸리지만, 더 빽빽한 채보에서 **깜빡임으로만 보이는 재생**을 막는 가드다.
    배속 상한에 걸려 임팩트를 넘긴 클립은 리액션 크로스페이드가 끊는데, 0.2초짜리는 그마저 안 보인다.

- [x] **Step 4 — 굽기 툴에서 프리뷰** (`ChartGen/Editor/PatternChartWindow.cs`)
  - 자리: `전투 —` 폴드아웃 안, 패턴 소유 값(회색 읽기 전용) 바로 아래. 견제도 **패턴 소유 값**이라 같은 구역이 맞다.
  - 표시: 클립 이름 + 트림 길이(초) + **이 엔트리의 실제 창**(`impactTime − 직전 엔트리 마지막 노드`)을 같이 적는다.
    저작자가 "이 클립이 이 엔트리에 들어가는가"를 그 자리에서 판단할 수 있어야 한다. 창보다 길면 노랑 경고.
  - 프리뷰는 **Unity 내장 클립 프리뷰를 그대로 쓴다** — `Editor.CreateEditor(clip)` + `OnInteractivePreviewGUI(rect, style)`.
    새 렌더 리그를 만들지 않는다.
    - ⚠ 에디터 인스턴스를 **클립별로 캐시**하고 클립이 바뀌거나 창이 닫힐 때 `DestroyImmediate`한다. 안 그러면 샌다.
    - ⚠ 애니메이션이 돌려면 `Repaint()`가 계속 불려야 한다(`EditorApplication.update` 구독).
    - ⚠ **내장 프리뷰는 트림을 모른다** — 클립 전체를 재생한다. 그래서 트림 구간을 숫자로 같이 적는다.
      "대충 어떤 동작인지" 확인용이고, 정밀 저작은 아래 버튼으로 넘긴다.
  - **`Pattern Action Editor로 열기` 버튼**: `AnimationClipTrimmerWindow`를 그 패턴으로 연다.
    거기엔 이미 `PreviewRenderUtility` 기반 짝 재생·임팩트 기준 타임라인·궤도 카메라가 있다 — **프리뷰 리그를 복제하지 않는다.**
    - 진입점 한 줄 추가: `public static void Open(Pattern pattern)`.
  - 견제 클립이 비어 있으면 "이 패턴은 견제 없음"만 적는다(에러 아님 — 선택 슬롯이다).

- [x] **Step 5 — 짝 에디터에 견제 슬롯 노출** (`Character/Editor/AnimationClipTrimmerWindow.cs`)
  - 지금은 **역할에 맞는 두 배우**만 연다(`Attacker.Player` → 플레이어 `playerAttack` / 적 `playerParry`).
    견제는 결과(패링·사망)가 아니라 **임팩트 이전** 구간의 동작이라 성격이 다르다.
  - 최소 변경: 적 배우의 슬롯 **선택기**(패링 / 견제)를 둔다. 시간축·정렬·저장 경로는 그대로 쓴다.
  - ⚠ 견제는 임팩트를 안 찍는 것이 기본이라 그 창의 `t = 0`(임팩트)에 **클립 끝**이 온다.
    선택기가 견제일 때 한 줄로 안내한다(안 그러면 "임팩트 마크가 없다"고 오해한다).

- [ ] **Step 6 — 저작 (Unity, 코드 아님)**
  - `Tools/Animation Clip Trimmer`로 견제 클립의 Start/End를 자른다. **Impact는 찍지 않는다**(끝이 임팩트가 된다).
  - **트림 0.8초 이하**를 기준으로 삼는다. 원본이 2초짜리 공격 모션이면 **가장 읽히는 0.8초만** 잘라 쓴다.
  - `Attacker.Player` 패턴 몇 개에 지정해 본다. 전부 채울 필요 없다 — 빈 패턴은 예전 그대로 동작한다.

## 검증

- [ ] **Step 7 — 확인**
  - **적이 결투 위치로 걸어오지 않는가**(share 1.0). 제자리 Run도 없어야 한다.
  - 견제가 **표적이 되는 순간** 시작해 **임팩트에 끝나는가**.
  - **짧은 창(0.5초)**: 배속으로 압축되고, 넘치면 리액션이 끊는가. `minFeintWindow` 아래에서는 아예 안 나오는가.
  - **긴 창(2.1초)**: 슬로모션이 아니라 늦게 시작해 임팩트에 붙는가.
  - **실패 직후 패턴**(후퇴 → 재접근): 후퇴가 견제에 덮이지 않고 순서대로 나오는가(`ScheduleMoveAfter` 인수인계).
  - **패링 비율**이 눈에 띄게 늘지 않았는가(Step 0의 부수 영향). 늘었으면 `retreatWindowMargin`을 줄인다.
  - `Attacker.Enemy` 패턴 / 클립 미지정 패턴: 예전과 완전히 동일한가.
  - **굽기 툴**: 엔트리를 여러 개 펼쳐도 프리뷰가 새지 않는가(에디터 인스턴스 캐시·해제). 패턴을 갈아끼우면 따라 바뀌는가.

## 하지 않는 것

- 무대 위 군중(링 대기 적)의 동작 — 별도 플랜.
- 견제 전용 애니메이터 스테이트 — `Attack` 슬롯 재사용으로 충분하다.
- 견제 클립의 임팩트 프레임 지원 — 찍으면 `ClipAlignment`가 그 프레임을 임팩트에 맞추므로 **이미 되지만**,
  기본 사용법은 "찍지 않는다"로 안내한다(닿지 않는 동작이므로).
- `playerShare`를 다시 낮추는 경로 — 적의 "마중"이 필요해지면 그건 견제 클립으로 표현한다(제자리 반보 등).
