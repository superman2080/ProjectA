using System;
using PatternSpace;
using SliceSpace;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 패턴 <b>그룹</b> 하나를 내고, 통째로 성공할 때까지 기다린다. 튜토리얼의 드릴 한 번이다.
    ///
    /// <para><b>단위가 패턴 하나가 아니라 그룹인 이유</b>: 연속 공격(사슬)이 "넷을 이어 치고
    /// 하나라도 놓치면 넷을 처음부터"를 요구한다. 그룹이 1개짜리면 단일 패턴과 같아서 분기가 늘지 않는다.</para>
    ///
    /// <para><b>실패 경로에 호출이 하나도 없다.</b> <see cref="SliceTargetDirector"/>는 성패가
    /// <c>Pending</c>인 동안 표적을 건드리지 않으므로, <c>Resolve</c>를 안 부르면 다다미가 그 자리에 그대로 서 있다.
    /// "실패하면 잘리지 않는다"가 새 분기가 아니라 기존 분기다.</para>
    ///
    /// <para><b>플레이어를 직접 옮긴다.</b> 튜토리얼 씬에는 적이 없어 <c>PlayerCombatMover</c>가
    /// 한 프레임도 위치를 쓰지 않는다(그쪽 이동과 거리 커브가 전부 <c>OnDuelScheduled</c>에서 온다) —
    /// 그래서 위치의 주인이 둘이 되지 않는다(CLAUDE.md 11-2). 목적지는 표적 앵커에서 파생되므로
    /// <see cref="MoveToStep"/>처럼 좌표를 따로 저작하지 않는다.</para>
    ///
    /// <para><b>주의: 클래스 이름과 네임스페이스를 바꾸지 않는다.</b> <c>[SerializeReference]</c>가
    /// 그 이름으로 참조를 저장하므로 옮기면 저작해 둔 시퀀스가 <c>Managed Reference missing</c>이 된다.</para>
    /// </summary>
    [Serializable]
    public class PatternDrillStep : SequenceStep
    {
        // 토큰은 음수로 발급한다. EnemyDirector가 같은 씬에 살아 있어도 그쪽 토큰과 겹치지 않는다.
        private static int nextToken = -1;

        [Tooltip("이어 낼 패턴들. 1개면 단일 드릴, 여럿이면 연속 공격(사슬)이 된다.")]
        [SerializeField] private Pattern[] patterns = Array.Empty<Pattern>();

        [SequenceSlot]
        [Tooltip("패턴을 투입할 PatternHandler 슬롯.")]
        [SerializeField] private string patternHandlerSlot;

        [SequenceSlot]
        [Tooltip("표적을 예약할 SliceTargetDirector 슬롯. 비우면 표적 없이 입력만 익히는 드릴이다.")]
        [SerializeField] private string targetDirectorSlot;

        [SequenceSlot]
        [Tooltip("씬에 미리 서 있는 다다미 몸통 슬롯. 표적 위치와 플레이어 목적지를 함께 준다. 비우면 걷지도 베지도 않는다.")]
        [SerializeField] private string targetAnchorSlot;

        [Tooltip("갈라질 조각 세트. 비우면 표적이 서 있기만 한다.")]
        [SerializeField] private SliceSet target;

        [Tooltip("켜면 표적 앞까지 가기만 하고 끝난다. 도착한 뒤에 설명하고 싶을 때 쓴다 - " +
                 "뒤따르는 드릴이 같은 앵커를 가리키면 그쪽 걷기는 저절로 no-op이 된다.")]
        [SerializeField] private bool walkOnly;

        [Header("Approach")]
        [Tooltip("다다미에서 이만큼 떨어진 곳에 선다(미터).")]
        [Min(0.5f)]
        [SerializeField] private float standOffDistance = 2.5f;

        [Tooltip("이동 속도(m/s). 이름은 walk지만 runToTarget이 켜져 있으면 달리기 클립으로 간다 - " +
                 "저작해 둔 드릴들의 직렬화 값이 이 이름에 묶여 있어 바꾸지 않는다.")]
        [Min(0.1f)]
        [SerializeField] private float walkSpeed = 4f;

        [Tooltip("켜면 달리기 클립, 끄면 걷기 클립으로 이동한다. 클립 선택은 CharacterActionPlayer가 한다.")]
        [SerializeField] private bool runToTarget = true;

        [Tooltip("이 거리 안에 들면 도착으로 본다.")]
        [Min(0.01f)]
        [SerializeField] private float arriveRadius = 0.15f;

        [Min(0.01f)]
        [SerializeField] private float turnDuration = 0.15f;

        [Header("Timing")]
        [Tooltip("첫 노드까지의 여유(초). 링을 보고 준비할 시간.")]
        [Min(0.1f)]
        [SerializeField] private float leadTime = 1.5f;

        [Tooltip("노드 간격(초). 기본값은 1스테이지 곡(105 BPM)의 4분음표라, 곡이 시작될 때 손이 이미 그 격자를 알고 있다.")]
        [Min(0.1f)]
        [SerializeField] private float nodeInterval = 0.571f;

        [Tooltip("그룹 안 패턴 사이의 간격(초). 채보의 최소 입력 간격은 0.4초다.")]
        [Min(0.4f)]
        [SerializeField] private float patternGap = 0.6f;

        [Tooltip("연타 패턴의 창 길이(초).")]
        [Min(0.5f)]
        [SerializeField] private float mashWindow = 4f;

        [Tooltip("실패한 뒤 다시 낼 때까지의 간격(초).")]
        [Min(0f)]
        [SerializeField] private float retryDelay = 1f;

        private enum Phase { Walk, Drill, Done }

        [NonSerialized] private Phase phase;
        [NonSerialized] private PatternHandler handler;
        [NonSerialized] private SliceTargetDirector director;
        [NonSerialized] private Transform anchor;
        [NonSerialized] private Transform player;

        // 애니메이터는 CharacterActionPlayer만 안다 - 스테이트 이름을 여기서 복제하지 않는다.
        // 없으면 null인 채로 진행한다(배선이 비면 조용히 비활성되는 기존 규율).
        [NonSerialized] private CharacterActionPlayer actionPlayer;

        [NonSerialized] private Vector3 destination;
        [NonSerialized] private int token;
        [NonSerialized] private bool reserved;
        [NonSerialized] private bool subscribed;

        [NonSerialized] private int completed;
        [NonSerialized] private bool failed;
        [NonSerialized] private float retryAt;

        public string PatternHandlerSlot => patternHandlerSlot;
        public string TargetDirectorSlot => targetDirectorSlot;
        public string TargetAnchorSlot => targetAnchorSlot;

        public override void Enter(SequenceContext context)
        {
            phase = Phase.Walk;
            completed = 0;
            failed = false;
            reserved = false;
            subscribed = false;

            player = context.Player;
            actionPlayer = player != null ? player.GetComponentInChildren<CharacterActionPlayer>() : null;
            handler = context.Bindings.Resolve<PatternHandler>(patternHandlerSlot, context.Runner);

            director = string.IsNullOrEmpty(targetDirectorSlot)
                ? null
                : context.Bindings.Resolve<SliceTargetDirector>(targetDirectorSlot, context.Runner);

            anchor = string.IsNullOrEmpty(targetAnchorSlot)
                ? null
                : context.Bindings.ResolveTransform(targetAnchorSlot, context.Runner);

            // walkOnly면 패턴이 없는 것이 정상이다 - 이 스텝은 이동만 한다.
            if (!walkOnly && (handler == null || patterns == null || patterns.Length == 0))
            {
                Debug.LogError("[PatternDrillStep] PatternHandler나 패턴이 없습니다. 이 스텝을 건너뜁니다.", context.Runner);
                phase = Phase.Done;
                return;
            }

            if (!walkOnly && handler != null)
            {
                handler.OnPatternComplete += HandlePatternComplete;
                subscribed = true;
            }

            // 걸어갈 자리가 없으면 그 자리에서 바로 친다(입력만 익히는 드릴).
            if (anchor == null || player == null)
            {
                BeginDrill();
                return;
            }

            destination = ResolveStandPoint();
        }

        public override void Tick(SequenceContext context)
        {
            if (phase == Phase.Walk) TickWalk();
            else if (phase == Phase.Drill) TickDrill();
        }

        public override bool IsFinished(SequenceContext context) => phase == Phase.Done;

        public override void Exit(SequenceContext context)
        {
            if (subscribed && handler != null) handler.OnPatternComplete -= HandlePatternComplete;
            subscribed = false;

            // 중단(Stop/OnDisable)으로 빠져나온 경우 남은 큐를 걷어낸다. 정상 종료면 이미 비어 있다.
            if (phase != Phase.Done && handler != null) handler.ClearAllPatterns();

            handler = null;
            director = null;
            anchor = null;
            player = null;
            actionPlayer = null;
        }

        public override string Label
        {
            get
            {
                if (walkOnly) return $"WalkTo '{targetAnchorSlot}'";
                if (patterns == null || patterns.Length == 0) return "Drill (empty)";

                string first = patterns[0] != null ? patterns[0].name : "none";
                return patterns.Length == 1 ? $"Drill '{first}'" : $"Drill '{first}' 외 {patterns.Length - 1}개";
            }
        }

        // 접근 ────────────────────────────────────────────────────────────────

        /// <summary>다다미에서 현재 플레이어 쪽으로 <see cref="standOffDistance"/>만큼 떨어진 지점.</summary>
        private Vector3 ResolveStandPoint()
        {
            Vector3 away = Vector3.ProjectOnPlane(player.position - anchor.position, Vector3.up);

            // 겹쳐 서 있으면 방향이 정의되지 않는다. 다다미가 보는 쪽을 쓴다.
            if (away.sqrMagnitude <= 1e-4f) away = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
            if (away.sqrMagnitude <= 1e-4f) away = Vector3.back;

            Vector3 point = anchor.position + away.normalized * standOffDistance;
            point.y = player.position.y;
            return point;
        }

        private void TickWalk()
        {
            Vector3 toTarget = destination - player.position;
            float distance = toTarget.magnitude;

            if (distance > arriveRadius)
            {
                player.position += toTarget / distance * Mathf.Min(walkSpeed * Time.deltaTime, distance);
                FaceTowards(destination);

                // ⚠ 매 프레임 불러야 한다 - 이 메서드가 convergeUntil 래치를 갱신해 base 레이어의 주인을 쥔다.
                // 한 번만 부르면 0.1초 뒤 CharacterActionPlayer의 복귀 로직이 Idle로 덮는다.
                // ⚠ 반대로 도착한 뒤에는 부르지 않는다 - 호출이 멎으면 래치가 만료되며 저절로 Idle로 돌아가고,
                //    드릴 중에도 계속 쥐고 있으면 베기 뒤 복귀(Release·Idle) 경로와 싸운다.
                if (actionPlayer != null) actionPlayer.SetExploreLocomotion(walkSpeed, runToTarget);
                return;
            }

            FaceTowards(anchor.position);
            BeginDrill();
        }

        private void FaceTowards(Vector3 worldPoint)
        {
            Vector3 flat = Vector3.ProjectOnPlane(worldPoint - player.position, Vector3.up);
            if (flat.sqrMagnitude <= 1e-6f) return;

            player.rotation = Quaternion.Slerp(player.rotation, Quaternion.LookRotation(flat),
                                               Mathf.Clamp01(Time.deltaTime / turnDuration));
        }

        // 드릴 ──────────────────────────────────────────────────────────────────

        private void BeginDrill()
        {
            // 이동만 하는 스텝은 도착이 곧 끝이다. 표적도 안 건드린다 - 감추고 세우는 것은
            // 실제로 치는 드릴의 일이고, 그것이 이 앵커를 다시 받는다.
            if (walkOnly)
            {
                phase = Phase.Done;
                return;
            }

            phase = Phase.Drill;

            // 씬에 미리 서 있던 다다미를 감추고 같은 자리에 세트의 원본을 세운다.
            // 예약은 드릴당 한 번뿐이다 - 재시도해도 같은 표적을 계속 쓴다.
            if (anchor != null && director != null && target != null)
            {
                Vector3 spot = anchor.position;
                anchor.gameObject.SetActive(false);

                token = nextToken--;
                float now = Time.time;

                // 스폰과 임팩트가 같은 지점이라 표적은 이동하지 않고 그 자리에 선다.
                director.Reserve(token, target, now, now + 0.02f, Vector2.zero, spot, spot);
                reserved = true;
            }

            Feed();
        }

        private void TickDrill()
        {
            if (!failed) return;
            if (Time.time < retryAt) return;

            failed = false;
            completed = 0;
            handler.ClearAllPatterns();
            Feed();
        }

        /// <summary>그룹 전체를 한 프레임에 큐로 밀어 넣는다. 큐 겹침은 정상이고 판정 대상은 선두 하나다.</summary>
        private void Feed()
        {
            float cursor = leadTime;

            foreach (Pattern pattern in patterns)
            {
                if (pattern == null) continue;

                if (pattern.IsMash)
                {
                    // 연타는 노드의 나열이 아니라 창이다 - 시각이 시작과 끝 둘뿐이다.
                    handler.SetPattern(pattern, new[] { cursor, cursor + mashWindow });
                    cursor += mashWindow + patternGap;
                    continue;
                }

                int count = pattern.AllData.Count;
                var times = new float[count];
                for (int i = 0; i < count; i++) times[i] = cursor + i * nodeInterval;

                handler.SetPattern(pattern, times);
                cursor += (count - 1) * nodeInterval + patternGap;
            }
        }

        private void HandlePatternComplete(PatternCompletionInfo info)
        {
            if (phase != Phase.Drill) return;

            // 이미 실패한 그룹의 잔여 패턴이 뒤늦게 완료되는 것은 세지 않는다.
            if (failed) return;

            if (!info.AllCorrect)
            {
                // Resolve를 부르지 않는다 - 다다미는 잘리지 않고 그대로 서 있는다.
                // 큐 정리는 다음 Tick에서 한다(PatternHandler가 완료를 발행하는 도중이라 여기서 건드리지 않는다).
                failed = true;
                retryAt = Time.time + retryDelay;
                return;
            }

            completed++;
            if (completed < CountUsablePatterns()) return;

            if (reserved && director != null) director.Resolve(token, true);
            phase = Phase.Done;
        }

        private int CountUsablePatterns()
        {
            int usable = 0;

            foreach (Pattern pattern in patterns)
                if (pattern != null) usable++;

            return usable;
        }
    }
}
