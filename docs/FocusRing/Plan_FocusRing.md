# Plan: 포커스 링 (낙하 노드 → 축소 링 교체) + 노브 표시 제어

근거: `docs/FocusRing/Research_FocusRing.md`

---

## 결정 사항 (피드백 대상)

구현 전에 확정이 필요한 항목. `>>>`로 의견 남겨주세요.

### D1. "포인트의 2배"의 기준 크기 — **추천: Visual(노브) 60 기준**

| 안 | 시작 → 끝 | 근거 |
|---|---|---|
| **A. Visual 60 기준 (추천)** | 120 → 60 | 플레이어 눈에 실제로 보이는 건 60x60 노브(Unity 내장 `Knob` 스프라이트)다. 링이 노브 외곽과 딱 겹치는 순간이 입력 타이밍이라 "완벽히 같아졌다"가 눈으로 읽힌다. |
| B. 본체 100 기준 | 200 → 100 | 판정 영역과 일치하지만 그 경계는 화면에 안 보인다(알파 0). 링이 노브보다 크게 끝나 "같아졌다"는 느낌이 약하다. |

간격이 700이라 A/B 어느 쪽이든 인접 Point와 겹치지 않는다.

### D2. 인덱스 숫자(1~9) 라벨 — **추천: 비활성** (겹침 분석 후 뒤집음)

Research §5-3 — 같은 Point에 링이 둘 겹치는 0.1초 동안 **같은 숫자가 같은 자리에 두 번 그려져** 글자가 두꺼워 보이는 아티팩트가 난다. 링 자체는 크기(72 vs 120)·색이 달라 구분되지만 라벨은 완전히 겹친다.

프리팹에서 오브젝트만 비활성화한다(삭제하지 않음 — 되돌리기 쉽게).

### D3. 동일 Point 링 겹침 대응 — **추천: 그대로 둔다**

Research §5-2 — 실측 7쌍 / 28쌍, 전부 **0.100초**. 크기·색으로 구분되고, D2로 라벨 아티팩트를 제거하면 남는 문제가 없다.

대안(채택 안 함): `exposureDuration` 기본값을 0.4 이하로 낮추면 겹침이 0이 되지만 노출 시간이 줄어 난도가 올라간다.

### D4. 노브 표시 규칙 — **추천: 살아 있는 모든 패턴의 합집합**

Research §6-2. 판정 대상(`JudgeTarget`)만 기준으로 하면 큐에 든 다음 패턴의 링이 **감춰진 노브 위에서 약 0.2초간 수축**한다. `activePatterns` 전체의 합집합으로 잡아야 링 등장과 노브 등장이 같은 시각에 맞는다.

### D5. 클래스/키 개명 — **추천: 함께 진행 (Step 7)**

아무것도 낙하하지 않는데 `FallingNodeView` · `PoolKey.FallingNode` · `OnFallingNode*`가 남으면 이름이 거짓말이 된다. 배선까지 번지므로 독립 단계로 분리해, 원하지 않으면 Step 7만 빼면 된다.

### D6. 기존 SongChart 재굽기 — **추천: 이번 작업에 포함하지 않음**

판정 타이밍은 안 틀어지므로 급하지 않다(Research §2). 노출 시간만 의도보다 길다. Step 8에서 절차만 안내하고, 실제 재굽기는 사용자가 원할 때 진행.

---

## 구현 단계

### - [x] Step 1. `Focus.png` 임포트 설정 수정 (선행 조건)

`Assets/06. Sprites/Focus.png`의 `spriteImportMode`를 `Multiple` → `Single`로 변경하고 재임포트한다.

- 현재 `Multiple` + 슬라이스 0개라 Sprite 서브에셋이 아예 생성되지 않는다 (`LoadAsset<Sprite>` = NULL).
- 완료 판정: `AssetDatabase.LoadAssetAtPath<Sprite>("Assets/06. Sprites/Focus.png")`가 null이 아니다.

---

### - [x] Step 2. `FocusRingView` 스크립트 작성

