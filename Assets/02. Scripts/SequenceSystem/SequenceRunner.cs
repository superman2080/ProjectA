using System;
using System.Collections.Generic;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// <see cref="SequenceAsset"/>을 실행하고 씬 참조를 배선하는 유일 지점.
    /// 에셋(정의) · 러너(실행 + 씬 배선) · 스텝(로직)으로 셋이 갈린다.
    /// </summary>
    public class SequenceRunner : MonoBehaviour
    {
        // 대기 없는 스텝이 줄줄이 이어질 수 있어 한 프레임에 여러 스텝이 넘어간다.
        // 순환 참조나 즉시 완료 스텝의 무한 반복을 여기서 끊는다.
        private const int MaxStepsPerFrame = 64;

        [SerializeField] private SequenceAsset asset;
        [SerializeField] private SequenceBindings bindings = new();

        [Tooltip("슬롯을 비운 스텝의 기본 대상. 비면 플레이어를 옮기는 스텝이 동작하지 않는다.")]
        [SerializeField] private Transform player;

        [Tooltip("holdMode를 적용할 대상. 비면 모드를 건드리지 않는다(경고만).")]
        [SerializeField] private PlayerModeDirector playerModeDirector;

        [SerializeField] private bool playOnStart;

        /// <summary>시퀀스가 끝까지 재생됐을 때. <see cref="Stop"/>으로 중단하면 나지 않는다.</summary>
        public event Action OnFinished;

        public bool IsPlaying => runtimeSteps != null;

        /// <summary>현재 스텝에 진입한 뒤 지난 시간(초).</summary>
        public float ElapsedInStep { get; private set; }

        public SequenceAsset Asset => asset;
        public SequenceBindings Bindings => bindings;

        private List<SequenceStep> runtimeSteps;
        private int stepIndex;
        private SequenceContext context;

        private readonly HashSet<string> flags = new();

        private bool modeApplied;
        private PlayerMode modeBeforePlay;

        void Start()
        {
            if (playOnStart) Play();
        }

        void OnDisable()
        {
            // 복구 경로 셋 중 하나. 하나라도 빠지면 "시퀀스가 끝났는데 조작이 안 돌아온다"가 된다.
            RestorePresentation();
        }

        /// <summary>처음부터 재생한다. 이미 재생 중이면 중단하고 다시 시작한다.</summary>
        public void Play()
        {
            if (asset == null)
            {
                Debug.LogError("[SequenceRunner] SequenceAsset이 배선되지 않았습니다.", this);
                return;
            }

            if (IsPlaying) Stop();

            if (asset.Steps == null || asset.Steps.Count == 0)
            {
                Debug.LogWarning($"[SequenceRunner] '{asset.name}'에 스텝이 없습니다.", this);
                OnFinished?.Invoke();
                return;
            }

            // 에셋의 스텝을 그대로 돌리면 런타임 상태(경과 시간, 완료 플래그)가 에셋에 저장된다.
            runtimeSteps = new List<SequenceStep>(asset.Steps.Count);
            foreach (SequenceStep step in asset.Steps)
            {
                if (step == null) continue;
                runtimeSteps.Add(step.CreateRuntimeCopy());
            }

            if (runtimeSteps.Count == 0)
            {
                Debug.LogWarning($"[SequenceRunner] '{asset.name}'의 스텝이 전부 비어 있습니다.", this);
                runtimeSteps = null;
                OnFinished?.Invoke();
                return;
            }

            flags.Clear();
            context = new SequenceContext(this, bindings, player, playerModeDirector);

            ApplyMode();

            stepIndex = 0;
            EnterCurrentStep();
        }

        /// <summary>재생을 중단한다. <see cref="OnFinished"/>는 나지 않는다.</summary>
        public void Stop()
        {
            if (!IsPlaying)
            {
                RestorePresentation();
                return;
            }

            runtimeSteps[stepIndex].Exit(context);
            runtimeSteps = null;
            RestorePresentation();
        }

        /// <summary>씬 쪽(트리거 볼륨 등)이 <see cref="WaitFlagStep"/>을 통과시킬 때 부른다.</summary>
        public void SetFlag(string flagName)
        {
            if (string.IsNullOrEmpty(flagName)) return;
            flags.Add(flagName);
        }

        public void ClearFlag(string flagName)
        {
            if (string.IsNullOrEmpty(flagName)) return;
            flags.Remove(flagName);
        }

        public bool HasFlag(string flagName)
            => !string.IsNullOrEmpty(flagName) && flags.Contains(flagName);

        void Update()
        {
            if (!IsPlaying) return;

            ElapsedInStep += Time.deltaTime;

            int guard = 0;
            while (IsPlaying)
            {
                SequenceStep step = runtimeSteps[stepIndex];
                step.Tick(context);

                if (!step.IsFinished(context)) break;

                step.Exit(context);
                Advance();

                if (++guard < MaxStepsPerFrame) continue;

                Debug.LogError($"[SequenceRunner] 한 프레임에 스텝 {MaxStepsPerFrame}개를 넘겼습니다 - " +
                               "즉시 완료되는 스텝이 반복되고 있습니다. 재생을 중단합니다.", this);
                Stop();
                return;
            }
        }

        private void Advance()
        {
            stepIndex++;

            if (stepIndex < runtimeSteps.Count)
            {
                EnterCurrentStep();
                return;
            }

            runtimeSteps = null;
            RestorePresentation();
            OnFinished?.Invoke();
        }

        private void EnterCurrentStep()
        {
            ElapsedInStep = 0f;
            runtimeSteps[stepIndex].Enter(context);
        }

        private void ApplyMode()
        {
            if (asset.HoldMode == SequenceHoldMode.Keep) return;

            if (playerModeDirector == null)
            {
                Debug.LogWarning($"[SequenceRunner] '{asset.name}'의 holdMode가 {asset.HoldMode}인데 " +
                                 "PlayerModeDirector가 배선되지 않았습니다. 모드를 잡지 않고 진행합니다.", this);
                return;
            }

            modeBeforePlay = playerModeDirector.Mode;
            modeApplied = true;

            playerModeDirector.SetMode(asset.HoldMode == SequenceHoldMode.Overlay
                ? PlayerMode.Overlay
                : PlayerMode.Cutscene);
        }

        /// <summary>
        /// 재생 전 모드로 되돌리고, 스텝이 화면에 남긴 것을 걷어낸다. <b>멱등이다</b> —
        /// 종료 · <see cref="Stop"/> · <see cref="OnDisable"/> 세 경로에서 전부 불리므로
        /// 여러 번 불려도 안전해야 한다.
        /// </summary>
        private void RestorePresentation()
        {
            // ⚠ 강조(HighlightStep)는 스텝이 끄지 않으면 화면에 남는다 - 켜고 끄는 것이 서로 다른 스텝이라
            // 저작이 마지막 '끄기'를 빠뜨리거나 중단으로 거기에 닿지 못할 수 있다. 여기가 그 안전망이다.
            // 씬에 강조 장치가 없으면 아무 일도 안 한다.
            TutorialHighlightView.Instance?.Hide();

            if (!modeApplied) return;

            modeApplied = false;
            if (playerModeDirector != null) playerModeDirector.SetMode(modeBeforePlay);
        }
    }
}
