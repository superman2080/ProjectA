# Story — 곡과 사운드 (Music)

> `Story_Overview.md` = 구조 / `Story_Narrative.md` = 내용 / `Story_Script.md` = 대본과 자산 / `Story_Playthrough.md` = 시간 순서 / **이 문서 = 곡이 서사를 어떻게 나르는가 + 생성 프롬프트**.
> 위 네 문서와 어긋나면 **저쪽이 옳다.**
> ⚠ 전 곡 **Suno AI 생성**이다. 기존 `Dreamer_Lv10`(NCS)은 **예시용이며 폐기한다.**
> ⚠ 전 곡 **인스트루멘털**이다. 근거는 §1-3.

---

## 1. 곡이 나르는 것 (설계 원칙 넷)

### 1-1. 두 계통이 싸우고, 전자음이 이긴다 (⚠ 이 문서의 본체)

`Story_Narrative.md` §3-2가 **이 세계에는 이름이 없고, 놓여 있는 것들의 계통이 뒤섞여 있다**를 간접 단서로 삼는다.
**음악에도 계통이 둘 있고, 그 비율이 다섯 곡에 걸쳐 뒤집힌다.**

| | 화풍(和) 악기 — 표층 | 전자음 — 진실 |
|---|---|---|
| 정체 | 칼과 아홉 자리가 있는 세계의 소리 | 휴대폰. 그녀가 실제로 만지고 있던 것 |
| 1곡 | **지배적** | 거의 안 들림 |
| 5곡 | **거의 사라짐** | **혼자 남는다** |

- 이것은 §3-5 이상 현상 표의 *"1스테이지 판정음 밑에 −18dB 전자음이 깔린다"*와 **같은 축이다.** 그 SFX 한 줄이 하던 일을 곡 전체가 이어받는다.
- ⚠ **음악도 세계를 설명하지 않는다**(§Overview 0). 비율이 옮겨 갈 뿐이고, 어느 곡도 "여기가 어디인지"를 소리로 선언하지 않는다.
- ⚠ **한 곡 안에서 뒤집지 않는다.** 곡마다 비율이 조금씩 옮겨 갈 뿐이고, 다섯 곡을 통과한 뒤에야 뒤집혀 있다. 한 곡 안에서 뒤집으면 그 곡이 스스로 정답을 말한다.
- ⚠ **1곡의 전자음은 "안 들려야" 한다.** 들리면 복선이 아니라 예고가 된다.

### 1-2. 첫 곡과 마지막 곡의 BPM이 같다

`Story_Narrative.md` §5-2의 5행 — *"5스테이지는 1스테이지의 패턴 풀을 그대로 다시 쓴다. 마지막에 그녀가 하는 것은 처음에 배운 동작이다."*
**곡도 같은 일을 한다.** 1곡과 5곡을 같은 BPM(105)으로 둔다.

- 플레이어는 5스테이지에서 **손이 편해졌다는 감각**을 먼저 느끼고, 그것이 첫 곡과 같은 템포라는 것은 나중에 안다.
- 덤으로 **트루 엔딩의 심전도 템포가 첫 곡의 템포와 같아진다**(§Script 8 — "템포는 마지막 곡의 BPM을 그대로 쓴다"). 수미상관이 공짜로 성립한다.
- ⚠ **설명하지 않는다.**

### 1-3. 목소리가 없다

- **미오는 0줄**이고, **사키의 목소리가 이 게임의 유일한 진짜 목소리**다(§Narrative 2 "바깥에서 들어오는 목소리"). 곡이 노래하면 그 규율이 깨진다.
- 기술적 근거도 같은 방향이다 — 보컬은 온셋 분석(`ChartGen.Core.OnsetDetector`)에 노이즈를 준다. **채보가 보컬의 자음에 반응하면 굽기가 통째로 흔들린다.**
- 그래서 **전 곡 인스트루멘털**이며, 이건 취향이 아니라 두 겹의 요구다.

### 1-4. 소리가 꽉 찰수록 혼자다

3스테이지가 **적이 가장 많은 곡**이고 서사적으로는 카시마가 **통제**로 넘어가는 곡이다(§Narrative 5 — "사람이 많을수록 혼자라는 그림").
음악이 같은 말을 한다: **레이어는 최대인데 멜로디가 없다.** 화성이 안 움직이고 타악만 쌓인다.

