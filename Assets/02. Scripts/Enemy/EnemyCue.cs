using System;
using UnityEngine;

namespace EnemySpace
{
    /// <summary>
    /// 이 패턴에서 <b>누가 휘두르는가</b>. 이름 하나에서 세 갈래가 전부 따라 나온다 —
    /// 적 클립 재생 여부 / 플레이어 클립 선택 / 실패 시 플레이어가 맞는지.
    /// </summary>
    public enum Attacker
    {
        /// <summary>적이 공격해 오고 플레이어는 <b>패링</b>한다. 실패하면 플레이어가 맞는다.</summary>
        Enemy,

        /// <summary>적이 무방비고 플레이어가 <b>벤다</b>. 실패해도 플레이어는 안 맞는다(적이 회피).</summary>
        Player
    }

    /// <summary>
    /// 채보 엔트리 하나에 실리는 전투 지시. <b>"몇 번 적"은 지정하지 않는다</b> —
    /// 교전 상대는 <c>EnemyDirector</c>가 링 로스터에서 정하고, <see cref="killOnSuccess"/>가
    /// "이 공격을 성공하면 다음 적으로 넘어가라"는 신호다.
    ///
    /// <para><b>누가 휘두르는가(<c>Attacker</c>)와 임팩트 보정은 여기 없다 — 패턴이 정한다.</b>
    /// 둘 다 모션의 성질이라(획 모양 → 스윙 → 역할·타이밍) 채보에 두면 같은 패턴이
    /// 엔트리마다 다른 값을 갖게 되어 어긋난 조합이 조용히 만들어진다.
    /// 여기 남은 둘은 반대로 <b>진짜 채보 순간의 성질</b>이다 — 같은 패턴이라도
    /// 이 엔트리에서만 죽이고, 이 엔트리에서만 투사체가 날아온다.</para>
    /// </summary>
    [Serializable]
    public class EnemyCue
    {
        [Tooltip("이 공격을 성공하면 현재 상대를 처치하고 다음 적으로 넘어간다. 실패하면 안 죽고 교전이 이어진다.")]
        public bool killOnSuccess = true;

        [Tooltip("이 패턴에서 날아오는 원거리 오브젝트. 비우면 없음.")]
        public SliceSpace.SliceSet projectile;
    }
}
