# Research — 마지막 일격 실루엣 (FinaleSilhouette)

곡의 **마지막 패턴을 성공으로 끝낸 순간** 화면을 킬 빌 식으로 뒤집는다 —
**적과 플레이어는 검게, 나머지 배경은 빨갛게.**

레퍼런스: 킬 빌(붉은 배경 + 검은 실루엣) · 블라스퍼머스(색 반전형).
저작자 지시로 **배경 = 빨강 / 캐릭터 = 검정**으로 고정한다(블라스퍼머스와 반대 방향).

---

## 1. 언제 터지는가 — 트리거의 재료는 이미 다 있다

### 1-1. "성공"은 `PatternCompletionInfo.AllCorrect`
`PatternHandler.OnPatternComplete(PatternCompletionInfo)`가 완주/만료 모두에서 발행되고,
페이로드가 `AllCorrect`·`Template`·`Deadline`을 든다(`Assets/02. Scripts/Pattern/PatternCompletionInfo.cs:13,18,33`).

### 1-2. "마지막"은 채보가 안다
`ChartPlayer`는 곡 종료 이벤트를 **오디오가 멈춘 뒤**에 낸다:

```csharp
// ChartPlayer.cs:187
private void RaiseSongEndedIfFinished()
{
    if (songEndRaised || audioSource.isPlaying) return;   // ⚠ 오디오 끝
    ...
    OnSongEnded?.Invoke();
}
```

**`OnSongEnded`는 이 연출의 트리거로 쓸 수 없다.** 마지막 노트 입력과 곡의 오디오 끝 사이에는
아웃트로 길이만큼(수 초) 간격이 있다. 연출은 **칼이 닿는 그 순간**에 터져야 한다.

대신 **엔트리 수를 세는 경로가 이미 선례로 있다** — `ScoreDirector`가 채보를 직접 읽어 총량을 잡는다:

```csharp
// ScoreDirector.cs:148
totalPatterns = chart.entries.Length;
foreach (var entry in chart.entries) totalNotes += entry.onsetTimes.Length;
```

즉 `ChartPlayer.ActiveChart.entries.Length`와 `OnPatternComplete` 발행 횟수를 비교하면
**`ChartPlayer`를 한 줄도 안 고치고** "지금 것이 마지막 패턴인가"를 알 수 있다.

### 1-3. 화면에서 사건이 일어나는 시각은 `info.ImpactTime()`
패턴 완료 = 마지막 노드 **입력**이고, 칼이 닿는 것은 거기서 `goodWindow`(0.1초) + `ImpactOffset` 뒤다.
`CameraDirector`·`HitStopDirector`·`EffectManager`가 전부 같은 확장 메서드
`info.ImpactTime()`(= `Deadline + Pattern.ImpactOffset`)로 예약한다(CLAUDE.md §6·§7-1·§7-3).
이 연출도 같은 앵커를 써야 히트스톱·쉐이크·임팩트음과 한 프레임에 붙는다.

### 1-4. 같은 순간에 이미 일어나는 일들
| 시스템 | 그 순간의 동작 |
|---|---|
| `HitStopDirector` | `AttackSpeed`/`DeathSpeed` = 0으로 두 배우를 얼린다(§7-3) |
| `CameraDirector` | `CinemachineBrain.enabled = false`로 **카메라를 완전히 언다** |
| `PatternEffectDirector` | 활성 뷰의 `simulationSpeed`를 0으로 |
| `EnemyDirector` | `burstTime`을 정지 시간만큼 밀고, 그 뒤 시체 교체 |

**정지 창과 실루엣 창이 겹치는 것은 오히려 이 연출에 유리하다** — 멈춘 화면이 곧 그림이 된다.
다만 `hitStopDuration`은 0.05~0.10초라 실루엣은 그보다 훨씬 길어야 한다(레퍼런스는 정지 컷).

---

## 2. 어떻게 그리는가 — 렌더 파이프라인 현황

### 2-1. URP 17.3 / Unity 6000.3.11f1
`Packages/manifest.json:30` · `ProjectSettings/ProjectVersion.txt`.
`FullScreenPassRendererFeature`와 Render Graph를 쓸 수 있는 버전이다.

### 2-2. `PC_Renderer`에 이미 붙은 피처 5개
`Assets/Settings/PC_Renderer.asset`:

