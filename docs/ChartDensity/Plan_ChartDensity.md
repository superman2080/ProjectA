# Plan — 레벨별 노트 밀도 (ChartDensity)

근거: `Research_ChartDensity.md`

## 목표
Linear 굽기에서 **레벨이 노트 수를 직접 정한다.** 레벨이 1 오를 때마다 노트가 조금씩 늘고, 10/11·20/21 경계에서 계단식으로 뛰지 않는다. 넘치는 온셋은 **박 위치를 감안해 약한 것부터** 버린다.

## 결정 사항 (피드백 바람)

- **D1. 목표 밀도 곡선 = 레벨에 대한 선형 보간(NPS).**
  `nps(level) = lerp(minNps, maxNps, (level − 1) / 29)`. 기본값 `minNps = 0.8`, `maxNps = 5.0`
  → Lv1 0.8 · Lv10 ≈ 2.1 · **Lv11 ≈ 2.25** · Lv20 ≈ 3.6 · Lv30 5.0 NPS.
  (현재 Lv10 실측 2.3 NPS와 거의 같아 기존 체감이 유지된다.) 두 값은 툴 필드로 노출한다.
- **D2. 해상도(`LevelToSubdivision`)는 레벨에서 떼지 않는다.** 노트 **수**는 솎아내기가 연속으로 만들고, 해상도는 "엇박이 후보가 될 수 있는가"만 정한다. 박 위치 가중(D4) 덕에 Lv11에서 8분 격자가 열려도 대부분 정박이 남는다.
  - ⚠ 남는 차이: `maxGroupGapSteps`가 격자 칸 단위라 Lv11부터 패턴이 더 잘게 끊긴다. 노트 수와 무관해 이번 범위에서 빼지만, 체감 차이가 남으면 다음 작업으로 **그룹 기준을 박 단위로** 바꾼다.
  - 대안: 해상도를 항상 최고치(16분)로 두고 박 위치 가중만으로 레벨을 표현한다(경계가 완전히 사라지지만 그룹 기준을 반드시 같이 고쳐야 한다).
- **D3. 분모는 첫 온셋 ~ 마지막 온셋 구간이다.** 인트로·아웃트로 무음이 밀도를 깎지 않게 한다.
  목표 노트 수 = `round(nps × (마지막 − 첫 온셋))`.
- **D4. 버리는 순서 = 세기 × 박 위치 가중.** 가중 기본값: 마디 첫 박 1.3 · 정박 1.0 · 8분 엇박 0.7 · 16분 엇박 0.5. 같은 점수면 이른 온셋이 남는다(결과가 결정적이어야 재굽기가 같은 채보를 낸다).
- **D5. 전역이 아니라 구간별로 솎는다.** 전역 상위 N개를 고르면 **조용한 구간이 통째로 사라진다**(세기가 절대 RMS 증가량이라서). 대신 곡 전체의 비율 `r = 목표 / 후보`를 구하고, **`segmentBars`(기본 2마디) 구간마다** 그 구간 후보의 `r`배를 남긴다. 반올림 오차는 다음 구간으로 이월해 총합이 목표와 정확히 맞는다. → 음악이 빽빽한 곳은 빽빽하게, 성긴 곳은 성기게 남는다.
- **D6. 후보가 목표보다 적으면 그대로 둔다.** 감지 임계를 몰래 낮추지 않는다. 툴에 "후보 N < 목표 M — 이 곡은 이 레벨 밀도를 못 채웁니다(`RMS 임계값`을 낮춰 보세요)" 경고를 띄운다.
- **D7. 밀도 노브(`minNps`·`maxNps`·`segmentBars`)는 툴 필드로만 둔다.** `rmsThreshold`처럼 `SongChart`에 저장하지 않는다(런타임이 안 읽고, 스키마를 늘리지 않는다).

## 단계

- [x] **Step 1: 세기를 살린다 — 감지** (`Core/OnsetDetector.cs`)
  - `public readonly struct Onset { float Time; float Strength; }` 추가(Core).
  - `DetectWithStrength(...) : Onset[]` — `Strength = rms − previousRms`. 기존 `Detect`는 이것을 불러 시각만 돌려준다(기존 테스트·호출부 회귀 0).

- [x] **Step 2: 세기를 살린다 — 스냅** (`Core/BeatGrid.cs`)
  - `SnapStrongest(IReadOnlyList<Onset>, BeatGrid) : Onset[]` — 칸당 **가장 센** 온셋을 남기고 시각을 격자로 맞춘다. 기존 `Snap`은 그대로 둔다.

