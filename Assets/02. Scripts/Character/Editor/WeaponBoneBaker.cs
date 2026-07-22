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

    private static readonly string[] PosProps = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
    private static readonly string[] RotProps = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };

    /// <summary>손 기준 무기 오프셋. 회전이 상수라는 것이 이 접근의 전제이므로 편차도 함께 들고 다닌다.</summary>
    public struct Grip
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float MaxPosDev;
        public float MaxRotDev;
    }

    // ─────────────────────────── 공개 API ───────────────────────────

    /// <summary>굽지 않고 그립 오프셋만 역산해 보고한다(전제 검증용).</summary>
    public static string Inspect(GameObject prefab, AnimationClip reference, bool katana, bool sheath, int fps)
    {
        GameObject instance = null;
        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Bones bones = ResolveBones(instance);
            if (bones == null) return BoneError;

            AnimationMode.StartAnimationMode();

            var sb = new StringBuilder();
            sb.AppendLine("참조 클립: " + reference.name + " (" + fps + "fps, 길이 " + reference.length.ToString("F3") + "초)");
            if (katana) sb.AppendLine(Describe("칼  hand_r ", ExtractGrip(instance, bones.HandR, bones.WeaponR, reference, fps)));
            if (sheath) sb.AppendLine(Describe("검집 hand_l", ExtractGrip(instance, bones.HandL, bones.WeaponL, reference, fps)));
            sb.AppendLine();
            sb.AppendLine("회전편차가 0에 가까우면 강체 그립 — 상수 오프셋으로 구워도 안전합니다.");
            return sb.ToString();
        }
        finally
        {
            Cleanup(instance);
        }
    }

    /// <summary>참조 클립의 그립을 대상 클립에 적용해 무기 본 커브를 생성한다.</summary>
    public static string Bake(GameObject prefab, AnimationClip reference, AnimationClip target,
        bool katana, bool sheath, int fps, bool backup)
    {
        if (!katana && !sheath) return "굽기 대상이 없습니다.";

        GameObject instance = null;
        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Bones bones = ResolveBones(instance);
            if (bones == null) return BoneError;

            AnimationMode.StartAnimationMode();

            Grip gripR = default(Grip), gripL = default(Grip);
            if (katana) gripR = ExtractGrip(instance, bones.HandR, bones.WeaponR, reference, fps);
            if (sheath) gripL = ExtractGrip(instance, bones.HandL, bones.WeaponL, reference, fps);

            int frames = Mathf.Max(2, Mathf.RoundToInt(target.length * fps));
            var times = new float[frames + 1];
            var posR = new Vector3[frames + 1]; var rotR = new Quaternion[frames + 1];
            var posL = new Vector3[frames + 1]; var rotL = new Quaternion[frames + 1];

            for (int i = 0; i <= frames; i++)
            {
                float t = target.length * i / frames;
                times[i] = t;
                SampleAt(instance, target, t);
                if (katana) ComposeLocal(bones.Root, bones.HandR, gripR, out posR[i], out rotR[i]);
                if (sheath) ComposeLocal(bones.Root, bones.HandL, gripL, out posL[i], out rotL[i]);
            }

            AnimationMode.StopAnimationMode();

            string backupNote = backup ? Backup(target) : "백업 생략";
            if (katana) WriteCurves(target, PathWeaponR, times, posR, rotR);
            if (sheath) WriteCurves(target, PathWeaponL, times, posL, rotL);

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.AppendLine("굽기 완료: " + target.name);
            sb.AppendLine("  " + (frames + 1) + "프레임 / " + fps + "fps / 길이 " + target.length.ToString("F3") + "초");
            if (katana) sb.AppendLine("  " + Describe("칼  ", gripR));
            if (sheath) sb.AppendLine("  " + Describe("검집", gripL));
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

    private const string BoneError = "본 탐색 실패 — root/hand_r/hand_l/add_weapon_r/add_weapon_l 중 없는 것이 있습니다.";

    private static string Describe(string label, Grip g)
    {
        return string.Format("[{0}] 오프셋 위치={1} 회전편차max={2:F2}도 위치편차max={3:F4}m",
            label, g.Position.ToString("F4"), g.MaxRotDev, g.MaxPosDev);
    }

    private static Grip ExtractGrip(GameObject instance, Transform hand, Transform weapon, AnimationClip clip, int fps)
    {
        int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * fps));
        var positions = new List<Vector3>(frames + 1);
        var rotations = new List<Quaternion>(frames + 1);

        for (int i = 0; i <= frames; i++)
        {
            SampleAt(instance, clip, clip.length * i / frames);
            positions.Add(hand.InverseTransformPoint(weapon.position));
            rotations.Add(Quaternion.Inverse(hand.rotation) * weapon.rotation);
        }

        Vector3 mean = Vector3.zero;
        for (int i = 0; i < positions.Count; i++) mean += positions[i];
        mean /= positions.Count;

        float maxPos = 0f;
        for (int i = 0; i < positions.Count; i++) maxPos = Mathf.Max(maxPos, (positions[i] - mean).magnitude);
        float maxRot = 0f;
        for (int i = 0; i < rotations.Count; i++) maxRot = Mathf.Max(maxRot, Quaternion.Angle(rotations[0], rotations[i]));

        return new Grip { Position = mean, Rotation = rotations[0], MaxPosDev = maxPos, MaxRotDev = maxRot };
    }

    /// <summary>손의 현재 자세에 그립 오프셋을 곱해 무기의 root 기준 로컬 TRS를 만든다.</summary>
    private static void ComposeLocal(Transform root, Transform hand, Grip grip, out Vector3 localPos, out Quaternion localRot)
    {
        Vector3 worldPos = hand.TransformPoint(grip.Position);
        Quaternion worldRot = hand.rotation * grip.Rotation;
        localPos = root.InverseTransformPoint(worldPos);
        localRot = Quaternion.Inverse(root.rotation) * worldRot;
    }

    private static void SampleAt(GameObject instance, AnimationClip clip, float time)
    {
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(instance, clip, time);
        AnimationMode.EndSampling();
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
        public Transform Root, HandR, HandL, WeaponR, WeaponL;
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
                case "hand_r": b.HandR = all[i]; break;
                case "hand_l": b.HandL = all[i]; break;
                case "add_weapon_r": b.WeaponR = all[i]; break;
                case "add_weapon_l": b.WeaponL = all[i]; break;
            }
        }
        bool ok = b.Root != null && b.HandR != null && b.HandL != null && b.WeaponR != null && b.WeaponL != null;
        return ok ? b : null;
    }
}
