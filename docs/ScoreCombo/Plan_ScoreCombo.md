# Plan — 점수 · 콤보 (ScoreCombo)

근거: `docs/ScoreCombo/Research_ScoreCombo.md`

## 방침 한 줄

**`ScoreDirector` 하나가 `PatternHandler`·`ChartPlayer`의 기존 이벤트만 구독해 채점한다.** `PatternHandler`는 한 줄도 안 고친다(§7의 관심사 분리 규율). 점수는 **고정 만점으로 정규화**하고, 콤보는 배율이 아니라 **별도 배점 풀**로 둔다.

**놓친 노트는 이벤트로 오지 않는다** — `OnJudged` 횟수를 세다가 `OnPatternComplete`에서 `Template.AllData.Count`와 뺄셈한다(Research §3~4).

---

## 점수 구조 (확정 전 — `>>>`로 이견 주면 갈아엎는다)

만점 `1,000,000`을 세 풀로 나눈다.

| 풀 | 비중 | 만점 조건 |
|---|---|---|
| 노트 | 70% | 전 노트 Perfect |
| 패턴 | 20% | 전 패턴 `AllCorrect` |
| 콤보 | 10% | 풀콤보 |

```
노트점수 = Σ weight(판정) / 총노트수 × 700,000      weight: Perfect 1.0 / Good 0.6 / Miss 0
패턴점수 = 성공패턴수 / 총패턴수 × 200,000
콤보점수 = Σ combo_i / (N(N+1)/2) × 100,000        combo_i = i번째 노트 직후의 콤보 수
```

- **콤보를 노트 점수에 곱하지 않는다.** 곱하면 같은 Perfect라도 곡 후반이 몇 배 비싸져 초반 실수가 싸게 먹힌다(Research §7).
- 콤보 풀의 분모가 **풀콤보일 때의 값**이라 튜닝 테이블 없이 만점이 정확히 풀콤보에서 나온다.
- 비중 셋은 인스펙터 노브. 합이 1이 아니면 `OnValidate`가 경고한다.

## 만점 — 곡이 소유한다

- [x] `SongChart`에 `maxScore` 필드. **곡마다 인스펙터에서 조정**한다
- [x] 0 이하면 `ScoreDirector.defaultMaxScore`(1,000,000)로 폴백 — 기존 채보 회귀 0

**왜 곡 에셋인가**: 만점은 "이 곡을 완주하면 몇 점인가"라 **채보의 성질**이다. `SongChart.patternPool`이 같은 근거로 곡 에셋에 사는 것과 같다(§5). `ScoreDirector`에 두면 곡을 바꿀 때마다 씬을 고쳐야 한다.

## 등급 — 비율로 가른다

**⚠ 만점이 곡마다 다르므로 절대 점수로 등급을 가르면 안 된다.** 만점 2,000,000짜리 곡의 60만 점과 500,000짜리 곡의 60만 점은 완전히 다른 성취다. 등급은 **달성 비율**의 함수다.

| 등급 | 비율 | 뜻 |
|---|---|---|
| `SSS` | **퍼펙트** | 전 노트 Perfect · 전 패턴 성공 · 풀콤보 |
| `SS` | 95% 이상 | |
| `S` | 90% 이상 | |
| `A` | 85% 이상 | |
| `B` | 77% 이상 | |
| `C` | 60% 이상 | |
| `D` | 60% 미만 | |

- 경계는 **이상(`>=`)**이다. 정확히 90%면 `S`.
- 문턱 5개(0.95 / 0.90 / 0.85 / 0.77 / 0.60)는 인스펙터 배열. **`SSS`는 배열에 없다** — 아래 참조.
- **만점 값을 바꿔도 등급은 안 바뀐다.** 비율은 달성도이지 점수가 아니므로, 곡별 만점은 *화면에 뜨는 숫자*만 정한다. 난이도 높은 곡에 큰 숫자를 주면서 공정성이 유지된다.

### ⚠ `SSS`는 비율이 아니라 '사실'이다

`SSS`를 `ratio >= 1.0`으로 판정하면 **부동소수점 오차 하나로 영영 안 나올 수 있다** — 세 풀의 가중합(0.7 + 0.2 + 0.1)이 정확히 1.0이 아닐 수 있고, 점수를 `long`으로 반올림하는 과정에서도 1점이 샌다.

