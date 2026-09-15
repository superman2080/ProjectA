# Research — 균열에서 늘어나 나오는 적 등장 연출 (EnemySpawnWarp)

## 0. 요구

적이 **씬의 균열(Rift) 위치에서** 태어나, **스파게티처럼 늘어난 형태**로 뽑혀 나왔다가
자기 자리에 도착하며 정상 형태로 복원된다. 레퍼런스는 틱톡 Time Warp Scan 필터.
연출 전용이며 판정에 개입하지 않는다.

## 1. 지금 코드가 어디까지 준비돼 있나

### 1-1. 등장 연출은 이미 한 메서드에 격리돼 있다

`EnemyDirector.SpawnAt`(922행) 주석이 이 작업을 미리 예고해 두었다:

> 등장 연출은 여기 하나로 격리돼 있다. 후속 파티클 등장은 이 메서드만 갈아끼우면 되고,
> 그때는 오프셋도 절두체 판정도 통째로 필요 없어진다(자리에 바로 나타나면 되므로).

현재 흐름:

```
SpawnCluster / SpawnOneIntoStaged
      -> SpawnAt(slot, isVisible, viewPosition, delay)
           - EnemyRing.PickSpawnNearCluster: 자리 근처의 '화면 밖' 지점 from 을 고른다
           - pool.Rent(definition.Prefab)
           - view.Setup(definition, angle, slot, from, duration + delay, PlayerPosition)
```

`SpawnIntoStage`(851행)는 무리를 안 쓰는 폴백 경로(`clusterEnabled=false`)이며 같은 구조다.

**핵심 제약**: 지금 스폰 지점은 **절두체 판정으로 화면 밖**을 고른다(팝인을 숨기는 것이 목적).
이번 연출은 반대로 **보이는 것이 목적**이라 그 판정의 근거가 반전된다 — §11-6이 무리 등장 이동을
되살릴 때 겪은 것과 같은 반전이다.

### 1-2. `EnemyView.Setup`이 이미 등장 구간을 든다

```csharp
transform.position = enterPosition;      // 균열 자리가 될 후보
ScheduleMove(enterPosition, ringPosition, now, now + enterDuration);
```

즉 **"어디서 시작해 언제 자리에 선다"는 계약이 이미 있다.** 새 이동 시스템이 필요 없고,
연출은 그 구간에 **셰이더 진행값 하나**를 얹는 문제로 축소된다.

### 1-3. 머티리얼 스왑 파이프라인이 이미 있다 (재사용의 핵심)

- `Util/DissolveSwap.cs` — 렌더러 묶음을 특수 머티리얼로 갈아끼웠다 되돌린다.
  원래 텍스처를 `MaterialPropertyBlock`으로 넘겨 **겉모습은 적마다 유지**된다.
  풀 반납 시 `Restore()`를 빠뜨리면 다음 대여가 그 머티리얼을 입은 채 나온다는 함정도
  그 클래스 한 곳에 모여 있다.
- `EnemyView`: `renderers`(자식 전체 캐시) · `propertyBlock` · `SetDissolveAmount(float)` ·
  `dissolveSwap` · `ResetState()`의 원복까지 **똑같은 모양의 코드가 이미 있다.**
  소멸(Dissolve)은 "죽을 때", 이번 것은 "태어날 때"로 **시간축만 반대인 쌍둥이**다.
- `Shaders/Dissolve/EnemyDissolve.shader` — 손으로 쓴 URP HLSL. 진행을 `_Dissolve` 하나로
  **바깥에서 민다**(그래프가 `_Time`으로 스스로 돌면 시작·종료를 코드가 못 잡기 때문).
  **vertex 단계를 이미 직접 쓰고 있어 정점 변형을 넣을 자리가 있다.**

평상시 적 머티리얼은 툰(`CelShader`)이고 `_Dissolve`도 `_Warp`도 없다 — 그래서
**갈아끼우는 단계가 있어야 연출이 화면에 보인다**(DissolveSwap 헤더 주석).

### 1-4. 균열(Rift)의 현재 상태

