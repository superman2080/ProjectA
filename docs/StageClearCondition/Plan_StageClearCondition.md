# Plan — 스테이지 종료 조건을 "모든 패턴 해결"로

근거: `Research_StageClearCondition.md`. 위험 번호(R-xx)는 그 문서를 가리킨다.

---

## 0. 설계 결정 — 여섯 줄

1. **이벤트를 새로 만들지 않는다.** 종점은 계속 `ChartPlayer.OnSongEnded` 하나다. 바뀌는 것은 *언제 쏘는가*뿐이라 `ScoreDirector`·`BattleSceneBootstrap`의 구독이 한 줄도 안 바뀐다(R-C1·R-M2).
2. **되감는 것은 채보 시계뿐이다.** 오디오는 손대지 않는다. 재시도할 때 `chartOffset`을 밀어 **채보 시계를 그 엔트리의 스폰 시각으로 되돌린다** — 뒤따르는 엔트리의 *간격*이 보존되어 R-R1(쏟아짐)이 원천 소멸한다.
3. **드리프트는 박의 정수배로만 쌓인다.** 대기를 초가 아니라 **박 수**로 저작하고 `chartOffset`을 박 간격으로 올림한다 — 그래야 되감긴 온셋이 `BeatGrid` 위에 그대로 앉는다(R-L4). 곡은 루프라 끝나지 않는다(R-L2). **이 둘이 같이 참일 때만 동기화가 유지된다**(Research §2-A).
4. **재시도본은 세지 않는다.** 표식 하나(`IsRetry`)가 채점(R-R5)과 마무리 실루엣(R-R6)을 **동시에** 막는다.
5. **방어 패턴과 적 방어 모션은 코드를 안 짠다.** 대미지·피격 연출·카메라 큐(Research §1-3)도, `Attacker.Player` 실패 시의 적 제자리 패링(§11-2)도 이미 붙어 있다.
6. **모드 값 하나(`StageMode`)가 종료 조건·재시도·`audioSource.loop` 셋을 같이 가른다**(R-M3). 따로 두면 "`Linear`인데 곡이 루프해서 영영 안 끝나는" 조합이 표현 가능해진다.

### 0-1. 새 진행 모델

**소비의 기준은 역할이 아니라 "대가를 치렀는가"다.** 즉 *적의 칼이 닿았으면* 그 엔트리는 끝난 것이다.

```
소비(consume) = 성공  OR  (실패 AND 적이 휘둘렀다)
재시도(retry) = 실패  AND 적이 안 휘둘렀다
```

**⚠ 이 조건은 패턴만 보고 알 수 있어 새 구독이 0개다.** §6이 *"맞는 경우는 둘뿐 — `Attacker.Enemy`이거나 상호 공격(`CountersOnFail`)"*이라고 이미 못박아 뒀으므로, 게터 하나로 끝난다:

```csharp
// Pattern
public bool HitsOnFail => attacker == Attacker.Enemy || CountersOnFail;
```

- **⚠ `OnPlayerHit`을 구독하면 안 된다.** 그 이벤트는 `impactTime`(= `Deadline + ImpactOffset`)에 예약돼 나므로 **`OnPatternComplete`(= `Deadline`)보다 늦다.** 완료 시점에는 아직 대미지가 안 들어와 있어서, 구독으로 판단하면 *"맞을 예정인데 아직 안 맞았다"*를 재시도로 오판한다.
- **⚠ 기습 회피(§11-8) 실패로 맞은 것은 이 규칙에 안 들어간다.** 그건 패턴 밖의 사건이고 `PlayHitReaction`을 같은 경로로 쓸 뿐이다 — **패턴의 대가가 아니다.** 게터가 `Pattern`만 보므로 이 구분이 공짜로 성립한다.

**재시도 단위는 엔트리가 아니라 사슬이다**(§11-5). 사슬 안에서 하나라도 재시도 사유가 나오면 **사슬 머리로 되돌린다.**

```
재시도 시퀀스:  공격 실패 → 적 방어 → N박 대기 → (사슬 머리부터) 재공격
                          └ 기존 코드(§11-2)  └ retryGapBeats

ChartPlayer가 "진행 중 사슬"을 들고 있다가 OnPatternComplete에서 판단:
  소비   → 다음 엔트리로 (사슬 끝이면 다음 사슬로)
  재시도 → head = 그 사슬의 첫 엔트리
           chartOffset += CeilToBeat(chartTime − head.spawnTimes[0]) + retryGapBeats × beat
           사슬의 엔트리 전부를 pendingEntries 맨 앞에 되돌린다 (isRetry = true)
           남은 타는 진행하지 않는다
```

### 0-2. 채보 시계 — 루프를 넘어 단조 증가한다

```
// ⚠ audioSource.time은 클립 안의 위치라 루프마다 0으로 되감긴다 (R-L1)
if (audioSource.time < prevAudioTime) loops++;
prevAudioTime = audioSource.time;
songTime  = loops * clip.length + audioSource.time;

chartTime = songTime − chartOffset;   // 스폰 판단은 이 값으로
beat      = 60f / bpm;                // ⚠ bpm <= 0은 "모른다"가 규약 (R-L4)
```

`chartOffset`은 **재시도할 때만**, **박 간격의 정수배로만** 증가한다. 0이면 지금과 완전히 같다.

### 0-3. 종료

```
cleared  = 남은 엔트리 0  AND  진행 중 엔트리 없음  AND  PatternHandler에 살아 있는 패턴 0
finishAt = cleared가 된 시각 + outroHold
종료     = Time.time >= finishAt  AND  마무리 실루엣이 바쁘지 않다
```

