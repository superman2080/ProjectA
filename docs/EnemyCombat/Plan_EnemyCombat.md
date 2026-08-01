# Plan — EnemyCombat (무쌍형 전투 전환)

근거: `docs/EnemyCombat/Research_EnemyCombat.md`

---

## 한눈에

원형 아레나 중앙에 선 플레이어가, 링에 둘러선 적들과 **한 번에 하나씩** 교전한다. **적의 공격 하나 = 패턴 하나**이며 플레이어는 그 패턴을 그어 **패링**(적이 공격해 옴)하거나 **공격**(적이 무방비)한다. 처치하면 다음 적으로 넘어간다.

**판정 계층은 한 줄도 안 바뀐다.** 이 전환은 통째로 연출 계층 교체다.

## 확정된 방향 (사용자 결정)

| 쟁점 | 결정 |
|---|---|
| 무대 | ~~**원형 아레나.** 플레이어 중앙 고정, 적은 링 외곽에 랜덤 배치. 전진 이동 없음~~ → **폐기(2026-08-02).** 무대가 월드 원점에 고정되고 플레이어가 그 사이를 이동한다. `docs/StageTraversal/` |
| 적 수명 | **적이 여러 패턴을 산다.** 채보가 `killOnSuccess`로 지정한 공격을 **성공**하면 처치. **실패하면 안 죽고 같은 적과 교전 지속** |
| 공격 주체 | 패턴마다 **누가 휘두르는가**가 정해진다(`Attacker { Enemy, Player }`). **판정·입력은 동일**, 모션과 연출만 분기. `PatternHandler` 무수정 |
| 플레이어 이동 | 플레이어 공격 = 조금 전진 / 적 공격(패링) = 조금 후퇴. **패턴 사이에만**, 시간 없으면 생략 |
| 체력 | 단순 카운트. **적 공격을 못 막았을 때만** 감소, 0이면 곡 클리어 실패 |
| Slice 시스템 | **유지·확장.** ① 원거리 오브젝트 절단/패링 ② 적 처치 시 모델이 갈라짐 |
| 조각 운동 | **절단 이후는 전부 Rigidbody.** 접근 구간만 닫힌 식(도착 시각 계약) |
| 적 모션 | 플레이어 클립 **휴머노이드 리타깃** |
| 곡 종료 | 남은 적은 **머티리얼 dissolve**로 소멸 후 스테이지 종료 |

## 설계 원칙 (Plan 전체에 걸리는 제약)

1. **`PatternHandler`를 수정하지 않는다.** 전부 기존 이벤트 구독으로 붙인다.
2. **타이밍은 기존 식을 그대로 승계한다** — `impactTime = Deadline + impactOffset`, `Deadline = LastNodeTime + GoodWindow`. 플레이어 임팩트 프레임·카메라 쉐이크가 이 식 하나로 동기화돼 있으므로 적 공격이 닿는 순간도 반드시 같은 식이어야 한다.
3. **`SliceTargetDirector`를 백지에서 다시 쓰지 않는다.** 예약·토큰 FIFO 매칭·접근 클램프·프리팹 풀은 검증된 코드다.
4. **저작 데이터는 소유자를 기준으로 나눈다** — 모양에 종속되면 `Pattern`, 채보 위치에 종속되면 `SongChartEntry`, 런타임에 정해지면 `EnemyDirector`(§1-1 표).
5. **미배선/미지정은 전부 무연출 폴백.** 기존 채보 4종이 그대로 재생돼야 한다.
6. **패턴 진행 중에는 아무도 움직이지 않는다.** 모든 이동·회전은 클립 시작 시각(`scheduleStart`) 전에 끝난다(§4-2).

---

## 아키텍처 한 장

```
ChartPlayer ─┬─ EnemyDirector.EnqueueCue(entry.enemyCue)   ← SetPattern 직전, 같은 프레임
             └─ PatternHandler.SetPattern(...)
                        │
                        ├─ OnPatternQueued ──▶ EnemyDirector: cue 하나 dequeue
                        │                        ├ currentOpponent에게 공격 배정 (링 → 결투 앵커 이동)
                        │                        └ cue.projectile → SliceTargetDirector에 투사체 예약
                        ├─ OnJudgeTargetBegan ─▶ CharacterActionPlayer: attacker를 pull → 클립 선택
                        ├─ OnJudgeTargetFirstMiss ▶ 실패 확정 (피격은 impactTime에 예약)
                        └─ OnPatternComplete ──▶ 성패 확정 → 적 리액션 / 처치 / 다음 상대
```

**패턴 인스턴스 ↔ 적 매칭**: 이벤트 페이로드에 인스턴스 ID가 없다(Research §1.1). `SliceTargetDirector`가 이미 쓰는 **토큰 FIFO 매칭**을 승계한다. cue도 같은 FIFO 규율로 `ChartPlayer`가 밀어 넣는다 — 판정 대상은 언제나 선두 하나이고 완료도 순서대로 일어나므로 안전하다.

> **왜 cue를 `ChartPlayer`가 미는가**: `SetPattern`은 `Pattern` 템플릿만 받는다. 엔트리 정보를 `PatternHandler` 페이로드에 실으면 판정 계층이 적을 알게 된다(원칙 1 위반). 호출 직전에 옆으로 밀어 넣으면 판정 계층은 아무것도 모른 채로 남는다.

---

# Step 1 — 데이터 계층

- [x] `Assets/02. Scripts/Enemy/EnemyDefinition.cs` (ScriptableObject, `EnemySpace`)
  - `prefab` / `displayName` / `deathSliceSet`(비면 절단 없이 소멸) / `initialPoolSize` / `maxPoolSize`
  - **HP는 두지 않는다.** 처치 시점은 채보가 정한다 — 곡에 맞춰 죽어야 하므로 저작 데이터가 진실의 원천이다.
