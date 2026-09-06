# Plan — 튜토리얼 마무리 실루엣 · 페이드 · 텍스트

근거: `docs/TutorialFinale/Research_TutorialFinale.md`
피드백 1회 반영(열린 질문 3건 확정 + 검은 화면 동안의 무대 정리 추가).

## 확정된 답

| 질문 | 답 |
|---|---|
| 페이드 인 뒤의 텍스트 | **기존 26번 `DialogStep` 그대로**(허물/미오/카시마) |
| 페이드 인 뒤의 화면 | 미오가 **무대 중앙**에 서 있고, **그 앞에 적 파편**이 놓여 있다 |
| `holdDuration`/`slowTimeScale`/`audioPitchScale` | 튜토리얼도 씬 값 그대로(1.5 / 0.1 / 0.5) |
| 검은 화면 동안 | **`Rift` 비활성화** — 페이드 인 뒤로는 안 보인다 |

## 최종 화면 흐름

```
마지막 연타 드릴 성공
  → (ImpactTime) 실루엣: 배경 빨강 · 배우 검정 · 슬로우 0.1배 · 1.5초
  → 페이드 아웃(검정)
      ├ Rift 끈다
      ├ 파편 켠다 (무대 중앙, 미오가 볼 자리)
      └ 미오를 무대 중앙으로 옮기고 파편 쪽을 보게 한다
  → 페이드 인
  → 26번 대사 출력
  → UnlockStep tutorial_done
```

## 설계 요지

연출·배선·레이어·렌더러 피처는 **전부 이미 있다**(Research §1). `FinaleSilhouetteDirector`의
발사·복구·레이어 스왑·렌더러 피처 로직은 **한 줄도 안 고친다** — 게이트만 우회하므로
§14의 복구 경로 셋이 그대로 유효하다.

신규 코드는 **메서드 하나 · enum 값 하나 · 스텝 클래스 두 개 · 기존 스텝 필드 둘**이다.
검은 화면 동안의 무대 정리(Rift off · 파편 on · 미오 이동)는 **전부 저작**이고 새 런타임 로직이 아니다.

---

## Step 1 — `FinaleSilhouetteDirector.ArmNextSuccess()`

- [x] `public void ArmNextSuccess()` 추가. 필드 `armedByRequest`(bool) 하나를 세운다.
- [x] `HandlePatternComplete`의 게이트를 **한 줄만** 넓힌다:
      `if (!armedByRequest && (totalPatterns <= 0 || completedPatterns < totalPatterns)) return;`
      그 아래 `AllCorrect` 검사·`fireTime` 계산·히트스톱 억제는 **그대로 통과**한다.
- [x] 게이트를 통과한 순간 `armedByRequest = false`로 내린다(한 번만 터진다 — `ArmAmbush`와 같은 성질).
- [x] **실패 완료(`AllCorrect == false`)에서는 무장을 유지한다.** 드릴은 실패하면 같은 그룹을
      다시 내므로(Research §2-1), 여기서 내리면 한 번 놓친 플레이어는 마무리 연출을 영영 못 본다.
- [x] `ResetRun`에서 `armedByRequest = false`(전투 씬 재도전에서 무장이 새는 것을 막는다).

> ⚠ `Time.timeScale`을 건드리는 §14의 안전 근거("판정이 남아 있지 않다")는 이 경로에서도 참이다 —
> 무장은 저작자가 **마지막 드릴 앞**에 두는 것이고 그 뒤에는 드릴이 없다. Step 6의 저작 위치가 곧 그 보장이다.

## Step 2 — `TutorialDirector.ArmFinale()` 통로

- [x] `[SerializeField] private FinaleSilhouetteDirector finaleDirector;`
      툴팁: "비우면 마무리 실루엣만 조용히 비활성된다".
- [x] `public void ArmFinale()` → `finaleDirector?.ArmNextSuccess();`
- [x] 씬의 `TutorialDirector`에 `FinaleSilhouetteDirector` 배선.
- [x] `public FinaleSilhouetteDirector Finale => finaleDirector;` — Step 5의 `ScreenFadeStep`이
      `OnFinaleEnded`를 구독할 통로다(`requiredBindings`를 안 늘리기 위해 이 글루를 거친다).

## Step 3 — `TutorialCueStep.Action.ArmFinale`

- [x] enum **맨 끝**에 `ArmFinale` 추가(⚠ 중간 삽입 금지 — 명시 정수가 없어 서수가 곧 직렬화 키다).
- [x] `switch`에 `case Action.ArmFinale: director.ArmFinale(); break;`
- [x] `IsFinished`는 손대지 않는다 — `RunTo`만 기다리는 기존 식이 그대로 맞다.

## Step 4 — `SetActiveStep` (신규 스텝 1개)

