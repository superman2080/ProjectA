# Research: 적 사망 연출 — 절단 + 시체 래그돌 (레퍼런스 수집)

> 목적: "임팩트 프레임에 맞춰 절단되고, 시체처럼 관절이 축 늘어지는" 연출을 위한 **타 게임 기법 조사**.
> 현재 구현 분석은 `docs/SliceTarget/`, `docs/EnemyCombat/` 참조. 이 문서는 **외부 레퍼런스 전용**이다.

---

## 0. 조사 전 정리 — 문제는 두 개다

| 원하는 그림 | 필요한 기술 | 현재 프로젝트 상태 |
|---|---|---|
| 갈라지는 절단 | 메쉬 분할 + 단면(cap) | O 있음 (`MeshSliceBaker`, 정적 메쉬 사전 굽기) |
| 축 늘어진 시체 | 스켈레톤 + 관절 물리 | X 전무 (`CharacterJoint`/`Ragdoll` 0건) |

핵심 충돌: **현재 굽기는 `BakeMesh`로 스킨을 벗겨 정적 메쉬로 만든다** (`MeshSliceBakerWindow.cs:604`).
그 순간 boneWeights·bindposes가 버려지므로 조각은 원리상 "늘어질" 수 없다 — 딱딱한 돌덩이 N개다.

아래 레퍼런스들은 이 충돌을 각자 다른 방식으로 푼다.

---

## 1. Metal Gear Rising: Revengeance — "Dynamic Skeleton" (가장 직접적인 레퍼런스)

