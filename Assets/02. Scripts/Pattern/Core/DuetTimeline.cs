using UnityEngine;

namespace PatternSpace
{
    /// <summary>
    /// 합주 프리뷰의 시간 매핑. <b>순수 산술만 있고 UnityEditor에 의존하지 않는다</b> — 그래야 테스트된다.
    ///
    /// <para><b>왜 따로 빼는가.</b> 화면은 임팩트 기준 상대시간이고 에셋은 클립 절대시간이다.
    /// 이 변환이 UI 코드에 흩어지면 검증할 방법이 없고, 틀려도 저장값만 조용히 어긋난다.
    /// 산술을 한 곳에 모아 왕복 항등을 테스트로 못박는다.</para>
    ///
    /// <para><b>배속을 식에 포함한다.</b> 두 배우의 저작 <c>Speed</c>가 다를 수 있으므로
    /// 빼면 "저작 배속 기준"이라는 표시가 거짓이 된다. 다만 <c>t = 0</c>에서는 배속이 소거되어
    /// <b>임팩트 프레임은 배속과 무관하게 정확하다</b> — 이 프리뷰가 성립하는 근거가 그것이다.</para>
    /// </summary>
    public static class DuetTimeline
    {
        /// <summary>배속이 0/음수면 시간이 멈추거나 뒤집힌다. 나눗셈 분모로도 쓰이므로 하한을 둔다.</summary>
        private const float MinSpeed = 0.01f;

        /// <summary>뷰(상대시간) → 클립 절대시각.</summary>
        public static float ClipTimeOf(float impact, float speed, float t)
            => impact + t * Mathf.Max(speed, MinSpeed);

        /// <summary>클립 절대시각 → 뷰(상대시간). <see cref="ClipTimeOf"/>의 역함수다.</summary>
        public static float RelativeOf(float impact, float speed, float clipTime)
            => (clipTime - impact) / Mathf.Max(speed, MinSpeed);

        /// <summary>임팩트 앞쪽 길이(상대시간, 양수). 임팩트가 트림 시작보다 앞이면 0.</summary>
        public static float LeadOf(float start, float impact, float speed)
        {
            float v = RelativeOf(impact, speed, start); // start가 impact보다 앞이면 음수
            return v < 0f ? -v : 0f;
        }

        /// <summary>임팩트 뒤쪽 길이(상대시간). 클립이 없으면 0.</summary>
        public static float TailOf(float impact, float end, float speed)
        {
            float v = RelativeOf(impact, speed, end);
            return v > 0f ? v : 0f;
        }

        /// <summary>
        /// 두 배우를 모두 담는 스크럽 범위(상대시간). 한쪽이 비어 있으면(전부 0) 나머지 하나로 성립한다.
        /// 둘 다 비면 <c>(0, 0)</c>이라 호출자가 슬라이더를 그리지 않게 판단할 수 있다.
        /// </summary>
        public static (float min, float max) Range(
            float aStart, float aImpact, float aEnd, float aSpeed,
            float bStart, float bImpact, float bEnd, float bSpeed)
        {
            float min = -Mathf.Max(LeadOf(aStart, aImpact, aSpeed), LeadOf(bStart, bImpact, bSpeed));
            float max = Mathf.Max(TailOf(aImpact, aEnd, aSpeed), TailOf(bImpact, bEnd, bSpeed));
            return (min, max);
        }

        /// <summary>
        /// 트림 구간 밖을 경계로 자른다. <b>늘려 보여주면 거짓이다</b> —
        /// 런타임은 트림 밖을 재생하지 않는다.
        /// </summary>
        public static float ClampToTrim(float clipTime, float start, float end)
            => end > start ? Mathf.Clamp(clipTime, start, end) : start;
    }
}
