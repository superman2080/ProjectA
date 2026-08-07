# Plan — 패턴별 월드 이펙트 (PatternEffect)

근거: `docs/PatternEffect/Research_PatternEffect.md`

## 목표
패턴 에셋이 **자기 이펙트들을 리스트로 소유**하고, 그것이 **월드 공간**에서 **패턴 진행 중 임의의 시점·임의의 부착 지점에** 재생되며,
**전용 에디터 창**에서 그 목록·배치·타이밍을 편집하고 저장한다.

---

## 설계 결정 (피드백 대상 — `>>>`로 반박해 주세요)

### D0. 구조는 **고정 슬롯이 아니라 큐 리스트**다
`Pattern`이 `List<PatternEffectCue>` **하나**를 갖는다. 큐 하나가 "**언제 · 어디에 · 어떤 조건에서** 무엇을 재생할지"를 스스로 들고 있다.

```csharp
[Serializable]
public class PatternEffectCue
{
    public string label;             // 편집 편의 — 툴 목록에 뜨는 이름
    public GameObject prefab;        // 비면 무연출

    public EffectCondition condition;// Always / Success / Parry / Evade
    public EffectTiming timing;      // PatternStart / FirstNode / Node / LastNode / Impact
    public int nodeIndex;            // timing == Node일 때 패턴 내 순번(0-based)
    public float timeOffset;         // ±초

    public EffectAnchor anchor;      // ImpactAnchor / Player / PlayerWeapon / Opponent
    public bool follow;              // true면 앵커의 자식으로 붙어 따라간다
    public Vector3 positionOffset;   // 앵커 로컬
    public Vector3 rotationOffset;
    [Min(0.01f)] public float scale = 1f;   // 크기 배율
    [Min(0.01f)] public float speed = 1f;   // 파티클 재생 배속(1 = 프리팹 그대로)
    [Min(0)]     public int poolSize = 2;
}
```

- **하한은 타입으로 못박는다**(`countdownDuration`의 `[Min(3f)]`과 같은 규율). `scale`/`speed`가 0이면 **보이지 않거나 영원히 안 끝나는 이펙트**가 되어 무연출과 구분이 안 되고, 음수는 뒤집힌 파티클이라 저작 실수 외에 쓸 일이 없다. 0은 **런타임 정지(D7)의 값**이지 저장값이 아니다.

- **`speed`는 `ParticleSystem.MainModule.simulationSpeed`에 실린다** — 하위 시스템 전부에 같은 값을 건다. 프리팹 하나를 여러 패턴이 공유하면서 "빠른 베기엔 빠른 스파크"를 만들 수 있다. `scale`(크기)과 축이 다르므로 둘 다 둔다.
- ⚠ **배속은 지속시간을 함께 줄인다**(수명·방출이 같이 빨라진다). 회수는 파티클 전멸 감지라 자동으로 따라온다(`CanvasEffectView.LifeRoutine`).
- ⚠ `speed = 0`이면 파티클이 **얼어붙는다**. 히트스톱이 이 값을 쓴다(D7).

- **왜 슬롯이 아니라 리스트인가**: 한 패턴에 이펙트가 몇 개 붙을지, 어느 시점에 붙을지는 **만들면서 정해진다**. 고정 슬롯은 "성공에 하나, 패링에 하나"라는 개수를 코드가 미리 정해 버려 "칼 뽑을 때 하나 + 칼날에 하나 + 맞부딪힐 때 하나"를 표현할 수 없다.
- **조건은 슬롯 이름이 아니라 큐의 필드**가 된다 — 아래 D1이 그 값의 의미를 정한다.
- ⚠ **`ClipAlignment` 슬롯들과는 성질이 다르다.** 클립은 배우당 한 번에 하나만 재생되므로 슬롯이 맞고, 이펙트는 동시에 여럿 뜰 수 있으므로 리스트가 맞다.

>>>

### D1. 큐의 `condition`은 **적의 반응 단위**다 — `Success` / `Parry` / `Evade` / `Always`
**이펙트는 판정 결과가 아니라 적이 재생하는 반응 클립을 따라간다.** 막는 애니메이션이면 스파크가 튀고, 뒷구르기면 아무것도 안 튀는 게 맞다 — 칼이 만났느냐 아니냐가 화면에 남는 사실이기 때문이다.

`EnemyView.Resolve()`가 고르는 반응은 셋뿐이므로(`parryStateName` / `evadeStateName` / `knockBackStateName`) 슬롯도 그 수를 넘지 않는다.

| `attacker` | 성패 | 적 반응 클립 | 화면 사실 | `condition` |
|---|---|---|---|---|
| Player | 성공 | (죽거나 Idle) | 벤다 | `Success` |
| Player | 실패·창 짧음 | `parryStateName` | **칼이 맞부딪힌다** | `Parry` (스파크) |
| Player | 실패·창 넉넉 | `evadeStateName` | 적이 물러난다, 칼 안 닿음 | `Evade` (**보통 큐를 아예 안 만든다**) |
| Enemy | 성공 | `knockBackStateName` | 플레이어가 받아쳐 적이 밀린다 | `Success` |
| Enemy | 실패 | `evadeStateName` | 플레이어가 맞는다 | `Evade` |

