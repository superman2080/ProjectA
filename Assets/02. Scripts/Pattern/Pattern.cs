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

        [Tooltip("표적이 된 순간부터 임팩트까지 적이 하는 동작(견제). Attacker.Player일 때만 재생된다. 비우면 기본 Idle.\n" +
                 "⚠ ImpactTime을 찍지 말 것 — 트림 끝이 임팩트에 붙는 것이 기본 동작이다.\n" +
                 "⚠ 트림 0.8초 이하 권장. 실측상 그 길이가 배속 없이 들어가는 비율이 97%다(docs/EnemyFeint).")]
        [SerializeField] private ClipAlignment enemyFeint = new ClipAlignment();

        [Tooltip("적이 죽는 클립. 임팩트 프레임이 플레이어 공격 임팩트와 같은 시각에 오도록 배속을 역산해 재생한다.\n" +
                 "절단(시체 교체·폭발)은 이 클립의 트림 끝에 일어난다. 비우면 임팩트에 바로 갈라진다.\n" +
                 "⚠ ImpactTime은 트림 시작 근처에 찍을 것 — 배속이 클립 전체에 걸려 쓰러지는 속도까지 빨라진다.")]
        [SerializeField] private ClipAlignment enemyDeath = new ClipAlignment();

        [Tooltip("이 패턴에 맞은 적의 리액션. 성공했으나 처치되지 않을 때(사슬 중간 타격) 재생된다. Attacker.Player 전용.\n" +
                 "비우면 EnemyView의 knockBack 스테이트로 폴백한다.\n" +
                 "⚠ 트림 0.5초 이하 권장 — 임팩트에 정렬되므로 길면 다음 패턴의 견제 클립이 끊는다.")]
        [SerializeField] private ClipAlignment enemyHit = new ClipAlignment();

        [Tooltip("이 패턴을 막아낸 적의 리액션(패링). 실패하고 물러나지 않을 때 재생된다.\n" +
                 "비우면 EnemyView의 parry 스테이트로 폴백한다. 회피(물러남)는 이 슬롯을 쓰지 않는다.")]
        [SerializeField] private ClipAlignment enemyParry = new ClipAlignment();

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

        [Header("World Effects")]
        [Tooltip("이 패턴이 재생할 월드 이펙트들. 큐 하나가 '언제·어디에·어떤 조건에서'를 스스로 든다.\n" +
                 "개수 제한이 없으므로 칼날·플레이어·적·임팩트 지점에 각각 붙일 수 있다.\n" +
                 "저장은 Tools/Pattern Effect Tool로 한다.")]
        [SerializeField] private List<PatternEffectCue> effectCues = new List<PatternEffectCue>();

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
        /// 표적이 된 순간부터 임팩트까지 적이 하는 동작(<c>Attacker.Player</c>일 때만).
        ///
        /// <para><b>임팩트가 없는 슬롯이다.</b> 닿지 않는 동작이므로 <c>ImpactTime</c>을 찍지 않고,
        /// 그러면 <see cref="ClipAlignment"/>가 트림 끝을 임팩트로 폴백해 <b>클립 끝이 임팩트 시각에 붙는다</b>.</para>
        ///
        /// <para>비어 있으면 적은 기본 Idle로 서 있는다(예전 동작). 이 슬롯은 그 정지 구간을 메우기 위한 것이다.</para>
        /// </summary>
        public ClipAlignment EnemyFeint => enemyFeint;

        /// <summary>
        /// 적이 죽는 클립. <b>런타임에 재생된다</b> — 임팩트 프레임을 <c>Deadline + ImpactOffset</c>에 맞추고
        /// <b>트림 끝에서 절단</b>이 일어난다. 굽기 툴은 그 트림 끝 포즈로 절단 프록시를 굽는다.
        /// </summary>
        public ClipAlignment EnemyDeath => enemyDeath;

        /// <summary>
        /// 이 패턴에 <b>맞았는데 죽지 않은</b> 적의 리액션(사슬 중간 타격). <c>Attacker.Player</c> 전용.
        ///
        /// <para><b>패턴이 소유하는 이유는 공격 클립과 같다</b> — 획 모양이 스윙을 정하고, 그 스윙이
        /// 어느 방향으로 젖혀지는지를 정한다. 고정 스테이트 이름이면 가로베기든 내려베기든 같은 모션이 나온다.</para>
        ///
        /// <para>비어 있으면 <c>EnemyView</c>의 <c>knockBackStateName</c>으로 폴백한다(기존 동작).</para>
        /// </summary>
        public ClipAlignment EnemyHit => enemyHit;

        /// <summary>
        /// 이 패턴을 <b>막아낸</b> 적의 리액션(제자리 패링). 물러나는 회피는 이 슬롯을 쓰지 않는다 —
        /// 회피는 클립·후퇴 이동·무대 경계 클램프가 한 덩어리라 <see cref="ClipAlignment"/> 하나로 끝나지 않는다.
        ///
        /// <para>비어 있으면 <c>EnemyView</c>의 <c>parryStateName</c>으로 폴백한다(기존 동작).</para>
        /// </summary>
        public ClipAlignment EnemyParry => enemyParry;

        /// <summary>이 패턴이 띄울 표적. <see cref="PlayerAttack"/>과 같은 '모양에 종속된 정적 데이터'다.</summary>
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

        /// <summary>
        /// 이 패턴이 재생할 월드 이펙트 큐들. <b>슬롯이 아니라 리스트인 이유</b>는
        /// 개수와 시점이 코드가 아니라 저장 단계에서 정해지기 때문이다(<see cref="PatternEffectCue"/>).
        /// </summary>
        public IReadOnlyList<PatternEffectCue> EffectCues => effectCues;

        public NodeType GetNodeType(int position)
        {
            if (position == 0) return NodeType.Start;
            if (position == patternDatas.Length - 1) return NodeType.End;
            return NodeType.Progress;
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            enemyAttack?.ValidateImpactTime(this, "EnemyAttack");
            playerParry?.ValidateImpactTime(this, "PlayerParry");
            playerAttack?.ValidateImpactTime(this, "PlayerAttack");
            enemyHit?.ValidateImpactTime(this, "EnemyHit");
            enemyParry?.ValidateImpactTime(this, "EnemyParry");
            // enemyDeath는 트림 구간 검증만 한다. 선딜 제약은 없다 —
            // 굽기 포즈가 런타임 정합성 요구가 아니라 저작 보조이기 때문(Plan_HumanoidSlice 결정 1-1).
            enemyDeath?.ValidateImpactTime(this, "EnemyDeath");
            enemyFeint?.ValidateImpactTime(this, "EnemyFeint");
            WarnUnusedSlots();
            ValidateEffectCues();
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

            // 견제는 Attacker.Player 전용이다 — 적이 공격자면 그 구간을 EnemyAttack이 이미 채운다.
            if (enemyIsAttacker) Warn(enemyFeint, "EnemyFeint");

            // 피격 리액션도 Attacker.Player 전용이다 — 적이 공격자인 패턴의 실패는 §11-2대로 언제나 물러나고,
            // 성공은 패링이라 밀려나는 쪽(knockBack)이 이미 자리를 잡고 있다.
            if (enemyIsAttacker) Warn(enemyHit, "EnemyHit");

            void Warn(ClipAlignment slot, string label)
            {
                if (slot?.Clip == null) return;

                Debug.LogWarning(
                    $"[Pattern] '{name}'의 attacker가 {attacker}인데 {label}에 클립 '{slot.Clip.name}'이 배선돼 있습니다. " +
                    "이 슬롯은 재생되지 않습니다 — 이관 찌꺼기라면 비우세요.", this);
            }
        }

        /// <summary>
        /// 이펙트 큐의 <b>배선 실수를 잡는 유일한 장치</b>. 셋을 본다 —
        /// 파티클이 없는 프리팹, 노드 범위를 벗어난 인덱스, 그리고 <b>재생될 수 없는 조건</b>이다.
        ///
        /// <para>결과 조건 큐가 <c>LastNode</c>보다 이른 시각에 걸리는 것은 런타임에 조용히 폐기되므로
        /// 여기서 잡지 않으면 "왜 안 뜨지"가 된다.</para>
        /// </summary>
        private void ValidateEffectCues()
        {
            if (effectCues == null) return;

            int nodeCount = patternDatas != null ? patternDatas.Length : 0;

            for (int i = 0; i < effectCues.Count; i++)
            {
                var cue = effectCues[i];
                if (cue == null || !cue.IsUsable) continue;

                if (cue.Prefab.GetComponentInChildren<ParticleSystem>(true) == null)
                {
                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 이펙트 큐 {i}('{cue.Label}') 프리팹에 ParticleSystem이 없습니다. " +
                        "아무것도 보이지 않습니다.", this);
                }

                if (cue.Timing == EffectTiming.Node && nodeCount > 0 && cue.NodeIndex >= nodeCount)
                {
                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 이펙트 큐 {i}('{cue.Label}') 노드 인덱스가 {cue.NodeIndex}인데 " +
                        $"이 패턴의 노드는 {nodeCount}개입니다. 마지막 노드로 클램프됩니다.", this);
                }

                // 절대 시각이 아니라 기준점들의 '순서'만 보면 되므로 간격을 벌린 가짜 시각으로 판단한다.
                // (start 0 < first 1 < last 2 < impact 2.5) — 실제 채보에서도 이 순서는 불변이다.
                const float fakeStart = 0f, fakeFirst = 1f, fakeLast = 2f, fakeDeadline = 2.5f;
                float fired = cue.ResolveTime(fakeStart, fakeFirst, fakeLast, fakeDeadline, null, 0f);

                if (cue.NeedsOutcome && !cue.IsTimingValid(fired, fakeLast))
                {
                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 이펙트 큐 {i}('{cue.Label}')는 조건이 {cue.Condition}인데 " +
                        "성패가 정해지는 LastNode보다 이른 시각에 걸려 있습니다. 재생되지 않습니다.", this);
                }

                if (attacker == EnemySpace.Attacker.Enemy && cue.Condition == EffectCondition.Parry)
                {
                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 attacker가 Enemy인데 이펙트 큐 {i}('{cue.Label}')의 조건이 Parry입니다. " +
                        "그 역할에서는 적이 막지 않으므로 재생되지 않습니다.", this);
                }
            }
        }
#endif
    }
}
