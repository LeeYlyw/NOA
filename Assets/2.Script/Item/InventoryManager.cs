using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public Slot[] slots;

    public void AddItem(ItemData newItem)
    {
        if (newItem == null)
            return;

        // 단서는 인벤토리 슬롯에 넣지 않음
        // 단서 카운트 증가는 PotionItem.cs에서만 처리
        if (newItem.type == ItemData.ItemType.Clue)
        {
            Debug.Log("단서는 인벤토리 슬롯에 넣지 않습니다.");
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].item == null)
            {
                slots[i].SetItem(newItem);
                Debug.Log(newItem.itemName + "이(가) 인벤토리에 들어왔습니다.");
                return;
            }
        }

        Debug.Log("인벤토리가 가득 찼습니다!");
    }
}