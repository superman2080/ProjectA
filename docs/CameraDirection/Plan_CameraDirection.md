# Plan: 카메라 연출 (CameraDirection)

근거: [Research_CameraDirection.md](Research_CameraDirection.md)

## 목표

**패턴 성공·실패 모두에 짧은 카메라 쉐이크**를 넣되, 각각 화면에서 실제로 사건이 일어나는 순간에 맞춘다.
동시에, 이후 카메라 워킹이 **코드 수정 없이 카탈로그 한 줄로** 얹히는 뼈대를 세운다.

## 확정된 설계 결정

### 세 개의 큐와 각각의 시각

| 큐 | 시각 | 동기되는 연출 | 이벤트 |
|---|---|---|---|
| `PatternSuccess` | `Deadline + Pattern.SliceTargetImpactOffset` | 칼날 임팩트 프레임 · 표적 **절단** | `OnPatternComplete` (`AllCorrect`) |
| `PatternMiss` | **첫 미스 순간(즉시)** | 피격(Hit) 애니메이션 | `OnJudgeTargetFirstMiss` |
| `PatternFailure` | `Deadline + Pattern.SliceTargetImpactOffset` | 표적 **충돌·소멸** | `OnPatternComplete` (`!AllCorrect`) |

실패는 **사건이 둘**이라 큐도 둘이다 — 피격은 미스 순간에 즉시 나오고, 표적 파괴는 `Deadline`이다(Research §3).

### 그 밖

| 결정 | 내용 |
|---|---|
| 쉐이크 구현 | **기존 `CinemachineBasicMultiChannelPerlin`을 코드로 구동** (Impulse 미사용) |
| 확장 구조 | `EffectManager` + `EffectCatalog` 관례를 그대로 따른다 — "연출 추가 = 카탈로그에 한 줄" |
| 관심사 | 판정 파이프라인에 개입하지 않는 **순수 연출**. `PatternHandler`는 `GoodWindow` getter 한 줄만 늘린다 |

## 구조

```
PatternHandler ──OnPatternComplete──────────────┐   성공/실패 → Deadline에 예약
               ──OnJudgeTargetFirstMiss─────────┤   피격 → 즉시
               ──OnAllPatternsCleared───────────┤   정리
               (Deadline = LastNodeTime + GoodWindow)
                                               ▼
                                        CameraDirector ──읽기──> CameraCueCatalog
                                               │                  (트리거 → 큐 설정)
                                               ├─ 예약 큐(발사 시각 도달 시 Play)
                                               └─ ApplyShake() ── CinemachineBasicMultiChannelPerlin
                                                  ↑ 실행부는 여기 한 곳에만 격리한다
                                                    (나중에 Impulse로 갈아끼워도 위층은 불변)
```

**확장이 열리는 지점은 세 곳이고, 각각 무엇을 위한 것인지 명확하다:**

| 확장 축 | 방법 | 예 |
|---|---|---|
| **언제** | `CameraTrigger` enum에 값 추가 + `CameraDirector`가 해당 이벤트 구독 | 콤보 달성, 곡 시작/종료, 미스 |
| **무엇을** | `CameraCue`에 필드 추가 | FOV 펀치, 롤(기울기), 히트스톱 |
| **어떻게** | `ApplyShake` 교체 | Perlin → Cinemachine Impulse |

**지금은 쉐이크 하나만 구현한다**(YAGNI). 위 표는 "구멍을 미리 뚫어 둔다"가 아니라
**"이 축으로 넓히면 된다"는 방향만 코드 구조로 남긴다**는 뜻이다.

## 쉐이크 수식

```
t     = (now - startTime) / duration        // 0 → 1
decay = (1 - t) * (1 - t)                   // 1 → 0, 끝에서 부드럽게

perlin.AmplitudeGain = idleAmplitude + amplitude      * decay
perlin.FrequencyGain = idleFrequency + shakeFrequency * decay

t >= 1 → 휴지값(idleAmplitude / idleFrequency)으로 복귀
```

감쇠는 `AnimationCurve` 대신 **공식**이다 — 세 큐 모두 1→0이라 곡선을 셋 오서링할 이유가 없다(ponytail-review 반영).

- **휴지값은 `Awake`에서 씬의 현재 값을 읽어 캐시한다** — 0으로 되돌리면 상시 흔들림 연출이 있을 때 그것을 지워버린다(Research 제약 2).

### 겹침 규칙 (반드시 정의해야 한다)

Perlin은 채널이 하나라 합성되지 않는다. 그리고 **실패 경로에서는 겹침이 확실히 일어난다** —
마지막 노드 미스면 `PatternMiss`와 `PatternFailure`가 `goodWindow`(0.1초) 간격으로 붙는다(Research §3, 제약 1).

규칙: **새 쉐이크가 오면 타이머를 재시작하되, 진폭은 `max(현재 남은 진폭, 새 진폭)`으로 잡는다.**