그래서 `SSS`는 **불리언 세 개의 논리곱**으로 정한다:

```
IsPerfect = (Miss 0 && Good 0) && (실패 패턴 0) && (최대 콤보 == 총 노트 수)
```

세 항 전부 정수 비교라 오차가 낄 자리가 없다. **"퍼펙트 = SSS"를 글자 그대로 구현한 것**이고, 동시에 최고 등급의 유일성이 구조적으로 보장된다.

## 콤보 단계 — 3단계 + 화면 외곽 붉은 연출

| 단계 | 콤보(기본) | 화면 |
|---|---|---|
| 0 | 0 ~ 9 | 평소 |
| 1 | 10 ~ 29 | 외곽이 옅게 붉어진다 |
| 2 | 30 ~ 59 | 진해진다 |
| 3 | 60 ~ | 가장 진하다 |

- 문턱 3개는 인스펙터 배열. **단계 수를 코드가 정하지 않는다** — 배열 길이가 곧 단계 수다.
- 콤보 점수(위의 풀)와 **무관하다.** 단계는 순수 연출이고 점수는 연속 함수다 — 섞으면 문턱 근처에서 점수가 계단식으로 튄다.

---

## Step 1 — 곡에서 읽어 오는 것 둘

- [x] `ChartPlayer.ActiveChart`를 `private` → `public`. **한 단어**

**왜**: 총 노트/패턴 수와 만점을 알려면 채점기가 곡을 알아야 하는데, `GameSession.SelectedChart ?? debugChart` 판단을 복제하면 **디버그 재생에서만 만점이 어긋난다**. 곡의 진실의 원천은 하나여야 한다.

- [x] `SongChart`에 만점 필드 추가

```csharp
[Tooltip("이 곡의 만점. 0 이하면 ScoreDirector의 기본값을 쓴다.\n" +
         "⚠ 등급은 비율로 갈리므로 이 값을 키워도 등급이 쉬워지지 않는다 — 화면에 뜨는 숫자만 커진다.")]
public long maxScore;
```

**왜 곡 에셋인가**: 만점은 "이 곡을 완주하면 몇 점인가"라 **채보의 성질**이다. `patternPool`이 같은 근거로 여기 사는 것과 같다(§5). 씬의 디렉터에 두면 곡을 바꿀 때마다 씬을 고쳐야 한다.

---

## Step 2 — 순수 계산을 `ScoreMath`로 (`Score/Core`, asmdef)

- [x] `Assets/02. Scripts/Score/Core/ScoreMath.cs` + `Score.Core.asmdef`

```csharp
namespace ScoreSpace
{
    /// <summary>
    /// 채점의 <b>순수 계산</b>. MonoBehaviour·씬에 의존하지 않아 그대로 유닛테스트할 수 있다
    /// (<c>EnemyRing</c>·<c>DuelGap</c>과 같은 규율).
    /// </summary>
    public static class ScoreMath
    {
        /// <summary>판정 하나의 가중치. Miss는 0이다 — 친 것과 안 친 것을 점수로 구분하지 않는다.</summary>
        public static float Weight(JudgementResult r, float good) => ...

        /// <summary>풀콤보일 때의 콤보 누적합 = N(N+1)/2. <b>콤보 풀의 분모</b>다.</summary>
        public static long FullComboSum(int noteCount) => (long)noteCount * (noteCount + 1) / 2;

        /// <summary>세 풀을 합쳐 최종 점수로. 총 수가 0이면 그 풀은 0으로 접힌다(0 나누기 없음).</summary>
        public static long Total(...);

        /// <summary>
        /// 등급. <b>절대 점수가 아니라 달성 비율</b>로 가른다 — 만점이 곡마다 다르므로
        /// 점수로 가르면 쉬운 곡의 만점과 어려운 곡의 절반이 같은 등급을 받는다.
        ///
        /// <para><b>⚠ <paramref name="isPerfect"/>가 비율보다 먼저다.</b> SSS를 <c>ratio >= 1</c>로 판정하면
        /// 가중합(0.7+0.2+0.1)과 <c>long</c> 반올림의 오차 하나로 <b>영영 안 나올 수 있다</b>.
        /// 퍼펙트는 정수 비교 셋의 논리곱이라 오차가 낄 자리가 없다.</para>
        /// </summary>
        /// <param name="ratio">달성 비율(0~1). 점수 ÷ 만점이 아니라 <b>세 풀의 달성도</b> 그 자체다.</param>
        /// <param name="thresholds">SSS를 뺀 내림차순 비율 문턱 5개. 경계는 이상(&gt;=)이다.</param>
        public static ScoreGrade GradeOf(float ratio, bool isPerfect, IReadOnlyList<float> thresholds);

        /// <summary>콤보가 속한 단계(0 = 평소). <b>문턱 배열의 길이가 곧 단계 수</b>다 — 코드가 3을 모른다.</summary>
        public static int ComboTierOf(int combo, IReadOnlyList<int> thresholds);
    }

    /// <summary>⚠ 정수를 명시한다 — 직렬화·저장 기록의 키가 되므로 순서가 바뀌면 과거 기록이 밀린다.</summary>
    public enum ScoreGrade { D = 0, C = 1, B = 2, A = 3, S = 4, SS = 5, SSS = 6 }
}
```

