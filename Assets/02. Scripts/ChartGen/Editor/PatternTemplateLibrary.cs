using System.Collections.Generic;
using System.Linq;
using PatternSpace;
using UnityEditor;
using UnityEngine;

namespace ChartGen
{
    /// <summary>
    /// 굽기에 쓸 Pattern 템플릿을 노드 개수 기준으로 묶어 둔다.
    ///
    /// <para>출처는 둘이다 — <b>채보가 지정한 목록</b>(<see cref="SongChart.patternPool"/>)이 있으면 그것만 쓰고,
    /// 비어 있으면 템플릿 폴더 전체를 스캔한다. 곡마다 어울리는 패턴이 다르므로 지정이 기본이고,
    /// 폴더 스캔은 아직 고르지 않은 채보를 위한 폴백이다.</para>
    /// </summary>
    public class PatternTemplateLibrary
    {
        public const string TemplateFolder = "Assets/04. Datas/Patterns/Templates";

        /// <summary>폴더 전체가 아니라 지정된 목록만 쓴다. 비었거나 null이면 폴더 스캔으로 넘어간다.</summary>
        public static PatternTemplateLibrary From(IEnumerable<Pattern> explicitPool)
        {
            var list = explicitPool?.Where(p => p != null).ToList();
            return list != null && list.Count > 0
                ? new PatternTemplateLibrary(list)
                : new PatternTemplateLibrary();
        }

        private PatternTemplateLibrary(IReadOnlyList<Pattern> pool)
        {
            foreach (var pattern in pool) Register(pattern);
            SortNodeCounts();
        }

        private readonly Dictionary<int, List<Pattern>> templatesByNodeCount = new Dictionary<int, List<Pattern>>();
        private readonly Dictionary<int, int> roundRobinIndex = new Dictionary<int, int>();
        private readonly List<int> availableNodeCounts = new List<int>();

        /// <summary>라이브러리에 존재하는 노드 개수 목록 (오름차순 정렬).</summary>
        public IReadOnlyList<int> AvailableNodeCounts => availableNodeCounts;

        public PatternTemplateLibrary()
        {
            if (!AssetDatabase.IsValidFolder(TemplateFolder))
            {
                Debug.LogWarning($"[PatternTemplateLibrary] '{TemplateFolder}' 폴더가 없습니다. 빈 라이브러리로 동작합니다.");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Pattern", new[] { TemplateFolder });
            foreach (var guid in guids)
                Register(AssetDatabase.LoadAssetAtPath<Pattern>(AssetDatabase.GUIDToAssetPath(guid)));

            SortNodeCounts();
        }

        private void Register(Pattern pattern)
        {
            if (pattern == null || pattern.AllData == null) return;

            // ⚠ 연타는 자동 배정에서 뺀다. 음악의 온셋이 연타를 정하지 않으므로(어디에 둘지는 저작 판단이다)
            // 손으로 꽂는 것이 맞고, 그냥 두면 더 나쁘다 — 연타의 patternDatas는 게이지 자리 1칸뿐이라
            // 이 사전에서 '1노드 패턴'으로 분류돼 1온셋 그룹에 무작위로 꽂히는데,
            // 정작 연타가 요구하는 온셋은 2개(창 시작·끝)라 배정하는 족족 저장이 막힌다.
            if (pattern.IsMash) return;

            int count = pattern.AllData.Count;
            if (count <= 0) return;

            if (!templatesByNodeCount.TryGetValue(count, out var list))
                templatesByNodeCount[count] = list = new List<Pattern>();

            if (!list.Contains(pattern)) list.Add(pattern); // 같은 패턴을 두 번 넣어도 확률만 왜곡된다
        }

        private void SortNodeCounts()
        {
            availableNodeCounts.Clear();
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
