# Research: 카메라 연출 (CameraDirection)

## 목표

패턴 성공 시 짧은 카메라 쉐이크를 넣는다. 다만 쉐이크는 **첫 번째 연출일 뿐**이고,
이후 카메라 워킹(줌·이동·앵글 전환 등)이 코드 수정 없이 얹힐 수 있는 뼈대를 세우는 것이 진짜 목표다.

---

## 현재 구현 분석

### 1. 씬의 카메라 구성

| 오브젝트 | 구성 |
|---|---|
| `Main Camera` | `CinemachineBrain`, FOV 25, depth −1 |
| `CinemachineCamera` | **live**, priority 0, follow/lookAt **없음**, body/aim **없음**, extensions **없음**, noise = **`CinemachineBasicMultiChannelPerlin`** |

- **Cinemachine 3.1.7**이 설치되어 있다(`com.unity.cinemachine`).
- vcam이 **한 대뿐**이고 follow/aim이 없다 = 고정 앵글. 카메라 워킹이 붙으면 vcam이 늘거나 body/aim이 채워진다.
- **`CinemachineBasicMultiChannelPerlin`이 이미 붙어 있다.** 이것은 '상시 흔들림(handheld)' 컴포넌트로,
  `AmplitudeGain` / `FrequencyGain`을 코드로 올렸다 내리면 그대로 쉐이크가 된다.

### 2. 쉐이크 구현 후보 비교

| 방식 | 장점 | 단점 |
|---|---|---|
| **Perlin `AmplitudeGain` 구동** (채택) | 배선 추가 없이 이미 붙어 있는 컴포넌트를 그대로 쓴다. 코드가 짧고 값의 의미가 직관적이다 | **겹침이 합쳐지지 않는다** — 채널이 하나뿐이라 연속 패턴에서 두 쉐이크가 겹치면 나중 것이 앞 것을 덮어쓴다. 감쇠 곡선을 직접 만들어야 한다 |
| Cinemachine Impulse | 여러 임펄스가 자연 합성, 에셋으로 오서링, 리스너별 감쇠 | vcam에 `CinemachineImpulseListener` + 소스 배선이 필요하고 개념이 하나 늘어난다 |

**채택: Perlin 구동.** 겹침 한계는 "나중 쉐이크가 이긴다(진행 중이면 더 강한 쪽/새 것으로 재시작)"는 규칙을 명시해 흡수한다.
다만 **쉐이크 실행부를 director 안에서 한 메서드로 격리**해 두면, 나중에 Impulse로 갈아끼울 때 트리거·카탈로그 층은 건드리지 않아도 된다.

### 3. 트리거 시각 — 성공은 `Deadline`, 실패는 둘로 갈린다

**실패 시에도 쉐이크를 넣는다**(플레이어가 맞는 연출이 나오므로). 그런데 실패 쪽은 **사건이 둘**이고 시각이 다르다:

| 사건 | 시각 | 근거 |
|---|---|---|
| 피격 리액션 | **첫 미스 순간** | `CharacterActionPlayer.HandleJudgeTargetFirstMiss`가 Hit 클립을 **즉시** 재생한다 |
| 표적 충돌·소멸 | **`Deadline`** | `SliceTargetDirector.CrushTarget` |

패턴 중간에 미스가 나면 이 둘이 수백 ms 벌어지고, **마지막 노드에서 미스가 나면 `goodWindow`(0.1초)까지 좁혀진다.**
→ 두 쉐이크가 붙는 경우를 규칙으로 흡수해야 한다(아래 제약 1).

### 3-1. 왜 성공 트리거가 `Deadline`인가

이 프로젝트는 이미 **`Deadline`을 임팩트의 기준선**으로 통일해 왔다:

