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

        private void OnGUI()
        {
            DrawSourceFields();
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
            referenceHandler = (PatternHandler)EditorGUILayout.ObjectField("씬 PatternHandler 참조", referenceHandler, typeof(PatternHandler), true);

            if (referenceHandler == null)
                EditorGUILayout.HelpBox("스폰 시각 계산을 위해 씬의 PatternHandler를 지정해야 합니다. 지정 전까지 저장이 비활성화됩니다.", MessageType.Warning);
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

            var library = new PatternTemplateLibrary();

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

            foreach (var entry in existingChart.entries)
            {
                var draft = new ChartEntryDraft
                {
                    template = entry.template,
                    onsetTimes = (float[])entry.onsetTimes.Clone(),
                    exposureDurations = (float[])entry.exposureDurations.Clone(),
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

        private void DrawEntryList()
        {
            EditorGUILayout.LabelField("그룹(패턴) 목록", EditorStyles.boldLabel);
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

            EditorGUILayout.EndVertical();
        }

        private void Reassign(ChartEntryDraft draft)
        {
            var library = new PatternTemplateLibrary();
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
            var entries = drafts.Select(d => new SongChartEntry
            {
                template = d.template,
                onsetTimes = (float[])d.onsetTimes.Clone(),
                exposureDurations = (float[])d.exposureDurations.Clone(),
                spawnTimes = (float[])d.spawnTimes.Clone(),
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
