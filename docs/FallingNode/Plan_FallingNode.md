> ⚠️ **이 설계는 `docs/FocusRing/`으로 대체되었습니다.** 낙하 노드는 제거되고, 입력할 Point 자리에서 줄어드는 포커스 링으로 바뀌었습니다. 아래 내용은 이력으로만 남겨둡니다.

# Plan_FallingNode.md — 패턴 노드 역할(시작/진행/끝) + 낙하 노드 시각화 구현

> 기반 문서: `Research_FallingNode.md`
> 사용자 결정사항:
>
> 1. 낙하는 실제 오브젝트(낙하 시각 요소)로 구현한다.
> 2. 한 번에 하나의 `Pattern`만 진행한다 (`PatternHandler.nowPattern` 단일 구조 유지).
> 3. Progress(진행) 노드는 0개 이상 임의 개수 허용 (`Start - (Progress)* - End`).
> 4. `PatternData.inputTime`은 "도착(판정) 시각"으로 그대로 사용하고, 낙하 소요시간(리드타임)은 별도 설정값(`fallDuration`)으로 분리한다.
> 5. 낙하 경로는 해당 Point 바로 위(수직)에서 출발해 Point 위치까지 수직으로 하강한다.
> 6. (v2 피드백) 가시성을 위해 낙하 노드는 타입(Start/Progress/End) 대신 **낙하 순서마다 다른 색**을 사용한다.
> 7. (v2 피드백) 낙하 노드 내부에 **목표 Point 인덱스를 텍스트로 표시**한다.
> 8. (v2 피드백) 향후 노드 낙하 타이밍에 맞춰 좌측 캐릭터가 액션(베기 등)을 취하는 기능이 추가될 예정 — 이번 구현은 그 확장을 염두에 두고 **이벤트 훅**을 미리 노출한다.
> 9. (v3 피드백) 낙하 노드는 매번 `Instantiate`/`Destroy`하지 않고 **오브젝트 풀**을 사용해 재사용한다.
> 10. (v3 피드백) `Pattern` SO 내에 동일한 Point 인덱스가 중복 등장하면 잘못된 데이터로 간주해 **경고/에러**를 표시한다.
> 11. (v4 피드백) 풀은 새로 만들지 말고 **프로젝트에 기존에 있던 공용 `Pool`**(`Assets/02. Scripts/Pool/Pool.cs`, `PoolKey` 기반 Singleton)을 그대로 활용한다.
> 12. (v5 피드백) 상단 행 노드가 하단 행 노드보다 화면에 노출되는 시간이 짧아 UX상 잘 안 보임 — **생성 위치(target + `fallSpawnOffsetY`)는 행마다 동일하게 유지**하되, **낙하 속도를 행마다 다르게 해서 노출 시간을 동일하게** 맞춘다.
> 13. (v6 피드백) 생성 위치를 타겟 기준 상대 오프셋(`fallSpawnOffsetY`)이 아니라, **`fallingNodeParent` 로컬 좌표계 기준 절대 Y 위치(`fallSpawnPositionY`)**로 지정한다 — 모든 노드가 행과 무관하게 동일한 절대 높이에서 낙하를 시작한다.
> 14. (v7, 이후 되돌림) v5의 노출 시간 균일화 공식은 `fallingNodeParent.rect.yMax`를 "이 위쪽은 안 보인다"는 경계로 가정했지만 실제로는 아무 것도 가려지지 않아(Mask 부재) 행마다 노출 시간이 오히려 더 크게 벌어졌다. 1차 수정으로 `FallingNodeParent`에 `RectMask2D`를 추가했으나, **사용자가 이 방향을 명시적으로 거부**(패널 안에 숨기는 게 아니라 화면 바깥 위쪽에서 진짜로 떨어져야 함) — Mask는 제거하고 되돌림.
> 15. (v8, 반려됨) "노출 시간 균일화를 폐기하고 등속으로 통일" 방향을 제안했으나 **사용자가 반려** — 등속이 아니라 노출 시간을 계속 동일하게 유지하길 원함.
> 16. (v9 피드백, 확정) 노출 시간은 계속 동일하게 맞추되(v5와 동일한 목표), 그 기준이 되는 "안 보이는 경계"를 **Mask나 패널 자체의 작은 rect가 아니라 Canvas의 실제 화면 바깥 경계**로 삼는다. 노드는 그 경계보다 위, 즉 캔버스 화면 바깥에서 생성되어 내려온다(Mask 없이도 Canvas 밖은 실제로 렌더링되지 않으므로 자연히 안 보임). 목표 지점이 화면 상단에 가까울수록(=보이는 구간이 짧을수록) 같은 노출 시간을 유지하기 위해 **속도가 더 느려진다** — 반대로 하단 목표는 보이는 구간이 길어 더 빠르게 떨어진다.
> 17. (v10 피드백) 생성 위치(낙하 시작 Y)는 자동 계산(`screenTopY + spawnMargin`)이 아니라 **사용자가 직접 값으로 설정**할 수 있어야 한다. 대신 그 값이 실제로 화면 밖(안 보이는 지점)에 있는지 에디터에서 바로 확인할 수 있도록 **Gizmo로 낙하 시작 위치를 표시**한다.
> 18. (v11 피드백) 노출 시간(`visibleExposureDuration`)은 `PatternHandler`(씬 전역 단일 값)가 아니라 **각 `Pattern` SO마다 개별로 설정**할 수 있어야 한다 — 패턴별로 난이도/템포에 맞게 다른 노출 시간을 줄 수 있도록.
> 19. (v12 피드백, 확정) 노출 시간은 `Pattern` 전체가 아니라 **`PatternData`(노드 하나하나) 단위로 설정**한다 — 같은 패턴 안에서도 노드마다 다른 노출 시간을 줄 수 있도록. 기본값은 `0.5`.
> 20. (v13 피드백) 드래그로 실제 사용 중인 패턴 포인트(`Point`)는 판정 결과에 따라 색이 바뀐다 — **Perfect = 파란색, Good = 초록색, Miss(실패) = 빨간색**.