- [x] ⚠ **0 나누기를 전부 접는다.** 노트 0개인 채보(빈 곡·중단)는 정상 상태다 — 예외가 아니라 0점이다
- [x] ⚠ `long`으로 든다. `Σ combo_i`가 노트 3000개면 450만이고, 곱하기 전에 `int`로 잡으면 넘칠 여지가 생긴다

---

## Step 3 — `ScoreDirector` (채점의 유일 관리 지점)

- [x] `Assets/02. Scripts/Score/ScoreDirector.cs`

```csharp
[SerializeField] private PatternHandler handler;
[SerializeField] private ChartPlayer chartPlayer;

[Header("Score")]
[Tooltip("곡이 maxScore를 지정하지 않았을 때(0 이하) 쓰는 폴백. 만점의 진실의 원천은 SongChart다.")]
[SerializeField] private long defaultMaxScore = 1_000_000;
[Range(0f,1f)] [SerializeField] private float noteShare = 0.7f;
[Range(0f,1f)] [SerializeField] private float patternShare = 0.2f;
[Range(0f,1f)] [SerializeField] private float comboShare = 0.1f;
[Range(0f,1f)] [SerializeField] private float goodWeight = 0.6f;

[Header("Grade")]
[Tooltip("등급 비율 문턱(내림차순). SS / S / A / B / C의 하한이며 경계는 이상(>=)이다.\n" +
         "⚠ SSS는 여기 없다 — 퍼펙트(Miss·Good 0 · 실패 패턴 0 · 풀콤보)라는 사실로만 나온다.\n" +
         "⚠ 절대 점수가 아니라 비율이다. 만점이 곡마다 다르므로 점수로 가르면 곡 간 비교가 깨진다.")]
[SerializeField] private float[] gradeThresholds = { 0.95f, 0.90f, 0.85f, 0.77f, 0.60f };

[Header("Combo Tier")]
[Tooltip("콤보 단계 문턱. 배열 길이가 곧 단계 수다 — 코드는 3을 모른다.")]
[SerializeField] private int[] comboTierThresholds = { 10, 30, 60 };

public event Action<long> OnScoreChanged;
public event Action<int> OnComboChanged;      // 0으로 오면 끊긴 것이다
public event Action<int> OnComboBroken;       // 끊기기 직전의 콤보(연출용)
public event Action<int> OnComboTierChanged;  // 단계가 바뀐 순간만. 매 노트가 아니다
public event Action<ScoreResult> OnFinalized;
```

- [x] **⚠ `OnComboTierChanged`는 단계가 바뀔 때만 발행한다.** 매 노트마다 쏘면 구독자가 같은 값을 초당 수십 번 받아 자기가 다시 비교해야 한다 — 판단은 한 곳에서 한 번만 한다

구독은 넷:

| 이벤트 | 하는 일 |
|---|---|
| `ChartPlayer.OnCountdownStarted` | 총 노트/패턴 수를 세고 전부 리셋 |
| `PatternHandler.OnJudged` | 노트 채점 · 콤보 증감 · **이 패턴에서 판정된 노트 수 +1** |
| `PatternHandler.OnPatternComplete` | 패턴 보너스 · **뺄셈으로 놓친 노트 Miss 처리** |
| `ChartPlayer.OnSongEnded` | 확정 → `GameSession`에 전달 |

