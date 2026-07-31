# Research: 포커스 링 (낙하 노드 → 축소 링 교체)

## 목표

노드가 위에서 떨어지는 대신, **입력해야 할 Point 자리에 `Focus.png` 링이 크게 나타나 점점 줄어들고, 링 크기가 Point와 정확히 같아지는 순간이 입력 타이밍**이 되도록 바꾼다. 리듬게임의 어프로치 서클(approach circle) 방식이다.

시선이 화면 위쪽(노드가 떨어지는 곳)과 아래쪽(입력하는 곳)으로 갈리지 않게 하는 것이 목적으로, 직전에 진행한 "타일 입력 영역과 플레이어 시점 분리 제거"(TileArea 삭제 · 격자 중앙 배치)와 같은 방향의 작업이다.

---

## 1. 현재 낙하 노드 파이프라인

### 1-1. 데이터 흐름

```
PatternHandler.SetPattern(pattern, inputTimes, spawnTimes?, exposureDurations?)
  └─ 노드마다 ScheduledSpawn { owner, position, spawnTime, fallDuration } 적재
       ├─ spawnTimes 있음(채보 재생): spawnOffset = spawnTimes[i]
       │                              fallDuration = inputTime - spawnOffset
       └─ 없음(디버그/수동):          fallDuration = ComputeFallDuration(pointIndex, exposureDuration)
                                      spawnOffset  = inputTime - fallDuration

PatternHandler.Update()
  └─ ProcessFallingNodeSpawns()  : Time.time >= spawnTime 인 예약을 스폰
       └─ SpawnFallingNode(owner, position, duration)
            ├─ Pool.Instance.Get<FallingNodeView>(PoolKey.FallingNode, init)
            ├─ FallingNodeView.Initialize(displayIndex, color, nodeType, targetLocalPos, fallSpawnPositionY, fallDuration)
            ├─ activeFallingNodes 에 { owner, position, view } 등록
            └─ OnFallingNodeSpawned(pointIndex, nodeType, worldPos) 발행

FallingNodeView.OnSpawn() → FallRoutine()
  └─ anchoredPosition 을 (targetX, fallSpawnPositionY) → targetLocalPos 로 fallDuration 동안 Lerp
  └─ 끝나면 OnArrived 발행
```

핵심 파일: `Assets/02. Scripts/UI/PatternHandler.cs`, `Assets/02. Scripts/UI/FallingNodeView.cs`

### 1-2. 노드가 사라지는 세 경로

| 경로 | 트리거 | 코드 | 발행 이벤트 |
|---|---|---|---|
| 판정됨 | 플레이어가 맞춤 | `ReleaseNodeOf(target, position)` (PatternHandler.cs:586) | `OnFallingNodeResolved(index, type, world, result)` |
| 미입력 도착 | 낙하 완료 | `HandleFallingNodeArrived` (PatternHandler.cs:668) | `OnFallingNodeMissedArrival(index, type, world)` |
| 패턴 종료 | 완료/만료 | `ClearNodesOf(owner)` (PatternHandler.cs:705) | 없음 (조용히 회수) |

세 경로 모두 `Pool.Instance.Return(PoolKey.FallingNode, node)`로 끝난다. **이 3분기 구조는 링으로 바뀌어도 그대로 유효하다** — 바뀌는 건 "노드가 어떻게 보이느냐"뿐이다.

### 1-3. 낙하 시간 계산 (`ComputeFallDuration`)

```csharp
totalDistance   = fallSpawnPositionY - targetLocalPos.y
visibleDistance = clamp(screenTopY - targetLocalPos.y, 0, totalDistance)
return totalDistance * exposureDuration / visibleDistance
```

생성 Y는 모든 행이 같지만 상단 행은 화면 밖 구간이 길어 노출 시간이 짧아지므로, **화면 안쪽 구간만 `exposureDuration` 동안** 이동하도록 전체 낙하 시간을 역산한다. 그래서 행마다 낙하 속도가 다르다.

**이 메서드가 이 시스템에서 유일하게 화면 기하에 의존하는 지점**이며, `fallSpawnPositionY` · `screenTopY` · `ComputeScreenTopY()`는 오로지 이것을 위해 존재한다.

#### 현재 이 계산이 깨져 있다

포인트 간격을 150 → 700으로 바꾸면서 격자가 1400 높이가 됐고, 상단 행(local +700 = world 1780)이 화면 상단(2160)에서 380px밖에 안 남게 됐다.

