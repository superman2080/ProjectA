# Research — Rift (균열 오브젝트 연출)

## 1. 지금 씬에 있는 것

`Assets/01. Scenes/Tutorial.unity`의 `Rift`(fileID 1360371986):

| 컴포넌트 | 값 |
|---|---|
| `Transform` | pos (0, 0, −1.56) · rot −90° about X · scale (1,1,1) |
| `MeshFilter` / `MeshCollider` | 빌트인 **Quad**(mesh 10209) |
| `MeshRenderer` | 머티리얼 `Cracks Material`(guid `31321ba1…`) |
| `CrackParameterHandler` | **데모 스크립트. 참조가 전부 비어 있다(`cracksRenderer: {fileID: 0}`)** |

- 회전이 −90°(X)라 Quad 법선이 +Y → **바닥에 누운 판**이다. 지면 균열.
- `CrackParameterHandler`(`99. External Assets/Cracks/Scripts/`)는 에셋 데모용이다 — `Camera.main`을 직접 움직이고 `Input.GetKeyDown(Q)`로 Canvas를 토글하며, `InputField` 4개를 요구한다. 참조가 비어 있어 지금은 `Update`에서 `animate`가 false라 아무 일도 안 하지만, **`menuShown`이 1이라 `Camera.main.transform.localPosition`을 매 프레임 (−1.27, 0, 0)으로 Lerp한다** → 카메라가 조용히 밀린다. **이 컴포넌트는 제거 대상이다.**

## 2. Cracks 셰이더가 주는 손잡이

`99. External Assets/Cracks/Shaders/Cracks.shader`:

| 프로퍼티 | 타입 | 셰이더에서 쓰는 방식 |
|---|---|---|
| `_NoiseOffset` | `Vector` (float2로만 읽는다) | `Turbulence(uv.x + _NoiseOffset.x, uv.y + _NoiseOffset.y, …)` — 노이즈 샘플 좌표를 통째로 민다 |
| `_Emission` | `Float` | `color += (tex2D(_EmissionTex, uv) * _EmissionColor) * _Emission` — 그냥 곱해지는 가산값이라 **상한이 없다**(0~2가 저작 관례) |
| `_CrackThickness` / `_CrackAmount` | Float / Int | 이번 작업 범위 밖 |

⚠ 머티리얼은 **에셋 하나를 공유**한다(`Cracks Material.mat`). `sharedMaterial`을 코드가 수정하면 **에디터에서 에셋에 그대로 저장된다** — §12의 "프로파일을 코드가 수정하지 않는다"와 같은 함정. `renderer.material`(인스턴스)로만 쓴다.

## 3. BPM의 출처

- `ChartGen.SongChart.bpm`(기본 120) + `beatOffset`.
- `ChartGen.ChartPlayer`:
  - `ActiveChart => GameSession.SelectedChart ?? debugChart` — **재생 전에도 읽힌다.**
  - `SongSource`(`AudioSource`) 공개.
  - 이벤트: `OnCountdownStarted(float)` · `OnSongEnded`.
- 채보는 `audioSource.time`으로 돌고 판정은 `Time.time`으로 돈다(§7-3). **음악에 맞는 맥박은 `SongSource.time`에서 파생해야** 곡 중간 정지·시작 지연과 어긋나지 않는다. 곡이 안 도는 동안(탐색·카운트다운)은 그 값이 0에 멈추므로 `Time.time` 폴백이 필요하다.
- ⚠ `Time.timeScale`은 §14의 마무리 실루엣이 0.1로 내릴 수 있다. `Time.time`은 스케일된 시계라 그때 맥박도 같이 느려진다 — **연출이므로 그게 오히려 맞다**(§14가 화면 전체를 늘리는 중이다).

## 4. 이미 있는 켜고 끄는 창구 — 새로 만들 필요가 없다

`02. Scripts/Encounter/Encounter.cs`:

