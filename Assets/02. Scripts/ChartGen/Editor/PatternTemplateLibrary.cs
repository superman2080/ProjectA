using System.Collections.Generic;
using System.Linq;
using PatternSpace;
using UnityEditor;
using UnityEngine;

namespace ChartGen
{
    /// <summary>Assets/Patterns/Templates 하위 Pattern 템플릿을 노드 개수 기준으로 스캔/캐싱한다.</summary>
    public class PatternTemplateLibrary
    {
        private const string TemplateFolder = "Assets/04. Datas/Patterns/Templates";

        private readonly Dictionary<int, List<Pattern>> templatesByNodeCount = new Dictionary<int, List<Pattern>>();
        private readonly Dictionary<int, int> roundRobinIndex = new Dictionary<int, int>();
        private readonly List<int> availableNodeCounts = new List<int>();

        /// <summary>라이브러리에 존재하는 노드 개수 목록 (오름차순 정렬).</summary>
        public IReadOnlyList<int> AvailableNodeCounts => availableNodeCounts;

        /// <summary>라이브러리에서 가장 큰 노드 개수. 템플릿이 없으면 0.</summary>
        public int MaxNodeCount => availableNodeCounts.Count > 0 ? availableNodeCounts[^1] : 0;

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

            availableNodeCounts.AddRange(templatesByNodeCount.Keys.OrderBy(k => k));
        }

        /// <summary>지정한 노드 개수의 템플릿을 라운드로빈으로 순환하며 반환한다. 해당 개수 템플릿이 없으면 null.</summary>
        public Pattern GetNextTemplate(int nodeCount)
        {
            if (!templatesByNodeCount.TryGetValue(nodeCount, out var list) || list.Count == 0)
                return null;

            if (!roundRobinIndex.TryGetValue(nodeCount, out int idx))
                idx = 0;

            var template = list[idx % list.Count];
            roundRobinIndex[nodeCount] = idx + 1;
            return template;
        }

        /// <summary>지정한 노드 개수의 템플릿을 무작위로 반환한다. 해당 개수 템플릿이 없으면 null.</summary>
        public Pattern GetRandomTemplate(int nodeCount)
        {
            if (!templatesByNodeCount.TryGetValue(nodeCount, out var list) || list.Count == 0)
                return null;

            return list[Random.Range(0, list.Count)];
        }
    }
}
