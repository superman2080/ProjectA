# Guide_TripoEnemyAssets — Tripo3D로 스테이지별 적 만들기

서사 원본: `docs/Story/Story_Overview.md` §4-1(괴물 5단계)·§4-3(면을 쓴 자)·§4-2(등을 보인 사람), `docs/Story/Story_Narrative.md` §3-3(아트 발주 규율).

---

## 0. 이 게임이 모델에 요구하는 것 (⚠ 프롬프트보다 이게 먼저다)

| 요구 | 왜 | 어긋나면 |
|---|---|---|
| **Humanoid 리그(biped)** | 적 클립(`Samurai_Idle`·`Walk` 블렌드 트리·공격·사망)이 전부 기존 팩 것이고 리타깃으로 돌아간다. Humanoid 아바타면 본 이름이 달라도 붙는다 | 애니메이션이 하나도 안 붙는다. **1·2단계 괴물도 팔다리는 사람 배치여야 한다** — 뒤틀림은 **실루엣·비율·질감**으로 내고 골격으로 내지 않는다 |
| **A/T 포즈 · 대칭 · 발바닥 = 원점** | 오토리그 정확도 + 캐릭터 root가 발바닥이라는 전제(CLAUDE.md §7-2) | 리그가 틀리거나 캐릭터가 땅에 박힌다 |
| **닫힌(watertight)에 가까운 단일 메쉬** | `MeshSliceBaker`(§11)가 절단면 캡을 만든다. 얇은 판·열린 껍데기는 캡이 지저분해진다 | 베었을 때 조각 안이 뚫려 보인다 |
| **적당한 폴리 · 텍스처 하나** | 곡 중 `Instantiate` 금지(프리웜) + 동시 8~10체 | 히치 = 판정 손실 |
| **무기 없음** (괴물 1~5단계) | 이 세계에서 무기를 든 인간형은 「면을 쓴 자」 **하나뿐**이라는 것이 §4-3의 전부 | 가짜 흑막의 유일성이 죽는다 |

**공통 스타일 토큰**(모든 프롬프트 끝에 붙인다):
```
stylized anime game character, cel-shaded flat colors, clean silhouette, game ready, low poly, single closed mesh, symmetrical A-pose, feet on ground plane, neutral studio lighting, no base, no pedestal
```
**공통 네거티브**:
```
--no weapon, armor, horns, demon, skull, blood, gore, glowing eyes, emissive, transparent, particles, base plate, text, realistic photoreal skin
```

---

## 1. Tripo 사용 절차 (한 캐릭터당)

1. **Text-to-3D** → 아래 프롬프트 그대로. 최신 모델 버전, Quality 켬.
2. 결과에 **Refine** 1회(메쉬·텍스처 밀도 상승).
3. **Rig & Animate** → `Model Type: Biped`, 본 규격은 **Mixamo-compatible**(Unity Humanoid 매핑이 가장 잘 붙는다). walk/idle 프리셋으로 리그 검수.
4. **Export**: `FBX` + `Export Skeleton` + **Bottom-Center Pivot** 켬.
5. Unity 임포트: `Rig > Animation Type = Humanoid`, `Avatar Definition = Create From This Model` → Configure에서 매핑이 초록인지만 확인.
6. `EnemyDefinition` 에셋 하나 + `Tools/Mesh Slice Baker`로 `DeathSliceSet` 굽기(§11).

> 프로젝트 안에서 바로 돌리려면 `generate_model` MCP 툴(provider `tripo`, mode `text`, format `fbx`)이 있다. 단 **리깅은 웹 콘솔에서 해야 한다** — 그 툴은 생성만 감싼다.

---

## 1-b. 이미지로 뽑을 때 (image-to-3D) — ⚠ 실패는 전부 여기서 난다

텍스트보다 형태 통제가 쉬워서 이 경로를 주로 쓰는데, **생성 이미지가 3D 복원에 안 맞으면 프롬프트를 아무리 고쳐도 결과가 안 바뀐다.** 실측으로 걸린 것 넷:

