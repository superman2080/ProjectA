using System;
using UnityEngine;

namespace SequenceSpace
{
    /// <summary>
    /// 슬롯 이름 -> 씬 오브젝트 배선표. <b>씬 쪽 세계로 들어가는 유일한 통로다.</b>
    ///
    /// <para><b>왜 필요한가</b>: <see cref="SequenceAsset"/>은 ScriptableObject라 씬 오브젝트를
    /// 참조할 수 없다. 시퀀스가 씬 전용 일회성이어도 이 제약은 그대로다 — 위치는 좌표로 해결되지만
    /// "어느 NPC를 움직이나", "어느 <c>PlayableDirector</c>를 재생하나"는 씬을 가리켜야 한다.</para>
    ///
    /// <para><b>키가 전역 enum이 아닌 이유</b>: 스테이지마다 시퀀스가 늘어나면 항목이 한 enum에 전부 쌓여
    /// <b>대사 하나 추가하는 콘텐츠 작업이 코드 수정을 요구하게 된다</b>. 에셋이 자기 슬롯을 선언하고
    /// (<see cref="SequenceAsset.RequiredBindings"/>) 스텝은 드롭다운으로 고르므로 오타도 안 난다.</para>
    /// </summary>
    [Serializable]
    public class SequenceBindings
    {
        [Serializable]
        public struct Entry
        {
            public string slot;
            public UnityEngine.Object target;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public Entry[] Entries => entries;

        /// <summary>
        /// 슬롯에 배선된 대상을 <typeparamref name="T"/>로 꺼낸다.
        /// GameObject가 배선돼 있고 <typeparamref name="T"/>가 컴포넌트면 <c>GetComponent</c>까지 해 준다 —
        /// 저작자가 프리팹 루트를 끌어다 놓는 것이 자연스럽기 때문.
        /// </summary>
        public T Resolve<T>(string slot, UnityEngine.Object contextObject = null) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(slot)) return null;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].slot != slot) continue;

                UnityEngine.Object target = entries[i].target;
                if (target == null) break;

                if (target is T typed) return typed;

                if (target is GameObject go && typeof(Component).IsAssignableFrom(typeof(T)))
                {
                    T found = go.GetComponent<T>();
                    if (found != null) return found;
                }

                if (target is Component component && typeof(T) == typeof(GameObject))
                    return component.gameObject as T;

                Debug.LogError($"[SequenceBindings] 슬롯 '{slot}'에 배선된 대상이 {typeof(T).Name}이 아닙니다 " +
                               $"(배선된 것: {target.GetType().Name}).", contextObject);
                return null;
            }

            Debug.LogError($"[SequenceBindings] 슬롯 '{slot}'이 배선되지 않았습니다.", contextObject);
            return null;
        }

        public Transform ResolveTransform(string slot, UnityEngine.Object contextObject = null)
            => Resolve<Transform>(slot, contextObject);

        /// <summary>선언된 슬롯 목록에 맞춰 항목을 다시 짠다(기존 배선은 이름으로 보존). 에디터 전용 경로.</summary>
        public void SyncSlots(string[] requiredSlots)
        {
            if (requiredSlots == null) requiredSlots = Array.Empty<string>();

            var next = new Entry[requiredSlots.Length];
            for (int i = 0; i < requiredSlots.Length; i++)
            {
                next[i].slot = requiredSlots[i];
                next[i].target = null;

                for (int j = 0; j < entries.Length; j++)
                {
                    if (entries[j].slot != requiredSlots[i]) continue;
                    next[i].target = entries[j].target;
                    break;
                }
            }

            entries = next;
        }
    }
}