---

## 2. 다섯 곡의 설계표

| # | 카시마(§2-1) | BPM | 길이 | 화풍:전자 | 곡의 일 | 획의 성질(§5-2) |
|---|---|---|---|---|---|---|
| 1 | 신뢰 | **105** | 2:00 | 9 : 1 | **편안하다.** 좋은 밤이고 아무 일도 안 일어난다 | 크고 길게 뻗는 획 |
| 2 | 지도 | 124 | 2:15 | 7 : 3 | **가장 즐겁다.** 익숙해지는 것이 기분 좋다 | 같은 획의 반복 |
| 3 | 통제 | **140** | 2:30 | 5 : 5 | 꽉 찼는데 비어 있다. 멜로디 없음 | 방향이 자주 바뀐다 |
| 4 | 불안 · 괴리 | 132 | 2:15 | 3 : 7 | 빠른 게 아니라 **가깝다.** 공간이 없다 | 짧고 급하게 꺾인다 |
| **4-b** | (재촉) | **150** | **0:50** | **8 : 2** | **유일하게 "보스전처럼" 들린다.** 웅장하고 극적이고 끝맺음이 확실하다 | **4곡의 풀 재사용** |
| 5 | 침묵 | **105** | 2:00 | 1 : 9 | 벗겨진다. 심장박동만 남는다 | **1곡의 풀 재사용** |

- **BPM 곡선의 정점은 3곡이다.** 4곡은 더 느리지만 **16분 서브디비전이 촘촘해** 체감이 더 급하다 — 밀도로 조이지 템포로 조이지 않는다. 5곡에서 갑자기 느려지는 것이 "군중이 사라졌다"의 소리다.
- ⚠ **2곡이 가장 즐거워야 한다.** 플레이어가 이 게임을 좋아하게 되는 지점이고, 카시마를 아직 의심하지 않는 지점이다. 여기에 불길함을 넣으면 §Narrative 3-7의 「카시마를 수상하게 연출하지 않는다」를 음악이 대신 어긴다.
- ⚠ **1곡에 단조·불협·낮은 드론을 쓰지 않는다.** 튜토리얼 규율과 같다.
- ⚠ **4-b는 §1-1의 비율 곡선에서 유일하게 역행한다**(3:7 → **8:2** → 1:9). **의도된 예외다** — 이 무대는 세계가 자기 표층을 가장 크게 내세우는 자리이고(칼과 검객과 결착), 그 표층이 5곡에서 통째로 벗겨지기 때문에 **역행 자체가 가짜 결말의 소리**가 된다. ⚠ 다만 **비율 곡선의 값으로 세지 않는다** — 곡선은 1→2→3→4→5로 읽고, 4-b는 그 바깥에 얹힌 한 곡이다.
- ⚠ **4-b가 이 세트에서 유일하게 "보스전 음악"이다.** 5곡이 보스전인데 보스전처럼 들리면 안 된다는 규율(§3 곡 5)은 **여기서 그 대비를 얻는다** — 진짜 결착이 가짜 결착보다 조용하다.

### 2-1. 5곡만의 요구 — 곡이 멎어야 한다

`Story_Script.md` §7 B10: *"곡이 멎는다. 소리가 전부 빠지고, 남아 있던 마지막 한 글자가 숫자로 바뀐다."*

- Suno에 **정확한 정지**를 지시하기 어렵다. 그래서 **아웃트로가 급격히 벗겨지는 구조**로 뽑고, 실제 정지는 **게임이 페이드아웃**으로 만든다.
- 5곡은 후반으로 갈수록 악기가 하나씩 빠지도록 뽑는다. 마지막에 남는 것은 **킥(심장박동)과 전자음 하나**여야 한다 — 그래야 그 둘이 트루 엔딩의 심전도로 이어진다.

---

## 3. Suno 프롬프트 (그대로 붙여 쓴다)

**공통 설정**
- **Instrumental: ON** (전 곡)
- Style 필드에 아래 텍스트를 넣는다. 영문이 가장 안정적이다.
- 길이는 표대로. 2분 미만이면 채보 엔트리가 모자라고, 3분을 넘으면 한 스테이지가 길어진다.

### 곡 1 — 신뢰 (105 BPM)

