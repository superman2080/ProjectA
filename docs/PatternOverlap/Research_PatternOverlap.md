# Research: 패턴 진행 중 다음 패턴으로 넘어가는 문제

## 증상
노드가 낙하하는 도중 패턴이 바뀐다. 현재 패턴을 다 입력하기도 전에 다음 패턴으로 넘어간다.

---

## 1. 근본 원인 — 구조적 충돌 (데이터로 확정)

`PatternHandler`는 **활성 패턴을 단 하나(`nowPattern`)만** 보유한다. 그런데 채보는 **다음 패턴의 노드를 이전 패턴이 끝나기 전에 스폰해야만** 하는 구조다. 노드는 판정 시각보다 먼저 떨어지기 시작해야 하기 때문이다.

### `ChartPlayer.Update()` — 다음 엔트리 투입 시점

```csharp
var next = pendingEntries[0];
if (audioSource.time < next.spawnTimes[0]) return;   // ★ 스폰 시각이 되면 즉시
patternHandler.SetPattern(next.template, relativeInputTimes, relativeSpawnTimes);
```

**`SetPattern()`은 스폰 시각(= 첫 노드가 떨어지기 시작하는 시각)에 호출된다.** 판정 시각이 아니다.

### `PatternHandler.SetPattern()` — 무조건 현재 패턴을 파기

```csharp
if (nowPattern != null)
    nowPattern.OnExit -= HandlePatternComplete;

ClearFallingNodes();   // ★ 아직 떨어지는 중인 이전 패턴 노드를 전부 회수
ResetPointColors();
...
nowPattern = pattern;  // ★ 진행 중이던 패턴을 덮어씀 (완료 이벤트 없이 소멸)
```

→ **이전 패턴은 완료 처리(`HandlePatternComplete`) 없이 사라지고, 낙하 중이던 노드도 즉시 회수된다.** 이것이 증상 그대로다.

### 실제 채보(`Dreamer_Lv10`, 112 엔트리) 측정값

| 항목 | 값 |
| --- | --- |
| 노드 스폰 리드타임 (온셋 − 스폰) | **0.96 ~ 1.08초** |
| 엔트리 간 입력 간격 (다음 첫 입력 − 현재 마지막 입력) | **0.40 ~ 2.00초** |
| **다음 스폰이 현재 패턴의 마지막 입력보다 먼저 오는 쌍** | **105 / 111 (95%)** |
| 입력(온셋) 시각이 겹치는 엔트리 쌍 | **0 / 111** |

**스폰 리드타임(≈1.0초)이 엔트리 간 입력 간격(최소 0.4초)보다 크므로, 겹침은 예외가 아니라 정상 동작이다.** 채보 대부분(95%)에서 발생한다.

**결정적으로 중요한 사실: 입력(판정) 시각은 전혀 겹치지 않는다 (0/111).**
엔트리는 온셋 시퀀스를 순차적으로 청킹한 것이므로 판정 구간은 항상 순차적이다.
→ **동시에 "입력을 받을 패턴"은 언제나 하나뿐이다. 겹치는 것은 오직 노드의 시각적 낙하 구간이다.**
→ 따라서 **"스폰/연출 파이프라인"과 "판정 파이프라인"을 분리하면 해결된다.** 동시 판정 대상을 다루는 복잡한 설계는 필요 없다.

---

## 2. 함께 드러난 2차 문제

### (a) `Pattern` ScriptableObject의 공유 런타임 상태

```csharp
public class Pattern : ScriptableObject
{
    private PatternData nowPattern;   // 진행 상태
    private int index;                // 진행 상태
    private float[] inputTimes;       // 주입된 타이밍
}
```

**에셋 하나가 진행 상태를 들고 있다.** `SongChartEntry.template`은 원본 에셋을 복제 없이 참조한다(주석에 명시).
현재는 패턴이 한 번에 하나만 살아 있어 문제가 드러나지 않았지만, **두 패턴을 동시에 살려두는 순간 같은 템플릿을 쓰는 두 엔트리가 서로의 `index`/`inputTimes`를 덮어쓴다.**

