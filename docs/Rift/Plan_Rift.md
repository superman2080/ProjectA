# Plan — Rift (균열 오브젝트 연출)

## 결론 한 줄

**신규 파일 하나(`RiftController.cs`, ~60줄) + 씬 작업 셋.** BPM 문제는 `docs/MusicPlayback/`의 통합(방안 C)이 이미 풀었다 — 참조가 **한 칸**이다. 파티클은 코드가 만들지 않고, 켜고 끄는 창구는 이미 있는 `Encounter.shimmerVfx`를 쓴다.

## 범위에서 뺀 것 (근거)

| 뺀 것 | 근거 |
|---|---|
| 런타임 `ParticleSystem` 생성 | 모듈 설정이 코드에 박히고 조정마다 리컴파일. 이 프로젝트의 이펙트는 전부 에디터 저작이다(Research §7). **스크립트는 파티클 참조조차 안 든다** — 부모가 꺼지면 자식이 같이 꺼진다. |
| Rift on/off · 상태 판단 | `Encounter.shimmerVfx`가 이미 `SetActive`로 한다(Research §4). §13의 "일렁임 VFX on/off"가 그것이다. |
| 오디오 시각(`SongSource.time`)에서 파생하는 맥박 | `PingPong`에는 위상 앵커가 없어 관측 가능한 사실이 **주기**뿐이다. 루프 BGM에서는 오히려 루프마다 발광이 튄다(Research §8). |
| `IBeatClock` 같은 인터페이스 / 비트 이벤트 | `MusicPlayerBase`가 이미 그 자리다(`docs/MusicPlayback/`). 비트 이벤트는 구독자가 0이다. |

---

## Step 0 — BPM의 출처 (해결됨)

`docs/MusicPlayback/Plan_MusicPlayback.md` **방안 C**로 해결했다(구현 완료). `MusicPlayerBase`가 생겨
`ChartPlayer`(채보의 시계)와 `MusicPlayer`(루프 BGM)가 **같은 타입**이 되었고, `Bpm`을 그 타입이 든다:

- `ChartPlayer.Bpm => ActiveChart?.bpm`
- `MusicPlayer.Bpm => bpm`(신규 필드 — **템포가 클립의 주인 옆에 적힌다**)
- **0 이하는 "모른다"**는 뜻이다.

그래서 `RiftController`는 **`MusicPlayerBase` 한 칸**을 배선한다. 예전 Plan에 있던 참조 둘 · 우선순위 규칙 ·
`OnValidate` 둘 다 배선 경고가 **전부 사라졌다.**

⚠ **`FindObjectOfType`은 여전히 금지다.** `Tutorial`에는 재생하지 않는 `ChartPlayer`가 `m_Enabled: 1`로
살아 있고 `debugChart = Dreamer_Lv10`을 물고 있다 — 자동 탐색은 **들리지 않는 곡의 BPM**을 집는다.
이 위험은 통합으로 사라지지 않는다.

---

## Step 1 — 데모 컴포넌트 제거
- [x] **이미 제거돼 있었다.** 라이브 씬의 `/Rift`는 `Transform`/`MeshFilter`/`MeshRenderer` 셋뿐이다(사용자가 오브젝트를 다시 배치하면서 `CrackParameterHandler`·`MeshCollider`가 같이 빠졌다).
  - ⚠ **디스크 YAML은 낡았다** — `Tutorial.unity`가 미저장 상태라 파일에는 아직 옛 구성(위치 `(0,0,-1.56)`, `CrackParameterHandler`, 다른 머티리얼)이 적혀 있다. **씬 상태는 MCP로 읽어야 맞다.**
  - 라이브 값: 위치 `(0, 5.0, 11.61)`, 스케일 1, 머티리얼 `99. External Assets/Cracks/Materials/Cracks Material.mat`(guid `144ca03c…`).

## Step 2 — `RiftController.cs` 작성
- [x] `Assets/02. Scripts/Effect/RiftController.cs` 신규(약 95줄, 주석 포함). Plan대로 필드·`Awake`·`Update`·`OnDisable`·프로퍼티 ID 캐시 전부 구현.
- [x] 리뷰 반영: `emissionRange`(Vector2) → `maxEmission`(`[Range]` float). 씬의 기본값이 2로 같아 **재배선 없음**.
- [x] 컴파일 에러 0건.

## Step 3 — 자식 파티클 저작
- [x] `/Rift/RiftFallVfx` 생성(MCP). **월드 회전을 항등으로 만들기 위해 루트에서 만들어 `world_position_stays = true`로 재부모화했다** — 이러면 `Rift`의 회전을 몰라도 파티클의 로컬 축이 월드 축과 같아진다(Research §7의 목적을 회전값 없이 달성).
- [x] 설정값(전부 읽어 확인): `ShapeModule.type = 5`(Box) · `ShapeModule.m_Scale = (0.2, 3, 0.2)`(세로로 긴 기둥 = 균열 장축이 월드 Y) · `gravityModifier = 1` · `startSpeed = 0` · `startLifetime = 2` · `startSize = 0.08` · `rateOverTime = 30` · `looping`·`playOnAwake` 기본 true.
- [x] **⚠ 파티클 머티리얼이 URP에서 마젠타로 깨져 나왔다**(빌트인 `Default-ParticleSystem`이 URP 셰이더가 아니다). 프로젝트에서 이미 동작하는 `M_BloodDrop`(URP Particles 셰이더)을 복제해 `03. Prefabs/Effects/Materials/M_RiftEmber.mat`을 만들고 배선했다 — `_BaseMap`은 `Cracks Material`이 쓰는 `Flare(2).png`, 색은 그 머티리얼의 `_CoreColor`(1, 0.732, 0). **화면에서 앰버색으로 확인됨.**
  - 이건 저작이 아니라 **깨진 기본값 수복**이다. 수명·크기·개수·색 전부 인스펙터에서 자유롭게 바꾸면 된다.