| 행 | 간격 150일 때 배수 | 간격 700일 때 배수 |
|---|---|---|
| 하단 | 1.99 | 2.39 |
| 중단 | 2.08 | 3.30 |
| 상단 | 2.19 | **7.53** |

`exposureDuration` 0.5초 기준 상단 행 노드가 입력 3.77초 전에 스폰된다. **링으로 바꾸면 이동 거리 자체가 없어져 이 문제가 통째로 사라진다** (`fallDuration == exposureDuration`, 행 간 차이 0).

### 1-4. 외부 의존

- **`PatternChartWindow.cs:215`** — 굽기 툴이 `referenceHandler.ComputeFallDuration(pointIndex, draft.exposureDurations[i])`를 호출해 `spawnTimes[i] = onsetTimes[i] - duration`을 굽는다. `ComputeFallDuration`이 `public`인 유일한 이유다.
- **`EffectManager.cs:54,64`** — `OnFallingNodeResolved`만 구독. `OnFallingNodeSpawned` / `OnFallingNodeMissedArrival`은 현재 구독자가 없지만 CLAUDE.md에 확장 포인트로 문서화돼 있다.
- 그 외 `OnFallingNode*` 이벤트를 쓰는 코드는 없다.

---

## 2. 채보 데이터와의 관계

`SongChartEntry` (`ChartGen/SongChart.cs`):

| 필드 | 의미 |
|---|---|
| `onsetTimes[]` | 판정 절대시각 |
| `exposureDurations[]` | 노출시간(= 플레이어에게 보이는 시간) |
| `spawnTimes[]` | **굽는 시점의 계산값 스냅샷** (`onsetTimes[i] - ComputeFallDuration(...)`) |

`ChartPlayer`는 `spawnTimes[0]`을 엔트리 투입 트리거로 쓰고, `spawnTimes`/`onsetTimes`를 `spawnTimes[0]` 기준 상대시각으로 바꿔 `SetPattern`에 넘긴다.

### 링 전환이 채보에 미치는 영향

- 새 계산식은 `spawnTimes[i] = onsetTimes[i] - exposureDurations[i]` (배수 없음).
- **기존에 구운 SongChart 에셋은 판정 타이밍이 틀어지지 않는다.** `fallDuration = inputTime - spawnTime`이라 도착 시각은 언제나 `inputTime`으로 맞춰지기 때문이다.
- 다만 **노출 시간은 authoring 의도와 어긋난다** — 굽힐 때 2.0~7.5배가 곱해진 값이 그대로 링의 수축 시간이 되어, 링이 의도보다 오래 떠 있는다. 정확히 맞추려면 `Tools/Pattern Chart Tool`로 **재굽기**가 필요하다.
- **스폰 리드타임이 크게 줄어든다.** CLAUDE.md §패턴 겹침 규칙이 근거로 삼는 "스폰 리드타임 ≈1.0초 > 엔트리 간 입력 간격 최소 0.4초"가 "리드타임 = `exposureDuration`(기본 0.5초)"으로 바뀐다. 0.5 > 0.4 이므로 **겹침은 여전히 발생하지만 빈도가 줄어든다.** 큐(`activePatterns`) · 소유자 추적 구조는 그대로 유효하므로 건드릴 필요가 없다.

---

## 3. 에셋 현황

### 3-1. `Assets/06. Sprites/Focus.png` — 지금 상태로는 못 쓴다

```
textureType        = Sprite
spriteImportMode   = Multiple      ← 문제
spriteSheet.sprites= []            ← 슬라이스 0개
LoadAsset<Sprite>  = NULL
텍스처 크기         = 1080 x 1080
```

`Multiple` 모드인데 슬라이스가 하나도 없어 **Sprite 서브에셋이 생성되지 않는다.** `Image.sprite`에 배정할 수 없으므로 임포트 설정을 `Single`로 바꾸는 것이 선행 작업이다.

(사용자가 말한 경로 `06. Sprite`의 실제 이름은 `06. Sprites`.)

### 3-2. `Assets/03. Prefabs/FallingNode.prefab`

```
FallingNode  (80x80)  RectTransform + CanvasRenderer + Image + FallingNodeView
└ IndexLabel          TMP_Text  (1~9 숫자)
```

`FallingNodeView`가 참조하는 것은 `background`(Image)와 `indexLabel`(TMP_Text) 둘뿐이다.

