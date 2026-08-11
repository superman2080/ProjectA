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

        [Header("Skinned (Humanoid)")]
        [Tooltip("휴머노이드 굽기 산출물인가. 런타임이 어느 경로를 탈지 판단하는 유일한 근거다.")]
        [SerializeField] private bool skinned;

        [Tooltip("시체 프리팹 — 자기 스켈레톤 사본 + 조각 SkinnedMeshRenderer N개. skinned일 때 이것이 산출물이며 piecePrefabs는 쓰지 않는다.")]
        [SerializeField] private GameObject corpsePrefab;

        [Tooltip("시체 프리팹 안에서 스킨드로 남아 래그돌할 조각 인덱스(루트 본을 포함한 조각). 없으면 -1.")]
        [SerializeField] private int rootPieceIndex = -1;

        [Tooltip("어떤 포즈로 구웠는지 기록(재굽기 재현용). 런타임 정합성 요구는 아니다.")]
        [SerializeField] private AnimationClip bakedPoseClip;

        [SerializeField] private float bakedPoseTime;

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

        /// <summary>휴머노이드(스킨드) 산출물인가. true면 <see cref="CorpsePrefab"/>이 산출물이다.</summary>
        public bool Skinned => skinned;

        /// <summary>시체 프리팹. 스킨드 경로에서만 유효하다.</summary>
        public GameObject CorpsePrefab => corpsePrefab;

        /// <summary>스킨드로 남아 래그돌할 조각 인덱스. 없으면 -1.</summary>
        public int RootPieceIndex => rootPieceIndex;

        /// <summary>
        /// 런타임에 쓸 수 있는 상태인지. 무연출로 넘길지의 유일한 근거다.
        /// <b>스킨드와 정적은 검증 대상이 다르다</b> — 스킨드는 시체 프리팹 하나가 산출물이라 조각 배열을 보지 않는다.
        /// </summary>
        public bool IsUsable => skinned ? IsSkinnedUsable : IsStaticUsable;

        private bool IsSkinnedUsable => corpsePrefab != null && rootPieceIndex >= 0;

        private bool IsStaticUsable =>
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
            skinned = false;
        }

        /// <summary>
        /// 휴머노이드(스킨드) 굽기 전용 기입 경로. 산출물이 <b>시체 프리팹 하나</b>라
        /// 조각 배열·오프셋·흩뿌림 방향을 쓰지 않는다(그 정보는 프리팹 안에 이미 배치돼 있다).
        /// </summary>
        public void EditorAssignSkinned(
            GameObject sourcePrefab, GameObject corpse, int rootPiece,
            SlicePlane[] planes, AnimationClip poseClip, float poseTime)
        {
            originalPrefab = sourcePrefab;
            corpsePrefab = corpse;
            rootPieceIndex = rootPiece;
            bakedPlanes = planes;
            shape = SliceShape.Custom;
            bakedPoseClip = poseClip;
            bakedPoseTime = poseTime;
            skinned = true;

            // 정적 경로 필드는 비워 둔다 — 남아 있으면 어느 쪽이 산출물인지 모호해진다.
            piecePrefabs = new GameObject[0];
            pieceLocalOffsets = new Vector3[0];
            pieceScatterDirs = new Vector3[0];
        }
#endif
    }
}
