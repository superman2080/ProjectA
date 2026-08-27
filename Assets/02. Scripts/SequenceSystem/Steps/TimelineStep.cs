using System;
using UnityEngine;
using UnityEngine.Playables;

namespace SequenceSpace
{
    /// <summary>
    /// Timeline 한 편을 재생하고 끝날 때까지 기다린다. <b>시퀀스가 위, Timeline이 아래다</b> —
    /// 시퀀스가 Timeline을 부르고 Timeline은 시퀀스를 부르지 않는다. 양방향이면 누가 주인인지 모호해진다.
    ///
    /// <para><b>Timeline의 씬 참조는 Timeline이 알아서 한다.</b> <c>PlayableDirector</c>가 트랙별 바인딩을
    /// 씬에 들고 있으므로(우리 <see cref="SequenceBindings"/>와 같은 해법) 우리는 "어느 director인가"만 배선한다.</para>
    ///
    /// <para><b>컷신이면 <see cref="SequenceAsset.HoldMode"/>를 <c>Cutscene</c>으로 둔다</b> —
    /// <c>PlayerMode.Cutscene</c>의 정의가 이미 "아무도 위치를 안 건드린다(카메라·타임라인이 몬다)"다.</para>
    ///
    /// <para><b>⚠ 타입이 <c>TimelineAsset</c>이 아니라 <c>PlayableAsset</c>이다.</b> 후자는 엔진 코어라
    /// Timeline 패키지에 대한 어셈블리 의존이 생기지 않는다. 인스펙터에서는 TimelineAsset을 그대로 끌어다 놓으면 된다.</para>
    /// </summary>
    [Serializable]
    public class TimelineStep : SequenceStep
    {
        [SerializeField] private PlayableAsset timeline;

        [SequenceSlot]
        [Tooltip("재생을 맡길 PlayableDirector 슬롯.")]
        [SerializeField] private string directorSlot;

        [NonSerialized] private PlayableDirector director;
        [NonSerialized] private bool stopped;
        [NonSerialized] private bool started;

        public string DirectorSlot => directorSlot;

        public override void Enter(SequenceContext context)
        {
            stopped = false;
            started = false;

            director = context.Bindings.Resolve<PlayableDirector>(directorSlot, context.Runner);

            if (director == null || timeline == null)
            {
                Debug.LogError($"[TimelineStep] 재생할 수 없습니다 " +
                               $"(director: {(director == null ? "없음" : "있음")}, " +
                               $"timeline: {(timeline == null ? "없음" : "있음")}). 이 스텝을 건너뜁니다.",
                               context.Runner);
                stopped = true;
                return;
            }

            director.playableAsset = timeline;

            // None이 아니면 끝나도 director가 계속 Playing이라 stopped 이벤트가 안 나고
            // 이 스텝이 영원히 안 끝난다. 저작에 맡기지 않고 여기서 못박는다.
            director.extrapolationMode = DirectorWrapMode.None;

            director.stopped += HandleStopped;
            director.time = 0d;
            director.Play();
        }

        public override void Tick(SequenceContext context)
        {
            // Play() 직후 한 프레임은 상태 폴링을 믿지 않는다.
            started = true;
        }

        public override bool IsFinished(SequenceContext context)
        {
            if (stopped) return true;
            if (director == null) return true;

            // stopped 이벤트를 주 경로로 쓰고 상태 폴링을 폴백으로 둔다.
            return started && director.state != PlayState.Playing;
        }

        public override void Exit(SequenceContext context)
        {
            if (director == null) return;

            director.stopped -= HandleStopped;

            if (director.state == PlayState.Playing) director.Stop();

            director = null;
        }

        private void HandleStopped(PlayableDirector _) => stopped = true;

        public override string Label => $"Timeline '{(timeline != null ? timeline.name : "none")}'";
    }
}
