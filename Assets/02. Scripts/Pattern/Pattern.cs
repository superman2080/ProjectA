using System;
using System.Collections.Generic;
using UnityEngine;

namespace PatternSpace
{
    public enum NodeType
    {
        Start,
        Progress,
        End
    }

    [Serializable]
    public class PatternData
    {
        [Range(0, 8)] public int index;
    }

    /// <summary>
    /// 패턴의 '모양 원본' 에셋. 진행 상태(입력 시각, 현재 위치 등)는 갖지 않는다 —
    /// 그것은 <see cref="ActivePattern"/>이 들고 있으며, 그래야 같은 템플릿을 쓰는 두 패턴이
    /// 동시에 살아 있어도 서로의 상태를 덮어쓰지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "Pattern", menuName = "Scriptable Objects/Pattern")]
    public class Pattern : ScriptableObject
    {
        [SerializeField] private PatternData[] patternDatas;

        [Header("Combat Clips (EnemyCombat)")]
        [Tooltip("이 패턴에서 누가 휘두르는가. Enemy = 적 공격을 패링 / Player = 무방비 적을 공격.\n" +
                 "이 값이 아래 클립 슬롯의 표시 여부를 정한다. 한 패턴은 한 역할만 갖는다 — " +
                 "양쪽이 필요하면 패턴 에셋을 복제하라.")]
        [SerializeField] private EnemySpace.Attacker attacker = EnemySpace.Attacker.Player;

        [Tooltip("적이 이 패턴으로 휘두르는 공격. Attacker.Enemy(패링)일 때만 재생된다. 비우면 무연출.")]
        [SerializeField] private ClipAlignment enemyAttack = new ClipAlignment();

        [Tooltip("적 공격을 받아치는 플레이어 패링. Attacker.Enemy일 때 재생된다. 비우면 무연출.")]
        [SerializeField] private ClipAlignment playerParry = new ClipAlignment();

        [Tooltip("무방비 적을 베는 플레이어 공격. Attacker.Player일 때 재생된다. 비우면 무연출.")]
        [SerializeField] private ClipAlignment playerAttack = new ClipAlignment();

        [Tooltip("위 공격/패링 클립 '앞에' 순서대로 재생될 클립들. 비우면 예전 동작(클립 하나).\n" +
                 "⚠ 정렬 앵커는 언제나 마지막 클립(playerAttack/playerParry) 하나다 — 여기 원소에는 ImpactTime을 찍지 말 것.\n" +
                 "리드인 구간의 히트스톱은 그 원소의 extraImpactTimes에 찍는다.\n" +
                 "창이 모자라면 시퀀스 전체가 같은 비율로 배속된다(원소가 잘리지 않는다).")]
        [SerializeField] private List<ClipAlignment> playerLeadInClips = new List<ClipAlignment>();

        [Tooltip("표적이 된 순간부터 임팩트까지 적이 하는 동작(견제). Attacker.Player일 때만 재생된다. 비우면 기본 Idle.\n" +
                 "⚠ ImpactTime을 찍지 말 것 — 트림 끝이 임팩트에 붙는 것이 기본 동작이다.\n" +
                 "⚠ 트림 0.8초 이하 권장. 실측상 그 길이가 배속 없이 들어가는 비율이 97%다(docs/EnemyFeint).")]
        [SerializeField] private ClipAlignment enemyFeint = new ClipAlignment();

        [Tooltip("적이 죽는 클립. 임팩트 프레임이 플레이어 공격 임팩트와 같은 시각에 오도록 배속을 역산해 재생한다.\n" +
                 "절단(시체 교체·폭발)은 이 클립의 트림 끝에 일어난다. 비우면 임팩트에 바로 갈라진다.\n" +
                 "⚠ ImpactTime은 트림 시작 근처에 찍을 것 — 배속이 클립 전체에 걸려 쓰러지는 속도까지 빨라진다.")]
        [SerializeField] private ClipAlignment enemyDeath = new ClipAlignment();

        [Tooltip("이 패턴에 맞은 적의 리액션. 성공했으나 처치되지 않을 때(사슬 중간 타격) 재생된다. Attacker.Player 전용.\n" +
                 "비우면 EnemyView의 knockBack 스테이트로 폴백한다.\n" +
                 "⚠ 트림 0.5초 이하 권장 — 임팩트에 정렬되므로 길면 다음 패턴의 견제 클립이 끊는다.")]
        [SerializeField] private ClipAlignment enemyHit = new ClipAlignment();

