# Plan — HitStop

근거: `docs/HitStop/Research_HitStop.md`

## 목표

**패턴 성공 시 임팩트 프레임에 히트스톱.**

1. **A — 애니메이터 프리즈**: 플레이어 공격 클립과 죽는 적의 사망 클립이 `hitStopDuration`만큼 멈춘다.
2. **B — 카메라 펀치**: 같은 순간 FOV가 짧게 튀었다 돌아온다.
3. **캐치업**: 정지로 생긴 공백은 해제 후 배속으로 흡수해 **원래 예정된 절대 시각(`actionEndTime`·`burstTime`)을 지킨다.**

**판정·오디오·포커스 링·이동은 절대 멈추지 않는다.** `Time.timeScale`은 쓰지 않는다(Research 1).

---

## 설계 결정

### D-1. `Time.timeScale` 금지 — 애니메이터 배속만 0으로

Research 1. 시계를 건드리면 `audioSource.time`과 `Time.time`이 갈라져 **판정이 영구 드리프트**한다. 히트스톱 한 번(0.05~0.10초)이 `perfectWindow`(0.05초)를 넘는다.

멈추는 것은 **Animator의 Speed Multiplier 파라미터**뿐이다 — `AttackSpeed`(플레이어), `DeathSpeed`(적). 이건 이미 두 클래스가 소유·조작하는 값이라 새 개입 지점이 아니다.

**확정.**

### D-2. 소유자는 신규 `HitStopDirector` 하나

임팩트 시각을 아는 곳은 넷이지만(Research 2), **"언제·얼마나 멈출지"의 진실의 원천은 하나여야 한다.** 세 배우가 각자 타이머를 들면 인스펙터에 같은 값이 세 번 적히고, 언젠가 하나만 고쳐진다(§7-2의 `BlendTime` 교훈과 같은 부류).

`HitStopDirector`는 `EffectManager`·`CameraDirector`와 **같은 자리·같은 규율**이다:
- `PatternHandler.OnPatternComplete`만 구독하는 **순수 연출**. 판정에 개입하지 않는다.
- `info.AllCorrect`가 참일 때만, `fireTime = LastNodeTime + GoodWindow + Pattern.ImpactOffset`에 예약.
- 예약은 **최대 하나**(`CameraDirector`와 같은 근거 — 완료가 순차적이고 최소 0.4초 간격).
- 배우 참조(`CharacterActionPlayer`, `EnemyDirector`)가 **비면 그 배우만 조용히 빠진다.**

**⚠ `CameraDirector`에 얹지 않는다.** 그쪽은 "카메라 연출의 유일 관리 지점"이라 헌장이 명확하고, 애니메이터를 만지기 시작하면 그 경계가 무너진다. 반대로 **B(카메라 펀치)는 `CameraDirector`가 한다** — 히트스톱 디렉터가 카메라를 만지지 않는다. **각자 자기 층에 남는다.**

새 파일: `Assets/02. Scripts/HitStop/HitStopDirector.cs` (전역 네임스페이스, 기존 디렉터들과 동일).

**확정.**

### D-3′. ⚠ 개정 — 플레이어는 **밀기**, 적만 캐치업 (구현 후 실측으로 뒤집힘)

D-3/D-4는 두 배우 모두 캐치업을 전제했다. **구현 후 에셋 전수 측정으로 그 전제가 플레이어에서 깨졌다.**

| 배우 | 임팩트 위치 | 임팩트 이후 잔여(실측) | 캐치업 가능? |
|---|---|---|---|
| 플레이어 공격 | 트림 **끝** 근처 | **0.036~0.109초 (10/10 패턴)** | **불가** |
| 적 사망 | 트림 **시작** 근처(§11-3) | 0.245~0.953초 (클립 있는 5개) | 가능 |

정지 0.08초가 플레이어 잔여보다 길다 → **재개하는 순간 이미 원래 종료 시각이 지나 있어 압축할 시간이 음수**다. D-4의 "여유가 거의 없다"는 실제로는 **언제나 0**이었고, 사전 가드(3배)에 10/10이 걸려 **플레이어는 한 번도 안 멈췄다.**

