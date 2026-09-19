using UnityEngine;
using UnityEngine.UI;

public class Slot : MonoBehaviour
{
    public ItemData item;
    public Image iconImage;

    public void SetItem(ItemData newItem)
    {
        item = newItem;
        iconImage.sprite = item.icon;
        iconImage.enabled = true;
    }

    public void ClearSlot()
    {
        item = null;
        iconImage.sprite = null;
        iconImage.enabled = false;
    }

    public void OnClickSlot()
    {
        if (item == null)
            return;

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

    private Transform GetLocalPlayer()
    {
        if (NetworkClient.Instance == null)
        {
            Debug.LogError("NetworkClient.Instance가 없습니다.");
            return null;
        }

        if (NetworkClient.Instance.localPlayerTransform == null)
        {
            Debug.LogError("NetworkClient.localPlayerTransform이 설정되지 않았습니다.");
            return null;
        }

        return NetworkClient.Instance.localPlayerTransform;
    }

    private Transform GetRemotePlayer()
    {
        if (NetworkClient.Instance == null)
        {
            Debug.LogError("NetworkClient.Instance가 없습니다.");
            return null;
        }

        if (NetworkClient.Instance.remotePlayerTransform == null)
        {
            Debug.LogError("NetworkClient.remotePlayerTransform이 설정되지 않았습니다.");
            return null;
        }

        return NetworkClient.Instance.remotePlayerTransform;
    }

    private void UseHealItem()
    {
        Transform localPlayer = GetLocalPlayer();
        if (localPlayer == null)
            return;

        // 시각적 이펙트는 즉시 재생하여 조작감 유지
        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayHealEffect(localPlayer.position);
        }

        // 로컬 체력 회복 로직 제거 (서버 권한 구조)
        // player.HealToFull(); 

        // 서버로 힐 아이템 사용 요청 전송
        if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.SendItemUseRequest("Heal");
        }

        Debug.Log("[아이템] 회복 아이템 사용 요청 전송");
        ClearSlot();
    }

    private void UseStealthItem()
    {
        Transform localPlayer = GetLocalPlayer();
        if (localPlayer == null)
            return;

        // 시각 이펙트는 즉시 실행
        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayStealthEffect(localPlayer.position);
        }

        // [수정] 로컬 직접 호출 제거 -> 서버 권한 요청 전송
        // stealth.ActivateStealth();
        if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.SendItemUseRequest("Stealth");
        }

        Debug.Log("[아이템] 은신 아이템 사용 요청 전송");
        ClearSlot();
    }

    private void UseTeleportItem()
    {
        Transform localPlayer = GetLocalPlayer();
        if (localPlayer == null)
            return;

        GameObject targetPoint = GameObject.FindGameObjectWithTag("TeleportPoint");
        if (targetPoint == null)
        {
            Debug.LogError("맵에 TeleportPoint 태그를 가진 오브젝트가 없습니다.");
            return;
        }

        CharacterController cc = localPlayer.GetComponent<CharacterController>();

        Vector3 originPos = localPlayer.position;
        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayTeleportEffect(originPos);
        }

        if (cc != null)
            cc.enabled = false;

        localPlayer.position = targetPoint.transform.position;

        if (cc != null)
            cc.enabled = true;

        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayTeleportEffect(targetPoint.transform.position);
        }

        Debug.Log("텔레포트 아이템 사용 완료");
        ClearSlot();
    }

    // Slot.cs 내의 UseResurrectionItem() 메서드 부분 수정
    private void UseResurrectionItem()
    {
        Transform remotePlayer = GetRemotePlayer();
        if (remotePlayer == null) return;

        PlayerController teammateController = remotePlayer.GetComponent<PlayerController>();
        if (teammateController == null || !teammateController.IsDead())
        {
            Debug.Log("동료가 살아있어서 부활 아이템을 사용할 수 없습니다.");
            return;
        }

        if (ItemEffectManager.Instance != null)
        {
            ItemEffectManager.Instance.PlayResurrectionEffect(remotePlayer.position);
        }

        int targetPlayerId = (NetworkClient.Instance.playerId == 1) ? 2 : 1;

        // STEP 1: 즉시 부활 대신 서버에 부활 요청만 송신
        NetworkClient.Instance.SendPlayerReviveRequest(targetPlayerId);

        Debug.Log($"[부활 요청 송신] Target PlayerId: {targetPlayerId}");
        ClearSlot();
    }
}