```
Japanese neo-folk electronic, 105 BPM, warm and nostalgic, mid-tempo.
Lead melody on shakuhachi and koto in a bright major pentatonic scale.
Soft analog synth pads, gentle four-on-the-floor kick, clean crisp hi-hats.
Wide open reverb, night street ambience, spacious and unhurried.
Feels safe, kind, slightly wistful. No tension, no dissonance, no dark drones.
Very faint digital blip layer buried far under the mix, barely audible.
Instrumental, no vocals.
```
- ⚠ 마지막에서 두 번째 줄이 **1-1의 씨앗**이다. **들리면 실패다** — Suno가 그 레이어를 앞으로 끌어내면 다시 뽑는다.
- 구조 태그: `[Intro] [Verse] [Chorus] [Verse] [Chorus] [Outro]`

### 곡 2 — 지도 (124 BPM)

```
Japanese neo-folk electronic dance, 124 BPM, uplifting and confident.
Same shakuhachi motif as before, now doubled by a bright synth lead.
Driving four-on-the-floor, side-chained pads, punchy claps.
Tight looping structure, the same phrase repeating with small variations.
Joyful, momentum building, the best feeling in the whole set.
Light digital arpeggio weaving under the traditional melody.
Instrumental, no vocals.
```
- **반복이 곡의 형식이어야 한다** — `patternPool` 2행(같은 획의 반복)과 같은 축이다.
- 구조 태그: `[Intro] [Build] [Drop] [Break] [Drop] [Outro]`

### 곡 3 — 통제 (140 BPM)

```
Aggressive taiko-driven electronic, 140 BPM, dense and relentless.
Massive layered Japanese taiko drums, rapid rolls, tribal percussion stacks.
NO melodic lead, no singing instrument. Harmony stays on one static chord.
Distorted bass, metallic hits, crowded wall of sound.
Overwhelming and claustrophobic despite the energy. Feels surrounded.
Traditional and digital percussion equally loud, fighting each other.
Instrumental, no vocals.
```
- ⚠ **멜로디를 넣지 않는 것이 이 곡의 전부다.** Suno가 리드를 붙이면 다시 뽑는다 — 붙는 순간 "혼자"가 사라진다.
- 구조 태그: `[Intro] [Build] [Drop] [Drop] [Outro]`

### 곡 4 — 불안 · 괴리 (132 BPM)

```
Dark hybrid trap and Japanese electronic, 132 BPM, tense and suffocating.
Rapid 16th-note hi-hats, snare rolls, heavy distorted sub bass.
Close dry mix with almost no reverb, everything pressed right against the ear.
Traditional instruments are fading out, replaced by digital glitches and stutters.
Claustrophobic, no space, no air. Pressure rather than speed.
Instrumental, no vocals.
```
- **리버브를 줄이는 것이 "거리가 사라진다"의 소리다.** 3곡이 넓고 4곡이 좁아야 한다.
- 구조 태그: `[Intro] [Build] [Drop] [Build] [Drop] [Outro]`

### 곡 4-b — 「면을 쓴 자」 (150 BPM, 0:50)

```
Epic Japanese battle electronic, 150 BPM, 50 seconds, heroic and decisive.
Big taiko hits, shakuhachi lead returning loud and clear, driving synth bass.
Classic boss-fight energy: short intro, immediate drop, one build, hard ending.
Confident and conclusive, like a duel that settles everything.
Ends on a clean final hit, no fade out.
Instrumental, no vocals.
```
- ⚠ **일부러 클리셰여야 한다.** "드디어 최종 보스"라고 음악이 대놓고 말하는 것이 이 곡의 일이며, **그 확신이 5곡에서 배신당한다.**
- ⚠ **화풍 악기가 크게 돌아온다**(8:2). 4곡에서 거의 사라졌던 것이 여기서 되살아나는 것이 **표층이 마지막으로 우겨지는 소리**다.
- ⚠ **짧아야 한다.** 1분을 넘기면 결착이 아니라 한 스테이지가 되고, 그러면 "가짜 결말"이 아니라 진짜 5번째 스테이지로 읽힌다.
- ⚠ **끝을 페이드로 뽑지 않는다.** 마지막 타격에서 딱 끊겨야 참수와 붙는다.
- 구조 태그: `[Intro] [Drop] [Build] [Drop] [End]`