        [Tooltip("이 패턴을 막아낸 적의 리액션(패링). 실패하고 물러나지 않을 때 재생된다.\n" +
                 "비우면 EnemyView의 parry 스테이트로 폴백한다. 회피(물러남)는 이 슬롯을 쓰지 않는다.")]
        [SerializeField] private ClipAlignment enemyParry = new ClipAlignment();

        [Tooltip("판정 종료 시각(Deadline) 대비 ±초. 칼이 닿는 순간을 앞뒤로 민다.\n" +
                 "플레이어 칼 · 적 칼 · 시체 교체 · 투사체 · 카메라 큐가 전부 이 값 하나를 읽는다.")]
        [UnityEngine.Serialization.FormerlySerializedAs("sliceTargetImpactOffset")]
        [SerializeField] private float impactOffset;

        [Tooltip("결투 앵커 기준 ±m. 이 모션의 리치에 맞춘다. 0이면 씬 앵커 그대로.\n" +
                 "런타임 배치 · 합주 프리뷰 · 슬라이서 칼 평면 유도가 전부 이 값을 읽는다.")]
        [SerializeField] private float duelDistanceOffset;

        [Tooltip("패턴 진행 중 결투 간격(m). 키 시간 = 임팩트 기준 상대초(0 = 임팩트), 값 = 절대 간격.\n" +
                 "음수면 플레이어가 적을 지나쳐 뒤로 간다. 비우면 duelDistanceOffset 상수 경로 그대로.\n" +
                 "저작은 Tools/Animation Clip Trimmer. 구동 구간은 플레이어 클립 재생 구간이다.")]
        [SerializeField] private AnimationCurve duelDistanceCurve = new AnimationCurve();

        [Header("Mash (연타)")]
        [Tooltip("연타 패턴인가. 켜면 아무 노드나 눌러 타수를 채우는 구간이 된다(노드 순서를 보지 않는다).\n" +
                 "⚠ patternDatas는 정확히 1칸이어야 한다 — 그 자리가 게이지(포커스 링)가 앉는 곳이다.\n" +
                 "⚠ Attacker.Player 전용.")]
        [SerializeField] private bool isMash;

        [Tooltip("성공에 필요한 타수. 이 수까지는 입력마다 점수가 오르고, 그 이상은 애니메이션만 나온다.\n" +
                 "⚠ 창 전체가 아니라 '입력 마감'까지 안에 들어가야 한다 — 마감은 마무리 일격(playerAttack)의 와인드업만큼 앞당겨진다.")]
        [Min(1)]
        [SerializeField] private int mashTargetHits = 10;

        [Tooltip("타격마다 번갈아 쓰는 '베기 + 맞는 리액션' 쌍들. 각 클립은 ImpactTime '부터' 재생된다.\n" +
                 "⚠ 정렬 대상이 아니다 — 타격 시각은 플레이어가 정하므로 역산할 시각이 없다.\n" +
                 "비우면 무연출(아무 일도 안 일어난다). 저작은 Tools/Animation Clip Trimmer.")]
        [SerializeField] private List<MashStrike> mashStrikes = new List<MashStrike>();

        [Header("Slice")]
        [Tooltip("이 스윙이 만드는 절단면(canonical = 적 루트 로컬). 굽기 툴이 playerAttack의 임팩트 프레임에서\n" +
                 "유도해 기입한다 — 손으로 적지 않는다(Tools/Mesh Slice Baker의 '패턴 감사' 탭).\n" +
                 "런타임은 이 평면으로 적 정의의 절단 세트 중 가장 비슷한 것을 고른다. 비면 정의의 기본 세트.")]
        [SerializeField] private SliceSpace.SlicePlane bladePlane;

        [SerializeField, HideInInspector] private bool hasBladePlane;

        [Header("World Effects")]
        [Tooltip("이 패턴이 재생할 월드 이펙트들. 큐 하나가 '언제·어디에·어떤 조건에서'를 스스로 든다.\n" +
                 "개수 제한이 없으므로 칼날·플레이어·적·임팩트 지점에 각각 붙일 수 있다.\n" +
                 "저장은 Tools/Pattern Effect Tool로 한다.")]
        [SerializeField] private List<PatternEffectCue> effectCues = new List<PatternEffectCue>();

        public IReadOnlyList<PatternData> AllData => patternDatas;

        /// <summary>
        /// 이 패턴에서 누가 휘두르는가. <b>패턴의 성질이지 채보 순간의 성질이 아니다</b> —
        /// 획 모양이 스윙 모션을 정하고, 그 모션이 공격으로 읽히는지 패링으로 읽히는지를 정한다.
        /// 한 패턴은 한 역할만 가지며, 양쪽이 필요하면 에셋을 복제한다.
        /// </summary>
        public EnemySpace.Attacker Attacker => attacker;

