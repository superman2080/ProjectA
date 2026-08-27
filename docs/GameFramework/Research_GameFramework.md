# Research — 게임 프레임 개편 (HFSM · UI · SequenceSystem)

작성일: 2026-08-27 / 대상: 미커밋 신규 코드 3덩어리

- `Assets/02. Scripts/StateMachine/` — `IState` · `StateBase<T>` · `StateMachine<T>` · `CompositeStateBase<T>`
- `Assets/02. Scripts/UI/` — `UIManager` · `SubUIManager` · `DialogUI`
- `Assets/02. Scripts/SequenceSystem/` — `SequenceController` · `SequenceStateBase` · `DialogState`

## 0. 한 줄 요약

**셋 다 아직 아무도 안 쓴다.** 전체 코드베이스에서 이 타입들을 참조하는 곳이 0곳이고(`PlayerModeDirector.cs:28`의 주석 언급이 유일), 세 스크립트의 GUID가 `01. Scenes`·`03. Prefabs` 어디에도 안 박혀 있다. **즉 지금은 회귀 위험이 0이고, 설계를 바꿀 자유도가 최대인 시점이다.**

---

## 1. 기존 아키텍처와의 관계 (가장 중요)

CLAUDE.md가 기술하는 현재 게임은 **상태 기계가 아니라 "이벤트 + 예약" 모델**이다:

- `PatternHandler`가 이벤트를 발행하고, 연출 디렉터들(`EffectManager`·`CameraDirector`·`HitStopDirector`·`ScoreDirector`·`PatternEffectDirector`·`FinaleSilhouetteDirector`)이 **구독만 하는 순수 소비자**다.
- 상태를 든 유일한 클래스들은 각자 **`enum` + 가드**로 굴린다 — `EnemyView.Phase`(6단계), `PlayerModeDirector.PlayerMode`(4모드), `CharacterActionPlayer`의 복귀 경로 3갈래.
- 시각의 진실의 원천이 하나다(`Deadline + Pattern.ImpactOffset` = `info.ImpactTime()`).

**그래서 새 HFSM은 기존 전투 루프를 대체할 물건이 아니다.** 위 구조를 상태 객체로 옮기면 이벤트 구독자가 전부 상태 진입/이탈 쌍으로 갈라져, `PlayerModeDirector` 주석이 지목한 바로 그 버그(`OnEnter` 넷에는 넣고 `OnExit` 하나를 빠뜨림)가 디렉터 수만큼 생긴다. **HFSM이 실제로 값을 내는 자리는 지금 코드에 없는 층 — 게임 전체 흐름(탐색 ↔ 조우 ↔ 곡 ↔ 결과 ↔ 컷신)과 시퀀스(튜토리얼·브리핑·엔딩)다.** 그 층은 CLAUDE.md §9·§13에서 "설계 확정 · 코드 미구현"으로 남아 있는 부분과 정확히 겹친다.

### 이미 존재하는 인접 코드

| 있는 것 | 하는 일 | 새 코드와의 관계 |
|---|---|---|
| `PlayerModeDirector` (미커밋) | 4모드 · `ApplyMode`가 모든 노브를 무조건 대입 | **상태 기계 후보 1순위**. 주석에 "승격 조건"까지 적혀 있다 |
| `InputHandler.SetMode` (미커밋) | 액션 맵 배타 스위칭 | 상태 진입 시 부를 대상 |
| `Singleton<T>` / `ManagerRoot` | 씬 넘김 · 중복 파괴 | `UIManager`가 여기 얹힐지가 §3의 쟁점 |
| `GameSession` | 씬 간 `SelectedChart` · `LastResult`(읽는 쪽 없음) | 결과 화면 = `SubUIManager` 후보 |
| `EffectManager` / `CanvasEffectPool` | 게임플레이 Canvas 이펙트 | `SubUIManager` 체계 **밖**이다. 편입 여부 미정 |
| `ScoreHudView` · `DodgePointView` | 씬 Canvas 직속 뷰 | 같음 |

---

## 2. StateMachine 계층 — 무엇을 주고 무엇이 비었나

### 2-1. 구조

- `IState`: `Enter`/`Execute`/`FixedExecute`/`Exit` 넷.
- `StateBase<T>`: `caster`(소유자) 보관 + 전부 `virtual` 빈 구현.
- `StateMachine<T>`: `Enum → StateBase<T>` 사전, from→to 조건 전이, AnyState 전이, `OnStateChanged` 이벤트. **AnyState가 일반 전이보다 우선**하고(`CheckNextStateCondition`), 자기 자신으로의 AnyState 전이는 막혀 있다.
- `CompositeStateBase<T>`: 서브 머신 보유. `Enter`/`Execute`/`FixedExecute`는 부모 → 자식 순, `Exit`만 **자식 → 부모 역순**(주석대로 지켜져 있다).

