using System.Collections.Generic;
using UnityEngine;

// 온라인 조사는 서버 확정 상태를 따른다. 한 드럼통은 한 번만 조사한다.
[DisallowMultipleComponent]
public class BarrelInteraction : MonoBehaviour
{
    private static readonly HashSet<BarrelInteraction> Barrels = new HashSet<BarrelInteraction>();
    private static int lastUseFrame = -1;
    private static int closestFrame = -1;
    private static BarrelInteraction closestThisFrame;

    [Min(0.5f)] public float interactionRange = 2.5f;
    [Min(0f)] public float rewardTopGap = 0.08f;
    public int NetworkId { get; set; }
    public int RewardId => reward != null ? reward.GetComponent<PotionItem>()?.itemId ?? 0 : 0;

    private GameObject reward;
    private float rewardBottomOffset;
    private bool inspected;
    private string feedback;
    private float feedbackUntil;

    private void OnEnable()
    {
        Barrels.Add(this);
        closestFrame = -1;
    }

    private void OnDisable()
    {
        Barrels.Remove(this);
        closestFrame = -1;
    }

    public void SetReward(GameObject item)
    {
        reward = item;
        if (reward == null) return;

        // 아이템의 피벗이 모델 바닥과 다를 수 있으므로 렌더러의 아래쪽을 기록한다.
        float bottom = float.PositiveInfinity;
        foreach (Renderer renderer in reward.GetComponentsInChildren<Renderer>(true))
            bottom = Mathf.Min(bottom, renderer.bounds.min.y);
        rewardBottomOffset = float.IsPositiveInfinity(bottom)
            ? 0f : reward.transform.position.y - bottom;
        PositionRewardOnTop();
        reward.SetActive(false);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F) || lastUseFrame == Time.frameCount ||
            ItemPickupInteraction.BlocksDoorInteraction ||
            CassetteInteraction.BlocksOtherInteraction || ClosestBarrel() != this) return;

        lastUseFrame = Time.frameCount;
        NetworkClient network = NetworkClient.Instance;
        if (network != null && !network.offlineMode)
        {
            network.SendPropUse("Barrel", NetworkId);
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
            PositionRewardOnTop();
            if (!reward.GetComponent<PotionItem>().IsPicked) reward.SetActive(true);
            feedback = "아이템 발견";
        }
        else feedback = "비어 있음";
        feedbackUntil = Time.time + 1.5f;
    }

    private void PositionRewardOnTop()
    {
        // 실제 드럼통 윗면 높이를 사용해 모델끼리 겹치지 않게 올린다.
        Bounds barrelBounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!hasBounds) { barrelBounds = renderer.bounds; hasBounds = true; }
            else barrelBounds.Encapsulate(renderer.bounds);
        }
        if (!hasBounds)
            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            {
                if (!hasBounds) { barrelBounds = collider.bounds; hasBounds = true; }
                else barrelBounds.Encapsulate(collider.bounds);
            }
        if (!hasBounds) barrelBounds = new Bounds(transform.position, Vector3.one);

        float top = barrelBounds.max.y;
        reward.transform.position = new Vector3(barrelBounds.center.x,
            top + rewardTopGap + rewardBottomOffset, barrelBounds.center.z);

        // 아이템은 위에 보이되, 플레이어가 드럼통 옆에서 F 홀드로 주울 수 있게 한다.
        SphereCollider reach = reward.GetComponent<SphereCollider>();
        if (reach == null) reach = reward.AddComponent<SphereCollider>();
        reach.isTrigger = true;
        Vector3 reachCenter = new Vector3(barrelBounds.center.x, top - 0.9f,
            barrelBounds.center.z);
        reach.center = reward.transform.InverseTransformPoint(reachCenter);
        Vector3 scale = reward.transform.lossyScale;
        float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        reach.radius = 1.2f / Mathf.Max(0.01f, maxScale);
    }

    private void OnGUI()
    {
        if (ItemPickupInteraction.BlocksDoorInteraction ||
            CassetteInteraction.BlocksOtherInteraction) return;
        if (ClosestBarrel() == this)
            InteractionPrompt.Draw("F  드럼통 조사");
        else if (Time.time < feedbackUntil)
            InteractionPrompt.Draw(feedback);
    }

    // 드럼통을 조사하는 F 입력이 의자나 문도 작동시키지 않도록 한다.
    public static bool BlocksOtherInteraction => lastUseFrame == Time.frameCount ||
        (ClosestBarrel() != null && !ItemPickupInteraction.BlocksDoorInteraction &&
         !CassetteInteraction.BlocksOtherInteraction);

    private static BarrelInteraction ClosestBarrel()
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
        foreach (BarrelInteraction barrel in Barrels)
        {
            if (barrel == null || !barrel.isActiveAndEnabled || barrel.inspected) continue;
            float distance = (barrel.transform.position - player.position).sqrMagnitude;
            if (distance > barrel.interactionRange * barrel.interactionRange || distance >= nearest) continue;
            nearest = distance;
            closestThisFrame = barrel;
        }
        return closestThisFrame;
    }
}