        /// <summary>적이 이 패턴으로 휘두르는 공격(<c>Attacker.Enemy</c>일 때만).</summary>
        public ClipAlignment EnemyAttack => enemyAttack;

        /// <summary>적 공격을 받아치는 플레이어 패링(<c>Attacker.Enemy</c>).</summary>
        public ClipAlignment PlayerParry => playerParry;

        /// <summary>무방비 적을 베는 플레이어 공격(<c>Attacker.Player</c>).</summary>
        public ClipAlignment PlayerAttack => playerAttack;

        /// <summary>
        /// 활성 슬롯(<see cref="PlayerAttack"/> 또는 <see cref="PlayerParry"/>) <b>앞에</b> 순서대로 재생될 클립들.
        ///
        /// <para><b>슬롯을 리스트로 갈아엎지 않고 앞에 붙이는 이유</b>: 마지막 클립이 곧 정렬 앵커이고(§6),
        /// 칼 평면 유도(<c>Tools/Mesh Slice Baker</c>)·이펙트 프리뷰가 전부 그 슬롯을 본다. 앵커를 제자리에 두면
        /// <b>기존 데이터의 마이그레이션이 0이고 그 소비자들이 한 줄도 안 바뀐다</b>.</para>
        ///
        /// <para><b>리스트가 하나뿐인 이유</b>: "한 패턴은 한 역할만 갖는다"가 이미 참이므로
        /// (<see cref="Attacker"/> · <c>WarnUnusedSlots</c>) 리드인은 <b>그 패턴의 활성 슬롯 앞</b>으로 정의된다.
        /// 역할별로 리스트를 나누면 언제나 하나는 죽은 데이터다.</para>
        /// </summary>
        public IReadOnlyList<ClipAlignment> PlayerLeadInClips => playerLeadInClips;

        /// <summary>
        /// 연타 패턴인가. <b>노드의 나열이 아니라 타수를 세는 구간</b>이라 판정 규칙이 통째로 갈린다 —
        /// 어느 인덱스든 유효타이고, 정해진 시각이 없고, 위치가 밀리지 않는다.
        ///
        /// <para><b>목표 타수를 <see cref="AllData"/>로 표현할 수 없어서</b> 별도 필드가 됐다 —
        /// <see cref="OnValidate"/>가 중복 인덱스를 막고 노드는 9개뿐이라 20타를 20칸으로 적을 수 없다.
        /// 여기서 <c>patternDatas</c>는 <b>게이지가 앉을 자리 하나</b>만 뜻한다.</para>
        /// </summary>
        public bool IsMash => isMash;

        /// <summary>성공에 필요한 타수. 이 수까지가 점수 대상이고 그 이상은 연출만이다.</summary>
        public int MashTargetHits => Mathf.Max(mashTargetHits, 1);

        /// <summary>타격마다 번갈아 쓰는 '베기 + 리액션' 쌍들. 각 원소는 <c>ImpactTime</c>부터 재생된다(정렬 대상이 아니다).</summary>
        public IReadOnlyList<MashStrike> MashStrikes => mashStrikes;

        /// <summary>재생할 타격 클립이 하나라도 있는가. 없으면 연타가 무연출로 지나간다.</summary>
        public bool HasMashStrikes
        {
            get
            {
                if (mashStrikes == null) return false;

                for (int i = 0; i < mashStrikes.Count; i++)
                    if (mashStrikes[i] != null && mashStrikes[i].IsUsable) return true;

                return false;
            }
        }

        /// <summary>
        /// <paramref name="hitNumber"/>번째 타격(1부터)이 쓸 쌍. 리스트를 순환한다.
        ///
        /// <para><b>⚠ 커서를 들지 않는 것이 핵심이다.</b> 이 값을 보는 곳이 셋인데
        /// (플레이어 클립 · 적 리액션 · 월드 이펙트) 각자 커서를 돌리면 언젠가 어긋나
        /// <b>다른 모션에 다른 리액션이 붙는다</b>. 누적 타수에서 유도하면 셋이 같은 답을 볼 수밖에 없다.</para>
        ///
        /// <para>빈 원소를 건너뛰지 않는다 — 그러면 순환 위치가 데이터에 따라 달라져 위 보장이 깨진다.
        /// 빈 원소는 <b>그 타격이 무연출</b>이라는 뜻이고, <c>OnValidate</c>가 경고한다.</para>
        /// </summary>
        public MashStrike MashStrikeFor(int hitNumber)
        {
            if (mashStrikes == null || mashStrikes.Count == 0) return null;

            int index = (Mathf.Max(hitNumber, 1) - 1) % mashStrikes.Count;
            return mashStrikes[index];
        }

