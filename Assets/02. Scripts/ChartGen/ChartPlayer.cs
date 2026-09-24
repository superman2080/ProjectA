using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PatternSpace;
using UnityEngine;

namespace ChartGen
{
    /// <summary>
    /// SongChart를 오디오 재생 시각에 맞춰 순차적으로 PatternHandler.SetPattern에 흘려보내는 재생 글루.
    ///
    /// <para><b>진행의 주인이기도 하다</b>(<see cref="StageMode.Loop"/>). 공격 패턴을 실패하면 그 엔트리는
    /// 소비되지 않고 <b>사슬 머리부터 다시</b> 나오며, 스테이지는 곡 길이가 아니라
    /// <b>모든 엔트리가 소비되면</b> 끝난다. 소비의 기준은 역할이 아니라 <b>대가를 치렀는가</b>다 —
    /// 적의 칼이 닿았으면(<see cref="Pattern.HitsOnFail"/>) 그 엔트리는 끝난 것이다.
    /// </para>
    ///
    /// <para><b>⚠ 되감는 것은 채보 시계뿐이고 오디오는 손대지 않는다.</b> 재시도할 때
    /// <see cref="chartOffset"/>을 밀어 채보 시계를 그 사슬의 스폰 시각으로 되돌리므로,
    /// 뒤따르는 엔트리의 <b>간격</b>이 보존된다(그러지 않으면 재시도 뒤에 전부 쏟아진다).</para>
    ///
    /// <para><b>⚠ 드리프트는 박의 정수배로만 쌓인다.</b> 대기를 초가 아니라 <b>박 수</b>로 저작하고
    /// 되감기 양을 박(또는 마디) 간격으로 <b>올림</b>하기 때문에, 되감긴 온셋이 <c>BeatGrid</c> 위에
    /// 그대로 앉는다. 곡이 루프라 끝나지 않는 것과 <b>이 둘이 같이 참일 때만</b> 동기화가 유지된다.</para>
    /// </summary>
    public class ChartPlayer : MusicPlayerBase
    {
        [SerializeField] private SongChart debugChart;
        [SerializeField] private PatternHandler patternHandler;

        [Tooltip("전투 연출. 비우면 적 없이 패턴만 재생된다(기존 동작).")]
        [SerializeField] private EnemySpace.EnemyDirector enemyDirector;

        [Tooltip("마무리 실루엣. 비우면 연출을 기다리지 않고 곧바로 종료한다.")]
        [SerializeField] private FinaleSilhouetteDirector finale;

        // 이 구간은 프리웜 창이자 인트로 연출 창이다. 어느 쪽으로 봐도 짧아져서 좋을 게 없어 하한을 타입으로 못박는다.
        [Min(3f)]
        [SerializeField] private float countdownDuration = 3f;
        [SerializeField] private bool playOnStart = false;

        [Header("진행")]
        [Tooltip("스테이지 모드. GameSession.StageModeOverride가 있으면 그것이 이긴다.")]
        [SerializeField] private StageMode stageMode = StageMode.Loop;

        [Tooltip("공격 실패 후 같은 사슬이 다시 나오기까지의 대기(박). 적 방어 클립이 들어갈 창이기도 하다 - " +
                 "BPM이 빠른 곡에서는 그만큼 짧아진다. 4박 = 1마디.")]
        [Min(1)] [SerializeField] private int retryGapBeats = 4;

        [Tooltip("되감기 양을 마디 단위로 올림한다. 격자 보존에는 박이면 충분하고, 프레이즈 머리에 맞추려면 마디다.")]
        [SerializeField] private bool rewindToBar = false;

        [Tooltip("마디당 박 수. rewindToBar일 때만 쓰인다. 채보가 beatsPerBar를 들고 있으면 그것이 이긴다.")]
        [Min(1)] [SerializeField] private int fallbackBeatsPerBar = 4;