- **네 값이 다섯 경우를 덮는 이유**: `attacker`가 **패턴의 성질**이라(한 패턴은 한 역할만 갖는다) 같은 값이 역할에 따라 다른 의미를 가져도 한 에셋 안에서는 절대 섞이지 않는다. `Attacker.Player` 패턴의 `Success`는 베는 이펙트, `Attacker.Enemy` 패턴의 그것은 받아치는 스파크다.
- **패턴별이어야 하는 이유**: 막는 그림은 **어느 궤적을 막았는가**에 따라 다르다(가로베기를 막은 불꽃과 내려베기를 막은 불꽃은 위치·방향이 다르다). 궤적은 패턴이 소유하므로 큐도 패턴에 산다.
- **`Always`는 성패와 무관한 큐**다 — 패턴 시작의 기 모으기, 칼날 잔광처럼 **결과를 알기 전에 떠야 하는 것**이 여기 온다. D3의 시각 제약과 짝이다.
- "뒷구르기는 이펙트 없음"은 **`Evade` 큐를 안 만드는 것**이지 코드 분기가 아니다.

>>>

### D2. 부착 지점은 **앵커 enum 넷 + 따라가기 토글**
`anchor`가 기준 Transform을 고르고, `positionOffset`/`rotationOffset`/`scale`이 그 **로컬 기준**으로 실린다.

| `anchor` | 기준 | 쓰임 |
|---|---|---|
| `ImpactAnchor` | `SliceTargetDirector`·`EnemyDirector`가 공유하는 그 앵커 | 맞부딪힘·절단 스파크 |
| `Player` | 플레이어 루트 | 발밑 기 모으기, 돌진 잔상 |
| `PlayerWeapon` | 칼날 노드 | 칼에 붙어 궤적을 따라가는 것 |
| `Opponent` | `EnemyDirector.CurrentOpponent`의 Transform | 적 몸에서 터지는 것 |

- **`follow`가 부모 관계를 정한다.** true면 앵커의 **자식으로 붙어 따라가고**(칼날 잔광), false면 발사 순간의 위치·회전만 복사한 채 월드에 남는다(스파크). 회수할 때 부모를 반드시 풀 루트로 되돌린다 — 안 그러면 앵커가 죽을 때 풀 인스턴스가 같이 파괴된다.
- ⚠ **`ImpactAnchor`는 월드 고정점이 아니라 플레이어의 자식이다**(BattleScene 기준 로컬 `(0, 0, 1)` — **발밑 높이 y=0**, 1m 앞). `EnemyDirector.duelAnchor`도 같은 오브젝트를 가리킨다. 플레이어를 따라 움직이고 같이 회전하므로 오프셋은 **플레이어 기준**으로 읽어야 하고, **툴 프리뷰도 이 앵커를 플레이어에 붙여 그린다**(허공에 띄우면 씬에서는 발밑에 뜨는 이펙트가 프리뷰에서만 가슴 높이로 보인다).
- ⚠ **`Opponent`는 큐 시점에 못 물어본다**(§11-1 — 상대는 판정 대상이 되는 순간에 배정된다). **발사 순간에** `CurrentOpponent`를 읽는다. 그 시점엔 언제나 확정돼 있다.
- ⚠ **`PlayerWeapon` 배선 주의**: 칼날은 같은 이름 노드가 2단이고 실제 노드는 안쪽이다(`root/add_weapon_r/Weapon_Katana_01_Blade/Weapon_Katana_01_Blade` — `WeaponTrailController`가 겪은 그 함정). **디렉터 인스펙터에 Transform으로 직접 배선**하고 경로 문자열을 코드에 넣지 않는다.
- **앵커가 비면 그 큐만 조용히 빠진다**(배선 누락 시 조용히 비활성 — 기존 규율).

#### D2-1. 칼날은 점이 아니라 **선분**이다 — `bladeT`로 그 위를 집는다
`anchor == PlayerWeapon`일 때만 쓰는 필드 둘을 큐에 더한다:

```csharp
[Range(0f, 1f)] public float bladeT;   // 0 = 손잡이 쪽 끝, 1 = 칼끝
public bool alignToBlade;              // true면 회전을 칼날 축 + 진행 방향에 맞춘다
```

