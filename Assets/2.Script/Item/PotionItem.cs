using UnityEngine;

public class PotionItem : MonoBehaviour
{
    [Header("Network")]
    public int itemId = 1;
    private bool isPicked = false;

    [Header("Item Data")]
    public ItemData itemData;

    private void OnTriggerEnter(Collider other)
    {
        if (isPicked)
            return;

        Debug.Log("포션 충돌 감지: " + other.name);

        if (!other.CompareTag("Player"))
            return;

        PlayerController player = other.GetComponent<PlayerController>();

        if (player == null)
            player = other.GetComponentInParent<PlayerController>();

        // 멀티에서 상대 플레이어가 내 화면에서 닿았다고 내 클라가 먹으면 안 됨
        if (player != null && !player.isLocalPlayer)
            return;

        PlayerRoleSetup role = other.GetComponent<PlayerRoleSetup>();
        if (role == null) role = other.GetComponentInParent<PlayerRoleSetup>();

        if (role != null)
        {
            // 단서는 탐색자만 획득 가능
            if (itemData.type == ItemData.ItemType.Clue && !role.IsExplorer)
            {
                Debug.Log("탐색자만 단서를 획득할 수 있습니다!");
                return;
            }
        }

        InventoryManager inv = FindObjectOfType<InventoryManager>();

        if (inv != null)
        {
            isPicked = true;

            inv.AddItem(itemData);
            if (itemData != null && itemData.type == ItemData.ItemType.Clue)
            {
                if (ClueManager.instance != null)
                {
                    ClueManager.instance.AddClue();
                }
                else
                {
                    Debug.LogError("ClueManager 인스턴스를 찾을 수 없습니다!");
                }
            }
            if (NetworkClient.Instance != null)
                NetworkClient.Instance.SendItemPickup(itemId);

            gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("InventoryManager를 찾지 못했습니다!");
        }
    }

    public void ApplyRemotePickup()
    {
        if (isPicked)
            return;

        isPicked = true;
        gameObject.SetActive(false);
    }
}