`outroHold`(기본 **1.5초**)가 덮는 것: `goodWindow`(0.1) + `Pattern.ImpactOffset` + 절단·사망 폭발 + 히트스톱 + 여운. 실루엣은 `holdDuration`이 더 길 수 있어 **별도로 기다린다**(합치면 실루엣이 없는 실패 경로가 이유 없이 길어진다).

---

## 단계

### - [x] Step 1 — 밖에서 물을 수 있게 게터를 연다 (R-S3·R-S4·R-R5)

- `PatternHandler`에 `public bool HasActivePatterns => activePatterns.Count > 0;`
- `PatternHandler`에 `public bool JudgeTargetIsRetry` — 지금 판정 대상이 재시도본인가. `ScoreDirector.HandleJudged`가 물어본다(그 이벤트는 인덱스만 주므로 다른 방법이 없다).
- `FinaleSilhouetteDirector`에 `public bool IsBusy => armed || active;`
  - **⚠ `armed`도 봐야 한다.** `active`만 보면 완료~임팩트 사이에 종료가 끼어들어 **실루엣이 시작조차 못 한다.**

**판정 로직은 한 줄도 안 고친다.** §7의 "PatternHandler는 연출을 위해 수정하지 않는다"는 *판정 파이프라인*에 대한 규율이며 읽기 게터는 어긋나지 않는다(`IsDragging`·`DebugAutoPerfect`가 이미 같은 성격).

### - [x] Step 2 — 재시도 표식을 페이로드에 싣는다 (R-R5·R-R6 · **둘을 한 번에**)

- `PatternHandler.SetPattern(..., bool isRetry = false)` — 기본값이 있어 **기존 호출자(튜토리얼 드릴 포함)가 한 줄도 안 바뀐다.**
- `ActivePattern`에 `IsRetry` 저장, `PatternCompletionInfo`·`PatternQueuedInfo`에 `IsRetry` 필드 추가.
  - `PatternCompletionInfo`는 "필드만 더하면 기존 구독자가 안 깨진다"를 **자기 주석으로 명시**하고 있다(PatternCompletionInfo.cs:6~9). 생성자 인자가 늘어나므로 호출 지점만 따라간다.
- **소비자들이 이 표식만 보면 끝난다**:
  - `ScoreDirector.HandleJudged` — `handler.JudgeTargetIsRetry`면 **채점 누적에서 뺀다**(아래)
  - `ScoreDirector.HandlePatternComplete` — `info.IsRetry`면 즉시 return(뺄셈도 `successPatterns++`도 없다)
  - `FinaleSilhouetteDirector.HandlePatternComplete` — `info.IsRetry`면 `completedPatterns++` 전에 return
- **규칙: "다시 해도 점수는 안 오른다."** 그 엔트리의 성적은 **첫 시도**로 확정된다. 연타의 "초과 타격은 애니메이션만 나오고 점수는 안 오른다"(§2-1)와 같은 관용구라 새 개념이 0개다.
  - **⚠ 그래서 공격 패턴을 실패하면 그 엔트리의 노트는 전부 Miss로 확정된다.** 재시도는 *진행을 위한 것*이지 만회 수단이 아니다.

**⚠ `isRetry`는 사슬이 아니라 엔트리별로 매긴다.**
사슬을 머리부터 되감으면 **플레이어가 한 번도 못 본 엔트리**가 딸려 온다 — 실패한 타 뒤의 엔트리는 취소됐거나(Step 4-A) 아예 투입도 안 됐다. 사슬 단위로 `isRetry`를 매기면 그것들이 **첫 시도를 해 본 적도 없이 영원히 채점 불가**가 된다.

> `isRetry` = **그 엔트리가 `OnPatternComplete`를 실제로 한 번이라도 겪었는가.**
> **취소(`Cancelled`)는 겪은 것으로 치지 않는다** — 플레이어가 입력할 기회가 없었다.

**⚠ 콤보는 갈라진다 — 표시는 다시 쌓이고 점수는 안 쌓인다.**
실패하면 콤보가 0이 되고(지금도 Miss가 그렇게 한다) **재시도에서 화면 콤보는 다시 올라간다.** 그래야 재시도 구간 내내 0이 박혀 있지 않다. 그런데 채점의 콤보 풀은 분모가 `N(N+1)/2`로 고정이라(§12) 재시도분을 더하면 1을 넘는다(R-R5).

§12가 이미 둘을 갈라 놓아서 **구조 변경이 없다** — `Combo`/`OnComboChanged`는 HUD·`ComboPostFxView`가 보는 표시값이고, `comboSum`이 점수의 분자다:

| 값 | 재시도본에서 | 보는 쪽 |
|---|---|---|
| `Combo` · `OnComboChanged` · `OnComboTierChanged` · 앰비언트 | **오른다** | `ScoreHudView` · `ComboPostFxView` · `EffectManager` |
| `comboSum` · `perfectCount`/`goodCount`/`missCount` · `successPatterns` | **안 오른다** | 점수·등급 |
| `MaxCombo` | **안 오른다** | ⚠ 점수가 아니라 `IsPerfect()`의 `MaxCombo == totalNotes`가 쓴다 — 부풀면 **SSS가 영영 안 나온다** |

### - [x] Step 3 — 루프를 넘어 단조 증가하는 채보 시계 (R-L1·R-P1·R-R1 · **치명**)

`ChartPlayer`에 `songStarted`(bool) · `loops`(int) · `prevAudioTime`(float) · `songTime`(float) · `chartOffset`(float). `Update`의 게이트를 바꾼다(0-2의 식).

