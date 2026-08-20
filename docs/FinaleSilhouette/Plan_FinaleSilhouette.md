# Plan — 마지막 일격 실루엣 (FinaleSilhouette)

근거: `docs/FinaleSilhouette/Research_FinaleSilhouette.md`

---

## 설계 요약

> **곡의 마지막 패턴을 성공으로 끝낸 순간, 칼이 닿는 그 프레임에 화면이 붉게 물들고 배우들만 검은 실루엣으로 남는다.**

관례는 `CameraDirector`·`HitStopDirector`와 같다 — **기존 이벤트만 구독하는 순수 소비자**이고,
**판정에 개입하지 않으며**, 배선이 비면 조용히 비활성된다.

```
PatternHandler.OnPatternComplete ─┐
                                  ├→ FinaleSilhouetteDirector ─→ 렌더러 피처 2장 on/off
ChartPlayer.ActiveChart(엔트리 수) ┘        (예약 시각 = info.ImpactTime())
```

---

## 소유 관계

| 무엇 | 누가 든다 | 왜 |
|---|---|---|
| "마지막 패턴인가" | `FinaleSilhouetteDirector`가 **채보를 읽어 센다** | `ScoreDirector`가 이미 같은 방식(§Research 1-2). `ChartPlayer`를 안 고친다 |
| 터지는 시각 | `info.ImpactTime()` | §6·§7-1·§7-3과 **같은 확장 메서드**. 식을 손으로 다시 조립하지 않는다 |
| 색·강도 | **머티리얼**(언릿 2장) | "연출 조정 = 에셋 한 곳" — `AmbushOutlineMaterial`과 같은 결 |
| 지속·페이드 | 디렉터 인스펙터 | 시간은 연출 파라미터지 에셋 성질이 아니다 |
| 켜고 끄기 | `silhouetteEnabled` bool | §7-3의 연출 토글 규율 |

## 하지 않을 것 (Non-goal)

- **판정·점수·채보에 손대지 않는다.** `PatternHandler`·`ChartPlayer`·`ScoreDirector`는 한 줄도 안 고친다.
- **결과 화면을 만들지 않는다.** `GameSession.LastResult`를 읽는 쪽은 여전히 없다(§12) — 이 연출은
  그 자리로 가는 **문**이지 문 너머가 아니다.
- **새 시계를 만들지 않는다.** 예약은 `Time.time` 기준 하나뿐이다.
  ⚠ `Time.timeScale`은 **발사 이후에만** 건드린다 — 그 경계의 근거는 Step 4-1.
- **`OnSongEnded`를 트리거로 쓰지 않는다.** 오디오 아웃트로만큼 늦다(§Research 1-1).

---

## ⚠ 먼저 정해야 할 것 — 분리 방식

Research §4의 세 후보 중 **A(레이어 스왑 + `RenderObjects` 2패스)**를 기본안으로 잡는다.

