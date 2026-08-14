using System.Collections.Generic;
using UnityEngine;

namespace EnemySpace
{
    /// <summary>
    /// 시체·파편·디졸브 회수와 조각 예산. 처치가 끝난 <b>뒤</b>의 뒷정리만 모았다.
    ///
    /// <para><b>클래스를 쪼갠 것이 아니라 파일만 쪼갰다</b>(<c>partial</c>) — 무리·예약·처치가 서로의 상태를
    /// 직접 읽으므로 진짜 분리는 성립하지 않는다. 얻는 것은 "한 파일을 스크롤하지 않는다" 하나다.</para>
    /// </summary>
    public partial class EnemyDirector
    {
        /// <summary>수명이 다했거나 조각이 전부 잠든 시체를 회수한다.</summary>
        private void RecycleCorpses()
        {
            for (int i = corpses.Count - 1; i >= 0; i--)
            {
                var corpse = corpses[i];
                if (corpse.view == null) { corpses.RemoveAt(i); continue; }

                bool expired = Time.time - corpse.time >= debrisLifetime;
                if (!expired && !corpse.view.AllPiecesSettled) continue;

                ReleaseCorpse(corpse);
                corpses.RemoveAt(i);
            }

            // 시체 수 상한 — 래그돌 비용은 조각 수가 아니라 시체(스켈레톤) 수에 비례한다.
            while (corpses.Count > maxActiveCorpses)
            {
                ReleaseCorpse(corpses[0]);
                corpses.RemoveAt(0);
            }
        }

        private void ReleaseCorpse(Corpse corpse)
        {
            if (corpse.view == null) return;

            corpse.view.ResetState(frozenMeshPool);
            pool.Release(corpse.view.gameObject);
        }

        private void RecycleDissolved()
        {
            for (int i = ring.Count - 1; i >= 0; i--)
            {
                var view = ring[i];
                if (view == null) { ring.RemoveAt(i); continue; }
                if (!view.DissolveFinished) continue;

                ring.RemoveAt(i);
                ForgetFromClusters(view);
                ReleaseEnemy(view);
            }

            if (currentOpponent != null && currentOpponent.DissolveFinished)
            {
                ForgetFromClusters(currentOpponent);
                ReleaseEnemy(currentOpponent);
                currentOpponent = null;
            }

        }

        private void RecycleDebris()
        {
            for (int i = debris.Count - 1; i >= 0; i--)
            {
                var d = debris[i];
                bool expired = Time.time - d.time >= debrisLifetime;
                if (!expired && !AllSettled(d)) continue;

                RecycleDebrisEntry(d);
                debris.RemoveAt(i);
            }
        }

        private static bool AllSettled(Debris d)
        {
            if (d.pieces == null || d.pieces.Count == 0) return true;

            foreach (var p in d.pieces)
            {
                if (p != null && !p.IsSettled) return false;
            }

            return true;
        }

        private void RecycleDebrisEntry(Debris d)
        {
            if (d.pieces != null)
            {
                foreach (var p in d.pieces)
                {
                    if (p == null) continue;
                    p.ResetState();
                    pool.Release(p.gameObject);
                }
            }

            if (d.view != null) ReleaseEnemy(d.view);
        }

        private void EnforcePieceBudget()
        {
            int total = 0;
            foreach (var d in debris) total += d.pieces != null ? d.pieces.Count : 0;

            for (int i = 0; i < debris.Count && total > maxActivePieces; i++)
            {
                var d = debris[i];
                total -= d.pieces != null ? d.pieces.Count : 0;
                RecycleDebrisEntry(d);
                debris.RemoveAt(i);
                i--;
            }
        }
    }
}
