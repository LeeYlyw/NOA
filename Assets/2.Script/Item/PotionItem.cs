using UnityEngine;

public class PotionItem : MonoBehaviour
{
    // [추가] 유니티에서 만든 'HealItem' 데이터를 연결할 칸입니다.
    public ItemData itemData;

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("포션 충돌 감지: " + other.name);

        if (other.CompareTag("Player"))
        {
            Debug.Log("플레이어가 포션 획득 시도");

            // [수정] 바로 회복하는 대신, 인벤토리 매니저를 찾아서 아이템을 추가합니다.
            InventoryManager inv = FindObjectOfType<InventoryManager>();

            if (inv != null)
            {
                inv.AddItem(itemData); // 인벤토리에 데이터 전달
                Destroy(gameObject);   // 맵에서 아이템 제거
            }
            else
            {
                Debug.LogWarning("InventoryManager를 찾지 못했습니다!");
            }
        }
    }
}