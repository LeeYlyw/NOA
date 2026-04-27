using UnityEngine;
using System.Collections;

public class PlayerStealth : MonoBehaviour
{
    public bool isStealth = false;
    public float stealthDuration = 5f; // 은신 지속 시간 (5초)

    [Header("Material Settings")]
    public Material stealthMaterial;   // 1단계에서 만든 투명 메테리얼 넣는 칸
    private Material[] originalMaterials; // 원래 메테리얼들 저장용
    private Renderer playerRenderer;

    void Start()
    {
        // 플레이어의 MeshRenderer를 가져옵니다. (자식 오브젝트에 있다면 GetComponentsInChildren 사용)
        playerRenderer = GetComponentInChildren<MeshRenderer>();
        if (playerRenderer == null)
        {
            // 만약 못 찾았다면, 모든 자식을 다 뒤져서라도 하나 가져오게 합니다.
            playerRenderer = GetComponent<MeshRenderer>(); // 본인에게 있는 경우
            if (playerRenderer == null)
                playerRenderer = GetComponentInChildren<SkinnedMeshRenderer>(); // 캐릭터 모델인 경우 이게 많음
        }
        if (playerRenderer != null)
        {
            // 원래 메테리얼들을 배열에 백업해둡니다.
            originalMaterials = playerRenderer.materials;
        }
    }

    public void ActivateStealth()
    {
     
        if (isStealth || playerRenderer == null || stealthMaterial == null)
        {
            Debug.LogWarning("은신 실행 실패! 원인: " +
                (playerRenderer == null ? "렌더러 없음 " : "") +
                (stealthMaterial == null ? "메테리얼 없음" : "이미 은신중"));
            return;
        }
        
        StartCoroutine(StealthRoutine());
    }

    IEnumerator StealthRoutine()
    {
        isStealth = true;
        Debug.Log("은신 시작!");

        // 시각 효과: 모든 자식 렌더러를 찾아 투명 메테리얼 적용
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (var rend in renderers)
        {
            Material[] stealthMats = new Material[rend.materials.Length];
            for (int i = 0; i < stealthMats.Length; i++)
            {
                stealthMats[i] = stealthMaterial;
            }
            rend.materials = stealthMats;
        }

        yield return new WaitForSeconds(stealthDuration);

        // 복구: 원래대로 (가장 간단한 건 Rebind나 씬 재배치지만, 일단 원래대로 복구)
        // 사실 원래 메테리얼을 배열로 다 저장해두는 건 복잡하니 
        // 그냥 렌더러를 껐다 켜거나 씬의 기본 메테리얼을 다시 입히는 게 확실합니다.
        foreach (var rend in renderers)
        {
            // 원래 메테리얼로 복구하는 로직 (기존 originalMaterials 활용)
            rend.materials = originalMaterials;
        }

        isStealth = false;
        Debug.Log("은신 해제!");
    }
}