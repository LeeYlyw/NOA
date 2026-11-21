using System.Collections.Generic;
using UnityEngine;

// 온라인 조사는 서버 확정 상태를 따른다. 드럼통처럼 방마다 침대 하나만 보상을 가진다.
[DisallowMultipleComponent]
public class BedInteraction : MonoBehaviour
{
    private static readonly HashSet<BedInteraction> Beds = new HashSet<BedInteraction>();
    private static int lastUseFrame = -1;
    private static int closestFrame = -1;
    private static BedInteraction closestThisFrame;

    [Min(0.5f)] public float interactionRange = 2.5f;
    [Min(0f)] public float rewardTopGap = 0.08f;
    public int NetworkId { get; set; }
    public int RewardId => reward != null ? reward.GetComponent<PotionItem>()?.itemId ?? 0 : 0;
    public Vector3 InteractionAnchor => hasVisualBounds ? visualBounds.center : transform.position;

    private Bounds visualBounds;
    private bool hasVisualBounds;
    private GameObject reward;
    private float rewardBottomOffset;
    private bool inspected;
    private string feedback;
    private float feedbackUntil;

    public void Configure(Bounds bounds)
    {
        visualBounds = bounds;
        hasVisualBounds = true;
    }

    private void OnEnable()
    {
        Beds.Add(this);
        closestFrame = -1;
    }

    private void OnDisable()
    {
        Beds.Remove(this);
        closestFrame = -1;
    }

    public void SetReward(GameObject item)
    {
        reward = item;
        if (reward == null) return;
        float bottom = float.PositiveInfinity;
        foreach (Renderer renderer in reward.GetComponentsInChildren<Renderer>(true))
            bottom = Mathf.Min(bottom, renderer.bounds.min.y);
        rewardBottomOffset = float.IsPositiveInfinity(bottom)
            ? 0f : reward.transform.position.y - bottom;
        PositionRewardOnMattress();
        reward.SetActive(false);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F) || lastUseFrame == Time.frameCount ||
            ItemPickupInteraction.BlocksDoorInteraction ||
            CassetteInteraction.BlocksOtherInteraction ||
            BarrelInteraction.BlocksOtherInteraction ||
            CabinetInteraction.BlocksOtherInteraction || ClosestBed() != this) return;

        lastUseFrame = Time.frameCount;
        NetworkClient network = NetworkClient.Instance;
        if (network != null && !network.offlineMode)
        {
            network.SendPropUse("Bed", NetworkId);
            return;
        }
        ApplyServerState();
    }

    public void ApplyServerState()
    {
        if (inspected) return;
        inspected = true;
        closestFrame = -1;
        if (reward != null)
        {
            PositionRewardOnMattress();
            if (!reward.GetComponent<PotionItem>().IsPicked) reward.SetActive(true);
            feedback = "아이템 발견";
        }
        else feedback = "비어 있음";
        feedbackUntil = Time.time + 1.5f;
    }

    private void PositionRewardOnMattress()
    {
        Bounds bounds = hasVisualBounds ? visualBounds : new Bounds(transform.position, Vector3.one);
        Vector3 center = bounds.center;
        float surfaceY = bounds.max.y;

        // 외형 최상단은 침대 머리판일 수 있어, 중앙 아래로 쏜 광선으로 매트리스 높이를 찾는다.
        Ray down = new Ray(new Vector3(center.x, bounds.max.y + 1f, center.z), Vector3.down);
        float bestDistance = float.PositiveInfinity;
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            if (collider.enabled && !collider.isTrigger &&
                collider.Raycast(down, out RaycastHit hit, bounds.size.y + 2f) &&
                hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                surfaceY = hit.point.y;
            }

        reward.transform.position = new Vector3(center.x,
            surfaceY + rewardTopGap + rewardBottomOffset, center.z);

        // 아이템은 매트리스 위에 보이고, 플레이어는 침대 옆에서 F 홀드로 획득한다.
        SphereCollider reach = reward.GetComponent<SphereCollider>();
        if (reach == null) reach = reward.AddComponent<SphereCollider>();
        reach.isTrigger = true;
        Vector3 reachCenter = new Vector3(center.x, surfaceY - 0.3f, center.z);
        reach.center = reward.transform.InverseTransformPoint(reachCenter);
        Vector3 scale = reward.transform.lossyScale;
        float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        reach.radius = 1.4f / Mathf.Max(0.01f, maxScale);
    }

    private void OnGUI()
    {
        if (ItemPickupInteraction.BlocksDoorInteraction ||
            CassetteInteraction.BlocksOtherInteraction ||
            BarrelInteraction.BlocksOtherInteraction ||
            CabinetInteraction.BlocksOtherInteraction) return;
        if (ClosestBed() == this)
            InteractionPrompt.Draw("F  침대 조사");
        else if (Time.time < feedbackUntil)
            InteractionPrompt.Draw(feedback);
    }

    public static bool BlocksOtherInteraction => lastUseFrame == Time.frameCount ||
        (ClosestBed() != null && !ItemPickupInteraction.BlocksDoorInteraction &&
         !CassetteInteraction.BlocksOtherInteraction &&
         !BarrelInteraction.BlocksOtherInteraction);

    private static BedInteraction ClosestBed()
    {
        if (closestFrame == Time.frameCount) return closestThisFrame;
        closestFrame = Time.frameCount;
        closestThisFrame = null;

        NetworkClient network = NetworkClient.Instance;
        if (network == null || network.localPlayerTransform == null ||
            (!network.offlineMode && (!network.GameStarted || !network.IsConnected)) ||
            Cursor.lockState != CursorLockMode.Locked ||
            (ClueManager.instance != null && ClueManager.instance.IsEnding)) return null;
        Transform player = network.localPlayerTransform;
        PlayerController controller = player.GetComponent<PlayerController>();
        if (controller == null || !controller.isLocalPlayer || controller.IsDeadState) return null;

        float nearest = float.PositiveInfinity;
        foreach (BedInteraction bed in Beds)
        {
            if (bed == null || !bed.isActiveAndEnabled || bed.inspected || !bed.hasVisualBounds) continue;
            float distance = (bed.visualBounds.ClosestPoint(player.position) - player.position).sqrMagnitude;
            if (distance > bed.interactionRange * bed.interactionRange || distance >= nearest) continue;
            nearest = distance;
            closestThisFrame = bed;
        }
        return closestThisFrame;
    }
}
