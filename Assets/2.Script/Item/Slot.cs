using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;

public class Slot : MonoBehaviour
{
    public ItemData item;
    public Image iconImage;
    public int ItemId { get; private set; }
    private bool pendingUse;

    public void SetItem(ItemData newItem) { SetItem(newItem, 0); }

    public void SetItem(ItemData newItem, int itemId)
    {
        item = newItem;
        ItemId = itemId;
        pendingUse = false;
        if (iconImage != null)
        {
            iconImage.sprite = item != null ? item.icon : null;
            iconImage.enabled = iconImage.sprite != null;
        }
    }

    public void ClearSlot()
    {
        item = null;
        ItemId = 0;
        pendingUse = false;
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
    }

    public void ConfirmUse() { ClearSlot(); }
    public void CancelUse() { pendingUse = false; }

    public void OnClickSlot()
    {
        if (item == null || pendingUse) return;
        NetworkClient network = NetworkClient.Instance;
        if (network == null || network.localPlayerTransform == null) return;

        Transform local = network.localPlayerTransform;
        Transform teammate = network.remotePlayerTransform;
        PlayerController controller = local.GetComponent<PlayerController>();
        if (controller == null || controller.IsDeadState) return;

        int targetId = 0;
        Vector3 destination = Vector3.zero;
        switch (item.type)
        {
            case ItemData.ItemType.Heal:
                if (controller.CurrentHp >= controller.maxHealth) return;
                break;
            case ItemData.ItemType.Stealth:
                break;
            case ItemData.ItemType.Resurrection:
                if (teammate == null || !teammate.gameObject.activeInHierarchy ||
                    (!network.offlineMode && !network.HasRemotePlayerState) ||
                    Vector3.Distance(local.position, teammate.position) > 5f) return;
                PlayerController targetController = teammate.GetComponent<PlayerController>();
                if (targetController == null || !targetController.IsDeadState) return;
                targetId = 3 - network.playerId;
                break;
            case ItemData.ItemType.Teleport:
                if (teammate == null || !teammate.gameObject.activeInHierarchy ||
                    (!network.offlineMode && !network.HasRemotePlayerState)) return;
                Transform point = null;
                foreach (Transform child in teammate.GetComponentsInChildren<Transform>(true))
                    if (child != teammate && child.CompareTag("TeleportPoint"))
                    {
                        point = child;
                        break;
                    }
                if (!TryTeleportBesideTeammate(local, teammate, point, out destination))
                {
                    Debug.LogWarning("[Teleport] No clear floor beside teammate. Item was not consumed.");
                    return;
                }
                targetId = 3 - network.playerId;
                break;
            default:
                return;
        }

        if (network.offlineMode)
        {
            ApplyOfflineUse(local, teammate, controller, destination);
            ClearSlot();
            return;
        }
        if (ItemId <= 0 || !network.SendItemUseRequest(ItemId, item.type, targetId, destination))
            return;
        pendingUse = true;
    }

    private void ApplyOfflineUse(Transform local, Transform teammate,
        PlayerController controller, Vector3 destination)
    {
        switch (item.type)
        {
            case ItemData.ItemType.Heal:
                controller.HealToFull();
                if (ItemEffectManager.Instance != null)
                    ItemEffectManager.Instance.PlayHealEffect(local.position);
                break;
            case ItemData.ItemType.Stealth:
                PlayerStealth stealth = local.GetComponent<PlayerStealth>();
                if (stealth != null) stealth.ActivateStealth();
                if (ItemEffectManager.Instance != null)
                    ItemEffectManager.Instance.PlayStealthEffect(local.position);
                break;
            case ItemData.ItemType.Resurrection:
                PlayerController target = teammate != null ? teammate.GetComponent<PlayerController>() : null;
                if (target != null) target.Revive();
                if (ItemEffectManager.Instance != null && teammate != null)
                    ItemEffectManager.Instance.PlayResurrectionEffect(teammate.position);
                break;
            case ItemData.ItemType.Teleport:
                if (ItemEffectManager.Instance != null)
                    ItemEffectManager.Instance.PlayTeleportEffect(local.position);
                CharacterController cc = local.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                local.position = destination;
                if (cc != null) cc.enabled = true;
                if (ItemEffectManager.Instance != null)
                    ItemEffectManager.Instance.PlayTeleportEffect(destination);
                break;
        }
    }

    private bool TryTeleportBesideTeammate(Transform local, Transform teammate,
        Transform marker, out Vector3 destination)
    {
        CharacterController controller = local.GetComponent<CharacterController>();
        // Old prefab markers can be far outside the teammate's hierarchy origin.
        if (marker != null && Vector3.Distance(marker.position, teammate.position) <= 2.5f &&
            TrySafeTeleportDestination(local, teammate, marker.position, controller, out destination))
            return true;

        // A blocked preferred side must not make the item unusable in an open room.
        for (int i = 0; i < 8; i++)
        {
            Vector3 offset = Quaternion.AngleAxis(i * 45f, Vector3.up) * teammate.right * 1.5f;
            if (TrySafeTeleportDestination(local, teammate, teammate.position + offset,
                    controller, out destination))
                return true;
        }
        destination = Vector3.zero;
        return false;
    }

    private bool TrySafeTeleportDestination(Transform local, Transform teammate,
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

        Vector3 rayStart = teammate.position + Vector3.up;
        Vector3 rayEnd = floor.position + Vector3.up;
        Vector3 ray = rayEnd - rayStart;
        foreach (RaycastHit hit in Physics.RaycastAll(rayStart, ray.normalized, ray.magnitude,
                     ~0, QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(teammate) &&
                !hit.collider.transform.IsChildOf(local))
                return false;

        Vector3 scale = local.lossyScale;
        float radius = controller != null ? controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) : 0.4f;
        float height = controller != null ? Mathf.Max(2f * radius, controller.height * Mathf.Abs(scale.y)) : 1.8f;
        Vector3 foot = floor.position + Vector3.up * (radius + 0.05f);
        Vector3 head = floor.position + Vector3.up * Mathf.Max(radius, height - radius);
        foreach (Collider obstacle in Physics.OverlapCapsule(foot, head, radius * 0.9f,
                     ~0, QueryTriggerInteraction.Ignore))
        {
            if (obstacle.transform.IsChildOf(local) || obstacle.transform.IsChildOf(teammate))
                continue;
            return false;
        }
        safe = floor.position;
        return true;
    }
}
