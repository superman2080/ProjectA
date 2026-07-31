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
}
