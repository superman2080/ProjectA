using System.Collections.Generic;
using PatternSpace;
using UnityEditor;
using UnityEngine;

namespace ChartGen
{
    /// <summary>Assets/Patterns/Templates 하위 Pattern 템플릿을 노드 개수 기준으로 스캔/캐싱한다.</summary>
    public class PatternTemplateLibrary
    {
        private const string TemplateFolder = "Assets/Patterns/Templates";

        private readonly Dictionary<int, List<Pattern>> templatesByNodeCount = new Dictionary<int, List<Pattern>>();

        public PatternTemplateLibrary()
        {
            if (!AssetDatabase.IsValidFolder(TemplateFolder))
            {
                Debug.LogWarning($"[PatternTemplateLibrary] '{TemplateFolder}' 폴더가 없습니다. 빈 라이브러리로 동작합니다.");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Pattern", new[] { TemplateFolder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var pattern = AssetDatabase.LoadAssetAtPath<Pattern>(path);
                if (pattern == null) continue;

                int count = pattern.AllData.Count;
                if (!templatesByNodeCount.TryGetValue(count, out var list))
                {
                    list = new List<Pattern>();
                    templatesByNodeCount[count] = list;
                }

                list.Add(pattern);
            }
        }

        public Pattern GetRandomTemplate(int nodeCount)
        {
            if (!templatesByNodeCount.TryGetValue(nodeCount, out var list) || list.Count == 0)
                return null;

            return list[Random.Range(0, list.Count)];
        }
    }
}
