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

        // 시연에서는 단서 아이콘/개수 UI 안 쓰기
        if (clueIcon != null)
            clueIcon.enabled = false;

        if (clueCountText != null)
            clueCountText.text = "";

        if (endingPanel != null)
            endingPanel.SetActive(false);
    }

    public void AddClue()
    {
        if (isEnding)
            return;

        clueCount++;

        Debug.Log("단서 획득: " + clueCount + " / " + needClueCount);

        // 아이콘, 카운트 UI는 표시하지 않음
        if (clueIcon != null)
            clueIcon.enabled = false;

        if (clueCountText != null)
            clueCountText.text = "";

        if (clueCount >= needClueCount)
        {
            RequestEnding();
        }
    }

    private void RequestEnding()
    {
        if (isEnding)
            return;

        Debug.Log("단서 3개 수집 완료. 엔딩 처리 시작");

        // 내 화면은 바로 엔딩 표시
        ShowEnding();

        // 멀티 상태면 서버에 전체 엔딩 요청
        if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.SendGameClear();
        }
    }

    public void ShowEnding()
    {
        if (isEnding)
            return;

        isEnding = true;

        Debug.Log("엔딩 패널 활성화");

        if (endingPanel != null)
        {
            endingPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("EndingPanel이 ClueManager에 연결되어 있지 않습니다.");
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Time.timeScale = 0f;
    }
}