**근거**: ① §11-8이 이미 같은 관용구를 쓰고 있어 새로 배울 것이 0이다 ② 지시("적과 나는 검게,
나머지 배경을 빨갛게")를 **정확히** 만족하는 유일한 안이다 — C(깊이 문턱)는 먼 무리를 붉게 칠하고
바닥을 검게 칠한다 ③ B(스텐실)는 A와 같은 레이어 비용에 셰이더가 하나 더 붙는다.

**대가**는 레이어 복구다(Research §4-1). Step 3이 그것만 다룬다.

> **확정: A(레이어 스왑).**

---

## 단계

### - [x] Step 1 — 트리거: `FinaleSilhouetteDirector` 뼈대

`Assets/02. Scripts/Effect/FinaleSilhouetteDirector.cs` (네임스페이스 전역, `EffectManager` 옆).

- `PatternHandler.OnPatternComplete` · `OnAllPatternsCleared`만 구독. `OnEnable`/`OnDisable` 대칭.
- 완료 횟수를 세고 `chartPlayer.ActiveChart.entries.Length`와 비교해 **마지막 엔트리**를 판정.
  - ⚠ **`AllCorrect`가 false면 아무 일도 안 한다**(저작자 지시: "성공한다면").
  - ⚠ 채보가 없거나(`ActiveChart == null`) 엔트리가 0이면 **조용히 비활성** — 디버그 재생 보호.
- `info.ImpactTime()`에 예약. **예약은 최대 하나**(§7-1과 같은 근거: 마지막 패턴은 하나뿐이다).
- `OnAllPatternsCleared`에서 예약을 회수하고 즉시 원복(곡 중단).

**검증**: 오토플레이(§8)로 한 곡 완주 → 마지막 패턴에서만 `Fire()`가 한 번 불린다(로그).

---

### - [x] Step 2 — 그림: 레이어 · 머티리얼 · 렌더러 피처

**레이어**: `ProjectSettings/TagManager.asset`의 빈 슬롯에 **`Silhouette`**(10번) 추가.

**머티리얼 2장** (`Assets/07. Materials/`):
| 이름 | 셰이더 | 색 |
|---|---|---|
| `FinaleSilhouetteBlack` | `Universal Render Pipeline/Unlit` | 검정 |
| `FinaleSilhouetteRed` | `Universal Render Pipeline/Unlit` | 빨강 |

**`PC_Renderer`에 `RenderObjects` 피처 2장**(둘 다 `m_Active: 0`으로 시작):

| 이름 | LayerMask | 오버라이드 머티리얼 | Event |
|---|---|---|---|
| `FinaleBackground` | `Silhouette`를 **뺀** 나머지 전부 | `FinaleSilhouetteRed` | `AfterRenderingOpaques` |
| `FinaleActors` | `Silhouette`만 | `FinaleSilhouetteBlack` | `AfterRenderingOpaques`(뒤 순서) |

- ⚠ **깊이 상태를 건드리지 않는다**(`overrideDepthState = 0`). §11-8이 정확히 이걸로 데였다 —
  `depthCompareFunction = Always`로 열면 가려진 면까지 칠해져 그림이 무너진다.
- ⚠ `Mobile_Renderer`에도 같은 2장이 필요하다. 안 붙이면 그 품질 등급에서만 연출이 없다.

**검증**: 피처 둘을 인스펙터에서 수동으로 켜고 씬을 재생 → 레퍼런스 구도가 나오는지 눈으로.

---

### - [x] Step 3 — ⚠ 레이어 스왑과 **복구**

이 설계의 유일한 위험 지점이다(Research §4-1).

- `FinaleSilhouetteDirector`가 발사 시각에 **대상 루트들의 서브트리 레이어를 통째로 바꾼다**:
  플레이어 루트 · `EnemyDirector`가 아는 살아 있는 적 전부 · 시체(`CorpseView`) · 절단 조각.
- **바꾸기 전 원래 레이어를 오브젝트별로 기록**하고, 원복은 그 기록으로만 한다.
  전부 0으로 되돌리면 **`AmbushOutline`(9번)에 올라가 있던 기습자가 아웃라인을 잃는다**(§11-8).
- ⚠ **적은 풀에서 나온다.** 곡이 끝나기 전에 원복이 안 끝나면 다음 대여가 실루엣으로 나온다 —
  §11-8이 `Finish()`·`Abort()`·`ResetState` 세 곳에서 끄는 것과 같은 이유로
  **`OnDisable`·`OnAllPatternsCleared`에도 원복 경로를 둔다.**
- ⚠ **발사 이후 새로 생기는 적은 실루엣이 아니다.** 마지막 패턴이라 새 스폰이 없는 게 정상이지만
  (§11-6은 사망 1:스폰 1이라 마지막 처치도 하나 태운다), **그 적은 무대 밖에서 걸어오므로
  화면에 없다** — 감수하고 넘어간다.

**검증**: 곡 두 번 연속 플레이 → 두 번째 곡의 적이 정상 색으로 나온다.

---

### - [x] Step 4 — 시간: 슬로우모션 · 유지 · 이탈  (저작자 확정)

**확정값**: `Time.timeScale = 0.1` · **실시간 1초 노출** · 그 뒤 원래 화면 복귀.

#### 4-1. ⚠ 여기서만 `Time.timeScale`을 쓸 수 있다 — 그 근거를 명시한다

§7-3은 `Time.timeScale`을 **금지**한다. 근거는 하나다 — 판정·클립 정렬은 `Time.time`인데 채보는
`audioSource.time`으로 돌고 **오디오는 timeScale의 지배를 받지 않아** 차이가 **영구 누적**되며,
`perfectWindow`가 0.05초라 한 번으로 판정이 무너진다.

**그 근거가 여기서만 성립하지 않는다.** 이 시점 이후로 **판정할 패턴이 없다** —
마지막 엔트리의 마지막 노드가 이미 입력됐고 `pendingEntries`는 비었다. 누적될 차이가 도달할 곳이 없다.

그래서 이 절은 §7-3의 **예외가 아니라 그 규칙의 경계**다. 규칙을 다시 쓴다:

> **`Time.timeScale`은 판정이 남아 있는 동안 못 쓴다.** 곡의 마지막 판정이 끝난 뒤에는
> 누적될 곳이 없으므로 쓸 수 있고, **되돌리는 책임만 남는다.**

⚠ 그래서 **`AllCorrect`가 false여도, 마지막 패턴이 아니어도 절대 건드리면 안 된다.**
트리거 조건(Step 1)이 곧 이 안전 조건이다.

#### 4-2. ⚠ 히트스톱과 정면으로 부딪힌다 — 그대로 두면 슬로우가 아니라 정지가 된다

`HitStopDirector`는 **같은 임팩트 프레임**에 발사되고(§7-3), 해제 시각을 `Time.time + hitStopDuration`으로 잡는다.
`hitStopDuration = 0.1f`(`HitStopDirector.cs:56`)인데 **`Time.time`은 스케일된 시계**다:

```
timeScale 0.1  →  0.1초(게임) = 1.0초(실시간)  =  실루엣 창 전체
```

즉 **노출 1초 내내 애니메이터가 `Speed = 0`으로 얼어 있고**, `CameraDirector.HoldForHitStop`이
그 동안 `CinemachineBrain.enabled = false`로 카메라까지 굳힌다. 결과는 슬로우모션이 아니라 **정지 컷**이다.

→ **마지막 일격에서는 히트스톱을 억제한다.** 슬로우가 그 역할을 대신하므로 타격감이 줄지 않는다.
`HitStopDirector`에 억제 훅을 하나 두고(`SuppressNext()` 또는 `OnPatternComplete` 이전 판단),
**층 분리는 유지한다** — 실루엣 디렉터가 애니메이터·카메라를 직접 만지지 않는다(§7-3의 규율).

#### 4-3. ⚠ 노출 타이머는 반드시 `Time.unscaledDeltaTime`이다
`Time.deltaTime`으로 재면 0.1배속에서 **10초** 노출된다. 프로젝트 안에 unscaled 시간을 쓰는 코드가
지금 한 줄도 없으므로(전수 확인) **이 클래스가 유일한 사용처**이고, 그래서 헷갈릴 여지가 크다.

#### 4-4. 오디오는 안 느려진다
오디오는 timeScale 밖이라(§7-3) **음악만 정상 속도로 흐른다.** 곡의 아웃트로 구간이라
"느려진 화면 위로 곡이 그대로 끝난다"로 읽힐 수도 있다.

**확정: ① 정상 속도.** 다만 연출을 보고 나중에 바꿀 수 있게 `audioPitchScale` 노브를 인스펙터에 남긴다
(기본 1 = 안 건드림). 내리면 `RaiseSongEndedIfFinished`가 `audioSource.isPlaying`을 보므로
**곡 종료가 그만큼 늦어진다** — 노브 툴팁에 적어 둔다.

#### 4-5. 진입·이탈
- 발사 즉시 피처 둘 `SetActive(true)` + `Time.timeScale = 0.1` — **페이드 인 없이 즉발**.
  타격은 사건이라 서서히 물들면 "임팩트"가 아니라 "전환"으로 읽힌다(§12의 콤보 브레이크와 같은 근거).
- 실시간 1초 유지 후 `Time.timeScale = 1` 복귀 + 피처 `SetActive(false)`.
- ⚠ **`Time.timeScale` 복구 경로가 셋이다** — 정상 종료 · `OnAllPatternsCleared`(곡 중단) · `OnDisable`.
  하나라도 빠지면 **게임이 0.1배속으로 굳는다.** 렌더러 피처 원복과 같은 자리에 묶어 둔다.
- `fixedDeltaTime`은 안 건드린다 — 이 게임은 이동·조각에 물리를 안 쓴다(§11-2·§11).

### - [x] Step 5 — 토글과 배선 누락 내성

- `silhouetteEnabled` bool 하나로 층 전체가 죽는다(§7-3의 연출 토글과 같은 결).
- `patternHandler`·`chartPlayer`·피처 참조 중 **하나라도 비면 조용히 비활성**한다
  — 경고를 찍되 다른 층은 그대로 돈다(프로젝트 전반의 규율).
- ⚠ **`ScriptableRendererFeature.SetActive`는 렌더러 에셋의 상태를 바꾼다.** 플레이 종료 시
  반드시 false로 되돌린다 — 안 그러면 **에디터 세션에 빨간 화면이 남는다**(Research §5).

---

### - [x] Step 6 — HUD 처리 결정

패턴인풋 루트 Canvas가 `ScreenSpaceOverlay`라 **점수 HUD·콤보 포스트FX가 실루엣 위에 그대로 남는다**(§7-5).

**확정: ② 감춘다.**

- `ScoreHudView`에 `CanvasGroup`을 붙이고 `SetHidden(bool)` 하나만 연다 — 여전히 **순수 표시**이고,
  누가 언제 감출지는 실루엣 디렉터가 정한다(표시 계층이 게임플레이를 알지 않는다).
- ⚠ **`ComboPostFxView`의 Volume은 안 건드린다.** 붉은 비네트라 실루엣의 빨강과 같은 방향이고,
  weight를 만지면 그 클래스의 "weight만 민다" 규율을 두 주인이 나눠 갖게 된다.

---

### - [x] Step 7 — 문서

- `CLAUDE.md`에 **§14 마무리 실루엣** 한 절 추가 — 트리거 앵커가 `info.ImpactTime()`이라는 것,
  레이어 복구가 유일한 위험 지점이라는 것, 토글 이름.
- `docs/FinaleSilhouette/`에 최종 설계 반영.

---

## 검증 기준

| 항목 | 통과 조건 |
|---|---|
| 트리거 | 마지막 패턴 **성공에서만** 1회. 실패·중간 패턴에서는 0회 |
| 시각 | 카메라 쉐이크·절단과 **같은 프레임** |
| 슬로우 | 실시간 1초 노출(0.1배속). **정지가 아니라 느린 움직임**으로 보인다 |
| 시계 복구 | 정상 종료·곡 중단·`OnDisable` **세 경로 모두**에서 `Time.timeScale == 1` |
| 그림 | 플레이어·적·시체·조각이 검정, 나머지 전부 빨강 |
| 복구 | 곡 2회 연속 플레이 시 2회차 적이 정상 색 |
| 안전 | 플레이 종료 후 에디터 화면·렌더러 에셋이 원상 |
| 무해성 | `silhouetteEnabled = false`면 화면이 1픽셀도 안 바뀐다 |

---

## 열린 질문 (`>>>`로 답해 주세요)

전부 확정됐다.

1. **분리 방식** — **A(레이어 스왑)**
2. **끝맺음** — **0.1배속 · 실시간 1초 · 원래 화면 복귀**
3. **HUD** — **감춘다**
4. **오디오** — **정상 속도**(`audioPitchScale` 노브만 남김, 연출 보고 재결정)
5. **색** — 단색 빨강으로 시작. 그라데이션이 필요하면 `ComboPostFxVolume`의 비네트가 이미 같은 방향이다
4. **색** — 정확한 빨강 값. 레퍼런스는 중앙이 밝고 가장자리가 어두운 **그라데이션**인데,
   단색으로 갈지 비네트를 겹칠지(겹치면 `ComboPostFxView`의 Volume을 재사용할 수 있다)
