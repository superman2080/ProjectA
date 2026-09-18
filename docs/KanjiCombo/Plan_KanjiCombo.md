# Plan — 콤보 숫자 한자 표기 (KanjiCombo)

근거: `docs/KanjiCombo/Research_KanjiCombo.md`

## 설계 요약

- 콤보 값을 **한자 수사**로 바꾼다(`137` → `百三十七`). 변환은 **순수 함수 하나**(`ScoreSpace.KanjiNumeral.Of(int)`).
- 부르는 곳은 **`ScoreHudView.HandleComboChanged`의 한 줄**뿐이다 — 콤보를 글자로 만드는 곳이 코드베이스에 그것 하나라서다.
- `ScoreDirector`·`ScoreMath`·`ComboPostFxView`·이벤트는 **한 줄도 안 고친다**(§12의 "순수 소비자" 규율 유지).
- 폰트 `mplus-2c-bold SDF Dynamic`은 **씬에서** `ScoreLabel`·`ComboLabel`에 꽂는다. **코드에 폰트 필드를 만들지 않는다** — TMP 컴포넌트가 이미 폰트의 주인이고, 코드가 들면 진실의 원천이 둘이 된다.
- 점수(`ScoreLabel`)는 **아라비아 숫자 `N0` 유지**. 서체만 통일한다.

---

- [x] **Step 1 — `KanjiNumeral` 순수 함수 (신규)**

  `Assets/02. Scripts/Score/Core/KanjiNumeral.cs`, 네임스페이스 `ScoreSpace`.
  **새 asmdef를 만들지 않는다** — `Score.Core`가 이미 있고 `autoReferenced: true`라 `ScoreHudView`(Assembly-CSharp)에서 그냥 보인다(`ScoreDirector`가 `ScoreMath`를 그렇게 쓰고 있다).

  ```csharp
  public static string Of(int value)
  ```

  규칙 셋(Research §3):
  1. `0`인 자리는 건너뛴다 — `250` → `二百五十`, `105` → `百五`.
  2. **`十`·`百`·`千` 앞의 `一`은 생략** — `10` → `十`, `100` → `百`, `137` → `百三十七`.
  3. 네 자리씩 묶고 `万`·`億`을 붙인다. **⚠ 묶음 표시 앞의 `一`은 생략하지 않는다** — `10000` → `一万`(`万`이 아니다). 2번과 방향이 반대라 한 함수 안에서 섞이기 쉬운 지점이다.

  경계:
  - `0` → `〇`. (화면에는 안 뜬다 — `comboGroup`이 감춘다. 그래도 함수는 값을 갖는다.)
  - 음수 → `〇`로 클램프. 콤보는 음수가 될 수 없지만 `int`를 받는 함수라 정의는 해 둔다.
  - `int` 전 범위를 덮는다(`億`까지면 `int.MaxValue`가 들어온다).

  **`StringBuilder`를 쓰지 않는다** — 최대 십여 글자라 문자열 결합이 더 짧고 읽힌다. 호출 빈도는 노트당 1회다.

- [x] **Step 2 — 유닛테스트 (신규)**

  `Assets/02. Scripts/Score/Tests/KanjiNumeralTests.cs`.
  **기존 `Score.Tests` asmdef를 그대로 쓴다**(이미 `Score.Core` + nunit 참조). 새 배선 0.

  최소로 지켜야 할 케이스 — 전부 `[TestCase]` 한 줄씩:

  | 입력 | 기대 | 지키는 규칙 |
  |---|---|---|
  | `0` | `〇` | 경계 |
  | `-5` | `〇` | 클램프 |
  | `7` | `七` | 한 자리 |
  | `10` | `十` | 규칙 2(`一十` 금지) |
  | `12` | `十二` | 규칙 2 |
  | `47` | `四十七` | 일반 |
  | `100` | `百` | 규칙 2 |
  | `105` | `百五` | 규칙 1(십의 자리 0 건너뛰기) |
  | `137` | `百三十七` | 규칙 1+2 |
  | `250` | `二百五十` | 규칙 1 |
  | `274` | `二百七十四` | **현재 채보 풀콤보 실측값** |
  | `1000` | `千` | 규칙 2 |
  | `10000` | `一万` | **규칙 3 — 2번과 반대 방향** |
  | `12345` | `一万二千三百四十五` | 묶음 결합 |
  | `100000000` | `一億` | 억 자리 |
  | `int.MaxValue` | `二十一億四千七百四十八万三千六百四十七` | 전 범위 |

  ⚠ 이 표가 이 작업의 유일한 검증 수단이다 — **틀린 한자는 화면에 멀쩡히 그려지므로 플레이로는 못 잡는다**(`ScoreMathTests`의 존재 이유와 같다).

