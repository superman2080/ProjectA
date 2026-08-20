using System.Collections.Generic;
using UnityEngine;

namespace EnemySpace
{
    /// <summary>
    /// 적 한 종류의 정의. <b>HP를 두지 않는다</b> — 처치 시점은 채보(<see cref="EnemyCue.killOnSuccess"/>)가 정한다.
    /// 곡에 맞춰 죽어야 하므로 저작 데이터가 진실의 원천인 편이 맞다.
    /// </summary>
    [CreateAssetMenu(fileName = "EnemyDefinition", menuName = "Scriptable Objects/Enemy Definition")]
    public class EnemyDefinition : ScriptableObject
    {
        [Tooltip("적 프리팹. EnemyView가 런타임에 붙는다.")]
        [SerializeField] private GameObject prefab;

        [Tooltip("처치 시 갈라질 절단 프록시(폴백). 아래 배열이 비었거나 패턴에 칼 평면이 없을 때 쓰인다.")]
        [SerializeField] private SliceSpace.SliceSet deathSliceSet;

        [Tooltip("절단 각도별 세트들. 패턴의 스윙(Pattern.BladePlane)과 가장 비슷한 것이 뽑힌다.\n" +
                 "⚠ 여기 넣는 세트는 전부 위 prefab에서 구워진 것이어야 한다 — 시체 프리팹이 그 적의\n" +
                 "스켈레톤 사본을 들고 있어 모델을 넘나들 수 없다. 굽기 툴이 정의를 받는 이유가 이것이다.\n" +
                 "비우면 위 단일 필드 그대로(기존 동작).")]
        [SerializeField] private SliceSpace.SliceSet[] deathSliceSets;

        [Header("Ambush")]
        [Tooltip("패턴 밖 공백에서 기습할 때 쓸 공격 클립들. 발동마다 랜덤으로 하나를 고른다.\n" +
                 "⚠ 적 종류가 소유한다 — 클립은 리그·무기에 종속이라 패턴에 둘 수 없다(DeathSliceSet과 같은 논리).\n" +
                 "⚠ 길이를 섞어 넣을 것 — 창이 짧으면 짧은 클립만 후보에 남는다. 긴 것만 넣으면 좁은 공백에서 안 뜬다.\n" +
                 "비우면 DodgeDirector의 폴백을 쓰고, 그것도 비면 이 종류는 기습을 하지 않는다.")]
        [SerializeField] private PatternSpace.ClipAlignment[] ambushAttacks;

        [Header("Variation")]
        [Tooltip("스폰 시 적용할 스케일 편차(±비율). 같은 모델 반복이 티 나지 않게 한다.")]
        [Range(0f, 0.3f)][SerializeField] private float scaleJitter = 0.05f;

        [Header("Pooling")]
        [Tooltip("곡 시작 전(카운트다운)에 미리 만들어 둘 인스턴스 수.")]
        [SerializeField] private int initialPoolSize = 4;

        [Tooltip("풀에 보관할 최대 인스턴스 수.")]
        [SerializeField] private int maxPoolSize = 12;

        public GameObject Prefab => prefab;
        /// <summary>폴백 세트. 패턴에 칼 평면이 없거나 매칭 후보가 하나도 없을 때 쓰인다.</summary>
        public SliceSpace.SliceSet DeathSliceSet => deathSliceSet != null ? deathSliceSet : FirstUsableSet;

        /// <summary>절단 각도별 세트. 스윙과의 근접도로 뽑는다(<see cref="SliceSpace.SliceMatch"/>).</summary>
        public IReadOnlyList<SliceSpace.SliceSet> DeathSliceSets => deathSliceSets;

        private SliceSpace.SliceSet FirstUsableSet
        {
            get
            {
                if (deathSliceSets == null) return null;

                foreach (var set in deathSliceSets)
                    if (set != null) return set;

                return null;
            }
        }

        /// <summary>기습 공격 클립 후보. 비어 있으면 디렉터의 폴백이 대신한다.</summary>
        public PatternSpace.ClipAlignment[] AmbushAttacks => ambushAttacks;
        public float ScaleJitter => scaleJitter;
        public int InitialPoolSize => Mathf.Max(0, initialPoolSize);
        public int MaxPoolSize => Mathf.Max(1, maxPoolSize);

        /// <summary>런타임에 쓸 수 있는 상태인지. 프리팹이 없으면 무연출로 넘긴다.</summary>
        public bool IsUsable => prefab != null;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (deathSliceSets == null) return;

            // canonical 평면이 없는 세트는 매칭에서 영영 안 뽑힌다 — 조용히 죽은 배선이 되므로 여기서 잡는다.
            for (int i = 0; i < deathSliceSets.Length; i++)
            {
                var set = deathSliceSets[i];
                if (set == null || set.HasBakedBladePlane) continue;

                Debug.LogWarning(
                    $"[EnemyDefinition] '{name}'의 deathSliceSets[{i}] '{set.name}'에 canonical 칼 평면이 없습니다. " +
                    "매칭 후보에서 빠집니다 — Tools/Mesh Slice Baker의 '패턴 감사' 탭에서 평면을 기입하세요.", this);
            }
        }
#endif
    }
}