`Rift`를 끄고 파편을 켜는 데 둘 다 쓴다. **한 클래스로 두 일을 한다** — 켜고 끄는 것은 같은 동작이다.

- [x] `Assets/02. Scripts/SequenceSystem/Steps/SetActiveStep.cs`, `namespace SequenceSpace`.
- [x] 필드: `[SequenceSlot] string targetSlot` + `bool active`.
- [x] `Enter`: `context.Bindings.ResolveTransform(targetSlot, ...)?.gameObject.SetActive(active)`.
- [x] `IsFinished`는 **언제나 true**(`HighlightStep`·`TutorialCueStep`과 같은 관용구 — 상태만 바꾸고 안 기다린다).
- [x] 대상이 없으면 에러 한 줄 찍고 넘어간다(시퀀스가 멎지 않는다).
- [x] `Label`: `"SetActive '<slot>' = true/false"`.
- [x] ⚠ 클래스 이름·네임스페이스 고정(`[SerializeReference]` 규율).

## Step 5 — `ScreenFadeStep` (신규 스텝 1개)

- [x] `Assets/02. Scripts/SequenceSystem/Steps/ScreenFadeStep.cs`, `namespace SequenceSpace`.
- [x] 필드: `enum Mode { Out, In }` + `Mode mode` + `bool waitForFinale` + `[SequenceSlot] string tutorialDirectorSlot`.
- [x] `Enter`: `waitForFinale`면 대기 상태로 들어가고, 아니면 즉시
      `context.Owner.StartCoroutine(ScreenFader.Instance.FadeOut()/FadeIn())`.
      `ScreenFader`는 싱글톤이라 **슬롯이 필요 없다**.
- [x] `IsFinished`: 코루틴 종료 플래그. **자기 타이머를 들지 않는다** —
      `ScreenFader`가 unscaled로 도는데 러너의 `ElapsedInStep`은 스케일된 시계라 섞으면 어긋난다(Research §4-1).
- [x] `Exit`: 진행 중 코루틴 정리(중단 경로) + 구독 해제.
- [x] `Label`: `"FadeOut" / "FadeIn"`.

### 5-1. `waitForFinale`가 푸는 문제

`PatternDrillStep.IsFinished`는 **패턴 완료 시점**에 true가 되고, 실루엣은 그보다 `ImpactTime` 뒤에 터져
`holdDuration` 동안 떠 있다(Research §4). 그대로 페이드하면 **실루엣이 한 프레임도 안 보인다.**

- [x] `waitForFinale`면 `TutorialDirector.Finale.OnFinaleEnded`를 구독하고, 그게 오면 페이드를 시작한다.
- [x] ⚠ **실루엣이 안 떴을 때도 반드시 진행한다** — 연출 토글(`silhouetteEnabled`)이 꺼져 있거나
      `Silhouette` 레이어가 없거나 마지막 드릴이 다른 경로로 끝나면 `OnFinaleEnded`가 영영 안 온다.
      **unscaled 안전 타임아웃**(기본 3초)을 둔다. 없으면 튜토리얼이 통째로 멈춘다.
- [x] 타임아웃도 `Time.unscaledTime`으로 잰다(대기 중에는 아직 슬로우가 걸려 있다).

## Step 6 — `MoveToStep`에 도착 방위 지정

미오가 파편을 **보고** 서 있어야 한다. 지금 `MoveToStep`은 **이동 방향**을 볼 뿐이고,
마지막 드릴 뒤 미오는 이미 무대 중앙 근처라 이동 거리가 0에 가까워 **방위가 결정되지 않는다**
(`IsFinished`가 첫 프레임에 true가 되어 회전이 한 번도 안 걸린다).

- [x] `[SerializeField] private bool overrideFacing;`
- [x] `[SerializeField] private float facingYaw;` (월드 Y 오일러, 도)
- [x] 도착 판정이 참이 되는 순간(또는 `Exit`) `overrideFacing`이면
      `actor.rotation = Quaternion.Euler(0f, facingYaw, 0f)`를 **대입**한다.
- [x] `overrideFacing`이 꺼져 있으면 **기존 동작 그대로**(저작해 둔 다른 `MoveToStep` 회귀 0).
- [x] 검은 화면 중의 이동이므로 `speed`를 크게(예: 50) 저작해 사실상 즉시 도착시킨다 — **새 필드가 아니다.**

## Step 7 — 머리 파편 프리팹 + 씬 저작 (`Tutorial.unity`)

미오 앞에 놓을 것은 **시체 한 구가 아니라 머리가 붙은 파편 하나**다.

### 7-1. ⚠ 조각은 `SkinnedMeshRenderer`다 — 떼어내면 안 그려진다