- [x] `Assets/02. Scripts/Enemy/EnemyCue.cs` — `[Serializable]`
  - `attacker`(`enum Attacker { Enemy, Player }` — **이 패턴에서 누가 휘두르는가**) / `killOnSuccess`(bool) / `projectile`(SliceSet, null이면 없음) / `impactOffset`(float)
  - `Attacker.Enemy` = 적이 공격해 오고 플레이어는 **패링**한다. `Attacker.Player` = 적이 무방비고 플레이어가 **벤다**. 이름에서 세 갈래(적 클립 재생 여부 / 플레이어 클립 선택 / 피격 여부)가 전부 따라 나온다.
  - **슬롯 인덱스 없음.** 교전 상대는 디렉터가 링 로스터에서 고른다(§3-1). `killOnSuccess`가 곧 "다음 적으로 넘어가라"는 신호다.
- [x] `SongChart.cs` — `SongChartEntry`에 `public EnemyCue enemyCue;` (기본값 = 무연출)
- [x] `Pattern.cs`의 `sliceTarget*` 3필드는 **이번 단계에서 지우지 않는다.** 기존 채보가 아직 이걸로 돈다 → Step 9에서 정리.

## 1-1. 소유권 — 무엇이 어디에 사는가

**기준: 모양에 종속되나, 채보 위치에 종속되나, 런타임에 정해지나.**

| 데이터 | 소유자 | 이유 |
|---|---|---|
| 적 공격 / 플레이어 패링 / 플레이어 공격 애니메이션 | **`Pattern`** | 셋 다 같은 칼 궤적의 앞뒷면이다. `SuccessAnimationClip`이 패턴에 있는 이유(모양 = 칼 궤적)와 정확히 같다 |
| 어떤 성격의 공격인가 / 이 공격이 교전을 끝내는가 | `SongChartEntry.EnemyCue` | 같은 모양이 곡 안에서 재사용되는데 상황은 매번 다르다 |
| 누가 상대인가 / 어디에 서 있는가 | `EnemyDirector` (런타임) | 링 로스터에서 정해진다 — 저작 데이터가 아니다 |

- [x] `Assets/02. Scripts/Pattern/ClipAlignment.cs` — `[Serializable] class ClipAlignment { clip, startOffset, duration, impactTime, speed }`
  - `ResolvedDuration` / `ResolveImpactSpan()`은 지금 `Pattern`·`CharacterActionPlayer`에 흩어져 있는 계산을 옮긴 것. **플레이어와 적이 같은 정렬 규칙을 쓰게 되는 게 핵심**이다.
- [x] `Pattern`에 `ClipAlignment` 세 필드 추가. 비면 각각 무연출.

| 필드 | 재생 주체 | 조건 |
|---|---|---|
| `enemyAttack` | 적 | `Attacker.Enemy` — 적이 휘두른다 |
| `playerParry` | 플레이어 | `Attacker.Enemy` — 그 공격을 받아친다 |
| `playerAttack` | 플레이어 | `Attacker.Player` — 무방비 적을 벤다 |

- [x] **기존 플레이어 클립 5필드는 건드리지 않는다.** 구조체로 접으면 직렬화가 깨져 패턴 에셋 10종을 전부 재오서링해야 한다. 이관은 Step 9에서 마이그레이션 코드로.
- [ ] 클립은 **휴머노이드**여야 한다 — 리타깃으로 플레이어 리그와 적 리그 양쪽에 걸린다.
- [ ] 오서링은 기존 `Tools/Animation Clip Trimmer`(Start/**Impact**/End) 재사용. 적 클립도 임팩트 프레임을 찍어야 `impactTime`에 칼이 닿는다.
- [x] `Pattern.OnValidate`의 임팩트 범위 검증을 세 필드 전부에 적용.

## 1-2. Attacker × 성패의 네 칸 (연출 사양의 진실의 원천)

| | **성공** | **실패** |
|---|---|---|
| **`Attacker.Enemy`** (적이 공격 → 패링) | 칼이 맞부딪히고 **적이 뒤로 밀려난다**(KnockBack). 적 생존 — `killOnSuccess`면 처치 | **`impactTime`에 플레이어가 맞는다**(적 칼이 도착하는 그 순간, 첫 미스 순간이 아니다) → **체력 −1** |
| **`Attacker.Player`** (적 무방비 → 공격) | 적 피격 / `killOnSuccess`면 처치 | **적이 뒤로 물러나 회피**, 플레이어는 헛스윙. **피격 없음, 체력 감소 없음** |

- **적이 공격자일 때만 플레이어가 맞는다.** 플레이어가 공격자면 적은 애초에 휘두르지 않았다.
- 따라서 `hitClips`는 **`Attacker.Enemy` 실패 전용**이고, `Attacker.Player` 실패에는 재생되지 않는다.
- **`killOnSuccess` 실패 시 적은 죽지 않고 교전이 이어진다.** 같은 적이 계속 `currentOpponent`다. 처치는 나중 `killOnSuccess` 엔트리에서 재시도된다 — 곡 끝까지 안 죽어도 정상이며 Step 8-3이 정리한다.
- 적 리액션 클립 2종 필요: **KnockBack**(`Attacker.Enemy` 성공 — 패링당함) / **Evade**(`Attacker.Player` 실패 — 헛스윙을 피함).

---

# Step 2 — 굽기 툴: 새 필드 편집·보존

- [x] `ChartGen/Editor/PatternChartWindow.cs` — 엔트리 행에 `enemyCue` 편집 UI(접이식). 최소: attacker / killOnSuccess / projectile.
- [x] 재분석(re-bake)이 온셋만 갈아치우고 **cue를 날리지 않는지** 확인. 날린다면 엔트리 인덱스 기준으로 보존.
- [ ] 기존 SongChart 4종을 열었다 저장해도 값이 안 깨지는지 확인.

