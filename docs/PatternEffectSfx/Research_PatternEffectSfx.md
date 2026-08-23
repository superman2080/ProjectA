# Research — 패턴별 이펙트 사운드 (PatternEffectSfx)

패턴마다 다른 임팩트 효과음을 내고 싶다. 지금은 `SfxTrigger.PatternImpact` 하나가 모든 패턴에 공용으로 울린다. 이 문서는 소리를 **어느 데이터가 소유해야 하는가**를 기존 구조에서 찾는다.

---

## 1. 지금 소리가 나는 경로 전부

| 소리 | 발행 지점 | 시각 |
|---|---|---|
| `Perfect`/`Good`/`Miss` | `EffectManager.HandleFocusRingResolved` (`EffectManager.cs:119`) | 판정 즉시 |
| `PatternImpact` | `EffectManager.Update` (예약: `HandlePatternComplete`) | `Deadline + ImpactOffset` |
| `AmbushTelegraph` | `DodgeDirector.Fire` (`:501`) | 텔레그래프 시작 |
| `DodgeSuccess`/`DodgeFail` | `DodgeDirector.Succeed`/`Fail` (`:538`, `:559`) | 판정 즉시 |

전부 **`SfxTrigger` enum 키 → 카탈로그 조회**다(`SfxCatalog.cs`). 재생은 `SfxManager.Play(SfxTrigger)` 하나뿐이고, 보이스 8개 풀에 `spatialBlend = 0`(2D)이다.

**enum 키 방식의 한계가 정확히 이 요구다** — 키는 코드가 정하므로 패턴 수만큼 늘릴 수 없다. `SliceSet`이 enum 카탈로그를 두지 않고 `EnemyCue.projectile`이 직접 참조하는 것과 같은 지점에 도달했다(§11).

---

## 2. 소리를 소유할 후보

### 2-1. `Pattern`에 슬롯 하나 (`impactSfx`)

`Pattern`은 이미 클립을 소유한다 — `PlayerAttack`·`EnemyAttack`·`EnemyDeath` 등 `ClipAlignment` 슬롯 7종. CLAUDE.md가 근거를 명시한다: *"클립은 '모양에 종속된 정적 데이터'라 에셋에 두어도 원칙과 충돌하지 않는다."* 사운드도 같은 성질이다.

**비용이 가장 작다** — 필드 하나 + `SfxManager` 오버로드 + `EffectManager`에서 템플릿 우선.

**문제는 저작 지점이 둘로 갈린다는 것이다.** 아래 2-2의 이펙트 큐가 이미 "임팩트에 무엇이 뜨는가"를 타임라인으로 보여 주는데, 소리만 그 바깥에 산다. "이 패턴의 임팩트에 뭐가 나는가"를 두 화면에서 따로 봐야 한다.

### 2-2. `PatternEffectCue`에 클립 슬롯 (§7-4)

`Pattern.effectCues`(리스트)의 원소가 이미 **"언제 · 어디에 · 어떤 조건에서"**를 스스로 든다(`Pattern/Core/PatternEffectCue.cs:70`).

| 큐가 이미 가진 것 | 소리에 그대로 쓰이는가 |
|---|---|
| `EffectTiming` (PatternStart/FirstNode/Node[i]/LastNode/Impact) + `timeOffset` | O 임팩트뿐 아니라 칼 뽑는 소리·타격 중간음 |
| `EffectCondition` (Always/Success/Parry/Evade) | O **여기가 핵심** — 아래 3절 |
| 리스트 = 개수 자유 | O 한 패턴에 여러 소리 |
| `ResolveTime` 단일 소유 + 툴 타임라인 프리뷰 | O 저작 화면과 게임이 같은 함수를 본다 |
| `anchor`/`follow`/`bladeT`/`scale`/`speed`/`poolSize` | X 소리에 무의미 (2D · 풀 불필요) |

**시각 계산·조건 판정·저작 도구가 전부 이미 있고, 소리가 안 쓰는 필드는 그냥 안 쓰면 된다.**

---

## 3. 조건(`EffectCondition`)이 소리에 더 강하게 맞는다

§7-4의 설계 근거가 그대로, 오히려 더 직접적으로 적용된다:

> 조건은 판정 결과가 아니라 **적의 반응 클립**을 따라간다. 막는 모션이면 스파크가 튀고 뒷구르기면 아무것도 안 튄다 — **칼이 만났느냐**가 화면에 남는 사실이기 때문.

소리는 그 사실이 **가장 직접 들리는 채널**이다:

| 조건 | 화면 | 소리 |
|---|---|---|
| `Success` (`Attacker.Player`) | 벤다 | 촥 — 살을 가르는 소리 |
| `Success` (`Attacker.Enemy`) | 받아친다 | 챙 — 쇠 부딪는 소리 |
| `Parry` | 적이 제자리에서 막았다 | 챙 (막힌 쪽) |
| `Evade` | 적이 물러났다 | **무음** — 큐를 안 만드는 것으로 표현 |

