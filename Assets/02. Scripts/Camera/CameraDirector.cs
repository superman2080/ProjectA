using System.Collections.Generic;
using PatternSpace;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 카메라 연출의 <b>유일한 관리 지점</b>. <see cref="PatternHandler"/>의 기존 확장 이벤트만 구독해
/// 카탈로그에서 큐를 골라 재생한다. 판정 파이프라인에는 개입하지 않는다(순수 연출) —
/// <c>EffectManager</c>·<c>SliceTargetDirector</c>와 같은 위치다.
///
/// <para><b>큐는 셋이고, 각각 화면에서 실제로 사건이 일어나는 순간에 맞춘다.</b>
/// 성공/실패(표적 파괴)는 <b>Deadline</b> — 칼날 임팩트 프레임·표적 절단과 같은 식이다.
/// 반면 피격은 <see cref="PatternHandler.OnJudgeTargetFirstMiss"/> 순간에 <b>즉시</b>다
/// (<c>CharacterActionPlayer</c>가 그때 Hit 클립을 바로 재생하므로). 실패는 사건이 둘이라 큐도 둘이다.</para>
///
/// <para><b>예약은 하나면 충분하다.</b> 패턴 완료는 순차적이고, A의 Deadline은 A 마지막 노드 +goodWindow(0.1초)인데
/// B의 완료는 A보다 최소 0.4초 뒤다 → 동시에 대기 중인 예약은 최대 하나다. 리스트를 두지 않는다.</para>
///
/// <para><b>겹침은 합성되지 않는다.</b> Perlin은 채널이 하나뿐이다. 그리고 마지막 노드에서 미스가 나면
/// <see cref="CameraTrigger.PatternMiss"/>와 <see cref="CameraTrigger.PatternFailure"/>가 0.1초 간격으로 확실히 붙는다.
/// 그래서 새 쉐이크는 타이머를 재시작하되 <b>진폭은 큰 쪽을 취한다</b> — 단순 덮어쓰기면 강한 쉐이크 도중
/// 약한 쉐이크가 들어와 세기가 뚝 떨어진다.</para>
///
/// <para><b>휴지값은 0이 아니다.</b> 씬의 Perlin에 상시 흔들림 값이 들어 있을 수 있어
/// <see cref="Awake"/>에서 현재 값을 캐시해 그리로 복귀한다.</para>
/// </summary>
public class CameraDirector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PatternHandler handler;

    [Tooltip("흔들 대상. 씬의 CinemachineCamera에 붙어 있는 노이즈 컴포넌트.")]
    [SerializeField] private CinemachineBasicMultiChannelPerlin perlin;

    [Header("Cue Catalog")]
    [Tooltip("트리거별 쉐이크 설정. 연출 추가 = 여기에 한 줄.")]
    [SerializeField] private List<CameraCueEntry> catalog = new List<CameraCueEntry>();

    [Tooltip("모든 큐가 공유하는 흔들림 주파수. 0.2초 남짓 쉐이크에서는 큐별로 나눌 만한 차이가 나지 않는다.")]
    [SerializeField] private float shakeFrequency = 1.6f;

    private readonly Dictionary<CameraTrigger, CameraCueEntry> catalogByTrigger =
        new Dictionary<CameraTrigger, CameraCueEntry>();

    // Perlin의 휴지값(씬에 설정된 상시 흔들림). 쉐이크가 끝나면 0이 아니라 여기로 되돌아간다.
    private float idleAmplitude;
    private float idleFrequency;

    // 진행 중인 쉐이크.
    private float shakeAmplitude;
    private float shakeStartTime;
    private float shakeDuration;

    // 대기 중인 예약(최대 하나). duration이 0 이하면 예약 없음.
    private bool hasPending;
    private float pendingFireTime;
    private CameraTrigger pendingTrigger;

    void Awake()
    {
        if (handler == null)
            Debug.LogError("[CameraDirector] handler가 배선되지 않았습니다 — 카메라 연출이 동작하지 않습니다.", this);

        if (perlin == null)
            Debug.LogError("[CameraDirector] perlin이 배선되지 않았습니다 — CinemachineCamera의 노이즈 컴포넌트를 넣으세요.", this);
        else
        {
            idleAmplitude = perlin.AmplitudeGain;
            idleFrequency = perlin.FrequencyGain;
        }

        foreach (var entry in catalog)
            catalogByTrigger[entry.trigger] = entry;
    }

    void OnEnable()
    {
        if (handler == null) return;
        handler.OnPatternComplete += HandlePatternComplete;
        handler.OnJudgeTargetFirstMiss += HandleFirstMiss;
        handler.OnAllPatternsCleared += HandleAllCleared;
    }

    void OnDisable()
    {
        if (handler != null)
        {
            handler.OnPatternComplete -= HandlePatternComplete;
            handler.OnJudgeTargetFirstMiss -= HandleFirstMiss;
            handler.OnAllPatternsCleared -= HandleAllCleared;
        }

        StopShake(); // 꺼진 채 흔들림이 남지 않도록.
    }

    // ── 이벤트 처리 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 성공/실패 큐를 <b>Deadline에 맞춰 예약</b>한다. 표적이 갈라지거나 부딪히는 바로 그 순간이다.
    /// 완주 성공은 Deadline 이전에, 만료 실패는 Deadline 직후에 이 이벤트가 오므로
    /// "시각이 이미 지났으면 즉시 발사"는 예외가 아니라 실패 경로의 정상 동작이다.
    /// </summary>
    private void HandlePatternComplete(PatternCompletionInfo info)
    {
        var trigger = info.AllCorrect ? CameraTrigger.PatternSuccess : CameraTrigger.PatternFailure;

        float offset = info.Pattern != null ? info.Pattern.SliceTargetImpactOffset : 0f;
        float fireTime = info.LastNodeTime + handler.GoodWindow + offset;

        if (Time.time >= fireTime)
        {
            PlayCue(trigger);
            return;
        }

        hasPending = true;
        pendingFireTime = fireTime;
        pendingTrigger = trigger;
    }

    /// <summary>피격 큐는 예약하지 않는다 — Hit 클립이 이 순간 즉시 재생되므로 같이 터뜨려야 동기가 맞는다.</summary>
    private void HandleFirstMiss() => PlayCue(CameraTrigger.PatternMiss);

    private void HandleAllCleared()
    {
        hasPending = false;
        StopShake();
    }

    // ── 루프 ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (hasPending && Time.time >= pendingFireTime)
        {
            hasPending = false;
            PlayCue(pendingTrigger);
        }

        UpdateShake();
    }

    private void UpdateShake()
    {
        if (shakeDuration <= 0f) return;

        float t = (Time.time - shakeStartTime) / shakeDuration;
        if (t >= 1f)
        {
            StopShake();
            return;
        }

        ApplyShake(shakeAmplitude * Decay(t), shakeFrequency * Decay(t));
    }

    /// <summary>1 → 0 감쇠. 제곱이라 끝에서 부드럽게 잦아든다.</summary>
    private static float Decay(float t)
    {
        float inv = 1f - t;
        return inv * inv;
    }

    // ── 큐 재생 ──────────────────────────────────────────────────────────────

    private void PlayCue(CameraTrigger trigger)
    {
        if (!catalogByTrigger.TryGetValue(trigger, out var cue)) return; // 카탈로그에 없으면 무연출
        if (cue.shakeDuration <= 0f) return;

        // 겹침: 진행 중인 쉐이크의 남은 진폭과 비교해 큰 쪽을 취한다(세기 낙차 방지).
        float remaining = 0f;
        if (shakeDuration > 0f)
        {
            float t = Mathf.Clamp01((Time.time - shakeStartTime) / shakeDuration);
            remaining = shakeAmplitude * Decay(t);
        }

        shakeAmplitude = Mathf.Max(remaining, cue.shakeAmplitude);
        shakeDuration = cue.shakeDuration;
        shakeStartTime = Time.time;
    }

    private void StopShake()
    {
        shakeDuration = 0f;
        shakeAmplitude = 0f;
        ApplyShake(0f, 0f);
    }

    /// <summary>
    /// 쉐이크를 실제로 카메라에 적용하는 <b>유일한 지점</b>. Cinemachine 타입이 등장하는 곳도 여기뿐이라,
    /// 나중에 Impulse로 갈아끼울 때 위층(트리거·카탈로그·타이밍)은 그대로 둘 수 있다.
    /// </summary>
    private void ApplyShake(float amplitude, float frequency)
    {
        if (perlin == null) return;

        perlin.AmplitudeGain = idleAmplitude + amplitude;
        perlin.FrequencyGain = idleFrequency + frequency;
    }
}
