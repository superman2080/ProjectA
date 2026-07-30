# Research: 노트 판정 효과음 (SFX 매니저)

## 요청 배경
노트를 때릴 때(판정 발생 시) 효과음이 나야 한다. 효과음 클립을 저장·관리하는 매니저 하나를 만들고, 필요한 곳에서 그 매니저에게 "이 효과음 재생해줘"라고 요청하는 구조로 만든다.

## 현재 상태 (조사 결과)

### 오디오 관련 기존 코드
- 프로젝트 전체에 `AudioManager`/`SoundManager`/`SFX`/`PlayOneShot` 없음. **완전 새 영역.**
- 유일한 오디오 사용처는 곡 재생: `ChartPlayer.cs:12`의 `[SerializeField] private AudioSource audioSource;` — `SongChart.song`(AudioClip)을 재생/정지(`ChartPlayer.cs:67,84,85`). 씬에 고정 배치된 단일 AudioSource이며 매니저 패턴이 아니다.
- `AudioListener`는 씬당 1개(Main Camera 추정, `DefaultScene.unity:583`). 새 SFX 매니저가 두 번째 AudioListener를 추가하면 안 된다(Unity 경고). AudioSource는 여러 개 둬도 무방.

### 판정 이벤트 (SFX 트리거 후보)
`PatternHandler`(`Assets/02. Scripts/UI/PatternHandler.cs`)가 발행하는 확장 이벤트 중 "노트를 때리는" 시점과 맞는 것:
- **`OnFallingNodeResolved(int index, NodeType nodeType, Vector3 worldPosition, JudgementResult result)`** — 노드 하나가 판정 처리되는 순간(Perfect/Good/Miss). **이게 "노트 때릴 때"에 해당하는 이벤트.** `EffectManager.HandleFallingNodeResolved`가 이미 이 이벤트로 시각 이펙트를 재생 중(`EffectManager.cs:145-154`).
- `OnNodeConnected(int index, Vector3 world)` — 노드가 라인에 연결(입력은 됐지만 판정 전 단계, 스와이프 중 지나가는 시점).
- `OnPatternComplete(PatternCompletionInfo)` — 패턴 전체 완료/실패.
- CLAUDE.md 이벤트 확장 포인트 문서에 이 목록이 정리돼 있음(`CLAUDE.md` "이벤트 확장 포인트" 섹션).

### 이미 존재하는 "카탈로그 + 트리거enum" 패턴 두 가지 (그대로 재사용할 템플릿)

**A. `EffectCatalog.cs`** (전역 네임스페이스) — 풀링까지 포함하는 무거운 버전:
```csharp
public enum EffectTrigger { Perfect, Good, Miss, PatternCompleteFull, PatternComplete, NodeConnected }

[Serializable]
public struct EffectEntry
{
    public EffectTrigger trigger;
    public GameObject prefab;
    public int initialSize;
    public int maxSize;
}
```

**B. `CameraCueCatalog.cs`** (전역 네임스페이스) — 프리팹/풀 없이 파라미터만 있는 가벼운 버전:
```csharp
public enum CameraTrigger { PatternSuccess, PatternMiss, PatternFailure }

[Serializable]
public struct CameraCueEntry
{
    public CameraTrigger trigger;
    public float shakeAmplitude;
    public float shakeDuration;
}
```

SFX는 "트리거 → AudioClip(+volume/pitch)" 매핑이라 **B(CameraCueCatalog) 쪽에 더 가깝다** — GameObject 프리팹·풀링이 필요 없고 AudioClip 재생은 `AudioSource.PlayOneShot`으로 끝난다.

### 기존 매니저들의 배선 컨벤션
- **`EffectManager`**, **`CameraDirector`` 둘 다 `Singleton<T>`가 **아니다**. 일반 `MonoBehaviour`로 씬에 배치되고, 인스펙터의 `[SerializeField] private PatternHandler handler` 필드로 참조를 받아 `OnEnable/OnDisable`에서 `PatternHandler`의 기존 이벤트만 구독한다. **`PatternHandler` 본체는 이펙트/카메라를 위해 수정하지 않는다**(관심사 분리 원칙, CLAUDE.md 명시).
- `Singleton<T>`(`Assets/02. Scripts/Util/Singleton.cs`)를 쓰는 건 `Pool` 하나뿐(`Pool.cs:12`, `DontDestroy` 옵션 있음).
- **결론**: SFX 매니저도 `EffectManager`/`CameraDirector`와 동일한 컨벤션(비-싱글톤, `handler` 참조, 이벤트 자체 구독)을 따르는 게 기존 아키텍처와 일관적이다. 다만 사용자가 "필요할 때 해당 객체에 요청해서 발생"이라 명시했으므로, **자체 이벤트 구독(주 경로) + 외부에서 직접 호출 가능한 `public Play(SfxTrigger)` 메서드(보조 경로)**를 함께 둔다 — `CharacterActionPlayer`(타격 임팩트)나 `SliceTargetDirector`(절단 순간) 등 향후 다른 시스템이 직접 요청할 여지를 열어둔다.

### Pool.cs 재사용 가능성
- `Pool.cs`는 `IPoolable`(`OnSpawn/OnDespawn`) 구현 GameObject를 `PoolKey` enum으로 대여/반납하는 시스템. AudioSource를 여기 태우려면 `PoolKey.Sfx` 하나만 두고 그 아래서 여러 클립을 재생해야 하는데, 이 시스템은 "프리팹 1종 = 키 1개" 구조라 동시 재생 다중 보이스에 맞지 않음(FallingNode처럼 하나의 프리팹을 여러 개 뽑아 쓰는 용도로 설계됨).
- **효과음은 자체적으로 작은 `AudioSource` 풀(N개, 예: 8개)을 두는 게 더 단순하다.** `Pool.cs`를 억지로 끼워 맞추지 않는다.

## 제약사항 / 설계에 반영할 점
1. AudioListener 중복 생성 금지 — SFX 매니저는 AudioListener를 만들지 않는다.
2. 짧은 효과음이 겹쳐 재생될 수 있다(연타/콤보) → 단일 AudioSource로는 클립이 서로 끊기므로 **동시 재생 가능한 AudioSource 풀** 필요.
3. `EffectManager`와 마찬가지로 **`PatternHandler`는 수정하지 않는다** — 기존 이벤트만 구독.
4. 트리거 enum은 최소 `Perfect/Good/Miss`(노트 판정)로 시작하되, `EffectTrigger`와 이름을 맞출지 새 enum(`SfxTrigger`)을 별도로 둘지는 Plan에서 결정 필요(→ 사용자 피드백 대상).
5. 씬 배치 컨벤션: `EffectManager`/`CameraDirector`처럼 씬에 배치되는 일반 MonoBehaviour로 하되, 외부 요청용 public API도 제공.
