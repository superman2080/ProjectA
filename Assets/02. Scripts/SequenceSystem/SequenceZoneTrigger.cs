using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 플레이어가 이 자리를 지나면 시퀀스를 재생한다. 브리핑이 <b>화면이 아니라 자리</b>인 이유가 이것이다.
    ///
    /// <para><b>⚠ 그 장소를 처음 열 때만 재생된다.</b> 되돌아가서 또 들으면 대사가 환경음이 되고
    /// 태도 곡선이 죽는다. 그런데 <b>"들었다"를 기록하는 상태를 새로 만들지 않는다</b> —
    /// 시퀀스 자신의 <see cref="UnlockStep"/>이 세우는 플래그를 여기서 그대로 읽는다.
    /// 그래서 저작자는 플래그 이름 하나만 두 곳에 적으면 된다.</para>
    ///
    /// <para><b>⚠ 이 트리거가 재생하는 시퀀스는 <c>holdMode = Keep</c>이어야 한다</b> —
    /// 브리핑은 통로 옆에서 한 줄 하고 물러나는 것이고 이동을 멈추지 않는다.</para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SequenceZoneTrigger : MonoBehaviour
    {
        [SerializeField] private SequenceRunner runner;

        [Tooltip("이 플래그가 이미 서 있으면 재생하지 않는다. 보통 그 시퀀스의 UnlockStep이 세우는 플래그를 적는다.")]
        [SerializeField] private string onceFlag;

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerExploreMover>() == null) return;
            if (runner == null) return;

            // HasFlag는 빈 이름에 true를 돌려주므로(조건을 안 건 것과 같다) 그 경우는 1회 제한이 없다는 뜻이 된다.
            // 여기서는 반대여야 하므로 빈 이름을 "아직 안 섰다"로 읽는다.
            if (!string.IsNullOrEmpty(onceFlag) && GameProgress.HasFlag(onceFlag)) return;

            runner.Play();
        }

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;
        }
    }
}