```csharp
[Tooltip("아직 볼일이 남은 자리의 일렁임. Fresh와 Retry에서만 켜진다.")]
[SerializeField] private GameObject shimmerVfx;
...
public void ApplyShimmer() { if (shimmerVfx != null) shimmerVfx.SetActive(HasBusiness); }
```

§13이 신규 코드로 꼽은 **"일렁임 VFX on/off"가 이것**이고 이미 구현돼 있다. `Rift`를 그 슬롯에 꽂으면 `Locked`/`Done`에서 조용해지는 규칙(§9 표)이 **새 코드 0줄로** 성립한다. 즉 Rift 스크립트는 **on/off·상태·진행도를 몰라도 된다** — `SetActive`가 `OnEnable`/`OnDisable`을 알아서 부른다.

## 5. 용어

§13: 무대가 서 있는 지점은 **균열**. 오브젝트 이름 `Rift`가 그 영어 대응이라 새 용어를 만들지 않는다.

## 6. 선행 사례 — 이 코드베이스의 "순수 소비자" 관례

`CameraDirector`·`HitStopDirector`·`FinaleSilhouetteDirector`가 같은 관례다: 기존 이벤트/참조만 읽고, 판정에 개입하지 않고, **배선이 비면 조용히 비활성**된다. Rift도 그 부류다 — `ChartPlayer`가 없으면 폴백 BPM으로 돌면 되고 아무것도 터지지 않아야 한다.

## 7. 파티클은 코드가 만들 일이 아니다

`ParticleSystem`을 런타임에 `AddComponent`로 세우면 모듈 설정 수십 줄이 코드에 박히고, **조정할 때마다 리컴파일**이 필요하다. 이 프로젝트의 다른 이펙트(`VFX_CorpseBleed`·`VFX_BloodSplash`)는 전부 **프리팹/씬 오브젝트로 저작**하고 코드는 참조만 든다(§11-3 · §7-4). 파티클은 에디터에서 자식으로 붙이고 **스크립트는 그것을 알 필요조차 없다**(부모가 꺼지면 자식도 꺼진다).

- 중력: `ParticleSystem.main.gravityModifier`(인스펙터).
- "세로로 긴 가운데": `Shape = Box` + `scale`을 균열 장축 방향으로만 길게. Quad가 −90° 누워 있어 **로컬 Z가 월드 −Y**다 → 파티클을 로컬 축으로 저작하면 방향이 헷갈린다. 자식 파티클 오브젝트의 로컬 회전을 +90°(X)로 되돌려 **월드 축과 맞추는 것이 가장 덜 헷갈린다**.

---

## 8. ⚠ 튜토리얼의 "곡"은 `ChartPlayer`가 아니다 (이번 질문의 정체)

씬을 열어 보면 오디오를 내는 오브젝트가 **둘**이고 서로 다른 물건이다.

| 오브젝트 | 컴포넌트 | 클립 | 상태 |
|---|---|---|---|
| `ChartPlayer` | `ChartGen.ChartPlayer` | `debugChart` = **`Dreamer_Lv10`** | `AudioSource.clip`이 비어 있고 `PlayOnAwake = 0`. 튜토리얼은 채보를 재생하지 않는다(드릴은 `PatternDrillStep`이 `PatternHandler`에 직접 투입한다). |
| `TutorialMusic` | **`MusicPlayer`** + `AudioSource(Loop = 1)` | `guid ba99a099…` | `playOnStart = 1`. **실제로 들리는 소리가 이것이고 무한 루프다.** |

`MusicPlayer`의 헤더 주석이 이미 그 경계를 못박아 두었다:

> **왜 `ChartPlayer`를 쓰지 않는가**: 그쪽 오디오는 **채보의 시계**다(`audioSource.time`이 패턴 투입 시각을 정한다). 배경음은 판정과 아무 관계가 없으므로 그 클래스에 얹으면 "곡이 아닌 곡"이 생겨 채보 재생 경로에 분기가 는다.

여기서 나오는 결론이 셋이다.

