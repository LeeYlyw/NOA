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
    public Image clueIcon; // 인스펙터에서 단서 스프라이트가 꼭 미리 등록되어 있어야 합니다!
    public TextMeshProUGUI clueCountText;

    [Header("Ending")]
    public GameObject endingPanel;

    private bool isEnding = false;

    void Awake()
    {
        instance = this;

        // 이제 시작할 때 이미지를 끄지 않습니다! 화면에 항상 띄워둡니다.
        if (clueIcon != null)
        {
            clueIcon.enabled = true;

            // [옵션] 시작할 때는 획득 전이므로 반투명하게 설정 (원치 않으시면 이 두 줄은 지우셔도 됩니다)
            Color color = clueIcon.color;
            color.a = 0.4f; // 40% 투명도
            clueIcon.color = color;
        }

        // 시작하자마자 원하는 위치에 "0 / 3"이 정상적으로 뜹니다.
        if (clueCountText != null)
            clueCountText.text = clueCount + " / " + needClueCount;

        if (endingPanel != null)
            endingPanel.SetActive(false);
    }

    public void AddClue()
    {
        if (isEnding)
            return;

        clueCount++;
        Debug.Log(" ClueManager :: 단서 획득 성공! 현재 개수: " + clueCount);

        // 단서를 획득하면 이미지를 선명하게 100% 채웁니다. (하얀 네모 방지)
        if (clueIcon != null)
        {
            Color color = clueIcon.color;
            color.a = 1.0f; // 100% 불투명
            clueIcon.color = color;
        }

        // 단서를 먹을 때마다 실시간으로 1 / 3, 2 / 3 갱신
        if (clueCountText != null)
        {
            clueCountText.text = clueCount + " / " + needClueCount;
        }

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
        ShowEnding();

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
            endingPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }
}