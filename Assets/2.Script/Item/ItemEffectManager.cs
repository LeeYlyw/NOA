using UnityEngine;

public class ItemEffectManager : MonoBehaviour
{
    // 5개 슬롯 어디서든 마우스 딸깍으로 접근할 수 있게 만드는 싱글톤 변수
    public static ItemEffectManager Instance;

    [Header("아이템 사용 이펙트 프리팹들")]
    public GameObject healEffectPrefab;
    public GameObject stealthEffectPrefab;
    public GameObject teleportEffectPrefab;
    public GameObject resurrectionEffectPrefab;

    void Awake()
    {
        // 씬에 이 매니저가 딱 하나만 존재하도록 고정
        Instance = this;
    }

    // 1. 힐 이펙트 재생
    public void PlayHealEffect(Vector3 position)
    {
        if (healEffectPrefab != null) Instantiate(healEffectPrefab, position, Quaternion.identity);
    }

    // 2. 은신 이펙트 재생
    public void PlayStealthEffect(Vector3 position)
    {
        if (stealthEffectPrefab != null) Instantiate(stealthEffectPrefab, position, Quaternion.identity);
    }

    // 3. 텔레포트 이펙트 재생
    public void PlayTeleportEffect(Vector3 position)
    {
        if (teleportEffectPrefab != null) Instantiate(teleportEffectPrefab, position, Quaternion.identity);
    }

    // 4. 부활 이펙트 재생
    public void PlayResurrectionEffect(Vector3 position)
    {
        if (resurrectionEffectPrefab != null) Instantiate(resurrectionEffectPrefab, position, Quaternion.identity);
    }
}