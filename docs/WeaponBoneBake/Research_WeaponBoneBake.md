# Research — WeaponBoneBake (무기 본 커브가 빠진 클립에 칼/검집 굽기)

## 목표
`Flurry_Slashes` 재생 중 **칼이 손에 들려 있지 않은** 문제를 해결한다.
검집은 허리춤에 그대로 두고, 칼만 오른손에 쥐어지도록 한다.

---

## 원인

### 리그 구조 — 무기 본은 부모를 따라가지 않는다
캐릭터(`School_Katana_FullBody-Magica cloth2`)의 무기 본 계층:

```
School_Katana_FullBody-Magica cloth2
└── root
    ├── add_weapon_l      ← 검집이 붙는 본
    ├── add_weapon_r      ← 칼이 붙는 본
    └── (pelvis → spine_01..03 → ... → hand_l / hand_r)
```

**`add_weapon_l` / `add_weapon_r`은 손이나 골반의 자식이 아니라 `root`의 직계 자식이다.**
따라서 부모를 따라 손에 붙는 일이 없고, **애니메이션 클립이 매 프레임 위치·회전 키를 찍어줘야만** 제자리에 간다.

### 메시 바인딩 — 각 무기는 단일 본에 강체로 붙어 있다
프리팹의 SkinnedMeshRenderer:

| 메시 | rootBone | 본 수 |
|---|---|---|
| `Weapon_Katana` (칼) | **`add_weapon_r`** | 1 |
| `Weapon_Katana_sheath` (검집) | **`add_weapon_l`** | 1 |

본이 하나뿐인 강체 스킨이므로, **해당 본의 트랜스폼이 곧 무기의 트랜스폼**이다. 굽는 대상이 명확하다.

### 클립별 무기 커브 유무
`root/add_weapon_r` / `root/add_weapon_l`에 대한 `m_PositionCurves` + `m_RotationCurves` 보유 여부:

| 클립 | 길이 | 무기 커브 |
|---|---|---|
| `Swipe_1To9` / `3To7` / `4To6` / `6To4` / `8To2` / `9To1` | 2.0~2.6 | **r+l** |
| `Swipe_DoubleTime` / `Stab_5` / `Release` / `Run` | 0.67~4.5 | **r+l** |
| `Sprint_Forward` | 0.67 | **r만** (검집 커브 없음) |
| **`Flurry_Slashes`** | **4.0** | **없음** |
| **`Cross_Slash`** | **4.0** | **없음** |
| **`Down_Up_Slash`** | **4.0** | **없음** |
| `Placeholder_A/B`, `AttackSlot_Placeholder` | 1.0 | 없음(더미라 무관) |

→ **결함은 4.0초짜리 세 클립에 공통**이다(같은 소스에서 온 것으로 보인다). `Flurry_Slashes`만의 문제가 아니다.

### 정상 클립의 데이터 형태 (굽기 목표 스펙)
`Swipe_1To9` 기준:
- `m_PositionCurves` 2개 + `m_RotationCurves` 2개, path는 각각 `root/add_weapon_r`, `root/add_weapon_l`
- 키 개수 **69개 / 0 ~ 2.2667초** → **30fps 균등 샘플링**
- path가 `root/add_weapon_x`이므로 값은 **부모(`root`) 기준 로컬 TRS**다. 스케일 커브는 없다(스케일 불변).

굽기 결과물은 이 형태와 동일해야 한다.

---

## 접근 방법 비교

**A. 커브를 계산해서 굽는다 (채택)**
에디터에서 클립을 프레임 단위로 샘플링해 `hand_r`(칼) / `pelvis`(검집)의 트랜스폼을 읽고, 고정 오프셋을 곱해 `add_weapon_*`의 root 기준 로컬 TRS를 계산해 커브로 기록한다.
- 정상 클립들과 **같은 형태의 데이터**가 되어 런타임 부담이 0이고, 재생 경로에 분기가 생기지 않는다.
- 결함이 있는 나머지 두 클립에도 그대로 재사용할 수 있다.

**B. 런타임 부착 (기각)**
스크립트나 Parent Constraint로 매 프레임 `add_weapon_r`을 `hand_r`에 맞춘다.
- 무기 커브를 **이미 가진 클립들과 충돌**한다. "이 클립일 때만 켠다"는 분기가 생기고, 클립이 늘어날수록 관리 지점이 된다.

**C. 계층 재구성 (기각)**
`add_weapon_r`을 `hand_r`의 자식으로 옮긴다.
- 기존 정상 클립들이 **root 기준 좌표**로 키를 갖고 있어 이중 변환이 되어 전부 깨진다.

---

## 핵심 전제 / 검증 필요

1. **그립 오프셋이 상수인가.** A의 성립 조건은 "손이 칼자루를 강체로 쥐고 있다" = `hand_r⁻¹ · add_weapon_r`이 클립 내내 일정하다는 것이다. 정상 클립(`Swipe_1To9` 등)에서 프레임별로 역산해 실제로 상수인지 **먼저 확인해야 한다.** 흔들린다면(칼을 고쳐 쥐거나 손을 바꾸는 연출이 있다면) 단일 오프셋 굽기는 성립하지 않는다.
2. **검집의 기준 본.** 검집이 `pelvis` 기준으로 고정인지, `spine_01` 등 다른 본 기준인지 같은 방식으로 역산해 확인한다.
3. **`Flurry_Slashes`가 양손 동작인지.** 칼을 왼손으로 옮겨 쥐는 구간이 있으면 오른손 고정 굽기로는 어색해진다. 클립을 실제로 재생해 확인이 필요하다.
4. **씬의 `Weapon` 오브젝트가 비활성이다.** 에디트 모드 스냅샷 기준 `School_Katana_FullBody-Magica cloth2/Weapon`이 `active: false`였다. 이 상태면 칼도 검집도 렌더되지 않는다. 사용자가 본 화면과 다를 수 있으므로 **현재 상태를 확인해야 한다.** 굽기와는 별개 문제지만 검증 시 혼선을 준다.

## 제약
- 휴머노이드 머슬 커브(`m_FloatCurves`)는 건드리지 않는다. 무기 본은 휴머노이드 아바타에 포함되지 않는 여분 본이라 머슬로는 제어할 수 없고, 오직 트랜스폼 커브로만 제어된다.
- `.anim`은 원본 에셋이다. 굽기는 **되돌릴 수 있어야** 한다(백업 또는 커브 제거 기능).
