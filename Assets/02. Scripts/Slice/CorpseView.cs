using System.Collections.Generic;
using UnityEngine;

namespace SliceSpace
{
    /// <summary>
    /// 시체 하나의 뷰. <b>산 적과 시체는 근본적으로 다른 오브젝트다</b> — 시체는 자기 스켈레톤 사본을 갖는다.
    ///
    /// <para>그래서 교체 순간 산 적의 본 포즈만 전사하면 겉모습이 이어지고,
    /// <b>산 적 인스턴스는 통째로 풀에 반납</b>할 수 있다. 조각을 산 적의 스켈레톤에 물리면
    /// 그 스켈레톤이 살아 있어야 해서 반납이 불가능하다 — 이 클래스의 존재 이유가 그것이다.</para>
    ///
    /// <para>루트 본을 포함한 조각 하나만 스킨드로 남아(래그돌 대상) 스켈레톤을 따라가고,
    /// 나머지는 교체 순간 굳혀 부모에서 떼고 물리로 넘긴다. 스킨드로 두면 몸에 붙어서 같이 움직인다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class CorpseView : MonoBehaviour
    {
        [Tooltip("굽기 툴이 채운다. 순서가 SliceSet.RootPieceIndex의 기준이다.")]
        [SerializeField] private SlicePiece[] pieces;

        [Tooltip("포즈 전사의 기준이 되는 스켈레톤. 조각들의 SkinnedMeshRenderer가 공유한다.")]
        [SerializeField] private Transform[] bones;

        [Tooltip("스킨드로 남아 래그돌할 조각. 굽기 시점에 결정된다.")]
        [SerializeField] private int rootPieceIndex = -1;

        private readonly List<SlicePiece> launched = new List<SlicePiece>();

        // ── 소멸 (docs/EnemyDissolve) ───────────────────────────────────────────
        // 조각이 다 잠들면 그냥 회수해서 시체가 <b>한 프레임에 사라졌다</b>. 여기서 태워 없앤다.
        // 진행을 미는 방식은 EnemyView와 같다 — 머티리얼을 갈아끼우고 _Dissolve를 민다.
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        private readonly DissolveSwap dissolveSwap = new DissolveSwap();
        private readonly List<Renderer> dissolveRenderers = new List<Renderer>();
        private MaterialPropertyBlock propertyBlock;
        private bool dissolving;
        private float dissolveStart;
        private float dissolveDuration;

        /// <summary>소멸이 시작됐는지. 디렉터가 "시작할까 / 기다릴까"를 가르는 데 쓴다.</summary>
        public bool Dissolving => dissolving;

        /// <summary>소멸이 끝났는지. 디렉터가 회수 시점을 잡는 데 쓴다.</summary>
        public bool DissolveFinished => dissolving && Time.time >= dissolveStart + dissolveDuration;

        /// <summary>
        /// 태워 없애기 시작. <b>흩어진 조각까지 전부 포함한다</b> —
        /// 조각은 <see cref="SlicePiece.Launch"/>에서 부모를 떠나므로 계층을 훑어서는 못 찾는다.
        /// </summary>
        public void Dissolve(float duration, Material dissolveMaterial)
        {
            if (dissolving) return;

            dissolving = true;
            dissolveStart = Time.time;
            dissolveDuration = Mathf.Max(duration, 0.01f);

            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

            dissolveRenderers.Clear();
            if (pieces != null)
            {
                foreach (var piece in pieces)
                {
                    if (piece != null) piece.GetComponentsInChildren(true, cachedRenderers);
                    foreach (var r in cachedRenderers) dissolveRenderers.Add(r);
                }
            }

            // 출혈 파티클은 소멸 대상이 아니다 — 머티리얼을 갈아끼우면 피가 통째로 사라진다.
            dissolveRenderers.RemoveAll(r => r is ParticleSystemRenderer);

            StopBleeding();

            dissolveSwap.Begin(dissolveRenderers, dissolveMaterial, propertyBlock);
            SetDissolveAmount(0f);
        }

        private static readonly List<Renderer> cachedRenderers = new List<Renderer>();

        void Update()
        {
            if (!dissolving) return;

            SetDissolveAmount(Mathf.Clamp01((Time.time - dissolveStart) / dissolveDuration));
        }

        /// <summary><b>인스턴스별 진행.</b> 머티리얼을 공유하므로 프로퍼티 블록이 아니면 시체 전원이 같이 탄다.</summary>
        private void SetDissolveAmount(float amount)
        {
            foreach (var r in dissolveRenderers)
            {
                if (r == null) continue;

                r.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(DissolveId, amount);
                r.SetPropertyBlock(propertyBlock);
            }
        }

        /// <summary>
        /// 산 적의 포즈를 그대로 물려받는다.
        /// <b>본 배열 순서가 같다는 전제</b>가 성립하는 이유는 굽기 툴이 원본 리그의 본을 순서 그대로 복제하기 때문이다.
        /// </summary>
        public void AdoptPose(Transform sourceRoot, Transform[] sourceBones)
        {
            if (sourceRoot != null)
            {
                transform.position = sourceRoot.position;
                transform.rotation = sourceRoot.rotation;
                transform.localScale = sourceRoot.localScale;
            }

            if (bones == null || sourceBones == null) return;

            int count = Mathf.Min(bones.Length, sourceBones.Length);
            if (bones.Length != sourceBones.Length)
            {
                Debug.LogWarning(
                    $"[CorpseView] '{name}'의 본 수({bones.Length})가 원본({sourceBones.Length})과 다릅니다. " +
                    "리그가 바뀌었다면 다시 구우세요 — 포즈가 어긋난 채 교체됩니다.", this);
            }

            for (int i = 0; i < count; i++)
            {
                if (bones[i] == null || sourceBones[i] == null) continue;
                bones[i].localPosition = sourceBones[i].localPosition;
                bones[i].localRotation = sourceBones[i].localRotation;
                bones[i].localScale = sourceBones[i].localScale;
            }
        }

        /// <summary>
        /// 갈라뜨린다 — 루트 조각만 스킨드로 남기고 나머지는 굳혀 흩뿌린다.
        /// </summary>
        /// <param name="meshPool">굳힘에 재사용할 메쉬 큐. 교체마다 <c>new Mesh()</c>를 만들면 GC 압박이 된다.</param>
        /// <param name="keepRootSkinned">
        /// 루트 조각을 스킨드로 남길지. <b>래그돌이 붙어 있을 때만 true여야 한다</b> —
        /// 래그돌이 없으면 그 조각은 물리도 없이 스켈레톤에 매달려 <b>공중에 그대로 떠 있는다</b>.
        /// 후속 플랜(<c>docs/EnemyRagdoll/</c>)이 붙기 전까지는 false로 두어 나머지와 같이 떨어뜨린다.
        /// </param>
        public void Burst(float scatterSpeed, float scatterSpin, int pieceLayer, Queue<Mesh> meshPool,
            bool keepRootSkinned = false)
        {
            launched.Clear();
            if (pieces == null) return;

            int n = pieces.Length;
            for (int i = 0; i < n; i++)
            {
                var piece = pieces[i];
                if (piece == null) continue;

                piece.gameObject.SetActive(true);
                if (keepRootSkinned && i == rootPieceIndex) continue; // 스켈레톤에 남아 래그돌한다

                piece.FreezeToStaticMesh(meshPool != null && meshPool.Count > 0 ? meshPool.Dequeue() : null);

                // 바깥 방향은 시체 중심 → 조각 중심. 절단 평면에 의존하지 않아 어떤 절단에도 성립한다.
                Vector3 outward = piece.transform.position - transform.position;
                if (outward.sqrMagnitude < 1e-6f)
                {
                    float angle = 360f * i / Mathf.Max(n, 1) * Mathf.Deg2Rad;
                    outward = new Vector3(Mathf.Cos(angle), 0.3f, Mathf.Sin(angle));
                }

                // 서 있던 적이므로 승계할 이동 속도가 없다 — 그 자리에서 무너진다.
                piece.Launch(Vector3.zero, outward.normalized, scatterSpeed,
                    Random.onUnitSphere, Random.Range(-scatterSpin, scatterSpin), pieceLayer);
                launched.Add(piece);
            }
        }

        // ── 절단면 출혈 (CLAUDE.md §11-3) ───────────────────────────────────────
        // 조각마다 루프 파티클을 <b>자식으로</b> 하나 붙인다 — 조각이 구르면 피도 같이 돈다(추종 코드 0줄).
        private readonly List<GameObject> bleeders = new List<GameObject>();
        private PrefabPool bleedPool;

        /// <summary>
        /// 절단면마다 출혈 이펙트를 하나씩 붙인다. <b><see cref="Burst"/> 뒤에 부른다</b> —
        /// 흩어진 조각에만 붙이므로 <c>launched</c>가 채워져 있어야 한다.
        ///
        /// <para><b>발생원은 점이 아니라 절단면 자체다.</b> 캡(잘린 면)은 굽기가 <b>마지막 서브메쉬 하나</b>로
        /// 병합해 두므로(§11 머티리얼 슬롯 M+1 규칙), 파티클 shape을 그 서브메쉬로 주면
        /// 면 전체에서 고르게 솟는다. 방향은 캡의 면 법선이 그대로 준다 —
        /// <b>절단 평면도, 좌표계 변환도, 저작값도 필요 없다.</b></para>
        ///
        /// <para>서브메쉬가 하나뿐인 조각은 <b>잘린 면이 없다</b>는 뜻이라 그냥 건너뛴다.</para>
        /// </summary>
        public void Bleed(GameObject prefab, PrefabPool pool, int maxPoolSize)
        {
            if (prefab == null || pool == null) return;

            bleedPool = pool;

            foreach (var piece in launched)
            {
                if (piece == null) continue;

                var renderer = piece.GetComponent<MeshRenderer>();
                var filter = piece.GetComponent<MeshFilter>();
                if (renderer == null || filter == null || filter.sharedMesh == null) continue;

                int cap = filter.sharedMesh.subMeshCount - 1; // 캡은 언제나 마지막 슬롯이다
                if (cap <= 0) continue;

                var go = pool.Rent(prefab, maxPoolSize);
                if (go == null) continue;

                // 부모가 조각이라 조각이 구르면 발생면도 같이 돈다.
                // ⚠ 로컬 포즈는 항등이어야 한다 — MeshRenderer shape은 그 렌더러의 트랜스폼으로 샘플링하므로
                // 이펙트를 따로 옮기면 발생면과 그림이 어긋난다.
                go.transform.SetParent(piece.transform, false);
                go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var shape = ps.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                    shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                    shape.meshRenderer = renderer;
                    shape.useMeshMaterialIndex = true;
                    shape.meshMaterialIndex = cap;
                }

                bleeders.Add(go);
            }
        }

