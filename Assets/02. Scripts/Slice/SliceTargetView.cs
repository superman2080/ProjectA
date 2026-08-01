using System;
using System.Collections.Generic;
using UnityEngine;

namespace SliceSpace
{
    /// <summary>
    /// 표적 하나의 뷰. 스폰 지점에서 <b>−Z로 등속 이동</b>해 임팩트 시각에 판정 지점에 닿고,
    /// 성공하면 미리 구운 조각으로 갈라지며(<see cref="Slice"/>), 실패하면 소멸한다(<see cref="Crush"/>).
    ///
    /// <para>이동은 물리가 아니라 트랜스폼이다 — 임팩트 시각에 <b>정확히</b> 도달해야 하기 때문이다.</para>
    /// </summary>
    public class SliceTargetView : MonoBehaviour
    {
        private SliceSet set;
        private GameObject originalInstance;
        private readonly List<SlicePiece> pieces = new List<SlicePiece>();

        private Vector3 spawnPos;
        private Vector3 impactPos;
        private float spawnTime;
        private float impactTime;
        private bool arrived;
        private bool resolved;
        private float resolveTime;

        /// <summary>조각이 흩어지기 시작한 뒤 경과 시간. Director가 수명 관리에 쓴다.</summary>
        public float TimeSinceResolved => resolved ? Time.time - resolveTime : 0f;
        public bool Resolved => resolved;
        public SliceSet Set => set;
        public IReadOnlyList<SlicePiece> Pieces => pieces;

        /// <summary>원본 인스턴스를 붙여 초기화한다. 인스턴스 생성/회수는 Director(풀)가 담당한다.</summary>
        public void Setup(SliceSet sliceSet, GameObject original, Vector3 from, Vector3 to, float startTime, float arriveTime)
        {
            set = sliceSet;
            originalInstance = original;
            spawnPos = from;
            impactPos = to;
            spawnTime = startTime;
            impactTime = arriveTime;
            arrived = false;
            resolved = false;

            pieces.Clear();

            transform.position = from;
            if (originalInstance != null)
            {
                originalInstance.transform.SetParent(transform, false);
                originalInstance.transform.localPosition = Vector3.zero;
                originalInstance.transform.localRotation = Quaternion.identity;
                originalInstance.SetActive(true);
            }
        }

        void Update()
        {
            if (arrived) return;

            float duration = Mathf.Max(impactTime - spawnTime, 1e-4f);
            float t = Mathf.Clamp01((Time.time - spawnTime) / duration);
            transform.position = Vector3.Lerp(spawnPos, impactPos, t);

            if (t >= 1f) arrived = true;
        }

        /// <summary>표적이 절단 직전까지 이동하던 속도(월드). 조각의 초기 속도로 그대로 넘어간다.</summary>
        public Vector3 ApproachVelocity =>
            (impactPos - spawnPos) / Mathf.Max(impactTime - spawnTime, 1e-4f);

        /// <summary>
        /// 성공 — 원본을 감추고 조각으로 교체해 흩뿌린다.
        ///
        /// <para>조각은 배치 직후 <b>부모에서 떨어져 물리로 넘어간다</b>(<see cref="SlicePiece.Launch"/>).
        /// 예전처럼 자식으로 남겨 −Z 속도를 승계하지 않고, <see cref="ApproachVelocity"/>를 초기 속도로 명시해 넘긴다 —
        /// 그림은 같으면서 바닥에 부딪히고 멈춰 눕는 게 붙는다.</para>
        /// </summary>
        public void Slice(IReadOnlyList<SlicePiece> spawnedPieces, float scatterSpeed, float scatterJitter, float scatterSpin, int seed, int pieceLayer = -1)
        {
            if (resolved) return;
            resolved = true;
            resolveTime = Time.time;

            if (originalInstance != null) originalInstance.SetActive(false);

            pieces.Clear();
            pieces.AddRange(spawnedPieces);

            var offsets = set.PieceLocalOffsets;
            var dirs = set.PieceScatterDirs;
            int n = pieces.Count;

            // 굽기 결과가 같으면 런타임 결과도 같도록, 표적 인스턴스별 seed로 결정론적 난수를 쓴다.
            var rng = new System.Random(seed);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            Vector3 inherited = ApproachVelocity;

            for (int i = 0; i < n; i++)
            {
                Vector3 dir = i < dirs.Length ? dirs[i] : Vector3.zero;
                Vector2 xy = new Vector2(dir.x, dir.y);

                if (xy.sqrMagnitude < 1e-6f)
                {
                    // 중심이 절단축 위에 있어 바깥 방향이 정해지지 않는 조각(┼의 4조각은 전부 여기 걸릴 수 있다).
                    // 절단 평면에 의존하지 않는 결정론적 규칙: 조각 인덱스로 원을 균등 분할한다.
                    float angle = 360f * i / Mathf.Max(n, 1) * Mathf.Deg2Rad;
                    xy = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }

                xy.Normalize();

                float speed = Mathf.Max(0f, scatterSpeed + Range(-scatterJitter, scatterJitter));
                Vector3 axis = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
                float spin = Range(-scatterSpin, scatterSpin);

                // 배치는 로컬 기준(굽기 산출물이 로컬 오프셋), 발사 방향은 월드 기준으로 변환한다.
                pieces[i].Place(transform, i < offsets.Length ? offsets[i] : Vector3.zero);
                Vector3 worldDir = transform.TransformDirection(new Vector3(xy.x, xy.y, 0f));
                pieces[i].Launch(inherited, worldDir, speed, axis, spin, pieceLayer);
            }
        }

        /// <summary>조각이 전부 물리적으로 잠들었는지. 디렉터가 이른 회수 판단에 쓴다.</summary>
        public bool AllPiecesSettled
        {
            get
            {
                if (pieces.Count == 0) return false;

                foreach (var piece in pieces)
                {
                    if (piece != null && !piece.IsSettled) return false;
                }

                return true;
            }
        }

        /// <summary>실패 — 관통시키지 않는다. 이동을 멈추고 즉시 소멸 대상이 된다.</summary>
        public void Crush()
        {
            if (resolved) return;
            resolved = true;
            resolveTime = Time.time;
            arrived = true;

            if (originalInstance != null) originalInstance.SetActive(false);
        }

        /// <summary>회수 직전 상태 초기화. 원본 인스턴스는 Director가 풀로 되돌린다.</summary>
        public GameObject DetachOriginal()
        {
            var original = originalInstance;
            originalInstance = null;
            return original;
        }

        public Vector3 ImpactPosition => impactPos;
        public float ImpactTime => impactTime;
    }
}