        /// <summary>
        /// 연타 입력이 <b>Deadline보다 얼마나 일찍 닫히는가</b>(초). 마무리 일격(<see cref="PlayerAttack"/>)의
        /// 와인드업과 같은 값이다.
        ///
        /// <para><b>⚠ 이 값이 없으면 마무리 일격이 매번 끊긴다.</b> 그 클립은 <c>임팩트 − 와인드업</c>에 시작하는데
        /// 초과 타격은 계속 하도록 권장되고(연출만 나온다), 타격 하나하나가 새 <c>PlaySlot</c>이라
        /// 마무리를 처음부터 다시 시작시킨다 → 와인드업이 완주하지 못한 채 임팩트가 도착해
        /// <b>칼이 지나가지 않았는데 몸이 갈라진다</b>.</para>
        ///
        /// <para><b>저작 필드를 만들지 않고 유도하는 이유</b>: 와인드업은 이미 <c>playerAttack</c>의
        /// 트림·임팩트·배속에 들어 있다. 손으로 한 번 더 적으면 두 값이 언젠가 갈라진다.
        /// 슬롯이 비면 0 — 즉 <b>마무리 일격이 없는 연타</b>가 분기가 아니라 데이터로 표현된다.</para>
        /// </summary>
        public float MashInputDeadlineLead => ClipSequence.AuthoredImpactSpan(playerAttack);

        /// <summary>재생할 리드인 원소가 하나라도 있는가. 없으면 예전의 단일 클립 경로와 완전히 같다.</summary>
        public bool HasLeadInClips
        {
            get
            {
                if (playerLeadInClips == null) return false;

                for (int i = 0; i < playerLeadInClips.Count; i++)
                    if (playerLeadInClips[i] != null && playerLeadInClips[i].IsUsable) return true;

                return false;
            }
        }

        /// <summary>
        /// 표적이 된 순간부터 임팩트까지 적이 하는 동작(<c>Attacker.Player</c>일 때만).
        ///
        /// <para><b>임팩트가 없는 슬롯이다.</b> 닿지 않는 동작이므로 <c>ImpactTime</c>을 찍지 않고,
        /// 그러면 <see cref="ClipAlignment"/>가 트림 끝을 임팩트로 폴백해 <b>클립 끝이 임팩트 시각에 붙는다</b>.</para>
        ///
        /// <para>비어 있으면 적은 기본 Idle로 서 있는다(예전 동작). 이 슬롯은 그 정지 구간을 메우기 위한 것이다.</para>
        /// </summary>
        public ClipAlignment EnemyFeint => enemyFeint;

        /// <summary>
        /// 적이 죽는 클립. <b>런타임에 재생된다</b> — 임팩트 프레임을 <c>Deadline + ImpactOffset</c>에 맞추고
        /// <b>트림 끝에서 절단</b>이 일어난다. 굽기 툴은 그 트림 끝 포즈로 절단 프록시를 굽는다.
        /// </summary>
        public ClipAlignment EnemyDeath => enemyDeath;

        /// <summary>
        /// 이 패턴에 <b>맞았는데 죽지 않은</b> 적의 리액션(사슬 중간 타격). <c>Attacker.Player</c> 전용.
        ///
        /// <para><b>패턴이 소유하는 이유는 공격 클립과 같다</b> — 획 모양이 스윙을 정하고, 그 스윙이
        /// 어느 방향으로 젖혀지는지를 정한다. 고정 스테이트 이름이면 가로베기든 내려베기든 같은 모션이 나온다.</para>
        ///
        /// <para>비어 있으면 <c>EnemyView</c>의 <c>knockBackStateName</c>으로 폴백한다(기존 동작).</para>
        /// </summary>
        public ClipAlignment EnemyHit => enemyHit;

        /// <summary>
        /// 이 패턴을 <b>막아낸</b> 적의 리액션(제자리 패링). 물러나는 회피는 이 슬롯을 쓰지 않는다 —
        /// 회피는 클립·후퇴 이동·무대 경계 클램프가 한 덩어리라 <see cref="ClipAlignment"/> 하나로 끝나지 않는다.
        ///
        /// <para>비어 있으면 <c>EnemyView</c>의 <c>parryStateName</c>으로 폴백한다(기존 동작).</para>
        /// </summary>
        public ClipAlignment EnemyParry => enemyParry;

