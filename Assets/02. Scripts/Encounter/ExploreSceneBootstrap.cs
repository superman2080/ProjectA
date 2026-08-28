using System.Collections;
using UnityEngine;

/// <summary>
/// 탐색 씬이 열릴 때 한 번. 떠난 자리로 플레이어를 되돌리고 페이드를 걷는다.
///
/// <para><b>⚠ 되돌린 자리는 대개 그 무대의 트리거 안이다.</b> 그대로 두면 복귀 즉시 같은 곡이
/// 다시 시작되는 무한 루프가 된다 — 그래서 그 자리를 <see cref="EncounterDirector.SuppressUntilExit"/>로
/// 잠그고, 플레이어가 한 번 벗어나야 다시 열린다. <b>이 플랜에서 가장 미묘한 지점이다.</b></para>
/// </summary>
public class ExploreSceneBootstrap : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private PlayerModeDirector modeDirector;
    [SerializeField] private EncounterDirector encounterDirector;

    [Tooltip("이 씬의 모든 Encounter. 복귀한 자리를 찾고 일렁임을 갱신한다.")]
    [SerializeField] private Encounter[] encounters;

    private IEnumerator Start()
    {
        // 씬의 첫 프레임이 새어 보이지 않게 먼저 덮는다(전투 씬에서 이미 덮인 채로 왔으면 무해하다).
        ScreenFader fader = ScreenFader.Instance;
        fader?.CoverInstantly();

        Encounter returned = RestoreReturnPosition();

        // 등급이 바뀌었을 수 있으므로 전부 다시 판단한다(Encounter.Start보다 뒤일 수도 있다).
        foreach (Encounter encounter in encounters)
        {
            if (encounter != null) encounter.ApplyShimmer();
        }

        if (returned != null) encounterDirector?.SuppressUntilExit(returned);

        modeDirector?.SetMode(PlayerMode.Explore);

        if (fader != null) yield return fader.FadeIn();
    }

    /// <summary>복귀 정보가 이 씬을 가리키면 플레이어를 그 자리에 세우고, 그 자리의 무대를 돌려준다.</summary>
    private Encounter RestoreReturnPosition()
    {
        GameSession session = GameSession.Instance;
        if (session == null || string.IsNullOrEmpty(session.ReturnScene)) return null;
        if (session.ReturnScene != gameObject.scene.name) return null;

        if (player != null)
        {
            player.SetPositionAndRotation(session.ReturnPosition, Quaternion.Euler(0f, session.ReturnYaw, 0f));
        }

        Encounter returned = FindEncounter(session.EncounterId);

        // 한 번 쓰면 비운다. 남겨 두면 이 씬을 다음에 열 때도 그 자리로 끌려간다.
        session.ReturnScene = null;

        return returned;
    }

    private Encounter FindEncounter(string encounterId)
    {
        if (string.IsNullOrEmpty(encounterId)) return null;

        foreach (Encounter encounter in encounters)
        {
            if (encounter != null && encounter.EncounterId == encounterId) return encounter;
        }

        return null;
    }
}