| 규칙 | 어기면 |
|---|---|
| **A-pose — 팔과 몸통 사이에 명확한 간격** | 손이 허벅지에 닿으면 Tripo가 **한 덩어리로 융합**하고 오토리그가 팔을 못 찾는다 |
| **얇게 늘어진 가닥·물방울·천 자락 금지** | **종잇장 두께 판**으로 복원돼 ① `MeshSliceBaker`가 절단 캡을 못 만들고(베면 안이 뚫린다) ② 스킨 웨이트가 안 붙어 애니메이션에서 따로 논다. 흐물거림은 **표면 융기**로만 낸다 |
| **중간 회색 + 평평한 조명 + 샤프** | 순검정·DOF·림라이트는 명암 정보를 지워 표면이 뭉개진다. **어둡게는 Unity 머티리얼에서** 한다 |
| **⚠ 평면 일러스트가 아니라 3D 렌더처럼** | Tripo는 **명암 그라데이션으로 깊이를 추론**한다. 색면 두세 개짜리 벡터/셀셰이딩 그림을 넣으면 복원이 납작해진다. **셀셰이딩 룩은 Unity 머티리얼이 낸다 — 소스는 음영이 살아 있는 편이 언제나 낫다** |
| **이음매 없는 단일 볼륨** | 어깨·팔꿈치·무릎에 분절선이 보이면 **구체관절 인형/로봇**으로 읽히고, 괴물이 아니게 된다 |
| **전신 · 두 발 · 정면 직교 · 단색 배경** | 크롭·바닥 그림자 혼입은 pivot을 틀어 놓는다 |

⚠ **직전 결과를 레퍼런스로 물린 채 프롬프트만 고치면 네거티브가 거의 안 먹는다.** 방향을 꺾을 때는 **참조를 떼고 새 시드**로 간다.

⚠ **"몸"으로 부르면 인체 해부가 딸려온다.** `torso`·`limbs`·`body`를 쓰면 `no muscle`을 아무리 넣어도 근육이 나온다 — **`artist mannequin`(마네킹, 물건)으로 부르면** 그 계통이 통째로 안 나온다. 1·2단계 괴물처럼 **덜 굳은 형상**에는 이 치환이 사실상 유일한 해법이다.

### ⚠ 어디서 이미지를 뽑을 것인가

**Tripo 자체 이미지 생성을 쓴다.** 외부 이미지 생성 서비스(Civitai 등)는 이 캐릭터들에서 막힌다 — **옷 없는 매끈한 전신 인체**라 NSFW 분류기에 걸리고, 결과물이 잠겨 열람이 안 된다. `clay statue`·`no bare skin` 재정의로도 안 풀렸다. **이미지 한 장 얻자고 붙잡을 이유가 없다.**

- ⚠ `flat chest`·`androgynous`·`nude`·`breasts` 계열 어휘는 **네거티브에 넣어도 해롭다**(프롬프트 텍스트 자체가 모더레이션 대상). 어느 툴을 쓰든 안 쓴다.
- ⚠ SD/SDXL 계열을 쓸 일이 생기면 **산문이 아니라 태그 나열**이고 **부정문(`no muscles`)이 안 먹는다** — `no`를 무시하고 `muscles`만 먹는다. 부정은 전부 네거티브 칸으로. Flux 계열은 산문이 잘 먹는다.

### 1단계 이미지 생성 프롬프트 (Tripo 이미지 생성 기준)
```
Full-body front view of a rough clay sculpt of an abstract humanoid figure, matte dark grey,
standing in A-pose with arms held out at 45 degrees, clear gap between the arms and the sides.
The whole figure is one continuous seamless volume, like a lump of clay roughly shaped into
a person, with a completely smooth simplified surface and no detail.
Blank egg-shaped head with no facial features. Rounded mitten hands, rounded stump feet.
Soft 3D clay render with smooth shading gradients and subtle ambient occlusion,
flat even studio lighting, plain solid light grey background, sharp focus,
both feet fully visible, orthographic front view.
```
> ⚠ **§0의 공통 스타일 토큰·공통 네거티브를 이미지 프롬프트에 붙이지 않는다.** 그건 텍스트→3D 입력용이고, `cel-shaded flat colors`가 `soft 3D clay render`와 정면충돌한다 — 실제로 이 실수 때문에 근육·관절 이음매가 되살아났다.
>
> 근육이 나오면 `rough clay sculpt` 에 가중치를 주거나 `mannequin` 이라는 단어를 피한다 — 그 단어는 **구체관절 인형** 쪽으로 끌고 간다.