        /// <summary>
        /// 임팩트 시각을 Deadline 기준으로 미는 값(초). <b>모든 임팩트의 공통 앵커 보정이다</b> —
        /// 플레이어 칼·적 칼·시체 교체·투사체·카메라 큐가 전부 이것 하나를 읽으므로,
        /// 여기 값을 바꾸면 그 다섯이 <b>같이</b> 움직인다(어긋날 수가 없다).
        /// </summary>
        public float ImpactOffset => impactOffset;

        /// <summary>
        /// 결투 앵커 기준 거리 보정(m). <b>절대 거리가 아니라 보정값이다</b> —
        /// 씬 앵커는 카메라 구도가 정하고, 여기서는 이 모션의 리치만 조정한다.
        /// 그래서 씬 튜닝과 패턴 저작이 서로를 깨뜨리지 않는다.
        /// </summary>
        public float DuelDistanceOffset => duelDistanceOffset;

        /// <summary>
        /// 패턴 진행 중 간격을 그리는 커브. 키 시간은 <b>임팩트 기준 상대초</b>(저작 배속 단위)이고
        /// 값은 <b>절대 간격(m)</b>이다. 비어 있으면 상수 경로(<see cref="DuelDistanceOffset"/>)를 쓴다.
        /// </summary>
        public AnimationCurve DuelDistanceCurve => duelDistanceCurve;

        /// <summary>이 패턴이 시간 함수로 간격을 그리는가. 비면 예전 상수 경로다.</summary>
        public bool HasDuelDistanceCurve => DuelGap.Has(duelDistanceCurve);

        /// <summary>
        /// 커브의 첫 키 시각(임팩트 기준 상대초). 커브가 없으면 0.
        /// <b>결투 배치의 기준 시각</b>이다 — 그 시각의 값으로 자리를 잡고 그 시각까지 도착한다.
        /// </summary>
        public float DuelCurveStartTime => DuelGap.StartTime(duelDistanceCurve);

        /// <summary>
        /// 커브의 마지막 키 시각(임팩트 기준 상대초). 커브가 없으면 0.
        ///
        /// <para><b>이 값이 곧 "이 패턴이 위치를 언제까지 소유하는가"다.</b> 임팩트 이후 키를 찍으면
        /// 다음 패턴의 결투 계획이 그 구간 <b>안에</b> 도착하므로(계획은 임팩트보다 먼저 온다),
        /// <c>PlayerCombatMover</c>가 이 값까지 인수인계를 미룬다 — 안 미루면 임팩트 이후 키가
        /// <b>한 번도 재생되지 않는 죽은 데이터</b>가 된다.</para>
        /// </summary>
        public float DuelCurveEndTime => DuelGap.EndTime(duelDistanceCurve);

        /// <summary>
        /// 이 패턴의 <paramref name="relTime"/>(임팩트 기준 상대초)에서 둘이 유지할 간격(m).
        /// <b>런타임과 툴 프리뷰가 같은 함수를 부른다</b> — 두 그림이 어긋날 코드가 존재하지 않는다.
        ///
        /// <para>커브 경로에는 하한 클램프가 없다 — <b>음수(적을 지나침)가 이 기능의 목적</b>이기 때문이다.
        /// 구간 밖은 <see cref="AnimationCurve.Evaluate"/>의 기본 Clamp가 끝 키 값으로 홀드한다.</para>
        /// </summary>
        public float DuelGapAt(float relTime, float baseDistance) =>
            DuelGap.At(duelDistanceCurve, relTime, baseDistance, duelDistanceOffset);

        /// <summary>
        /// 이 패턴이 재생할 월드 이펙트 큐들. <b>슬롯이 아니라 리스트인 이유</b>는
        /// 개수와 시점이 코드가 아니라 저장 단계에서 정해지기 때문이다(<see cref="PatternEffectCue"/>).
        /// </summary>
        /// <summary>
        /// 이 스윙이 만드는 절단면. <b>좌표계는 적 루트 로컬</b>(표준 결투 배치에서 유도한 canonical 평면)이라
        /// 적 종류를 넘나들며 비교할 수 있다 — 굽기에 쓰는 메쉬 로컬 평면과 사는 공간이 다르다.
        ///
        /// <para><b>패턴은 <see cref="SliceSpace.SliceSet"/>을 직접 참조하지 않는다.</b> 시체 프리팹 안에는
        /// 그 적의 스켈레톤 사본이 들어 있어 세트는 원리적으로 적 모델을 넘나들 수 없다 — 패턴이 세트를 들면
        /// "세트는 패턴이 고르고 죽는 적은 링에서 고른다"가 되어 <b>엉뚱한 몸이 갈라지는 상태가 표현 가능</b>해진다.
        /// 패턴이 말할 수 있는 것은 '어느 각도'까지이고, 세트의 소유자는 <c>EnemyDefinition</c>으로 남는다.</para>
        /// </summary>
        public SliceSpace.SlicePlane BladePlane => bladePlane;