- **왜 `positionOffset`으로 충분하지 않은가**: 칼끝에 붙이려면 칼날 길이를 손으로 재서 오프셋에 적어야 하고, 무기를 바꾸면 모든 큐가 조용히 어긋난다. `bladeT`는 **길이에 무관한 비율**이라 무기가 바뀌어도 칼끝은 칼끝이다.
- **칼날 축은 추측하지 않고 유도한다** — `MeshSliceBakerWindow.SampleWeapon`이 이미 쓰는 방법 그대로: 칼 렌더러의 `localBounds` **최장 축**이 날 길이다. 그 코드가 진실의 원천이고 여기서 같은 규칙을 쓴다(로컬 축이 x인지 y인지 프리팹마다 다르므로 하드코딩하면 조용히 틀린다).
- **계산은 스폰 때 한 번뿐이다.** 칼날 노드의 자식으로 붙이고 `localPosition = axisLocal × extent × (2·bladeT − 1) + positionOffset`을 한 번 대입하면, 이후 궤적 추종은 **부모 관계가 공짜로 해 준다**(매 프레임 계산 없음).
- **`alignToBlade`**: 회전을 `LookRotation(진행 방향, 칼날 축)`으로 잡는다. 스파크·잔상이 칼이 지나가는 쪽을 향한다. 진행 방향은 직전 프레임 위치와의 차이로 얻는다(정지 프레임이면 이전 값 유지 — `DeriveBladePlane`이 정지 프레임에 세로 궤적을 가정하는 것과 같은 처리).
- **칼날 전체에 깔고 싶으면 큐를 복제해 `bladeT`만 바꾼다**(툴에 복제 버튼이 있다). 코드가 개수를 정하지 않는다.
- ⚠ `bladeT`는 `PlayerWeapon` 전용이다. 다른 앵커에서는 인스펙터에서 감춘다(툴도 같이).
- ⚠ **패턴인풋 노드(Point) 자리는 이 앵커 목록에 없다.** 루트 Canvas가 `ScreenSpaceOverlay`라 그 좌표는 월드가 아니다(§7-5). 노드 자리 이펙트는 **기존 `EffectManager`의 관할**이며(`OnFocusRingResolved`·`OnNodeConnected`가 이미 그 자리에 띄운다) 이 시스템이 침범하지 않는다.

>>>

### D3. 시각은 **패턴 진행상의 기준점 + 오프셋**
`timing`이 기준점을 고르고 `timeOffset`(±초)이 민다. 기준점은 전부 `PatternQueuedInfo`에서 이미 오거나(아래 Step 2에서 한 필드만 추가) 기존 식으로 계산된다 — **새 시계를 만들지 않는다.**

| `timing` | 시각 | 출처 |
|---|---|---|
| `PatternStart` | 큐 투입(= 링이 뜨기 시작) | `PatternQueuedInfo.StartTime` |
| `FirstNode` | 첫 노드 입력 예정 | `PatternQueuedInfo.FirstNodeTime` |
| `Node` | `nodeIndex`번째 노드 입력 예정 | `PatternQueuedInfo.NodeTimes[nodeIndex]` (Step 2에서 추가) |
| `LastNode` | 마지막 노드 입력 예정 | `PatternQueuedInfo.LastNodeTime` |
| `Impact` | 칼이 닿는 순간 | `Deadline + Pattern.ImpactOffset` — §6·§11과 **같은 식** |

- **모든 시각이 큐 투입 시점에 계산된다.** 그래서 디렉터는 이벤트를 여러 개 구독할 필요 없이 **정렬된 예약 목록 하나**만 돌린다.
- `Pattern.ImpactOffset`은 **건드리지 않는다**. 그건 다섯 소비자가 공유하는 공통 앵커고 `timeOffset`은 큐 하나만의 보정이다. 섞으면 이펙트를 당기려다 칼·적·카메라가 같이 밀린다.
- ⚠ **조건이 결과인 큐는 결과가 나온 뒤에만 뜰 수 있다.** 성패는 마지막 노드 입력에서 정해지므로 `Success`/`Parry`/`Evade` 큐의 시각이 그보다 이르면 **미래를 앞당겨 보여 주는 셈**이다. 런타임은 그런 큐를 조용히 건너뛰고, **툴이 저장 전에 빨간 경고**로 잡는다(`PatternStart`에 `Success`를 걸면 즉시 보인다).
- ⚠ **입력 시각은 '예정'이지 '실제'가 아니다.** `NodeTimes`는 채보가 정한 기대 시각이며 플레이어가 늦게 눌러도 안 밀린다. 입력 순간에 정확히 붙어야 하는 이펙트는 이미 `EffectManager`가 판정 이벤트로 처리하고 있다(D2 마지막 항목과 같은 경계).

>>>

### D4. 재생 소유자는 **새 `PatternEffectDirector`** 하나
`EffectManager`에 얹지 않는다 — 그쪽은 Canvas 좌표계·`UIParticle`·`EffectTrigger` 키가 전제다.

골격은 `HitStopDirector`의 결(순수 연출, 판정에 개입 안 함, 배선 없으면 조용히 비활성)을 따르되 **예약 구조는 다르다**:

| | `HitStopDirector` | `PatternEffectDirector` |
|---|---|---|
| 구독 | `OnPatternComplete` | **`OnPatternQueued`**(예약) + `OnPatternComplete`·`OnEnemyReacted`(결과) |
| 예약 | 최대 **하나** | 패턴당 **큐 수만큼**, 여러 패턴이 동시에 |
| 시각 | 임팩트 하나 | 큐마다 다름(D3 다섯 기준점 ± 오프셋) |