- [x] **Step 3 — `ScoreHudView` 호출 지점 교체**

  `Assets/02. Scripts/UI/ScoreHudView.cs:98`

  ```csharp
  -        comboLabel.text = combo.ToString();
  +        comboLabel.text = ScoreSpace.KanjiNumeral.Of(combo);
  ```

  이 한 줄이 전부다. 이 클래스에서 **손대지 않는 것**:
  - `comboGroup.SetActive(combo > 0)` — 0을 감추는 규칙은 그대로.
  - `punchUntil` / `TickComboPunch` — 스케일 펀치는 글자 수와 무관하다.
  - `Redraw()` / `scoreFormat` / `shownScore` — 점수는 아라비아 숫자 유지.
  - 폰트 필드 추가 없음(위 설계 요약).

- [x] **Step 4 — 씬 폰트 교체 (`BattleScene.unity`)**

  `UI/Canvas/ScoreHud` 아래 두 라벨의 `TextMeshProUGUI.font`를
  `Assets/09. Fonts/JpFont/TextMeshProFont/Dynamic/mplus-2c-bold SDF Dynamic.asset`(guid `f86c285d89a38b3419da63ff6d5f1319`)로 바꾼다.

  | 대상 | 지금 | 바꾼 뒤 |
  |---|---|---|
  | `ScoreLabel` | `LiberationSans SDF` | `mplus-2c-bold SDF Dynamic` |
  | `ComboLabel` | `LiberationSans SDF` | `mplus-2c-bold SDF Dynamic` |

  머티리얼(`fontSharedMaterial`)도 그 폰트의 기본 머티리얼로 같이 따라가야 한다 — 폰트만 바꾸고 머티리얼이 `LiberationSans SDF Material`에 남으면 **엉뚱한 아틀라스를 샘플링해 글자가 깨진다**.

  ⚠ 선행 조건은 **이미 충족돼 있다** — `Assets/TextMesh Pro/Shaders/TMPro_Properties.cginc` 결손으로 `TextMeshPro/Distance Field`가 컴파일에 실패해 JpFont 계열 머티리얼이 전부 깨져 있었고, 이번 세션에서 그 파일을 복원했다(`font assets checked=87 broken=0`). 이 복원이 없으면 폰트를 꽂는 순간 HUD가 마젠타가 된다.

- [x] **Step 5 — `ComboLabel` 폭 확보**

  한자는 전각이라 `fontSize 160`에서 글자당 ≈160px. `百三十七`(4자) ≈ 640px인데 `ComboLabel` rect는 **600px**다. `overflow=Overflow` + `align=TopLeft`이라 잘리지는 않고 오른쪽으로 삐져나간다.

  `ComboLabel`의 rect 폭 `600` → **`1000`**(6자 = `二十一億…` 같은 값은 안 나오지만, 실질 상한인 네 자리 `一千…`까지 여유). 높이·앵커·정렬은 그대로.

  ⚠ `enableAutoSizing`을 켜지 않는다 — 켜면 콤보가 오를 때마다 글자 크기가 들쭉날쭉해져 `comboPunchScale` 연출과 싸운다.