        [Header("종료")]
        [Tooltip("마지막 패턴이 끝나고 종료를 알리기까지의 여운(초). goodWindow + ImpactOffset + 절단 + 히트스톱을 덮는다.")]
        [Min(0f)] [SerializeField] private float outroHold = 1.5f;

        [Tooltip("종료 시 곡을 줄이는 시간(초). 루프곡은 스스로 끝나지 않으므로 이 경로가 유일한 정지 지점이다.")]
        [Min(0f)] [SerializeField] private float outroFade = 0.35f;

        /// <summary>곡이 끝난 순간(모든 엔트리 소비 + 여운). 남은 적 소멸·스테이지 종료가 구독한다.</summary>
        public event System.Action OnSongEnded;

        /// <summary>
        /// 카운트다운이 시작된 순간. 인자는 곡이 시작되기까지 남은 시간(초)이다.
        ///
        /// <para>프리웜(<c>PrepareStage</c>)이 <b>끝난 뒤</b> 발행되므로, 구독자는 적이 이미 배치된 무대를 본다.
        /// 이 창 안에서 끝나는 연출(카메라 인트로 등)이 구독한다.</para>
        /// </summary>
        public event System.Action<float> OnCountdownStarted;

        /// <summary>
        /// 사슬 하나가 <b>되감겼다</b>. 인자는 사슬 머리 엔트리와 그 사슬의 시도 횟수(2부터)다.
        ///
        /// <para><b>지금은 구독자가 0이다</b> — 재시도 피드백(전용 SFX·HUD·화면 효과)이 붙을 확장 포인트이며,
        /// <c>CharacterActionPlayer.OnSwingBegan</c>처럼 발행만 하고 비워 둔다. <c>attempt</c>를 같이 싣는 이유는
        /// 나중 연출이 <i>"또 실패했다"</i>를 구분할 유일한 수단이라서다 — 나중에 추가하면 시그니처가 깨진다.</para>
        /// </summary>
        public event System.Action<SongChartEntry, int> OnPatternRetried;

        /// <summary>엔트리 하나의 진행 상태. 채보 에셋은 건드리지 않는다(같은 곡을 다시 플레이할 수 있어야 한다).</summary>
        private sealed class EntryCursor
        {
            public SongChartEntry entry;

            /// <summary>정렬된 전체 목록에서의 위치. <c>pending</c>의 정렬 키이기도 하다.</summary>
            public int index;

            /// <summary>이 엔트리가 속한 사슬의 첫 엔트리 <see cref="index"/>. 재시도 단위가 사슬이다.</summary>
            public int chainHead;

            /// <summary>
            /// 이 엔트리가 완료 이벤트를 <b>실제로 한 번이라도 겪었는가</b>. 겪었으면 다시 나올 때 채점되지 않는다.
            ///
            /// <para><b>⚠ 취소는 겪은 것으로 치지 않는다</b> — 플레이어가 입력할 기회가 없었다.
            /// 사슬 단위로 표식을 매기면 사슬 뒤쪽 엔트리가 <b>첫 시도를 해 본 적도 없이 영원히 채점 불가</b>가 된다.</para>
            /// </summary>
            public bool experienced;
        }

        private List<EntryCursor> ordered;                                     // 정렬된 전체(Play에서 한 번 만든다)
        private List<EntryCursor> pending;                                     // 남은 것. 언제나 index 오름차순
        private readonly List<EntryCursor> inAir = new List<EntryCursor>();    // 투입됐고 아직 완료 안 된 것(FIFO)
        private readonly Dictionary<int, int> chainAttempts = new Dictionary<int, int>();

        private Coroutine playCoroutine;
        private bool songEndRaised;
        private StageMode activeMode;

        // ── 채보 시계 ────────────────────────────────────────────────────────
        private bool songStarted;
        private int loops;
        private float prevAudioTime;
        private float songTime;
        private float chartOffset;
        private float clipLength;

        private bool retryRequested;
        private bool bpmWarned;
        private float finishAt = float.NaN;
        private float fadeFactor = 1f;

        protected override float VolumeFactor => fadeFactor;

