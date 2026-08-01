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

        [Tooltip("에디터·로그 식별용 이름. 비우면 에셋 이름을 쓴다.")]
        [SerializeField] private string displayName;

        [Tooltip("처치 시 갈라질 절단 프록시. 비우면 절단 없이 소멸한다.")]
        [SerializeField] private SliceSpace.SliceSet deathSliceSet;

        [Header("Variation")]
        [Tooltip("스폰 시 적용할 스케일 편차(±비율). 같은 모델 반복이 티 나지 않게 한다.")]
        [Range(0f, 0.3f)][SerializeField] private float scaleJitter = 0.05f;

        [Header("Pooling")]
        [Tooltip("곡 시작 전(카운트다운)에 미리 만들어 둘 인스턴스 수.")]
        [SerializeField] private int initialPoolSize = 4;

        [Tooltip("풀에 보관할 최대 인스턴스 수.")]
        [SerializeField] private int maxPoolSize = 12;

        public GameObject Prefab => prefab;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public SliceSpace.SliceSet DeathSliceSet => deathSliceSet;
        public float ScaleJitter => scaleJitter;
        public int InitialPoolSize => Mathf.Max(0, initialPoolSize);
        public int MaxPoolSize => Mathf.Max(1, maxPoolSize);

        /// <summary>런타임에 쓸 수 있는 상태인지. 프리팹이 없으면 무연출로 넘긴다.</summary>
        public bool IsUsable => prefab != null;
    }
}