전이 검사가 `Update` 안(`Execute` 직후)이라 **한 프레임에 상태가 여러 번 바뀔 수 없다**(전이 후 즉시 `break`/`return`). 이건 장점이다 — 연쇄 전이로 프레임이 튀는 부류가 없다.

> **[2026-08-28 결정] HFSM을 도입하지 않는다.** `CompositeStateBase`는 다른 프로젝트에서 그대로 가져온 코드였고 이 프로젝트에 사용처가 없다. 파일을 삭제했고, 그것만을 위해 있던 `SetInitialState`·`Enter()`도 함께 제거했다(`Exit()`는 `Stop()`으로 이름을 바꿔 남겼다 — 소유자가 꺼질 때 현재 상태를 정리할 곳이 필요하다).
> 그 결과 **아래 C-1 · C-2 · C-3과 R-8이 코드째 사라졌다**(표현 불가능해졌다). 기록으로만 남긴다. 계층이 다시 필요해지면 이 절이 그때 확인할 목록이다.

### 2-2. 확인된 결함 — 치명(조용히 기능이 죽는다) *(해소됨 — 위 결정 참조)*

#### C-1. 복합 상태를 두 번째로 들어가면 자식이 영구히 죽는다

`StateMachine`은 상태를 **두 곳**에 들고 있는 것처럼 보이지만 실은 `state` 필드 하나뿐이다. `SetInitialState`(`:81-85`)는 "초기 상태"를 따로 기억하는 게 아니라 그냥 `state`에 대입한다. 그리고 `Exit()`(`:74-78`)은 그 `state`를 `null`로 지운다.

```
1회차 진입: SetInitialState(Idle) → state = Idle
            Enter()              → Idle.Enter()      정상
   이탈:    Exit()               → Idle.Exit(); state = null
2회차 진입: Enter()              → null?.Enter()     아무 일도 안 일어남
            Update()             → null?.Execute()   영원히 빈 채로 돈다
```

`SetInitialState`를 다시 불러 주는 코드는 어디에도 없다. **예외도, 로그도, 경고도 없다** — 복합 상태 안이 그냥 텅 빈 채 계속 돌아간다. 탐색 → 전투 → 탐색처럼 같은 상태를 다시 들어가는 흐름이 이 개편의 전제이므로 첫 실사용에서 바로 밟는다.

고치려면 `SetInitialState`가 값을 **별도 필드로 기억**하고 `Enter()`가 그 값으로 되돌린 뒤 진입해야 한다. 지금 구조에는 기억할 자리가 없다.

#### C-2. HFSM의 존재 이유인 "자식 어디서든 부모 밖으로"를 표현할 수 없다

계층형 상태 기계를 쓰는 이유는 대개 하나다 — *"전투 안의 어느 세부 상태에 있든, 죽으면 전투 밖으로 나간다"*를 **한 곳에 한 번만** 적기 위해서다. 평평한 FSM이면 세부 상태 수만큼 같은 전이를 복사해야 한다.

그런데 지금 `AnyState` 전이는 **등록된 그 머신 안에서만** 유효하다. 서브 머신에 걸면 목적지도 서브 머신 안의 형제 상태여야 하고, 부모 머신에 걸면 그 조건이 자식 상태의 사정을 들여다봐야 한다 — 어느 쪽도 계층이 아니다.

```
Combat (복합)                     ← 여기서 나가고 싶다
 ├ Approach   ┐
 ├ Attack     ├ 어느 자식에 있든
 └ Recover    ┘
Explore

지금 가능한 것: Combat.SubStateMachine에 AnyState → Approach/Attack/Recover 중 하나
             (= 자식들끼리만 오갈 수 있다)
필요한 것:    자식이 어디에 있든 Combat 자체를 빠져나가 Explore로
```

즉 **계층 구조는 있는데 계층 전이가 없다.** 이걸 성립시키려면 전이 검사가 아래(자식)가 아니라 **위(부모)부터 내려오면서** 이뤄져야 하고, 부모에서 전이가 결정되면 자식 검사를 건너뛰어야 한다. 지금은 순서가 정확히 반대다(C-3).

#### C-3. 전이 검사 순서가 뒤집혀 있어, 자식이 켜졌다가 같은 프레임에 꺼진다

`Update()`(`:88-92`)는 이렇게 돈다:

