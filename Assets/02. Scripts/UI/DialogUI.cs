using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System;
using TMPro;
using UnityEngine.UI;

[Serializable]
public class DialogData
{
    public float printTime;
    public string name;
    [TextArea(3, 10)] public string script;
    [Range(0, 1)] public float skipProgress = 0.75f;
}

/// <summary>
/// 대사 창. 한 줄씩 타자기로 출력하고, 다 넘어가면 완료를 알린 뒤 스스로 닫는다.
///
/// <para><b>표시 계층은 게임플레이를 모른다</b> — 누가 왜 대사를 띄우는지는 부르는 쪽(<c>DialogStep</c>)이 안다.</para>
///
/// <para><b>왜 정적 접근자인가</b>: <c>DialogStep</c>은 ScriptableObject에 직렬화되므로 씬 오브젝트를
/// 참조할 수 없다. <c>Singleton{T}</c>를 안 쓰는 이유는 그쪽 게터가 <b>없으면 빈 게임오브젝트를 만들어 내기</b> 때문 —
/// 여기서는 자식 참조가 없는 껍데기가 생기느니 <c>null</c>이 낫다(부르는 쪽이 에러를 찍고 그 스텝만 건너뛴다).</para>
/// </summary>
public class DialogUI : MonoBehaviour
{
    /// <summary>씬에 배치된 대사 창. 없으면 <c>null</c>.</summary>
    public static DialogUI Instance { get; private set; }

    [SerializeField] private GameObject dialog;
    [SerializeField] private TextMeshProUGUI dialogName;
    [SerializeField] private TextMeshProUGUI dialogScript;
    [SerializeField] private Button nextDialog;

    private readonly Queue<DialogData> dialogQueue = new Queue<DialogData>();
    private Coroutine dialogCoroutine;

    // 지금 출력 중인 대사
    private DialogData currentData;

    // 현재 대사의 출력 진행도 (0~1)
    private float progress;

    private Action onFinished;

    /// <summary>컴포넌트를 붙이거나 Reset을 눌렀을 때 자식 이름으로 자동 배선한다. <b>에디터 전용 경로다.</b></summary>
    private void Reset()
    {
        Transform found = transform.Find("Dialog");
        if (found == null) return;

        dialog = found.gameObject;
        dialogName = found.Find("Name")?.GetComponent<TextMeshProUGUI>();
        dialogScript = found.Find("Script")?.GetComponent<TextMeshProUGUI>();
        nextDialog = found.Find("Next")?.GetComponent<Button>();
        dialog.SetActive(false);
    }

