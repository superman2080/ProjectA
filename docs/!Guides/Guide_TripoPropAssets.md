# Guide_TripoPropAssets — Tripo3D로 배경 프롭 만들기

`Guide_TripoEnemyAssets.md`의 짝 문서다. 저쪽이 **사람 형체**(리그·애니메이션·절단 세트)를 다루고, 이쪽은 **배경 프롭**(리그 없음, 배치만)을 다룬다.

서사 원본: `docs/Story/Story_Narrative.md` §3-3(무대에 놓이는 것들 · 아트 발주 규율), `docs/Story/Story_Overview.md` §2-2(닫힌 문)·§2-6(문건)·§7(엔딩).
배치 좌표의 진실의 원천: `Assets/02. Scripts/Props/Editor/StageLayoutBuilder.cs`.

---

## 0. 이 게임이 프롭에 요구하는 것 (⚠ 프롬프트보다 이게 먼저다)

| 요구 | 왜 | 어긋나면 |
|---|---|---|
| **무대 원 안에는 아무것도 놓지 않는다**(반경 8m, 5스테이지 3.5m) | 플레이어·적 이동이 `transform.position` **대입**이라 물리를 안 본다(CLAUDE.md §11-2) | 캐릭터가 프롭을 통과하거나 경사에 박힌다 |
| **콜라이더는 무대 원 밖에만** | 탐색 중에는 걸어가서 들여다보는 사물이고, 통과해 지나가면 배경으로 되돌아간다(§Narrative 3-3) | 전투 동선이 깨지거나, 사물이 허깨비가 된다 |
| **Y = 0 이 바닥, pivot은 바닥 중심** | 배치표가 `y = 0` 으로 좌표를 적는다. `StageLayoutBuilder`는 **프리팹의 로컬 트랜스폼을 건드리지 않는다** | 공중에 뜨거나 바닥에 박힌다 |
| **1 유닛 = 1m 실척** | 문 2.0m · 자판기 1.8m · 육지장 0.9m. 배치표가 실척 전제로 적혀 있다 | 스케일 보정이 프리팹마다 흩어진다 |
| **텍스처 하나 · 적당한 폴리** | 배경층이 프롭 수십 개다. 곡 중 히치 = 판정 손실(§5 프리웜) | 프레임이 떨어진다 |
| **상호작용 금지가 기본** | 도상에는 하이라이트·프롬프트·조사 텍스트가 하나도 붙지 않는다(§Overview 2-2). **예외는 글씨가 적힌 종이뿐** | §0(세계가 자기를 설명하지 않는다)이 깨진다 |

**공통 스타일 토큰**(모든 프롭 프롬프트 끝에 붙인다):
```
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, upright, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it
```

> ⚠ **Tripo3D에는 네거티브 칸이 없다.** `--no ...` 줄을 만들어 붙이면 그 단어들을 **오히려 불러온다**(적 가이드 §0과 같은 함정). 빼고 싶은 것은 전부 **긍정문으로 못박는다** — "낙서 없음"이 아니라 `clean unmarked surface`, "사람 없음"이 아니라 `empty`.
> ⚠ **`weathered`·`abandoned`·`ruined`를 함부로 넣지 않는다.** 이 세계의 물건들은 **망가진 것이 아니라 아무도 안 만지는 것**이다(§Overview 0). 폐허로 만들면 포스트아포칼립스가 되고, 카시마의 *"넌 아직 안 죽었어"*가 붙을 자리가 없어진다.

### 캐릭터와 다른 점 셋

1. **리그가 필요 없다** — Tripo의 `Rig & Animate` 단계를 건너뛴다. 유일한 예외가 **닫힌 문**이고, 그것도 스켈레톤이 아니라 **문짝을 자식으로 뺀 프리팹 + Unity 회전·이동 애니메이션**으로 만든다(§2-1).
2. **`MeshSliceBaker`와 무관하다** — 프롭은 베이지 않는다. 그래서 watertight 요구가 없고, 판·천·얇은 종이가 **허용된다**(적 쪽에서 금지였던 것).
3. **A-pose·대칭 토큰을 안 쓴다** — 물건이므로 비대칭이 오히려 자연스럽다.

---

## 1. 지금 있는 것 (⚠ 다시 만들지 않는다)

`Assets/03. Prefabs/StoryProps` 28종이 이미 있다 — 자판기 · 간판 3종 · 노렌 · 제등 · 육지장열 · 마른 수로 · 축대 · 가드레일 · 계단 · 언덕 경사 · 돌탑 4종 · 아홉 칸 격자(+슬립) · 선반 2종 · 죽도걸이 · 도구실 · 링거대 · 의자 · 벽차트 · 죽도가방 · 가방 · 적 머리 조각.