```
state.Execute()            ← 복합 상태면 이 안에서 서브 머신 Update()
                             = 자식 전이가 여기서 먼저 일어난다
CheckNextStateCondition()  ← 그 다음에야 부모 전이를 검사한다
```

부모가 나가기로 결정된 프레임에도 자식은 **이미 전이를 마친 뒤**다. 그 자식의 `Enter()`가 이펙트를 띄우고 소리를 내고 예약을 걸어 놨다면 그것들이 전부 새어나간 뒤 곧바로 `Exit()`된다. 한 프레임짜리 깜빡임과 유령 예약이 남는 부류이고, **재현이 프레임 타이밍에 달려 있어 원인을 짚기 어렵다**.

올바른 순서는 "부모 전이 검사 → (안 나가면) 자식 실행"이다.

### 2-3. 확인된 결함 — 위험(조건이 맞으면 예외 또는 상태 붕괴)

> **[2026-08-28 결정] 전이 구성은 실행 전에 확정한다.** 머신을 돌리기 시작하기 전에 상태와 전이 조건을 **전부** 등록하고, 그 뒤로는 구성을 바꾸지 않는다. 런타임에 전이를 추가·삭제하는 것은 기능이 아니라 **오용**이다.
>
> 이 규칙 하나가 아래 세 항목을 지운다:
> - **R-6은 전제가 무효다** — 순회 중 컬렉션이 변경될 경로 자체가 없다.
> - **R-7은 고칠 게 아니라 없앨 코드였다** — 해제 API 6개가 통째로 죽은 코드가 된다.
> - **R-5는 규약으로 봉인된다** — 전환이 조건 평가로만 일어나면 `Enter`/`Exit` 안에서 `ChangeState`를 부를 이유가 없다. 다만 **강제되지는 않는다**(아래 R-5 참조).

#### R-4. `CompareState(StateBase<T>, Enum)`만 규칙이 둘 다 다르다 *(수정 완료 — 2026-08-28)*

**조치**: `stateDict[e]` 직접 인덱싱을 `TryGetValue`로 바꿔 미등록 키에 `false`를 돌려주게 했다(예외 소멸). **비교 방식은 참조 그대로 두었다** — Enum 키는 인스턴스 하나를 정확히 가리키므로 타입 비교보다 좁고 정확하고, D-14(같은 클래스 인스턴스 둘을 같다고 답하는 문제)가 여기서는 애초에 안 생긴다. 그 의도를 주석으로 명시했다.

아래는 원래 지적의 기록이다.

```csharp
public bool CompareState(StateBase<T> s1, StateBase<T> s2)  // null-safe, 타입 비교
public bool CompareState(StateBase<T> s1, string s2)        // null-safe, 타입명 비교
public bool CompareState(StateBase<T> s1, Enum e) => s1 == stateDict[e];  // 예외, 참조 비교
```

세 번째만 (a) 미등록 키에 `KeyNotFoundException`을 던지고 (b) 타입이 아니라 참조를 비교한다. 같은 이름의 메서드가 인자 타입에 따라 다르게 동작하면 호출부는 그 차이를 못 본다.

#### R-5. `Enter()`/`Exit()` 안에서 전이하면 바깥 전이가 안쪽 전이를 덮어쓴다 *(가드 추가 완료 — 2026-08-28)*

**⚠ 이것은 "전이 조건 등록"이 아니라 "`ChangeState` 명령형 호출"에 관한 문제다**(R-6과 다른 항목이다). 전환이 조건 평가로만 일어나면 이 경로는 실제로 안 밟지만, `ChangeState`가 **머신을 시작하는 유일한 공개 진입점**이라 public이어야 하고 그 API가 곧 규칙을 어길 수 있는 통로다.

**조치**: `isTransitioning` 플래그를 두어 전이 도중의 재진입 호출을 **거부하고 `LogError`를 찍는다**(프로젝트 관용구 — `PlayerModeDirector.WarnIfBothMoversLive`). 조용한 오작동이 시끄러운 에러가 됐다.

- **바깥 전이가 이긴다.** 안쪽 호출을 무시하는 쪽을 골랐다 — 바깥은 이미 이전 상태의 `Exit()`를 끝냈으므로 그걸 되돌릴 방법이 없다.
- **`try`/`finally`로 플래그를 푼다.** `Enter`/`Exit`가 예외를 던져도 플래그가 갇히지 않는다 — 갇히면 이후 **모든** 전이가 막혀 원래 버그보다 나쁘다.
- **`Stop()`에도 같은 가드를 건다.** 없으면 `Exit()` 안의 `ChangeState`가 방금 멈춘 머신을 되살려 `Stop`이 무효가 된다.

