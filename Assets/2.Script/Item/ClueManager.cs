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

    void Awake()
    {
        instance = this;

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
        Debug.Log("엔딩 패널 활성화");

        if (endingPanel != null)
            endingPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }
}