## 설계 요약

- **역할(Start/Progress/End) 분류**: `PatternData`에 별도 필드를 추가하지 않고, `Pattern`이 배열 내 위치(position)로부터 계산해서 노출한다. `position == 0` → `Start`, `position == length-1` → `End`, 그 사이 → `Progress`. 게임 로직상 즉시 쓰이진 않지만(완료 판정은 기존 `Pattern.OnExit`로 이미 처리됨), **캐릭터 액션 확장 시 "이 노드가 시작/진행/끝 중 무엇인가"에 따라 다른 액션을 고르는 데 필요**하므로 이벤트 payload에 포함해 그대로 노출한다.
- **낙하 시각 요소**: 새 컴포넌트 `FallingNodeView`(UI, `RectTransform` 기반)를 신설. `Pattern`의 각 `PatternData` 위치마다 `spawnTime = patternStartTime + inputTime - fallDuration` 시점에 스폰되어, `fallDuration` 동안 해당 Point 바로 위(`spawnOffsetY`만큼 위)에서 Point 위치까지 수직 하강한다.
- **색상 = 낙하 순서 기반**: 타입별 고정 3색 대신, `PatternHandler`가 보유한 `Color[] fallingNodeColorPalette`를 스폰 순서(누적 카운터) 기준 라운드로빈으로 순환 배정한다. 같은 타입(Progress)이 여러 번 반복돼도 동시에 화면에 뜬 노드끼리 색이 겹치지 않도록 스폰 시점마다 팔레트 인덱스를 증가시킨다.
- **인덱스 텍스트 표시**: `FallingNodeView`에 `TMP_Text`(또는 `Text`) 참조를 추가해 목표 Point 인덱스(사람이 읽기 쉬운 1~9 표기, 즉 `pointIndex + 1`)를 표시한다.
- **판정 로직은 변경하지 않음**: 기존 `PatternHandler.AddPattern`의 Perfect/Good/Miss 판정(`patternStartTime + ExpectedTime` 기준 `Time.time` 오차 계산)은 그대로 재사용한다. 낙하 노드는 그 판정 시점을 시각적으로 보여주는 역할만 하며, 게임 로직(정오답/타이밍 판정)에는 관여하지 않는다.
- **소비(despawn) 시점**: 플레이어가 올바른 인덱스를 눌러 `AddPattern`이 현재 포지션을 판정하면(Perfect/Good/Miss 무관), 해당 포지션에 대응하는 낙하 노드를 즉시 `Despawn()`한다. 자연 도착(`fallDuration` 경과)까지 눌리지 않은 노드는 도착 시 스스로 사라진다(별도 실패 처리는 이번 스코프에 포함하지 않음, 아래 "범위 밖" 참고).
- **여러 낙하 노드 동시 존재 허용**: 한 `Pattern` 안에서도 서로 다른 position의 낙하 노드가 시간상 겹쳐서 동시에 화면에 떠 있을 수 있다. "동시 진행 패턴 수"는 1개, "동시 낙하 노드 수"는 여러 개 가능.
- **캐릭터 액션 확장 포인트**: 이번 스코프에서 캐릭터 애니메이션/액션 자체는 구현하지 않는다. 대신 `PatternHandler`가 낙하 노드의 생애주기(스폰 / 히트 판정 / 자연 도착)마다 **월드 좌표 + NodeType을 포함한 이벤트**를 발행하도록 설계해, 이후 별도 캐릭터 컨트롤러가 이 이벤트만 구독하면 타이밍에 맞춰 액션을 재생할 수 있게 한다 (`Plan_PatternLine.md`에서 확립한 "이벤트 기반 확장" 원칙과 동일).
- **오브젝트 풀링 (v4: 기존 공용 풀 재사용)**: 별도 풀을 새로 만들지 않고, 프로젝트에 이미 있던 `Pool`(`Assets/02. Scripts/Pool/Pool.cs`, `Singleton<Pool>`, `PoolKey` enum 기반)을 그대로 사용한다.
  - `PoolKey`에 `FallingNode` 항목을 추가하고, 씬의 `Pool` 싱글턴 인스펙터에 `FallingNode.prefab`과 초기 개수를 등록한다.
  - `FallingNodeView`는 `Poolable`(`IPoolable` 구현체)을 상속해 `OnSpawn()`/`OnDespawn()`을 오버라이드한다 — `Initialize()`는 비활성 상태에서도 데이터만 채우고, 실제 낙하 코루틴은 `OnSpawn()`(풀에서 대여되어 활성화되는 시점)에서 시작한다. 이렇게 분리한 이유는 `Pool.Get<T>(key, initializer)`가 `initializer` 호출 → `obj.OnSpawn()` 순서로 동작하는데, 오브젝트가 아직 비활성 상태인 시점에 코루틴을 시작하면 실행되지 않기 때문.
  - `PatternHandler`는 `Pool.Instance.Get<FallingNodeView>(PoolKey.FallingNode, n => { ... })`로 대여(대여 콜백 안에서 `fallingNodeParent`로 재부모 지정 + `Initialize` 호출), `Pool.Instance.Return(PoolKey.FallingNode, node)`로 반환한다. 반환 시 `Poolable.OnDespawn()`이 코루틴 중지 + `SetActive(false)`까지 처리하므로 `PatternHandler`가 직접 `SetActive`/코루틴을 다루지 않는다.
  - `PatternHandler`에는 더 이상 `fallingNodePrefab`/`poolDefaultCapacity`/`poolMaxSize` 필드가 없다 (프리팹 등록과 초기 크기는 전부 `Pool` 싱글턴 쪽 책임).
