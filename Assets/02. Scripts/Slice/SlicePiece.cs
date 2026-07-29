using UnityEngine;

namespace SliceSpace
{
    /// <summary>
    /// 절단 조각 하나의 운동. <b>물리 엔진을 쓰지 않고</b> 경과 시간 t로 위치·회전을 <b>닫힌 식</b>으로 계산한다
    /// (적분 누적이 아니므로 프레임률이 흔들려도 궤적이 같다).
    ///
    /// <para>좌표는 전부 <b>부모(SliceTargetView) 로컬</b>이다. 부모가 계속 −Z로 등속 이동하므로
    /// 조각은 로컬 XY로만 흩어지면 되고, "Z 속도는 유지한다"는 규칙이 구조로 표현된다.</para>
    /// </summary>
    public class SlicePiece : MonoBehaviour
    {
        private Vector3 startLocalPos;
        private Quaternion startLocalRot;
        private Vector3 velocity;
        private Vector3 gravity;
        private Vector3 spinAxis;
        private float spinSpeed;
        private float elapsed;
        private bool scattering;

        /// <summary>절단 직전 실루엣 위치에 배치한다(아직 흩어지지 않음).</summary>
        public void Place(Transform parent, Vector3 localPosition)
        {
            transform.SetParent(parent, false);
            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.identity;

            startLocalPos = localPosition;
            startLocalRot = Quaternion.identity;
            elapsed = 0f;
            scattering = false;
            gameObject.SetActive(true);
        }

        /// <summary>흩어짐 시작. <paramref name="direction"/>은 부모 로컬 기준(보통 XY 평면 위).</summary>
        public void Scatter(Vector3 direction, float speed, Vector3 gravityAccel, Vector3 axis, float degreesPerSecond)
        {
            velocity = direction * speed;
            gravity = gravityAccel;
            spinAxis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
            spinSpeed = degreesPerSecond;
            elapsed = 0f;
            scattering = true;
        }

        void Update()
        {
            if (!scattering) return;

            elapsed += Time.deltaTime;
            float t = elapsed;

            transform.localPosition = startLocalPos + velocity * t + 0.5f * t * t * gravity;
            transform.localRotation = startLocalRot * Quaternion.AngleAxis(spinSpeed * t, spinAxis);
        }

        /// <summary>풀 반납 전 상태 초기화. 여기 한 곳에 모아 두어야 재사용 시 이전 궤적이 새지 않는다.</summary>
        public void ResetState()
        {
            scattering = false;
            elapsed = 0f;
            velocity = Vector3.zero;
            gravity = Vector3.zero;
            spinSpeed = 0f;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            gameObject.SetActive(false);
        }
    }
}
