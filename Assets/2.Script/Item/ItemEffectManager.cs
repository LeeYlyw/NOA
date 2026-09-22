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
        if (healEffectPrefab != null)
        {
            GameObject effect = Instantiate(healEffectPrefab, position, Quaternion.identity);
            Destroy(effect, 0.1f); // 2초 뒤 삭제
        }
    }

    // 2. 은신 이펙트 재생
    public void PlayStealthEffect(Vector3 position)
    {
        if (stealthEffectPrefab != null)
        {
            GameObject effect = Instantiate(stealthEffectPrefab, position, Quaternion.identity);
            Destroy(effect, 0.1f); // 2초 뒤 삭제
        }
    }

    // 3. 텔레포트 이펙트 재생
    public void PlayTeleportEffect(Vector3 position)
    {
        if (teleportEffectPrefab != null)
        {
            GameObject effect = Instantiate(teleportEffectPrefab, position, Quaternion.identity);
            Destroy(effect, 0.1f); // 2초 뒤 삭제
        }
    }

    // 4. 부활 이펙트 재생
    public void PlayResurrectionEffect(Vector3 position)
    {
        if (resurrectionEffectPrefab != null)
        {
            GameObject effect = Instantiate(resurrectionEffectPrefab, position, Quaternion.identity);
            Destroy(effect, 0.1f); // 2초 뒤 삭제
        }
    }
}