> 근육 정의가 또렷하게 나온 결과는 버리지 말고 **2단계 베이스로 돌린다**(팔만 더 길게, 팔꿈치에 마디 혹 추가). 1단계는 **가장 덜 사람이어야** 하고, 2단계는 이미 인체가 있어야 한다.

---

## 2. 스테이지별 프롬프트

### 1스테이지 — `Enemy_S1_Blot` (검은 덩어리, 윤곽이 흔들린다)
> 행동: 멀리서 하나씩. 형상보다 **여유**가 먼저 읽혀야 한다.
```
A humanoid-shaped mass of matte black tar, roughly the size of a person, standing upright with arms at its sides.
Surface is soft and unresolved like wet clay that never finished forming; edges are frayed and wispy, no facial features at all, no eyes, no mouth.
Body is one continuous smooth volume with vague suggestions of shoulders, arms and legs.
stylized anime game character, cel-shaded flat colors, clean silhouette, game ready, low poly, single closed mesh, symmetrical A-pose, feet on ground plane, neutral studio lighting, no base, no pedestal
--no weapon, armor, horns, demon, skull, blood, gore, glowing eyes, emissive, transparent, particles, base plate, text, realistic photoreal skin, face
```

### 2스테이지 — `Enemy_S2_Misjoint` (팔다리가 생겼지만 관절이 어긋난다)
> ⚠ **골격은 사람 배치 그대로**. 어긋남은 **비율과 표면**으로 낸다(팔뚝이 지나치게 길다, 팔꿈치 자리에 마디가 둘인 것처럼 보이는 융기).
```
A dark grey humanoid creature with unnaturally long thin forearms and shins, the limbs appearing to have one segment too many, with extra knobby bulges where joints should not be.
Skin is dull matte clay-grey, faintly cracked. Head is a smooth featureless ovoid, no face, no eyes.
Standing upright, ordinary human limb layout, thin torso.
stylized anime game character, cel-shaded flat colors, clean silhouette, game ready, low poly, single closed mesh, symmetrical A-pose, feet on ground plane, neutral studio lighting, no base, no pedestal
--no weapon, armor, horns, demon, skull, blood, gore, glowing eyes, emissive, transparent, particles, base plate, text, realistic photoreal skin, face
```

### 3스테이지 — `Enemy_S3_Shrouded` (사람 크기, 천 조각, 얼굴 자리가 비어 있다)
> 가장 많이 나온다(`clusterSize` 최대). **실루엣이 군중으로 읽혀야** 하므로 장식을 최소로.
```
A person-sized humanoid figure draped in torn, dust-coloured cloth strips hanging loosely from the shoulders and hips.
Where the face should be there is a smooth concave hollow, completely blank, no eyes, no nose, no mouth.
Ordinary human proportions, bare grey limbs visible under the cloth, hands empty and open.
stylized anime game character, cel-shaded flat colors, clean silhouette, game ready, low poly, single closed mesh, symmetrical A-pose, feet on ground plane, neutral studio lighting, no base, no pedestal
--no weapon, armor, horns, demon, skull, blood, gore, glowing eyes, emissive, transparent, particles, base plate, text, realistic photoreal skin, hood ornament
```

### 4스테이지 — `Enemy_S4_Miscast` (이목구비가 생겼지만 배치가 틀렸다)
> ⚠ **특정 인물의 얼굴이 나오면 안 된다**(§4-1). 익명·무표정·부품의 오배치로만.
```
A pale grey humanoid figure whose face has its features in the wrong places: one eye set high on the forehead, another below the cheek, a mouth turned sideways along the jaw, nostrils on the temple.
The features are simplified and anonymous, blank expression, no emotion. Close to normal human proportions, plain dark bodysuit-like skin.
stylized anime game character, cel-shaded flat colors, clean silhouette, game ready, low poly, single closed mesh, symmetrical A-pose, feet on ground plane, neutral studio lighting, no base, no pedestal
--no weapon, armor, horns, demon, skull, blood, gore, glowing eyes, emissive, transparent, particles, base plate, text, realistic photoreal skin, recognizable portrait, hairstyle detail, beard
```

