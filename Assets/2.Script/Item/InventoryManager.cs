using UnityEngine;
using System.Collections.Generic;

public class InventoryManager : MonoBehaviour
{
    public Slot[] slots;
    private readonly HashSet<int> pendingPickups = new HashSet<int>();

    public bool TryReservePickup(int itemId)
    {
        if (slots == null || !pendingPickups.Add(itemId)) return false;
        int freeSlots = 0;
        foreach (Slot slot in slots)
            if (slot != null && slot.item == null) freeSlots++;
        if (freeSlots >= pendingPickups.Count) return true;
        pendingPickups.Remove(itemId);
        return false;
    }

    public void ReleasePickup(int itemId) { pendingPickups.Remove(itemId); }

    public void AddItem(ItemData newItem)
    {
        if (newItem == null)
            return;

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

    public void UseItem(int slotIndex)
    {
        if (slots == null || slotIndex < 0 || slotIndex >= slots.Length || slots[slotIndex] == null)
            return;
        slots[slotIndex].OnClickSlot();
    }
}