- [x] **놓친 노트 뺄셈**이 이 클래스의 핵심이다

```csharp
private void HandlePatternComplete(PatternCompletionInfo info)
{
    if (info.AllCorrect) successPatterns++;

    // ⚠ 놓친 노트는 이벤트로 오지 않는다(Research §3~4).
    // OnFocusRingMissedArrival은 ExpectedTime 정각에 나므로 '늦은 Good'도 전부 걸린다 — 쓸 수 없다.
    int nodes = info.Template != null ? info.Template.AllData.Count : judgedInPattern;
    for (int i = judgedInPattern; i < nodes; i++) ApplyNote(JudgementResult.Miss);

    judgedInPattern = 0;
}
```

- [x] ⚠ **`OnFocusRingMissedArrival`을 구독하지 않는다.** 이유를 주석에 남긴다 — 이름이 유혹적이라 나중에 누가 붙인다
- [x] ⚠ **오답 인덱스는 그 자체로 콤보를 안 끊는다.** `OnJudged`가 안 나기 때문이며, 그 결과로 놓친 노트가 뺄셈에 잡혀 끊는다. 즉시 끊고 싶으면 `OnJudgeTargetFirstMiss`가 이미 있다 — **지금은 안 쓴다**(패턴이 정체하는 동안 콤보가 두 번 끊기는 것으로 읽힌다)
- [x] `OnAllPatternsCleared`에서 진행 중 카운터만 정리

---

## Step 4 — HUD (`ScoreHudView`)

- [x] `Assets/02. Scripts/UI/ScoreHudView.cs` — `ScoreDirector` 이벤트만 구독하는 순수 표시
- [x] 점수는 **굴러가는 숫자**(목표값으로 감쇠). 즉시 대입하면 큰 가산이 안 읽힌다
- [x] 콤보는 갱신마다 살짝 펀치, `OnComboBroken`에서 페이드아웃
- [x] ⚠ **자리는 패턴인풋 밖**이다. Point_9가 (700, 700)이라 우상단이 비어 있다. `Canvas` 직속이어야 한다 — `PatternHandler`(1400x1400)의 자식이면 그 rect 기준 앵커가 되어 패턴 한복판에 온다(`DodgePointView`가 겪은 것과 같은 함정, §11-8)
- [x] 배선이 비면 그 표시만 조용히 빠진다(기존 규율)

---

## Step 5 — 콤보 포스트 이펙트 (붉은 외곽 + 색수차)

**씬에 이미 URP `Global Volume`이 있고 `Vignette`가 켜져 있다**(검정, intensity 0.2). 셰이더도 스프라이트도 새로 만들지 않는다 — **전용 Volume 하나를 위에 얹고 `weight`만 민다.**

- [x] 씬에 `ComboPostFxVolume`(`Volume`) 추가 — `Priority`를 `Global Volume`보다 높게, **`Weight = 0`**으로 시작
- [x] 전용 프로파일 에셋 하나(`Assets/Settings/ComboPostFxProfile.asset`)에 오버라이드 **둘**:
  - `Vignette` — `color = 붉은색`, `intensity = 최고 단계의 값`, `smoothness` 취향
  - `Chromatic Aberration` — `intensity = 최고 단계의 값`. **약하게**(0.15~0.3 권장, 아래 주석)
- [x] `Assets/02. Scripts/UI/ComboPostFxView.cs` — `ScoreDirector.OnComboTierChanged`만 구독

```csharp
[SerializeField] private Volume volume;
[Tooltip("단계별 목표 weight. 배열 길이는 ScoreDirector의 문턱 수 + 1이어야 한다(0단계 포함).")]
[SerializeField] private float[] tierWeights = { 0f, 0.35f, 0.65f, 1f };
[Tooltip("올라갈 때의 감쇠 시간(초). 끊길 때는 이 값을 안 쓴다 — 아래 주석.")]
[SerializeField] private float riseDamp = 0.25f;
```

