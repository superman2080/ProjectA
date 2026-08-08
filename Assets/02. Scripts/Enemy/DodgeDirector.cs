using System.Collections.Generic;
using PatternSpace;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EnemySpace
{
    /// <summary>
    /// <b>플레이어 애니메이션 공백에 들어오는 기습과 회피</b>의 유일 관리 지점.
    ///
    /// <para>자리는 <c>CharacterActionPlayer.OnIdleWindow</c>가 알려 주는 구간 —
    /// 적에게 도달한 뒤부터 다음 액션 클립이 시작되기 전까지, 플레이어가 <b>서 있기만 하는 시간</b>이다.
    /// §11-6 무리 배치가 이동을 0에 가깝게 만들면서 이 구간이 커졌다.</para>
    ///
    /// <para><b>판정 파이프라인에 개입하지 않는다.</b> <c>PatternHandler</c>는 패턴 판정의 단일 소유자로 남고,
    /// 회피는 여기서 자기 시계로 판정한다. 공백 끝을 첫 노드로 잘라 두므로 두 입력이 겹치지 않는다.</para>
    ///
    /// <para><b>구조는 셋으로 갈린다</b>(docs/EnemyAmbushDodge):
    /// ① <b>사전 접근</b> — 이동을 창 밖(이전 패턴 시간)으로 뺀다. 기준점은 플레이어의 '갈 자리'다.
    /// ② <b>텔레그래프</b> — 적 클립이 먼저 시작해 "온다"를 알리고, 링은 늦게 떠서 "지금"만 맡는다.
    /// ③ <b>원호 회피</b> — 반경이 보존돼 결투 앵커가 상대에게서 벗어나지 않는다.</para>
    /// </summary>
    public class DodgeDirector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CharacterActionPlayer actionPlayer;
        [SerializeField] private EnemyDirector enemyDirector;
        [SerializeField] private PlayerCombatMover mover;
        [SerializeField] private DodgePromptView prompt;
        [Tooltip("오토퍼펙트 스위치의 출처. 비우면 자동 회피만 비활성된다.")]
        [SerializeField] private PatternHandler handler;
        [Tooltip("기습자를 구도에 담을 카메라 디렉터. 비우면 카메라만 손대지 않는다.")]
        [SerializeField] private global::CameraDirector cameraDirector;

        [Header("Toggle")]
        [Tooltip("끄면 이 층만 죽는다 — 나머지 연출은 그대로 돈다(기존 연출 토글 규율).")]
        [SerializeField] private bool dodgeEnabled = true;

        [Header("Clips")]
        [Tooltip("기습 클립의 진짜 출처는 EnemyDefinition.AmbushAttacks다. 이건 미배선 종류용 폴백이며,\n" +
                 "둘 다 비면 그 적은 기습 후보에서 빠진다(무연출 기습 = 회피할 대상이 없는 프롬프트).")]
        [SerializeField] private ClipAlignment[] fallbackAmbushClips;

        [Tooltip("회피 성공 시 왼쪽으로 구르는 클립.")]
        [SerializeField] private AnimationClip dodgeClipLeft;
        [Tooltip("회피 성공 시 오른쪽으로 구르는 클립.")]
        [SerializeField] private AnimationClip dodgeClipRight;

        [Header("Budget")]
        [Tooltip("구르기 클립 길이(초). 실효 시간은 이 값 ÷ rollSpeed다.")]
        [SerializeField] private float rollDuration = 0.8f;
        [Tooltip("구르기 배속. 공백이 빠듯할 때 조이는 주 노브 — 구르기는 짧게 도는 게 오히려 날카롭다.")]
        [SerializeField] private float rollSpeed = 1.6f;
        [Tooltip("예산에 잡는 <b>최소</b> 이동 시간(초). 이동은 다음 스윙의 기하가 얼기 전에 끝나야 하므로\n" +
                 "이 값만 창에 요구한다 — 클립 전체를 요구하면 발동 구간이 반토막 난다.\n" +
                 "⚠ 실제 이동 시간은 창이 허락하는 만큼 늘어난다(최대 클립 길이 ÷ rollSpeed).\n" +
                 "그래야 코드 이동이 끝난 뒤 제자리에서 구르는 그림이 안 생긴다.")]
        [SerializeField] private float rollMoveDuration = 0.25f;
        [Tooltip("링이 수축하는 시간(초). ⚠ 적 클립 길이와 무관하다 — 적 모션이 '온다'를, 링이 '지금'을 맡는다.\n" +
                 "패턴 링과 같은 감각을 유지해야 학습한 속도가 깨지지 않는다.")]
        [SerializeField] private float ringExposure = 0.4f;
        [Tooltip("회피 판정 창(±초). 패턴 goodWindow(0.10)보다 관대하다 — 노드는 손가락, 회피는 온몸이다.")]
        [SerializeField] private float dodgeWindow = 0.15f;
        [Tooltip("공백 끝과의 여유(초).")]
        [SerializeField] private float margin = 0.2f;
        [Tooltip("이보다 짧은 공백에서는 아예 발동하지 않는다. 예산 계산과 별개인 하한선이다.\n" +
                 "실측(Dreamer_Lv10): 빈 구간 p50 0.54s · ≥0.8s가 45% · ≥1.0s가 25%.")]
        [SerializeField] private float minIdleWindow = 0.8f;
        [Tooltip("한 번 발동한 뒤 다음 발동까지의 최소 간격(초). 매 패턴 뜨면 그건 사건이 아니라 새 패턴이다.")]
        [SerializeField] private float cooldown = 6f;

        [Header("Placement")]
        [Tooltip("사전 접근 대기 거리(m). 결투 거리(≈1m)보다 확실히 커야 '현재 상대'로 오인되지 않는다.")]
        [SerializeField] private float stageDistance = 2.5f;
        [Tooltip("기습 순간 마저 좁혀 멈춰 서는 거리(m).")]
        [SerializeField] private float lungeDistance = 1.5f;
        [Tooltip("프롬프트를 띄울 적 기준 높이(m).")]
        [SerializeField] private float promptHeight = 1.7f;

        [Header("Roll")]
        [Tooltip("원호로 도는 각도(도). 좌우 중 '적이 없는 쪽'으로 돈다.")]
        [SerializeField] private float rollDegrees = 60f;

        [Header("Input")]
        [Tooltip("프롬프트 클릭 대신 쓸 키.")]
        [SerializeField] private Key dodgeKey = Key.Space;

#if UNITY_EDITOR
        [Header("Debug (Editor Only)")]
        [Tooltip("공백 구간의 길이와 발동 여부를 한 줄씩 찍는다. minIdleWindow 기본값의 근거를 뽑는 용도.")]
        [SerializeField] private bool logWindows = true;
#endif

        // ── 상태 ────────────────────────────────────────────────────────────
        /// <summary>사전 접근으로 붙여 둔 후보. 발동 여부는 공백이 열려야 정해진다.</summary>
        private EnemyView stagedAmbusher;

        private bool hasEvent;      // 이번 공백에 기습이 예약됐는가
        private bool fired;         // 적 클립을 걸었는가(텔레그래프 시작)
        private bool promptShown;   // 링이 떴는가 — 이 전의 입력은 무시한다
        private bool resolved;      // 성패가 정해졌는가

        private EnemyView ambusher;
        private ClipAlignment ambushClip;
        private ClipAlignment lastClip;   // 같은 모션이 연달아 나오지 않게
        private float fireTime;
        private float impactTime;
        private float windowEnd;    // 다음 액션 클립이 시작되는 시각. 구르기 이동이 여기 전에 끝나야 한다.
        private float cooldownUntil;
        private float cameraClearAt;      // 구르기·피격이 끝나 기습자를 구도에서 뺄 시각

        // 로그용 스냅샷. 구르기를 건 쪽이 알고 로그를 찍는 쪽은 모르는 값이라 여기로 넘긴다.
        private float lastRollMove;
        private bool lastRollRight;

        private readonly List<ClipAlignment> clipScratch = new List<ClipAlignment>();

        private float RollTime => rollDuration / Mathf.Max(rollSpeed, 0.01f);

        void OnEnable()
        {
            if (actionPlayer != null) actionPlayer.OnIdleWindow += HandleIdleWindow;
            if (enemyDirector != null) enemyDirector.OnDuelScheduled += HandleDuelScheduled;
            if (prompt != null) prompt.OnPressed += HandlePressed;
            if (handler != null) handler.OnAllPatternsCleared += HandleAllCleared;
        }

        void OnDisable()
        {
            if (actionPlayer != null) actionPlayer.OnIdleWindow -= HandleIdleWindow;
            if (enemyDirector != null) enemyDirector.OnDuelScheduled -= HandleDuelScheduled;
            if (prompt != null) prompt.OnPressed -= HandlePressed;
            if (handler != null) handler.OnAllPatternsCleared -= HandleAllCleared;

            Abort();
        }

        // ── ① 사전 접근 ──────────────────────────────────────────────────────

        /// <summary>
        /// 기습 후보를 <b>플레이어가 갈 자리</b> 옆에 미리 붙인다. 마감은 플레이어와 <b>같은 도착 시각</b>이다.
        ///
        /// <para><b>⚠ 기준점이 <c>plan.PlayerPosition</c>인 것이 핵심이다.</b> 플레이어의 현재 위치로 붙이면
        /// 플레이어가 <c>cruiseSpeed × 창</c>만큼 대시할 때 <b>적만 뒤에 남고</b>, 이동이 공백 안으로 되돌아온다.
        /// 그러면 예산이 0.67초 늘어 이 기능이 거의 안 뜬다.</para>
        ///
        /// <para>둘 다 그냥 이동이라(클립 정렬이 없다) <b>진짜로 병렬</b>이다 —
        /// "클립 시작 전에 도착"이라는 직렬 제약은 공격 배정에만 걸린다.</para>
        ///
        /// <para>붙여 놓고 발동을 안 해도 손해가 없다. 그냥 배회로 돌아간다(§11-6: 보이는 이동은 아무 일도 아니다).</para>
        /// </summary>
        private void HandleDuelScheduled(EnemyDirector.DuelPlan plan)
        {
            if (!dodgeEnabled || enemyDirector == null) return;
            if (hasEvent) return;                       // 진행 중인 기습이 있으면 새로 붙이지 않는다
            if (Time.time < cooldownUntil) return;

            if (stagedAmbusher == null
                || stagedAmbusher == enemyDirector.CurrentOpponent
                || !stagedAmbusher.gameObject.activeInHierarchy)
            {
                stagedAmbusher = enemyDirector.PickIdleAmbusher(enemyDirector.BuildVisibilityTest(), HasAmbushClip);
            }

            if (stagedAmbusher == null) return;

            Vector3 target = PlaceAround(plan.PlayerPosition, stagedAmbusher.transform.position, stageDistance);

            stagedAmbusher.ScheduleFace(plan.PlayerPosition);
            stagedAmbusher.ScheduleMove(stagedAmbusher.transform.position, target, Time.time, plan.PlayerArriveTime);
        }

        /// <summary><paramref name="center"/> 주위, <paramref name="from"/> 쪽 방향으로 <paramref name="distance"/>만큼 떨어진 자리. 무대 안으로 자른다.</summary>
        private Vector3 PlaceAround(Vector3 center, Vector3 from, float distance)
        {
            Vector3 dir = Vector3.ProjectOnPlane(from - center, Vector3.up);
            dir = dir.sqrMagnitude < 1e-4f ? Vector3.forward : dir.normalized;

            Vector3 target = center + dir * distance;
            target.y = center.y;

            if (enemyDirector == null) return target;

            // 무대 밖으로 나가면 경계에 붙인다 — 무대를 아는 값은 디렉터가 연다.
            Vector3 fromArena = Vector3.ProjectOnPlane(target - enemyDirector.ArenaCenter, Vector3.up);
            if (fromArena.magnitude > enemyDirector.StageRadius)
            {
                target = enemyDirector.ArenaCenter + fromArena.normalized * enemyDirector.StageRadius;
                target.y = center.y;
            }

            return target;
        }

        // ── ② 공백 접수 · 예약 ───────────────────────────────────────────────

        /// <summary>
        /// 공백이 열렸다. 여기서 <b>이번 공백에 기습을 넣을지</b>를 정한다.
        ///
        /// <para><b>랜덤은 예산 뒤가 아니라 예산 안에서 뽑는다</b> — 클립마다 와인드업이 달라 필요시간도 다르므로,
        /// 먼저 뽑고 안 맞으면 포기하면 긴 클립을 뽑을 때마다 이벤트가 통째로 사라진다.
        /// 들어가는 클립만 추린 뒤 그중에서 고른다.</para>
        /// </summary>
        private void HandleIdleWindow(float start, float end)
        {
            if (!dodgeEnabled || enemyDirector == null || prompt == null) return;

            float from = Mathf.Max(start, Time.time);
            float window = end - from;

            string reason = null;

            if (hasEvent) reason = "진행 중";
            else if (Time.time < cooldownUntil) reason = "쿨다운";
            else if (window < minIdleWindow) reason = "창 부족";
            else if (enemyDirector.CurrentAttacker == Attacker.Enemy) reason = "적이 공격자";
            else if (stagedAmbusher == null || !stagedAmbusher.IsIdle) reason = "대기 후보 없음";
            else if (!IsVisible(stagedAmbusher)) reason = "화면 밖";

            ClipAlignment picked = null;
            if (reason == null)
            {
                picked = PickClipWithin(stagedAmbusher, window);
                if (picked == null) reason = "맞는 클립 없음";
            }

            LogWindow(window, reason);

            if (reason != null) return;

            ambusher = stagedAmbusher;
            ambushClip = picked;
            lastClip = picked;

            // 구르기 <b>이동</b>이 창 안에 끝나도록 임팩트를 역산한다(클립 뒷부분은 끊겨도 된다).
            // 적 클립은 자기 와인드업만큼 앞서 알아서 시작한다.
            impactTime = end - (Mathf.Max(rollMoveDuration, 0f) + margin);
            fireTime = from;
            windowEnd = end;

            hasEvent = true;
            fired = false;
            promptShown = false;
            resolved = false;
        }

        /// <summary>
        /// 남은 창 <paramref name="window"/>에 들어가는 클립 중 하나를 랜덤으로 고른다. 없으면 null.
        /// 후보가 둘 이상이면 <b>직전에 쓴 클립은 제외</b>한다 — 같은 모션이 연달아 나오면 랜덤으로 안 읽힌다.
        /// </summary>
        private ClipAlignment PickClipWithin(EnemyView view, float window)
        {
            clipScratch.Clear();

            foreach (var clip in ClipsFor(view))
            {
                if (clip == null || !clip.IsUsable) continue;
                if (RequiredTime(clip) > window) continue;

                clipScratch.Add(clip);
            }

            if (clipScratch.Count == 0) return null;
            if (clipScratch.Count > 1) clipScratch.Remove(lastClip);

            return clipScratch[Random.Range(0, clipScratch.Count)];
        }

        /// <summary>
        /// 이 클립으로 기습하려면 공백이 얼마나 필요한가. <b>클립마다 다르다</b> — 상수로 굳히면 안 된다.
        ///
        /// <para><b>⚠ 와인드업과 <see cref="ringExposure"/> 중 <u>큰 쪽</u>이다.</b> 둘은 같은 구간을 공유하지만
        /// 어느 쪽이 길지는 클립이 정한다 — 배속을 올려 와인드업이 링보다 짧아지면 <b>링이 적 모션보다 먼저 뜬다</b>.
        /// 그러면 "모션이 온다를, 링이 지금을 맡는다"는 분리가 뒤집힌다.</para>
        ///
        /// <para><b>⚠ 구르기 <i>클립</i>은 예산에 없다.</b> "피했다"는 임팩트에서 이미 성립하므로 뒷부분은
        /// 다음 공격 크로스페이드에 끊겨도 된다. 창 안에 들어가야 하는 것은 <b>자리를 옮기는 시간</b>
        /// (<see cref="rollMoveDuration"/>)뿐이다 — 그건 다음 스윙의 기하가 얼기 전에 끝나야 한다.</para>
        /// </summary>
        private float RequiredTime(ClipAlignment clip) =>
            Mathf.Max(clip.ResolvedImpactSpan / clip.Speed, ringExposure)
            + dodgeWindow + Mathf.Max(rollMoveDuration, 0f) + margin;

        /// <summary>이 적이 쓸 기습 클립 목록. <b>적 종류가 진짜 출처</b>이고, 비면 디렉터의 폴백이 대신한다.</summary>
        private ClipAlignment[] ClipsFor(EnemyView view)
        {
            var own = view != null && view.Definition != null ? view.Definition.AmbushAttacks : null;
            if (own != null && own.Length > 0) return own;

            return fallbackAmbushClips ?? System.Array.Empty<ClipAlignment>();
        }

        /// <summary>쓸 수 있는 기습 클립이 하나라도 있는가. 없는 적은 후보 단계에서 빠진다.</summary>
        private bool HasAmbushClip(EnemyView view)
        {
            foreach (var clip in ClipsFor(view))
            {
                if (clip != null && clip.IsUsable) return true;
            }

            return false;
        }

        private bool IsVisible(EnemyView view)
        {
            var test = enemyDirector.BuildVisibilityTest();
            return test == null || test(view.transform.position);
        }

        // ── ③ 발동 · 판정 ────────────────────────────────────────────────────

        void Update()
        {
            if (cameraClearAt > 0f && Time.time >= cameraClearAt)
            {
                cameraClearAt = 0f;
                cameraDirector?.SetAmbusher(null);
            }

            if (!hasEvent) return;

            if (ambusher == null) { Abort(); return; }

            if (!fired && Time.time >= fireTime) Fire();
            if (!fired) return;

            if (!promptShown && Time.time >= impactTime - ringExposure) ShowPrompt();
            if (!promptShown || resolved) return;

#if UNITY_EDITOR
            // 오토플레이는 회피도 자동이다 — 토글은 PatternHandler 하나뿐이다(§8).
            // 임팩트 그 순간이 곧 Perfect라 별도 계산이 없다.
            if (handler != null && handler.DebugAutoPerfect && Time.time >= impactTime)
            {
                Succeed("오토");
                return;
            }
#endif

            if (Keyboard.current != null && Keyboard.current[dodgeKey].wasPressedThisFrame)
                HandlePressed();

            if (!resolved && Time.time > impactTime + dodgeWindow) Fail();
        }

        /// <summary>
        /// 적 클립을 건다. <b>이 순간부터 텔레그래프가 보인다</b> — 링은 아직 안 뜬다.
        /// <c>ClipAlignment</c>가 시작 시각·배속을 임팩트에서 역산하므로 "먼저 보이는 것"은 공짜다.
        /// </summary>
        private void Fire()
        {
            fired = true;

            Vector3 player = mover != null ? mover.transform.position : transform.position;
            Vector3 lungeSpot = PlaceAround(player, ambusher.transform.position, lungeDistance);

            ambusher.AssignAttack(ambushClip, impactTime, lungeSpot, player, enemyDirector.MaxAttackSpeed);

            // "공격하는 적"이 확정된 순간이 여기다 — 사전 접근에서 담으면 발동 없이 끝날 후보까지 담겨
            // 매 패턴 화면이 넓어졌다 좁아졌다 한다.
            cameraDirector?.SetAmbusher(ambusher);
            cameraClearAt = 0f;
        }

        private void ShowPrompt()
        {
            promptShown = true;
            prompt.Show(ambusher.transform, Vector3.up * promptHeight, ringExposure);
        }

        /// <summary>
        /// 입력. <b>이른 입력은 무시한다</b>(실패로 치면 연타로 자멸한다) — 늦은 것만 실패다.
        /// 링이 뜨기 전 텔레그래프 구간의 입력도 무시다: 타이밍의 유일한 단서는 링이라는 계약이다.
        /// </summary>
        private void HandlePressed()
        {
            if (!hasEvent || !promptShown || resolved) return;
            if (Mathf.Abs(Time.time - impactTime) > dodgeWindow) return;

            Succeed("입력");
        }

        private void Succeed(string source)
        {
            resolved = true;
            prompt.Hide();

            RollAway();
            LogResult($"성공({source}) — 오차 {Time.time - impactTime:+0.000;-0.000}s · " +
                      $"이동 {lastRollMove:0.00}s {(lastRollRight ? "오른쪽" : "왼쪽")}");
            Finish();
        }

        private void Fail()
        {
            resolved = true;
            prompt.Hide();

            // 카메라 피격 큐·체력 감소는 OnPlayerHit 구독자가 이미 처리한다(새 배선 없음).
            actionPlayer?.PlayHitReaction();
            LogResult($"실패 — 피격(창 ±{dodgeWindow:0.00}s 안에 입력 없음)");
            Finish();
        }

        /// <summary>
        /// 원호로 굴러 피한다. <b>중심은 지금 교전 중인 적</b>(없으면 기습자)이라 <b>반경이 보존</b>되고,
        /// 그래서 결투 앵커(ImpactAnchor)가 상대에게서 벗어나지 않는다.
        ///
        /// <para>방향은 <b>구른 뒤 위치에서 가장 가까운 적까지의 거리가 큰 쪽</b> — "적이 없는 쪽"이다.
        /// 좌/우 클립은 부호가 아니라 <b>실제 변위를 플레이어의 오른쪽 축에 투영</b>해 고른다
        /// (부호와 화면상 방향의 대응은 중심 위치에 따라 뒤집힌다).</para>
        /// </summary>
        private void RollAway()
        {
            if (mover == null) return;

            var opponent = enemyDirector.CurrentOpponent;
            Vector3 center = opponent != null ? opponent.transform.position : ambusher.transform.position;

            Vector3 plus = mover.PredictArcEnd(center, rollDegrees);
            Vector3 minus = mover.PredictArcEnd(center, -rollDegrees);

            float signed = ScoreSpot(plus) >= ScoreSpot(minus) ? rollDegrees : -rollDegrees;
            Vector3 end = signed > 0f ? plus : minus;

            float move = ResolveMoveDuration();
            mover.RollArc(center, signed, move);

            bool toRight = Vector3.Dot(end - mover.transform.position, mover.transform.right) >= 0f;
            actionPlayer?.PlayOneShot(toRight ? dodgeClipRight : dodgeClipLeft, rollSpeed);

            lastRollMove = move;
            lastRollRight = toRight;
        }

        /// <summary>
        /// 실제로 원호를 도는 데 쓸 시간. <b>예산은 <see cref="rollMoveDuration"/>만 요구하고,
        /// 재생은 창이 허락하는 만큼 늘려 쓴다.</b>
        ///
        /// <para><b>왜 늘리나</b>: Apply Root Motion이 꺼져 있어 이동은 코드가 한다 —
        /// 이동이 클립보다 짧으면 <b>남은 절반을 제자리에서 구른다</b>. 클립 길이까지 늘리면 둘이 맞는다.</para>
        ///
        /// <para><b>왜 예산은 안 늘리나</b>: 클립 전체를 창에 요구하면 발동 가능 구간이 25% → 4%로 떨어진다.
        /// 짧은 창에서는 <see cref="rollMoveDuration"/>으로 줄어들 뿐 회피 자체는 그대로 성립한다.</para>
        ///
        /// <para>상한이 둘이다 — <b>클립 길이</b>(그보다 길면 다시 어긋난다)와 <b>다음 클립 시작 − margin</b>
        /// (스윙의 기하가 얼기 전에 자리가 확정돼 있어야 한다).</para>
        /// </summary>
        private float ResolveMoveDuration()
        {
            float floor = Mathf.Max(rollMoveDuration, 0.01f);
            float available = windowEnd - Time.time - margin;

            return Mathf.Min(RollTime, Mathf.Max(available, floor));
        }

        /// <summary>이 자리가 얼마나 안전한가 = 가장 가까운 적까지의 거리. 무대 밖이면 크게 감점한다.</summary>
        private float ScoreSpot(Vector3 spot)
        {
            float nearest = float.MaxValue;

            foreach (var view in enemyDirector.Ring)
            {
                if (view == null) continue;
                nearest = Mathf.Min(nearest, Vector3.ProjectOnPlane(view.transform.position - spot, Vector3.up).magnitude);
            }

            var opponent = enemyDirector.CurrentOpponent;
            if (opponent != null)
                nearest = Mathf.Min(nearest, Vector3.ProjectOnPlane(opponent.transform.position - spot, Vector3.up).magnitude);

            if (nearest == float.MaxValue) nearest = 0f;

            float outside = Vector3.ProjectOnPlane(spot - enemyDirector.ArenaCenter, Vector3.up).magnitude
                            - enemyDirector.StageRadius;

            return outside > 0f ? nearest - outside * 10f : nearest;
        }

        /// <summary>
        /// 성패와 무관한 뒷정리 — 적은 어느 쪽이든 찌르고 물러난다.
        /// <b>카메라는 구르기·피격이 끝난 뒤에 뺀다</b>(대상만 비우면 가중치가 감쇠로 빠져 컷이 안 생긴다).
        /// </summary>
        private void Finish()
        {
            enemyDirector.ReleaseAmbusher(ambusher);

            cameraClearAt = Time.time + RollTime;
            cooldownUntil = Time.time + cooldown;

            stagedAmbusher = null;
            ambusher = null;
            ambushClip = null;
            hasEvent = false;
            fired = false;
            promptShown = false;
            resolved = false;
        }

        /// <summary>곡 정리·비활성 — 진행 중이던 프롬프트와 예약을 회수한다(잔존물 규율).</summary>
        private void HandleAllCleared() => Abort();

        private void Abort()
        {
            if (prompt != null) prompt.Hide();
            cameraDirector?.SetAmbusher(null);

            // 클립을 이미 건 적은 예약을 들고 있다 — 놓아주지 않으면 그 자리에 굳는다.
            if (fired && ambusher != null && enemyDirector != null) enemyDirector.ReleaseAmbusher(ambusher);

            stagedAmbusher = null;
            ambusher = null;
            ambushClip = null;
            hasEvent = false;
            fired = false;
            promptShown = false;
            resolved = false;
            cameraClearAt = 0f;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogWindow(float window, string reason)
        {
#if UNITY_EDITOR
            if (!logWindows) return;

            Debug.Log($"[DodgeDirector] 공백 {window:0.00}s → {(reason == null ? "발동" : "무시(" + reason + ")")}", this);
#endif
        }

        /// <summary>성패 결과. <b>같은 토글을 쓴다</b> — 발동 로그와 결과 로그가 따로 켜지면 짝이 안 맞는 기록이 남는다.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogResult(string text)
        {
#if UNITY_EDITOR
            if (!logWindows) return;

            Debug.Log($"[DodgeDirector] 회피 {text} · 적 {(ambusher != null ? ambusher.name : "(없음)")}", this);
#endif
        }
    }
}