- **~~행별 낙하 속도 보정 (v5)~~ → v9에서 경계 기준만 교체, 개념은 그대로 유지**: (기록) 상단 행일수록 생성 지점이 "보이는 경계" 위쪽 바깥에서 시작하는 비중이 크다고 보고, 그 경계 안쪽 구간만 `visibleExposureDuration` 동안 이동하도록 Point별 낙하 시간을 역산(`fallDuration = totalDistance * visibleExposureDuration / visibleDistance`)했다. **공식/목표(노출 시간 균일화) 자체는 사용자가 재확인한 올바른 방향**이었고, 문제는 그 "경계"로 패널 자신의 작은 `rect.yMax`를 썼다는 점뿐이었다 (아래 v9에서 교정).
- **~~생성 위치 = 절대 좌표 (v6)~~ → 그대로 유지**: `fallSpawnOffsetY`(타겟 기준 상대 오프셋)를 절대 Y로 바꾼 결정은 그대로 유지한다. 다만 그 절대 Y 값을 사람이 임의로 정하지 않고, v9에서 Canvas 실제 경계 기준으로 자동 계산하도록 바꾼다.
- **~~실제 Mask로 경계 실체화 (v7)~~ → 사용자가 거부, 되돌림**: `FallingNodeParent`에 `RectMask2D`를 추가해 `rect.yMax` 경계를 실제로 가리려 했으나, 이는 "작은 패널 안에 숨겼다 나타나는" 방식이라 사용자가 원하는 "화면 바깥 위쪽에서 떨어지는" 느낌과 다르다. **Mask는 제거하고 되돌림.**
- **~~등속으로 통일 (v8)~~ → 사용자가 반려**: 노출 시간 균일화를 폐기하고 등속으로 통일하자고 제안했으나, 사용자는 "노출 시간은 계속 동일해야 한다"고 명확히 함. **폐기.**
- **경계를 Canvas 실제 화면 밖으로 교정 (v9, 확정)**: v5의 "노출 시간 균일화" 공식·목표는 그대로 두고, 그 공식이 쓰는 "보이는 경계"만 `fallingNodeParent.rect.yMax`(작은 패널 rect, 실제로 아무것도 안 가림)에서 **루트 `Canvas`의 실제 화면 상단 경계**로 교체한다. Canvas는 Screen Space Overlay라 자기 rect 자체가 실제 화면 해상도와 정확히 일치하므로, 그 경계 밖은 Mask 없이도 하드웨어 뷰포트 클리핑으로 실제 렌더링되지 않는다(Research 문서에서 검증 완료).
  - 내부 상태 `float screenTopY`(로컬 좌표, "화면의 진짜 위쪽 경계") 와 `float spawnPositionY`(= `screenTopY + spawnMargin`, 실제 생성 지점)를 `Start()`에서 1회 계산해 캐싱한다.
    - 계산: 루트 `Canvas`의 `RectTransform`에서 `canvasRect.TransformPoint(new Vector3(0f, canvasRect.rect.yMax, 0f))`로 화면 진짜 상단의 월드 좌표를 구하고, 기존 `WorldToLocal(fallingNodeParent, ...)` 헬퍼로 `fallingNodeParent` 로컬 좌표로 변환한 값이 `screenTopY`.
  - `fallSpawnPositionY`(사람이 임의로 정하던 절대 로컬 Y) 필드는 제거하고, 대신 **화면 경계 위 여유값**을 뜻하는 `spawnMargin` 필드(예: 100)로 대체 — 실제 스폰 위치는 `spawnPositionY = screenTopY + spawnMargin`으로 자동 계산되며, 모든 행에서 동일하다(v6 결정 유지).
  - `ComputeFallDuration(pointIndex)`는 v5와 동일한 구조를 유지하되 경계값만 교체한다:
    ```
    float totalDistance = Mathf.Max(spawnPositionY - targetLocalPos.y, 0f);
    float visibleDistance = Mathf.Clamp(screenTopY - targetLocalPos.y, 0f, totalDistance);
    return visibleDistance <= 0f ? visibleExposureDuration : totalDistance * visibleExposureDuration / visibleDistance;
    ```
  - 결과: 목표 Point가 화면 상단에 가까울수록(=`screenTopY`까지의 보이는 구간이 짧을수록) 같은 노출 시간을 유지하기 위해 **속도가 더 느려지고**, 하단 목표는 보이는 구간이 길어 더 빠르게 떨어진다 — 사용자가 확인한 논리와 일치.