## Step 4 — 씬 배선
- [x] `/Rift`에 `RiftController` 부착.
- [x] `musicSource` = `/Manager/TutorialMusic`(`MusicPlayer`). `targetRenderer`는 비워 뒀다(`Awake`의 `GetComponent` 폴백).
- [ ] `TutorialMusic.bpm`에 `Citadel (Short Loop 1).wav`의 **실제 템포**를 적는다. 지금은 기본값 **120**이다 — 그 곡의 BPM을 모르므로 사용자가 채워야 한다.
- [ ] `Encounter`가 있는 탐색 씬의 `shimmerVfx` 배선(그 씬들이 아직 없다 — CLAUDE.md 9).

## Step 5 — 검증
- [x] 컴파일 에러 0건 · 플레이모드 진입/종료 시 런타임 에러 0건.
- [x] **머티리얼 인스턴스 경로 확인** — 플레이 중에 `Cracks Material.mat` 에셋의 `_NoiseOffset`이 `(0,0,0,0)`로 그대로다. `sharedMaterial`을 쓰고 있었다면 누적값이 에셋에 박혔을 것이다.
- [x] 파티클이 균열 **세로 중앙선에서 아래로** 떨어진다(게임 뷰 확인).
- [x] 플레이모드 종료 후에도 부착·배선이 남아 있다.
- [ ] **발광 왕복 · 무늬 흐름 · BPM 2배 시 왕복 2배** — ⚠ **관측하지 못했다.** Unity가 포그라운드가 아니면 플레이모드가 틱하지 않아 연속 캡처가 **완전히 동일한 프레임**으로 나온다(파티클 위치·캐릭터 포즈까지 같았다). 에디터를 띄운 상태에서 확인해야 한다.
- [ ] BGM 루프 경계에서 발광이 튀지 않는지(같은 이유로 미관측).
- [ ] `musicSource`를 비웠을 때 `fallbackBpm`으로 도는지.
- [ ] `Rift.SetActive(false)` → 파티클·발광 정지, 재활성 시 0에서 이어짐.

## ⚠ 저장하지 않았다
`Tutorial.unity`는 **미저장 상태**다(사용자의 `Rift` 배치 작업이 들어 있었고, 거기에 이번 변경이 얹혔다). 저장은 사용자가 확인 후 한다.

## Step 6 — 뒷면 렌더러를 한 컨트롤러가 함께 몬다

`Rift/Backside`(앞면과 대칭인 뒷면)에 `RiftController`를 하나 더 붙여 두었더니
컨트롤러마다 머티리얼 인스턴스가 따로 생겨 **진실의 원천이 둘**이 됐다.
값이 같아 지금은 맞아 보이지만, 한쪽 인스펙터 값만 바꾸면 앞뒤가 조용히 갈린다.

- [x] `RiftController.additionalRenderers`(`MeshRenderer[]`) 추가.
      `Awake`에서 `targetRenderer.material`로 만든 **인스턴스를 그 렌더러들에 대입**한다
      (게터가 아니라 세터라 사본이 더 생기지 않는다).
- [x] `Backside`의 `RiftController` 제거 — 컴포넌트가 `Transform`/`MeshFilter`/`MeshRenderer` 셋만 남았다.
- [x] `/Rift`의 `additionalRenderers[0]` = `Backside`의 `MeshRenderer` 배선.
- [x] 컴파일 에러 0건.
- [x] **플레이모드 검증**: 두 렌더러의 `sharedMaterial`이 **같은 인스턴스**
      (`ReferenceEquals = true`, id 동일, 이름 `Cracks Material (Instance)`)이고
      `_Emission`이 양쪽 `0.1600`으로 동일. `Backside`의 `RiftController` 개수 0.
- [x] **에셋 비오염 확인**: `Cracks Material.mat`의 `_NoiseOffset`이 `(0,0,0,0)` 그대로고,
      git diff가 `_CoreColor` 한 줄(이 작업 이전의 편집)뿐이다.
- [x] 씬 저장.

### ⚠ 검사할 때 `renderer.material`을 쓰면 안 된다
그 게터는 **아직 인스턴스화되지 않은 렌더러에 사본을 만들어 대입한다**.
검증 중에 그걸로 읽었다가 `same=False`가 나와 오진할 뻔했다 — 읽을 때는 `sharedMaterial`이다
(대입된 인스턴스를 사본 없이 그대로 돌려준다).

### 대칭 배치에 관한 측정 기록 (Backside의 트랜스폼은 건드리지 않았다)
메쉬가 빌트인 **Plane**(121정점, 로컬 XZ 평면, 법선 +Y)이라 `euler (0,0,180) + scale (-1,1,1)`은
**정점 위치가 앞면과 완전히 동일**(delta 0)하고 UV도 월드 기준으로 같으며 법선만 `(0,-1,0)`으로 뒤집힌다.
즉 뒤에서 보면 같은 균열을 반대편에서 본 그림이 된다. 180도 회전 계열은 전부 UV를 뒤집으므로
(X180 → V 반전, Z180 단독 → 면이 안 뒤집힘) **평면 메쉬에서 "같은 자리·같은 UV·반대 면"은
det < 0 없이 표현할 수 없다.** 음수 스케일이 TBN 손잡이를 깨거나 시차를 역전시키지 않는다는 것도
정점·TBN 덤프와 앞뒤 캡처로 확인했다(앞쪽 뷰 A/B가 픽셀 단위로 동일 = 컬링 정상).
