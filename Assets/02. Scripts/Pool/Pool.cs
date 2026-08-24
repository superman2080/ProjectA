using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;

public enum PoolKey
{
    FocusRing,
}


public class Pool : Singleton<Pool>
{
    [SerializeField] private SerializedDictionary<PoolKey, GameObject> prefabs = new();
    [SerializeField] private SerializedDictionary<PoolKey, int> initialSizes = new();

    private readonly Dictionary<PoolKey, Queue<IPoolable>> pools = new();
    private readonly int defaultSize = 5;

    private void Start()
    {
        foreach (var kvp in prefabs)
        {
            var queue = new Queue<IPoolable>();
            int size = initialSizes.TryGetValue(kvp.Key, out int s) ? s : defaultSize;
            for (int i = 0; i < size; i++)

            {
                var obj = CreateNew(kvp.Key);
                obj.OnDespawn();
                queue.Enqueue(obj);
            }
            pools[kvp.Key] = queue;
        }
    }

    private IPoolable CreateNew(PoolKey key)
    {
        if (!prefabs.TryGetValue(key, out var prefab) || prefab == null)
            throw new InvalidOperationException(
                $"[Pool] 키 '{key}'에 대한 프리팹이 등록되지 않았습니다.");

        if(prefab.TryGetComponent(out IPoolable poolable))
            return Instantiate(prefab).GetComponent<IPoolable>();
        else throw new InvalidOperationException( $"[Pool] '{key}'프리팹에 IPoolable 인터페이스가 없습니다.");
    }

    private IPoolable GetInternal(PoolKey key)
    {
        if (!pools.TryGetValue(key, out var queue))
            pools[key] = queue = new Queue<IPoolable>();
        return queue.Count > 0 ? queue.Dequeue() : CreateNew(key);
    }

    public IPoolable Get(PoolKey key)
    {
        var obj = GetInternal(key);
        obj.OnSpawn();
        return obj;
    }

    // 데이터 주입 오버로드 — initializer 실행 후 OnSpawn() 호출
    public T Get<T>(PoolKey key, Action<T> initializer) where T : class, IPoolable
    {
        var obj = GetInternal(key) as T;
        initializer?.Invoke(obj);
        obj.OnSpawn();
        return obj;
    }

    public void Return(PoolKey key, IPoolable obj)
    {
        obj.OnDespawn();
        if (!pools.TryGetValue(key, out var queue))
            pools[key] = queue = new Queue<IPoolable>();
        queue.Enqueue(obj);
    }

    /// <summary>
    /// <b>정리 경로에서 쓰는 반납</b> — 풀이 이미 사라졌으면 조용히 넘어간다.
    ///
    /// <para><see cref="Singleton{T}.Instance"/>는 <b>종료 중에 null을 돌려준다</b>(닫히는 씬에 미아
    /// 오브젝트를 만들지 않기 위해서다 — 그 게터의 주석이 "파괴된 싱글톤을 OnDisable/OnDestroy에서
    /// 참조하는 순간 이 경로가 열린다"고 이미 적고 있다). 즉 <c>Pool.Instance.Return(...)</c>은
    /// <c>OnDisable</c>·<c>OnDestroy</c>에서 <b>NullReferenceException으로 터진다.</b></para>
    ///
    /// <para>반납할 곳이 없으면 <see cref="IPoolable.OnDespawn"/>도 부르지 않는다 — 풀과 함께
    /// 파괴될 오브젝트라 되돌릴 상태가 없고, 그 훅이 이미 파괴된 자식을 만지면 예외가 한 번 더 난다.</para>
    /// </summary>
    public static void Release(PoolKey key, IPoolable obj)
    {
        if (obj == null) return;

        var pool = Instance;
        if (pool == null) return;   // 종료 중 — 풀도 오브젝트도 곧 사라진다

        pool.Return(key, obj);
    }
}