- **"최대 하나"를 못 쓰는 이유**: 그 근거는 시각이 임팩트로 고정이라 완료가 최소 0.4초 간격이라는 것인데, 여기선 큐가 `PatternStart`까지 앞당겨질 수 있어 **앞 패턴의 임팩트 큐와 다음 패턴의 시작 큐가 겹친다**(리드타임 0.5초 > 간격 0.4초, §3). 예약은 리스트다.
- **예약과 결과가 갈린 이유**: 시각은 큐 투입 때 전부 확정되지만(D3) 조건은 마지막 노드에서야 정해진다. 그래서 **예약 시점과 조건 확정 시점이 구조적으로 다르고**, 토큰으로 이어 붙인다(§3 — 같은 템플릿이 겹칠 수 있다).
- 취소는 그대로 `OnAllPatternsCleared` 하나.

>>>

### D5. `SliceTargetDirector`의 `sliceEffectPrefab`/`crushEffectPrefab`은 **이번에 안 건드린다**
풀링 없는 `Instantiate` 부채인 건 맞지만, 요구사항 밖이다. 새 디렉터의 풀이 검증되면 그때 옮긴다.

>>>

### D6. 어떤 반응이 나왔는지는 **`PatternHandler` 이벤트로 못 잡는다** → `EnemyDirector`에 이벤트 하나 추가
```csharp
public enum EnemyReaction { Parry, Evade }   // 성공 경로는 OnPatternComplete가 이미 덮는다

/// 실패로 적이 반응 클립을 재생하는 순간(확정). 어떤 클립인지와 칼이 만나는 시각을 함께 준다.
public event Action<Pattern, EnemyReaction, float> OnEnemyReacted;
```
- 발행처는 `ResolveReservation` 안, `opponent.Resolve(...)`를 부르는 자리 **하나**. 반응 판정은 **이미 계산된 `retreat` 값을 그대로** 재사용한다 — `EnemyView.Resolve`가 `parried`를 정하는 식과 **같은 값**을 봐야 애니메이션과 이펙트가 어긋날 수 없다.
- **`EnemyView`가 아니라 `EnemyDirector`가 발행하는 이유**: 뷰는 패턴도 임팩트 시각도 모른다(창을 모르는 것과 같은 이유). 둘 다 아는 곳은 예약을 든 디렉터뿐이다.
- **왜 `OnPatternComplete`로는 안 되나**: 그 페이로드에는 "막았는지 굴렀는지"가 없다. 그건 다음 패턴의 창을 보고 `EnemyDirector`가 정하며(§Research), 실패를 한 덩어리로 보면 **적이 뒤로 구르는 동안 허공에서 스파크가 튄다**.
- **왜 확정 시각이 아니라 임팩트 시각을 같이 주나**: 확정은 마지막 노드 입력 순간이고 칼이 만나는 건 그보다 `goodWindow + ImpactOffset` 뒤다. `OnEnemyKilled`(확정) / `OnEnemyBurst`(임팩트)가 갈려 있는 것과 같은 이유 — 시각을 같이 실어 보내면 디렉터가 예약만 하면 된다.
- ⚠ 이 이벤트가 생기면 Canvas 쪽 죽은 값 `EffectTrigger.Parried`도 나중에 배선할 수 있다. **이번엔 안 한다**(요구는 월드 이펙트다).

>>>

### D7. 히트스톱 창 동안 **이펙트도 멈춘다**
`HitStopDirector.Fire()`가 배우·카메라에 이어 `patternEffectDirector.ApplyHitStop(duration)`을 부른다. 디렉터는 **활성 뷰 전부의 `simulationSpeed`를 0으로** 내렸다가 창이 끝나면 각자의 `speed`로 되돌린다.

- **왜 이제는 하나**: 임팩트 이펙트가 곧 정지 창에서 가장 눈에 띄는 물체다. 캐릭터가 멈췄는데 스파크만 흐르면 "멈췄다"가 아니라 "캐릭터만 렉 걸렸다"로 읽힌다. `speed` 필드가 생긴 지금 구현이 한 줄이라 미룰 이유가 없다.
- **`Time.timeScale`은 여전히 금지**(§7-3). 멈추는 것은 파티클 시스템의 자기 배속뿐이라 판정·오디오·포커스 링은 그대로 흐른다 — Animator Speed Multiplier만 멈추는 기존 모델과 **같은 성질**이다.
- **복귀값은 뷰가 안다.** 큐마다 `speed`가 다르므로 디렉터가 하나로 되돌리면 안 된다 — 뷰가 자기 `CurrentSpeed`를 들고 있다가 그리로 돌아간다(`CameraDirector`가 씬의 휴지값을 캐시하는 것과 같은 이유).
- **정지 중 새로 발사되는 큐도 0으로 시작한다** — 창 안에서 태어난 이펙트만 혼자 흐르면 같은 문제가 그대로 남는다.
- ⚠ **`HitStopDirector`는 여전히 파티클을 직접 안 만진다.** 창을 알려 줄 뿐이고 `simulationSpeed` 호출은 전부 이펙트 층에 남는다(카메라와 같은 경계).
- ⚠ `SlicePiece`(표적 조각)는 **여전히 안 멈춘다** — 닫힌 식이라 배속 개념이 없다. 알려진 구멍으로 남긴다.

