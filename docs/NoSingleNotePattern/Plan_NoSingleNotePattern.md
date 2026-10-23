# Plan — 1노트 패턴 제거 (NoSingleNotePattern)

근거: `Research_NoSingleNotePattern.md`

## 목표
1. 지금 있는 1노트 패턴(`PatternBlock(4)`, `PatternChain(4)`)과 그것을 쓰는 채보 엔트리를 없앤다.
2. 채보 생성(Linear 굽기 · 수동 편집 · Loop 저작) 어디에서도 1노트 엔트리가 **생기지 않고**, 생겨도 **저장되지 않게** 한다.

## 결정 사항 (피드백 바람)

- **D1. 연타(`isMash`)는 예외로 둔다.** `patternDatas` 1칸은 노드가 아니라 게이지 자리이고 엔트리 온셋도 2개다. 규칙은 "**연타가 아닌 패턴은 노드 2개 이상**"으로 적는다.
- **D2. 최소 노드 수는 상수 하나**(`Pattern.MinNodeCount = 2`)로 두고 툴·검증·분할기가 전부 이것을 읽는다. 분할기(`ChartGen.Core`)는 `Pattern`을 참조할 수 없으므로 인자로 받는다.
- **D3. 단독 온셋(그룹 크기 1)은 버린다.** 앞뒤가 쉼표로 막힌 온셋 하나를 이웃 그룹에 붙이면 쉬는 구간을 가로지르는 패턴이 된다(`MergeWithNext`가 이미 경고하는 부류). 굽기 결과 로그에 버린 개수와 시각을 찍는다.
  - 대안: 가까운 이웃 그룹에 강제로 붙인다(온셋 손실 0, 대신 쉼표를 가로지름).
- **D4. 기존 채보는 재굽기가 아니라 엔트리 이관으로 고친다.** 재굽기는 수동 편집과 cue가 인덱스로 밀린다. 이관 규칙:
  1. 1노트 엔트리를 **간격이 더 짧은 이웃**(동률이면 앞)과 합친다 — 대부분이 같은 그룹의 자투리라(실측 간격 0.25~0.5s) 원래 그룹으로 돌아가는 것이다.
  2. cue는 기존 `MergeWithNext` 규칙대로 **뒤 엔트리의 것이 살아남는다**(사슬 마무리 보존).
  3. 합친 크기의 템플릿을 그 채보의 `patternPool`에서 라운드로빈으로 다시 고른다. 없으면 반대쪽 이웃으로 재시도, 그래도 없으면 **그 온셋을 버린다**(D3과 같은 결말).
  4. 결과(합침/버림 건수, 템플릿이 바뀐 엔트리 번호)를 로그로 남긴다.
  - ⚠ 합쳐진 이웃은 **템플릿이 바뀐다**(노드 수가 달라졌으므로) — 그 엔트리의 클립·이펙트 연출도 바뀐다. 회피할 방법이 없다.
  - 대안: 1노트 엔트리를 그냥 삭제(이웃 무변경, 대신 Lv20은 노트 127개 손실).
- **D5. 튜토리얼 드릴은 `PatternChain(4)`를 빼고 3타로 줄인다.** `(3,4)→(4,6)→(6,4)`. 드릴은 마지막 패턴에 처치를 주므로 `(6,4)`가 자동으로 마무리가 된다. 대사("짧은 획으로…")와 어긋나지 않는다.

## 단계

- [x] **Step 1: 최소 노드 수 상수와 패턴 검증**
  - `Pattern.MinNodeCount = 2` 상수 추가.
  - `Pattern.OnValidate`: 연타가 아니고 `patternDatas.Count`가 1이면 `LogError`(연타의 "정확히 1칸"과 대칭).

- [x] **Step 2: 분할기가 1청크를 만들지 않는다** (`OnsetChunkSplitter`, `ChartGen.Core`)
  - `Split(totalCount, availableSizes, minSize = 1)` — 기본값 1이면 기존 동작 그대로.
  - `minSize` 미만 크기는 순환에서 제외. **청크를 떼고 나면 `minSize` 미만 자투리가 남는 선택은 미리 건너뛴다**(같은 조건을 만족하는 최대 크기로 대체) — 뒤에서 빌려 오지 않아도 되도록 앞에서 피한다(구현 중 변경: 빌리기보다 단순하고, 결과 크기가 언제나 `availableSizes` 안에 있다). 전체가 `minSize` 미만이면 **빈 리스트** → 호출부가 버린다(D3).
  - 맞는 크기가 하나도 없으면 남은 개수를 통째로 한 청크로 둔다(호출부의 기존 "템플릿 없음" 경로가 잡는다).
  - 테스트(`OnsetChunkSplitterTests`): `minSize = 2`에서 ① 모든 청크 ≥ 2 · 합 보존 ② `[3]`, 7 → 1이 안 나옴 ③ total 1 → 빈 리스트 ④ 기본값 경로의 기존 테스트는 유지.