| 연출 | 임팩트 시각 |
|---|---|
| 표적 절단 (`SliceTargetDirector`) | `Deadline + Pattern.SliceTargetImpactOffset` |
| 베기 애니메이션 임팩트 프레임 (`CharacterActionPlayer`) | `Deadline + Pattern.SliceTargetImpactOffset` |
| **카메라 쉐이크 (신규)** | **동일하게 맞춘다** |

`Deadline`(= `LastNodeTime + goodWindow`)이 기준인 이유는 `SliceTargetDirector` 주석에 이미 정리되어 있다 —
마지막 노드를 `goodWindow` 안에 늦게 눌러도 Good 성공이므로 **성패는 `Deadline`에서야 확정된다.**
`OnPatternComplete` 시점(마지막 입력 순간)에 즉시 흔들면 칼날·절단보다 최대 0.1초 먼저 터져 셋이 따로 논다.

### 4. 이벤트 확장 포인트 현황 (`PatternHandler`)

| 이벤트 | 페이로드 | 기존 구독자 |
|---|---|---|
| `OnJudged` | `(JudgementResult, int index)` | EffectManager |
| `OnPatternComplete` | `PatternCompletionInfo` | CharacterActionPlayer(과거), EffectManager |
| `OnPatternQueued` | `PatternQueuedInfo` (`StartTime`/`Deadline` 포함) | SliceTargetDirector |
| `OnAllPatternsCleared` | — | SliceTargetDirector |
| `OnJudgeTargetBegan` | `JudgeTargetInfo` (`Deadline` 포함) | CharacterActionPlayer |
| `OnJudgeTargetFirstMiss` | — | CharacterActionPlayer, SliceTargetDirector |
| `OnNodeConnected` / `OnFallingNode*` | — | EffectManager |

**성공 여부를 알려주는 이벤트는 `OnPatternComplete` 하나뿐이다.**

### 5. `PatternCompletionInfo`에 `Deadline`이 없다

```csharp
public readonly struct PatternCompletionInfo
{
    public readonly bool AllCorrect;
    public readonly Pattern Pattern;
    public readonly float LastNodeTime;
    public readonly float NextLastNodeTime;
}
```

`LastNodeTime`은 있지만 `Deadline`이 없고, `goodWindow`는 `PatternHandler`의 **private 필드**라 소비자가 역산할 수 없다.

다만 이 구조체의 주석이 이미 이렇게 적고 있다:

> 향후 소비자(**카메라 연출 등**)가 늘거나 필요한 데이터가 추가돼도 여기 필드만 더하면 되고 기존 구독자 시그니처는 깨지지 않는다.

→ **`Deadline` 추가가 설계상 의도된 확장 경로다.** `JudgeTargetInfo`에 `Deadline`을 더한 선례(`docs/SliceImpactFrame/`)도 있다.

### 6. 타이밍이 성립하는가 — `OnPatternComplete`는 항상 `Deadline` 이전인가

`PatternHandler`에서 패턴이 끝나는 경로는 둘이다:

| 경로 | 발생 시각 |
|---|---|
| 완주(`CompletePattern`) | 마지막 노드 입력 순간. 판정이 성립하려면 `LastNodeTime ± goodWindow` 안 → **≤ `Deadline`** |
| 만료(`ExpireOverduePatterns`) | `Time.time > Deadline`인 프레임 → `Deadline` 직후. 단 이 경우 `AllCorrect = false`라 쉐이크 대상이 아니다 |

→ **성공 케이스에서 `OnPatternComplete`는 언제나 `Deadline` 이전(또는 같은 프레임)에 온다.**
따라서 `SliceTargetDirector`처럼 토큰·예약·성패확정을 도는 복잡한 구조가 필요 없다 —
**완료 이벤트에서 `Deadline`을 받아 그 시각에 발사하도록 큐에 넣기만 하면 된다.**

