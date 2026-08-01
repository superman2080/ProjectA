using System;
using UnityEngine;

namespace PatternSpace
{
    /// <summary>
    /// 클립 하나를 <b>임팩트 프레임 기준으로 정렬</b>하는 데 필요한 값 묶음.
    ///
    /// <para>플레이어(<see cref="CharacterActionPlayer"/>)와 적(<c>EnemyView</c>)이 <b>같은 정렬 규칙</b>을 쓰게 하는 것이
    /// 이 타입의 존재 이유다. 칼이 지나가는 순간을 양쪽이 같은 식으로 계산해야
    /// "적 칼이 지나가는 순간 = 플레이어 칼이 지나가는 순간"이 구조적으로 성립한다.</para>
    ///
    /// <para><b>공유하는 것은 임팩트 순간 하나뿐이다.</b> 두 클립은 길이도, 저작 배속도, 압축을 유발하는 제약도
    /// 다르므로(플레이어는 다음 패턴까지의 여유, 적은 처치 확정~임팩트 간격) 배속이 같아질 이유가 없다.
    /// 그래서 <b>시작이나 끝을 맞추는 정렬은 원리적으로 성립하지 않는다</b> — 시작을 맞춰도 배속이 다르면
    /// 임팩트가 어긋난다. 각 배우는 같은 절대 시각에 자기 임팩트 프레임이 오도록
    /// <b>자기 시작 시점(<see cref="ResolveScheduleStart"/>)과 자기 배속(<see cref="ResolvePlaySpeed"/>)을 역산</b>한다.
    /// 그래서 이 두 메서드가 전부 <c>impactAlignTime</c>을 받는다.</para>
    ///
    /// <para>⚠ <b>배속은 클립 전체에 걸린다</b>(Animator의 Speed Multiplier). <see cref="ResolvePlaySpeed"/>는
    /// <i>임팩트 이전</i> 구간만 보고 배속을 정하지만 그 값이 임팩트 이후에도 적용되므로,
    /// <see cref="ImpactTime"/>을 뒤에 찍을수록 마무리 동작까지 빨라진다.</para>
    ///
    /// <para>오서링은 <c>Tools/Animation Clip Trimmer</c>(Start/Impact/End)로 한다.</para>
    /// </summary>
    [Serializable]
    public class ClipAlignment
    {
        [Tooltip("재생할 클립. 비우면 무연출.")]
        [SerializeField] private AnimationClip clip;

        [Tooltip("트림 시작 오프셋(초). 선딜레이 제거용.")]
        [SerializeField] private float startOffset = 0f;

        [Tooltip("트림 길이(초). 0 이하면 클립 끝까지.")]
        [SerializeField] private float duration = 0f;

        [Tooltip("칼날이 표적/상대를 지나가는 프레임의 클립 절대 시각(초). 0 이하거나 트림 범위 밖이면 트림 끝으로 폴백.")]
        [SerializeField] private float impactTime = 0f;

        [Tooltip("기본 배속(하한). 입력 구간이 짧으면 소비자가 자동으로 더 배속한다.")]
        [SerializeField] private float speed = 1f;

        public AnimationClip Clip => clip;
        public float StartOffset => startOffset;
        public float ImpactTime => impactTime;
        public float Speed => Mathf.Max(speed, 0.01f);

        /// <summary>배선된 클립이 있고 트림 길이가 양수인지. 아니면 무연출로 넘긴다.</summary>
        public bool IsUsable => clip != null && ResolvedDuration > 0f;

        /// <summary>트림 길이(초). <see cref="duration"/>이 0 이하면 클립 끝까지로 본다. 클립이 없으면 0.</summary>
        public float ResolvedDuration
        {
            get
            {
                if (duration > 0f) return duration;
                if (clip == null) return 0f;
                return Mathf.Max(clip.length - startOffset, 0f);
            }
        }

        /// <summary>
        /// 트림 시작부터 임팩트 프레임까지의 길이(클립 초). 배속 역산의 기준이다.
        /// 임팩트가 오서링되지 않았거나(0 이하) 트림 범위 밖이면 <b>트림 끝</b>으로 폴백한다 —
        /// 그래야 값이 없는 기존 데이터도 그대로 동작한다.
        /// </summary>
        public float ResolvedImpactSpan
        {
            get
            {
                float dur = ResolvedDuration;
                if (impactTime <= 0f) return dur;

                float span = impactTime - startOffset;
                if (span <= 0f || span > dur) return dur;

                return span;
            }
        }

        /// <summary>
        /// 임팩트 프레임이 <paramref name="impactAlignTime"/>에 오도록 <b>재생을 시작해야 하는 시각</b>.
        /// <paramref name="earliest"/>보다 이르면 그 값으로 잘린다(첫 노드보다 먼저 휘두를 수 없다).
        /// </summary>
        public float ResolveScheduleStart(float impactAlignTime, float earliest)
        {
            return Mathf.Max(impactAlignTime - ResolvedImpactSpan / Speed, earliest);
        }

        /// <summary>
        /// 지금(<paramref name="now"/>) 재생을 시작할 때 임팩트를 <paramref name="impactAlignTime"/>에 맞추기 위한 배속.
        /// <paramref name="maxSpeed"/>로 클램프하며, <paramref name="clamped"/>가 true면 <b>정렬이 깨진 것</b>이다(경고 대상).
        /// </summary>
        public float ResolvePlaySpeed(float now, float impactAlignTime, float maxSpeed, out bool clamped)
        {
            float remaining = Mathf.Max(impactAlignTime - now, 0.0001f);
            float needed = ResolvedImpactSpan / remaining;
            float cap = Mathf.Max(maxSpeed, Speed);

            clamped = needed > cap;
            return Mathf.Clamp(needed, Speed, cap);
        }

#if UNITY_EDITOR
        /// <summary>임팩트 프레임이 트림 구간 안에 있는지 검증한다. 미지정(0 이하)은 정상 — 트림 끝으로 폴백하기 때문.</summary>
        public void ValidateImpactTime(UnityEngine.Object context, string label)
        {
            if (impactTime <= 0f) return;

            float dur = ResolvedDuration;
            if (dur <= 0f) return;

            float trimEnd = startOffset + dur;
            if (impactTime < startOffset || impactTime > trimEnd)
            {
                Debug.LogWarning(
                    $"[ClipAlignment] '{context.name}'의 {label} ImpactTime({impactTime:0.000}s)이 " +
                    $"트림 구간 [{startOffset:0.000}s, {trimEnd:0.000}s] 밖입니다. 트림 끝으로 폴백합니다.", context);
            }
        }

        /// <summary>마이그레이션 전용 기입 경로(구 Pattern 필드 → playerAttack 이관). 런타임은 호출하지 않는다.</summary>
        public void EditorAssign(AnimationClip newClip, float newStartOffset, float newDuration, float newImpactTime, float newSpeed)
        {
            clip = newClip;
            startOffset = newStartOffset;
            duration = newDuration;
            impactTime = newImpactTime;
            speed = newSpeed;
        }
#endif
    }
}