**개정**: 플레이어는 `actionEndTime`·`recoveryEndTime`을 정지 시간만큼 **민다**. 클립은 멈춘 자리에서 원래 배속으로 이어진다.
- **임팩트는 이미 지나간 뒤라 §6의 정렬은 안 깨진다** — 미는 것은 마무리 동작과 복귀뿐이다.
- 다음 공격은 **자기 Deadline에서 독립 예약**이라 제시각에 그대로 시작한다. 실제 비용은 그 사이 Sprint 노출 −D뿐(`minRunExposure` 0.35초 대비 여유).
- 그래서 플레이어 쪽은 `maxCatchupSpeed`·`minCatchupHeadroom`을 **쓰지 않는다**(적 전용 값이 됐다). 가드도 없어 **항상 멈춘다**.

**반려한 대안**: ① *공격 클립 배속을 올려 여유를 만든다* — 거꾸로다. 잔여 실시간 = `(트림−임팩트span)/배속`이라 배속이 분모다, 빨릴수록 여유가 **준다**. ② *트림 `duration`을 늘려 잔여를 만든다* — 클립마다 미사용 꼬리가 0.46~3.52초 남아 있어 가능하지만, 트림 끝은 §6에서 "복귀 시작점 + `AttackSpeed`를 1로 되돌리는 지점"이라 **연장 구간이 배속 걸린 채 재생되고 복귀가 늦어진다**. 저작 10건 대비 이득이 밀기와 같아 보류.

**확정.** 아래 D-3/D-4는 **적에게만** 유효하다.

### D-3. 캐치업 — 원래 종료 시각을 목표로 배속 역산 (⚠ 적 전용, D-3′ 참조)

```
남은클립초 R = playDur − (freezeTime − playStartTime) × playSpeed
남은실시간 T = originalEndTime − (freezeTime + hitStopDuration)
캐치업배속   = R / T
```

`ClipAlignment.ResolvePlaySpeed`(`ResolvedImpactSpan / remaining`)와 **같은 꼴**이다 — 새 개념이 아니라 이미 이 코드베이스가 쓰는 정렬 문법 그대로다.

이렇게 하면 **다운스트림 스케줄이 하나도 안 밀린다** — `recoveryEndTime`, 연계 판정(`IsLinkedToNextAction`), `burstTime`, `OnEnemyBurst`, 카메라 쉐이크 예약이 전부 원래 시각에 그대로 온다. 요구의 "빈 공백은 빠르게 배속해서 없앤다"가 이것이다.

**확정.**

### D-4. ⚠ 캐치업 상한과 **건너뛰기 가드**

`T → 0`이면 배속이 폭발한다. 둘로 막는다:

**① 사전 가드 — 여유가 없으면 그 배우는 아예 멈추지 않는다.**
`originalEndTime − freezeTime < hitStopDuration × minCatchupHeadroom`(기본 3배)이면 **건너뛴다.** 멈췄다가 8배속으로 튀는 것보다 안 멈추는 게 낫다.

**② 사후 상한 — 걸리면 종료 시각을 민다.**
캐치업 배속을 `maxCatchupSpeed`(기본 3.0)로 클램프하고, 그래도 모자라면 **`actionEndTime`/`burstTime`을 부족분만큼 뒤로 민다.** 안 밀면 클립이 중간에 잘린 채 다음 단계로 넘어간다. **판정은 어차피 무관하므로 연출이 조금 늦는 쪽이 옳은 열화다.**

두 배우의 여유는 크게 다르다 — 적 사망은 임팩트가 트림 시작 근처라(§11-3) 잔여가 클립 대부분이고, 플레이어는 `maxAttackSpeed` 2.5에 이미 걸린 패턴이면 여유가 거의 없다. **그래서 가드는 배우별로 각자 건다.** 한쪽만 멈춰도 타격감은 성립한다.

**확정.**

### D-5. 성공에서만 발동

요구 그대로. 실패는 표적이 **부딪혀 소멸**하는 것이라 멈출 임팩트 프레임이 없고, 피격(`OnPlayerHit`)은 플레이어가 맞는 순간이라 멈추면 리듬이 아니라 렉으로 읽힌다.

