using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PatternSpace;
using UnityEditor;
using UnityEngine;

namespace ChartGen
{
    /// <summary>
    /// Loop 모드 저작 화면 — <b>분석기가 아니라 스텝 시퀀서다</b>. 마디 격자 위에 엔트리를 놓고,
    /// 드래프트가 <b>스텝 인덱스</b>를 진실의 원천으로 든다(초가 아니라).
    ///
    /// <para><b>왜 분석이 성립하지 않는가</b>: <see cref="StageMode.Loop"/>에서는 재시도마다
    /// 채보 시계가 박의 정수배로 밀린다 — 그 드리프트가 온셋과 음악 트랜지언트의 대응을 지우므로,
    /// 온셋을 트랜지언트에서 뽑는다는 전제 자체가 무너진다. 대신 <b>격자 위에만 놓으면</b>
    /// 밀려도 여전히 격자 위에 앉는다.</para>
    ///
    /// <para><b>Linear는 한 줄도 안 고쳤다</b> — 분석 파이프라인(<c>OnsetDetector</c>·<c>OnsetGrouper</c>·
    /// <c>OnsetChunkSplitter</c>·라운드로빈 배정)은 Linear 분기 안으로 <b>격리</b>될 뿐 삭제하지 않는다.</para>
    /// </summary>
    public partial class PatternChartWindow
    {
        // ── 저작 상태 ────────────────────────────────────────────────────────

        /// <summary>어느 편집기로 저작하는가. 저장 시 <c>SongChart.authoredWith</c>에 기록된다.</summary>
        private ChartAuthoring mode = ChartAuthoring.Linear;

        /// <summary>마디당 박 수. 저장 시 <c>SongChart.beatsPerBar</c>로 나가고 런타임도 읽는다.</summary>
        private int beatsPerBar = 4;

        /// <summary>박당 스텝 수(격자 해상도). Loop에서는 저작자가 직접 고른다 — level에서 파생시키지 않는다.</summary>
        private int stepsPerBeat = 2;

        /// <summary>
        /// 캔버스에 그리는 마디 수. <b>곡 길이에서 유도하되 저작자가 늘릴 수 있다</b> —
        /// 루프 2~3바퀴를 쓰는 채보가 정상이다(곡이 끝나도 채보는 이어진다).
        /// </summary>
        private int barCount = 8;

        /// <summary>캔버스에서 클릭할 때 놓을 템플릿. 비면 배치가 안 된다(빈 엔트리를 만들지 않는다).</summary>
        private Pattern loopTemplate;

        /// <summary>등간격 배치의 노드 간격(스텝). 불규칙 리듬은 놓은 뒤 행 편집에서 개별 이동한다.</summary>
        private int loopNodeGap = 2;

        /// <summary>연타 창의 길이(스텝). 연타 엔트리는 온셋이 정확히 둘(시작·끝)이다.</summary>
        private int loopMashSpan = 8;

        /// <summary>드래그 중 기준점(마우스가 눌린 스텝). -1이면 드래그 중이 아니다.</summary>
        private int dragAnchorStep = -1;

        private Vector2 loopScroll;

        // ── 런타임 노브 복제 ────────────────────────────────────────────────
        //
        // ⚠ 상수가 두 곳에 산다. 씬의 PatternHandler를 바꾸면 여기 검사가 조용히 낡는다 —
        //   chainKillRatio가 이미 그 상태다(§4가 "툴은 씬을 요구하지 않는다"를 일부러 만들었으므로
        //   씬을 읽는 쪽으로 되돌리지 않는다). 유일한 방어는 검사 메시지에 가정한 숫자를 찍는 것이다.

        /// <summary>판정 창(초). <b>런타임 값은 씬의 <c>PatternHandler.goodWindow</c>가 정한다.</b></summary>
        private const float AssumedGoodWindow = 0.10f;

        /// <summary>승계 직후 입력 무시 구간(초). <b>런타임 값은 씬의 <c>PatternHandler.handoverIgnoreDuration</c>이 정한다.</b></summary>
        private const float AssumedHandoverIgnore = 0.08f;

        /// <summary>승계 무시 구간 위에 얹는 판정 여유(초). 다음 패턴의 첫 노드가 자기 창에 들어올 시간이다.</summary>
        private const float HandoverMargin = 0.12f;

        /// <summary>루프 길이가 마디 정수배에서 벗어나도 봐줄 오차(초).</summary>
        private const float LoopToleranceSeconds = 0.015f;

        private bool IsLoopMode => mode == ChartAuthoring.Loop;

        private int StepsPerBar => Mathf.Max(beatsPerBar, 1) * Mathf.Max(stepsPerBeat, 1);

        /// <summary>
        /// 인접 엔트리에 요구하는 최소 입력 간격(초). <b>런타임 노브에서 유도한다</b> —
        /// 상수 0.4로 박으면 노출시간을 바꾼 채보에서 조용히 틀린 답을 낸다.
        /// </summary>
        private float MinEntryGap => Mathf.Max(
            AssumedHandoverIgnore + HandoverMargin,
            defaultExposureDuration - AssumedGoodWindow);

