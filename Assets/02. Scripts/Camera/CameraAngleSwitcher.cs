using System;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 앵글 vcam 여러 대를 <b>쿨다운 → 패턴 경계(예약) → 카메라 큐 종료(발사)</b> 순서로 랜덤 교체한다.
/// <see cref="CameraDirector"/>가 소유하는 헬퍼로, 우선순위만 갈아끼우고 실제 이동은 Cinemachine 블렌드가 한다.
///
/// <para><b>이 게임이 카메라를 마음껏 바꿔도 되는 이유</b>: 패턴인풋·포커스 링·가이드라인이 전부
/// <c>ScreenSpaceOverlay</c> Canvas라 카메라와 완전히 독립이다. 앵글이 바뀌어도
/// <b>플레이어가 봐야 할 것은 1픽셀도 움직이지 않는다.</b></para>
///
/// <para><b>⚠ 세 단계를 분리하는 이유가 이 클래스의 핵심이다.</b>
/// 패턴 승계(<c>OnJudgeTargetBegan</c>)는 임팩트보다 <b>먼저</b> 온다 — 완료는 마지막 노드 입력 순간이고
/// 임팩트는 거기서 <c>goodWindow</c>(0.1초)만큼 뒤이기 때문이다. 그래서 경계에서 곧바로 교체하면
/// 블렌드가 임팩트·히트스톱·쉐이크 구간을 <b>매번</b> 덮는다(드문 일이 아니라 구조적으로 고정된 거리다).
/// 예약만 걸어 두고 <b>큐가 끝난 뒤 발사</b>하면 블렌드가 조용한 구간에서 돌고,
/// 예약이 그 패턴에 묶여 있으므로 음악적 착지점도 유지된다.</para>
///
/// <para><b>앵글은 <c>FollowOffset</c> 벡터 하나로 정의된다.</b> vcam들이 <c>BindingMode = LockToTarget</c>이고
/// 타깃 그룹이 <c>RotationMode = Manual</c>이라 오프셋이 그룹 로컬 축으로 해석되며,
/// <c>CameraDirector</c>가 그 그룹을 플레이어 yaw로 몰고 있다 → 오프셋만 바꿔도 자동으로 플레이어 기준 앵글이다.
/// 이 클래스는 그래서 앵글이 무엇인지 <b>전혀 모른다</b>.</para>
/// </summary>
[Serializable]
public class CameraAngleSwitcher
{
    [Tooltip("앵글 교체 On/Off. 끄면 예약도 잡지 않고 시작 카메라만 세운다.")]
    [SerializeField] private bool switchEnabled = true;

    [Tooltip("교체 후보 vcam들. 0번이 곡 시작 카메라다(랜덤이 아니다).\n" +
             "2대 미만이면 교체 기능만 조용히 비활성된다.")]
    [SerializeField] private CinemachineCamera[] cameras;

    [Tooltip("선택된 vcam의 우선순위. 인트로 우선순위(20)보다 낮아야 인트로가 이긴다.")]
    [SerializeField] private int activePriority = 10;

    [Tooltip("나머지 vcam의 우선순위.")]
    [SerializeField] private int restingPriority = 0;

    [Tooltip("교체 사이의 최소 간격(초). 만료 후 처음 오는 패턴 경계에서 예약되고, 그 패턴의 카메라 큐가 끝나면 발사된다.")]
    [SerializeField] private float switchInterval = 6f;

    private int currentIndex = -1;
    private float lastSwitchTime;
    private bool armed;

    /// <summary>지금 선택된 앵글(디버그·검증용).</summary>
    public int CurrentIndex => currentIndex;

    private bool IsUsable => switchEnabled && cameras != null && cameras.Length >= 2;

    /// <summary>
    /// <b>0번을 활성으로</b> 세우고 나머지를 휴지로 내린다. 랜덤이 아니다 —
    /// 곡 시작 구도가 매번 같아야 인트로 스플라인의 끝점이 어느 구도로 흡수될지 정해지고,
    /// 리스트 순서가 곧 저작 의도("0번이 기준 앵글")가 된다. 씬에 저장된 우선순위가 무엇이든 여기서 덮는다.
    /// </summary>
    public void Setup()
    {
        if (cameras == null || cameras.Length == 0) return;

        Select(0);
        lastSwitchTime = Time.time; // 곡 시작 직후 곧바로 바뀌지 않게 쿨다운을 여기서 시작한다.
        armed = false;
    }

    /// <summary>
    /// 패턴이 넘어가는 순간. 쿨다운이 찼으면 <b>예약만</b> 걸고 교체하지 않는다(위 ⚠ 참조).
    /// </summary>
    public void OnPatternBoundary()
    {
        if (!IsUsable) return;
        if (Time.time < lastSwitchTime + switchInterval) return;

        armed = true;
    }

    /// <summary>
    /// 예약이 걸려 있고 <paramref name="cuePlaying"/>이 false면 실제로 교체한다.
    ///
    /// <para>호출자(<c>CameraDirector.Update</c>)는 히트스톱 잠금 중 조기 반환하므로 이 메서드도 그동안 서고,
    /// 교체가 잠금 뒤로 밀린다 — 의도된 동작이다.</para>
    /// </summary>
    public void Tick(bool cuePlaying)
    {
        if (!armed || cuePlaying) return;
        if (!IsUsable) { armed = false; return; }

        armed = false;
        lastSwitchTime = Time.time;
        Select(PickOther());
    }

    /// <summary>곡 중단 등으로 정리. 카메라는 그대로 두고 예약만 접는다(화면이 튀지 않게).</summary>
    public void Reset()
    {
        armed = false;
        lastSwitchTime = Time.time;
    }

    /// <summary>현재 것을 뺀 균등 랜덤. 같은 걸 다시 뽑으면 교체가 무연출로 낭비된다.</summary>
    private int PickOther()
    {
        int n = cameras.Length;
        int offset = UnityEngine.Random.Range(1, n); // 1..n-1 → 현재 인덱스는 절대 안 나온다
        return (currentIndex + offset) % n;
    }

    private void Select(int index)
    {
        currentIndex = index;

        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null) continue;
            cameras[i].Priority = new PrioritySettings
            {
                Enabled = true,
                Value = i == index ? activePriority : restingPriority,
            };
        }
    }
}