### 곡 5 — 침묵 (105 BPM)

```
Sparse minimal electronic, 105 BPM, cold and clear, almost empty.
Kick drum in a soft heartbeat pattern, two beats close together then silence.
One sustained synth tone, faint phone notification blips, occasional single piano note.
Traditional Japanese instruments are gone entirely.
Extremely restrained. Instruments drop away one by one as the track progresses.
Ends almost bare, just the heartbeat kick and a single electronic tone.
Not sad, not dramatic. Quiet and exact.
Instrumental, no vocals.
```
- ⚠ **"극적"이면 실패다.** 보스전이지만 **보스전처럼 들리면 안 된다** — 웅장한 오케스트라·합창·빌드업 드롭이 붙으면 다시 뽑는다. §Script 7 B7이 "지금까지와 완전히 같은 처치"여야 하는 것과 **같은 근거**다: 참수가 평범해야 B8이 놀랍다.
- ⚠ **화풍 악기가 하나도 없어야 한다.** 세계의 표층이 걷힌 상태를 소리가 먼저 말한다.
- 구조 태그: `[Intro] [Verse] [Verse] [Breakdown] [Outro]`

### 탐색 앰비언트 (BPM 없음, 60–90초 루프) — 구 「튜토리얼 앰비언트」

```
Quiet ambient, no drums, no beat. A single sustained koto note with long decay.
Warm room tone, distant night city hum, very sparse and still.
Calm, safe, patient. Nothing threatening.
Instrumental, no vocals.
```
- §Script 1이 요구하는 *"적 없음, 곡 없음, 잔잔한 앰비언트"*가 이것이다. 채보가 없으므로 온셋 요구도 없다.
- ⚠ **이제 이 루프가 심상세계의 탐색 구간 전체를 덮는다**(§Overview 1-1). 튜토리얼 전용이 아니라 **곡이 흐르지 않는 모든 시간의 소리**이며, 그래서 이 게임에서 **가장 오래 재생되는 오디오**다.
- **다섯 장소가 같은 루프를 쓴다. 신규 곡 0.** 스테이지 단계(§Overview 5)는 **필터·믹스로만** 나눈다:

| 스테이지 | 같은 루프에 거는 것 |
|---|---|
| 1 | 원본 그대로 |
| 2 | 하이패스 살짝 — **밤의 도시 소리가 빠진다**(하늘이 없다) |
| 3 | 짧은 딜레이 — **같은 코토 음이 두 번 들린다**(반복) |
| 4 | 리버브 크게 · 룸톤 제거 — **벽이 높다** |
| 4-b | **4와 같다**(같은 실내). 따로 만들지 않는다 |
| 5 | **무음.** 정확한 도구실에는 앰비언트가 없다 |

- ⚠ **5스테이지의 무음이 §2-1(곡이 멎어야 한다)과 같은 장치다.** 소리가 사라지는 것이 이상 현상의 마지막 단계다.
- ⚠ **탐색 앰비언트에 리듬을 넣지 않는다.** 박이 있으면 플레이어의 손이 미리 준비하고, 무대에 들어서는 순간의 곡 시작이 **사건이 아니라 이어짐**이 된다.
- **곡 ↔ 앰비언트 전환은 크로스페이드 하나다.** 무대 진입 → 앰비언트 페이드 아웃 → `countdownDuration` 3초(인트로) → 곡. 종료 후 역순. ⚠ **로딩·페이드아웃 암전이 그 사이에 없다** — 씬 전환이 사라졌다는 사실이 소리에서 먼저 들린다.

---

## 4. 채보 관점의 요구 (⚠ 안 지키면 굽기가 안 된다)

`ChartGen.Core.OnsetDetector`가 **음압 변화**로 온셋을 잡는다. 곡이 서사에 맞아도 아래를 어기면 채보를 못 굽는다.

| 요구 | 이유 |
|---|---|
| **킥·스네어가 뚜렷할 것** | 온셋이 안 걸리면 엔트리가 안 만들어진다 |
| **템포가 일정할 것** | `BeatGrid`가 고정 BPM을 전제한다. 루바토·템포 체인지 금지 |
| **앰비언트 구간을 30초 넘게 두지 말 것** | 그 구간에 패턴이 안 생겨 플레이가 빈다 |
| **보컬 없음** | §1-3 |
| **곡 간 음압을 맞출 것** | 곡마다 판정음 대비 밸런스가 달라진다 |

