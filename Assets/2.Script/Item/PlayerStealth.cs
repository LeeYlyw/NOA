using UnityEngine;
using System.Collections;

public class PlayerStealth : MonoBehaviour
{
    public bool isStealth = false;
    public float stealthDuration = 5f; // 은신 지속 시간 (5초)

    [Header("Material Settings")]
    public Material stealthMaterial;   // 1단계에서 만든 투명 메테리얼 넣는 칸
    private Material[] originalMaterials; // 원래 메테리얼들 저장용
    private MeshRenderer playerRenderer;   // 플레이어의 렌더러

    void Start()
    {
        // 플레이어의 MeshRenderer를 가져옵니다. (자식 오브젝트에 있다면 GetComponentsInChildren 사용)
        playerRenderer = GetComponentInChildren<MeshRenderer>();

        if (playerRenderer != null)
        {
            // 원래 메테리얼들을 배열에 백업해둡니다.
            originalMaterials = playerRenderer.materials;
        }
    }

    public void ActivateStealth()
    {
        if (isStealth || playerRenderer == null || stealthMaterial == null) return;
        StartCoroutine(StealthRoutine());
    }

    IEnumerator StealthRoutine()
    {
        isStealth = true;
        Debug.Log("은신 시작! 투명해집니다.");

        // --- [시각 효과] 메테리얼을 은신용으로 교체 ---
        // 원래 배열과 크기가 같은 새 배열을 만들고 전부 stealthMaterial로 채웁니다.
        Material[] stealthMats = new Material[originalMaterials.Length];
        for (int i = 0; i < stealthMats.Length; i++)
        {
            stealthMats[i] = stealthMaterial;
        }
        playerRenderer.materials = stealthMats;
        // ----------------------------------------------

        yield return new WaitForSeconds(stealthDuration);

        // 은신 해제: 원래 메테리얼로 복구
        playerRenderer.materials = originalMaterials;
        isStealth = false;
        Debug.Log("은신 해제!");
    }
}