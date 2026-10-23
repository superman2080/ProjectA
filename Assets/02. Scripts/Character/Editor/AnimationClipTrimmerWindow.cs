using System.Collections.Generic;
using PatternSpace;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 패턴 하나의 <b>전투 액션 짝</b>(플레이어 + 적)을 함께 저작하는 툴.
///
/// <para><b>저작 단위가 클립 하나가 아니라 패턴 하나다.</b> 적이 인터랙티브해지면서
/// 확인해야 하는 것이 "칼이 적에 닿는가 / 임팩트 순간 적 포즈가 말이 되는가"로 바뀌었고,
/// 그 판단은 <b>두 마크를 되먹임하며 조정해야 수렴한다</b>. 슬롯을 하나씩 열어 찍고 나가는 방식으로는 안 된다.</para>
///
/// <para><b>시간축은 임팩트 기준 상대시간 하나다.</b> <c>t = 0</c>이 두 클립의 임팩트 프레임이고,
/// 곧 칼이 지나가는 순간 = 적이 베어지는 순간이다. 배속이 서로 달라도
/// <c>t = 0</c>에서는 식에서 소거되므로 <b>판단이 이루어지는 지점은 언제나 정확하다</b>.</para>
///
/// <para><b>상태는 언제나 클립 시간으로 들고 있다</b>(에셋과 같은 단위). <c>t</c>는 뷰 전용 파생값이라
/// 저장 경로에 변환이 없다 — 조용히 어긋날 코드가 존재하지 않는다. 산술은 전부
/// <see cref="DuetTimeline"/>(테스트됨)에 있다.</para>
/// </summary>
public class AnimationClipTrimmerWindow : EditorWindow
{
    private const float PreviewHeight = 340f;
    private const string WeaponBoneName = "add_weapon_r";

    [MenuItem("Tools/Animation Clip Trimmer")]
    private static void Open() => GetWindow<AnimationClipTrimmerWindow>("Pattern Action Editor");

    /// <summary>패턴을 지정해 연다. 굽기 툴(<c>Pattern Chart Tool</c>)이 프리뷰에서 넘어올 때 쓴다.</summary>
    public static void Open(Pattern pattern)
    {
        var window = GetWindow<AnimationClipTrimmerWindow>("Pattern Action Editor");
        window.targetPattern = pattern;
        window.LoadFromPattern();
    }

    /// <summary>배우 하나의 저작 상태. <b>전부 클립 절대시간</b>이라 저장 시 변환이 없다.</summary>
    private class Actor
    {
        public string label;
        public string slotPath;      // Pattern의 ClipAlignment 필드 이름
        public AnimationClip clip;
        public float clipStart;
        public float clipImpact;
        public float clipEnd;
        public float speed = 1f;

        /// <summary>마지막 베기 '이전'의 칼질들(클립 절대 초). 다중 히트스톱 전용이라 플레이어 슬롯에서만 의미가 있다.</summary>
        public readonly List<float> extraImpacts = new List<float>();

        public GameObject prefab;    // 프리뷰용
        public GameObject instance;
        public GameObject cachedPrefab;
        public Vector3 placement;
        public Quaternion facing;

        /// <summary>
        /// 이 배우 자리가 <b>리드인 원소</b>인가. 리드인은 정렬 앵커가 아니라서 <c>ImpactTime</c>을 저장하지 않고
        /// (찍혀 있으면 <c>Pattern.OnValidate</c>가 경고한다) 트림 끝이 t 축의 <see cref="tOffset"/> 지점에 온다.
        /// </summary>
        public bool isLeadIn;

        /// <summary>
        /// 이 클립의 앵커(<see cref="clipImpact"/>)가 놓이는 t. 마지막 클립은 0이고, 리드인 원소는
        /// <c>ClipSequence.AuthoredEndOffset</c>이 준 음수다 — 그래야 원소들이 타임라인에 <b>이어져</b> 보인다.
        /// </summary>
        public float tOffset;

        public bool HasClip => clip != null;
        public float Duration => clipEnd - clipStart;

        /// <summary>지금 t에서 이 배우가 서 있어야 할 클립 시각(트림으로 잘린 값).</summary>
        public float SampleTime(float t) => DuetTimeline.ClampToTrim(
            DuetTimeline.ClipTimeOf(clipImpact, speed, t - tOffset), clipStart, clipEnd);
    }

    /// <summary>
    /// 처치가 확정되는 시각(임팩트 기준 상대시간, 양수 = 임팩트보다 이만큼 앞).
    /// 런타임에서는 마지막 노드 입력이 그 순간이고 임팩트는 거기서 <c>goodWindow</c>만큼 뒤다 —
    /// <b>견제가 사망으로 넘어가는 지점이 정확히 여기다.</b>
    /// </summary>
    private const float HandoffLead = 0.1f;

    /// <summary>정지 예산이 이 값(초)을 넘으면 경고한다. 실측: 엔트리 간 최소 입력 간격이 0.4초다.</summary>
    private const float ExtraFreezeBudgetWarning = 0.3f;

    /// <summary>
    /// 한 번 멈추는 시간(초). <b>런타임 값은 씬의 <c>HitStopDirector</c>가 든다</b> — 이 창은 씬 없이도 돌아야 해서
    /// 예산 <b>표시용 근사</b>만 둔다(굽기 툴의 <c>GoodWindowApprox</c>와 같은 규율). 저장값에는 관여하지 않는다.
    /// </summary>
    private float hitStopDurationHint = 0.1f;

    // 입력
    private Pattern targetPattern;
    private float duelBaseDistance = 1f;
    private float duelDistanceOffset;
    private bool lockRootPosition = true;

    private readonly Actor player = new Actor { label = "플레이어" };
    private readonly Actor enemy = new Actor { label = "적" };

    /// <summary>
    /// 적의 <b>앞 구간</b>(견제). <see cref="enemy"/>와 같은 프리팹 인스턴스를 시간으로 나눠 쓴다 —
    /// 런타임이 한 배우의 한 줄 진행이기 때문이다. 배우를 둘로 그리면 화면에 적이 둘 서 있게 된다.
    /// </summary>
    private readonly Actor feint = new Actor { label = "적 — enemyFeint (견제)" };

    /// <summary>
    /// <see cref="feint"/> 자리에 어느 적 보조 슬롯을 열지. 셋 다 <b>적 한 명의 다른 시각 구간</b>이라
    /// 배우를 늘리지 않고 슬롯만 갈아 끼운다(배우를 더 그리면 화면에 적이 여럿 서 있게 된다).
    /// </summary>
    private enum EnemyAuxSlot
    {
        Feint,   // 표적이 된 순간 ~ 임팩트 (Attacker.Player 전용)
        Attack,  // 표적이 된 순간 ~ 임팩트 — 상호 공격(Attacker.Player에서 견제를 대체한다)
        Hit,     // 임팩트 — 맞았는데 안 죽었다(사슬 중간 타격)
        Parry    // 임팩트 — 막아냈다
    }

    private EnemyAuxSlot enemyAuxSlot = EnemyAuxSlot.Feint;

    /// <summary>
    /// 플레이어 자리에 열려 있는 클립의 <b>종류</b>. 셋 다 한 배우의 다른 구간이라 배우를 늘리지 않고
    /// 슬롯만 갈아 끼운다(<see cref="EnemyAuxSlot"/>과 같은 관용구).
    ///
    /// <para>종류와 인덱스를 <b>한 정수에 인코딩하지 않는다</b> — 리드인과 연타 타격이 둘 다 리스트라
    /// 음수/양수로 가르면 세 번째가 생기는 순간 무너진다.</para>
    /// </summary>
    private enum PlayerSlotKind
    {
        /// <summary>정렬 앵커(<c>playerAttack</c>/<c>playerParry</c>). 연타에서는 '마무리 일격'이다.</summary>
        Anchor,

        /// <summary>앵커 앞에 순서대로 붙는 리드인 원소.</summary>
        LeadIn,

        /// <summary>연타 타격 클립. <b>정렬 대상이 아니지만 ImpactTime은 의미가 있다</b> — 재생 시작점이다.</summary>
        MashHit
    }

    private PlayerSlotKind playerSlotKind = PlayerSlotKind.Anchor;

    /// <summary>리스트형 슬롯에서 몇 번째인지. <see cref="PlayerSlotKind.Anchor"/>면 쓰이지 않는다.</summary>
    private int playerSlotIndex;

    private int LeadInCount => targetPattern != null && targetPattern.PlayerLeadInClips != null
        ? targetPattern.PlayerLeadInClips.Count
        : 0;

    private int MashClipCount => targetPattern != null && targetPattern.MashStrikes != null
        ? targetPattern.MashStrikes.Count
        : 0;

    private bool IsMashTarget => targetPattern != null && targetPattern.IsMash;

    /// <summary>지금 패턴의 정렬 앵커 슬롯(역할이 정한다).</summary>
    private ClipAlignment AnchorAlignment => targetPattern == null
        ? null
        : (targetPattern.Attacker == EnemySpace.Attacker.Enemy ? targetPattern.PlayerParry : targetPattern.PlayerAttack);

    private static string AuxSlotPath(EnemyAuxSlot slot) => slot switch
    {
        EnemyAuxSlot.Attack => "enemyAttack",
        EnemyAuxSlot.Hit => "enemyHit",
        EnemyAuxSlot.Parry => "enemyParry",
        _ => "enemyFeint"
    };

    private static string AuxSlotLabel(EnemyAuxSlot slot) => slot switch
    {
        EnemyAuxSlot.Attack => "적 — enemyAttack (상호 공격)",
        EnemyAuxSlot.Hit => "적 — enemyHit (피격)",
        EnemyAuxSlot.Parry => "적 — enemyParry (패링)",
        _ => "적 — enemyFeint (견제)"
    };

    // 시간축 (임팩트 기준 상대시간)
    private float t;
    private bool isPlaying;
    private double lastUpdateTime;

    // 프리뷰
    private PreviewRenderUtility previewUtil;
    private float yaw = 120f;
    private float pitch = 10f;
    private float zoom = 1f;
    private Vector2 scroll;

    private float bladeGap = float.NaN;

    /// <summary>패턴이 커브를 저작하는 중인지. 창의 커브가 진실의 원천이다(에셋 저장 전 상태를 프리뷰에 반영).</summary>
    private AnimationCurve duelDistanceCurve = new AnimationCurve();

    /// <summary>기준 거리를 씬 앵커에서 읽었는가. 못 읽으면 수동 입력이 그대로 산다.</summary>
    private bool duelBaseFromScene;

    /// <summary>높이 커브(창의 것이 진실의 원천 — 저장 전 상태를 프리뷰에 반영한다).</summary>
    private AnimationCurve duelHeightCurve = new AnimationCurve();

    /// <summary>
    /// 이 패턴이 상대하는 적들의 자리(임팩트 시점 플레이어 기준 결투 축 로컬 평면, m).
    /// <b>길이가 곧 교전 인원이다</b> — 비어 있으면 마주보고 한 명(예전 동작).
    /// </summary>
    private readonly List<Vector2> engageOffsets = new List<Vector2>();

    /// <summary>
    /// 적 2..N의 <b>프리뷰 전용</b> 몸. 클립은 전원이 공유하므로(A안, docs/PatternMultiTarget)
    /// 저작 상태(<see cref="Actor"/>)를 늘리지 않고 <b>몸만</b> 늘린다.
    /// </summary>
    private readonly List<GameObject> enemyMirrors = new List<GameObject>();

    /// <summary>적마다의 칼–몸 간격(m). 음수 = 관통(닿았다). 인덱스가 <see cref="engageOffsets"/>와 짝이다.</summary>
    private readonly List<float> mirrorGaps = new List<float>();

    /// <summary>배치 패드에서 지금 끌고 있는 점. -1이면 없음.</summary>
    private int padDragIndex = -1;

    private bool HasCurve => duelDistanceCurve != null && duelDistanceCurve.length > 0;

    private bool HasHeightCurve => duelHeightCurve != null && duelHeightCurve.length > 0;