출처: [simonschreibt.de — Metal Gear Rising: Slicing](https://simonschreibt.de/gat/metal-gear-rising-slicing/)

### 핵심 발견
- **완전 절차적 CSG가 아니다.** 플래티넘은 **하이브리드 수작업 시스템**을 썼다 —
  "한 방향으로 자르고 애니메이션 붙이고, 다른 각도로 자르고 또 애니메이션 붙이고"를 개발 내내 반복.
  즉 **절단 방향별 리액션이 저작 데이터**다.
- **본 구조가 사전 정의된 절단 지점에서 쪼개진다** ("Dynamic Skeleton").
  → 잘리면 스켈레톤 자체가 분리되어 보인다. **본이 실제로 잘리는 유일한 레퍼런스.**
- **부위마다 사전 정의된 컷 포인트**가 있어, 절단 방향마다 다른 단면 텍스처를 쓴다.
  "가로로 자르면 오렌지, 세로로 자르면 수박" — 방향별 단면 매핑.
- **지오메트리가 2겹**이다: 바깥 껍데기 + 안쪽 살점 레이어.
  안쪽은 저폴리이고, 얕게 베였을 땐 살점이 안 보이도록 깊이 처리가 들어간다.
- 성능: 조각은 **약 10초 후 페이드아웃**. 물리 오브젝트가 너무 많으면 **새 절단을 아예 막는 것으로 보인다**.
- 한계: **사이보그만 잘린다.** 유기체 캐릭터는 절단 대상이 아니다.

### 우리 프로젝트에 주는 시사점
- 우리가 이미 "사전 굽기 + 평면 저작"을 하고 있다 → **MGR과 같은 계열**이다. 방향이 틀리지 않았다.
- 우리에게 없는 것: ① 방향별 단면 텍스처 ② 안쪽 살점 레이어 ③ **본 분리**.
- MGR도 조각 수명 제한 + 동시 절단 상한을 뒀다 → 우리 `debrisLifetime`/`maxActivePieces`와 같은 규율.

---

## 2. Overgrowth (Wolfire) — 부분 래그돌(Partial Ragdoll)

출처: [Wolfire Forums — DISMEMBERMENT!](https://forums.wolfire.com/viewtopic.php?f=13&t=12277&p=141754), [Steam News — Ragdoll Physics Update](https://store.steampowered.com/news/app/25000/view/2883948831518627544), [ModDB a201 devlog](https://www.moddb.com/games/overgrowth/news/new-overgrowth-a201-video-devlog)

### 핵심 발견
- **"팔다리가 베이면 그 팔다리의 관절만 물리 시뮬레이션으로 넘긴다. 나머지 몸은 계속 애니메이션이 제어한다."**
  이게 부분 래그돌의 정의다. Overgrowth는 이미 있던 **active ragdoll** 기능 덕에 이 전환이 공짜였다.
- 절단 자체는 "detachable limbs는 어려운 작업"이라며 커뮤니티 요청에도 미구현으로 남았다.
  → **래그돌은 쉽고, 절단은 어렵다**는 증언.
- 무기 히트별 리액션 오버라이드 시스템, 래그돌 전환 스무딩을 별도로 개발.

### 시사점
- **전신을 다 늘어뜨릴 필요가 없다.** 잘려나간 쪽만 물리로 넘기고 나머지는 애니메이션 유지 = 비용 최소.
- 다만 우리는 "죽는 순간"이라 전신 전환이 맞을 수 있다. 부분 래그돌은 **비치명타 리액션**에 더 맞는 도구다.

---

## 3. 래그돌 블렌딩 — "전환을 안 보이게 하는 게 진짜 문제"

출처: [game-mechanics-optimizations/117_ragdoll_blending.md](https://github.com/raduacg/game-mechanics-optimizations/blob/main/117_ragdoll_blending.md), [MoCap Online — Ragdoll Physics in Games](https://mocaponline.com/blogs/mocap-news/ragdoll-physics-animation-guide), [Daydreamsoft — Ragdoll Blending](https://www.daydreamsoft.com/blog/ragdoll-physics-blending-with-animation-creating-realistic-character-reactions-in-modern-games)

### 핵심 발견
- **"래그돌 구현에서 가장 어려운 문제는 물리를 켜는 게 아니라, 전환을 플레이어에게 안 보이게 하는 것이다.
  애니메이션에서 물리로 갑자기 스냅하면 끔찍해 보인다."**
- 해법: **블렌드 인 구간**. 애니메이션 포즈와 물리 포즈가 둘 다 최종 결과에 기여하는 구간을 둔다.
  전문 구현은 블렌드 웨이트를 **0.1~0.3초(통상 0.2초)** 에 걸쳐 0→1로 올린다.
- **부분 래그돌**: 팔을 맞으면 팔만 늘어지고 몸은 서 있음. 부위별 반응(다리=휘청, 가슴=뒤로 넘어짐).
- **Active ragdoll**: 물리 바디가 애니메이션 포즈를 *따라가려 시도*하면서 외력에 반응.
  블렌드가 "죽음"이 아니라 **"의식이 서서히 빠져나감"** 을 표현한다.
- 업계 최고 사례로 **RDR2** 언급 — 쓰러지기 전 잠깐 균형을 잡으려 하고, 데미지 타입에 따라 블렌드가 0.2~0.6초로 달라진다.

### 시사점 — 우리에게 가장 중요한 항목
우리는 **`EnemyView.Kill()`이 `Deadline` 프레임에 정확히 불린다.** 리듬게임이라 이 시각이 음악과 묶여 있다.
- 블렌드 구간 0.2초를 넣으면 **절단 순간과 늘어짐 시작이 어긋난다.** 칼은 지나갔는데 몸은 0.2초 뒤에 힘이 빠진다.
- 반대로 블렌드 없이 즉시 전환하면 위 인용대로 "끔찍해 보인다".
- **절단은 블렌드가 필요 없을 수 있다** — 잘리는 순간 갑자기 힘이 빠지는 게 오히려 자연스럽다.
  RDR2식 "균형 잡으려 함"은 총상용이고, 참수엔 안 맞는다. → **즉시 전환이 우리 케이스에선 정답일 가능성이 높다.**

---

## 4. LimbHacker — 스킨드 메쉬를 런타임에 자르는 Unity 구현체

출처: [GitHub — JoeCooper/LimbHacker](https://github.com/JoeCooper/LimbHacker)

### 핵심 발견
- **"스킨드 메쉬 캐릭터를 자른다."** sever 한 번에 원본 지오메트리를 나눠 가진 **오브젝트 2개**를 산출.
  **본 계층이 원본과 일치해야 한다** — 즉 잘린 뒤에도 양쪽이 같은 스켈레톤에 붙는다.
- 알고리즘: NVIDIA John Ratcliff의 **plane-triangle splitting**. 평면-삼각형 교차점을 찾아 구멍을 새 지오메트리로 메움.
- **제약**:
  - **닫힌(closed) 메쉬**를 가정. 겹친 지오메트리·자기교차 삼각형에 취약. → 우리 `MeshSliceBaker.Validate`와 같은 제약.
  - **모든 본이 좋은 결과를 내지 않는다.** 본을 "unseverable"로 표시하는 기능이 있다. 쇄골은 "흉한 결과"로 악명.
  - infill 알고리즘이 둘 — "sloppy"는 복잡한 단면에서 실패, "meticulous"는 불완전한 절단을 **아예 거부**한다.
- 성능: 클라이언트 자금으로 **스레딩 + 컬렉션 풀링**을 추가했다 → 원래 알고리즘이 프레임 안에 안 들어갔다는 뜻.

### 시사점
- **"본 유지하며 자르기"는 실제로 존재하고 상용 수준으로 검증됐다.** 우리 `MeshSliceBaker`에 boneWeights/bindposes를 실어 보내면 같은 결과가 된다.
- 다만 **런타임 절단은 스레딩이 필요할 만큼 무겁다.** 우리는 리듬게임이라 프레임 히치 = 판정 손실.
  → **사전 굽기 유지 + 조각에 스키닝 정보만 보존**이 우리에겐 맞다. 런타임 절단은 하지 말 것.
- "unseverable 본" 개념은 우리도 필요할 수 있다 — 절단 평면이 지나가면 안 되는 부위.

---

## 5. Blade & Sorcery — 절단 지점을 저작 데이터로 고정

출처: [Nexus — Simple Dismemberment](https://www.nexusmods.com/bladeandsorcery/mods/1289?tab=posts), [Nexus — Better Decaps](https://www.nexusmods.com/bladeandsorcery/mods/4830), [Steam Discussion](https://steamcommunity.com/app/629730/discussions/1/1743355067107744111/?ctp=2)

### 핵심 발견
- **임의 위치에서 못 자른다.** 어깨·손목·무릎 등 **정해진 "dismemberment point"** 에서만 분리된다.
  물리 기반 VR 검술 게임인데도 자유 절단이 아니다.
- 절단 설정이 **크리처 래그돌 파일(JSON)** 안에 산다 → 래그돌 정의와 절단 정의가 **한 데이터**다.
- 개발 측 언급: "팔을 자르려면 래그돌을 단일 개체가 아닌 방식으로 다뤄야 한다" — 이게 기술적 난점.

### 시사점
- 물리 시뮬레이션의 정점인 게임조차 **절단은 저작 지점으로 제한**했다. 우리가 사전 굽기 하는 게 타협이 아니라 업계 표준.
- **래그돌 데이터와 절단 데이터를 같은 에셋에 두는 구조**를 참고할 만하다.
  우리 대응: `EnemyDefinition.DeathSliceSet` 옆에 래그돌 설정도 놓는 식.

---

## 6. Mortal Kombat (NetherRealm) — 부위별 분리 메쉬

출처: [Polycount — About Mortal Kombat gore system](https://polycount.com/discussion/210903/about-mortal-kombat-gore-system), [foro3d — NetherRealm customizes UE4 for MK1](https://foro3d.com/en/2026/february/netherrealm-customizes-unreal-engine-4-to-create-mortal-kombat-1.html), [Playbite — MK-like dismemberment in Unity](https://www.playbite.com/q/how-to-create-a-dismemberment-system-like-mortal-kombat-unity)

### 핵심 발견
- **캐릭터 모델이 애초에 부위별 분리 메쉬로 세팅**된다. 히트 감지 스크립트가 분리된 부위에 힘을 준다.
- 분리 메쉬 방식에서 **추가로 만들 게 단면(내장/해부 단면) 지오메트리뿐**이라는 점이 장점으로 꼽힌다.
- 절단 자체보다 **부위별 전용 애니메이션·VFX 저작량**이 시스템의 본질이라는 평.
- MK1은 UE4를 쓰되 렌더링 코어를 대폭 개조 — 고어 표현을 위한 엔진 수준 통제.

### 시사점
- "미리 나눠진 메쉬 + 단면만 추가"는 **우리 `MeshSliceBaker` 산출물과 정확히 같은 형태**다.
- 차이는 우리가 굽기를 **툴로 자동화**했다는 점(MK는 아티스트 수작업). 우리 쪽이 유리하다.
- 다만 MK도 **부위마다 전용 리액션 저작**이 들어간다 → 연출 품질은 결국 저작량이다.

---

## 7. Hellish Quart — 전면 Active Ragdoll 검술

출처: [hellishquart.com](https://www.hellishquart.com/), [Wikipedia](https://en.wikipedia.org/wiki/Hellish_Quart), [GOG DB Release Notes](https://www.gogdb.org/product/1731372333/releasenotes)

### 핵심 발견
- 개발자 Kubold(애니메이터 출신). **수천 개의 모캡 동작이 active ragdoll을 구동**한다.
- 칼끼리 엔진 물리로 실제로 부딪히고 막힌다.
- **"래그돌 물리를 더 쓸지, 더 '단단한' 애니메이션을 쓸지" 플레이어 설정이 있다.**
  → 개발사도 물리와 애니메이션의 균형에 정답이 없다고 보고 **옵션으로 넘겼다.**
- 한 방에 라운드가 끝나는 치명상 모델.

### 시사점
- Active ragdoll 전면 채택은 **모캡 물량이 전제**다. 우리 규모엔 과하다.
- "물리 강도 슬라이더"라는 발상은 참고할 만하다 — 우리도 래그돌 세기를 인스펙터 값 하나로 빼두면 튜닝이 쉽다.

---

## 8. 성능 — 래그돌은 비싸고, 비싼 이유가 정해져 있다

출처: [Unity Game Optimization — Optimize Ragdolls](https://www.oreilly.com/library/view/unity-2017-game/9781788392365/b671c859-0b30-4d6f-8ff1-9294e531789c.xhtml), [— Avoid inter-Ragdoll collisions](https://www.oreilly.com/library/view/unity-game-optimization/9781838556518/342836c4-148b-4789-badc-636e16c88b90.xhtml), [GameDev.net — How We Optimized Ragdoll Death Animation in Unity](https://gamedev.net/tutorials/programming/general-and-gameplay-programming/how-we-optimized-ragdoll-death-animation-in-unity-r4717), [Unity Discussions — pooling ragdolls](https://discussions.unity.com/t/any-way-to-effectively-pool-ragdolls/564397)

### 핵심 발견
- **래그돌끼리 충돌시키면 비용이 지수적으로 는다.** 관절 하나가 충돌하면 솔버가 그 관절에 연결된 모든 관절,
  또 거기 연결된 모든 관절의 속도를 계산해야 해서 **두 래그돌 전체를 여러 번 완전히 풀어야 한다.**
  → **해법은 Collision Matrix로 래그돌 간 충돌을 끄는 것.**
- 움직이거나 다른 물체와 충돌 중인 래그돌이 많으면 솔버 반복 횟수 때문에 성능이 크게 무너진다.
- **극단적 최적화: 래그돌 애니메이션을 미리 녹화한다.** 캐릭터마다 4~5개 녹화해두고 죽을 때 랜덤 재생.
  물리를 아예 안 돌린다.
- 래그돌 풀링은 알려진 난점 — 재대여 시 본 포즈 복구가 문제.

### 시사점 — 우리 프로젝트에 직결
- **무쌍 게임이라 시체가 동시에 여러 구 눕는다.** 래그돌 간 충돌 끄기는 **선택이 아니라 필수**.
  → 다행히 `SlicePiece.IgnoreSelfCollision(layer)`가 이미 같은 트릭을 쓴다. `pieceLayer` 재사용 가능.
- 현재 `EnemyDirector`의 `maxActivePieces = 96`은 **조각 수 기준**이라 래그돌엔 안 맞는다.
  래그돌은 **관절 수 × 시체 수**가 비용이므로 별도 상한 필요.
- **"미리 녹화" 기법**은 우리에게 뜻밖에 잘 맞을 수 있다 —
  적이 항상 **같은 임팩트 프레임 포즈에서, 같은 방향으로** 죽기 때문에 변주가 적다.
  다만 "매번 똑같이 죽는다"가 무쌍에서 티가 날 위험.
- 풀링 복구는 **우리도 그대로 밟을 함정**이다(`EnemyDirector.ReleaseEnemy` → `EnemyView.ResetState`).
  본 로컬 포즈 캐시 + `isKinematic` 복구 + `animator.enabled` 복구가 없으면 다음 대여 때 뒤틀린 채 나온다.

---

## 9. 부수 발견 (조사 중 확인된 우리 코드 문제)

- **`EnemyView.Kill()`의 Death 스테이트는 절대 안 보인다.**
  `EnemyView.cs:217`에서 `CrossFade(deathStateName)` 직후 `SetRenderersEnabled(false)`.
  절단 경로에서 `deathStateName`은 사실상 죽은 배선이다.
- **굽기 툴의 `deathPoseTime`은 수동 동기화**다(`MeshSliceBakerWindow.cs:29-33`).
  런타임 실제 킬 포즈는 `Pattern.EnemyAttack`(`ClipAlignment.Clip` + `ImpactTime`)에서 **파생 가능**하다.
  단 두 구멍: ① `cue.attacker == Player`면 적은 Idle이라 EnemyAttack이 안 쓰인다
  ② `DeathSliceSet`은 `EnemyDefinition`당 1개인데 패턴은 N개 → 포즈 하나로 다 못 덮는다.

---

## 10. 정리 — 선택지 4개

> **확정 요구사항: 단면이 뚫려 보이면 안 된다.** (2026-08-01, 사용자 결정)
> 이 한 줄이 셰이더 클립 기반 안(B)을 탈락시켰다. 캡(cap)이 반드시 있어야 한다.

| 안 | 절단 | 늘어짐 | 단면 | 작업량 | 판정 |
|---|---|---|---|---|---|
| **A. 래그돌만** (절단 포기) | ✗ | O 전신 | — | 최소 | ✗ 절단이 게임의 핵심이라 포기 불가 |
| **B. 셰이더 클립 하이브리드** | △ | O | X **뚫림** | 소 | ✗ **요구사항 위반으로 탈락** |
| **C. 스키닝 보존 절단** | O | O | O | 중 | O **채택** |
| **D. 본 분리** (스켈레톤 자체를 쪼갬) | O | O | O | 최대 | 보류 — MGR급. 저작량이 본질이라 스코프 초과 |

### 채택: C — 스키닝 보존 절단

코드 확인 결과 **C가 예상보다 가깝다.** 캡 생성이 이미 완성돼 있기 때문:

- `MeshSliceBaker.AppendCap`(`MeshSliceBaker.cs:367`) — 폐루프 추출 → 중심점 팬 삼각분할 → 감기 방향을 캡 노멀에 정렬.
  주석에 "뒤집으면 캡이 전부 백페이스 컬링되어 단면이 뚫린 것처럼 보인다"고 이미 대비돼 있다.
- 다중 평면 교차부도 "앞 평면이 만든 캡을 다음 평면이 다시 자른다"로 막혀 있다(클래스 주석 (1)).
- 즉 **뚫림 문제는 정적 메쉬 경로에서 이미 해결된 상태**이고, 스키닝 경로도 같은 코드를 탄다.

### C의 부수 효과 — 죽는 프레임 동기화 문제가 소멸한다

이 문서 §9에서 지적한 `deathPoseTime` 수동 동기화 문제는 **C를 하면 존재 자체가 사라진다.**
조각이 boneWeights를 유지하면 프록시가 **포즈에 묶이지 않는다** — 살아 있는 스켈레톤을 따라 변형되므로
어느 프레임에 `Kill()`이 불려도 스왑이 보이지 않는다.

→ `MeshSliceBakerWindow`의 `deathPoseClip` / `deathPoseTime` 필드는 **삭제 대상**이다.
→ "패턴에서 죽는 프레임을 가져올 수 없나?"라는 원 질문은 **가져올 필요가 없어지는 것**으로 해소된다.

### C를 막고 있는 코드 4곳 (정확한 위치)

| # | 위치 | 문제 |
|---|---|---|
| 1 | `MeshSliceBaker.cs:47-48` | 스킨드 메쉬를 **명시적으로 거부**한다 |
| 2 | `MeshSliceBaker.cs:415` `Vertex` | boneWeight 필드 없음 → `Vertex.Lerp`(`:422`)에 가중치 보간 필요 |
| 3 | `MeshSliceBaker.cs:279` | `cutSegments.Add((onPlane[0].position, ...))` — **Vertex를 버리고 position만 남긴다.** 캡 정점이 가중치를 잃는 지점 |
| 4 | `MeshSliceBaker.cs:702` `ToMesh()` | boneWeights/bindposes를 기록하지 않음 |

3번이 핵심이다. `onPlane`은 이미 `List<Vertex>`이므로 Vertex를 그대로 실어 보내면 캡 정점도 가중치를 갖는다.

### 날아가는 조각의 포즈 — `BakeMesh`로 킬 프레임에 확정

C를 해도 "잘려 날아가는 조각"은 스켈레톤을 따라가면 안 된다(몸에 붙어 있게 된다).
해법: **킬 순간 그 조각만 `SkinnedMeshRenderer.BakeMesh`로 정적 메쉬를 떠서 Rigidbody로 넘긴다.**
포즈가 그 프레임에서 캡처되므로 어긋날 여지가 원리적으로 없다. 저작값도 필요 없다.
루트 본(Hips)을 포함한 조각만 스킨드로 남아 래그돌한다.

### 구현 계획
→ **`docs/HumanoidSlice/Plan_HumanoidSlice.md`**

### 어느 안이든 반드시 처리해야 하는 것 (조사에서 반복 확인됨)
- [ ] 래그돌 간 충돌 끄기 (Collision Matrix / `IgnoreLayerCollision`) — **§8, 지수적 비용**
- [ ] 풀 반납 시 본 로컬 포즈 복구 + `isKinematic` + `animator.enabled` — **§8, 알려진 난점**
- [ ] 절단면 위 본의 콜라이더 비활성 (안 그러면 안 보이는 유령이 바닥을 침)
- [ ] 시체 수 상한 (조각 수 상한과 별개) + 초과 시 `isKinematic = true`로 동결(제거 아님)
- [ ] 블렌드 없이 즉시 전환할지 결정 — **§3, 참수엔 즉시 전환이 맞을 가능성 높음**

---

## 참고 링크 전체

- [simonschreibt — MGR Slicing](https://simonschreibt.de/gat/metal-gear-rising-slicing/)
- [Wolfire Forums — Dismemberment](https://forums.wolfire.com/viewtopic.php?f=13&t=12277&p=141754)
- [Wolfire — Ragdoll Physics Update](https://store.steampowered.com/news/app/25000/view/2883948831518627544)
- [Overgrowth a201 devlog](https://www.moddb.com/games/overgrowth/news/new-overgrowth-a201-video-devlog)
- [Ragdoll Blending 기법 정리](https://github.com/raduacg/game-mechanics-optimizations/blob/main/117_ragdoll_blending.md)
- [MoCap Online — Ragdoll Physics in Games](https://mocaponline.com/blogs/mocap-news/ragdoll-physics-animation-guide)
- [Daydreamsoft — Ragdoll Blending](https://www.daydreamsoft.com/blog/ragdoll-physics-blending-with-animation-creating-realistic-character-reactions-in-modern-games)
- [GitHub — LimbHacker](https://github.com/JoeCooper/LimbHacker)
- [Blade & Sorcery — Simple Dismemberment](https://www.nexusmods.com/bladeandsorcery/mods/1289?tab=posts)
- [Blade & Sorcery — Better Decaps](https://www.nexusmods.com/bladeandsorcery/mods/4830)
- [Polycount — MK gore system](https://polycount.com/discussion/210903/about-mortal-kombat-gore-system)
- [foro3d — NetherRealm / MK1 UE4](https://foro3d.com/en/2026/february/netherrealm-customizes-unreal-engine-4-to-create-mortal-kombat-1.html)
- [Hellish Quart 공식](https://www.hellishquart.com/)
- [Unity Game Optimization — Optimize Ragdolls](https://www.oreilly.com/library/view/unity-2017-game/9781788392365/b671c859-0b30-4d6f-8ff1-9294e531789c.xhtml)
- [Unity Game Optimization — Avoid inter-Ragdoll collisions](https://www.oreilly.com/library/view/unity-game-optimization/9781838556518/342836c4-148b-4789-badc-636e16c88b90.xhtml)
- [GameDev.net — Optimizing Ragdoll Death Animation in Unity](https://gamedev.net/tutorials/programming/general-and-gameplay-programming/how-we-optimized-ragdoll-death-animation-in-unity-r4717)
- [Unity Discussions — pooling ragdolls](https://discussions.unity.com/t/any-way-to-effectively-pool-ragdolls/564397)
- [Skinned Mesh Armature Remapper](https://github.com/CascadianWorks/Skinned-Mesh-Armature-Remapper)
