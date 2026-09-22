using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public Slot[] slots;

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
        if (slotIndex < 0 || slotIndex >= slots.Length) return;

        ItemData item = slots[slotIndex].item;
        if (item == null) return;

        Transform localPlayer = null;
        if (NetworkClient.Instance != null && NetworkClient.Instance.localPlayerTransform != null)
        {
            localPlayer = NetworkClient.Instance.localPlayerTransform;
        }
        if (localPlayer == null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) localPlayer = pObj.transform;
        }

        Vector3 effectPos = localPlayer != null ? localPlayer.position + Vector3.up * 1f : Vector3.zero;

        switch (item.type)
        {
            case ItemData.ItemType.Heal:
                if (localPlayer != null)
                {
                    var pc = localPlayer.GetComponent<PlayerController>();
                    if (pc != null) pc.HealToFull();
                }
                if (ItemEffectManager.Instance != null) ItemEffectManager.Instance.PlayHealEffect(effectPos);
                break;

            case ItemData.ItemType.Stealth:
                if (localPlayer != null)
                {
                    var stealth = localPlayer.GetComponent<PlayerStealth>();
                    if (stealth != null) stealth.ActivateStealth();
                }
                if (ItemEffectManager.Instance != null) ItemEffectManager.Instance.PlayStealthEffect(effectPos);
                break;

            case ItemData.ItemType.Resurrection:
                if (localPlayer != null)
                {
                    var pc = localPlayer.GetComponent<PlayerController>();
                    if (pc != null) pc.Revive();
                }
                if (ItemEffectManager.Instance != null) ItemEffectManager.Instance.PlayResurrectionEffect(effectPos);
                break;

            case ItemData.ItemType.Teleport:
                if (ItemEffectManager.Instance != null) ItemEffectManager.Instance.PlayTeleportEffect(effectPos);
                break;
        }

        if (NetworkClient.Instance != null && !NetworkClient.Instance.offlineMode)
        {
            NetworkClient.Instance.SendItemUseRequest(item.type.ToString());
        }

        slots[slotIndex].ClearSlot();
        Debug.Log($"[Inventory] {item.itemName} 사용 완료 및 소모");
    }
}