        /// <summary>
        /// GameSession의 선택된 채보를 우선 사용하고, 없으면 debugChart를 사용한다.
        ///
        /// <para><b>공개인 이유</b>: 채점기(<c>ScoreDirector</c>)가 총 노트 수와 만점을 알려면 곡을 알아야 하는데,
        /// 이 판단(<c>SelectedChart ?? debugChart</c>)을 복제하면 <b>디버그 재생에서만 만점이 어긋난다</b>.
        /// 곡의 진실의 원천은 하나여야 한다.</para>
        /// </summary>
        public SongChart ActiveChart =>
            (GameSession.Instance != null && GameSession.Instance.SelectedChart != null)
                ? GameSession.Instance.SelectedChart
                : debugChart;

        /// <summary>재생 중인 채보의 템포. 채보가 없으면 0(=모른다)이다.</summary>
        public override float Bpm => ActiveChart != null ? ActiveChart.bpm : 0f;

        /// <summary>
        /// 채보 시계. <b>오디오 루프를 넘어 단조 증가</b>하며, 재시도 되감기만큼 뒤로 밀려 있다.
        ///
        /// <para><b>⚠ <c>audioSource.time</c>을 직접 쓸 수 없다</b> — 그것은 클립 <b>안의</b> 위치라
        /// 루프마다 0으로 되감기고, 그러면 채보가 곡 2회차에서 처음으로 돌아간다.</para>
        /// </summary>
        public float ChartTime => songTime - chartOffset;

        private void Start()
        {
            if (playOnStart)
                Play();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            if (patternHandler != null)
                patternHandler.OnPatternComplete += HandlePatternComplete;
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            if (patternHandler != null)
                patternHandler.OnPatternComplete -= HandlePatternComplete;
        }

        [ContextMenu("Play")]
        public override void Play()
        {
            if (ActiveChart == null)
            {
                Debug.LogError("[ChartPlayer] 재생할 채보가 없습니다. GameSession.SelectedChart 또는 debugChart를 설정하세요.", this);
                return;
            }
            if (audioSource == null)
            {
                Debug.LogError("[ChartPlayer] AudioSource가 연결되지 않았습니다.", this);
                return;
            }
            if (patternHandler == null)
            {
                Debug.LogError("[ChartPlayer] PatternHandler가 연결되지 않았습니다.", this);
                return;
            }
            if (ActiveChart.entries == null || ActiveChart.entries.Length == 0)
            {
                Debug.LogError($"[ChartPlayer] 채보 '{ActiveChart.name}'에 엔트리가 없습니다. 스테이지가 시작과 동시에 끝납니다.", this);
                return;
            }

            if (playCoroutine != null)
                StopCoroutine(playCoroutine);

            playCoroutine = StartCoroutine(PlayRoutine());
        }

        [ContextMenu("Stop")]
        public override void Stop()
        {
            if (playCoroutine != null)
            {
                StopCoroutine(playCoroutine);
                playCoroutine = null;
            }

            audioSource.Stop();
            ResetProgress();

            fadeFactor = 1f;
            ApplyMusicVolume();

            if (patternHandler != null)
                patternHandler.ClearAllPatterns();
        }

        private void ResetProgress()
        {
            ordered = null;
            pending = null;
            inAir.Clear();
            chainAttempts.Clear();

            songStarted = false;
            retryRequested = false;
            loops = 0;
            prevAudioTime = 0f;
            songTime = 0f;
            chartOffset = 0f;
            finishAt = float.NaN;
        }

