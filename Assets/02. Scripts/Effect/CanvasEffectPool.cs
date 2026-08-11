using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <see cref="CanvasEffectView"/> 전용 프리팹별 풀. <see cref="EffectManager"/>(Canvas 이펙트)와
/// <see cref="PatternEffectDirector"/>(월드 이펙트)가 <b>같은 뷰 타입</b>을 쓰므로 풀도 하나를 공유한다.
///
/// <para><see cref="PrefabPool"/>과 나눈 이유는 <b>반납 훅</b>이다 — 뷰는 <c>OnDespawn()</c>에서
/// 부모·크기·배속을 원복해야 하고, 원본 프리팹도 표식 컴포넌트가 아니라
/// <see cref="CanvasEffectView.SourcePrefab"/>이 직접 든다.</para>
/// </summary>
public sealed class CanvasEffectPool
{
    private readonly Dictionary<GameObject, Queue<CanvasEffectView>> pools = new Dictionary<GameObject, Queue<CanvasEffectView>>();
    private readonly Dictionary<GameObject, int> maxSizes = new Dictionary<GameObject, int>();

    private readonly Transform parent;
    private readonly bool assignPoolParent;

    /// <param name="parent">생성 시 붙일 부모(Canvas 레이어 또는 풀 루트).</param>
    /// <param name="assignPoolParent">
    /// 뷰에게 "반납할 때 돌아올 자리"를 알려 줄지. <b>월드 이펙트는 true</b>여야 한다 —
    /// 앵커를 따라가느라 부모가 바뀌므로, 반납 시 돌아올 곳을 스스로 알아야 앵커와 함께 파괴되지 않는다.
    /// </param>
    public CanvasEffectPool(Transform parent, bool assignPoolParent = false)
    {
        this.parent = parent;
        this.assignPoolParent = assignPoolParent;
    }

    /// <summary>유휴 뷰를 꺼낸다. 없으면 만든다. <paramref name="maxSize"/>는 보관 상한을 갱신한다(더 큰 쪽 유지).</summary>
    public CanvasEffectView Rent(GameObject prefab, int maxSize)
    {
        if (prefab == null) return null;

        maxSizes[prefab] = Mathf.Max(GetMaxSize(prefab), maxSize);

        var queue = GetQueue(prefab);
        return queue.Count > 0 ? queue.Dequeue() : Create(prefab);
    }

    /// <summary>뷰를 되돌린다. <c>OnDespawn</c>으로 상태를 원복한 뒤 보관하고, 상한을 넘으면 파기한다.</summary>
    public void Return(CanvasEffectView view)
    {
        if (view == null) return;

        view.OnDespawn(); // 부모·크기·배속 원복

        var prefab = view.SourcePrefab;
        if (prefab == null) { Object.Destroy(view.gameObject); return; }

        var queue = GetQueue(prefab);
        int cap = Mathf.Max(GetMaxSize(prefab), 1);

        if (queue.Count < cap) queue.Enqueue(view);
        else Object.Destroy(view.gameObject);
    }

    /// <summary>곡 시작 전에 미리 만들어 둔다. 이미 그만큼 있으면 아무것도 하지 않는다.</summary>
    public void Prewarm(GameObject prefab, int count, int maxSize)
    {
        if (prefab == null || count <= 0) return;

        maxSizes[prefab] = Mathf.Max(GetMaxSize(prefab), Mathf.Max(maxSize, count));

        var queue = GetQueue(prefab);
        for (int i = queue.Count; i < count; i++)
        {
            var view = Create(prefab);
            view.OnDespawn();
            queue.Enqueue(view);
        }
    }

    private int GetMaxSize(GameObject prefab) => maxSizes.TryGetValue(prefab, out int m) ? m : 0;

    private Queue<CanvasEffectView> GetQueue(GameObject prefab)
    {
        if (!pools.TryGetValue(prefab, out var queue))
            pools[prefab] = queue = new Queue<CanvasEffectView>();
        return queue;
    }

    private CanvasEffectView Create(GameObject prefab)
    {
        var go = Object.Instantiate(prefab, parent, false);
        go.name = prefab.name;

        var view = go.GetComponent<CanvasEffectView>();
        if (view == null)
        {
            Debug.LogError($"[CanvasEffectPool] 프리팹 '{prefab.name}'에 CanvasEffectView가 없습니다.", prefab);
            view = go.AddComponent<CanvasEffectView>();
        }

        view.SourcePrefab = prefab;
        if (assignPoolParent) view.SetPoolParent(parent);
        return view;
    }
}
