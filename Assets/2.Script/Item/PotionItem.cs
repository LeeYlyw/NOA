using UnityEngine;

public class PotionItem : MonoBehaviour
{
    [Header("Network")]
    public int itemId = 1;
    private bool isPicked = false;
    private bool isRequestingPickup = false;

    [Header("Item Data")]
    public ItemData itemData;

    private void OnTriggerEnter(Collider other)
    {
        if (isPicked || isRequestingPickup)
            return;

        if (!other.CompareTag("Player"))
            return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();

        // 내 화면에서 상대 플레이어가 닿은 경우 내 요청을 보내지 않음
        if (player != null && !player.isLocalPlayer)
            return;

        if (itemData == null) return;

        PlayerRoleSetup role = other.GetComponent<PlayerRoleSetup>();
        if (role == null) role = other.GetComponentInParent<PlayerRoleSetup>();

        if (role != null && itemData.type == ItemData.ItemType.Clue && !role.IsExplorer)
        {
            Debug.Log("[아이템] 탐색자만 단서를 획득할 수 있습니다!");
            return;
        }

        // STEP 1: 서버로 아이템 습득 요청 패킷만 전송
        if (NetworkClient.Instance != null)
        {
            isRequestingPickup = true;
            NetworkClient.Instance.SendItemPickupRequest(itemId, itemData);
            return;
        }
    }

    // 서버에서 S_ITEM_PICKUP 방송이 오면 호출됨
    public void ApplyServerPickup(int pickedPlayerId)
    {
        if (isPicked) return;

        isPicked = true;
        isRequestingPickup = false;

        bool isLocal = (NetworkClient.Instance != null && pickedPlayerId == NetworkClient.Instance.playerId);
        Debug.Log($"[PickupDebug] isLocal={isLocal}, pickedPlayerId={pickedPlayerId}, itemData={itemData?.itemName}");

        if (isLocal && itemData != null && itemData.type != ItemData.ItemType.Clue)
        {
            InventoryManager inv = FindObjectOfType<InventoryManager>();
            Debug.Log($"[PickupDebug] InventoryManager found={inv != null}");
            if (inv != null) inv.AddItem(itemData);
        }

        gameObject.SetActive(false);
    }


}