1. **`RiftController`가 `ChartPlayer`를 자동으로 찾으면 안 된다.** 튜토리얼에는 `ChartPlayer`가 **살아 있는데 재생은 안 하고** `debugChart`만 물고 있다 — 자동 탐색은 **들리지도 않는 `Dreamer_Lv10`의 BPM**을 집는다. 배선은 언제나 손으로 한다.
2. **`MusicPlayer`에는 BPM 필드가 없다.** 튜토리얼 BGM의 템포가 지금 프로젝트 어디에도 안 적혀 있다 — 즉 "어느 값을 읽을까"가 아니라 **읽을 값이 존재하지 않는다**가 문제다.
3. **루프 오디오에서 `source.time`을 시계로 쓰면 안 된다.** 클립이 한 바퀴 돌 때마다 `time`이 0으로 되돌아가므로 발광이 **루프 경계마다 튄다**. 클립 길이가 비트의 정수배가 아니면 위상도 매 바퀴 어긋난다. 반면 `Time.time`은 단조 증가라 `PingPong`이 매끄럽게 이어진다.

### `PingPong`은 위상 앵커가 없다 — 그래서 시계를 고를 자유가 있다

`Mathf.PingPong`으로 만드는 맥박은 **주기만 있고 기준점(다운비트)이 없다.** 발광이 곡의 1박에 정확히 맞아떨어지는지 화면에서 판별할 수 없고, 몇십 ms 위상이 어긋난 것도 보이지 않는다. **주기가 BPM과 맞는 것만이 관측 가능한 사실**이다.

따라서 오디오 시각(`SongSource.time`)을 쓸 이유가 없다 — 오히려 위 3번의 튐을 들여온다. `Time.time`이면 충분하고, 필요한 것은 **BPM 숫자 하나**뿐이다. (진짜 다운비트 정렬이 필요해지면 그때 `곡 시작 시각 + beatOffset`을 앵커로 잡는다. 그건 루프 BGM에는 성립하지 않는 얘기다.)

---

## 9. 프레임 드랍 원인 — Cracks 셰이더의 픽셀 비용 (측정 기록)

### 결론

**GPU 프래그먼트 바운드이고, 범인은 `Cracks Material`의 `_ParallaxSamples = 200`이다.** `RiftController`도, 파티클도, 드로우콜도 아니다.

### 측정 (Unity 6000.3.11f1, Tutorial 씬, 플레이모드)

같은 장면 상태(균열이 화면에 들어오고 적이 배치된 시점, 삼각형 40~43만)에서 `_ParallaxSamples`만 바꿔 비교했다.

| `_ParallaxSamples` | 균열 화면 밖 | 균열 화면 안 | CPU 메인스레드(화면 안) |
|---|---|---|---|
| **200**(원래) | GPU 7.6 ~ 9.4 ms | GPU **15.8 → 24.3 ms** | 19.3 ms |
| **12** | GPU 7.5 ~ 8.5 ms | GPU **9.1 ~ 11.8 ms** | 6.5 ~ 9.0 ms |

- **드로우콜·배치·setPass는 두 경우가 거의 같다**(217~270 / 19~26 / 85~110). 즉 배칭 문제가 아니다.
- **CPU 메인스레드도 같이 절반 이하로 떨어졌다**(19.3 → 6.5~9.0 ms). 이것은 CPU 일이 줄어든 게 아니라 **메인스레드가 GPU를 기다리던 시간**이 사라진 것이다 — GPU 바운드의 전형적인 증상이고, "CPU가 느리다"로 오진하기 쉬운 지점이다.
- ⚠ **측정 환경 주의**: Unity가 포그라운드가 아니면 플레이모드가 틱하지 않아 `gpuFrameTimeMs`가 **0으로 읽힌다**(연속 캡처도 완전히 같은 프레임이 나온다). 샘플 직전에 에디터를 포커스해야 한다.

