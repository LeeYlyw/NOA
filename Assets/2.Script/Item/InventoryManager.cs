using UnityEngine;
using System.Collections.Generic;

public class InventoryManager : MonoBehaviour
{
    public Slot[] slots;
    private readonly HashSet<int> pendingPickups = new HashSet<int>();

    private void Start()
    {
        // Scene UI contains an editor sample item without a server item ID.
        NetworkClient network = NetworkClient.Instance;
        if (network == null || network.offlineMode || slots == null) return;
        foreach (Slot slot in slots)
            if (slot != null && slot.item != null && slot.ItemId == 0)
                slot.ClearSlot();
    }

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

    public void AddItem(ItemData newItem) { AddItem(newItem, 0); }

    public bool AddItem(ItemData newItem, int itemId)
    {
        if (newItem == null || slots == null)
            return false;

        if (newItem.type == ItemData.ItemType.Clue)
        {
            Debug.Log("단서는 인벤토리 슬롯에 넣지 않습니다.");
            return false;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].item == null)
            {
                slots[i].SetItem(newItem, itemId);
                Debug.Log(newItem.itemName + "이(가) 인벤토리에 들어왔습니다.");
                return true;
            }
        }

        Debug.Log("인벤토리가 가득 찼습니다!");
        return false;
    }

    public void ApplyItemUse(int itemId, bool accepted)
    {
        if (slots == null) return;
        foreach (Slot slot in slots)
        {
            if (slot == null || slot.ItemId != itemId) continue;
            if (accepted) slot.ConfirmUse();
            else slot.CancelUse();
            return;
        }
    }

    public void UseItem(int slotIndex)
    {
        if (slots == null || slotIndex < 0 || slotIndex >= slots.Length || slots[slotIndex] == null)
            return;
        slots[slotIndex].OnClickSlot();
    }
}