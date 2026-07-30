using UnityEngine;

public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    protected virtual bool DontDestroy { get; set; } = false;

    private static T instance;
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                instance = (T)FindAnyObjectByType(typeof(T));

                if (instance == null)
                {
                    GameObject obj = new GameObject(typeof(T).Name, typeof(T));
                    instance = obj.GetComponent<T>();
                }
            }
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
