using UnityEditor;
using UnityEngine;

namespace SequenceSpace.EditorTools
{
    /// <summary>
    /// <see cref="SequenceSlotAttribute"/>가 붙은 문자열을 <b>선언된 슬롯 드롭다운</b>으로 그린다.
    ///
    /// <para>스텝이 <see cref="SequenceAsset"/> 안에 직렬화돼 있으므로 <c>serializedObject.targetObject</c>가
    /// 곧 그 에셋이다 — 그래서 드로어가 <see cref="SequenceAsset.RequiredBindings"/>를 읽을 수 있다.
    /// <b>이것이 전역 enum 없이도 문자열 키가 안전한 이유다.</b></para>
    /// </summary>
    [CustomPropertyDrawer(typeof(SequenceSlotAttribute))]
    public class SequenceSlotDrawer : PropertyDrawer
    {
        private const string NoneLabel = "(비움 - 기본 대상)";

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var asset = property.serializedObject.targetObject as SequenceAsset;
            string[] slots = asset != null ? asset.RequiredBindings : null;

            // 선언된 슬롯이 없으면 드롭다운으로 고를 것이 없다 - 자유 입력으로 물러난다.
            if (slots == null || slots.Length == 0)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var options = new string[slots.Length + 1];
            options[0] = NoneLabel;
            for (int i = 0; i < slots.Length; i++) options[i + 1] = slots[i];

            int current = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != property.stringValue) continue;
                current = i + 1;
                break;
            }

            // 선언 목록에서 사라진 이름이 남아 있으면 조용히 0으로 보이면 안 된다.
            bool unknown = !string.IsNullOrEmpty(property.stringValue) && current == 0;
            if (unknown) label.text += "  (미선언 슬롯!)";

            EditorGUI.BeginChangeCheck();
            int next = EditorGUI.Popup(position, label.text, current, options);
            if (!EditorGUI.EndChangeCheck()) return;

            property.stringValue = next == 0 ? string.Empty : slots[next - 1];
        }
    }
}
