using UnityEngine;

/// <summary>
/// 칼날 위의 한 점을 <b>비율</b>로 집는다(0 = 손잡이 쪽 끝, 1 = 칼끝).
///
/// <para><b>축을 하드코딩하지 않는다.</b> <c>MeshSliceBakerWindow.SampleWeapon</c>과 같은 규칙으로 유도한다 —
/// 칼 렌더러 <c>localBounds</c>의 <b>최장 축이 날 길이</b>다. 프리팹마다 어느 로컬 축이 날인지 다르므로
/// 추측하면 조용히 틀린다(이펙트가 칼 옆구리로 튀어나간다).</para>
///
/// <para><b>계산은 생성 때 한 번뿐이다.</b> 칼날 노드의 로컬 좌표라 포즈가 바뀌어도 값이 유효하고,
/// 이후 궤적 추종은 부모 관계가 공짜로 해 준다.</para>
///
/// <para>런타임(<c>PatternEffectDirector</c>)과 저장 툴이 <b>같은 이 클래스</b>를 쓴다 —
/// 프리뷰에서 맞춘 자리가 게임에서 달라지면 툴을 믿을 수 없다.</para>
/// </summary>
public sealed class BladePath
{
    private readonly Transform weapon;
    private Vector3 localBase;   // bladeT = 0 (손잡이 쪽)
    private Vector3 localTip;    // bladeT = 1 (칼끝)
    private Vector3 localAxis = Vector3.up;

    /// <summary>칼날 노드와 렌더러를 찾았는가. 아니면 모든 질의가 안전한 기본값을 돌려준다.</summary>
    public bool IsValid { get; }

    public BladePath(Transform weapon)
    {
        this.weapon = weapon;
        if (weapon == null) return;

        var renderer = weapon.GetComponentInChildren<Renderer>();
        if (renderer == null) return;

        var bounds = renderer.localBounds;
        Vector3 ext = bounds.extents;
        Vector3 axis = ext.x >= ext.y && ext.x >= ext.z ? Vector3.right
            : ext.y >= ext.z ? Vector3.up
            : Vector3.forward;

        var rt = renderer.transform;
        Vector3 worldCenter = rt.TransformPoint(bounds.center);
        Vector3 worldHalf = rt.TransformVector(Vector3.Scale(axis, ext));

        Vector3 a = weapon.InverseTransformPoint(worldCenter - worldHalf);
        Vector3 b = weapon.InverseTransformPoint(worldCenter + worldHalf);

        // 칼끝은 손(= 칼날 노드 원점)에서 먼 쪽이다. 어느 방향으로 모델링됐는지 가정하지 않는다.
        bool bIsTip = b.sqrMagnitude >= a.sqrMagnitude;
        localBase = bIsTip ? a : b;
        localTip = bIsTip ? b : a;

        Vector3 span = localTip - localBase;
        if (span.sqrMagnitude > 1e-8f) localAxis = span.normalized;

        IsValid = true;
    }

    /// <summary>칼날 노드 로컬 기준, 비율 <paramref name="t"/> 지점.</summary>
    public Vector3 LocalPoint(float t) => IsValid ? Vector3.Lerp(localBase, localTip, t) : Vector3.zero;

    /// <summary>현재 프레임의 월드 지점. 툴이 궤적을 그릴 때 쓴다.</summary>
    public Vector3 WorldPoint(float t) =>
        IsValid && weapon != null ? weapon.TransformPoint(LocalPoint(t)) : Vector3.zero;

    /// <summary>현재 프레임의 칼날 장축(월드). 회전 정렬에 쓴다.</summary>
    public Vector3 WorldAxis() =>
        IsValid && weapon != null ? weapon.TransformDirection(localAxis) : Vector3.up;
}
