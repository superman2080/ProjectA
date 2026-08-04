# Research — CombatIdle (교전 공백을 메우는 법)

## 문제

적과 플레이어가 만난 뒤 다음 공격이 시작될 때까지 **둘 다 가만히 서 있는 구간**이 있고, 그게 길어서 전투가 끊긴 것처럼 보인다.

## 1. 이 게임의 공백은 어디서 나오는가 (구조)

공백은 버그가 아니라 **현재 설계의 산술적 귀결**이다. 두 배우가 각자 "일찍 도착"하도록 만들어져 있다.

| 구간 | 길이(실측·설정값) | 근거 |
|---|---|---|
| 패턴 주기(평균) | **1.38초** | `CLAUDE.md` §7-5 |
| 결투 창(평균) | **1.48초** | `docs/FailConverge/Research_FailConverge.md` |
| 적 이동 | 1m 남짓을 `moveSpeed` 3m/s = **≈0.33초** | `EnemyView.EarliestArrival` |
| → **적이 서 있는 시간** | **≈1.1초** | 창 − 이동 |
| 플레이어 이동 | 창 전체(share=1) 또는 창×`playerApproachShare` | `PlayerCombatMover` |
| → **플레이어가 서 있는 시간** | 0초(share=1) ~ **0.74초**(share=0.5) | 방금 추가한 노브 |

**⚠ 적이 일찍 서는 것은 의도된 것이다**(§11-2) — 안 그러면 "도착하는 순간이 곧 베이는 순간"이라 서 있는 구간이 아예 없고, 화면에는 제자리 Run이 돈다. 즉 **공백은 그 결정의 대가**이고, 없애는 게 아니라 **메워야 하는** 것이다.

그리고 `playerApproachShare`를 내리면 **플레이어 쪽 공백이 새로 생긴다.** 지금 겪는 어색함이 그것과 겹쳐 있을 가능성이 높다.

### 지금 그 구간에 실제로 재생되는 것

- 플레이어: `Katana_Idle`(도착 후 복귀 로직이 base를 Idle로 되돌림 — 방금 고친 부분)
- 적: 대기 포즈. 링에 서 있는 **나머지 적들도 전부 정지**
- 카메라: 프레이밍 감쇠만. 앵글 교체는 `switchInterval` 6초라 대부분의 공백에는 안 걸림
- 음악: 계속 흐름 ← **여기가 핵심 비대칭이다. 소리는 진행하는데 화면만 멈춘다.**

---

## 2. 다른 게임은 이 공백을 어떻게 메우는가

### 2-1. "대기 = 참여"로 바꾼다 (액션 게임의 표준 해법)

근접 전투 게임은 **공격하지 않는 적을 절대 정지시키지 않는다.**

