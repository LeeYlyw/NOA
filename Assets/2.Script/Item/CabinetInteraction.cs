using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A reusable two-door cabinet. The map generator may assign one hidden pickup.
[DisallowMultipleComponent]
public class CabinetInteraction : MonoBehaviour
{
    private static readonly HashSet<CabinetInteraction> Cabinets = new HashSet<CabinetInteraction>();
    private static int lastUseFrame = -1;
    private static int closestFrame = -1;
    private static CabinetInteraction closestThisFrame;

    [Min(0.5f)] public float interactionRange = 2.6f;
    [Min(0.05f)] public float openDuration = 0.45f;
    [Range(30f, 150f)] public float openAngle = 105f;
    public Transform leftHinge;
    public Transform rightHinge;
    public Transform rewardPoint;
    public int NetworkId { get; set; }
    public int RewardId => rewardItem != null ? rewardItem.itemId : 0;

    private GameObject reward;
    private PotionItem rewardItem;
    private Coroutine doorMotion;
    private bool isOpen;
    private bool openedOnce;
    private string feedback;
    private float feedbackUntil;

    public Vector3 RewardPosition => rewardPoint != null ? rewardPoint.position :
        transform.TransformPoint(new Vector3(0f, 1.05f, 0.12f));

    private void OnEnable()
    {
        Cabinets.Add(this);
        closestFrame = -1;
    }

    private void OnDisable()
    {
        Cabinets.Remove(this);
        closestFrame = -1;
        if (doorMotion != null) StopCoroutine(doorMotion);
        doorMotion = null;
    }

    public void SetReward(GameObject item)
    {
        reward = item;
        rewardItem = item != null ? item.GetComponent<PotionItem>() : null;
        if (reward == null) return;
        reward.transform.position = RewardPosition;

        // Keep the item on the inner shelf, but allow pickup from in front of the doors.
        SphereCollider reach = reward.AddComponent<SphereCollider>();
        reach.isTrigger = true;
        reach.center = reward.transform.InverseTransformPoint(
            transform.TransformPoint(new Vector3(0f, 1.05f, 0.75f)));
        Vector3 scale = reward.transform.lossyScale;
        float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        reach.radius = 0.8f / Mathf.Max(0.01f, maxScale);
        reward.SetActive(false);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F) || lastUseFrame == Time.frameCount ||
            ItemPickupInteraction.BlocksDoorInteraction || ClosestCabinet() != this) return;

        lastUseFrame = Time.frameCount;
        if (NetworkClient.Instance != null && !NetworkClient.Instance.offlineMode)
        {
            NetworkClient.Instance.SendPropUse("Cabinet", NetworkId, !isOpen);
            return;
        }
        ApplyServerState(!isOpen);
    }

    public void ApplyServerState(bool opening)
    {
        isOpen = opening;
        closestFrame = -1;
        if (doorMotion != null) StopCoroutine(doorMotion);
        if (!isOpen && reward != null && (rewardItem == null || !rewardItem.IsPicked))
            reward.SetActive(false);
        doorMotion = StartCoroutine(AnimateDoors(isOpen));
    }

    private IEnumerator AnimateDoors(bool opening)
    {
        Quaternion leftStart = leftHinge != null ? leftHinge.localRotation : Quaternion.identity;
        Quaternion rightStart = rightHinge != null ? rightHinge.localRotation : Quaternion.identity;
        Quaternion leftTarget = Quaternion.Euler(0f, opening ? -openAngle : 0f, 0f);
        Quaternion rightTarget = Quaternion.Euler(0f, opening ? openAngle : 0f, 0f);
        float elapsed = 0f;
        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / openDuration));
            if (leftHinge != null) leftHinge.localRotation = Quaternion.Slerp(leftStart, leftTarget, t);
            if (rightHinge != null) rightHinge.localRotation = Quaternion.Slerp(rightStart, rightTarget, t);
            yield return null;
        }
        if (leftHinge != null) leftHinge.localRotation = leftTarget;
        if (rightHinge != null) rightHinge.localRotation = rightTarget;
        doorMotion = null;
        if (!opening) yield break;

        if (reward != null && (rewardItem == null || !rewardItem.IsPicked))
            reward.SetActive(true);
        if (!openedOnce)
        {
            openedOnce = true;
            feedback = reward != null ? "아이템 발견" : "비어 있음";
            feedbackUntil = Time.time + 1.5f;
        }
    }

    private void OnGUI()
    {
        if (ItemPickupInteraction.BlocksDoorInteraction) return;
        if (ClosestCabinet() == this)
            InteractionPrompt.Draw("F", isOpen ? "캐비닛 닫기" : "캐비닛 열기");
        else if (Time.time < feedbackUntil)
            InteractionPrompt.Draw(feedback);
    }

    public static bool BlocksOtherInteraction => lastUseFrame == Time.frameCount ||
        (ClosestCabinet() != null && !ItemPickupInteraction.BlocksDoorInteraction);

    private static CabinetInteraction ClosestCabinet()
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
        foreach (CabinetInteraction cabinet in Cabinets)
        {
            if (cabinet == null || !cabinet.isActiveAndEnabled) continue;
            Vector3 front = cabinet.transform.TransformPoint(new Vector3(0f, 1f, 0.4f));
            float distance = (front - player.position).sqrMagnitude;
            if (distance > cabinet.interactionRange * cabinet.interactionRange || distance >= nearest) continue;
            nearest = distance;
            closestThisFrame = cabinet;
        }
        return closestThisFrame;
    }
}