`Assets/02. Scripts/UI/FocusRingView.cs` 신규. `FallingNodeView`를 대체하되 **`IPoolable` 계약과 `OnArrived` 이벤트 형태는 그대로 유지**해 PatternHandler 쪽 3분기 회수 구조를 건드리지 않는다.

```csharp
public void Initialize(int displayIndex, Color color, NodeType type,
                       Vector2 targetLocalPos, float startScale, float duration)
```

- `anchoredPosition`은 `targetLocalPos`로 **고정**(이동 없음).
- `rectTransform.sizeDelta`를 `endSize * startScale` → `endSize`로 `duration` 동안 선형 보간.
  - `endSize`는 인스펙터 직렬화 필드(D1 결정값, 기본 60x60).
  - 선형(Lerp)으로 간다 — 어프로치 서클은 등속이어야 남은 시간이 크기로 정직하게 읽힌다. 이징을 넣으면 타이밍 판단이 왜곡된다.
- 수축 완료 시 `OnArrived` 발행. 이후 처리는 PatternHandler가 기존과 동일하게 한다.
- `OnDespawn()`에서 코루틴 정지 + `OnArrived = null` + 비활성 (기존과 동일).

---

### - [x] Step 3. `FocusRing` 프리팹 제작

`Assets/03. Prefabs/FocusRing.prefab` 신규 (`FallingNode.prefab` 복제 후 수정).

```
FocusRing   RectTransform + CanvasRenderer + Image(Focus 스프라이트) + FocusRingView
└ IndexLabel  TMP_Text   (D2에 따라 비활성)
```

- `Image.raycastTarget = false` — 링이 Point의 레이캐스트를 가로채면 마우스 입력이 죽는다. **반드시 확인할 항목.**
- `FocusRingView.background` 참조 배선.

---

### - [x] Step 4. `Pool` 등록

- `PoolKey`에 `FocusRing` 추가 (Step 7에서 `FallingNode`를 제거할 때까지 공존).
- 씬 `Pool` 오브젝트의 `prefabs` / `initialSizes`에 `FocusRing → FocusRing.prefab` 매핑 추가.
- 풀 크기는 기존 `FallingNode` 값을 그대로 쓴다 — 동시 생존 개수가 바뀌지 않는다(오히려 리드타임이 줄어 감소).

---

### - [x] Step 5. `PatternHandler` 배선 교체

| 대상 | 처리 |
|---|---|
| `SpawnFallingNode` | `Pool.Get<FocusRingView>(PoolKey.FocusRing, ...)`로 교체. `WorldToLocal(fallingNodeParent, worldPosition)`은 그대로 사용(링을 그 Point 자리에 놓아야 함). |
| `ComputeFallDuration` | **시그니처 유지**(굽기 툴 `PatternChartWindow.cs:215`가 호출). 본문을 `return exposureDuration;`으로 단순화. `pointIndex`는 쓰이지 않게 되지만 인자는 남긴다. |
| `fallSpawnPositionY` | 제거 (인스펙터 필드) |
| `screenTopY`, `ComputeScreenTopY()` | 제거 |
| `OnDrawGizmos()` | 제거 (`fallSpawnPositionY` 선 표시 전용) |
| `EnsureLayoutInitialized()` | `screenTopY` 대입만 제거. **`canvas` / `canvasCamera` 초기화는 유지** — 마우스 드래그 라인이 쓴다. |
| 3분기 회수(`ReleaseNodeOf` / `HandleFallingNodeArrived` / `ClearNodesOf`) | **변경 없음** |
| `OnFallingNode*` 이벤트 발행 지점 | **변경 없음** (Step 7에서 개명) |

**검증**: 인스펙터 디버그(`debugTestPattern` + `debugInputTimes`)로 링이 각 Point 자리에 뜨고 정확히 `exposureDuration` 뒤에 노브 크기와 일치하는지, 그 순간 입력이 Perfect로 잡히는지 확인.

---

### - [x] Step 6. 노브 표시/숨김 + 빠른 페이드

#### 6-1. 씬 작업

`Point_1~9`의 자식 `Visual` 9개에 **`CanvasGroup`** 컴포넌트를 추가한다. 에디터상 알파는 1로 둔다(런타임에서 PatternHandler가 초기 상태를 잡는다).

