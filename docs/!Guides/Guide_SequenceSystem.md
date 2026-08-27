# Guide — SequenceSystem 사용법

대사·브리핑·컷신을 **에셋으로 저작하고 씬에서 재생**하는 시스템. 설계 근거는 `docs/GameFramework/Plan_SequenceSystem.md`.

---

## 1. 구성 요소 셋

| | 무엇 | 어디 사는가 |
|---|---|---|
| `SequenceAsset` | **정의** — 순서·대사·좌표·대기 조건 | 에셋 (`04. Datas/Sequences/`) |
| `SequenceRunner` | **실행 + 씬 배선** | 씬 |
| `SequenceStep` | **로직** — 스텝 한 종류 | 코드 |

핵심 규칙 하나만 기억하면 된다: **에셋은 씬 오브젝트를 참조할 수 없다.** 그래서 위치는 좌표로 박고, 오브젝트가 필요하면 러너의 **슬롯**을 거친다.

---

## 2. 시퀀스 하나 만들기 (5분)

### 2-1. 에셋 생성

프로젝트 창 우클릭 → **Create > Sequence > Sequence Asset**
경로는 `Assets/04. Datas/Sequences/{Stage}/Seq_{이름}.asset`.

### 2-2. Hold Mode 정하기

**이 시퀀스가 도는 동안 플레이어를 어떻게 붙잡을지**를 정한다. 이것 하나가 "시간 정지"와 "누가 위치를 소유하는가"를 동시에 해결한다.

| Hold Mode | 언제 | 결과 |
|---|---|---|
| `Keep` | 걸으면서 듣는 대사 (카시마 브리핑) | 모드를 안 건드린다. 플레이어는 계속 움직인다 |
| `Overlay` | 멈춰 서서 보는 대사 | 이동·입력 정지. 위치의 주인이 없다 |
| `Cutscene` | Timeline 컷신 | 위치·카메라를 Timeline이 몬다 |

> ⚠ **`Time.timeScale`을 쓰지 말 것.** CLAUDE.md §7-3이 금지한다 — 채보는 `audioSource.time`으로 도는데 오디오는 timeScale 밖이라 판정이 영구히 어긋난다. 멈춰 세우고 싶으면 `Overlay`를 쓴다.

### 2-3. Required Bindings 선언

이 시퀀스가 필요로 하는 **씬 오브젝트의 이름표**를 적는다. 예: `Kashima`, `GateDirector`.

위치만 다루는 시퀀스라면 **비워 둬도 된다** — 좌표는 슬롯이 필요 없다.

### 2-4. Steps 추가

`Steps` 리스트의 `+` → 스텝 종류를 고른다. 순서대로 실행된다.

### 2-5. 씬에 러너 배치

빈 GameObject에 `SequenceRunner` 추가 후 배선:

| 필드 | 채울 것 |
|---|---|
| `Asset` | 만든 SequenceAsset |
| `Player` | 플레이어 Transform (슬롯을 비운 스텝의 기본 대상) |
| `Player Mode Director` | 플레이어의 `PlayerModeDirector` (Hold Mode가 `Keep`이 아니면 필수) |
| `Play On Start` | 씬 시작과 동시에 재생할지 |

그 아래 **Bindings** 섹션에 `Required Bindings`마다 칸이 하나씩 뜬다. 씬 오브젝트를 끌어다 놓는다.
선언 목록과 어긋나면 **"슬롯 동기화"** 버튼이 뜬다 — 누르면 맞춰진다(기존 배선은 이름으로 보존).

---

## 3. 스텝 종류별 사용법

### WaitStep
`Duration`초만큼 기다린다.

### MoveToStep
대상을 `Destination` 좌표까지 걸어가게 한다.

| 필드 | 설명 |
|---|---|
| `Actor Slot` | 옮길 대상. **비우면 플레이어** |
| `Destination` | 월드 좌표 |
| `Speed` / `Arrive Radius` | 이동 속도 / 도착 판정 반경 |
| `Face Move Direction` / `Turn Duration` | 가는 쪽으로 도는가 |

