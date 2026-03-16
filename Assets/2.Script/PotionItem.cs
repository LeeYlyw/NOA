using UnityEngine;

public class PotionItem : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("포션 충돌 감지: " + other.name);

        if (other.CompareTag("Player"))
        {
            Debug.Log("플레이어가 포션 획득");

            Health health = other.GetComponentInParent<Health>();

            if (health != null)
            {
                health.HealToFull();
                Destroy(gameObject);
            }
            else
            {
                Debug.LogWarning("플레이어에서 Health를 찾지 못함");
            }
        }
    }
}