        /// <summary>칼 평면이 유도돼 있는가. 없으면 런타임이 적 정의의 기본 세트로 폴백한다.</summary>
        public bool HasBladePlane => hasBladePlane;

#if UNITY_EDITOR
        /// <summary>굽기 툴 전용 기입 경로. 런타임 코드는 호출하지 않는다.</summary>
        public void EditorAssignBladePlane(SliceSpace.SlicePlane plane)
        {
            bladePlane = plane;
            hasBladePlane = true;
        }
#endif

        public IReadOnlyList<PatternEffectCue> EffectCues => effectCues;

        /// <summary>
        /// 이 패턴이 <b>자기 소리</b>를 들고 있는가. <c>EffectManager</c>의 공용 임팩트음
        /// (<c>SfxTrigger.PatternImpact</c>)이 겹치지 않게 물러나는 근거다 —
        /// 폴백은 "소리를 저작하지 않은 패턴"만 위한 것이다.
        ///
        /// <para><b>⚠ 큐의 타이밍이 <c>Impact</c>인지까지는 보지 않는다.</b> <c>PatternStart</c>에
        /// 칼 뽑는 소리만 저작해도 폴백이 물러난다 — <b>"소리를 직접 설계한 패턴"이라는 사실 하나</b>로
        /// 가르는 것이 규칙으로 단순하고, 임팩트음이 필요하면 그 큐를 만들면 된다.</para>
        /// </summary>
        public bool HasSfxCue
        {
            get
            {
                if (effectCues == null) return false;

                for (int i = 0; i < effectCues.Count; i++)
                    if (effectCues[i] != null && effectCues[i].Sfx != null) return true;

                return false;
            }
        }