### 왜 그렇게 비싼가 (셰이더 소스 근거)

`99. External Assets/Cracks/Shaders/Cracks.shader`의 `frag`는 **시차 레이마칭 루프**다.

```
nNumSamples = (int)lerp(6, min(_ParallaxSamples, 500), 1 - dot(worldViewDir, i.normal));
for (nStepIndex = 0; nStepIndex < nNumSamples; nStepIndex++) {
    tex2D(_Mask, ...);
    fCurrHeight = Turbulence(..., _CrackAmount, offset);   // <-- 이것
}
```

그리고 `Turbulence`는 **옥타브 루프**다:

```
while (size >= 1) { val += PerlinNoise(...) * size; size *= 0.5; }
```

`_CrackAmount = 64` → `64, 32, 16, 8, 4, 2, 1` = **`PerlinNoise` 7회**. `PerlinNoise`는 `permute` 2단 + `taylorInvSqrt` + dot/lerp 다수로 대략 수십 ALU다.

**픽셀 하나당 최악 비용 = 200 스텝 × 7 옥타브 = 1,400회 Perlin + 200회 텍스처 샘플.**

여기에 이 셰이더의 성질이 겹친다:
- **`Queue = Transparent` + `Blend SrcAlpha OneMinusSrcAlpha`** → **early-Z로 걸러지지 않는다.** 균열이 덮은 모든 픽셀이 전액을 낸다.
- 샘플 수가 `1 - dot(V, N)`에 비례 → **비스듬히 볼수록 200에 가까워진다.** 균열이 세로 판이고 카메라가 위에서 내려보므로 상시 비스듬하다.
- 루프의 조기 탈출(`nStepIndex = nNumSamples + 1`)은 **웨이브 단위로는 안 먹는다** — 한 웨이브의 비용은 가장 늦게 탈출하는 레인이 정한다.
- **`_Parallax = 0.1`은 이 셰이더의 최대값**이라 시차 오프셋이 가장 길다.

즉 비용이 **균열의 화면 면적 × 시선 각도**로 붙는다 — 측정에서 GPU 시간이 균열 화면 진입에 정확히 따라 오른 이유다.

### 손댈 knob (효과 순)

1. **`_ParallaxSamples`** 200 → **12~24**. 측정상 이것 하나로 GPU 24.3 → 9.8 ms. 시차 깊이감이 계단질 수 있으니 눈으로 보며 올린다.
2. **`_CrackAmount`** 64 → 16이면 옥타브가 7 → 5로 줄어 샘플당 비용이 약 30% 준다(무늬가 굵어진다).
3. **`_Parallax`** 0.1 → 0.03~0.05. 시차 거리가 짧아져 실효 샘플 수도 같이 준다.
4. 그래도 모자라면 **화면 면적을 줄인다**(균열 스케일). 비용이 면적에 선형이다.

### 범인이 아닌 것들 (확인)

- **`RiftController`**: 프레임당 `SetVector` 1 + `SetFloat` 1. `_NoiseOffset`이 매 프레임 바뀌어도 셰이더 비용은 불변이다(캐싱하는 구조가 없어 원래부터 매 픽셀 전액이었다).
- **파티클(`RiftFallVfx`)**: rate 30 · 수명 2초 → 동시 60개, 빌보드 쿼드 60장. 드로우콜 수치가 두 조건에서 같았다.
- **배칭**: `Cracks.shader`는 CGPROGRAM이라 SRP Batcher 비호환이지만 그 대가는 **드로우콜 1개**다.

### ⚠ 지금 상태

측정을 위해 **`Cracks Material.mat`의 `_ParallaxSamples`를 200 → 12로 바꿔 놓았다**(에셋 수정). 이 머티리얼을 쓰는 다른 곳은 `99. External Assets/Cracks/Scenes/ExampleScene.unity`(에셋 데모 씬)뿐이라 게임에 부수 효과는 없다. 되돌리거나 값을 조정하는 것은 사용자 판단이다.
