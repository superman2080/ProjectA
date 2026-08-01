using System;
using System.Collections.Generic;
using System.Linq;
using PatternSpace;
using UnityEditor;
using UnityEngine;

namespace ChartGen
{
    /// <summary>음원을 분석해 채보(SongChart)를 굽거나, 기존 SongChart를 불러와 그룹별로 편집/저장하는 에디터 툴.</summary>
    public class PatternChartWindow : EditorWindow
    {
        private class ChartEntryDraft
        {
            /// <summary>null이면 이 그룹에 배정된 템플릿이 없다는 뜻(가정 9 — 시각적으로 강조 표시).</summary>
            public Pattern template;
            public float[] onsetTimes;
            public float[] exposureDurations;
            public float[] spawnTimes;

            /// <summary>전투 지시. 재분석해도 <b>엔트리 인덱스 기준으로 보존</b>된다 — 온셋만 갈아치우려다 저작이 날아가면 안 된다.</summary>
            public EnemySpace.EnemyCue enemyCue = new EnemySpace.EnemyCue();
        }

        /// <summary>cue는 참조 타입이라 그대로 넘기면 에셋과 드래프트가 같은 인스턴스를 공유한다. 반드시 복사한다.</summary>
        private static EnemySpace.EnemyCue CloneCue(EnemySpace.EnemyCue source)
        {
            if (source == null) return new EnemySpace.EnemyCue();

            return new EnemySpace.EnemyCue
            {
                killOnSuccess = source.killOnSuccess,
                projectile = source.projectile
            };
        }

        [MenuItem("Tools/Pattern Chart Tool")]
        private static void Open()
        {
            GetWindow<PatternChartWindow>("Pattern Chart Tool");
        }

        private AudioClip clip;
        private float rmsThreshold = 0.05f;
        private int windowSize = 1024;
        private float defaultExposureDuration = 0.5f;

        private float bpm = 120f;
        private float beatOffset = 0f;
        private int level = 1;
        private int tier1Max = 10;
        private int tier2Max = 20;
        private int maxGroupGapSteps = 1;

        private PatternHandler referenceHandler;
        private SongChart existingChart;

        private readonly List<ChartEntryDraft> drafts = new List<ChartEntryDraft>();
        private Vector2 scroll;

        /// <summary>이 곡이 쓸 패턴. 비우면 템플릿 폴더 전체를 쓴다. 저장 시 SongChart에 함께 기록된다.</summary>
        private readonly List<Pattern> patternPool = new List<Pattern>();
        private bool poolFoldout = true;

        /// <summary>일괄 처치 간격. 링 소모 속도를 정하는 값이라 눈에 보이게 둔다.</summary>
        private int killInterval = 4;

        private void OnGUI()
        {
            DrawSourceFields();
            EditorGUILayout.Space();
            DrawPatternPool();
            EditorGUILayout.Space();
            DrawGridFields();
            EditorGUILayout.Space();
            DrawActionButtons();
            EditorGUILayout.Space();

            if (drafts.Count > 0)
            {
                DrawMismatchBanner();
                DrawWaveform();
                EditorGUILayout.Space();
                DrawEntryList();
                EditorGUILayout.Space();
                DrawSaveButton();
            }
        }

        private void DrawSourceFields()
        {
            EditorGUILayout.LabelField("음원 / 대상 SongChart", EditorStyles.boldLabel);
            clip = (AudioClip)EditorGUILayout.ObjectField("Audio Clip", clip, typeof(AudioClip), false);
            existingChart = (SongChart)EditorGUILayout.ObjectField("기존 SongChart (불러오기용)", existingChart, typeof(SongChart), false);
            EditorGUI.BeginChangeCheck();
            referenceHandler = (PatternHandler)EditorGUILayout.ObjectField("씬 PatternHandler 참조", referenceHandler, typeof(PatternHandler), true);
            if (EditorGUI.EndChangeCheck())
            {
                // 핸들러가 없으면 스폰 시각을 못 구해 비어 있다 — 꽂는 즉시 되살린다.
                foreach (var d in drafts) RecomputeSpawnTimes(d);
            }

            if (referenceHandler == null)
                EditorGUILayout.HelpBox("스폰 시각 계산을 위해 씬의 PatternHandler를 지정해야 합니다. 지정 전까지 저장이 비활성화됩니다.", MessageType.Warning);
        }

