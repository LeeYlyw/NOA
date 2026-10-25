using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;

public class Slot : MonoBehaviour
{
    public ItemData item;
    public Image iconImage;

    public void SetItem(ItemData newItem)
    {
        item = newItem;
        if (iconImage != null && item != null && item.icon != null)
        {
            iconImage.sprite = item.icon;
            iconImage.enabled = true;
        }
    }

    public void ClearSlot()
    {
        item = null;
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
    }

    // 기존 if-else if 구조 유지 [cite: 12]
    public void OnClickSlot()
    {
        if (item == null)
            return;

        Debug.Log($"[Slot] 클릭 감지됨: {item.itemName} (Type: {item.type}) [cite: 12]");

        if (item.type == ItemData.ItemType.Heal)
        {
            UseHealItem();
        }
        else if (item.type == ItemData.ItemType.Stealth)
        {
            UseStealthItem();
        }
        else if (item.type == ItemData.ItemType.Teleport)
        {
            UseTeleportItem();
        }
        else if (item.type == ItemData.ItemType.Resurrection)
        {
            UseResurrectionItem();
        }
    }

    #region Helper Methods (공통 중복 정리)
    private Transform GetPlayerTransform(bool isRemote)
    {
        if (NetworkClient.Instance == null)
        {
            Debug.LogError("NetworkClient.Instance가 없습니다. [cite: 12]");
            return null;
        }

        Transform target = isRemote
            ? NetworkClient.Instance.remotePlayerTransform
            : NetworkClient.Instance.localPlayerTransform;

        if (target == null)
        {
            Debug.LogError(isRemote
                ? "NetworkClient.remotePlayerTransform이 설정되지 않았습니다. [cite: 12]"
                : "NetworkClient.localPlayerTransform이 설정되지 않았습니다. [cite: 12]");
        }
        return target;
    }

    private Transform GetLocalPlayer() => GetPlayerTransform(false);
    private Transform GetRemotePlayer() => GetPlayerTransform(true);

    private bool IsOfflineMode()
    {
        return NetworkClient.Instance != null && NetworkClient.Instance.offlineMode;
    }
    #endregion

    private void UseHealItem()
    {
        Transform localPlayer = GetLocalPlayer();
        if (localPlayer == null) return;

        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayHealEffect(localPlayer.position);
        }

        if (IsOfflineMode())
        {
            var pc = localPlayer.GetComponent<PlayerController>();
            if (pc != null) pc.HealToFull();
        }
        else if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.SendItemUseRequest("Heal");
        }

        Debug.Log("[아이템] 회복 아이템 사용 완료 [cite: 12]");
        ClearSlot();
    }

    private void UseStealthItem()
    {
        Transform localPlayer = GetLocalPlayer();
        if (localPlayer == null) return;

        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayStealthEffect(localPlayer.position);
        }

        if (IsOfflineMode())
        {
            var stealth = localPlayer.GetComponent<PlayerStealth>();
            if (stealth != null) stealth.ActivateStealth();
        }
        else if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.SendItemUseRequest("Stealth");
        }

        Debug.Log("[아이템] 은신 아이템 사용 완료 [cite: 12]");
        ClearSlot();
    }

    private void UseTeleportItem()
    {
        Transform localPlayer = GetLocalPlayer();
        if (localPlayer == null) return;

        Transform teammate = GetRemotePlayer();
        if (teammate == null || !teammate.gameObject.activeInHierarchy ||
            (!IsOfflineMode() && !NetworkClient.Instance.HasRemotePlayerState))
        {
            Debug.LogWarning("[Teleport] Teammate is not available yet.");
            return;
        }

        Transform targetPoint = null;
        foreach (Transform child in teammate.GetComponentsInChildren<Transform>(true))
        {
            if (child != teammate && child.CompareTag("TeleportPoint"))
            {
                targetPoint = child;
                break;
            }
        }

        Vector3 destination = targetPoint != null
            ? targetPoint.position
            : teammate.position + teammate.right * 1.2f;

        CharacterController cc = localPlayer.GetComponent<CharacterController>();
        if (!TrySafeTeleportDestination(localPlayer, teammate, destination, cc, out destination))
        {
            Debug.LogWarning("[Teleport] No clear floor beside teammate. Item was not consumed.");
            return;
        }
        Vector3 originPos = localPlayer.position;

        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayTeleportEffect(originPos);
        }

        if (cc != null) cc.enabled = false;
        localPlayer.position = destination;
        if (cc != null) cc.enabled = true;

        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayTeleportEffect(destination);
        }

        Debug.Log("텔레포트 아이템 사용 완료 [cite: 12]");
        ClearSlot();
    }

    private bool TrySafeTeleportDestination(Transform localPlayer, Transform teammate,
        Vector3 requested, CharacterController controller, out Vector3 safe)
    {
        safe = requested;
        if (!NavMesh.SamplePosition(requested, out NavMeshHit floor, 0.75f, NavMesh.AllAreas))
            return false;
        if (Vector2.Distance(new Vector2(requested.x, requested.z),
                             new Vector2(floor.position.x, floor.position.z)) > 0.5f ||
            Mathf.Abs(requested.y - floor.position.y) > 1f ||
            NavMesh.Raycast(teammate.position, floor.position, out _, NavMesh.AllAreas))
            return false;

        float radius = controller != null ? controller.radius : 0.4f;
        float height = controller != null ? controller.height : 1.8f;
        Vector3 foot = floor.position + Vector3.up * (radius + 0.05f);
        Vector3 head = floor.position + Vector3.up * Mathf.Max(radius, height - radius);
        foreach (Collider obstacle in Physics.OverlapCapsule(foot, head, radius * 0.9f,
                     ~0, QueryTriggerInteraction.Ignore))
        {
            if (obstacle.transform.IsChildOf(localPlayer) || obstacle.transform.IsChildOf(teammate))
                continue;
            return false;
        }
        safe = floor.position;
        return true;
    }

    private void UseResurrectionItem()
    {
    RemotePlayerCheck:
        Transform remotePlayer = GetRemotePlayer();
        if (remotePlayer == null) return;

        PlayerController teammateController = remotePlayer.GetComponent<PlayerController>();
        if (teammateController == null || !teammateController.IsDead())
        {
            Debug.Log("동료가 살아있어서 부활 아이템을 사용할 수 없습니다. [cite: 12]");
            return;
        }

        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayResurrectionEffect(remotePlayer.position);
        }

        int targetPlayerId = (NetworkClient.Instance.playerId == 1) ? 2 : 1;
        if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.SendPlayerReviveRequest(targetPlayerId);
        }

        Debug.Log($"[부활 요청 송신] Target PlayerId: {targetPlayerId} [cite: 12]");
        ClearSlot();
    }
}