```
amplitude = max(amplitude * decay(현재 t), newAmplitude)
startTime = now
```

- 단순 덮어쓰기였다면 강한 쉐이크가 진행 중일 때 약한 쉐이크가 와서 **세기가 뚝 떨어진다.** 이 규칙은 그 낙차를 막는다.
- 겹친 둘은 하나의 연속된 흔들림으로 체감된다 — 실패 시 카메라가 두 번 따로 떠는 것보다 낫다.
- 주파수는 공용 값이라 `max` 대상이 아니다. **한 줄이면 되는 규칙이라 남긴다** — ponytail-review는 이것도 컷 후보로 봤지만, 없애면 실패 경로(겹침이 확실한 유일한 경로)에서 낙차가 그대로 드러난다.

---

## 구현 단계

- [x] **Step 1 — `PatternHandler.GoodWindow` 공개**
  - `Assets/02. Scripts/UI/PatternHandler.cs`
  - `public float GoodWindow => goodWindow;` **한 줄.** 소비자가 `LastNodeTime + GoodWindow`로 `Deadline`을 만든다.
  - `PatternCompletionInfo`는 **건드리지 않는다** — 구조체·생성자·전 호출부 갱신이 사라진다.
    (ponytail-review 반영. 원안은 구조체에 `Deadline` 필드를 추가하는 것이었다.)

- [x] **Step 2 — `CameraCueCatalog` 작성 (트리거 + 큐 데이터)**
  - `Assets/02. Scripts/Camera/CameraCueCatalog.cs` (신규 폴더 `Camera/`, 전역 네임스페이스 — `EffectCatalog` 선례)
  - `public enum CameraTrigger { PatternSuccess, PatternMiss, PatternFailure }` — 확장 시 여기에 값을 추가한다.
    (`PatternMiss` = 피격 순간 / `PatternFailure` = 표적 충돌. 실패는 사건이 둘이라 키도 둘이다.)
  - `[Serializable] public struct CameraCueEntry`:
    - `CameraTrigger trigger`
    - `float shakeAmplitude` / `float shakeDuration`
  - `EffectEntry`와 같은 성격의 순수 데이터다. **엔트리가 없거나 `shakeDuration <= 0`이면 무연출**(`EffectCatalog`의 "prefab 비면 무연출"과 동일한 규칙).
  - ponytail-review 반영으로 **뺀 것**: 엔트리별 `AnimationCurve`(셋 다 1→0 감쇠라 공식으로 대체), 엔트리별 `shakeFrequency`(0.2초 쉐이크에서 차이가 체감되지 않아 director 공용 필드 하나로).

- [x] **Step 3 — `CameraDirector` 작성**
  - `Assets/02. Scripts/Camera/CameraDirector.cs`
  - 직렬화 필드: `PatternHandler handler`, `CinemachineBasicMultiChannelPerlin perlin`, `List<CameraCueEntry> catalog`, `float shakeFrequency`(공용)
  - `Awake`: 카탈로그를 `Dictionary<CameraTrigger, CameraCueEntry>`로 인덱싱(`EffectManager.BuildPools` 선례), **Perlin의 현재 값을 휴지값으로 캐시**, 배선 누락 시 `Debug.LogError`.
  - `OnEnable/OnDisable`: `handler.OnPatternComplete`, `handler.OnJudgeTargetFirstMiss`, `handler.OnAllPatternsCleared` 구독·해제.
  - `HandlePatternComplete(info)`:
    - 트리거 선택: `info.AllCorrect ? PatternSuccess : PatternFailure`.
    - 발사 시각 `fireTime = info.LastNodeTime + handler.GoodWindow + info.Pattern.SliceTargetImpactOffset`.
    - **`fireTime`이 이미 지났으면 즉시 발사.** 실패(만료) 경로는 `Deadline` 직후에 오므로 **이쪽이 정상 경로다** — 예외 처리가 아니다(Research §6).
  - **예약은 단일 필드**(`pendingFireTime` + `pendingTrigger`)다. 리스트가 필요 없는 이유: 패턴 완료는 순차적이고, A의 `Deadline`은 A 마지막 노드 +0.1초인데 B의 완료는 A보다 최소 0.4초 뒤다 → **동시 대기는 최대 1개.** (ponytail-review 반영)
  - `HandleFirstMiss()`: `PatternMiss` 큐를 **즉시** 재생한다(예약하지 않는다). 이벤트는 인자가 없고 패턴당 1회만 온다(Research §6-1).
  - `Update()`: 예약 시각이 지났으면 발사하고, 진행 중인 쉐이크를 매 프레임 갱신한다.
  - `HandleAllCleared()`: 예약 비우기 + 즉시 휴지값 복귀(Research 제약 6).
  - `OnDisable`에서도 휴지값 복귀 — 꺼진 채 흔들림이 남지 않도록.
  - **`ApplyShake(float amplitude, float frequency)` 한 메서드에만 Cinemachine 타입이 등장하게 한다.** Research §2의 교체 가능성을 위한 격리다.