`Point.image`(= `Visual`의 Image)의 알파를 쓰지 않는 이유는 Research §6-3 — `SetJudgementColor` / `ResetColor`가 알파 포함 색을 통째로 대입하므로 페이드를 덮어쓴다.

#### 6-2. `Point.cs`

```csharp
[SerializeField] private CanvasGroup visualGroup;   // Visual의 CanvasGroup

/// <summary>노브(Visual)를 페이드로 표시/숨김한다. 판정 색(image.color)과는 독립이다.</summary>
public void SetKnobVisible(bool visible, float duration);
```

- 알파를 `visible ? 1 : 0`으로 `duration` 동안 보간하는 코루틴. 재호출 시 기존 코루틴 정지 후 **현재 알파에서** 이어간다(값 점프 없음).
- `duration <= 0`이면 즉시 대입.
- `Initialize` / `Reset`에서 `visualGroup ??= image != null ? image.GetComponent<CanvasGroup>() : null` 로 자동 배선(수동 배선 누락 방어).
- `visualGroup`이 null이면 조용히 무시한다 — 노브 페이드는 연출이라 없다고 게임이 멈추면 안 된다.

**`CanvasGroup`은 `Visual`과 그 자식에만 걸리므로 부모 Point 본체의 레이캐스트(`hitGraphic`)에는 영향이 없다.** 노브를 감춰도 판정 영역은 살아 있다(영역 축소는 `inactiveHitAreaRatio`가 따로 담당).

#### 6-3. `PatternHandler.cs`

```csharp
[Header("Knob")]
[SerializeField] private float knobFadeDuration = 0.12f;   // "빠르게"

private readonly bool[] knobUsage = new bool[9];

/// <summary>살아 있는 모든 패턴이 쓰는 Point의 노브만 표시한다(판정 대상뿐 아니라 큐에 든 패턴 포함).</summary>
private void ApplyKnobVisibility()
{
    for (int i = 0; i < knobUsage.Length; i++) knobUsage[i] = false;

    foreach (var active in activePatterns)
        foreach (var data in active.Template.AllData)
            knobUsage[data.index] = true;

    for (int i = 0; i < patternPoints.Length; i++)
        patternPoints[i].SetKnobVisible(knobUsage[i], knobFadeDuration);
}
```

호출 지점 — **`activePatterns`가 바뀌는 모든 곳**:

| 위치 | 비고 |
|---|---|
| `Start()` | 초기 상태(패턴 없음) → 9개 전부 숨김. `duration = 0`으로 즉시 적용해 첫 프레임 깜빡임 방지. |
| `SetPattern()` 끝 | **`becomesJudgeTarget` 분기 밖**에서 무조건 호출. 큐에 얹히기만 한 패턴의 노브도 떠야 한다(D4). |
| `CompletePattern()` | `activePatterns.Remove` 이후. 기존 `RefreshJudgeTargetVisuals()` 호출 옆에 붙인다. |
| `ClearAllPatterns()` | 전부 숨김. |

`RefreshJudgeTargetVisuals()` / `ApplyHitAreas()`는 **건드리지 않는다** — 판정 영역 축소는 계속 `JudgeTarget` 기준이다(목적이 다름).

#### 6-4. 검증

- 패턴 없는 대기 구간: 노브 9개 전부 안 보임.
- 패턴 투입 순간: 그 패턴이 쓰는 노브만 ~0.12초 페이드인, 동시에 첫 링 등장.
- Research §5-2의 겹침 쌍(A 마지막 = B 첫 노드가 같은 Point): 인수인계 중 **깜빡임이 없어야 한다**(합집합이라 계속 true).
- 패턴 완료: 다음 패턴이 안 쓰는 노브만 페이드아웃.
- 판정 색(Perfect/Good/Miss)이 페이드에 먹히지 않는지 확인.

---

### - [x] Step 7. 개명 및 낙하 잔재 제거 (D5 채택 시)

