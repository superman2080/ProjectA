using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 풀에서 나온 인스턴스가 <b>어느 프리팹에서 나왔는지</b> 되짚기 위한 표식.
/// 반납 경로가 프리팹을 인자로 받지 않아도 되게 해 준다(호출부가 그걸 들고 다니면 짝이 어긋난다).
///
/// <para>런타임에 <see cref="PrefabPool"/>이 붙인다 — 프리팹 에셋에 미리 붙여 두지 않는다.</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class PooledInstance : MonoBehaviour
{
    public GameObject SourcePrefab;
}

/// <summary>
/// <b>프리팹별</b> 인스턴스 풀. 소유자가 필드로 하나씩 들고 쓰는 순수 C# 클래스다(MonoBehaviour 아님).
///
/// <para><see cref="Pool"/>(<c>PoolKey</c> 싱글톤)과 역할이 다르다 — 저쪽은 <b>키가 고정된 소수의 프리팹</b>을
/// <c>IPoolable</c> 계약으로 다루고, 이쪽은 <b>에셋이 데이터로 지정하는 임의 개수의 프리팹</b>을 다룬다
/// (적 종류·절단 세트·이펙트 큐는 전부 인스펙터에서 늘어난다).</para>
///
/// <para><b>보관 상한(<c>maxSize</c>)은 대여할 때마다 갱신된다.</b> 같은 프리팹이라도 부르는 쪽이
/// 아는 상한이 다를 수 있어(세트마다 <c>MaxPoolSize</c>가 다르다) 마지막 값을 쓴다.
/// 상한을 넘는 반납분은 보관하지 않고 파기한다.</para>
/// </summary>
public sealed class PrefabPool
{
    private readonly Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();
    private readonly Dictionary<GameObject, int> maxSizes = new Dictionary<GameObject, int>();

    private readonly Transform root;
    private readonly bool detachOnRent;

    /// <param name="root">보관 중인 인스턴스를 매달아 둘 곳. 비활성 오브젝트를 쓰면 보관분이 통째로 잠든다.</param>
    /// <param name="detachOnRent">
    /// 대여할 때 부모에서 떼어낼지. <b>적처럼 무대 위를 자유롭게 움직이는 인스턴스는 true</b>여야 한다 —
    /// 비활성 <paramref name="root"/> 아래 남으면 활성화해도 부모가 꺼져 있어 화면에 안 나온다.
    /// </param>
    public PrefabPool(Transform root, bool detachOnRent = false)
    {
        this.root = root;
        this.detachOnRent = detachOnRent;
    }

    /// <summary>유휴 인스턴스를 꺼낸다. 없으면 새로 만든다. 프리팹이 null이면 null.</summary>
    public GameObject Rent(GameObject prefab, int maxSize)
    {
        if (prefab == null) return null;

        maxSizes[prefab] = maxSize;

        GameObject instance = null;
        if (pools.TryGetValue(prefab, out var queue) && queue.Count > 0)
            instance = queue.Dequeue();

        instance ??= Create(prefab);
        if (instance == null) return null;

        if (detachOnRent) instance.transform.SetParent(null, false);
        instance.SetActive(true);
        return instance;
    }

    /// <summary>인스턴스를 되돌린다. 상한을 넘으면 보관하지 않고 파기한다. 표식이 없으면 풀 밖에서 온 것이라 파기한다.</summary>
    public void Release(GameObject instance)
    {
        if (instance == null) return;

        var link = instance.GetComponent<PooledInstance>();
        if (link == null || link.SourcePrefab == null)
        {
            Object.Destroy(instance);
            return;
        }

        var prefab = link.SourcePrefab;
        if (!pools.TryGetValue(prefab, out var queue))
            pools[prefab] = queue = new Queue<GameObject>();

        int cap = maxSizes.TryGetValue(prefab, out int m) ? m : int.MaxValue;
        if (queue.Count >= cap)
        {
            Object.Destroy(instance);
            return;
        }

        instance.SetActive(false);
        instance.transform.SetParent(root, false);
        queue.Enqueue(instance);
    }

    /// <summary>
    /// 곡이 시작되기 전에 미리 만들어 둔다. <b>곡 도중 <c>Instantiate</c>가 한 번이라도 일어나면 히치 = 판정 손실</b>이라
    /// 이 호출이 존재한다(카운트다운 구간에서 부른다).
    /// </summary>
    public void Prewarm(GameObject prefab, int count, int maxSize)
    {
        if (prefab == null || count <= 0) return;

        maxSizes[prefab] = Mathf.Max(maxSize, count);
        for (int i = 0; i < count; i++)
            Release(Create(prefab));
    }

    /// <summary>인스턴스 하나를 만들어 표식을 붙인다. 대여 상태가 아니라 <b>root 아래에 그대로</b> 둔다.</summary>
    private GameObject Create(GameObject prefab)
    {
        if (prefab == null) return null;

        var go = Object.Instantiate(prefab, root);
        go.name = prefab.name;

        var link = go.GetComponent<PooledInstance>();
        if (link == null) link = go.AddComponent<PooledInstance>();
        link.SourcePrefab = prefab;
        return go;
    }
}