아래는 가드가 없을 때 무슨 일이 났는지의 기록이다.

`ChangeState`(`:46-56`)에 "전이 진행 중" 가드가 없다.

```csharp
var prevState = state;
state?.Exit();      // ← 이 안에서 다시 ChangeState(B)가 불리면
                    //   그쪽이 state = B 까지 끝내고 돌아온다
state = newState;   // ← 그런데 여기서 A로 덮어써 버린다. B 전이가 통째로 사라진다
state?.Enter();
OnStateChanged?.Invoke(prevState, state);  // prevState도 이미 거짓말
```

`Exit()`에서 이벤트를 발행하고 구독자가 그걸 보고 상태를 바꾸는 것은 흔한 패턴이라 실제로 밟는다. 최소 수정은 전이 중 플래그를 두고 재진입 호출을 큐에 넣거나 거부하는 것이다.

#### R-6. `Enter()` 안에서 전이를 등록하면 `InvalidOperationException` *(폐기 — 전제 무효)*

`CheckAnyStateTransitions`·`CheckNormalTransitions`가 리스트를 `foreach`로 순회하는 도중 `ChangeState`를 부르고, 그 `Enter()`가 `RegisterCondition`으로 같은 리스트를 건드리면 순회가 깨진다고 봤다. **그러나 전이 구성은 실행 전에 확정되므로 런타임 등록 자체가 없다** — 이 문제는 성립하지 않는다.

#### R-7. `UnregisterState`가 절반만 지운다 → **해제 API 삭제로 소멸** *(조치 완료 — 2026-08-28)*

원래 지적: 상태는 `stateDict`(Enum → 인스턴스)·`transitionDict`(**인스턴스** → 전이 목록)·`anyStateTransitions` 세 곳에 흩어져 있는데 `UnregisterState`는 `stateDict`만 지운다. 나머지 둘에 남은 항목이 조건 충족 시 **등록 해제된 상태로 전이시킨다**("지웠는데 되살아난다").

**그런데 위 결정에 따르면 이건 고칠 게 아니라 없앨 코드다.** 아래 여섯은 전부 "이미 돌고 있는 머신의 전이 구성을 바꾼다"를 위한 API이고, 전이를 실행 전에 확정하는 모델에서는 부를 일이 없다:

```
UnregisterState(Enum)
UnregisterCondition(StateBase<T>, StateBase<T>)
UnregisterCondition(Enum, Enum)
UnregisterAnyStateTransition(StateBase<T>)
UnregisterAnyStateTransition(Enum)
ClearAnyStateTransitions()
```

**여섯 개를 전부 삭제했고 R-7은 함께 소멸했다.** 규칙은 `StateMachine.cs` 헤더 주석에 못박아 두었다 — 나중에 누가 "해제 메서드가 없네"라고 다시 추가하는 것을 막는 것이 그 주석의 목적이다.

#### R-8. `SetInitialState`가 현재 상태를 `Exit` 없이 덮어쓴다 *(해소됨 — 메서드 삭제)*

활성 중에 부르면 지금 상태의 이탈 처리가 통째로 누락됐다. 진입/이탈 쌍이 안 맞는 그 순간부터 그 상태가 켜 놓은 것(레이어 웨이트, 구독, 타이머)이 영구히 남는다. **진입 경로가 `ChangeState` 하나로 좁혀져 이 부류가 원천 소멸했다.**

### 2-4. 확인된 결함 — 설계(지금 고치면 싸다)

