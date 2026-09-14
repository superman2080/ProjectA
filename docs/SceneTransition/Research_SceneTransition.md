# Research — 씬 전환 (SceneTransition)

## 1. 지금 씬을 로드하는 곳 — 넷

| 파일 | 줄 | 모양 |
|---|---|---|
| `EncounterDirector.EnterRoutine` | 125 | `SetMode(Overlay)` → 복귀정보 기입 → `fader.FadeOut()` → `LoadScene(battleSceneName)` |
| `BattleSceneBootstrap.ReturnRoutine` | 127 | `Unsubscribe` → `fader.FadeOut()` → `LoadScene(복귀 씬 or freePlayReturnScene)` |
| `SongSelectManager.SelectChart` | 21 | 페이드 **없이** 즉시 `LoadScene` |
| `BattleSceneBootstrap.LoadStageBackground` | 64 | `LoadSceneAsync(..., Additive)` — **가산 배경이라 이 개편 대상이 아니다** |

셋 중 둘은 이미 `FadeOut` → `LoadScene` 모양이 같다. 즉 **패턴은 이미 있고 동기 로드라는 것과 최소 지속시간이 없다는 것만 다르다.**

받는 쪽(`ExploreSceneBootstrap.Start`, `BattleSceneBootstrap.Start`)은 둘 다
`CoverInstantly()` → 준비 → `FadeIn()`이다. **이 개편은 받는 쪽을 한 줄도 안 건드린다.**

## 2. `ScreenFader` (`UI/ScreenFader.cs`)

- `Managers` 프리팹 자식(`Singleton<T>`, `DontDestroy`) — 씬이 언로드되는 동안에도 살아 있다(§10).
- `FadeOut()`/`FadeIn()`은 코루틴, `fadeDuration` 0.4초, **`Time.unscaledDeltaTime`** 보간.
- `Apply`가 알파와 `blocksRaycasts`를 같이 민다.
- 알파가 이미 목표값이면 코루틴이 **즉시 끝난다** — 이미 덮인 상태에서 다시 `FadeOut`을 불러도 무해하다.
- 헤더에 못박힌 규율: **"이 클래스가 씬을 로드하지 않는다 — 여기는 알파만 민다."**
- 씬 오브젝트 구조: `ScreenFader` 아래 `Cover`(전체 덮는 `Image`) 하나뿐. 로딩 아이콘이 붙을 자리가 여기다.

## 3. `FocusRing.prefab` — 재사용 가능한 것

- 루트 `Image`: 스프라이트 guid `fbfb11699328cec4aa6696fb39ba7d8b`, **`m_FillMethod: 4`(Radial360)**, `m_FillAmount: 1`.
- 즉 **링 스프라이트와 Radial360 설정이 이미 그대로 있다.** 로딩 아이콘은 그 스프라이트를 쓰는 `Image` 하나면 된다.
- `FocusRingView`(스크립트)는 판정 타이밍 전용(`OnArrived`·`IPoolable`·`Pool`)이라 **재사용 대상이 아니다** — 재사용하는 것은 **그림**이지 클래스가 아니다.
- Radial360 `Image`가 노출하는 축은 셋뿐이다: `fillOrigin` · `fillClockwise` · `fillAmount`.
  - 하단에서 **시계방향으로 채움** = `origin = Bottom`, `clockwise = true`, `amount 0→1`
  - 하단에서부터 **시계방향으로 비움** = `origin = Bottom`, `clockwise = false`, `amount 1→0`
    (반시계로 잰 호의 길이가 줄면, 사라지는 쪽이 하단부터 시계방향이 된다)
  - **두 단계가 같은 `origin`을 쓰고 `clockwise`만 뒤집힌다 — 새 계산이 0줄이다.**

## 4. 튜토리얼 시퀀스 끝부분 (`Seq_Tutorial.asset`)

steps 배열 끝 8개(rid → 타입):

```
486408  HighlightStep    (하이라이트 해제)
926102  ScreenFadeStep   Out, waitForFinale = true   ← 마무리 실루엣이 걷힐 때까지 기다렸다 덮는다
926101  SetActiveStep    Rift        = false
926100  SetActiveStep    FinaleDebris = true
926099  MoveToStep       중앙으로 순간이동(speed 50)
926098  ScreenFadeStep   In
486409  DialogStep       허물/미오/카시마            ← 지울 대상
486410  UnlockStep       tutorial_done
```

- 926101~926098 네 스텝은 **오직 그 대사를 위해** 존재한다(검은 화면 사이에 파편을 세우고 미오를 그 앞에 옮긴 뒤 걷어내 보여 준다). 대사가 사라지면 **걷어낼 이유가 없어진다** — 페이드 인 했다가 곧바로 다시 페이드 아웃하는 그림이 된다.
- `ScreenFadeStep`의 `waitForFinale`이 실루엣 종료 대기를 이미 든다. **"하이라이트 연출이 끝나고"의 구현은 이미 있다.**
- 시퀀스에는 **씬을 로드하는 스텝이 없다**(`Steps/` 12종: Camera·Dialog·Highlight·MoveTo·PatternDrill·ScreenFade·SetActive·Timeline·TutorialCue·Unlock·WaitFlag·WaitInput·Wait). 그래서 튜토리얼은 지금 시퀀스가 끝나면 **그 자리에 그냥 서 있다.**

## 5. 제약

- **`Time.timeScale`을 못 믿는다.** 마무리 실루엣(§14)이 0.1을 걸고 있고 복구가 늦을 수 있다 → 전환 코드는 전부 **unscaled** 시계를 쓴다(`ScreenFader`가 이미 그렇다).
- `LoadSceneAsync`는 `allowSceneActivation = false`면 `progress`가 **0.9에서 멈춘다**. 완료 판정은 `progress >= 0.9f`다.
- `SongSelectManager`는 `Managers` 프리팹 없이 진입할 수 있는 경로다(`GameSession`을 직접 만든다) → **로딩 아이콘 배선이 비어도 전환은 반드시 성립해야 한다.**
- 진행바·퍼센트·문구는 두지 않는다(`ScreenFader` 헤더: "로딩 화면이 아니다", §9의 "이음매가 안 보인다").
