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

        Debug.Log("아이템 충돌 감지: " + other.name);

        if (!other.CompareTag("Player"))
            return;

        PlayerController player = other.GetComponent<PlayerController>();

        if (player == null)
            player = other.GetComponentInParent<PlayerController>();

        // 멀티에서 상대 플레이어가 내 화면에서 닿았다고 내 클라가 먹으면 안 됨
        if (player != null && !player.isLocalPlayer)
            return;

        if (itemData == null)
        {
            Debug.LogError(gameObject.name + " itemData가 설정되지 않았습니다.");
            return;
        }

        PlayerRoleSetup role = other.GetComponent<PlayerRoleSetup>();
        if (role == null)
            role = other.GetComponentInParent<PlayerRoleSetup>();

        if (role != null)
        {
            // 단서는 탐색자만 획득 가능
            if (itemData.type == ItemData.ItemType.Clue && !role.IsExplorer)
            {
                Debug.Log("탐색자만 단서를 획득할 수 있습니다!");
                return;
            }
        }

        // 서버 권한 구조: 클라이언트는 획득을 확정하지 않고 서버에 요청만 보낸다.
        if (NetworkClient.Instance != null)
        {
            isRequestingPickup = true;
            NetworkClient.Instance.SendItemPickupRequest(itemId, itemData);
            return;
        }

        // 서버가 없는 단독 테스트용 fallback
        ApplyLocalPickup();
    }

    private void ApplyLocalPickup()
    {
        if (isPicked)
            return;

        isPicked = true;
        isRequestingPickup = false;

        if (itemData != null && itemData.type != ItemData.ItemType.Clue)
        {
            InventoryManager inv = FindObjectOfType<InventoryManager>();
            if (inv != null)
                inv.AddItem(itemData);
        }

        if (itemData != null && itemData.type == ItemData.ItemType.Clue)
        {
            if (ClueManager.instance != null)
                ClueManager.instance.AddClue();
        }

        gameObject.SetActive(false);
    }

    public void ApplyServerPickup(int pickedPlayerId, int itemType, int clueCount)
    {
        if (isPicked)
            return;

        isPicked = true;
        isRequestingPickup = false;

        bool pickedByLocalPlayer = NetworkClient.Instance != null && pickedPlayerId == NetworkClient.Instance.playerId;

        // 실제 아이템 지급은 서버가 획득을 확정한 뒤, 획득한 로컬 플레이어에게만 적용한다.
        if (pickedByLocalPlayer && itemData != null && itemData.type != ItemData.ItemType.Clue)
        {
            InventoryManager inv = FindObjectOfType<InventoryManager>();
            if (inv != null)
            {
                inv.AddItem(itemData);
            }
            else
            {
                Debug.LogWarning("InventoryManager를 찾지 못했습니다!");
            }
        }

        // 단서 카운트는 서버의 S_CLUE_COUNT 패킷에서 ClueManager가 처리한다.
        gameObject.SetActive(false);
    }

    // 구버전 ITEM_PICKUP 패킷 호환용
    public void ApplyRemotePickup()
    {
        ApplyServerPickup(-1, -1, -1);
    }
}