- [x] **Step 3: 템플릿 라이브러리가 1노트를 등록하지 않는다** (`PatternTemplateLibrary.Register`)
  - `count < Pattern.MinNodeCount`면 등록하지 않는다(연타는 이미 앞에서 빠진다). → `AvailableNodeCounts`, `GetNextTemplate`, `GetRandomTemplate`(=`Reassign`), `MergeUnassigned`가 전부 1노트 템플릿을 못 고른다.
  - 풀 요약(PatternChartWindow 214행 근처 노드 수별 표시)에 1노트 템플릿이 끼어 있으면 "배정에서 제외됨" 경고.

- [x] **Step 4: Linear 굽기 호출부**
  - `Split(group.Count, library.AvailableNodeCounts, Pattern.MinNodeCount)`.
  - 빈 결과(단독 온셋)는 엔트리를 만들지 않고 버린 온셋을 모아 한 번에 로그.

- [x] **Step 5: 수동 편집 경로가 1노트를 만들지 않는다** (`PatternChartWindow.cs`)
  - 행 쪼개기 UI: `splitAt`을 `[MinNodeCount, nodeCount − MinNodeCount]`로 클램프, 범위가 없으면(노드 3개 이하) 쪼개기 버튼 비활성.
  - `ManualAssign`: 연타가 아니고 `have − want`가 1이면 거부 + 안내("뒤에 1노트가 남습니다 — 합치기로 크기를 맞추세요"). 1노트 템플릿 자체의 배정도 거부.
  - `MergeUnassigned`: 라이브러리가 1을 모르므로 자동으로 배정 안 됨(추가 코드 없음).

- [x] **Step 6: Loop 저작이 1노트를 놓지 않는다** (`PatternChartWindow.Loop.cs`)
  - `loopTemplate` 선택 시 연타가 아니고 노드가 1이면 선택을 거부하고 HelpBox로 사유 표시. `PlaceLoopEntry`에도 같은 가드.

- [x] **Step 7: 저장 검증에 항목 추가** (`ValidateForSave` — 두 모드 공유)
  - 연타가 아닌 엔트리의 온셋이 `MinNodeCount` 미만이면 **두 모드 모두 저장 중단**, 몇 번 엔트리인지 찍는다. (Step 8 이관 후에는 기존 채보도 이 검사를 통과하므로 Linear에서 막아도 "기존 채보를 못 고친다" 문제가 없다.)

- [x] **Step 8: 기존 채보 이관 메뉴** (`Tools/Pattern Chart Migration/Remove Single-Note Patterns`, `ChartGen/Editor/SingleNoteMigration.cs`, 에디터 전용 · 1회성) — ⚠ **코드 완료 · 실행 대기**(Unity가 프로젝트를 열고 있어 에디터 안에서 돌려야 한다)
  - 대상: 프로젝트의 모든 `SongChart`. D4 규칙으로 엔트리를 합치고 `spawnTimes`를 재계산한 뒤 저장. `patternPool`에서 1노트 템플릿 제거.
  - 실행 후 결과 로그를 이 문서 하단 "실행 기록"에 옮겨 적는다.

- [x] **Step 9: 씬·시퀀스 참조 정리** — Step 8 메뉴가 같이 한다(배열 원소째 제거, `[SerializeReference]` 안까지). 열려 있는 씬은 메모리에서만 고치고 더티로 남긴다 · ⚠ 실행 대기
  - `BattleScene`·`Tutorial`의 `PatternEffectDirector.prewarmPatterns`에서 두 GUID 제거.
  - `Seq_Tutorial.asset` 드릴에서 `PatternChain(4)` 제거(D5).

- [x] **Step 10: 1노트 템플릿 에셋 삭제** — Step 8 메뉴의 마지막 단계(남은 참조 0개인 것만) · ⚠ 실행 대기
  - `PatternBlock(4).asset`, `PatternChain(4).asset`(+ `.meta`). 삭제 전 GUID 전수 검색으로 남은 참조 0 확인.

- [x] **Step 11: 문서**
  - `CLAUDE.md` §5에 "연타가 아닌 패턴은 노드 2개 이상 — 분할기·라이브러리·수동 편집·Loop·저장 검증이 같은 상수를 본다" 한 줄과 상세 링크.
  - `docs/!Guides/Guide_PatternChartTool.md`에 쪼개기 범위·단독 온셋 버림 반영.

## 범위 밖
- 런타임(`PatternHandler`·`ChartPlayer`) 변경 없음.
- 연타 패턴, `chainKillRatio` 필드 제거는 건드리지 않는다.

## 실행 기록

**사전 시뮬레이션**(같은 규칙을 채보 YAML에 돌린 결과 — 실제 실행 전 예상치):

| 채보 | 앞과 합침 | 뒤와 합침 | 버림 | 엔트리 | 노트(전 → 후) |
|---|---|---|---|---|---|
| `…Dreamer…_Lv10` | 5 | 0 | 0 | 88 → 83 | 275 → 275 |
| `…Dreamer…_Lv20` | 31 | 70 | 0 | 220 → 119 | 382 → 382 |
| `LinearHeleen…_Lv10` | 1 | 22 | 0 | 101 → 78 | 282 → 282 |

→ 노트 손실 0. 풀의 크기가 2~5라 합친 크기(최대 6)가 맞지 않을 때는 반대쪽 이웃이 받아 준다.

(메뉴 실행 후 콘솔 로그를 여기에 옮겨 적는다.)