        private IEnumerator PlayRoutine()
        {
            ResetProgress();

            // 모드가 종료 조건·재시도·루프 셋을 같이 정한다. 씬의 loop 직렬화 값에 기대면
            // Linear + loop = true인 씬에서 스테이지가 영영 안 끝난다.
            activeMode = GameSession.Instance != null && GameSession.Instance.StageModeOverride.HasValue
                ? GameSession.Instance.StageModeOverride.Value
                : stageMode;
            audioSource.loop = activeMode == StageMode.Loop;

            BuildCursors();
            songEndRaised = false;
            clipLength = ActiveChart.song != null ? ActiveChart.song.length : 0f;

            // 카운트다운이 곧 프리웜 창이다 — 곡 도중에는 Instantiate가 한 번도 일어나면 안 된다
            // (스키닝 메쉬 생성 한 프레임이 히치가 되고, 그게 곧 판정 손실이다).
            // 절단 세트는 EnemyDefinition이 소유하므로 rosterPool을 훑는 것만으로 프리웜이 끝난다.
            //
            // ⚠ 적 수는 채보가 정한다(docs/CombatLegibility) — 엔트리별 처치 수의 합이라
            //   마지막 엔트리에서 마지막 적이 죽고 잔여가 0이 된다. 여기가 채보를 아는 유일한 자리다.
            enemyDirector?.PrepareStage(CountKills(ActiveChart));

            if (countdownDuration > 0f)
            {
                OnCountdownStarted?.Invoke(countdownDuration);
                yield return new WaitForSeconds(countdownDuration);
            }

            audioSource.clip = ActiveChart.song;
            fadeFactor = 1f;
            ApplyMusicVolume();
            audioSource.Play();

            prevAudioTime = 0f;
            songStarted = true;
            playCoroutine = null;
        }

        /// <summary>
        /// 엔트리를 정렬하고 <b>사슬 경계</b>를 한 번 계산한다. 사슬은 <c>killOnSuccess == false</c>가
        /// 이어지다 <c>true</c>인 엔트리에서 끝나는 구간이고(§11-5), 그 정보가 이미 채보에 있다 —
        /// <b>새 저작 데이터가 0개</b>다. 비사슬 엔트리는 길이 1인 사슬이라 분기가 늘지 않는다.
        /// </summary>
        private void BuildCursors()
        {
            var sorted = ActiveChart.entries
                .Where(e => e != null && e.spawnTimes != null && e.spawnTimes.Length > 0)
                .OrderBy(e => e.spawnTimes[0])
                .ToList();

            ordered = new List<EntryCursor>(sorted.Count);
            int head = 0;

            for (int i = 0; i < sorted.Count; i++)
            {
                ordered.Add(new EntryCursor { entry = sorted[i], index = i, chainHead = head });

                bool chainEnds = sorted[i].enemyCue == null || sorted[i].enemyCue.killOnSuccess;
                if (chainEnds) head = i + 1;
            }

            pending = new List<EntryCursor>(ordered);
        }

        private void Update()
        {
            // 튜토리얼 가드 — Play()를 안 부르면 pending이 null이고 여기서 끝난다.
            // 드릴(PatternDrillStep)이 자체 재시도를 도는 동안 이 시스템은 아무것도 하지 않는다.
            if (pending == null) return;

            // ⚠ 취소는 완료 이벤트 디스패치 안에서 부를 수 없다 — 구독 순서가 보장되지 않아
            //   실패 패턴의 토큰을 아직 꺼내지 못한 구독자가 있으면 엉뚱한 예약이 버려진다.
            if (retryRequested)
            {
                retryRequested = false;
                patternHandler.CancelQueuedPatterns();
            }

            TickSongClock();
            TickDispatch();
            TickFinish();
        }

        /// <summary>
        /// 오디오 루프를 넘어 단조 증가하는 <see cref="songTime"/>을 민다.
        ///
        /// <para><b>⚠ 되감김 감지는 <c>audioSource.time</c>이 줄어드는 것 하나다.</b> 루프 한 바퀴를
        /// 통째로 건너뛰는 프레임은 실무상 없다(가장 짧은 루프도 수 초).</para>
        ///
        /// <para><b>폴백</b>: 어떤 이유로든 오디오가 멈추면 <c>Time.deltaTime</c>으로 이어 간다
        /// (커스텀 모드의 루프 꺼짐·오디오 로드 실패). <b>⚠ <c>unscaledDeltaTime</c>이 아니다</b> —
        /// 스폰은 판정(<c>Time.time</c>)과 짝이 맞아야 하고, 그 덕에 일시정지(<c>timeScale = 0</c>)에서
        /// 채보 시계도 같이 언다.</para>
        /// </summary>
        private void TickSongClock()
        {
            if (!songStarted) return;

            if (audioSource.isPlaying && clipLength > 0f)
            {
                float t = audioSource.time;
                if (t < prevAudioTime) loops++;

                prevAudioTime = t;
                songTime = loops * clipLength + t;
                return;
            }

            songTime += Time.deltaTime;
        }