외부 팩으로 덮이는 것도 다시 만들지 않는다:

| 필요한 것 | 어디에 있나 |
|---|---|
| 건물 · 도로 타일 · 거리 표지 · 쓰레기통 | `Pandazole City Town Pack` |
| 침대 · 책 · 실내 가구 | `Pandazole Home Interior` |
| 주택가 외부 · 차량 | `Toon Suburban Pack` |

⚠ **`Prop_Tree_01`~`05`는 FBX만 있고 프리팹이 없다** — `StageLayoutBuilder`의 2스테이지 배치가 다섯 개를 못 찾는다. Tripo 일이 아니라 **프리팹 5개 만드는 일**이다.

---

## 2. 만들어야 하는 것

우선순위 순서다. 1번은 서사가 그것 하나에 걸려 있고, 나머지는 해당 무대·엔딩을 세울 때 필요하다.

| # | 이름 | 어디에 | 근거 |
|---|---|---|---|
| 1 | `Prop_ClosedDoor` | **1~5스테이지 전부** | §Overview 2-2 |
| 2 | `Prop_Notice_Wall` | 탐색 구간(문건) | §Overview 2-6 |
| 3 | `Prop_Paper_Folded` | 탐색 구간(문건) | §Overview 2-6 |
| 4 | `Prop_Booklet` | 탐색 구간(문건) | §Overview 2-6 |
| 5 | `Prop_HospitalBed` | 트루 엔딩 병실 | §Overview 7 |
| 6 | `Prop_BedsideTable` | 트루 엔딩 병실 | §Overview 7 |
| 7 | `Prop_DocumentFolder` | 트루 엔딩 병실 | §Script 10 |
| 8 | `Prop_BoguSet` | 4스테이지 돌탑 원본 → 5스테이지 | §Narrative 3-3 |
| 9 | `Prop_ShinaiBundle` | 5스테이지 무너진 선반 아래 | §Narrative 3-3 |
| 10 | `Prop_JizoStatue` · `Prop_JizoPedestal_Empty` | 1·3스테이지 · 굿 엔딩 | §Narrative 3-3 (⚠ 먼저 확인 — §2-9) |

### 2-1. `Prop_ClosedDoor` — 닫힌 문 (최우선)

**다섯 무대에서 완전히 같은 에셋이어야 한다.** 재질·손잡이·긁힌 자국까지. 조금이라도 다르면 "같은 문"이 성립하지 않고, 그 한 줄이 §2-2의 전부다.

현실의 물건은 **도장 도구실의 문**이다(5스테이지가 그 문 안쪽이다).

```
A single closed sliding wooden door of a Japanese school storage room, standing in its own plain wooden frame.
Pale dry wood with a visible straight grain, one horizontal rail across the middle, a small recessed metal finger pull on the right side, a few shallow scratches near the bottom edge.
The door is completely shut, nothing visible behind it, solid opaque panel, clean unmarked surface with no posters and no writing.
About two meters tall, plain and ordinary, the kind of door nobody looks at.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, upright, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it
```

**임포트 후 손으로 하는 것 둘:**
- 문짝을 **프레임의 자식으로 분리**한다(`Prop_ClosedDoor/Frame` + `Prop_ClosedDoor/Leaf`). pivot이 미닫이 레일 한쪽 끝에 와야 한다.
- 열림 애니메이션은 **`Leaf`의 localPosition 하나를 미는 클립 1개**다. 트루 엔딩 전용이고 다른 무대에서는 재생되지 않는다.

> ⚠ **미닫이(sliding)로 못박은 이유** — 여닫이면 열리는 순간 문짝이 화면을 가로질러 카메라를 덮는다. 트루 엔딩은 문 너머가 하얗게 열리며 컷 없이 1인칭이 되는 장면이라(§Overview 7), 문짝이 옆으로 빠지는 편이 그 전환을 안 방해한다.
> ⚠ **프레임을 같이 넣는 이유** — 문짝만 있으면 배치할 때마다 벽 개구부를 맞춰야 하고, 무대마다 벽이 달라 "같은 문"이 조금씩 달라진다. 프레임째 벽에 박으면 그 부류가 원천 소멸한다.

### 2-2. `Prop_Notice_Wall` — 벽에 붙은 공고문

