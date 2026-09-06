using System;
using Unity.Cinemachine;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 샷 vcam 하나를 올리고 그 길이만큼 기다린다. <b>컷신 카메라 워크가 이것 하나로 성립한다.</b>
    ///
    /// <para><b>Timeline을 쓰지 않는 이유</b>: 이 컷이 요구하는 트랙이 카메라 하나뿐이다.
    /// 적은 풀에서 나와 트랙에 못 묶이고 플레이어는 제자리에 서 있으므로,
    /// <c>PlayableDirector</c> · 타임라인 에셋 · 트랙 바인딩은 배선 비용만 남고 이득이 없다.
    /// 카메라 이동을 만드는 것은 이미 Cinemachine 블렌드와 <see cref="CinemachineSplineDolly"/>이고,
    /// 후자를 미는 코드는 <c>CameraDirector.IntroRoutine</c>에 이미 있는 열 줄이다.
    /// <b>⚠ <see cref="TimelineStep"/>은 지우지 않는다</b> — 컷 안에서 카메라 말고 다른 것이
    /// 시각에 맞춰 움직여야 할 때 그때 쓴다.</para>
    ///
    /// <para><b>⚠ Cinemachine 타입이 여기서 네 번째 이음매로 등장한다.</b> CLAUDE.md §7-2는
    /// <c>ApplyShake</c> · <c>ApplyFraming</c> · <c>IntroRoutine</c> 셋으로 못박아 두었는데,
    /// 스텝은 상위 로직이 아니라 <b>저작 글루</b>라 그 규율의 대상이 아니라고 보고 직접 만진다.</para>
    ///
    /// <para><b>⚠ 블렌드 시간을 여기서 바꾸지 않는다.</b> 그 값의 주인은 <c>CinemachineBrain.DefaultBlend</c>이고
    /// <c>CameraDirector.angleBlendDuration</c>이 <c>Awake</c>에서 그것을 덮는다(§7-2 — 진실의 원천을 Brain 하나로 모으는 설계).
    /// 느린 전환이 필요하면 <b>vcam 자신의 돌리</b>로 만든다.</para>
    ///
    /// <para><b>주의: 클래스 이름과 네임스페이스를 바꾸지 않는다.</b> <c>[SerializeReference]</c>가
    /// 그 이름으로 참조를 저장하므로 옮기면 저작해 둔 시퀀스가 <c>Managed Reference missing</c>이 된다.</para>
    /// </summary>
    [Serializable]
    public class CameraShotStep : SequenceStep
    {
        [SequenceSlot]
        [Tooltip("올릴 샷 vcam 슬롯(CinemachineCamera).")]
        [SerializeField] private string cameraSlot;

        [Tooltip("샷이 도는 동안의 우선순위. 게임플레이(CameraAngleSwitcher의 activePriority 10)를 이기고 " +
                 "인트로(20)에는 져야 한다.")]
        [SerializeField] private int priority = 15;

        [Tooltip("이 샷의 길이(초). 0이면 즉시 끝난다 - 구도만 갈아끼우고 다음 스텝으로 넘어간다.")]
        [Min(0f)]
        [SerializeField] private float duration = 3f;

        [Tooltip("vcam에 CinemachineSplineDolly가 붙어 있으면 CameraPosition을 이 곡선으로 0→1까지 민다. " +
                 "돌리가 없으면 아무 일도 안 한다(구도만 유지).")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("끝나며 우선순위를 원래대로 되돌린다. 다음 샷이 이어서 이길 거면 끈다.")]
        [SerializeField] private bool restoreOnExit = true;

        [NonSerialized] private CinemachineCamera cam;
        [NonSerialized] private CinemachineSplineDolly dolly;
        [NonSerialized] private int restorePriority;

        public string CameraSlot => cameraSlot;

        public override void Enter(SequenceContext context)
        {
            cam = context.Bindings.Resolve<CinemachineCamera>(cameraSlot, context.Runner);
            dolly = null;

            if (cam == null)
            {
                Debug.LogError($"[CameraShotStep] 샷 vcam이 없습니다(slot: '{cameraSlot}'). 이 스텝을 건너뜁니다.",
                               context.Runner);
                return;
            }

            restorePriority = cam.Priority.Value;
            cam.Priority = new PrioritySettings { Enabled = true, Value = priority };

            dolly = cam.GetComponent<CinemachineSplineDolly>();
            if (dolly == null) return;

            // ⚠ PositionUnits = Normalized가 0→1 주행의 전제다(§7-2). 어긋나면 경로가 조용히 일부만 재생된다.
            if (dolly.PositionUnits != UnityEngine.Splines.PathIndexUnit.Normalized)
            {
                Debug.LogWarning($"[CameraShotStep] '{cam.name}'의 SplineDolly PositionUnits가 Normalized가 아닙니다 - " +
                                 "경로가 일부만 재생됩니다.", context.Runner);
            }

            dolly.CameraPosition = 0f;
        }

        public override void Tick(SequenceContext context)
        {
            if (dolly == null || duration <= 0f) return;

            dolly.CameraPosition = ease.Evaluate(Mathf.Clamp01(context.ElapsedInStep / duration));
        }

        public override bool IsFinished(SequenceContext context) => cam == null || context.ElapsedInStep >= duration;

        public override void Exit(SequenceContext context)
        {
            // 마지막 프레임이 1에 못 미쳐도 끝점에서 마무리한다(IntroRoutine과 같은 이유).
            if (dolly != null && duration > 0f) dolly.CameraPosition = ease.Evaluate(1f);

            if (cam != null && restoreOnExit)
                cam.Priority = new PrioritySettings { Enabled = true, Value = restorePriority };

            cam = null;
            dolly = null;
        }

        public override string Label => $"Shot '{cameraSlot}' {duration:0.##}s";
    }
}