"뒷구르기는 무연출"이 코드 분기가 아니라 **큐를 안 만드는 것**이라는 §7-4의 규율이 소리에도 그대로 성립한다.

---

## 4. 큐에 소리를 넣을 때 건드려야 하는 이음매

### 4-1. `IsUsable` 하나가 모든 게이트의 통로다

```csharp
// PatternEffectCue.cs:145
public bool IsUsable => prefab != null;
```

이 값을 보는 곳이 **전부**다 — 예약(`PatternEffectDirector.cs:153`), 프리웜(`:377`), 툴 목록(`PatternEffectWindow.cs:196`, `:259`, `:599`), 툴 경고(`:505`). **여기만 넓히면 나머지가 자동으로 따라온다.**

### 4-2. ⚠ `Fire`가 앵커부터 본다

```csharp
// PatternEffectDirector.cs:293
private void Fire(PatternEffectCue cue)
{
    Transform anchor = ResolveAnchor(cue.Anchor);
    if (anchor == null) return;   // ← 소리만 있는 큐가 여기서 통째로 죽는다

    var view = pool.Rent(cue.Prefab, cue.PoolSize);
    ...
```

소리는 앵커가 필요 없다(2D). **소리를 먼저 재생하고 그 다음에 앵커를 보게** 순서를 바꿔야 한다.

### 4-3. ⚠ 프리웜은 프리팹이 있을 때만

`:379`의 `pool.Prewarm(cue.Prefab, ...)`이 null을 받으면 안 된다. `IsUsable`을 넓히면 이 줄에 null이 도달한다 — **`IsUsable` 확장이 만드는 유일한 회귀 지점이다.**

### 4-4. ⚠ 히트스톱은 소리를 얼리지 않는다

`:307`의 `if (frozen) view.OverrideSpeed(0f)`는 파티클용이다. 소리에는 걸면 안 된다:

- §7-3이 이미 말한다 — 정지 창은 `Time.timeScale`이 아니라 Animator 배속으로 만든다. **오디오는 원래 그 지배를 받지 않는다.**
- 타격감으로도 **멈추는 그 순간에 울리는 것이 맞다**. 재생 중 `AudioSource`를 멈추면 "정지"가 아니라 "소리가 끊겼다"로 읽힌다(§7-3의 카메라 규율과 같은 결).

### 4-5. `SfxManager`는 클립 재생 경로가 없다

`Play(SfxTrigger)`가 카탈로그를 조회한 뒤 하는 일(`SfxManager.cs:83~90`)은 클립·볼륨·피치만 있으면 되는 순수 절차다. **볼륨 처리를 빠뜨리면 안 된다** — `baseVolumes[index]`에 기록해야 재생 도중 `SoundManager` 볼륨 변경이 반영된다(`HandleVolumeChanged:66`).

### 4-6. ⚠ 방금 넣은 `PatternImpact` 폴백과 겹친다

`EffectManager`가 성공 시 `Deadline + ImpactOffset`에 `SfxTrigger.PatternImpact`를 울린다(방금 구현). 패턴이 임팩트 사운드 큐를 저작하면 **둘 다 울린다.**

폴백을 없애면 큐 없는 패턴이 무음이 되므로 남겨야 하고, 그러면 **"큐가 있으면 폴백을 안 낸다"** 규칙이 필요하다. 없으면 소리를 저작할 때마다 조용히 겹치고, 저작자는 원인을 짚기 어렵다(둘 다 임팩트 시각이라 한 소리로 들린다).

### 4-7. 툴 표시

- `:273` `EstimateDuration`이 `cue.Prefab == null`이면 0을 돌려준다 → 소리 전용 큐는 타임라인에 **길이 0 막대**로 뜬다. 클립 길이를 쓰면 된다.
- `:521` "프리팹에 ParticleSystem이 없습니다" 경고가 소리 전용 큐에서 오탐이 된다.

---

## 5. 제약 정리

- 소리는 **2D 유지**(`spatialBlend = 0`). 임팩트음이 앵커 위치를 따라 좌우로 흔들리면 판정 단서가 흐려진다 — 리듬게임에서 그건 손해다.
- 풀이 필요 없다. `SfxManager`의 보이스 8개가 이미 동시 재생을 감당한다.
- `Pattern`은 `Pattern/Core`가 아니라 `PatternSpace`이고 `PatternEffectCue`는 `Pattern/Core`(asmdef)다 — **`AudioClip`은 `UnityEngine` 타입이라 asmdef 의존이 늘지 않는다.**
- 기존 채보 회귀 0: 큐에 클립을 안 넣으면 `IsUsable`도 `Fire`도 예전 그대로다.