---

# Step 3 — EnemyDirector: 무대와 로스터

- [x] `Assets/02. Scripts/Enemy/EnemyDirector.cs` — `SliceTargetDirector`의 검증된 구조 승계
  - 예약/활성 리스트, 토큰 FIFO, 프리팹별 자체 풀 + prewarm, 기즈모
  - `EnqueueCue(EnemyCue)` — `ChartPlayer`가 `SetPattern` 직전에 호출
  - `OnPatternQueued`: cue dequeue → **현재 교전 상대**에게 공격 배정 (`impactTime = info.Deadline + cue.impactOffset`, 접근시간은 `impactTime − info.StartTime`으로 클램프)
  - `OnJudgeTargetFirstMiss` / `OnPatternComplete`: 성패 확정 (기존 `SetOutcome` 로직 그대로)
  - `OnAllPatternsCleared`: 전 적 회수
- [x] `ChartPlayer.Update`에 `enemyDirector?.EnqueueCue(next.enemyCue);` 한 줄 (SetPattern 직전).
- [ ] **이 Step의 완료 기준**: 적 프리팹을 임시 캡슐로 두고 **등장→공격 예고→임팩트→성패 반영**이 채보 타이밍과 맞는지 기즈모로 확인. 모션은 아직 없어도 된다.

## 3-1. 원형 링 로스터 + 현재 교전 상대

무대는 **원형 아레나**. 플레이어는 중앙, 적들은 외곽에 충분한 거리를 두고 랜덤 배치.

- [x] 배치 파라미터(전부 인스펙터): `ringRadius` / `radiusJitter`(±편차) / `minAngleGap`(겹침·뭉침 방지) / `ringCount`(유지할 적 수)
- [x] 디렉터가 **링 로스터**(살아 있는 적 목록) + **`currentOpponent`** 하나를 든다. 위치는 링 위 각도로 **런타임 생성**(`slotAnchors` 배열 없음).
- [x] 로스터 소스: `[SerializeField] EnemyDefinition[] rosterPool` — 여기서 골라 `ringCount`를 유지한다.
- [x] `cue.killOnSuccess` **성공** → 교전 종료 → 다음 상대 전환. **실패** → 적 생존, `currentOpponent` 유지(§1-2).
- [x] **링 → 대기석 승격은 링 각도 순.** 배치는 랜덤(다양성), 선택은 순서(연출 안정) — 플레이어 시점에서 아레나를 한 바퀴 훑는 그림이 된다. 완전 랜덤 선택은 좌우로 홱홱 꺾여 어지럽다.
- [ ] 교전 중이 아닌 적은 링에서 대기·위협 모션.

## 3-2. 대기타석(on-deck) — 숫자가 강제하는 구조

**전환 예산을 계산하면 "죽은 뒤 링에서 걸어온다"는 불가능하다.**

```
처치 임팩트       = Deadline_N = LastNode_N + 0.1
다음 첫 입력      ≥ LastNode_N + 0.4
다음 패턴 큐 투입  ≥ (LastNode_N + 0.4) − 0.5(exposureDuration) = LastNode_N − 0.1
```

1. **디렉터는 현재 적이 죽기 0.2초 전에 이미 다음 상대를 안다.** 선택 시점은 늦지 않다.
2. **처치 → 다음 첫 입력까지 최소 0.3초.** 그런데 다음 적의 공격 클립은 임팩트 정렬 때문에 그보다 더 일찍 시작해야 한다(`scheduleStart = impactAlign − playTime`). → **다음 적의 windup은 현재 적이 죽는 시점이나 그 이전에 이미 시작돼 있어야 한다.**

- [x] 앞쪽에 **2체**를 둔다: `currentOpponent` + `onDeck`. 나머지는 링에서 배회.
- [x] 처치 → `onDeck` **승격**(이미 앞에 있으므로 이동 0) → 링에서 새 `onDeck` 충원(다다음 차례라 몇 초 여유).
- [x] 결과: **전환 비용이 상수**. 채보가 아무리 촘촘해도 안 무너진다.
- [x] 대기 위치는 결투 앵커 옆 비스듬히(예: ±40°). 승격 이동은 0.3초에 충분한 거리다.
- [x] 무쌍 그림으로도 맞다 — 둘이 둘러싸고 하나씩 덤빈다.

## 3-3. 처치 연출과 전환은 분리한다

죽는 데 시간이 걸린다(사망 모션 + 절단 + 잔해 낙하). **그걸 기다리면 전환이 늦는다.**

- [x] 죽는 적은 그 자리에 버려두고 잔해는 알아서 굴러가게 둔다. 플레이어·카메라는 **즉시** 다음 상대로. 무쌍 감각의 핵심이 이거다 — 베고 뒤도 안 돌아본다.
- [x] `EnemyView.Kill()` 이후 그 개체는 **교전 로직에서 즉시 이탈**하고 수명 관리(잔해 회수)만 남는다.
- [x] `EnemyDirector.OnOpponentChanged(prev, next)` 이벤트 노출 — 카메라 재프레이밍·UI·SFX가 본체 수정 없이 붙는다.

> ⚠️ **히트스톱(`Time.timeScale`)은 이번 범위 금지.** 판정·임팩트 정렬·스폰 예약이 전부 `Time.time` 기반인데 음악(`AudioSource`)은 timeScale 영향을 안 받는다. 한 번 쓰면 그 시점부터 곡 끝까지 판정이 어긋나고, 쓸수록 누적된다. 하려면 정렬 전체를 `unscaledTime`으로 옮겨야 하므로 **별도 주제**다. 대신 카메라 쉐이크 + 이펙트 + **클립 자체에 임팩트 전후를 느리게 오서링**하는 방식으로 타격감을 낸다(이쪽은 timeScale을 안 건드려 안전).

