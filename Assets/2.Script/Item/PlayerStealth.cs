using UnityEngine;
using System.Collections;

public class PlayerStealth : MonoBehaviour
{
    [Header("Stealth State")]
    public bool isStealth = false;
    public float stealthDuration = 5f;

    [Header("Stealth Target")]
    [Tooltip("플레이어 루트가 아니라 외형 모델 자식 오브젝트를 넣는 것을 권장합니다. 비워두면 이 오브젝트의 자식 Renderer들을 자동으로 찾습니다.")]
    public GameObject playerModel;

    [Header("Multiplayer Option")]
    [Tooltip("체크 시 로컬 플레이어일 때만 은신 아이템을 사용할 수 있습니다.")]
    public bool onlyLocalPlayerCanUse = true;

    private Coroutine stealthCoroutine;
    private Renderer[] renderers;
    private SkinnedMeshRenderer[] skinnedRenderers;

    void Awake()
    {
        CacheRenderers();
    }

    void Start()
    {
        CacheRenderers();
        SetVisible(true);
        isStealth = false;
    }

    public void ActivateStealth()
    {
        if (!CanUseStealth())
            return;

        if (renderers == null || renderers.Length == 0)
            CacheRenderers();

        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogError("PlayerStealth: 은신 처리할 Renderer를 찾지 못했습니다. playerModel 설정을 확인하세요.");
            return;
        }

        if (stealthCoroutine != null)
        {
            StopCoroutine(stealthCoroutine);
            stealthCoroutine = null;
        }

        stealthCoroutine = StartCoroutine(StealthRoutine());
    }

    private bool CanUseStealth()
    {
        if (!onlyLocalPlayerCanUse)
            return true;

        PlayerController controller = GetComponent<PlayerController>();

        if (controller != null)
        {
            if (!controller.isLocalPlayer)
            {
                Debug.LogWarning("PlayerStealth: 로컬 플레이어가 아니므로 은신을 실행하지 않습니다. Object: " + gameObject.name);
                return false;
            }
        }

        if (NetworkClient.Instance != null && NetworkClient.Instance.localPlayerTransform != null)
        {
            if (transform != NetworkClient.Instance.localPlayerTransform)
            {
                Debug.LogWarning("PlayerStealth: localPlayerTransform이 아니므로 은신을 실행하지 않습니다. Object: " + gameObject.name);
                return false;
            }
        }

        return true;
    }

    private IEnumerator StealthRoutine()
    {
        isStealth = true;
        SetVisible(false);

        Debug.Log("은신 시작: " + gameObject.name);

        yield return new WaitForSeconds(stealthDuration);

        EndStealth();
    }

    public void EndStealth()
    {
        if (stealthCoroutine != null)
        {
            StopCoroutine(stealthCoroutine);
            stealthCoroutine = null;
        }

        SetVisible(true);
        isStealth = false;

        Debug.Log("은신 해제: " + gameObject.name);
    }

    private void CacheRenderers()
    {
        GameObject target = playerModel != null ? playerModel : gameObject;

        renderers = target.GetComponentsInChildren<Renderer>(true);
        skinnedRenderers = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

    private void SetVisible(bool visible)
    {
        if (renderers == null || renderers.Length == 0)
            CacheRenderers();

        if (renderers == null)
            return;

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    void OnDisable()
    {
        if (isStealth)
        {
            SetVisible(true);
            isStealth = false;
            stealthCoroutine = null;
        }
    }

    void OnDestroy()
    {
        if (isStealth)
        {
            SetVisible(true);
            isStealth = false;
            stealthCoroutine = null;
        }
    }
}