- ⚠ **3곡이 가장 위험하다.** 멜로디를 빼면 Suno가 앰비언트로 흐르기 쉬운데, 이 곡은 적이 가장 많아 온셋이 가장 많이 필요하다. **타악은 촘촘하되 멜로디는 없는** 상태를 유지해야 한다.
- ⚠ **5곡도 위험하다.** 벗겨지는 곡이라 후반 온셋이 희박해진다. 후반 패턴은 **사슬**(§11-5)이라 입력이 촘촘한데 곡은 비어 간다 — 굽기 후 수동 보정이 필요할 수 있다.

---

## 5. 다섯 곡을 한 세계로 묶는 법 (Suno의 약점)

**같은 모티프를 다섯 곡에 관통시키는 것**이 §1-1·1-2의 전제인데, Suno는 곡 간 모티프 유지가 약하다. 순서를 이렇게 잡는다.

1. **곡 1을 먼저 확정한다.** 여기서 나온 화풍 멜로디가 전 세계의 씨앗이다.
2. 곡 1이 확정되면 **Persona / Cover / Extend 기능으로 곡 2를 파생**시킨다. 새로 뽑는 것보다 모티프가 남을 확률이 높다.
3. 곡 3·4는 모티프를 **버려도 되는 구간**이다(3은 멜로디 자체가 없고, 4는 화풍이 물러나는 중이다). 여기서 연결이 끊겨도 손해가 적다.
4. **곡 4-b도 곡 1에서 파생시킨다.** 화풍 멜로디가 크게 돌아오는 곡이라(8:2) 씨앗이 같아야 *"표층이 마지막으로 우겨진다"*가 성립한다. ⚠ **곡 4에서 파생시키지 않는다** — 4는 화풍이 물러난 곡이라 되살릴 멜로디가 없다.
5. **곡 5는 곡 1에서 다시 파생시킨다.** 같은 BPM이고 「처음에 배운 동작」이므로, 오히려 1과 이어지는 편이 옳다.

- ⚠ 모티프 관통이 끝내 안 되면 **BPM 일치(1↔5)만은 반드시 지킨다.** 그것 하나로 §1-2가 성립한다.
- ⚠ **곡을 다섯 개 다 뽑고 고르지 않는다.** 1을 확정한 뒤 파생시켜야 세계가 하나가 된다.

---

## 6. 소리가 나지 않는 것들 (⚠ 이 문서의 두 번째 규칙)

**이 게임에는 소리를 내지 않는 것이 셋 있고, 그 침묵이 전부 설계다.**

| | 소리 | 이유 |
|---|---|---|
| **미오** | 0줄 | §Overview 3 |
| **「등을 보인 사람」** | **0. 전용 SFX도, 전용 모티프도, 등장 스팅어도 없다** | 소리가 붙는 순간 "초자연적 존재"가 되고 §Overview 4-2의 계약이 깨진다 |
| **닫힌 문** | 0 (트루 엔딩에서 **열릴 때 한 번**만 난다) | 다섯 스테이지 내내 소리가 없던 물건이 마지막에 소리를 낸다 |
| **「면을 쓴 자」** | **목소리 0.** 발소리와 칼 소리만(기존 적 SFX 재사용) | 목소리를 주면 그가 **자기를 설명하게** 된다(§Narrative). 등장 스팅어·전용 모티프도 없다 — 그의 곡은 **무대에 들어선 뒤에야** 시작된다 |

- ⚠ **그녀에게 라이트모티프를 주고 싶은 유혹을 거절한다.** 음악이 "저 사람이 중요하다"고 말하는 순간, 카시마도 플레이어도 모르는 사실을 **작곡가만 아는 상태**가 된다 — 그게 §Overview 0을 음악이 대신 어기는 방식이다.
- **대신 그녀가 나타나는 구간은 이미 조용하다.** 회피 성공 직후는 기습이 끝난 직후이고(§11-8), 각본형 등장은 인트로 3초·클리어 직후다. **비어 있는 구간에 서 있는 것만으로 충분하다.**
- ⚠ **탐색 중 그녀에게 다가갈 때도 소리가 없다**(§Overview 4-2). 접근 페이드아웃에 소리를 붙이면 그 부재가 **연출**이 되고, 그러면 "게임이 아껴 둔 것"으로 읽힌다.