## 3-4. 적 개체 수 — 등급을 나눈다

**부자연스러움의 원인은 풀링이 아니라 "화면 안에서 생성되는 것"이다.** 풀은 안 보인다. 플레이어가 보는 건 **입장**뿐이므로, 입장만 시야 밖에서 시키면 풀을 아무리 굴려도 안 들킨다.

- [x] **곡 도중 `Instantiate` 0회.** 곡 중 스키닝 메쉬 생성은 히치이고, 그 한 프레임이 판정을 먹는다. 문제는 "몇 체냐"가 아니라 **"언제 만드냐"**다.
- [x] 프리웜 시점 = **`ChartPlayer.countdownDuration`(3초) 구간**. 이미 있는 대기 시간이 그대로 프리웜 창이다.

| 등급 | 수 | 필요한 것 |
|---|---|---|
| **교전 참여자** (`currentOpponent` + `onDeck`) | 2 | 애니메이터 오버라이드, 임팩트 정렬, 결투 앵커 이동, 절단 프록시 |
| **링 대기** | 4~6 | idle/위협 루프 하나. **타이밍 계약 없음** |

- [x] 동시에 공격하는 적은 **언제나 1체**(판정 대상이 하나). 나머지는 순수 무대장치이므로 풀스펙 애니메이터가 필요 없다.
- [x] 링 대기 적: `Animator.cullingMode = CullCompletely` + **그림자 끄기**. 둘 다 공짜에 가깝다.
- [x] **출발값 `ringCount` = 6.** 링 6 + 교전 2 = 인스턴스 8, 풀 여유 포함 10. 곡 2분에 처치 30회가 나와도 재사용으로 충분하다. 최종 수치는 §3-6 기즈모로 잡는다(상한 둘: `minAngleGap`으로 원주에 들어가는 수 / 화각에 잡히는 수).
- [ ] 재사용 티 안 나게: `rosterPool`에 **모델 2~3종**(현재 `Humanoid_F`, `Humanoid_FeKatana` 보유) + 스케일 ±5% + 머티리얼 색조 편차 + **입장 지점 랜덤화**.

> 진짜 성능 예산은 적 수가 아니라 **절단 잔해 물리**(§7-1)와 그림자다. 적 수를 줄이기 전에 그쪽부터 잡는다.

## 3-5. 추가 스폰은 시야 반대편에 — 내적으로 고른다

- [x] 링 위 빈 각도 후보를 **시야 방향과의 내적으로 점수 매겨 가장 낮은 것**(= 가장 뒤쪽)을 고른다.

```csharp
// 기준은 카메라 forward — 화면에 뭐가 보이는지를 정하는 건 카메라다.
// 카메라 미배선이면 플레이어 forward로 폴백.
float score = Vector3.Dot((candidatePos - center).normalized, viewForward);
// score 최소 = 가장 확실한 등 뒤
```

- [x] **"뒤가 될 때까지 재시도"가 아니라 "가장 뒤인 곳을 고른다".** 빈 각도가 앞쪽밖에 없어도 실패하지 않고 최선을 고른다 — 무한 재시도·스폰 실패가 원천적으로 없다.
- [x] 등장은 링보다 더 바깥(`ringRadius × entryDistanceMul`)에서 시작해 걸어 들어온다. 링 위에 즉시 나타나면 등 뒤여도 여전히 팝인이다.
- [x] 각도 순 스윕과 궁합이 좋다 — 등 뒤 신규는 플레이어가 한 바퀴 돌아올 때쯤 순서가 오므로 도보 시간이 넉넉하다.

## 3-6. 링 기즈모 (반경을 눈으로 잡는다)

반경은 카메라 화각·격자 크기와 같이 봐야 정해진다. 숫자만 봐선 못 정하므로 씬 뷰에 그린다. `SliceTargetDirector`의 관례(`drawGizmos` 토글 + 색 필드)를 따른다 — **에디터 전용, 빌드 영향 없음.**

- [x] `ringRadius` 원 + `radiusJitter` 안팎 경계 원 2개(배치 가능 범위가 띠로 보인다)
- [x] `minAngleGap` 방사선 — 최소 간격을 각도로 표시
- [x] 결투 앵커 위치 + 중앙~앵커 선 + `maxOffset`(리시) 원
- [x] **편집 중에도 그린다**(플레이 없이 반경을 잡아야 하므로). 플레이 중에는 실제 적 위치와 `currentOpponent` 강조 추가.

---

# Step 4 — EnemyView / 결투 기하 / 정렬

- [x] `Assets/02. Scripts/Enemy/EnemyView.cs` — 적 하나의 뷰
  - 상태: `Enter(화면 밖→링) → RingIdle → Engage(링→결투앵커) → Windup → Impact → (KnockBack | Evade | Hit | Death)`. 전이는 **전부 시각 기반**(디렉터가 준 절대시각), 물리 없음 — `SliceTargetView`와 같은 규율.
  - `AssignAttack(ClipAlignment attack, float impactTime, Attacker attacker)` / `Resolve(success)` / `Kill()` / `Dissolve(duration)`
  - **공격 클립은 `Pattern.enemyAttack`에서 온다**(§1-1). 디렉터가 `OnPatternQueued`의 `info.Template`으로 이미 패턴을 받으므로 **배선이 따로 필요 없다.**
  - 공격 배정 시 클립의 **임팩트 프레임이 `impactTime`에 오도록** 시작 시각·배속 역산 — `CharacterActionPlayer.HandleJudgeTargetBegan` + `TryStartPendingSuccess`의 식을 차용(공통 계산은 `ClipAlignment`로 뺀다). 적은 복귀 3경로 같은 정교함이 필요 없으므로 **정렬만** 가져오고 복귀는 단순 CrossFade.
  - 결과: **적 칼이 지나가는 순간 = 플레이어 칼이 지나가는 순간 = 표적이 갈라지는 순간.** 세 정렬이 `Deadline` 식 하나로 묶인다.