- **⚠ 되감김 감지는 `audioSource.time < prevAudioTime` 하나다.** 루프 한 바퀴를 통째로 건너뛰는 프레임은 실무상 없다(가장 짧은 루프도 수 초). 다만 **히치·일시정지 해제·씬 추가 로드 프레임**에서 검증한다(Step 12).
- **⚠ `audioSource.Pause()` 뒤 `UnPause()`에서 `time`이 이어지는지 확인한다.** Step 7의 일시정지가 이 시계를 통과한다.
- `clip.length`는 `ActiveChart.song.length`를 캐시해 쓴다(매 프레임 접근하지 않는다).
- **폴백**: 어떤 이유로든 오디오가 멈추면(`!isPlaying`) `songTime += Time.deltaTime`으로 이어 간다. 루프곡에서는 안 쓰이는 경로지만, 커스텀 모드(루프 꺼짐)와 오디오 로드 실패를 덮는다.
  - **⚠ `Time.deltaTime`이지 `unscaledDeltaTime`이 아니다.** 스폰은 판정(`Time.time`)과 짝이 맞아야 한다 — unscaled로 이으면 마무리 실루엣의 0.1배속에서 스폰만 10배 빨라진다.

### - [x] Step 4 — `ChartPlayer`가 결과를 듣고 재시도를 판단한다 (R-R2·R-R3·R-R9 · **이 계획의 심장**)

`ChartPlayer`가 `patternHandler.OnPatternComplete`를 구독한다. 새 상태: `inFlight`(진행 중 엔트리) · `chainHead`(그 엔트리가 속한 사슬의 첫 엔트리 인덱스).

**사슬 경계는 `Play()`에서 한 번 계산한다** — 사슬은 `killOnSuccess == false`가 이어지다 `true`에서 끝나는 구간이고(§11-5) 그 정보가 이미 `entries`에 있다. **새 저작 데이터가 0개**다. 비사슬 엔트리는 길이 1인 사슬이라 분기가 안 는다.

```
SetPattern 직전:  inFlight = next;  pendingEntries.RemoveAt(0);
OnPatternComplete(info):
    소비 조건: info.AllCorrect  ||  inFlight.template.HitsOnFail
      → inFlight = null;  다음 엔트리로
    아니면(재시도):
      head = chainHead가 가리키는 엔트리
      float back = CeilToBeat(chartTime - head.spawnTimes[0]);   // ⚠ 박 간격으로 올림
      chartOffset += back + retryGapBeats * beat;
      그 사슬의 엔트리 전부를 pendingEntries 맨 앞에 되돌린다 (isRetry = true)
      inFlight = null;  남은 타는 진행하지 않는다
      OnPatternRetried 발행
```

- **⚠ `OnJudgeTargetFirstMiss`를 쓰지 않는다**(R-R9). 그건 첫 미스라 *아직 만회 가능한 패턴*을 포기시킨다. 실패의 두 경로(정체 후 만료 / 마지막 노드 Miss)는 `OnPatternComplete`에서 이미 하나로 합쳐져 있다.
- **⚠ 사슬 성공 = 전 엔트리 성공이다.** `chainKillRatio`(§11-5, "성공 수 >= `ceil(길이 × ratio)`")는 **모든 타가 결국 성공하므로 항상 만족된다 — 죽은 노브가 된다.** 대안(사슬 끝에서 실제 처치 여부로 판단)은 `EnemyDirector`에 물어야 해서 계층을 넘는다. **툴이 경고로 알린다**(Step 11).
- **⚠ 사슬 중간에 실패하면 남은 타를 진행하지 않는다.** 진행시켜 봐야 어차피 사슬 머리로 되돌아가고, 그동안 적은 안 죽은 채 리액션만 반복한다.
- **⚠ 되돌린 사슬 전체에 `EnqueueCue`를 다시 부른다**(R-R2). 한 엔트리라도 빠뜨리면 재시도본이 다음 엔트리의 cue를 먹고 **전 채보가 한 칸씩 영구히 밀린다**(§11-1이 겪은 부류). 사슬 단위라 **누락 지점이 늘었다.**
- **⚠ 되감기 양을 박 간격으로 올림한다**(R-L4). `chartTime − spawnTimes[0]`은 임의 실수이고 `retryGapBeats × beat`만 더하면 **격자에서 벗어난 채로 굳는다.** 올림한 순간부터 드리프트는 영원히 박의 정수배다.
  - `beat = 60 / bpm`. **⚠ `bpm <= 0`이면 되감기를 못 한다** — 그 경우 `retryGapBeats`를 초로 읽는 폴백을 두고 경고를 찍는다(격자 보존은 포기되지만 잠기지는 않는다).
  - **마디 정렬이 필요하면** `beatsPerBar`(기본 4)를 두고 올림 단위를 마디로 바꾼다. 격자 보존에는 박이면 충분하고 프레이즈 머리에 맞추려면 마디다 — **노브 하나로 가른다.**
- **⚠ `retryGapBeats`(기본 **4박** = 1마디)는 적 방어 클립이 들어갈 창이기도 하다**(R-R3). BPM 180이면 1.33초, BPM 90이면 2.67초 — **빠른 곡에서 패링 클립이 잘린다.** 하한을 초로 하나 더 두는 것(`minRetryGapSeconds`)을 Step 12에서 실측해 판단한다.
- **적 방어 연출에는 코드가 0줄이다** — §11-2가 `Attacker.Player` 실패에 넉백 0을 돌려주고 그 0이 곧 패링이라 `Pattern.EnemyParry`가 자동으로 골라진다.
- **⚠ 되감을 때 이미 큐에 올라간 패턴이 있다**(R-R10). **언제나 정확히 최대 1개**이며(사슬이어도 같다 — 투입이 완료 0.2초 전마다 하나씩 일어난다) 그 회수는 **Step 4-A**가 맡는다.
  - **⚠ 그 "최대 1개"는 `gap ≥ 0.4초`에 기대는데, 그 값은 지금까지 강제된 적이 없다**(온셋이 음악에서 나와 실측상 그랬을 뿐이다, §3). 격자 저작에서는 즉시 깨질 수 있어 **`docs/LoopChartTool/` Step 5가 간격·노출을 저장 차단으로 강제한다** — 그래야 Step 4-A가 "1개만 회수하면 된다"는 전제 위에 설 수 있다. **툴과 런타임이 짝이다.**

