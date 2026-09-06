using System.Collections.Generic;
using PatternSpace;
using UnityEngine;
using UnityEngine.Serialization;

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
        // FormerlySerializedAs가 없으면 이름을 바꾸는 순간 씬 배선이 끊긴다(조용히 null이 된다).
        [FormerlySerializedAs("prompt")]
        [SerializeField] private DodgePointView dodgePoint;
        [Tooltip("오토퍼펙트 스위치의 출처. 비우면 자동 회피만 비활성된다.")]
        [SerializeField] private PatternHandler handler;
        [Tooltip("기습자를 구도에 담을 카메라 디렉터. 비우면 카메라만 손대지 않는다.")]
        [SerializeField] private global::CameraDirector cameraDirector;
        [Tooltip("회피 입력의 출처. 비우면 닷지 포인트 클릭으로만 회피한다.")]
        [SerializeField] private global::InputHandler inputHandler;

        [Header("Toggle")]
        [Tooltip("끄면 이 층만 죽는다 — 나머지 연출은 그대로 돈다(기존 연출 토글 규율).")]
        [SerializeField] private bool dodgeEnabled = true;

        [Tooltip("켜면 ArmNext()로 무장한 창에서만 발동한다. 튜토리얼처럼 지정한 패턴에서만 내보낼 때 쓴다. " +
                 "끄면(기본) 예전 그대로 조건이 맞는 창마다 발동한다 - 곡이 도는 씬은 이 값을 건드리지 않는다.")]
        [SerializeField] private bool requireArm;

        [Header("Clips")]
        [Tooltip("기습 클립의 진짜 출처는 EnemyDefinition.AmbushAttacks다. 이건 미배선 종류용 폴백이며,\n" +
                 "둘 다 비면 그 적은 기습 후보에서 빠진다(무연출 기습 = 회피할 대상이 없는 닷지 포인트).")]
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
        [Tooltip("링을 띄우는 <b>최소</b> 시간(초). 이보다 짧으면 사람이 못 읽는다 — 후보 자격(RequiredLead)도 이 값을 쓴다.\n" +
                 "⚠ max와 같은 값으로 두면 노출이 상수가 된다(현재 1.0). 그게 지금 의도다 —\n" +
                 "'경고 시간은 언제나 일정해야 한다'가 요구이고, 자격 검사가 lead >= 이 값을 보장하므로\n" +
                 "약속한 시간보다 짧게 보이는 경우가 구조적으로 안 생긴다.\n" +
                 "대가는 빈도다(lead가 모자란 구간은 아예 안 뜬다) — 그건 채보가 공백을 남겨 해결한다.")]
        [SerializeField] private float minRingExposure = 1f;
        [Tooltip("링을 띄우는 <b>최대</b> 시간(초). 실제 노출은 clamp(리드, min, max)다.\n" +
                 "⚠ 시작 크기는 상수다 — 노출이 길어지면 수축이 느려질 뿐이다. 크기를 키워 늘리면\n" +
                 "'크기 = 남은 시간'이라는 학습이 깨진다. 이 상한이 그 속도 편차를 묶는 노브다.\n" +
                 "현재 min과 같은 1.0 — 편차를 0으로 묶은 상태다.")]
        [SerializeField] private float maxRingExposure = 1f;
        [Tooltip("임팩트 <b>이후</b>의 유예(초). 입력 창은 링 등장부터 impactTime + 이 값까지 하나로 이어지며,\n" +
                 "그 안이면 언제 눌러도 성공이다(±판정 없음) — 노드는 손가락, 회피는 온몸이다.\n" +
                 "여기서는 창을 닫는 마감이자 minIdleWindow 예산의 항으로만 쓰인다.")]
        [SerializeField] private float dodgeWindow = 0.15f;
        [Tooltip("공백 끝과의 여유(초).")]
        [SerializeField] private float margin = 0.2f;
        [Tooltip("서 있는 구간에 요구하는 최소 길이(초) = dodgeWindow + rollMoveDuration + margin.\n" +
                 "⚠ 와인드업은 여기 안 들어간다 — 대시 구간에서 쓴다(정정 5).\n" +
                 "실측(Dreamer_Lv10): ≥0.6s가 83% · ≥0.8s가 25%.")]
        [SerializeField] private float minIdleWindow = 0.6f;
        [Tooltip("한 번 발동한 뒤 다음 발동까지의 최소 간격(초). 매 패턴 뜨면 그건 사건이 아니라 새 패턴이다.\n" +
                 "⚠ 6초에서 3초로 내렸다(실측 근거) — ringExposure를 1.0으로 고정하면서 '리드 >= 1.0' 자격이\n" +
                 "빈도를 훨씬 강하게 조이게 됐다(곡당 기회가 ~10번뿐). 그 위에 6초를 얹으면 기회의 1/3이 사라진다.\n" +
                 "'사건답게'를 지키는 일은 이제 쿨다운이 아니라 자격 검사가 한다.")]
        [SerializeField] private float cooldown = 3f;

        [Header("Placement")]
        [Tooltip("늦은 선정에서 도착 마감을 잡을 때 임팩트에서 빼는 여유(초) = 예상 와인드업.\n" +
                 "마감은 임팩트가 아니라 <b>클립 시작</b>이다 — 와인드업 동안에는 이미 서 있어야 칼이 어긋나지 않는다.\n" +
                 "그 시점엔 클립이 아직 안 골라졌으므로 실측 최대치(0.30초)를 보수적으로 쓴다.")]
        [SerializeField] private float lateStageWindup = 0.3f;

        [Tooltip("사전 접근 대기 거리(m). 결투 거리(≈1m)보다 확실히 커야 '현재 상대'로 오인되지 않는다.")]
        [SerializeField] private float stageDistance = 2.5f;
        [Tooltip("기습 순간 마저 좁혀 멈춰 서는 거리(m).")]
        [SerializeField] private float lungeDistance = 1.5f;

        [Header("Roll")]
        [Tooltip("원호로 도는 각도(도). 좌우 중 '적이 없는 쪽'으로 돈다.")]
        [SerializeField] private float rollDegrees = 60f;

