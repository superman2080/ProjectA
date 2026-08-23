using System.Collections.Generic;
using UnityEngine;

namespace PatternSpace
{
    /// <summary>
    /// 한 패턴이 <b>클립 여러 개를 순서대로</b> 재생할 때의 시간 계산. 런타임(<c>CharacterActionPlayer</c>)과
    /// 저작 툴(<c>Animation Clip Trimmer</c>)이 <b>같은 함수</b>를 부르므로 두 그림이 어긋날 코드가 존재하지 않는다
    /// (<see cref="DuelGap"/>과 같은 자리·같은 규율).
    ///
    /// <para><b>정렬 앵커는 여전히 하나뿐이다</b> — 마지막 클립(<c>playerAttack</c>/<c>playerParry</c>)의
    /// 임팩트 프레임이 <c>Deadline + ImpactOffset</c>에 온다(§6). 그 앞에 붙는 리드인 원소들은
    /// <b>트림 전체</b>가 재생 길이이고 <see cref="ClipAlignment.ImpactTime"/>은 정렬 의미를 갖지 않는다 —
    /// 리드인 구간에 히트스톱을 걸고 싶으면 <see cref="ClipAlignment.ResolvedExtraImpactSpans"/>에 찍는다.</para>
    ///
    /// <para><b>단위는 '저작 초'다.</b> 원소마다 저작 배속(<see cref="ClipAlignment.Speed"/>)이 다르므로
    /// 클립 초를 그대로 더하면 의미가 없다. 저작 배속으로 나눈 값(= 그 원소가 배속 보정 없이 흐르는 실시간)을
    /// 더해야 시퀀스 전체를 하나의 가상 클립으로 다룰 수 있고, 그래야 <b>비율 하나로 전체를 압축</b>할 수 있다.</para>
    /// </summary>
    public static class ClipSequence
    {
        /// <summary>리드인 원소 하나의 저작 초(트림 전체). 못 쓰는 원소는 0 — 빈 슬롯이 리스트에 섞이는 것은 정상 상태다.</summary>
        public static float AuthoredDuration(ClipAlignment element)
        {
            if (element == null || !element.IsUsable) return 0f;
            return element.ResolvedDuration / element.Speed;
        }

        /// <summary>마지막(정렬 대상) 클립의 <b>임팩트까지</b>의 저작 초. 임팩트 이후 잔여 트림은 포함하지 않는다.</summary>
        public static float AuthoredImpactSpan(ClipAlignment final)
        {
            if (final == null || !final.IsUsable) return 0f;
            return final.ResolvedImpactSpan / final.Speed;
        }

        /// <summary>
        /// 시퀀스 시작부터 <b>마지막 임팩트</b>까지의 저작 초. 재생 시작 시각을 역산하는 기준이자
        /// <c>DuelCurveTime</c>의 원점 거리다.
        ///
        /// <para>리드인이 비면 <c>AuthoredImpactSpan(final)</c>과 같다 — 즉 <b>기존 단일 클립 경로와 대수적으로 동일</b>하다.
        /// 이 항등식이 이 기능의 회귀 0 보장 전부다.</para>
        /// </summary>
        public static float AuthoredSpan(IReadOnlyList<ClipAlignment> leadIn, ClipAlignment final)
        {
            float span = 0f;

            if (leadIn != null)
            {
                for (int i = 0; i < leadIn.Count; i++)
                    span += AuthoredDuration(leadIn[i]);
            }

            return span + AuthoredImpactSpan(final);
        }

        /// <summary>
        /// 시퀀스 <b>전체에 걸리는 균일 배속 비율</b>. 창이 저작 총 길이를 감당하면 1이다.
        ///
        /// <para><b>1 미만으로 내려가지 않는다</b> — 저작 배속보다 느리게 재생하지 않는 기존 규칙
        /// (<c>Mathf.Clamp(needed, baseSpeed, cap)</c>의 하한)과 같다. 상한은 여기서 걸지 않는다:
        /// "원소가 전부 재생된다"가 이 기능의 요구라 상한과 양립하지 않으므로, 상한 판단은 호출부가
        /// 경고로만 처리한다.</para>
        /// </summary>
        public static float ResolveRatio(float authoredSpan, float availableTime)
        {
            if (authoredSpan <= 0f) return 1f;

            return Mathf.Max(authoredSpan / Mathf.Max(availableTime, 0.0001f), 1f);
        }

        /// <summary>원소 하나가 실제로 흐르는 실시간(초). 저작 초를 비율로 나눈 값이다.</summary>
        public static float RealDuration(ClipAlignment element, float ratio)
        {
            return AuthoredDuration(element) / Mathf.Max(ratio, 0.01f);
        }

        /// <summary>
        /// 리드인 원소 <paramref name="index"/>의 <b>트림 끝</b>이 놓이는 시각(임팩트 기준 저작 상대초, 언제나 음수).
        /// 그 지점이 곧 다음 원소가 시작하는 지점이다.
        ///
        /// <para>저작 툴의 타임라인이 리드인 원소를 제자리에 그리는 데 쓴다 — 런타임은 순서대로 이어 재생하므로
        /// 이 값을 따로 묻지 않는다.</para>
        /// </summary>
        public static float AuthoredEndOffset(IReadOnlyList<ClipAlignment> leadIn, int index, ClipAlignment final)
        {
            float tail = AuthoredImpactSpan(final);

            if (leadIn != null)
            {
                for (int i = index + 1; i < leadIn.Count; i++)
                    tail += AuthoredDuration(leadIn[i]);
            }

            return -tail;
        }

        /// <summary>쓸 수 있는 원소만 추린 배열. 런타임은 이 배열로만 진행한다(빈 슬롯에서 멈추지 않게).</summary>
        public static ClipAlignment[] Usable(IReadOnlyList<ClipAlignment> leadIn)
        {
            if (leadIn == null || leadIn.Count == 0) return System.Array.Empty<ClipAlignment>();

            var list = new List<ClipAlignment>(leadIn.Count);
            for (int i = 0; i < leadIn.Count; i++)
            {
                if (leadIn[i] != null && leadIn[i].IsUsable) list.Add(leadIn[i]);
            }

            return list.Count == 0 ? System.Array.Empty<ClipAlignment>() : list.ToArray();
        }
    }
}