- [ ] 적 애니메이터: `Humanoid_FeKatana`에 휴머노이드 리타깃
  - `05. Animations/Animator/EnemyAnimator.controller` 신규. `PlayerAnimator`의 축소판 — **공격 슬롯 1개**(placeholder + `AnimatorOverrideController` 런타임 주입) + idle + 리액션 + 사망
  - **적 인스턴스마다 `AnimatorOverrideController`를 따로 만든다**(`Awake`). 공유하면 동시에 공격하는 두 적이 서로의 클립을 덮어쓴다.
  - 공격 클립은 컨트롤러에 박지 않는다(패턴이 준다). 리액션 클립은 개체 공통이라 박는다.
- [x] `Attacker.Enemy` 실패 시 적은 공격을 **끝까지 휘두른다**(막히지 않았으므로).

## 4-1. 결투 앵커 — 접점을 공간으로도 고정한다

시간은 `Deadline` 식으로 이미 맞는다. 남은 건 **칼이 만나는 지점**이다. 적이 링 위 제각각인 자리에서 휘두르면 접점이 매번 달라져 패링이 허공에서 일어난다.

- [x] **기존 `impactAnchor`를 플레이어 정면으로 옮기고 플레이어의 자식으로 만든다.** 새 개념을 만들지 않는다 — 이 앵커가 곧 결투 지점이고, `SliceTargetDirector`(표적·투사체)도 같은 앵커를 쓰므로 전부 따라온다.
- [x] 공격을 배정받은 적은 windup 동안 **링 → 결투 앵커**로 이동한다.
- [x] 플레이어는 **루트만 상대 방향으로 회전**한다.
- [x] 결과: 두 클립은 **언제나 같은 상대 기하**에서 재생된다 → 접점 맞추기가 클립 오서링 하나로 줄어든다.
- [x] 해소 후 적은 자기 링 위치로 되돌아간다(KnockBack/Evade가 그 이동을 겸하면 자연스럽다).

## 4-2. 이동 규칙 — 전진/후퇴, 그리고 "패턴 중엔 안 움직인다"

- [x] **`Attacker.Player` = 조금 전진, `Attacker.Enemy` = 조금 후퇴.** 거리는 인스펙터(`stepForward` / `stepBackward`).
- [x] **이동 셋이 전부 `scheduleStart` 전에 끝난다** — 전진/후퇴 · 플레이어 회전 · 적의 링→결투앵커 접근. **패턴 진행 중에는 아무도 안 움직인다.**
  > **왜 필수인가**: 결투 앵커는 플레이어의 자식이다. 스윙 도중 플레이어가 전진하면 앵커가 따라 움직여 적이 겨냥하던 지점이 늦게 바뀐다 → 칼이 어긋난다. 패턴 중 기하를 얼려야 정렬이 성립한다.
- [x] **`scheduleStart`까지 못 끝내면 스텝을 생략한다.** 기존 `minRunExposure` 게이팅과 같은 모양 — 새 개념이 아니다.
- [x] **누적 표류 방지(리시)**: 공격이 연속으로 나오면 플레이어가 계속 전진해 링 쪽으로 걸어 나간다. 중앙 이탈이 `maxOffset`을 넘으면 그 스텝을 **생략**(위 규칙 재사용).
- [x] **교전 전환 시 중앙 복귀.** 다음 상대로 몸을 돌리는 구간에 같이 처리하면 자연스럽고, 표류가 교전 단위로 리셋된다.

## 4-3. 배속 클램프가 정렬을 깬다 (패링에서만 들통난다)

`CharacterActionPlayer`는 `speed = impactSpan / remaining`을 `maxAttackSpeed`(2.5)로 **클램프**한다. 클램프가 걸리면 임팩트 프레임이 제시각에 못 온다. 표적 절단은 조금 어긋나도 티가 안 나지만 **패링은 칼끼리 만나는 거라 즉시 보인다.**

- [ ] 패링 클립은 `impactSpan`을 **짧게 오서링**(임팩트까지 가는 구간을 앞으로 당김). 채보 최소 간격 0.4초 안에 2.5배속으로 담기면 클램프가 안 걸린다.
- [x] 클램프가 실제로 발동하면 **경고 로그**. 조용히 어긋나는 게 최악이다.

## 4-4. 적 공격을 못 막았을 때의 피격은 `impactTime`에 예약한다

지금 `CharacterActionPlayer`는 `OnJudgeTargetFirstMiss` 순간 즉시 Hit을 재생한다. 그러면 **적 칼이 도착하기도 전에 플레이어가 맞는다.**

- [x] 첫 미스에서 성공 클립 취소는 지금처럼 **즉시**, Hit 재생만 `impactTime`에 **예약**한다. 기존 `hasPending`/`pendingScheduleStart` 메커니즘 재사용 — 새 구조 없음.
- [x] `Attacker.Player`(적 무방비)면 **Hit을 아예 재생하지 않는다**(§1-2).
- [x] `CameraDirector`의 피격 큐도 같은 시각으로 옮긴다 — 지금은 `OnJudgeTargetFirstMiss` 즉시 발행이라 화면 흔들림이 피격보다 먼저 온다.

---

# Step 5 — attacker를 `CharacterActionPlayer`에 알린다

