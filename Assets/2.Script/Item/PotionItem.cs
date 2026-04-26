using UnityEngine;

public class PotionItem : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("포션 충돌 감지: " + other.name);

        PlayerController player = other.GetComponentInParent<PlayerController>();

        if (player == null)
        {
            Debug.LogWarning("PlayerController를 찾지 못함");
            return;
        }

        if (!player.isLocalPlayer)
        {
            Debug.Log("리모트 플레이어라 포션 회복 무시");
            return;
        }

        Debug.Log("플레이어가 포션 획득");

        player.HealToFull();
        Destroy(gameObject);
    }
}