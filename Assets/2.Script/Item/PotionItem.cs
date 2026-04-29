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

        Debug.Log("플레이어가 포션 획득 시도 / itemId: " + itemId);

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