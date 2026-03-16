using UnityEngine;
using UnityEngine.UI;

public class Health : MonoBehaviour
{
    public int maxHp = 100;
    public int currentHp;

    public Slider hpSlider;

    private bool isDead = false;

    void Start()
    {
        currentHp = maxHp;
        UpdateHpUI();
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHp -= damage;

        if (currentHp < 0)
            currentHp = 0;

        UpdateHpUI();

        Debug.Log(gameObject.name + " 피격! 현재 HP: " + currentHp);

        if (currentHp <= 0)
        {
            Die();
        }
    }

    public void HealToFull()
    {
        if (isDead) return;

        currentHp = maxHp;
        UpdateHpUI();

        Debug.Log(gameObject.name + " 체력 전부 회복: " + currentHp);
    }

    void UpdateHpUI()
    {
        if (hpSlider != null)
        {
            hpSlider.maxValue = maxHp;
            hpSlider.value = currentHp;
        }
    }

    void Die()
    {
        isDead = true;
        Debug.Log(gameObject.name + " 사망");
    }
}