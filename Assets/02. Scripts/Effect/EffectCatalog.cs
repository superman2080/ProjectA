using System;
using UnityEngine;

/// <summary>이펙트가 재생되는 순간을 식별하는 트리거 키. 카탈로그 매핑의 키로 쓰인다.</summary>
public enum EffectTrigger
{
    Perfect,
    Good,
    Miss,
    PatternCompleteFull, // 전체 정답 완성
    PatternComplete,     // 정답 아님(부분) 완성
    NodeConnected,
    Parried,             // 적 공격을 받아쳐 칼이 맞부딪히는 순간
    EnemyEvaded,         // 무방비 적을 놓쳐 헛스윙 — 적이 물러나 회피
    EnemyKilled,         // 적 처치(모델이 갈라지는 순간)
    ProjectileSliced,    // 원거리 오브젝트 절단
}

/// <summary>
/// 카탈로그 한 행. "어떤 트리거에 어떤 프리팹을, 얼마의 풀로" 쓸지 인스펙터에서 지정한다.
/// 이펙트 추가 = 이 엔트리를 리스트에 한 줄 더하는 것(코드 수정 없음). prefab이 비면 그 트리거는 무연출.
/// </summary>
[Serializable]
public struct EffectEntry
{
    public EffectTrigger trigger;
    public GameObject prefab;   // 루트에 UIParticle + CanvasEffectView를 가진 프리팹
    public int initialSize;     // 풀 초기 확보 개수
    public int maxSize;         // 풀 보관 상한(초과 반환분은 파기)
}
