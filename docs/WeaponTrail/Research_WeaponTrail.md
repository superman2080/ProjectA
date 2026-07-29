# Research: 칼날 트레일 (WeaponTrail)

## 목표

베기 애니메이션이 재생되는 동안에만 칼날 트레일을 켜고, 스윙이 끝나면 끈다.
트레일 자체는 이미 씬에 배선되어 있으므로 **켜고 끄는 시점만** 만들면 된다.

---

## 현재 구현 분석

### 1. 트레일 컴포넌트 — `Tiny.Trail`

경로: `Assets/99. External Assets/MiniGames/Trail/Trail.cs` (MIT, MiniGames)
**asmdef가 없다** → `Assembly-CSharp`에 속하므로 `CharacterActionPlayer`에서 직접 타입 참조가 가능하다.

인스펙터 필드:

| 필드 | 씬 현재값 | 의미 |
|---|---|---|
| `material` | `MiniGames/Trail/Demo/TrailStick.mat` | 트레일 메쉬 머티리얼 |
| `duration` | **0.1** | 점 하나의 수명(초). 리본이 남는 길이 |
| `corner` | 1 | 코너 둥글기(Catmull-Rom 보간 삽입 수) |
| `loop` | false | 첫/끝 점 연결 |
| `points` | `(0.02, 0.92, 0)` / `(0, 0.61, 0)` / `(0, 0.1, 0)` | **로컬 좌표 3점** — 칼날을 따라 찍혀 있다 |

`points`는 트랜스폼 로컬 좌표다(`TransformVertices`가 `localToWorldMatrix.MultiplyPoint3x4(points[i])`).
칼날 메쉬의 `localBounds.size.y ≈ 1.12`이므로 y 0.1~0.92는 **칼날 날 부분**에 해당한다 — 이미 적절히 오서링되어 있다.

### 2. 트레일이 붙어 있는 위치 (중요 — 오배선하기 쉽다)

```
Char_School_Katana_FullBody-Magica cloth2
└── root
    └── add_weapon_r                      ← 칼이 바인딩된 단일 본(칼의 트랜스폼 그 자체)
        ├── Weapon_Katana_01_Blade        ← 바깥 노드 (Transform만)
        │   └── Weapon_Katana_01_Blade    ← ★ MeshFilter + MeshRenderer + Tiny.Trail
        └── Weapon_Katana_02_Blade        ← 비활성(칼날 변형 2)
            └── Weapon_Katana_02_Blade
```

**같은 이름의 노드가 2단으로 겹쳐 있고, `Trail`은 안쪽(메쉬) 노드에 있다.** 배선 시 바깥 노드를 잡으면 조용히 아무 일도 일어나지 않는다.

`add_weapon_r`이 `root`의 직계 자식이며 칼은 본 하나짜리 강체 스킨이라는 점은 `docs/WeaponBoneBake/`에서 이미 규명됐다 — 트레일도 이 본의 애니메이션 커브를 그대로 따라간다.

### 3. enable/disable 시 동작 (핵심)

```csharp
private void OnEnable()
{
    if (trailGo == null) return;
    trailGo.SetActive(true);
    Initialize((int)(duration / Time.fixedDeltaTime));
}

private void OnDisable()
{
    if (trailGo) trailGo.SetActive(false);
    if (update != null) StopCoroutine(update);
    update = null;
}
```

| 동작 | 결과 | 평가 |
|---|---|---|
| **켤 때** | `Initialize` → `ClearVertices()`가 **모든 정점을 현재 위치로 접어 넣는다** | ✅ 이전 위치에서 늘어나는 잔상이 없다. 별도 처리 불필요 |
| **끌 때** | `trailGo.SetActive(false)` — **즉시 사라진다** | ⚠️ 페이드아웃이 없어 리본이 뚝 끊긴다 (아래 참고) |

**최초 활성화 경로**: 컴포넌트가 비활성 상태로 시작하면 Unity는 `Start()`를 호출하지 않아 `trailGo`가 없다.
런타임에 처음 켜면 `OnEnable`이 early-return하고, 곧이어 `Start()`가 돌며 `trailGo` 생성 + `Initialize`까지 마친다.
→ **첫 스윙도 정상 동작한다.** 다만 `OnEnable`의 early-return 경로를 버그로 오인하지 말 것.

### 4. 트레일 생성 파이프라인

