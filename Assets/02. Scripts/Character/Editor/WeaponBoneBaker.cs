using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 무기 본(add_weapon_r/l) 커브 굽기의 실제 동작. UI(<see cref="WeaponBoneBakeWindow"/>)와 분리되어 스크립트에서도 호출할 수 있다.
///
/// 원리: 정상 클립에서 <b>손 기준 무기 오프셋(그립)</b>을 역산한 뒤, 대상 클립의 손 자세에 그 오프셋을 곱해
/// 무기 본의 root 기준 로컬 TRS를 프레임마다 계산해 커브로 기록한다.
/// 실측상 정상 클립의 무기 회전은 정확히 "손 회전 × 상수"였다(편차 0.00도). 즉 이 모델이 기존 데이터와 동일하다.
///
/// 상세: docs/WeaponBoneBake/
/// </summary>
public static class WeaponBoneBaker
{
    private const string ClipDir = "Assets/05. Animations/Clip";
    private const string BackupDir = ClipDir + "/_Backup";
    public const string PathWeaponR = "root/add_weapon_r";
    public const string PathWeaponL = "root/add_weapon_l";

    /// <summary>손이 쥔 무기의 있을 법한 최대 거리(m). 실측 그립은 0.09m다 — 0.25m를 넘으면 손에 없는 것이다.</summary>
    private const float HandOffsetLimit = 0.25f;

    /// <summary>허리에 찬 검집의 있을 법한 최대 거리(m). 실측 0.30m라 손보다 넉넉해야 한다.</summary>
    private const float WaistOffsetLimit = 0.6f;

