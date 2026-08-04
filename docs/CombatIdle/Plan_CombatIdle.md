# Plan — CombatIdle 방향 A (대기 모션을 비트에 붙인다)

근거: `docs/CombatIdle/Research_CombatIdle.md` · 참고작: `docs/ReferenceGames/Research_ReferenceGames.md`

## 측정된 사실 (설계의 전제)

에디터에서 실제 클립을 재 본 값이다. **이 숫자가 계획을 둘로 가른다.**

| | `Samurai_Idle` (적) | `Katana_Idle` (플레이어) |
|---|---|---|
| 길이 / 루프 | **1.10초** / O | **2.50초** / O |
| 움직이는 커브 | 71 / 130 | 113 / 297 |
| 모션 성격 | **전신 호흡 사이클 한 바퀴** | 오른 어깨만 한 바퀴 |
| Spine·Chest 진폭 | 0.04 (≈2°) | 0.06 / 0.002 |
| 최대 진폭 | 0.084 (팔·다리·머리 고르게) | 0.734 (어깨 단독) |
| **RootT.y(수직)** | **0.000 — 완전 고정** | **0.004 — ≈4mm** |

두 결론:

1. **주기 모션은 적에게만 있다.** 적은 1.10초 주기의 진짜 호흡 사이클이라 위상을 물릴 대상이 존재한다.
   플레이어는 "어깨가 2.5초에 한 번 크게 도는" 것뿐이라 **박자로 읽힐 수가 없다.**
2. **둘 다 수직 운동이 0이다.** 그래서 "몸이 박자를 탄다"는 그림은 **어느 클립에도 없다** — 넣어야 한다.

→ 계획은 **위상 고정(A-1)**과 **박자 펄스(A-2)** 두 층으로 나뉜다. A-1은 타이밍을, A-2는 진폭을 담당한다.
**A-1만으로도 적 쪽은 효과가 난다** — 열 몇 명이 지금은 랜덤 위상이라 2°가 노이즈로 흩어지는데,
위상을 맞추면 **같은 박에 동시에** 숨쉰다. 진폭이 작아도 **동시성 자체가 읽힌다.**

## 설계 요약

```
beats      = (songTime - beatOffset) * bpm / 60          ← 곡 시각의 순수 함수
A-1 위상   : normalized = (beats / beatsPerLoop) % 1  →  animator.Play(idle, layer, normalized)
A-2 펄스   : chest.localRotation *= Euler(deg * Decay(frac(beats / beatsPerPulse)), 0, 0)   ← LateUpdate에서 덧씀
```

**⚠ 주기가 둘이고 서로 다르다.** `beatsPerLoop`(클립 한 바퀴)와 `beatsPerPulse`(숙이는 간격)를 하나로 묶으면 안 된다.
- `beatsPerLoop`은 **클립 길이에서 파생**되므로 배우마다 다르다(150 BPM에서 적 3박 / 플레이어 6박).
- `beatsPerPulse`는 **전원이 공유**해야 한다. 배우별로 다르면 **유니즌이 깨지는데, 그게 A-1의 핵심 효과다.**

**펄스를 매 박에 걸면 안 된다.** 150 BPM에서 매 박은 0.4초 간격 = 분당 150회로, 호흡이 아니라 진동으로 읽힌다.
사람 안정 호흡이 분당 12~20, 전투 직후가 30~40이므로 **간격 0.6~1.0초**가 맞는 구간이다.

**⚠ 박수를 고정하면 안 된다 — 시간을 고정하고 박수를 파생시킨다.** 박수를 못박으면 간격이 BPM에 **반비례**해
양 끝에서 둘 다 깨진다(2박 고정 기준: 90 BPM에서 1.33초로 늘어지고, 250 BPM에서 0.48초로 다시 진동이 된다).
리듬게임 채보는 보통 120~200이지만 220~300도 나오고, 극단은 beatmania IIDX의 고정 400 BPM·최고 876 BPM까지 간다.

```
beatsPerPulse = {1, 2, 4, 8} 중  (60 / bpm) × N >= minPulseInterval  을 만족하는 최소 N
```

`minPulseInterval` 기본 **0.6초**: 90→1박(0.67s) / 150→2박(0.80s) / 200→2박(0.60s) / 250→4박(0.96s) / 400→4박(0.60s).
**전 구간이 0.6~1.0초 안에 들어오고**, 2의 거듭제곱만 쓰므로 어떤 BPM에서도 음악적으로 떨어진다.
곡마다 다시 만질 값이 아니다 — 채보를 바꿔도 자동으로 맞는다.

**IK는 쓰지 않는다.** 펄스가 골반 아래로 내려가지 않기 때문이다 — 상체만 움직이면 접지가 개입하지 않는다.
IK가 필요해지는 것은 무릎 굽힘 바운스(hips 수직 이동 + 발 고정)를 고를 때뿐이고, 그건 Step 4의 `ponytail:` 항목으로 미룬다.