    void Awake()
    {
        // ⚠ 등록은 Awake다(Start가 아니라). Unity는 Start 순서를 보장하지 않으므로
        // 소비자가 자기 Start에서 조회하면 null을 받을 수 있다 - SequenceRunner의 playOnStart가 그 경로다.
        Instance = this;

        // ⚠ 예전에는 여기서 Reset()을 직접 불렀다. 그러면 인스펙터에서 배선한 참조를 매 실행 덮어써
        // 하이어라키의 이름이 계약이 되고 프리팹 구조를 못 바꾼다. 대신 누락을 시끄럽게 잡는다.
        if (dialog == null || dialogName == null || dialogScript == null || nextDialog == null)
        {
            Debug.LogError("[DialogUI] 참조가 배선되지 않았습니다 - 인스펙터에서 채우거나 " +
                           "컴포넌트 우클릭 > Reset으로 자동 배선하세요.", this);
            return;
        }

        nextDialog.onClick.AddListener(ShowNextLine);
        dialog.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// 대사를 띄운다. 다 넘어가면 <paramref name="onDialogFinished"/>가 한 번 불린다.
    ///
    /// <para><b>완료 통보를 인자로 받는다</b> — 예전에는 <c>Action OnExit</c> 필드에 <c>+=</c> 하는 방식이라
    /// 구독이 안 풀려, 대사를 띄우는 쪽이 둘 이상이면 <b>서로의 완료까지 같이 받았다</b>.</para>
    /// </summary>
    public void SetDialog(List<DialogData> datas, Action onDialogFinished = null)
    {
        if (dialog == null) return;

        StopPrinting();

        // ⚠ 이전 호출의 잔여가 남아 있으면 새 대사 뒤에 옛 대사가 이어 붙는다.
        dialogQueue.Clear();
        currentData = null;
        progress = 0f;

        onFinished = onDialogFinished;

        if (datas != null)
        {
            foreach (DialogData data in datas)
            {
                if (data != null) dialogQueue.Enqueue(data);
            }
        }

        dialog.SetActive(true);
        ShowNextLine();
    }

    private void ShowNextLine()
    {
        // 출력 중이면 먼저 전문을 채운다(진행도가 문턱을 넘어야 스킵이 먹는다).
        if (currentData != null && currentData.skipProgress > progress) return;

        if (dialogCoroutine != null)
        {
            StopPrinting();
            dialogName.text = currentData.name;
            dialogScript.text = currentData.script;
        }

        if (dialogQueue.TryDequeue(out DialogData data))
        {
            currentData = data;
            dialogCoroutine = StartCoroutine(PrintDialogScript(currentData));
            return;
        }

        Close();
    }

    private void Close()
    {
        currentData = null;

        // ⚠ 대사가 끝났다고 창을 무조건 내리면, 그 아래 깔려 있던 프롬프트가 같이 사라진다.
        if (promptText == null) dialog.SetActive(false);
        else ShowPromptNow();

        // 콜백이 또 대사를 띄울 수 있으므로 먼저 비우고 부른다.
        Action callback = onFinished;
        onFinished = null;
        callback?.Invoke();
    }

    // ─── 프롬프트 ───
    //
    // 대사와 같은 창을 쓰지만 성질이 다르다: 큐도 타이핑도 완료 콜백도 없고, 내려 달라고 할 때까지 떠 있다.
    // ⚠ 창의 주인이 둘이 되는 문제를 여기서 끝낸다 - 대사가 언제나 이기고, 대사가 끝나면 프롬프트가 돌아온다.
    // 그래서 프롬프트를 띄우는 쪽(Encounter)은 시퀀스가 도는지 알 필요가 없다.

    private string promptText;

    /// <summary>내려 달라고 할 때까지 떠 있는 한 줄. 대사가 들어오면 잠시 가려졌다가 되돌아온다.</summary>
    public void ShowPrompt(string text)
    {
        if (dialog == null) return;

        promptText = text;
        if (currentData != null) return; // 대사가 우선. 그 대사가 끝나면 Close가 되살린다.

        ShowPromptNow();
    }

    /// <summary>프롬프트를 내린다. 대사가 떠 있으면 그 대사는 건드리지 않는다.</summary>
    public void HidePrompt()
    {
        if (dialog == null) return;

        promptText = null;
        if (currentData != null) return;

        dialog.SetActive(false);
    }

    private void ShowPromptNow()
    {
        StopPrinting();

        dialogName.text = string.Empty;
        dialogScript.text = promptText;
        dialog.SetActive(true);
    }

    private void StopPrinting()
    {
        if (dialogCoroutine == null) return;

        StopCoroutine(dialogCoroutine);
        dialogCoroutine = null;
    }

    private IEnumerator PrintDialogScript(DialogData data)
    {
        progress = 0f;
        dialogName.text = data.name;
        dialogScript.text = "";

        int visibleCount = CountVisibleChars(data.script);

        // 대사는 시간이 멈춘 화면 위에서도 흘러야 하므로 Realtime을 쓴다.
        WaitForSecondsRealtime waitTime = new WaitForSecondsRealtime(
            visibleCount > 0 ? data.printTime / visibleCount : data.printTime);

        int printed = 0;
        for (int i = 0; i < data.script.Length; i++)
        {
            // 리치 텍스트 태그는 한 덩어리로 넣고 글자 수에 세지 않는다.
            if (data.script[i] == '<')
            {
                int closeIndex = data.script.IndexOf('>', i);
                if (closeIndex != -1)
                {
                    dialogScript.text += data.script.Substring(i, closeIndex - i + 1);
                    i = closeIndex;
                    continue;
                }
            }

            dialogScript.text += data.script[i];
            printed++;
            progress = visibleCount > 0 ? printed / (float)visibleCount : 1f;

            yield return waitTime;
        }

        progress = 1f;
        dialogCoroutine = null;
    }

    private int CountVisibleChars(string script)
    {
        if (string.IsNullOrEmpty(script)) return 0;

        int count = 0;
        for (int i = 0; i < script.Length; i++)
        {
            if (script[i] == '<')
            {
                int closeIndex = script.IndexOf('>', i);
                if (closeIndex != -1)
                {
                    i = closeIndex;
                    continue;
                }
            }
            count++;
        }
        return count;
    }
}
