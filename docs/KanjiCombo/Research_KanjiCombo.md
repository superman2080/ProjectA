# Research — 콤보 숫자 한자 표기 (KanjiCombo)

## 0. 요구

- 콤보 카운트를 아라비아 숫자 대신 **한자 수사**로 표기한다(`137` → `百三十七`).
- 폰트는 `Assets/09. Fonts/JpFont/TextMeshProFont/Dynamic/mplus-2c-bold SDF Dynamic.asset`.
- 확정된 결정(사용자 선택):
  - **표기 방식 = 한자 수사**(자릿수 단순 치환이 아니다). `十`·`百`·`千` 자리표를 쓴다.
  - **한자 치환은 콤보만**. 점수(`ScoreLabel`)는 `N0` 아라비아 숫자를 유지한다.
  - **폰트는 HUD 전체**(`ScoreLabel` + `ComboLabel`)에 적용해 서체를 통일한다.

## 1. 현재 구현 — 콤보 숫자가 화면에 나오는 경로는 하나뿐이다

`ScoreDirector`(순수 채점, §12) → `OnComboChanged(int)` → `ScoreHudView.HandleComboChanged`.

`Assets/02. Scripts/UI/ScoreHudView.cs:91-100`
```csharp
private void HandleComboChanged(int combo)
{
    if (comboGroup != null) comboGroup.SetActive(combo > 0);
    else if (comboLabel != null) comboLabel.gameObject.SetActive(combo > 0);

    if (comboLabel == null) return;

    comboLabel.text = combo.ToString();     // ← 여기 한 줄이 전부다
    if (combo > 0) punchUntil = Time.time + Mathf.Max(comboPunchDuration, 0.01f);
}
```

**전 코드베이스에서 콤보 값을 문자열로 만드는 곳이 이 한 줄뿐이다.** 확인한 다른 소비자:

| 소비자 | 콤보를 쓰는 방식 | 글자를 만드나 |
|---|---|---|
| `ComboPostFxView` | `OnComboTierChanged`(단계 정수) → Volume `weight` | 아니다 |
| `ScoreDirector.PushIntensity` | `EffectManager.SetIntensity(float)` | 아니다 |
| `ScoreMath.ComboAchievement` | 점수 계산(콤보 합) | 아니다 |
| `GameSession.LastResult` | `MaxCombo` 저장 | **읽는 쪽이 아직 없다**(결과 화면 미구현, §12) |

→ **치환 지점이 한 곳으로 모여 있다.** 새 이벤트도, `ScoreDirector` 수정도 필요 없다.

⚠ 비슷하지만 **대상이 아닌 것**: `FocusRingView.SetLabel`(연타 남은 타수, §2-1). 콤보가 아니라 입력 게이지다.

## 2. 값의 범위 — 어디까지 표현해야 하나

`ScoreDirector`: `Combo`는 0에서 시작해 노트마다 1씩 오르고 `BreakCombo`에서 0으로 떨어진다. **상한은 그 곡의 총 노트 수**(= 풀콤보).

현재 프로젝트의 유일한 채보 실측:

```
Dreamer (BEAUZ & Heleen Remix)_Lv10 | entries=88 notes=274
```

→ 지금은 **세 자리**면 충분하다. 다만 더 긴 곡이 들어오면 네 자리(`千`)가 필요해지고, 그 경계에서 조용히 깨지면 원인을 못 짚는다. 순수 함수 쪽에서 `万`·`億`까지 덮어 두면 `int` 전 범위가 커버된다(`2,147,483,647` = `二十一億四千七百四十八万三千六百四十七`).

⚠ **`0`은 화면에 안 뜬다** — `comboGroup.SetActive(combo > 0)`이 통째로 감춘다. 그래도 순수 함수는 `0` → `〇`를 정의해 둔다(호출자가 감추는 것과 함수가 값을 갖는 것은 별개다).

## 3. 표기 규칙 — 일본어 수사

자리별 글자:

```
0 1 2 3 4 5 6 7 8 9
〇 一 二 三 四 五 六 七 八 九
자리표: 十(10) 百(100) 千(1000) / 묶음: 万(10^4) 億(10^8)
```

핵심 규칙 셋:

1. **`0`인 자리는 통째로 건너뛴다** — `250` → `二百五十`(`二百五十〇`가 아니다), `105` → `百五`.
2. **`十`·`百`·`千` 앞의 `一`은 생략한다** — `10` → `十`, `100` → `百`, `137` → `百三十七`. (사용자가 고른 미리보기가 정확히 이 규칙이다.)
3. **네 자리씩 묶고 묶음 표시를 붙인다** — `12345` → `一万二千三百四十五`. 묶음이 통째로 0이면 그 묶음 표시도 안 붙인다.

⚠ 3번 규칙에서 **`万`·`億` 앞의 `一`은 생략하지 않는다**(`一万`이 맞다, `万` 아니다). 2번과 방향이 반대라 한 함수 안에서 섞기 쉬운 지점이고, 테스트가 지켜야 할 곳이다.

## 4. 어디에 두나 — 기존 자리 재사용

`Assets/02. Scripts/Score/Core/`가 이미 있다:

```
Score.Core.asmdef   (name: Score.Core, rootNamespace: ScoreSpace, autoReferenced: true)
  ScoreMath.cs      순수 계산
  ScoreResult.cs
Score.Tests.asmdef  (references: Score.Core, nunit)
  ScoreMathTests.cs
```

