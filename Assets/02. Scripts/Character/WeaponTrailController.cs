using UnityEngine;

/// <summary>
/// 칼날 트레일의 <b>유일한 관리 지점</b>. <see cref="CharacterActionPlayer"/>의 스윙 이벤트만 구독해
/// 베기 트림 구간 동안에만 <c>Tiny.Trail</c>을 enable한다. 애니메이션 재생기는 트레일을 위해 수정하지 않는다
/// (이벤트 2개를 발행할 뿐이다) — <c>EffectManager</c>·<c>SliceTargetDirector</c>와 같은 관례다.
///
/// <para><b>피격(Hit) 클립에는 트레일이 붙지 않는다.</b> 휘두르는 동작이 아니고, 성공/실패의 시각적 대비도 생긴다.
/// 스윙 도중 미스가 나서 피격으로 끊기는 경우에도 재생기가 종료 이벤트를 내주므로 켜진 채 남지 않는다.</para>
///
/// <para><b>켜기는 안전하지만 끄기는 즉발이다.</b> <c>Tiny.Trail.OnEnable</c>은 정점을 전부 현재 위치로 접어 넣어
/// 이전 위치에서 늘어나는 잔상이 없다. 반면 <c>OnDisable</c>은 트레일 오브젝트를 그냥 비활성화해 리본이 즉시 사라진다
/// (페이드 API가 없다). 트림 끝에는 스윙이 이미 감속해 리본이 짧아진 상태라 그대로 둔다.</para>
///
/// <para><b>배선 주의</b>: 칼날은 같은 이름의 노드가 2단으로 겹쳐 있고 <c>Trail</c>은 <b>안쪽(메쉬) 노드</b>에 있다 —
/// <c>root/add_weapon_r/Weapon_Katana_01_Blade/<b>Weapon_Katana_01_Blade</b></c>.
/// 바깥 노드를 잡으면 조용히 무연출이 되므로 <see cref="Awake"/>의 경고 로그가 유일한 방어선이다.</para>
/// </summary>
public class WeaponTrailController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("스윙 구간을 알려주는 재생기. 이 컴포넌트의 OnSwingBegan/OnSwingEnded만 구독한다.")]
    [SerializeField] private CharacterActionPlayer actionPlayer;

    [Tooltip("칼날의 Trail 컴포넌트들. 칼날 변형이 여럿이라 배열이다(비활성 변형은 자동으로 건너뛴다).")]
    [SerializeField] private Tiny.Trail[] bladeTrails;

    void Awake()
    {
        if (actionPlayer == null)
            Debug.LogError("[WeaponTrailController] actionPlayer가 배선되지 않았습니다 — 트레일이 동작하지 않습니다.", this);

        if (bladeTrails == null || bladeTrails.Length == 0)
            Debug.LogError("[WeaponTrailController] bladeTrails가 비어 있습니다 — 칼날 안쪽 노드의 Trail을 넣으세요.", this);

        // 씬에 켜진 채 저장돼 있어도 휴지 상태를 보장한다.
        SetTrailsEnabled(false);
    }

    void OnEnable()
    {
        if (actionPlayer == null) return;
        actionPlayer.OnSwingBegan += HandleSwingBegan;
        actionPlayer.OnSwingEnded += HandleSwingEnded;
    }

    void OnDisable()
    {
        if (actionPlayer == null) return;
        actionPlayer.OnSwingBegan -= HandleSwingBegan;
        actionPlayer.OnSwingEnded -= HandleSwingEnded;

        SetTrailsEnabled(false);
    }

    private void HandleSwingBegan() => SetTrailsEnabled(true);

    private void HandleSwingEnded() => SetTrailsEnabled(false);

    private void SetTrailsEnabled(bool value)
    {
        if (bladeTrails == null) return;

        foreach (var trail in bladeTrails)
        {
            if (trail == null) continue;

            // 비활성 칼날 변형은 건드리지 않는다 — 켜도 그려지지 않고, 나중에 그 변형이 활성화될 때
            // 엉뚱한 시점의 상태를 물려받는다.
            if (!trail.gameObject.activeInHierarchy) continue;

            trail.enabled = value;
        }
    }
}