- [x] **⚠ 색수차 때문에 코드가 한 줄도 안 늘어난다.** URP는 프로파일 안의 오버라이드 전부를 같은 `weight`로 보간하므로, **효과를 추가하는 일 = 프로파일에 오버라이드 한 줄 추가**다(`EffectCatalog`·`CameraCueCatalog`의 "연출 추가 = 카탈로그 한 줄"과 같은 결). 클래스 이름이 `ComboVignette`가 아니라 `ComboPostFx`인 이유가 이것이다 — 앞으로 붙을 효과가 더 있다
- [x] **⚠ 색수차는 약해야 한다.** 이 게임의 타이밍 단서는 **포커스 링의 크기**(§4)인데, 색수차는 화면 가장자리일수록 강하게 갈라진다. 패턴인풋이 화면 중앙 ±700px이라 링이 그 영역에 들어오지만, 세게 걸면 링 가장자리가 번져 "딱 맞았다"는 판단이 흐려진다. **판정을 방해하는 순간 연출이 아니라 손해다**
- [x] **⚠ 두 효과가 한 `weight`를 공유한다.** 비네트와 색수차의 상승 곡선을 따로 잡고 싶어지면 그때 Volume을 둘로 나눈다 — 지금 나누면 노브가 둘인데 값이 같은 상태로만 굴러간다

- [x] **⚠ 프로파일을 코드가 수정하지 않는다.** `volume.profile`은 런타임 사본을 만들지만 `sharedProfile`을 건드리면 **에디터에서 에셋에 그대로 저장된다**. `weight` 하나만 밀면 그 부류가 원천 소멸하고, 색·강도·부드러움은 전부 아티스트가 프로파일에서 잡는다
- [x] **⚠ 기존 `Global Volume`의 비네트를 안 건드린다.** URP는 우선순위가 높은 Volume 쪽으로 **weight만큼 보간**하므로, `weight = 0`이면 평소 화면이 1픽셀도 안 바뀐다. 지우거나 덮어쓰면 콤보 시스템을 꺼도 원래 룩이 안 돌아온다
- [x] **⚠ 올라갈 때는 감쇠, 끊길 때는 즉시 0.** 감쇠로 사라지면 "서서히 식는다"로 읽혀 **끊겼다는 사실 자체가 안 보인다.** 콤보 브레이크는 사건이라 즉발이어야 한다
- [x] 배선(`volume`)이 비면 이 층만 조용히 죽는다(기존 규율)
- [x] **⚠ 붉은 비네트가 피격 연출과 겹칠 수 있다.** 지금은 피격 화면 효과가 없지만(§7-6 — `OnDamaged` 구독자 없음) 나중에 붙일 때 **같은 채널을 두 시스템이 다투게 된다**. 그때는 피격이 이기도록 우선순위를 나눈다 — 이 플랜에서는 자리만 비워 둔다

---

## Step 6 — 이미 예약된 자리 둘 연결

- [x] **앰비언트 강도** — `EffectManager.SetIntensity(float)`의 호출자가 드디어 생긴다(`EffectManager.cs:166` 주석이 이 플랜을 가리키고 있다). `콤보 / comboIntensityFull`(기본 50)을 0~1로 클램프해 넘긴다
- [x] **최종 점수 전달** — `GameSession.LastResult`(`ScoreResult` struct: 점수·**등급**·최대콤보·Perfect/Good/Miss 수·풀콤보 여부). 결과 **화면**은 범위 밖 — 값을 둘 자리만 만든다

---

## Step 7 — 검증

- [x] `Score/Tests`에 `ScoreMath` 유닛테스트
  - 전 노트 Perfect + 전 패턴 성공 + 풀콤보 → **정확히 `maxScore`** (경계가 맞는지의 유일한 증거). ⚠ `maxScore`를 1,000,000 / 500,000 / 2,000,000 셋으로 돌린다 — 반올림이 어느 값에서만 1점 새는지 여기서 잡힌다
  - 전 노트 Miss → 0
  - 노트 0개 → 0 (0 나누기 없음)
  - `FullComboSum(3) == 6`
  - Good만으로 채운 곡 → 노트 풀이 정확히 `goodWeight` 비율
  - **등급 문턱 경계** — 비율 정확히 0.90이면 `S`, 0.8999면 `A`
  - **`SSS`는 퍼펙트에서만** — `ratio = 1.0`이어도 `isPerfect = false`면 `SS` (최고 등급의 유일성)
  - **`isPerfect = true`면 비율과 무관하게 `SSS`** — 반올림으로 ratio가 0.9999가 되어도 강등되지 않는다
  - **만점을 바꿔도 등급이 안 바뀐다** — 같은 플레이를 maxScore 500k/2M로 채점해 등급이 같은가
  - **콤보 단계** — 문턱 정각에 올라가는가, 배열이 비면 언제나 0단계인가