| 현재 | 변경 후 |
|---|---|
| `FallingNodeView.cs` | 삭제 (Step 2에서 `FocusRingView`가 대체) |
| `FallingNode.prefab` | 삭제 |
| `PoolKey.FallingNode` | 제거 |
| `PatternHandler.fallingNodeParent` | `focusRingParent` |
| 씬 `FallingNodeParent` 오브젝트 | `FocusRingParent`로 리네임 |
| `activeFallingNodes` / `SpawnFallingNode` / `ReleaseFallingNode` / `HandleFallingNodeArrived` | `activeFocusRings` / `SpawnFocusRing` / `ReleaseFocusRing` / `HandleFocusRingArrived` |
| `ScheduledSpawn.fallDuration` | `shrinkDuration` |
| `fallingNodeColorPalette` | `focusRingColorPalette` |
| `OnFallingNodeSpawned` / `OnFallingNodeResolved` / `OnFallingNodeMissedArrival` | `OnFocusRingSpawned` / `OnFocusRingResolved` / `OnFocusRingMissedArrival` |
| `EffectManager.cs:54,64` 구독 | 새 이벤트명으로 갱신 |

이벤트 개명은 `EffectManager` 한 곳만 고치면 된다(다른 구독자 없음). 씬의 인스펙터 배선(`fallingNodeParent`, 색 팔레트)이 필드명 변경으로 끊기므로 **씬에서 재배선 후 저장**이 필요하다.

---

### - [x] Step 8. 문서 갱신

**`CLAUDE.md`**

- **§4 낙하 노드 시스템 → 포커스 링 시스템**으로 전면 교체.
  - "생성 Y / 화면 경계 / 행별 낙하 속도" 서술 전체 삭제 — 링은 이동하지 않으므로 행 간 차이가 없다.
  - `exposureDuration`이 이제 **정확히 링이 보이는 시간**임을 명시(예전엔 화면 안쪽 구간의 노출 시간이었다).
  - 동일 Point 링 겹침(최대 0.1초, 이전 패턴 마지막 × 다음 패턴 첫 노드)이 정상 동작임을 기록.
- **§1 패턴인풋**에 노브 표시 규칙 추가 — 표시 기준은 **살아 있는 패턴 전체의 합집합**이고, 판정 영역 축소(`inactiveHitAreaRatio`)는 **`JudgeTarget` 기준**이라 둘의 기준이 다르다는 점을 명시(헷갈리기 쉬운 지점).
- **§패턴 겹침 규칙** — "스폰 리드타임 ≈1.0초"를 "리드타임 = `exposureDuration`(기본 0.5초)"로 수정. 0.5 > 0.4(최소 입력 간격)이므로 겹침은 계속 발생하며 큐 구조는 그대로 유효하다는 점을 함께 적는다.
- **§이벤트 확장 포인트** — Step 7 개명 반영.
- **폴더 구조** — `FallingNodeView.cs` → `FocusRingView.cs`.

**`docs/FallingNode/`** — 상단에 "이 설계는 `docs/FocusRing/`으로 대체됨" 한 줄 추가(삭제하지 않고 이력으로 남긴다).

**재굽기 안내(D6)** — 기존 SongChart는 판정 타이밍이 틀어지지 않지만 노출 시간이 의도보다 길다. 맞추려면 `Tools/Pattern Chart Tool`에서 각 채보를 열어 재저장(`spawnTimes` 재계산)한다.

---

## 범위 밖 (이번에 하지 않음)

- **늦은 Good 구간의 링 잔존**: 현재 링은 수축 완료 즉시 회수되지만 `goodWindow`(0.1초) 동안 늦은 입력이 유효하다. 이 불일치는 낙하 노드 시절부터 있던 것으로 그대로 둔다.
- **기존 SongChart 재굽기 실행** (D6).
- **판정 윈도우 · 난이도 조정**.
- **링 색/이징/추가 연출** — 필요하면 `EffectManager` 카탈로그로 별도 처리.
- **노브 감춤과 판정 영역 연동**: 노브를 감춰도 레이캐스트 영역은 살아 있다. 영역 제어는 `inactiveHitAreaRatio`가 이미 담당하므로 이중으로 건드리지 않는다.