`Stage1Enemy_*_Corpse.prefab`의 `Piece_N`은 전부 **스킨드 메쉬**이고
`SkinnedMeshRenderer.bones`가 그 프리팹의 **스켈레톤을 가리킨다**.
자식 하나만 꺼내 새 프리팹으로 만들면 본 참조가 끊겨 **바인드 포즈로 뭉개지거나 아예 안 보인다.**
그래서 만드는 법은 "조각을 꺼내는 것"이 아니라 **"나머지를 지우는 것"**이다.

- [x] `Stage1Enemy_Pattern(8_5_2_1)_Corpse.prefab`을 씬에 인스턴스화한다.
      (머리 비중 실측: `Piece_2`가 **Head/Neck 28%**, `Head` 본 단독 26%로 전 세트 중 가장 높다.
       대안: `Stage1Enemy_PatternChain(4)_Corpse`의 `Piece_1` — Head/Neck 27%.)
- [x] `Piece_2`만 남기고 나머지 `Piece_N` 게임오브젝트를 **지운다**. **스켈레톤(본 계층)은 남긴다** —
      남은 조각이 그것을 참조하고, 굽기 당시의 사망 포즈도 거기 들어 있다.
- [x] `CorpseView` · 남은 `SlicePiece` 컴포넌트를 **제거한다.** 정적 소품이지 살아 있는 시체가 아니다 —
      두면 런타임에 흩어지거나 소멸(`Dissolve`)을 시작한다.
- [x] `Assets/03. Prefabs/StoryProps/Debris_EnemyHead.prefab`으로 저장한다.

### 7-2. 씬 배치

- [x] `Debris_EnemyHead`를 무대 중앙 **앞**(미오가 볼 자리)에 놓고 회전·높이를 바닥에 맞춘 뒤
      `SetActive(false)`로 꺼 둔다.
- [x] `SequenceRunner`의 `requiredBindings`에 **`Rift`** 와 **`FinaleDebris`** 슬롯 추가 + 씬 오브젝트 배선
      (`FinaleDebris` ← `Debris_EnemyHead` 인스턴스).
- [x] `TutorialDirector`에 `FinaleSilhouetteDirector` 배선(Step 2).

> ⚠ **런타임 시체를 남기는 방식은 쓸 수 없다** — `CorpseView`는 소멸 뒤 풀로 반납되고,
> 죽은 자리도 미오가 설 중앙이 아니다. 그래서 정적 배치다.

## Step 8 — 시퀀스 저작 (`Seq_Tutorial.asset`)

현재 끝부분:
```
23: HighlightStep  "아무 점이든 빠르게 연타"
24: PatternDrillStep   ← 마지막 드릴
25: HighlightStep      (해제)
26: DialogStep     허물/미오/카시마
27: UnlockStep     tutorial_done
```

바꿀 모양(기존 스텝은 **하나도 안 고친다** — 순서만 밀린다):
```
23  HighlightStep    "아무 점이든 빠르게 연타"
24  TutorialCueStep  ArmFinale                       ← 신규
25  PatternDrillStep (마지막 드릴, 그대로)
26  HighlightStep    (해제, 그대로)
27  ScreenFadeStep   Out, waitForFinale = true       ← 신규
28  SetActiveStep    Rift = false                    ← 신규
29  SetActiveStep    FinaleDebris = true             ← 신규
30  MoveToStep       중앙, speed 50, overrideFacing  ← 신규
31  ScreenFadeStep   In                              ← 신규
32  DialogStep       (26번 그대로)
33  UnlockStep       tutorial_done (그대로)
```

- [x] ⚠ **순서가 요구 그 자체다** — 28~30은 반드시 화면이 검은 **사이**에 있어야 한다.
      27(FadeOut)이 완료를 기다리는 스텝이라 그 보장이 순서만으로 성립한다.

## Step 9 — 검증

- [x] 컴파일 에러 0 (`read_console`).
- [ ] 플레이: 마지막 연타 드릴 성공 → 배경 빨강 + 배우 검정 + 슬로우 → 검은 페이드 →
      페이드 인 시 `Rift`가 안 보이고, 미오가 중앙에서 파편을 보고 서 있다 → 26번 대사.
- [ ] 마지막 드릴을 **일부러 실패**한 뒤 성공 → 실루엣이 여전히 나온다(무장 유지, Step 1).
- [ ] `silhouetteEnabled`를 꺼도 튜토리얼이 **안 멈춘다**(Step 5 타임아웃).
- [ ] 시퀀스 종료 후 `Time.timeScale == 1`, 렌더러 피처 둘 다 `active=False`, 플레이어·적 레이어 원복.
- [ ] 플레이 중단(Stop) → 화면이 검은 채로/빨간 채로 굳지 않는다.