        // ── 모드 토글 ────────────────────────────────────────────────────────

        private void DrawModeToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("저작 방식", GUILayout.Width(60));

            var next = (ChartAuthoring)GUILayout.Toolbar((int)mode,
                new[] { "Linear (온셋 분석)", "Loop (마디 격자)" }, EditorStyles.miniButton);

            EditorGUILayout.EndHorizontal();

            if (next == mode) return;

            // ⚠ 드래프트를 버리지 않는다. Linear로 구운 채보를 Loop에서 다듬는 것은 유효한 작업 흐름이고
            //   (온셋이 이미 격자에 스냅돼 있으므로 스텝으로 환산된다), 반대도 그냥 초로 읽으면 된다.
            mode = next;
            if (IsLoopMode) SnapDraftsToSteps();
        }

        // ── 격자 세우기 / 시각 파생 ─────────────────────────────────────────

        private void StartLoopAuthoring()
        {
            drafts.Clear();
            ClearIndexedState();
            selectedEntry = -1;

            barCount = Mathf.Max(EstimateBarCount(), 1);
        }

        /// <summary>곡 길이가 몇 마디인가(올림). 클립이 없으면 8마디로 시작한다.</summary>
        private int EstimateBarCount()
        {
            float barSeconds = BarSeconds();
            if (clip == null || barSeconds <= 0f) return 8;

            return Mathf.CeilToInt((clip.length - beatOffset) / barSeconds);
        }

        private float BarSeconds() => bpm > 0f ? 60f / bpm * Mathf.Max(beatsPerBar, 1) : 0f;

        /// <summary>
        /// 스텝에서 온셋 시각을 <b>다시 파생시킨다</b>. BPM·오프셋·해상도를 고치면 배치는 그대로 남고
        /// 시각만 늘어나거나 줄어든다 — 그게 격자를 진실의 원천으로 둔 이유다.
        /// </summary>
        private void RebuildLoopTimes()
        {
            var grid = BuildGrid();

            foreach (var draft in drafts)
            {
                if (draft.onsetSteps == null) continue;

                draft.onsetTimes = draft.onsetSteps.Select(grid.GridIndexToTime).ToArray();
                EnsureExposures(draft);
                RecomputeSpawnTimes(draft);
            }
        }

        /// <summary>초로 들어온 드래프트에 스텝을 매긴다(불러오기 · 모드 전환). 이미 격자에 있으면 왕복이 무손실이다.</summary>
        private void SnapDraftsToSteps()
        {
            var grid = BuildGrid();

            foreach (var draft in drafts)
            {
                if (draft.onsetTimes == null) continue;
                draft.onsetSteps = draft.onsetTimes.Select(grid.TimeToGridIndex).ToArray();
            }

            RebuildLoopTimes();
            barCount = Mathf.Max(barCount, LastStep() / Mathf.Max(StepsPerBar, 1) + 1);
        }

        private void EnsureExposures(ChartEntryDraft draft)
        {
            int count = draft.onsetTimes.Length;
            if (draft.exposureDurations != null && draft.exposureDurations.Length == count) return;

            draft.exposureDurations = Enumerable.Repeat(defaultExposureDuration, count).ToArray();
        }

        private int LastStep()
        {
            int last = 0;
            foreach (var draft in drafts)
            {
                if (draft.onsetSteps == null || draft.onsetSteps.Length == 0) continue;
                last = Mathf.Max(last, draft.onsetSteps[^1]);
            }

            return last;
        }

        // ── 캔버스 ───────────────────────────────────────────────────────────

        private const float LoopRowHeight = 34f;

        /// <summary>
        /// 마디 격자. <b>가로 = 스텝, 세로 = 마디</b>(마디당 한 줄)다.
        ///
        /// <para><b>⚠ 한 줄로 길게 늘이지 않는다</b> — 4마디만 돼도 스텝이 수십 개라 가로 스크롤만 남는다.</para>
        ///
        /// <para>좌표는 <c>BeatGrid</c>가 그대로 준다 — <b>새 수학이 0이다</b>.</para>
        /// </summary>
        private void DrawLoopCanvas()
        {
            DrawLoopPalette();

            int stepsPerBar = StepsPerBar;
            loopScroll = EditorGUILayout.BeginScrollView(loopScroll, GUILayout.Height(Mathf.Min(barCount, 8) * LoopRowHeight + 12f));

            for (int bar = 0; bar < barCount; bar++)
            {
                Rect row = GUILayoutUtility.GetRect(0f, LoopRowHeight, GUILayout.ExpandWidth(true));
                DrawLoopRow(row, bar, stepsPerBar);
            }

            EditorGUILayout.EndScrollView();

            DrawLoopSelectionTools();
        }

        private void DrawLoopPalette()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("놓을 패턴", GUILayout.Width(60));
            loopTemplate = (Pattern)EditorGUILayout.ObjectField(loopTemplate, typeof(Pattern), false);