- [x] **Step 4 — 씬 배선**
  - `DefaultScene`에 `CameraDirector`를 배치한다(`Main Camera` 또는 연출 매니저 오브젝트).
  - `handler` → 씬의 `PatternHandler`, `perlin` → `CinemachineCamera`의 `CinemachineBasicMultiChannelPerlin`.
  - 공용 `shakeFrequency` = 1.6.
  - 카탈로그에 세 줄 추가. 초기값 제안:

    | 트리거 | amplitude | duration | 의도 |
    |---|---|---|---|
    | `PatternSuccess` | 1.2 | 0.18 | 날카롭고 짧게 — 절단의 쾌감 |
    | `PatternMiss` | 0.8 | 0.22 | 낮고 둔탁하게 — 맞은 느낌 |
    | `PatternFailure` | 1.0 | 0.20 | 표적이 부딪혀 무너지는 무게감 |

  - **셋 다 짧게 시작한다** — 리듬게임에서 긴 쉐이크는 다음 패턴 조준을 방해한다.
    특히 실패 시엔 두 쉐이크가 이어질 수 있어(겹침 규칙) 개별 `duration`을 더 늘리지 않는다.

- [x] **Step 5 — 문서 갱신**
  - `CLAUDE.md`: 폴더 구조에 `Camera/` 항목, 새 섹션 "카메라 연출"(트리거 시각이 `Deadline`으로 통일된다는 점, 카탈로그 확장 규칙, Perlin 겹침 한계).
  - 이벤트 확장 포인트 절의 `OnPatternComplete` 설명에 `Deadline` 포함을 반영.
  - 본 Plan의 체크박스 갱신.

- [x] **Step 6 — 검증** *(정적 검증만 완료 — 플레이 확인은 사람이 해야 함)*
  - `refresh_unity(force, compile)` 후 `read_console` — **에러/경고 0건**, `compilation.is_compiling = false`. ✅
  - 씬 배선 재조회 — `handler`→`PointBackground`, `perlin`→`CinemachineCamera`, 카탈로그 3행, `shakeFrequency` 1.6. ✅
  - `PatternHandler.GoodWindow`가 인스펙터 리소스에 `0.1`로 노출되는 것 확인(getter 동작). ✅
  - **실측**: 씬 Perlin의 `AmplitudeGain`이 **0.1**이었다(0 아님) — 휴지값 캐시가 실제로 필요했다(Research 제약 2 실증).
  - **미검증(플레이 모드 육안 확인 필요)**:
    - **성공**: 표적이 갈라지는 **바로 그 순간** 흔들리는가(칼날·절단·쉐이크가 한 순간에 모이는가).
    - **패턴 중간 미스**: 피격 모션이 나오는 순간 흔들리고, 이후 `Deadline`에 표적이 충돌할 때 한 번 더 흔들리는가.
    - **마지막 노드 미스**(겹침 최악 케이스): 두 쉐이크가 0.1초 간격으로 붙을 때 세기가 중간에 **떨어지지 않고** 하나의 연속된 흔들림으로 느껴지는가 — 겹침 규칙이 실제로 동작하는지 보는 지점이다.
    - 실패했는데 성공 쉐이크가(또는 그 반대로) 나오지 않는가.
    - 연속 패턴에서 쉐이크가 끊기거나 누적되어 남지 않는가.
    - 곡 중단(`ClearAllPatterns`) 후 흔들림이 즉시 멎는가.
    - 쉐이크가 끝난 뒤 카메라가 **원래 상태**로 돌아오는가(휴지값 복원).

---

## 건드리지 않는 것

- 판정 파이프라인 — `PatternHandler`는 `GoodWindow` getter 한 줄만 추가한다. `PatternCompletionInfo`는 무변경.
- `SliceTargetDirector` / `CharacterActionPlayer` / `EffectManager`.
- 씬의 vcam 구성(follow/aim/priority) — 카메라 워킹이 실제로 필요해질 때 정한다.
- Cinemachine Impulse 도입 — 겹침이 실제로 문제가 될 때 `ApplyShake`만 교체한다.

## 명시적으로 하지 않는 것 (YAGNI)

지금 만들지 않는다. 필요해질 때 위 "확장 축" 표대로 붙인다.

- FOV 펀치 / 히트스톱 / 카메라 롤
- vcam 여러 대 + priority 블렌딩
- 쉐이크 프리셋 ScriptableObject (`SliceSet`처럼 에셋화) — 카탈로그 인스펙터로 충분한지 먼저 본다
- `Time.timeScale` 비종속 시간축 (Research 제약 5) — 히트스톱이 실제로 들어올 때