- **배속(`speed`)을 맞추는 방식은 쓰지 않는다.** 누적 오차로 밀리고, 아이들에 들어오는 순간의 위상이 매번 다르다.
  위치를 **시각의 함수**로 직접 쓰면 드리프트가 원리적으로 없고 언제 들어와도 그 박에 얹힌다 —
  `SlicePiece`(닫힌 식)·`FocusRing`(선형 수축)과 같은 규율.
- **판정에 닿지 않는다.** 순수 연출이며 `Time.timeScale`도 안 쓴다(§7-3 금지).
- **곡이 없으면(디버그·씬 단독 실행) 통째로 조용히 비활성**된다. 기존 배선 누락 규율과 같다.

## 단계

- [ ] **Step 1 — 연속 비트 값** (`ChartGen/Core/BeatGrid.cs`)
  - 이미 `BeatGrid(bpm, beatOffset, subdivisions)`가 있고 `TimeToGridIndex`가 **정수**만 준다.
    연속값 한 줄을 더한다: `public float TimeToBeats(float time) => (time - beatOffset) / GridInterval;`
  - 새 클래스 안 만든다. `subdivisionsPerBeat = 1`로 만들면 `GridInterval`이 곧 1박이다.
  - `ChartGen.Tests`에 왕복 테스트 1개(정수 인덱스와 연속값이 어긋나지 않는다).

- [ ] **Step 2 — 곡 시계를 공개한다** (`ChartGen/ChartPlayer.cs`)
  - `audioSource`의 유일한 소유자가 여기다. 다른 곳에서 `AudioSource`를 다시 찾으면 진실의 원천이 둘이 된다.
  - 추가: `public bool TryGetSongTime(out float time)` (재생 중일 때만 true) + `public SongChart Chart => ActiveChart;`
  - ⚠ 카운트다운 구간에는 `audioSource.isPlaying`이 false다 → 그 동안은 비트가 없다(인트로 카메라가 도는 구간이라 무해).

- [ ] **Step 3 — 비트 시계 디렉터** (`Assets/02. Scripts/Beat/BeatSyncDirector.cs`, 신규)
  - `Singleton<BeatSyncDirector>`(기존 `Util/Singleton.cs` 재사용). 씬에 하나.
  - `chartPlayer` 직렬화 참조 + `public bool TryGetBeats(out float beats)`.
  - **펄스 주기도 여기가 소유한다.** 전원이 같은 박에 숙여야 유니즌이 성립하므로 배우별 값이 아니다 —
    컴포넌트가 각자 들면 어긋난 조합이 조용히 만들어진다.
    - 인스펙터 값은 박수가 아니라 **`minPulseInterval`(초, 기본 0.6)** 하나다.
    - `public int BeatsPerPulse` = `{1,2,4,8}` 중 `(60/bpm) × N >= minPulseInterval`인 최소 N (곡이 바뀔 때만 재계산).
    - 박수를 인스펙터에 두면 BPM이 다른 곡에서 조용히 깨진다(위 ⚠ 참조).
  - 계산은 `BeatGrid`가 하고 이 클래스는 **시계를 나눠 주기만** 한다. 등록 리스트를 두지 않는다 —
    대상이 프리팹에 컴포넌트로 붙으므로 스폰/풀링과 무관하게 자동으로 따라온다.