        public NodeType GetNodeType(int position)
        {
            if (position == 0) return NodeType.Start;
            if (position == patternDatas.Length - 1) return NodeType.End;
            return NodeType.Progress;
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            enemyAttack?.ValidateImpactTime(this, "EnemyAttack");
            playerParry?.ValidateImpactTime(this, "PlayerParry");
            playerAttack?.ValidateImpactTime(this, "PlayerAttack");
            enemyHit?.ValidateImpactTime(this, "EnemyHit");
            enemyParry?.ValidateImpactTime(this, "EnemyParry");
            // enemyDeath는 트림 구간 검증만 한다. 선딜 제약은 없다 —
            // 굽기 포즈가 런타임 정합성 요구가 아니라 저작 보조이기 때문(Plan_HumanoidSlice 결정 1-1).
            enemyDeath?.ValidateImpactTime(this, "EnemyDeath");
            enemyFeint?.ValidateImpactTime(this, "EnemyFeint");
            ValidateLeadInClips();
            ValidateMash();
            WarnUnusedSlots();
            ValidateEffectCues();
#endif

            if (patternDatas == null) return;

            var seen = new HashSet<int>();
            foreach (var data in patternDatas)
            {
                if (!seen.Add(data.index))
                {
                    Debug.LogError($"[Pattern] '{name}'에 중복된 인덱스 {data.index}가 있습니다.", this);
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 리드인 리스트의 <b>배선 실수를 잡는 유일한 장치</b>. 셋을 본다 —
        /// 트림 구간 검증, <b>정렬 의미가 없는 곳에 찍힌 ImpactTime</b>, 그리고 앵커가 비어 있는 상태다.
        ///
        /// <para>정렬 앵커는 마지막 클립 하나뿐이므로(§6) 리드인 원소의 <c>ImpactTime</c>은 아무 일도 하지 않는다 —
        /// 저작자가 "여기서 칼이 닿는다"로 오해할 유일한 진입점이라 여기서 막는다.</para>
        /// </summary>
        private void ValidateLeadInClips()
        {
            if (playerLeadInClips == null || playerLeadInClips.Count == 0) return;

            bool enemyIsAttacker = attacker == EnemySpace.Attacker.Enemy;
            var anchor = enemyIsAttacker ? playerParry : playerAttack;
            string anchorLabel = enemyIsAttacker ? "PlayerParry" : "PlayerAttack";

            for (int i = 0; i < playerLeadInClips.Count; i++)
            {
                var element = playerLeadInClips[i];
                if (element == null || element.Clip == null) continue;

                element.ValidateImpactTime(this, $"LeadIn[{i}]");

                if (element.ImpactTime > 0f)
                {
                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 리드인 원소 {i}('{element.Clip.name}')에 ImpactTime이 찍혀 있습니다. " +
                        "정렬 앵커는 마지막 클립 하나뿐이라 이 값은 아무 일도 하지 않습니다 — " +
                        "이 구간에서 멈추고 싶다면 같은 원소의 extraImpactTimes에 찍으세요.", this);
                }
            }

            if (HasLeadInClips && (anchor == null || !anchor.IsUsable))
            {
                Debug.LogWarning(
                    $"[Pattern] '{name}'에 리드인 클립이 있는데 {anchorLabel}(정렬 앵커)이 비어 있습니다. " +
                    "앵커가 없으면 시퀀스 전체가 무연출입니다.", this);
            }
        }

        /// <summary>
        /// 연타 패턴의 <b>배선 실수를 잡는 유일한 장치</b>. 연타는 판정 규칙이 통째로 다른데
        /// 인스펙터에서는 체크박스 하나 차이라 <b>잘못된 조합이 조용히 만들어지기 쉽다</b>.
        /// </summary>
        private void ValidateMash()
        {
            if (!isMash) return;

            int nodeCount = patternDatas != null ? patternDatas.Length : 0;
            if (nodeCount != 1)
            {
                Debug.LogError(
                    $"[Pattern] 연타 '{name}'의 노드가 {nodeCount}개입니다. 정확히 1개여야 합니다 — " +
                    "그 자리가 게이지(포커스 링)가 앉는 곳이고, 목표 타수는 mashTargetHits가 따로 듭니다.", this);
            }

            if (attacker != EnemySpace.Attacker.Player)
            {
                Debug.LogWarning(
                    $"[Pattern] 연타 '{name}'의 attacker가 {attacker}입니다. 연타는 Attacker.Player 전용이라 " +
                    "플레이어가 패링 클립을 고르고 적이 휘두르는 그림이 됩니다.", this);
            }

            if (HasLeadInClips)
            {
                Debug.LogWarning(
                    $"[Pattern] 연타 '{name}'에 리드인 클립이 배선돼 있습니다. 타격 하나하나가 시퀀스를 끊으므로 " +
                    "리드인은 재생될 수 없습니다 — 비우세요.", this);
            }

            if (!HasMashStrikes)
            {
                Debug.LogWarning(
                    $"[Pattern] 연타 '{name}'에 타격 클립이 하나도 없습니다. 두들겨도 아무 모션이 안 나옵니다.", this);
            }

            if (mashStrikes != null)
            {
                for (int i = 0; i < mashStrikes.Count; i++)
                {
                    var strike = mashStrikes[i];
                    if (strike == null) continue;

                    strike.PlayerClip?.ValidateImpactTime(this, $"MashStrike[{i}].PlayerClip");
                    strike.EnemyReaction?.ValidateImpactTime(this, $"MashStrike[{i}].EnemyReaction");

                    // 순환은 빈 원소를 건너뛰지 않는다 — 그 타격은 통째로 무연출이 된다.
                    if (!strike.IsUsable)
                    {
                        Debug.LogWarning(
                            $"[Pattern] 연타 '{name}'의 타격 [{i}]에 플레이어 클립이 없습니다. " +
                            $"{mashStrikes.Count}타마다 한 번씩 아무 모션도 안 나옵니다 — 채우거나 원소를 지우세요.", this);
                    }
                }
            }

            // 연타 큐는 시각이 아니라 사건에 붙으므로 성패를 알 수 없다 — 결과 조건은 영영 안 뜬다.
            if (effectCues != null)
            {
                for (int i = 0; i < effectCues.Count; i++)
                {
                    var cue = effectCues[i];
                    if (cue == null || !cue.IsUsable) continue;
                    if (cue.Timing != EffectTiming.MashHit || !cue.NeedsOutcome) continue;

                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 이펙트 큐 {i}('{cue.Label}')는 타이밍이 MashHit인데 조건이 {cue.Condition}입니다. " +
                        "타격 순간에는 성패가 아직 안 정해져 재생되지 않습니다 — 조건을 Always로 두세요.", this);
                }
            }
        }

        /// <summary>
        /// 이 패턴의 역할에서 <b>절대 재생되지 않을 슬롯</b>에 클립이 배선돼 있으면 경고한다.
        /// 인스펙터가 그 슬롯을 숨기므로, 이관 후 남은 찌꺼기를 찾을 유일한 장치다.
        /// </summary>
        private void WarnUnusedSlots()
        {
            bool enemyIsAttacker = attacker == EnemySpace.Attacker.Enemy;

            Warn(enemyIsAttacker ? playerAttack : enemyAttack, enemyIsAttacker ? "PlayerAttack" : "EnemyAttack");
            Warn(enemyIsAttacker ? enemyDeath : playerParry, enemyIsAttacker ? "EnemyDeath" : "PlayerParry");

            // 견제는 Attacker.Player 전용이다 — 적이 공격자면 그 구간을 EnemyAttack이 이미 채운다.
            if (enemyIsAttacker) Warn(enemyFeint, "EnemyFeint");

            // 피격 리액션도 Attacker.Player 전용이다 — 적이 공격자인 패턴의 실패는 §11-2대로 언제나 물러나고,
            // 성공은 패링이라 밀려나는 쪽(knockBack)이 이미 자리를 잡고 있다.
            if (enemyIsAttacker) Warn(enemyHit, "EnemyHit");

            void Warn(ClipAlignment slot, string label)
            {
                if (slot?.Clip == null) return;

                Debug.LogWarning(
                    $"[Pattern] '{name}'의 attacker가 {attacker}인데 {label}에 클립 '{slot.Clip.name}'이 배선돼 있습니다. " +
                    "이 슬롯은 재생되지 않습니다 — 이관 찌꺼기라면 비우세요.", this);
            }
        }

        /// <summary>
        /// 이펙트 큐의 <b>배선 실수를 잡는 유일한 장치</b>. 셋을 본다 —
        /// 파티클이 없는 프리팹, 노드 범위를 벗어난 인덱스, 그리고 <b>재생될 수 없는 조건</b>이다.
        ///
        /// <para>결과 조건 큐가 <c>LastNode</c>보다 이른 시각에 걸리는 것은 런타임에 조용히 폐기되므로
        /// 여기서 잡지 않으면 "왜 안 뜨지"가 된다.</para>
        /// </summary>
        private void ValidateEffectCues()
        {
            if (effectCues == null) return;

            int nodeCount = patternDatas != null ? patternDatas.Length : 0;

            for (int i = 0; i < effectCues.Count; i++)
            {
                var cue = effectCues[i];
                if (cue == null || !cue.IsUsable) continue;

                // ⚠ IsUsable이 '소리만 있어도 true'라 Prefab이 null일 수 있다.
                // 소리 전용 큐에서는 프리팹이 없는 것이 정상 상태이므로 경고 대상이 아니다.
                if (cue.Prefab != null && cue.Prefab.GetComponentInChildren<ParticleSystem>(true) == null)
                {
                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 이펙트 큐 {i}('{cue.Label}') 프리팹에 ParticleSystem이 없습니다. " +
                        "아무것도 보이지 않습니다.", this);
                }

                if (cue.Timing == EffectTiming.Node && nodeCount > 0 && cue.NodeIndex >= nodeCount)
                {
                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 이펙트 큐 {i}('{cue.Label}') 노드 인덱스가 {cue.NodeIndex}인데 " +
                        $"이 패턴의 노드는 {nodeCount}개입니다. 마지막 노드로 클램프됩니다.", this);
                }

                // 사건에 붙는 큐는 시각이 없다 — ResolveTime을 부르면 뜻 없는 숫자가 나온다(ValidateMash가 따로 본다).
                if (cue.IsEventDriven) continue;

                // 절대 시각이 아니라 기준점들의 '순서'만 보면 되므로 간격을 벌린 가짜 시각으로 판단한다.
                // (start 0 < first 1 < last 2 < impact 2.5) — 실제 채보에서도 이 순서는 불변이다.
                const float fakeStart = 0f, fakeFirst = 1f, fakeLast = 2f, fakeDeadline = 2.5f;
                float fired = cue.ResolveTime(fakeStart, fakeFirst, fakeLast, fakeDeadline, null, 0f);

                if (cue.NeedsOutcome && !cue.IsTimingValid(fired, fakeLast))
                {
                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 이펙트 큐 {i}('{cue.Label}')는 조건이 {cue.Condition}인데 " +
                        "성패가 정해지는 LastNode보다 이른 시각에 걸려 있습니다. 재생되지 않습니다.", this);
                }

                if (attacker == EnemySpace.Attacker.Enemy && cue.Condition == EffectCondition.Parry)
                {
                    Debug.LogWarning(
                        $"[Pattern] '{name}'의 attacker가 Enemy인데 이펙트 큐 {i}('{cue.Label}')의 조건이 Parry입니다. " +
                        "그 역할에서는 적이 막지 않으므로 재생되지 않습니다.", this);
                }
            }
        }
#endif
    }
}
