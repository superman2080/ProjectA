# 배경 프롭 · 구조물 발주표 (Tripo3D 프롬프트 쌍)

배치표의 진실의 원천은 `Assets/02. Scripts/Props/Editor/StageLayoutBuilder.cs` 좌표 테이블이고,
무엇을 뜻하는지는 `docs/Story/Story_Narrative.md` §3-3 · `docs/Story/Story_Overview.md` §5에 있다.
이 문서는 **그 둘이 요구하는 3D 에셋을 전부 한 줄씩 나열한 발주표**다.

## 그래픽 스타일

**1990~2000년대 일본 애니메이션 풍.** 셀 음영, 면이 또렷하게 갈리는 단색 블록, 손으로 그린 배경화의
질감이다. 런타임의 `ToonPosterize`(밝기 N단계 절단)와 `FilmLookProfile`(채도 −25)이 그 위에 얹히므로
**모델 자체는 형태가 단순하고 면 구분이 분명**해야 한다 — 미세 디테일은 계단화에 먹힌다(§12-1).

아래 프롬프트에는 그 스타일 문구가 **이미 붙어 있다.** 그대로 복사해 쓴다.

## 상태 표기

| 표기 | 뜻 |
|---|---|
| `있음` | `Assets/03. Prefabs/StoryProps/`에 프리팹이 있다. 발주 불필요(프롬프트는 재굽기용) |
| `외부` | 외부 에셋팩(Pandazole City Town Pack)이 덮는다. 발주 불필요 |
| **`발주`** | **없다. 만들어야 한다** |

---

## 1. 도상 — 회수되는 물건 (⚠ 규율이 모양을 정한다)

`Story_Narrative.md` §3-3의 「아트 발주 시 반드시 지킬 것」이 그대로 제약이다.

| 상태 | 이름 | Tripo3D 프롬프트 |
|---|---|---|
| **발주** | `Prop_ClosedDoor` | `a plain weathered sliding wooden storeroom door in a concrete frame, closed, single worn metal handle, faint scratch marks near the handle, no signage, no glass, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_ClosedDoor_Open` | `the same plain weathered wooden storeroom door slid fully open, dark empty opening behind it, same frame, same worn metal handle, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_JizoRow` | `a row of six small stone jizo statues on low stone pedestals, five statues wearing faded reddish-brown cloth bibs and one pedestal completely empty, moss on the bases, roadside shrine, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_DryCulvert` | `a straight concrete drainage culvert section, bone dry cracked bed, low side walls, weeds in the seams, urban waterway, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Cairn_A` / `Prop_Cairn_B` | `a small stacked stone cairn of flat river rocks, five to seven stones, slightly leaning, standing upright, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_CairnFallen_A/B/C` | `a collapsed stone cairn, flat river rocks scattered on the ground around a toppled base stone, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_GridBoard` | `a dark wooden notice board with a perfectly even 3x3 grid of square cells routed into the face, empty cells, simple frame, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_GridBoard_Slips` | `nine thin paper name slips hanging in a 3x3 grid, slightly curled edges, hand-written vertical japanese characters, no board, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |

> ⚠ `Prop_ClosedDoor`는 **다섯 스테이지에서 완전히 같은 에셋**이어야 한다. 변형·색차 금지.
> `_Open`은 트루 엔딩 전용 1개이며 **문 본체와 정확히 같은 실루엣**에서 문짝만 옮긴 것이어야 한다.
> ⚠ `Prop_JizoRow`는 **여섯 기 중 하나가 비어야** 한다. 앞치마는 **바랜 적갈색**(선명한 붉은색은 단편 컷 5·7이 쓴다).
> ⚠ `Prop_GridBoard`의 칸 비율은 패턴인풋과 같은 **3x3 등간격**.

---

## 2. 1 · 3스테이지 — 상점가

| 상태 | 이름 | Tripo3D 프롬프트 |
|---|---|---|
| 있음 | `Prop_VendingMachine` | `a japanese street vending machine, lit front panel with rows of canned drinks, coin slot and return tray, boxy, standing against a wall, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Sign_Vertical` | `a vertical japanese shop sign board mounted on a wall bracket, tall narrow lightbox, blank face, weathered metal edge, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Sign_Horizontal` | `a horizontal japanese shop signboard on a wall bracket, wide shallow lightbox, blank face, weathered metal edge, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Sign_Standing` | `a freestanding a-frame sidewalk sign board, metal legs, blank panel, slightly scuffed, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Noren` | `a japanese noren fabric shop curtain hanging from a wooden rod, split into three panels, plain dyed cloth, soft folds, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Lantern` | `a cylindrical japanese paper lantern hanging from a short bracket, ribbed frame, warm plain paper, no text, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 외부 | `Env_CommercialBuilding_01/02/03` | `a low-rise japanese commercial street building facade, shutter front, air conditioner units, exterior stairs, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 외부 | `Env_ResidentBuilding_01~06` | `a small japanese residential apartment block, balconies, exterior staircase, flat roof with water tank, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 외부 | `Env_Road_Cross/Straight/Cornor_01` | `a 10x10 meter modular asphalt road tile with painted lane markings and concrete sidewalk curbs, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_Shutter` | `a closed corrugated metal rolling shutter in a concrete storefront frame, rust streaks along the bottom edge, no graffiti, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_Shutter_Fallen` | `a corrugated metal rolling shutter torn loose and fallen forward onto the pavement, buckled panel, bent guide rails, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |

> `Prop_Shutter`는 §5의 「닫힌 문은 골목 끝 셔터 옆」과 사건 2의 *"셔터가 떨어져 통로가 열린다"*가 요구한다.
> ⚠ 3스테이지는 1스테이지와 **완전히 같은 프리팹**을 재사용한다(반복 증상). 변형본을 만들지 않는다.

---

## 3. 2스테이지 — 언덕길

| 상태 | 이름 | Tripo3D 프롬프트 |
|---|---|---|
| 있음 | `Prop_RetainingWall` | `a concrete retaining wall section with regular block joints and weep holes, weathered gray, straight run, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Guardrail` | `a roadside steel guardrail section, two horizontal beams on posts, chipped white paint, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Stairs` | `a narrow outdoor concrete staircase flight with a simple steel handrail on one side, worn treads, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_HillRoadSlope` | `a sloped asphalt road segment climbing gently, concrete curbs on both sides, drainage gutter, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 외부 | `Prop_Tree_01~05` | `a stylized roadside tree, bare trunk with a compact rounded canopy, foliage as a few solid clustered masses, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |

> ⚠ 수로는 **반드시 말라 있어야** 한다 — 물이 있으면 삼도천이 되고 카시마의 *"넌 아직 안 죽었어"*가 거짓이 된다.
> ⚠ 경사는 **무대 원(8m) 밖에만** 둔다. 전투 이동이 `transform.position` 대입이라 경사면을 못 탄다(§11-2).

---

## 4. 4스테이지 — 도장 실내

| 상태 | 이름 | Tripo3D 프롬프트 |
|---|---|---|
| 있음 | `Dojo_Stage`(FBX) | `a japanese kendo dojo interior shell, polished wooden floor, timber post-and-beam walls, high clerestory windows, no furniture, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Shelf_Intact` | `a plain wooden storage shelf unit, four open shelves, upright and intact, scuffed edges, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_KatanaRack` | `a wooden wall-mounted sword rack holding katana, horizontal cradles, simple joinery, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_BoguSet` | `a set of kendo armor resting on the floor, helmet men with grille, chest do, waist tare, folded cords with a woven diamond pattern, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_BoguCord` | `a coiled woven kendo armor cord with a repeating diamond pattern, still inside its unopened paper wrapper, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |

> `Prop_BoguCord`는 표식 2(§3-4)가 요구한다 — 무대 1 가방 속의 **포장을 뜯지 않은** 끈이며
> 참수 후 목에 감긴 같은 끈과 **같은 무늬 에셋**이어야 한다. ⚠ 카메라가 이것을 클로즈업하지 않는다.

---

## 5. 5스테이지 — 도구실 (⚠ 도상이 하나도 없다)

| 상태 | 이름 | Tripo3D 프롬프트 |
|---|---|---|
| 있음 | `Stage5_StorageRoom` | `a small cramped school equipment storeroom interior, bare concrete floor, plain plastered walls, single ceiling light, one door frame, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_CollapsedShelf` | `a wooden storage shelf collapsed onto the floor, shelf boards split and fanned out, contents spilled, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_BagKit` | `a worn canvas sports duffel bag lying on the floor, half open, patterned tape wrapped around the handle, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_ShinaiBag` | `a long narrow cloth shinai bag with a shoulder strap, patterned tape wrapped around the handle, lying flat, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |

> ⚠ 여기 놓이는 것은 **원래 물건**이다. 왜곡·발광·변형 금지.
> 4스테이지 돌탑과 **같은 상대 좌표**에 무너진 선반이 온다("그것이 이것이었다").
> 바닥의 붉은 자국은 3D가 아니라 **데칼 텍스처**다 — 발주 대상이 아니다.

---

## 6. 트루 엔딩 — 병실

| 상태 | 이름 | Tripo3D 프롬프트 |
|---|---|---|
| 있음 | `Prop_IVStand` | `a hospital IV drip stand on casters, thin metal pole, hook arm, empty bag hanger, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_Chair` | `a plain hospital visitor chair, tubular metal frame, vinyl seat and back, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| 있음 | `Prop_WallChart` | `a clipboard style wall chart holder with a blank printed form sheet, small metal wall bracket, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_HospitalBed` | `an adjustable hospital bed with side rails, thin mattress and folded sheet, casters, headboard panel with controls, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_BedsideTable` | `a small hospital bedside cabinet on casters, one drawer and a lower shelf, laminate top, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_CurtainRail` | `a ceiling-mounted hospital curtain track with a plain hanging privacy curtain, half drawn, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_HeartMonitor` | `a compact bedside patient vital signs monitor on a stand, blank screen, cable hooks, no branding, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_DocumentFolder` | `a closed manila document folder holding a few printed sheets, slightly worn corners, no writing, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |

> `Prop_HeartMonitor`는 §2-5의 **박의 정체**(심박 측정기 전자음)가 회수되는 자리다.
> `Prop_DocumentFolder`는 서류철 다섯 장(§7-4)을 플레이어가 직접 넘기는 지면 뷰가 쓴다.

---

## 7. 문건 — 읽히는 종이 (⚠ 도상이 아니다)

여덟 장(무대 0에 한 장 + 2·2·1·1·0)이며 **전부 손글씨**, 트루 엔딩 서류철 다섯 장만 **활자 인쇄**다.
지면은 같은 3D 에셋 한 종을 돌려쓰고 **텍스처만 갈아 끼운다**(원문은 `Story_Script.md` §10-A).

| 상태 | 이름 | Tripo3D 프롬프트 |
|---|---|---|
| **발주** | `Prop_NoticePaper_Wall` | `a single sheet of paper taped flat to a wall at its four corners, slightly curled bottom edge, blank face, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_NoticePaper_Board` | `a single sheet of paper pinned to a small wooden notice board with two thumbtacks, blank face, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |
| **발주** | `Prop_NoticePaper_Loose` | `a single sheet of paper lying loose on the ground, one corner folded, blank face, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |

> ⚠ 근접 표시는 기능 안내가 아니라 **그 종이에 적힌 제목 한 줄**이다. 수집률·목록·획득음이 없다.
> ⚠ 도상 쪽 프롬프트 금지는 이것 때문에 느슨해지지 않는다 — **글씨가 적힌 것만 읽힌다.**

---

## 8. 균열 (`EnemyDirector.riftPoints` · §11-11)

| 상태 | 이름 | Tripo3D 프롬프트 |
|---|---|---|
| **발주** | `Prop_Rift` | `a thin vertical tear in space, jagged narrow slit shape, flat double-sided plane geometry, sharp irregular edges, no frame, no surroundings, 1990s japanese anime background art style, cel shaded flat color blocking with crisp hard-edged shading, hand-painted anime background look, simple readable silhouette, low-poly game asset, clean quad topology, single object, real-world scale in meters, no base plate, no ground plane` |

> ⚠ 무늬와 발광은 셰이더가 든다(`RiftController`) — 3D는 **찢어진 형태의 얇은 평면 하나**면 된다.
> 적이 여기 붙은 채 늘어난 줄로 끌려 나오므로(§11-11) 두께가 얇고 뒷면도 같은 무늬여야 한다.
> `Encounter` 상태에 따라 `SetActive`로 꺼지므로(§9 상태 표) 켜짐/꺼짐 변형본을 따로 만들지 않는다.

---

## 9. 3D 발주가 아닌 것 (⚠ 여기로 밀지 말 것)

| 무엇 | 어디서 하나 |
|---|---|
| 하늘 없음 / 경계가 어둠으로 끊김(2스테이지) | 스카이박스 · 라이팅 |
| 색이 빠짐(4스테이지), 붉은 대역만 유지 | `FilmLookProfile` 채도(§12-1) |
| 셀 음영의 밝기 단계 · 필름 긁힘 | `ToonPosterize` · `FilmScratch` 풀스크린 패스(§12-1) |
| 도구실 바닥의 붉은 자국 | 데칼 텍스처 |
| 일렁임 | VFX + `RiftController` 셰이더 |
| 「등을 보인 사람」 | **캐릭터**다 — 플레이어 모델과 같은 실루엣·같은 도복, 다른 것은 맨발과 왼손 붕대 둘뿐. 이펙트·발광·반투명 금지 |
| 잘린 머리 조각 | 기존 `SliceSet` 파이프라인(`Debris_EnemyHead` 있음) |
| NPC | **만들지 않는다.** 없는 것이 곧 1스테이지의 이상 현상이다 |

---

## 10. 발주 합계

**15종.**
`Prop_ClosedDoor` · `Prop_ClosedDoor_Open` · `Prop_Shutter` · `Prop_Shutter_Fallen` ·
`Prop_BoguSet` · `Prop_BoguCord` · `Prop_HospitalBed` · `Prop_BedsideTable` · `Prop_CurtainRail` ·
`Prop_HeartMonitor` · `Prop_DocumentFolder` · `Prop_Rift` ·
`Prop_NoticePaper_Wall` · `Prop_NoticePaper_Board` · `Prop_NoticePaper_Loose`

우선순위는 **`Prop_ClosedDoor` 하나가 압도적으로 앞선다** — 다섯 스테이지 전부에 나오고,
이 게임에 문은 그것 하나뿐이며(§5), 5스테이지와 트루 엔딩의 회수 전체가 그 위에 얹힌다.

## 반입 절차

1. Tripo3D → FBX 내보내기 → `Assets/06. Models/Props/`
2. `Tools/Story Props/Setup Materials` (멱등)
3. `Tools/Story Props/Extract Prefabs` (이미 있는 프리팹은 안 건드린다)
4. `Tools/Story Props/Build Stage Layout/…`
5. 새 프롭은 `StageLayoutBuilder.cs`의 좌표 테이블에 줄을 추가해야 배치된다 — **그 파일이 배치표의 진실의 원천이다.**