- [ ] **Step 4 — 대기 모션 컴포넌트** (`Assets/02. Scripts/Beat/BeatIdleMotion.cs`, 신규)
  - 붙이는 곳: **플레이어 오브젝트**와 **적 프리팹**(`Enemy_Samurai_Male`) 각 1개.
    프리팹에 붙이면 링의 대기 적까지 전부 자동 적용된다(= 방향 C의 절반이 공짜로 따라온다).
  - 인스펙터: `animator`, `layerName`, `idleStateName`, `beatsPerLoopOverride`(0=자동), 아래 펄스 값들, `phaseLockEnabled`/`pulseEnabled`.
  - **A-1 위상 고정**
    - `beatsPerLoop = max(1, round(clipLength * bpm / 60))` — 자동 산출. 적 1.10초/120BPM → 2박(약 10% 빨라짐), 플레이어 2.50초/120BPM → 5박(정확히 일치).
    - 매 프레임 `animator.Play(idleHash, layerIndex, normalized)`.
    - ⚠ **아이들 상태일 때만.** `IsInTransition(layer)`가 true이거나 현재 스테이트가 아이들이 아니면 손대지 않는다 —
      Sprint/Quickshift/Attack 중에 재생 위치를 밀면 로코모션이 깨지고, 전이 중에 걸면 크로스페이드가 튄다.
  - **A-2 박자 펄스 — 골반 이동이 아니라 상체 회전이다**
    - ⚠ **hips를 내리면 안 된다.** hips는 스켈레톤 루트라 다리와 발이 통째로 따라 내려가 **발이 바닥에 박힌다.**
      보이는 진폭(2cm)이면 박히는 것도 그만큼 보인다. 그걸 무릎 굽힘으로 흡수하려면 **발 IK가 필요해지고**
      (`OnAnimatorIK` + 오프셋 전 발 위치를 IK 목표로 고정), 무릎 뒤집힘 튜닝까지 딸려 온다. 그 값을 치를 이유가 없다.
    - 대신 **골반 위쪽만** 움직인다: `chest = animator.GetBoneTransform(HumanBodyBones.Chest)`
      (둘 다 휴머노이드라 리그 본 이름과 무관하게 잡힌다). 발은 아예 손대지 않으므로 IK가 필요 없다.
    - **방향은 이미 클립이 알려 준다** — `Samurai_Idle`의 `Spine Front-Back`이 −0.157↔−0.133으로 진동한다(≈2°).
      **모션의 축은 맞고 진폭만 부족하다.** 같은 축에 몇 도를 더 실어 주는 것이 이 단계의 전부다.
    - `LateUpdate`에서 `chest.localRotation *= Quaternion.Euler(pulseDegrees * Decay(frac(beats / beatsPerPulse)), 0, 0)`.
      Animator가 매 프레임 포즈를 다시 쓰므로 **매 프레임 덧쓰기**가 곧 애디티브다.
    - `beatsPerPulse`는 디렉터에서 읽는다(Step 3). 매 박이 아니다 — 150 BPM에서 매 박은 분당 150회로 진동이 된다.
    - 감쇠는 다운비트에 숙였다 펴는 모양: `Decay(f) = (1-f)^2` (쉐이크·펀치와 같은 공식 재사용).
    - `pulseDegrees` 기본 **4도**. 측정된 기존 진폭(≈2°)의 두 배라 확실히 읽히고, 상체만 움직여 접지와 무관하다.
    - ⚠ **위상 고정과 같은 게이트**를 쓴다. 달리는 중 상체를 흔들면 대시 실루엣이 뭉개진다.
    - `ponytail:` 무릎 굽힘 바운스(hips 수직 + 발 IK)가 필요해지면 그때 IK 경로를 연다. 지금은 상체 액센트로 충분하다.

- [ ] **Step 5 — 씬·프리팹 배선** (Unity, 코드 아님)
  - `BattleScene`에 `BeatSyncDirector` 오브젝트 1개 + `chartPlayer` 연결.
  - 플레이어: `BeatIdleMotion`(layer=`Running Layer`, state=`Katana_Idle`).
  - `Enemy_Samurai_Male` 프리팹: `BeatIdleMotion`(적 아이들 레이어/스테이트 이름은 `EnemyView`의 `idleStateName`과 같은 값).
  - 곡의 `bpm`/`beatOffset` 확인 — **여기서 처음으로 그 값이 화면에 드러난다**(§검증 참조).

## 검증

- [ ] **Step 6 — 확인**
  - 적 여럿이 화면에 있을 때 **같은 박에 동시에** 숨쉬는가(A-1의 핵심 효과. 한 명만 보면 판단 불가).
  - 플레이어가 도착해 서 있는 구간에서 상체 펄스가 박자에 떨어지는가.
  - **발이 바닥에 박히거나 뜨지 않는가** — 상체만 움직이므로 원리적으로 없어야 한다. 보이면 펄스를 hips에 걸고 있는 것이다.
  - 달리는 중·공격 중에 펄스나 위상 밀기가 **일어나지 않는가**(실루엣 뭉개짐·포즈 튐 없음).
  - 곡 없이 씬만 재생 → 아무 일도 안 일어나고 예전과 동일한가.
  - `pulseEnabled=false`, `phaseLockEnabled=false` 각각 껐을 때 나머지가 그대로 도는가.
  - ⚠ **채보의 `bpm`/`beatOffset`이 틀리면 즉시 눈에 띈다.** 지금까지 런타임에서 아무도 안 읽어서 조용했던 값이다 —
    어긋나 보이면 연출 버그가 아니라 **채보 메타데이터 점검** 신호다.

## 이 계획이 하지 않는 것

- **공백에 정보를 넣지 않는다.** 다음 타점이 언제인지는 여전히 화면에 없다 — 그건 방향 B(윈드업)의 몫이다.
  A는 죽은 프레임을 리듬 프레임으로 바꿀 뿐이고, B와 겹쳐야 공백이 완전히 덮인다.
- 아이들 클립을 새로 만들지 않는다. 진폭은 전부 절차적으로 만든다(BPM이 곡마다 달라도 공짜로 맞는 쪽).
- 링 적들의 선회·도발(방향 C의 나머지)은 별개다. Step 4가 프리팹에 붙으므로 **호흡 유니즌까지만** 공짜로 따라온다.
