using System;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 지정한 입력이 실제로 들어올 때까지 기다린다. 튜토리얼의 <i>"눌러 봐"</i> 한 번이다.
    ///
    /// <para><b>⚠ 노드 입력은 <see cref="PatternHandler.OnNodeConnected"/>로 듣는다(<c>InputHandler</c>가 아니라).</b>
    /// 그 이벤트가 나는 <c>AppendPointToLine</c>은 <b>키보드와 마우스가 모두 지나가는 유일한 지점</b>이다 —
    /// 키보드도 <c>ForceDown</c> → <c>OnPointPressed</c>를 탄다. <c>InputHandler.OnKeyPressed</c>는 키보드 전용이라
    /// 그것만 들으면 <i>"마우스로도 됩니다"</i>라고 안내해 놓고 <b>마우스로는 영영 안 넘어간다.</b></para>
    ///
    /// <para>회피·이동·상호작용은 여전히 <see cref="InputHandler"/>가 유일한 출처다 — 키 바인딩은
    /// <c>IngameInputs.inputactions</c> 한 곳이 소유하므로 여기는 <c>UnityEngine.InputSystem</c>을 모른다.</para>
    ///
    /// <para><b>⚠ 입력 모드를 바꾸지 않는다.</b> 회피는 <c>Combat</c> 맵에서만, 이동·상호작용은
    /// <c>Explore</c> 맵에서만 발행되는데 모드의 주인은 <c>PlayerModeDirector</c>다(CLAUDE.md §11-2).
    /// 진입 시 모드가 안 맞으면 <b>경고를 찍고 그대로 기다린다</b> — 조용히 영원히 안 끝나는 것보다 낫다.</para>
    ///
    /// <para><b>타임아웃이 없다.</b> 튜토리얼은 "될 때까지"가 규칙이고(드릴이 이미 그렇다),
    /// 시간으로 넘겨 주면 못 배운 채 다음으로 간다.</para>
    ///
    /// <para><b>주의: 클래스 이름과 네임스페이스를 바꾸지 않는다.</b> <c>[SerializeReference]</c>가
    /// 그 이름으로 참조를 저장하므로 옮기면 저작해 둔 시퀀스가 <c>Managed Reference missing</c>이 된다.</para>
    /// </summary>
    [Serializable]
    public class WaitInputStep : SequenceStep
    {
        public enum InputKind
        {
            /// <summary>패턴인풋 노드. 숫자키와 마우스 드래그 <b>둘 다</b> 통과한다.</summary>
            Node,

            /// <summary>회피(Space).</summary>
            Dodge,

            /// <summary>탐색 이동(WASD).</summary>
            Move,

            /// <summary>상호작용(E).</summary>
            Interact,
        }

        [SerializeField] private InputKind kind = InputKind.Node;

        [Tooltip("기다릴 노드 인덱스(0~8). Point_1이 0이다 - 패턴 에셋 이름과 같은 인덱스 체계다.")]
        [Range(0, 8)]
        [SerializeField] private int nodeIndex;

        [Tooltip("켜면 아무 노드나 통과한다. 연타를 익힐 때 쓴다.")]
        [SerializeField] private bool anyNode;

        [Tooltip("이 횟수만큼 들어와야 통과한다.")]
        [Min(1)]
        [SerializeField] private int requiredCount = 1;

        [SequenceSlot]
        [Tooltip("노드 입력을 들을 PatternHandler 슬롯. kind = Node일 때만 쓴다.")]
        [SerializeField] private string patternHandlerSlot;

        [SequenceSlot]
        [Tooltip("회피·이동·상호작용을 들을 InputHandler 슬롯. kind = Node면 안 쓴다.")]
        [SerializeField] private string inputHandlerSlot;

        [Header("Move")]
        [Tooltip("이동 입력이 이 시간(초)만큼 이어져야 통과한다. 한 프레임 튀는 값으로 통과하지 않게 한다.")]
        [Min(0.05f)]
        [SerializeField] private float moveHoldDuration = 0.3f;

        [NonSerialized] private PatternHandler handler;
        [NonSerialized] private InputHandler input;
        [NonSerialized] private int count;
        [NonSerialized] private float moveHeld;
        [NonSerialized] private bool subscribed;

        public string PatternHandlerSlot => patternHandlerSlot;
        public string InputHandlerSlot => inputHandlerSlot;

        public override void Enter(SequenceContext context)
        {
            count = 0;
            moveHeld = 0f;
            subscribed = false;

            if (kind == InputKind.Node)
            {
                handler = context.Bindings.Resolve<PatternHandler>(patternHandlerSlot, context.Runner);
                if (handler == null) return;

                handler.OnNodeConnected += HandleNode;
                subscribed = true;
                return;
            }

            input = context.Bindings.Resolve<InputHandler>(inputHandlerSlot, context.Runner);
            if (input == null) return;

            PlayerInputMode required = kind == InputKind.Dodge
                ? PlayerInputMode.Combat
                : PlayerInputMode.Explore;

            if (input.Mode != required)
            {
                Debug.LogWarning($"[WaitInputStep] {kind}는 {required} 모드에서만 발행되는데 지금은 {input.Mode}입니다 - " +
                                 "이 스텝이 통과되지 않을 수 있습니다.", context.Runner);
            }

            switch (kind)
            {
                case InputKind.Dodge:
                    input.OnDodgePressed += HandleTriggered;
                    break;

                case InputKind.Interact:
                    input.OnInteractPressed += HandleTriggered;
                    break;
            }

            subscribed = true;
        }

        public override void Tick(SequenceContext context)
        {
            if (kind != InputKind.Move || input == null) return;

            // 이동만 폴링이다 - InputHandler가 상태로 노출한다(눌린 동안이 곧 상태라서).
            if (input.MoveInput.sqrMagnitude > 0.04f) moveHeld += Time.deltaTime;
            else moveHeld = 0f;

            if (moveHeld >= moveHoldDuration) count = requiredCount;
        }

        public override bool IsFinished(SequenceContext context)
        {
            // 배선이 없으면 막지 않는다 - 에러는 이미 Resolve가 찍었다.
            if (kind == InputKind.Node) return handler == null || count >= requiredCount;
            return input == null || count >= requiredCount;
        }

        public override void Exit(SequenceContext context)
        {
            if (subscribed)
            {
                if (handler != null) handler.OnNodeConnected -= HandleNode;

                if (input != null)
                {
                    switch (kind)
                    {
                        case InputKind.Dodge:
                            input.OnDodgePressed -= HandleTriggered;
                            break;

                        case InputKind.Interact:
                            input.OnInteractPressed -= HandleTriggered;
                            break;
                    }
                }
            }

            subscribed = false;
            handler = null;
            input = null;
        }

        public override string Label
        {
            get
            {
                string what = kind == InputKind.Node
                    ? (anyNode ? "Node any" : $"Node {nodeIndex}")
                    : kind.ToString();

                return requiredCount > 1 ? $"WaitInput {what} x{requiredCount}" : $"WaitInput {what}";
            }
        }

        private void HandleNode(int index, Vector3 world)
        {
            if (!anyNode && index != nodeIndex) return;
            count++;
        }

        private void HandleTriggered() => count++;
    }
}