- **생성 위치는 사용자가 직접 설정 + Gizmo로 확인 (v10)**: `spawnPositionY`를 `screenTopY + spawnMargin`으로 자동 계산하던 것을 되돌리고, v6처럼 **`[SerializeField] private float fallSpawnPositionY`를 다시 사람이 직접 입력**하는 값으로 되돌린다. `screenTopY`(Canvas 실제 화면 상단, 자동 계산)는 없애지 않고 `ComputeFallDuration`의 "보이는 경계" 계산에만 계속 사용한다 — 즉 "낙하 시작 위치"(사용자 설정)와 "노출 시간 계산에 쓰는 화면 경계"(자동 계산)는 서로 다른 두 값으로 분리된다. 사용자가 `fallSpawnPositionY`를 화면 밖(안 보이는 지점)에 제대로 두었는지 에디터에서 바로 확인할 수 있도록, `PatternHandler.OnDrawGizmos()`에서 `fallingNodeParent` 로컬 좌표계의 `(rect.xMin, fallSpawnPositionY)` ~ `(rect.xMax, fallSpawnPositionY)`를 잇는 가로선을 Scene 뷰에 그린다.
- **~~노출 시간을 Pattern SO별로 설정 (v11)~~ → v12에서 단위를 더 세분화**: `Pattern` 전체에 노출 시간을 하나 두려 했으나, 사용자가 "`PatternData`(노드 하나하나) 단위로 설정"하도록 정정 — 같은 패턴 안에서도 노드마다 다른 노출 시간을 줄 수 있어야 하므로.
- **노출 시간을 PatternData별로 설정, 기본값 0.5 (v12, 확정)**: `visibleExposureDuration`을 `PatternHandler`의 씬 전역 단일 값이 아니라 **`PatternData`(개별 노드) 자신의 필드**로 옮긴다. `fallSpawnPositionY`/`screenTopY`(낙하 시작 위치, 화면 경계)는 계속 `PatternHandler`(씬 레이아웃에 종속적인 값)에 남긴다 — "노출 시간"만 노드 데이터의 일부로 옮기고, "어디서/어디까지 떨어지는지"는 여전히 씬/패널 설정이라는 구분을 유지한다.
  - `PatternData`에 기존 `index`/`inputTime`과 동일한 스타일로 `[Min(0f)] public float visibleExposureDuration = 0.5f;` 필드 추가 (private+프로퍼티가 아니라 공개 필드 — `PatternData`의 기존 관례를 따름)
  - `ComputeFallDuration(int pointIndex, float exposureDuration)`으로 시그니처 변경 — 호출부(`SetPattern()`)에서 `nowPattern.AllData[i].visibleExposureDuration`을 넘겨준다. 계산 로직 자체(경계/거리 비교)는 동일, 인자로 받은 `exposureDuration`을 사용하도록 내부 참조만 교체
  - `PatternHandler`의 `visibleExposureDuration` 필드는 제거
- **Pattern SO 데이터 검증**: `Pattern.OnValidate()`(에디터 전용 콜백)에서 `patternDatas` 배열 내 `index` 중복 여부를 검사한다. 중복이 있으면 `Debug.LogError`로 즉시 알려 인스펙터에서 값을 수정하는 시점에 바로 인지할 수 있도록 한다 (플레이 중이 아니라 **에셋 편집 시점**에 잡아내는 것이 목표이므로 런타임 `Initialize()`가 아닌 `OnValidate()`에 배치).
- **Point 판정 색상 피드백 (v13)**: 드래그로 실제 눌린 `Point`(3x3 그리드의 노드 자신, 낙하 노드와는 별개의 기존 UI 오브젝트)가 판정 결과에 따라 색이 바뀐다 — `AddPattern`이 이미 계산하는 `JudgementResult`(Perfect/Good/Miss)를 그대로 재사용한다.
  - `Point`에 `Image` 참조(기존 `handler` 자동 해석 관례처럼 `GetComponent<Image>()`로 자동 획득)와 `perfectColor`(파랑)/`goodColor`(초록)/`missColor`(빨강) 직렬화 필드를 추가, `public void SetJudgementColor(JudgementResult result)`로 즉시 색을 바꾼다.
  - 원래 색(현재는 흰색)은 `Start()`에서 1회 캐싱해뒀다가 `public void ResetColor()`로 되돌릴 수 있게 한다.
  - `PatternHandler.AddPattern(int index)`에서: 정답 인덱스로 판정된 직후 `patternPoints[index].SetJudgementColor(result)` 호출. 오답 인덱스(패턴의 기대 인덱스와 다름) 분기에도 "실패"로 간주해 `patternPoints[index].SetJudgementColor(JudgementResult.Miss)` 호출.
  - 색은 계속 유지되지 않고, 드래그가 끝나거나(`EndDrag()`) 새 패턴이 시작될 때(`SetPattern()`) 9개 Point 전부 `ResetColor()`로 원래 색으로 되돌린다 (`EndDrag()`의 기존 `foreach (var p in patternPoints) p.ResetBusy();` 루프에 같이 묶음).

### 범위 밖 (이번 Plan에서 다루지 않음)

- 낙하 노드가 판정 윈도우를 완전히 지나도록 플레이어가 전혀 누르지 않았을 때 자동으로 Miss 처리하는 로직은 없음 (현재 구조상 `AddPattern`은 실제 입력이 들어와야만 호출됨). 필요 시 별도 Plan으로 분리.
- 캐릭터 액션(베기 애니메이션 등) 자체 구현은 포함하지 않음 — 이벤트 훅만 노출.
- 노드 타입별 사운드/파티클 이펙트는 포함하지 않음.

