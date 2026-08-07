using System;
using System.Collections.Generic;
using UnityEngine;

namespace PatternSpace
{
    /// <summary>이펙트가 뜰 조건. <b>판정 결과가 아니라 적이 재생하는 반응 클립</b>을 따라간다.</summary>
    public enum EffectCondition
    {
        /// <summary>성패와 무관하게 제 시각에 뜬다. 결과를 알기 전에 떠야 하는 것(기 모으기·칼날 잔광)이 여기 온다.</summary>
        Always,

        /// <summary>완주 성공. <c>Attacker.Player</c>면 베는 이펙트, <c>Attacker.Enemy</c>면 받아친 스파크다.</summary>
        Success,

        /// <summary>적이 제자리에서 막았다(<c>parryStateName</c>). 칼이 맞부딪히는 유일한 실패다.</summary>
        Parry,

        /// <summary>적이 물러났다(<c>evadeStateName</c>). <c>Attacker.Enemy</c>에서는 플레이어 피격이다.</summary>
        Evade
    }

    /// <summary>이펙트 시각의 기준점. 전부 패턴 진행상의 지점이며 <b>새 시계를 만들지 않는다</b>.</summary>
    public enum EffectTiming
    {
        /// <summary>큐 투입(= 포커스 링이 뜨기 시작하는 시각).</summary>
        PatternStart,

        /// <summary>첫 노드 입력 예정 시각.</summary>
        FirstNode,

        /// <summary><see cref="PatternEffectCue.NodeIndex"/>번째 노드 입력 예정 시각.</summary>
        Node,

        /// <summary>마지막 노드 입력 예정 시각. <b>성패가 정해지는 시각</b>이기도 하다.</summary>
        LastNode,

        /// <summary>칼이 닿는 순간(<c>Deadline + Pattern.ImpactOffset</c>). §6·§11과 같은 식.</summary>
        Impact
    }

    /// <summary>이펙트가 붙는 기준 Transform. 실제 배선은 <c>PatternEffectDirector</c>가 든다.</summary>
    public enum EffectAnchor
    {
        /// <summary>칼이 지나가는 씬 지점(<c>SliceTargetDirector</c>와 같은 자리). 씬 고정이라 상대 배정 시점 함정이 없다.</summary>
        ImpactAnchor,

        /// <summary>플레이어 루트.</summary>
        Player,

        /// <summary>칼날 노드. <see cref="PatternEffectCue.BladeT"/>로 날 위의 한 점을 집는다.</summary>
        PlayerWeapon,

        /// <summary>지금 교전 중인 상대. <b>발사 순간에</b> 조회한다(큐 시점엔 아직 배정되지 않았다).</summary>
        Opponent
    }

    /// <summary>
    /// 패턴이 소유하는 이펙트 큐 하나. "<b>언제 · 어디에 · 어떤 조건에서</b> 무엇을 재생할지"를 스스로 들고 있다.
    ///
    /// <para><b>왜 고정 슬롯이 아니라 리스트의 원소인가</b>: 한 패턴에 이펙트가 몇 개 붙을지, 어느 시점에 붙을지는
    /// 만들면서 정해진다. 고정 슬롯은 그 개수를 코드가 미리 정해 버려 "칼 뽑을 때 하나 + 칼날에 하나 +
    /// 맞부딪힐 때 하나"를 표현할 수 없다. <see cref="ClipAlignment"/> 슬롯들과는 성질이 다르다 —
    /// 클립은 배우당 한 번에 하나만 재생되지만 이펙트는 동시에 여럿 뜬다.</para>
    ///
    /// <para><b>시각 계산의 유일한 소유자는 <see cref="ResolveTime"/> 하나다.</b> 런타임 발사와 툴의 타임라인·경고가
    /// 같은 함수를 보므로 저장 화면에서 맞춘 값이 게임에서 어긋날 수 없다.</para>
    /// </summary>
    [Serializable]
    public class PatternEffectCue
    {
        [Tooltip("편집 편의 — 툴 목록에 뜨는 이름. 비면 프리팹 이름을 쓴다.")]
        [SerializeField] private string label;

        [Tooltip("재생할 월드 프리팹(ParticleSystem 보유). 비우면 무연출.")]
        [SerializeField] private GameObject prefab;

        [Header("When")]
        [Tooltip("이 이펙트가 뜰 조건. Always는 성패와 무관하게 뜬다.\n" +
                 "⚠ 결과 조건(Success/Parry/Evade)은 성패가 정해지는 LastNode보다 이른 시각에 걸 수 없다.")]
        [SerializeField] private EffectCondition condition = EffectCondition.Success;

        [Tooltip("시각의 기준점. 여기에 timeOffset을 더한 시각에 재생된다.")]
        [SerializeField] private EffectTiming timing = EffectTiming.Impact;

        [Tooltip("timing이 Node일 때 몇 번째 노드인지(0부터).")]
        [Min(0)]
        [SerializeField] private int nodeIndex;

        [Tooltip("기준점 대비 ±초. 음수면 기준점보다 먼저 뜬다.")]
        [SerializeField] private float timeOffset;

        [Header("Where")]
        [Tooltip("붙을 기준 지점. 배선이 비어 있으면 이 큐만 조용히 빠진다.")]
        [SerializeField] private EffectAnchor anchor = EffectAnchor.ImpactAnchor;

