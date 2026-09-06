# Research — 튜토리얼 마무리 실루엣 · 페이드 · 텍스트

## 0. 요구

튜토리얼 마지막 허물을 벨 때 `BattleScene`과 같은 마무리 실루엣(배경 빨강 · 배우 검정)을 내고,
그 직후 화면을 검게 페이드 아웃 → 다시 페이드 인 → 텍스트를 출력한다.

---

## 1. 이미 있는 것 — 코드는 하나도 새로 만들 필요가 없다

### 1-1. `FinaleSilhouetteDirector` (§14, `02. Scripts/Effect/`)

연출 자체는 완성돼 있다. **튜토리얼 씬에도 이미 놓여 있고 배선이 전부 채워져 있다**(실측):

| 필드 | 값 |
|---|---|
| `handler` | `Canvas/PatternHandler` |
| `chartPlayer` | `ChartPlayer` |
| `enemyDirector` | `EnemyDirector` |
| `playerRoot` | `Char_School_Katana_FullBody-Magica cloth2` |
| `hitStopDirector` | `Main Camera` |
| `scoreHud` | `Canvas/ScoreHud` |
| `renderers` | `PC_Renderer`, `Mobile_Renderer` (둘 다) |
| `silhouetteLayerName` | `Silhouette` → 레이어 인덱스 **10** (존재함) |
| `holdDuration` / `slowTimeScale` / `audioPitchScale` | 1.5 / 0.1 / **0.5** |

렌더러 피처도 양쪽 렌더러에 `FinaleBackground` · `FinaleActors`가 `RenderObjects`로 존재하며 `active=False`(휴지)다.

### 1-2. `ScreenFader` (`02. Scripts/UI/`)

- `Singleton<ScreenFader>`, `DontDestroy = true` — 슬롯 배선이 필요 없다.
- `FadeOut()` / `FadeIn()` 둘 다 **`IEnumerator`**이고 **`Time.unscaledDeltaTime`**으로 보간한다 → 슬로우모션 중에도 실시간으로 돈다.
- 튜토리얼 씬 루트에 `ScreenFader` 오브젝트가 이미 있다.

### 1-3. 시퀀스

`Assets/04. Datas/Sequences/Tutorial/Seq_Tutorial.asset` — 28스텝, `holdMode = Keep`. 끝부분:

```
23: HighlightStep  "아무 점이든 빠르게 연타"
24: PatternDrillStep   ← 마지막 드릴(연타). 이때 마지막 허물이 죽는다
25: HighlightStep      (하이라이트 해제)
26: DialogStep     허물 "...미오..." / 미오 "……방금, 제 이름을 부른 것 같은데요."
                   / 카시마 "...신경쓰지 않아도 돼." / "가끔 저런 변종이 나올 때가 있어."
27: UnlockStep     flagName = "tutorial_done"
```

`requiredBindings`: `PatternHandler` · `PatternInput` · `Point5` · `TutorialDirector` · `CamCall` · `CamArrival` · `EnemyDirector`.

---

## 2. 왜 지금은 안 나오는가 — 원인은 하나뿐

`FinaleSilhouetteDirector.HandlePatternComplete`의 게이트:

```csharp
if (totalPatterns <= 0 || completedPatterns < totalPatterns) return;
```

`totalPatterns`는 `ResolveTotalPatterns()` → `chartPlayer.ActiveChart.entries.Length`에서 온다.
**튜토리얼 씬에는 채보가 없다**(`ChartPlayer`는 꺼져 있고 드릴은 `PatternDrillStep`이 `Time.time`으로 직접 낸다 — `TutorialDirector` 헤더 주석이 그 사실을 명시한다).
따라서 `totalPatterns == 0`이라 **게이트가 언제나 막는다**.

즉 연출·배선·레이어·렌더러 피처가 전부 준비된 상태에서 **"지금이 마지막이다"를 말해 줄 사람만 없다.**

### 2-1. 채보 카운트를 튜토리얼에 이식할 수 없는 이유

드릴은 **실패하면 같은 그룹을 다시 낸다**(`PatternDrillStep`의 `retryAt` 재시도 경로).
그래서 `completedPatterns`가 총 패턴 수를 넘길 수도, 못 채울 수도 있다 —
**완료 횟수를 세는 방식은 튜토리얼에서 성립하지 않는다.**
"마지막"을 아는 것은 채보가 아니라 **시퀀스의 저작 순서**다.