    /// <summary>이 패턴이 동시에 상대하는 적 수. <b>배치 리스트의 길이가 곧 인원이다.</b></summary>
    private int EngagedCount => EngageSlots.CountOf(engageOffsets);

    /// <summary>
    /// <paramref name="time"/>에서의 플레이어 높이(m, 지면 기준). <b>런타임 <see cref="DuelGap.HeightAt"/>과 같은 식</b>이며
    /// 커브만 창의 것을 쓴다 — 저장 전에도 프리뷰가 편집 중인 값을 보여 줘야 한다.
    /// </summary>
    private float HeightAt(float time) => DuelGap.HeightAt(duelHeightCurve, time);

    /// <summary>임팩트 시점(t = 0)의 간격. 적 배치와 카메라 폴백이 이 값을 쓴다.</summary>
    private float DuelDistance => GapAt(0f);

    /// <summary>
    /// <paramref name="time"/>(임팩트 기준 상대초)에서의 간격(m).
    /// <b>런타임 <see cref="Pattern.DuelGapAt"/>과 같은 식</b>이며, 커브만 창의 것을 쓴다 —
    /// 저장 전에도 프리뷰가 지금 편집 중인 값을 보여 줘야 하기 때문이다.
    /// </summary>
    private float GapAt(float time) => HasCurve
        ? duelDistanceCurve.Evaluate(time)
        : Mathf.Max(duelBaseDistance + duelDistanceOffset, 0.1f);

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        lastUpdateTime = EditorApplication.timeSinceStartup;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        DestroyInstance(player);
        DestroyInstance(enemy);
        DestroyMirrors();
        previewUtil?.Cleanup();
        previewUtil = null;
    }

    private void OnEditorUpdate()
    {
        if (!isPlaying) return;

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastUpdateTime);
        lastUpdateTime = now;

        var (min, max) = ScrubRange();
        t += dt;
        if (t > max) t = min;

        Repaint();
    }

    // ─────────────────────────── 창 ───────────────────────────

    private void OnGUI()
    {
        lastUpdateTime = EditorApplication.timeSinceStartup; // 창 비활성 후 점프 방지

        // 창 전체가 스크롤된다 — 프리뷰(340px)·타임라인을 고정해 두면 창이 낮을 때 아래 옵션 영역이 0으로 짜부라진다.
        // 프리뷰 위의 휠은 줌이 먹는다(HandlePreviewInput이 Use한다) — 스크롤은 프리뷰 밖에서 한다.
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawInputs();

        if (targetPattern == null)
        {
            EditorGUILayout.HelpBox("Pattern을 지정하면 그 패턴의 역할에 맞는 두 배우가 열립니다.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawPreview();
        DrawReadouts();
        DrawTimeline();

        DrawActorBlock(player);
        if (feint.slotPath != null) DrawActorBlock(feint); // 시간순: 견제가 사망보다 앞이다
        DrawActorBlock(enemy);
        DrawNotes();
        DrawSwordTrailEvents();
        DrawApply();
        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// 플레이어 자리에 어느 클립을 열지 고르고, 리드인 리스트를 편집한다.
    ///
    /// <para><b>총 저작 시간을 같이 찍는다</b> — 그 값이 채보의 창을 넘으면 런타임이 시퀀스 전체를 압축한다.
    /// 다만 경고하지 않는다: 창은 채보 길이로 해결하는 것이 이 기능의 전제라, 여기서 필요한 것은
    /// <b>판단할 숫자</b>이지 금지가 아니다(<c>추가 칼질</c>의 정지 예산 표시와 같은 결).</para>
    /// </summary>
    private void DrawPlayerSlotSelector()
    {
        int leadIn = LeadInCount;
        int mash = IsMashTarget ? MashClipCount : 0;

        // 목록: [마지막] [리드인 0..N] [연타 타격 0..M]
        var names = new string[1 + leadIn + mash];
        names[0] = IsMashTarget ? "마지막 (마무리 일격)" : "마지막 (정렬 앵커)";
        for (int i = 0; i < leadIn; i++) names[1 + i] = $"리드인 [{i}]";
        for (int i = 0; i < mash; i++) names[1 + leadIn + i] = $"연타 타격 [{i}]";

        int current = playerSlotKind switch
        {
            PlayerSlotKind.LeadIn => 1 + Mathf.Clamp(playerSlotIndex, 0, Mathf.Max(leadIn - 1, 0)),
            PlayerSlotKind.MashHit => 1 + leadIn + Mathf.Clamp(playerSlotIndex, 0, Mathf.Max(mash - 1, 0)),
            _ => 0
        };

        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(
            new GUIContent("플레이어 슬롯", "한 배우의 다른 구간이라 배우를 늘리지 않고 슬롯만 바꾼다.\n" +
                                       "리드인 = 마지막 클립 '앞에' 순서대로 재생(ImpactTime 없음).\n" +
                                       "연타 타격 = 입력마다 번갈아 재생(ImpactTime이 '재생 시작점'이다)."),
            Mathf.Clamp(current, 0, names.Length - 1), names);
        if (EditorGUI.EndChangeCheck() && picked != current && ConfirmBeforeReload())
        {
            if (picked == 0) { playerSlotKind = PlayerSlotKind.Anchor; playerSlotIndex = 0; }
            else if (picked <= leadIn) { playerSlotKind = PlayerSlotKind.LeadIn; playerSlotIndex = picked - 1; }
            else { playerSlotKind = PlayerSlotKind.MashHit; playerSlotIndex = picked - 1 - leadIn; }

            LoadFromPattern();
        }

        if (IsMashTarget) DrawMashClipTools(mash);
        else DrawLeadInTools(leadIn);
    }

    /// <summary>연타 타격 리스트 편집 + 이 슬롯의 뜻을 한 줄로 설명한다.</summary>
    private void DrawMashClipTools(int count)
    {
        bool onMash = playerSlotKind == PlayerSlotKind.MashHit;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ 타격 클립 추가", GUILayout.Width(130f)))
                InsertListElement("mashStrikes", count, PlayerSlotKind.MashHit, count);

            using (new EditorGUI.DisabledScope(!onMash))
            {
                if (GUILayout.Button("◀", GUILayout.Width(30f))) MoveListElement("mashStrikes", playerSlotIndex, -1, PlayerSlotKind.MashHit);
                if (GUILayout.Button("▶", GUILayout.Width(30f))) MoveListElement("mashStrikes", playerSlotIndex, 1, PlayerSlotKind.MashHit);
                if (GUILayout.Button("클립 삭제", GUILayout.Width(80f))) RemoveListElement("mashStrikes", playerSlotIndex);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"연타 {targetPattern.MashTargetHits}타 · 클립 {count}개",
                EditorStyles.miniLabel, GUILayout.Width(160f));
        }

        // 어느 타격이 자기 리액션을 들고 어느 타격이 공용 폴백을 쓰는지 한 줄로 보여 준다.
        //
        // ⚠ 이게 없으면 "저장이 안 된다"로 읽힌다 — 공용 Pattern.EnemyHit이 채워져 있으면
        //   리액션을 하나만 꽂아도 나머지가 그 클립으로 폴백해 화면이 전혀 안 바뀌기 때문이다.
        if (count > 0)
        {
            var owned = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                var st = targetPattern.MashStrikes[i];
                bool has = st != null && st.EnemyReaction != null && st.EnemyReaction.IsUsable;
                owned.Append(i == 0 ? "" : "  ")
                     .Append('[').Append(i).Append("] ")
                     .Append(has ? st.EnemyReaction.Clip.name : "공용");
            }

            string shared = targetPattern.EnemyHit != null && targetPattern.EnemyHit.IsUsable
                ? targetPattern.EnemyHit.Clip.name
                : "없음(knockBack 스테이트)";

            EditorGUILayout.LabelField("적 리액션", owned.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(" ", $"공용 폴백 = {shared}", EditorStyles.miniLabel);
        }

        if (onMash)
        {
            EditorGUILayout.HelpBox(
                "연타 타격 클립입니다 — 입력마다 이 리스트를 번갈아 돕니다.\n" +
                "⚠ Impact는 '칼이 닿는 시각'이 아니라 재생 시작점입니다. 런타임은 Impact보다 mashPreRoll만큼 " +
                "앞에서 시작해 트림 끝까지 재생하므로, 여기서 t = 0 근처가 곧 화면에 보이는 구간입니다.\n" +
                "⚠ 타격 간격이 0.15초 수준이라 보이는 것은 그 앞부분뿐입니다 — 베는 동작이 Impact 직후에 오게 잡으세요.",
                MessageType.Info);
        }
        else if (count == 0)
        {
            EditorGUILayout.HelpBox("타격 클립이 없습니다 — 두들겨도 아무 모션이 안 나옵니다.", MessageType.Warning);
        }

        if (AnchorAlignment == null || !AnchorAlignment.IsUsable)
        {
            EditorGUILayout.HelpBox(
                "마무리 일격(마지막 슬롯)이 비어 있습니다. 연타가 끝날 때 전용 마무리 모션 없이 절단만 일어납니다 — " +
                "의도라면 그대로 두세요. 채우면 창 끝에 정렬돼 들어오고, 그만큼 연타 입력이 일찍 닫힙니다.",
                MessageType.None);
        }
    }

    private void DrawLeadInTools(int count)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ 리드인 추가", GUILayout.Width(110f)))
                InsertListElement("playerLeadInClips", count, PlayerSlotKind.LeadIn, count);

            using (new EditorGUI.DisabledScope(playerSlotKind != PlayerSlotKind.LeadIn))
            {
                if (GUILayout.Button("◀", GUILayout.Width(30f))) MoveListElement("playerLeadInClips", playerSlotIndex, -1, PlayerSlotKind.LeadIn);
                if (GUILayout.Button("▶", GUILayout.Width(30f))) MoveListElement("playerLeadInClips", playerSlotIndex, 1, PlayerSlotKind.LeadIn);
                if (GUILayout.Button("원소 삭제", GUILayout.Width(80f))) RemoveListElement("playerLeadInClips", playerSlotIndex);
            }

            GUILayout.FlexibleSpace();

            float authored = ClipSequence.AuthoredSpan(targetPattern.PlayerLeadInClips, AnchorAlignment);
            EditorGUILayout.LabelField(
                $"시퀀스 {count + 1}개 · 저작 {authored:0.00}s", EditorStyles.miniLabel, GUILayout.Width(180f));
        }

        if (count > 0 && (AnchorAlignment == null || !AnchorAlignment.IsUsable))
        {
            EditorGUILayout.HelpBox(
                "리드인이 있는데 정렬 앵커(마지막 클립)가 비어 있습니다 — 시퀀스 전체가 무연출입니다.",
                MessageType.Warning);
        }
    }

    /* 아래 셋은 리드인과 연타 타격이 <b>공유</b>한다. 둘 다 ClipAlignment 리스트라
     * 배열 이름만 다르고 조작이 같다 — 따로 두면 한쪽만 고쳐지는 날이 온다. */

    private void InsertListElement(string arrayName, int index, PlayerSlotKind kind, int selectIndex)
    {
        // 배열을 건드리기 전에 묻는다 — 인덱스가 밀리면 슬롯 경로가 엉뚱한 원소를 가리킨다.
        if (!ConfirmBeforeReload()) return;

        var so = new SerializedObject(targetPattern);
        var list = so.FindProperty(arrayName);
        if (list == null || !list.isArray) return;

        list.InsertArrayElementAtIndex(index);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(targetPattern);

        playerSlotKind = kind;
        playerSlotIndex = selectIndex;
        LoadFromPattern();
    }

    private void RemoveListElement(string arrayName, int index)
    {
        // 배열을 건드리기 전에 묻는다 — 인덱스가 밀리면 슬롯 경로가 엉뚱한 원소를 가리킨다.
        if (!ConfirmBeforeReload()) return;

        var so = new SerializedObject(targetPattern);
        var list = so.FindProperty(arrayName);
        if (list == null || !list.isArray || index < 0 || index >= list.arraySize) return;

        list.DeleteArrayElementAtIndex(index);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(targetPattern);

        // 지운 자리를 계속 가리키면 범위 밖이 된다 — 앵커로 물러난다.
        playerSlotKind = PlayerSlotKind.Anchor;
        playerSlotIndex = 0;
        LoadFromPattern();
    }

    private void MoveListElement(string arrayName, int index, int delta, PlayerSlotKind kind)
    {
        // 배열을 건드리기 전에 묻는다 — 인덱스가 밀리면 슬롯 경로가 엉뚱한 원소를 가리킨다.
        if (!ConfirmBeforeReload()) return;

        var so = new SerializedObject(targetPattern);
        var list = so.FindProperty(arrayName);
        if (list == null || !list.isArray) return;

        int target = index + delta;
        if (target < 0 || target >= list.arraySize) return;

        list.MoveArrayElement(index, target);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(targetPattern);

        playerSlotKind = kind;
        playerSlotIndex = target;
        LoadFromPattern();
    }

    private void DrawInputs()
    {
        EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        var pickedPattern = (Pattern)EditorGUILayout.ObjectField("Pattern", targetPattern, typeof(Pattern), false);
        if (EditorGUI.EndChangeCheck() && pickedPattern != targetPattern)
        {
            if (ConfirmBeforeReload())
            {
                targetPattern = pickedPattern;
                playerSlotKind = PlayerSlotKind.Anchor;
                playerSlotIndex = 0;
                LoadFromPattern();
            }
        }

        if (targetPattern == null) return;

        bool enemyIsAttacker = targetPattern.Attacker == EnemySpace.Attacker.Enemy;
        EditorGUILayout.LabelField("역할",
            enemyIsAttacker ? "Enemy — 적 공격 → 플레이어 패링" : "Player — 플레이어 공격 → 적 사망");

        DrawPlayerSlotSelector();

        EditorGUI.BeginChangeCheck();
        enemyAuxSlot = (EnemyAuxSlot)EditorGUILayout.EnumPopup(
            new GUIContent("적 보조 슬롯", "적 한 명의 다른 시각 구간이라 배우를 늘리지 않고 슬롯만 바꾼다.\n" +
                                      "Feint = 표적이 된 순간~임팩트 / Attack = 상호 공격(같은 구간, 닿는다) / " +
                                      "Hit = 맞았는데 안 죽음(사슬 중간) / Parry = 막아냄."),
            enemyAuxSlot);
        if (EditorGUI.EndChangeCheck()) LoadFromPattern();

        if (enemyAuxSlot == EnemyAuxSlot.Hit || enemyAuxSlot == EnemyAuxSlot.Parry)
        {
            EditorGUILayout.HelpBox(
                "피격·패링은 임팩트에 시작합니다 — ImpactTime을 찍지 않으면 t = 0이 클립 시작입니다.\n" +
                "⚠ 트림 0.5초 이하 권장: 길면 다음 패턴의 견제 클립이 끊습니다.",
                MessageType.Info);
        }

        if (!enemyIsAttacker && enemyAuxSlot == EnemyAuxSlot.Attack)
        {
            EditorGUILayout.HelpBox(
                "상호 공격 — 적이 견제 대신 진짜 공격을 같이 휘두릅니다. 클립을 배선하면 이 패턴은 " +
                "실패 시 플레이어가 대미지를 입습니다(비우면 견제 경로로 돌아갑니다).\n" +
                "ImpactTime은 플레이어 공격과 같은 t = 0에 찍습니다 — 두 칼이 같은 시각을 겨눕니다.\n" +
                "성공하면 enemyHit(또는 enemyDeath)이 이 클립을 임팩트 직전에 끊습니다.",
                MessageType.Info);
        }

        if (!enemyIsAttacker && enemyAuxSlot == EnemyAuxSlot.Feint && feint.HasClip)
        {
            EditorGUILayout.HelpBox(
                $"적은 한 줄로 진행합니다 — 견제 → (t = {-HandoffLead:0.00}s에서 처치 확정) → 사망.\n" +
                "그 시각을 기준으로 프리뷰의 적이 두 클립을 이어서 재생하므로, 견제 끝 포즈와 사망 시작 포즈가 이어지는지 볼 수 있습니다.\n" +
                "견제는 닿지 않는 동작이라 ImpactTime을 찍지 않는 것이 기본입니다(트림 끝이 t = 0에 옵니다).",
                MessageType.Info);
        }

        EditorGUILayout.BeginHorizontal();
        player.prefab = (GameObject)EditorGUILayout.ObjectField("플레이어 프리팹", player.prefab, typeof(GameObject), false);
        enemy.prefab = (GameObject)EditorGUILayout.ObjectField("적 프리팹", enemy.prefab, typeof(GameObject), false);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        duelBaseDistance = EditorGUILayout.FloatField(
            new GUIContent("기준 결투 거리", "씬 결투 앵커의 거리. 가이드 4단계에서 잡은 값을 넣는다."),
            duelBaseDistance);
        duelDistanceOffset = EditorGUILayout.FloatField(
            new GUIContent("거리 보정 (패턴 저장)", "이 모션의 리치에 맞춘 ±m. Apply 때 패턴에 함께 저장된다."),
            duelDistanceOffset);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField(" ",
            duelBaseFromScene
                ? $"기준 {duelBaseDistance:0.00}m — 씬 결투 앵커에서 읽음"
                : $"기준 {duelBaseDistance:0.00}m — 수동 입력 (씬에 EnemyDirector가 없습니다)",
            EditorStyles.miniLabel);

        DrawDuelCurve();
        DrawHeightCurve();
        DrawEngagePad();

        lockRootPosition = EditorGUILayout.Toggle(
            new GUIContent("루트 위치 고정", "루트 모션이 배우를 밀어내면 프레임마다 거리가 변해 배치 확인이 무의미해진다."),
            lockRootPosition);

        EditorGUILayout.Space();
    }

    /// <summary>배치 패드의 높이(px). 무대 반경이 아니라 <b>결투 근방</b>만 보여 준다.</summary>
    private const float PadHeight = 190f;

    /// <summary>패드에서 점을 집을 수 있는 반경(px).</summary>
    private const float PadGrabRadius = 12f;

    /// <summary>
    /// <b>배치 패드</b> — 적들이 설 자리를 평면도에서 끌어 저작한다.
    ///
    /// <para><b>⚠ 씬 뷰 핸들을 쓸 수 없다.</b> 이 창의 프리뷰는 <see cref="PreviewRenderUtility"/>(창 안 오프스크린)이고
    /// 배우 인스턴스는 <c>HideAndDontSave</c>라 <b>씬에 없다</b>. 시퀀스 저작이 <c>Handles.PositionHandle</c>을
    /// 쓰는 것은 러너가 씬 오브젝트이기 때문이라 그 관용구를 여기로 옮길 수 없다(docs/PatternMultiTarget).</para>
    ///
    /// <para>좌표가 <c>Vector2</c>(결투 축 로컬 평면)라 <b>평면도 하나로 저작이 완결된다</b> — 3D 피킹 수학이 없다.</para>
    /// </summary>
    private void DrawEngagePad()
    {
        EditorGUILayout.Space();

        EditorGUILayout.LabelField(
            engageOffsets.Count == 0
                ? "교전 배치 — 비어 있음 (마주보고 한 명)"
                : $"교전 배치 — {EngagedCount}명",
            EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(10f, PadHeight, GUILayout.ExpandWidth(true));
        DrawPad(rect);
        HandlePadInput(rect);

        DrawEngageRows();
    }

    /// <summary>패드 평면도. 플레이어가 중앙 아래, 결투 축(앞)이 위쪽이다.</summary>
    private void DrawPad(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.18f));

        Vector2 origin = PadOrigin(rect);
        float scale = PadScale(rect);

        Handles.BeginGUI();

        // 1m 격자 — 저작한 자리가 리치 안인지 눈으로 보는 기준이다.
        Handles.color = new Color(1f, 1f, 1f, 0.07f);
        for (float m = -8f; m <= 8f; m += 1f)
        {
            float x = origin.x + m * scale;
            if (x > rect.xMin && x < rect.xMax)
                Handles.DrawLine(new Vector3(x, rect.yMin), new Vector3(x, rect.yMax));

            float y = origin.y - m * scale;
            if (y > rect.yMin && y < rect.yMax)
                Handles.DrawLine(new Vector3(rect.xMin, y), new Vector3(rect.xMax, y));
        }

        // 임팩트 간격 원 — 거리 커브가 정한 t=0의 간격이다. 이 원 위에 있으면 마주보기와 같은 거리다.
        Handles.color = new Color(0.4f, 0.7f, 1f, 0.5f);
        DrawPadCircle(origin, DuelDistance * scale);

        Handles.color = new Color(0.4f, 0.8f, 1f, 1f);
        DrawPadCircle(origin, 6f);
        Handles.Label(origin + new Vector2(8f, -4f), "플레이어", EditorStyles.miniLabel);

        // 칼날 선분(지금 t의 실제 칼) — "칼 한 번이 셋에 다 닿는가"를 재는 기준이다.
        if (TryBladeSegment(out Vector3 bladeFrom, out Vector3 bladeTo))
        {
            Handles.color = new Color(1f, 0.95f, 0.5f, 0.9f);
            Handles.DrawLine(PadPoint(origin, scale, bladeFrom), PadPoint(origin, scale, bladeTo));
        }

        float impactGap = DuelDistance;
        bool authored = engageOffsets.Count > 0;

        for (int i = 0; i < EngagedCount; i++)
        {
            Vector2 point = PadPoint(origin, scale, SlotOf(i, impactGap));

            // 닿지 않는 적은 빨갛다. 막지는 않는다 — 스치는 연출이 의도일 수 있다.
            bool reaching = i < mirrorGaps.Count && !float.IsNaN(mirrorGaps[i]) && mirrorGaps[i] < 0f;

            Handles.color = !authored
                ? new Color(0.6f, 0.6f, 0.6f, 0.8f)
                : reaching ? new Color(0.4f, 0.9f, 0.5f, 1f) : new Color(1f, 0.45f, 0.4f, 1f);

            DrawPadCircle(point, 7f);
            Handles.Label(point + new Vector2(9f, -4f), authored ? i.ToString() : "기본", EditorStyles.miniLabel);
        }

        Handles.EndGUI();

        EditorGUI.LabelField(
            new Rect(rect.xMin + 6f, rect.yMin + 4f, rect.width - 12f, 16f),
            "위 = 결투 축(앞) · 격자 1m · 초록 = 칼이 닿음 / 빨강 = 안 닿음",
            EditorStyles.miniLabel);
    }

    /// <summary>패드에서 점을 끌어 오프셋을 고친다. 드래그는 대략, 아래 숫자 필드는 정확.</summary>
    private void HandlePadInput(Rect rect)
    {
        if (engageOffsets.Count == 0) return; // 저작 전에는 읽기 전용(기본 자리만 보여 준다)

        Vector2 origin = PadOrigin(rect);
        float scale = PadScale(rect);
        var e = Event.current;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            padDragIndex = -1;
            float best = PadGrabRadius;

            for (int i = 0; i < engageOffsets.Count; i++)
            {
                float distance = Vector2.Distance(PadPoint(origin, scale, SlotOf(i, DuelDistance)), e.mousePosition);
                if (distance > best) continue;

                best = distance;
                padDragIndex = i;
            }

            if (padDragIndex >= 0) e.Use();
        }
        else if (e.type == EventType.MouseDrag && padDragIndex >= 0)
        {
            Vector2 local = (e.mousePosition - origin) / scale;

            // 화면 y는 아래로 증가하고 결투 축은 위로 간다 — 부호를 뒤집는다.
            engageOffsets[padDragIndex] = new Vector2(local.x, -local.y);
            Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseUp && padDragIndex >= 0)
        {
            padDragIndex = -1;
            e.Use();
        }
    }

    /// <summary>적마다 숫자 필드 한 줄 + 추가·삭제·순서 이동. <b>순서가 곧 적 인덱스다.</b></summary>
    private void DrawEngageRows()
    {
        for (int i = 0; i < engageOffsets.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            string gapText = i < mirrorGaps.Count && !float.IsNaN(mirrorGaps[i])
                ? $"{mirrorGaps[i]:+0.00;-0.00;0.00}m"
                : "—";

            EditorGUILayout.LabelField($"적 {i}", GUILayout.Width(40f));
            engageOffsets[i] = EditorGUILayout.Vector2Field(GUIContent.none, engageOffsets[i]);
            EditorGUILayout.LabelField(gapText, EditorStyles.miniLabel, GUILayout.Width(60f));

            bool moveUp = false, moveDown = false, remove = false;

            using (new EditorGUI.DisabledScope(i == 0))
                moveUp = GUILayout.Button("▲", GUILayout.Width(26f));

            using (new EditorGUI.DisabledScope(i == engageOffsets.Count - 1))
                moveDown = GUILayout.Button("▼", GUILayout.Width(26f));

            remove = GUILayout.Button("삭제", GUILayout.Width(50f));

            EditorGUILayout.EndHorizontal();

            // 리스트를 그리는 도중에 바꾸면 이 프레임의 나머지 행이 어긋난다 — 그려낸 뒤에 적용하고 나간다.
            if (moveUp) { SwapOffsets(i, i - 1); break; }
            if (moveDown) { SwapOffsets(i, i + 1); break; }
            if (remove) { engageOffsets.RemoveAt(i); break; }
        }

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ 적 추가", GUILayout.Width(90f)))
        {
            // 첫 원소는 마주보는 그 자리여야 한다 — 저작을 시작하는 순간 그림이 바뀌면 안 된다.
            engageOffsets.Add(engageOffsets.Count == 0
                ? EngageSlots.DefaultOffset(DuelDistance)
                : engageOffsets[engageOffsets.Count - 1] + new Vector2(1f, 0f));
        }

        using (new EditorGUI.DisabledScope(engageOffsets.Count == 0))
            if (GUILayout.Button("배치 비우기", GUILayout.Width(100f))) engageOffsets.Clear();

        EditorGUILayout.LabelField(
            engageOffsets.Count == 0
                ? "비우면 마주보고 한 명 — 예전 동작이다"
                : "리스트 길이가 곧 교전 인원이다",
            EditorStyles.miniLabel);

        EditorGUILayout.EndHorizontal();
    }

    private void SwapOffsets(int a, int b)
    {
        Vector2 held = engageOffsets[a];
        engageOffsets[a] = engageOffsets[b];
        engageOffsets[b] = held;
    }

    /// <summary>패드에서 플레이어(원점)가 놓이는 자리. 앞쪽(위)에 공간을 더 준다.</summary>
    private static Vector2 PadOrigin(Rect rect) => new Vector2(rect.center.x, rect.yMax - 40f);

    /// <summary>패드의 한 미터당 픽셀. 가장 먼 적과 임팩트 간격이 화면에 남도록 맞춘다.</summary>
    private float PadScale(Rect rect)
    {
        float span = Mathf.Max(DuelDistance, 1f);
        foreach (var offset in engageOffsets)
            span = Mathf.Max(span, Mathf.Abs(offset.x), Mathf.Abs(offset.y));

        span = Mathf.Max(span, 0.5f);

        return Mathf.Min((rect.height - 60f) / span, rect.width * 0.45f / span);
    }

    /// <summary>프리뷰 공간의 자리를 패드 좌표로. 결투 축(+Z)이 위쪽이다.</summary>
    private static Vector2 PadPoint(Vector2 origin, float scale, Vector3 slot) =>
        new Vector2(origin.x + slot.x * scale, origin.y - slot.z * scale);

    private static void DrawPadCircle(Vector2 center, float radius)
    {
        const int Segments = 24;
        Vector3 previous = center + new Vector2(radius, 0f);

        for (int i = 1; i <= Segments; i++)
        {
            float angle = i * Mathf.PI * 2f / Segments;
            Vector3 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            Handles.DrawLine(previous, next);
            previous = next;
        }
    }

    /// <summary>
    /// 지금 <c>t</c>의 칼날 선분(프리뷰 공간). 프리뷰 인스턴스가 없으면 false.
    /// <b>최장 축이 날 길이라는 규칙은 <c>BladePath</c>·<c>MeshSliceBakerWindow.SampleWeapon</c>과 같다.</b>
    /// </summary>
    private bool TryBladeSegment(out Vector3 from, out Vector3 to)
    {
        from = to = Vector3.zero;
        if (player.instance == null) return false;

        Transform weapon = FindDeep(player.instance.transform, WeaponBoneName);
        var bladeRenderer = weapon != null ? weapon.GetComponentInChildren<Renderer>() : null;
        if (bladeRenderer == null) return false;

        Bounds bounds = bladeRenderer.bounds;
        Vector3 extents = bounds.extents;

        Vector3 axis = extents.x >= extents.y && extents.x >= extents.z
            ? Vector3.right
            : extents.y >= extents.z ? Vector3.up : Vector3.forward;

        float half = Vector3.Scale(extents, axis).magnitude;
        from = bounds.center - axis * half;
        to = bounds.center + axis * half;
        return true;
    }

    /// <summary>
    /// 높이 커브 저작 줄. <b>거리 커브와 같은 축·같은 시계</b>라 필드 하나만 얹으면 된다 —
    /// 새 저작 화면이 0개다(docs/PatternMultiTarget).
    /// </summary>
    private void DrawHeightCurve()
    {
        EditorGUI.BeginChangeCheck();
        duelHeightCurve = EditorGUILayout.CurveField(
            new GUIContent("높이 커브",
                "키 시간 = 임팩트 기준 상대초(거리 커브와 같은 축), 값 = 지면 기준 절대 높이(m).\n" +
                "비우면 0 = 지면이라 기존 동작 그대로다. 도약·낙하 참격이 이 커브로 표현된다.\n" +
                "⚠ 적 높이가 아니다 - 움직이는 것은 플레이어뿐이다."),
            duelHeightCurve);
        if (EditorGUI.EndChangeCheck()) Repaint();

        if (!HasHeightCurve)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(" ", "커브 없음 — 지면(0m)", EditorStyles.miniLabel);

            if (GUILayout.Button("+ 커브 만들기", GUILayout.Width(110f)))
            {
                // 지면에서 시작해 지면으로 끝난다 — 만들자마자 그림이 바뀌면 무엇이 바뀐 건지 알 수 없다.
                duelHeightCurve = AnimationCurve.Linear(CurveDriveStart, 0f, 0f, 0f);
            }
            EditorGUILayout.EndHorizontal();
            return;
        }

        EditorGUILayout.LabelField(" ",
            $"높이 t={t:+0.00;-0.00;0.00}s : {HeightAt(t):0.00}m" +
            (Mathf.Abs(duelHeightCurve[duelHeightCurve.length - 1].value) > 0.01f
                ? "   ⚠ 끝 키가 지면이 아니다 — 착지가 안 되면 발이 떠 있다"
                : string.Empty),
            EditorStyles.miniLabel);
    }

    /// <summary>
    /// 결투 거리 커브 저작 줄. <b>지점 찍기·끌기·탄젠트는 Unity 커브 에디터가 전부 처리한다</b> —
    /// 여기서는 필드 하나와 현재 <c>t</c>의 간격만 보여 준다.
    /// </summary>
    private void DrawDuelCurve()
    {
        EditorGUI.BeginChangeCheck();
        duelDistanceCurve = EditorGUILayout.CurveField(
            new GUIContent("결투 거리 커브",
                "키 시간 = 임팩트 기준 상대초(0 = 임팩트), 값 = 절대 간격(m).\n" +
                "음수면 플레이어가 적을 지나쳐 뒤로 간다. 비우면 위의 상수 거리를 쓴다.\n" +
                "구동 구간은 플레이어 클립 재생 구간이며, 그 밖은 끝 키 값으로 홀드된다."),
            duelDistanceCurve);
        if (EditorGUI.EndChangeCheck()) Repaint();

        if (!HasCurve)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(" ", $"커브 없음 — 상수 {DuelDistance:0.00}m", EditorStyles.miniLabel);

            if (GUILayout.Button("+ 커브 만들기", GUILayout.Width(110f)))
            {
                float gap = Mathf.Max(duelBaseDistance + duelDistanceOffset, 0.1f);

                // 지금 상수와 같은 직선으로 시작한다 — 만들자마자 그림이 바뀌면 무엇이 바뀐 건지 알 수 없다.
                duelDistanceCurve = AnimationCurve.Linear(CurveDriveStart, gap, 0f, gap);
            }
            EditorGUILayout.EndHorizontal();
            return;
        }

        EditorGUILayout.LabelField(" ",
            $"간격 t={t:+0.00;-0.00;0.00}s : {GapAt(t):0.00}m" +
            (GapAt(t) < 0f ? "  (지나침)" : string.Empty) +
            $"   ·   구동 시작 t={CurveDriveStart:0.00}s (플레이어 클립 시작)",
            EditorStyles.miniLabel);

        WarnCurveOutsideDriveWindow();
    }

    /// <summary>
    /// 커브 구동이 시작되는 시각(임팩트 기준 상대초) = <b>플레이어 클립이 재생되기 시작하는 t</b>.
    /// 그보다 이른 키는 화면에 나타날 수 없다 — 그 구간에는 아직 수렴 이동이 진행 중이다.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>선택된 원소가 아니라 시퀀스 전체의 시작</b>이다. 리드인이 있으면 재생은 그만큼 먼저 시작하므로
    /// 앵커 클립만 보고 재면 구동 구간을 실제보다 짧게 잡아 멀쩡한 키에 경고가 뜬다.
    /// </remarks>
    private float CurveDriveStart
    {
        get
        {
            if (targetPattern == null) return player.HasClip
                ? -DuetTimeline.LeadOf(player.clipStart, player.clipImpact, player.speed)
                : -0.5f;

            float authored = ClipSequence.AuthoredSpan(targetPattern.PlayerLeadInClips, AnchorAlignment);
            return authored > 0f ? -authored : -0.5f;
        }
    }

    /// <summary>
    /// 키가 구동 구간 밖에 있으면 알린다. <b>이 창에서 가장 흔한 실수가 여기다</b> —
    /// 빈 커브 필드를 직접 누르면 Unity가 <c>0 → 1</c> 기본 램프를 주는데,
    /// 이 축은 <b>음수가 과거</b>라 그 키들은 전부 임팩트 <b>이후</b>다.
    /// 결과는 "시작 거리만 멀어지고 아무 일도 안 일어남"(첫 키 값이 계속 홀드된다).
    /// </summary>
    private void WarnCurveOutsideDriveWindow()
    {
        float start = CurveDriveStart;
        bool allAfterImpact = true;
        bool anyBeforeStart = false;

        for (int i = 0; i < duelDistanceCurve.length; i++)
        {
            float key = duelDistanceCurve[i].time;
            if (key < -1e-3f) allAfterImpact = false;
            if (key < start - 1e-3f) anyBeforeStart = true;
        }

        if (allAfterImpact)
            EditorGUILayout.HelpBox(
                $"키가 전부 임팩트 이후(t ≥ 0)입니다 — 클립이 재생되는 구간은 t = {start:0.00}s ~ 0s입니다.\n" +
                "그 구간에는 키가 없어 첫 키 값이 그대로 홀드되므로 화면에서는 '시작 거리만 멀어지고 안 움직임'이 됩니다.\n" +
                "지나가며 베려면 임팩트 앞 구간에 키를 찍고, t = 0의 값이 칼이 닿는 간격이 되게 하세요.",
                MessageType.Warning);
        else if (anyBeforeStart)
            EditorGUILayout.HelpBox(
                $"클립 시작(t = {start:0.00}s)보다 이른 키가 있습니다 — 그 구간은 아직 수렴 이동 중이라 커브가 안 보입니다.",
                MessageType.Info);
    }

    /// <summary>
    /// 씬에 <c>EnemyDirector</c>가 있으면 기준 거리를 거기서 읽는다 — <b>런타임과 같은 함수</b>
    /// (<c>EnemyDirector.DuelBaseDistance</c>)를 부르므로 손으로 맞출 값이 하나 사라진다.
    /// 씬이 없어도 툴은 그대로 돌아간다(수동 입력 유지).
    /// </summary>
    private void PullDuelBaseFromScene()
    {
        var director = Object.FindObjectOfType<EnemySpace.EnemyDirector>();
        if (director == null) { duelBaseFromScene = false; return; }

        duelBaseDistance = director.DuelBaseDistance();
        duelBaseFromScene = true;
    }

    /// <summary>패턴의 역할이 짝을 정한다. 두 슬롯을 <b>동시에</b> 읽는다.</summary>
    private void LoadFromPattern()
    {
        if (targetPattern == null) return;

        bool enemyIsAttacker = targetPattern.Attacker == EnemySpace.Attacker.Enemy;

        // 다른 패턴으로 갈아타면 리스트 길이가 달라진다 — 범위 밖이면 앵커로 되돌린다.
        int slotCount = playerSlotKind switch
        {
            PlayerSlotKind.LeadIn => LeadInCount,
            PlayerSlotKind.MashHit => IsMashTarget ? MashClipCount : 0,
            _ => 0
        };
        if (playerSlotKind != PlayerSlotKind.Anchor && (playerSlotIndex < 0 || playerSlotIndex >= slotCount))
        {
            playerSlotKind = PlayerSlotKind.Anchor;
            playerSlotIndex = 0;
        }

        string anchorPath = enemyIsAttacker ? "playerParry" : "playerAttack";

        switch (playerSlotKind)
        {
            case PlayerSlotKind.LeadIn:
                // ⚠ 리드인만 isLeadIn이다 — ImpactTime을 저장하지 않고 트림 끝이 t 축 앵커다.
                player.isLeadIn = true;
                player.slotPath = $"playerLeadInClips.Array.data[{playerSlotIndex}]";
                player.label = $"플레이어 — 리드인 [{playerSlotIndex}] (앵커 앞 {LeadInCount - playerSlotIndex}번째)";
                player.tOffset = ClipSequence.AuthoredEndOffset(targetPattern.PlayerLeadInClips, playerSlotIndex, AnchorAlignment);
                break;

            case PlayerSlotKind.MashHit:
                // ⚠ 연타 타격은 정렬 대상이 아니지만 ImpactTime은 '재생 시작점'으로 살아 있다.
                //    그래서 일반 슬롯과 똑같이 다룬다(저장도 하고 t=0 앵커로도 쓴다) — isLeadIn이 아니다.
                player.isLeadIn = false;
                player.slotPath = $"mashStrikes.Array.data[{playerSlotIndex}].playerClip";
                player.label = $"플레이어 — 연타 타격 [{playerSlotIndex}] / {MashClipCount}개";
                player.tOffset = 0f;
                break;

            default:
                player.isLeadIn = false;
                player.slotPath = anchorPath;
                player.label = targetPattern.IsMash
                    ? $"플레이어 — {anchorPath} (마무리 일격)"
                    : $"플레이어 — {anchorPath} (정렬 앵커)";
                player.tOffset = 0f;
                break;
        }

        enemy.slotPath = enemyIsAttacker ? "enemyAttack" : "enemyDeath";
        enemy.label = enemyIsAttacker ? "적 — enemyAttack" : "적 — enemyDeath (사망)";

        if (playerSlotKind == PlayerSlotKind.MashHit)
        {
            // 연타 타격을 고르면 <b>그 쌍의 적 리액션</b>이 자동으로 같이 열린다 —
            // 둘은 짝이고, 짝을 나란히 보는 것이 이 슬롯 저작의 전부다(보조 슬롯 팝업을 덮는다).
            feint.slotPath = $"mashStrikes.Array.data[{playerSlotIndex}].enemyReaction";
            feint.label = $"적 — 연타 리액션 [{playerSlotIndex}] (이 타격의 짝)";
        }
        else
        {
            // 견제는 Attacker.Player 전용이다 — 적이 공격자면 그 구간을 enemyAttack이 채운다.
            // 피격·패링은 역할과 무관하게 열어 둔다(적이 공격자여도 막힐 수는 있다).
            // 견제·상호 공격은 Attacker.Player 전용이다 — 적이 공격자면 그 구간을 위의 enemy 배우가
            // 이미 enemyAttack으로 열고 있어, 여기서 또 열면 같은 슬롯이 화면에 둘 뜬다.
            bool auxIsEngage = enemyAuxSlot == EnemyAuxSlot.Feint || enemyAuxSlot == EnemyAuxSlot.Attack;
            feint.slotPath = enemyIsAttacker && auxIsEngage ? null : AuxSlotPath(enemyAuxSlot);
            feint.label = AuxSlotLabel(enemyAuxSlot);
        }

        var so = new SerializedObject(targetPattern);
        LoadActor(so, player);
        LoadActor(so, enemy);
        LoadActor(so, feint);

        duelDistanceOffset = so.FindProperty("duelDistanceOffset")?.floatValue ?? 0f;
        duelDistanceCurve = so.FindProperty("duelDistanceCurve")?.animationCurveValue ?? new AnimationCurve();
        duelHeightCurve = so.FindProperty("duelHeightCurve")?.animationCurveValue ?? new AnimationCurve();

        engageOffsets.Clear();
        var offsetsProp = so.FindProperty("engageOffsets");
        if (offsetsProp != null)
        {
            for (int i = 0; i < offsetsProp.arraySize; i++)
                engageOffsets.Add(offsetsProp.GetArrayElementAtIndex(i).vector2Value);
        }
        PullDuelBaseFromScene();
        t = 0f;
    }

    private static void LoadActor(SerializedObject so, Actor actor)
    {
        if (actor.slotPath == null)
        {
            actor.clip = null;
            actor.clipStart = actor.clipImpact = actor.clipEnd = 0f;
            return;
        }

        actor.clip = so.FindProperty($"{actor.slotPath}.clip")?.objectReferenceValue as AnimationClip;
        actor.clipStart = so.FindProperty($"{actor.slotPath}.startOffset")?.floatValue ?? 0f;
        actor.speed = Mathf.Max(so.FindProperty($"{actor.slotPath}.speed")?.floatValue ?? 1f, 0.01f);

        // duration 0 이하 = "클립 끝까지"(ClipAlignment.ResolvedDuration과 같은 규약).
        float duration = so.FindProperty($"{actor.slotPath}.duration")?.floatValue ?? 0f;
        actor.clipEnd = duration > 0f
            ? actor.clipStart + duration
            : (actor.clip != null ? actor.clip.length : actor.clipStart);

        // 미오서링(0 이하)이면 런타임 폴백과 같게 트림 끝에 세운다.
        // ⚠ 리드인 원소는 언제나 트림 끝이 앵커다 — ImpactTime은 저장하지 않는다(§ClipSequence).
        float impact = actor.isLeadIn ? 0f : (so.FindProperty($"{actor.slotPath}.impactTime")?.floatValue ?? 0f);
        actor.clipImpact = impact > 0f ? impact : actor.clipEnd;

        actor.extraImpacts.Clear();
        var extras = so.FindProperty($"{actor.slotPath}.extraImpactTimes");
        if (extras != null && extras.isArray)
        {
            for (int i = 0; i < extras.arraySize; i++)
                actor.extraImpacts.Add(extras.GetArrayElementAtIndex(i).floatValue);
        }
    }

    // ─────────────────────────── 프리뷰 ───────────────────────────

    private void DrawPreview()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));

        if (player.prefab == null && enemy.prefab == null)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            EditorGUI.LabelField(rect, "프리팹을 지정하면 두 배우가 표시됩니다.", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        HandlePreviewInput(rect);
        if (Event.current.type != EventType.Repaint) return;

        previewUtil ??= new PreviewRenderUtility();

        PlaceActors();
        EnsureInstance(player);
        EnsureInstance(enemy);
        EnsureMirrors();
        PoseActor(player, player);
        PoseActor(enemy, ActiveEnemyActor()); // 리그는 적 하나, 모션만 시간으로 갈린다
        PoseMirrors();
        MeasureBladeGaps();

        previewUtil.BeginPreview(rect, GUIStyle.none);
        SetupCameraAndLights();
        previewUtil.Render(true);
        GUI.DrawTexture(rect, previewUtil.EndPreview(), ScaleMode.StretchToFill, false);
    }

    /// <summary>
    /// 지금 <c>t</c>에서 적을 포즈시킬 배우. <b>런타임의 한 줄 진행을 그대로 옮긴 것이다</b> —
    /// 처치 확정(<see cref="HandoffLead"/>)까지는 견제가, 그 뒤로는 사망이 애니메이터를 점유한다.
    ///
    /// <para>같은 프리팹 인스턴스를 시간으로 나눠 쓰므로 <b>이음매가 화면에 그대로 드러난다</b> —
    /// 견제 끝 포즈와 사망 시작 포즈가 튀면 여기서 보인다. 배우를 둘로 세우면 그 판단을 할 수 없다.</para>
    ///
    /// <para>한쪽 클립이 없으면 나머지 하나가 전 구간을 맡는다.</para>
    /// </summary>
    private Actor ActiveEnemyActor()
    {
        if (!feint.HasClip) return enemy;
        if (!enemy.HasClip) return feint;

        return t < -HandoffLead ? feint : enemy;
    }

    /// <summary>이 배우가 지금 화면의 적을 맡고 있는가(배우 블록 강조용).</summary>
    private bool IsActiveEnemy(Actor actor) => ReferenceEquals(ActiveEnemyActor(), actor);

    /// <summary>
    /// 적은 임팩트 시점 자리에 <b>고정</b>하고 <b>플레이어가 움직인다</b> — 런타임과 같은 모델이다
    /// (적 고정 · 플레이어가 축 위에서 간격을 그림). <c>t = 0</c>에서 플레이어가 원점이라 기존 구도가 그대로다.
    ///
    /// <para>간격이 음수인 구간에서는 플레이어가 적을 지나쳐 뒤로 나간다 — 화면이 런타임과 같아지고
    /// 칼날 경로도 실제 궤적이 된다. 회전은 건드리지 않는다(지나쳐도 등을 돌리지 않는 규칙과 같다).</para>
    /// </summary>
    private void PlaceActors()
    {
        float impactGap = DuelDistance;

        // 원점 = 임팩트 시점의 플레이어 위치. EngageSlots의 기준점 정의가 그것이라 프리뷰 좌표가 그대로 맞는다.
        player.placement = Vector3.forward * (impactGap - GapAt(t)) + Vector3.up * HeightAt(t);
        player.facing = Quaternion.identity;

        // 적 0번은 저작 상태를 가진 배우가 맡고, 나머지는 프리뷰 전용 몸(미러)이 맡는다.
        enemy.placement = SlotOf(0, impactGap);
        enemy.facing = FacingAt(enemy.placement);
    }

    /// <summary>
    /// 적 <paramref name="index"/>의 프리뷰 자리. <b>런타임과 같은 함수를 부른다</b>
    /// (<see cref="EngageSlots.Resolve"/>) — 식이 두 곳에 적히면 프리뷰와 게임이 조용히 갈라진다.
    /// </summary>
    private Vector3 SlotOf(int index, float fallbackGap) =>
        EngageSlots.Resolve(engageOffsets, index, Vector3.zero, Vector3.forward, fallbackGap);

    /// <summary>그 자리에서 플레이어(원점)를 바라보는 회전. 배치가 부채꼴이면 적마다 각도가 다르다.</summary>
    private static Quaternion FacingAt(Vector3 slot)
    {
        Vector3 toPlayer = -slot;
        toPlayer.y = 0f;

        return toPlayer.sqrMagnitude < 1e-6f
            ? Quaternion.Euler(0f, 180f, 0f)
            : Quaternion.LookRotation(toPlayer);
    }

    private void EnsureInstance(Actor actor)
    {
        if (actor.prefab == null) { DestroyInstance(actor); return; }
        if (actor.instance != null && actor.cachedPrefab == actor.prefab) return;

        DestroyInstance(actor);

        actor.instance = Instantiate(actor.prefab);
        actor.instance.hideFlags = HideFlags.HideAndDontSave;
        previewUtil.AddSingleGO(actor.instance);
        actor.cachedPrefab = actor.prefab;
    }

    /// <summary>
    /// 적 2..N의 프리뷰 몸을 인원에 맞춘다. <b>저작 상태를 늘리지 않는다</b> —
    /// 클립은 전원이 공유하므로(A안) 필요한 것은 몸뿐이다.
    ///
    /// <para>⚠ 한때 이 창은 <b>배우를 둘로 그리지 않겠다</b>고 적어 두었다. 그 근거는 "적 한 명의 다른 시각 구간을
    /// 시간으로 나눠 쓴다"였고 <b>인원이 1일 때는 여전히 참이다</b> — 미러는 그 한 줄 진행을 <b>그대로 복제</b>할 뿐
    /// 새 구간을 만들지 않는다. 여럿이 서 있는 것이 이제 맞는 그림이다(docs/PatternMultiTarget).</para>
    /// </summary>
    private void EnsureMirrors()
    {
        int want = enemy.prefab == null ? 0 : EngagedCount - 1;

        while (enemyMirrors.Count > want)
        {
            int last = enemyMirrors.Count - 1;
            if (enemyMirrors[last] != null) DestroyImmediate(enemyMirrors[last]);
            enemyMirrors.RemoveAt(last);
        }

        // 프리팹이 바뀌면 통째로 다시 만든다 — 미러는 상태가 없어 비교할 것이 프리팹뿐이다.
        for (int i = 0; i < enemyMirrors.Count; i++)
        {
            if (enemyMirrors[i] != null) continue;

            enemyMirrors[i] = Instantiate(enemy.prefab);
            enemyMirrors[i].hideFlags = HideFlags.HideAndDontSave;
            previewUtil.AddSingleGO(enemyMirrors[i]);
        }

        while (enemyMirrors.Count < want)
        {
            var go = Instantiate(enemy.prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            previewUtil.AddSingleGO(go);
            enemyMirrors.Add(go);
        }
    }

    /// <summary>미러를 각자 자리에 세운다. 포즈는 <see cref="ActiveEnemyActor"/>의 것을 그대로 쓴다.</summary>
    private void PoseMirrors()
    {
        var motion = ActiveEnemyActor();
        float impactGap = DuelDistance;

        for (int i = 0; i < enemyMirrors.Count; i++)
        {
            var go = enemyMirrors[i];
            if (go == null) continue;

            Vector3 slot = SlotOf(i + 1, impactGap);
            PoseBody(go, motion, slot, FacingAt(slot));
        }
    }

    private void DestroyMirrors()
    {
        foreach (var go in enemyMirrors)
            if (go != null) DestroyImmediate(go);

        enemyMirrors.Clear();
    }

    private static void DestroyInstance(Actor actor)
    {
        if (actor.instance != null) DestroyImmediate(actor.instance);
        actor.instance = null;
        actor.cachedPrefab = null;
    }

    /// <summary>
    /// 배우를 지금 <c>t</c>의 포즈로 세운다.
    ///
    /// <para><b>배치를 샘플링 전에 하면 안 된다.</b> <see cref="AnimationClip.SampleAnimation"/>은
    /// 루트 트랜스폼을 클립 값으로 <b>덮어쓴다</b> — 배치를 먼저 하면 회전이 날아가
    /// 두 배우가 모두 클립이 정한 방향을 보게 되고, 적이 등을 돌린 것처럼 보인다.</para>
    ///
    /// <para>그래서 <b>중립(원점·무회전)에서 샘플링한 뒤 배치를 그 위에 합성</b>한다.
    /// 클립이 루트 커브를 갖든 안 갖든 같은 식으로 동작한다.</para>
    /// </summary>
    /// <param name="rig">인스턴스와 배치를 가진 배우(화면에 서 있는 몸).</param>
    /// <param name="motion">지금 이 몸이 재생할 클립을 가진 배우. 적은 시간에 따라 견제/사망으로 갈린다.</param>
    private void PoseActor(Actor rig, Actor motion)
    {
        if (rig.instance == null) return;

        PoseBody(rig.instance, motion, rig.placement, rig.facing);
    }

    /// <summary>
    /// 몸 하나를 지금 <c>t</c>의 포즈로 세운다. <b>배우와 미러가 같은 경로를 쓴다</b> —
    /// 포즈 규칙(중립 샘플링 → 배치 합성)이 두 곳에 적히면 미러만 어긋난다.
    /// </summary>
    private void PoseBody(GameObject body, Actor motion, Vector3 placement, Quaternion facing)
    {
        var tr = body.transform;

        // 중립에서 샘플링 — 이래야 나온 값이 순수한 루트 모션이 된다.
        tr.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        if (motion.HasClip) motion.clip.SampleAnimation(body, motion.SampleTime(t));

        Quaternion rootRotation = tr.localRotation;
        Vector3 rootOffset = tr.localPosition;

        // 루트 모션이 배우를 밀어내면 두 배우의 거리가 프레임마다 변해 배치 확인이 무의미해진다.
        // 위치만 잠그고 회전은 살린다 — 방향 전환은 봐야 한다.
        Vector3 position = placement + (lockRootPosition ? Vector3.zero : facing * rootOffset);
        tr.SetPositionAndRotation(position, facing * rootRotation);
    }

    private void HandlePreviewInput(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDrag && e.button == 0)
        {
            yaw += e.delta.x;
            pitch = Mathf.Clamp(pitch - e.delta.y, -89f, 89f);
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.ScrollWheel)
        {
            zoom = Mathf.Clamp(zoom + e.delta.y * 0.05f, 0.2f, 5f);
            e.Use();
            Repaint();
        }
    }

    private void SetupCameraAndLights()
    {
        // 두 배우를 합친 bounds로 잡는다 — 한쪽만 보면 상대가 화면 밖으로 잘린다.
        Bounds b = CombinedBounds();

        var cam = previewUtil.camera;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 1000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

        float radius = Mathf.Max(b.extents.magnitude, 0.1f);
        float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * zoom;

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 camPos = b.center + rot * new Vector3(0f, 0f, -dist);
        cam.transform.position = camPos;
        cam.transform.rotation = Quaternion.LookRotation(b.center - camPos);

        previewUtil.ambientColor = new Color(0.4f, 0.4f, 0.4f);
        if (previewUtil.lights.Length > 0)
        {
            previewUtil.lights[0].intensity = 1.1f;
            previewUtil.lights[0].transform.rotation = Quaternion.Euler(35f, 40f, 0f);
        }
        if (previewUtil.lights.Length > 1)
        {
            previewUtil.lights[1].intensity = 0.6f;
            previewUtil.lights[1].transform.rotation = Quaternion.Euler(-20f, -140f, 0f);
        }
    }

    private Bounds CombinedBounds()
    {
        bool any = false;
        Bounds result = new Bounds(Vector3.forward * (DuelDistance * 0.5f), Vector3.one);

        foreach (var actor in new[] { player, enemy })
        {
            if (actor.instance == null) continue;

            foreach (var r in actor.instance.GetComponentsInChildren<Renderer>())
            {
                if (!any) { result = r.bounds; any = true; }
                else result.Encapsulate(r.bounds);
            }
        }

        return result;
    }

    // ─────────────────────────── 판단 보조 ───────────────────────────

    /// <summary>
    /// 칼날과 적 사이의 최단거리. <b>t=0에서 음수(관통)여야 벤 것이다</b> —
    /// 각도를 눈으로 보는 것보다 이 숫자 하나가 확실하다.
    /// </summary>
    /// <summary>
    /// 칼–적 간격을 <b>적마다</b> 잰다. <see cref="bladeGap"/>에는 <b>가장 먼 값</b>이 들어간다 —
    /// "칼 한 번이 셋에 다 닿는가"의 답은 <b>가장 안 닿는 적</b>이 정하기 때문이다.
    /// </summary>
    private void MeasureBladeGaps()
    {
        mirrorGaps.Clear();
        bladeGap = float.NaN;

        if (player.instance == null || enemy.instance == null) return;

        Transform weapon = FindDeep(player.instance.transform, WeaponBoneName);
        var bladeRenderer = weapon != null ? weapon.GetComponentInChildren<Renderer>() : null;
        if (bladeRenderer == null) return;

        float worst = float.NegativeInfinity;

        AddGap(bladeRenderer, enemy.instance, ref worst);
        foreach (var mirror in enemyMirrors) AddGap(bladeRenderer, mirror, ref worst);

        if (mirrorGaps.Count > 0) bladeGap = worst;
    }

    private void AddGap(Renderer bladeRenderer, GameObject body, ref float worst)
    {
        if (body == null) { mirrorGaps.Add(float.NaN); return; }

        var renderers = body.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { mirrorGaps.Add(float.NaN); return; }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        float gap = BoundsGap(bladeRenderer.bounds, bounds);
        mirrorGaps.Add(gap);
        worst = Mathf.Max(worst, gap);
    }

    /// <summary>AABB 두 개의 간격. 겹치면 음수(가장 얕은 축의 침투 깊이).</summary>
    private static float BoundsGap(Bounds a, Bounds b)
    {
        Vector3 gap = new Vector3(
            Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x),
            Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y),
            Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));

        // 세 축이 전부 음수면 겹친 것 — 가장 0에 가까운 값이 침투 깊이다.
        if (gap.x < 0f && gap.y < 0f && gap.z < 0f)
            return Mathf.Max(gap.x, Mathf.Max(gap.y, gap.z));

        Vector3 positive = new Vector3(Mathf.Max(gap.x, 0f), Mathf.Max(gap.y, 0f), Mathf.Max(gap.z, 0f));
        return positive.magnitude;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }

    private void DrawReadouts()
    {
        bool atImpact = Mathf.Abs(t) < 1e-3f;

        string gapText = float.IsNaN(bladeGap)
            ? "칼–적 거리 —  (칼 본 또는 적 프리팹 없음)"
            : bladeGap < 0f
                ? $"칼–적 거리  {bladeGap:0.000}m  ▶ 관통 (베고 있음)"
                : $"칼–적 거리  {bladeGap:0.000}m  ▶ 떨어짐 (안 닿음)";

        var style = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = atImpact
            ? (bladeGap < 0f ? new Color(0.3f, 0.85f, 0.4f) : new Color(0.95f, 0.6f, 0.3f))
            : new Color(0.5f, 0.5f, 0.5f);

        // 추가 칼질 위에 서 있으면 임팩트와 구분되는 배지를 낸다 — 둘 다 '칼이 지나가는 순간'이지만
        // 절단이 붙는 것은 IMPACT 하나뿐이라 눈으로 갈려야 한다.
        int extraHit = ExtraMarkAt(player, t);
        if (!atImpact && extraHit >= 0)
        {
            GUI.backgroundColor = new Color(0.45f, 0.65f, 0.95f);
            GUILayout.Box($"◆ {extraHit + 1}타 (히트스톱)   {gapText}", style, GUILayout.Height(22f));
        }
        else
        {
            GUILayout.Box(atImpact ? $"✦ IMPACT   {gapText}" : gapText, style, GUILayout.Height(22f));
        }
        GUI.backgroundColor = prev;
    }

    // ─────────────────────────── 시간축 ───────────────────────────

    /// <summary>
    /// 세 클립을 모두 담는 스크럽 범위. 견제는 임팩트 앞쪽으로만 뻗으므로 보통 <c>min</c>을 늘린다 —
    /// 범위에서 빠지면 <b>견제 구간을 스크럽할 수가 없다</b>.
    /// </summary>
    private (float min, float max) ScrubRange()
    {
        var (min, max) = DuetTimeline.Range(
            player.clipStart, player.clipImpact, player.clipEnd, player.speed,
            enemy.clipStart, enemy.clipImpact, enemy.clipEnd, enemy.speed);

        // 리드인 원소는 임팩트보다 한참 앞에 산다 — 범위에서 빠지면 그 구간을 스크럽할 수가 없다.
        if (player.HasClip && !Mathf.Approximately(player.tOffset, 0f))
        {
            min = Mathf.Min(min, player.tOffset - DuetTimeline.LeadOf(player.clipStart, player.clipImpact, player.speed));
            max = Mathf.Max(max, player.tOffset);
        }

        // 앵커를 보고 있어도 시퀀스 전체를 훑을 수 있어야 한다(원소들이 이어지는지가 저작의 판단점이다).
        min = Mathf.Min(min, ClipSequence.AuthoredEndOffset(
            targetPattern != null ? targetPattern.PlayerLeadInClips : null, -1, AnchorAlignment));

        if (feint.HasClip)
        {
            min = Mathf.Min(min, -DuetTimeline.LeadOf(feint.clipStart, feint.clipImpact, feint.speed));
            max = Mathf.Max(max, DuetTimeline.TailOf(feint.clipImpact, feint.clipEnd, feint.speed));
        }

        // 거리 커브는 트림 끝보다 뒤까지 뻗을 수 있다(지나쳐 나가는 구간은 임팩트 '이후'다).
        // 범위에 안 넣으면 정작 그 연출을 스크럽할 수 없다 — 클립은 트림이 끝나도 계속 재생되므로
        // 런타임에는 그 구간이 실제로 존재한다.
        if (HasCurve)
        {
            min = Mathf.Min(min, duelDistanceCurve[0].time);
            max = Mathf.Max(max, duelDistanceCurve[duelDistanceCurve.length - 1].time);
        }

        return (min, max);
    }

    private void DrawTimeline()
    {
        var (min, max) = ScrubRange();
        float frameRate = player.HasClip && player.clip.frameRate > 0f ? player.clip.frameRate : 30f;

        using (new EditorGUILayout.HorizontalScope())
        {
            isPlaying = GUILayout.Toggle(isPlaying, isPlaying ? "❚❚ Pause" : "▶ Play", "Button", GUILayout.Width(80f));

            if (GUILayout.Button("◀ Frame", GUILayout.Width(70f))) { isPlaying = false; t = Mathf.Max(t - 1f / frameRate, min); }
            if (GUILayout.Button("Frame ▶", GUILayout.Width(70f))) { isPlaying = false; t = Mathf.Min(t + 1f / frameRate, max); }
            if (GUILayout.Button("t = 0", GUILayout.Width(50f))) { isPlaying = false; t = 0f; }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"t = {t:+0.000;-0.000;0.000}s  (impact 기준, {frameRate:0}fps)",
                EditorStyles.boldLabel, GUILayout.Width(260f));
        }

        Rect sliderRect = GUILayoutUtility.GetRect(10f, 22f, GUILayout.ExpandWidth(true));
        DrawTimelineMarkers(sliderRect, min, max);

        float newT = GUI.HorizontalSlider(sliderRect, t, min, max);
        if (!Mathf.Approximately(newT, t))
        {
            t = newT;
            isPlaying = false;
            Repaint();
        }
    }

    private void DrawTimelineMarkers(Rect rect, float min, float max)
    {
        if (max - min <= 0f) return;

        DrawActorMarkers(rect, min, max, player, new Color(0.3f, 0.85f, 0.4f), new Color(0.95f, 0.4f, 0.4f));
        DrawActorMarkers(rect, min, max, enemy, new Color(0.4f, 0.7f, 0.95f), new Color(0.9f, 0.5f, 0.9f));
        DrawActorMarkers(rect, min, max, feint, new Color(0.85f, 0.8f, 0.35f), new Color(0.85f, 0.65f, 0.2f));

        // 처치 확정 — 견제가 사망으로 넘어가는 지점. 여기가 이어지는지가 저작의 판단점이다.
        if (feint.HasClip && enemy.HasClip)
            DrawMarker(rect, Mathf.InverseLerp(min, max, -HandoffLead), new Color(0.95f, 0.95f, 0.95f), 2f);

        // t=0 — 두 임팩트가 만나는 지점. 가장 굵게.
        DrawMarker(rect, Mathf.InverseLerp(min, max, 0f), new Color(1f, 0.9f, 0.2f), 3f);
    }

    private static void DrawActorMarkers(Rect rect, float min, float max, Actor actor, Color startColor, Color endColor)
    {
        if (!actor.HasClip) return;

        float startT = DuetTimeline.RelativeOf(actor.clipImpact, actor.speed, actor.clipStart) + actor.tOffset;
        float endT = DuetTimeline.RelativeOf(actor.clipImpact, actor.speed, actor.clipEnd) + actor.tOffset;

        DrawMarker(rect, Mathf.InverseLerp(min, max, startT), startColor, 2f);
        DrawMarker(rect, Mathf.InverseLerp(min, max, endT), endColor, 2f);
    }

    private static void DrawMarker(Rect rect, float t01, Color color, float width)
    {
        if (t01 < 0f || t01 > 1f) return;
        float x = Mathf.Lerp(rect.x + 4f, rect.xMax - 4f, t01);
        EditorGUI.DrawRect(new Rect(x - width * 0.5f, rect.y, width, rect.height), color);
    }

    // ─────────────────────────── 배우 블록 ───────────────────────────

    private void DrawActorBlock(Actor actor)
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // 적 배우가 둘이라 지금 화면에 보이는 쪽을 표시한다 — 안 그러면 어느 클립을 만지는지 헷갈린다.
        bool isEnemySlot = ReferenceEquals(actor, enemy) || ReferenceEquals(actor, feint);
        string prefix = isEnemySlot && feint.HasClip && enemy.HasClip && IsActiveEnemy(actor) ? "▶ " : "";
        EditorGUILayout.LabelField(prefix + actor.label, EditorStyles.boldLabel);

        actor.clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", actor.clip, typeof(AnimationClip), false);

        if (!actor.HasClip)
        {
            EditorGUILayout.HelpBox("클립이 비어 있습니다. 이 배우는 바인드 포즈로 서 있습니다.", MessageType.None);
            EditorGUILayout.EndVertical();
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Mark Start")) actor.clipStart = CurrentClipTime(actor);
            if (!actor.isLeadIn && GUILayout.Button("Mark Impact")) MarkImpact(actor);
            if (GUILayout.Button("Mark End")) actor.clipEnd = CurrentClipTime(actor);
        }

        // 입력 필드는 클립 시간이다 — 에셋에 들어갈 숫자를 그대로 보여 인스펙터와 눈으로 대조할 수 있게.
        actor.clipStart = Mathf.Max(EditorGUILayout.FloatField("Start (clip s)", actor.clipStart), 0f);

        // ⚠ 리드인에는 Impact 필드가 없다. 정렬 앵커는 마지막 클립 하나뿐이라(§ClipSequence)
        // 여기 값을 받으면 "여기서 칼이 닿는다"는 오해만 만든다 — 트림 끝이 곧 다음 원소의 시작이다.
        if (!actor.isLeadIn)
            actor.clipImpact = Mathf.Max(EditorGUILayout.FloatField("Impact (clip s)", actor.clipImpact), 0f);

        actor.clipEnd = EditorGUILayout.FloatField("End (clip s)", actor.clipEnd);

        if (actor.isLeadIn)
        {
            actor.clipImpact = actor.clipEnd; // t 축 앵커. 저장되지 않는다.
            EditorGUILayout.LabelField("타임라인 위치",
                $"트림 끝이 t = {actor.tOffset:+0.000;-0.000;0.000}s (다음 원소가 시작하는 지점)");
        }

        EditorGUILayout.LabelField("Duration", $"{actor.Duration:0.000}s   ·   speed {actor.speed:0.00}");

        if (actor.Duration <= 0f)
            EditorGUILayout.HelpBox("End가 Start보다 뒤여야 합니다 (Duration > 0).", MessageType.Warning);
        if (actor.clipImpact < actor.clipStart || actor.clipImpact > actor.clipEnd)
            EditorGUILayout.HelpBox("Impact가 Start~End 밖입니다. 저장 시 구간 안으로 클램프됩니다.", MessageType.Warning);

        DrawExtraImpacts(actor);

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 마지막 베기 <b>이전</b>의 칼질 마크들. 여러 번 베는 클립에서 칼질마다 히트스톱을 걸기 위한 저작이다.
    ///
    /// <para><b>예산 표시가 이 UI의 핵심이다</b> — 정지 N회 × 정지시간 = F만큼 재생을 일찍 시작해야 하고,
    /// 그 여유는 앞 패턴과의 간격에서 나온다. 실측(엔트리 간 최소 0.4초 · 창 p50 1.30초) 기준으로
    /// <b>F가 0.3초를 넘으면 대부분의 채보에서 창을 넘긴다</b> — 저작 단계에서 잡는 게 런타임 경고보다 싸다.</para>
    /// </summary>
    private void DrawExtraImpacts(Actor actor)
    {
        EditorGUILayout.Space(2f);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"추가 칼질 ({actor.extraImpacts.Count})", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("+ Mark Extra", GUILayout.Width(110f)))
            {
                actor.extraImpacts.Add(CurrentClipTime(actor));
                actor.extraImpacts.Sort();
            }
        }

        for (int i = 0; i < actor.extraImpacts.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                actor.extraImpacts[i] = Mathf.Max(
                    EditorGUILayout.FloatField($"  {i + 1}타 (clip s)", actor.extraImpacts[i]), 0f);

                if (GUILayout.Button("→", GUILayout.Width(26f))) t = TimeOfClipMark(actor, actor.extraImpacts[i]);
                if (GUILayout.Button("×", GUILayout.Width(26f)))
                {
                    actor.extraImpacts.RemoveAt(i);
                    i--;
                }
            }
        }

        if (actor.extraImpacts.Count == 0) return;

        int valid = 0;
        foreach (float mark in actor.extraImpacts)
            if (mark > actor.clipStart && mark < actor.clipImpact) valid++;

        if (valid != actor.extraImpacts.Count)
            EditorGUILayout.HelpBox(
                $"{actor.extraImpacts.Count - valid}개가 Start~Impact 밖입니다 — 저장에서 제외됩니다.\n" +
                "추가 칼질은 마지막 베기(Impact)보다 앞이어야 합니다. 정렬 앵커는 언제나 Impact 하나입니다.",
                MessageType.Warning);

        float freeze = valid * hitStopDurationHint;
        EditorGUILayout.LabelField("정지 예산",
            $"{valid}회 × {hitStopDurationHint:0.00}s = {freeze:0.00}s   (이만큼 클립을 일찍 시작한다)");

        if (freeze > ExtraFreezeBudgetWarning)
            EditorGUILayout.HelpBox(
                $"예산 {freeze:0.00}s는 실측 채보 대부분의 여유(엔트리 간 0.4초)를 넘습니다 — " +
                "런타임이 예산을 포기하고 배속으로 벌충하며, 상한에 걸리면 마지막 베기가 절단보다 늦습니다.",
                MessageType.Warning);
    }

    /// <summary>지금 t가 어느 추가 칼질 위인지(없으면 -1). 한 프레임 폭(1/30초)을 허용 오차로 본다.</summary>
    private static int ExtraMarkAt(Actor actor, float time)
    {
        for (int i = 0; i < actor.extraImpacts.Count; i++)
            if (Mathf.Abs(TimeOfClipMark(actor, actor.extraImpacts[i]) - time) < 1f / 30f) return i;

        return -1;
    }

    /// <summary>클립 시각 하나를 타임라인 t(임팩트 기준 상대시간)로 되돌린다. 마크로 점프하는 데 쓴다.</summary>
    private static float TimeOfClipMark(Actor actor, float clipMark) =>
        DuetTimeline.RelativeOf(actor.clipImpact, actor.speed, clipMark);

    /// <summary>지금 t가 가리키는 이 배우의 클립 시각. 마킹의 유일한 변환 지점이다.</summary>
    private float CurrentClipTime(Actor actor) => DuetTimeline.ClipTimeOf(actor.clipImpact, actor.speed, t);

    /// <summary>
    /// 임팩트는 시간축의 원점이다 — 옮기면 원점이 따라 옮겨지고 <c>t</c>는 0이 된다.
    /// 보정 코드가 아니라 식에서 나오는 결과라, 여기서는 그 결과를 그대로 반영만 한다.
    /// </summary>
    private void MarkImpact(Actor actor)
    {
        actor.clipImpact = CurrentClipTime(actor);
        t = 0f;
    }

    private void DrawNotes()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "· 저작 배속 기준입니다. 런타임은 채보 간격과 입력 시각에 따라 압축되며, 그때도 임팩트 프레임은 동일합니다.\n" +
            "· 두 임팩트는 정의상 같은 시각(t=0)입니다 — 서로 맞출 필요가 없습니다.",
            MessageType.None);
    }

    // ─────────────────────────── 칼날 트레일 ───────────────────────────

    private void DrawSwordTrailEvents()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sword Trail Events (플레이어 클립)", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!player.HasClip))
        {
            if (GUILayout.Button("Write StartSwordTrail(Start) / StopSwordTrail(End) to Clip"))
                ApplySwordTrailEvents();
        }
    }

    private void ApplySwordTrailEvents()
    {
        Undo.RecordObject(player.clip, "Apply Sword Trail Events");

        var events = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(player.clip));
        events.RemoveAll(e => e.functionName == "StartSwordTrail" || e.functionName == "StopSwordTrail");

        events.Add(new AnimationEvent { time = player.clipStart, functionName = "StartSwordTrail" });
        events.Add(new AnimationEvent { time = player.clipEnd, functionName = "StopSwordTrail" });
        events.Sort((a, b) => a.time.CompareTo(b.time));

        AnimationUtility.SetAnimationEvents(player.clip, events.ToArray());

        EditorUtility.SetDirty(player.clip);
        AssetDatabase.SaveAssets();
    }

    // ─────────────────────────── 저장 ───────────────────────────

    private void DrawApply()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("창 → Pattern (쓰기)", EditorStyles.miniBoldLabel);

        bool canApply = (player.HasClip && player.Duration > 0f)
                        || (enemy.HasClip && enemy.Duration > 0f)
                        || (feint.HasClip && feint.Duration > 0f)
                        || HasCurve; // 클립 없이 거리 커브만 저작하는 경우도 저장할 수 있어야 한다

        using (new EditorGUI.DisabledScope(!canApply))
        {
            if (GUILayout.Button("▶ Apply to Pattern (모든 슬롯 + 거리 보정)", GUILayout.Height(28f)))
                ApplyToPattern();
        }

        if (!canApply)
            EditorGUILayout.HelpBox("배우 하나 이상이 클립과 Duration > 0을 가져야 저장할 수 있습니다.", MessageType.Warning);

        DrawSyncState();
    }

    /// <summary>창의 값이 에셋과 같은가. 저장 여부 표시와 <see cref="ConfirmBeforeReload"/>가 같은 판단을 쓴다.</summary>
    private bool IsSynced()
    {
        if (targetPattern == null) return true;

        var so = new SerializedObject(targetPattern);
        return ActorSynced(so, player) && ActorSynced(so, enemy) && ActorSynced(so, feint)
               && Mathf.Approximately(so.FindProperty("duelDistanceOffset")?.floatValue ?? 0f, duelDistanceOffset)
               && CurveSynced(so);
    }

    /// <summary>
    /// 창을 다시 읽기 <b>전에</b> 저장 안 된 편집을 어떻게 할지 묻는다. 계속해도 되면 true.
    ///
    /// <para><b>⚠ 이 가드가 없으면 편집이 조용히 사라진다.</b> <see cref="LoadFromPattern"/>은 창의 값을
    /// 에셋 값으로 덮어쓰는데, 슬롯을 바꾸거나 리스트를 건드릴 때마다 그것이 불린다 —
    /// 즉 <b>"클립을 꽂고 다른 슬롯으로 넘어가면" 그 편집이 없던 일이 된다</b>.
    /// 슬롯이 여럿인 패턴(리드인·연타 타격)에서는 여러 개를 차례로 채우는 것이 정상 작업이라
    /// 이 경로를 반드시 밟게 된다.</para>
    /// </summary>
    private bool ConfirmBeforeReload()
    {
        if (targetPattern == null || IsSynced()) return true;

        int choice = EditorUtility.DisplayDialogComplex(
            "저장 안 된 변경",
            $"'{targetPattern.name}'의 창 값이 에셋과 다릅니다. 지금 이동하면 이 편집은 사라집니다.",
            "저장하고 이동", "취소", "버리고 이동");

        if (choice == 0) { ApplyToPattern(); return true; }
        return choice == 2;
    }

    /// <summary>창의 상태와 에셋이 같은지. 다르면 아직 저장 전이라는 뜻이다.</summary>
    private void DrawSyncState()
    {
        bool synced = IsSynced();

        EditorGUILayout.LabelField(" ",
            synced ? "✔ 저장됨 (창과 에셋이 일치)" : "● 저장 전 — 창의 값이 에셋과 다릅니다",
            EditorStyles.miniLabel);
    }

    /// <summary>창의 커브와 에셋의 커브가 같은가. 키 개수·시각·값만 본다(탄젠트는 값이 같으면 같은 그림이다).</summary>
    private bool CurveSynced(SerializedObject so)
    {
        var stored = so.FindProperty("duelDistanceCurve")?.animationCurveValue;
        int storedLength = stored?.length ?? 0;
        int windowLength = duelDistanceCurve?.length ?? 0;

        if (storedLength != windowLength) return false;

        for (int i = 0; i < storedLength; i++)
        {
            if (!Mathf.Approximately(stored[i].time, duelDistanceCurve[i].time)) return false;
            if (!Mathf.Approximately(stored[i].value, duelDistanceCurve[i].value)) return false;
        }

        return true;
    }

    private static bool ActorSynced(SerializedObject so, Actor actor)
    {
        if (actor.slotPath == null) return true;

        var storedClip = so.FindProperty($"{actor.slotPath}.clip")?.objectReferenceValue as AnimationClip;
        float off = so.FindProperty($"{actor.slotPath}.startOffset")?.floatValue ?? 0f;
        float dur = so.FindProperty($"{actor.slotPath}.duration")?.floatValue ?? 0f;
        // 리드인은 ImpactTime을 저장하지 않으므로(0이 정상) 창의 시각 앵커와 대조하면 안 된다.
        float imp = actor.isLeadIn
            ? Mathf.Clamp(actor.clipImpact, actor.clipStart, actor.clipEnd)
            : so.FindProperty($"{actor.slotPath}.impactTime")?.floatValue ?? 0f;

        return storedClip == actor.clip
               && Mathf.Approximately(off, actor.clipStart)
               && Mathf.Approximately(dur, actor.Duration)
               && Mathf.Approximately(imp, Mathf.Clamp(actor.clipImpact, actor.clipStart, actor.clipEnd));
    }

    /// <summary>
    /// 두 배우와 거리 보정을 <b>한 번에</b> 기록한다 — 한쪽만 저장돼 짝이 어긋난 상태를 만들지 않는다.
    /// <b>상태가 이미 에셋 단위(클립 시간)라 여기서 시간 변환을 하지 않는다.</b>
    /// </summary>
    private void ApplyToPattern()
    {
        Undo.RecordObject(targetPattern, "Apply Pattern Actions");

        var so = new SerializedObject(targetPattern);
        if (!WriteActor(so, player) || !WriteActor(so, enemy) || !WriteActor(so, feint)) return;

        var offsetProp = so.FindProperty("duelDistanceOffset");
        if (offsetProp != null) offsetProp.floatValue = duelDistanceOffset;

        var curveProp = so.FindProperty("duelDistanceCurve");
        if (curveProp != null) curveProp.animationCurveValue = duelDistanceCurve ?? new AnimationCurve();

        var heightProp = so.FindProperty("duelHeightCurve");
        if (heightProp != null) heightProp.animationCurveValue = duelHeightCurve ?? new AnimationCurve();

        var offsetsProp = so.FindProperty("engageOffsets");
        if (offsetsProp != null)
        {
            offsetsProp.arraySize = engageOffsets.Count;
            for (int i = 0; i < engageOffsets.Count; i++)
                offsetsProp.GetArrayElementAtIndex(i).vector2Value = engageOffsets[i];
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(targetPattern);
        AssetDatabase.SaveAssetIfDirty(targetPattern);

        VerifyRoundTrip();

        Debug.Log(
            $"[PatternActionEditor] '{targetPattern.name}' 저장 — " +
            $"{Describe(player)} / {Describe(feint)} / {Describe(enemy)} / 거리보정 {duelDistanceOffset:+0.00;-0.00;0.00}m" +
            (HasCurve ? $" / 거리커브 키 {duelDistanceCurve.length}개" : string.Empty) +
            (HasHeightCurve ? $" / 높이커브 키 {duelHeightCurve.length}개" : string.Empty) +
            (engageOffsets.Count > 0 ? $" / 교전 {engageOffsets.Count}명" : string.Empty),
            targetPattern);
    }

    private bool WriteActor(SerializedObject so, Actor actor)
    {
        if (actor.slotPath == null || !actor.HasClip) return true; // 빈 슬롯은 건드리지 않는다

        var clipProp = so.FindProperty($"{actor.slotPath}.clip");
        var offProp = so.FindProperty($"{actor.slotPath}.startOffset");
        var durProp = so.FindProperty($"{actor.slotPath}.duration");
        var impProp = so.FindProperty($"{actor.slotPath}.impactTime");

        // 하나라도 안 잡히면 조용히 아무 데도 안 쓰이는 상태가 된다 — 즉시 알린다.
        if (clipProp == null || offProp == null || durProp == null || impProp == null)
        {
            Debug.LogError(
                $"[PatternActionEditor] '{targetPattern.name}'에서 슬롯 '{actor.slotPath}'의 필드를 찾지 못했습니다. 저장하지 않았습니다.",
                targetPattern);
            return false;
        }

        // 임팩트는 트림 안에 있어야 한다 — 밖이면 런타임이 트림 끝으로 폴백해 오서링이 조용히 무시된다.
        actor.clipImpact = Mathf.Clamp(actor.clipImpact, actor.clipStart, actor.clipEnd);

        clipProp.objectReferenceValue = actor.clip;
        offProp.floatValue = actor.clipStart;
        durProp.floatValue = actor.Duration;

        // ⚠ 리드인 원소에는 ImpactTime을 쓰지 않는다. 정렬 앵커는 마지막 클립 하나뿐이라 아무 일도 하지 않는데,
        // 값이 남아 있으면 Pattern.OnValidate가 "오해의 진입점"으로 보고 경고한다.
        impProp.floatValue = actor.isLeadIn ? 0f : actor.clipImpact;

        // 추가 칼질 — 트림 시작~마지막 베기 '사이'만 남긴다. 밖의 값은 런타임이 어차피 버리므로
        // 여기서 걸러야 "찍었는데 안 나온다"가 안 생긴다.
        var extrasProp = so.FindProperty($"{actor.slotPath}.extraImpactTimes");
        if (extrasProp != null && extrasProp.isArray)
        {
            var kept = new List<float>(actor.extraImpacts.Count);
            foreach (float mark in actor.extraImpacts)
                if (mark > actor.clipStart && mark < actor.clipImpact) kept.Add(mark);

            kept.Sort();

            int dropped = actor.extraImpacts.Count - kept.Count;
            if (dropped > 0)
                Debug.LogWarning(
                    $"[PatternActionEditor] '{targetPattern.name}'의 {actor.label} 추가 칼질 {dropped}개가 " +
                    "Start~Impact 구간 밖이라 저장에서 제외됐습니다.", targetPattern);

            extrasProp.arraySize = kept.Count;
            for (int i = 0; i < kept.Count; i++)
                extrasProp.GetArrayElementAtIndex(i).floatValue = kept[i];
        }
        return true;
    }

    /// <summary>저장 직후 에셋을 다시 읽어 상태와 대조한다. 앞의 방어를 다 뚫고 온 것까지 잡는 마지막 그물.</summary>
    private void VerifyRoundTrip()
    {
        var so = new SerializedObject(targetPattern);
        if (ActorSynced(so, player) && ActorSynced(so, enemy) && ActorSynced(so, feint)) return;

        Debug.LogError(
            $"[PatternActionEditor] '{targetPattern.name}' 저장 후 값이 창과 다릅니다. " +
            "시간 단위 변환이 저장 경로에 끼어들었는지 확인하세요.", targetPattern);
    }

    private static string Describe(Actor actor) => actor.HasClip
        ? $"{actor.slotPath}: {actor.clip.name} [{actor.clipStart:0.000}/{actor.clipImpact:0.000}/{actor.clipEnd:0.000}]"
        : $"{actor.slotPath}: (비어 있음)";
}
