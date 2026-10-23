using System.Collections.Generic;
using System.Linq;
using PatternSpace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChartGen
{
    /// <summary>
    /// <b>1노트 패턴을 프로젝트에서 걷어내는 1회성 이관</b>(docs/NoSingleNotePattern Step 8~10).
    ///
    /// <para>순서가 곧 안전장치다 — ① 채보의 1노트 엔트리를 이웃과 합친다 ② 그 밖의 참조(씬·시퀀스·풀)를 지운다
    /// ③ 남은 참조가 0개인 것만 에셋을 삭제한다. 거꾸로 지우면 채보 엔트리의 <c>template</c>이 Missing이 되어
    /// <c>ChartPlayer</c>가 그 엔트리를 투입하지 못한다.</para>
    ///
    /// <para>에디터 안에서 돈다 — 열려 있는 씬은 메모리에서 고치고 더티로 남긴다(저장되지 않은 작업을 덮지 않는다).</para>
    /// </summary>
    public static class SingleNoteMigration
    {
        [MenuItem("Tools/Pattern Chart Migration/Remove Single-Note Patterns")]
        private static void Run()
        {
            var singles = FindSingleNotePatterns();

            // ⚠ 1노트 패턴이 없어도 채보는 고쳐야 한다 — 패턴 에셋을 제자리에서 2노트로 고치면(GUID 유지)
            //   그 패턴을 쓰던 엔트리는 온셋 1개를 든 채 남아 PatternHandler가 "입력 시각 개수가 노드 개수와 다르다"로 거부한다.
            //   ①은 패턴이 아니라 엔트리의 온셋 수를 보므로 그 경우도 그대로 고친다.
            string names = singles.Count > 0 ? string.Join(", ", singles.Select(p => p.name)) : "(없음 — 채보의 1노트 엔트리만 정리)";
            if (!EditorUtility.DisplayDialog("1노트 패턴 제거",
                    $"채보의 1노트 엔트리를 이웃과 합칩니다.\n삭제할 1노트 패턴: {names}\n\n" +
                    "열려 있는 씬은 수정 후 더티로 남습니다(직접 저장하세요).", "실행", "취소"))
                return;

            var singleSet = new HashSet<Object>(singles);
            var singlePaths = new HashSet<string>(singles.Select(AssetDatabase.GetAssetPath));

            // ① 채보
            foreach (var guid in AssetDatabase.FindAssets("t:SongChart"))
            {
                var chart = AssetDatabase.LoadAssetAtPath<SongChart>(AssetDatabase.GUIDToAssetPath(guid));
                if (chart != null) MigrateChart(chart, singleSet);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (singles.Count == 0) return; // 지울 패턴이 없다 — ②·③은 할 일이 없다

            // ② 그 밖의 참조
            var cleanedInMemory = new HashSet<string>();
            foreach (var path in FindReferencingPaths(singlePaths).Values.SelectMany(v => v).Distinct().ToList())
                StripReferences(path, singleSet, cleanedInMemory);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ③ 남은 참조가 없는 것만 삭제. 열려 있어 메모리에서만 고친 씬은 디스크가 아직 옛 참조를 들고 있으므로 뺀다
            //    — 그 씬을 저장하면 참조가 사라진다.
            var remaining = FindReferencingPaths(singlePaths);
            foreach (var pattern in singles)
            {
                string path = AssetDatabase.GetAssetPath(pattern);
                var users = remaining.TryGetValue(path, out var list)
                    ? list.Where(u => !cleanedInMemory.Contains(u)).ToList()
                    : new List<string>();
                if (users.Count > 0)
                {
                    Debug.LogWarning($"[SingleNoteMigration] '{pattern.name}'을 아직 참조하는 곳이 있어 삭제하지 않았습니다: " +
                                     string.Join(", ", users));
                    continue;
                }

                AssetDatabase.DeleteAsset(path);
                Debug.Log($"[SingleNoteMigration] 삭제: {path}");
            }
        }

        private static List<Pattern> FindSingleNotePatterns()
        {
            return AssetDatabase.FindAssets("t:Pattern")
                .Select(g => AssetDatabase.LoadAssetAtPath<Pattern>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p => p != null && !p.IsMash && p.AllData != null
                            && p.AllData.Count > 0 && p.AllData.Count < Pattern.MinNodeCount)
                .ToList();
        }

        // ───────────────────────── ① 채보 ─────────────────────────

        private static bool IsSingleEntry(SongChartEntry e)
        {
            if (e.template != null && e.template.IsMash) return false;
            return e.onsetTimes != null && e.onsetTimes.Length > 0 && e.onsetTimes.Length < Pattern.MinNodeCount;
        }

        /// <summary>
        /// 1노트 엔트리를 <b>간격이 더 짧은 이웃</b>(동률이면 앞)과 합친다(Plan D4). 대부분 같은 온셋 그룹의 자투리라
        /// 원래 그룹으로 돌아가는 것이다. 합친 크기의 템플릿이 풀에 없으면 반대쪽, 그래도 없으면 그 온셋을 버린다.
        /// </summary>
        private static void MigrateChart(SongChart chart, HashSet<Object> singleSet)
        {
            if (chart.entries == null) return;

            bool poolChanged = false;
            if (chart.patternPool != null && chart.patternPool.Any(p => p != null && singleSet.Contains(p)))
            {
                chart.patternPool = chart.patternPool.Where(p => p == null || !singleSet.Contains(p)).ToArray();
                poolChanged = true;
            }

            var entries = chart.entries.ToList();
            if (!entries.Any(IsSingleEntry))
            {
                if (poolChanged) EditorUtility.SetDirty(chart);
                return;
            }

            var library = PatternTemplateLibrary.From(chart.patternPool);
            int mergedPrev = 0, mergedNext = 0, dropped = 0;
            var retemplated = new List<string>();

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!IsSingleEntry(e)) continue;

                var sides = new List<int>();
                float gapPrev = i > 0 && CanAbsorb(entries[i - 1]) ? e.onsetTimes[0] - entries[i - 1].onsetTimes[^1] : float.MaxValue;
                float gapNext = i + 1 < entries.Count && CanAbsorb(entries[i + 1]) ? entries[i + 1].onsetTimes[0] - e.onsetTimes[^1] : float.MaxValue;
                if (gapPrev != float.MaxValue) sides.Add(-1);
                if (gapNext != float.MaxValue) sides.Add(+1);
                if (sides.Count == 2 && gapNext < gapPrev) sides.Reverse();

                bool merged = false;
                foreach (int side in sides)
                {
                    int n = i + side;
                    var neighbor = entries[n];
                    int newCount = neighbor.onsetTimes.Length + e.onsetTimes.Length;
                    var template = library.GetNextTemplate(newCount);
                    if (template == null) continue;

                    // 흡수된 온셋은 이웃의 노출 시간을 따른다 — 1노트 패턴만의 긴 노출(1.5s)이 패턴 중간에 남지 않게.
                    float exposure = neighbor.exposureDurations[0];
                    var head = side < 0 ? neighbor : e;
                    var tail = side < 0 ? e : neighbor;

                    var combined = new SongChartEntry
                    {
                        template = template,
                        onsetTimes = head.onsetTimes.Concat(tail.onsetTimes).ToArray(),
                        exposureDurations = (side < 0 ? neighbor.exposureDurations : Enumerable.Repeat(exposure, 1))
                            .Concat(side < 0 ? Enumerable.Repeat(exposure, 1) : neighbor.exposureDurations).ToArray(),
                        enemyCue = tail.enemyCue, // 마무리는 언제나 뒤쪽이다(MergeWithNext와 같은 규칙)
                    };
                    combined.spawnTimes = combined.onsetTimes.Select((t, k) => t - combined.exposureDurations[k]).ToArray();

                    int at = Mathf.Min(i, n);
                    entries.RemoveAt(at + 1);
                    entries[at] = combined;
                    retemplated.Add($"{at}:{template.name}");

                    if (side < 0) mergedPrev++; else mergedNext++;
                    i = at - 1; // 합친 자리부터 다시 본다(인덱스가 당겨졌다)
                    merged = true;
                    break;
                }

                if (merged) continue;

                // 합칠 곳이 없다 — 버린다. 처치를 들고 있었으면 앞 엔트리가 물려받아 사슬 마무리가 끊기지 않게 한다.
                if (e.enemyCue != null && e.enemyCue.killOnSuccess && i > 0 && !entries[i - 1].enemyCue.killOnSuccess)
                    entries[i - 1].enemyCue.killOnSuccess = true;

                Debug.LogWarning($"[SingleNoteMigration] '{chart.name}' {i}번 엔트리({e.onsetTimes[0]:F2}s)를 합칠 곳이 없어 버렸습니다.");
                entries.RemoveAt(i);
                dropped++;
                i--;
            }

            chart.entries = entries.ToArray();
            EditorUtility.SetDirty(chart);

            Debug.Log($"[SingleNoteMigration] '{chart.name}': 앞과 합침 {mergedPrev} · 뒤와 합침 {mergedNext} · 버림 {dropped} " +
                      $"→ 엔트리 {chart.entries.Length}개. 템플릿이 바뀐 엔트리(합친 직후 번호): {string.Join(", ", retemplated)}");

            // 재배정이 마지막 엔트리의 역할을 바꿨을 수 있다 — 저장 검증(ValidateForSave)의 ①·①-b와 같은 조건을 미리 알린다.
            var last = chart.entries.Length > 0 ? chart.entries[^1] : null;
            if (last != null && ((last.template != null && last.template.HitsOnFail) || !last.enemyCue.killOnSuccess))
            {
                Debug.LogWarning($"[SingleNoteMigration] '{chart.name}'의 마지막 엔트리가 공격+처치가 아닙니다. " +
                                 "Pattern Chart Tool로 열어 마지막 패턴을 고친 뒤 저장하세요.");
            }
        }

        private static bool CanAbsorb(SongChartEntry e)
        {
            return e.onsetTimes != null && e.onsetTimes.Length > 0
                   && !(e.template != null && e.template.IsMash)
                   && e.exposureDurations != null && e.exposureDurations.Length > 0;
        }

        // ───────────────────────── ② 그 밖의 참조 ─────────────────────────

        /// <summary>대상 경로마다 그것을 직접 참조하는 에셋 경로 목록.</summary>
        private static Dictionary<string, List<string>> FindReferencingPaths(HashSet<string> targets)
        {
            var result = targets.ToDictionary(t => t, _ => new List<string>());
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/") || targets.Contains(path)) continue;
                if (!(path.EndsWith(".asset") || path.EndsWith(".prefab") || path.EndsWith(".unity"))) continue;

                foreach (var dep in AssetDatabase.GetDependencies(path, false))
                    if (result.TryGetValue(dep, out var users)) users.Add(path);
            }
            return result;
        }

        private static void StripReferences(string path, HashSet<Object> singleSet, HashSet<string> cleanedInMemory)
        {
            if (path.EndsWith(".unity"))
            {
                if (StripScene(path, singleSet)) cleanedInMemory.Add(path);
                return;
            }

            if (path.EndsWith(".prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                int count = root.GetComponentsInChildren<Component>(true).Where(c => c != null).Sum(c => Strip(c, singleSet));
                if (count > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                Report(path, count);
                return;
            }

            int total = 0;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj == null || obj is SongChart) continue; // 채보는 ①이 맡았다 — 여기서 null로 지우면 엔트리가 Missing이 된다
                int n = Strip(obj, singleSet);
                if (n > 0) EditorUtility.SetDirty(obj);
                total += n;
            }
            Report(path, total);
        }

        /// <returns>열려 있던 씬을 메모리에서만 고쳤는가(디스크에는 아직 참조가 남아 있다).</returns>
        private static bool StripScene(string path, HashSet<Object> singleSet)
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            int count = 0;
            foreach (var go in scene.GetRootGameObjects())
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c != null) count += Strip(c, singleSet);

            if (count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                // 열려 있던 씬은 저장하지 않는다 — 사용자의 저장 안 된 작업을 같이 덮을 수 있다.
                if (!wasOpen) EditorSceneManager.SaveScene(scene);
            }

            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);

            Report(path + (wasOpen && count > 0 ? " (열려 있는 씬 — 저장하세요)" : ""), count);
            return wasOpen && count > 0;
        }

        /// <summary>
        /// 이 오브젝트의 직렬화 필드에서 대상 참조를 지운다. 배열 원소면 <b>원소째 삭제</b>(프리웜 목록·드릴 패턴),
        /// 단일 필드면 null. <c>[SerializeReference]</c> 안(시퀀스 스텝)까지 들어간다.
        /// </summary>
        private static int Strip(Object obj, HashSet<Object> singleSet)
        {
            var so = new SerializedObject(obj);
            var hits = new List<string>();

            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference
                    && it.objectReferenceValue != null && singleSet.Contains(it.objectReferenceValue))
                    hits.Add(it.propertyPath);
            }

            if (hits.Count == 0) return 0;

            // 뒤에서부터 지운다 — 앞 원소를 지우면 뒤 원소의 경로 인덱스가 당겨진다.
            foreach (var propPath in hits.OrderByDescending(p => p, new ArrayPathComparer()))
            {
                var prop = so.FindProperty(propPath);
                if (prop == null) continue;

                prop.objectReferenceValue = null;

                if (TrySplitArrayElement(propPath, out string arrayPath, out int index))
                    so.FindProperty(arrayPath)?.DeleteArrayElementAtIndex(index);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return hits.Count;
        }

        private static bool TrySplitArrayElement(string propPath, out string arrayPath, out int index)
        {
            arrayPath = null;
            index = -1;
            const string marker = ".Array.data[";
            int at = propPath.LastIndexOf(marker, System.StringComparison.Ordinal);
            if (at < 0 || !propPath.EndsWith("]")) return false;

            arrayPath = propPath.Substring(0, at);
            return int.TryParse(propPath.Substring(at + marker.Length, propPath.Length - at - marker.Length - 1), out index);
        }

        /// <summary>같은 배열의 원소는 인덱스 숫자 순으로 비교한다(문자열 비교면 10이 9보다 앞선다).</summary>
        private class ArrayPathComparer : IComparer<string>
        {
            public int Compare(string a, string b)
            {
                bool ea = TrySplitArrayElement(a, out string pa, out int ia);
                bool eb = TrySplitArrayElement(b, out string pb, out int ib);
                if (ea && eb && pa == pb) return ia.CompareTo(ib);
                return string.CompareOrdinal(a, b);
            }
        }

        private static void Report(string path, int count)
        {
            if (count > 0) Debug.Log($"[SingleNoteMigration] 참조 {count}개 제거: {path}");
        }
    }
}
