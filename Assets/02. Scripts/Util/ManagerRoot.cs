using UnityEngine;

/// <summary>
/// <c>Managers</c> 프리팹의 루트. <b>하는 일은 중복 루트를 통째로 지우는 것 하나뿐이다.</b>
///
/// <para><b>왜 필요한가</b>: 자식 매니저들은 전부 <see cref="Singleton{T}"/>이고
/// <c>DontDestroyOnLoad(transform.root.gameObject)</c>로 <b>루트째</b> 살아남는다(<see cref="Singleton{T}.Awake"/>).
/// 그래서 다음 씬의 프리팹 인스턴스는 자식들만 각자 자기를 파괴하고 <b>빈 <c>Managers</c> 껍데기가 남는다</b> —
/// 하이어라키에 같은 이름이 둘 보이고, 그중 하나는 아무것도 안 들었다. 디버깅할 때 엉뚱한 쪽을 열게 된다.</para>
///
/// <para>루트에도 같은 규칙을 걸면 그 껍데기가 원천 소멸한다. 새 로직이 없다 —
/// <see cref="Singleton{T}"/>이 이미 "중복이면 자기 게임오브젝트를 파괴한다"를 하고 있고 여기서는 그것만 쓴다.</para>
/// </summary>
public class ManagerRoot : Singleton<ManagerRoot>
{
    protected override bool DontDestroy => true;
}