        private void TickDispatch()
        {
            if (!songStarted || pending.Count == 0) return;

            var next = pending[0];
            if (ChartTime < next.entry.spawnTimes[0]) return;

            Dispatch(next);
        }

        private void Dispatch(EntryCursor cursor)
        {
            var entry = cursor.entry;
            float baseTime = entry.spawnTimes[0];
            var relativeInputTimes = entry.onsetTimes.Select(t => t - baseTime).ToArray();
            var relativeSpawnTimes = entry.spawnTimes.Select(t => t - baseTime).ToArray();

            // cue를 SetPattern '직전에' 옆으로 밀어 넣는다 — 판정 계층(PatternHandler)은 적을 몰라야 하므로
            // 페이로드에 싣지 않는다. 디렉터는 OnPatternQueued에서 같은 FIFO 순서로 꺼낸다.
            // ⚠ 되돌린 사슬은 엔트리마다 여기를 다시 지나므로 cue도 전부 다시 들어간다. 한 엔트리라도
            //   빠뜨리면 재시도본이 다음 엔트리의 cue를 먹고 전 채보가 한 칸씩 영구히 밀린다(§11-1의 부류).
            enemyDirector?.EnqueueCue(entry.enemyCue);
            patternHandler.SetPattern(entry.template, relativeInputTimes, relativeSpawnTimes,
                null, entry.enemyCue != null && !entry.enemyCue.killOnSuccess, cursor.experienced);

            pending.RemoveAt(0);
            inAir.Add(cursor);
        }

        // ── 진행 판단 ────────────────────────────────────────────────────────

        /// <summary>
        /// 패턴 하나가 끝났다. <b>소비냐 재시도냐</b>를 여기서 가른다.
        ///
        /// <para><b>완료 순서가 곧 투입 순서다</b>(취소도 FIFO로 발행된다) — 그래서 <see cref="inAir"/>의
        /// 맨 앞이 언제나 이 완료의 주인이다.</para>
        /// </summary>
        private void HandlePatternComplete(PatternCompletionInfo info)
        {
            if (pending == null || inAir.Count == 0) return;

            var done = inAir[0];
            inAir.RemoveAt(0);

            // 취소 — 우리가 방금 요청한 회수다. 입력 기회가 없었으므로 '겪은' 것으로 치지 않고,
            // 대기 목록으로 되돌린다(index 오름차순이 유지되므로 정렬 한 번이면 자리가 정확하다).
            if (info.Cancelled)
            {
                Requeue(done);
                return;
            }

            done.experienced = true;

            if (activeMode != StageMode.Loop) return;

            // 소비 = 성공했거나, 실패했지만 적의 칼이 닿았다. 즉 대가를 치렀으면 그 엔트리는 끝난 것이다.
            // ⚠ OnPlayerHit을 구독해 판단할 수 없다 — 그것은 임팩트 시각(이 완료보다 goodWindow 뒤)에 난다.
            bool consumed = info.AllCorrect || (done.entry.template != null && done.entry.template.HitsOnFail);
            if (consumed) return;

            RequestRetry(done);
        }