        /// <summary>
        /// 방출만 멈춘다 — 이미 떠 있는 입자는 제 수명대로 사라진다.
        /// <b>즉시 파기하면 소멸 시작 프레임에 피가 뚝 끊겨</b> "탄다"가 아니라 "사라졌다"로 읽힌다.
        /// </summary>
        private void StopBleeding()
        {
            foreach (var go in bleeders)
            {
                if (go == null) continue;

                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>날아간 조각이 전부 잠들었는지. 디렉터가 이른 회수 판단에 쓴다.</summary>
        public bool AllPiecesSettled
        {
            get
            {
                if (launched.Count == 0) return true;

                foreach (var piece in launched)
                {
                    if (piece != null && !piece.IsSettled) return false;
                }

                return true;
            }
        }

        /// <summary>풀 반납 직전 복구. 굳힘에 쓴 메쉬는 풀로 돌려준다.</summary>
        public void ResetState(Queue<Mesh> meshPool)
        {
            // ⚠ 소멸 상태를 안 지우면 다음 대여가 타다 만 채로,
            // 그것도 DissolveFinished가 이미 true라 즉시 회수 대상으로 나온다.
            // pieces가 비어도 반드시 돌려야 하므로 조기 return보다 앞이다.
            dissolveSwap.Restore();
            dissolveRenderers.Clear();
            dissolving = false;

            // ⚠ 조각을 제자리로 되돌리기 <b>전에</b> 떼어낸다 — 안 떼면 출혈 이펙트가 시체와 함께 반납돼
            // 다음 대여가 피를 흘리며 나온다.
            foreach (var go in bleeders) bleedPool?.Release(go);
            bleeders.Clear();

            if (pieces == null) return;

            foreach (var piece in pieces)
            {
                if (piece == null) continue;

                var mesh = piece.DetachFrozenMesh();
                piece.ResetState(); // 부모·로컬 포즈·물리 상태를 제자리로 되돌린다

                if (mesh != null) meshPool?.Enqueue(mesh);
            }

            launched.Clear();
        }

#if UNITY_EDITOR
        /// <summary>굽기 툴 전용 기입 경로. 런타임은 호출하지 않는다.</summary>
        public void EditorAssign(SlicePiece[] bakedPieces, Transform[] bakedBones, int rootPiece)
        {
            pieces = bakedPieces;
            bones = bakedBones;
            rootPieceIndex = rootPiece;
        }
#endif
    }
}