### 3-3. Point 구조 (크기 기준점)

```
Point_N       100 x 100   투명 Image(알파 0), raycastTarget=true   ← 판정 영역, Point.hitGraphic
└ Visual       60 x  60   Knob 스프라이트, raycastTarget=false     ← 시각 표현, Point.image
```

"포인트의 2배"의 기준이 **본체 100**인지 **눈에 보이는 노브 60**인지 갈린다. Plan에서 결정한다.

### 3-4. 렌더 순서

`PointBackground`의 자식 순서:

```
[0]  PatternGuideLine     ← 맨 아래
[1..9] Point_1 ~ Point_9
[10] FallingNodeParent    ← 노드(=링)는 Point 위에 그려진다
[11] PatternLine          ← 맨 위
```

링은 가운데가 뚫린 형태이므로 Point 위에 그려도 노브를 가리지 않는다. **현행 순서를 그대로 두면 된다.**

### 3-5. Pool

`PoolKey.FallingNode` → 씬의 `Pool` 오브젝트에서 `FallingNode.prefab`에 매핑. 대여 대상은 `IPoolable`(`OnSpawn`/`OnDespawn`).

---

## 4. 제약 · 주의사항

1. **`Focus.png` 임포트 설정 변경이 선행 조건.** Single로 안 바꾸면 프리팹에 스프라이트를 붙일 수 없다.
2. **`ComputeFallDuration`의 시그니처는 유지해야 한다.** 굽기 툴이 호출한다. 반환값만 `exposureDuration`으로 단순화하면 툴은 손대지 않아도 된다.
3. **`fallSpawnPositionY` · `screenTopY` · `ComputeScreenTopY()` · `OnDrawGizmos`는 전부 낙하 전용이라 함께 제거 대상**이다. 단 `EnsureLayoutInitialized()`의 `canvas` / `canvasCamera` 초기화는 마우스 드래그 라인이 쓰므로 남겨야 한다.
4. **`WorldToLocal(fallingNodeParent, worldPosition)`은 계속 필요하다** — 링을 해당 Point 자리에 놓아야 하므로.
5. **판정된 노드는 즉시 회수되고, 미입력 도착 노드도 도착 즉시 회수된다.** 즉 `goodWindow`(0.1초) 안의 늦은 Good 입력이 유효한 동안에도 노드는 이미 사라진 상태다. 이 동작은 현행 그대로이며, 링으로 바뀌어도 유지된다(변경하려면 별도 작업).
6. **클래스/키 이름이 의미와 어긋나게 된다.** 아무것도 낙하하지 않는데 `FallingNodeView` · `PoolKey.FallingNode` · `OnFallingNode*`가 남는다. 개명은 배선(프리팹·Pool 인스펙터·EffectManager·CLAUDE.md)까지 번지므로 Plan에서 독립 단계로 분리한다.
7. **`fallingNodeColorPalette`**(패턴별 순환 색)는 링에도 그대로 적용 가능하다.

---

## 5. 한 Point에 링이 둘 이상 생길 수 있는가

### 5-1. 한 패턴 안에서는 불가능

`Pattern.OnValidate`(Pattern.cs:106-113)가 중복 인덱스를 에러로 막는다. 한 패턴이 같은 Point를 두 번 쓸 수 없다.

### 5-2. 패턴 간에는 가능 (실측)

링의 생존 구간을 `[onset - exposureDuration, onset]`으로 놓고 기존 채보 2개를 전수 조사한 결과:

| 채보 | 노드 수 | 동일 Point 겹침 쌍 | 최대 겹침 |
|---|---|---|---|
| `Dreamer_lv10` | 279 | 7 | 0.100s |
| `Heleen…Dreamer (remix)_Lv10` | 282 | 28 | 0.100s |

전부 정확히 **0.100초**이고, 전부 같은 형태다 — **이전 패턴의 마지막 노드 × 다음 패턴의 첫 노드**.

```
e45 Pattern(0,4,8)[2] × e46 Pattern(8,5,2,1)[0]   point 8
e48 Pattern(2,4,6)[2] × e49 Pattern(6,3,0,1)[0]   point 6
e110 Pattern(4)[0]    × e111 Pattern(4)[0]        point 4
```

값의 근거: `exposureDuration`(0.5) − 엔트리 간 최소 입력 간격(0.4) = 0.1. CLAUDE.md가 말하는 0.4초 최소 간격이 그대로 겹침의 상한이 된다.