- `Start()`에서 `trailGo`(별도 루트 GameObject, `DontDestroyOnLoad`)를 만들고 그 안의 Mesh를 매 프레임 갱신한다. **캐릭터의 자식이 아니라 월드 공간의 독립 오브젝트**다.
- 정점 이력은 `WaitForFixedUpdate` 코루틴이 밀어 넣는다 → **샘플링 레이트가 프레임레이트가 아니라 물리 레이트**다.
- 세그먼트 수 = `duration / Time.fixedDeltaTime` = `0.1 / 0.02` = **5**. 스윙이 빠를수록(배속 최대 2.5배) 리본이 각져 보일 수 있다 → 튜닝 여지(`duration` ↑ 또는 Fixed Timestep ↓).
- `OnDestroy`에서 `trailGo`를 파괴하므로 씬 전환 시 누수는 없다.

### 5. 스윙 구간을 아는 곳 — `CharacterActionPlayer`

트레일을 켜고 끌 시점은 이 클래스만 안다.

| 지점 | 코드 | 의미 |
|---|---|---|
| 스윙 시작 | `PlaySlot(clip, startOffset, dur, speed)` | 클립 재생 시작. `blendInStartTime` 등 상태를 여기서 세팅 |
| 스윙 끝 | `actionEndTime = Time.time + dur / speed` | **트림 끝 = 복귀 시작점** |
| 끝 감지 | `Update()`의 `if (!speedRestored && Time.time >= actionEndTime)` | 이미 트림 끝을 정확히 한 번 감지하는 래치가 있다 |

`PlaySlot`은 **두 경로**에서 호출된다:

| 호출자 | 클립 | 트레일 |
|---|---|---|
| `TryStartPendingSuccess()` | 성공 베기(`Pattern.SuccessAnimationClip`) | **켠다** |
| `HandleJudgeTargetFirstMiss()` | 피격(`hitClips`) | **끄다** — 휘두르는 동작이 아니다 |

→ `PlaySlot` 안에서 분기하려면 호출자가 "스윙인지"를 알려줘야 한다.

### 6. 인터럽트 경로 (놓치기 쉬운 케이스)

`actionEndTime`에 도달하기 전에 다음 액션이 시작될 수 있다:
- 연계가 촘촘하면 다음 `PlaySlot`이 현재 액션을 CrossFade로 끊는다.
- 스윙 도중 첫 미스가 나면 `HandleJudgeTargetFirstMiss` → Hit 클립 `PlaySlot`으로 끊긴다.

→ **`PlaySlot`은 항상 "이전 스윙 종료 + (스윙이면) 새 스윙 시작"으로 다뤄야 한다.** 그러지 않으면 베기 도중 미스가 나서 Hit으로 넘어갔는데 트레일이 계속 켜져 있는 상태가 남는다.

### 7. 프로젝트의 확장 관례

`CLAUDE.md`가 명시하는 원칙 — 새 연출은 **이벤트만 구독해서** 붙이고 본체를 고치지 않는다.
- `EffectManager`는 `PatternHandler`의 이벤트만 구독한다("PatternHandler는 이펙트를 위해 수정하지 않는다").
- `SliceTargetDirector`는 `OnPatternQueued` / `OnAllPatternsCleared`만 구독한다.

`CharacterActionPlayer`에는 아직 외부 확장 이벤트가 없다(구독만 한다). 트레일이 첫 소비자가 된다.

---

## 제약사항 / 알려진 한계

1. **끌 때 페이드아웃이 없다.** `Tiny.Trail`에 알파를 낮추는 API가 없어 리본이 즉시 사라진다.
   늦게 꺼도 그동안 칼이 계속 움직이므로 리본이 계속 그려질 뿐, 해결되지 않는다.
   근본 해결은 머티리얼 인스턴스의 알파를 낮추는 것인데, 트림 끝에서는 스윙이 이미 감속해 리본이 짧아진 상태라
   체감이 크지 않을 것으로 본다. → **기본은 즉시 끄기, 페이드는 선택 단계로 남긴다.**
2. **칼날 변형이 둘이다**(`_01_Blade` 활성 / `_02_Blade` 비활성). 트레일은 `_01`에만 있다.
   칼날 교체 기능은 현재 없으므로 참조 하나로 충분하되, 필드는 나중에 배열로 넓힐 수 있게 둔다.
3. **씬 배선이 유일한 안전장치다.** 같은 이름 노드가 2단이라 오배선 시 조용히 무연출이 된다 → 배선 누락 시 경고 로그가 필요하다.
4. **`Tiny.Trail`은 외부 에셋이다.** `Assets/99. External Assets/` 아래이므로 **수정하지 않는다** — 업데이트 시 덮어써진다.
5. 물리 레이트 샘플링이라 Fixed Timestep 설정에 트레일 품질이 종속된다. 트레일을 위해 프로젝트 물리 설정을 바꾸는 것은 부작용이 크므로 `duration` 쪽으로 튜닝한다.