        /// <summary>
        /// 이 엔트리가 속한 <b>사슬 전체</b>를 되돌리고 채보 시계를 그 머리로 되감는다.
        ///
        /// <para><b>사슬이 단위인 이유</b>: 사슬 중간을 실패하면 그 뒤 타는 이미 취소됐거나 투입도 안 됐고,
        /// 적은 안 죽은 채 리액션만 반복하게 된다 — 머리부터 다시 치는 것이 그 교전의 유일한 정의다.</para>
        /// </summary>
        private void RequestRetry(EntryCursor failed)
        {
            var head = ordered[failed.chainHead];

            // ⚠ 되감기 양을 박(또는 마디) 간격으로 올림한다. ChartTime − spawnTimes[0]은 임의 실수라
            //   그대로 쓰면 격자에서 벗어난 채로 굳고, 그 드리프트는 되감을 때마다 쌓인다.
            float back = CeilToGrid(ChartTime - head.entry.spawnTimes[0]);
            chartOffset += back + RetryGapSeconds();

            // 이 사슬의 엔트리를 전부 대기 목록으로 되돌린다. 아직 투입되지 않은 뒤쪽 타가 남아 있을 수 있어
            // 먼저 빼고 다시 넣는다(안 그러면 같은 엔트리가 두 번 나온다).
            pending.RemoveAll(c => c.chainHead == failed.chainHead);
            for (int i = failed.chainHead; i < ordered.Count && ordered[i].chainHead == failed.chainHead; i++)
                pending.Add(ordered[i]);

            SortPending();

            // 큐에 이미 올라간 패턴(언제나 최대 1개)은 다음 Update에서 회수한다.
            retryRequested = true;

            chainAttempts.TryGetValue(failed.chainHead, out int attempts);
            attempts = attempts <= 0 ? 2 : attempts + 1;
            chainAttempts[failed.chainHead] = attempts;

            OnPatternRetried?.Invoke(head.entry, attempts);
        }

        /// <summary>취소된 엔트리를 대기 목록으로 되돌린다. 이미 들어 있으면(같은 사슬) 아무 일도 안 한다.</summary>
        private void Requeue(EntryCursor cursor)
        {
            if (pending.Contains(cursor)) return;

            pending.Add(cursor);
            SortPending();
        }

        /// <summary>
        /// <see cref="pending"/>은 언제나 <see cref="EntryCursor.index"/> 오름차순이다.
        /// 되돌린 사슬은 인덱스가 작아 정렬만 하면 제자리로 간다 — 앞/뒤를 따질 필요가 없다.
        /// </summary>
        private void SortPending() => pending.Sort((a, b) => a.index.CompareTo(b.index));

        private float BeatInterval => Bpm > 0f ? 60f / Bpm : 0f;

        private int BeatsPerBar
        {
            get
            {
                int fromChart = ActiveChart != null ? ActiveChart.beatsPerBar : 0;
                return Mathf.Max(fromChart > 0 ? fromChart : fallbackBeatsPerBar, 1);
            }
        }

        /// <summary>재시도 대기(초). 박 수로 저작하므로 BPM이 바뀌면 같이 따라온다.</summary>
        private float RetryGapSeconds()
        {
            float beat = BeatInterval;

            // ⚠ BPM을 모르면 격자 보존을 포기하고 박 수를 초로 읽는다. 잠기는 것보다 낫다.
            if (beat <= 0f)
            {
                WarnMissingBpm();
                return retryGapBeats;
            }

            return retryGapBeats * beat;
        }

        /// <summary>되감기 양을 격자 단위(박 또는 마디)로 <b>올림</b>한다. 이 올림이 드리프트를 박의 정수배로 묶는다.</summary>
        private float CeilToGrid(float seconds)
        {
            float value = Mathf.Max(seconds, 0f);
            float beat = BeatInterval;

            if (beat <= 0f)
            {
                WarnMissingBpm();
                return value;
            }

            float unit = rewindToBar ? beat * BeatsPerBar : beat;
            return Mathf.Ceil(value / unit) * unit;
        }

