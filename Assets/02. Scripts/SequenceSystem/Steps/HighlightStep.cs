using System;
using PatternSpace;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 화면의 한 자리를 강조하고 한 줄로 설명한다. <b>대사가 아니라 조작 안내다</b> —
    /// 조작은 설명해도 세계는 설명하지 않는다(<c>Story_Overview.md</c> §0).
    ///
    /// <para><b>기다리지 않는다 — <see cref="IsFinished"/>가 언제나 true다.</b> 강조는 상태 변경이고,
    /// 대기는 다음 스텝(<see cref="WaitInputStep"/> · <see cref="DialogStep"/> · <see cref="PatternDrillStep"/>)이 한다.
    /// 그 분리 덕에 <i>"강조를 켠 채로 드릴을 시킨다"</i>가 새 필드 없이 성립한다.</para>
    ///
    /// <para><b>끄는 것도 이 스텝이다</b> — <see cref="TargetSlot"/>과 캡션을 모두 비우면 강조가 내려간다.
    /// 별도의 Hide 스텝을 만들지 않는다.</para>
    ///
    /// <para><b>주의: 클래스 이름과 네임스페이스를 바꾸지 않는다.</b> <c>[SerializeReference]</c>가
    /// 그 이름으로 참조를 저장하므로 옮기면 저작해 둔 시퀀스가 <c>Managed Reference missing</c>이 된다.</para>
    /// </summary>
    [Serializable]
    public class HighlightStep : SequenceStep
    {
        [SequenceSlot]
        [Tooltip("강조할 UI 슬롯(RectTransform). 비우고 캡션도 비우면 강조를 끈다.")]
        [SerializeField] private string targetSlot;

        [TextArea(2, 4)]
        [Tooltip("화면 위쪽에 뜨는 한 줄. 조작만 설명하고 세계는 설명하지 않는다.")]
        [SerializeField] private string caption;

        [Tooltip("링이 대상보다 이만큼 크게 그려진다(픽셀).")]
        [Min(0f)]
        [SerializeField] private float padding = 24f;

        [Tooltip("대상이 Point면 그 노브를 같이 켠다. 패턴이 없는 구간에서는 노브가 감춰져 있기 때문.")]
        [SerializeField] private bool showKnob = true;

        public string TargetSlot => targetSlot;

        public override void Enter(SequenceContext context)
        {
            TutorialHighlightView view = TutorialHighlightView.Instance;

            if (view == null)
            {
                Debug.LogError("[HighlightStep] 씬에 TutorialHighlightView가 없습니다. 이 스텝을 건너뜁니다.",
                               context.Runner);
                return;
            }

            if (string.IsNullOrEmpty(targetSlot) && string.IsNullOrEmpty(caption))
            {
                view.Hide();
                return;
            }

            RectTransform target = string.IsNullOrEmpty(targetSlot)
                ? null
                : context.Bindings.Resolve<RectTransform>(targetSlot, context.Runner);

            // ⚠ PatternHandler는 살아 있는 패턴이 쓰는 Point의 노브만 보이게 한다(CLAUDE.md §1).
            // 드릴 전에 Point를 강조하면 빈 자리에 링만 뜨므로 여기서 켠다.
            // 되돌릴 필요가 없다 — 다음 SetPattern이 합집합을 다시 계산해 원래 규칙으로 복귀한다.
            if (showKnob && target != null)
            {
                Point point = target.GetComponentInParent<Point>();
                if (point != null) point.SetKnobVisible(true, 0f);
            }

            view.Show(target, caption, padding);
        }

        // Exit에서 끄지 않는다 — 끄면 다음 스텝(드릴·키 입력) 동안 강조가 유지되지 않는다.

        public override bool IsFinished(SequenceContext context) => true;

        public override string Label
        {
            get
            {
                if (!string.IsNullOrEmpty(targetSlot)) return $"Highlight '{targetSlot}'";
                return string.IsNullOrEmpty(caption) ? "Highlight (off)" : "Highlight (caption)";
            }
        }
    }
}
