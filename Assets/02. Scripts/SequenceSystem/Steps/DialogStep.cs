using System;
using System.Collections.Generic;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 대사를 띄우고 다 넘어갈 때까지 기다린다.
    ///
    /// <para><b>⚠ <c>Time.timeScale</c>을 건드리지 않는다.</b> CLAUDE.md §7-3의 금지 사항이며,
    /// 멈춰 세우고 싶으면 <see cref="SequenceAsset.HoldMode"/>를 <c>Overlay</c>로 둔다.
    /// 예전 <c>DialogState</c>는 여기서 <c>timeScale = 0</c>을 걸었다.</para>
    /// </summary>
    [Serializable]
    public class DialogStep : SequenceStep
    {
        [SerializeField] private List<DialogData> lines = new();

        [NonSerialized] private bool finished;

        public override void Enter(SequenceContext context)
        {
            finished = false;

            if (lines == null || lines.Count == 0)
            {
                Debug.LogWarning("[DialogStep] 대사가 비어 있습니다. 이 스텝을 건너뜁니다.", context.Runner);
                finished = true;
                return;
            }

            DialogUI dialogUI = DialogUI.Instance;

            if (dialogUI == null)
            {
                Debug.LogError("[DialogStep] 씬에 DialogUI가 없습니다. 이 스텝을 건너뜁니다.", context.Runner);
                finished = true;
                return;
            }

            // 완료 통보를 인자로 받는다. Action 필드에 += 하던 예전 방식은 구독이 안 풀려
            // 대사 상태가 둘 이상 붙으면 서로의 완료를 같이 받았다.
            dialogUI.SetDialog(lines, () => finished = true);
        }

        public override bool IsFinished(SequenceContext context) => finished;

        public override string Label => lines != null && lines.Count > 0
            ? $"Dialog x{lines.Count} \"{Preview()}\""
            : "Dialog (empty)";

        private string Preview()
        {
            string script = lines[0].script ?? string.Empty;
            return script.Length <= 16 ? script : script.Substring(0, 16) + "...";
        }
    }
}