>>>

---

## 단계

### - [x] Step 1 — `PatternEffectCue` 타입 + `Pattern`의 큐 리스트
- `Assets/02. Scripts/Pattern/Core/PatternEffectCue.cs` 신규 (`PatternSpace`, `ClipAlignment`와 같은 asmdef).
  - D0의 필드 + enum 셋(`EffectCondition` / `EffectTiming` / `EffectAnchor`).
  - `bool IsUsable => prefab != null`
  - `float ResolveTime(PatternQueuedInfo info, float impactOffset)` — D3 표를 그대로 옮긴 switch 하나. **시각 계산의 유일한 소유자**이며 그래서 테스트 가능한 순수 함수다.
  - `bool NeedsOutcome => condition != Always` / `float OutcomeKnownTime(info)` (= `LastNodeTime`) — 툴 경고와 런타임 가드가 **같은 함수**를 본다.
- `Pattern.cs`에 `[SerializeField] private List<PatternEffectCue> effectCues = new();` + `public IReadOnlyList<PatternEffectCue> EffectCues => effectCues;`
- `OnValidate`:
  - 프리팹에 `ParticleSystem`이 하나도 없으면 경고(배선 실수의 유일한 탐지 장치).
  - `condition`이 결과인데 시각이 `LastNode`보다 이르면 경고(D3 ⚠).
  - `timing == Node`인데 `nodeIndex`가 패턴 노드 수 밖이면 경고.
  - `attacker == Attacker.Enemy`인데 `Parry` 큐가 있으면 경고(그 역할에선 적이 막지 않는다). `WarnUnusedSlots()` 옆.

### - [x] Step 2 — `PatternQueuedInfo`에 `NodeTimes` 추가
- `Pattern/PatternQueuedInfo.cs`에 `float[] NodeTimes` 한 필드(노드별 입력 예정 시각). `PatternHandler.SetPattern`이 `ActivePattern`에서 채워 발행한다.
- **이 한 필드가 `timing == Node`를 가능하게 하고, 그 대가로 디렉터가 이벤트를 더 구독하지 않아도 된다**(모든 시각이 큐 투입 때 확정 — D3).
- 기존 구독자(`SliceTargetDirector` 등)는 필드가 늘어도 영향 없음(struct 생성자 인수만 추가).

### - [x] Step 3 — 월드 이펙트 뷰
- `CanvasEffectView`를 **이름 그대로 두고** 월드에서도 쓸 수 있게 일반화한다(프리팹 스크립트 참조가 클래스명에 묶여 있어 rename은 프리팹을 깬다).
  - `[RequireComponent(typeof(RectTransform))]` 제거, `uiParticle`·`rectTransform` null 허용(이미 null 체크가 대부분 있다).
  - `SetWorldPose(Transform anchor, bool follow, Vector3 posOffset, Vector3 rotOffset, float scale)` 추가 — `follow`면 앵커의 자식으로 붙이고, 아니면 앵커 기준 월드 포즈만 복사한다(D2).
  - `SetSpeed(float speed)` 추가 — 캐시된 `systems` 배열을 돌며 `var main = s.main; main.simulationSpeed = speed;`. **`MainModule`은 구조체 사본이라 되대입해야 반영된다**(빠뜨리면 조용히 무시된다).
  - `OnDespawn`에서 `scale`·`speed`를 프리팹 기본값으로 되돌린다 — 풀에서 재사용되므로 안 되돌리면 **다음 큐가 이전 큐의 배속을 물려받는다**.
  - `float CurrentSpeed { get; }` 노출 — 히트스톱이 0으로 얼렸다가 **이 값으로** 되돌린다(D7).
  - `OnDespawn`에서 **부모를 풀 루트로 되돌린다**(따라가기 큐가 앵커와 함께 파괴되는 것을 막는 유일한 장치).
  - Canvas 경로(`SetLocalPosition`)는 그대로 — `EffectManager`는 한 줄도 안 고친다.
- ⚠ 이 단계에서 기존 Canvas 이펙트가 그대로 도는지 확인한다(회귀 지점).

### - [x] Step 4 — `EnemyDirector.OnEnemyReacted` 이벤트
- D6대로 `EnemyReaction` enum + 이벤트 선언, `ResolveReservation`의 `else` 분기(= 처치가 아닌 경로)에서 발행.
- 반응 판정은 이미 계산된 `retreat` 값을 그대로 재사용한다 — `EnemyView.Resolve`의 `parried` 식과 **같은 값**을 봐야 애니메이션과 이펙트가 어긋날 수 없다. 실패가 아니면(성공 + 처치 안 함) 발행하지 않는다.
- 구독자가 없으면 아무 일도 안 일어난다(기존 이벤트들과 같은 성질).

