# Research — ReferenceGames (닮은 게임 · 무엇을 훔칠 것인가)

공백 연출 조사(`docs/CombatIdle/Research_CombatIdle.md`)의 후속. 이 게임과 **어디가** 닮은 게임인지 축으로 갈라 정리한다.

## 0. 이 게임의 좌표

세 축이 겹쳐 있고, **세 축을 다 가진 게임은 조사 범위에서 찾지 못했다.**

| 축 | 이 게임 | 같은 축의 대표작 |
|---|---|---|
| A. 리듬 × 3D 액션 연출 | 판정에 맞춰 캐릭터가 베고 적이 갈라진다 | Hi-Fi RUSH, Beat Slayer, Metal: Hellsinger, Soundfall, Sword of Symphony |
| B. **제스처(경로 그리기) 입력** | 3x3 잠금 패턴을 이어 긋는다 | maimai(슬라이드), osu!(슬라이더), Gitaroo Man, Arcaea(아크), Ouendan/Elite Beat Agents |
| C. 무쌍 무대(1 대 다수) | 무대 원 안의 적을 차례로 벤다 | Dynasty Warriors 계열 |

**A×B 조합이 비어 있다.** 리듬 액션은 전부 버튼/방향 입력이고, 경로 그리기 입력은 전부 2D 노트 게임에 산다. 거기가 이 프로젝트의 자리다.

---

## 1. 축 A — 리듬 × 액션 (연출·타격감을 훔칠 대상)

