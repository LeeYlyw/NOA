using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 드럼통 조사는 현재 오프라인에서만 처리한다. 한 드럼통은 한 번만 조사한다.
[DisallowMultipleComponent]
public class BarrelInteraction : MonoBehaviour
{
    private static readonly HashSet<BarrelInteraction> Barrels = new HashSet<BarrelInteraction>();
    private static int lastUseFrame = -1;
    private static int closestFrame = -1;
    private static BarrelInteraction closestThisFrame;

    [Min(0.5f)] public float interactionRange = 2.5f;
    [Min(0.3f)] public float rewardDistance = 0.9f;

    private GameObject reward;
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
        if (reward != null) reward.SetActive(false);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F) || lastUseFrame == Time.frameCount ||
            ItemPickupInteraction.BlocksDoorInteraction ||
            CassetteInteraction.BlocksOtherInteraction || ClosestBarrel() != this) return;

        lastUseFrame = Time.frameCount;
        inspected = true;
        closestFrame = -1;
        if (reward != null)
        {
            PositionRewardTowardPlayer();
            reward.SetActive(true);
            reward = null;
            feedback = "아이템 발견";
        }
        else feedback = "비어 있음";
        feedbackUntil = Time.time + 1.5f;
    }

    private void PositionRewardTowardPlayer()
    {
        Transform player = NetworkClient.Instance.localPlayerTransform;
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
        direction.y = 0f;
        direction.Normalize();

        Vector3 target = transform.position + direction * rewardDistance;
        if (NavMesh.SamplePosition(target, out NavMeshHit floor, 1.5f, NavMesh.AllAreas))
            target.y = floor.position.y + 0.25f;
        else
            target.y = transform.parent != null ? transform.parent.position.y + 0.25f : 0.25f;
        reward.transform.position = target;
    }

    private void OnGUI()
    {
        if (ItemPickupInteraction.BlocksDoorInteraction ||
            CassetteInteraction.BlocksOtherInteraction) return;
        if (ClosestBarrel() == this)
            GUI.Box(new Rect(Screen.width * 0.5f - 85f, Screen.height * 0.5f + 70f, 170f, 36f),
                "F  드럼통 조사");
        else if (Time.time < feedbackUntil)
            GUI.Box(new Rect(Screen.width * 0.5f - 85f, Screen.height * 0.5f + 70f, 170f, 36f),
                feedback);
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
        if (network == null || !network.offlineMode || network.localPlayerTransform == null ||
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