### - [x] Step 5 — `PatternEffectDirector`
- `Assets/02. Scripts/Effect/PatternEffectDirector.cs` 신규.
- 참조: `PatternHandler`, `EnemyDirector`(비면 결과 조건 큐만 빠진다), **앵커 Transform**(`impactAnchor`/`player`/`playerWeapon` — `Opponent`는 런타임 조회), `effectsEnabled`(bool 토글 — 다른 디렉터와 같은 규율).
- **예약은 `handler.OnPatternQueued` 하나로 끝난다.** 패턴의 모든 큐를 돌며 `cue.ResolveTime(info, pattern.ImpactOffset)`으로 시각을 계산해 예약 리스트에 넣는다. 패턴 인스턴스 **토큰**과 함께 넣는다 — 같은 템플릿이 동시에 살아 있을 수 있다(§3).
- **결과는 나중에 채워진다.** `OnPatternComplete`(성패)와 `OnEnemyReacted`(패링/회피)가 그 토큰의 결과 칸을 채운다.
  - `Always` 큐는 결과와 무관하게 제 시각에 발사.
  - 결과 조건 큐는 결과가 **찼고 조건이 맞을 때만** 발사, 안 맞으면 조용히 폐기. 발사 시각이 됐는데 아직 결과가 없으면 그 프레임에 폐기한다(D3 ⚠ — 툴이 저장 전에 이미 경고한 상태).
  - ⚠ 두 이벤트의 도착 순서에 기대지 않는다. 결과는 "성패"·"반응" 두 칸이며 각자 채워진다.
- **발사**: 앵커 Transform 해석(D2, `Opponent`는 이 순간 `CurrentOpponent`를 읽는다) → 풀에서 대여 → `SetWorldPose` → 재생. `Time.time >= fireTime`이면 즉시 발사(`HitStopDirector`와 같은 분기).
- **칼날 경로 해석(D2-1)**: `BladePath` 헬퍼 하나가 칼날 렌더러의 `localBounds`에서 **장축과 반길이를 한 번 캐시**하고, `bladeT` → 로컬 위치를 돌려준다. `MeshSliceBakerWindow.SampleWeapon`과 같은 규칙(최장 축 = 날 길이)이며 그 사실을 주석에 남긴다.
  - `alignToBlade`면 뷰가 매 프레임 진행 방향을 갱신해 회전을 잡는다 — **이때만** 프레임 비용이 있고, 안 쓰면 부모 관계만으로 따라간다.
- **큐가 없거나 프리팹이 비면 예약을 안 만든다** — 그게 "뒷구르기는 무연출"의 구현 전부다.
- 풀: 프리팹별 `Queue<CanvasEffectView>` + `maxSizes`(`EffectManager`/`SliceTargetDirector`와 같은 모양). 비활성 `poolRoot` 아래 보관. **모든 큐가 같은 풀을 공유한다** — 키가 프리팹이라 큐를 구분할 이유가 없다.
- 프리웜: `[SerializeField] Pattern[] prewarmPatterns`. `Start()`에서 각 패턴의 **모든 큐** `poolSize`만큼 미리 만든다(곡 도중 `Instantiate` 금지 — CLAUDE.md §5).
- 회수: `CanvasEffectView.OnFinished` 구독(기존 수명 감지 재사용). `OnAllPatternsCleared`에서 예약 비우고 활성분 회수.

### - [x] Step 6 — 히트스톱 연동 (D7)
- `PatternEffectDirector.ApplyHitStop(float duration)` — 활성 뷰 전부 `SetSpeed(0)`, 창이 끝나면 각 뷰의 `CurrentSpeed`로 복귀. 창 안에서 새로 발사되는 큐도 0으로 시작.
- `HitStopDirector`에 `[SerializeField] PatternEffectDirector patternEffectDirector;` 한 줄 + `Fire()`에서 호출 한 줄. **비면 이펙트만 안 멈춘다**(배우 참조가 비면 그 배우만 빠지는 기존 규율 그대로).
- `HitStopDirector`는 `simulationSpeed`를 직접 안 건드린다(D7 ⚠).

### - [x] Step 7 — 씬 배선
- `BattleScene`에 `PatternEffectDirector` 컴포넌트 추가(`HitStopDirector`와 같은 오브젝트), `handler`·`enemyDirector`·앵커 Transform들 배선.
- ⚠ `playerWeapon`은 **안쪽 칼날 노드**를 잡는다(D2). 바깥 동명 노드를 잡으면 이펙트가 칼과 어긋난 자리에 붙는다.
- `HitStopDirector`의 새 참조(Step 6)도 같이 배선한다.
- 배선이 비면 조용히 무연출(기존 규율).