- **D-9. `StateMachine.caster`가 죽은 필드다**(`:20`, `:34`, `:40`). 어디서도 안 읽힌다. `StateBase.caster`가 실제로 쓰이는 쪽이라 완전 중복이고, 지우면 `StateMachine<T>`의 `T`는 `StateBase<T>` 타입을 맞추는 용도만 남는다.
- **D-10. `IState.FixedExecute`는 이 프로젝트에서 죽은 API다.** 플레이어·적 이동이 전부 `transform.position` 직접 대입이고 물리를 쓰지 않는다(CLAUDE.md §11-2). 남겨 두면 상태를 만들 때마다 빈 구현이 하나씩 늘어난다.
- **D-11. 네임스페이스가 없다.** `IState`·`StateBase`·`StateMachine`은 패키지·에셋스토어 코드와 충돌하기 딱 좋은 이름이다. 프로젝트 관용구는 `PatternSpace`·`EnemySpace`·`SliceSpace`다.
- **D-12. 미등록 키 정책이 메서드마다 다르다.** `ChangeState(Enum)`(`:59`)은 조용히 무시, `RegisterCondition(Enum, Enum, ...)`도 조용히 무시(`GetState`가 null 반환 → 내부에서 return), `CompareState(_, Enum)`은 예외. 개발 중 오타가 "아무 일도 안 일어남"으로 나타난다. 이 프로젝트의 관용구는 경고를 찍는 쪽이다(`PlayerModeDirector.WarnIfBothMoversLive`, 굽기 툴의 저장 중단).
- **D-13. 일반 전이에는 자기 자신 방지가 없다.** AnyState에는 `if (state == next) continue`가 있는데(`:239`) `CheckNormalTransitions`에는 없다. `RegisterCondition(A, A, cond)` 하나면 매 프레임 `Exit`/`Enter`가 돈다.
- **D-14. `CompareState(s1, s2)`의 타입 비교가 위험하다.** 같은 클래스로 인스턴스를 둘 만들면(파라미터가 다른 공격 상태 둘) 서로 같다고 답한다.
- **D-15. `StateMachine(T, StateBase<T>)` 생성자만 규칙이 다르다**(`:38-43`). 생성 도중에 `Enter()`가 돌고, 그때는 아직 구독자가 없어 `OnStateChanged`도 안 난다. 첫 진입만 다른 규칙을 따른다.
- **D-16. 전이 후 그 프레임에는 새 상태의 `Execute`가 안 돈다.** `Update()`가 `Execute` → 전이 검사 순서라 전이는 프레임 끝에 일어난다. 의도된 1프레임 지연인지 확인이 필요하다(대개는 무해하지만, 진입 프레임에 즉시 무언가 해야 하는 상태에서는 `Enter`에 넣어야 한다는 뜻이 된다).
- **D-17. `OnStateChanged`가 HFSM 경로에서는 안 난다.** `ChangeState`에서만 발행되고 `Enter()`/`Exit()`(`:68`, `:74`)에서는 발행되지 않아, 서브 머신 구독자는 진입·이탈을 못 본다. 지금은 구독자가 없어 무해하다.

### 2-5. 남은 작업 / 열린 질문

HFSM 제거(§2-2) + 전이 구성 사전 확정(§2-3) 두 결정을 반영한 현재 상태:

| 항목 | 상태 |
|---|---|
| C-1 · C-2 · C-3 | **소멸** — `CompositeStateBase` 삭제 |
| R-8 | **소멸** — `SetInitialState` 삭제 |
| R-6 | **폐기** — 전제 무효(런타임 등록이 없다) |
| R-7 | **소멸** — 해제 API 6개 삭제 (2026-08-28) |
| R-4 | **수정 완료** — `TryGetValue`로 예외 제거 (2026-08-28) |
| R-5 | **가드 추가 완료** — 재진입 거부 + `LogError` (2026-08-28) |
| D-9 ~ D-17 | 유효 — **보류**(아래) |

`StateMachine` 계층에 **코드로 반드시 고칠 것은 남아 있지 않다.** 현재 공개 API는 다음이 전부다:

```
CurState / OnStateChanged
ChangeState(StateBase<T>) / ChangeState(Enum) / Stop()
Update() / FixedUpdate()
RegisterState / GetState
CompareState x3
RegisterCondition x3 / RegisterAnyStateTransition x2
```

Unity 컴파일 확인 완료(에러·경고 0).

### 2-6. [2026-08-28] FSM 리팩토링 종료

**C·R 계열은 전부 처리됐다**(소멸 5 · 폐기 1 · 수정 2). **D 계열은 의도적으로 남긴다** — 전부 "쓰기 시작하면 답이 정해지는" 항목이라 지금 고치면 근거 없이 정하는 셈이 된다:

- **D-10 `FixedExecute` 제거** — 물리를 쓰는 상태가 하나도 안 나올 것이 확실해지면 지운다. 지금 지우면 되살릴 때 `IState` 구현체 전부를 건드려야 한다.
- **D-16 전이 후 1프레임 지연** — 진입 프레임에 즉시 무언가 해야 하는 상태가 나와야 문제인지 아닌지 판단된다.
- **D-9 `caster` 중복 · D-11 네임스페이스 · D-12 미등록 키 정책 · D-13 자기 전이 · D-14 타입 비교 · D-15 생성자 · D-17 이벤트 누락** — 첫 실사용처에서 실제로 걸리는 것부터 처리한다.

