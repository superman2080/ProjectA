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

        /// <summary>패턴 단위 일괄 처치의 대상 템플릿.</summary>
        private Pattern killPattern;

        /// <summary>사슬 길이(한 적에게 이어 치는 패턴 수). 마지막 타가 마무리다.</summary>
        private int chainLength = 3;

        /// <summary>엔트리별 쪼개기 지점(앞 조각의 노드 수). 행마다 다른 값을 만지므로 인덱스 키로 든다.</summary>
        private readonly Dictionary<int, int> splitCounts = new Dictionary<int, int>();

        /// <summary>엔트리별 "여기부터 N타 사슬"의 N.</summary>
        private readonly Dictionary<int, int> chainStarts = new Dictionary<int, int>();

        /// <summary>그리기가 끝난 뒤 실행할 리스트 변경. 그리는 도중 엔트리를 삽입하면 GUILayout이 터진다.</summary>
        private Action pending;

        /// <summary>파형에서 고른 엔트리. -1이면 선택 없음.</summary>
        private int selectedEntry = -1;

        /// <summary>다음 Repaint에서 목록을 선택 행으로 스크롤할 것. 행 높이가 가변이라 실제 rect를 재기 전에는 위치를 모른다.</summary>
        private bool scrollToSelection;

        /// <summary>
        /// 사슬을 처치로 인정할 성공 타수의 비율. <b>런타임 값은 씬의 <c>EnemyDirector.chainKillRatio</c>다</b> —
        /// 툴은 씬을 모르므로 같은 기본값을 복제해 저작 시 필요 타수를 보여 주기만 한다.
        /// </summary>
        private float chainKillRatio = 0.6f;

        /// <summary>EnemyDirector.RequiredHits와 <b>같은 식</b>이어야 한다 — 다르면 툴이 거짓말을 한다.</summary>
        private int RequiredHits(int length) => Mathf.Max(1, Mathf.CeilToInt(length * chainKillRatio));

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

            FlushPending();
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
            ClearIndexedState();

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
            ClearIndexedState();

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

        /// <summary>엔트리 인덱스를 키로 든 UI 상태를 전부 버린다. 리스트가 바뀌면 그 키들이 다 어긋난다.</summary>
        private void ClearIndexedState()
        {
            cueFoldouts.Clear();
            splitCounts.Clear();
            chainStarts.Clear();
        }

        private void RecomputeSpawnTimes(ChartEntryDraft draft)
        {
            if (draft.template == null || referenceHandler == null)
            {
                draft.spawnTimes = null;
                return;
            }

            // 템플릿의 노드 수와 이 그룹의 온셋 수가 어긋나면 계산 자체가 성립하지 않는다.
            // 던지지 않고 spawnTimes를 비워 둔다 — 저장이 그 값을 보고 멈추므로(Save) 조용히 틀린 채보가 안 나온다.
            int nodeCount = draft.template.AllData == null ? 0 : draft.template.AllData.Count;
            if (nodeCount != draft.onsetTimes.Length)
            {
                Debug.LogWarning($"[PatternChartWindow] '{draft.template.name}'의 노드 수({nodeCount})가 " +
                                 $"이 그룹의 온셋 수({draft.onsetTimes.Length})와 다릅니다. 스폰 시각을 계산하지 않았습니다 — " +
                                 "쪼개기/합치기로 크기를 맞추거나 다른 템플릿을 배정하세요.");
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
                return;
            }

            EditorGUILayout.HelpBox(
                $"{totalNodes - assignedNodes}개 노드가 아직 패턴에 배정되지 않았습니다 ({assignedNodes}/{totalNodes})",
                MessageType.Error);

            // 자투리는 대개 "풀에 그 노드 수의 패턴이 없다"라서 남는다 — 이웃끼리 합치면 맞는 크기가 된다.
            if (GUILayout.Button(new GUIContent("배정 안 된 이웃끼리 합치기",
                    "미배정 엔트리의 연속 구간을 하나로 모으고, 합친 크기에 맞는 템플릿이 풀에 있으면 바로 배정한다. 배정된 엔트리는 경계로 남는다.")))
                pending = MergeUnassigned;
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

            // 선택 구간 강조 — 마커보다 먼저 깔아야 마커가 위에 남는다.
            if (selectedEntry >= 0 && selectedEntry < drafts.Count)
            {
                var picked = drafts[selectedEntry];
                float startX = rect.x + picked.onsetTimes[0] / clip.length * rect.width;
                float endX = rect.x + picked.onsetTimes[^1] / clip.length * rect.width;
                EditorGUI.DrawRect(new Rect(startX - 2f, rect.y, Mathf.Max(endX - startX, 2f) + 4f, rect.height), new Color(1f, 0.9f, 0.2f, 0.35f));
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

            HandleWaveformClick(rect);
        }

        /// <summary>
        /// 파형의 온셋 마커를 클릭하면 아래 목록을 그 엔트리로 스크롤한다.
        ///
        /// <para>엔트리가 수백 개라 목록만으로는 "이 소리가 몇 번 그룹인지"를 찾을 방법이 없다 —
        /// 파형이 유일한 색인이다. 배정 실패(빨강) 그룹도 똑같이 잡히게 둔다: 고쳐야 할 그룹이 정확히 그것들이다.</para>
        /// </summary>
        private void HandleWaveformClick(Rect rect)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return;

            int hit = NearestDraftIndex((e.mousePosition.x - rect.x) / rect.width * clip.length);
            if (hit < 0) return;

            selectedEntry = hit;
            scrollToSelection = true;
            GUI.FocusControl(null); // 편집 중이던 필드가 포커스를 붙들고 있으면 스크롤이 도로 끌려간다.
            e.Use();
            Repaint();
        }

        /// <summary>
        /// 시각에 가장 가까운 온셋을 가진 엔트리. 그룹 <b>구간</b>이 아니라 마커 하나하나와 견준다 —
        /// 화면에 그려진 것이 그 선이라, 구간으로 재면 그룹 사이 빈 곳에서 눈에 보이는 선과 다른 답이 나온다.
        /// </summary>
        private int NearestDraftIndex(float time)
        {
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < drafts.Count; i++)
            {
                foreach (float onset in drafts[i].onsetTimes)
                {
                    float distance = Mathf.Abs(onset - time);
                    if (distance >= bestDistance) continue;

                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// 전투 지시 일괄 설정. <b>엔트리가 수백 개라 하나씩은 못 만진다.</b>
        ///
        /// <para>처치 간격이 곧 링 소모 속도다 — 매 엔트리 처치면 링(기본 6명)이 몇 초 만에 마르고
        /// 스폰이 계속 따라붙어야 한다. 간격을 눈에 보이게 두어 그 판단을 하게 한다.</para>
        ///
        /// <para><b>패턴 단위 일괄도 같이 둔다.</b> 처치가 어울리는지는 인덱스가 아니라 <b>동작</b>이
        /// 정한다 — 마무리로 읽히는 획(<c>Pattern.PlayerAttack</c>)만 죽이고 견제 획은 안 죽이는 식이다.
        /// 같은 패턴이 채보 전체에 흩어져 있어 인덱스 규칙으로는 그 저작 의도를 표현할 수 없다.</para>
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
            if (GUILayout.Button(new GUIContent("번째만 처치", "첫 타가 마무리가 된다([처치][비][비]). 사슬을 만들려면 옆의 '사슬 길이'를 쓸 것."),
                                 EditorStyles.miniButton))
                ApplyKillInterval(killInterval);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("사슬(연계)", EditorStyles.miniBoldLabel, GUILayout.Width(140));

            GUILayout.Label("길이", EditorStyles.miniLabel, GUILayout.Width(28));
            chainLength = Mathf.Max(1, EditorGUILayout.IntField(chainLength, GUILayout.Width(36)));
            if (GUILayout.Button(new GUIContent("사슬로 굽기", "한 적에게 이 개수만큼 이어 친다 — 마지막 타가 마무리다([비][비][처치])."),
                                 EditorStyles.miniButton))
                ApplyChainLength(chainLength);

            GUILayout.Label("처치 비율", EditorStyles.miniLabel, GUILayout.Width(56));
            chainKillRatio = Mathf.Clamp01(EditorGUILayout.FloatField(chainKillRatio, GUILayout.Width(36)));
            GUILayout.Label($"→ {chainLength}타 중 {RequiredHits(chainLength)}타 필요", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            int matchCount = killPattern != null ? drafts.Count(d => d.template == killPattern) : 0;
            EditorGUILayout.LabelField($"패턴 단위 ({matchCount}개)", EditorStyles.miniBoldLabel, GUILayout.Width(140));

            killPattern = (Pattern)EditorGUILayout.ObjectField(killPattern, typeof(Pattern), false);

            using (new EditorGUI.DisabledScope(killPattern == null))
            {
                if (GUILayout.Button("이 패턴 켜기", EditorStyles.miniButton)) SetKillForPattern(killPattern, true);
                if (GUILayout.Button("이 패턴 끄기", EditorStyles.miniButton)) SetKillForPattern(killPattern, false);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>지정 패턴을 쓰는 엔트리만 처치 지시를 설정한다. 다른 패턴 엔트리는 건드리지 않는다.</summary>
        private void SetKillForPattern(Pattern template, bool value)
        {
            foreach (var d in drafts)
            {
                if (d.template != template) continue;

                d.enemyCue ??= new EnemySpace.EnemyCue();
                d.enemyCue.killOnSuccess = value;
            }
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

        /// <summary>
        /// 길이 N의 사슬로 굽는다 — <b>마지막 타가 마무리다</b>([비][비][처치]).
        /// <see cref="ApplyKillInterval"/>과 위상이 반대라는 점이 핵심이다: 거기서는 첫 타가 마무리라
        /// 사슬을 만들려고 쓰면 <b>첫 타에 적이 죽고 나머지가 새 적에게 간다</b>.
        /// </summary>
        private void ApplyChainLength(int length)
        {
            for (int i = 0; i < drafts.Count; i++)
            {
                drafts[i].enemyCue ??= new EnemySpace.EnemyCue();
                drafts[i].enemyCue.killOnSuccess = i % length == length - 1;
            }
        }

        /// <summary>
        /// 이 엔트리가 속한 사슬에서 몇 번째 타이고 그 사슬이 몇 타인가(1-based). 처치 지시가 사슬의 끝이다.
        /// 마지막 사슬이 마무리 없이 끝나면 <paramref name="closed"/>가 false — 그 적은 곡이 끝나도 남는다.
        /// </summary>
        private (int position, int length, bool closed) ChainInfoAt(int index)
        {
            int start = 0;
            for (int i = index - 1; i >= 0; i--)
            {
                if (drafts[i].enemyCue != null && drafts[i].enemyCue.killOnSuccess) { start = i + 1; break; }
            }

            for (int i = index; i < drafts.Count; i++)
            {
                if (drafts[i].enemyCue != null && drafts[i].enemyCue.killOnSuccess)
                    return (index - start + 1, i - start + 1, true);
            }

            return (index - start + 1, drafts.Count - start, false);
        }

        /// <summary>사슬 안에서 공격 주체가 갈리는가. 갈리면 그 타에서만 넉백이 살아나 재접근이 늘어진다.</summary>
        private bool ChainMixesAttackers(int start, int length)
        {
            EnemySpace.Attacker? first = null;

            for (int i = start; i < start + length && i < drafts.Count; i++)
            {
                if (drafts[i].template == null) continue;

                var attacker = drafts[i].template.Attacker;
                if (first == null) first = attacker;
                else if (first != attacker) return true;
            }

            return false;
        }

        private void DrawEntryList()
        {
            EditorGUILayout.LabelField("그룹(패턴) 목록", EditorStyles.boldLabel);
            DrawBulkCueTools();
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(250));

            // 내용의 시작 y. 행 높이가 가변(경고 박스·사슬 뱃지)이라 인덱스 × 상수로는 위치를 계산할 수 없어 실제 rect를 잰다.
            float contentTop = GUILayoutUtility.GetRect(0f, 0f).y;

            for (int i = 0; i < drafts.Count; i++)
            {
                DrawEntryRow(i, drafts[i]);

                // rect는 Repaint 패스에서만 확정된다. 여기서 scroll을 바꿔도 이 프레임은 이미 옛 위치로 그려졌으니 한 번 더 그린다.
                if (i != selectedEntry || !scrollToSelection || Event.current.type != EventType.Repaint) continue;

                scroll.y = GUILayoutUtility.GetLastRect().y - contentTop;
                scrollToSelection = false;
                Repaint();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 리스트를 바꾸는 작업(쪼개기·합치기·재배정)은 <b>그리기가 전부 끝난 뒤</b> 실행한다 —
        /// 도중에 엔트리를 삽입·삭제하면 Layout과 Repaint 사이에 컨트롤 수가 달라져 GUILayout이 터진다.
        /// </summary>
        private void FlushPending()
        {
            if (pending == null) return;

            var action = pending;
            pending = null;
            action();
            Repaint();
        }

        private void DrawEntryRow(int index, ChartEntryDraft draft)
        {
            bool unassigned = draft.template == null;
            Color previousColor = GUI.backgroundColor;
            // 배정 실패(빨강)가 선택 강조보다 우선한다 — 저장을 막는 상태라 선택 때문에 가려지면 안 된다.
            if (unassigned) GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            else if (index == selectedEntry) GUI.backgroundColor = new Color(0.5f, 0.85f, 1f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = previousColor;

            int nodeCount = draft.onsetTimes.Length;
            string label = unassigned
                ? $"[{index}] 템플릿 없음 ({nodeCount}개 노드)"
                : $"[{index}] {draft.template.name} ({nodeCount}개 노드)";

            // 사슬 뱃지 — 엔트리가 수백 개라 토글 하나만 보고는 몇 번째 타인지 셀 수 없다.
            var chain = ChainInfoAt(index);
            if (chain.length > 1)
                label += $"   ⛓ 사슬 {chain.position}/{chain.length} · {RequiredHits(chain.length)}타 이상 필요";

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            // 배정은 됐는데 크기가 안 맞는 상태. 저장이 여기서 멈추므로 행에서 바로 보이게 한다.
            if (!unassigned && draft.spawnTimes == null && referenceHandler != null)
            {
                int templateNodes = draft.template.AllData == null ? 0 : draft.template.AllData.Count;
                EditorGUILayout.HelpBox(
                    $"템플릿 노드 수({templateNodes})가 이 그룹({nodeCount})과 다릅니다. 이대로는 저장되지 않습니다 — " +
                    "쪼개기/합치기로 맞추거나 다른 템플릿을 배정하세요.",
                    MessageType.Error);
            }

            if (chain.length > 1 && chain.position == 1)
            {
                if (!chain.closed)
                {
                    EditorGUILayout.HelpBox(
                        "이 사슬에 마무리(성공 시 처치)가 없습니다 — 곡이 끝나도 그 적이 남습니다.",
                        MessageType.Warning);
                }

                if (ChainMixesAttackers(index, chain.length))
                {
                    EditorGUILayout.HelpBox(
                        "이 사슬 안에 공격 주체가 섞여 있습니다. Attacker.Enemy 타에서만 적이 1.5m 밀려나고, " +
                        "그 재접근은 창에 맞춰지지 않아 기어갑니다(docs/FailConverge).",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("재배정", GUILayout.Width(60)))
                Reassign(draft);

            var manualTemplate = (Pattern)EditorGUILayout.ObjectField(draft.template, typeof(Pattern), false);
            if (manualTemplate != draft.template)
            {
                // 배정이 엔트리를 쪼갤 수 있다 — 그리는 도중에 리스트를 건드리면 레이아웃이 깨진다.
                int captured = index;
                pending = () => ManualAssign(captured, drafts[captured], manualTemplate);
            }
            EditorGUILayout.EndHorizontal();

            // 쪼개기 / 사슬 — 둘 다 이 엔트리를 기준점으로 삼는다.
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(nodeCount < 2))
            {
                GUILayout.Label("앞", EditorStyles.miniLabel, GUILayout.Width(18));
                if (!splitCounts.TryGetValue(index, out int splitAt)) splitAt = nodeCount / 2;
                splitAt = Mathf.Clamp(EditorGUILayout.IntField(splitAt, GUILayout.Width(28)), 1, Mathf.Max(nodeCount - 1, 1));
                splitCounts[index] = splitAt;

                if (GUILayout.Button(new GUIContent("개로 쪼개기", "이 엔트리를 둘로 나눈다. 남은 노드는 뒤에 새 엔트리로 남아 다시 배정할 수 있다."),
                                     EditorStyles.miniButton, GUILayout.Width(80)))
                {
                    int captured = index, at = splitAt;
                    pending = () => SplitDraft(captured, at);
                }
            }

            using (new EditorGUI.DisabledScope(index + 1 >= drafts.Count))
            {
                if (GUILayout.Button(new GUIContent("아래와 합치기", "다음 엔트리를 흡수해 하나로 만든다. 노드 수가 달라지므로 템플릿은 비워진다."),
                                     EditorStyles.miniButton, GUILayout.Width(84)))
                {
                    int captured = index;
                    pending = () => MergeWithNext(captured);
                }
            }

            GUILayout.Space(12);
            GUILayout.Label("여기부터", EditorStyles.miniLabel, GUILayout.Width(52));
            if (!chainStarts.TryGetValue(index, out int len)) len = chainLength;
            len = Mathf.Max(1, EditorGUILayout.IntField(len, GUILayout.Width(28)));
            chainStarts[index] = len;

            if (GUILayout.Button(new GUIContent("타 사슬", "이 엔트리부터 N개를 한 사슬로 묶는다(마지막만 처치). 나머지 엔트리는 안 건드린다."),
                                 EditorStyles.miniButton, GUILayout.Width(60)))
                ApplyChainAt(index, len);

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

            DrawFeint(draft, index);

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

        // ── 견제 클립 정보 ──────────────────────────────────────────────────

        /// <summary>판정 종료가 마지막 노드에서 얼마나 뒤인지(PatternHandler.goodWindow). 창 표시는 근사면 충분하다.</summary>
        private const float GoodWindowApprox = 0.1f;

        /// <summary>
        /// 견제 클립(<c>Pattern.EnemyFeint</c>)을 확인용으로 보여 준다. <b>여기서 편집하지 않는다</b> —
        /// 패턴 소유 값이라 위의 회색 필드들과 같은 규율이다.
        ///
        /// <para><b>이 엔트리의 실제 창을 같이 적는다.</b> 클립이 창보다 길면 배속으로 압축되거나 잘리는데,
        /// 그 판단은 클립 길이만 봐서는 할 수 없고 <b>직전 엔트리와의 간격</b>을 알아야 한다.
        /// 저작자가 그 자리에서 판단할 수 있어야 트림 길이를 고칠 수 있다.</para>
        ///
        /// <para>⚠ <b>여기에 클립 프리뷰를 넣지 않는다.</b> 내장 <c>AnimationClipEditor</c>를
        /// <c>Editor.CreateEditor</c>로 만들어 <c>OnInteractivePreviewGUI</c>를 부르면
        /// 인스펙터 밖에서는 내부 아바타 프리뷰가 초기화되지 않아 매 프레임 NullReferenceException이 터진다
        /// (견제 클립이 배정된 엔트리를 그리는 순간 콘솔이 막힌다). 그래서 트림 길이를 숫자로 병기하고,
        /// 정밀 저작은 <c>Pattern Action Editor</c>로 넘긴다.</para>
        /// </summary>
        private void DrawFeint(ChartEntryDraft draft, int index)
        {
            var template = draft.template;
            if (template == null) return;
            if (template.Attacker == EnemySpace.Attacker.Enemy) return; // 그 구간은 EnemyAttack이 채운다

            var feint = template.EnemyFeint;

            if (feint?.Clip == null)
            {
                EditorGUILayout.LabelField("견제 동작", "없음 — 표적이 된 뒤 임팩트까지 기본 Idle로 선다", EditorStyles.miniLabel);
                DrawOpenActionEditorButton(template);
                return;
            }

            float trim = feint.ResolvedDuration / feint.Speed;
            float window = ResolveFeintWindow(index, template);

            EditorGUILayout.LabelField("견제 동작",
                $"{feint.Clip.name} · 트림 {trim:0.00}s / 창 {window:0.00}s");

            if (window > 0f && trim > window)
            {
                EditorGUILayout.HelpBox(
                    $"클립({trim:0.00}s)이 이 엔트리의 창({window:0.00}s)보다 깁니다 — 배속으로 압축되거나 임팩트에서 잘립니다.\n" +
                    "트림을 줄이세요(0.8초 이하 권장).",
                    MessageType.Warning);
            }

            DrawOpenActionEditorButton(template);
        }

        private void DrawOpenActionEditorButton(Pattern template)
        {
            if (GUILayout.Button("Pattern Action Editor로 열기"))
                AnimationClipTrimmerWindow.Open(template);
        }

        /// <summary>
        /// 이 엔트리의 견제 창 — 직전 엔트리가 끝나는 순간(= 표적이 되는 순간)부터 임팩트까지.
        /// 첫 엔트리는 직전이 없어 0을 돌려주고, 그때는 경고를 띄우지 않는다.
        /// </summary>
        private float ResolveFeintWindow(int index, Pattern template)
        {
            if (index <= 0 || index >= drafts.Count) return 0f;

            var prev = drafts[index - 1];
            var cur = drafts[index];
            if (prev.onsetTimes == null || prev.onsetTimes.Length == 0) return 0f;
            if (cur.onsetTimes == null || cur.onsetTimes.Length == 0) return 0f;

            float prevLast = prev.onsetTimes[prev.onsetTimes.Length - 1];
            float curLast = cur.onsetTimes[cur.onsetTimes.Length - 1];

            return curLast + GoodWindowApprox + template.ImpactOffset - prevLast;
        }

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

        /// <summary>
        /// 엔트리에 템플릿을 손으로 배정한다.
        ///
        /// <para><b>노드 수가 적은 패턴은 거부하지 않고 엔트리를 쪼갠다.</b> 4노드 그룹에 2노드 패턴을 넣으면
        /// 앞 2노드가 이 패턴이 되고 남은 2노드는 <b>뒤에 새 엔트리로</b> 남아 또 배정할 수 있다 —
        /// 그게 "4노드 자리를 2노드 패턴 둘로 갈아 끼운다"이다. 예전에는 이 경우가 경고 한 줄로 막혀 있었다.</para>
        ///
        /// <para>반대로 <b>더 큰 패턴은 여전히 거부한다</b> — 뒤 엔트리를 삼켜 합치는 것은 온셋 그룹 경계를
        /// 넘는 일이라 분할과 대칭이 아니다(그건 재분석의 몫이다).</para>
        /// </summary>
        private void ManualAssign(int index, ChartEntryDraft draft, Pattern candidate)
        {
            if (candidate == null)
            {
                draft.template = null;
                draft.spawnTimes = null;
                return;
            }

            int want = candidate.AllData == null ? 0 : candidate.AllData.Count;
            int have = draft.onsetTimes.Length;

            // 노드가 없는 패턴은 배정할 수 없다 — 갓 만든 빈 에셋이 이 경로로 들어온다.
            // 아래 분할이 '0개로 쪼개기'를 무시하고 지나가므로, 여기서 막지 않으면 노드 수가 어긋난 채 꽂힌다.
            if (want <= 0)
            {
                Debug.LogWarning($"[PatternChartWindow] '{candidate.name}'에 노드가 없습니다. " +
                                 "패턴 에셋의 Pattern Datas를 채운 뒤 배정하세요.");
                return;
            }

            if (want > have)
            {
                Debug.LogWarning($"[PatternChartWindow] '{candidate.name}'의 노드 개수({want})가 이 그룹({have})보다 많습니다. " +
                                 "'아래와 합치기'로 뒤 엔트리를 흡수해 크기를 맞춘 뒤 배정하세요 — " +
                                 "합치기는 온셋 그룹 경계를 넘을 수 있어 자동으로 하지 않습니다.");
                return;
            }

            if (want < have) SplitDraft(index, want);

            draft.template = candidate;
            RecomputeSpawnTimes(draft);
        }

        /// <summary>
        /// <paramref name="index"/> 엔트리를 <paramref name="firstCount"/>개와 나머지로 <b>둘로 쪼갠다</b>.
        /// 남은 조각은 바로 뒤 엔트리로 삽입되고 템플릿은 비워진다(노드 수가 달라졌으므로).
        ///
        /// <para><b>앞 조각의 처치 지시는 꺼진다.</b> 쪼갠다는 것은 곧 "한 덩어리를 여러 타로 나눈다"이고,
        /// 그 중간 타에서 적이 죽으면 나머지 타가 새 적에게 간다 — 즉 쪼갠 결과는 자연히 사슬이다.
        /// 원래 cue(처치 지시 포함)는 <b>뒤 조각</b>이 물려받아 마무리가 된다.</para>
        /// </summary>
        private void SplitDraft(int index, int firstCount)
        {
            var draft = drafts[index];
            int have = draft.onsetTimes.Length;

            if (firstCount <= 0 || firstCount >= have) return;

            var tail = new ChartEntryDraft
            {
                template = null, // 노드 수가 달라졌다 — 다시 골라야 한다
                onsetTimes = draft.onsetTimes.Skip(firstCount).ToArray(),
                exposureDurations = draft.exposureDurations.Skip(firstCount).ToArray(),
                enemyCue = draft.enemyCue, // 마무리(처치 지시)는 뒤 조각이 물려받는다
            };

            draft.onsetTimes = draft.onsetTimes.Take(firstCount).ToArray();
            draft.exposureDurations = draft.exposureDurations.Take(firstCount).ToArray();
            draft.template = null;
            draft.enemyCue = CloneCue(draft.enemyCue);
            draft.enemyCue.killOnSuccess = false; // 앞 조각은 사슬 중간 타다

            RecomputeSpawnTimes(draft);
            RecomputeSpawnTimes(tail);
            drafts.Insert(index + 1, tail);

            // 인덱스를 키로 쓰는 상태는 삽입으로 통째로 밀린다. 되살리는 것보다 버리는 게 싸다.
            ClearIndexedState();
        }

        /// <summary>
        /// <paramref name="index"/> 엔트리와 <b>바로 다음 엔트리를 하나로 합친다</b>(<see cref="SplitDraft"/>의 역).
        /// 노드 수가 달라졌으므로 템플릿은 비워지고, 뒤 엔트리의 cue가 살아남는다(마무리는 언제나 뒤쪽이다).
        ///
        /// <para><b>원래 그룹 경계는 음악이 정했다</b>(<c>maxGroupGapSteps</c>). 큰 공백을 넘겨 합치면
        /// 쉬는 구간을 가로지르는 패턴이 되므로 그때는 경고한다 — 막지는 않는다(의도한 저작일 수 있다).</para>
        /// </summary>
        private void MergeWithNext(int index)
        {
            if (index < 0 || index + 1 >= drafts.Count) return;

            var head = drafts[index];
            var tail = drafts[index + 1];

            float gap = tail.onsetTimes[0] - head.onsetTimes[^1];
            float threshold = BuildGrid().GridInterval * (maxGroupGapSteps + 1);
            if (gap > threshold)
            {
                Debug.LogWarning($"[PatternChartWindow] 엔트리 {index}와 {index + 1} 사이 간격이 {gap:F2}초로 " +
                                 $"그룹 경계({threshold:F2}초)보다 넓습니다. 쉬는 구간을 가로지르는 패턴이 됩니다.");
            }

            head.onsetTimes = head.onsetTimes.Concat(tail.onsetTimes).ToArray();
            head.exposureDurations = head.exposureDurations.Concat(tail.exposureDurations).ToArray();
            head.template = null;      // 노드 수가 달라졌다 — 다시 골라야 한다
            head.enemyCue = tail.enemyCue;

            drafts.RemoveAt(index + 1);
            RecomputeSpawnTimes(head);
            ClearIndexedState();
        }

        /// <summary>
        /// <b>배정되지 않은 이웃끼리 합친다.</b> 청킹이 남긴 자투리(풀에 그 노드 수의 패턴이 없어 빈 엔트리)를
        /// 연속 구간 단위로 하나로 모으고, 합친 크기에 맞는 템플릿이 있으면 바로 배정한다.
        ///
        /// <para>배정된 엔트리는 경계로 남는다 — 이미 저작된 것을 삼키지 않는다.</para>
        /// </summary>
        private void MergeUnassigned()
        {
            var library = PatternTemplateLibrary.From(patternPool);
            int merged = 0, assigned = 0;

            for (int i = 0; i < drafts.Count; i++)
            {
                if (drafts[i].template != null) continue;

                // 이 엔트리에서 시작하는 미배정 연속 구간을 통째로 끌어당긴다.
                while (i + 1 < drafts.Count && drafts[i + 1].template == null)
                {
                    MergeWithNext(i);
                    merged++;
                }

                var fit = library.GetNextTemplate(drafts[i].onsetTimes.Length);
                if (fit != null)
                {
                    drafts[i].template = fit;
                    RecomputeSpawnTimes(drafts[i]);
                    assigned++;
                }
            }

            Debug.Log($"[PatternChartWindow] 미배정 병합: {merged}건 합침, 그중 {assigned}개 엔트리에 템플릿 배정됨.");
        }

        /// <summary>
        /// <paramref name="start"/>부터 <paramref name="length"/>개를 <b>한 사슬로</b> 묶는다 —
        /// 앞의 것들은 처치 지시를 끄고 마지막 하나만 켠다. 나머지 엔트리는 건드리지 않는다.
        ///
        /// <para>전체 일괄(<see cref="ApplyChainLength"/>)과 달리 <b>구간만</b> 바꾼다. 사슬은 곡의 특정
        /// 구간(클라이맥스 등)에만 넣고 싶은 저작 대상이라, 전곡에 같은 주기를 까는 것으로는 표현할 수 없다.</para>
        /// </summary>
        private void ApplyChainAt(int start, int length)
        {
            for (int i = start; i < start + length && i < drafts.Count; i++)
            {
                drafts[i].enemyCue ??= new EnemySpace.EnemyCue();
                drafts[i].enemyCue.killOnSuccess = i == start + length - 1;
            }
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
