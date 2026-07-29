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

        // 이 필드는 '모양에 종속된 정적 데이터'다(진행 상태가 아니라). 그래서 이 에셋에 두어도
        // "Pattern은 모양 원본일 뿐 진행 상태를 갖지 않는다"는 원칙과 충돌하지 않는다.
        [Tooltip("패턴을 전 노드 Good/Perfect로 완주했을 때 캐릭터가 재생할 애니메이션 클립. 비우면 무연출.")]
        [SerializeField] private AnimationClip successAnimationClip;

        [Tooltip("성공 애니메이션의 시작 오프셋(초). 선딜레이 제거용.")]
        [SerializeField] private float animationStartOffset = 0f;

        [Tooltip("성공 애니메이션의 재생 지속 시간(초). 후딜레이 제거용. 0 이하면 클립 끝까지 재생.")]
        [SerializeField] private float animationDuration = 0f;

        [Tooltip("이 패턴 베기의 기본 배속(하한). 패턴 입력 구간이 짧으면 자동으로 더 배속된다.")]
        [SerializeField] private float animationSpeed = 1f;

        [Tooltip("이 패턴에서 등장할 베이는 표적. 비우면 표적 없음.")]
        [SerializeField] private SliceSpace.SliceSet sliceTarget;

        [Tooltip("임팩트 지점 기준 XY 배치. 칼 궤적 밖으로 벌리지 않는다.")]
        [SerializeField] private Vector2 sliceTargetOffset;

        [Tooltip("판정 종료 시각(Deadline) 대비 ±초. 표적이 닿는 순간을 앞뒤로 민다.")]
        [SerializeField] private float sliceTargetImpactOffset;

        public IReadOnlyList<PatternData> AllData => patternDatas;

        /// <summary>이 패턴이 띄울 표적. <see cref="SuccessAnimationClip"/>과 같은 '모양에 종속된 정적 데이터'다.</summary>
        public SliceSpace.SliceSet SliceTarget => sliceTarget;

        /// <summary>표적의 임팩트 지점 기준 XY 배치. 스폰·임팩트 양쪽에 똑같이 실린다.</summary>
        public Vector2 SliceTargetOffset => sliceTargetOffset;

        /// <summary>표적 도착 시각을 Deadline 기준으로 미는 값(초).</summary>
        public float SliceTargetImpactOffset => sliceTargetImpactOffset;

        public AnimationClip SuccessAnimationClip => successAnimationClip;

        public float AnimationStartOffset => animationStartOffset;

        public float AnimationDuration => animationDuration;

        public float AnimationSpeed => Mathf.Max(animationSpeed, 0.01f);

        public NodeType GetNodeType(int position)
        {
            if (position == 0) return NodeType.Start;
            if (position == patternDatas.Length - 1) return NodeType.End;
            return NodeType.Progress;
        }

        private void OnValidate()
        {
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
    }
}