`JudgeTargetInfo`에는 attacker가 없다. 디렉터가 **밀어 넣으면**(push) FIFO 큐가 둘이 되고(cue 큐 + response 큐), 두 큐가 어긋나는 순간을 디버깅하게 된다. **당겨 온다**(pull) — 큐는 하나뿐이다.

```csharp
// EnemyDirector — 지금 판정 대상 패턴에서 누가 휘두르는가. 예약이 없으면 Player(적 무방비)로 본다.
public Attacker CurrentAttacker =>
    pendingTokens.Count > 0 && TryFind(pendingTokens.Peek(), out var r) ? r.attacker : Attacker.Player;

// CharacterActionPlayer.HandleJudgeTargetBegan
var attacker = enemyDirector != null ? enemyDirector.CurrentAttacker : Attacker.Player;
var alignment = attacker == Attacker.Enemy ? info.Template.PlayerParry : info.Template.PlayerAttack;
```

**왜 시각이 정확히 맞는가** — `pendingTokens`의 선두는 언제나 현재 판정 대상이다:

| 경로 | `PatternHandler` 발행 순서 | 그 시점의 선두 토큰 |
|---|---|---|
| 최초 투입 | `OnPatternQueued`(315행) → `OnJudgeTargetBegan`(321행) | 방금 발급된 토큰 = 이 패턴 |
| 승계 | `OnPatternComplete`(618행, 디렉터가 dequeue) → `RaiseJudgeTargetBegan`(629행) | 앞 토큰이 빠진 뒤 = 다음 패턴 |

두 경로 다 디렉터가 **먼저** 손을 대고 그다음 `OnJudgeTargetBegan`이 뜬다. 별도 동기화 불필요.

- [x] `EnemyDirector.CurrentAttacker` 공개 프로퍼티 추가
- [x] `CharacterActionPlayer`에 `enemyDirector` 참조 + 클립 선택 분기. **정렬·배속·복귀 3경로는 한 줄도 안 바뀐다.**
- [x] 폴백: 디렉터 미배선/cue 없음(디버그 경로) → `Attacker.Player`. `ClipAlignment`가 비면 무연출. 어디서도 안 죽는다.
- [x] `EffectCatalog.EffectTrigger`에 `Parried` / `EnemyEvaded` / `EnemyKilled` / `ProjectileSliced` 추가 — 카탈로그 한 줄 원칙 유지
- [x] `CameraCueCatalog.CameraTrigger`에 `EnemyKilled` 추가. 기존 트리거는 그대로.

---

# Step 6 — 원거리 오브젝트 (절단/패링 대상)

- [x] `SliceTargetDirector`를 **그대로 살린다.** 투사체 = 지금의 표적과 완전히 같은 사건(날아와서 Deadline에 갈라지거나 부딪힌다).
- [x] 참조 소스만 `Pattern.SliceTarget` → `EnemyCue.projectile`로 옮긴다:
  - `EnemyDirector`가 cue를 읽고 `SliceTargetDirector.Reserve(set, impactTime, startTime, offset, spawnPos)`로 예약을 넘긴다.
  - `SliceTargetDirector`의 `OnPatternQueued` 직접 구독 제거 → **예약 소스가 한 곳(EnemyDirector)**이 된다. 성패 확정 구독은 토큰과 함께 유지.
- [x] 발사 위치는 `spawnAnchor` 대신 **쏘는 적의 링 위치**.
- [x] 투사체 조각도 절단 이후엔 Rigidbody로 넘어간다 — §7-1이 적·투사체 공통 규율이다.

---

# Step 7 — 적 처치 시 모델 절단

**제약**: `MeshSliceBakerWindow`는 `MeshFilter` 전용이라 스키닝된 적을 못 자른다(Research §3.1).

- [x] `MeshSliceBakerWindow`에 **`SkinnedMeshRenderer.BakeMesh` 입력 경로 추가** — 대상이 스키닝 메쉬면 지정한 **사망 포즈 프레임**에서 `BakeMesh`로 정적 메쉬를 떠서 자른다. 기존 `MeshFilter` 경로는 그대로.
- [x] 런타임: `EnemyView.Kill()`이 스키닝 적을 감추고 **같은 포즈의 절단 프록시**(`EnemyDefinition.deathSliceSet`)를 스왑해 갈라뜨린다.
- [x] **알려진 한계**: 프록시는 굽기 시점의 **단일 포즈**다. 사망 순간 실제 포즈와 어긋나면 스왑 프레임에 튄다 → 사망 모션의 특정 프레임에서 반드시 `Kill()`이 불리도록 시각을 고정하고, 그 프레임으로 굽는다. (`// ponytail:` 주석으로 한계와 업그레이드 경로(런타임 BakeMesh + 즉석 절단) 명시)

## 7-1. 조각은 전부 물리로 간다 (적·투사체 공통)

**경계는 "접근이냐 절단 이후냐"다.** 접근 구간은 `impactTime`에 **정확히** 도착해야 하므로 닫힌 식·트랜스폼 이동을 유지한다. **절단된 순간부터는 맞출 시각이 없다** → 적 조각이든 투사체 조각이든 전부 Rigidbody. 규율이 하나라 코드도 갈리지 않는다.

기존 흩뿌림이 어색한 이유: 조각이 뷰의 **자식**이라 −Z 접근 속도를 구조적으로 승계하고 로컬 XY로만 흩어진다. 바닥을 통과하고, 절대 멈추지 않는다.

