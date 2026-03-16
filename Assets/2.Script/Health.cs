using UnityEngine;

public class Health : MonoBehaviour
{
    public int maxHp = 100;
    public int currentHp;

    private bool isDead = false;

    void Start()
    {
        currentHp = maxHp;
        Debug.Log("플레이어 체력 시작: " + currentHp);
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHp -= damage;
        Debug.Log("플레이어 피격! 현재 HP: " + currentHp);

        if (currentHp <= 0)
        {
            currentHp = 0;
            Die();
        }
    }

    void Die()
    {
        isDead = true;
        Debug.Log("플레이어 사망");

        // 여기서는 일단 테스트용으로만 멈춤
        // 필요하면 이동 스크립트 비활성화 추가 가능
    }
}