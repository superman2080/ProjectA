using UnityEngine;

/// <summary>
/// 모든 <see cref="Singleton{T}"/>이 공유하는 종료 플래그. 제네릭 타입별로 정적 필드가 갈리는 것을 피하려고
/// 비제네릭 클래스에 둔다(SoundManager가 죽는 순간을 SfxManager 쪽 게터도 알아야 하기 때문).
/// </summary>
public static class SingletonRuntime
{
    /// <summary>애플리케이션(에디터에서는 플레이 모드)이 종료 중인가.</summary>
    public static bool IsQuitting { get; private set; }

    // SubsystemRegistration은 도메인 리로드를 꺼도 플레이 시작마다 호출되므로 여기서 플래그를 되돌린다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        IsQuitting = false;

        // 리로드가 꺼져 있으면 이 메서드가 여러 번 돌므로 중복 구독을 막는다.
        Application.quitting -= HandleQuitting;
        Application.quitting += HandleQuitting;
    }

    private static void HandleQuitting() => IsQuitting = true;
}

public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    protected virtual bool DontDestroy { get; set; } = false;

    private static T instance;
    public static T Instance
    {
        get
        {
            if (instance != null)
                return instance;

            instance = (T)FindAnyObjectByType(typeof(T));

            if (instance != null)
                return instance;

            // 종료 중에는 새로 만들지 않는다. 여기서 만들면 닫히는 씬에 미아 오브젝트가 남아
            // "Some objects were not cleaned up when closing the scene" 경고가 난다.
            // (파괴된 싱글톤을 OnDisable/OnDestroy에서 참조하는 순간 이 경로가 열린다.)
            if (SingletonRuntime.IsQuitting)
                return null;

            GameObject obj = new GameObject(typeof(T).Name, typeof(T));
            instance = obj.GetComponent<T>();

            return instance;
        }

    }

    protected virtual void Awake()
    {
        // 씬이 다시 로드돼 같은 타입의 오브젝트가 하나 더 생기면, 이미 살아있는 인스턴스를 두고
        // 이 중복 오브젝트를 즉시 파괴한다. 안 그러면 각자 DontDestroyOnLoad를 걸어버려 인스턴스가
        // 두 개로 늘어나고, 씬을 닫을 때 "정리 안 된 오브젝트" 경고와 함께 미아 오브젝트가 남는다.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this as T;

        if (DontDestroy == false)
            return;

        if (transform.parent != null && transform.root != null)
        {
            DontDestroyOnLoad(transform.root.gameObject);
        }
        else
        {
            DontDestroyOnLoad(gameObject);
        }
    }
}
