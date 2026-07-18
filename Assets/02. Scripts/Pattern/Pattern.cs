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

        public IReadOnlyList<PatternData> AllData => patternDatas;

        public AnimationClip SuccessAnimationClip => successAnimationClip;

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
