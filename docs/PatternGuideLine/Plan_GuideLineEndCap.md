# Plan — 가이드라인 끝 장식 (GuideLineEndCap)

근거: `docs/PatternGuideLine/Research_GuideLineEndCap.md`

## 설계 결정

1. **메쉬를 안 건드린다.** 끝 장식은 씬의 `Image` 오브젝트이고 `PatternLineRenderer`는
   **그 자리와 색만 밀어 준다**(C1). `OnPopulateMesh` 경로는 한 줄도 안 바뀌므로
   기존 가이드의 회귀가 구조적으로 0이다.
2. **주인은 `PatternLineRenderer`다.** `PatternHandler`는 지금도 `SetPoints` 하나만 부르고
   끝점이 어디인지 이미 그 안에 있다(C6) — 호출부를 늘리면 진실의 원천이 둘이 된다.
3. **끝 하나당 `Image` 둘**(뒤: 아웃라인, 앞: 채움). 아웃라인이 원래 "크게 한 번 더"라
   **새 개념이 0개**다(C2). 배율은 코드가 `(lineWidth/2 + outlineWidth) / (lineWidth/2)`로 낸다.
4. **배선이 비면 기능만 조용히 꺼진다** — 지금 가이드 그대로다(`EffectManager` 프리팹 규율과 같은 결).
   그래서 `BattleScene`·`Tutorial` 중 한쪽만 붙여도 안전하다.
5. **`endCapSprite` 같은 저작 필드를 안 만든다.** 스프라이트·색조·머티리얼·9-slice는
   `Image` 인스펙터가 이미 가진 것이고, 코드가 필드로 다시 받으면 **같은 값이 두 군데** 생긴다.
   코드가 미는 것은 **자리·각도·크기·색** 넷뿐이다.
6. **회전은 켜고 끌 수 있다**(`rotateCapsToPath`). 화살표는 경로를 따라 돌아야 하고,
   장식(문양·불꽃)은 돌면 어색하다 — 어느 쪽이 맞는지는 그림이 정한다.

## 단계

- [ ] **Step 1 — 배선 필드**: `PatternLineRenderer`에 끝 장식 참조를 넣는다.
      `startCapFill` · `startCapOutline` · `endCapFill` · `endCapOutline`(전부 `RectTransform`,
      색은 `GetComponent<Graphic>()`로 집는다 — `Image`로 못 박으면 다른 그래픽을 못 쓴다) +
      `rotateCapsToPath`(bool, 기본 true).
- [ ] **Step 2 — 배치**: `SetPoints`/`FadeOutAndClear`/`SetCorrectState`가 지나가는 자리마다
      `ApplyEndCaps()` 하나를 부른다. 그 안에서
      **자리**(`points[0]`, `points[n-1]`) · **각도**(첫·마지막 선분 방향, `rotateCapsToPath`일 때만) ·
      **크기**(채움 = `lineWidth`, 아웃라인 = `lineWidth + outlineWidth * 2`) ·
      **색**(`BuildPointColors`와 **같은 식** — 그라데이션이면 `Evaluate(0)`/`Evaluate(1)`,
      아니면 `missColor`/`normalColor`, 양쪽 다 `WithFadeAlpha`)을 민다.
      ⚠ 색 계산을 새로 적지 않는다 — 기존 헬퍼를 그대로 부른다(둘이 갈리면 끝만 다른 색이 된다).
- [ ] **Step 3 — 표시/숨김**: 점이 0개면 끝 장식도 끈다(`SetActive(false)`).
      ⚠ `capsuleMode`가 꺼진 입력 라인에서는 **언제나 끈다** — 가이드 전용 기능이다.
      ⚠ 점이 1개면 방향이 없다(C7) — 각도는 0으로 두고 **양 끝을 같은 자리**에 놓는다(겹쳐 하나로 보인다).
- [ ] **Step 4 — 페이드**: `FadeRoutine`이 매 프레임 `SetVerticesDirty`를 부르는 그 자리에서
      `ApplyEndCaps()`도 부른다. 알파가 `currentAlpha` 하나에서 나오므로 선과 정확히 같이 사라진다.
- [ ] **Step 5 — 씬 배선**: `PointBackground` 아래에 형제 순서를
      `[EndCapOutline ×2] → PatternGuideLine → [EndCapFill ×2]`로 놓는다(C4).
      ⚠ 가이드 자체는 여전히 `PointBackground`의 **첫 자식 그룹**이어야 한다(Point·노드·입력 라인 아래).
      `BattleScene`·`Tutorial` 양쪽에 같은 구성.
- [ ] **Step 6 — 확인**: 패턴 하나를 재생해 ① 끝 장식이 경로 끝에 붙는지 ② 아웃라인이 선과 이어져 보이는지
      ③ 페이드가 같이 지는지 ④ 연타 패턴에서 안 뜨는지(가이드 자체가 안 뜬다). 콘솔 에러 0건.

## 안 하는 것

- 노드마다 장식 붙이기 — 요구는 양 끝이다. 자리 계산이 이미 목록이라 필요해지면 그때 늘린다.
- 메쉬 안에서 스프라이트 그리기(C1) — 가이드 전체를 아틀라스에 묶는 대가가 장식 하나보다 크다.
- 반투명 이중 블렌딩 보정(C5) — 가이드가 불투명인 동안은 증상이 없다.
  보이면 **아웃라인을 스프라이트에 그려 넣고 `Image` 하나만** 쓰면 되고 그건 코드 변경이 아니다.