- [x] **절단 프레임에 조각을 부모에서 뗀다.** Rigidbody와 부모 트랜스폼이 같이 움직이면 서로 싸운다.
- [x] **초기 속도 = 접근 속도 + 흩뿌림 임펄스.** 표적은 `(impactPos − spawnPos) / (impactTime − spawnTime)`로 접근 속도를 이미 안다. 이걸 `rb.velocity` 출발값으로 주면 "날아오던 게 갈라져 계속 날아간다"는 지금 그림이 유지되고 거기에 중력·바닥이 붙는다. **서 있는 적은 이 항이 0이라 그 자리에서 무너진다 — 같은 식이 양쪽을 다 설명한다.**
- [x] 조각 프리팹에 `Rigidbody` + **convex** `MeshCollider`. 절단 조각은 대개 비볼록이라 convex 근사가 되지만 잔해라 티가 안 난다.
- [x] 임펄스는 갈라진 면 법선 쪽으로 살짝 + 약한 랜덤 토크. 사방으로 터뜨리지 않는다.
- [x] **조각끼리 충돌은 끈다.** 전용 레이어로 바닥하고만 부딪히게 — 계산이 크게 줄고 조각 더미 지터도 사라진다.
- [x] 풀 반납 시 `velocity`/`angularVelocity` **리셋 필수**(`SlicePiece.ResetState`) — 안 하면 다음 절단에서 조각이 튀어나간다.
- [x] `Rigidbody.IsSleeping()` 감지 → 페이드아웃 후 회수. `maxActivePieces` 예산 유지.
- [x] `SlicePiece`의 닫힌 식 운동(`Scatter` + `Update`)은 **대체된다** — 남기지 않는다. `scatterGravity`/`scatterSpin` 튜닝 값은 물리 파라미터로 옮긴다.
- [x] 바닥 콜라이더가 씬에 필요하다(현재 없음) → Step 9.

---

# Step 8 — 플레이어 체력 · 곡 종료 / 실패

## 8-1. 체력

- [x] `Assets/02. Scripts/PlayerHealth.cs` — **단순 카운터**. `maxHealth`, `Current`, `OnDamaged(int remaining)`, `OnDepleted`. HP 바·수치 밸런싱 없음, 리듬게임의 목숨 카운트다.
- [x] 감소 시점 = **`impactTime`에 실제로 맞는 그 순간**(§4-4의 예약된 Hit과 같은 시각). 첫 미스 순간이 아니다.
- [x] **`Attacker.Enemy` 실패에서만** 감소(§1-2).
- [x] 0 도달 → 곡 클리어 실패.

## 8-2. 실패 처리

- [x] `ChartPlayer.Stop()` → 오디오 정지 + `PatternHandler.ClearAllPatterns()`
  - `ClearAllPatterns`가 `OnAllPatternsCleared`를 쏘고 디렉터들이 이미 전량 회수한다 — **회수 경로를 새로 만들지 않는다.**
- [x] 남은 적은 §8-3 소멸 연출로 정리한 뒤 스테이지 종료.

## 8-3. 곡 종료 — 남은 적 소멸

`killOnSuccess` 실패가 쌓이면 **적이 안 죽어 로스터가 표류한다**(잔여 턴·잔여 적). 버그가 아니라 사양이며, 곡 끝에서 정리한다.

- [x] `ChartPlayer`에 `OnSongEnded` 이벤트 추가. **현재 종료 감지가 없다** — `pendingEntries`가 비고 `audioSource.isPlaying`이 false가 되는 지점.
- [x] 곡 종료(또는 체력 소진) 시 살아 있는 적 전원을 **머티리얼 소멸(dissolve)**로 없앤다. **절단하지 않는다** — 베지 않았으니 갈라지면 안 된다.
- [x] 셰이더: **`Assets/Shaders/Dissolve/Dissolve.shadergraph`** (Particle Pack에서 추출·보존. 의존 텍스처 `Clouds02.png` / `HexagonPattern_Albedo.tif`가 같은 폴더. 나머지 ParticlePack은 전량 삭제됨)
- [ ] 적 머티리얼(`Humanoid_Body_*` 4종)을 이 셰이더 기반으로 만들고, 소멸 시 dissolve 프로퍼티를 시간에 따라 밀어 올린다. 노출 프로퍼티 이름은 구현 시 확인.
- [x] `EnemyView.Dissolve(duration)`는 **`MaterialPropertyBlock`으로 인스턴스별 진행** — 머티리얼을 공유하면 적 전원이 같이 사라진다.
- [x] 소멸 완료 후 스테이지 종료 신호.

---

# Step 9 — 씬 구성 / 마이그레이션

- [x] `DefaultScene`: `EnemyDirector` 오브젝트 신설. `SliceTargetDirector`는 투사체 전용으로 유지·재배선.
- [ ] **링 반경 실측** — §3-6 기즈모를 보며 `ringRadius`/`radiusJitter`/`ringCount`를 화각·격자와 함께 잡는다. 코드가 아니라 값 문제.
- [x] `impactAnchor`를 플레이어 자식으로 재배치(§4-1).
- [x] 바닥 콜라이더 배치(§7-1).
- [ ] 카메라 구도 점검 — 링 위 적이 화각에 들어오는지. 필요 시 `CameraCenter`/FOV 조정(연출 값만). 회전 추종 필요 여부도 여기서 판단.
- [ ] 기존 SongChart 4종이 `enemyCue` 없이 무연출로 재생되는지 확인 → 그중 1곡을 적 전투로 재저작해 레퍼런스 채보로 삼는다.
- [ ] `Pattern`의 `sliceTarget*` 3필드 정리(전 채보가 cue로 넘어간 뒤에만).
- [ ] 기존 `SuccessAnimationClip` + 트림 4필드 → `playerAttack` 이관. 손으로 옮기지 말고 **에디터 일회성 마이그레이션 코드**로 복사(값이 이미 오서링돼 있다). 이관 후 구 필드 제거.

---

# Step 10 — 검증