**실패 케이스는 `Deadline` 직후에 온다**(만료 경로). 예약해 봐야 이미 지난 시각이므로
소비자는 **"예약 시각이 이미 지났으면 즉시 발사"** 를 반드시 처리해야 한다 — 실패 쉐이크에서는 이쪽이 정상 경로다.
(완주했지만 `AllCorrect = false`인 경우는 완주 경로로 오므로 `Deadline` 이전이다. 두 경우가 섞이니 분기하지 말고 "지났으면 즉시"로 통일한다.)

### 6-1. 첫 미스 이벤트는 페이로드가 없다

`OnJudgeTargetFirstMiss`는 `Action`(인자 없음)이며 **패턴당 1회만** 발행된다
(`CharacterActionPlayer.missedThisTarget`, `SliceTargetDirector.HandleFirstMiss`가 이미 이 성질에 의존한다).
피격 쉐이크는 그 순간 즉시 터뜨리면 되므로 페이로드가 필요 없다 — 이벤트를 바꾸지 않아도 된다.

### 7. 따라야 할 관례 — `EffectManager` + `EffectCatalog`

`CLAUDE.md`가 명시하는 확장 규칙의 모범 사례가 이미 있다.

```csharp
public enum EffectTrigger { Perfect, Good, Miss, PatternCompleteFull, PatternComplete, NodeConnected }

[Serializable]
public struct EffectEntry
{
    public EffectTrigger trigger;
    public GameObject prefab;
    public int initialSize;
    public int maxSize;
}
```

- `EffectManager`는 `PatternHandler`의 **기존 이벤트만 구독**하고, `List<EffectEntry> catalog`를 인스펙터에서 받아 트리거→엔트리 딕셔너리를 만든다.
- **"이펙트 추가 = 카탈로그에 한 줄 추가(코드 수정 없음)"**, 프리팹이 비면 무연출.
- 카메라 연출도 이 형태를 그대로 따르면 "연출 추가 = 카탈로그에 한 줄"이 성립한다.

---

## 제약사항

1. **Perlin 채널은 하나다 — 쉐이크가 겹치면 합성되지 않는다.**
   패턴 사이 간격(최소 0.4초)만 보면 충돌이 드물어 보이지만, **실패 경로에서는 확실히 겹친다** —
   마지막 노드 미스면 `피격 쉐이크`와 `표적 충돌 쉐이크`가 `goodWindow`(0.1초) 간격으로 붙는다(§3).
   따라서 "겹치면 어떻게 되는가"는 선택이 아니라 **반드시 정의해야 하는 규칙**이다.
   단순히 덮어쓰면 진행 중이던 강한 쉐이크가 약한 새 쉐이크로 바뀌며 세기가 뚝 떨어진다.
2. **`AmplitudeGain`의 휴지값을 존중해야 한다.** 씬의 Perlin에 이미 상시 흔들림 값이 들어 있을 수 있다 — 쉐이크가 끝나면 0이 아니라 **원래 값**으로 되돌려야 한다.
3. **`CinemachineBrain`은 vcam을 블렌딩한다.** 나중에 vcam이 늘면 "어느 vcam의 노이즈를 흔들 것인가"가 문제가 된다. 지금은 vcam 1대라 직접 참조로 충분하지만, 참조를 director가 들고 있게 해서 나중에 `Brain.ActiveVirtualCamera`로 바꿀 여지를 남긴다.
4. **판정 파이프라인에 개입하지 않는다.** 카메라 연출은 순수 연출이다 — `EffectManager`/`SliceTargetDirector`와 같은 위치다.
5. **`Time.timeScale`에 종속되지 않아야 한다.** 향후 히트스톱(순간 정지) 연출이 붙으면 `Time.time` 기반 쉐이크가 멈춘다. 지금 결정할 필요는 없지만, 시간축 선택을 한 곳에 모아 두면 나중에 갈아끼우기 쉽다.
6. **곡 중단 시 잔존 처리**가 필요하다. `OnAllPatternsCleared`에서 예약된 쉐이크를 비우고 Perlin을 휴지값으로 되돌려야 한다(`SliceTargetDirector` 선례).