**다음 작업은 FSM이 아니라 UI(§3) · SequenceSystem(§4)이다.** 그쪽에는 설계 이전에 고쳐야 할 것이 남아 있다(§6) — 특히 `SequenceStateBase`의 파라미터 생성자는 **씬 배치 자체를 막는다**.

- `PlayerModeDirector`를 상태 기계로 승격할 것인가. 승격하면 그 클래스 주석이 경고한 "진입/이탈 쌍 누락" 위험을 **다시 들이는** 셈이다. 승격의 이득(모드별 매 프레임 로직)이 실제로 생겼는지 먼저 확인해야 한다.
- 이 FSM의 첫 실사용처는 어디인가(게임 흐름 / 플레이어 모드 / 적 `Phase`). 그에 따라 D-10(`FixedExecute` 제거)·D-16(1프레임 지연)의 답이 갈린다.

---

## 3. UI 계층

> **[2026-08-28 결정] `SubUIManager`와 `UIManager`를 삭제했다.**
>
> 씬의 UI 계층 전부를 `SubUIManager`로 관리할 수 있는지 검토한 결과, **현재 씬의 6개 중 편입 대상이 하나도 없었다**:
> - `AmbientEffectLayer` · `EffectOverlayLayer` — 이펙트 부모일 뿐, 조회도 개폐도 안 한다
> - `PatternHandler` — 판정의 중심이고 직접 참조로 배선된다
> - `EffectManager` — 뷰가 아니라 디렉터다
> - `DodgePoint` — `DodgeDirector`가 직접 참조로 제어한다
> - `ScoreHud` — **편입하면 위험하다.** `SubUIManager.Hide()`가 `SetActive(false)`인데 CLAUDE.md §14가
>   그것을 금지한다(`OnDisable`이 구독을 풀어 감춘 사이의 점수 변화를 놓친다). `SetHidden`이 `CanvasGroup`
>   알파를 미는 이유가 그것이고, `Hide()`는 그 버그를 부르는 공개 API가 된다.
>
> 즉 클라이언트가 `DialogUI` 하나뿐인 추상화였다. 그 하나는 **정적 접근자 4줄**로 대체했다
> (`DialogUI.Instance`). `Singleton{T}`를 안 쓴 이유는 그 게터가 없을 때 **빈 게임오브젝트를 만들어 내기** 때문 —
> 자식 참조가 없는 껍데기보다 `null`이 낫다.
>
> **부수 효과로 함정 셋이 통째로 사라졌다**: ① 중첩 Canvas에서 `sortingOrder`가 조용히 무시되는 것
> ② 루트 Canvas로 분리 시 `GraphicRaycaster`가 각자 필요해지는 것(`PatternHandler` 판정 직격)
> ③ 렌더 순서의 진실의 원천이 형제 인덱스(CLAUDE.md §1)에서 숫자로 이동하는 마이그레이션.
> 정렬 순서는 이제 `Canvas` 컴포넌트 하나가 소유한다.
>
> 아래 §3-1·§3-2는 삭제 전 분석 기록이다. `DialogUI` 관련 결함(3·4·5·6·8)은 §6에 따라 수정됐고,
> 1(등록 순서 레이스)은 `Awake` 등록으로 유지된다.

### 3-1. 구조 *(삭제 전 기록)*

- `UIManager : Singleton<UIManager>` — `Type → SubUIManager` 사전. `Register`/`Get<T>`/`TryGet<T>`뿐. **씬 오브젝트가 아니어도 되는 순수 레지스트리**다.
- `SubUIManager : MonoBehaviour` — `Canvas`를 요구하고 `SortingOrder`(추상 프로퍼티)를 `Awake`에 대입, `Start`에서 자기를 등록. `Show`/`Hide`는 `SetActive`.
- `DialogUI : SubUIManager` — 대사 큐 + 타자기 연출 + `OnExit`.

### 3-2. 확인된 결함 / 함정

