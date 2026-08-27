# Research — 탐색 이동과 전투 조작의 분리 (PlayerExplore)

## 0. 이 문서의 범위

`곡 선택 씬 → 전투 씬` 구조를 폐기하고 **1자 진행(심상세계를 걸어 다니다 무대에 들어서면 곡이 시작된다)** 으로 가는
개편의 **첫 단계**만 다룬다 — 즉 **플레이어 이동(탐색)과 현재 플레이어 조작(패턴 판정·결투 이동)의 분리**다.
`EncounterDirector`·`Encounter`·`GameMode`·씬 재편은 **이 문서의 범위 밖**이며 다음 단계에서 다룬다.

근거 문서: `CLAUDE.md` §9 · §11-2 / `docs/Story/Story_Overview.md` §1-1 · §9 (A행).

---

## 1. 지금 플레이어의 위치를 소유하는 것들

`transform.position`을 **대입**하는 주체가 현재 셋이다. 전부 `PlayerCombatMover` 안에 있다.

| 주체 | 코드 | 구동 조건 |
|---|---|---|
| 수렴 이동 | `Update`의 `Vector3.Lerp(moveFrom, moveTo, …)` | `moving == true` (디렉터의 `OnDuelScheduled`가 켠다) |
| 결투 거리 커브 | `TickCurveDrive()` | 도착 후 + 자기 패턴이 재생 중(`PlayingImpactAlignTime` 대조) |
| 회피 구르기 | `TickRoll()` | `RollArc()` 호출 후 (`DodgeDirector`) |

우선순위는 `Update` 안에 순서로 박혀 있다: `rolling` > 커브 구동 > 수렴 이동.
**셋 다 물리를 쓰지 않는다** — 콜라이더·Rigidbody 없이 닫힌 보간이며, 도착 시각 계약(§6 임팩트 정렬)이
그 이유다(`PlayerCombatMover` 클래스 주석 참조).

회전도 같은 클래스가 든다(`ScheduleTurn`, `turnDuration` 0.15초). 이동과 **별개 스케줄**이다.

### 위험 지점 (§11-2가 이미 명시)

> 탐색 컨트롤러가 같은 프레임에 살아 있으면 대입이 서로를 덮어 **간격 커브·도착 시각 계약이 통째로 깨진다.**

즉 이 단계에서 실제로 설계할 것은 "탐색 이동을 어떻게 만드는가"가 아니라
**"위치의 주인이 언제나 정확히 하나임을 무엇이 보장하는가"** 다.

---

## 2. 지금 플레이어의 조작을 받는 것들

`InputHandler`(`Assets/02. Scripts/Input/InputHandler.cs`)가 **게임플레이 입력의 유일한 출처**다.

- `IngameInputs.inputactions`의 `Player` 맵을 `Start()`에서 **무조건 `Enable()`** 한다.
- 발행 이벤트는 둘뿐: `OnKeyPressed(int 0~8)`(노드 1~9), `OnDodgePressed`(Space).
- 구독자: `PatternHandler`(노드) · `DodgeDirector`(회피).
- `IngameInputs.cs`는 Input System 자동생성 래퍼라 **직접 수정하지 않는다** — `.inputactions` 에셋을 고치면 재생성된다.

액션 맵 현황:

| 맵 | 액션 |
|---|---|
| `Player` | `Input1`~`Input9`(키보드 숫자열 + 넘패드), `Dodge`(space) |
| `UI` | Unity 기본 UI 맵 (그대로) |

**이동 입력이 존재하지 않는다.** 탐색용 `Move`/`Look`은 새로 만들어야 한다.

---

## 3. 애니메이션 — base 레이어의 주인 다툼

`PlayerAnimator.controller`의 스테이트:

| 레이어 | 스테이트 |
|---|---|
| `Running Layer` (base) | `Katana_Idle`, `Sprint`, `Quickshift` |
| `Attack Layer` | `Attack_A`, `Attack_B`, `Release` |

파라미터: `AttackSpeed` · `ReleaseSpeed` · `QuickshiftSpeed` · `SprintSpeed`.

**⚠ 걷기 클립이 없다.** 탐색 로코모션은 `Katana_Idle` ↔ `Sprint`(배속 조절)로 시작할 수밖에 없다.

`CharacterActionPlayer`가 base 레이어를 매 프레임 되찾는다:

```
Update() → 액션이 없으면 → SwitchBaseStateUnlessConverging(idleStateHash)
```

그리고 그 되찾기를 막는 **기존 래치가 이미 있다** — `convergeUntil`:

```csharp
private void SwitchBaseStateUnlessConverging(int stateHash)
{
    if (Time.time < convergeUntil) return;   // "지금 base는 내 것이 아니다"
    SwitchBaseState(stateHash);
}
```

수렴 로코모션(`ApplyLocomotion`)이 `convergeUntil = plan.PlayerArriveTime`으로 이 래치를 쓴다.
**탐색 로코모션이 요구하는 의미가 정확히 같다** — 새 상태 필드를 만들 이유가 없다.

애니메이터 레이어 인덱스·스테이트 해시·`AnimatorOverrideController`는 전부 `CharacterActionPlayer.Awake`에 있다.
탐색 컨트롤러가 이것을 복제하면 **스테이트 이름의 진실의 원천이 둘**이 된다.

---

## 4. 카메라

`CameraDirector`가 셋을 든다(§7-2): 쉐이크 / 프레이밍(`CinemachineTargetGroup`) / 인트로(스플라인).
게임플레이 vcam은 `TrackingTarget = CameraTargetGroup`, `BindingMode = LockToTarget`,
그룹 회전을 `CameraDirector`가 플레이어 yaw로 몬다.

**이 구도는 교전을 전제한다** — 그룹 멤버 1번이 상대이고, 프레이밍이 "둘을 담는다"를 한다.
탐색에는 상대가 없으므로 그대로 쓰면 "가중치 0인 멤버를 든 그룹"이 되고, 카메라 조작(마우스 회전)도 들어갈 자리가 없다.

**교체 관용구는 이미 있다** — `CameraAngleSwitcher`(§7-5)가 **우선순위만 갈아끼우고 이동은 Cinemachine 블렌드가 한다.**
탐색 vcam도 같은 방식으로 붙일 수 있다. 활성 우선순위 참고: 게임플레이 앵글 10 · 인트로 20.

**⚠ 루트 Canvas가 `ScreenSpaceOverlay`**라(§7-5) 패턴인풋·포커스 링은 카메라와 완전히 독립이다 —
탐색 중에도 화면에 그대로 남는다. 감추는 책임은 별도다(`ScoreHudView.SetHidden`이 같은 성질의 선례, §14).

---

## 5. 곡 재생 — 지금은 무엇이 곡을 시작하는가

`ChartPlayer.Play()`는 `playOnStart` 인스펙터 불 또는 `ContextMenu`로만 시작된다.
채보는 `GameSession.SelectedChart ?? debugChart`(`ActiveChart`)로 고른다.
`SongSelectManager`는 `SelectedChart`를 채우고 `SceneManager.LoadScene("BattleScene")`을 부를 뿐이다.

곡 시작 직전에 `enemyDirector.PrepareStage()`(프리웜) → `OnCountdownStarted(3초)` → 오디오 재생.
**곡 종료는 `OnSongEnded`** 이고 `DissolveAll()`이 뒤따른다.

즉 **`Play()`를 부르는 주체만 바뀌면 되고 `ChartPlayer` 자체는 한 줄도 안 고쳐도 된다** —
이 단계에서는 그 호출자를 만들지 않는다(다음 단계의 `EncounterDirector`). 대신 **탐색 ↔ 전투 모드를
바꾸는 진입점**만 만들어 두고, 지금은 디버그로 그 진입점을 두드린다.

---

## 6. 정리 — 이 단계가 만들어야 하는 것

1. **위치 소유권의 배타성을 보장하는 단일 지점** (§11-2가 지목한 유일한 위험 지점)
2. **탐색 이동 컨트롤러** (카메라 기준 3인칭, 물리 없음)
3. **입력의 분리** — 탐색 중에 숫자키가 패턴 판정으로 흘러 들어가지 않아야 한다
4. **탐색 로코모션** — base 레이어의 주인을 기존 래치(`convergeUntil`)로 넘겨받는다
5. **탐색 카메라** — 우선순위 교체(§7-5 관용구)

만들지 **않는** 것: `EncounterDirector` · `Encounter` · `GameMode` · 씬 재편 · 콜라이더/충돌 · 상호작용 프롬프트.