## 단계별 구현 계획

### Step 1 — `Pattern.cs` 확장 (역할 계산 + 외부 노출)

- [x] `PatternSpace` 네임스페이스에 `public enum NodeType { Start, Progress, End }` 추가
- [x] `Pattern`에 `public IReadOnlyList<PatternData> AllData => patternDatas;` 추가 (스포너가 전체 배열을 조회할 수 있도록)
- [x] `Pattern`에 `public int CurrentIndex => index;` 추가 (현재 판정 위치 노출 — 낙하 노드 despawn 매칭용)
- [x] `Pattern`에 `public NodeType GetNodeType(int position)` 추가 — `position == 0` → `Start`, `position == patternDatas.Length - 1` → `End`, 그 외 → `Progress` (배열 길이가 1인 경우 `Start`이자 `End`이므로 `position == 0`을 먼저 체크해 `Start`를 우선 반환)
- [x] `Pattern`에 `private void OnValidate()` 추가 — `patternDatas`를 순회하며 `HashSet<int>`로 `index` 중복 여부 검사, 중복 발견 시 `Debug.LogError($"[Pattern] '{name}'에 중복된 인덱스 {dupIndex}가 있습니다.", this)` 호출 (에디터에서 값 편집/저장 시점마다 자동 실행되어 즉시 인지 가능)

### Step 2 — `FallingNodeView` 컴포넌트 신설 (기존 `Pool`/`Poolable` 재사용 전제)

- [x] 파일: `Assets/02. Scripts/UI/FallingNodeView.cs`
- [x] UI 오브젝트(`Image` + 자식 `TMP_Text`)에 부착, **`MonoBehaviour`가 아니라 기존 `Poolable`(`Assets/02. Scripts/Pool/Poolable.cs`, `IPoolable` 구현체)을 상속** — `[RequireComponent(typeof(RectTransform))]`
- [x] `SerializeField Image background` (색상 적용 대상), `SerializeField TMP_Text indexLabel` (목표 인덱스 텍스트 표시)
- [x] `public int PointIndex { get; private set; }`, `public NodeType Type { get; private set; }` 프로퍼티 추가 (이벤트 구독자가 조회)
- [x] `public void Initialize(int displayIndex, Color color, NodeType type, Vector2 targetLocalPos, float spawnOffsetY, float fallDuration)` — 풀에서 `Get()` 직후(비활성 상태에서도) 호출되는 진입점. **낙하 코루틴은 여기서 시작하지 않고** `PointIndex`/`Type`/색상/텍스트/목표좌표 등 데이터만 채워 대기시킨다:
  - `PointIndex`/`Type` 필드 설정 (이벤트 payload 및 despawn 매칭용)
  - `background.color = color`, `indexLabel.text = displayIndex.ToString()`
  - `pendingTarget`/`pendingSpawnOffsetY`/`pendingFallDuration` 필드에 값 저장 (실제 이동은 `OnSpawn()`에서 시작)
- [x] `public override void OnSpawn()` — `base.OnSpawn()`(`SetActive(true)`) 후 대기 중이던 낙하 코루틴 시작. **이유**: `Pool.Get<T>(key, initializer)`는 `initializer` 호출 → `obj.OnSpawn()` 순서로 실행되는데, 오브젝트가 아직 비활성 상태인 `initializer` 시점에 코루틴을 시작하면 비활성 GameObject에서 코루틴이 동작하지 않아 즉시 죽는다. 그래서 "데이터 채우기"(`Initialize`)와 "낙하 시작"(`OnSpawn`)을 분리했다.
- [x] `public override void OnDespawn()` — `StopFalling()` + `OnArrived = null` 정리 후 `base.OnDespawn()`(`SetActive(false)`) 호출
- [x] `public event Action<FallingNodeView> OnArrived;` (구독자가 `PointIndex`/`Type` 등 자기 정보를 조회할 수 있도록 인자로 자기 자신을 전달), 코루틴이 목표 좌표에 도달하면 발행 (파괴는 하지 않음 — 반환은 구독자인 `PatternHandler`가 `Pool.Instance.Return(...)`으로 처리)
- [x] `public void StopFalling()` — 진행 중인 낙하 코루틴만 중지 (플레이어가 맞춰서 풀로 반환되기 직전, 그리고 `OnDespawn()` 내부에서 호출 — `OnArrived` 미발행)

### Step 3 — `PatternHandler`에 낙하 스포너 로직 추가 (기존 `Pool` 싱글턴 사용)

- [x] `PoolKey` enum(`Assets/02. Scripts/Pool/Pool.cs`)에 `FallingNode` 항목 추가
- [x] 신규 `SerializeField`:
  - `RectTransform fallingNodeParent` (낙하 노드가 배치될 부모 — Point들과 동일 좌표계를 쓰는 별도 레이어)
  - `float fallDuration = 0.6f`
  - `float fallSpawnOffsetY = 300f`
  - `Color[] fallingNodeColorPalette` (라운드로빈용)
  - (프리팹 참조와 풀 사전 확보/상한값은 `PatternHandler`가 아니라 씬의 `Pool` 싱글턴 인스펙터에서 `PoolKey.FallingNode` 키로 관리 — `fallingNodePrefab`/`poolDefaultCapacity`/`poolMaxSize` 필드는 두지 않음)