### - [x] Step 4-A — 취소를 "완료의 한 종류"로 만든다 (R-R10 · **미결 3번의 답**)

되감기 시점에 큐에 올라가 있는 패턴 1개를 회수한다. **새 개념을 안 만들고 기존 완료 경로에 얹는다.**

```csharp
// PatternCompletionInfo — 필드 둘 (IsRetry는 Step 2)
public readonly bool Cancelled;

// PatternHandler — ⚠ 남아 있는 것은 전부 취소 대상이다(아래 정정)
public int CancelQueuedPatterns()
{
    while (activePatterns.Count > 0)
    {
        var p = activePatterns[0];
        activePatterns.RemoveAt(0);
        ClearNodesOf(p);                    // ⚠ 이미 있는 함수 (PatternHandler.cs:978)
        OnPatternComplete?.Invoke(new PatternCompletionInfo(
            false, ..., cancelled: true));  // ⚠ AllCorrect를 false로 못박는다(아래)
    }
    ResetPointColors(); TriggerLineFadeOut(); RefreshJudgeTargetVisuals();
    ApplyKnobVisibility(knobFadeDuration);
}
```

**⚠ 구현 중 정정된 것 둘** (계획의 스니펫이 틀렸다):

1. **선두(`index 0`)를 건너뛰면 안 된다.** 이 함수가 불리는 시점에 되감기를 결정한 패턴은
   `CompletePattern`에서 **이미 큐를 떠났고**, 그 순간 뒤에 있던 패턴이 판정 대상으로 승계됐다 —
   즉 `index 0`이 바로 취소해야 할 그 패턴이다. `i >= 1`로 두면 **취소가 통째로 no-op**이 된다.
   (이 계획 자신이 아래 "그 한 프레임 동안 취소될 패턴이 판정 대상이 된다"로 같은 사실을 적고 있었다.)
2. **`AllCorrect`를 `false`로 못박아 보낸다.** 손 안 댄 패턴은 그 값이 **`true`**(초기값)라
   그대로 흘리면 **취소가 처치·성공 연출로 읽힌다** — `EnemyDirector`가 적을 죽이고
   `HitStopDirector`가 정지를 걸고 실루엣이 터진다. 구독자별 `Cancelled` 조기 반환과 **둘 다** 둔다.
3. **회수 순서는 FIFO다**(뒤에서부터가 아니라). 구독자가 자기 큐를 앞에서 꺼내므로 역순으로 발행하면
   취소 통지와 예약이 짝이 어긋난다.

**왜 완료 이벤트로 쏘는가** — 큐 시점에 상태를 만든 구독자 셋이 **전부 `OnPatternComplete`를 이미 구독**하고 있다. 취소를 그 이벤트로 보내면 **새 배선이 0개**다:

| 구독자 | `Cancelled`에서 할 일 |
|---|---|
| `EnemyDirector` | 토큰 dequeue + `Reservation` 제거. **⚠ `ResolveReservation`의 연출은 건너뛴다** |
| `PatternEffectDirector` | 그 패턴의 예약 큐 폐기 |
| `SliceTargetDirector` | 표적 회수 |
| `ScoreDirector` · `FinaleSilhouetteDirector` · `HitStopDirector` · `CameraDirector` · `EffectManager` | 즉시 return |

- **⚠ 연출 생략이 핵심이다.** 그냥 "실패"로 흘리면 **오지도 않은 칼에 적이 패링 모션을 한다.**
- **⚠ `OnPatternComplete` 디스패치 안에서 부르면 안 된다.** `EnemyDirector.HandlePatternComplete`도 같은 이벤트 구독자인데 구독 순서가 보장되지 않아, 실패 패턴의 토큰을 **아직 못 꺼낸 상태에서 취소가 들어오면 엉뚱한 토큰이 버려진다.** `ChartPlayer`는 핸들러에서 플래그만 세우고 **자기 `Update` 첫머리**에서 부른다(R-S1이 세운 규칙과 같은 것).
- **⚠ 그 한 프레임 동안 취소될 패턴이 판정 대상이 된다.** `CompletePattern` 끝의 `RaiseJudgeTargetBegan()`이 이미 돌아 `CharacterActionPlayer`가 그 패턴의 베기를 예약해 둔다 — 취소할 때 **그 예약도 끊는다**. `PlaySlot(continuesSequence: false)` 경로가 이미 그 일을 하므로(§6-1) 새 로직이 아니라 호출 하나다.

**곁들이는 조기 억제 (거의 공짜, 빈도만 줄인다)**
`OnJudgeTargetFirstMiss`가 뜨는 순간 `AllCorrect`는 다시 참이 될 수 없다 — 공격 패턴에서 이게 뜨면 **다음 엔트리 투입을 멈춘다.** 초반 노드 미스는 대부분 여기서 걸려 취소 자체가 안 일어난다.
- **⚠ 마지막 노드 미스는 못 막는다**(완료 0.1초 전이라 이미 투입된 뒤). **대체재가 아니라 빈도 저감**이다.
- **⚠ R-R9와 충돌하지 않는다.** 여기서는 *결정*이 아니라 *투입 보류*만 한다. 재시도 결정은 여전히 `OnPatternComplete`가 한다.

**기각 — "겹침을 끈다"**
`inFlight != null`이면 투입을 보류하는 안은 **판정 시각을 민다.** `ActivePattern.StartTime`이 투입 시각이고 `inputTimes`가 그 기준 상대값이라(PatternHandler.cs:377) 보류 δ가 곧 판정 지연 δ이고, 모든 엔트리가 0.2초씩 밀려 **채보 전체가 영구 오프비트**가 된다 — §2-A의 박 정렬이 지키려던 바로 그것이 깨진다. 상대 시각에서 δ를 빼 보정할 수는 있으나 **첫 링의 수축이 0.5 → 0.3초로 짧아진다**(경고 시간 40% 감소).