- [x] **Step 3: 목표 개수 계산** (`Core/ChartDensity.cs`, 신규)
  - `TargetNps(level, minNps, maxNps)`, `TargetCount(nps, firstTime, lastTime)`.

- [x] **Step 4: 솎아내기** (`Core/OnsetThinner.cs`, 신규)
  - `Thin(IReadOnlyList<Onset> snapped, BeatGrid grid, int subdivisionsPerBeat, int beatsPerBar, int targetCount, int segmentBars, BeatWeights weights) : float[]`
  - 박 위치 판정: 격자 인덱스 `i`에서 `i % (sub × beatsPerBar) == 0` → 마디 첫 박, `i % sub == 0` → 정박, `sub ≥ 4`이고 `i % (sub/2) == 0` → 8분 엇박, 그 외 16분 엇박.
  - D5의 구간별 비율 + 이월. `targetCount >= 후보 수`면 그대로 반환.
  - 결과는 시각 오름차순.

- [x] **Step 5: 굽기 파이프라인에 끼운다** (`PatternChartWindow.cs` Linear 분기)
  - `DetectWithStrength → SnapStrongest → Thin → (기존) OnsetGrouper.Group → Split → 템플릿 배정`.
  - 로그 한 줄: `레벨 L · 목표 X NPS · 후보 N → 남김 M (구간 S마디)`.

- [x] **Step 6: 툴 UI** (`DrawGridFields`, Linear 전용)
  - 필드: `최소 NPS(Lv1)`, `최대 NPS(Lv30)`, `솎기 구간(마디)`.
  - 표시: `이 레벨 목표: X NPS`. 분석이 한 번 돈 뒤에는 `후보 N → 목표 M` 미리보기와 D6 경고.
  - `beatsPerBar` 필드가 지금 Loop 모드에서만 보인다 → Linear에도 보이게 한다(마디 첫 박 판정과 구간 길이가 읽는다).

- [x] **Step 7: 테스트** (`Tests/`)
  - `OnsetDetectorTests`: `DetectWithStrength`의 세기 = RMS 증가량.
  - `BeatGridTests`: `SnapStrongest`가 칸당 센 쪽을 남긴다.
  - `ChartDensityTests`: 레벨 단조 증가 · 10→11 증가폭이 작다(계단 없음).
  - `OnsetThinnerTests`: ① 남긴 개수 == 목표 ② 같은 세기면 정박이 엇박보다 남는다 ③ 조용한 구간도 비율만큼 살아남는다 ④ 목표 ≥ 후보면 무변경 ⑤ 같은 입력 → 같은 출력.

- [x] **Step 8: 문서**
  - `CLAUDE.md` §5: "Linear 굽기의 노트 수는 레벨 → NPS가 정한다(해상도는 후보 범위만). 솎기는 세기 × 박 위치, 구간별 비율" 요약 + 링크.
  - `docs/!Guides/Guide_PatternChartTool.md`: 밀도 노브 설명.

## 범위 밖
- 기존 채보 3개 자동 재굽기(필요하면 툴에서 다시 굽는다. Lv20의 BPM 120 불일치는 재굽기 전에 확인할 것 — Research §2).
- ~~그룹 기준(`maxGroupGapSteps`)을 박 단위로 바꾸는 것(D2 참고).~~ → **2차에서 다룬다**(아래).
- Loop 모드·런타임.

## 다른 작업과의 관계
- `docs/NoSingleNotePattern`과 같은 Linear 경로(Step 5 ↔ 그쪽 Step 4)를 고친다. 솎기 뒤 단독 온셋이 늘면 그쪽 규칙(D3)으로 버려져 **최종 노트 수가 목표보다 조금 적을 수 있다.** 두 작업을 모두 하면 Step 5의 로그가 "솎은 뒤 M → 최종 K"까지 찍도록 맞춘다.

---

# 2차 — 그룹 기준을 박 단위로 (1박)

근거: `Research_ChartDensity.md` §7~10

## 목표
그룹 기준을 **격자 칸이 아니라 박**으로 잰다. 해상도 경계(10/11·20/21)를 넘어도 같은 음악에 같은 기준이 적용되어, 레벨이 오를 때 노트 수가 줄지 않는다.

## 결정 사항
- **D8. 기준은 1박(기본값)이다.** 툴 필드 `최대 그룹 갭(그리드 스텝)`을 `최대 그룹 갭(박)`으로 바꾸고 기본 1. 내부에서는 `박 수 × subdivisionsPerBeat` 칸으로 환산해 기존 `OnsetGrouper.Group`에 넘긴다.
  - 4분 격자(Lv1~10)에서는 1박 = 1칸이라 **결과가 지금과 같다**.
  - 실측 예상: Lv11 147 → 253, Lv20 335 → 423, Lv21 194 → 438, Lv30 439 → 597(Research §9).
