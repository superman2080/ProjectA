using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SequenceSpace.EditorTools
{
    /// <summary>
    /// 시퀀스 저작 도구.
    ///
    /// <para><b>씬 뷰 핸들이 이 리팩토링의 체감 비용을 없앤다.</b> "MonoBehaviour가 편했다"의 실체는
    /// 씬 뷰에서 눈으로 보고 드래그하는 것인데, 그건 MonoBehaviour가 아니라 <c>Handles</c>가 주는 것이다.
    /// 덤으로 이동 경로 전체를 폴리라인으로 볼 수 있다 - 게임오브젝트를 흩어 놓던 방식으로는 못 보던 것이다.</para>
    /// </summary>
    [CustomEditor(typeof(SequenceRunner))]
    public class SequenceRunnerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // bindings는 아래에서 슬롯 이름표를 달아 직접 그리므로 기본 인스펙터에서 뺀다(중복 방지).
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "bindings");
            serializedObject.ApplyModifiedProperties();

            var runner = (SequenceRunner)target;
            SequenceAsset asset = runner.Asset;

            EditorGUILayout.Space();

            if (asset == null)
            {
                EditorGUILayout.HelpBox("SequenceAsset을 배선하세요.", MessageType.Info);
                return;
            }

            DrawBindings(runner, asset);
            EditorGUILayout.Space();
            DrawStepList(asset);
        }

        private void DrawBindings(SequenceRunner runner, SequenceAsset asset)
        {
            EditorGUILayout.LabelField("Bindings", EditorStyles.boldLabel);

            string[] slots = asset.RequiredBindings;
            if (slots == null || slots.Length == 0)
            {
                EditorGUILayout.HelpBox("이 시퀀스는 씬 오브젝트를 요구하지 않습니다.", MessageType.None);
                return;
            }

            SerializedProperty bindings = serializedObject.FindProperty("bindings");
            SerializedProperty entries = bindings.FindPropertyRelative("entries");

            if (NeedsSync(entries, slots))
            {
                EditorGUILayout.HelpBox("선언된 슬롯과 배선표가 어긋납니다.", MessageType.Warning);
                if (GUILayout.Button("슬롯 동기화"))
                {
                    Undo.RecordObject(runner, "Sync Sequence Binding Slots");
                    runner.Bindings.SyncSlots(slots);
                    EditorUtility.SetDirty(runner);
                    serializedObject.Update();
                }
                return;
            }

            serializedObject.Update();

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                SerializedProperty slot = entry.FindPropertyRelative("slot");
                SerializedProperty targetObject = entry.FindPropertyRelative("target");

                EditorGUILayout.PropertyField(targetObject, new GUIContent(slot.stringValue));

                if (targetObject.objectReferenceValue == null)
                    EditorGUILayout.HelpBox($"슬롯 '{slot.stringValue}'이 비어 있습니다.", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static bool NeedsSync(SerializedProperty entries, string[] slots)
        {
            if (entries.arraySize != slots.Length) return true;

            for (int i = 0; i < slots.Length; i++)
            {
                SerializedProperty slot = entries.GetArrayElementAtIndex(i).FindPropertyRelative("slot");
                if (slot.stringValue != slots[i]) return true;
            }

            return false;
        }

        private static void DrawStepList(SequenceAsset asset)
        {
            EditorGUILayout.LabelField("Steps", EditorStyles.boldLabel);

            IReadOnlyList<SequenceStep> steps = asset.Steps;
            if (steps == null || steps.Count == 0)
            {
                EditorGUILayout.HelpBox("스텝이 없습니다.", MessageType.Warning);
                return;
            }

            for (int i = 0; i < steps.Count; i++)
                EditorGUILayout.LabelField($"{i}.", steps[i] != null ? steps[i].Label : "(비어 있음)");

            if (GUILayout.Button("시퀀스 에셋 열기")) Selection.activeObject = asset;
        }

        private void OnSceneGUI()
        {
            var runner = (SequenceRunner)target;
            SequenceAsset asset = runner.Asset;
            if (asset == null || asset.Steps == null) return;

            var path = new List<Vector3>();

            foreach (SequenceStep step in asset.Steps)
            {
                if (step is not MoveToStep move) continue;

                EditorGUI.BeginChangeCheck();
                Vector3 next = Handles.PositionHandle(move.Destination, Quaternion.identity);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset, "Move Sequence Destination");
                    move.SetDestination(next);
                    EditorUtility.SetDirty(asset);
                }

                Handles.Label(next + Vector3.up * 0.3f, move.Label);
                path.Add(next);
            }

            if (path.Count < 2) return;

            Handles.color = Color.cyan;
            Handles.DrawPolyLine(path.ToArray());
        }
    }
}
