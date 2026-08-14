using System;
using UnityEngine;

/// <summary>
/// 이펙트가 재생되는 순간을 식별하는 트리거 키. 카탈로그 매핑의 키로 쓰인다.
///
/// <para><b>⚠ 정수를 명시한다.</b> 이 값은 <c>EffectManager.catalog</c>에 그대로 직렬화되어
/// <c>BattleScene.unity</c>에 박힌다 — 순서를 바꾸거나 중간 항목을 지우면 씬의 매핑이 조용히 밀린다.
/// 6·7·9는 삭제된 값의 자리이며 <b>재사용하지 않는다</b>.</para>
///
/// <para><b>여기 있다 = Play() 호출이 코드에 있다.</b> 프리팹이 비어 무연출인 것과,
/// 호출 자체가 없어 프리팹을 꽂아도 안 뜨는 것은 다르다 — 후자는 죽은 스위치이므로 이 enum에서 뺀다.
/// (패링·회피 이펙트는 §7-4 <c>PatternEffectCue.EffectCondition</c>으로 이관됐다.)</para>
/// </summary>
public enum EffectTrigger
{
    Perfect = 0,
    Good = 1,
    Miss = 2,
    PatternCompleteFull = 3, // 전체 정답 완성
    PatternComplete = 4,     // 정답 아님(부분) 완성
    NodeConnected = 5,
    EnemyKilled = 8,         // 적 처치(모델이 갈라지는 순간)
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
