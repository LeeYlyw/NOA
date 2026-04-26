using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    // 아까 버튼들에 붙인 Slot 스크립트들을 담을 배열입니다.
    public Slot[] slots;

    // 아이템을 먹었을 때 호출될 함수
    public void AddItem(ItemData newItem)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            // 슬롯들 중에서 빈 칸(item이 null인 곳)을 찾습니다.
            if (slots[i].item == null)
            {
                slots[i].SetItem(newItem); // 그 칸에 아이템 정보를 넘겨줍니다.
                Debug.Log(newItem.itemName + "이(가) 인벤토리에 들어왔습니다.");
                return;
            }
        }
        Debug.Log("인벤토리가 가득 찼습니다!");
    }
}