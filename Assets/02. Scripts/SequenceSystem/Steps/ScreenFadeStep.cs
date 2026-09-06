using System;
using System.Collections;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 화면을 검게 덮거나(<see cref="Mode.Out"/>) 걷어낸다(<see cref="Mode.In"/>).
    /// 페이드가 끝날 때까지 <b>기다린다</b> — 그 대기가 곧 "검은 화면 사이에서만 무대를 바꾼다"의 보장이다.
    ///
    /// <para><b><see cref="ScreenFader"/>는 싱글톤이라 슬롯이 필요 없다</b>(<c>Managers</c> 안에 살며
    /// 씬을 넘어간다, CLAUDE.md §10). 그래서 이 스텝은 <c>requiredBindings</c>를 안 늘린다 —
    /// <see cref="tutorialDirectorSlot"/>은 <see cref="waitForFinale"/>일 때만 쓰이고 그 슬롯도 이미 있다.</para>
    ///
    /// <para><b>⚠ 자기 타이머를 들지 않는다.</b> <see cref="ScreenFader"/>는 <c>Time.unscaledDeltaTime</c>으로
    /// 보간하는데 러너의 <c>ElapsedInStep</c>은 <b>스케일된 시계</b>다 — 마무리 실루엣이
    /// <c>Time.timeScale = 0.1</c>을 걸고 있어 둘을 섞으면 저작값이 화면과 어긋난다.
    /// 완료 판정은 코루틴이 끝났는가 하나뿐이다.</para>
    ///
    /// <para><b>주의: 클래스 이름과 네임스페이스를 바꾸지 않는다.</b> <c>[SerializeReference]</c>가
    /// 그 이름으로 참조를 저장하므로 옮기면 저작해 둔 시퀀스가 <c>Managed Reference missing</c>이 된다.</para>
    /// </summary>
    [Serializable]
    public class ScreenFadeStep : SequenceStep
    {
        public enum Mode
        {
            /// <summary>검게 덮는다.</summary>
            Out,

            /// <summary>걷어낸다.</summary>
            In,
        }

        [Tooltip("덮을 것인가 걷어낼 것인가.")]
        [SerializeField] private Mode mode = Mode.Out;

        [Tooltip("마무리 실루엣이 걷힐 때까지 기다렸다가 페이드를 시작한다.\n" +
                 "패턴 완료는 칼이 닿는 순간(ImpactTime)보다 이르므로, 이걸 안 켜면 " +
                 "실루엣이 검은 화면에 덮여 한 프레임도 안 보인다.")]
        [SerializeField] private bool waitForFinale;

        [SequenceSlot]
        [Tooltip("waitForFinale일 때 OnFinaleEnded를 받아 올 TutorialDirector 슬롯.")]
        [SerializeField] private string tutorialDirectorSlot;

        [Tooltip("실루엣을 기다리는 최대 시간(초, 실시간).\n" +
                 "⚠ 0으로 두면 안 된다 — 연출 토글이 꺼져 있거나 Silhouette 레이어가 없으면 " +
                 "OnFinaleEnded가 영영 안 와서 시퀀스가 통째로 멈춘다.")]
        [Min(0.1f)]
        [SerializeField] private float finaleTimeout = 3f;

        public string TutorialDirectorSlot => tutorialDirectorSlot;

        [NonSerialized] private TutorialDirector director;
        [NonSerialized] private FinaleSilhouetteDirector finale;
        [NonSerialized] private bool waiting;
        [NonSerialized] private bool finaleEnded;
        [NonSerialized] private float waitDeadline;
        [NonSerialized] private bool started;
        [NonSerialized] private bool done;
        [NonSerialized] private Coroutine routine;

        public override void Enter(SequenceContext context)
        {
            waiting = false;
            finaleEnded = false;
            started = false;
            done = false;
            routine = null;
            director = null;
            finale = null;

            if (waitForFinale && !string.IsNullOrEmpty(tutorialDirectorSlot))
            {
                director = context.Bindings.Resolve<TutorialDirector>(tutorialDirectorSlot, context.Runner);
                finale = director != null ? director.Finale : null;
            }

            if (finale != null)
            {
                finale.OnFinaleEnded += HandleFinaleEnded;
                waiting = true;

                // ⚠ 실시간으로 잰다 — 기다리는 동안에는 아직 슬로우모션이 걸려 있다.
                waitDeadline = Time.unscaledTime + finaleTimeout;
                return;
            }

            Begin(context);
        }

        public override void Tick(SequenceContext context)
        {
            if (!waiting) return;

            // 실루엣이 아예 안 뜨는 배선(연출 토글 off · 레이어 없음)에서도 반드시 진행한다.
            if (!finaleEnded && Time.unscaledTime < waitDeadline) return;

            StopWaiting();
            Begin(context);
        }

        public override bool IsFinished(SequenceContext context) => done;

        public override void Exit(SequenceContext context)
        {
            StopWaiting();

            if (routine != null && context.Owner != null) context.Owner.StopCoroutine(routine);
            routine = null;
            director = null;
        }

        // 이벤트는 context를 못 받으므로 플래그만 세우고 실제 시작은 다음 Tick이 한다.
        private void HandleFinaleEnded() => finaleEnded = true;

        private void StopWaiting()
        {
            if (finale != null) finale.OnFinaleEnded -= HandleFinaleEnded;
            finale = null;
            waiting = false;
        }

        private void Begin(SequenceContext context)
        {
            if (started) return;
            started = true;

            ScreenFader fader = ScreenFader.Instance;

            if (fader == null || context.Owner == null)
            {
                Debug.LogError("[ScreenFadeStep] ScreenFader를 찾지 못했습니다. 이 스텝을 건너뜁니다.",
                               context.Runner);
                done = true;
                return;
            }

            routine = context.Owner.StartCoroutine(Run(fader));
        }

        private IEnumerator Run(ScreenFader fader)
        {
            yield return mode == Mode.Out ? fader.FadeOut() : fader.FadeIn();

            routine = null;
            done = true;
        }

        public override string Label => mode == Mode.Out ? "FadeOut" : "FadeIn";
    }
}
