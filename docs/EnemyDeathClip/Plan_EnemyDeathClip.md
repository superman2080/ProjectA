# Plan — EnemyDeathClip

근거: `docs/EnemyDeathClip/Research_EnemyDeathClip.md`

## 목표

1. `Pattern.EnemyDeath`를 **런타임에 재생**한다. 임팩트 프레임이 플레이어 공격 임팩트와 같은 절대 시각에 오도록 **배속을 역산**한다.
2. **절단(시체 교체 + 폭발)을 클립 끝으로** 미룬다.

바뀌는 그림:

```
지금   L ──────── impact ─╂─ 즉시 절단
                          (맞는 순간 = 터지는 순간)

이후   L ──────── impact ───── 쓰러지는 연출 ───── T_end ─╂─ 절단
                  ↑ 플레이어 칼 · 사망 클립 임팩트 프레임
```

## 설계 결정

### D-1. 공유하는 것은 **임팩트 순간 하나뿐**이다 — 배속·시작·끝은 배우마다 다르다

두 클립의 배속은 **원리적으로 같아질 이유가 없다**(Research 6-6). 클립 길이가 다르고 압축을 유발하는 제약도 다르다 — 플레이어는 다음 패턴까지의 여유(`maxAttackSpeed`), 적은 확정~임팩트 간격.

그래서 **클립 시작이나 끝을 맞추는 정렬은 성립하지 않는다.** 배속이 다르면 시작을 맞춰도 임팩트가 어긋나고, 끝을 맞춰도 마찬가지다.

> **공유 가능한 시각은 임팩트 하나뿐이다.**
> 각 배우는 `impactTime = Deadline + Pattern.ImpactOffset`이라는 **같은 절대 시각**에
> **자기 클립의 임팩트 프레임**이 오도록 **자기 시작 시점과 자기 배속을 역산**한다.
> 시작·끝·배속이 서로 달라도 칼이 닿는 순간은 구조적으로 일치한다.

`ClipAlignment`가 이미 그렇게 설계돼 있다 — `ResolveScheduleStart`/`ResolvePlaySpeed`가 전부 `impactAlignTime`을 받는다. **코드는 맞는데 문서가 없어서** 이번에 규칙으로 못박는다(Step 7).

`impactTime`은 플레이어 칼 · 표적 절단 · 시체 교체 · 투사체 · 카메라 큐가 **이미 전부 읽는 값**이다. 사망 클립은 그 목록의 하나가 더 늘어나는 것이고, **하나만 옮기고 다른 쪽을 안 옮겨 어긋나는 일이 구조적으로 불가능하다.**

>>> 

### D-2. 재생은 `Attack` 슬롯이 아니라 **전용 `Death` 오버라이드 슬롯**으로

`EnemyAnimator`의 `Death` 스테이트가 지금 고정 클립 `Dead`를 물고 있다(Research 4). `Attack`처럼 **placeholder를 두고 오버라이드**한다.

**`Attack` 슬롯을 재활용하지 않는다.** `Attacker.Enemy` 패턴에서는 적이 공격 중에 죽을 수 있고, 그때 같은 슬롯을 덮으면 진행 중인 공격 클립이 교체돼 화면이 튄다. 슬롯이 갈리면 그 상황이 표현 불가능해진다.

`enemyDeath`가 비면 **기존 `Dead` 클립으로 폴백**한다 — 지금 패턴 에셋 대부분이 이 슬롯을 안 채웠으므로, 안 채운 채로도 전보다 나아야 한다.

>>> 

### D-3. 재생 시작은 **처치 확정(L) 즉시**, 배속으로 임팩트를 맞춘다

`ResolveScheduleStart`는 `earliest`로 클램프되는데 처치 확정보다 이른 시각은 **알 수가 없다**(성패가 그때 정해진다). 그래서 확정 순간 바로 시작하고 `ResolvePlaySpeed(now, impactTime, maxAttackSpeed)`로 배속을 역산한다 — `TryStartAttack`과 같은 처리다.

**주어지는 실시간은 `goodWindow`(0.1초) + `impactOffset`뿐이다**(Research 6-1). 사망 클립의 `ImpactTime`은 **트림 시작 근처**에 찍어야 한다. 죽는 모션은 원래 "맞는 순간"이 거의 시작점이라 자연스럽게 맞는다. 상한에 걸리면 기존과 같은 경고를 낸다.

**`ImpactTime` 위치가 쓰러지는 속도까지 정한다**(Research 6-7). `ResolvePlaySpeed`는 *임팩트 이전* 구간을 남은 시간에 맞추려 배속을 올리는데, 그 배속은 Animator의 Speed Multiplier라 **클립 전체**에 걸린다:

```
ImpactSpan 0.05초 → speed ≈ 1 → 쓰러짐 자연 속도
ImpactSpan 0.30초 → speed = 3 → 쓰러짐도 3배속, T_end도 1/3
```

**"임팩트를 앞에 찍어라"는 정렬 요구이자 연출 요구다.** 뒤에 찍으면 정렬이 깨지기 전에 쓰러짐부터 빨라진다. Step 8의 검증 항목에 이 증상을 넣는다.

>>> 

### D-4. 절단 시각은 **클립 끝**이고, 그 값은 재생을 시작한 쪽이 계산한다

`T_end = 재생시작 + ResolvedDuration / speed`

**여기 들어가는 `speed`는 적의 것이다.** 플레이어의 배속과 무관하다(D-1) — 절단은 **사망 클립을 따라간다**. 플레이어의 마무리 동작이 그보다 먼저 끝나든 뒤에 남든 상관없고, 남는 게 정상이다(§6의 "임팩트 이후 잔여 트림 구간은 이어 재생").

배속은 재생 시작 순간에야 정해지므로 **`EnemyView`가 계산해 돌려준다.** 디렉터가 따로 추정하면 두 값이 갈라진다.

`PendingKill.impactTime` → **`burstTime`**으로 의미가 바뀐다. `TickPendingKills`의 폴링 구조는 그대로다.

클립이 없거나(`enemyDeath` 미배선) 사용 불가면 `burstTime = impactTime` — **지금 동작 그대로**다. 기존 패턴이 안 깨진다.

>>> 

### D-5. 죽는 적은 결투 위치에서 **비켜준다**

Research 6-2 — 승격은 확정 시점에 즉시 일어나므로, 다음 적이 시체가 서 있는 자리로 걸어 들어온다.

죽는 순간 `deathClearOffset`(기본 0.6m)만큼 **뒤로 밀어** 자리를 비운다. `Resolve`의 후퇴와 같은 방식(`ScheduleMove`)이고 클립 길이에 맞춰 천천히 간다 — 넘어지는 관성으로 읽힌다.

**루트 모션이 있는 사망 클립이면 이게 이중이 된다.** 0으로 두면 꺼진다.

>>> 

### D-6. `OnEnemyKilled`는 **확정 시점 그대로**, 카메라 큐만 절단으로 옮긴다

`OnEnemyKilled`는 "이 적은 죽는다"는 **사실**의 통지다. 승격·링 보충과 같은 시점이어야 하고, 이걸 미루면 그 셋이 갈라진다.

반면 카메라 쉐이크는 **화면에서 사건이 일어나는 순간**에 맞춰야 한다(CLAUDE.md §7-1의 규율). 절단이 뒤로 밀렸으니 쉐이크도 같이 밀린다.

→ 새 이벤트 `OnEnemyBurst(EnemyView)`를 절단 시점에 발행하고 `CameraDirector`가 `EnemyKilled` 큐를 **이쪽으로 옮겨** 구독한다.

>>> 

### D-7. 굽기 포즈를 **트림 끝**으로 바꾼다

Research 6-3 — 슬라이서가 `death.ImpactTime` 포즈로 굽는데, 이제 터지는 순간은 **트림 끝**이다.

정합성 요구는 아니지만(스키닝 보존) 가까울수록 관절 뒤틀림이 준다. **터지는 순간의 포즈로 굽는 게 맞다.**

`MeshSliceBakerWindow`가 샘플링하는 시각을 `ImpactTime` → `StartOffset + ResolvedDuration`으로 바꾸고, 표시 라벨도 같이 고친다.

>>> 

### D-8. 산 적을 오래 붙잡는 비용은 감수한다

Research 6-4 — 반납이 클립 길이만큼 늦어진다. 동시 활성 = 죽는 중 1~2 + 링 `ringCount`(6) + 상대. **한 자릿수 증가**라 프리웜된 풀로 감당된다.

곡 정리(`HandleAllCleared`)에서 **미절단 예약을 즉시 실행**해 회수한다 — 지금도 `pendingKills`를 비우지만, 그러면 산 적이 반납되지 않은 채 버려진다.

>>> 

---

## 단계

- [x] **Step 1 — `EnemyAnimator`에 `Death` 오버라이드 슬롯 (D-2)**
  `Death` 스테이트의 모션을 `DeathSlot_Placeholder`(전용 placeholder 클립)로 바꾸고, `DeathSpeed` 파라미터를 Speed Multiplier에 연결.
  기존 `Dead` 클립은 폴백으로 `EnemyView`가 인스펙터에 들고 있는다.