문건 일곱 장 중 **벽에 붙은 것**이 쓰는 프롭이다. ⚠ **글자는 이 프롭에 넣지 않는다** — 원문은 지면 뷰(UI)가 띄우고, 이 프롭은 *"벽에 종이가 붙어 있다"*만 말한다. 글자를 넣으면 다섯 스테이지에서 같은 글이 반복된다.

```
A single sheet of off-white paper taped flat to a wall, slightly curled at the two lower corners, one strip of aged tape at the top.
The paper is blank with faint grey printed ruling lines and no readable text, no logo, no stamp.
Plain rectangular sheet, roughly A3 size, hanging vertically.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it, no wall
```

**변형** `Prop_Notice_Stack` — **같은 종이 다섯 장이 겹쳐 붙은 것**(사건 8 · `Story_Events.md` 382행). 위 프롬프트에 한 문장을 더한다:
```
Five identical sheets layered one over another, each slightly offset and rotated a few degrees, the topmost one taped last.
```

### 2-3. `Prop_Paper_Folded` — 접힌 등사물

```
A single sheet of thin off-white paper folded twice into quarters, lying flat on the ground with the fold lines creased sharply and one corner lifted.
The paper is blank, no readable text, no logo. Slightly yellowed, thin and light.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it
```

### 2-4. `Prop_Booklet` — 표지가 뜯긴 얇은 책자

```
A thin stapled paper booklet lying flat, about twenty pages thick, its front cover torn away so the first inner page is exposed, two staples visible along the left spine.
Pages are blank off-white with faint grey ruling, edges uneven and slightly bent, no readable text.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it
```

### 2-5. `Prop_HospitalBed` — 병실 침대

트루 엔딩은 **1인칭으로 천장 → 링거 → 붕대 감긴 왼손 → 의자에 잠든 사키** 순서다(§Overview 7). 카메라가 이 침대 위에 앉으므로 **머리판·측면 레일의 실루엣이 시야 아래쪽에 들어온다.**

```
A single hospital bed with a thin white mattress, a folded white blanket across the lower half, one flat pillow at the head, a pale metal frame, a plain headboard, and side rails raised on both sides.
Small caster wheels at the four corners, plain and clinical, completely empty with nobody in it.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, upright, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it
```
> ⚠ 급하면 `Pandazole Home Interior`의 `Prop_Bed_0*`로 대체할 수 있지만 **레일과 캐스터가 없어 가정집 침대로 읽힌다** — 병실이라는 사실이 그 둘에 걸려 있다.

### 2-6. `Prop_BedsideTable` — 침대 옆 작은 탁자

서류철 다섯 장이 놓이는 자리다.

```
A small bedside cabinet with one drawer and one open lower shelf, pale laminate top, plain light grey body, about seventy centimeters tall, empty.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, upright, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it
```

### 2-7. `Prop_DocumentFolder` — 서류철

⚠ **읽히는 것은 지면 뷰(UI)다.** 이 프롭은 탁자 위에 놓인 물건일 뿐이고, 다섯 장이 활자 인쇄라는 사실도 UI가 진다(§Overview 2-6).

```
A closed manila document folder lying flat, slightly bulging with a few sheets of paper inside whose edges stick out unevenly, a plain paper label on the front cover with no readable text.
Faded buff cardboard, one corner softened from handling.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it
```

### 2-8. `Prop_BoguSet` · `Prop_ShinaiBundle` — 떨어진 도구들

4스테이지의 **돌탑**이 5스테이지에서 **무너진 선반과 떨어진 도구들**로 회수된다(§Narrative 3-3). 회수될 쪽의 물건이 없으면 그 회수가 성립하지 않는다.

```
A set of kendo protective gear resting on the floor in a loose heap: a face mask with a metal grille lying on its side, a padded chest protector, a waist guard with hanging flaps, and one thick padded glove.
Dark indigo cloth and worn tan leather, plain cotton cords, dull and dusty, no decoration and no insignia.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it
```

```
A bundle of five bamboo kendo shinai lying together on the floor, tied once near the middle with a plain cloth strip, the tips fanned slightly apart.
Pale split bamboo staves with dark leather caps and grips, plain and well used.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no base, no pedestal, no scene, no props around it
```

> ⚠ **호구 끈의 무늬는 단편 컷 2와 공유한다**(§Narrative 3-4의 표식). 여기서는 무늬를 넣지 말고 **Unity 머티리얼에서** 같은 텍스처를 물린다 — 두 곳에서 따로 생성하면 무늬가 달라져 표식이 죽는다.

### 2-9. 육지장 — 먼저 확인할 것

