using System.Collections;
using UnityEngine;

public class ClueVisibilityByRole : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("비워두면 이 오브젝트와 자식 Renderer를 자동으로 찾습니다.")]
    public GameObject visualRoot;

    [Header("Collider Option")]
    [Tooltip("Detector 클라이언트에서 단서 Collider를 꺼서 획득 트리거 자체를 막습니다.")]
    public bool disableColliderForDetector = true;

    private Renderer[] renderers;
    private Collider[] colliders;

    private void Awake()
    {
        CacheComponents();
    }

    private void Start()
    {
        StartCoroutine(ApplyVisibilityWhenReady());
    }

    private void CacheComponents()
    {
        GameObject target = visualRoot != null ? visualRoot : gameObject;

        renderers = target.GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
    }

    private IEnumerator ApplyVisibilityWhenReady()
    {
        while (NetworkClient.Instance == null || NetworkClient.Instance.localPlayerTransform == null)
        {
            yield return null;
        }

        PlayerRoleSetup roleSetup =
            NetworkClient.Instance.localPlayerTransform.GetComponent<PlayerRoleSetup>();

        if (roleSetup == null)
        {
            Debug.LogWarning("ClueVisibilityByRole: 로컬 플레이어에서 PlayerRoleSetup을 찾지 못했습니다.");
            yield break;
        }

        bool isExplorer = roleSetup.IsExplorer;

        SetVisible(isExplorer);

        if (disableColliderForDetector)
        {
            SetColliderEnabled(isExplorer);
        }

        Debug.Log(
            "ClueVisibilityByRole 적용 완료 / Object: " + gameObject.name +
            " / Explorer: " + isExplorer
        );
    }

    private void SetVisible(bool visible)
    {
        if (renderers == null || renderers.Length == 0)
            CacheComponents();

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    private void SetColliderEnabled(bool enabled)
    {
        if (colliders == null || colliders.Length == 0)
            CacheComponents();

        foreach (Collider col in colliders)
        {
            if (col != null)
                col.enabled = enabled;
        }
    }
}