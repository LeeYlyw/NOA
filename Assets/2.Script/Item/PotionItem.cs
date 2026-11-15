using UnityEngine;

public class PotionItem : MonoBehaviour
{
    [Header("Network")]
    public int itemId = 1;
    private bool isPicked = false;
    private bool isRequestingPickup = false;
    private InventoryManager requestingInventory;
    private float nextPickupAttemptAt;
    private PlayerController nearbyLocalPlayer;
    private float lastContactAt = -100f;

    [Header("Item Data")]
    public ItemData itemData;
    public PlayerController NearbyLocalPlayer => nearbyLocalPlayer;

    private void OnTriggerEnter(Collider other)
    {
        TrackNearbyPlayer(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TrackNearbyPlayer(other);
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null && player == nearbyLocalPlayer)
        {
            nearbyLocalPlayer = null;
            ItemPickupInteraction.Unregister(this);
        }
    }

    private void OnDisable()
    {
        nearbyLocalPlayer = null;
        ItemPickupInteraction.Unregister(this);
    }

    private void Update()
    {
        NetworkClient network = NetworkClient.Instance;
        if (isRequestingPickup && (network == null ||
            (!network.offlineMode && !network.IsConnected)))
            DenyPickup();
    }

    private void TrackNearbyPlayer(Collider other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();
        // CharacterController belongs to the player root, even when the
        // imported character prefab does not carry the Player tag.
        if (player == null || !player.isLocalPlayer || player.IsDeadState || itemData == null || isPicked)
            return;

        nearbyLocalPlayer = player;
        lastContactAt = Time.unscaledTime;
        ItemPickupInteraction.Register(this);
    }

    public bool CanInteract(PlayerController player)
    {
        return player != null && player == nearbyLocalPlayer && player.isLocalPlayer &&
            player.gameObject.activeInHierarchy && !player.IsDeadState && gameObject.activeInHierarchy &&
            itemData != null && !isPicked && !isRequestingPickup &&
            Time.unscaledTime - lastContactAt < 0.2f;
    }

    public bool TryPickup(PlayerController player)
    {
        if (!CanInteract(player) || Time.unscaledTime < nextPickupAttemptAt)
            return false;

        nextPickupAttemptAt = Time.unscaledTime + 0.75f;

        if (itemData.type != ItemData.ItemType.Clue)
        {
            requestingInventory = FindObjectOfType<InventoryManager>();
            if (requestingInventory == null || !requestingInventory.TryReservePickup(itemId))
            {
                Debug.Log("[Item] Inventory is full; pickup remains in the world.");
                return false;
            }
        }

        // STEP 1: 서버로 아이템 습득 요청 패킷만 전송
        if (NetworkClient.Instance != null)
        {
            if (!NetworkClient.Instance.offlineMode &&
                (!NetworkClient.Instance.IsConnected || !NetworkClient.Instance.HasAssignedPlayerId))
            {
                DenyPickup();
                return false;
            }
            isRequestingPickup = true;
            if (!NetworkClient.Instance.SendItemPickupRequest(itemId, itemData))
            {
                DenyPickup();
                return false;
            }
            return true;
        }
        DenyPickup();
        return false;
    }

    public void DenyPickup()
    {
        isRequestingPickup = false;
        nextPickupAttemptAt = Time.unscaledTime + 0.75f;
        if (requestingInventory != null) requestingInventory.ReleasePickup(itemId);
        requestingInventory = null;
    }

    private void OnDestroy() { DenyPickup(); }

    // 서버에서 S_ITEM_PICKUP 방송이 오면 호출됨
    public void ApplyServerPickup(int pickedPlayerId)
    {
        if (isPicked) return;

        InventoryManager inventory = requestingInventory;
        isPicked = true;
        DenyPickup();

        bool isLocal = NetworkClient.Instance != null &&
            pickedPlayerId == NetworkClient.Instance.playerId;
        if (isLocal && itemData != null && itemData.type != ItemData.ItemType.Clue)
        {
            if (inventory == null) inventory = FindObjectOfType<InventoryManager>();
            if (inventory == null || !inventory.AddItem(itemData, itemId))
                Debug.LogError("[Item] Server pickup could not be placed in local inventory.");
        }

        gameObject.SetActive(false);
    }


}
