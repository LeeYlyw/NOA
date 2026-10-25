using System.Collections.Generic;
using UnityEngine;

public class HealerSkill : MonoBehaviour
{
    [Header("Skill Settings")]
    public float healRadius = 5f;
    public int healAmount = 30;
    public float skillCooldown = 10f;

    private float currentCooldown;
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
        if (roleSetup == null || roleSetup.role != PlayerRole.Healer ||
            !roleSetup.IsLocalOwner || playerController == null || playerController.IsDeadState)
            return;

        if (currentCooldown > 0f)
        {
            currentCooldown = Mathf.Max(0f, currentCooldown - Time.deltaTime);
            return;
        }

        if (Input.GetKeyDown(KeyCode.E))
            UseHealSkill();
    }

    void UseHealSkill()
    {
        if (healRadius <= 0f || healAmount <= 0)
            return;

        NetworkClient networkClient = NetworkClient.Instance;
        HashSet<PlayerController> targets = new HashSet<PlayerController>();
        bool healedAnyone = false;

        foreach (Collider hit in Physics.OverlapSphere(transform.position, healRadius))
        {
            PlayerController target = hit.GetComponentInParent<PlayerController>();
            if (target == null || target.IsDeadState || !targets.Add(target))
                continue;

            if (networkClient != null && !networkClient.offlineMode)
            {
                PlayerRoleSetup targetRole = target.GetComponent<PlayerRoleSetup>();
                if (targetRole == null || targetRole.ownerClientId < 1)
                    continue;

                networkClient.SendPlayerHealRequest(targetRole.ownerClientId, healAmount);
            }
            else
            {
                target.SetHp(target.CurrentHp + healAmount);
            }

            healedAnyone = true;
        }

        if (healedAnyone)
            currentCooldown = skillCooldown;
    }
}