#if UNITY_EDITOR
        [Header("Debug (Editor Only)")]
        [Tooltip("공백 구간의 길이와 발동 여부를 한 줄씩 찍는다. minIdleWindow 기본값의 근거를 뽑는 용도.")]
        [SerializeField] private bool logWindows = true;
#endif

        // ── 상태 ────────────────────────────────────────────────────────────
        /// <summary>사전 접근으로 붙여 둔 후보. 발동 여부는 공백이 열려야 정해진다.</summary>
        private EnemyView stagedAmbusher;

        /// <summary>
        /// 플레이어가 <b>갈 자리</b>(<c>DuelPlan.PlayerPosition</c>). 시전을 도착 전으로 당겼으므로
        /// 찌를 자리의 기준점을 현재 위치로 잡으면 <b>아직 출발도 안 한 자리를 찌른다</b>(정정 5 함정 1).
        /// </summary>
        private Vector3 stagedPlayerSpot;
        private bool hasStagedPlayerSpot;

        private bool hasEvent;      // 이번 공백에 기습이 예약됐는가
        private bool armed;         // requireArm일 때만 본다 — 텔레그래프가 실제로 시작되면 내려간다
        private bool fired;         // 적 클립을 걸었는가(텔레그래프 시작)
        private bool dodgePointShown;   // 링이 떴는가 — 이 전의 입력은 무시한다
        private bool resolved;      // 성패가 정해졌는가

        private EnemyView ambusher;
        private ClipAlignment ambushClip;
        private ClipAlignment lastClip;   // 같은 모션이 연달아 나오지 않게
        private float fireTime;
        private float impactTime;

        /// <summary>
        /// 이번 기습에서 링이 실제로 떠 있는 시간 = <c>clamp(리드, min, max)</c>.
        /// <b>리드마다 다르다</b> — 상수로 두면 리드가 길어도 0.4초만 보인다(정정 7 문제 1).
        /// </summary>
        private float ringDuration;
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
            // 회피 키는 여기가 모른다 — InputHandler가 게임플레이 입력의 유일한 출처이고,
            // 실제 바인딩은 IngameInputs의 Player/Dodge(<Keyboard>/space)에 있다.
            if (inputHandler != null) inputHandler.OnDodgePressed += HandlePressed;

            if (actionPlayer != null) actionPlayer.OnIdleWindow += HandleIdleWindow;
            if (enemyDirector != null) enemyDirector.OnDuelScheduled += HandleDuelScheduled;
            if (dodgePoint != null) dodgePoint.OnPressed += HandlePressed;
            if (handler != null) handler.OnAllPatternsCleared += HandleAllCleared;
        }

        void OnDisable()
        {
            if (actionPlayer != null) actionPlayer.OnIdleWindow -= HandleIdleWindow;
            if (enemyDirector != null) enemyDirector.OnDuelScheduled -= HandleDuelScheduled;
            if (dodgePoint != null) dodgePoint.OnPressed -= HandlePressed;
            if (handler != null) handler.OnAllPatternsCleared -= HandleAllCleared;

            if (inputHandler != null) inputHandler.OnDodgePressed -= HandlePressed;

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
        /// <summary>
        /// <b>다음 기습 하나를 무장한다.</b> <see cref="requireArm"/>가 켜져 있을 때만 뜻이 있다 —
        /// 그때는 이 호출 전까지 어떤 창도 기습을 만들지 않는다.
        ///
        /// <para><b>한 번 쓰고 마는 래치가 아니라 <u>실제로 발동할 때까지</u> 남는다.</b> 창이 열려도
        /// 후보가 없거나 못 따라오면 그 창은 그냥 지나가는데(그게 정상 경로다), 거기서 내려 버리면
        /// 지정한 자리에서 수업이 통째로 사라진다. 텔레그래프가 시작되는 순간(<see cref="Fire"/>)에 내린다.</para>
        ///
        /// <para>쿨다운도 같이 푼다 — "여기서는 반드시 낸다"가 요구인데 직전 기습의 쿨다운이 남아 있으면
        /// 그 요구가 조용히 어긋난다.</para>
        /// </summary>
        public void ArmNext()
        {
            armed = true;
            cooldownUntil = 0f;
        }

        private void HandleDuelScheduled(EnemyDirector.DuelPlan plan)
        {
            if (!dodgeEnabled || enemyDirector == null) return;
            if (requireArm && !armed) return;           // 무장 전에는 사전 접근도 안 한다(적이 헛되이 움직인다)
            if (hasEvent) return;                       // 진행 중인 기습이 있으면 새로 붙이지 않는다
            if (Time.time < cooldownUntil) return;

            if (stagedAmbusher == null
                || stagedAmbusher == enemyDirector.CurrentOpponent
                || !stagedAmbusher.gameObject.activeInHierarchy)
            {
                StageAmbusher(includeStaged: true);
            }

            if (stagedAmbusher == null) return;

            // 시전이 도착 전에 시작되므로 '찌를 자리'의 기준점도 여기서 받아 둔다(정정 5 함정 1).
            stagedPlayerSpot = plan.PlayerPosition;
            hasStagedPlayerSpot = true;

            Vector3 target = PlaceAround(plan.PlayerPosition, stagedAmbusher.transform.position, stageDistance);

            stagedAmbusher.ScheduleFace(plan.PlayerPosition);

            // ⚠ ScheduleMove가 아니라 ScheduleApproach다(정정 6 B) — 마감까지 끌면 moveEnd가 임팩트보다 뒤여서
            // "임팩트 때 자유로운가"가 언제나 실패한다. 실제 도착은 적 자기 moveSpeed가 정한다.
            stagedAmbusher.ScheduleApproach(target, plan.PlayerArriveTime);
        }

        /// <summary>
        /// 기습 후보를 하나 고른다. <b>스테이징의 유일한 지점</b> — 사전 접근(<see cref="HandleDuelScheduled"/>)과
        /// 늦은 선정(<see cref="HandleIdleWindow"/>) 양쪽이 같은 기준을 쓴다.
        ///
        /// <para><b>왜 두 번 시도하나</b>(실측): 예전에는 사전 접근이 유일한 지점이라 <b>패턴당 한 번</b>이었고,
        /// 그 순간 조건을 만족하는 적이 없으면 창이 통째로 날아갔다 — 0.2초 뒤에 만족해도 소용없었다.
        /// 실측에서 <b>자격을 갖춘 창 8번 중 5번이 "후보 없음"으로</b> 날아갔고, 그게 구조적이었다:
        /// 리드가 긴 창은 <b>처치 직후</b>에 생기는데(그때 새 상대를 고른다), 처치 직후는 §11-6의
        /// "사망 1 : 스폰 1"로 <b>새 적이 화면 밖에서 걸어 들어오는 중</b>이라 후보가 가장 적다.
        /// <b>시간이 가장 많은 창이 하필 후보가 가장 적은 순간이다.</b></para>
        /// </summary>
        /// <summary>
        /// 기습 후보를 하나 고른다. <b>두 진입점이 같은 기준을 쓰되 마감만 다르다.</b>
        ///
        /// <para><b>⚠ 늦은 선정도 <c>staged</c>를 본다</b>(실측 근거). 초안은 "<c>staged</c>는 멀다"며 통째로 막았는데
        /// <b>물어야 할 것은 거리가 아니라 도착 가능성</b>이었다 — 집결지는 창에 비례해 3~8m로 변하므로
        /// 거리로 뭉뚱그리면 <b>가까울 때까지 같이 버린다.</b> 실측에서 "후보 없음"으로 날아간 창이
        /// 전부 <c>active 1 · staged 3</c>이었다(바로 옆에 3명이 서 있는데 안 봤다).</para>
        /// </summary>
        /// <param name="arriveBy">
        /// 이 시각까지 닿을 수 있는 적만 후보로 본다. 음수면 검사하지 않는다(사전 접근 —
        /// 한 패턴 앞이라 이동 시간이 넉넉하고, 못 따라오면 <c>Fire()</c>의 <c>BusyReasonBy</c>가 거른다).
        /// </param>
        private void StageAmbusher(bool includeStaged, float arriveBy = -1f)
        {
            System.Func<EnemyView, bool> canReach = null;

            if (arriveBy > 0f)
            {
                Vector3 spot = mover != null ? mover.transform.position : transform.position;
                float budget = arriveBy - Time.time;

                canReach = view => view.TravelTime(spot) <= budget;
            }

            stagedAmbusher = enemyDirector.PickIdleAmbusher(
                enemyDirector.BuildVisibilityTest(), HasAmbushClip, includeStaged, canReach);
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
            if (!dodgeEnabled || enemyDirector == null || dodgePoint == null) return;
            if (requireArm && !armed) return;

            // ⚠ 숫자를 둘로 나눠 든다(정정 5) — 요구가 둘이고 시계가 서로 다르다.
            //  standing: 플레이어가 서 있는 구간. 판정과 구르기가 여기 들어가야 한다.
            //  lead    : 지금부터 임팩트까지. 와인드업·링은 여기 들어가면 되고 대시 구간을 먹어도 된다.
            float standing = end - Mathf.Max(start, Time.time);

            // 구르기 <b>이동</b>이 창 안에 끝나도록 임팩트를 역산한다(클립 뒷부분은 끊겨도 된다).
            // 후보 선별이 이 값을 쓰므로 먼저 계산한다.
            float impact = end - (Mathf.Max(rollMoveDuration, 0f) + margin);
            float lead = impact - Time.time;

            string reason = null;
            bool lateStaged = false;

            if (hasEvent) reason = "진행 중";
            else if (Time.time < cooldownUntil) reason = "쿨다운";
            else if (standing < minIdleWindow) reason = "창 부족";
            else if (enemyDirector.CurrentAttacker == Attacker.Enemy) reason = "적이 공격자";
            else
            {
                // ⚠ 늦은 선정. 앞의 값싼 게이트를 <b>통과한 뒤에만</b> 시도한다 —
                // 쿨다운·창 부족으로 어차피 버릴 창에서 후보를 잡으면 그 적이 헛되이 묶인다.
                // 사전 접근을 못 했어도 성립한다: 무리 반경이 2m라(§11-6) 후보는 이미 2~4m 안에 있고,
                // moveSpeed 3m/s면 0.7~1.0초에 붙는다(리드가 1.0초 이상인 창만 여기 온다).
                // 못 따라오면 바로 아래 BusyReasonBy(impact)가 거른다 — 새 안전장치가 없다.
                if (stagedAmbusher == null)
                {
                    // 마감은 <b>클립 시작</b>이지 임팩트가 아니다 — 와인드업 동안에는 이미 서 있어야 한다.
                    // 클립이 아직 안 골라졌으므로 실측 최대 와인드업(0.3초)을 보수적으로 뺀다.
                    StageAmbusher(includeStaged: true, arriveBy: impact - lateStageWindup);
                    lateStaged = stagedAmbusher != null;
                }

                // ⚠ 기준 시각이 <b>임팩트</b>다 — 요구는 "지금 노는가"가 아니라 "칼이 닿을 때 서 있는가"다(정정 5 함정 2).
                // 사전 접근 이동은 그보다 먼저 끝나므로 자기가 켠 moving에 자기가 걸리지 않는다(정정 4).
                // 사유는 뷰가 직접 낸다 — 5개 항을 뭉치면 로그에서 병목이 안 보인다.
                string stagedBusy = stagedAmbusher != null ? stagedAmbusher.BusyReasonBy(impact) : null;

                if (stagedAmbusher == null) reason = "후보 없음";
                else if (stagedBusy != null) reason = "후보 " + stagedBusy;
                else if (!IsVisible(stagedAmbusher)) reason = "화면 밖";
            }

            ClipAlignment picked = null;
            if (reason == null)
            {
                picked = PickClipWithin(stagedAmbusher, lead);
                if (picked == null) reason = "맞는 클립 없음";
            }

            LogWindow(standing, lead, start, impact, reason, lateStaged);

            if (reason != null) return;

            ambusher = stagedAmbusher;
            ambushClip = picked;
            lastClip = picked;

            impactTime = impact;

            // 링 노출을 리드에 맞춘다(정정 7). 짧은 리드는 통째로 덮고, 긴 리드는 상한에서 자른다 —
            // ⚠ 크기가 아니라 시간만 늘린다. 시작 크기를 키우면 "크기 = 남은 시간"이라는 학습이 깨진다.
            ringDuration = Mathf.Clamp(lead, Mathf.Max(minRingExposure, 0.01f), Mathf.Max(maxRingExposure, minRingExposure));

            // ⚠ 즉시 발동이다(정정 5) — start를 기다리면 와인드업이 창 안으로 되돌아온다.
            // 적 모션과 링은 플레이어가 대시 중이어도 성립한다(링은 ScreenSpaceOverlay라 이동과 무관하다).
            // 서 있어야 하는 것은 판정과 구르기뿐이고, 그건 impact가 창 끝에 붙어 있어 구조적으로 보장된다.
            fireTime = Time.time;
            windowEnd = end;

            hasEvent = true;
            fired = false;
            dodgePointShown = false;
            resolved = false;
        }

        /// <summary>
        /// 리드타임 <paramref name="lead"/>(지금 → 임팩트)에 들어가는 클립 중 하나를 랜덤으로 고른다. 없으면 null.
        /// 후보가 둘 이상이면 <b>직전에 쓴 클립은 제외</b>한다 — 같은 모션이 연달아 나오면 랜덤으로 안 읽힌다.
        /// </summary>
        private ClipAlignment PickClipWithin(EnemyView view, float lead)
        {
            clipScratch.Clear();

            foreach (var clip in ClipsFor(view))
            {
                if (clip == null || !clip.IsUsable) continue;
                if (RequiredLead(clip) > lead) continue;

                clipScratch.Add(clip);
            }

            if (clipScratch.Count == 0) return null;
            if (clipScratch.Count > 1) clipScratch.Remove(lastClip);

            return clipScratch[Random.Range(0, clipScratch.Count)];
        }

        /// <summary>
        /// 이 클립으로 기습하려면 <b>임팩트까지 몇 초가 남아 있어야 하는가</b>. <b>클립마다 다르다</b> — 상수로 굳히면 안 된다.
        ///
        /// <para><b>⚠ 와인드업과 <see cref="minRingExposure"/> 중 <u>큰 쪽</u>이다.</b> 둘은 같은 구간을 공유하지만
        /// 어느 쪽이 길지는 클립이 정한다 — 링은 최소 노출만큼은 떠 있어야 읽히고, 클립은 자기 와인드업만큼 필요하다.</para>
        ///
        /// <para><b>⚠ 판정·구르기·margin은 여기 없다</b>(정정 5). 그건 <b>서 있는 구간</b>에 걸리는 요구라
        /// <c>minIdleWindow</c>가 따로 본다. 리드타임은 <b>지금부터</b> 재는 값이라 대시 구간을 먹어도 되고,
        /// 임팩트가 창 끝에 붙어 있으므로 두 요구가 같은 시간을 두 번 세지 않는다.</para>
        /// </summary>
        private float RequiredLead(ClipAlignment clip) =>
            Mathf.Max(clip.ResolvedImpactSpan / clip.Speed, minRingExposure);

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

            if (!dodgePointShown && Time.time >= impactTime - ringDuration) ShowDodgePoint();
            if (!dodgePointShown || resolved) return;

#if UNITY_EDITOR
            // 오토플레이는 회피도 자동이다 — 토글은 PatternHandler 하나뿐이다(§8).
            // 임팩트 그 순간이 곧 Perfect라 별도 계산이 없다.
            if (handler != null && handler.DebugAutoPerfect && Time.time >= impactTime)
            {
                Succeed("오토");
                return;
            }
#endif

            if (!resolved && Time.time > impactTime + dodgeWindow) Fail();
        }

        /// <summary>
        /// 적 클립을 건다. <b>이 순간부터 텔레그래프가 보인다</b> — 링은 아직 안 뜬다.
        /// <c>ClipAlignment</c>가 시작 시각·배속을 임팩트에서 역산하므로 "먼저 보이는 것"은 공짜다.
        /// </summary>
        private void Fire()
        {
            // 사전 접근을 <b>못 따라올</b> 적은 여기서 걸러진다.
            // ⚠ 기준 시각이 impactTime이다(정정 5 함정 2) — 시전을 도착 전으로 당겼으므로 지금 이동 중인 것은
            // 정상이고, <b>임팩트 때</b> 아직 이동 중인 것이 비정상이다. IsIdle로 물으면 항상 실격된다.
            // Abort는 fired == false라 ReleaseAmbusher도 쿨다운도 걸지 않는다 — 다음 공백이 곧바로 다시 시도한다.
            string busy = ambusher.BusyReasonBy(impactTime);
            if (busy != null)
            {
                LogResult("발동 취소 — 기습자가 임팩트까지 " + busy);
                Abort();
                return;
            }

            fired = true;
            armed = false;   // 여기까지 와야 무장을 내린다 — 위 Abort는 fired == false라 다음 창이 다시 시도한다.

            // ⚠ 플레이어의 <b>갈 자리</b>다(정정 5 함정 1). 시전이 도착 전에 시작되므로 현재 위치로 잡으면
            // 아직 출발도 안 한 자리를 찌른다 — 정정 2 B와 같은 실수다.
            Vector3 player = hasStagedPlayerSpot ? stagedPlayerSpot
                           : mover != null ? mover.transform.position : transform.position;
            // ⚠ 통로(플레이어–현재 상대)를 비켜 선다. PlaceAround는 플레이어와 기습자 자신의 방위만 보는데,
            // 후보는 <b>현재 상대와 같은 무리</b>에서 고르므로(§11-6) 그 방위가 곧 상대 방향인 경우가 흔하다 —
            // 그대로 두면 둘 사이에 낀 채로 선다. 그리고 기습자는 hasPendingAttack이라
            // 매 프레임 이격이 건너뛰므로(설계상 면제) <b>여기서 피하지 않으면 아무도 못 치운다</b>.
            // ⚠ 연타처럼 임팩트가 먼 패턴에서는 그 자세가 수 초간 굳는다.
            Vector3 lungeSpot = enemyDirector.KeepOutOfDuelLane(
                PlaceAround(player, ambusher.transform.position, lungeDistance));

            ambusher.AssignAttack(ambushClip, impactTime, lungeSpot, player, enemyDirector.MaxAttackSpeed);

            // 텔레그래프가 실제로 링보다 먼저 보이는가(§4-2 A)를 로그로 확인할 수 있게 남긴다 —
            // 와인드업 < 링 노출이면 링이 먼저 뜬다 — 정정 7 이후로는 실격이 아니라 기록만 한다
            // (링이 전 구간을 덮으므로 타이밍 단서는 링이 전담한다).
            LogFire();

            // "공격하는 적"이 확정된 순간이 여기다 — 사전 접근에서 담으면 발동 없이 끝날 후보까지 담겨
            // 매 패턴 화면이 넓어졌다 좁아졌다 한다.
            cameraDirector?.SetAmbusher(ambusher);
            cameraClearAt = 0f;

            // 아웃라인과 소리도 같은 순간이다 — 여기가 곧 텔레그래프의 시작이고,
            // 셋(카메라·아웃라인·소리)이 한 사건의 세 채널이라 시각이 갈리면 안 된다.
            // 링은 아직 안 뜬다: 아웃라인이 "누가·어디서", 링이 "지금"을 맡는다.
            ambusher.SetHighlight(true);
            SfxManager.Instance?.Play(SfxTrigger.AmbushTelegraph);
        }

        /// <summary>
        /// 링을 띄운다. <b>자리는 넘기지 않는다</b> — 닷지 포인트는 화면 좌하단 고정이다.
        /// 기습자가 어디에 있는지는 아웃라인이 이미 말하고 있고, 링은 남은 시간만 맡는다.
        /// </summary>
        private void ShowDodgePoint()
        {
            dodgePointShown = true;
            dodgePoint.Show(ringDuration);
        }

        /// <summary>
        /// 입력. <b>이른 입력은 무시한다</b>(실패로 치면 연타로 자멸한다) — 늦은 것만 실패다.
        /// 링이 뜨기 전 텔레그래프 구간의 입력도 무시다: 타이밍의 유일한 단서는 링이라는 계약이다.
        /// </summary>
        /// <summary>
        /// 회피 입력. <b>링이 떠 있는 동안이면 언제 눌러도 성공</b>이다 — 창은 <c>dodgePointShown</c>부터
        /// <c>impactTime + dodgeWindow</c>(<see cref="Fail"/>가 닫는 시각)까지 하나로 이어진다.
        ///
        /// <para>±<c>dodgeWindow</c> 정밀 판정을 걷어냈다: 노드는 손가락이고 <b>회피는 온몸</b>이라
        /// 정밀도를 요구할 자리가 아니다. 게다가 링이 보이는데 눌렀을 때 아무 일도 안 일어나면
        /// 사람은 "이르다"가 아니라 <b>"안 먹혔다"</b>로 읽는다 — 링의 등장 자체가 곧 입력 허용 신호다.
        /// 회피 연출·이동 타이밍은 그대로다(입력 순간 <see cref="Succeed"/>).</para>
        /// </summary>
        private void HandlePressed()
        {
            if (!hasEvent || !dodgePointShown || resolved) return;

            Succeed("입력");
        }

        private void Succeed(string source)
        {
            resolved = true;
            dodgePoint.Hide();
            SfxManager.Instance?.Play(SfxTrigger.DodgeSuccess);

            RollAway();

            // 원호가 실제로 원호인가(Step 7)를 로그로 본다 — 중심까지의 거리가 구르기 전후로 유지돼야 한다.
            var center = enemyDirector.CurrentOpponent != null ? enemyDirector.CurrentOpponent : ambusher;
            float radius = center != null && mover != null
                ? Vector3.ProjectOnPlane(mover.transform.position - center.transform.position, Vector3.up).magnitude
                : 0f;

            LogResult($"성공({source}) — 오차 {Time.time - impactTime:+0.000;-0.000}s · " +
                      $"이동 {lastRollMove:0.00}s {(lastRollRight ? "오른쪽" : "왼쪽")} {rollDegrees:0}° · " +
                      $"중심 {(center != null ? center.name : "(없음)")} 반경 {radius:0.00}m · " +
                      $"창 끝까지 {windowEnd - Time.time:0.00}s");
            Finish();
        }

        private void Fail()
        {
            resolved = true;
            dodgePoint.Hide();
            SfxManager.Instance?.Play(SfxTrigger.DodgeFail);

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

            // ⚠ 착지점은 통로(플레이어–상대 캡슐) <b>밖</b> 원주 위라 매 프레임 이격에 안 걸린다.
            // ScoreSpot이 좌/우 중 빈 쪽을 고르지만 그건 점수일 뿐 보장이 아니다 — 여기서 한 번 비운다.
            // 도착 시각이 아니라 <b>출발 시각</b>인 것이 핵심이다: 원호를 도는 동안 적이 미리 비켜선다.
            // 현재 상대와 기습자는 PushOut이 이미 거른다(끝점 / hasPendingAttack).
            enemyDirector.ClearSpot(end);

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
            // 강조는 여기서 끈다 — 기습이 끝난 적은 더 이상 특별하지 않다.
            // 카메라와 달리 구르기를 기다리지 않는다: 아웃라인은 "지금 온다"의 표시라 사건이 끝나면 즉시 거짓이 된다.
            //
            // ⚠ '?.'가 아니라 '!= null'이다 — 널 조건 연산자는 진짜 C# null만 보므로
            //    파괴된 UnityEngine.Object를 통과시킨다(Abort의 같은 줄이 그래서 터졌다).
            if (ambusher != null) ambusher.SetHighlight(false);

            enemyDirector.ReleaseAmbusher(ambusher);

            cameraClearAt = Time.time + RollTime;
            cooldownUntil = Time.time + cooldown;

            stagedAmbusher = null;
            hasStagedPlayerSpot = false;   // 다음 결투 계획이 새로 준다 — 낡은 자리를 찌르지 않는다
            ambusher = null;
            ambushClip = null;
            hasEvent = false;
            fired = false;
            dodgePointShown = false;
            resolved = false;
        }

        /// <summary>곡 정리·비활성 — 진행 중이던 닷지 포인트와 예약을 회수한다(잔존물 규율).</summary>
        private void HandleAllCleared() => Abort();

        private void Abort()
        {
            if (dodgePoint != null) dodgePoint.Hide();
            if (cameraDirector != null) cameraDirector.SetAmbusher(null);

            // ⚠ 강조는 Finish와 Abort <b>양쪽</b>에서 끈다(ReleaseAmbusher가 두 곳에 있는 것과 같은 이유).
            // 곡 정리·비활성으로 여기 들어오면 켜진 채로 남고, 그 적이 풀에 반납되면 다음 대여가 빛난다.
            //
            // ⚠ 여기는 OnDisable에서도 불린다 — 플레이 종료 시 파괴 순서가 정해져 있지 않아
            //    적이 먼저 파괴돼 있을 수 있다. '?.'는 파괴된 UnityEngine.Object를 통과시켜
            //    MissingReferenceException이 났다. Unity의 '=='만이 파괴를 null로 본다.
            if (ambusher != null) ambusher.SetHighlight(false);

            // 클립을 이미 건 적은 예약을 들고 있다 — 놓아주지 않으면 그 자리에 굳는다.
            if (fired && ambusher != null && enemyDirector != null) enemyDirector.ReleaseAmbusher(ambusher);

            stagedAmbusher = null;
            hasStagedPlayerSpot = false;   // 다음 결투 계획이 새로 준다 — 낡은 자리를 찌르지 않는다
            ambusher = null;
            ambushClip = null;
            hasEvent = false;
            fired = false;
            dodgePointShown = false;
            resolved = false;
            cameraClearAt = 0f;
        }

        /// <summary>
        /// 공백 접수 결과. <b>두 숫자를 같이 찍는다</b>(정정 5) — 서 있는 구간과 리드타임은 요구가 다르고 시계가 달라서
        /// 하나만 보면 어느 쪽이 막았는지 추적이 안 된다.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogWindow(float standing, float lead, float start, float impact, string reason, bool lateStaged)
        {
#if UNITY_EDITOR
            if (!logWindows) return;

            // 후보의 이동 마감을 임팩트와 나란히 찍는다 — 사전 접근이 건 이동(마감 = 플레이어 도착)이라면
            // 임팩트보다 앞이어야 한다. 뒤면 다른 이동(집결·링 복귀·후퇴)이 자격을 막고 있다는 뜻이다.
            // 후보가 없으면 <b>왜</b> 없는지가 유일하게 알고 싶은 것이다 — 디렉터가 낸 탈락 내역을 그대로 싣는다.
            string who = $"(후보 없음 — {enemyDirector.LastAmbusherPick ?? "선정 안 함"})";
            if (stagedAmbusher != null)
            {
                float moveEnd = stagedAmbusher.MoveEndsAt;
                who = moveEnd < 0f
                    ? $"{stagedAmbusher.name} 정지"
                    : $"{stagedAmbusher.name} 이동마감 {moveEnd - Time.time:+0.00;-0.00}s (임팩트 {impact - Time.time:+0.00;-0.00}s)";
            }

            Debug.Log($"[DodgeDirector] 공백 {standing:0.00}s (리드 {lead:0.00}s · 도착까지 {start - Time.time:+0.00;-0.00}s) → " +
                      $"{(reason == null ? "발동" : "무시(" + reason + ")")}{(lateStaged ? " [늦은선정]" : "")} · {who}", this);
#endif
        }

        /// <summary>
        /// 기습 발동(적 클립을 건 순간). <b>같은 토글을 쓴다</b> — 공백·발동·결과 세 줄이 한 사건의 기록이라
        /// 따로 켜지면 짝이 안 맞는다.
        ///
        /// <para>와인드업과 <b>실제 링 노출</b>을 나란히 찍는다 — 노출이 리드마다 다르므로(정정 7)
        /// 로그에 없으면 "얼마나 보였나"를 사후에 알 수 없다.</para>
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogFire()
        {
#if UNITY_EDITOR
            if (!logWindows) return;

            // ⚠ 배속은 AssignAttack이 다시 역산한다(maxAttackSpeed 클램프 포함) — 예산이 쓴 authored 값이 아니라
            // 실제로 재생될 값으로 찍어야 로그와 화면이 같은 얘기를 한다.
            float speed = ambushClip.ResolvePlaySpeed(Time.time, impactTime, enemyDirector.MaxAttackSpeed, out bool clamped);
            float windup = ambushClip.ResolvedImpactSpan / speed;
            string order = windup >= ringDuration ? "모션 먼저" : "링이 먼저";

            Debug.Log($"[DodgeDirector] 기습 발동 — 적 {ambusher.name} · 클립 {ambushClip.Clip?.name} " +
                      $"(트림 {ambushClip.ResolvedDuration:0.00}s ×{speed:0.0}{(clamped ? " 클램프" : "")}) · " +
                      $"와인드업 {windup:0.00}s vs 링 {ringDuration:0.00}s ({order}) · " +
                      $"임팩트까지 {impactTime - Time.time:0.00}s · 창 끝까지 {windowEnd - Time.time:0.00}s", this);
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
