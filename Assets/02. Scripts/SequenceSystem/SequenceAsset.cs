using System;
using System.Collections.Generic;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 시퀀스가 도는 동안 플레이어를 어느 모드에 붙잡아 둘지.
    ///
    /// <para><b>⚠ 이 필드가 <c>Time.timeScale</c>을 대신한다.</b> CLAUDE.md §7-3이 timeScale을 금지한다 —
    /// 판정·클립 정렬은 <c>Time.time</c>인데 채보는 <c>audioSource.time</c>으로 돌고 오디오는 timeScale 밖이라
    /// 차이가 영구 누적된다. 대사를 위해 시간을 멈출 것이 아니라 <b>조작과 위치의 주인을 정리</b>하면 된다.</para>
    /// </summary>
    public enum SequenceHoldMode
    {
        /// <summary>모드를 안 건드린다. 걸으면서 듣는 대사(카시마 브리핑).</summary>
        Keep,

        /// <summary>이동·입력 정지. 위치의 주인이 없다. 멈춰 서서 보는 대사.</summary>
        Overlay,

        /// <summary>위치·카메라를 Timeline이 몬다. 컷신.</summary>
        Cutscene,
    }

    /// <summary>
    /// 시퀀스 하나의 정의. <b>씬 참조가 0개다</b> — 그래서 씬을 열지 않고 편집·비교할 수 있고
    /// 씬 diff를 오염시키지 않는다.
    ///
    /// <para><b>러너와 1:1이다</b>(씬 전용 일회성). 여러 씬이 공유하는 템플릿이 아니므로
    /// 위치를 <c>Vector3</c>로 그냥 박는다 — 그 씬의 그 자리는 영원히 같은 값이다.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Sequence/Sequence Asset", fileName = "Seq_New")]
    public class SequenceAsset : ScriptableObject
    {
        [Tooltip("이 시퀀스가 도는 동안 플레이어를 붙잡아 둘 모드. Keep이면 건드리지 않는다.")]
        [SerializeField] private SequenceHoldMode holdMode = SequenceHoldMode.Keep;

        [Tooltip("이 시퀀스가 필요로 하는 씬 오브젝트 슬롯 이름. 러너 인스펙터가 이 목록마다 배선 칸을 그린다.")]
        [SerializeField] private string[] requiredBindings = Array.Empty<string>();

        [SerializeReference]
        [Tooltip("순서대로 실행된다. + 버튼으로 스텝 종류를 골라 추가한다.")]
        private List<SequenceStep> steps = new();

        public SequenceHoldMode HoldMode => holdMode;
        public string[] RequiredBindings => requiredBindings;
        public IReadOnlyList<SequenceStep> Steps => steps;

        private void OnValidate()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] == null)
                {
                    Debug.LogWarning($"[{name}] 스텝 {i}가 비어 있습니다.", this);
                    continue;
                }

                // 플레이어를 옮기는데 모드를 안 잡으면, 같은 프레임에 PlayerExploreMover나
                // PlayerCombatMover가 transform.position을 덮어써 이동이 씹힌다(CLAUDE.md §11-2).
                if (steps[i] is MoveToStep move
                    && string.IsNullOrEmpty(move.ActorSlot)
                    && holdMode == SequenceHoldMode.Keep)
                {
                    Debug.LogWarning($"[{name}] 스텝 {i}({move.Label})가 플레이어를 옮기는데 holdMode가 Keep입니다. " +
                                     "이동을 담당하는 무버가 같은 프레임에 위치를 덮어씁니다 - " +
                                     "Overlay나 Cutscene으로 두세요.", this);
                }
            }

            WarnUnknownSlots();
        }

        /// <summary>스텝이 가리키는 슬롯이 선언 목록에 없으면 저작 시점에 잡는다.</summary>
        private void WarnUnknownSlots()
        {
            foreach (SequenceStep step in steps)
            {
                if (step == null) continue;

                // 스텝 하나가 슬롯을 여럿 가리킬 수 있다(PatternDrillStep은 셋).
                switch (step)
                {
                    case MoveToStep move:
                        WarnIfUndeclared(step, move.ActorSlot);
                        break;

                    case TimelineStep timeline:
                        WarnIfUndeclared(step, timeline.DirectorSlot);
                        break;

                    case PatternDrillStep drill:
                        WarnIfUndeclared(step, drill.PatternHandlerSlot);
                        WarnIfUndeclared(step, drill.TargetDirectorSlot);
                        WarnIfUndeclared(step, drill.TargetAnchorSlot);
                        break;

                    case HighlightStep highlight:
                        WarnIfUndeclared(step, highlight.TargetSlot);
                        break;

                    case WaitInputStep waitInput:
                        WarnIfUndeclared(step, waitInput.PatternHandlerSlot);
                        WarnIfUndeclared(step, waitInput.InputHandlerSlot);
                        break;
                }
            }
        }

        private void WarnIfUndeclared(SequenceStep step, string slot)
        {
            if (string.IsNullOrEmpty(slot)) return;
            if (Array.IndexOf(requiredBindings, slot) >= 0) return;

            Debug.LogWarning($"[{name}] 스텝 {step.Label}이 선언되지 않은 슬롯 '{slot}'을 가리킵니다. " +
                             "Required Bindings에 추가하세요.", this);
        }
    }
}