`Prop_JizoRow`가 이미 있다. 서사 요구는 **여섯 기이고 그중 하나가 빈 좌대**이며(§Narrative 3-3), 굿 엔딩에서 **그 빈 좌대 위에 가방이 놓인다.**

**순서대로 확인한다:**
1. 기존 `Prop_JizoRow`가 여섯 기인가, 한 좌대가 비어 있는가.
2. 앞치마가 **바랜 적갈색**인가(선명한 붉은색은 단편 컷 5·7 전용).
3. 빈 좌대 위에 `Prop_BagKit`을 얹을 수 있는 자리가 있는가.

셋이 다 참이면 **아무것도 만들지 않는다.** 아니면 낱개 둘을 만들어 배치표에서 여섯 번 놓는다 — 그러면 위 요구가 좌표로 표현되어 다시 어긋날 수가 없다.

```
A single small stone jizo statue standing on a plain square stone pedestal, rounded shoulders, a simple carved face with closed eyes and a calm expression, hands together in front of the chest.
Grey stone, a faded brick-red cloth bib tied around the neck, plain and modest, about ninety centimeters tall including the pedestal.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, upright, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no scene, no props around it
```

```
A plain empty square stone pedestal with a flat top and nothing standing on it, grey stone, moss in the seams, about thirty centimeters tall.
The top surface is bare and flat, completely empty.
stylized anime game prop, cel-shaded flat colors, clean silhouette, game ready, low poly, single object, upright, resting flat on the ground plane, neutral studio lighting, plain solid grey background, no scene, no props around it
```
> ⚠ **`no base, no pedestal` 토큰을 이 둘에서만 뺐다** — 좌대가 이 프롭의 내용이라 그 토큰이 남아 있으면 좌대가 사라진다.

---

## 3. Tripo 사용 절차 (프롭 하나당)

1. **Text-to-3D** → 위 프롬프트 그대로. 최신 모델, Quality 켬.
2. **Refine** 1회.
3. **`Rig & Animate`는 건너뛴다**(프롭은 리그가 없다).
4. **Export**: `FBX`, **Bottom-Center Pivot** 켬.
5. `Assets/06. Models/Props/` 에 `Prop_{이름}.fbx` 로 저장.
6. `Tools/Story Props/Setup Materials` — 임포트 설정 + URP/Lit 머티리얼 생성(**멱등**이라 재실행 안전).
7. `Tools/Story Props/Extract Prefabs` — 낱개 프리팹 생성(**이미 있는 프리팹은 안 건드린다**).
8. `StageLayoutBuilder.cs`의 해당 스테이지 좌표 테이블에 `new P("Prop_...", x, y, z, yaw)` 한 줄 추가 → `Tools/Story Props/Build Stage Layout/…` 재실행.
9. 무대 원 밖이면 콜라이더를 붙인다(안이면 붙이지 않는다, §0).

> 프로젝트 안에서 바로 돌리려면 `generate_model` MCP 툴(provider `tripo`, mode `text`, format `fbx`)이 있다.

### 이미지 경로로 갈 때

적 가이드 §1-b의 규칙 중 **프롭에도 그대로 걸리는 것**: 평평한 조명 · 샤프 · 단색 배경 · 정면 직교 · 전체가 프레임에 들어옴 · **평면 일러스트가 아니라 3D 렌더처럼**(Tripo가 명암 그라데이션으로 깊이를 추론한다).
**프롭에서는 풀리는 것**: A-pose · 대칭 · 얇은 판 금지 · watertight(§0의 "캐릭터와 다른 점 셋").

---

## 4. Tripo로 만들지 않는 것

| 무엇 | 어디서 만드나 |
|---|---|
| 균열 · 일렁임 | VFX(`RiftController` · 파티클). CLAUDE.md §11-11 |
| 5스테이지 바닥의 붉은 자국 | 데칼 / 머티리얼. 프롭이 아니다 |
| 문건 일곱 장 · 서류철 다섯 장의 **글** | 지면 뷰(UI). 원문은 `Story_Script.md` §10·§10-A |
| 단편 컷 이미지 여덟 장 | 2D. `Story_Overview.md` §6 |
| 건물 · 도로 · 거리 표지 · 실내 가구 | 기존 외부 팩(§1) |
| `Prop_Tree_01`~`05` | FBX가 이미 있다 — 프리팹만 만든다(§1) |
| 탐색 씬의 골목 · 계단참 | 기존 팩 조합 + 씬 배치. 신규 에셋이 아니다 |
