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

        PlayerController player = localPlayer.GetComponent<PlayerController>();
        if (player == null)
        {
            Debug.LogError("로컬 플레이어에 PlayerController가 없습니다.");
            return;
        }

        player.HealToFull();
        Debug.Log("회복 아이템 사용 완료");
        ClearSlot();
    }

    private void UseStealthItem()
    {
        Transform localPlayer = GetLocalPlayer();
        if (localPlayer == null)
            return;

        PlayerStealth stealth = localPlayer.GetComponent<PlayerStealth>();
        if (stealth == null)
        {
            Debug.LogError("로컬 플레이어에 PlayerStealth가 없습니다.");
            return;
        }

        stealth.ActivateStealth();
        Debug.Log("은신 아이템 사용 완료");
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
        if (cc != null)
            cc.enabled = false;

        localPlayer.position = targetPoint.transform.position;

        if (cc != null)
            cc.enabled = true;

        Debug.Log("텔레포트 아이템 사용 완료");
        ClearSlot();
    }

    private void UseResurrectionItem()
    {
        Transform remotePlayer = GetRemotePlayer();
        if (remotePlayer == null)
            return;

        PlayerController teammateController = remotePlayer.GetComponent<PlayerController>();
        if (teammateController == null)
        {
            Debug.LogError("동료 플레이어에 PlayerController가 없습니다.");
            return;
        }

        if (!teammateController.IsDead())
        {
            Debug.Log("동료가 살아있어서 부활 아이템을 사용할 수 없습니다.");
            return;
        }

        int targetPlayerId = NetworkClient.Instance.playerId == 1 ? 2 : 1;

        teammateController.Revive();
        NetworkClient.Instance.SendPlayerRevive(targetPlayerId);

        Debug.Log("동료 부활 아이템 사용 완료 / 대상 PlayerId: " + targetPlayerId);
        ClearSlot();
    }
}