- **D9. 환산은 Core에 둔다**(`OnsetGrouper.GroupByBeats(snapped, grid, subdivisionsPerBeat, maxGapBeats)`) — 툴이 칸 수를 직접 곱하면 굽기와 `MergeWithNext` 경고가 각자 곱해 어긋날 수 있다. 같은 값을 주는 `GapSteps(subdivisionsPerBeat, maxGapBeats)`도 공개해 경고 식이 그것을 읽는다. 기존 `Group(…, maxGroupGapSteps)`는 그대로 둔다(테스트·호출 회귀 0).
- **D10. 낮은 레벨 손실(Lv1 95 → 37)은 이번 범위 밖이다** → **3차(D11~D15)에서 다룬다**(Research §10). 기준을 밀도에 맞춰 넓히는 안은 별도 결정으로 남긴다.

## 단계

- [x] **Step 9: Core — 박 단위 그룹핑** (`Core/OnsetGrouper.cs`)
  - `GapSteps(int subdivisionsPerBeat, int maxGapBeats) => max(sub, 1) × max(beats, 1)`.
  - `GroupByBeats(snapped, grid, subdivisionsPerBeat, maxGapBeats) => Group(snapped, grid, GapSteps(...))`.

- [x] **Step 10: 툴 — 필드와 호출부** (`PatternChartWindow.cs`)
  - `maxGroupGapSteps` → `maxGroupGapBeats`(기본 1), 라벨 `최대 그룹 갭(박)` + 현재 해상도에서의 칸 수·초 표시(`= N칸 · X.XX초`).
  - Linear 굽기: `OnsetGrouper.GroupByBeats(snapped, grid, subdivision, maxGroupGapBeats)`.
  - `MergeWithNext` 경고 임계: `GridInterval × (GapSteps(subdivision, maxGroupGapBeats) + 1)`.

- [x] **Step 11: 테스트** (`Tests/OnsetGrouperTests.cs`)
  - 8분 격자에서 정박만 남은 온셋(2칸 간격)이 1박 기준이면 **한 그룹**이다(격자 1칸 기준이면 갈라진다 — 회귀의 정확한 재현).
  - 4분 격자에서 `GroupByBeats(…, 1)`과 `Group(…, 1)`의 결과가 같다(Lv1~10 회귀 0).
  - `GapSteps`: sub 1/2/4 × 1박 = 1/2/4칸.

- [x] **Step 12: 검증** — 같은 곡 시뮬레이션(Research §7 방식)으로 Lv1~30 최종 노트 수가 **레벨에 대해 줄지 않는지** 확인하고 결과를 아래 표에 적는다.

- [x] **Step 13: 문서**
  - `CLAUDE.md` §5의 "⚠ 그룹 기준은 여전히 격자 칸 단위(남은 과제)"를 "그룹 기준은 박 단위(기본 1박) — 칸 단위였을 때 Lv11에서 노트가 절반으로 줄었다" + 낮은 레벨 손실이 남았다는 한 줄로 바꾼다.
  - `docs/!Guides/Guide_PatternChartTool.md`의 `최대 그룹 갭` 설명을 박 단위로.

## 검증 결과

유닛테스트 45개 통과(`ChartGen.Core` + Tests를 .NET으로 컴파일해 실행). 실곡 시뮬레이션(Research §7과 같은 조건, 그룹 기준 1박):

| 레벨 | 목표 | 단독 버림 | 최종 |
|---|---|---|---|
| 1 | 95 | 58 | 37 |
| 5 | 164 | 32 | 132 |
| 9 | 233 | 6 | 227 |
| 10 | 251 | 4 | 247 |
| 11 | 268 | 15 | 253 |
| 12 | 286 | 12 | 274 |
| 15 | 338 | 7 | 331 |
| 20 | 424 | 1 | 423 |
| 21 | 442 | 4 | 438 |
| 25 | 511 | 1 | 510 |
| 30 | 598 | 1 | 597 |

→ **Lv1~30 전 구간에서 레벨이 오를 때 최종 노트 수가 줄지 않는다**(10→11: 247 → 253, 20→21: 423 → 438). Lv1~10은 1박 = 1칸이라 예전과 같다. 낮은 레벨 손실(Lv1 37/95)은 D10대로 남아 있다.