        [Tooltip("켜면 앵커의 자식으로 붙어 따라간다(칼날 잔상). 끄면 발사 순간의 포즈만 복사해 월드에 남는다(스파크).")]
        [SerializeField] private bool follow;

        [Tooltip("칼날 위의 위치(0 = 손잡이 쪽 끝, 1 = 칼끝). anchor가 PlayerWeapon일 때만 쓰인다.\n" +
                 "길이가 아니라 비율이므로 무기를 바꿔도 칼끝은 칼끝이다.")]
        [Range(0f, 1f)]
        [SerializeField] private float bladeT = 1f;

        [Tooltip("켜면 회전을 칼날 축 + 진행 방향에 맞춘다(스파크가 칼이 지나가는 쪽을 향한다).\n" +
                 "이 옵션을 쓸 때만 매 프레임 비용이 생긴다.")]
        [SerializeField] private bool alignToBlade;

        [Tooltip("앵커 로컬 기준 위치 보정.")]
        [SerializeField] private Vector3 positionOffset;

        [Tooltip("앵커 로컬 기준 회전 보정(오일러).")]
        [SerializeField] private Vector3 rotationOffset;

        [Header("Play")]
        [Tooltip("크기 배율.")]
        [Min(0.01f)]
        [SerializeField] private float scale = 1f;

        [Tooltip("파티클 재생 배속(1 = 프리팹 그대로). 배속은 지속시간도 같이 줄인다.")]
        [Min(0.01f)]
        [SerializeField] private float speed = 1f;

        [Tooltip("미리 확보할 인스턴스 수. 곡 도중 Instantiate는 히치(=판정 손실)라 프리웜이 필수다.")]
        [Min(0)]
        [SerializeField] private int poolSize = 2;

        public string Label => string.IsNullOrEmpty(label) ? (prefab != null ? prefab.name : "(비어 있음)") : label;
        public GameObject Prefab => prefab;
        public EffectCondition Condition => condition;
        public EffectTiming Timing => timing;
        public int NodeIndex => nodeIndex;
        public float TimeOffset => timeOffset;
        public EffectAnchor Anchor => anchor;
        public bool Follow => follow;
        public float BladeT => bladeT;
        public bool AlignToBlade => alignToBlade;
        public Vector3 PositionOffset => positionOffset;
        public Vector3 RotationOffset => rotationOffset;
        public float Scale => Mathf.Max(scale, 0.01f);
        public float Speed => Mathf.Max(speed, 0.01f);
        public int PoolSize => Mathf.Max(poolSize, 0);

        /// <summary>재생할 것이 있는가. 비면 예약 자체를 만들지 않는다(= "뒷구르기는 무연출"의 구현 전부).</summary>
        public bool IsUsable => prefab != null;

        /// <summary>성패가 정해져야만 뜰 수 있는 큐인가.</summary>
        public bool NeedsOutcome => condition != EffectCondition.Always;

        /// <summary><see cref="BladeT"/>가 의미를 갖는가. 다른 앵커에서는 인스펙터·툴이 이 값을 감춘다.</summary>
        public bool UsesBlade => anchor == EffectAnchor.PlayerWeapon;

        /// <summary>
        /// 이 큐가 발사될 절대 시각. <b>모든 기준점이 큐 투입 시점에 이미 알려져 있으므로</b>
        /// 디렉터는 이벤트를 더 구독하지 않고 예약 목록 하나만 돌린다.
        /// </summary>
        /// <param name="nodeTimes">노드별 입력 예정 시각(절대). 비거나 짧으면 범위 안으로 클램프한다.</param>
        /// <param name="impactOffset">패턴의 <c>ImpactOffset</c> — 다섯 소비자가 공유하는 공통 앵커 보정.</param>
        public float ResolveTime(
            float startTime, float firstNodeTime, float lastNodeTime, float deadline,
            IReadOnlyList<float> nodeTimes, float impactOffset)
        {
            float baseTime;
            switch (timing)
            {
                case EffectTiming.PatternStart: baseTime = startTime; break;
                case EffectTiming.FirstNode: baseTime = firstNodeTime; break;
                case EffectTiming.Node: baseTime = ResolveNodeTime(nodeTimes, firstNodeTime, lastNodeTime); break;
                case EffectTiming.LastNode: baseTime = lastNodeTime; break;
                default: baseTime = deadline + impactOffset; break;
            }

            return baseTime + timeOffset;
        }

        private float ResolveNodeTime(IReadOnlyList<float> nodeTimes, float firstNodeTime, float lastNodeTime)
        {
            if (nodeTimes == null || nodeTimes.Count == 0)
                return nodeIndex <= 0 ? firstNodeTime : lastNodeTime; // 노드 시각을 못 받았을 때의 폴백

            int index = Mathf.Clamp(nodeIndex, 0, nodeTimes.Count - 1);
            return nodeTimes[index];
        }

        /// <summary>
        /// 이 시각에 이 조건이 성립할 수 있는가. 성패는 마지막 노드 입력에서 정해지므로
        /// <b>결과 조건 큐가 그보다 이르면 미래를 앞당겨 보여 주는 셈</b>이라 런타임이 폐기하고 툴이 경고한다.
        /// </summary>
        public bool IsTimingValid(float resolvedTime, float lastNodeTime)
        {
            return !NeedsOutcome || resolvedTime >= lastNodeTime;
        }
    }
}
