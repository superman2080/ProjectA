using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PatternSpace.EditorTools
{
    /// <summary>
    /// <see cref="Pattern"/> 인스펙터. <b>역할에 쓰이지 않는 클립 슬롯을 숨긴다.</b>
    ///
    /// <para>한 패턴에서 실제로 재생되는 슬롯은 언제나 <b>플레이어 클립 1 + 적 클립 1</b>이고,
    /// 나머지 둘은 무슨 값을 넣어도 재생되지 않는다. 넷을 다 보여주면 저작자가 죽은 슬롯을 채우게 되고,
    /// 그건 조용히 무시된다(경고도 없다). 그래서 <c>attacker</c>가 고른 둘만 그린다.</para>
    ///
    /// <para><b>데이터는 넷 그대로 둔다.</b> 슬롯 이름이 "이게 공격인지 패링인지"를 알려주는 유일한 단서이고
    /// (트리머의 슬롯 목록이 이 이름을 쓴다), 필드를 합치면 그 단서가 사라진다.
    /// 숨기는 것으로 저작 표면만 줄이고 데이터는 건드리지 않는다.</para>
    ///
    /// <para>숨긴 슬롯에 값이 남아 있으면 <b>접이식으로 열어 볼 수 있게</b> 한다 —
    /// 완전히 감추면 이관 찌꺼기를 지울 방법이 없어진다.</para>
    /// </summary>
    [CustomEditor(typeof(Pattern))]
    public class PatternEditor : Editor
    {
        private const string AttackerProperty = "attacker";
        private const string RetreatDistanceProperty = "retreatDistance";

        // 역할별로 살아 있는 슬롯. 나머지는 접이식으로 내려간다.
        private static readonly string[] EnemyRoleSlots = { "enemyAttack", "playerParry" };
        private static readonly string[] PlayerRoleSlots = { "playerAttack", "enemyDeath" };

        private static readonly string[] AllSlots =
        {
            "enemyAttack", "playerParry", "playerAttack", "enemyDeath"
        };

        private bool showUnused;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var attackerProp = serializedObject.FindProperty(AttackerProperty);
            bool enemyIsAttacker = attackerProp != null
                && attackerProp.enumValueIndex == (int)EnemySpace.Attacker.Enemy;

            var active = enemyIsAttacker ? EnemyRoleSlots : PlayerRoleSlots;
            var hidden = Collect(AllSlots, active);

            // 블랙리스트 방식이라 나중에 필드를 추가해도 자동으로 인스펙터에 나타난다.
            var excluded = new List<string> { "m_Script" };
            excluded.AddRange(AllSlots);

            // 밀려나는 거리는 적이 공격자일 때만 쓰인다(RetreatDistanceOf). 다른 역할에서 보이면
            // 저작자가 조용히 무시되는 값을 채우게 된다 - 위 클립 슬롯과 같은 근거다.
            if (!enemyIsAttacker) excluded.Add(RetreatDistanceProperty);
            DrawPropertiesExcluding(serializedObject, excluded.ToArray());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                enemyIsAttacker ? "이 패턴의 클립 (적 공격 → 플레이어 패링)" : "이 패턴의 클립 (플레이어 공격 → 적 사망)",
                EditorStyles.boldLabel);

            foreach (string slot in active)
            {
                var prop = serializedObject.FindProperty(slot);
                if (prop != null) EditorGUILayout.PropertyField(prop, true);
            }

            DrawHiddenSlots(hidden);

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 재생되지 않는 슬롯. <b>값이 남아 있으면 경고를 띄운다</b> — 이관 찌꺼기를 찾는 자리다.
        /// </summary>
        private void DrawHiddenSlots(List<string> hidden)
        {
            bool anyAssigned = false;
            foreach (string slot in hidden)
            {
                var prop = serializedObject.FindProperty(slot);
                if (HasValue(prop)) { anyAssigned = true; break; }
            }

            EditorGUILayout.Space();

            string label = anyAssigned
                ? "⚠ 이 역할에서 재생되지 않는 슬롯 (값이 남아 있음)"
                : "이 역할에서 재생되지 않는 슬롯";

            showUnused = EditorGUILayout.Foldout(showUnused || anyAssigned, label, true);
            if (!showUnused) return;

            EditorGUI.indentLevel++;

            if (anyAssigned)
            {
                EditorGUILayout.HelpBox(
                    "아래 슬롯은 이 패턴의 attacker에서 절대 재생되지 않습니다. " +
                    "이관 찌꺼기라면 비우세요. 역할을 바꾸려면 위의 attacker를 바꾸세요.",
                    MessageType.Warning);
            }

            foreach (string slot in hidden)
            {
                var prop = serializedObject.FindProperty(slot);
                if (prop != null) EditorGUILayout.PropertyField(prop, true);
            }

            EditorGUI.indentLevel--;
        }

        private static bool HasValue(SerializedProperty prop)
        {
            if (prop == null) return false;

            // SliceSet 같은 오브젝트 참조는 그대로, ClipAlignment는 안쪽 clip으로 판단한다.
            if (prop.propertyType == SerializedPropertyType.ObjectReference)
                return prop.objectReferenceValue != null;

            var clip = prop.FindPropertyRelative("clip");
            return clip != null && clip.objectReferenceValue != null;
        }

        private static List<string> Collect(string[] all, string[] exclude)
        {
            var result = new List<string>();
            foreach (string name in all)
            {
                if (System.Array.IndexOf(exclude, name) >= 0) continue;
                result.Add(name);
            }
            return result;
        }
    }
}
