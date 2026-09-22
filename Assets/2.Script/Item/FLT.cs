using UnityEngine;

public class FLT : MonoBehaviour

{
    [Header("손전등 오브젝트")]
    public GameObject flashlight; // 아까 만든 Spotlight를 여기에 연결합니다.

    void Start()
    {
        // 게임 시작 시 무조건 꺼진 상태로 초기화
        if (flashlight != null)
        {
            flashlight.SetActive(false);
        }
    }

    void Update()
    {
        // U키를 누를 때마다 켜짐/꺼짐 상태가 반전됨
        if (Input.GetKeyDown(KeyCode.U))
        {
            if (flashlight != null)
            {
                bool isActive = flashlight.activeSelf;
                flashlight.SetActive(!isActive);
            }
        }
    }
}