| 이름 | 종류 | 상태 |
|---|---|---|
| `WatercolorFx` | 외부 에셋 | — |
| `CharacterOutline` | **`RenderObjects`** | **`m_Active: 0` (꺼짐)**, LayerMask `m_Bits: 256` = 레이어 8 |
| `AmbushOutline` | **`RenderObjects`** | 켜짐 — §11-8의 기습 강조 |
| `FlowFx` | 외부 에셋 | — |
| `ScreenSpaceAmbientOcclusion` | 내장 | — |

**`RenderObjects` + 오버라이드 머티리얼 + 레이어 스왑**은 이 프로젝트가 이미 쓰는 관용구다(§11-8).
`CharacterOutline`은 꺼진 채 남아 있어 **레이어 기반 캐릭터 렌더링을 한 번 시도한 흔적**이다.

### 2-3. 레이어 현황 (`ProjectSettings/TagManager.asset`)
```
0 Default   1 TransparentFX   2 Ignore Raycast   4 Water   5 UI
8 SlicePiece   9 AmbushOutline
10~31 전부 비어 있음
```

**⚠ 플레이어도 적도 레이어 0(Default)이다** — 배경·무대 프롭과 같은 레이어다.
프리팹 루트부터 자식까지 전부 `m_Layer: 0`.
즉 **`RenderObjects`의 LayerMask만으로는 캐릭터와 배경을 가를 수 없다.**

조각은 다르다 — `EnemyDirector.pieceLayer`(인스펙터 값)가 절단 조각에만 따로 걸린다
(`EnemyDirector.cs:174,1775,1803`). 레이어 8 `SlicePiece`가 그 자리다.

### 2-4. 패턴인풋은 이 연출의 사정권 밖이다
루트 Canvas가 **`ScreenSpaceOverlay`**라(§7-5) 포커스 링·가이드·HUD는 렌더러 피처보다 **뒤에** 그려진다.
실루엣이 UI를 덮지 않는다 — 곡의 마지막 순간이라 링은 이미 없지만, **점수 HUD는 그대로 남는다**(의도 확인 필요).

---

## 3. 선례 — 이 프로젝트가 "화면 전체 연출"을 붙여 온 방식

`ComboPostFxView`(§12, `Assets/02. Scripts/UI/ComboPostFxView.cs`)가 유일한 선례이고 규율이 셋이다:

1. **코드가 미는 것은 `Volume.weight` 하나뿐이다.** 효과 추가 = 프로파일에 오버라이드 한 줄.
2. **프로파일을 코드가 수정하지 않는다** — `sharedProfile`을 건드리면 에디터에서 에셋에 저장된다.
3. **`OnDisable`에서 반드시 되돌린다** — 안 하면 화면이 물든 채 굳는다.

⚠ 다만 **이 연출은 Volume으로 안 된다.** Vignette/ChromaticAberration 같은 URP 내장 오버라이드에는
"이 픽셀이 캐릭터인가"를 아는 수단이 없다. 화면색만으로는 검은 갑옷과 검은 배경을 구분할 수 없다.
**분리 정보는 렌더 단계에서만 얻을 수 있다** — 그래서 이 연출의 몸통은 Volume이 아니라 렌더러 피처다.

---

## 4. 캐릭터/배경을 가르는 방법 — 후보 셋

| | 방법 | 필요한 것 | 위험 |
|---|---|---|---|
| **A** | **레이어 스왑 + `RenderObjects` 2패스**<br>배경 레이어 → 빨강 머티리얼 / 액터 레이어 → 검정 머티리얼 | 새 레이어 1개, 언릿 머티리얼 2개, 런타임 레이어 스왑 | 스왑 복구(**풀 반납이 걸린다**), `AmbushOutline`과 레이어 충돌 |
| **B** | **스텐실 마스크**<br>액터를 스텐실에 찍고 전체화면 셰이더가 `stencil==0`은 빨강, `==1`은 검정 | 새 레이어 1개(찍을 대상 지정), 전체화면 셰이더 1개, `RenderObjects.stencilSettings` | A와 같은 레이어 문제 + 셰이더 하나 더 |
| **C** | **깊이 문턱**<br>전체화면 셰이더가 `SceneDepth < 문턱`이면 검정, 아니면 빨강 | 전체화면 셰이더 1개. **레이어 스왑 0** | 무대 바닥이 카메라 앞까지 이어져 **바닥도 검게 칠해진다**(레퍼런스 1번 이미지는 바닥이 검다 — 오히려 맞을 수도) |

**A와 B는 같은 전제를 공유한다 — "액터만 담긴 레이어가 필요하다".** 그 레이어를 만드는 비용이
이 설계의 실질 비용이고, 아래가 그 이유다.