1. **⚠ 등록 순서 레이스.** `SubUIManager.Start()`에서 등록하는데, 소비자(`DialogState.Enter` → `UIManager.Instance.Get<DialogUI>()`)가 같은 프레임의 `Start`에서 돌면 **null을 받는다** — `SequenceController.Start()`가 정확히 그 경로다(`SequenceController.cs:21`이 첫 상태의 `Enter`를 `Start`에서 부른다). Unity의 `Start` 순서는 보장되지 않는다. → **등록은 `Awake`로 내려야 한다.**
2. **`UIManager`가 `Managers` 프리팹에 없다.** `Singleton.Instance` 게터가 없으면 런타임에 빈 게임오브젝트를 만들어 준다 — 즉 **에러 없이 조용히 동작하고**, 씬 전환 시 `DontDestroy`가 false라 레지스트리가 씬과 함께 사라진다. 이건 오히려 **맞는 동작**이다(UI 인스턴스가 씬에 산다). 다만 CLAUDE.md §10의 `Pool`과 같은 이유로 **의도라는 사실을 명시해야** 한다.
3. **`SubUIManager.Awake`가 `Canvas` 없으면 다음 줄에서 `NullReferenceException`.** 최소 수정은 `[RequireComponent(typeof(Canvas))]` 한 줄.
4. **`DialogUI.Awake`가 `Reset()`을 직접 부른다.** `Reset`은 에디터 매직 메서드이고, 그 안에서 `transform.Find("Dialog")` 등으로 **인스펙터에서 배선한 `[SerializeField]`를 매 실행 덮어쓴다.** 하이어라키 이름이 계약이 되어 프리팹 구조를 못 바꾼다. → 런타임 경로에서 빼는 것이 맞다.
5. **`DialogUI` 주석이 인코딩 깨져 있다**(`// ���� ������` 두 줄). CP949로 저장된 파일이 UTF-8로 읽힌 흔적. 파일 인코딩을 UTF-8로 통일해야 한다.
6. **`OnExit` 구독이 절대 안 풀린다**(`DialogState.cs:25`가 람다를 `+=`). 같은 `DialogUI` 인스턴스에 대사 상태가 둘 이상 붙으면 **이전 상태의 `finishDialog`까지 같이 true가 된다.** `Exit`에서 해제하거나, 애초에 `SetDialog`가 완료 콜백을 인자로 받는 편이 낫다.
7. `NextDialog`의 스킵 가드가 `nowData?.skipProgress > progress`라 **`nowData`가 null인 첫 호출은 통과**한다(의도된 동작). 다만 "출력 중이면 전문 즉시 표시" 경로와 "큐에서 다음을 꺼냄" 경로가 **한 호출에 붙어 있어**, 스킵과 다음 넘기기가 한 번의 클릭으로 동시에 일어난다. 의도 확인 필요.
8. `dialogQueue`가 `SetDialog` 호출 간에 **비워지지 않는다** — 이전 대사가 남아 있으면 이어 붙는다.
9. 타자기는 `WaitForSecondsRealtime`이라 `Time.timeScale = 0`에서도 돈다. **이 조합 자체는 맞다**(§4의 timeScale 쟁점과 짝).
10. `dialogScript.text += ...`가 문자마다 문자열을 새로 만들고, 그때마다 `CountVisibleChars`가 전체를 다시 훑는다(O(n²)). TMP는 `maxVisibleCharacters`로 같은 연출을 할당 0으로 낸다 — **전문을 한 번 대입하고 보이는 글자 수만 늘리는 쪽**이 표준 해법이고, 리치 텍스트 태그 건너뛰기 코드도 통째로 사라진다.

---

## 4. SequenceSystem

### 4-1. 구조

`SequenceController`(MonoBehaviour)가 `List<SequenceStateBase>`를 `Queue`로 옮겨 하나씩 실행한다. `Update`→`Execute`, `FixedUpdate`→`FixedExecute`, `LateUpdate`→`FinishSequenceCondition()` 폴링 → 참이면 다음으로. 큐가 마르면 `OnFinishSequence`.

`DialogState`는 진입 시 `timeScale = 0` → `DialogUI` 표시, 이탈 시 timeScale 복구.

### 4-2. 확인된 결함 / 함정

