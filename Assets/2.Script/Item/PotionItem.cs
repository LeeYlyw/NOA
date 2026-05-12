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
            // 만약 이 아이템이 '단서' 타입인데, 먹으려는 사람이 '탐지기(0번)'라면?
            if (itemData.type == ItemData.ItemType.Clue && role.ownerClientId == 0)
            {
                Debug.Log("탐지기는 단서를 획득할 수 없습니다!");
                return; // 함수 종료 (못 먹게 함)
            }
        }
        
        InventoryManager inv = FindObjectOfType<InventoryManager>();

        if (inv != null)
        {
            isPicked = true;

            inv.AddItem(itemData);

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