> **좌표는 씬 뷰에서 잡는다.** 러너를 선택하면 이 시퀀스의 모든 `MoveToStep` 목적지에 **이동 핸들**이 뜨고, 목적지들을 잇는 **하늘색 경로선**이 그려진다. 핸들을 끌면 에셋의 좌표가 바뀐다(Undo 지원).

> ⚠ **플레이어를 옮기려면 Hold Mode가 `Overlay`나 `Cutscene`이어야 한다.** `Keep`이면 `PlayerExploreMover`/`PlayerCombatMover`가 같은 프레임에 위치를 덮어써 이동이 씹힌다(CLAUDE.md §11-2). 이 조합은 에셋 저장 시 경고가 뜬다.

### DialogStep
`Lines`의 대사를 순서대로 띄우고, 다 넘어갈 때까지 기다린다.

| `DialogData` 필드 | 설명 |
|---|---|
| `Print Time` | 이 줄을 다 출력하는 데 걸리는 시간(초) |
| `Name` / `Script` | 화자 / 본문. `<color=#ff0000>` 같은 리치 텍스트를 쓸 수 있고 태그는 글자 수에 안 센다 |
| `Skip Progress` | 이 비율(0~1)만큼 출력돼야 클릭으로 넘길 수 있다. 0이면 언제든 |

씬에 `DialogUI`가 있어야 한다(§5).

### WaitFlagStep
씬 쪽이 신호를 줄 때까지 기다린다. **트리거 볼륨에 들어가기, 버튼 누르기**처럼 스텝이 직접 콜백을 못 받는 조건에 쓴다.

```csharp
// 트리거 볼륨 쪽
void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("Player")) runner.SetFlag("GateReached");
}
```

`Flag Name`에 `GateReached`를 적으면 그 호출로 통과한다. 진입 시 플래그를 지우므로 앞서 세워진 값이 남아 즉시 통과하는 일은 없다.

### TimelineStep
Timeline 한 편을 재생하고 끝날 때까지 기다린다.

| 필드 | 설명 |
|---|---|
| `Timeline` | `TimelineAsset`을 끌어다 놓는다 |
| `Director Slot` | 재생을 맡길 `PlayableDirector` 슬롯 |

- **Timeline의 트랙 바인딩은 Timeline이 알아서 한다** — 씬의 `PlayableDirector`에 배선하면 된다. 여기서 중복 배선하지 않는다.
- `extrapolationMode`는 재생 시작할 때 코드가 `None`으로 맞춘다(다른 값이면 끝나도 계속 Playing이라 스텝이 안 끝난다).
- 컷신이면 **Hold Mode를 `Cutscene`으로** 둔다.

---

## 4. 씬 쪽에서 시퀀스 다루기

```csharp
[SerializeField] private SequenceSpace.SequenceRunner runner;

void Start()
{
    runner.OnFinished += HandleSequenceFinished;
    runner.Play();          // 처음부터 재생 (재생 중이면 중단하고 다시)
}

void SomethingHappened()
{
    runner.SetFlag("GateReached");   // WaitFlagStep 통과
}

void Cancel()
{
    runner.Stop();          // 중단. OnFinished는 나지 않는다
}
```

`Play On Start`를 켜면 `Start()`에서 자동 재생된다.

**Hold Mode는 세 경로에서 전부 복구된다** — 정상 종료 · `Stop()` · `OnDisable`. 어느 쪽으로 끝나도 조작이 돌아온다.

---

## 5. DialogUI 배치

대사를 쓰려면 씬에 `DialogUI`가 하나 있어야 한다.

```
UI_Dialog          Canvas(ScreenSpaceOverlay) + CanvasScaler + GraphicRaycaster + DialogUI
└ Dialog           대사창 루트 (비활성으로 시작)
   ├ Name          TextMeshProUGUI
   ├ Script        TextMeshProUGUI
   └ Next          Button   (누르면 다음 줄)
```

`DialogUI` 컴포넌트 우클릭 → **Reset** 하면 위 이름 규칙으로 자동 배선된다. 이름이 다르면 인스펙터에서 직접 채운다.