**확정.**

### D-6. B는 `CameraCueEntry`에 필드 두 개 — "연출 추가 = 카탈로그에 한 줄"

`CameraCueEntry`에 `punchFovDelta`(도, 음수면 줌인) + `punchDuration`(초) 추가. 기존 큐는 값이 0이라 **무연출로 그대로 동작한다.**

- 감쇠는 쉐이크와 같은 공식 `(1−t)²`를 재사용한다.
- 적용 지점은 **새 이음매 `ApplyPunch`** — `ApplyShake`/`ApplyFraming`/`IntroRoutine`에 이어 Cinemachine이 등장하는 **네 번째이자 마지막** 봉인 지점. 클래스 주석의 "세 이음매"를 갱신한다.
- **`Lens.FieldOfView`에만 건다.** `FollowOffset`(돌리)을 건드리면 `GroupFraming`이 매 프레임 돌리를 계산하므로 둘이 싸운다. `SizeAdjustment = DollyOnly`의 "Zoom 금지"는 **상시 프레이밍**에 대한 경고지 0.1초 전환에 대한 것이 아니다(Research 5).
- 휴지 FOV는 `Awake`에서 캐시한다 — 쉐이크의 `idleAmplitude`와 같은 규율(씬 값이 진실의 원천).
- ⚠ 인트로 vcam은 별개 vcam이라 무관하다.

**확정.**

### D-9. 정지 창 동안 카메라를 **잠그고**, 큐는 **해제 뒤에** 낸다

요구: "쉐이크도 잠깐 끈 후 카메라 락 걸어 못 움직이게 한 다음, 그 후 카메라 연출."

원래 `PatternSuccess` 큐는 임팩트(= 정지 시작)와 **같은 시각**에 터졌다. 그러면 멈추는 바로 그 순간 화면이 흔들려 **"멈췄다"가 아니라 "끊겼다"로 읽힌다.** 정지의 무게는 화면이 **완전히 정지**해야 생긴다.

**잠금은 `CinemachineBrain.enabled = false`다.** 프레이밍 갱신만 멈추는 걸로는 부족하다 — `PositionDamping`이 1.0이라 목표가 고정돼도 감쇠가 남은 오차를 계속 따라가 카메라는 여전히 흐른다. Brain을 끄면 카메라 Transform이 **마지막 값에 굳는다**. Brain 미배선이면 프레이밍·쉐이크 정지만으로 **부분 잠금**이 되고 나머지는 그대로 동작한다(기존 규율).

**큐는 버리지 않고 미룬다.** `Update` 실행 순서가 보장되지 않아 `CameraDirector`가 먼저 돌면 큐가 이미 시작된 뒤에 잠금이 들어온다 → 그 경우 진행 중인 큐의 트리거를 지연 큐로 **옮긴다**. 반대 순서면 `PlayCue`가 잠금을 보고 바로 미룬다. **어느 순서로 돌든 결과가 같다.**

⚠ `OnDisable`에서 반드시 Brain을 되살린다 — 잠금 도중 컴포넌트가 꺼지면 **카메라가 영구히 굳는다.**

`HitStopDirector`는 정지 창을 **알려 주기만** 한다(D-2의 층 분리 유지). 정지 시간이 한 값이어야 배우와 카메라가 같은 창에서 멈추므로 원천은 계속 `hitStopDuration` 하나다.

**확정.**

### D-8. 연출은 전부 인스펙터 bool로 끌 수 있다

"이런 연출들은 설정값으로 끌 수 있게" 요구. **중앙 설정 오브젝트를 만들지 않는다** — 각 디렉터가 이미 자기 층의 유일 관리 지점이라, 토글도 거기 있는 게 맞고 배선 누락 시 조용히 비활성되는 기존 규율과 결이 같다.