#### 일렁이는 자리의 소리 (§Overview 2-4)

- **아직 가라앉지 않은 무대에서는 §Narrative 3-5의 그 −18dB 전자음이 들린다.** 탐색 앰비언트 위에 얹히며, **가까워질수록 커진다**(거리 감쇠 하나. 새 자산 0).
- ⚠ **경고음이 아니다.** 톤·불협·심박·저역 드론 전부 금지 — **판정음 밑에 깔려 있던 그 소리 그대로**여야 한다. 그래야 5스테이지에서 그것이 무엇이었는지 뒤집힐 때 *"내내 들리던 소리"*가 성립한다.
- **가라앉은 자리는 완전히 무음이다.** 앰비언트만 남는다 — 그리고 **트루 엔딩으로 가는 세계는 이 소리가 하나도 남지 않은 세계다.**

### 6-1. 기습 보이스와 음악의 관계

`Kashima_Ambush_01`(*"뒤를 보지 마."*, §Script 4-1)은 **다섯 곡 전부에서 재생되는 유일한 보이스**다.

- ⚠ **믹스에서 이 한 줄이 묻히면 안 된다.** 기습 텔레그래프 구간(1.0초)에서 **음악을 −3dB 덕킹**한다. 판정음은 건드리지 않는다.
- ⚠ **곡마다 다르게 처리하지 않는다.** 같은 볼륨·같은 위치(센터·드라이). 다섯 스테이지 내내 **똑같이 들리는 것**이 이 장치의 전부다.
- 5곡에서는 악기가 이미 벗겨져 있어 덕킹이 거의 필요 없다 — **그래서 마지막에 이 목소리가 가장 또렷하다.**

---

## 7. 엔딩의 소리 (신규 곡 0)

| 엔딩 | 소리 | 신규 자산 |
|---|---|---|
| **배드** | 격자가 꺼지는 소리 → 조명 → 암전 → `Saki_Bad` 두 번 → 끊김 | **0** |
| **굿** | **문이 열리는 소리** → 죽도 부딪히는 소리 → 자판기 → 매미 → `Saki_Good` | 환경음 3개 + 문 1개 |
| **트루** | **문이 열리는 소리** → **곡 5의 킥이 심전도로 이어진다.** 템포 유지, 샷 8까지 | 심전도 SFX 1개 (문은 굿과 공유) |

- **엔딩 전용 곡이 없다.** 곡 5의 마지막 요소(심장박동 킥)가 그대로 심전도가 되므로 §Script 8의 *"곡이 끊기지 않는다"*가 새 자산 없이 성립한다.
- ⚠ **참수·몽타주(§Script 7 B7–B9)에는 음악을 얹지 않는다.** 곡은 B7까지 평범하게 흐르고, B8에서 **악기만 빠진다**(정지 아님). 몽타주 8컷 아래에는 **컷 전환음도 없다** — 오케스트라 히트나 스팅어가 들어가면 "반전!"이라고 소리가 말하는 것이 된다.
- **「마지막 한 번」(§Script 8 공통 0-a~0-d)에는 음악이 없다.** 곡은 B10에서 멎었고, 그 구간의 소리는 §Narrative 3-5의 **그 전자음 하나**뿐이다.

---

## 8. 자산 요약

| 항목 | 수량 | 조달 |
|---|---|---|
| 스테이지 곡 | **6** (1~5 + **4-b**) | Suno. 4-b는 0:50 |
| 탐색 앰비언트 | **1** | Suno. 다섯 장소가 공유하고 **필터로만** 갈린다(§3) |
| 심전도 SFX | 1 | 생성 또는 무료 음원 |
| 환경음(죽도·자판기·매미) | 3 | 무료 음원 |
| **문 열리는 소리** | **1** | 굿·트루 공유. **다섯 스테이지 내내 안 나던 소리** |
| 판정음 밑 −18dB 전자음 | 1 | §Narrative 3-5. 기존 판정음에 레이어 |
| 기습 보이스 | 1 | §Script 4-1. 보이스 자산이며 음악 아님 |
| **「등을 보인 사람」 전용 사운드** | **0** | §6 |
| **엔딩 전용 곡** | **0** | §7 |