- [x] 유닛테스트(`Enemy/Tests`, asmdef) — **순수 로직만**, MonoBehaviour·씬 의존 없이 분리:
  - cue FIFO 매칭 / 링 배치(최소 각 간격 보장) / 다음 상대 선택 순서 / `killOnSuccess` 성공·실패 분기 / 접근시간 클램프 / 스텝 생략 규칙(시간 부족·리시 초과) / 스폰 각도 선택(내적 최소)
- [ ] 플레이 검증 체크리스트

**타이밍**
  - 패턴 겹침 구간(동시 2패턴)에서 각자 올바른 시각에 임팩트하는가
  - 마지막 노드 Good(늦은 입력) 성공이 실패 연출로 새지 않는가
  - 전진/후퇴가 패턴 진행 중에 일어나지 않는가(기하 고정)
  - 교전 전환이 다음 패턴 시작 전에 끝나는가

**상태**
  - `killOnSuccess` 실패 후 같은 적과 교전이 이어지고, 다음 `killOnSuccess`에서 정상 처치되는가
  - 체력이 `Attacker.Enemy` 실패에서만 줄고 `Attacker.Player` 실패에선 안 줄어드는가
  - 첫 미스 이후 남은 노드를 계속 입력해도 상태가 꼬이지 않는가
  - 공격이 연속으로 나와도 플레이어가 중앙에서 멀리 밀려나지 않는가(리시)

**연출·성능**
  - 곡 도중 `Instantiate`가 한 번도 안 일어나는가(프로파일러)
  - 추가 스폰이 화면 안에서 튀어나오지 않는가
  - 곡 끝에 남은 적이 전부 소멸로 정리되는가(절단이 아니라 소멸)
  - 곡 중단(`Stop`) 시 적·투사체·조각이 전부 회수되는가
  - 기존 채보(cue 없음)가 에러 없이 재생되는가

---

## 구현 순서 메모

- **Step 1 → 3 → 4가 임계 경로.** Step 3 끝에서 "캡슐이 채보에 맞춰 다가와 성패가 반영되는" 상태가 나오고, 여기서 타이밍이 맞으면 나머지는 살 붙이기다.
- **Step 2(굽기 툴)는 Step 1 직후 아무 때나.** 안 하면 채보를 손으로 못 짜므로 Step 9 전에는 끝나야 한다.
- **Step 6·7·8은 서로 독립.** 병렬 가능.
- **Step 5는 Step 3·4 이후.** 디렉터와 클립 세 벌이 있어야 의미가 있다.

---

## 리스크 / 미결정

| 항목 | 내용 |
|---|---|
| **화면 공간** | 3x3 격자가 1400x1400px로 화면 중앙을 먹는다. 링 반경·화각은 **씬에서 눈으로** 잡아야 한다. Step 9. |
| **카메라 회전 추종** | 플레이어가 사방으로 돌면 어지럽다. 카메라 yaw가 현재 상대 쪽으로 damped 추종하면 적이 늘 화면 앞에 온다. 상대는 판정보다 0.5초+ 먼저 확정되므로 미리 돌 시간이 있고, 카메라는 판정에 개입하지 않아 어긋나도 안전하다. Step 9에서 판단. |
| **로코모션 클립 의미 변화** | base `Running Layer`의 Run/Sprint는 **전진 달리기**다. 아레나에서는 전투 스탠스/제자리 스텝이어야 한다. `SwitchBaseState` 구조는 두고 **클립만 교체**. |
| **적 idle·위협 모션 부재** | 대기 클립이 없다. 초기엔 `Run` 정지 포즈로 버티고, 어색하면 클립 확보. |
| **물리 예산** | 적 사망 + 투사체 절단 **양쪽**이 물리다. 조각끼리 충돌을 끄고 바닥만 남기면 대부분 해결되지만, 밀도가 올라가면 `maxActivePieces` 실측 필요. |
| **절단 결정론 상실** | 지금은 seed 난수로 굽기 결과가 같으면 런타임 결과도 같았다. 물리로 가면 재현성이 사라진다. 잔해 연출이라 문제없다고 보지만, 절단 코어 유닛테스트가 이 성질에 의존하는지 Step 10에서 확인. |
| **사망 절단 포즈 튐** | Step 7의 알려진 한계. 프로토타입에서 거슬리면 절단 대신 래그돌/VFX로 후퇴하는 폴백을 남긴다. |
| **로스터 표류** | `killOnSuccess` 실패가 쌓이면 잔여 턴·잔여 적이 남는다. **사양이다** — Step 8-3이 곡 끝에 정리한다. 다만 실패가 많은 플레이에선 링이 붐빌 수 있어 `ringCount` 실측 필요. |
| **dissolve 머티리얼** | 셰이더는 확보됨. 적 머티리얼 4종을 이 셰이더로 다시 만드는 작업이 남았다. 셀 셰이더(`CelShader`)를 쓰면 룩이 바뀔 수 있으니 소멸 순간에만 교체할지 판단 필요. |
| **cue 없는 구간** | 채보에 cue가 비어도 디렉터는 `currentOpponent`를 들고 있다. `Attacker.Player` 폴백이라 "무방비 적을 벤다"로 재생된다 — 기존 채보 호환 경로이기도 하다. |
| **히트스톱** | `Time.timeScale`은 판정·정렬 전체를 깬다. 이번 범위 금지, `unscaledTime` 전환이 선행(§3-3). |
| **보스** | 이번 범위 아님. `killOnSuccess`를 늦게 주면 "여러 패턴을 버티는 적"이 그대로 보스다 — 코드 추가 없이 채보로 확장. |
| **점수/HP UI** | 이번 범위 아님. |

---

## `>>>` 피드백란

여기 또는 각 Step 아래에 `>>>`로 남겨 주세요.