            EditorGUILayout.LabelField("노드 간격", GUILayout.Width(56));
            loopNodeGap = Mathf.Max(1, EditorGUILayout.IntField(loopNodeGap, GUILayout.Width(36)));

            if (loopTemplate != null && loopTemplate.IsMash)
            {
                EditorGUILayout.LabelField("연타 창", GUILayout.Width(48));
                loopMashSpan = Mathf.Max(1, EditorGUILayout.IntField(loopMashSpan, GUILayout.Width(36)));
            }

            EditorGUILayout.LabelField("마디 수", GUILayout.Width(48));
            barCount = Mathf.Max(1, EditorGUILayout.IntField(barCount, GUILayout.Width(40)));
            EditorGUILayout.EndHorizontal();

            // 풀에서 바로 집을 수 있게 — 팔레트는 "자동 배정 재료"가 아니라 "고르는 목록"이다(문구만 다르다).
            var pool = patternPool.Where(p => p != null).ToList();
            if (pool.Count == 0) return;

            EditorGUILayout.BeginHorizontal();
            foreach (var p in pool)
            {
                bool picked = p == loopTemplate;
                if (GUILayout.Toggle(picked, p.name, EditorStyles.miniButton) && !picked)
                    loopTemplate = p;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLoopRow(Rect row, int bar, int stepsPerBar)
        {
            EditorGUI.DrawRect(row, new Color(0.15f, 0.15f, 0.15f));

            float stepWidth = row.width / stepsPerBar;
            int baseStep = bar * stepsPerBar;

            for (int i = 0; i <= stepsPerBar; i++)
            {
                float x = row.x + i * stepWidth;
                bool barHead = i == 0 || i == stepsPerBar;
                bool beatHead = i % Mathf.Max(stepsPerBeat, 1) == 0;

                Color color = barHead ? new Color(1f, 1f, 1f, 0.55f)
                    : beatHead ? new Color(1f, 1f, 1f, 0.28f)
                    : new Color(1f, 1f, 1f, 0.12f);

                EditorGUI.DrawRect(new Rect(x, row.y, barHead ? 2f : 1f, row.height), color);
            }

            // 곡 1바퀴가 끝나는 마디에 경계선. ⚠ 캔버스가 clip.length에서 끝나지 않는다 —
            // 루프 2~3바퀴를 쓰는 채보가 정상이고, 그 경계가 어디인지만 보이면 된다.
            DrawLoopBoundary(row, bar, stepsPerBar);

            EditorGUI.LabelField(new Rect(row.x + 2f, row.y, 34f, 14f), (bar + 1).ToString(), EditorStyles.miniLabel);

            DrawLoopEntries(row, baseStep, stepsPerBar, stepWidth);
            DrawLoopPlayhead(row, bar, stepsPerBar);
            HandleLoopRowInput(row, baseStep, stepsPerBar, stepWidth);
        }

        private void DrawLoopBoundary(Rect row, int bar, int stepsPerBar)
        {
            if (clip == null || bpm <= 0f) return;

            var grid = BuildGrid();
            float rowStart = grid.GridIndexToTime(bar * stepsPerBar);
            float rowEnd = grid.GridIndexToTime((bar + 1) * stepsPerBar);
            if (clip.length < rowStart || clip.length > rowEnd) return;

            float x = row.x + (clip.length - rowStart) / Mathf.Max(rowEnd - rowStart, 0.0001f) * row.width;
            EditorGUI.DrawRect(new Rect(x, row.y, 2f, row.height), new Color(1f, 0.55f, 0.1f, 0.9f));
        }

        private void DrawLoopEntries(Rect row, int baseStep, int stepsPerBar, float stepWidth)
        {
            for (int i = 0; i < drafts.Count; i++)
            {
                var draft = drafts[i];
                if (draft.onsetSteps == null || draft.onsetSteps.Length == 0) continue;

                int first = draft.onsetSteps[0];
                int last = draft.onsetSteps[^1];
                if (last < baseStep || first >= baseStep + stepsPerBar) continue;

                bool selected = i == selectedEntry;
                int chainHead = ChainInfoAt(i).position == 1 ? 1 : 0;

                // 사슬은 재시도 단위라 한 덩어리로 보여야 한다 — 머리 타를 밝게 칠한다.
                Color fill = draft.template == null
                    ? new Color(0.6f, 0.1f, 0.1f, 0.65f)
                    : selected ? new Color(1f, 0.9f, 0.2f, 0.55f)
                    : chainHead == 1 ? new Color(0.25f, 0.65f, 0.9f, 0.45f)
                    : new Color(0.2f, 0.45f, 0.65f, 0.45f);

                float startX = row.x + Mathf.Clamp(first - baseStep, 0, stepsPerBar) * stepWidth;
                float endX = row.x + Mathf.Clamp(last - baseStep, 0, stepsPerBar) * stepWidth;
                EditorGUI.DrawRect(new Rect(startX, row.y + 16f, Mathf.Max(endX - startX, 3f), 10f), fill);

                foreach (int step in draft.onsetSteps)
                {
                    if (step < baseStep || step >= baseStep + stepsPerBar) continue;

                    float x = row.x + (step - baseStep) * stepWidth;
                    EditorGUI.DrawRect(new Rect(x - 1f, row.y + 12f, 3f, 18f),
                        draft.template != null ? Color.cyan : Color.red);
                }
            }
        }

        /// <summary>
        /// 클릭으로 배치, 드래그로 이동, 선택 후 삭제.
        ///
        /// <para><b>⚠ 리스트 변경은 그리는 도중에 하지 않는다</b> — 기존 <c>pending</c> 액션 큐를 그대로 쓴다
        /// (안 그러면 컨트롤 수가 바뀌어 <c>GUILayout</c>이 터진다).</para>
        /// </summary>
        private void HandleLoopRowInput(Rect row, int baseStep, int stepsPerBar, float stepWidth)
        {
            var e = Event.current;
            if (!row.Contains(e.mousePosition)) return;

            int step = baseStep + Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.x - row.x) / stepWidth), 0, stepsPerBar - 1);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                int hit = LoopEntryAtStep(step);

                if (hit >= 0)
                {
                    selectedEntry = hit;
                    dragAnchorStep = step;
                }
                else if (loopTemplate != null)
                {
                    int placeStep = step;
                    pending = () => PlaceLoopEntry(placeStep);
                }

                GUI.FocusControl(null);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseDrag && dragAnchorStep >= 0 && selectedEntry >= 0 && selectedEntry < drafts.Count)
            {
                int delta = step - dragAnchorStep;
                if (delta == 0) return;

                dragAnchorStep = step;
                ShiftLoopEntry(drafts[selectedEntry], delta);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseUp)
                dragAnchorStep = -1;
        }

        private int LoopEntryAtStep(int step)
        {
            for (int i = 0; i < drafts.Count; i++)
            {
                var steps = drafts[i].onsetSteps;
                if (steps == null || steps.Length == 0) continue;
                if (step >= steps[0] && step <= steps[^1]) return i;
            }

            return -1;
        }

        /// <summary>
        /// 엔트리를 하나 놓는다 — <b>(시작 스텝, 노드 간격, 템플릿)</b>이 전부다.
        ///
        /// <para>온셋이 <c>start + i × gap</c>으로 자동 생성되므로 <b>격자 위에 있음</b>과
        /// <b>"노드 수 = 온셋 수"</b>가 구조적으로 보장된다 — Linear에서 계속 나던
        /// "템플릿 노드 수가 이 그룹과 다릅니다"가 <b>이 모델에서는 생길 수 없다</b>.</para>
        /// </summary>
        private void PlaceLoopEntry(int startStep)
        {
            if (loopTemplate == null) return;

            int[] steps;
            if (loopTemplate.IsMash)
            {
                // 연타는 노드의 나열이 아니라 창이다 — 시작·끝 둘뿐이다.
                steps = new[] { startStep, startStep + Mathf.Max(loopMashSpan, 1) };
            }
            else
            {
                int count = Mathf.Max(RequiredOnsets(loopTemplate), 1);
                steps = new int[count];
                for (int i = 0; i < count; i++) steps[i] = startStep + i * Mathf.Max(loopNodeGap, 1);
            }

            var draft = new ChartEntryDraft { template = loopTemplate, onsetSteps = steps };

            drafts.Add(draft);
            SortLoopDrafts();
            RebuildLoopTimes();

            selectedEntry = drafts.IndexOf(draft);
            barCount = Mathf.Max(barCount, LastStep() / Mathf.Max(StepsPerBar, 1) + 1);
        }

        private void ShiftLoopEntry(ChartEntryDraft draft, int deltaSteps)
        {
            if (draft.onsetSteps == null) return;
            if (draft.onsetSteps[0] + deltaSteps < 0) return;

            for (int i = 0; i < draft.onsetSteps.Length; i++)
                draft.onsetSteps[i] += deltaSteps;

            RebuildLoopTimes();
        }

        /// <summary>
        /// 캔버스 위치가 곧 진행 순서다 — 옮기면 목록도 따라 정렬돼야 한다.
        ///
        /// <para><b>⚠ 인덱스를 키로 든 UI 상태는 여기서 버린다.</b> 리스트 순서가 바뀌면 그 키들이 다 어긋난다.</para>
        /// </summary>
        private void SortLoopDrafts()
        {
            // cue는 드래프트가 직접 들고 있으므로 정렬만으로 따라온다. 버려야 하는 것은 인덱스 키 상태뿐이다.
            drafts.Sort((a, b) => a.onsetSteps[0].CompareTo(b.onsetSteps[0]));
            ClearIndexedState();
        }

        private void DrawLoopSelectionTools()
        {
            EditorGUILayout.BeginHorizontal();

            bool hasSelection = selectedEntry >= 0 && selectedEntry < drafts.Count && drafts[selectedEntry].onsetSteps != null;

            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                EditorGUILayout.LabelField(hasSelection
                    ? $"선택 {selectedEntry}번 · 시작 스텝 {drafts[selectedEntry].onsetSteps[0]}"
                    : "선택 없음", GUILayout.Width(200));

                if (GUILayout.Button("←", EditorStyles.miniButton, GUILayout.Width(28)))
                    ShiftLoopEntry(drafts[selectedEntry], -1);
                if (GUILayout.Button("→", EditorStyles.miniButton, GUILayout.Width(28)))
                    ShiftLoopEntry(drafts[selectedEntry], 1);

                if (GUILayout.Button("삭제", EditorStyles.miniButton, GUILayout.Width(48)))
                {
                    int remove = selectedEntry;
                    pending = () =>
                    {
                        drafts.RemoveAt(remove);
                        ClearIndexedState();
                        selectedEntry = -1;
                    };
                }
            }

            EditorGUILayout.EndHorizontal();

            DrawLoopPreviewTools();
        }

        /// <summary>
        /// 노드별 스텝을 직접 고친다 — 등간격에서 벗어난 리듬을 만드는 유일한 자리다.
        ///
        /// <para><b>값이 스텝이라 격자를 벗어날 수 없다.</b> 초를 직접 고치게 두면 그 값이 다음
        /// <see cref="RebuildLoopTimes"/>에서 덮이고, 저작자에게는 "고친 게 사라진다"로 보인다.</para>
        ///
        /// <para>연타는 창의 시작·끝 둘이므로 같은 화면에서 창 길이를 늘였다 줄이는 일이 된다.</para>
        /// </summary>
        private void DrawLoopStepFields(ChartEntryDraft draft)
        {
            if (draft.onsetSteps == null) return;

            var grid = BuildGrid();
            int stepsPerBar = StepsPerBar;

            for (int i = 0; i < draft.onsetSteps.Length; i++)
            {
                string label = draft.template != null && draft.template.IsMash
                    ? (i == 0 ? "창 시작 스텝" : "창 끝 스텝")
                    : $"노드 {i} 스텝";

                EditorGUILayout.BeginHorizontal();
                int next = EditorGUILayout.IntField(label, draft.onsetSteps[i]);
                GUILayout.Label(
                    $"{draft.onsetSteps[i] / stepsPerBar + 1}마디 {draft.onsetSteps[i] % stepsPerBar + 1}스텝  ({grid.GridIndexToTime(draft.onsetSteps[i]):F2}s)",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();

                if (next == draft.onsetSteps[i] || next < 0) continue;

                draft.onsetSteps[i] = next;
                Array.Sort(draft.onsetSteps);   // 순서가 뒤집히면 온셋이 내림차순이 되어 판정이 성립하지 않는다
                RebuildLoopTimes();
            }
        }

        // ── 요약 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 채보 한눈 요약. <b>두 모드가 공유한다</b> — <c>Attacker</c>가 이제 진행 규칙의 입력이므로
        /// 공격/방어 비율이 채보 화면에서 읽혀야 한다(§9).
        /// </summary>
        private void DrawChartSummary()
        {
            int attack = 0, pass = 0;
            foreach (var d in drafts)
            {
                if (d.template == null) continue;
                if (d.template.HitsOnFail) pass++;
                else attack++;
            }

            // 이 무대에 설 적 수 = 처치 수의 합(EnemyDirector.PrepareStage가 같은 값을 받는다).
            // ⚠ 라벨이 "killOnSuccess N"이 아니라 "적 N명"인 이유: 한 엔트리가 여럿을 죽이게 되면 둘이 갈라진다.
            int enemies = KillCount();

            float lastInput = drafts.Count > 0 ? drafts.Max(d => d.onsetTimes != null && d.onsetTimes.Length > 0 ? d.onsetTimes[^1] : 0f) : 0f;
            float songLength = clip != null ? clip.length : 0f;

            string barsPart = IsLoopMode && BarSeconds() > 0f
                ? $" · {barCount}마디 (곡 {songLength / BarSeconds():F1}마디)"
                : string.Empty;

            string gapPart = drafts.Count > 1 ? $" · 최소 간격 {MinObservedGap():F2}s" : string.Empty;

            EditorGUILayout.LabelField(
                $"패턴 {drafts.Count}개 (재시도 {attack} · 통과 {pass}) · 적 {enemies}명 · 마지막 판정 {FormatTime(lastInput)} / 곡 {FormatTime(songLength)}" +
                $" · 꼬리 {Mathf.Max(songLength - lastInput, 0f):F1}초{barsPart}{gapPart}",
                EditorStyles.miniBoldLabel);
        }

        /// <summary>
        /// 이 채보가 죽이는 적의 수. <c>EnemyDirector.PrepareStage</c>가 세울 인원과 <b>같은 식</b>이어야 한다
        /// (<c>ChartPlayer.CountKills</c>) — 다르면 툴이 거짓말을 한다.
        /// </summary>
        private int KillCount()
        {
            int total = 0;
            foreach (var d in drafts)
                total += d.enemyCue.killOnSuccess ? 1 : 0;

            return total;
        }

        private static string FormatTime(float seconds) => $"{(int)(seconds / 60f)}:{seconds % 60f:00.0}";

        /// <summary>인접 엔트리의 입력 간격 중 가장 좁은 값(초). 0 이하면 겹쳤다는 뜻이다.</summary>
        private float MinObservedGap()
        {
            float min = float.MaxValue;

            for (int i = 1; i < drafts.Count; i++)
            {
                var prev = drafts[i - 1];
                var cur = drafts[i];
                if (prev.onsetTimes == null || cur.onsetTimes == null) continue;
                if (prev.onsetTimes.Length == 0 || cur.onsetTimes.Length == 0) continue;

                min = Mathf.Min(min, cur.onsetTimes[0] - prev.onsetTimes[^1]);
            }

            return min == float.MaxValue ? 0f : min;
        }

        // ── 저장 검증 ────────────────────────────────────────────────────────

        /// <summary>
        /// <b>음악이 공짜로 보장해 주던 것을 여기서 강제한다.</b> Linear에서는 온셋이 음악에서 나와
        /// 실측상 간격이 지켜졌을 뿐이고, 격자 저작에서는 즉시 깨질 수 있다.
        ///
        /// <para><b>⚠ 간격·큐 깊이(2·3)는 Loop에서만 저장을 막는다.</b> Linear는 온셋이 음악에서 나와
        /// 그 값을 지킨 적이 없으므로(기존 채보에 0.25s 간격이 흔하다) 거기서 막으면 <b>이미 있는 채보를
        /// 열어 고치는 것조차 불가능</b>해진다 — <see cref="CheckSpacing"/>에 그 갈림이 적혀 있다.</para>
        /// </summary>
        /// <returns>저장해도 되는가.</returns>
        private bool ValidateForSave()
        {
            if (drafts.Count == 0)
            {
                Debug.LogError("[PatternChartWindow] 빈 채보는 저장할 수 없습니다.");
                return false;
            }

            // 1) 마지막 엔트리는 공격(재시도) 패턴이어야 한다.
            //    ⚠ 이 강제가 "마지막이 방어라 실패해도 끝나는데 마무리 실루엣이 없다"를 표현 불가능하게 만든다 —
            //      분기가 아니라 데이터로 막히므로 런타임에 대비 코드가 0줄이다.
            //    ⚠ Attacker는 패턴이 소유해 툴이 고칠 수 없다(§5). 그래서 자동 보정이 아니라 저장 차단 + 안내다.
            var last = drafts[^1];
            if (last.template != null && last.template.HitsOnFail)
            {
                Debug.LogError(
                    $"[PatternChartWindow] 마지막 엔트리('{last.template.name}')가 통과 패턴입니다. " +
                    "실패해도 소비되어 마무리 실루엣 없이 스테이지가 끝납니다 — 마지막은 공격(Attacker.Player, 상호 공격 아님) 패턴이어야 합니다.");
                return false;
            }

            // 1-b) 마지막 엔트리는 처치(killOnSuccess)여야 한다.
            //   적 수가 채보의 처치 수와 같으므로(docs/CombatLegibility), 마지막 엔트리가 처치가 아니면
            //   마무리 실루엣(§14)이 터지는 프레임에 적이 살아서 서 있는다. 런타임에서 맞출 수 없는 것을
            //   저작 단계에서 못박는다 — 위 ①과 같은 관용구(자동 보정이 아니라 차단 + 안내)다.
            if (!last.enemyCue.killOnSuccess)
            {
                Debug.LogError(
                    "[PatternChartWindow] 마지막 엔트리에 killOnSuccess가 꺼져 있습니다. " +
                    "마지막 일격에서 마지막 적이 죽어야 마무리 실루엣에 잔여 적이 안 남습니다 - 켜고 저장하세요.");
                return false;
            }

            // 2~3) 간격과 큐 깊이. ⚠ 차단은 Loop 저작에서만이다 — 아래 CheckSpacing 주석 참조.
            if (!CheckSpacing() && IsLoopMode) return false;

            // 4~5) 경고만. 저장은 막지 않는다.
            WarnLoopLength();
            WarnNegativeSpawn();
            return true;
        }

        /// <summary>
        /// 인접 엔트리 간격과 큐 깊이를 검사한다. <b>어긴 항목마다 로그를 남기고, 하나라도 어겼으면 false</b>다.
        ///
        /// <para><b>⚠ 차단은 Loop 저작에서만이다.</b> Linear는 온셋이 <b>음악에서</b> 나오므로 이 값을 지킨 적이 없다 —
        /// 실측상 대체로 그랬을 뿐이고, 기존 채보에는 0.25s 간격이 흔하다. 거기서 저장을 막으면
        /// <b>이미 있는 채보를 열어 한 글자 고치는 것조차 불가능</b>해진다(회귀 0이 이 개편의 전제였다).
        /// 격자 저작에서는 반대로 저작자가 방금 놓은 것이라 그 자리에서 고칠 수 있고, 어긴 채보는
        /// <i>조금 이상한</i> 게 아니라 <b>재시도 취소가 하나만 회수하고 나머지를 흘려 적 배정이 영구히 밀린다</b>.</para>
        ///
        /// <para>그래서 <b>Linear에서는 같은 사실을 경고로만</b> 알린다 — 그 채보를 <c>StageMode.Loop</c>로
        /// 재생하면 실제로 그 증상이 나므로, 조용히 넘기지도 않는다.</para>
        /// </summary>
        private bool CheckSpacing()
        {
            bool ok = true;

            // 2) 인접 엔트리 입력 간격
            for (int i = 1; i < drafts.Count; i++)
            {
                var prev = drafts[i - 1];
                var cur = drafts[i];
                if (prev.onsetTimes == null || cur.onsetTimes == null) continue;
                if (prev.onsetTimes.Length == 0 || cur.onsetTimes.Length == 0) continue;

                float gap = cur.onsetTimes[0] - prev.onsetTimes[^1];
                if (gap >= MinEntryGap) continue;

                ok = false;
                Report(
                    $"[PatternChartWindow] {i}번 엔트리의 입력 간격이 {gap:F2}s로 필요치 {MinEntryGap:F2}s보다 좁습니다 " +
                    $"(goodWindow {AssumedGoodWindow:F2} · 승계 무시 {AssumedHandoverIgnore:F2} · 노출 {defaultExposureDuration:F2} 가정). " +
                    "승계 직후 입력이 삼켜지고, 재시도 취소가 패턴 하나를 흘려 적 배정이 영구히 밀립니다.");
            }

            // 3) 큐에 셋이 오르지 않는다 — k가 끝나기 전에 k+2가 스폰되면 안 된다.
            for (int k = 0; k + 2 < drafts.Count; k++)
            {
                var head = drafts[k];
                var third = drafts[k + 2];
                if (head.onsetTimes == null || third.spawnTimes == null) continue;
                if (head.onsetTimes.Length == 0 || third.spawnTimes.Length == 0) continue;

                float headDeadline = head.onsetTimes[^1] + AssumedGoodWindow;
                if (third.spawnTimes[0] > headDeadline) continue;

                ok = false;
                Report(
                    $"[PatternChartWindow] {k}번 엔트리가 끝나기 전에 {k + 2}번이 스폰됩니다 " +
                    $"({third.spawnTimes[0]:F2}s ≤ {headDeadline:F2}s). 큐에 패턴이 셋 오르면 재시도 취소가 " +
                    "하나만 회수하고 나머지를 흘립니다 — 노출시간을 줄이거나 간격을 벌리세요.");
            }

            if (!ok && !IsLoopMode)
            {
                Debug.LogWarning("[PatternChartWindow] 위 간격 경고는 Linear 채보라 저장을 막지 않습니다 — " +
                                 "다만 이 채보를 StageMode.Loop로 재생하면 그 증상이 실제로 납니다.");
            }

            return ok;
        }

        /// <summary>Loop에서는 저장을 막을 사유이므로 에러, Linear에서는 같은 사실을 경고로 남긴다.</summary>
        private void Report(string message)
        {
            if (IsLoopMode) Debug.LogError(message);
            else Debug.LogWarning(message);
        }

        /// <summary>
        /// 곡 길이가 마디의 정수배인가. <b>코드로 고칠 수 없는 유일한 위험이라 여기서만 잡힌다</b> —
        /// 루프할 때마다 음악이 채보 격자에서 그만큼 밀리고 바퀴마다 누적된다.
        ///
        /// <para><b>⚠ 경고이지 차단이 아니다.</b> 커스텀 모드(루프 꺼짐)에서는 상관없는 값이고,
        /// 툴은 이 채보가 어느 모드로 쓰일지 모른다.</para>
        /// </summary>
        private void WarnLoopLength()
        {
            float barSeconds = BarSeconds();
            if (clip == null || barSeconds <= 0f) return;

            float bars = (clip.length - beatOffset) / barSeconds;
            float errorSeconds = Mathf.Abs(bars - Mathf.Round(bars)) * barSeconds;
            if (errorSeconds <= LoopToleranceSeconds) return;

            Debug.LogWarning(
                $"[PatternChartWindow] 곡 길이가 마디의 정수배가 아닙니다({bars:F2}마디, 오차 {errorSeconds * 1000f:F0} ms). " +
                "루프할 때마다 음악이 채보 격자에서 그만큼 밀리고 바퀴마다 누적됩니다 — 노트 위치는 그대로라 " +
                "\"판정이 이상하다\"로 보입니다. 압축 포맷의 패딩이 원인일 수 있습니다(PCM으로 확인하세요).");
        }

        private void WarnNegativeSpawn()
        {
            for (int i = 0; i < drafts.Count; i++)
            {
                var spawn = drafts[i].spawnTimes;
                if (spawn == null || spawn.Length == 0 || spawn[0] >= 0f) continue;

                Debug.LogWarning(
                    $"[PatternChartWindow] {i}번 엔트리의 스폰 시각이 음수입니다({spawn[0]:F2}s) — 노출시간이 온셋보다 깁니다. " +
                    "곡 시작과 동시에 링이 떠 경고 시간이 그만큼 짧아집니다.");
            }
        }

        // ── 미리듣기 ─────────────────────────────────────────────────────────
        //
        // 분석이 없어졌으므로 "이 리듬이 곡과 맞는가"를 귀로 확인해야 한다. 그 확인 수단이 통째로 사라진 채
        // 저작하는 것이 이 개편의 가장 큰 실무 위험이라 범위에 넣는다.
        //
        // ⚠ 온셋마다 클릭을 울리는 것은 포기했다 — 에디터의 미리듣기 채널이 하나뿐이라(AudioUtil은
        //   새 미리듣기가 이전 것을 끊는다) 클릭이 곡을 잘라 먹는다. 대신 재생 커서를 캔버스에 그린다:
        //   Plan이 폴백으로 적어 둔 그것이고, 격자 정합은 눈으로 충분히 보인다. 클릭이 필요해지면
        //   런타임 AudioSource를 에디터에서 돌리는 별개 작업이다.

        private double previewStartedAt;
        private float previewFromTime;
        private bool previewing;

        private void DrawLoopPreviewTools()
        {
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(clip == null || bpm <= 0f))
            {
                EditorGUILayout.LabelField("미리듣기 시작 마디", GUILayout.Width(120));
                previewBar = Mathf.Max(1, EditorGUILayout.IntField(previewBar, GUILayout.Width(40)));

                if (GUILayout.Button(previewing ? "정지" : "재생", EditorStyles.miniButton, GUILayout.Width(48)))
                {
                    if (previewing) StopPreview();
                    else StartPreview();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (clip != null && bpm > 0f)
            {
                EditorGUILayout.LabelField(
                    "여러 마디에서 같은 리듬을 들어 보는 것이 루프가 균질한지 판단하는 전부다 — 시작 마디를 옮겨 가며 확인한다.",
                    EditorStyles.miniLabel);
            }
        }

        private int previewBar = 1;

        private float PreviewTime => previewing
            ? previewFromTime + (float)(EditorApplication.timeSinceStartup - previewStartedAt)
            : float.NaN;

        private void StartPreview()
        {
            previewFromTime = beatOffset + (previewBar - 1) * BarSeconds();
            int startSample = Mathf.Clamp((int)(previewFromTime * clip.frequency), 0, Mathf.Max(clip.samples - 1, 0));

            // ⚠ 에디터 오디오 재생은 공개 API가 없다. 리플렉션이 막히면(버전 차이) 조용히 비활성되고
            //   커서만 남는다 — 재생이 안 돼도 나머지 저작은 성립해야 한다(기존 배선 누락 규율).
            if (!InvokeAudioUtil(new[] { "PlayPreviewClip", "PlayClip" }, new object[] { clip, startSample, false }))
            {
                Debug.LogWarning("[PatternChartWindow] 에디터 미리듣기를 시작할 수 없습니다(AudioUtil 비공개 API). " +
                                 "재생 커서 없이 저작은 그대로 가능합니다.");
                return;
            }

            previewStartedAt = EditorApplication.timeSinceStartup;
            previewing = true;
        }

        private void StopPreview()
        {
            previewing = false;
            InvokeAudioUtil(new[] { "StopAllPreviewClips", "StopAllClips" }, Array.Empty<object>());
        }

        private static bool InvokeAudioUtil(IEnumerable<string> names, object[] args)
        {
            Type type = typeof(EditorWindow).Assembly.GetType("UnityEditor.AudioUtil");
            if (type == null) return false;

            foreach (string name in names)
            {
                var method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
                if (method == null) continue;

                method.Invoke(null, args);
                return true;
            }

            return false;
        }

        private void DrawLoopPlayhead(Rect row, int bar, int stepsPerBar)
        {
            float now = PreviewTime;
            if (float.IsNaN(now) || bpm <= 0f) return;

            var grid = BuildGrid();
            float rowStart = grid.GridIndexToTime(bar * stepsPerBar);
            float rowEnd = grid.GridIndexToTime((bar + 1) * stepsPerBar);
            if (now < rowStart || now > rowEnd) return;

            float x = row.x + (now - rowStart) / Mathf.Max(rowEnd - rowStart, 0.0001f) * row.width;
            EditorGUI.DrawRect(new Rect(x, row.y, 2f, row.height), new Color(0.2f, 1f, 0.4f, 0.95f));
        }

        private void Update()
        {
            if (!previewing) return;

            if (clip != null && PreviewTime > clip.length)
            {
                StopPreview();
                return;
            }

            Repaint();
        }

        private void OnDisable() => StopPreview();
    }
}