| 토글 | 소유자 | 끄면 |
|---|---|---|
| `hitStopEnabled` | `HitStopDirector` | 예약 자체를 안 잡는다 |
| `shakeEnabled` | `CameraDirector` | Perlin이 씬 휴지값 그대로 |
| `punchEnabled` | `CameraDirector` | 렌즈가 씬 값 그대로 |
| `framingEnabledOption` | `CameraDirector` | 그룹을 안 건드린다(등 뒤 추적 포함) |
| `introEnabled` | `CameraDirector` | 인트로 vcam이 안 뜬다 |

⚠ **저장·UI는 범위 밖**이다. 지금은 인스펙터 값이고, 나중에 옵션 화면이 생기면 이 bool들을 그쪽에서 쓰면 된다 — 지금 `PlayerPrefs`나 설정 SO를 만들면 쓰는 데가 없는 배관이다.

**확정.**

### D-7. 표적 조각(`SlicePiece`)은 이번 범위 밖

닫힌 식 기반이라 정지시키려면 `t` 오프셋이라는 **별도 메커니즘**이 필요하다(Research 3-3). 애니메이터 프리즈와 성격이 다르다.

⚠ **이음매가 남는다** — 표적 절단은 임팩트 바로 그 순간이라, 캐릭터가 멈춘 0.08초 동안 조각만 날아간다. 적 사망 폭발(`burstTime`)은 트림 끝이라 훨씬 뒤여서 무관하다.

`hitStopDuration`이 0.08초 남짓이면 실사용에서 눈에 띄기 어렵다고 본다. **먼저 A+B만 넣고 눈에 띄면 그때 조각을 붙인다** — 안 띄면 안 짜도 되는 코드다.

**확정.**

---

## 구현 단계

- [x] **Step 1 — `CharacterActionPlayer`에 재생 스냅샷 보관**
  `PlaySlot`이 `playStartTime`(= `Time.time`), `playSpeed`, `playDur`를 필드에 남긴다. 지금은 `actionEndTime`만 남아 캐치업 역산이 불가능하다(Research 3-1).

- [x] **Step 2 — `CharacterActionPlayer.ApplyHitStop(float duration, float maxSpeed, float headroom) : bool`**
  1. 스윙 재생 중이 아니면 `false`(멈출 게 없다).
  2. 사전 가드(D-4 ①) 미달이면 `false`.
  3. `AttackSpeed = 0`, 해제 시각 래치. `actionEndTime`은 **건드리지 않는다**(캐치업이 지킬 목표).
  4. `Update` 맨 앞에서 해제 시각 도달 시 캐치업 배속 대입. 상한에 걸리면 `actionEndTime`·`recoveryEndTime`을 부족분만큼 민다(D-4 ②).
  5. **프리즈 중에는 `Update`의 복귀 로직을 통과시키지 않는다** — `Time.time >= actionEndTime` 분기가 프리즈 중에 열리면 배속이 1로 복원되어 캐치업이 무산된다.

- [x] **Step 3 — `EnemyView.ApplyHitStop(...) : float`** (새 `burstTime`을 반환, 변화 없으면 원래 값)
  같은 구조. `Phase.Dying`이 아니거나 여유 미달이면 원래 `burstTime` 그대로 반환. `DeathSpeed = 0` → 캐치업.

- [x] **Step 4 — `EnemyDirector.ApplyHitStop(...)`**
  `pendingKills`에서 아직 안 터진 항목을 훑어 `EnemyView.ApplyHitStop`을 호출하고 **반환값으로 `pending.burstTime`을 갱신**한다. 배속은 뷰가 알고 시각은 디렉터가 드는 구조라 **둘을 같이 갱신하지 않으면 갈라진다**(Research 3-2).

- [x] **Step 5 — `HitStopDirector` 신규**
  `Assets/02. Scripts/HitStop/HitStopDirector.cs`. 필드: `handler`, `actionPlayer`, `enemyDirector`, `hitStopDuration`(0.08), `maxCatchupSpeed`(3.0), `minCatchupHeadroom`(3.0), `enabled` 토글.
  `OnPatternComplete` 구독 → `AllCorrect`일 때만 `fireTime`에 예약(최대 하나) → `Update`에서 도달 시 두 배우에게 지시. `OnAllPatternsCleared`에서 예약 파기.