⚠ Unity 안에서의 컴파일·툴 동작은 아직 확인하지 않았다(에디터 코드는 .NET 시뮬레이션에 포함되지 않는다).

---

# 3차 — 낮은 레벨 손실: 악구로 묶고, 솎고, 소속으로 모은다

근거: `Research_ChartDensity.md` §11~13

## 목표
모든 레벨에서 최종 노트 수가 목표에 닿게 한다(실측 기대: Lv1 37 → 93, Lv5 132 → 163). **쉼표 경계**와 **조용한 구간의 몫**(1차 D5)은 지금 그대로 지킨다.

## 결정 사항 (확정 — 추천안 채택)
- **D11. B′ 방식(확정)** — 순서를 `후보를 악구로 묶기 → 구간별 솎기(지금 그대로) → 남은 온셋을 소속 악구로 모으기 → 1개 남은 악구 보정`으로 바꾼다.
  - 악구 = 솎기 **전** 후보를 `최대 그룹 갭(박)`(기본 1)으로 묶은 그룹. 음악의 쉼표만 경계가 된다.
  - **기각**: A(솎은 뒤 갭을 넓힘)는 쉼표를 가로지르고(Lv1 5박), B(악구 안 상위 N)는 조용한 구간을 굶긴다(공정성 최소 0.00). Research §12 표.
- **D12. 1개만 남은 악구 보정(확정)** — 그때까지 남긴 수가 목표보다 **적으면** 같은 악구의 다음 후보(세기 × 박 위치 점수가 가장 높은 것)를 하나 더 살려 2개로 만들고, **많거나 같으면** 그 1개를 버린다. 악구를 시간순으로 돌며 누적 수로 판단한다(결정적).
  - 후보가 1개뿐인 악구(원래부터 고립된 단독 온셋)는 지금처럼 버린다(`docs/NoSingleNotePattern` D3 그대로).
- **D13. `OnsetThinner`는 고치지 않는다(확정)** — 구간별 비율·박 위치 가중·결정성이 그대로 살아야 공정성이 유지된다. 새 단계는 그 **뒤**에 붙는다.
- **D14. 툴의 병합 경고는 악구로 판정한다(확정)** — 분석으로 만든 드래프트는 자기 악구 번호를 든다(툴 전용 필드, 저장하지 않는다). `아래와 합치기`는 **악구가 다를 때만** "쉬는 구간을 가로지른다"를 경고한다. 악구 번호가 없는 드래프트(불러온 채보·쪼갠 조각의 출처가 없는 경우)는 지금의 시간 임계로 폴백한다.
- **D15. 청크를 빈 박에서 우선 자르는 개선은 범위 밖이다(확정)**(Research §13) — 패턴 하나가 악구 안의 긴 빈 박을 낄 수 있다. 쉼표는 넘지 않는다.

## 단계

- [x] **Step 14: Core — 악구 조립** (`Core/OnsetPhraser.cs`, 신규)
  - `Assemble(IReadOnlyList<Onset> candidates, IReadOnlyList<float> kept, BeatGrid grid, int subdivisionsPerBeat, int beatsPerBar, int maxGapBeats, int minSize, int targetCount, BeatWeights weights) : List<List<float>>`
    1. 후보 시각을 `OnsetGrouper.GroupByBeats(…, maxGapBeats)`로 묶어 악구를 만든다.
    2. `kept`를 격자 인덱스로 각 악구에 배정한다(소속으로 모으기 — 간격을 보지 않는다).
    3. 악구를 시간순으로 돌며 D12 보정: `minSize` 미만으로 남은 악구는 누적 수 < 목표면 같은 악구의 남은 후보 중 최고 점수(`OnsetThinner.WeightOf` × 세기, 동점이면 이른 것)를 채워 `minSize`로 올리고, 아니면 비운다.
    4. 비지 않은 악구만, 각 악구 안은 시각 오름차순으로 돌려준다.
  - 결과 악구 번호를 같이 돌려준다(D14가 쓴다 — 리스트 인덱스로 충분).

- [x] **Step 15: 툴 파이프라인** (`PatternChartWindow.cs` Linear 분기)
  - `OnsetGrouper.GroupByBeats(snapped, …)` 자리를 `OnsetPhraser.Assemble(candidates, snapped, …)`로 바꾼다. 이후 청크 분할·배정은 그대로.
  - 로그: `후보 N → 악구 P개 → 솎기 M → 보정 +a −b → 최종 K`.

