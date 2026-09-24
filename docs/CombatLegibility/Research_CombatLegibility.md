# Research — CombatLegibility (지나가는 적을 벤다 · 전투가 눈에 안 들어온다)

## 보고된 증상

1. **전투 중 "지나가는 적"을 대상으로 공격해서 전투가 이상하다.**
2. **전투가 눈에 안 들어온다.** 사용자 추정 원인: 적 무리 시스템(§11-6).

아래는 두 증상을 코드에서 되짚은 것이다. 결론부터: **둘은 같은 원인의 두 얼굴이고, 무리 시스템 자체가 아니라 "무리가 화면에서 하는 일"이 원인이다.**

---

## 1. 표적은 어떻게 정해지는가 (현재 코드)

| 단계 | 위치 | 하는 일 |
|---|---|---|
| 선정 | `EnemyDirector.TakeTargetForWindow` (1315행) | `desired = cruiseSpeed × 창 / playerShare + duelDistance`에 **가장 가까운 거리의 적**을 `active` 무리에서 고른다 |
| 배정 | `BindReservation` (1585행) | 그 패턴이 판정 대상이 되는 순간 `currentOpponent`에 넣는다(§11-1) |
| 정지 | `EnemyView.AssignAttack` / `AssignFeint` | 첫 줄에서 `StopWander()` — **표적은 실제로 멈춘다** |

즉 **선정 로직은 어긋나 있지 않다.** 표적이 된 적은 배회를 멈추고 `gazeTarget`으로 플레이어를 본다.

### 그런데 왜 "지나가는 적"으로 보이는가

세 가지가 겹친다.

**(a) 표적과 비표적이 화면에서 구분되지 않는다.**
표적이 된 적이 얻는 시각적 사실은 **"멈춤"** 하나뿐이다. 그 신호는 주변 3명이 계속 걷고 있을 때만 읽히는데, 그 3명은 **`standoffDistance` 3.5m 궤도를 `orbitSpeed` 15도/초로 계속 돈다**(`TickWander`, 1138행). 정지가 배경이 아니라 **움직임이 배경**이라 정지는 신호가 되지 못한다.
- `Pattern.EnemyFeint`(§11-4)가 원래 이 자리를 메우려고 만든 슬롯이지만, **`Attacker.Player` + 클립이 배선된 패턴에서만** 재생되고 창이 `minFeintWindow`(0.35초)보다 짧으면 아예 안 건다. 즉 **표적 표시가 패턴 저작 상태에 따라 있다 없다 한다.**
- 아웃라인(`EnemyView.SetHighlight`)은 이미 **기습자 전용**이다(§11-8). 표적에도 쓰면 "공격해 온다"는 뜻이 흐려진다.

**(b) 후보 4명이 반경 2m 안에 뭉쳐 있어 "거리로 고른다"가 무의미해진다.**
`clusterSize` 4 · `clusterRadius` 2 · `standoffDistance` 3.5. `desired`는 창에 비례해 **2~10m로 흔들리는데** 후보는 전부 3.5m 언저리에 있다. `PickTargetByDistance`는 그중 desired에 가장 가까운 하나를 집을 뿐이라, 화면에서는 **뭉친 넷 중 하나가 임의로 뽑히는 것**으로 보인다. §11-6이 제거하려 했던 "거리 이산화"가 무리 안에서 되살아난 셈이다.

**(c) 교전이 시작돼도 나머지가 멈추지 않는다.**
`TickWander`는 `currentOpponent`만 제외하고 나머지 `active` 전원을 계속 돌린다. 결투 구간(마지막 노드 −0.5~2.1초 ~ 임팩트) 내내 **3명이 플레이어와 표적 사이를 가로지른다.** 카메라는 플레이어+상대만 프레이밍하므로(§7-2, `fullFrameDistance` 3m / `dropoffDistance` 6m) 그 3명은 **화면 앞을 스치는 실루엣**이 된다.

> **(a)+(c)가 사용자가 본 그림이다** — 넷이 걸어 다니고, 그중 하나가 갑자기 갈라진다. 어느 쪽이 표적이었는지는 **칼이 닿은 뒤에야 알 수 있다.**

---

## 2. 왜 전투가 눈에 안 들어오는가

### 2-1. 화면에서 움직이는 것이 너무 많다

| 상시 움직이는 것 | 출처 |
|---|---|
| 비표적 적 3명의 궤도 이동 + 방향 뒤집기 | `TickWander`, `orbitReverseInterval` 3~7초 |
| 플레이어 대시(창마다 최대 10m) | §11-2 `TakeTargetForWindow` |
| 카메라 앵글 교체(패턴 경계마다 0.4초 블렌드) | §7-5 `CameraAngleSwitcher` |
| 카메라 yaw 추종(`cameraTurnDamping` 0.45초) | §7-2 |
| 균열 등장 연출(사망 1 : 스폰 1) | §11-11 |
| 콤보 포스트FX · 필름 긁힘 | §12 · §12-1 |

