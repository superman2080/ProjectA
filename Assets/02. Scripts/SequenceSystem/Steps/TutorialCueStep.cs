using System;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// <see cref="TutorialDirector"/>의 상태 하나를 바꾼다.
    ///
    /// <para><b>기다리지 않는다 — <see cref="IsFinished"/>가 언제나 true다</b>(<see cref="HighlightStep"/>과 같은 관용구).
    /// 상태 변경과 대기를 나눠 두면 <i>"달리게 해 둔 채로 대사를 넘긴다"</i>가 새 필드 없이 성립한다.</para>
    ///
    /// <para><b>주의: 클래스 이름과 네임스페이스를 바꾸지 않는다.</b> <c>[SerializeReference]</c>가
    /// 그 이름으로 참조를 저장하므로 옮기면 저작해 둔 시퀀스가 <c>Managed Reference missing</c>이 된다.</para>
    /// </summary>
    [Serializable]
    public class TutorialCueStep : SequenceStep
    {
        public enum Action
        {
            /// <summary>허물을 세운다(프리웜 포함). <b>전투가 아니라 대사 구간에서 부른다.</b></summary>
            PrepareStage,

            /// <summary>제자리 달리기 시작(위치는 안 움직인다).</summary>
            RunOn,


            /// <summary>제자리 달리기 정지.</summary>
            RunOff,

            /// <summary>다음 판정 대상의 첫 노드에서 한 번 감속한다.</summary>
            ArmSlowMo,

            /// <summary>
            /// 실제로 목적지까지 달려간다. <b>이 Action만 기다린다</b> —
            /// 도착이 곧 다음 구도의 시작이라 여기서 넘겨 버리면 카메라가 빈 자리를 찍는다.
            ///
            /// <para><b>⚠ 새 값은 반드시 enum 끝에 붙인다.</b> 명시 정수가 없어 <b>서수가 곧 직렬화 키</b>라,
            /// 중간에 끼우면 저작해 둔 시퀀스의 뒤쪽 값이 통째로 한 칸씩 밀린다
            /// (실제로 이 값을 중간에 넣었다가 <c>RunOff</c>가 <c>RunTo</c>로 읽혔다).</para>
            /// </summary>
            RunTo,

            /// <summary>
            /// 다음 기습 하나를 무장한다(<c>DodgeDirector.requireArm</c>가 켜져 있을 때만 뜻이 있다).
            /// 무장은 <b>실제로 발동할 때까지</b> 남으므로 드릴 바로 앞에 한 번만 두면 된다.
            ///
            /// <para><b>⚠ 위 RunTo 주석의 규칙이 여기에도 걸린다 — 새 값은 enum 끝에 붙인다.</b></para>
            /// </summary>
            ArmAmbush,

            /// <summary>
            /// 다음 성공 패턴을 곡의 마지막처럼 취급해 마무리 실루엣을 터뜨리게 무장한다.
            /// <b>마지막 드릴 바로 앞</b>에 둔다 — 그 위치가 곧 <c>Time.timeScale</c>을 쓰는 안전 근거다
            /// (뒤에 판정할 드릴이 없다).
            ///
            /// <para><b>⚠ 위 RunTo 주석의 규칙이 여기에도 걸린다 — 새 값은 enum 끝에 붙인다.</b></para>
            /// </summary>
            ArmFinale,
        }

        [SerializeField] private Action action = Action.RunOn;

        [Tooltip("PrepareStage의 허물 인원. 다른 Action에서는 쓰지 않는다.")]
        [Min(1)]
        [SerializeField] private int value = 4;

        [SequenceSlot]
        [Tooltip("지시를 받을 TutorialDirector 슬롯.")]
        [SerializeField] private string tutorialDirectorSlot;

        public string TutorialDirectorSlot => tutorialDirectorSlot;

        [NonSerialized] private TutorialDirector resolved;

        public override void Enter(SequenceContext context)
        {
            TutorialDirector director = context.Bindings.Resolve<TutorialDirector>(tutorialDirectorSlot, context.Runner);
            resolved = director;

            if (director == null)
            {
                Debug.LogError($"[TutorialCueStep] TutorialDirector가 없습니다(slot: '{tutorialDirectorSlot}'). " +
                               "이 스텝을 건너뜁니다.", context.Runner);
                return;
            }

            switch (action)
            {
                case Action.PrepareStage:
                    director.PrepareStage(value);
                    break;

                case Action.RunOn:
                    director.SetRunning(true);
                    break;

                case Action.RunTo:
                    director.RunTo();
                    break;

                case Action.RunOff:
                    director.SetRunning(false);
                    break;

                case Action.ArmSlowMo:
                    director.ArmSlowMo();
                    break;

                case Action.ArmAmbush:
                    director.ArmAmbush();
                    break;

                case Action.ArmFinale:
                    director.ArmFinale();
                    break;
            }
        }

        // RunTo만 기다린다. 나머지는 HighlightStep과 같이 상태만 바꾸고 즉시 넘어간다.
        public override bool IsFinished(SequenceContext context)
            => action != Action.RunTo || resolved == null || resolved.RunArrived;

        public override void Exit(SequenceContext context) => resolved = null;

        public override string Label => action == Action.PrepareStage
            ? $"Tutorial PrepareStage x{value}"
            : $"Tutorial {action}";
    }
}
