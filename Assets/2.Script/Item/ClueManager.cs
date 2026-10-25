using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ClueManager : MonoBehaviour
{
    public static ClueManager instance;

    [Header("Clue")]
    public int clueCount = 0;
    public int needClueCount = 3;

    [Header("UI")]
    public Image clueIcon;
    public TextMeshProUGUI clueCountText;

    [Header("Ending")]
    public GameObject endingPanel;

    private bool isEnding = false;
    private bool canRestartTogether = true;
    private bool waitingForRestart;
    private string resultTitle = "MISSION COMPLETE";
    public bool IsEnding => isEnding;

    void Awake()
    {
        instance = this;
        GamePresentation.Ensure();

        if (clueIcon != null)
        {
            clueIcon.enabled = true;
            Color color = clueIcon.color;
            color.a = 0.4f;
            clueIcon.color = color;
        }

        RefreshClueUI();

        if (endingPanel != null)
            endingPanel.SetActive(false);

        if (GetComponent<ObjectiveHUD>() == null) gameObject.AddComponent<ObjectiveHUD>();
        // The mission HUD owns the shared clue counter.
        if (clueIcon != null) clueIcon.enabled = false;
        if (clueCountText != null) clueCountText.enabled = false;
    }

    public void SetClueCount(int count, int needCount)
    {
        clueCount = count;
        needClueCount = needCount;

        Debug.Log("서버 단서 개수 반영: " + clueCount + " / " + needClueCount);
        RefreshClueUI();
    }

    private void RefreshClueUI()
    {
        if (clueIcon != null)
        {
            Color color = clueIcon.color;
            color.a = clueCount > 0 ? 1.0f : 0.4f;
            clueIcon.color = color;
        }

        if (clueCountText != null)
            clueCountText.text = clueCount + " / " + needClueCount;
    }

    // 서버가 없는 단독 테스트용 fallback.
    // 멀티 서버 권한 구조에서는 서버의 S_CLUE_COUNT 패킷으로 SetClueCount가 호출된다.
    public void AddClue()
    {
        if (isEnding)
            return;

        clueCount++;
        Debug.Log("ClueManager :: 단서 획득 성공! 현재 개수: " + clueCount);

        RefreshClueUI();

        if (clueCount >= needClueCount)
            ShowEnding();
    }

    public void ShowEnding()
    {
        if (isEnding)
            return;

        isEnding = true;
        resultTitle = "MISSION COMPLETE";
        Debug.Log("엔딩 패널 활성화");

        if (endingPanel != null)
            endingPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }
    public void ShowDefeat()
    {
        if (isEnding) return;
        isEnding = true;
        resultTitle = "MISSION FAILED - BOTH PLAYERS DOWN";
        if (endingPanel != null) endingPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }

    public void ShowConnectionLost(string reason)
    {
        if (isEnding) return;
        isEnding = true;
        canRestartTogether = false;
        resultTitle = reason;
        if (endingPanel != null) endingPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }

    private void OnGUI()
    {
        if (!isEnding) return;
        NetworkClient network = NetworkClient.Instance;
        Rect panel = new Rect((Screen.width - 440f) / 2f, Screen.height - 185f, 440f, 170f);
        GUI.Box(panel, resultTitle);
        if (network == null) return;
        GUI.Label(new Rect(panel.x + 20f, panel.y + 35f, 400f, 35f),
            waitingForRestart ? "Waiting for teammate to restart..." :
            canRestartTogether ? "Both players can restart together, or return to the lobby." :
            "Connection ended. Return to the lobby to reconnect.");
        GUI.enabled = canRestartTogether && !waitingForRestart && (network.IsConnected || network.offlineMode);
        if (GUI.Button(new Rect(panel.x + 20f, panel.y + 82f, 185f, 46f), "Restart together"))
        {
            waitingForRestart = true;
            network.RequestRestart();
        }
        GUI.enabled = true;
        if (GUI.Button(new Rect(panel.x + 235f, panel.y + 82f, 185f, 46f), "Return to lobby"))
            network.ReturnToLobby();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
