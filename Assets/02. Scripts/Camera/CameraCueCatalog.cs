using System;
using UnityEngine;

/// <summary>카메라 연출이 재생되는 순간을 식별하는 트리거 키. 카탈로그 매핑의 키로 쓰인다.</summary>
public enum CameraTrigger
{
    PatternSuccess, // 표적이 갈라지는 순간(Deadline)
    PatternMiss,    // 첫 미스 순간 — 피격(Hit) 애니메이션과 동기
    PatternFailure, // 표적이 충돌·소멸하는 순간(Deadline)
    EnemyKilled,    // 적 처치 — 무쌍 타격감의 주 큐
}

/// <summary>
/// 카탈로그 한 행. "어떤 트리거에 얼마나 세게, 얼마나 짧게 흔들지"를 인스펙터에서 지정한다.
/// 연출 추가 = 이 엔트리를 리스트에 한 줄 더하는 것(코드 수정 없음). <c>shakeDuration</c>이 0 이하면 무연출.
///
/// <para>감쇠 곡선과 주파수는 여기 없다 — 셋 다 1→0 감쇠라 <see cref="CameraDirector"/>가 공식으로 처리하고,
/// 주파수는 0.2초 남짓 쉐이크에서 큐별 차이가 체감되지 않아 director의 공용 값 하나를 쓴다.</para>
///
/// <para><b>펀치는 쉐이크와 채널이 다르다</b> — 쉐이크는 Perlin 노이즈, 펀치는 렌즈 FOV다.
/// 그래서 한 큐가 둘 다 내도 간섭하지 않고, 한쪽만 쓰고 싶으면 다른 쪽 지속시간을 0으로 두면 된다.</para>
/// </summary>
[Serializable]
public struct CameraCueEntry
{
    public CameraTrigger trigger;
    public float shakeAmplitude; // Perlin AmplitudeGain에 더해질 최대치
    public float shakeDuration;  // 초. 0 이하면 무연출

    [Tooltip("임팩트 순간 FOV를 이만큼 밀었다 되돌린다(도). 음수면 확 당겨지는 줌인 — 타격감은 보통 이쪽이다.")]
    public float punchFovDelta;

    [Tooltip("펀치가 원래 FOV로 돌아오는 시간(초). 0 이하면 펀치 없음(기존 큐는 여기가 0이라 그대로 동작한다).")]
    public float punchDuration;
}