- [x] **Step 16: 병합 경고를 악구로** (D14)
  - `ChartEntryDraft`에 `int phrase = -1`(툴 전용, 저장 안 함). 분석이 채우고, `SplitDraft`는 꼬리에 같은 값을 물려준다. `MergeWithNext`는 두 값이 모두 0 이상이면 **값이 다를 때만** 경고하고, 하나라도 −1이면 지금의 시간 임계로 판정한다. 합친 결과는 앞 엔트리의 값을 든다.

- [x] **Step 17: 테스트** (`Tests/OnsetPhraserTests.cs`)
  - 한 악구 안에서 솎기가 2박 간격으로 남겨도 **한 그룹**으로 모인다(현재 회귀의 정확한 재현 — 간격 묶기라면 전부 단독).
  - 쉼표(> 갭)로 갈린 두 악구는 섞이지 않는다.
  - 1개만 남은 악구: 누적 < 목표면 2개로 채워지고(채운 것은 남은 후보 중 최고 점수), ≥ 목표면 비워진다.
  - 후보 1개짜리 악구는 언제나 버려진다.
  - 같은 입력 → 같은 출력.

- [x] **Step 18: 검증** — 같은 실곡 시뮬레이션으로 Lv1~30의 최종 수(목표 대비)와 공정성(2마디 구간 `남김 / 후보` 최소값)을 다시 재고 아래에 적는다. 기준: **모든 레벨에서 목표 −5 이내 · 레벨에 대해 단조 증가 · 공정성이 2차 결과보다 크게 나빠지지 않을 것**.

- [x] **Step 19: 문서**
  - `CLAUDE.md` §5: 순서가 `악구로 묶기 → 솎기 → 소속으로 모으기 → 1개 남은 악구 보정`이라는 것, 그리고 "솎은 뒤 간격으로 묶으면 그룹 경계가 솎기가 낸 구멍에서 생긴다(Lv1 95 → 37)"를 한 줄로. 2차의 "⚠ 낮은 레벨 손실은 남아 있다"를 지운다.
  - `Guide_PatternChartTool.md`: `최대 그룹 갭(박)`의 뜻이 "후보 사이 쉼표"라는 것, 낮은 레벨에서 패턴 안 노드 간격이 넓어지는 것이 정상이라는 것.

## 3차 검증 결과

유닛테스트 **51개 통과**(`ChartGen.Core` + Tests를 .NET으로 컴파일해 실행, `OnsetPhraserTests` 6개 추가). 실곡 시뮬레이션(Research §7 조건, Lv1~30 전부 실행 — 아래는 발췌):

| 레벨 | 목표 | 최종 | 차이 | 보정 | 공정성 | 패턴 안 평균 간격(박) |
|---|---|---|---|---|---|---|
| 1 | 95 | 93 | −2 | +0 −2 | 0.20 | 2.89 |
| 3 | 130 | 129 | −1 | +1 −2 | 0.20 | 2.12 |
| 5 | 164 | 163 | −1 | +1 −2 | 0.20 | 1.67 |
| 8 | 216 | 214 | −2 | +0 −2 | 0.60 | 1.27 |
| 10 | 251 | 248 | −3 | +0 −3 | 0.60 | 1.11 |
| 11 | 268 | 267 | −1 | +0 −1 | 0.50 | 1.02 |
| 15 | 338 | 337 | −1 | +0 −1 | 0.62 | 0.81 |
| 20 | 424 | 423 | −1 | +0 −1 | 0.88 | 0.65 |
| 21 | 442 | 441 | −1 | +0 −1 | 0.56 | 0.62 |
| 25 | 511 | 511 | 0 | +0 −0 | 0.75 | 0.53 |
| 30 | 598 | 597 | −1 | +0 −1 | 0.86 | 0.46 |

- **목표 −5 이내**: 전 레벨 −3 ~ 0. ✅ (2차: Lv1 −58)
- **레벨에 대해 단조 증가**: Lv1~30 전부. ✅
- **공정성**(2마디 구간 `남김 / 후보` 최소값): 낮은 레벨에서 솎기 직후 값보다 조금 낮다(Lv5 0.40 → 0.20, Lv10 0.75 → 0.60) — 1개 남은 악구를 비우는 보정 몫이다. 0이 된 구간은 없다. 2차의 실제 최종 결과는 단독 버림으로 이보다 나빴다.
- 패턴 안 평균 노드 간격은 Lv1 2.9박 → Lv30 0.46박으로 레벨에 따라 매끄럽게 준다(Research §13 예상대로).

⚠ Unity 안에서의 컴파일·툴 동작은 아직 확인하지 않았다(에디터 코드는 .NET 시뮬레이션에 포함되지 않는다).

