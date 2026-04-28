using UnityEngine;
using UnityEngine.UI;

public class ClueManager : MonoBehaviour
{
    public static ClueManager instance;

    public int clueCount = 0;
    public Image clueIcon;      // 단서 Sprite가 들어갈 이미지 컴포넌트
    public Text clueCountText;  // 숫자(1, 2, 3)가 표시될 텍스트 컴포넌트

    void Awake()
    {
        instance = this;

        // 게임 시작 시에는 아무것도 안 보이게 설정
        if (clueIcon != null) clueIcon.enabled = false;
        if (clueCountText != null) clueCountText.text = "";
    }

    public void AddClue()
    {
        clueCount++;
        UpdateClueUI();
    }

    void UpdateClueUI()
    {
        // 단서를 하나라도 먹으면
        if (clueCount > 0)
        {
            // 1. 이미지를 보이게 합니다. (이미 인스펙터에 이미지를 넣어두셨다면 enabled만 true)
            if (clueIcon != null) clueIcon.enabled = true;

            // 2. 숫자 텍스트를 업데이트합니다.
            if (clueCountText != null)
            {
                clueCountText.text = clueCount.ToString();
            }

            Debug.Log($"단서 UI 업데이트: {clueCount}개");
        }
    }
}