**확장 포인트 — `ChartPlayer.OnPatternRetried(SongChartEntry head, int attempt)`**
지금은 **구독자가 0**이다(결정: 재시도 피드백은 나중에 붙인다). `CharacterActionPlayer.OnSwingBegan`처럼 **발행만 하고 비워 두는 확장 포인트**이며, 여기에 전용 SFX·HUD·화면 효과가 붙는다. `attempt`(몇 번째 시도인가)를 같이 싣는 이유는 나중 연출이 *"또 실패했다"*를 구분할 유일한 수단이라서다 — 나중에 추가하면 시그니처가 깨진다.
  - → **⚠ 이것이 이 계획에서 가장 큰 미결 지점이다.** 아래 「피드백 자리」 3번.

### - [x] Step 5 — 종료 상태기 (R-P2·R-P3·R-P4·R-S2·R-S4)

`RaiseSongEndedIfFinished`를 `TickFinish`로 교체. 새 필드 `finishAt`(`NaN` = 아직 아님).

```
Loop 모드: cleared = pendingEntries 0 && inFlight == null && !handler.HasActivePatterns
Linear 모드:        cleared = pendingEntries 0 && !audioSource.isPlaying          (지금과 동일)

cleared 첫 프레임:  finishAt = Time.time + outroHold
finishAt 경과 && !(finale != null && finale.IsBusy):
    1) 오디오 페이드 아웃(outroFade, 기본 0.35초) → 끝나면 audioSource.Stop()
    2) enemyDirector.DissolveAll()
    3) OnSongEnded 발행 (songEndRaised 가드 유지)
```

- **⚠ 루프곡에서는 `audioSource.Stop()`이 선택이 아니다**(R-L2). `loop = true`면 곡이 스스로 안 끝나므로 이 경로가 유일한 정지 지점이다.
- **⚠ `Linear` 모드의 조건은 루프를 끈다는 전제 위에서만 성립한다**(Step 6이 그것을 보장한다).

- **⚠ `Stop()`을 재사용하지 않는다**(R-P3). `Stop()`은 `ClearAllPatterns`를 부르고 그것이 실루엣을 죽인다. 여기서는 **오디오만** 멈춘다(패턴 큐는 이미 비어 있다).
- **⚠ `DissolveAll()`은 실루엣이 끝난 뒤다**(R-P2). 순서가 요구 그 자체다.
- 페이드는 `audioSource.volume`을 직접 대입하지 않는다 — `ApplyMusicVolume()`이 볼륨의 주인이라(MusicPlayerBase.cs:84) 페이드 중 `OnVolumeChanged`가 오면 값이 되돌아간다. **페이드 계수를 하나 두고 `ApplyMusicVolume`이 곱하게** 한다.
- `finale` 참조는 선택 배선이다. 비면 대기가 없고 예전처럼 동작한다.

### - [x] Step 6 — 모드와 가드 (R-M1·R-M3·R-T4·R-P5)

- `ChartGen`에 `public enum StageMode { Loop, Linear }`.
  - **`Loop`** — 곡이 무한 루프하고, 공격 실패는 재시도하며, **모든 엔트리가 소비되면** 끝난다. 기본값.
  - **`Linear`** — 곡이 1회 재생되고, 재시도가 없으며, **곡이 끝나면** 끝난다. 지금 동작 그대로. 커스텀 모드용.
  - **⚠ 이름이 종료 조건이 아니라 곡의 구조를 가리킨다.** 세 가지(종료·재시도·`loop`)가 한 값에 묶여 있어 그중 하나를 이름에 쓰면 나머지 둘이 숨는다. `Loop`/`Linear`는 **곡이 어떻게 생겼는가**를 말하고 나머지가 거기서 따라온다.
- `ChartPlayer`에 `[SerializeField] private StageMode stageMode = StageMode.Loop;`
- `GameSession.StageModeOverride`(`StageMode?`, 기본 `null`) — 있으면 그것을, 없으면 인스펙터 값을 쓴다(`ActiveChart`가 `SelectedChart ?? debugChart`인 것과 같은 관용구).
- **⚠ §9의 `GameMode`(Story/FreePlay)와 합치지 않는다.** 합치면 *"자유 연주는 반드시 `Linear`"*가 코드에 박혀 나중에 못 뗀다. 둘은 직교하고, `GameMode`가 생기면 **`StageMode`의 기본값을 정해 주는 관계**로만 엮는다.
  - **⚠ `SongChart`에 두지 않는다**(R-M1). 같은 곡을 두 모드로 플레이하는 것이 요구라 채보에 박으면 커스텀 모드가 스토리 채보를 못 쓴다.
- **⚠ 모드 값 하나가 셋을 정한다**(R-M3) — 종료 조건 · 재시도 활성 · **`audioSource.loop`**. `Play()`에서 `audioSource.loop = (stageMode == StageMode.Loop)`를 **코드가 대입한다.**
  - 지금 `loop`는 **씬의 직렬화 값**이다(BattleScene의 `Loop: 0`). 코드가 안 건드리면 씬마다 어긋난 채로 굴러가고, **`Linear` + `loop = true`면 스테이지가 영영 안 끝난다.**
  - `MusicPlayer`(배경음)가 이미 `Awake`에서 `loop = true`를 대입하는 선례가 있다(MusicPlayer.cs:32).
