using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 이 문자열 필드가 <see cref="SequenceAsset.RequiredBindings"/>의 슬롯 이름임을 표시한다.
    /// 에디터가 자유 입력 대신 <b>선언된 슬롯 드롭다운</b>을 그리므로 오타가 불가능해진다 —
    /// 전역 enum 없이도 문자열 키가 안전한 이유다.
    /// </summary>
    public class SequenceSlotAttribute : PropertyAttribute
    {
    }
}
