using NUnit.Framework;
using PatternSpace;
using UnityEngine;

namespace PatternSpace.Tests
{
    /// <summary>
    /// <see cref="PatternEffectCue.ResolveTime"/>은 <b>시각 계산의 유일한 소유자</b>다 —
    /// 런타임 발사와 툴의 타임라인·경고가 같은 함수를 본다. 여기가 깨지면 저장 화면에서 맞춘 값이
    /// 게임에서 어긋난다.
    /// </summary>
    public class PatternEffectCueTests
    {
        // 실제 채보에서도 불변인 순서: start < first < ... < last < deadline
        private const float Start = 10f;
        private const float First = 11f;
        private const float Last = 13f;
        private const float Deadline = 13.1f;
        private static readonly float[] NodeTimes = { 11f, 12f, 13f };

        private static PatternEffectCue Make(EffectTiming timing, float offset = 0f,
            int nodeIndex = 0, EffectCondition condition = EffectCondition.Always)
        {
            var cue = new PatternEffectCue();

            var so = new SerializedLikeSetter(cue);
            so.Set("timing", timing);
            so.Set("timeOffset", offset);
            so.Set("nodeIndex", nodeIndex);
            so.Set("condition", condition);

            return cue;
        }

        private static float Resolve(PatternEffectCue cue, float impactOffset = 0f) =>
            cue.ResolveTime(Start, First, Last, Deadline, NodeTimes, impactOffset);

        [Test]
        public void PatternStartUsesQueueTime()
        {
            Assert.AreEqual(Start, Resolve(Make(EffectTiming.PatternStart)), 1e-4f);
        }

        [Test]
        public void FirstNodeUsesFirstNodeTime()
        {
            Assert.AreEqual(First, Resolve(Make(EffectTiming.FirstNode)), 1e-4f);
        }

        [Test]
        public void LastNodeUsesLastNodeTime()
        {
            Assert.AreEqual(Last, Resolve(Make(EffectTiming.LastNode)), 1e-4f);
        }

        [Test]
        public void ImpactIsDeadlinePlusPatternImpactOffset()
        {
            // 임팩트만 패턴의 공통 앵커 보정을 싣는다 — 칼·적·카메라와 같은 식이어야 하기 때문.
            Assert.AreEqual(Deadline + 0.05f, Resolve(Make(EffectTiming.Impact), 0.05f), 1e-4f);
        }

        [Test]
        public void NodePicksTheRequestedNode()
        {
            Assert.AreEqual(NodeTimes[1], Resolve(Make(EffectTiming.Node, nodeIndex: 1)), 1e-4f);
        }

        [Test]
        public void NodeIndexIsClampedIntoRange()
        {
            Assert.AreEqual(NodeTimes[NodeTimes.Length - 1], Resolve(Make(EffectTiming.Node, nodeIndex: 99)), 1e-4f);
        }

        [Test]
        public void TimeOffsetKeepsItsSign()
        {
            Assert.AreEqual(Last - 0.25f, Resolve(Make(EffectTiming.LastNode, -0.25f)), 1e-4f);
            Assert.AreEqual(Last + 0.25f, Resolve(Make(EffectTiming.LastNode, 0.25f)), 1e-4f);
        }

        [Test]
        public void AlwaysCueIsValidAnywhere()
        {
            var cue = Make(EffectTiming.PatternStart, condition: EffectCondition.Always);
            Assert.IsFalse(cue.NeedsOutcome);
            Assert.IsTrue(cue.IsTimingValid(Resolve(cue), Last));
        }

        [Test]
        public void OutcomeCueBeforeLastNodeIsInvalid()
        {
            // 성패는 마지막 노드에서 정해진다 — 그보다 이르면 미래를 앞당겨 보여 주는 셈이다.
            var cue = Make(EffectTiming.FirstNode, condition: EffectCondition.Success);
            Assert.IsTrue(cue.NeedsOutcome);
            Assert.IsFalse(cue.IsTimingValid(Resolve(cue), Last));
        }