- **빈 채보 가드**: `Play()`에서 `entries == null || Length == 0`이면 기존 에러 가드들과 같은 자리에서 `LogError` 후 return(R-T4).
- **튜토리얼 가드**: 종료 판정·재시도 판단 모두 `songStarted`일 때만 돈다. 튜토리얼은 `Play()`를 안 부르므로 `pendingEntries == null`이고 `Update` 첫 줄에서 return한다(R-P5). **드릴의 자체 재시도와 절대 겹치지 않는다.**

### - [x] Step 7 — 일시정지 / 포기 (R-R7 · **신규 기능 · 이것이 없으면 갇힌다**)

무한 재시도가 설계상 정상이므로 **플레이어가 나가는 수단이 필수**다. 코드베이스에 일시정지가 0건이라 새로 만든다.

- `PauseDirector`(`UI/`) — `CameraDirector`·`HitStopDirector`와 같은 관례(순수 소비자, 배선이 비면 조용히 비활성).
- **`Time.timeScale = 0` + `audioSource.Pause()`를 같이 건다.**
  - **⚠ §7-3의 금지와 충돌하지 않는다.** 그 금지의 근거는 *"게임 시계만 느려지고 오디오는 안 느려져 차이가 영구 누적된다"*인데, **오디오도 같이 멈추면 차이가 안 생긴다.** §14가 "판정이 남아 있지 않다"로 예외를 얻은 것과 **다른 근거로** 얻는 예외다 — CLAUDE.md에 그 경계를 적는다.
  - **⚠ 복구 주체가 §14와 겹친다**(R-S5). 실루엣이 0.1을 걸어 둔 상태에서 일시정지하면 해제가 1로 덮는다. **`FinaleSilhouetteDirector.IsBusy`면 일시정지를 막는다**(Step 1의 게터를 재사용 — 새 상태가 0개).
- **포기**: `BattleSceneBootstrap`에 `Abandon()` 공개 메서드. `HandleDepleted`와 **같은 처리**다 — 곡을 끊고 아무것도 기록하지 않고 복귀(§9의 "중단에서 등급이 새어 나가면 `Fresh`가 `Retry`로 바뀐다"). 새 분기가 0개.
- 입력은 `Esc`. **⚠ `EventSystem`의 입력 모듈이 씬마다 다르다**(§10) — 전투 씬은 `InputSystemUIInputModule`.

### - [x] Step 8 — `ScoreDirector` 이중 확정 가드 (R-M2)

`HandleSongEnded`에 `if (finalized) return; finalized = true;`, `ResetRun`에서 해제. `BattleSceneBootstrap.resolved`와 같은 성격이다.

### - [x] Step 9 — 등급 기록을 한 프레임 미룬다 (R-S1·R-C2)

`BattleSceneBootstrap.HandleSongEnded`를 코루틴으로 바꿔 `yield return null` 뒤에 `GameProgress.ReportGrade(...)`를 부른다(`resolved` 가드는 지금 자리에서 **즉시** 세운다).

- 근거: `session.LastResult`는 `ScoreDirector.HandleSongEnded`가 채우는데 두 클래스의 구독 순서가 씬 오브젝트 순서라 **보장이 없다.**
- `outroHold`(Step 5)는 *마지막 패턴 완료 뒤의 여유*이지 **같은 호출 흐름 안의 순서를 바꾸지 않는다.** 둘은 다른 문제다.

> **⚠ Step 10·11은 두 모드가 공유하는 `Save()`/엔트리 행에 구현됐다.** 검증 함수는
> `PatternChartWindow.ValidateForSave`(`docs/LoopChartTool/` Step 5와 **같은 코드**)이고,
> 역할 뱃지·요약은 `DrawChartSummary`/`DrawEntryRow`에 있다 — 아래 표의 항목이 전부 그 안에 있다.
>
> **⚠ 원래 이 절은 `Linear` 모드(현행 툴) 기준이었다.** `Loop` 모드는 굽기 방식을 통째로 새로 짜며
> **온셋 분석이 근거를 잃는다**(드리프트가 트랜지언트와의 대응을 지운다). 그 설계와 검증은
> **`docs/LoopChartTool/`**이 든다 — 여기 아래 검증들도 그쪽에서 격자 저작 형태로 다시 강제된다.

### - [x] Step 10 — 굽기 툴(Linear): 저장을 막는 검증 (R-T1·R-T4)

`Save()`의 기존 검증 옆에 셋을 더한다. 저장 중단은 앞의 둘, 마지막은 경고만:

| 검사 | 처리 | 메시지 |
|---|---|---|
| `drafts.Count == 0` | 중단 | `빈 채보는 저장할 수 없습니다.` |
| 마지막 엔트리의 `template.Attacker != Player` | 중단 | `마지막 엔트리는 공격 패턴이어야 합니다. 방어 패턴이면 실패해도 소비되어, 마무리 실루엣 없이 스테이지가 끝납니다.` |
| `spawnTimes[0] < 0` | 중단 | `{i}번 그룹의 스폰 시각이 음수입니다({t:F2}s). 노출시간이 온셋보다 깁니다.` |
| `onsetTimes[^1] > clip.length` | 경고 | `{i}번 그룹이 곡 길이({len:F2}s)를 넘습니다. 루프 2회차에 나옵니다.` |

- **⚠ 툴은 씬 `PatternHandler`를 계속 요구하지 않는다**(§4가 그 의존을 없앴다). `goodWindow`를 모르므로 `Deadline`을 계산하지 않는다 — 검증은 `onsetTimes`/`spawnTimes`와 `clip.length`만으로 한다.

**루프 규격 검사를 같이 넣는다**(R-T7·R-L3 — **코드로 고칠 수 없는 유일한 위험이라 여기서만 잡힌다**):

```
bars = (clip.length − beatOffset) / (60/bpm × beatsPerBar)
|bars − round(bars)| × 마디길이 가 toleranceMs(기본 15ms)를 넘으면 경고
```