- `Effect/RiftController.cs` — Cracks 셰이더의 `_NoiseOffset`·`_Emission` 두 값만 미는 순수 연출.
  **켜고 끄는 일을 하지 않는다**(그건 `Encounter.shimmerVfx`의 `SetActive`가 한다).
- 씬 배선은 현재 `Tutorial.unity`에 `Rift` 하나(+`RiftFallVfx`). 스테이지별 씬은 아직 미구현(§9).
- 균열은 **위치를 알려 주는 오브젝트가 아직 아니다** — 스폰 원점으로 쓰려면 그 목록을
  누가 드는지 정하는 것이 이번 설계의 첫 결정이다.

### 1-5. 건드리면 안 되는 이웃

- **레이어 스왑 두 건**: `AmbushOutline`(§11-8, 기습 강조)·`Silhouette`(§14, 마무리 실루엣).
  둘 다 `RenderObjects`로 **그 레이어만 한 번 더 그린다**. 연출용 머티리얼이 그 패스에
  섞이면 테두리/실루엣이 깨진다. 특히 §11-8은 깊이 상태를 건드려 데인 전례가 있다.
- **풀 반납 원복**: `ResetState`에서 되돌리지 않으면 다음 대여가 늘어난 채 나온다
  (아웃라인·피 파티클과 같은 부류의 함정이 이미 세 번 나왔다).
- **곡 도중 `Instantiate` 금지**(§5): 연출용 파티클·머티리얼은 `PrepareStage` 프리웜에 얹어야 한다.
- **`Time.timeScale` 금지**(§7-3): 연출 시계는 `Time.time`.
- **사망 1 : 스폰 1**(§11-6): 곡 도중 스폰은 적이 죽을 때마다 일어난다 —
  즉 이 연출은 **전투 한복판에서 계속 보인다**. 길면 시야를 먹고, 짧으면 안 보인다.

## 2. 레퍼런스 조사

### 2-1. 기법 그 자체 (확인된 자료)