- `autoReferenced: true`라 **Assembly-CSharp(`ScoreHudView`)에서 그냥 보인다** — `ScoreDirector`가 이미 `ScoreMath`를 그렇게 쓰고 있다.
- 유닛테스트 어셈블리가 이미 `Score.Core`를 참조한다 → **새 asmdef·새 테스트 배선이 0이다.**
- `ScoreMath`의 규율(§12: "`JudgementResult`를 모른다 — 들어오는 것은 이미 세어진 개수뿐")과 성질이 같다. 한자 변환도 **엔진·씬·판정을 모르는 `int → string`**이다.

→ `Score/Core/KanjiNumeral.cs`(`ScoreSpace`)에 둔다. 새 폴더도 새 어셈블리도 만들지 않는다.

## 5. 폰트 — 대상 에셋과 현재 씬 상태

`Assets/09. Fonts/JpFont/TextMeshProFont/Dynamic/mplus-2c-bold SDF Dynamic.asset`
```
guid: f86c285d89a38b3419da63ff6d5f1319
m_AtlasPopulationMode: 1   (Dynamic — 글리프를 런타임에 굽는다)
m_AtlasWidth/Height: 1024
m_Version: 1.1.0
m_SourceFontFile: mplus-2c-bold.ttf (guid 550e39a93bdf11c4eb1ab20d722406be)
```

**⚠ 이 폰트는 직전까지 머티리얼이 깨져 있었다.** `Assets/TextMesh Pro/Shaders/TMPro_Properties.cginc`가 없어 `TextMeshPro/Distance Field` 셰이더가 컴파일에 실패했고, JpFont 계열 전부가 그 셰이더를 쓴다. 이번 세션에서 그 파일을 복원했고 현재 상태:

```
font assets checked=87 broken=0
```

→ **이 작업의 선행 조건은 이미 충족돼 있다.** 그 복원이 없으면 폰트를 바꾸는 순간 HUD가 마젠타가 된다.

씬(`BattleScene.unity`)의 현재 배선:

```
UI/Canvas/ScoreHud            RectTransform, ScoreHudView, CanvasGroup
  ScoreLabel                  TextMeshProUGUI  font=LiberationSans SDF  size=120
                              rect=(900,220) anchor=(1,1) align=TopRight  wrap=NoWrap overflow=Overflow
  ComboGroup                  RectTransform (컨테이너 — 텍스트 없음)
    ComboLabel                TextMeshProUGUI  font=LiberationSans SDF  size=160
                              rect=(600,200) anchor=(0,1) align=TopLeft  wrap=NoWrap overflow=Overflow
```

관찰:

- **`"COMBO"` 같은 고정 글자 오브젝트가 없다.** `ComboGroup` 아래 텍스트는 `ComboLabel` 하나뿐이라 로마자 라벨을 한자로 바꿀 것도 없다.
- **폭이 모자란다.** 한자는 전각이라 `fontSize 160`에서 글자당 ≈160px. `百三十七`(4자) ≈ 640px > `ComboLabel` rect 600px. `overflow=Overflow` + `align=TopLeft`이라 **잘리지는 않고 오른쪽으로 삐져나간다**. 화면이 깨지진 않지만 rect를 넓혀 두는 편이 낫다.
- `enableAutoSizing=False`라 폰트 크기가 자동으로 줄지 않는다 — 폭 문제를 rect가 흡수해야 한다.

## 6. 제약·함정

- **⚠ 다이내믹 폰트 에셋은 에디터 플레이 중 글리프를 자기 `.asset`에 굽는다.** 같은 폴더의 `mplus-1c-medium`·`mplus-1c-thin`·`mplus-1m-light`가 7KB → **2.1MB**로 불어 있는 것이 그 증거다(Sep 18 16:44 수정). `mplus-2c-bold`도 플레이할수록 커지고 **git diff에 매번 올라온다**. 쓰는 글자가 `〇一二三四五六七八九十百千万億` + 아라비아 숫자·쉼표뿐이라 절대량은 작지만, 커밋에 섞이는 것 자체가 잡음이다.
- **⚠ `ScoreHudView`는 순수 표시다**(클래스 주석). 한자 변환은 표시 계층의 일이 맞지만, **변환 로직 자체를 이 클래스에 두면 테스트가 씬을 요구한다.** §4대로 순수 함수를 분리하고 여기서는 부르기만 한다.
- **⚠ `comboPunchScale` 연출은 `comboRect.localScale`을 만진다**(`TickComboPunch`). 글자 수가 늘어도 스케일 펀치는 그대로 동작한다 — 건드릴 것 없다.
- **⚠ `Redraw()`(점수)는 이 작업과 무관하다.** 점수는 아라비아 숫자 유지가 확정이라 `scoreFormat`·`shownScore` 보간 경로에 손대지 않는다.
- 폰트 교체는 **씬 파일 수정**이다(`BattleScene.unity`). `ScoreHudView`에 폰트 필드를 새로 만들지 않는다 — TMP 컴포넌트가 이미 폰트의 주인이고, 코드가 폰트를 들면 진실의 원천이 둘이 된다.

## 7. 파일 요약

| 파일 | 상태 | 이번 작업에서 |
|---|---|---|
| `Assets/02. Scripts/Score/Core/KanjiNumeral.cs` | 없음 | **신규**(순수 `int → string`) |
| `Assets/02. Scripts/Score/Tests/KanjiNumeralTests.cs` | 없음 | **신규**(기존 Tests asmdef 재사용) |
| `Assets/02. Scripts/UI/ScoreHudView.cs` | 있음 | `comboLabel.text` 한 줄 교체 |
| `Assets/01. Scenes/BattleScene.unity` | 있음 | 두 라벨 폰트 교체 + `ComboLabel` rect 폭 |
| `Assets/TextMesh Pro/Shaders/TMPro_Properties.cginc` | **복원 완료** | 선행 조건(이미 충족) |