메시지: `곡 길이가 마디의 정수배가 아닙니다(N.NN마디, 오차 M ms). 루프할 때마다 음악이 채보 격자에서 그만큼 밀리고 바퀴마다 누적됩니다 — 노트 위치는 그대로라 "판정이 이상하다"로 보입니다.`

- **⚠ 경고이지 차단이 아니다.** 커스텀 모드(루프 꺼짐)에서는 상관없는 값이고, 툴은 어느 모드로 쓰일지 모른다.
- **⚠ `AudioClip`의 Compression Format이 이 값을 바꾼다**(R-L3). 경고에 한 줄 덧붙인다 — `압축 포맷의 패딩이 원인일 수 있습니다(PCM으로 확인하세요).`

### - [x] Step 11 — 굽기 툴(Linear): 새 규칙을 숫자로 보여 준다 (R-T2·R-T3·R-T5·R-T6)

`Attacker`가 이제 **진행 규칙의 입력**이므로(Research §8-4) 채보 화면에서 읽혀야 한다.

- 상단 요약: `패턴 12개 (공격 8 · 방어 4) · 마지막 판정 1:42.6 / 곡 1:50.2 · 꼬리 7.6초`
- 엔트리 행 배지: **`재시도`**(`Attacker.Player`) / **`통과`**(`Attacker.Enemy`). 지금은 회색 읽기 전용 텍스트뿐이라 한눈에 안 들어온다.
- **마지막 엔트리는 공격 패턴으로 강제된다**(Step 10의 저장 차단). 그 대가와 이득을 행에 적는다 — `실패하는 동안 스테이지가 끝나지 않습니다. 대신 마무리 실루엣이 반드시 성공에서 나옵니다.`(R-T3)
  - **⚠ 이 강제가 R-S3을 소멸시킨다.** "마지막 엔트리가 방어라 실패해도 끝나는데 실루엣이 없다"는 경우가 **표현 불가능**해진다 — 분기가 아니라 데이터로 막히므로 런타임에 대비 코드가 0줄이다.
  - **⚠ `Attacker`는 패턴이 소유해 툴이 못 고친다**(§5). 그래서 자동 보정이 아니라 **저장 차단 + 안내**가 유일한 수단이다.
- 꼬리(`clip.length − onsetTimes[^1]`)가 `tailWarnSeconds`(기본 3초)를 넘으면 표시. 파형에 마지막 온셋 세로선.
- 사슬 안 역할 혼재 경고에 한 줄 덧붙인다 — `사슬 안의 공격 타는 개별 재시도됩니다(chainKillRatio가 사실상 무의미해집니다).`(R-T5·R-R4)

### - [ ] Step 12 — 검증(수동 시나리오) — **미실행: 에디터에서 사람이 돌려야 한다**

`BattleScene` + 실채보. **오토플레이(§8)는 실패 경로를 못 만든다** — 재시도 경로 검증에 쓸 수 없다.

- [ ] 공격 패턴 실패 → **적 방어 모션이 온전히 나오고** `retryGapBeats` 뒤에 같은 패턴이 다시 나온다 (R-R3)
- [ ] 재시도 성공 → 다음 엔트리가 **원래 간격 그대로** 이어진다 (R-R1)
- [ ] 재시도를 3회 반복 → 뒤따르는 엔트리가 쏟아지지 않는다 (R-R1)
- [ ] **재시도를 10회 반복해도 패턴이 박에 맞는다** — 곡을 들으며 육안 확인 (R-L4)
- [ ] 곡이 **2바퀴 이상 돌아도** 채보가 처음으로 안 돌아가고 판정이 안 어긋난다 (R-L1·R-L3)
- [ ] BPM이 빠른 곡(180+)에서 적 패링 클립이 **안 잘린다** (R-R3의 `minRetryGapSeconds` 필요 여부 판단)
- [ ] `bpm = 0`인 채보 → 잠기지 않고 경고와 함께 진행된다 (R-L4)
- [ ] 재시도한 패턴의 적이 **원래 배정된 그 적**이다 (R-R2)
- [ ] 재시도 노트로 **점수는 안 오르는데 화면 콤보는 다시 쌓인다**, 첫 시도의 Miss가 결과에 남는다 (R-R5)
- [ ] 재시도를 섞어도 퍼펙트 플레이가 **SSS를 받는다** — `MaxCombo` 오염 확인 (R-R5)
- [ ] 곡 중반에 재시도를 여러 번 해도 **마무리 실루엣이 안 터진다** (R-R6)
- [ ] 방어 패턴 실패 → 대미지 들어가고 **그대로 넘어간다** (신규 코드 0 확인)
- [ ] **상호 공격 실패 → 맞고 넘어간다**(재시도 안 함) — `HitsOnFail` 확인
- [ ] 기습 회피 실패로 맞아도 **그 패턴의 재시도 판단이 안 바뀐다**
- [ ] **사슬 중간 실패 → 사슬 머리부터 전체가 다시 나온다**, 남은 타는 진행하지 않는다
- [ ] 사슬 재시도에서 **적이 처음부터 다시 배정된다**(cue 전체 재투입 확인, R-R2)
- [ ] **취소된 패턴의 링이 회수되고** 이펙트·표적이 허공에 안 남는다 (R-R10)
- [ ] 취소 직후 **적이 패링 모션을 하지 않는다**(연출 생략 확인, Step 4-A)
- [ ] 취소를 여러 번 반복해도 **적 배정이 한 칸씩 안 밀린다** — 토큰 큐 정합 (R-R10)
- [ ] **취소된 뒤 다시 나온 엔트리는 채점된다**(`isRetry`가 안 붙는다, Step 2)
- [ ] 마지막 노드에서 미스 → 조기 억제가 못 막고 **취소 경로를 탄다** (Step 4-A)
- [ ] 연타 실패 → 창 전체가 다시 나온다
- [ ] 마지막 패턴(공격) 성공 → 실루엣이 온전히 재생된 **뒤** 씬 전환 (R-S4)
- [ ] 굽기 툴이 **마지막 엔트리가 방어인 채보의 저장을 막는다** (R-S3 소멸 확인)
- [ ] 종료 시 **곡이 실제로 멈춘다**(루프라 스스로 안 끝난다) (R-L2)
- [ ] 일시정지 → `UnPause` 뒤 루프 감지가 **오작동하지 않는다** (R-L1)
- [ ] 목숨 0 → 등급·완곡 플래그가 **안 남는다**
- [ ] 일시정지 → 곡과 판정이 **같이** 멈춘다. 해제 후 판정이 안 어긋난다 (R-R7)
- [ ] 실루엣 중에는 일시정지가 **막힌다** (R-S5)
- [ ] 포기 → 기록 없이 복귀
- [ ] 튜토리얼 씬 → 드릴이 이 시스템과 **아무 상호작용도 안 한다** (R-P5)
- [ ] `stageMode = Linear` → 재시도 없이 지금과 완전히 같다

