using System;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 대상을 목적지 좌표로 걸어가게 한다.
    ///
    /// <para><b>목적지는 씬 참조가 아니라 좌표다.</b> 시퀀스가 씬 전용이고 무대 중심이 월드 원점으로
    /// 고정이므로(CLAUDE.md §11-2) 그 씬의 그 자리는 영원히 같은 값이다.
    /// 씬 뷰에서 핸들로 잡는 저작은 <c>SequenceRunnerEditor</c>가 담당한다.</para>
    ///
    /// <para><b>물리를 쓰지 않는다</b> — <c>transform.position</c> 직접 대입. 플레이어·적 이동이
    /// 전부 그렇고(§11-2), 무대 원 안에는 콜라이더를 두지 않는다.</para>
    /// </summary>
    [Serializable]
    public class MoveToStep : SequenceStep
    {
        [SequenceSlot]
        [Tooltip("옮길 대상 슬롯. 비우면 플레이어.")]
        [SerializeField] private string actorSlot;

        [SerializeField] private Vector3 destination;

        [Min(0.01f)]
        [SerializeField] private float speed = 3f;

        [Tooltip("이 거리 안에 들면 도착으로 본다.")]
        [Min(0.01f)]
        [SerializeField] private float arriveRadius = 0.15f;

        [Tooltip("가는 쪽으로 돌린다. 끄면 회전을 건드리지 않는다.")]
        [SerializeField] private bool faceMoveDirection = true;

        [Tooltip("도는 데 걸리는 시간(초).")]
        [Min(0.01f)]
        [SerializeField] private float turnDuration = 0.12f;

        [NonSerialized] private Transform actor;
        [NonSerialized] private bool resolved;

        public string ActorSlot => actorSlot;
        public Vector3 Destination => destination;

        /// <summary>씬 뷰 핸들 저작용. 에디터에서만 부른다.</summary>
        public void SetDestination(Vector3 value) => destination = value;

        public override void Enter(SequenceContext context)
        {
            actor = string.IsNullOrEmpty(actorSlot)
                ? context.Player
                : context.Bindings.ResolveTransform(actorSlot, context.Runner);

            resolved = actor != null;

            if (!resolved)
            {
                // 대상이 없으면 즉시 완료로 처리해 시퀀스가 멎지 않게 한다.
                Debug.LogError($"[MoveToStep] 옮길 대상을 찾지 못했습니다 " +
                               $"(슬롯: '{(string.IsNullOrEmpty(actorSlot) ? "플레이어" : actorSlot)}'). " +
                               "이 스텝을 건너뜁니다.", context.Runner);
            }
        }

        public override void Tick(SequenceContext context)
        {
            if (!resolved) return;

            Vector3 toTarget = destination - actor.position;
            float distance = toTarget.magnitude;
            if (distance <= Mathf.Epsilon) return;

            Vector3 direction = toTarget / distance;
            float travel = Mathf.Min(speed * Time.deltaTime, distance);
            actor.position += direction * travel;

            if (!faceMoveDirection) return;

            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude <= 1e-6f) return;

            Quaternion target = Quaternion.LookRotation(flat);
            float t = Mathf.Clamp01(Time.deltaTime / turnDuration);
            actor.rotation = Quaternion.Slerp(actor.rotation, target, t);
        }

        public override bool IsFinished(SequenceContext context)
        {
            if (!resolved) return true;
            return (destination - actor.position).sqrMagnitude <= arriveRadius * arriveRadius;
        }

        public override string Label
            => $"MoveTo {(string.IsNullOrEmpty(actorSlot) ? "Player" : actorSlot)} -> {destination}";
    }
}
