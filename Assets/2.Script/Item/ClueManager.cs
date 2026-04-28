using UnityEngine;
using UnityEngine.UI;
using TMPro; // [추가] TMP를 쓰려면 이게 반드시 있어야 합니다!

public class ClueManager : MonoBehaviour
{
    public static ClueManager instance;

    public int clueCount = 0;
    public Image clueIcon;
    public TextMeshProUGUI clueCountText; // [수정] Text를 TextMeshProUGUI로 변경

    void Awake()
    {
        instance = this;
        if (clueIcon != null) clueIcon.enabled = false;
        if (clueCountText != null) clueCountText.text = "";
    }

    public void AddClue()
    {
        clueCount++;
        if (clueIcon != null) clueIcon.enabled = true;
        if (clueCountText != null) clueCountText.text = clueCount.ToString();
    }
}