- [x] 내부 상태 추가:
  - `struct ScheduledSpawn { public int position; public float spawnTime; }` 형태의 대기열 (`List<ScheduledSpawn>`)
  - `Dictionary<int, FallingNodeView> activeFallingNodes` (position → 풀에서 대여한 인스턴스)
  - `int spawnCounter` (팔레트 라운드로빈 인덱스 산정용, 패턴 시작 시 0으로 리셋)
- [x] `SetPattern(Pattern pattern)` 수정:
  - 기존 로직 앞/뒤에 이전 패턴의 잔여 스케줄/활성 노드를 전부 정리하는 방어 로직 추가 — 각 활성 노드에 대해 `ReleaseFallingNode(node)`(아래 공용 헬퍼) 호출 후 컬렉션 클리어
  - `spawnCounter = 0`으로 리셋
  - `pattern.AllData`를 순회하며 각 position에 대해 `spawnTime = patternStartTime + data.inputTime - fallDuration`을 계산해 대기열에 채움
- [x] `Update()`에 스폰 처리 추가: 대기열을 순회하며 `Time.time >= spawnTime`인 항목을 처리 —
  - `Pool.Instance.Get<FallingNodeView>(PoolKey.FallingNode, n => { n.transform.SetParent(fallingNodeParent, false); n.Initialize(...); })`로 대여 (대여 콜백 안에서 재부모 지정 + 데이터 초기화, 이후 풀이 자동으로 `OnSpawn()` 호출해 낙하 시작)
  - 색상 = `fallingNodeColorPalette[spawnCounter % fallingNodeColorPalette.Length]`, 이후 `spawnCounter++`
  - `activeFallingNodes`에 등록, 대기열에서 제거
  - `node.OnArrived += HandleFallingNodeArrived;` 구독 — 핸들러 내부에서 `activeFallingNodes`에서 제거 + **`OnFallingNodeMissedArrival(pointIndex, nodeType, worldPosition)` 발행**(아래 확장 이벤트) + `Pool.Instance.Return(PoolKey.FallingNode, node)`
  - 스폰 직후 **`OnFallingNodeSpawned(pointIndex, nodeType, worldPosition)` 발행** (아래 확장 이벤트)
- [x] `AddPattern(int index)` 수정: 인덱스가 `nowPattern.ExpectedPointIndex`와 일치해 실제 판정이 이뤄지는 분기에서,
  - 판정 직전 `pattern.CurrentIndex`(= 이번에 판정되는 position)에 해당하는 `activeFallingNodes` 항목이 있으면 `ReleaseFallingNode(node)`(아래 공용 헬퍼) 호출 후 딕셔너리에서 제거
  - 판정 결과(`JudgementResult`) 계산 직후 **`OnFallingNodeResolved(index, nodeType, worldPosition, result)` 발행** (아래 확장 이벤트) — 캐릭터 액션이 "몇 번 노드를, 어떤 타입으로, 어떤 판정으로 맞췄는지" 알 수 있도록
- [x] `HandlePatternComplete()`에도 방어적으로 남은 활성/대기 노드 정리 로직 추가 (동일하게 `ReleaseFallingNode` 재사용)
- [x] 공용 헬퍼 `private void ReleaseFallingNode(FallingNodeView node)` 추가 — `node.OnArrived -= HandleFallingNodeArrived;`(자연 도착 경로와 이중 반환 방지) 후 `Pool.Instance.Return(PoolKey.FallingNode, node);` 수행 (내부적으로 `Poolable.OnDespawn()`이 코루틴 중지 + `SetActive(false)`까지 처리). 히트 소비/패턴 교체/패턴 완료 시 남은 노드 정리 등 "플레이어가 도착 전에 노드를 없애야 하는" 모든 경로가 이 헬퍼 하나로 수렴
- [x] (v4 변경) 풀을 `PatternHandler`가 직접 소유하지 않으므로 `OnDestroy()`에서 풀을 `Dispose()`할 필요 없음 — 기존 이벤트 구독 해제 로직만 유지

### Step 4 — 캐릭터 액션 확장용 이벤트 노출

- [x] `PatternHandler`에 다음 이벤트 추가 (전부 `Vector3`로 월드 좌표 포함 — 캐릭터가 베는 방향/타겟 위치를 잡을 수 있도록, 기존 `OnNodeConnected`와 동일한 관례):
  - `public event Action<int pointIndex, NodeType nodeType, Vector3 worldPosition> OnFallingNodeSpawned;` — 노드 스폰 시점 (캐릭터 예비 동작/조준용)
  - `public event Action<int pointIndex, NodeType nodeType, Vector3 worldPosition, JudgementResult result> OnFallingNodeResolved;` — 플레이어가 실제로 맞춘 시점 (캐릭터 베기 액션 트리거 지점)
  - `public event Action<int pointIndex, NodeType nodeType, Vector3 worldPosition> OnFallingNodeMissedArrival;` — 맞추지 못한 채 자연 도착한 시점 (캐릭터 실패 리액션용)
- [x] 이번 스코프에서는 위 이벤트를 구독하는 캐릭터 컨트롤러를 만들지 않음 — 발행부만 구현하고 no-op 상태로 둠 (null 체크만 하는 가벼운 `Action`이므로 구독자 없을 때 성능 영향 없음)