### 4-1. ⚠ 레이어 스왑의 진짜 비용은 '복구'다
- **적은 풀에서 나온다**(`PrefabPool`). `EnemyView.ResetState`에서 안 되돌리면
  **다음 대여가 실루엣 레이어인 채로 나온다** — §11-8의 아웃라인이 정확히 이 함정을 밟아
  `Finish()`·`Abort()`·`ResetState` **세 곳**에서 끈다.
- **시체는 다른 오브젝트다**(`CorpseView`, §11). 산 적의 레이어를 바꿔도 시체는 안 따라간다.
- **기습 아웃라인과 겹친다** — 기습자는 이미 `AmbushOutline` 레이어에 올라가 있다.
  마지막 일격 순간에 기습이 진행 중일 수 있고, 그러면 **그 적만 실루엣에서 빠진다.**

### 4-2. C가 레이어를 안 쓰는 대신 무엇을 포기하는가
깊이 문턱은 **"카메라에서 얼마나 가까운가"**만 안다. 무대가 평면이라(§13) 바닥은 카메라 바로 앞부터
지평선까지 연속이므로 문턱 하나로는 못 자른다 — 발밑 바닥은 검게, 먼 바닥은 붉게 나온다.
레퍼런스 1번(킬 빌)은 실제로 **바닥이 검은 띠**라 이 산출물이 오히려 그림에 가깝다.
다만 **다른 적(먼 무리)은 붉게 칠해진다** — "적은 검게"라는 지시와 어긋난다.

---

## 5. 되돌릴 것 — 이 연출이 끝날 때 반드시 원복해야 하는 것들

`ComboPostFxView`·`CameraDirector`가 세운 규율과 같다.

| 대상 | 안 되돌리면 |
|---|---|
| 렌더러 피처 `SetActive(false)` | **화면이 빨간 채로 굳는다** |
| 레이어 스왑(A·B) | 다음 곡의 적이 실루엣으로 나온다 |
| `OnDisable`에서의 원복 | 도중에 컴포넌트가 꺼지면 위 둘이 영구화된다 |

⚠ **`ScriptableRendererFeature.SetActive`는 렌더러 **에셋**의 상태를 바꾼다.** 플레이 모드에서 바꾸면
에디터 세션에 남을 수 있다 — `ComboPostFxView`가 `sharedProfile`을 안 건드리는 것과 같은 부류의 함정이다.

---

## 6. 곡 종료 흐름과의 충돌 지점

마지막 패턴 성공 직후 순서:

```
마지막 노드 입력 → OnPatternComplete(AllCorrect)
   └ +goodWindow+ImpactOffset → ImpactTime()  ← 히트스톱 · 쉐이크 · 절단 · (실루엣)
                                    ↓
                      (오디오 아웃트로 수 초)
                                    ↓
                      audioSource 종료 → ChartPlayer.OnSongEnded
                                          ├ EnemyDirector.DissolveAll()  ← 남은 적이 사라진다
                                          └ ScoreDirector.HandleSongEnded → GameSession.LastResult
```

- **실루엣이 `OnSongEnded`까지 살아 있으면 `DissolveAll`과 겹친다** — 검은 실루엣들이 스르르 사라진다.
  그림으로 나쁘지 않을 수 있으나 **의도해서 고를 문제**다.
- `GameSession.LastResult`는 아직 **읽는 쪽이 없다**(§12) — 결과 화면 미구현.
  이 연출은 그 화면으로 넘어가는 자리의 유력한 후보이지만, **지금은 넘어갈 곳이 없다.**

---

## 7. 결론 — 설계가 답해야 할 질문

1. **캐릭터/배경 분리 방식** — A(레이어 스왑) / B(스텐실) / C(깊이 문턱) 중 무엇인가.
2. **지속 시간과 끝맺음** — 몇 초 유지하고, 원래 화면으로 **돌아오는가** 아니면 그대로 **결과로 넘어가는가**.
3. **실패로 끝나면?** — 저작자 지시는 "성공한다면"이므로 실패 시 무연출이 기본.
4. **HUD를 덮는가** — `ScreenSpaceOverlay`라 자동으로 위에 남는다. 남길 것인가 감출 것인가.
5. **트리거 소유자** — `ChartPlayer`에 이벤트를 하나 추가할 것인가, 아니면 `ScoreDirector`처럼
   채보를 직접 읽어 세는 순수 소비자로 둘 것인가.