- **앞뒤 순서는 `Canvas` 컴포넌트의 `Sorting Order`에서 정한다** — 스크립트가 따로 들고 있지 않다.
- 씬에 `EventSystem`이 있어야 버튼이 눌린다(`InputSystemUIInputModule` 사용).
- `DialogStep`은 `DialogUI.Instance`로 찾는다. `Awake`에서 자기를 등록하므로 씬에 놓아 두기만 하면 된다.
  씬에 없으면 `Instance`가 `null`이고, `DialogStep`이 에러를 찍은 뒤 **그 스텝만 건너뛴다**(시퀀스는 계속 간다).
- ⚠ 참조가 비면 `Awake`에서 **에러를 찍고 멈춘다**. 예전처럼 조용히 이름으로 자동 배선하지 않는다 — 그 방식은 인스펙터 배선을 매 실행 덮어써 프리팹 구조를 못 바꾸게 만들었다.

---

## 6. 새 스텝 종류 추가하기

`SequenceSystem/Steps/`에 파일 하나. **MonoBehaviour가 아니다.**

```csharp
using System;
using UnityEngine;

namespace SequenceSpace
{
    [Serializable]
    public class FadeStep : SequenceStep
    {
        [SerializeField] private float duration = 1f;

        public override void Enter(SequenceContext context) { /* 시작 */ }
        public override void Tick(SequenceContext context) { /* 매 프레임 */ }
        public override void Exit(SequenceContext context) { /* 정리 */ }

        public override bool IsFinished(SequenceContext context) => context.ElapsedInStep >= duration;

        public override string Label => $"Fade {duration:0.##}s";
    }
}
```

컴파일되면 `Steps` 리스트의 `+` 드롭다운에 자동으로 나타난다.

**`SequenceContext`로 얻을 수 있는 것**: `Bindings`(슬롯) · `Player` · `PlayerMode` · `Runner`(플래그·코루틴) · `ElapsedInStep`(스텝 진입 후 경과).

씬 오브젝트를 참조해야 하면 슬롯 문자열 필드에 `[SequenceSlot]`을 붙인다 — 자유 입력 대신 **선언된 슬롯 드롭다운**이 그려져 오타가 불가능해진다.

```csharp
[SequenceSlot]
[SerializeField] private string targetSlot;
```

---

## 7. 주의사항

- **⚠ 런타임 상태는 값 타입만.** 러너가 스텝의 **얕은 복사본**을 만들어 돌린다(안 그러면 경과 시간·완료 플래그가 에셋에 저장된다). 값 타입 필드는 안전하지만 **리스트에 `Add` 하는 식으로 참조 타입을 수정하면 에셋 원본이 바뀐다**.
- **⚠ 클래스 이름·네임스페이스를 바꾸지 말 것.** `[SerializeReference]`가 그것으로 참조를 저장하므로, 옮기면 **저작해 둔 시퀀스의 스텝이 전부 끊긴다**(`Managed Reference missing`). 꼭 옮겨야 하면 `[MovedFrom]`을 붙인다.
- **⚠ Timeline이 시퀀스를 부르게 만들지 말 것.** 계층은 한 방향이다(시퀀스가 위). Timeline Signal이 필요하면 `runner.SetFlag(...)`를 부르는 **단방향**으로만 붙인다.
- 한 프레임에 여러 스텝이 넘어갈 수 있다(대기 0인 스텝이 연속일 때). 64개를 넘기면 에러를 찍고 중단한다.
- **⚠ Unity 창이 백그라운드면 플레이 모드 프레임이 거의 안 돈다.** 자동 테스트로 시퀀스를 돌릴 때 멈춘 것처럼 보이면 `Application.runInBackground = true`를 켠다.

---

## 8. 참고

- 설계·결정 근거: `docs/GameFramework/Plan_SequenceSystem.md`
- 기존 코드 분석: `docs/GameFramework/Research_GameFramework.md` §3 · §4
- 위치 소유권 규칙: CLAUDE.md §11-2 / timeScale 금지: §7-3
