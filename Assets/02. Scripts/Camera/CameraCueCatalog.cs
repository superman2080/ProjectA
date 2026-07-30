using System;

/// <summary>카메라 연출이 재생되는 순간을 식별하는 트리거 키. 카탈로그 매핑의 키로 쓰인다.</summary>
public enum CameraTrigger
{
    PatternSuccess, // 표적이 갈라지는 순간(Deadline)
    PatternMiss,    // 첫 미스 순간 — 피격(Hit) 애니메이션과 동기
    PatternFailure, // 표적이 충돌·소멸하는 순간(Deadline)
}

/// <summary>
/// 카탈로그 한 행. "어떤 트리거에 얼마나 세게, 얼마나 짧게 흔들지"를 인스펙터에서 지정한다.
/// 연출 추가 = 이 엔트리를 리스트에 한 줄 더하는 것(코드 수정 없음). <c>shakeDuration</c>이 0 이하면 무연출.
///
/// <para>감쇠 곡선과 주파수는 여기 없다 — 셋 다 1→0 감쇠라 <see cref="CameraDirector"/>가 공식으로 처리하고,
/// 주파수는 0.2초 남짓 쉐이크에서 큐별 차이가 체감되지 않아 director의 공용 값 하나를 쓴다.</para>
/// </summary>
[Serializable]
public struct CameraCueEntry
{
    public CameraTrigger trigger;
    public float shakeAmplitude; // Perlin AmplitudeGain에 더해질 최대치
    public float shakeDuration;  // 초. 0 이하면 무연출
}
