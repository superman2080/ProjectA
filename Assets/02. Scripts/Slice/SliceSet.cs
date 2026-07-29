using UnityEngine;

namespace SliceSpace
{
    /// <summary>
    /// 굽기 툴의 산출물 1건 — 표적 하나의 원본과 미리 구운 조각들.
    ///
    /// <para>패턴이 이 에셋을 <b>직접 참조</b>한다(카탈로그를 거치지 않는다). 그래서 이 에셋의 GUID가
    /// 유일한 배선이며, 재굽기 때 GUID를 잃으면 참조하던 모든 패턴이 끊긴다 —
    /// 굽기 툴은 반드시 제자리 수정으로 갱신한다.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "SliceSet", menuName = "Scriptable Objects/Slice Set")]
    public class SliceSet : ScriptableObject
    {
        [Tooltip("온전한 표적 프리팹(절단 전 모습).")]
        [SerializeField] private GameObject originalPrefab;

        [Tooltip("조각 프리팹 N개. 2개라고 가정하지 않는다 — 절단 결과에 따라 개수가 달라진다.")]
        [SerializeField] private GameObject[] piecePrefabs;

        [Tooltip("각 조각의 원본 기준 로컬 위치. 조각 피벗을 무게중심으로 옮겼기 때문에 배치에 필요하다.")]
        [SerializeField] private Vector3[] pieceLocalOffsets;

        [Tooltip("조각별 바깥 방향(무게중심 − 원본 중심). 런타임 흩뿌림의 1차 방향원.")]
        [SerializeField] private Vector3[] pieceScatterDirs;

        [Tooltip("이 세트를 구울 때 사용한 절단 평면. 굽기 툴이 재굽기 때 프리셋 대신 이 값을 로드한다.")]
        [SerializeField] private SlicePlane[] bakedPlanes;

        [Tooltip("어떤 프리셋에서 출발했는지 나타내는 라벨. 런타임 조회에는 쓰지 않는다.")]
        [SerializeField] private SliceShape shape = SliceShape.Custom;

        [Header("Pooling")]
        [Tooltip("씬 시작 시 미리 만들어 둘 인스턴스 수(Director의 prewarmSets에 등록된 경우).")]
        [SerializeField] private int initialPoolSize = 2;

        [Tooltip("풀에 보관할 최대 인스턴스 수. 넘으면 파기한다.")]
        [SerializeField] private int maxPoolSize = 8;

        public GameObject OriginalPrefab => originalPrefab;
        public GameObject[] PiecePrefabs => piecePrefabs;
        public Vector3[] PieceLocalOffsets => pieceLocalOffsets;
        public Vector3[] PieceScatterDirs => pieceScatterDirs;
        public SlicePlane[] BakedPlanes => bakedPlanes;
        public SliceShape Shape => shape;
        public int InitialPoolSize => Mathf.Max(0, initialPoolSize);
        public int MaxPoolSize => Mathf.Max(1, maxPoolSize);

        public int PieceCount => piecePrefabs != null ? piecePrefabs.Length : 0;

        /// <summary>런타임에 쓸 수 있는 상태인지. 원본이 없거나 조각 배열 길이가 어긋나면 무연출로 넘긴다.</summary>
        public bool IsUsable =>
            originalPrefab != null &&
            piecePrefabs != null && piecePrefabs.Length > 0 &&
            pieceLocalOffsets != null && pieceLocalOffsets.Length == piecePrefabs.Length &&
            pieceScatterDirs != null && pieceScatterDirs.Length == piecePrefabs.Length;

#if UNITY_EDITOR
        /// <summary>굽기 툴 전용 기입 경로. 런타임 코드는 이 메서드를 호출하지 않는다.</summary>
        public void EditorAssign(
            GameObject original, GameObject[] pieces, Vector3[] offsets, Vector3[] scatterDirs,
            SlicePlane[] planes, SliceShape bakedShape)
        {
            originalPrefab = original;
            piecePrefabs = pieces;
            pieceLocalOffsets = offsets;
            pieceScatterDirs = scatterDirs;
            bakedPlanes = planes;
            shape = bakedShape;
        }
#endif
    }
}