        /// <summary>
        /// 이 곡이 쓸 패턴을 고른다. <b>비우면 템플릿 폴더 전체</b>를 쓴다(기존 동작).
        ///
        /// <para>굽기는 노드 개수별로 템플릿을 골라 쓰므로, 여기에 <b>어떤 개수가 들어 있는지</b>가
        /// 곧 어떤 청크로 쪼갤 수 있는지를 정한다. 그래서 개수 분포를 같이 보여 준다 —
        /// 3노드만 넣어 두면 2노드 청크에 배정할 게 없어 빈 엔트리가 나온다.</para>
        /// </summary>
        private void DrawPatternPool()
        {
            bool usingFolder = patternPool.Count(p => p != null) == 0;

            poolFoldout = EditorGUILayout.Foldout(poolFoldout,
                usingFolder ? "패턴 풀 — 폴더 전체 사용 중" : $"패턴 풀 — {patternPool.Count(p => p != null)}개 지정됨", true);
            if (!poolFoldout) return;

            EditorGUI.indentLevel++;

            for (int i = 0; i < patternPool.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                patternPool[i] = (Pattern)EditorGUILayout.ObjectField(patternPool[i], typeof(Pattern), false);
                if (GUILayout.Button("−", GUILayout.Width(24)))
                {
                    patternPool.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 추가")) patternPool.Add(null);
            if (GUILayout.Button("폴더에서 전부 채우기")) FillPoolFromFolder();
            using (new EditorGUI.DisabledScope(patternPool.Count == 0))
            {
                if (GUILayout.Button("비우기")) patternPool.Clear();
            }
            EditorGUILayout.EndHorizontal();

            if (usingFolder)
            {
                EditorGUILayout.HelpBox(
                    $"비어 있어 '{PatternTemplateLibrary.TemplateFolder}' 전체를 씁니다. " +
                    "곡에 맞는 패턴만 쓰려면 여기에 지정하세요.", MessageType.None);
            }
            else
            {
                // 노드 개수 분포가 곧 '쪼갤 수 있는 청크 크기'다. 비어 있는 개수가 있으면 빈 엔트리가 생긴다.
                var counts = patternPool.Where(p => p != null && p.AllData != null)
                    .GroupBy(p => p.AllData.Count).OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}노드×{g.Count()}");
                EditorGUILayout.LabelField("노드 개수 분포", string.Join("  ", counts));
            }

            EditorGUI.indentLevel--;
        }

        private void FillPoolFromFolder()
        {
            patternPool.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Pattern", new[] { PatternTemplateLibrary.TemplateFolder }))
            {
                var p = AssetDatabase.LoadAssetAtPath<Pattern>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null) patternPool.Add(p);
            }
        }

        private void DrawGridFields()
        {
            EditorGUILayout.LabelField("분석 파라미터", EditorStyles.boldLabel);
            rmsThreshold = EditorGUILayout.FloatField("RMS 임계값", rmsThreshold);
            windowSize = EditorGUILayout.IntField("윈도우 크기(샘플)", windowSize);
            defaultExposureDuration = EditorGUILayout.FloatField("기본 노출시간(초)", defaultExposureDuration);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("BPM / 비트 그리드", EditorStyles.boldLabel);
            bpm = EditorGUILayout.FloatField("BPM", bpm);
            beatOffset = EditorGUILayout.FloatField("Beat Offset(초)", beatOffset);
            level = EditorGUILayout.IntSlider("Level (1~30)", level, 1, 30);
            tier1Max = EditorGUILayout.IntField("Tier1 Max (4분음표 상한)", tier1Max);
            tier2Max = EditorGUILayout.IntField("Tier2 Max (8분음표 상한)", tier2Max);
            maxGroupGapSteps = EditorGUILayout.IntField("최대 그룹 갭(그리드 스텝)", maxGroupGapSteps);

            int subdivision = BeatGrid.LevelToSubdivision(level, tier1Max, tier2Max);
            EditorGUILayout.LabelField("현재 비트 분할 단위", $"1/{subdivision * 4}음표 (subdivisionsPerBeat = {subdivision})");
        }

        private BeatGrid BuildGrid()
        {
            int subdivision = BeatGrid.LevelToSubdivision(level, tier1Max, tier2Max);
            return new BeatGrid(bpm, beatOffset, subdivision);
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(clip == null))
            {
                if (GUILayout.Button("분석 (새로 만들기)"))
                    Analyze();
            }