### - [x] Step 8 — 저장 툴 `Tools/Pattern Effect Tool`
- `Assets/02. Scripts/Effect/Editor/PatternEffectWindow.cs` 신규.
- 화면 구성은 위→아래로 **프리뷰 → 타임라인 → 큐 리스트/상세**. 트리머와 같은 배치라 두 창을 오가도 눈이 헤매지 않는다.
- **왼쪽: 패턴 목록** — 템플릿 폴더(또는 선택한 `SongChart.patternPool`)의 패턴을 한 줄씩. **큐 개수**와 경고 유무를 표시.
- **가운데: 큐 리스트** — `ReorderableList`. 한 줄에 `label` · 조건 · 시각(`timing±offset`) · 앵커 요약. 추가/복제/삭제/순서 변경. **여기가 "리스트로 받아서 내가 붙인다"의 실체다.**
- **오른쪽: 선택한 큐 하나의 상세** — 프리팹, 조건, 시각(기준점 + 오프셋, `Node`면 노드 번호), 앵커 + `follow`, offset/rotation/scale/**speed**, poolSize.
  - `speed`를 바꾸면 타임라인의 재생 구간 막대가 **그만큼 짧아진다**(`(duration + startLifetime) / speed`). 배속이 화면에 미치는 영향이 그 자리에서 보인다.
  - `Pattern.ImpactOffset`은 **회색 읽기 전용**으로 같이 보여 준다(`PatternChartWindow`의 관례 — 어느 값이 공통 앵커인지 헷갈리지 않도록).
  - `attacker == Attacker.Enemy`면 조건 드롭다운에서 **`Parry`를 감춘다**(그 역할에선 적이 막지 않는다), `Evade`의 설명을 "피격"으로 바꾼다(D1 표).
  - 조건 옆에 그 반응이 재생하는 **애니메이터 스테이트 이름**을 같이 적어(`EnemyView`에서 읽음), 어떤 클립에 붙는 이펙트인지 편집 중에 보이게 한다.
- **타임라인 — 이 툴의 핵심 화면.** 가로축이 `PatternStart → 노드들 → LastNode → Impact`인 **패턴 진행 전체**이고(임팩트만이 아니다), 노드 눈금은 `NodeTimes` 간격 그대로 그린다. 각 큐를 그 시각에 마커로 얹고, 프리팹의 파티클 `duration + startLifetime` 최대값으로 재생 구간을 막대로 그린다.
  - **결과 조건 큐가 `LastNode`보다 왼쪽에 있으면 빨간 경고**(D3 ⚠) — 이 화면에서 한눈에 보이는 것이 이 배치의 목적이다.
  - 조건별로 마커 색을 나눈다(Always / Success / Parry / Evade).
- **프리뷰 — 애니메이션과 파티클을 같은 `t`로 함께 굴린다.** `AnimationClipTrimmerWindow`의 프리뷰 방식을 그대로 가져온다(`PreviewRenderUtility` + `AnimationClip.SampleAnimation`), 거기에 파티클을 얹는다.
  - **스크럽**: 타임라인을 끌면 그 `t`에서 **두 배우가 포즈를 잡고**(플레이어 공격/패링, 적 견제/사망 — 트리머와 같은 슬롯), **파티클은 `ParticleSystem.Simulate(elapsed, true, true)`로 그 시각의 상태를 그린다**. 재생 버튼이면 `OnEditorUpdate`가 `t`를 흘려보낸다(트리머와 같은 루프).
    - `Simulate`는 **되감기가 되는 유일한 방법**이다. 실시간 `Play()`로는 앞으로만 갈 수 있어 "임팩트 프레임에서 스파크가 어디까지 퍼져 있나"를 못 본다.
    - `elapsed = (t − 큐 발사 시각) × cue.speed`. 아직 안 뜬 큐는 안 그린다.
  - **클립 정렬은 런타임과 같은 식**을 쓴다(`ClipAlignment.ResolveScheduleStart`/`ResolvePlaySpeed`) — 그래야 프리뷰에서 맞춘 `timeOffset`이 게임에서도 맞는다. 트리머가 이미 그 계산을 하고 있으므로 새 수학이 없다.
  - **앵커 해석**: `Player`/`PlayerWeapon`은 프리뷰 인스턴스의 **본을 그대로** 찾아 붙인다(따라가기 큐가 칼끝에서 어떻게 도는지가 여기서 보인다). `Opponent`는 적 인스턴스. ⚠ `ImpactAnchor`만은 **씬 값이라 프리뷰에 없다** — 트리머와 같은 결투 규약(플레이어 원점, 적 `DuelDistance` 앞)으로 근사하고, 그 사실을 창에 한 줄 적는다.
  - **칼날 경로를 선으로 그린다(D2-1).** 클립 전 구간을 프레임마다 샘플링해 `bladeT` 지점이 지나간 자리를 폴리라인으로 얹고, 현재 `t`의 칼날 선분(손잡이→칼끝)을 굵게 표시한다. **"칼이 어디를 지나가는지"가 보여야 거기 붙이는 작업이 눈대중이 아니게 된다.**
    - 경로 위를 **클릭하면 그 프레임의 시각이 큐의 `timeOffset`으로 들어간다** — 칼이 표적을 스치는 지점을 찍으면 타이밍이 그 자리에서 정해진다. 숫자를 손으로 맞출 일이 없다.
    - `bladeT` 슬라이더를 끌면 폴리라인이 즉시 다시 그려져 손잡이/칼끝 궤적 차이가 그대로 보인다.
  - 이 화면이 **"막는 모션이 도는데 스파크가 다른 프레임에 뜬다"**(Step 9 ⑥)를 게임을 켜지 않고 잡는 자리다.
  - 프리뷰 인스턴스는 `HideFlags.HideAndDontSave`, 창이 닫히면 파기(트리머와 같은 수명 규율). 플레이 중엔 비활성.
- 일괄 도구: 선택한 여러 패턴에 **같은 큐를 통째로 추가/제거**(`PatternChartWindow`의 일괄 도구와 같은 결).
- 저장은 `SerializedObject` 경유 + `Undo` 등록.

### - [x] Step 9 — 검증
- 유닛 테스트: `PatternEffectCue.ResolveTime`이 다섯 `timing` 값과 `timeOffset` 부호를 정확히 옮기는지 + `NeedsOutcome`/`OutcomeKnownTime` 경계 + **`[Min]` 하한이 실제로 걸리는지**. (`Pattern/Tests` asmdef에 한 파일 추가.)
- 수동 확인 항목을 문서에 남긴다:
  ① 곡 재생 중 히치 없음(프리웜 확인)
  ② 카메라 앵글 교체 후에도 이펙트가 같은 자리
  ③ 패턴 겹침 구간에서 두 이펙트가 각자 뜸
  ④ 기존 Canvas 이펙트 회귀 없음
  ⑤ **`Attacker.Player` 실패 — 창이 짧아 적이 막으면 스파크가 뜨고, 창이 넉넉해 뒤로 구르면 아무것도 안 뜬다.** 두 경우를 한 곡 안에서 확인한다(창은 채보가 정하므로 둘 다 나온다).
  ⑥ **이펙트와 반응 클립이 같은 순간에 붙는지** — 막는 모션이 도는데 스파크가 다른 프레임에 뜨면 `timeOffset`으로 맞춘다.
  ⑦ **`Attacker.Enemy` 실패(피격)에는 패링 스파크가 안 뜬다**(그 패턴의 `Evade` 큐만 뜬다).
  ⑧ **`timing == Node` 큐가 그 노드 링이 닿는 순간에 뜬다** — 노드가 3개 이상인 패턴에서 중간 노드로 확인.
  ⑨ **`follow = true` 큐가 칼을 따라간다**, 그리고 회수 뒤에도 풀 인스턴스가 살아 있다(부모 복원 확인 — Step 3).
  ⑨-1 **`bladeT = 0`과 `1`이 각각 손잡이·칼끝에 붙는다**(D2-1). 축 유도가 틀리면 여기서 즉시 보인다 — 칼 옆구리로 튀어나간다.
  ⑩ **히트스톱 순간 이펙트가 캐릭터와 같이 언다**(D7), 해제 뒤 각 큐가 **자기 `speed`로** 이어진다 — `speed`가 다른 큐 둘을 붙여 확인.
  ⑪ **판정이 안 흔들린다** — 정지 창을 늘려도 Perfect 판정이 그대로여야 한다(`Time.timeScale`을 안 쓴다는 사실의 확인).

### - [x] Step 10 — 문서
- `docs/!Guides/Guide_PatternEffectTool.md` (툴 사용법).
- `CLAUDE.md`에 §7-4 정도로 한 절 추가 — "패턴별 월드 이펙트"의 소유(큐 리스트)·시각 기준점·앵커·툴 위치.
- `CLAUDE.md` §7-3(히트스톱)의 "⚠ 남은 이음매"를 갱신 — 이펙트는 이제 멈춘다, `SlicePiece`만 남는다.

---

## 하지 않는 것 (명시)
- **반응 클립 자체의 변경** — 어떤 반응이 나올지는 `EnemyDirector`가 창을 보고 정하는 기존 규율 그대로다(§11-2). 이펙트는 그 결정을 **읽기만** 한다
- **반응별 카메라·사운드 큐** — 같은 이벤트로 붙일 수 있지만 이번 범위 밖. `OnEnemyReacted` 하나가 생기면 나중에 한 줄로 붙는다
- `EffectTrigger` 카탈로그 변경 — Canvas 쪽은 한 줄도 안 고친다(죽은 값 `Parried` 배선도 이번엔 안 한다, D6)
- **`SlicePiece`(표적 조각) 정지** — 닫힌 식이라 배속 개념이 없다. 이펙트는 멈추게 됐지만(D7) 이건 알려진 구멍으로 남는다
- `SliceTargetDirector`의 이펙트 흡수 (D5)
- **임의 씬 오브젝트 앵커**(이름/경로로 찾기) — 앵커는 enum 넷으로 시작한다. 다섯 번째가 실제로 필요해지면 디렉터에 `namedAnchors` 배열을 두고 enum에 `Custom`을 더한다(큐에 인덱스 한 칸 추가)
- **패턴인풋 노드(Point) 자리 앵커** — 그 좌표는 월드가 아니라 Canvas다(D2 마지막). 기존 `EffectManager` 관할
- **실제 입력 시각 기준 큐** — `NodeTimes`는 예정 시각이다(D3 ⚠). 입력 순간에 정확히 붙는 연출은 이미 판정 이벤트가 처리한다