        [Test]
        public void OutcomeCueAtImpactIsValid()
        {
            var cue = Make(EffectTiming.Impact, condition: EffectCondition.Parry);
            Assert.IsTrue(cue.IsTimingValid(Resolve(cue), Last));
        }

        [Test]
        public void NegativeOffsetCanPushOutcomeCueBeforeItIsKnown()
        {
            var cue = Make(EffectTiming.Impact, -0.6f, condition: EffectCondition.Success);
            Assert.IsFalse(cue.IsTimingValid(Resolve(cue), Last));
        }

        [Test]
        public void ScaleAndSpeedAreClampedAboveZero()
        {
            var cue = new PatternEffectCue();
            var so = new SerializedLikeSetter(cue);
            so.Set("scale", -5f);
            so.Set("speed", 0f);

            // 0이면 보이지 않거나 영원히 안 끝나는 이펙트가 되어 무연출과 구분되지 않는다.
            Assert.Greater(cue.Scale, 0f);
            Assert.Greater(cue.Speed, 0f);
        }

        [Test]
        public void EmptyPrefabIsNotUsable()
        {
            Assert.IsFalse(new PatternEffectCue().IsUsable);
        }

        // ── 소리 큐 (docs/PatternEffectSfx) ──────────────────────────────────
        //
        // IsUsable은 예약·프리웜·툴 목록·툴 경고가 전부 보는 단일 게이트다.
        // '프리팹만'과 '소리만'이 같은 문을 통과해야 소리 전용 큐가 성립한다.

        [Test]
        public void PrefabOnlyCueIsUsable()
        {
            var cue = new PatternEffectCue();
            new SerializedLikeSetter(cue).Set("prefab", new GameObject("prefab-only"));

            Assert.IsTrue(cue.IsUsable);
        }

        [Test]
        public void SoundOnlyCueIsUsable()
        {
            // 이번 변경의 핵심 — 프리팹 없이 소리만 있어도 예약된다.
            var cue = new PatternEffectCue();
            new SerializedLikeSetter(cue).Set("sfx", AudioClip.Create("blip", 64, 1, 8000, false));

            Assert.IsTrue(cue.IsUsable);
        }

        [Test]
        public void CueWithNeitherPrefabNorSoundIsNotUsable()
        {
            Assert.IsFalse(new PatternEffectCue().IsUsable);
            Assert.IsNull(new PatternEffectCue().Sfx);
        }

        [Test]
        public void SoundOnlyCueStillHasAName()
        {
            // "(비어 있음)"으로 뜨면 툴 목록에서 소리 큐를 고를 수가 없다.
            var cue = new PatternEffectCue();
            new SerializedLikeSetter(cue).Set("sfx", AudioClip.Create("slash", 64, 1, 8000, false));

            Assert.AreEqual("slash", cue.Label);
        }

        [Test]
        public void SoundPitchNeverCollapsesToZero()
        {
            // 0 피치는 재생되지 않는다 — 무음과 구분이 안 되는 상태를 만들지 않는다.
            var cue = new PatternEffectCue();
            new SerializedLikeSetter(cue).Set("sfxPitch", 0f);

            Assert.Greater(cue.SfxPitch, 0f);
        }

        /// <summary>
        /// 직렬화 필드에 값을 넣는 테스트 전용 도구. 큐의 필드는 인스펙터 저장 대상이라 <c>private</c>인데,
        /// 그렇다고 테스트를 위해 세터를 열면 <b>런타임이 값을 바꿀 수 있는 문이 생긴다</b>(패턴은 모양 원본이다).
        /// </summary>
        private sealed class SerializedLikeSetter
        {
            private readonly object target;

            public SerializedLikeSetter(object target) => this.target = target;

            public void Set(string field, object value)
            {
                var info = target.GetType().GetField(field,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                Assert.IsNotNull(info, $"필드 '{field}'가 없습니다 — 이름이 바뀌었다면 테스트도 같이 고쳐야 합니다.");
                info.SetValue(target, value);
            }
        }
    }
}