            using (new EditorGUI.DisabledScope(existingChart == null))
            {
                if (GUILayout.Button("불러오기"))
                    LoadExisting();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void Analyze()
        {
            // 재분석은 온셋 구조만 다시 만든다 — 손으로 짠 전투 지시는 인덱스 기준으로 되살린다.
            var preservedCues = drafts.Select(d => CloneCue(d.enemyCue)).ToList();
            drafts.Clear();

            int channels = clip.channels;
            int sampleCount = clip.samples;
            var raw = new float[sampleCount * channels];
            clip.GetData(raw, 0);

            var mono = new float[sampleCount];
            if (channels <= 1)
            {
                Array.Copy(raw, mono, sampleCount);
            }
            else
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                        sum += raw[i * channels + c];
                    mono[i] = sum / channels;
                }
            }

            var rawOnsets = OnsetDetector.Detect(mono, clip.frequency, windowSize, rmsThreshold);
            var grid = BuildGrid();
            var snapped = BeatGrid.Snap(rawOnsets, grid);
            var groups = OnsetGrouper.Group(snapped, grid, maxGroupGapSteps);

            var library = PatternTemplateLibrary.From(patternPool);

            foreach (var group in groups)
            {
                var chunkSizes = OnsetChunkSplitter.Split(group.Count, library.AvailableNodeCounts);
                int offset = 0;

                foreach (int size in chunkSizes)
                {
                    var chunkOnsets = group.GetRange(offset, size).ToArray();
                    offset += size;

                    var draft = new ChartEntryDraft
                    {
                        template = library.GetNextTemplate(size),
                        onsetTimes = chunkOnsets,
                        exposureDurations = Enumerable.Repeat(defaultExposureDuration, size).ToArray(),
                    };

                    if (draft.template == null)
                        Debug.LogWarning($"[PatternChartWindow] 노드 {size}개짜리 청크에 배정할 템플릿이 없습니다. (온셋 시각: {chunkOnsets[0]:F2}s~)");

                    if (drafts.Count < preservedCues.Count)
                        draft.enemyCue = preservedCues[drafts.Count];

                    RecomputeSpawnTimes(draft);
                    drafts.Add(draft);
                }
            }
        }

        private void LoadExisting()
        {
            drafts.Clear();

            clip = existingChart.song;
            level = existingChart.level;
            bpm = existingChart.bpm;
            beatOffset = existingChart.beatOffset;

            // 풀도 같이 되살린다 — 안 그러면 재분석이 폴더 전체로 돌아가 다른 채보가 나온다.
            patternPool.Clear();
            if (existingChart.patternPool != null)
                patternPool.AddRange(existingChart.patternPool.Where(p => p != null));

            foreach (var entry in existingChart.entries)
            {
                var draft = new ChartEntryDraft
                {
                    template = entry.template,
                    onsetTimes = (float[])entry.onsetTimes.Clone(),
                    exposureDurations = (float[])entry.exposureDurations.Clone(),
                    enemyCue = CloneCue(entry.enemyCue),
                };

                RecomputeSpawnTimes(draft);
                drafts.Add(draft);
            }
        }

        private void RecomputeSpawnTimes(ChartEntryDraft draft)
        {
            if (draft.template == null || referenceHandler == null)
            {
                draft.spawnTimes = null;
                return;
            }

            draft.spawnTimes = new float[draft.onsetTimes.Length];
            for (int i = 0; i < draft.onsetTimes.Length; i++)
            {
                int pointIndex = draft.template.AllData[i].index;
                float duration = referenceHandler.ComputeFallDuration(pointIndex, draft.exposureDurations[i]);
                draft.spawnTimes[i] = draft.onsetTimes[i] - duration;
            }
        }

        private void DrawMismatchBanner()
        {
            int totalNodes = drafts.Sum(d => d.onsetTimes.Length);
            int assignedNodes = drafts.Where(d => d.template != null).Sum(d => d.onsetTimes.Length);

            if (totalNodes == assignedNodes)
            {
                EditorGUILayout.HelpBox($"모든 노드가 배정됨 ({assignedNodes}/{totalNodes})", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"{totalNodes - assignedNodes}개 노드가 아직 패턴에 배정되지 않았습니다 ({assignedNodes}/{totalNodes})",
                    MessageType.Error);
            }
        }

