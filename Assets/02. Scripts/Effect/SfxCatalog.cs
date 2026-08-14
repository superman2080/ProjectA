using System;
using UnityEngine;

/// <summary>효과음이 재생되는 순간을 식별하는 트리거 키. 카탈로그 매핑의 키로 쓰인다.</summary>
/// <remarks>
/// <b>⚠ 정수를 명시한다.</b> 이 값이 씬의 카탈로그 리스트에 직렬화되므로 순서가 바뀌면
/// 기존 배선이 조용히 밀린다(<c>EffectTrigger</c>와 같은 규율).
/// </remarks>
public enum SfxTrigger
{
    Perfect = 0,
    Good = 1,
    Miss = 2,
    /// <summary>적이 기습을 시작한 순간(칼을 드는 소리). 링보다 먼저 온다.</summary>
    AmbushTelegraph = 3,
    DodgeSuccess = 4,
    DodgeFail = 5,
    /// <summary>
    /// 패턴 성공 시 <b>칼이 닿는 순간</b>(= <c>Deadline + ImpactOffset</c>).
    /// <b>판정 순간이 아니다</b> — 완료는 마지막 노드 입력이고 임팩트는 거기서 <c>goodWindow</c> + 보정만큼 뒤다.
    /// </summary>
    PatternImpact = 6,
}

/// <summary>
/// 카탈로그 한 행. "어떤 트리거에 어떤 클립을, 어떤 볼륨/피치로" 재생할지 인스펙터에서 지정한다.
/// 효과음 추가 = 이 엔트리를 리스트에 한 줄 더하는 것(코드 수정 없음). clip이 비면 그 트리거는 무음.
/// </summary>
/// <remarks>
/// <b>⚠ struct가 아니라 class인 이유는 기본값 하나 때문이다.</b> struct는 필드 초기화가 안 돼
/// 인스펙터에서 새 행을 추가하면 <c>volume</c>·<c>pitch</c>가 <b>둘 다 0</b>으로 태어난다.
/// 볼륨 0은 "안 들린다"로 바로 알아채지만 <b>피치 0은 재생 자체가 멈춰</b> 클립을 꽂아도 무음이고,
/// 원인이 클립인지 배선인지 볼륨인지 구분되지 않는다. class면 아래 초기값이 그대로 새 행에 실린다.
/// </remarks>
[Serializable]
public class SfxEntry
{
    public SfxTrigger trigger;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Min(0.01f)] public float pitch = 1f;

    /// <summary>
    /// 실제로 쓸 피치. <b>이미 0으로 저장된 기존 행을 구제한다</b> —
    /// class 전환은 새 행만 고치고, 인스펙터에서 한 번도 안 건드린 옛 행은 0인 채로 남는다.
    /// </summary>
    public float Pitch => pitch > 0f ? pitch : 1f;
}
