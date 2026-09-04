using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 무대 <b>밖</b>에서의 3인칭 이동. 심상세계를 걸어 다니다 무대에 들어서면 곡이 시작되는
/// 1자 진행(CLAUDE.md §9)의 이동 절반을 담당한다.
///
/// <para><b>⚠ <see cref="PlayerCombatMover"/>와 배타적으로만 켜진다</b>(§11-2). 플레이어 위치의 주인이
/// 둘이 되면 결투 도착 시각 계약(§6 임팩트 정렬)과 거리 커브(§11-9)가 통째로 깨진다.
/// 그 배타성은 <see cref="PlayerModeDirector"/>가 <c>enabled</c>로 표현한다 —
/// <c>Update</c>가 안 도는 컴포넌트는 <c>transform.position</c>에 손댈 수가 없다.</para>
///
/// <para><b>물리를 쓰지 않는다.</b> 전투 이동과 같은 규율이다(닫힌 식 · 직접 대입).
/// 무대 원 안은 평면이고 콜라이더를 두지 않는다(§11-2 · §13).</para>
/// </summary>
// ponytail: transform.position 직접 대입. 탐색 영역에 벽·프롭 콜라이더가 필요해지면
// CharacterController.SimpleMove로 올린다(중력·충돌이 공짜로 따라온다). 지금은 그런 씬이 없다.
public class PlayerExploreMover : MonoBehaviour
{
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("탐색 로코모션을 걸 상대. 비우면 이동만 하고 애니메이션은 Idle에 남는다.")]
    [SerializeField] private CharacterActionPlayer actionPlayer;

    [Tooltip("이동 방향의 기준. 카메라 yaw로만 쓴다 — 비우면 월드 축 기준이 된다.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4f;

    [Tooltip("LShift를 누르고 있을 때의 이동 속도 배수. 1이면 클립만 달리기로 바뀌고 속도는 그대로다.")]
    [SerializeField] private float sprintSpeedMultiplier = 1.75f;

    [Tooltip("가는 쪽으로 도는 시간(초). 전투의 turnDuration과 별개다 — 저쪽은 상대를 보는 회전이고 이쪽은 진행 방향이다.")]
    [SerializeField] private float turnDuration = 0.12f;

    [Header("Look")]
    [Tooltip("마우스 시점으로 돌릴 탐색 vcam의 궤도. 비우면 시점 회전이 통째로 비활성된다.")]
    [SerializeField] private CinemachineOrbitalFollow orbit;

    [Tooltip("마우스 픽셀당 회전 각도. 마우스 델타는 이미 프레임 단위 이동량이라 deltaTime을 곱하지 않는다.")]
    [SerializeField] private float lookSensitivity = 0.15f;

    [SerializeField] private bool invertY = false;

    void Update()
    {
        if (inputHandler == null) return;

        // 시점이 먼저다 - 같은 프레임에 이동이 새 yaw를 따라간다.
        TickLook();

        Vector3 direction = ResolveDirection(inputHandler.MoveInput);
        bool sprinting = inputHandler.SprintHeld;
        float speed = direction.sqrMagnitude > 1e-6f
            ? moveSpeed * (sprinting ? Mathf.Max(sprintSpeedMultiplier, 0.01f) : 1f)
            : 0f;

        if (speed > 0f)
        {
            transform.position += direction * (speed * Time.deltaTime);

            // 회전은 진행 방향으로. 도는 동안에도 이동은 계속되므로 두 주인이 다투지 않는다
            // (전투와 달리 여기서는 위치와 회전이 같은 입력에서 나온다).
            Quaternion target = Quaternion.LookRotation(direction);
            float t = Mathf.Clamp01(Time.deltaTime / Mathf.Max(turnDuration, 0.01f));
            transform.rotation = Quaternion.Slerp(transform.rotation, target, t);
        }

        // 애니메이터는 CharacterActionPlayer만 안다 — 스테이트 이름을 여기서 복제하지 않는다.
        if (actionPlayer != null) actionPlayer.SetExploreLocomotion(speed, sprinting);
    }

    /// <summary>
    /// 마우스 입력으로 궤도를 돌린다. <b>모드 가드가 없다</b> — 이 컴포넌트는 탐색에서만
    /// <c>enabled</c>이고(<see cref="PlayerModeDirector"/>), 그것도 모자라 Explore 맵이 꺼지면
    /// <c>LookInput</c>이 0이다(이중 안전).
    ///
    /// <para>범위·랩어라운드는 <c>InputAxis.Validate</c>가 든다 — 축의 성질이라 그 값을 여기서
    /// 다시 클램프하면 진실의 원천이 둘이 된다.</para>
    /// </summary>
    // ponytail: 마우스(<Mouse>/delta) 기준 감도. 스틱은 축 값이라 deltaTime을 곱해야 맞는데,
    // 지금 탐색은 마우스 전용이다. 패드를 실제로 지원할 때 장치별로 가른다.
    private void TickLook()
    {
        if (orbit == null) return;

        Vector2 look = inputHandler.LookInput * lookSensitivity;
        if (look.sqrMagnitude < 1e-6f) return;

        orbit.HorizontalAxis.Value += look.x;
        orbit.VerticalAxis.Value += invertY ? look.y : -look.y;
        orbit.HorizontalAxis.Validate();
        orbit.VerticalAxis.Validate();
    }

    /// <summary>
    /// 입력을 <b>카메라 yaw 기준</b> 월드 방향으로 바꾼다. 피치·롤은 버린다 —
    /// 카메라가 아래를 보고 있다고 플레이어가 땅으로 걸어 들어가면 안 된다.
    /// </summary>
    private Vector3 ResolveDirection(Vector2 input)
    {
        if (input.sqrMagnitude < 1e-6f) return Vector3.zero;

        Vector3 raw = new Vector3(input.x, 0f, input.y);
        if (raw.sqrMagnitude > 1f) raw.Normalize(); // 대각선이 빠르지 않게(스틱은 원형이라 이미 1 이하)

        float yaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
        return Quaternion.Euler(0f, yaw, 0f) * raw;
    }
}
