using System;
using System.Collections.Generic;
using UnityEngine;

namespace PatternSpace
{
    public enum NodeType
    {
        Start,
        Progress,
        End
    }

    [Serializable]
    public class PatternData
    {
        [Range(0, 8)] public int index;
    }

    /// <summary>
    /// 패턴의 '모양 원본' 에셋. 진행 상태(입력 시각, 현재 위치 등)는 갖지 않는다 —
    /// 그것은 <see cref="ActivePattern"/>이 들고 있으며, 그래야 같은 템플릿을 쓰는 두 패턴이
    /// 동시에 살아 있어도 서로의 상태를 덮어쓰지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "Pattern", menuName = "Scriptable Objects/Pattern")]
    public class Pattern : ScriptableObject
    {
        [SerializeField] private PatternData[] patternDatas;

        // 이 필드는 '모양에 종속된 정적 데이터'다(진행 상태가 아니라). 그래서 이 에셋에 두어도
        // "Pattern은 모양 원본일 뿐 진행 상태를 갖지 않는다"는 원칙과 충돌하지 않는다.
        [Tooltip("패턴을 전 노드 Good/Perfect로 완주했을 때 캐릭터가 재생할 애니메이션 클립. 비우면 무연출.")]
        [SerializeField] private AnimationClip successAnimationClip;

        [Tooltip("성공 애니메이션의 시작 오프셋(초). 선딜레이 제거용.")]
        [SerializeField] private float animationStartOffset = 0f;

        [Tooltip("성공 애니메이션의 재생 지속 시간(초). 후딜레이 제거용. 0 이하면 클립 끝까지 재생.")]
        [SerializeField] private float animationDuration = 0f;

        [Tooltip("칼날이 표적을 지나가는 프레임의 클립 절대 시각(초). 이 프레임이 표적 절단 시각에 오도록 정렬된다. " +
                 "0 이하거나 트림 범위 밖이면 트림 끝으로 간주한다. Tools/Animation Clip Trimmer로 찍는다.")]
        [SerializeField] private float animationImpactTime = 0f;

        [Tooltip("이 패턴 베기의 기본 배속(하한). 패턴 입력 구간이 짧으면 자동으로 더 배속된다.")]
        [SerializeField] private float animationSpeed = 1f;

        [Header("Combat Clips (EnemyCombat)")]
        [Tooltip("이 패턴에서 누가 휘두르는가. Enemy = 적 공격을 패링 / Player = 무방비 적을 공격.\n" +
                 "이 값이 아래 클립 슬롯의 표시 여부를 정한다. 한 패턴은 한 역할만 갖는다 — " +
                 "양쪽이 필요하면 패턴 에셋을 복제하라.")]
        [SerializeField] private EnemySpace.Attacker attacker = EnemySpace.Attacker.Player;

        [Tooltip("적이 이 패턴으로 휘두르는 공격. Attacker.Enemy(패링)일 때만 재생된다. 비우면 무연출.")]
        [SerializeField] private ClipAlignment enemyAttack = new ClipAlignment();

        [Tooltip("적 공격을 받아치는 플레이어 패링. Attacker.Enemy일 때 재생된다. 비우면 무연출.")]
        [SerializeField] private ClipAlignment playerParry = new ClipAlignment();

        [Tooltip("무방비 적을 베는 플레이어 공격. Attacker.Player일 때 재생된다. 비우면 무연출.")]
        [SerializeField] private ClipAlignment playerAttack = new ClipAlignment();

        [Tooltip("적이 죽는 클립. 임팩트 프레임이 플레이어 공격 임팩트와 같은 시각에 오도록 배속을 역산해 재생한다.\n" +
                 "절단(시체 교체·폭발)은 이 클립의 트림 끝에 일어난다. 비우면 임팩트에 바로 갈라진다.\n" +
                 "⚠ ImpactTime은 트림 시작 근처에 찍을 것 — 배속이 클립 전체에 걸려 쓰러지는 속도까지 빨라진다.")]
        [SerializeField] private ClipAlignment enemyDeath = new ClipAlignment();

        [Tooltip("이 패턴에서 등장할 베이는 표적. 비우면 표적 없음.")]
        [SerializeField] private SliceSpace.SliceSet sliceTarget;

