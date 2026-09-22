using UnityEngine;
using UnityEngine.UI;

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

        GameObject targetPoint = GameObject.FindGameObjectWithTag("TeleportPoint");
        if (targetPoint == null)
        {
            Debug.LogError("맵에 TeleportPoint 태그를 가진 오브젝트가 없습니다. [cite: 12]");
            return;
        }

        CharacterController cc = localPlayer.GetComponent<CharacterController>();
        Vector3 originPos = localPlayer.position;

        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayTeleportEffect(originPos);
        }

        if (cc != null) cc.enabled = false;
        localPlayer.position = targetPoint.transform.position;
        if (cc != null) cc.enabled = true;

        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayTeleportEffect(targetPoint.transform.position);
        }

        Debug.Log("텔레포트 아이템 사용 완료 [cite: 12]");
        ClearSlot();
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