### Step 5 — 좌표 변환 헬퍼

- [x] `PatternHandler`에 Point의 월드 좌표를 `fallingNodeParent` 로컬 좌표로 변환하는 헬퍼 추가 (`WorldToLineLocal`과 동일한 패턴이나 대상 RectTransform이 다름 — 공용 헬퍼로 통합하거나 별도 오버로드로 분리)

### Step 6 — 씬/에디터 배치 (Unity Editor 작업, MCP 활용)

- [x] `FallingNode` 프리팹 생성: `Image` + 자식 `TMP_Text`(`TextMeshProUGUI`) + `FallingNodeView` 컴포넌트 — `Assets/03. Prefabs/FallingNode.prefab`
- [x] 패턴인풋 패널 하위에 `FallingNodeParent` RectTransform 생성 — `PointBackground` 자식으로 배치 후 sibling index를 `PatternLine` 바로 앞으로 이동 (Point 레이어 위, `PatternLine` 레이어 아래)
- [x] `PatternHandler` 인스펙터에 `fallingNodeParent` 참조 연결, `fallingNodeColorPalette`에 6색 팔레트 설정 (`fallDuration`/`fallSpawnOffsetY`는 기본값 유지)
- [x] (v4 추가) 씬에 `Pool` 싱글턴 GameObject 생성, `PoolKey.FallingNode` 키로 `FallingNode.prefab` + 초기 개수(9) 등록
- [x] ~~(v7) `FallingNodeParent`에 `RectMask2D` 추가~~ — 사용자가 거부하여 **제거하고 되돌림** (v8에서 대체)
- [x] 컴파일 확인 — 스크립트 컴파일 성공, 콘솔 에러/경고 없음
- [ ] ~~Play 모드에서 낙하 노드 스폰/하강/색상/텍스트 육안 확인~~ — 테스트용 `Pattern_Test.asset`(`Assets/03. Prefabs/Test/`)을 만들어 Play 모드 진입 후 `execute_code`로 `SetPattern` 호출까지는 성공했으나, **현재 에디터 세션이 창 포커스를 받지 못해 Play 모드의 프레임/`Time.time`이 전혀 진행되지 않는 환경 제약**으로 낙하 애니메이션의 실제 육안 확인은 자동화하지 못함. Unity 에디터를 직접 포커스한 상태에서 Play 버튼을 눌러 육안 확인이 필요함 (기존 `Plan_PatternLine.md`에서도 동일한 사유로 자동 검증 불가로 남긴 전례와 동일)

### ~~Step 7 — (v8) 등속 낙하로 재설계~~ (반려됨, 폐기)

사용자가 "등속이 아니라 노출 시간을 동일하게" 명확히 정정하여 이 방향은 폐기한다. 아래 Step 7'로 대체.

### Step 7' — (v9+v10, 확정) 노출 시간 균일화 유지 + 경계를 Canvas 실제 화면으로 교정 + 스폰 위치는 사용자 설정 + Gizmo

- [x] `PatternHandler`의 `visibleExposureDuration` 필드는 그대로 유지 (제거하지 않음)
- [x] `fallSpawnPositionY`(`fallingNodeParent` 로컬 좌표계 기준 절대 Y, v6에서 도입) 필드는 **그대로 유지** — 사용자가 직접 값을 입력해 낙하 시작 위치를 정한다 (자동 계산으로 대체하지 않음)
- [x] 내부 상태 `private float screenTopY;`(화면 진짜 상단의 `fallingNodeParent` 로컬 Y) 추가 — `Start()`에서 1회 계산해 캐싱, `ComputeFallDuration`의 "보이는 경계" 판정에만 사용(스폰 위치 계산에는 관여하지 않음):
  - 루트 `Canvas`의 `RectTransform`을 가져와 `canvasRect.TransformPoint(new Vector3(0f, canvasRect.rect.yMax, 0f))`로 "진짜 화면 상단"의 월드 좌표를 구함
  - 기존 `WorldToLocal(fallingNodeParent, ...)` 헬퍼로 그 월드 좌표를 `fallingNodeParent` 로컬 좌표로 변환한 값이 `screenTopY`
  - (Canvas는 런타임에 위치가 바뀌지 않는다고 가정 — 기존 `canvas`/`canvasCamera`를 `Start()`에서 1회만 해석하는 코드 관례와 동일)
- [x] `ComputeFallDuration(pointIndex)`를 v5 공식 그대로 유지하되 경계값만 `fallingNodeParent.rect.yMax` → `screenTopY`로 교체, 총 이동거리는 사용자가 설정한 `fallSpawnPositionY` 기준 그대로 사용:

  ```
  private float ComputeFallDuration(int pointIndex)
  {
      Vector3 worldPosition = patternPoints[pointIndex].transform.position;
      Vector2 targetLocalPos = WorldToLocal(fallingNodeParent, worldPosition);

      float totalDistance = Mathf.Max(fallSpawnPositionY - targetLocalPos.y, 0f);
      if (totalDistance <= 0f) return visibleExposureDuration;

      float visibleDistance = Mathf.Clamp(screenTopY - targetLocalPos.y, 0f, totalDistance);
      if (visibleDistance <= 0f) return visibleExposureDuration;

      return totalDistance * visibleExposureDuration / visibleDistance;
  }
  ```