        [Tooltip("임팩트 지점 기준 XY 배치. 칼 궤적 밖으로 벌리지 않는다.")]
        [SerializeField] private Vector2 sliceTargetOffset;

        [Tooltip("판정 종료 시각(Deadline) 대비 ±초. 칼이 닿는 순간을 앞뒤로 민다.\n" +
                 "플레이어 칼 · 적 칼 · 시체 교체 · 투사체 · 카메라 큐가 전부 이 값 하나를 읽는다.")]
        [UnityEngine.Serialization.FormerlySerializedAs("sliceTargetImpactOffset")]
        [SerializeField] private float impactOffset;

        [Tooltip("결투 앵커 기준 ±m. 이 모션의 리치에 맞춘다. 0이면 씬 앵커 그대로.\n" +
                 "런타임 배치 · 합주 프리뷰 · 슬라이서 칼 평면 유도가 전부 이 값을 읽는다.")]
        [SerializeField] private float duelDistanceOffset;

        public IReadOnlyList<PatternData> AllData => patternDatas;

        /// <summary>
        /// 이 패턴에서 누가 휘두르는가. <b>패턴의 성질이지 채보 순간의 성질이 아니다</b> —
        /// 획 모양이 스윙 모션을 정하고, 그 모션이 공격으로 읽히는지 패링으로 읽히는지를 정한다.
        /// 한 패턴은 한 역할만 가지며, 양쪽이 필요하면 에셋을 복제한다.
        /// </summary>
        public EnemySpace.Attacker Attacker => attacker;

        /// <summary>적이 이 패턴으로 휘두르는 공격(<c>Attacker.Enemy</c>일 때만).</summary>
        public ClipAlignment EnemyAttack => enemyAttack;

        /// <summary>적 공격을 받아치는 플레이어 패링(<c>Attacker.Enemy</c>).</summary>
        public ClipAlignment PlayerParry => playerParry;

        /// <summary>무방비 적을 베는 플레이어 공격(<c>Attacker.Player</c>).</summary>
        public ClipAlignment PlayerAttack => playerAttack;

        /// <summary>
        /// 적이 죽는 클립. <b>런타임에 재생된다</b> — 임팩트 프레임을 <c>Deadline + ImpactOffset</c>에 맞추고
        /// <b>트림 끝에서 절단</b>이 일어난다. 굽기 툴은 그 트림 끝 포즈로 절단 프록시를 굽는다.
        /// </summary>
        public ClipAlignment EnemyDeath => enemyDeath;

        /// <summary>이 패턴이 띄울 표적. <see cref="SuccessAnimationClip"/>과 같은 '모양에 종속된 정적 데이터'다.</summary>
        public SliceSpace.SliceSet SliceTarget => sliceTarget;

        /// <summary>표적의 임팩트 지점 기준 XY 배치. 스폰·임팩트 양쪽에 똑같이 실린다.</summary>
        public Vector2 SliceTargetOffset => sliceTargetOffset;

        /// <summary>
        /// 임팩트 시각을 Deadline 기준으로 미는 값(초). <b>모든 임팩트의 공통 앵커 보정이다</b> —
        /// 플레이어 칼·적 칼·시체 교체·투사체·카메라 큐가 전부 이것 하나를 읽으므로,
        /// 여기 값을 바꾸면 그 다섯이 <b>같이</b> 움직인다(어긋날 수가 없다).
        /// </summary>
        public float ImpactOffset => impactOffset;

        /// <summary>
        /// 결투 앵커 기준 거리 보정(m). <b>절대 거리가 아니라 보정값이다</b> —
        /// 씬 앵커는 카메라 구도가 정하고, 여기서는 이 모션의 리치만 조정한다.
        /// 그래서 씬 튜닝과 패턴 저작이 서로를 깨뜨리지 않는다.
        /// </summary>
        public float DuelDistanceOffset => duelDistanceOffset;

        public AnimationClip SuccessAnimationClip => successAnimationClip;

        public float AnimationStartOffset => animationStartOffset;

        public float AnimationDuration => animationDuration;