    private static readonly string[] PosProps = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
    private static readonly string[] RotProps = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };

    /// <summary>검집(add_weapon_l) 굽기 방식. 순서(0/1/2)가 윈도우 드롭다운 인덱스와 일치해야 한다.</summary>
    public enum SheathMode
    {
        None = 0,  // 굽지 않음(커브 없음 → 바인드 포즈에 얼어붙음)
        Waist = 1, // pelvis 기준 바인드 오프셋으로 허리에 강체 고정 → 몸을 따라 회전/이동
        Hand = 2,  // hand_l 기준 참조 클립 그립(정상 클립의 발도 자세)
    }

    /// <summary>
    /// 기준 본(손/허리) 기준 무기 오프셋. 오프셋이 상수라는 것이 이 접근의 전제이므로 편차도 함께 들고 다닌다.
    ///
    /// <para><b>편차는 평균이 아니라 실제로 구울 오프셋과 견준다.</b> 굽기 결과의 오차가 알고 싶은 값이지
    /// 표본의 산포가 아니다. 그리고 max와 평균을 <b>둘 다</b> 든다 — 한 프레임만 튄 것(평균 ≪ max, 보통
    /// 클립 경계의 프리롤)과 전 구간이 어긋난 것(평균 ≈ max, 참조 클립이 틀렸거나 리타깃 오차)은
    /// 원인도 대처도 완전히 다른데 max 하나로는 구분이 안 된다.</para>
    /// </summary>
    public struct Grip
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float MaxPosDev;
        public float MaxRotDev;

        /// <summary>전 프레임 평균 위치 편차. max와 가까우면 전 구간이 어긋난 것이다.</summary>
        public float MeanPosDev;

        /// <summary>위치 편차가 최대인 시각(초). 0이나 클립 끝이면 경계 프레임 하나만 튄 것이다.</summary>
        public float WorstTime;

        /// <summary>기준 본이 첫 프레임에서 월드로 이동한 최대 거리. 이동이 있는 클립을 잡는다.</summary>
        public float BodyTravel;

        /// <summary>무기 본이 첫 프레임에서 월드로 이동한 최대 거리. 몸만 가고 무기가 남으면 루트모션 불일치다.</summary>
        public float WeaponTravel;
    }

    // ─────────────────────────── 공개 API ───────────────────────────

    /// <summary>굽지 않고 그립/허리 오프셋만 역산해 보고한다(전제 검증용).</summary>
    public static string Inspect(GameObject prefab, AnimationClip reference, bool katana, SheathMode sheath, int fps, float gripTime)
    {
        if (reference == null) return "참조 클립이 필요합니다.";

        GameObject instance = null;
        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Bones bones = ResolveBones(instance);
            if (bones == null) return BoneError;

            instance.hideFlags = HideFlags.HideAndDontSave;

            var sb = new StringBuilder();
            sb.AppendLine("참조 클립: " + reference.name + " (" + fps + "fps, 길이 " + reference.length.ToString("F3") + "초)");
            sb.AppendLine("그립 기준: " + (gripTime >= 0f ? gripTime.ToString("F3") + "초 프레임" : "전 구간 평균"));
            sb.AppendLine();

            if (katana)
                sb.AppendLine(Describe("칼  hand_r ", ExtractGrip(instance, bones.HandR, bones.WeaponR, reference, fps, gripTime), HandOffsetLimit));

            if (sheath == SheathMode.Waist)
                sb.AppendLine(Describe("검집 pelvis", ExtractGrip(instance, bones.Pelvis, bones.WeaponL, reference, fps, gripTime), WaistOffsetLimit));
            else if (sheath == SheathMode.Hand)
                sb.AppendLine(Describe("검집 hand_l", ExtractGrip(instance, bones.HandL, bones.WeaponL, reference, fps, gripTime), HandOffsetLimit));

            sb.AppendLine();
            sb.AppendLine("위치편차 max가 0.005m 미만이면 강체 — 상수 오프셋으로 구워도 안전합니다.");
            sb.AppendLine("참조 클립은 그 무기가 실제로 그 본에 붙어 있는 구간이어야 합니다 —");
            sb.AppendLine("칼이 검집에 꽂힌 클립(Run/Release/Sprint_Forward/Katana_Idle)을 칼 참조로 쓰면 편차가 팔 궤적만큼 커집니다.");
            return sb.ToString();
        }
        finally
        {
            Cleanup(instance);
        }
    }

    /// <summary>
    /// 참조 클립에서 역산한 오프셋(칼=hand_r, 검집=pelvis 또는 hand_l)을 대상 클립에 적용해 무기 본 커브를 생성한다.
    ///
    /// <para><b>검집 허리 고정도 참조 클립에서 역산한다.</b> 예전에는 프리팹의 저장 포즈를 "바인드 포즈"로 보고
    /// 거기서 상수를 떴는데, 무기 본은 root 직계 자식이라 커브 없이는 아무 자세로나 굳는다 — 실제로 이 프리팹의
    /// <c>add_weapon_l</c>은 pelvis에서 <b>0.55m 떨어진</b> 자리에 저장돼 있어 검집이 허공에 붙었다.
    /// 저장 포즈는 데이터가 아니다.</para>
    /// </summary>
    public static string Bake(GameObject prefab, AnimationClip reference, AnimationClip target,
        bool katana, SheathMode sheath, int fps, bool backup, float gripTime)
    {
        bool doSheath = sheath != SheathMode.None;
        if (!katana && !doSheath) return "굽기 대상이 없습니다.";
        if (reference == null) return "참조 클립이 필요합니다.";

        GameObject instance = null;
        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Bones bones = ResolveBones(instance);
            if (bones == null) return BoneError;

            instance.hideFlags = HideFlags.HideAndDontSave; // 그래프가 본을 직접 몰므로 씬을 더럽히지 않게 막는다.

            // 검집이 따라갈 기준 본: 허리=pelvis, 손=hand_l. 오프셋 역산과 굽기가 반드시 같은 본이어야 한다.
            Transform sheathBone = sheath == SheathMode.Waist ? bones.Pelvis : bones.HandL;

            Grip gripR = default(Grip);
            Grip gripL = default(Grip);
            if (katana) gripR = ExtractGrip(instance, bones.HandR, bones.WeaponR, reference, fps, gripTime);
            if (doSheath) gripL = ExtractGrip(instance, sheathBone, bones.WeaponL, reference, fps, gripTime);

            int frames = Mathf.Max(2, Mathf.RoundToInt(target.length * fps));
            var times = new float[frames + 1];
            var posR = new Vector3[frames + 1]; var rotR = new Quaternion[frames + 1];
            var posL = new Vector3[frames + 1]; var rotL = new Quaternion[frames + 1];

            using (var sampler = new ClipSampler(instance, target))
            for (int i = 0; i <= frames; i++)
            {
                float t = target.length * i / frames;
                times[i] = t;
                sampler.Sample(t);
                if (katana) ComposeLocal(bones.Root, bones.HandR, gripR, out posR[i], out rotR[i]);
                if (doSheath) ComposeLocal(bones.Root, sheathBone, gripL, out posL[i], out rotL[i]);
            }

            string backupNote = backup ? Backup(target) : "백업 생략";
            if (katana) WriteCurves(target, PathWeaponR, times, posR, rotR);
            if (doSheath) WriteCurves(target, PathWeaponL, times, posL, rotL);

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.AppendLine("굽기 완료: " + target.name);
            sb.AppendLine("  " + (frames + 1) + "프레임 / " + fps + "fps / 길이 " + target.length.ToString("F3") + "초");
            if (katana) sb.AppendLine("  " + Describe("칼  ", gripR, HandOffsetLimit));
            if (sheath == SheathMode.Waist) sb.AppendLine("  " + Describe("검집(허리)", gripL, WaistOffsetLimit));
            else if (sheath == SheathMode.Hand) sb.AppendLine("  " + Describe("검집(손)  ", gripL, HandOffsetLimit));
            sb.AppendLine("  " + backupNote);
            return sb.ToString();
        }
        finally
        {
            Cleanup(instance);
        }
    }

    /// <summary>대상 클립에서 무기 본 커브를 모두 제거한다(되돌리기용).</summary>
    public static string RemoveWeaponCurves(AnimationClip clip)
    {
        int removed = 0;
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        for (int i = 0; i < bindings.Length; i++)
        {
            if (bindings[i].path != PathWeaponR && bindings[i].path != PathWeaponL) continue;
            AnimationUtility.SetEditorCurve(clip, bindings[i], null);
            removed++;
        }
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        return "무기 커브 " + removed + "개 제거: " + clip.name;
    }

    // ─────────────────────────── 내부 ───────────────────────────

    private const string BoneError = "본 탐색 실패 — root/pelvis/hand_r/hand_l/add_weapon_r/add_weapon_l 중 없는 것이 있습니다.";

    /// <param name="maxPlausibleOffset">
    /// 이 기준 본에서 무기까지 있을 법한 최대 거리(m). 손이 쥔 칼은 ~0.09m, 허리에 찬 검집은 ~0.30m다.
    /// <b>정지 클립은 무기가 엉뚱한 데 있어도 "강체"로 통과한다</b> — 칼이 검집에 꽂힌 Idle에서
    /// hand_r 기준 오프셋이 0.58m인데 편차는 22mm라 통과했다. 거리로만 잡을 수 있는 구멍이다.
    /// </param>
    private static string Describe(string label, Grip g, float maxPlausibleOffset)
    {
        // 절대 크기를 먼저 본다. 비율(평균 대 max)만 보면 2m짜리 파탄이 "국소 outlier"로,
        // 5mm짜리 정상이 "전 구간 어긋남"으로 뒤집혀 나온다 — 실제로 그렇게 나와서 고쳤다.
        // 비율은 "고칠 수 있는 수준"으로 판명된 뒤에야 대처를 가른다.
        string verdict;
        // 이동 클립이 먼저다. 휴머노이드는 이동을 RootT 머슬로 들고 가는데 무기 본은 root 기준 생 커브라,
        // 몸만 월드에서 전진하고 무기는 제자리에 남는다(실측: Sp_Run 0.87초에 몸 2.64m, 칼 0.05m).
        // 편차만 보면 "무기가 안 붙어 있다"로 오진하게 되므로 원인을 정확히 짚어 준다.
        if (g.BodyTravel - g.WeaponTravel > 0.2f)
            verdict = string.Format("✗ 이동 클립 실격 — 몸 {0:F2}m 이동 / 무기 {1:F2}m. 루트모션이 무기 본을 두고 간다. 제자리 클립을 쓸 것",
                g.BodyTravel, g.WeaponTravel);
        else if (g.Position.magnitude > maxPlausibleOffset)
            verdict = string.Format("✗ 오프셋 실격 — 기준 본에서 {0:F2}m 떨어져 있다(한계 {1:F2}m). 이 클립에서 무기가 그 본에 붙어 있지 않다",
                g.Position.magnitude, maxPlausibleOffset);
        else if (g.MaxPosDev > 0.05f || g.MaxRotDev > 45f)
            verdict = "✗ 참조 클립 실격 — 이 클립에서 무기가 그 본에 붙어 있지 않다(검집에 꽂힌 클립 등)";
        else if (g.MaxPosDev > 0.01f || g.MaxRotDev > 20f)
            verdict = g.MeanPosDev > g.MaxPosDev * 0.5f
                ? "△ 전 구간 어긋남 — 그립 기준 프레임을 임팩트에 맞춰라"
                : "△ 국소 outlier — worst 시각 근처만 튐(대개 클립 경계)";
        else
            verdict = "○ 강체 — 그대로 구워도 된다";

        return string.Format("[{0}] 오프셋={1} |{2:F3}m|  회전편차max={3:F2}도  위치편차 max={4:F4}m / 평균={5:F4}m (worst t={6:F3}초)\n    → {7}",
            label, g.Position.ToString("F4"), g.Position.magnitude, g.MaxRotDev, g.MaxPosDev, g.MeanPosDev, g.WorstTime, verdict);
    }

    /// <summary>
    /// 참조 클립을 훑어 기준 본↔무기 오프셋을 역산한다.
    ///
    /// <para><paramref name="gripTime"/>이 0 이상이면 <b>그 시각 한 프레임</b>의 오프셋을 쓴다.
    /// 오프셋이 진짜 상수가 아닐 때(휴머노이드 리타깃 오차 등) 평균은 <b>어느 프레임에서도 맞지 않는</b>
    /// 값이 된다 — 대신 가장 잘 보이는 프레임을 집으면 거기서 오차가 0이 되고 오차는 안 보이는 구간으로 밀린다.
    /// 음수면 종전대로 전 구간 평균.</para>
    /// </summary>
    private static Grip ExtractGrip(GameObject instance, Transform hand, Transform weapon, AnimationClip clip, int fps, float gripTime)
    {
        int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * fps));
        var positions = new List<Vector3>(frames + 1);
        var rotations = new List<Quaternion>(frames + 1);

        // 월드 이동량도 같이 잰다 — 이동이 있는 클립을 걸러내는 유일한 단서다(아래 Describe 참조).
        Vector3 handOrigin = Vector3.zero, weaponOrigin = Vector3.zero;
        float bodyTravel = 0f, weaponTravel = 0f;

        using (var sampler = new ClipSampler(instance, clip))
        for (int i = 0; i <= frames; i++)
        {
            sampler.Sample(clip.length * i / frames);
            positions.Add(hand.InverseTransformPoint(weapon.position));
            rotations.Add(Quaternion.Inverse(hand.rotation) * weapon.rotation);

            if (i == 0)
            {
                handOrigin = hand.position;
                weaponOrigin = weapon.position;
            }
            else
            {
                bodyTravel = Mathf.Max(bodyTravel, Vector3.Distance(hand.position, handOrigin));
                weaponTravel = Mathf.Max(weaponTravel, Vector3.Distance(weapon.position, weaponOrigin));
            }
        }

        Vector3 pickedPos;
        Quaternion pickedRot;

        if (gripTime >= 0f)
        {
            // 위치와 회전을 같은 프레임에서 뽑는다 — 서로 다른 프레임에서 뽑으면 어느 자세와도 맞지 않는 조합이 된다.
            int pick = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(gripTime, 0f, clip.length) / clip.length * frames), 0, frames);
            pickedPos = positions[pick];
            pickedRot = rotations[pick];
        }
        else
        {
            Vector3 mean = Vector3.zero;
            for (int i = 0; i < positions.Count; i++) mean += positions[i];
            pickedPos = mean / positions.Count;
            pickedRot = rotations[0];
        }

        // 편차는 표본 산포가 아니라 "실제로 구울 오프셋과의 오차"다. 그래야 숫자가 굽기 결과를 예측한다.
        float maxPos = 0f, sumPos = 0f, maxRot = 0f, worstTime = 0f;
        for (int i = 0; i < positions.Count; i++)
        {
            float distance = (positions[i] - pickedPos).magnitude;
            sumPos += distance;
            if (distance > maxPos)
            {
                maxPos = distance;
                worstTime = clip.length * i / frames;
            }

            maxRot = Mathf.Max(maxRot, Quaternion.Angle(pickedRot, rotations[i]));
        }

        return new Grip
        {
            Position = pickedPos,
            Rotation = pickedRot,
            MaxPosDev = maxPos,
            MaxRotDev = maxRot,
            MeanPosDev = sumPos / positions.Count,
            WorstTime = worstTime,
            BodyTravel = bodyTravel,
            WeaponTravel = weaponTravel,
        };
    }

    /// <summary>기준 본(손/허리)의 현재 자세에 오프셋을 곱해 무기의 root 기준 로컬 TRS를 만든다.</summary>
    private static void ComposeLocal(Transform root, Transform hand, Grip grip, out Vector3 localPos, out Quaternion localRot)
    {
        Vector3 worldPos = hand.TransformPoint(grip.Position);
        Quaternion worldRot = hand.rotation * grip.Rotation;
        localPos = root.InverseTransformPoint(worldPos);
        localRot = Quaternion.Inverse(root.rotation) * worldRot;
    }

    /// <summary>
    /// 클립 하나를 <b>런타임과 같은 경로</b>(Animator + PlayableGraph)로 샘플링한다.
    ///
    /// <para><b>⚠ <see cref="AnimationMode.SampleAnimationClip"/>을 쓰면 안 된다.</b> 그 API는
    /// 루트모션을 <b>포즈에 포함</b>시켜 몸이 전진한 상태를 준다. 반면 런타임은 <c>applyRootMotion=false</c>라
    /// 루트모션을 <b>버리고</b> 제자리에서 재생한다. 그 상태로 구우면 무기 커브에 전진량이 통째로 박혀,
    /// 재생 중 무기만 그만큼 날아간다 — 실측 <c>Run</c>에서 클립 끝 <b>2.68m</b>.
    /// 시작 프레임은 전진량이 0이라 멀쩡해 보이고 진행할수록 벌어지는 것이 이 증상의 특징이다.</para>
    ///
    /// <para>같은 클립 <c>Run</c>의 <c>hand_r</c> 월드 z 실측 — AnimationMode는 0.213→3.413로 전진,
    /// 그래프는 <c>SetTime</c> 랜덤 액세스든 델타 누적이든 0.213→0.213으로 제자리다.
    /// x·y는 셋이 완전히 같다(진행축만 갈린다).</para>
    /// </summary>
    private sealed class ClipSampler : System.IDisposable
    {
        private UnityEngine.Playables.PlayableGraph graph;
        private UnityEngine.Animations.AnimationClipPlayable playable;
        private readonly bool valid;

        public ClipSampler(GameObject instance, AnimationClip clip)
        {
            var animator = instance.GetComponent<Animator>();
            if (animator == null || clip == null) return;

            graph = UnityEngine.Playables.PlayableGraph.Create("WeaponBoneBaker");
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "out", animator);
            playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
            UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);
            valid = true;
        }

        /// <summary>시각을 두 번 대입하는 것은 의도된 것이다 — 한 번이면 이전 시각과의 델타가 남아 루프 경계에서 튄다.</summary>
        public void Sample(float time)
        {
            if (!valid) return;

            UnityEngine.Playables.PlayableExtensions.SetTime(playable, time);
            UnityEngine.Playables.PlayableExtensions.SetTime(playable, time);
            graph.Evaluate(0f);
        }

        public void Dispose()
        {
            if (valid && graph.IsValid()) graph.Destroy();
        }
    }

    private static void WriteCurves(AnimationClip clip, string path, float[] times, Vector3[] positions, Quaternion[] rotations)
    {
        // 쿼터니언 부호 연속성 — 이웃과 내적이 음수면 뒤집는다. 안 하면 보간이 먼 길로 돌아 칼이 한 바퀴 돈다.
        for (int i = 1; i < rotations.Length; i++)
        {
            if (Quaternion.Dot(rotations[i - 1], rotations[i]) < 0f)
                rotations[i] = new Quaternion(-rotations[i].x, -rotations[i].y, -rotations[i].z, -rotations[i].w);
        }

        var pos = new AnimationCurve[3];
        for (int c = 0; c < 3; c++) pos[c] = new AnimationCurve();
        var rot = new AnimationCurve[4];
        for (int c = 0; c < 4; c++) rot[c] = new AnimationCurve();

        for (int i = 0; i < times.Length; i++)
        {
            pos[0].AddKey(times[i], positions[i].x);
            pos[1].AddKey(times[i], positions[i].y);
            pos[2].AddKey(times[i], positions[i].z);
            rot[0].AddKey(times[i], rotations[i].x);
            rot[1].AddKey(times[i], rotations[i].y);
            rot[2].AddKey(times[i], rotations[i].z);
            rot[3].AddKey(times[i], rotations[i].w);
        }

        for (int c = 0; c < 3; c++) SetCurve(clip, path, PosProps[c], pos[c]);
        for (int c = 0; c < 4; c++) SetCurve(clip, path, RotProps[c], rot[c]);
    }

    private static void SetCurve(AnimationClip clip, string path, string property, AnimationCurve curve)
    {
        for (int k = 0; k < curve.length; k++) curve.SmoothTangents(k, 0f);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
    }

    private static string Backup(AnimationClip clip)
    {
        string src = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(src)) return "백업 실패 — 에셋 경로를 찾을 수 없습니다.";
        if (!AssetDatabase.IsValidFolder(BackupDir)) AssetDatabase.CreateFolder(ClipDir, "_Backup");

        string dst = BackupDir + "/" + Path.GetFileNameWithoutExtension(src) + "_backup.anim";
        if (File.Exists(dst)) return "백업 건너뜀(이미 존재): " + dst;

        return AssetDatabase.CopyAsset(src, dst) ? "백업: " + dst : "백업 실패: " + dst;
    }

    private static void Cleanup(GameObject instance)
    {
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        if (instance != null) Object.DestroyImmediate(instance);
    }

    private class Bones
    {
        public Transform Root, Pelvis, HandR, HandL, WeaponR, WeaponL;
    }

    private static Bones ResolveBones(GameObject instance)
    {
        Transform[] all = instance.GetComponentsInChildren<Transform>(true);
        var b = new Bones();
        for (int i = 0; i < all.Length; i++)
        {
            switch (all[i].name)
            {
                case "root": b.Root = all[i]; break;
                case "pelvis": b.Pelvis = all[i]; break;
                case "hand_r": b.HandR = all[i]; break;
                case "hand_l": b.HandL = all[i]; break;
                case "add_weapon_r": b.WeaponR = all[i]; break;
                case "add_weapon_l": b.WeaponL = all[i]; break;
            }
        }
        bool ok = b.Root != null && b.Pelvis != null && b.HandR != null && b.HandL != null && b.WeaponR != null && b.WeaponL != null;
        return ok ? b : null;
    }
}
