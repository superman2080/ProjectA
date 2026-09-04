# Research — 튜토리얼 설명 장치 (강조 · 키 입력)

목적: 튜토리얼 시퀀스에 **대사가 아닌 방식으로 조작을 가르치는 스텝**을 붙이기 위해, 지금 무엇이 있고 무엇이 없는지 확인한다.

## 1. 지금 상태

`docs/TutorialScene/Plan_TutorialScene.md`의 Step 1~6이 **전부 완료**다. 씬(`01. Scenes/Tutorial.unity`) · 드릴 스텝(`PatternDrillStep`) · 시퀀스 에셋(`Seq_Tutorial`) · 한글 폰트까지 배선·플레이 검증이 끝나 있다.

그 시퀀스가 가진 스텝은 실질적으로 **둘**이다.

| 스텝 | 하는 일 |
|---|---|
| `DialogStep` | 카시마의 대사(타자기 출력, 클릭으로 넘김) |
| `PatternDrillStep` | 다다미 앞으로 걸어가 패턴 그룹을 성공할 때까지 반복 |

**둘 사이가 비어 있다** — "무엇을 보라"와 "무엇을 눌러라"를 전달하는 수단이 없다. 지금은 대사 문장이 그 일을 대신하고 있다(*"점을 순서대로 이어 그으면 돼"*).

## 2. 있는 것 (재사용 가능)

- **`SequenceStep`** — `Enter`/`Tick`/`Exit`/`IsFinished` 넷. 새 스텝을 붙이는 비용이 파일 하나다.
- **`SequenceBindings` + `[SequenceSlot]`** — 씬 오브젝트를 이름 슬롯으로 받는다. 드롭다운 저작이라 오타가 안 난다.
- **`DialogUI.Instance` 정적 접근자** — SO인 스텝이 씬 UI에 닿는 기존 관용구. `Singleton<T>`를 안 쓰는 이유(빈 껍데기를 만들어 내는 것보다 `null`이 낫다)까지 주석에 남아 있다. **새 UI 뷰도 이 관용구를 그대로 쓰면 슬롯이 0개다.**
- **`InputHandler`** — 게임플레이 입력의 유일한 출처. `OnKeyPressed(int 0~8)` · `OnDodgePressed` · `OnInteractPressed` · `MoveInput` · `SprintHeld`. 구독자가 `UnityEngine.InputSystem`에 의존하지 않는다.
- **`Point.SetKnobVisible(bool, duration)`** — 노브 페이드. `public`이다.
- **`SequenceRunner`의 복구 경로 셋**(정상 종료 · `Stop()` · `OnDisable`) — 화면에 남는 것을 걷어낼 자리가 이미 규율로 존재한다(CLAUDE.md §15).

## 3. 없는 것

- **강조 장치.** Canvas에 "여기를 보라"를 표시하는 것이 하나도 없다. `EffectManager`는 판정 결과에 반응하는 이펙트 카탈로그라 임의 위치를 지목하는 용도가 아니고, `FocusRingView`는 `PatternHandler`가 스케줄링·소유한다(외부에서 빌려 쓰면 소유자가 둘이 된다).
- **키 입력 대기.** `WaitFlagStep`이 가장 가깝지만 `SetFlag`를 불러 줄 **씬 MonoBehaviour를 키마다 하나씩** 요구한다 — 그게 곧 콘텐츠 작업이 코드 수정을 부르는 부류다(`SequenceBindings` 주석이 경계하는 바로 그것).

## 4. ⚠ 설계를 가르는 제약들

1. **노브는 기본적으로 안 보인다.** `PatternHandler.ApplyKnobVisibility`는 **살아 있는 패턴이 쓰는 Point의 합집합**만 보이게 한다(CLAUDE.md §1). 드릴 전에 Point_4를 강조하면 **빈 자리에 링만 뜬다.** 강조 스텝이 노브를 같이 켜야 한다.
2. **노브 페이드는 `Visual`의 `CanvasGroup` 알파다.** 강조 링을 `Visual`의 자식으로 붙이면 **노브가 꺼질 때 같이 사라진다.** 링은 Canvas 직속이어야 하고 위치·크기만 복사한다.
3. **Point 본체(150x150)와 `Visual`(90x90)은 다른 RectTransform이다.** 어느 쪽을 강조 대상으로 배선하느냐로 링 크기가 달라진다.
4. **루트 Canvas가 `ScreenSpaceOverlay`**(§7-5)라 카메라·앵글 교체와 무관하다 — 강조 링에 월드 투영이 필요 없다.
5. **입력 모드는 `PlayerModeDirector`가 소유한다.** 튜토리얼은 `holdMode = Keep` + `Combat`이라 숫자키·Space는 살아 있고 **WASD·E는 죽어 있다**(`Explore` 맵이 꺼져 있음). 키 대기 스텝이 모드를 바꾸면 위치·입력의 주인이 둘이 된다 — **바꾸지 않고 어긋나면 경고만 찍는다.**
6. **`SequenceAsset.WarnUnknownSlots`의 `switch`에 새 스텝을 추가해야** 미선언 슬롯 경고가 산다.
7. **`PatternDrillStep`이 실패해도 대사가 끼어들지 않는다**는 기존 결정(`Plan_TutorialScene` 4-4b)이 있다 — 강조도 같은 규율을 따라야 한다(반복 실패 시 안내가 환경음이 된다).

## 5. 참고 — 이 프로젝트의 관례

- 새 연출·표시 계층은 **기존 이벤트만 구독하는 순수 소비자**이고 **배선이 비면 조용히 비활성**된다(`CameraDirector`·`HitStopDirector`·`FinaleSilhouetteDirector`).
- **표시 계층은 게임플레이를 모른다**(`DialogUI` 주석). 무엇을 왜 강조하는지는 부르는 쪽(스텝)이 안다.
- 스텝의 **클래스 이름·네임스페이스를 바꾸면** 저작해 둔 시퀀스가 `Managed Reference missing`이 된다(`[SerializeReference]`).
