using UnityEngine;

namespace SliceSpace
{
    /// <summary>
    /// 파티클 시스템 재생이 완료되면 자동으로 해당 게임오브젝트를 파괴하거나 비활성화합니다.
    /// 풀링 시스템이 없는 단발성 이펙트의 메모리 누수 및 오버헤드를 방지합니다.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class AutoDestroyParticle : MonoBehaviour
    {
        [SerializeField] private bool destroyOnFinish = true;

        private ParticleSystem ps;

        private void Awake()
        {
            ps = GetComponent<ParticleSystem>();
        }

        private void Update()
        {
            if (ps != null && !ps.IsAlive(true))
            {
                if (destroyOnFinish)
                {
                    Destroy(gameObject);
                }
                else
                {
                    gameObject.SetActive(false);
                }
            }
        }
    }
}
