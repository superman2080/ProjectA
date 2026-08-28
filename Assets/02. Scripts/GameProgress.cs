using ScoreSpace;
using UnityEngine;

/// <summary>
/// <b>씬을 넘어 남는 진행도의 유일 관리 지점.</b> 자리별 최고 등급과 해금 플래그 둘을 든다 —
/// 성질이 같아서(둘 다 "다음에 이 세계를 다시 열었을 때도 참이어야 하는 것") 소유자가 하나여야 한다.
///
/// <para><b>⚠ <c>PlayerPrefs</c>는 임시 백엔드다.</b> 저장 시스템은 나중에 따로 만든다.
/// 그래서 <b>읽고 쓰는 곳을 이 클래스 하나로 모은다</b> — 다른 클래스가 <c>PlayerPrefs</c>를
/// 직접 부르지 않으면 교체가 이 파일 안에서 끝난다.
/// ponytail: PlayerPrefs 단일 슬롯. 파편 수집 목록·엔딩 분기 이력처럼 구조체를 저장해야 하는 순간이
/// 곧 저장 시스템을 만들 때이고, 그때 이 세 메서드의 본문만 갈아끼운다.</para>
///
/// <para><b>⚠ <see cref="SequenceRunner"/>의 같은 이름 메서드와 다른 물건이다.</b>
/// 러너의 플래그는 <b>씬 한정 런타임 조건</b>(그 시퀀스가 도는 동안만 산다)이고,
/// 여기 플래그는 <b>영구 진행도</b>다. 전투 씬을 다녀오면 러너 쪽은 사라지고 이쪽은 남는다.</para>
///
/// <para><b>⚠ 자유 연주(FreePlay)의 결과는 여기 들어오지 않는다</b>(CLAUDE.md §9) —
/// 부르는 쪽(<c>BattleSceneBootstrap</c>)이 <c>EncounterId</c>가 비면 기록하지 않는다.
/// 들어오면 서사가 성적표의 부산물이 된다.</para>
/// </summary>
public static class GameProgress
{
    private const string GradeKeyPrefix = "Progress.Grade.";
    private const string FlagKeyPrefix = "Progress.Flag.";

    /// <summary>등급이 없다(= 한 번도 완곡하지 않았다)는 뜻의 값.</summary>
    private const int NoGrade = -1;

    /// <summary>이 자리의 최고 등급. 한 번도 완곡하지 않았으면 <c>false</c>를 돌려준다.</summary>
    public static bool TryGetGrade(string encounterId, out ScoreGrade grade)
    {
        grade = default;
        if (string.IsNullOrEmpty(encounterId)) return false;

        int stored = PlayerPrefs.GetInt(GradeKeyPrefix + encounterId, NoGrade);
        if (stored == NoGrade) return false;

        grade = (ScoreGrade)stored;
        return true;
    }

    /// <summary>
    /// 완곡 결과를 남긴다. <b>최고 기록으로만 갱신한다</b> —
    /// 다시 쳐서 못했다고 등급이 내려가면 재도전이 손해가 된다.
    /// </summary>
    public static void ReportGrade(string encounterId, ScoreGrade grade)
    {
        if (string.IsNullOrEmpty(encounterId)) return;

        if (TryGetGrade(encounterId, out ScoreGrade best) && best >= grade) return;

        PlayerPrefs.SetInt(GradeKeyPrefix + encounterId, (int)grade);
        PlayerPrefs.Save();
    }

    /// <summary>플래그가 서 있는가. <b>빈 이름은 언제나 참</b>이다 — 조건을 안 건 것과 같은 뜻이다.</summary>
    public static bool HasFlag(string flagName)
    {
        if (string.IsNullOrEmpty(flagName)) return true;
        return PlayerPrefs.GetInt(FlagKeyPrefix + flagName, 0) != 0;
    }

    /// <summary>플래그를 세운다. 해금(시퀀스가 부른다)과 완곡(전투 씬이 부른다)이 같은 이름 공간을 쓴다.</summary>
    public static void SetFlag(string flagName)
    {
        if (string.IsNullOrEmpty(flagName)) return;

        PlayerPrefs.SetInt(FlagKeyPrefix + flagName, 1);
        PlayerPrefs.Save();
    }

    /// <summary>전부 지운다. <b>디버그·새로 시작 전용</b>이다.</summary>
    public static void ResetAll()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
    }
}
