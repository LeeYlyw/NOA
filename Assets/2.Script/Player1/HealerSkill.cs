using System.Collections.Generic;
using UnityEngine;

public class HealerSkill : MonoBehaviour
{
    [Header("Skill Settings")]
    public float healRadius = 5f;
    public int healAmount = 30;
    public float skillCooldown = 10f;

    private float currentCooldown;
    private bool awaitingServerResult;
    private float requestTimeout;
    public float RemainingCooldown => currentCooldown;
    private PlayerRoleSetup roleSetup;
    private PlayerController playerController;

    void Awake()
    {
        roleSetup = GetComponent<PlayerRoleSetup>();
        playerController = GetComponent<PlayerController>();
    }

    void Update()
    {
        if (awaitingServerResult && Time.unscaledTime >= requestTimeout)
            awaitingServerResult = false;

        if (roleSetup == null || roleSetup.role != PlayerRole.Healer ||
            !roleSetup.IsLocalOwner || playerController == null || playerController.IsDeadState)
            return;

        if (currentCooldown > 0f)
        {
            currentCooldown = Mathf.Max(0f, currentCooldown - Time.deltaTime);
            return;
        }

        if (!awaitingServerResult && Input.GetKeyDown(KeyCode.E))
            UseHealSkill();
    }

    void UseHealSkill()
    {
        if (healRadius <= 0f || healAmount <= 0)
            return;

        NetworkClient networkClient = NetworkClient.Instance;
        if (networkClient != null && !networkClient.offlineMode)
        {
            if (networkClient.SendPlayerHealRequest())
            {
                awaitingServerResult = true;
                requestTimeout = Time.unscaledTime + 3f;
            }
            return;
        }

        HashSet<PlayerController> targets = new HashSet<PlayerController>();
        bool healedAnyone = false;

        targets.Add(playerController);

        foreach (Collider hit in Physics.OverlapSphere(transform.position, healRadius))
        {
            PlayerController target = hit.GetComponentInParent<PlayerController>();
            if (target == null || target.IsDeadState)
                continue;
            targets.Add(target);
        }

        foreach (PlayerController target in targets)
        {
            if (target.IsDeadState) continue;
            int previousHp = target.CurrentHp;
            target.SetHp(previousHp + healAmount);
            healedAnyone |= target.CurrentHp > previousHp;
        }

        if (healedAnyone)
            currentCooldown = skillCooldown;
    }

    public void OnServerHealResult(bool accepted)
    {
        awaitingServerResult = false;
        if (accepted)
            currentCooldown = skillCooldown;
    }
}