### - [x] Step 13 — 문서

- `CLAUDE.md` §2(판정)·§5(`ChartPlayer`)·§7-3(timeScale 금지의 **경계**가 하나 늘었다)·§9·§12·§14 갱신.
- 새 절 후보: **§16 진행과 재시도** — `Attacker`가 연출이 아니라 진행을 가른다는 사실이 지금 어느 절에도 없다.
- 이 문서의 단계 체크 갱신.

---

## 손대지 않는 것 (이번 범위 밖)

| 위험 | 왜 미루는가 |
|---|---|
| `chainKillRatio` 폐기 (R-R4의 잔여) | 사슬 전체 재시도로 **모든 타가 결국 성공**하므로 죽은 노브가 된다. 필드를 지우는 것은 별개 작업이고, 지금은 **툴이 경고**만 한다(Step 11) |
| R-R8 (연타 재시도 비용) | 재시도 대상으로 유지한다(예외 0개). 창이 길어 비용이 클 뿐이고 툴이 이미 초를 찍는다 |
| R-C3 (재시도 간격의 기습) | `DodgeDirector`가 재시도를 알게 하면 §11-8이 채보를 알게 되는 역류가 생긴다. 먼저 플레이해 보고 판단 |
| 커스텀 모드 UI/씬 배선 | 값을 읽을 자리만 만든다(Step 6) |
| 결과 화면 | `GameSession.LastResult`는 여전히 읽는 쪽이 없다(§12) |

---

## 피드백 자리

### 확정된 결정 (이 줄 위의 본문에 전부 반영됨)

| # | 결정 | 그래서 |
|---|---|---|
| 1 | 소비 기준은 **대미지**(역할 아님) | 상호 공격 실패는 **맞고 통과**. `Pattern.HitsOnFail` 게터 하나, 새 구독 0개 |
| 2 | 연타도 **재시도 대상** | 예외 0개 |
| 3 | **사슬 전체** 재시도 | `chainKillRatio`가 죽은 노브가 된다(인정, 툴 경고) |
| 4 | 모드 이름 **`StageMode { Loop, Linear }`** | §9의 `GameMode`와 합치지 않는다 |
| 5 | 재시도 피드백은 **이벤트만** 발행 | `OnPatternRetried(head, attempt)`, 구독자 0 |
| 6 | 마지막 엔트리는 **공격으로 강제** | 저장 차단. **R-S3이 소멸한다** |
| 7 | 실패하면 **콤보 삭제 후 재적립** | 표시 콤보는 오르고 채점 콤보는 안 오른다(§12가 이미 갈라 둠) |

---

### 아직 열린 것

> `>>>` 로 남겨 주면 반영해서 다시 씁니다.
>
> 1. **`retryGapBeats` 4박(1마디) · `outroHold` 1.5초** — 실측으로 잡아야 하는 두 숫자입니다. 특히 BPM이 빠른 곡에서 4박이 적 패링 클립보다 짧아지면 초 단위 하한을 하나 더 둬야 합니다(그 순간 드리프트의 박 정수배 보장이 깨지므로, **하한을 박 단위로 올림해서** 쓰는 형태가 됩니다).
> 2. **곡 에셋이 처음으로 플레이 가능성의 전제가 됩니다** — `clip.length`가 마디의 정수배여야 하고(R-L3), 압축 포맷 패딩도 같은 결과를 냅니다. 툴이 경고는 하지만(Step 10) **고칠 수는 없습니다.** 기존 곡들을 이 규격으로 다시 뽑을 계획이 있는지에 따라 `toleranceMs`를 얼마나 엄하게 잡을지가 갈립니다.
>
**3번(패턴 겹침)은 Step 4-A로 닫혔습니다** — 코드를 다시 읽어 전제 셋이 틀렸음을 확인했고, 그 결과 **겹침을 끄지 않는 쪽이 답**이 됐습니다:
>
> - 회수 원시 함수가 **이미 있다**(`ClearNodesOf`, PatternHandler.cs:978). "API가 없다"는 오판이었습니다.
> - 걸려 있는 패턴은 **사슬 길이가 아니라 언제나 최대 1개**입니다(투입이 완료 0.2초 전마다 하나씩 일어납니다).
> - **겹침을 끄면 판정 시각이 밀립니다** — `StartTime`이 투입 시각이라 보류가 곧 판정 지연이고, 채보 전체가 영구 오프비트가 됩니다. 이게 그 안을 탈락시켰습니다.
>
> 남은 두 개(`retryGapBeats`·`outroHold` 실측 / 곡 마디 규격 범위)는 **구현하면서 잡을 수 있는 숫자**라, 지금 상태에서 구현에 들어갈 수 있습니다.