---

## 3. 시퀀스에서 무언가를 지시하는 기존 관용구

`TutorialCueStep`(`SequenceSpace`)이 이미 그 자리다 — `TutorialDirector`의 상태 하나를 바꾸고 **기다리지 않는다**(`IsFinished`가 언제나 true, `HighlightStep`과 같은 관용구). 현재 `Action`:

```
PrepareStage, RunOn, RunOff, ArmSlowMo, RunTo, ArmAmbush
```

> ⚠ 주석이 못박고 있다 — **새 값은 반드시 enum 끝에 붙인다.** 명시 정수가 없어 서수가 곧 직렬화 키라,
> 중간에 끼우면 저작해 둔 시퀀스의 뒤쪽 값이 통째로 한 칸씩 밀린다(실제로 `RunTo`를 중간에 넣었다가
> `RunOff`가 `RunTo`로 읽힌 사고가 있었다).

`TutorialDirector`는 "이 씬에서만 참인 반칙"을 모아 두는 글루이며 `DodgeDirector`·`EnemyDirector`·`CharacterActionPlayer` 참조를 이미 필드로 들고 있다 — **참조를 하나 더 드는 것이 이 클래스의 기존 성질과 어긋나지 않는다.**

---

## 4. 시각의 문제 — 페이드를 언제 시작하는가

세 시각이 다르다:

| 사건 | 시각 |
|---|---|
| 패턴 완료(`OnPatternComplete`) | 마지막 노드 입력 시점 |
| 칼이 닿는 순간 = 실루엣 발사(`Fire`) | `info.ImpactTime()` = `Deadline + ImpactOffset` |
| 실루엣 종료(`Restore` / `OnFinaleEnded`) | 발사 + `holdDuration`(실시간 1.5초) |

`PatternDrillStep.IsFinished`는 **패턴 완료 시점**에 true가 된다 → 시퀀스는 실루엣이 터지기 **전에** 다음 스텝으로 넘어간다.
그대로 페이드를 걸면 **실루엣이 검은 화면에 덮여 한 프레임도 안 보인다.**

### 4-1. `WaitStep`으로 때울 수 없다

`SequenceRunner.ElapsedInStep += Time.deltaTime` — **스케일된 시계**다.
실루엣이 `Time.timeScale = 0.1`을 걸므로 `WaitStep 1.5s`는 **실시간 15초**가 된다.
반면 `ScreenFader`는 unscaled라 이 둘을 섞으면 저작값이 화면과 어긋난다.

→ 페이드 스텝은 **`OnFinaleEnded`를 기다리거나 unscaled로 재야 한다.**

---

## 5. 텍스트 출력

`DialogStep`(26번)이 이미 그 자리이며 `DialogUI.Instance`를 통해 대사 창을 연다.
"페이드 인 후 텍스트"는 **스텝 순서를 바꾸는 저작 문제**이지 새 코드가 아니다.

⚠ 다만 26번 대사는 허물이 죽으며 말을 거는 대사다(`"...미오..."`). 이것을
"페이드 후에 나오는 텍스트"로 그대로 쓸지, 별도의 새 대사를 넣을지는 **서사 판단**이며
`docs/Story/Story_Script.md`가 진실의 원천이다 — Plan의 열린 질문으로 남긴다.

---

## 6. 확인된 제약 정리

1. `FinaleSilhouetteDirector`는 **한 줄도 고칠 필요가 없다** — 게이트를 여는 진입점만 없다.
2. 새 `TutorialCueStep.Action` 값은 **enum 끝**에 붙여야 한다.
3. 페이드 스텝은 **unscaled** 시계이거나 `OnFinaleEnded`를 기다려야 한다.
4. `ScreenFader`는 싱글톤이라 **슬롯이 필요 없다**(`requiredBindings`를 안 늘린다).
5. 실루엣의 `Restore`는 세 경로(노출 종료 · `OnAllPatternsCleared` · `OnDisable`)에서 불린다 —
   새 진입점을 만들어도 **복구 경로는 늘리지 않는다**.
6. `PatternDrillStep`은 성공/실패를 반복할 수 있으므로 "무장은 실제로 발동할 때까지 남는다"는
   `ArmAmbush`의 성질을 그대로 따라야 한다(무장 → 다음 성공에서 발사).
