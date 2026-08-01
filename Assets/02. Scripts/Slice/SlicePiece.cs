using UnityEngine;

namespace SliceSpace
{
    /// <summary>
    /// 절단 조각 하나. <b>경계는 "접근이냐 절단 이후냐"다.</b>
    ///
    /// <para>접근 구간은 <c>impactTime</c>에 정확히 도착해야 하므로 뷰가 트랜스폼으로 옮긴다(닫힌 식).
    /// 하지만 <b>절단된 순간부터는 맞출 시각이 없다</b> — 적 조각이든 투사체 조각이든 전부 Rigidbody로 넘어간다.
    /// 그래야 바닥에 부딪히고, 구르고, 멈춰서 눕는다.</para>
    ///
    /// <para><b>부모에서 떼는 것이 필수다.</b> Rigidbody와 부모 트랜스폼이 같이 움직이면 서로 싸운다.
    /// 부모 승계가 사라지므로 접근 속도는 <see cref="Launch"/>의 <c>inheritedVelocity</c>로 명시적으로 넘긴다 —
    /// 날아오던 표적은 계속 날아가고, 서 있는 적은 이 항이 0이라 그 자리에서 무너진다.
    /// <b>같은 식이 양쪽을 다 설명한다.</b></para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SlicePiece : MonoBehaviour
    {
        private Rigidbody body;
        private bool launched;

        // 스킨드 경로(휴머노이드) 전용. 정적 표적 조각에서는 전부 null로 남아 기존 경로만 돈다.
        private SkinnedMeshRenderer skinned;
        private MeshFilter frozenFilter;
        private MeshRenderer frozenRenderer;
        private Mesh frozenMesh;
        private bool frozen;

        // 시체 프리팹 안에서의 제자리. 날아간 조각을 풀 반납 시 여기로 되돌린다 —
        // 안 되돌리면 다음 대여에서 조각이 계층 밖에 흩어진 채로 나온다.
        private Transform home;
        private Vector3 homeLocalPosition;
        private Quaternion homeLocalRotation;
        private bool homeCaptured;

        void Awake()
        {
            CaptureHome();
        }

        private void CaptureHome()
        {
            if (homeCaptured) return;

            home = transform.parent;
            homeLocalPosition = transform.localPosition;
            homeLocalRotation = transform.localRotation;
            homeCaptured = true;
        }

        /// <summary>스킨드 조각인가. 프리팹에 <see cref="SkinnedMeshRenderer"/>가 있으면 그렇다.</summary>
        public bool IsSkinned => Skinned != null;

        private SkinnedMeshRenderer Skinned
        {
            get
            {
                if (skinned == null) skinned = GetComponent<SkinnedMeshRenderer>();
                return skinned;
            }
        }

        /// <summary>
        /// 스킨드 조각을 <b>현재 포즈 그대로 굳혀</b> 정적 메쉬로 바꾼다.
        /// 날아갈 조각에만 쓴다 — 스켈레톤에 물린 채로는 몸에 붙어서 같이 움직이기 때문.
        ///
        /// <para>포즈를 그 프레임에서 캡처하므로 <b>어긋날 여지가 원리적으로 없다</b>. 저작값도 필요 없다.</para>
        /// </summary>
        /// <param name="target">재사용할 메쉬. 매 교체마다 <c>new Mesh()</c>를 만들면 GC 압박이 된다.</param>
        public void FreezeToStaticMesh(Mesh target)
        {
            var source = Skinned;
            if (source == null || frozen) return;

            frozenMesh = target != null ? target : new Mesh { name = $"{name}_Frozen" };
            source.BakeMesh(frozenMesh, true);

            if (frozenFilter == null) frozenFilter = gameObject.AddComponent<MeshFilter>();
            if (frozenRenderer == null) frozenRenderer = gameObject.AddComponent<MeshRenderer>();

            frozenFilter.sharedMesh = frozenMesh;
            frozenRenderer.sharedMaterials = source.sharedMaterials;
            // Unfreeze가 꺼 둔 것을 반드시 되켠다 — 안 켜면 풀에서 재사용된 조각이 통째로 안 보인다.
            frozenRenderer.enabled = true;
            source.enabled = false;
            frozen = true;
        }

        /// <summary>조각끼리의 충돌은 한 번만 끄면 된다(전역 레이어 설정). 매번 호출해도 안전하도록 래치한다.</summary>
        private static int ignoredSelfCollisionLayer = -1;

        /// <summary>물리가 잠들었는지. 디렉터가 페이드아웃·회수 시점을 잡는 데 쓴다.</summary>
        public bool IsSettled => launched && body != null && body.IsSleeping();

        /// <summary>절단 직전 실루엣 위치에 배치한다(아직 흩어지지 않음). 이 시점엔 물리가 꺼져 있다.</summary>
        public void Place(Transform parent, Vector3 localPosition)
        {
            transform.SetParent(parent, false);
            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.identity;

            EnsureBody();
            body.isKinematic = true;
            launched = false;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 흩어짐 시작 — <b>부모에서 떼고 물리에 넘긴다.</b>
        /// </summary>
        /// <param name="inheritedVelocity">절단 직전까지의 이동 속도(월드). 서 있는 적은 <see cref="Vector3.zero"/>.</param>
        /// <param name="direction">튀어나갈 방향(월드). 갈라진 면 법선 쪽.</param>
        /// <param name="speed">위 방향으로 더할 속도.</param>
        /// <param name="spinAxis">회전축(월드).</param>
        /// <param name="spinDegrees">초당 회전각(도).</param>
        /// <param name="layer">조각 전용 레이어. 0 이하가 아니면 적용하고 조각끼리의 충돌을 끈다.</param>
        public void Launch(Vector3 inheritedVelocity, Vector3 direction, float speed, Vector3 spinAxis, float spinDegrees, int layer = -1)
        {
            transform.SetParent(null, true); // 월드 포즈 유지한 채 분리 — 부모와 물리가 싸우지 않게
            EnsureBody();
            EnsureCollider();

            if (layer >= 0)
            {
                gameObject.layer = layer;
                IgnoreSelfCollision(layer);
            }

            Vector3 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Random.onUnitSphere;
            Vector3 axis = spinAxis.sqrMagnitude > 1e-6f ? spinAxis.normalized : Vector3.up;

            body.isKinematic = false;
            body.linearVelocity = inheritedVelocity + dir * speed;
            body.angularVelocity = axis * (spinDegrees * Mathf.Deg2Rad);
            launched = true;
        }

        /// <summary>풀 반납 전 상태 초기화. <b>속도 리셋이 필수다</b> — 안 하면 다음 절단에서 조각이 튀어나간다.</summary>
        public void ResetState()
        {
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            launched = false;

            if (IsSkinned)
            {
                // 시체 프리팹 계층으로 되돌린다. 끊어둔 채 반납하면 다음 대여에서 조각이 흩어져 나온다.
                CaptureHome();
                transform.SetParent(home, false);
                transform.localPosition = homeLocalPosition;
                transform.localRotation = homeLocalRotation;
            }
            else
            {
                transform.SetParent(null, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }

            Unfreeze();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 굳힘을 되돌려 다시 스킨드로 만든다. <b>안 되돌리면 다음 대여에서 MeshFilter가 남아 스키닝이 안 먹는다.</b>
        /// 굳힌 메쉬는 호출자가 풀로 회수한다(여기서 파기하지 않는다).
        /// </summary>
        public void Unfreeze()
        {
            if (!frozen) return;

            if (frozenFilter != null) frozenFilter.sharedMesh = null;
            if (frozenRenderer != null) frozenRenderer.enabled = false;
            if (Skinned != null) Skinned.enabled = true;

            frozen = false;
        }

        /// <summary>굳히는 데 쓴 메쉬를 돌려준다(풀 회수용). 굳힌 상태가 아니면 null.</summary>
        public Mesh DetachFrozenMesh()
        {
            var mesh = frozenMesh;
            frozenMesh = null;
            return mesh;
        }

        private void EnsureBody()
        {
            if (body != null) return;

            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();

            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        /// <summary>convex 쿠킹의 삼각형 상한. 넘으면 Unity가 부분 헐로 대체하며 경고를 뱉는다.</summary>
        private const int ConvexTriangleLimit = 255;

        /// <summary>
        /// 절단 조각은 대개 비볼록이라 convex 근사가 되지만, 잔해라 티가 안 난다.
        ///
        /// <para><b>메쉬를 매번 다시 대입한다.</b> 굳힌 메쉬는 풀에서 돌려쓰고 그 풀은 조각 전체가 공유하므로,
        /// 두 번째 대여에서 <b>다른 조각이 쓰던 메쉬</b>가 올 수 있다. 콜라이더가 있다고 건너뛰면
        /// 옛 메쉬를 붙든 채 내용만 바뀌어 엉뚱한 형상으로 충돌한다.</para>
        ///
        /// <para><b>폴리가 많으면 박스로 떨어뜨린다.</b> 캐릭터를 여러 조각으로 자르면 조각 하나가
        /// 상한(255삼각형)을 쉽게 넘어 부분 헐 경고가 매 처치마다 뜬다.
        /// 잔해는 바닥하고만 부딪히므로(조각끼리 충돌은 꺼져 있다) 박스로 충분하고, 쿠킹 비용도 사라진다.</para>
        /// </summary>
        private void EnsureCollider()
        {
            var filter = GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return; // 메쉬가 없으면 콜라이더도 못 만든다

            if (CountTriangles(mesh) > ConvexTriangleLimit)
            {
                UseBoxCollider(mesh);
                return;
            }

            var box = GetComponent<BoxCollider>();
            if (box != null) box.enabled = false;

            var collider = GetComponent<MeshCollider>();
            if (collider == null) collider = gameObject.AddComponent<MeshCollider>();

            collider.enabled = true;
            collider.sharedMesh = mesh;
            collider.convex = true;
        }

        private void UseBoxCollider(Mesh mesh)
        {
            var meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null) meshCollider.enabled = false;

            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();

            box.enabled = true;
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size;
        }

        /// <summary><c>mesh.triangles</c>는 배열을 새로 만든다 — 처치마다 도는 경로라 인덱스 수만 센다.</summary>
        private static int CountTriangles(Mesh mesh)
        {
            int total = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) total += (int)(mesh.GetIndexCount(i) / 3);
            return total;
        }

        /// <summary>
        /// 조각끼리의 충돌을 끈다. 계산이 크게 줄고, 조각 더미가 부들대는 지터도 사라진다 —
        /// 잔해는 바닥하고만 부딪히면 충분하다.
        /// </summary>
        private static void IgnoreSelfCollision(int layer)
        {
            if (ignoredSelfCollisionLayer == layer) return;

            Physics.IgnoreLayerCollision(layer, layer, true);
            ignoredSelfCollisionLayer = layer;
        }
    }
}