- [x] `ScoreDirector` 뺄셈 로직 테스트 — MonoBehaviour라 유닛테스트가 어렵다. **뺄셈만 static 메서드로 뽑아**(`ScoreMath.MissedNotes`) 검증했다
- [x] **실행 결과: 30/30 통과**(Unity Test Runner, `Score.Tests`)
- [ ] 플레이 검증 — **미실행. Unity 에디터에서 직접 확인 필요**
  - **늦은 Good(정각 +0.08초)이 콤보를 안 끊는가** ← Research §3의 함정을 안 밟았는지의 유일한 증거
  - 아무 입력 없이 패턴을 흘려보내면 노드 수만큼 콤보가 끊기고 Miss가 세지는가
  - 오답 노드 연타 후 만료 → 놓친 수가 정확한가
  - 오토플레이(§8)로 곡 완주 → **정확히 만점 · `SSS`**인가
  - 곡 재시작 시 점수·콤보가 리셋되는가
  - **콤보 10/30/60에서 화면 외곽이 세 번 진해지는가**(붉은 기 · 색수차 동시에)
  - **콤보가 끊기면 즉시 사라지는가**(서서히 식으면 끊긴 게 안 보인다)
  - **콤보 0일 때 화면이 원래 룩 그대로인가** — `Global Volume`의 검정 비네트가 살아 있어야 한다
  - **최고 단계에서 포커스 링의 가장자리가 읽히는가** — 색수차가 링을 번지게 하면 타이밍 단서가 흐려진다. 여기서 흐리면 **연출이 아니라 손해**이므로 프로파일의 색수차 intensity를 낮춘다

---

## Step 8 — 문서

- [x] `CLAUDE.md`에 §12 신설 — 채점 구조 · **`OnFocusRingMissedArrival`을 채점에 쓰면 안 되는 이유** · 놓친 노트 뺄셈 · 콤보를 배율로 안 거는 이유 · 등급은 점수의 함수 · 비네트는 `weight`만 민다
- [x] `EffectManager.cs:166`의 "추후 별도 플랜" 주석을 실제 연결로 갱신
- [x] 이 Plan의 체크박스 갱신

---

## 범위 밖 (의도적으로 안 한다)

| 항목 | 왜 |
|---|---|
| 결과 화면 | 점수·등급을 둘 자리(`GameSession.LastResult`)만 만든다. 화면은 별도 주제 |
| 최고점·등급 저장(기록) | 세이브 시스템이 없다. 오토플레이로 만점이 나오는 상태라(§8) 기록을 지금 남기면 처음부터 오염된다 |
| 등급별 연출·사운드 | 결과 화면이 생긴 뒤의 일이다 |
| 콤보 포스트 이펙트의 색/강도 곡선화 | 단계 3개면 충분하다. `weight` 배열 하나로 튜닝되고, 모자라면 문턱을 늘리면 된다(코드가 3을 모른다) |
| 비네트와 색수차의 상승 곡선 분리 | 지금 나누면 노브가 둘인데 값이 같은 상태로만 굴러간다. **둘의 곡선을 실제로 다르게 잡고 싶어진 순간** Volume을 둘로 나눈다 |
| 피격 화면 효과와의 우선순위 조정 | 피격 화면 효과 자체가 아직 없다(§7-6). 생길 때 같이 정한다 |
| 오토플레이 점수 차단 | 기록 저장이 붙는 시점의 문제다. 지금은 만점 검증에 오히려 필요하다 |
| 회피(§11-8) 성패 점수 | 회피는 자기 시계로 도는 별개 판정이다. 노트 채점에 섞으면 만점 정의가 흔들린다 |
| 판정별 배점의 곡선화 | Perfect/Good 두 값이면 충분하다. 3단계 이상은 판정 자체가 3단계가 될 때 |
| 콤보 배율 UI | 배율을 안 쓰므로 표시할 배율이 없다 |
