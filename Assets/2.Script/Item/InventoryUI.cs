using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    public GameObject inventoryPanel; // 꺼내고 들여보낼 패널
    private bool activeInventory = false; // 현재 인벤토리가 열렸는지 확인

    void Start()
    {
        // 게임 시작할 때는 인벤토리를 꺼둠
        inventoryPanel.SetActive(false);
    }

    void Update()
    {
        // Tab 키를 눌렀을 때
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            activeInventory = !activeInventory; // 상태 반전 (열림 <-> 닫힘)
            inventoryPanel.SetActive(activeInventory);

            // 인벤토리가 열렸을 때 마우스 커서가 보이게 설정 (선택 사항)
            if (activeInventory)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}