### Hi-Fi RUSH (Tango Gameworks, 2023) — **1순위**
- 세계·캐릭터·UI가 **전부** 비트에 동기화. 그래서 "아무 일도 없는 프레임"이 없다 → `CombatIdle` 방향 A의 원본.
- 개발팀 GDC 강연이 공개돼 있다: 코어 필러로 **긍정적 피드백 루프**를 만들고 진입 장벽을 낮춘 과정.
  [GDC Vault — Developing 'Hi-Fi RUSH' Backwards](https://gdcvault.com/play/1034256/Developing-Hi-Fi-RUSH-Backwards)
- **훔칠 것**: 대기 모션의 비트 동기화, 리듬을 못 맞춰도 게임이 진행되는 관용(이 게임은 오답이 패턴을 정체시키므로 더 빡빡하다 — 비교 대상).

### Beat Slayer (2024) — **가장 가까운 형태**
- Hades식 근접 액션 + 박자 판정. 박자에 맞으면 "Tanzrausch" 상태로 진입해 공격이 세지고 **음악 강도도 같이 올라간다.**
- **훔칠 것**: 리듬 성공이 **음악 레이어를 바꾸는** 보상 구조. 이 게임은 지금 성공/실패가 음악에 아무 영향이 없다 — 판정 결과를 오디오로 되돌려주는 층이 통째로 비어 있다.
  [PC Gamer 리뷰](https://www.pcgamer.com/games/action/beat-slayer-scratches-both-my-hi-fi-rush-and-hades-itches-even-if-its-functional-soundtrack-is-short-a-few-bangers/) · [eXputer 리뷰](https://exputer.com/reviews/beat-slayer/)

### Sword of Symphony (1인 개발) — **검 + 리듬의 직계**
- **"보이지 않는 메트로놈"**: 플레이어가 아무 때나 공격 입력을 해도 게임이 그 공격을 **박자까지 붙잡아 뒀다가** 발동시킨다. 콤보를 박자에 맞추면 크리티컬 연쇄.
- **훔칠 것**: 이 게임의 §6 임팩트 정렬(입력 시각과 무관하게 임팩트를 고정 시각에 맞춤)과 **철학이 같다.** 다만 저쪽은 그 규칙을 플레이어에게 드러내 보상으로 쓴다.
  [Game Rant — invisible metronome](https://gamerant.com/sword-of-symphony-metronome-combat-system-musical-beat/)

### Soundfall (2022)
- 트윈스틱 + 리듬. **모든 것이 비트에 맞춰 움직인다** — "액션 RPG판 Crypt of the NecroDancer".
  [GodisaGeek](https://godisageek.com/2022/03/soundfall-surprised-me-with-its-blend-of-twin-stick-action-and-rythmn-game-timing/) · [Screen Rant 프리뷰](https://screenrant.com/soundfall-game-preview/)

### Metal: Hellsinger / BPM: Bullets Per Minute
- FPS 쪽 계보. Hellsinger는 **연속 성공이 음악 레이어(보컬)를 쌓는다** — Beat Slayer와 같은 보상 구조의 가장 유명한 예.
  [The Gamer — 리듬 전투가 기타히어로를 대체했다](https://www.thegamer.com/rhythm-games-combat-bpm-hi-fi-rush-metal-hellsinger-beat-saber/) · [10 Rhythm Games For Hi-Fi RUSH Fans (CBR)](https://www.cbr.com/hi-fi-rush-other-rhythm-games/)

### The Metronomicon
- 리듬 + 파티 RPG. **노트 판정이 곧 캐릭터의 스킬 발동**이라는 점이 이 게임의 "패턴 완주 = 베기"와 같은 구조.

---

## 2. 축 B — 경로 그리기 입력 (조작감·채보 설계를 훔칠 대상)

이 축이 **더 중요하다.** 이 게임의 입력은 리듬 액션이 아니라 여기서 왔다.

### maimai (SEGA, 아케이드) — **가장 직접적인 참고**
- 원형 패널의 **슬라이드 노트**: "탭한 뒤 밀어야" 하고 **탭과 슬라이드가 각각 따로 판정된다.**
- **훔칠 것**: 이 게임은 지금 경로 위 각 노드를 개별 판정하는데(§2), maimai는 **시작점 판정 / 경로 주행 판정**을 분리한다. "그어지는 중"에 줄 피드백의 설계 원본.
  [maimai — TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/Maimai)

### osu! — 슬라이더 (채보 저작의 교과서)
- 슬라이더 = 머리 / 몸통 / 꼬리. 지그재그·컷·구겨진 슬라이더 등 **모양 자체가 난이도와 표현이 되는 매핑 기법**이 문서화돼 있다.
- **훔칠 것**: `Pattern` 에셋의 모양 설계 어휘. 지금 패턴 템플릿은 "노드 나열"일 뿐 **모양의 문법**이 없다.
  [osu! wiki — Slider](https://osu.ppy.sh/wiki/en/Gameplay/Hit_object/Slider) · [Slider mapping techniques](https://osu.ppy.sh/wiki/en/Mapping_techniques/Sliders)

### Arcaea
- 화면 XY 어디에나 노트를 놓는 **비이산(non-discrete) 레인**. 3x3 격자의 반대 극단이라 대비 참고.
  [Arcaea — Wikipedia](https://en.wikipedia.org/wiki/Arcaea)

### Gitaroo Man (iNiS, PS2)
- 선을 따라가는 트레이스 구간 + 스토리 연출. **iNiS가 이후 Ouendan / Elite Beat Agents(터치 드래그)를 만든다** — 잠금 패턴 입력의 직계 조상.
  [Gitaroo Man — Wikipedia](https://en.wikipedia.org/wiki/Gitaroo_Man)

### 채보 설계 일반
- [Game design and notecharting (Native Audio)](https://exceed7.com/native-audio/rhythm-game-crash-course/game-design-and-notecharting.html) — 이산 레인 vs 유동 레인의 표현력 차이를 정리. 이 게임의 굽기 툴(`PatternChartWindow`) 개선에 직접 쓸 수 있다.

### ⚠ 잠금 패턴 입력 자체
조사 범위에서 **리듬 게임으로 만든 사례를 찾지 못했다.** 나온 것은 전부 퍼즐(BreakLock, Lock Breaker, Pattern Recall). → **이 입력은 이 프로젝트의 고유 지분**이고, 참고할 선례가 없다는 뜻이기도 하다(설계 부담이 크다).

---

## 3. 축 C — 무쌍 무대

### Dynasty Warriors 계열
- 1 대 1000. **화면에 빈 곳이 없다**는 것이 장르의 정의. 사기(morale) 시스템으로 플레이어가 개입하지 않아도 양쪽이 서로 베어 넘긴다.
  [Rolling Stone — 무쌍 장르 해설](https://www.rollingstone.com/culture/rs-gaming/musou-dynasty-warriors-explained-1235238935/) · [Dynasty Warriors 5](https://grokipedia.com/page/Dynasty_Warriors_5)
- **훔칠 것**: 링에 세워 둔 적들을 배경이 아니라 **군중**으로 굴리는 것(`CombatIdle` 방향 C).

---

## 4. 추천 — 이 순서로 볼 것

1. **Hi-Fi RUSH** + 그 GDC 강연 — 지금 문제(전투 공백)의 해답이 통째로 들어 있다. 강연은 "왜 이 루프인가"까지 설명한다.
2. **maimai 슬라이드 노트 영상** — 경로 입력의 판정·피드백을 실제로 어떻게 나누는지. 이 게임의 입력과 가장 가깝다.
3. **Sword of Symphony** — 검 + 리듬 + 1인 개발. 규모감과 "메트로놈이 공격을 붙잡는" 설계.
4. **Beat Slayer** — 리듬 성공 → 음악 강도 상승. 지금 비어 있는 보상 층의 참고.
5. **osu! 슬라이더 매핑 위키** — 채보 저작 어휘. 코드가 아니라 데이터 설계 쪽 숙제.

## 5. 지금 이 게임에 없는데 위 게임들엔 다 있는 것

조사에서 반복적으로 나온, **이 프로젝트에 통째로 비어 있는 층 셋**:

1. **판정 → 오디오 되먹임.** Hellsinger·Beat Slayer·Hi-Fi RUSH 전부 연속 성공이 음악을 바꾼다. 이 게임은 성공해도 곡이 그대로다.
2. **비트 동기화된 상시 모션.** 정지 프레임이 존재하지 않게 만드는 층(→ `CombatIdle` 방향 A).
3. **연속 성공의 가시적 상태.** Tanzrausch 같은 "지금 잘하고 있다"는 상태 표시. 이 게임은 콤보 상태가 화면에 없다.

## 출처

- [GDC Vault — Developing 'Hi-Fi RUSH' Backwards and Finding Our Positive Gameplay Loop](https://gdcvault.com/play/1034256/Developing-Hi-Fi-RUSH-Backwards)
- [PC Gamer — Beat Slayer](https://www.pcgamer.com/games/action/beat-slayer-scratches-both-my-hi-fi-rush-and-hades-itches-even-if-its-functional-soundtrack-is-short-a-few-bangers/)
- [eXputer — Beat Slayer Review](https://exputer.com/reviews/beat-slayer/)
- [GameSpew — Beat Slayer review](https://www.gamespew.com/2024/04/beat-slayer-review/)
- [Game Rant — Sword of Symphony의 보이지 않는 메트로놈](https://gamerant.com/sword-of-symphony-metronome-combat-system-musical-beat/)
- [Collider — Sword of Symphony](https://collider.com/sword-of-symphony-game-details-explained/)
- [GodisaGeek — Soundfall](https://godisageek.com/2022/03/soundfall-surprised-me-with-its-blend-of-twin-stick-action-and-rythmn-game-timing/)
- [Screen Rant — Soundfall Preview](https://screenrant.com/soundfall-game-preview/)
- [The Gamer — Rhythm Combat Games Have Replaced Music Games](https://www.thegamer.com/rhythm-games-combat-bpm-hi-fi-rush-metal-hellsinger-beat-saber/)
- [CBR — 10 Rhythm Games For Hi-Fi RUSH Fans](https://www.cbr.com/hi-fi-rush-other-rhythm-games/)
- [Unbeatable — Wikipedia](https://en.wikipedia.org/wiki/Unbeatable_(video_game))
- [maimai — TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/Maimai)
- [osu! wiki — Slider](https://osu.ppy.sh/wiki/en/Gameplay/Hit_object/Slider)
- [osu! wiki — Slider mapping techniques](https://osu.ppy.sh/wiki/en/Mapping_techniques/Sliders)
- [Arcaea — Wikipedia](https://en.wikipedia.org/wiki/Arcaea)
- [Gitaroo Man — Wikipedia](https://en.wikipedia.org/wiki/Gitaroo_Man)
- [Game design and notecharting — Native Audio](https://exceed7.com/native-audio/rhythm-game-crash-course/game-design-and-notecharting.html)
- [Rolling Stone — Dynasty Warriors / 무쌍](https://www.rollingstone.com/culture/rs-gaming/musou-dynasty-warriors-explained-1235238935/)
- [Dynasty Warriors 5 — Grokipedia](https://grokipedia.com/page/Dynasty_Warriors_5)