        private void DrawWaveform()
        {
            Rect rect = GUILayoutUtility.GetRect(position.width - 20, 120, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));

            if (clip == null || clip.length <= 0f) return;

            float halfHeight = rect.height * 0.5f;

            // 배정 실패 그룹 구간 강조
            foreach (var draft in drafts)
            {
                if (draft.template != null) continue;
                float startX = rect.x + draft.onsetTimes[0] / clip.length * rect.width;
                float endX = rect.x + draft.onsetTimes[^1] / clip.length * rect.width;
                EditorGUI.DrawRect(new Rect(startX, rect.y, Mathf.Max(endX - startX, 2f), rect.height), new Color(0.6f, 0.1f, 0.1f, 0.5f));
            }

            // 비트 그리드 세로선
            var grid = BuildGrid();
            int gridLineCount = Mathf.CeilToInt(clip.length / grid.GridInterval);
            for (int i = 0; i <= gridLineCount; i++)
            {
                float t = grid.GridIndexToTime(i);
                if (t < 0f || t > clip.length) continue;
                float x = rect.x + t / clip.length * rect.width;
                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.15f));
            }

            // 온셋 마커
            foreach (var draft in drafts)
            {
                foreach (var onset in draft.onsetTimes)
                {
                    float x = rect.x + onset / clip.length * rect.width;
                    EditorGUI.DrawRect(new Rect(x, rect.y, 1.5f, rect.height), draft.template != null ? Color.cyan : Color.red);
                }
            }

            EditorGUI.DrawRect(new Rect(rect.x, rect.y + halfHeight, rect.width, 1f), new Color(1f, 1f, 1f, 0.3f));
        }

        /// <summary>
        /// 전투 지시 일괄 설정. <b>엔트리가 수백 개라 하나씩은 못 만진다.</b>
        ///
        /// <para>처치 간격이 곧 링 소모 속도다 — 매 엔트리 처치면 링(기본 6명)이 몇 초 만에 마르고
        /// 스폰이 계속 따라붙어야 한다. 간격을 눈에 보이게 두어 그 판단을 하게 한다.</para>
        /// </summary>
        private void DrawBulkCueTools()
        {
            int killCount = drafts.Count(d => d.enemyCue != null && d.enemyCue.killOnSuccess);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"처치 지시 {killCount} / {drafts.Count}", EditorStyles.miniBoldLabel, GUILayout.Width(140));

            if (GUILayout.Button("전부 켜기", EditorStyles.miniButton)) SetAllKill(true);
            if (GUILayout.Button("전부 끄기", EditorStyles.miniButton)) SetAllKill(false);

            GUILayout.Label("매", EditorStyles.miniLabel, GUILayout.Width(20));
            killInterval = Mathf.Max(1, EditorGUILayout.IntField(killInterval, GUILayout.Width(36)));
            if (GUILayout.Button("번째만 처치", EditorStyles.miniButton)) ApplyKillInterval(killInterval);

            EditorGUILayout.EndHorizontal();
        }

        private void SetAllKill(bool value)
        {
            foreach (var d in drafts)
            {
                d.enemyCue ??= new EnemySpace.EnemyCue();
                d.enemyCue.killOnSuccess = value;
            }
        }

        /// <summary>N번째마다 처치. 첫 엔트리(인덱스 0)가 첫 처치가 되도록 나머지 0을 기준으로 잡는다.</summary>
        private void ApplyKillInterval(int interval)
        {
            for (int i = 0; i < drafts.Count; i++)
            {
                drafts[i].enemyCue ??= new EnemySpace.EnemyCue();
                drafts[i].enemyCue.killOnSuccess = i % interval == 0;
            }
        }

        private void DrawEntryList()
        {
            EditorGUILayout.LabelField("그룹(패턴) 목록", EditorStyles.boldLabel);
            DrawBulkCueTools();
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(250));

            for (int i = 0; i < drafts.Count; i++)
            {
                DrawEntryRow(i, drafts[i]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEntryRow(int index, ChartEntryDraft draft)
        {
            bool unassigned = draft.template == null;
            Color previousColor = GUI.backgroundColor;
            if (unassigned) GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = previousColor;

            int nodeCount = draft.onsetTimes.Length;
            string label = unassigned
                ? $"[{index}] 템플릿 없음 ({nodeCount}개 노드)"
                : $"[{index}] {draft.template.name} ({nodeCount}개 노드)";
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("재배정", GUILayout.Width(80)))
                Reassign(draft);

            var manualTemplate = (Pattern)EditorGUILayout.ObjectField(draft.template, typeof(Pattern), false);
            if (manualTemplate != draft.template)
                ManualAssign(draft, manualTemplate);
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < draft.exposureDurations.Length; i++)
            {
                float newDuration = EditorGUILayout.FloatField($"노드 {i} 노출시간", draft.exposureDurations[i]);
                if (!Mathf.Approximately(newDuration, draft.exposureDurations[i]))
                {
                    draft.exposureDurations[i] = newDuration;
                    RecomputeSpawnTimes(draft);
                }
            }

            DrawEnemyCue(index, draft);

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 전투 지시 편집(접이식). <b>"몇 번 적"은 없다</b> — 교전 상대는 EnemyDirector가 링 로스터에서 정하고,
        /// <c>killOnSuccess</c>가 곧 "다음 적으로 넘어가라"는 신호다.
        /// </summary>
        private void DrawEnemyCue(int index, ChartEntryDraft draft)
        {
            draft.enemyCue ??= new EnemySpace.EnemyCue();

            if (!cueFoldouts.TryGetValue(index, out bool expanded))
                expanded = false;

            // 역할과 임팩트 보정은 패턴이 정한다 — 여기서는 읽기만 한다.
            var template = draft.template;
            bool enemyIsAttacker = template != null && template.Attacker == EnemySpace.Attacker.Enemy;

            string summary = template == null
                ? "패턴 미지정"
                : (enemyIsAttacker ? "적 공격 (패링)" : "적 무방비 (공격)");
            if (draft.enemyCue.killOnSuccess) summary += " · 성공 시 처치";

            expanded = EditorGUILayout.Foldout(expanded, $"전투 — {summary}", true);
            cueFoldouts[index] = expanded;
            if (!expanded) return;

            EditorGUI.indentLevel++;

            // 패턴 소유 값 — 여기서 바꿀 수 없다는 것이 보이도록 비활성 필드로 그린다.
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup(
                    new GUIContent("공격 주체", "패턴 에셋에서 편집. 획 모양이 스윙을 정하고 스윙이 역할을 정한다."),
                    template != null ? template.Attacker : EnemySpace.Attacker.Player);
                EditorGUILayout.FloatField(
                    new GUIContent("임팩트 오프셋", "패턴 에셋에서 편집. 플레이어 칼·적 칼·시체 교체·투사체·카메라가 이 값 하나를 읽는다."),
                    template != null ? template.ImpactOffset : 0f);
                EditorGUILayout.FloatField(
                    new GUIContent("결투 거리 보정", "패턴 에셋(짝 에디터)에서 편집. 이 모션의 리치에 맞춘 ±m."),
                    template != null ? template.DuelDistanceOffset : 0f);
            }

            if (template != null)
            {
                EditorGUILayout.LabelField(" ", "↑ 패턴 에셋에서 편집합니다", EditorStyles.miniLabel);
            }

            draft.enemyCue.killOnSuccess = EditorGUILayout.Toggle(
                new GUIContent("성공 시 처치", "이 공격을 성공하면 현재 상대를 처치하고 다음 적으로 넘어간다. 실패하면 교전이 이어진다."),
                draft.enemyCue.killOnSuccess);
            draft.enemyCue.projectile = (SliceSpace.SliceSet)EditorGUILayout.ObjectField(
                new GUIContent("원거리 오브젝트", "일반 메쉬 모드로 구운 세트만 쓸 수 있다. 비우면 없음."),
                draft.enemyCue.projectile, typeof(SliceSpace.SliceSet), false);

            // 휴머노이드 세트는 산출물이 시체 프리팹이라 조각 배열이 비어 있다 —
            // 런타임에서 걸러지지만 그때는 이미 무연출이라, 저작 시점에 알려야 한다.
            if (draft.enemyCue.projectile != null && draft.enemyCue.projectile.Skinned)
            {
                EditorGUILayout.HelpBox(
                    $"'{draft.enemyCue.projectile.name}'은 휴머노이드(시체) 세트라 투사체로 쓸 수 없습니다.\n" +
                    "슬라이서의 '일반 메쉬' 모드로 구운 세트를 지정하세요.",
                    MessageType.Error);
            }

            EditorGUI.indentLevel--;
        }

        private readonly Dictionary<int, bool> cueFoldouts = new Dictionary<int, bool>();

        private void Reassign(ChartEntryDraft draft)
        {
            var library = PatternTemplateLibrary.From(patternPool);
            var replacement = library.GetRandomTemplate(draft.onsetTimes.Length);
            if (replacement == null)
            {
                Debug.LogWarning($"[PatternChartWindow] 노드 {draft.onsetTimes.Length}개짜리 템플릿이 라이브러리에 없습니다.");
                return;
            }

            draft.template = replacement;
            RecomputeSpawnTimes(draft);
        }

        private void ManualAssign(ChartEntryDraft draft, Pattern candidate)
        {
            if (candidate == null)
            {
                draft.template = null;
                draft.spawnTimes = null;
                return;
            }

            if (candidate.AllData.Count != draft.onsetTimes.Length)
            {
                Debug.LogWarning($"[PatternChartWindow] '{candidate.name}'의 노드 개수({candidate.AllData.Count})가 이 그룹의 노드 개수({draft.onsetTimes.Length})와 다릅니다. 배정을 거부합니다.");
                return;
            }

            draft.template = candidate;
            RecomputeSpawnTimes(draft);
        }

        private void DrawSaveButton()
        {
            bool hasUnassigned = drafts.Any(d => d.template == null);
            bool canSave = !hasUnassigned && referenceHandler != null;

            using (new EditorGUI.DisabledScope(!canSave))
            {
                if (GUILayout.Button(existingChart != null ? "저장 (기존 SongChart 덮어쓰기)" : "저장 (새 SongChart 생성)"))
                    Save();
            }

            if (hasUnassigned)
                EditorGUILayout.HelpBox("배정되지 않은 그룹이 있습니다. 저장하려면 모든 그룹에 템플릿을 배정하세요.", MessageType.Error);
        }

        private void Save()
        {
            // 스폰 시각은 '굽는 시점의 계산값 스냅샷'이라 낡을 수 있다. 저장 직전에 전부 다시 계산한다.
            //
            // 특히 referenceHandler를 나중에 꽂으면 LoadExisting 시점에는 null이라 spawnTimes가 비어 있고,
            // 그 뒤 저장 버튼만 활성화돼 그대로 저장하려다 터진다. 여기서 다시 계산하면 그 경로가 닫힌다.
            foreach (var d in drafts) RecomputeSpawnTimes(d);

            var invalid = drafts.FindIndex(d => d.spawnTimes == null);
            if (invalid >= 0)
            {
                Debug.LogError($"[PatternChartWindow] {invalid}번 그룹의 스폰 시각을 계산하지 못했습니다 " +
                               "(템플릿 또는 씬 PatternHandler 확인). 저장하지 않았습니다.");
                return;
            }

            var entries = drafts.Select(d => new SongChartEntry
            {
                template = d.template,
                onsetTimes = (float[])d.onsetTimes.Clone(),
                exposureDurations = (float[])d.exposureDurations.Clone(),
                spawnTimes = (float[])d.spawnTimes.Clone(),
                enemyCue = CloneCue(d.enemyCue),
            }).ToArray();

            SongChart target = existingChart;
            bool isNew = target == null;
            if (isNew)
                target = CreateInstance<SongChart>();

            Undo.RecordObject(target, "Save Song Chart");
            target.song = clip;
            target.level = level;
            target.bpm = bpm;
            target.beatOffset = beatOffset;
            target.patternPool = patternPool.Where(p => p != null).ToArray();
            target.entries = entries;

            if (isNew)
            {
                string defaultFolder = "Assets/04. Datas/Song";
                if (!AssetDatabase.IsValidFolder(defaultFolder))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/04. Datas"))
                        AssetDatabase.CreateFolder("Assets", "04. Datas");
                    AssetDatabase.CreateFolder("Assets/04. Datas", "Song");
                }
                string songName = clip != null ? clip.name : "SongChart";
                string defaultName = $"{songName}_Lv{level}";
                string path = EditorUtility.SaveFilePanelInProject("SongChart 저장", defaultName, "asset", "SongChart를 저장할 위치를 선택하세요.", defaultFolder);
                if (string.IsNullOrEmpty(path)) return;

                AssetDatabase.CreateAsset(target, path);
                existingChart = target;
            }
            else
            {
                EditorUtility.SetDirty(target);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