**참고로 이 전환은 겹침을 줄인다.** 현행 구운 `spawnTimes`(낙하 배수 2.0~7.5배 포함) 기준으로는 51쌍 / 31쌍, 최대 **0.693초**였다.

### 5-3. 겹치는 0.1초에 화면에 보이는 것

두 링이 동심원으로 겹치되 크기가 다르다.

```
안쪽 링 (A 마지막 노드)  60 + 60×(0.1/0.5) = 72  ← 곧 닫힘
바깥 링 (B 첫 노드)      120                     ← 방금 뜸
```

`fallingNodeColorPalette`가 스폰마다 색을 돌리므로 색도 다르다. 크기·색 모두 다르므로 구분은 된다.

**다만 `IndexLabel`(1~9 숫자)은 같은 자리에 같은 숫자가 두 번 그려져** 글자가 두꺼워 보이는 아티팩트가 난다. 링 자체보다 이쪽이 실제 문제다.

---

## 6. 노브 표시/숨김 (신규 요구사항)

"포커스될 노브만 보이고 나머지는 감춘다"를 붙일 때 걸리는 지점.

### 6-1. 기존 유사 메커니즘 — 그대로 쓸 수 없다

`ApplyHitAreas`(PatternHandler.cs:491)가 이미 "이 패턴이 쓰는 Point" 집합을 계산한다. 하지만 이것은 `RefreshJudgeTargetVisuals()`가 **`JudgeTarget`(선두 패턴) 하나만** 넘겨주는 구조다.

**링은 큐에 들어간 다음 패턴에도 스폰된다.** 판정 대상이 되기 전에 이미 링이 뜬다. 따라서 `hitAreaUsage`를 그대로 재사용하면:

```
t = A.last - 0.1  : B가 큐 투입, B 첫 링 스폰. 그러나 JudgeTarget은 아직 A → B의 노브는 감춰진 상태
t = A.last + 0.1  : A 완료(Deadline), B 승계 → 이제서야 B 노브 페이드인
```

약 **0.2초 동안 감춰진 노브 위에서 링이 줄어든다.** 링이 "아무것도 없는 자리"로 수축하는 셈이라 타이밍 단서가 깨진다.

### 6-2. 따라서 노브 표시는 별도 규칙

표시 대상 = **살아 있는 모든 패턴(`activePatterns`)이 쓰는 Point의 합집합**. 판정 대상뿐 아니라 큐에 든 패턴까지 포함한다.

- 패턴이 큐에 투입되는 순간(`SetPattern`)이 곧 그 패턴의 첫 링이 스폰되는 순간이므로, 노브 등장과 링 등장이 같은 시각에 맞는다.
- 5-2의 겹침 쌍(A 마지막 = B 첫 노드가 같은 Point)에서 합집합이라 **인수인계 중 깜빡임이 없다**.
- 판정 영역 축소(`inactiveHitAreaRatio`)는 지금처럼 `JudgeTarget` 기준을 유지한다 — 목적이 다르다(입력 오인 방지 vs 시선 유도).

### 6-3. 페이드를 `Point.image`의 알파로 하면 안 되는 이유

`Point.image`(= `Visual`의 Image)는 **판정 색 표시에 이미 쓰이고 있다** — `SetJudgementColor`가 Perfect/Good/Miss 색을 통째로 대입하고 `ResetColor`가 `defaultColor`로 되돌린다. 두 색 대입 모두 알파를 포함하므로, 알파로 페이드하면 판정 색 대입이 페이드를 덮어쓴다.

→ `Visual`에 **`CanvasGroup`**을 얹고 그 알파를 페이드한다. 색 로직과 완전히 분리된다.

`CanvasGroup`은 `Visual`과 그 자식에만 적용되고 **부모인 Point 본체(판정용 `hitGraphic`)에는 영향이 없다.** 즉 노브를 감춰도 레이캐스트 판정 영역은 살아 있다 — 이는 의도된 동작이다(영역 축소는 `inactiveHitAreaRatio`가 따로 담당).

---

## 7. 관련 문서

- `docs/FallingNode/` — 기존 낙하 노드 설계 (이 작업으로 대체됨)
- `docs/PatternOverlap/` — 패턴 겹침 규칙 (스폰 리드타임 전제가 바뀜)
- `docs/!Guides/Guide_PatternChartTool.md` — 재굽기 절차
