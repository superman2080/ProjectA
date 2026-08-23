using System;
using UnityEngine;

namespace PatternSpace
{
    /// <summary>
    /// 연타 타격 하나 — <b>플레이어의 베기와 그것을 맞는 적의 리액션이 한 쌍</b>이다.
    ///
    /// <para><b>왜 두 리스트가 아니라 한 객체인가</b>: 둘은 인덱스로 짝지어진다. 평행 리스트로 두면
    /// 한쪽에만 원소를 넣는 순간 <b>짝이 통째로 밀리고</b> 에러도 경고도 없이 엉뚱한 리액션이 나온다.
    /// 한 객체면 그 상태가 <b>표현 불가능</b>해진다(<c>SliceSet</c>을 패턴이 안 드는 것과 같은 규율).</para>
    ///
    /// <para><b>⚠ 둘 다 정렬 대상이 아니다.</b> 연타 타격 시각은 플레이어가 정하므로 역산할 시각이 없다 —
    /// <see cref="ClipAlignment.ImpactTime"/>은 여기서 '칼이 닿는 시각'이 아니라 <b>재생 시작점</b>이고,
    /// 적 리액션은 맞는 순간이 곧 지금이라 예약 없이 즉시 재생된다.</para>
    ///
    /// <para><b>몇 번째 타격이 어느 쌍을 쓰는지는 <see cref="Pattern.MashStrikeFor"/>가 정한다</b> —
    /// 커서를 들지 않고 누적 타수에서 유도하므로, 이 값을 보는 셋(플레이어·적·이펙트)이
    /// 어긋날 수가 없다.</para>
    /// </summary>
    [Serializable]
    public class MashStrike
    {
        [Tooltip("플레이어가 휘두르는 클립. ImpactTime '부터' 재생된다(와인드업은 건너뛴다).")]
        [SerializeField] private ClipAlignment playerClip = new ClipAlignment();

        [Tooltip("이 타격을 맞은 적의 리액션. 비우면 Pattern.EnemyHit으로, 그것도 비면 EnemyView의 knockBack 스테이트로 폴백한다.\n" +
                 "⚠ 젖혀지는 동작이 클립 맨 앞에 와야 한다 — 타격 간격이 0.15초 수준이라 앞부분만 보인다.")]
        [SerializeField] private ClipAlignment enemyReaction = new ClipAlignment();

        /// <summary>플레이어가 휘두르는 클립.</summary>
        public ClipAlignment PlayerClip => playerClip;

        /// <summary>이 타격을 맞은 적의 리액션. 비어 있을 수 있다(패턴 공용 <c>EnemyHit</c>으로 폴백).</summary>
        public ClipAlignment EnemyReaction => enemyReaction;

        /// <summary>재생할 플레이어 클립이 있는가. 없으면 이 타격은 무연출이다.</summary>
        public bool IsUsable => playerClip != null && playerClip.IsUsable;

#if UNITY_EDITOR
        /// <summary>마이그레이션 전용 기입 경로(구 mashHitClips → mashStrikes). 런타임은 호출하지 않는다.</summary>
        public void EditorAssignPlayerClip(AnimationClip clip, float startOffset, float duration, float impactTime, float speed)
        {
            playerClip.EditorAssign(clip, startOffset, duration, impactTime, speed);
        }
#endif
    }
}