실측: `Dreamer_Lv10`에서 **인접 엔트리가 같은 템플릿인 쌍 6건, 그중 시간까지 겹치는 쌍 1건**(`[110]&[111]`, `Pattern_1Node_(4)`).
→ 실제로 터지는 케이스가 이미 채보에 있다. **동시 활성을 지원하려면 런타임 상태를 에셋 밖으로 빼야 한다.**

### (b) 미입력 패턴이 영원히 종료되지 않음

`Pattern.Next()`는 **올바른 인덱스를 입력해야만** 진행한다. 플레이어가 아무것도 입력하지 않으면 `nowPattern`은 계속 남는다. 낙하 노드는 `OnFallingNodeMissedArrival`만 발행하고 사라질 뿐, **패턴은 완료되지 않는다.**

지금은 다음 `SetPattern()`이 강제로 덮어써서 이 문제가 가려져 있었다. **패턴 교체를 없애면 이 버그가 그대로 드러난다.**
→ **입력 시한이 지난 패턴을 자동 종료(만료)시키는 규칙이 반드시 함께 필요하다.**

관련 코드: `SetPattern()`이 이미 `strokeDeadline = patternStartTime + 마지막입력시각 + goodWindow`를 계산해 두었다 (키보드 스트로크 종료용). 만료 판정에 동일 개념을 쓸 수 있다.

---

## 3. 영향 받는 코드

| 파일 | 관련 지점 |
| --- | --- |
| `Assets/02. Scripts/UI/PatternHandler.cs` | `nowPattern` 단일 슬롯, `SetPattern()`, `ClearFallingNodes()`, `scheduledSpawns`, `activeFallingNodes`(position 키), `HandlePatternComplete()`, `ApplyHitAreas()`, `ShowGuideLine()` |
| `Assets/02. Scripts/Pattern/Pattern.cs` | 에셋에 얹힌 런타임 상태 (`index`, `nowPattern`, `inputTimes`), `OnInput`/`OnExit` 이벤트 |
| `Assets/02. Scripts/ChartGen/ChartPlayer.cs` | 스폰 시각에 `SetPattern()` 호출 |
| `Assets/04. Datas/Song/Dreamer_lv10.asset` | 실측 대상 채보 (112 엔트리) |

`activeFallingNodes`는 `Dictionary<int position, FallingNodeView>`로 **패턴 내 위치만** 키로 쓴다. 두 패턴의 노드가 동시에 살아 있으면 **position이 충돌**하므로 키에 패턴 구분이 필요하다.

---

## 4. 대안 검토

| 방안 | 평가 |
| --- | --- |
| **A. 스폰/연출과 판정을 분리, 패턴을 큐로 관리** | **권장.** 입력 시각이 겹치지 않는다는 사실(1절) 덕분에 판정은 여전히 "한 번에 하나"로 단순하게 유지된다. 다음 패턴은 미리 받아 노드 스폰과 가이드만 시작하고, 판정 대상은 순차 승계한다. |
| B. `ChartPlayer`가 이전 패턴 완료 후에만 다음 엔트리 투입 | **불가.** 스폰이 1초 늦어지므로 노드가 판정 시각까지 낙하할 시간이 없다. 상대 스폰 시각이 음수가 되어 노드가 즉시 튀어나오거나 판정 시각을 넘겨 도착한다. 타이밍이 붕괴된다. |
| C. 채보를 다시 구워 스폰 리드타임을 엔트리 간격(0.4초) 미만으로 축소 | **불가.** 노드가 0.4초 만에 떨어져 가독성·난이도가 붕괴된다. 게다가 간격이 더 좁은 채보가 나오면 또 깨진다. 근본 해결이 아니다. |

→ **A 채택.** B/C는 데이터 측정값(리드타임 1.0초 vs 간격 0.4초)상 성립하지 않는다.
