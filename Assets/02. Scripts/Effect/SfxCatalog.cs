using System;
using UnityEngine;

/// <summary>효과음이 재생되는 순간을 식별하는 트리거 키. 카탈로그 매핑의 키로 쓰인다.</summary>
public enum SfxTrigger
{
    Perfect,
    Good,
    Miss,
}

/// <summary>
/// 카탈로그 한 행. "어떤 트리거에 어떤 클립을, 어떤 볼륨/피치로" 재생할지 인스펙터에서 지정한다.
/// 효과음 추가 = 이 엔트리를 리스트에 한 줄 더하는 것(코드 수정 없음). clip이 비면 그 트리거는 무음.
/// </summary>
[Serializable]
public struct SfxEntry
{
    public SfxTrigger trigger;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume;
    public float pitch;
}
