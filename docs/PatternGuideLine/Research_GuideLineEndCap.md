# Research — 가이드라인 끝 장식 (GuideLineEndCap)

`docs/PatternGuideLine/`의 후속 연구다. 캡슐 가이드 자체(Research/Plan_PatternGuideLine)는 그대로 유효하고,
여기서는 **경로 양 끝에 임의의 스프라이트를 붙일 수 있는가**만 다룬다.

## 0. 요구

가이드 경로의 **양 끝**에 저작자가 고른 그림(스프라이트)을 놓는다.
⚠ 요구에 따라오는 제약: **지금 캡슐이 가진 기능(아웃라인·그라데이션·미스 색·페이드)을 잃으면 안 된다.**

## 1. 지금 그림이 나오는 방식

- `PatternLineRenderer`(`Assets/02. Scripts/UI/PatternLineRenderer.cs`)는 `MaskableGraphic`이다.
  씬 오브젝트는 `PointBackground/PatternGuideLine`(`BattleScene.unity:13943`).
- `PatternHandler.ShowGuideLine`(`Assets/02. Scripts/UI/PatternHandler.cs:613`)이
  패턴의 노드 좌표를 로컬로 바꿔 `SetPoints`로 넘긴다. **연타는 넘기지 않는다**(이을 순서가 없다).
- `OnPopulateMesh` → `PopulateCapsuleMesh`(`:139`)가 **같은 캡슐을 반지름만 키워 두 번** 그린다:
  바깥(=`radius + outlineWidth`, `outlineColor`) 위에 안쪽(=`radius`, `normalColor`)을 덮는다.
  **아웃라인의 정체가 그것뿐이다** — 별도 셰이더도, 외곽선 계산도 없다.
- `BuildCapsule`(`:180`)은 점마다 원을, 점 사이마다 선분을 그리되
  **선분이 덮는 반원을 각도로 빼고 남은 호만** 채운다. 그래서 겹침이 0이고 **반투명에서 알파가 균일하다**.
- 끝점은 인접 방향이 하나뿐이라 그 반원이 그대로 남는다 — **지금 끝이 둥근 이유가 이것이다.**
- 색은 `BuildPointColors`(`:155`)가 점마다 만든다. `useGradient`면 `Evaluate(i / (n-1))`,
  아니면 단색(`useMissColor ? missColor : normalColor`). 어느 쪽이든 `currentAlpha`가 곱해진다(`WithFadeAlpha`).

## 2. 확인된 제약

### C1. 같은 메쉬에 스프라이트를 넣을 수 없다
`MaskableGraphic`은 `CanvasRenderer` 하나 = **텍스처 하나**다. 지금은 텍스처가 없고
모든 정점의 UV가 `Vector2.zero`라 색만으로 칠한다(`AddArc`/`AddSegmentQuad`).
여기에 스프라이트를 끼우려면 **선까지 그 스프라이트와 같은 아틀라스의 흰 픽셀을 가리키게** 해야 한다
— 끝 장식 하나 때문에 가이드 전체가 아틀라스에 묶인다.

### C2. 그래서 별도 `Image`인데, 아웃라인은 잃지 않는다
아웃라인이 "같은 걸 크게 한 번 더 그린 것"이므로 **스프라이트에도 같은 수법이 그대로 먹는다** —
끝 하나당 `Image` 둘(뒤: 크게·`outlineColor` / 앞: `normalColor`).
배율은 `(radius + outlineWidth) / radius`로 **코드가 계산한다**(저작 값이 안 늘어난다).

### C3. 나머지 기능도 전부 '색 하나'로 환원된다
| 기능 | 캡슐이 쓰는 값 | `Image`에서 |
|---|---|---|
| 그라데이션 | `fillGradient.Evaluate(0 / 1)` | 그 값이 곧 양 끝 색 |
| 미스 색 | `useMissColor ? missColor : normalColor` | 같은 분기 |
| 페이드 | `currentAlpha` | `WithFadeAlpha` 재사용 |
| 두께 | `lineWidth` / `outlineWidth` | 크기 계산의 입력 |

### C4. 형제 순서가 요구 사항이다
선은 **한 메쉬**(아웃라인 → 채움)라 통째로 한 번에 그려진다. 따라서
`[끝 아웃라인 Image] → PatternLineRenderer → [끝 채움 Image]` 순서로 끼워야
캡슐 안에서 일어나던 "아웃라인 위에 채움"이 끝 장식에서도 같게 성립한다.

### C5. 진짜 한계 — 반투명 이중 블렌딩
`BuildCapsule`은 겹침을 각도로 잘라 없앴지만(반투명에서 얼룩 방지), 별도 `Image`는
이음매에서 선과 **겹쳐 두 번 블렌딩된다**. 가이드가 불투명이면 아무 일도 없고,
반투명이면 그 자리만 진해 보인다.
→ 그때의 답은 **아웃라인을 스프라이트에 미리 그려 넣고 `Image` 하나만 쓰는 것**(코드 0줄).

### C6. 방향·시점은 이미 있는 값이다
각도는 첫/마지막 **선분 방향**(`Atan2`), 자리는 `points[0]` / `points[n-1]`.
켜고 끄는 시점도 `SetPoints`와 `FadeOutAndClear` 두 군데뿐이다 — **새 시계도 새 이벤트도 없다.**

### C7. 점이 1개인 패턴
`PopulateCapsuleMesh`는 점 1개면 원 하나를 그린다(`incidentDirs.Count == 0`).
끝 장식도 그 경우 **양 끝이 같은 점**이라 방향이 정의되지 않는다.