- [x] **Step 2 — `EnemyView.AssignDeath` 신설 (D-1, D-3, D-4)**
  `public float AssignDeath(ClipAlignment death, float impactTime, float maxSpeed)`
  - 반환값 = **절단 시각**(`T_end`). 클립이 없으면 `impactTime`을 그대로 돌려준다(D-4 폴백)
  - `Current = Phase.Dying`, 이동·시선·대기 공격 전부 중단
  - `ResolvePlaySpeed(now, impactTime, maxSpeed, out clamped)` → 상한에 걸리면 경고(문구는 공격 경로와 같은 형태)
  - `PlayDeath(death, speed)` — `PlayAttack`과 같은 구조. **`fixedTimeOffset`에 `StartOffset / speed`를 넘기는 함정을 그대로 밟는다**(Research 4)
  - `deathClearOffset`만큼 뒤로 `ScheduleMove`, 종료 시각 = `T_end` (D-5)

- [x] **Step 3 — `PendingKill.impactTime` → `burstTime` (D-4)**
  `KillOpponent`가 `opponent.AssignDeath(...)`를 부르고 그 반환값을 `burstTime`으로 넣는다.
  `TickPendingKills` 비교 대상만 바뀐다. `ExecuteKill` 본문은 그대로.
  사망 클립은 `Pattern.EnemyDeath`에서 온다 — `Reservation.template`을 `KillOpponent`까지 넘겨야 한다.

- [x] **Step 4 — `OnEnemyBurst` 이벤트 + 카메라 큐 이동 (D-6)**
  `EnemyDirector`에 `public event Action<EnemyView> OnEnemyBurst;` 추가, `ExecuteKill`에서 발행.
  `CameraDirector`가 `OnEnemyKilled` 대신 이걸 구독한다(`EnemyKilled` 트리거 이름은 유지 — 카탈로그 재배선을 만들지 않는다).

- [x] **Step 5 — 곡 정리에서 미절단 예약 즉시 실행 (D-8)**
  `HandleAllCleared`가 이미 `pendingKills`를 순회하며 `ReleaseEnemy`로 산 적을 반납하고 있다. 변경 불필요.

- [x] **Step 6 — 굽기 포즈를 트림 끝으로 (D-7)**
  `MeshSliceBakerWindow`의 포즈 샘플링 시각과 `DrawPoseInfo` 라벨을 `StartOffset + ResolvedDuration`으로.

- [x] **Step 7 — 정렬 규칙을 문서로 못박고 툴팁 갱신**
  - **`CLAUDE.md` §6에 "임팩트 정렬" 규칙 신설**(D-1) — *공유하는 것은 임팩트 순간 하나뿐이고 시작·끝·배속은 배우마다 다르다*. 지금은 플레이어 단독 정렬만 적혀 있어(`:179`) 두 배우를 맞추는 근거가 어디에도 없다.
  - `ClipAlignment` 타입 주석에도 같은 문장 한 줄(코드를 읽는 사람이 먼저 만나는 곳이다).
  - `enemyDeath` 툴팁에서 "저작 보조" 문구 제거 → **재생되는 클립**임을 명시 + `ImpactTime`을 트림 시작 근처에 찍으라는 이유(D-3).
  - `PatternEditor`의 역할별 슬롯 숨김 규칙 확인(`enemyDeath`는 `Attacker.Player` 쪽에 남아야 한다)
  - `docs/!Guides/Guide_EnemyCombat.md`

- [ ] **Step 8 — 검증(플레이)**
  - 플레이어 칼이 지나가는 순간 적이 **맞는 반응**을 보인다(임팩트 정렬)
  - 적이 **쓰러지는 것을 다 보고 나서** 갈라진다
  - **두 클립의 배속이 달라도 임팩트가 맞물린다**(D-1) — 프레임 단위로 확인. 저작 배속을 일부러 다르게 준 패턴으로 시험
  - 배속 상한 경고가 안 뜬다 — 뜨면 클립의 `ImpactTime`을 트림 시작 쪽으로 당긴다(D-3)
  - **쓰러지는 속도가 부자연스럽게 빠르지 않다** — 빠르면 `ImpactTime`이 뒤에 찍힌 것이다(Research 6-7). 정렬이 깨지기 전에 이 증상이 먼저 나온다
  - 다음 적이 **시체를 뚫고 서지 않는다**(D-5 `deathClearOffset` 조정)
  - 카메라 쉐이크가 **갈라지는 순간**에 온다(D-6)
  - `enemyDeath` 미배선 패턴이 **전과 똑같이** 동작한다(D-4 폴백)
  - 곡 중단 시 죽던 적이 남지 않는다(D-8)
  - 연속 처치에서 시체·산 적이 쌓이지 않는다

## 범위 밖

- **노드 단위 피격 반응**(패턴 진행 중 맞는 연출) — 별건. `OnJudged` 구독자가 아직 0이고 애니메이터에 피격 스테이트도 없다.
- 래그돌(`docs/EnemyRagdoll/`)
- `Attacker.Enemy` 패턴 저작