        private void WarnMissingBpm()
        {
            if (bpmWarned) return;
            bpmWarned = true;

            Debug.LogWarning("[ChartPlayer] 채보의 BPM이 0 이하입니다. 재시도 되감기를 박 격자에 맞출 수 없어 " +
                             "패턴이 음악에서 조금씩 밀립니다(retryGapBeats를 초로 읽습니다).", this);
        }

        // ── 종료 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 끝났는지 보고, 끝났으면 여운만큼 기다린 뒤 곡을 줄이고 종료를 알린다.
        ///
        /// <para><b>⚠ <see cref="Stop"/>을 재사용하지 않는다</b> — 그쪽은 <c>ClearAllPatterns</c>를 부르고
        /// 그것이 마무리 실루엣을 죽인다(<c>OnAllPatternsCleared</c>). 여기서는 <b>오디오만</b> 멈춘다.</para>
        /// </summary>
        private void TickFinish()
        {
            // ⚠ 곡이 시작되기 전에는 묻지 않는다. spawnTimes가 전부 비어 걸러진 채보에서는
            //    pending이 처음부터 0이라, 카운트다운이 도는 중에 종료가 나가 버린다.
            if (songEndRaised || !songStarted) return;

            bool cleared = activeMode == StageMode.Loop
                ? pending.Count == 0 && inAir.Count == 0 && !patternHandler.HasActivePatterns
                : pending.Count == 0 && !audioSource.isPlaying;

            if (!cleared)
            {
                finishAt = float.NaN;
                return;
            }

            if (float.IsNaN(finishAt))
            {
                finishAt = Time.time + outroHold;
                return;
            }

            if (Time.time < finishAt) return;

            // ⚠ 실루엣은 holdDuration이 더 길 수 있어 따로 기다린다. outroHold에 합치면
            //   실루엣이 없는 실패 경로가 이유 없이 길어진다.
            if (finale != null && finale.IsBusy) return;

            songEndRaised = true;
            StartCoroutine(FinishRoutine());
        }

        /// <summary>
        /// 이 채보가 <b>죽이는 적의 수</b>. <c>EnemyDirector.PrepareStage</c>가 세울 인원이다.
        ///
        /// <para><b>⚠ <c>Count(…)</c>가 아니라 합이다.</b> 값은 지금 같지만, 한 엔트리가 여럿을 죽이게 되면
        /// (다수 상대) <b>이 한 항만 바뀐다</b> — <c>Count</c>로 적으면 셈의 뜻 자체를 다시 정해야 한다.</para>
        /// </summary>
        private static int CountKills(SongChart chart)
        {
            if (chart == null || chart.entries == null) return 0;

            int total = 0;
            foreach (var entry in chart.entries)
                total += entry.enemyCue.killOnSuccess ? 1 : 0;

            return total;
        }

        private IEnumerator FinishRoutine()
        {
            // ⚠ 루프곡에서는 Stop()이 선택이 아니다 — loop = true면 곡이 스스로 끝나지 않으므로
            //   이 경로가 유일한 정지 지점이다.
            // ⚠ 잔여 적을 여기서 지운다 — 무대는 마지막 처치로 비는 것이 정상이고(적 수 = 처치 수),
            //   남는 것은 StageMode.Linear에서 처치 패턴을 실패했을 때뿐이다. 실루엣(§14)은 위에서
            //   이미 기다렸으므로 "검게 물든 적이 먼저 사라진다"는 함정에 걸리지 않고,
            //   페이드 <b>앞</b>이라 소멸이 화면에 보인다(예전에는 페이드가 끝난 뒤라 그냥 사라졌다).
            enemyDirector?.DissolveAll();

            if (outroFade > 0f && audioSource.isPlaying)
            {
                for (float t = 0f; t < outroFade; t += Time.deltaTime)
                {
                    fadeFactor = 1f - t / outroFade;
                    ApplyMusicVolume();
                    yield return null;
                }
            }

            audioSource.Stop();
            fadeFactor = 1f;
            ApplyMusicVolume();
            songStarted = false;

            OnSongEnded?.Invoke();
        }
    }
}