**정작 플레이어가 봐야 하는 것(포커스 링)은 `ScreenSpaceOverlay`라 이 전부와 무관하다.** 즉 화면의 움직임은 **정보를 하나도 안 싣고 시선만 가져간다.**

### 2-2. "정리했다"는 피드백이 없다

`KillOpponent`는 **걷어내기 → 보충 → 승격 → 재보충** 순서이고(§11-6), 사망 1 : 스폰 1이라 죽인 즉시 다음 한 명이 태어난다. 화면 위 인원수는 **항상 `clusterSize × 2`**다. 무리를 다 치우고 다음 무리로 대시하는 리듬은 설계에 있지만(§11-6), **인원수가 줄어드는 구간이 없어 그 리듬이 화면에 안 나타난다.**

### 2-3. 무리 시스템 자체는 범인이 아니다

`clusterEnabled`를 끄면 `PickStagePosition` 폴백으로 돌아가 **적이 무대 전체(반경 8m)에 흩어진다** — 그쪽이 더 안 읽힌다(§11-6이 그래서 만들어졌다). 문제는 "뭉쳐 있다"가 아니라 **뭉친 넷이 전부 같은 일을 하고 있다**는 것이다.

---

## 3. 레퍼런스 분석 — Dead as Disco

리듬 + 3인칭 군중 액션이라 이 프로젝트와 장르가 가장 가깝다. 전투는 "Beat Kune Do"로 불리고, **타겟 전환·연계·카운터 구조를 Batman: Arkham 계보에서 가져왔다**고 개발진이 밝히고 있다.

### 가져올 것

**(A) 동시 위협을 한 창으로 접는다.**
> "you only need to parry one incoming attack at a time, even if multiple enemies swing simultaneously. **The game groups simultaneous hits into a single parry window.**"

여럿이 동시에 휘둘러도 플레이어가 대응할 대상은 **언제나 하나**다. 군중은 밀도를 주고, 입력은 단일 채널로 유지된다.
→ **이 프로젝트는 이미 구조적으로 그렇다**(판정 대상은 언제나 선두 패턴 하나, §3). **가진 장점을 화면이 말해 주지 않고 있을 뿐이다.**

**(B) 이동은 게임이 대신한다.**
Arkham 계보의 핵심 — 플레이어는 "누구를" 만 정하고 접근은 자동이다. 이것도 **이미 구현돼 있다**(`TakeTargetForWindow` + `PlayerCombatMover` 수렴).

**(C) 이 게임이 실제로 실패한 지점을 피한다.**
공략 가이드 두 곳이 같은 말을 한다:
> "The visual chaos — neon particle effects, screen shake, enemies flying around — makes timing feel impossible if you're relying on your eyes."
> "Close your eyes for a few bars if you have to. **The audio cues are way more reliable than anything on screen.**"

**리듬 액션에서 화면 혼잡은 스타일 문제가 아니라 판정 문제**다. 이 게임은 그 대가로 "눈을 감으라"는 공략이 붙었다.
→ 우리 프로젝트는 §12(색수차 규율)·§7-5(Overlay 캔버스)에서 이미 같은 위험을 알고 방어해 왔다. **비표적 적의 상시 이동은 그 규율에서 유일하게 빠져 있는 층이다.**

**(D) 위협의 등급을 적 종류로 가른다.**
Baton Security Guard는 패링 불가 → 회피 전용. **"무엇을 해야 하는가"가 적의 외형에서 읽힌다.**
→ 우리 쪽 대응물은 `Attacker`(Player/Enemy)와 `CountersOnFail`인데, 이것들은 **패턴이 소유**하고 화면에서는 적의 모션으로만 드러난다(견제 vs 공격 와인드업). 적 외형은 전부 같다.

### 가져오지 말 것

- **적을 날려 보내는 넉백·군중 산란**: 우리 결투는 `transform.position` 대입 기반이고 도착 시각 계약(§6)이 있다. 물리적 산란은 그 계약을 깬다.
- **동시 다발 히트 이펙트**: (C)의 실패 지점 그 자체.

---

## 4. 정리 — 손댈 곳

| # | 사실 | 결과 |
|---|---|---|
| 1 | 표적의 유일한 시각 신호가 "멈춤"인데 배경이 움직인다 | 표적이 표적으로 안 보인다 |
| 2 | 교전 중에도 비표적 3명이 계속 돈다 | 화면 혼잡 + 표적 신호 상쇄 |
| 3 | 후보 넷이 2m 안에 뭉쳐 있다 | 거리 기반 선택이 임의 선택처럼 보인다 |
| 4 | 사망 1 : 스폰 1이라 인원이 안 준다 | "정리했다"가 없다 |
| 5 | 표적 표시(`EnemyFeint`)가 패턴 저작 상태에 의존한다 | 있다 없다 한다 |

해결안은 `docs/CombatLegibility/Plan_CombatLegibility.md`.