- [x] `SpawnFallingNode`의 `node.Initialize(..., fallSpawnPositionY, ...)` 호출은 필드명 변경 없이 그대로 유지 (v6 그대로)
- [x] **(v10 신규)** `PatternHandler`에 `private void OnDrawGizmos()` 추가 — `fallingNodeParent`가 할당돼 있으면, `fallingNodeParent` 로컬 좌표 `(rect.xMin, fallSpawnPositionY)` ~ `(rect.xMax, fallSpawnPositionY)`를 `fallingNodeParent.TransformPoint(...)`로 월드 좌표 변환해 `Gizmos.DrawLine`으로 가로선을 그린다 — Scene 뷰에서 낙하 시작 높이를 바로 확인/조정할 수 있도록 함 (Play 여부와 무관하게 항상 보이는 에디터 전용 시각화, 런타임 로직에 영향 없음)
- [ ] `PatternHandler` 인스펙터에서 `fallSpawnPositionY`를 Scene 뷰 Gizmo로 확인하며 실제 화면 밖 지점으로 조정, `visibleExposureDuration`은 기존 값 유지
- [ ] 컴파일 확인 후, Play 모드에서 (에디터 직접 포커스 상태로) 상단/중단/하단 행 모두 화면 위쪽 바깥에서 나타나 각기 다른 속도로(상단이 더 느리게) 떨어지지만 노출 시간은 비슷하게 느껴지는지 육안 확인

### ~~Step 8 — (v11) 노출 시간을 Pattern SO별로 설정~~ (v12로 대체)

`Pattern` 전체 단위였던 것을 `PatternData`(노드) 단위로 정정 — 아래 Step 8'로 대체.

### Step 8' — (v12, 확정) 노출 시간을 PatternData(노드)별로 설정, 기본값 0.5

- [x] `Pattern.cs`의 `PatternData` 클래스에 `[Min(0f)] public float visibleExposureDuration = 0.5f;` 필드 추가 (`index`/`inputTime`과 동일한 공개 필드 스타일)
- [x] `PatternHandler`에서 `[SerializeField] private float visibleExposureDuration` 필드 제거
- [x] `ComputeFallDuration(int pointIndex)`를 `ComputeFallDuration(int pointIndex, float exposureDuration)`로 시그니처 변경 — 내부에서 쓰던 `visibleExposureDuration` 참조를 매개변수 `exposureDuration`으로 교체 (경계/거리 비교 로직은 동일)
- [x] `SetPattern()`의 스폰 스케줄 계산 루프에서 `ComputeFallDuration(nowPattern.AllData[i].index, nowPattern.AllData[i].visibleExposureDuration)` 호출로 변경
- [x] 기존 `Pattern_Test.asset` 등 이미 만들어진 Pattern SO 에셋은 `patternDatas` 배열의 각 원소에 새 필드가 기본값 0.5로 채워짐 (필요시 인스펙터에서 노드별로 개별 조정)
- [x] 컴파일 확인, 콘솔 에러 없는지 확인

### Step 9 — (v13, 확정) Point 판정 색상 피드백

- [x] `Assets/02. Scripts/Pattern/Handler/Point.cs`에 `using UnityEngine.UI;` 추가, `[SerializeField] private Image image;` 필드 추가
- [x] `[SerializeField] private Color perfectColor = Color.blue;`, `[SerializeField] private Color goodColor = Color.green;`, `[SerializeField] private Color missColor = Color.red;` 필드 추가, `private Color defaultColor;` 내부 상태 추가
- [x] `Start()`/`Reset()`에서 기존 `handler ??= FindAnyObjectByType<PatternHandler>();`와 동일한 관례로 `image ??= GetComponent<Image>();` 추가, `Start()`에서 `defaultColor = image != null ? image.color : Color.white;`로 원래 색 캐싱
- [x] `public void SetJudgementColor(JudgementResult result)` 추가 — `result`에 따라 `image.color`를 `perfectColor`/`goodColor`/`missColor`로 설정 (`image`가 null이면 무시)
- [x] `public void ResetColor()` 추가 — `image.color = defaultColor;`
- [x] `PatternHandler.AddPattern(int index)` 수정:
  - 오답 인덱스 분기(`index != nowPattern.ExpectedPointIndex`)에 `patternPoints[index].SetJudgementColor(JudgementResult.Miss);` 추가
  - 정답 인덱스 판정 후(`result` 계산 직후) `patternPoints[index].SetJudgementColor(result);` 추가
- [x] `PatternHandler.EndDrag()`의 기존 `foreach (var p in patternPoints) p.ResetBusy();` 루프에 `p.ResetColor();`도 함께 호출하도록 추가
- [x] `PatternHandler.SetPattern()`에도 방어적으로 9개 Point 전부 `ResetColor()` 호출 (이전 패턴의 잔여 색상 정리)
- [x] 컴파일 확인, 콘솔 에러 없는지 확인
- [ ] Play 모드(에디터 직접 포커스)에서 실제로 Point를 드래그해 Perfect/Good/Miss 각각에서 파랑/초록/빨강으로 바뀌는지, 드래그 종료 후 원래 색으로 돌아오는지 육안 확인

## 진행 상태 표기 규칙

구현 시작 후 각 항목을 완료할 때마다 `- [ ]` → `- [x]`로 갱신하며, 전 단계가 끝날 때까지 중단 없이 진행합니다.

> > > 여기에 피드백을 남겨주세요.
> > > PatternData에서 노출 시간 정하고 기본 값은 0.5로 하자