- [x] **Step 6 — `CameraCueEntry` + `CameraDirector.ApplyPunch` (D-6)**
  `punchFovDelta`·`punchDuration` 필드 추가, `Awake`에서 휴지 FOV 캐시, `UpdatePunch`가 `(1−t)²` 감쇠로 `ApplyPunch` 호출. `PlayCue`에서 쉐이크와 함께 시작(진폭 겹침 규칙도 동일하게 큰 쪽 취함).

- [x] **Step 7 — 씬 배선 + 카탈로그 값**
  `BattleScene`에 `HitStopDirector` 배치(`Main Camera` 또는 연출 오브젝트), 참조 셋 연결. `CameraDirector` 카탈로그의 `PatternSuccess`·`EnemyKilled` 행에 `punchFovDelta` −4 / `punchDuration` 0.12 정도로 시작. 씬 저장.

- [x] **Step 8 — 문서 갱신**
  `CLAUDE.md`에 §12 HitStop 추가 — **`Time.timeScale` 금지 이유**(78곳이 `Time.time`, 오디오는 실시간), 캐치업 식, 가드 둘, 조각 이음매(D-7). `CameraCueCatalog.cs:9`의 "히트스톱을 못 쓰므로" 주석도 갱신(못 쓰는 건 `timeScale`이지 히트스톱이 아니다).

- [x] **Step 9a — 정적 검증 (완료)**
  - 컴파일 에러 0.
  - 씬 배선: `HitStopDirector`가 `Main Camera`에(= `CameraDirector`와 같은 오브젝트), `handler`/`actionPlayer`/`enemyDirector` 셋 다 연결. `duration 0.08 / maxCatchup 3 / headroom 3 / enabled True`.
  - `CameraDirector.gameplayCamera` = `CinemachineCamera`(FOV 40) 연결.
  - 카탈로그 4행: `PatternSuccess` shake 1.2/0.18 + punch −4/0.12 · `PatternMiss` 0.8/0.22 · `PatternFailure` 1/0.2 · **`EnemyKilled` punch −3/0.14 (신규 행)**.

  ⚠ **발견**: `EnemyKilled` 행이 카탈로그에 **아예 없었다** — CLAUDE.md는 이걸 "무쌍 타격감의 주 큐"라 적어 뒀는데 씬에는 무연출이었다. 펀치만 넣은 행을 추가했고 **쉐이크는 0으로 뒀다**(기존 동작을 바꾸지 않기 위해). 쉐이크도 원하면 진폭을 올리면 된다.

- [ ] **Step 9b — 플레이 검증 (사용자 확인 필요)**
  1. **판정이 안 흔들린다** — 오토플레이(디버그 §8)를 켜고 곡 끝까지 돌려 Perfect가 유지되는지. 히트스톱이 판정에 샜다면 여기서 즉시 Miss가 난다. **이게 가장 중요한 항목이다.**
  2. 성공 시 임팩트에 멈칫 + FOV 펀치가 보인다.
  3. **연계가 안 밀린다** — 촘촘한 구간에서 다음 공격이 제때 시작되는지(캐치업이 `actionEndTime`을 지켰는지).
  4. 적이 원래 시각에 갈라진다(`burstTime` 유지).
  5. 실패·피격에서는 안 멈춘다.
  6. 조각 이음매(D-7)가 눈에 띄는지.

  **튜닝 지점**: 약하면 `hitStopDuration`↑(0.10~0.12). 캐치업이 튀어 보이면 `maxCatchupSpeed`↓ 또는 `minCatchupHeadroom`↑(더 자주 건너뜀). 펀치는 카탈로그 `punchFovDelta`/`punchDuration`.

---

## 범위 밖

- `SlicePiece` 프리즈 (D-7 — 눈에 띄면 그때).
- 히트스톱 중 슬로우모션(0이 아닌 저배속). 0이 더 강하고 코드가 같다.
- 판정 등급별 강도 차등(Perfect만 더 세게 등). 카탈로그가 이미 트리거별로 갈리므로 필요해지면 그때 트리거를 늘린다.
