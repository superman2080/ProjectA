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

        public GameObject prefab;    // 프리뷰용
        public GameObject instance;
        public GameObject cachedPrefab;
        public Vector3 placement;
        public Quaternion facing;

        public bool HasClip => clip != null;
        public float Duration => clipEnd - clipStart;

        /// <summary>지금 t에서 이 배우가 서 있어야 할 클립 시각(트림으로 잘린 값).</summary>
        public float SampleTime(float t) => DuetTimeline.ClampToTrim(
            DuetTimeline.ClipTimeOf(clipImpact, speed, t), clipStart, clipEnd);
    }

    /// <summary>
    /// 처치가 확정되는 시각(임팩트 기준 상대시간, 양수 = 임팩트보다 이만큼 앞).
    /// 런타임에서는 마지막 노드 입력이 그 순간이고 임팩트는 거기서 <c>goodWindow</c>만큼 뒤다 —
    /// <b>견제가 사망으로 넘어가는 지점이 정확히 여기다.</b>
    /// </summary>
    private const float HandoffLead = 0.1f;

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
        Hit,     // 임팩트 — 맞았는데 안 죽었다(사슬 중간 타격)
        Parry    // 임팩트 — 막아냈다
    }

    private EnemyAuxSlot enemyAuxSlot = EnemyAuxSlot.Feint;

    private static string AuxSlotPath(EnemyAuxSlot slot) => slot switch
    {
        EnemyAuxSlot.Hit => "enemyHit",
        EnemyAuxSlot.Parry => "enemyParry",
        _ => "enemyFeint"
    };

    private static string AuxSlotLabel(EnemyAuxSlot slot) => slot switch
    {
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

    private float DuelDistance => duelBaseDistance + duelDistanceOffset;

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

        DrawInputs();

        if (targetPattern == null)
        {
            EditorGUILayout.HelpBox("Pattern을 지정하면 그 패턴의 역할에 맞는 두 배우가 열립니다.", MessageType.Info);
            return;
        }

        DrawPreview();
        DrawReadouts();
        DrawTimeline();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawActorBlock(player);
        if (feint.slotPath != null) DrawActorBlock(feint); // 시간순: 견제가 사망보다 앞이다
        DrawActorBlock(enemy);
        DrawNotes();
        DrawSwordTrailEvents();
        DrawApply();
        EditorGUILayout.EndScrollView();
    }

    private void DrawInputs()
    {
        EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        targetPattern = (Pattern)EditorGUILayout.ObjectField("Pattern", targetPattern, typeof(Pattern), false);
        if (EditorGUI.EndChangeCheck()) LoadFromPattern();

        if (targetPattern == null) return;

        bool enemyIsAttacker = targetPattern.Attacker == EnemySpace.Attacker.Enemy;
        EditorGUILayout.LabelField("역할",
            enemyIsAttacker ? "Enemy — 적 공격 → 플레이어 패링" : "Player — 플레이어 공격 → 적 사망");

        EditorGUI.BeginChangeCheck();
        enemyAuxSlot = (EnemyAuxSlot)EditorGUILayout.EnumPopup(
            new GUIContent("적 보조 슬롯", "적 한 명의 다른 시각 구간이라 배우를 늘리지 않고 슬롯만 바꾼다.\n" +
                                      "Feint = 표적이 된 순간~임팩트 / Hit = 맞았는데 안 죽음(사슬 중간) / Parry = 막아냄."),
            enemyAuxSlot);
        if (EditorGUI.EndChangeCheck()) LoadFromPattern();

        if (enemyAuxSlot != EnemyAuxSlot.Feint)
        {
            EditorGUILayout.HelpBox(
                "피격·패링은 임팩트에 시작합니다 — ImpactTime을 찍지 않으면 t = 0이 클립 시작입니다.\n" +
                "⚠ 트림 0.5초 이하 권장: 길면 다음 패턴의 견제 클립이 끊습니다.",
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

        EditorGUILayout.LabelField(" ", $"실제 배치 거리 {DuelDistance:0.00}m", EditorStyles.miniLabel);

        lockRootPosition = EditorGUILayout.Toggle(
            new GUIContent("루트 위치 고정", "루트 모션이 배우를 밀어내면 프레임마다 거리가 변해 배치 확인이 무의미해진다."),
            lockRootPosition);

        EditorGUILayout.Space();
    }

    /// <summary>패턴의 역할이 짝을 정한다. 두 슬롯을 <b>동시에</b> 읽는다.</summary>
    private void LoadFromPattern()
    {
        if (targetPattern == null) return;

        bool enemyIsAttacker = targetPattern.Attacker == EnemySpace.Attacker.Enemy;

        player.slotPath = enemyIsAttacker ? "playerParry" : "playerAttack";
        enemy.slotPath = enemyIsAttacker ? "enemyAttack" : "enemyDeath";
        player.label = enemyIsAttacker ? "플레이어 — playerParry" : "플레이어 — playerAttack";
        enemy.label = enemyIsAttacker ? "적 — enemyAttack" : "적 — enemyDeath (사망)";

        // 견제는 Attacker.Player 전용이다 — 적이 공격자면 그 구간을 enemyAttack이 채운다.
        // 피격·패링은 역할과 무관하게 열어 둔다(적이 공격자여도 막힐 수는 있다).
        feint.slotPath = enemyIsAttacker && enemyAuxSlot == EnemyAuxSlot.Feint ? null : AuxSlotPath(enemyAuxSlot);
        feint.label = AuxSlotLabel(enemyAuxSlot);

        var so = new SerializedObject(targetPattern);
        LoadActor(so, player);
        LoadActor(so, enemy);
        LoadActor(so, feint);

        duelDistanceOffset = so.FindProperty("duelDistanceOffset")?.floatValue ?? 0f;
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
        float impact = so.FindProperty($"{actor.slotPath}.impactTime")?.floatValue ?? 0f;
        actor.clipImpact = impact > 0f ? impact : actor.clipEnd;
    }

    // ─────────────────────────── 프리뷰 ───────────────────────────

    private void DrawPreview()
    {
        Rect rect = GUILayoutUtility.GetRect(position.width, PreviewHeight, GUILayout.ExpandWidth(true));

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
        PoseActor(player, player);
        PoseActor(enemy, ActiveEnemyActor()); // 리그는 적 하나, 모션만 시간으로 갈린다
        bladeGap = MeasureBladeGap();

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

    /// <summary>플레이어는 원점에서 +Z, 적은 결투 거리 앞에서 마주 본다(슬라이서와 같은 규약).</summary>
    private void PlaceActors()
    {
        player.placement = Vector3.zero;
        player.facing = Quaternion.identity;
        enemy.placement = Vector3.forward * DuelDistance;
        enemy.facing = Quaternion.Euler(0f, 180f, 0f);
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

        var tr = rig.instance.transform;

        // 중립에서 샘플링 — 이래야 나온 값이 순수한 루트 모션이 된다.
        tr.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        if (motion.HasClip) motion.clip.SampleAnimation(rig.instance, motion.SampleTime(t));

        Quaternion rootRotation = tr.localRotation;
        Vector3 rootOffset = tr.localPosition;

        // 루트 모션이 배우를 밀어내면 두 배우의 거리가 프레임마다 변해 배치 확인이 무의미해진다.
        // 위치만 잠그고 회전은 살린다 — 방향 전환은 봐야 한다.
        Vector3 position = rig.placement + (lockRootPosition ? Vector3.zero : rig.facing * rootOffset);
        tr.SetPositionAndRotation(position, rig.facing * rootRotation);
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
    private float MeasureBladeGap()
    {
        if (player.instance == null || enemy.instance == null) return float.NaN;

        Transform weapon = FindDeep(player.instance.transform, WeaponBoneName);
        var bladeRenderer = weapon != null ? weapon.GetComponentInChildren<Renderer>() : null;
        if (bladeRenderer == null) return float.NaN;

        var enemyRenderers = enemy.instance.GetComponentsInChildren<Renderer>();
        if (enemyRenderers.Length == 0) return float.NaN;

        Bounds enemyBounds = enemyRenderers[0].bounds;
        for (int i = 1; i < enemyRenderers.Length; i++) enemyBounds.Encapsulate(enemyRenderers[i].bounds);

        return BoundsGap(bladeRenderer.bounds, enemyBounds);
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

        GUILayout.Box(atImpact ? $"✦ IMPACT   {gapText}" : gapText, style, GUILayout.Height(22f));
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

        if (!feint.HasClip) return (min, max);

        return (
            Mathf.Min(min, -DuetTimeline.LeadOf(feint.clipStart, feint.clipImpact, feint.speed)),
            Mathf.Max(max, DuetTimeline.TailOf(feint.clipImpact, feint.clipEnd, feint.speed)));
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

        Rect sliderRect = GUILayoutUtility.GetRect(position.width, 22f);
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

        float startT = DuetTimeline.RelativeOf(actor.clipImpact, actor.speed, actor.clipStart);
        float endT = DuetTimeline.RelativeOf(actor.clipImpact, actor.speed, actor.clipEnd);

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
            if (GUILayout.Button("Mark Impact")) MarkImpact(actor);
            if (GUILayout.Button("Mark End")) actor.clipEnd = CurrentClipTime(actor);
        }

        // 입력 필드는 클립 시간이다 — 에셋에 들어갈 숫자를 그대로 보여 인스펙터와 눈으로 대조할 수 있게.
        actor.clipStart = Mathf.Max(EditorGUILayout.FloatField("Start (clip s)", actor.clipStart), 0f);
        actor.clipImpact = Mathf.Max(EditorGUILayout.FloatField("Impact (clip s)", actor.clipImpact), 0f);
        actor.clipEnd = EditorGUILayout.FloatField("End (clip s)", actor.clipEnd);

        EditorGUILayout.LabelField("Duration", $"{actor.Duration:0.000}s   ·   speed {actor.speed:0.00}");

        if (actor.Duration <= 0f)
            EditorGUILayout.HelpBox("End가 Start보다 뒤여야 합니다 (Duration > 0).", MessageType.Warning);
        if (actor.clipImpact < actor.clipStart || actor.clipImpact > actor.clipEnd)
            EditorGUILayout.HelpBox("Impact가 Start~End 밖입니다. 저장 시 구간 안으로 클램프됩니다.", MessageType.Warning);

        EditorGUILayout.EndVertical();
    }

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
                        || (feint.HasClip && feint.Duration > 0f);

        using (new EditorGUI.DisabledScope(!canApply))
        {
            if (GUILayout.Button("▶ Apply to Pattern (모든 슬롯 + 거리 보정)", GUILayout.Height(28f)))
                ApplyToPattern();
        }

        if (!canApply)
            EditorGUILayout.HelpBox("배우 하나 이상이 클립과 Duration > 0을 가져야 저장할 수 있습니다.", MessageType.Warning);

        DrawSyncState();
    }

    /// <summary>창의 상태와 에셋이 같은지. 다르면 아직 저장 전이라는 뜻이다.</summary>
    private void DrawSyncState()
    {
        var so = new SerializedObject(targetPattern);
        bool synced = ActorSynced(so, player) && ActorSynced(so, enemy) && ActorSynced(so, feint)
                      && Mathf.Approximately(so.FindProperty("duelDistanceOffset")?.floatValue ?? 0f, duelDistanceOffset);

        EditorGUILayout.LabelField(" ",
            synced ? "✔ 저장됨 (창과 에셋이 일치)" : "● 저장 전 — 창의 값이 에셋과 다릅니다",
            EditorStyles.miniLabel);
    }

    private static bool ActorSynced(SerializedObject so, Actor actor)
    {
        if (actor.slotPath == null) return true;

        var storedClip = so.FindProperty($"{actor.slotPath}.clip")?.objectReferenceValue as AnimationClip;
        float off = so.FindProperty($"{actor.slotPath}.startOffset")?.floatValue ?? 0f;
        float dur = so.FindProperty($"{actor.slotPath}.duration")?.floatValue ?? 0f;
        float imp = so.FindProperty($"{actor.slotPath}.impactTime")?.floatValue ?? 0f;

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

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(targetPattern);
        AssetDatabase.SaveAssetIfDirty(targetPattern);

        VerifyRoundTrip();

        Debug.Log(
            $"[PatternActionEditor] '{targetPattern.name}' 저장 — " +
            $"{Describe(player)} / {Describe(feint)} / {Describe(enemy)} / 거리보정 {duelDistanceOffset:+0.00;-0.00;0.00}m",
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
        impProp.floatValue = actor.clipImpact;
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