> "적이 공격하지 않을 때는 플레이어 주위를 돌거나, 다른 적을 응원하는 아이들 애니메이션을 재생하거나, 플레이어를 도발한다. **여전히 싸움에 참여하고 있는 것처럼 보이게 하는 데 이것이 매우 중요하다.**"
> — [Enemy design and enemy AI for melee combat systems (Game Developer)](https://www.gamedeveloper.com/design/enemy-design-and-enemy-ai-for-melee-combat-systems)

구체 행동: **카메라 안으로 들어오도록 위치 이동 / 플레이어 주위 선회 / 슬롯에서 좌우 페이싱 / 도발**.

동시 공격 수를 제한하는 **어택 토큰 · 쿵푸 서클**이 이 문제와 한 쌍이다 — 토큰을 못 받은 적은 "아무것도 안 하는 적"이 되므로, 그 적들의 대기 연기가 곧 시스템의 절반이다.

- [Beyond the Kung-Fu Circle: A Flexible System for Managing NPC Attacks (Game AI Pro, PDF)](http://www.gameaipro.com/GameAIPro/GameAIPro_Chapter28_Beyond_the_Kung-Fu_Circle_A_Flexible_System_for_Managing_NPC_Attacks.pdf) — *Kingdoms of Amalur*의 스테이지 매니저. 등록된 AI는 **자기 슬롯에서 좌우로 페이싱하며 허가를 기다린다.**
- [Battle Circle AI (Envato Tuts+)](https://gamedevelopment.tutsplus.com/tutorials/battle-circle-ai-let-your-player-feel-like-theyre-fighting-lots-of-enemies--gamedev-13535) — 안쪽 원(공격)/바깥 원(대기) 분리. 슬롯 예약제.
- [Realtime turn-based AI (Sam Bloomberg, ex-Rocksteady/Arkham)](https://xbloom.io/2021/03/19/designing-a-combat-system/) — 타깃이 **토큰 소스 컴포넌트**를 들고 공격 순번을 배급한다. *DOOM(2016)*도 같은 토큰 방식.
- 근접 공격 빈도는 **평균 2~3초에 한 번**으로 규제된다(위 Game Developer 기사). ⚠ **이 게임은 1.38초마다 한 번**이라 이미 그 두 배로 빽빽하다 — 공백이 어색한 이유가 여기 있다. **밀도가 낮아서가 아니라, 빽빽한 리듬 사이에 완전 정지가 끼어서**다.

### 2-2. 공백을 **예고(윈드업)**로 바꾼다

액션 게임은 공백을 "아무 일 없음"이 아니라 **다음 사건의 준비 동작**으로 채운다.

> 텔레그래프는 보통 "적 공격 애니메이션의 **시작 부분**"이며, 플레이어의 기대를 설정하는 데 쓰인다. *Devil May Cry*의 적은 **① 준비 자세 → ② 무기 번쩍임 + 효과음 → ③ 또 한 번의 준비 → ④ 휘두르기** 순으로 간다.
> — [Game Developer, 같은 기사](https://www.gamedeveloper.com/design/enemy-design-and-enemy-ai-for-melee-combat-systems)

리듬 게임에서는 이게 **연출이 아니라 판정 정보**다:

> "시청각 큐의 명료함이 대단히 중요하다 — **비트에 이르는 구간(lead up)**에 무엇을 쳐야 하는지 애니메이션과 사운드로 알린다."
> — [Rhythm Heaven design notes (Medium)](https://medium.com/quick-game-design-notes/rhythm-heaven-quick-design-notes-5abb6ce52e97)

### 2-3. **모든 것을 비트에 붙인다** (리듬 액션의 정답)

*Hi-Fi RUSH*는 정지 구간 자체를 없앤다 — 서 있는 캐릭터도, 배경도, UI도 비트에 맞춰 움직인다. 그래서 **"아무 일도 없는 프레임"이 존재하지 않는다.** 팀은 이걸 진입 장벽을 낮추는 "긍정적 피드백 루프"의 축으로 설명한다.

- [GDC Vault — Developing 'Hi-Fi RUSH' Backwards and Finding Our Positive Gameplay Loop](https://gdcvault.com/play/1034256/Developing-Hi-Fi-RUSH-Backwards)
- [Games Are Polyrhythmic & Syncopated](https://www.patreon.com/posts/games-are-113662035) — 게임의 여러 층이 서로 다른 주기로 뛰며 겹쳐질 때 리듬감이 생긴다는 분석.

*Crypt of the NecroDancer* 계열도 같다 — **적이 비트마다 움직이므로** 플레이어가 안 움직이는 순간에도 화면은 박자를 친다.

### 2-4. **군중**으로 메운다 (무쌍)

*Dynasty Warriors*는 1 대 1000이라 **화면에 빈 곳이 없다.** 주인공이 쉬는 순간에도 잡병·아군이 계속 움직이고, 사기(morale) 시스템으로 **플레이어 개입 없이도 양쪽이 서로를 베어 넘긴다.**

- [How 'Dynasty Warriors' Created One of Gaming's Best Action Subgenres (Rolling Stone)](https://www.rollingstone.com/culture/rs-gaming/musou-dynasty-warriors-explained-1235238935/)
- [Dynasty Warriors 5 (Grokipedia)](https://grokipedia.com/page/Dynasty_Warriors_5) — 모라레 기반 자율 전투.

⚠ **이 게임에는 이미 무대 위에 대기 중인 적들이 있다**(`stageRadius` 8, 링). 지금은 그들이 전부 정지해 있어 **군중이 아니라 마네킹**이다. 가장 값싼 자원이 여기 놀고 있다.

### 2-5. 아이들 자체의 품질 (기본기)

> 전투 아이들은 **미묘한 움직임 — 무게 이동, 가드 조정, 호흡 —** 을 담아 "정지"가 아니라 "준비됨"을 전달해야 한다. 무릎을 살짝 굽혀 무게를 옮길 수 있게 두면 **호흡처럼 위아래로 흔들리는 리듬**이 생긴다.
> — [Idle Fighting Stance (SLYNYRD)](https://www.slynyrd.com/blog/2024/9/26/pixelblog-52-idle-fighting-stance) · [Idle Animation for Games: Design Guide (MoCap Online)](https://mocaponline.com/blogs/mocap-news/idle-animation-game-dev-guide)

"아이들 브레이크"(가끔 튀어나오는 변주 동작)도 같은 목적이다.

### 2-6. 이론: 죽은 시간(dead time) vs 쉬는 시간(down time)

> **dead time**은 아무것도 계획할 수 없고 아무것도 걸려 있지 않은 시간이다. 설계자의 일은 dead time을 **plan time**(다음을 준비하는 시간)이나 **rest time**(의도적 완급)으로 바꾸는 것이다.
> — [Down Time vs. Dead Time in Sequential Games (Game Developer)](https://www.gamedeveloper.com/design/down-time-vs-dead-time-in-sequential-games) · [Downtime vs. Dead time in Games (Wiltgren)](https://www.wiltgren.com/downtime-vs-dead-time/)

> 완급은 **대비제**로 작동한다 — 느려지는 구간이 있어야 돌아온 전투가 더 날카롭게 느껴진다.
> — [Idle Time and Active Space: Pacing in Grid-Based Design](https://gaminggraduate.com/idle-time-and-active-space-pacing-in-grid-based-design/)

⚠ 이 대비 논리가 이 게임에는 **거의 적용되지 않는다.** 완급의 주인이 이미 음악이고, 채보가 공백을 만들 때만 쉼표여야 한다. **음악이 계속 치고 있는데 화면만 멈추는 건 rest가 아니라 dead다.**

---

## 3. 이 게임에 옮길 때의 제약

| 제약 | 결과 |
|---|---|
| 판정은 패턴인풋(ScreenSpaceOverlay)에서만 일어난다 | 3D 쪽에서 **무엇을 하든 판정에 개입하지 않는다** → 연출 자유도가 매우 높다(§7-5의 논리와 같다) |
| 공백 길이를 미리 안다 | `arriveTime`·`impactTime`이 전부 계산돼 있다 → **"몇 초짜리 공백"인지 알고 연출을 고를 수 있다** |
| BPM·beatOffset이 `SongChart`에 있다 | 비트 동기화 연출의 재료가 이미 있다(런타임에 아무도 안 읽는다) |
| 적 링이 이미 존재한다 | 군중 연출의 배우가 이미 무대에 서 있다 |
| 타임스케일 금지(§7-3) | 완급을 시간으로 못 만든다 → **공간·모션·카메라로만** 만든다 |
| 배우 정렬은 임팩트 한 점에만 묶인다(§6) | 임팩트 **이전** 구간은 자유롭게 채워도 정렬이 안 깨진다 |

---

## 4. 방향성 (권장 순서)

### 방향 A — 대기 자세를 비트에 붙인다 (권장 1순위)
서 있는 모든 배우(플레이어·상대·링의 적들)의 Idle을 **곡의 비트에 맞춰** 재생한다. `SongChart.bpm`/`beatOffset`은 이미 있고 런타임에 아무도 안 읽는다.
- 값싸다: Animator `speed`나 파라미터 하나를 비트에서 유도. 새 클립 불필요.
- 효과가 크다: *Hi-Fi RUSH*의 핵심이 이것이고, **"멈춘 화면"이 "박자를 타는 화면"으로 바뀐다.**
- 리듬 게임에서만 가능한 답이라 이 프로젝트의 정체성과 맞는다.

### 방향 B — 공백을 윈드업으로 바꾼다 (권장 2순위)
적 공격 클립을 **더 일찍, 더 느리게** 시작해 공백을 준비 동작으로 덮는다. 지금은 `ResolveScheduleStart`가 임팩트에서 역산해 **필요한 만큼만** 앞당기므로 남는 시간이 그대로 공백이 된다.
- ⚠ §6의 임팩트 정렬은 유지된다 — 시작만 앞당기고 배속을 낮추면 임팩트 시각은 그대로다.
- 부가 이득: 리듬 게임의 **선행 큐**가 생긴다(§2-2). 화면만 봐도 다음 타점이 언제인지 읽힌다.
- 비용: 배속 하한(`minPlaySpeed`)과 클립 길이에 걸린다. 공백이 클립보다 길면 남는다 → A와 함께 써야 완전히 덮인다.

### 방향 C — 링의 적을 군중으로 되살린다 (권장 3순위)
지금 무대에 서 있는 적들이 전부 정지 상태다. 선회·무게 이동·도발·간헐적 전진만 넣어도 화면이 통째로 살아난다(§2-1·2-4).
- 값싸다: `EnemyView`에 대기 로코모션 하나 + `EnemyDirector`가 링 전체에 뿌리기.
- ⚠ 절두체 안에 있는 적만 움직이게 해야 비용이 안 샌다(시야 판정 코드가 이미 있다 — `BuildVisibilityTest`).

### 방향 D — 공백 자체를 줄인다 (보조)
`playerApproachShare`를 1에 가깝게 두고, 적의 `EarliestArrival`도 "필요한 만큼만 일찍"으로 조인다. **다만 이건 §11-2가 의도적으로 만든 여유라 되돌리면 제자리 Run이 돌아온다** — A/B/C 없이 이것만 하면 문제가 이동할 뿐이다.

### 방향 E — 카메라로 메운다 (폴리시)
`CameraAngleSwitcher`(6초 주기)와 방금 만든 임팩트 줌이 이미 있다. **공백 길이를 알고 있으므로**, 긴 공백에만 앵글 전환이나 느린 푸시인을 배정할 수 있다.
- ⚠ §7-5의 규율 — 카메라 큐가 임팩트·히트스톱을 덮으면 안 된다. 공백 한복판이 정확히 그 안전 구간이다.

---

## 5. 권장 조합

**A + B를 먼저.** 둘이 서로를 보완한다 — B가 공백의 **앞쪽**(적의 준비 동작)을 덮고, A가 **남는 전부**를 박자로 채운다. C는 화면 전체의 밀도를 올리는 별개 층이라 언제 넣어도 되고, D는 A/B/C가 끝난 뒤 남는 어색함을 보고 판단한다.

**측정 먼저**: 실제 공백 길이 분포를 로그로 뽑는 게 첫 단계다. 지금은 "1.1초쯤"이 평균 추정치일 뿐이고, 채보마다 크게 흔들릴 것이다(창이 0.5~2.1초로 4배 흔들린다 — §11-2).

## 출처

- [Enemy design and enemy AI for melee combat systems — Game Developer](https://www.gamedeveloper.com/design/enemy-design-and-enemy-ai-for-melee-combat-systems)
- [Beyond the Kung-Fu Circle: A Flexible System for Managing NPC Attacks — Game AI Pro (PDF)](http://www.gameaipro.com/GameAIPro/GameAIPro_Chapter28_Beyond_the_Kung-Fu_Circle_A_Flexible_System_for_Managing_NPC_Attacks.pdf)
- [Battle Circle AI: Let Your Player Feel Like They're Fighting Lots of Enemies — Envato Tuts+](https://gamedevelopment.tutsplus.com/tutorials/battle-circle-ai-let-your-player-feel-like-theyre-fighting-lots-of-enemies--gamedev-13535)
- [Realtime turn-based AI — Sam Bloomberg](https://xbloom.io/2021/03/19/designing-a-combat-system/)
- [Developing 'Hi-Fi RUSH' Backwards and Finding Our Positive Gameplay Loop — GDC Vault](https://gdcvault.com/play/1034256/Developing-Hi-Fi-RUSH-Backwards)
- [Games Are Polyrhythmic & Syncopated](https://www.patreon.com/posts/games-are-113662035)
- [Rhythm Heaven quick design notes — Medium](https://medium.com/quick-game-design-notes/rhythm-heaven-quick-design-notes-5abb6ce52e97)
- [How 'Dynasty Warriors' Created One of Gaming's Best Action Subgenres — Rolling Stone](https://www.rollingstone.com/culture/rs-gaming/musou-dynasty-warriors-explained-1235238935/)
- [Dynasty Warriors 5 — Grokipedia](https://grokipedia.com/page/Dynasty_Warriors_5)
- [Pixelblog 52 — Idle Fighting Stance — SLYNYRD](https://www.slynyrd.com/blog/2024/9/26/pixelblog-52-idle-fighting-stance)
- [Idle Animation for Games: Design Guide — MoCap Online](https://mocaponline.com/blogs/mocap-news/idle-animation-game-dev-guide)
- [Down Time vs. Dead Time in Sequential Games — Game Developer](https://www.gamedeveloper.com/design/down-time-vs-dead-time-in-sequential-games)
- [Downtime vs. Dead time in Games — Wiltgren](https://www.wiltgren.com/downtime-vs-dead-time/)
- [Idle Time and Active Space: Pacing in Grid-Based Design — Gaming Graduate](https://gaminggraduate.com/idle-time-and-active-space-pacing-in-grid-based-design/)
