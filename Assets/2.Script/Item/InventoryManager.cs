using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    // 아까 버튼들에 붙인 Slot 스크립트들을 담을 배열입니다.
    public Slot[] slots;

    // 아이템을 먹었을 때 호출될 함수
    public void AddItem(ItemData newItem)
    {
        if (newItem.type == ItemData.ItemType.Clue)
        {
            // 씬에 있는 ClueManager를 찾아서 숫자를 올립니다.
            if (ClueManager.instance != null)
            {
                ClueManager.instance.AddClue();
                Debug.Log("단서는 전용 칸으로 들어갔습니다.");
            }
            return; // 일반 5칸 슬롯에 넣지 않도록 여기서 함수 종료!
        }
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