### 5스테이지 — `Boss_S5_Assembly` (거대한 그로테스크 개체, 사람의 부분들이 잘못 조립됨)
> ⚠ **참수 연출 때문에 목이 명확히 분리 가능한 형태**여야 한다(§7-1 · `SliceSet`).
```
A single towering grotesque figure, about three times human height, built from mismatched human parts assembled wrong: too many shoulders, an arm growing out of the ribs, hands of different sizes, a torso fused from two torsos.
The head sits on a long clearly defined neck; the head itself is a twisted knotted mass with no readable face.
Ashen grey flesh, matte and dry. Standing upright, biped, arms hanging.
stylized anime game character, cel-shaded flat colors, clean silhouette, game ready, low poly, single closed mesh, symmetrical A-pose, feet on ground plane, neutral studio lighting, no base, no pedestal
--no weapon, armor, horns, demon, skull, blood, gore, glowing eyes, emissive, transparent, particles, base plate, text, realistic photoreal skin, recognizable face
```
**딸림 에셋** `Boss_S5_HumanHead` — 참수 후 교체될 머리. **평범한 30대 남자, 감긴 눈, 무표정**(§7-1).
```
A single stylized anime male head, mid-thirties, short black hair, plain ordinary features, eyes closed, calm neutral expression, cleanly severed at the neck.
stylized anime game character, cel-shaded flat colors, game ready, low poly, single closed mesh, neutral studio lighting, no base
--no blood, gore, wound detail, pain expression, glowing, helmet, mask, text
```

---

## 3. 사람 형체 둘 (괴물이 아니다)

### `MaskedOne` — 면을 쓴 자 (2~4스테이지·4-b)
> ⚠ 카시마 모델과 **같은 체구·같은 도복·왼 소매만 걷어 올림**. 면끈 텍스처는 파편 2와 공유한다. **면 안쪽은 비어 있다**(내부 지오메트리 없음).
```
A standing adult male martial artist in a plain indigo kendo keikogi and hakama, left sleeve rolled up above the elbow, right sleeve full length.
He wears a kendo men mask covering the whole face, tied with plain cloth cords behind the head; the inside of the mask is hollow and empty, nothing visible through the grille.
Holding a plain uchigatana loosely in the right hand, blade down. Ordinary build, no decoration.
stylized anime game character, cel-shaded flat colors, clean silhouette, game ready, low poly, symmetrical A-pose, feet on ground plane, neutral studio lighting, no base, no pedestal
--no glow, emissive, outline, blood, stains, armor plates, ornaments, insignia, cape, text, visible face
```

### `SilentWitness` — 등을 보인 사람 (전 스테이지)
> ⚠ 플레이어 모델과 **같은 실루엣·같은 도복**. 다른 것은 **맨발**과 **왼손 붕대** 둘뿐. **발광·반투명·이펙트 금지.**
```
A standing young woman in a plain white kendo keikogi and dark blue hakama, calm posture, arms at her sides, barefoot, a plain white cloth bandage wrapped around the left hand and wrist.
Long dark hair tied back. Completely ordinary, no accessories, no weapon.
stylized anime game character, cel-shaded flat colors, clean silhouette, game ready, low poly, symmetrical A-pose, feet on ground plane, neutral studio lighting, no base, no pedestal
--no glow, emissive, translucency, particles, weapon, armor, ornaments, text, shoes, sandals
```

---

## 4. 하지 말 것 (프롬프트에 절대 안 넣는다)
- `demon` · `evil` · `undead` · `soldier` · `armored` — 괴물은 의도를 가진 존재가 아니라 **형태를 무너뜨리는 현상**이다(§3-7).
- 1~5단계 괴물에 **무기·발광·아웃라인** — 각각 「면을 쓴 자」와 기습자 강조가 이미 쓰고 있다(§11-8).
- 4단계·보스에 **알아볼 수 있는 얼굴** — 카시마의 얼굴은 참수 한 프레임뿐이다.