        /// <summary>
        /// 칼날이 표적을 지나가는 프레임의 클립 절대 시각(초). 0 이하 또는 트림 범위 밖이면 소비자가 트림 끝으로 폴백한다
        /// (<see cref="CharacterActionPlayer"/>). 폴백은 오서링되지 않은 기존 패턴을 위한 것이다.
        /// </summary>
        public float AnimationImpactTime => animationImpactTime;

        public float AnimationSpeed => Mathf.Max(animationSpeed, 0.01f);

        /// <summary>트림 길이(초). <see cref="animationDuration"/>이 0 이하면 클립 끝까지로 본다. 클립이 없으면 0.</summary>
        public float ResolvedAnimationDuration
        {
            get
            {
                if (animationDuration > 0f) return animationDuration;
                if (successAnimationClip == null) return 0f;
                return Mathf.Max(successAnimationClip.length - animationStartOffset, 0f);
            }
        }

        public NodeType GetNodeType(int position)
        {
            if (position == 0) return NodeType.Start;
            if (position == patternDatas.Length - 1) return NodeType.End;
            return NodeType.Progress;
        }

        private void OnValidate()
        {
            ValidateImpactTime();
#if UNITY_EDITOR
            enemyAttack?.ValidateImpactTime(this, "EnemyAttack");
            playerParry?.ValidateImpactTime(this, "PlayerParry");
            playerAttack?.ValidateImpactTime(this, "PlayerAttack");
            // enemyDeath는 트림 구간 검증만 한다. 선딜 제약은 없다 —
            // 굽기 포즈가 런타임 정합성 요구가 아니라 저작 보조이기 때문(Plan_HumanoidSlice 결정 1-1).
            enemyDeath?.ValidateImpactTime(this, "EnemyDeath");
            WarnUnusedSlots();
#endif

            if (patternDatas == null) return;

            var seen = new HashSet<int>();
            foreach (var data in patternDatas)
            {
                if (!seen.Add(data.index))
                {
                    Debug.LogError($"[Pattern] '{name}'에 중복된 인덱스 {data.index}가 있습니다.", this);
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 이 패턴의 역할에서 <b>절대 재생되지 않을 슬롯</b>에 클립이 배선돼 있으면 경고한다.
        /// 인스펙터가 그 슬롯을 숨기므로, 이관 후 남은 찌꺼기를 찾을 유일한 장치다.
        /// </summary>
        private void WarnUnusedSlots()
        {
            bool enemyIsAttacker = attacker == EnemySpace.Attacker.Enemy;

            Warn(enemyIsAttacker ? playerAttack : enemyAttack, enemyIsAttacker ? "PlayerAttack" : "EnemyAttack");
            Warn(enemyIsAttacker ? enemyDeath : playerParry, enemyIsAttacker ? "EnemyDeath" : "PlayerParry");

            void Warn(ClipAlignment slot, string label)
            {
                if (slot?.Clip == null) return;

                Debug.LogWarning(
                    $"[Pattern] '{name}'의 attacker가 {attacker}인데 {label}에 클립 '{slot.Clip.name}'이 배선돼 있습니다. " +
                    "이 슬롯은 재생되지 않습니다 — 이관 찌꺼기라면 비우세요.", this);
            }
        }
#endif

        /// <summary>
        /// 임팩트 프레임이 트림 구간 안에 있는지 확인한다. 미지정(0 이하)은 정상 — 트림 끝으로 폴백하기 때문에
        /// 오서링되지 않은 기존 패턴이 경고를 뿜지 않는다.
        /// </summary>
        private void ValidateImpactTime()
        {
            if (animationImpactTime <= 0f) return;

            float duration = ResolvedAnimationDuration;
            if (duration <= 0f) return; // 클립 미지정 — 검증할 구간 자체가 없다.

            float trimEnd = animationStartOffset + duration;
            if (animationImpactTime < animationStartOffset || animationImpactTime > trimEnd)
            {
                Debug.LogWarning(
                    $"[Pattern] '{name}'의 AnimationImpactTime({animationImpactTime:0.000}s)이 " +
                    $"트림 구간 [{animationStartOffset:0.000}s, {trimEnd:0.000}s] 밖입니다. 트림 끝으로 폴백합니다.", this);
            }
        }
    }
}