- [x] **Step 6 — 검증**

  1. **`Score.Tests` 실행 → `passed=54 failed=0 skipped=0 status=Passed`**(`ScoreMathTests` + `KanjiNumeralTests`).
     덤으로 같은 표를 `Of()`에 직접 먹여 본 인라인 검사도 `cases=23 · distinct-scan=1..1000 · failures=0`.
  2. **두 라벨 머티리얼 `ShaderHasError == false`** — 둘 다 `mplus-2c-bold Atlas Material`(`TextMeshPro/Distance Field`).
  3. **글리프 커버리지** — `TryAddCharacters("〇一二三四五六七八九十百千万億0123456789,")` → `missing=''`.
     콤보와 점수가 쓰는 글자가 이 폰트에 전부 있다(= 두부 글자 `□`가 안 나온다).
  4. **실제 렌더 폭 실측** — `ComboLabel`에 `百三十七`을 넣고 `ForceMeshUpdate`:
     `chars=4 renderedWidth=640 rectWidth=1000`.
     **Step 5가 실제로 필요했다** — 원래 rect 600px였고 렌더 폭이 640px다.
     `ScoreLabel`은 `1,234,567` → `chars=9 renderedWidth=622 rectWidth=900`으로 여유.
  5. ⚠ 프로브 텍스트는 씬에 저장하지 않았다(원래 값으로 되돌린 뒤 저장 안 함).

  ⚠ **에디터에서 `ComboGroup`은 비활성 상태로 저장돼 있다**(콤보 0). 정상이다 —
  `HandleComboChanged`가 런타임에 켠다. 편집 중 프로브가 `chars=0`으로 나오는 것이 그 이유이지 버그가 아니다.

- [x] **Step 7 — 다이내믹 폰트 아틀라스 git 잡음 확인**

  ⚠ 다이내믹 폰트 에셋은 **에디터 플레이 중 글리프를 자기 `.asset`에 굽는다**. 같은 폴더의 `mplus-1c-medium`·`mplus-1c-thin`·`mplus-1m-light`가 7KB → **2.1MB**로 불어 있는 것이 그 증거다.

  쓰는 글자가 `〇一二三四五六七八九十百千万億` + 아라비아 숫자·쉼표뿐이라 절대량은 작지만, 플레이할 때마다 `mplus-2c-bold SDF Dynamic.asset`이 git diff에 올라온다. Step 6 이후 실제 증가분을 재고, **거슬리는 크기면** 그때 폰트 에셋의 `Clear Dynamic Data on Build`를 켜거나 필요한 글자만 구운 Static 에셋으로 전환한다.

  **실측 결과 — 아무것도 안 한다.**

  | | |
  |---|---|
  | `mplus-2c-bold SDF Dynamic.asset` | 7,393 B → **2,133,674 B** |
  | 같은 폴더에서 이미 불어 있던 에셋 | `1c-medium`·`1c-thin`·`1m-light`·`1p-heavy`·**`2c-black`** (각 ≈2.1MB) |
  | `Dynamic` 폴더 전체 | 13MB |
  | `JpFont` 폴더 전체 | **460MB** (대부분 `.ttf` 원본과 데모) |

  판단 근거 셋:
  - **2MB는 글리프 20개의 크기가 아니라 아틀라스 텍스처 1024×1024가 통째로 직렬화된 값이다.**
    글자를 하나라도 구우면 한 번에 그 크기가 되고, **그 뒤로는 안 자란다**(20자가 한 페이지에 다 들어간다).
    즉 **일회성**이지 플레이할 때마다 쌓이는 잡음이 아니다.
  - 460MB짜리 폴더에 붙는 2MB라 상대적으로 잡음이 아니다.
  - **아틀라스를 128×128로 줄이면 ~32KB가 되지만 하지 않는다** — 이 폰트는 §15 대사 창의
    일본어 본문으로 쓰일 자리에 있고, 그때 1024가 맞는 크기다. 지금 줄이면 그 시점에
    아틀라스 페이지가 조용히 여러 장으로 갈린다.

---

## 하지 않는 것

- `ScoreDirector`·`ScoreMath`·이벤트 시그니처 수정 — 필요가 없다.
- 표기 방식 토글(인스펙터 bool `useKanji` 등) — 되돌릴 일이 생기면 한 줄을 되돌리면 된다. 쓰이지 않을 분기를 미리 만들지 않는다.
- `ScoreHudView`에 폰트/머티리얼 필드 추가 — TMP 컴포넌트가 폰트의 주인이다.
- 점수(`ScoreLabel`) 한자화 — 7자리는 한자로 못 읽는다(확정된 결정).
- `FocusRingView.SetLabel`(연타 남은 타수, §2-1) 한자화 — 콤보가 아니다. 원하면 같은 함수를 부르면 되지만 이번 범위가 아니다.
- 결과 화면(`GameSession.LastResult`)의 `MaxCombo` 표시 — 그 화면이 아직 없다(§12).
