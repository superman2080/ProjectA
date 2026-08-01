using System;
using PatternSpace;
using UnityEngine;

namespace ChartGen
{
    [Serializable]
    public class SongChartEntry
    {
        /// <summary>이 그룹에 배정된 패턴 템플릿(모양). 원본 에셋을 그대로 참조하며 복제하지 않는다.</summary>
        public Pattern template;

        /// <summary>입력 판정 절대시각(초). 곡의 실제 비트 위치 — 고정값이며, 재저장해도 바뀌지 않는다.</summary>
        public float[] onsetTimes;

        /// <summary>곡이 소유하는 노드별 노출시간(연출용, 초). 굽는 에디터에서 편집 가능한 저작 데이터.</summary>
        public float[] exposureDurations;

        /// <summary>굽는 시점의 계산값 스냅샷(스폰 절대시각, 초). exposureDurations를 바꾸고 재저장하지 않으면 실제 값과 어긋날 수 있다.</summary>
        public float[] spawnTimes;

        /// <summary>
        /// 이 엔트리의 전투 지시(누가 휘두르는가 / 처치 여부 / 투사체). 비어 있으면 무연출 폴백이라
        /// 기존 채보가 그대로 재생된다. <c>EnemyDirector</c>가 <c>ChartPlayer</c>를 통해 받는다.
        /// </summary>
        public EnemySpace.EnemyCue enemyCue = new EnemySpace.EnemyCue();
    }

    [CreateAssetMenu(fileName = "SongChart", menuName = "Scriptable Objects/Song Chart")]
    public class SongChart : ScriptableObject
    {
        public AudioClip song;

        [Range(1, 30)] public int level = 1;

        /// <summary>곡의 BPM. 자동 감지 없이 굽는 에디터에서 디자이너가 직접 입력한다.</summary>
        public float bpm = 120f;

        /// <summary>첫 박이 시작하는 절대시각(초). 굽는 에디터에서 직접 입력한다.</summary>
        public float beatOffset = 0f;

        /// <summary>
        /// 이 곡이 쓸 패턴 목록. <b>비우면 템플릿 폴더 전체</b>를 쓴다(기존 동작).
        ///
        /// <para>곡마다 어울리는 패턴이 다르므로 <b>선택은 채보의 성질</b>이고, 그래서 엔트리와 같은 곳에 산다 —
        /// 채보를 다시 열어 재분석해도 같은 풀로 구워진다. 굽기 툴에만 쓰이고 런타임은 읽지 않는다.</para>
        /// </summary>
        public Pattern[] patternPool;

        /// <summary>level/bpm/beatOffset을 인스펙터에서 직접 고쳐도 이미 구운 entries의 온셋/그룹 구조는 자동으로 바뀌지 않는다 — 굽는 에디터에서 재분석해야 한다.</summary>
        public SongChartEntry[] entries;
    }
}