Time Warp Scan은 **슬릿스캔(slit-scan)**의 소비자용 변종이다 — 한 프레임에 한 줄씩만
갱신하고 나머지는 지나간 시각의 픽셀을 유지해, 스캔 라인 뒤쪽이 "그 시각에 얼어붙은"
상을 남긴다. 움직이는 피사체가 라인을 통과하면 몸이 길게 늘어난 것처럼 보인다
([Slit-scan photography](https://en.wikipedia.org/wiki/Slit-scan_photography),
[Cycling '74 time warp map](https://cycling74.com/forums/slit-scan-time-warp-map)).

원리대로 하려면 **과거 프레임 N장**이 필요하다(마스크 임계값으로 프레임별 평면을 합성하는 방식 —
같은 포럼 글). 3D 캐릭터에 그대로 적용하면 **스키닝된 포즈 히스토리 N개**를 들어야 해서
이 프로젝트의 예산(곡 도중 할당 금지, 적 다수 동시 존재)에 맞지 않는다.

게임에서 쓰이는 실용적 근사는 **정점 변형**이다 — 스캔 평면을 기준으로 아직 통과하지 않은
정점을 한 축으로 밀어 "늘어남"을 만든다. Unity에서 이 축은 표준 기법이다
([Unity: Creating an Interactive Vertex Effect using Shader Graph](https://blog.unity.com/technology/creating-an-interactive-vertex-effect-using-shader-graph),
[Shader Graph Basics — Vertex Shaders](https://danielilett.com/2024-02-13-tut7-7-intro-to-shader-graph-part-5/)).
즉 **"진짜 슬릿스캔"이 아니라 "한 장의 메쉬를 평면으로 늘이는 것"**이 우리가 살 수 있는 물건이고,
정지 화면에서 보면 결과는 사실상 구분되지 않는다(원본 필터도 피사체가 한 방향으로 움직일 때만
그럴듯하다).

### 2-2. 게임 사례 (검색으로 확증되지 않음 — 설계 판단용 참고)

웹 검색으로는 상용 게임의 이 연출에 대한 **기술 해설 자료를 찾지 못했다**(VFX 브레이크다운은
대부분 영상/디스코드에 있다). 아래는 기억에 근거한 정리이며 **문헌 근거가 아니다** —
설계 결정의 근거로 쓸 때는 실제 영상으로 다시 확인할 것:

| 사례 | 등장 방식 | 이 프로젝트에 주는 시사점 |
|---|---|---|
| Doom(2016/Eternal) 데몬 텔레포트 | 포탈 평면에서 몸이 **끌려 나오듯** 쏟아진다 | 원점(포탈)이 화면에 같이 보여야 "어디서 왔는지"가 읽힌다 |
| Scarlet Nexus 「괴이」 | 이상 공간에서 늘어난 실루엣으로 강하 | 늘어남 자체를 **종족 정체성**으로 쓴다 — 우리 「허물」과 결이 같다 |
| Control 히스 | 왜곡 + 디졸브(늘어남 없음) | 늘어남 없이도 성립한다 = 이번 연출은 **선택된 사치**다 |
| Persona 5 섀도우 | 가면이 펼쳐지는 **스켈레탈 애니메이션** | 셰이더 대신 클립으로도 가능하지만 적 종류마다 저작이 필요 |
| Hi-Fi RUSH | 비트에 맞춘 플래시 등장 | **박자 정렬**이 리듬게임에서 값이 싸고 효과가 크다 |

**결론**: 3D 게임에서 "슬릿스캔 그대로"를 쓴 사례는 확인하지 못했고, 실제로 쓰이는 것은
**정점 늘이기 + 스캔 라인 발광**의 근사다. §2-1의 기법 선택과 모순되지 않는다.

## 3. 구현 후보

### A. 머티리얼 스왑 + 정점 늘이기 (추천)

`EnemyDissolve.shader`의 쌍둥이 `EnemySpawnWarp.shader`를 만들고, `DissolveSwap`을 그대로 써서
등장 구간 동안만 입힌다. 진행값 `_Warp`(1 -> 0)를 `EnemyView`가 `Time.time`으로 민다.

- 정점: 스캔 평면(원점 = 균열, 법선 = 균열 -> 자리 방향)보다 **뒤쪽 정점만** 원점 쪽으로
  당겨 붙여 늘인다. `_Warp`가 0이 되면 변형이 정확히 0 = **원형 복원이 공짜로 보장된다**.
- 픽셀: 스캔 평면 근처에 발광 띠(디졸브의 `_EdgeColor` 관용구 재사용).
- 비용: 셰이더 1개 + `EnemyView` 필드 몇 개. **`DissolveSwap`·`propertyBlock`·`ResetState` 원복이
  전부 이미 있다.**
- 위험: 스키닝된 메쉬에 정점 변형을 걸면 아웃라인 셸(`CelOutline`)과 어긋난다 -> 연출 구간에는
  아웃라인을 안 쓰는 편이 안전(스왑이 이미 그렇게 만든다).

### B. 파티클만 (늘어남 없음)

균열에서 입자가 솟고 그 자리에 적이 디졸브 인. 가장 싸고 §11-8/§14와 충돌이 없다.
요구한 그림은 아니다 — **A가 비싸다고 판명될 때의 폴백**으로 적어 둔다.

### C. 진짜 슬릿스캔(포즈 히스토리 N장)

`SkinnedMeshRenderer.BakeMesh`를 프레임마다 떠서 겹쳐 그린다. 원리에 가장 충실하지만
곡 도중 할당·드로우콜이 적 수만큼 곱해진다. **§5의 프리웜 규율과 정면 충돌**이라 기각.

## 4. 결정이 필요한 것 (Plan 이전)

1. 스폰 원점: 씬의 균열 오브젝트를 **누가 목록으로 드는가** — 그리고 균열이 하나뿐인 지금
   전원이 같은 지점에서 나오는가.
2. 절두체 판정의 운명: 화면 밖 스폰을 **폐기**하는가(연출이 보여야 하므로), 유지하는가.
3. 늘어난 채 **이동**하는가(균열 -> 자리), 자리에 선 채 **수축만** 하는가.
4. 연출 길이와 곡 시작(카운트다운 3초, 무리 전원 동시)에서의 취급.
5. 연출 중 그 적이 표적·기습 후보가 될 수 있는가.