1. **⚠ `SequenceStateBase`가 `MonoBehaviour`인데 파라미터 생성자만 있다**(`SequenceStateBase.cs:3,7`). 기본 생성자가 사라지므로 **Unity가 컴포넌트를 붙일 수 없다**(직렬화/`AddComponent` 실패). `DialogState`도 그 생성자를 그대로 물려받는다(`DialogState.cs:14`). **이 상태로는 씬에 배치 자체가 안 된다** — 지금 씬 배선이 0인 이유일 가능성이 높다.
2. **`controller` 필드가 죽어 있다.** MonoBehaviour라 생성자로 주입될 일이 없고, 읽는 곳도 없다. 상태가 "다음으로 넘어가라"를 스스로 호출할 길(`controller.SetNextSequence()`)이 끊겨 있다.
3. **`Start`에서 `actionQueue.Dequeue()`를 무방비로 부른다**(`SequenceController.cs:20`). `queueStates`가 비면 `InvalidOperationException`.
4. **`CurTutorialState`라는 이름이 남아 있다.** 이 시스템은 튜토리얼 전용이 아니다(브리핑·엔딩·컷신이 같은 틀을 쓴다). `CurrentState`가 맞다.
5. **`IState`를 두 곳이 서로 다른 뜻으로 쓴다.** `StateMachine` 쪽 상태는 순수 C# 객체 + 조건 전이인데, 시퀀스 쪽은 MonoBehaviour + `FinishSequenceCondition` 폴링이다. **인터페이스만 공유하고 실행 모델이 다르다** — 시퀀스는 `StateMachine<T>`으로도 표현 가능하다(선형 전이 = `RegisterCondition(i, i+1, cond)`). 둘을 합칠지 나눌지가 이 개편의 핵심 결정이다.
6. **⚠ `Time.timeScale = 0`은 이 프로젝트에서 가장 위험한 줄이다.** CLAUDE.md §7-3이 명시적으로 금지한다 — 판정·클립 정렬은 `Time.time`인데 채보는 `audioSource.time`으로 돌고 **오디오는 timeScale 밖**이라 차이가 영구 누적된다. §14(`FinaleSilhouetteDirector`)만이 예외이고, 그 근거는 "마지막 판정이 끝나 누적될 곳이 없다"는 것이다.
   → **대사가 곡 도중에 뜨면 안 된다.** 규칙으로 못박아야 할 것: *대사 시퀀스는 무대 밖(탐색·조우 전·곡 종료 후)에서만 열린다.* 그러면 `timeScale = 0`이 안전해지고, 그렇지 않다면 timeScale 대신 **입력 차단 + 이동 정지**로 바꿔야 한다.
   또한 `PlayerModeDirector`가 이미 "누가 위치의 주인인가"를 들고 있으므로, **대사를 `PlayerMode.Overlay`로 표현하는 것이 이 프로젝트의 관용구**다.
7. `FinishSequenceCondition` 폴링이 `LateUpdate`인 것 자체는 맞다(같은 프레임의 `Execute` 결과를 보고 넘어간다). 다만 상태가 스스로 끝을 알리는 방식(`controller.SetNextSequence()`)이 있으면 폴링이 필요 없다 — **지금은 두 방식의 뼈대가 반씩 있다**(`SetNextSequence`가 public인데 부르는 쪽이 없다).

---

## 5. 세 덩어리를 관통하는 결정 사항 (Plan에서 답할 것)

1. **`IState`를 하나로 유지할 것인가, 시퀀스를 `StateMachine<T>` 위에 얹을 것인가.**
2. ~~**HFSM(`CompositeStateBase`)의 첫 실사용처는 어디인가.**~~ → **[2026-08-28 결정] 도입하지 않는다. 파일 삭제, 평평한 FSM만 쓴다**(§2-2 상단).
3. **게임 흐름 최상위 상태를 누가 드는가** — 새 `GameFlowDirector`인가, `PlayerModeDirector` 승격인가. CLAUDE.md §9의 `EncounterDirector`(미구현)와 역할이 겹친다.
4. **대사 중 시간 정지를 무엇으로 표현하는가** — `timeScale`(§7-3 위반 위험) vs `PlayerMode.Overlay` + 입력 맵 차단.
5. **`SubUIManager` 체계에 기존 뷰(`ScoreHudView`·`DodgePointView`·`EffectManager`의 Canvas)를 편입할 것인가.** 편입하면 `sortingOrder`가 한 곳에 모이지만, 현재 루트 Canvas가 `ScreenSpaceOverlay` 하나이고 그것이 §7-5(앵글 교체가 자유로운 이유)와 §11-8(닷지 포인트가 카메라를 모르는 이유)의 전제다 — Canvas를 쪼개면 그 전제를 다시 확인해야 한다.
6. **`SequenceStateBase`의 MonoBehaviour 여부.** MonoBehaviour면 인스펙터에서 대사를 저작할 수 있고(`DialogState.datas`), 순수 C# 객체면 `StateMachine<T>`과 통합된다. **둘 다 갖고 싶으면 ScriptableObject가 세 번째 선택지다**(대사 데이터는 에셋, 실행은 순수 객체 — `Pattern`·`SongChart`가 이미 그 관용구다).

## 6. 반드시 먼저 고쳐야 하는 것 (설계와 무관한 버그)

- `SequenceStateBase`의 파라미터 생성자 제거 — **없으면 씬 배치가 불가능**하다.
- `CompositeStateBase` 재진입 시 초기 상태 리셋.
- `SubUIManager` 등록을 `Awake`로 이동.
- `DialogState`의 `OnExit` 구독 해제.
- `DialogUI.cs` 파일 인코딩 UTF-8 복구.
