using UnityEngine;
using UnityEngine.AI;

public class MonsterChase : MonoBehaviour
{
    [Header("Target")]
    public Transform player;

    [Header("Range")]
    public float detectRange = 10f;
    public float attackRange = 2f;

    [Header("Vision")]
    [Range(0f, 360f)] public float sightAngle = 120f;
    public float eyeHeight = 1.5f;
    public LayerMask sightMask = ~0;

    [Header("Attack")]
    public int damage = 10;
    public float attackCooldown = 1.5f;
    private float lastAttackTime = 0f;

    [Header("Rotation")]
    public float rotateSpeed = 5f;

    private NavMeshAgent agent;
    private Animator animator;

    private bool wasAttacking = false;

    public float NetworkSpeed { get; private set; }
    public bool NetworkIsWalking { get; private set; }
    public bool NetworkIsAttacking { get; private set; }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (agent == null)
            return;

        player = FindNearestPlayer();

        if (player == null)
        {
            StopMonster();
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= attackRange)
        {
            AttackPlayer();
        }
        else if (distance <= detectRange)
        {
            ChasePlayer();
        }
        else
        {
            StopMonster();
        }
    }

    bool CanSeePlayer(Transform target)
    {
        if (target == null) return false;

        PlayerStealth stealth = target.GetComponent<PlayerStealth>();
        if (stealth != null && stealth.isStealth) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPoint = target.position + Vector3.up * eyeHeight;
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (distance > detectRange || distance <= 0.001f) return false;

        Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (planarDirection.sqrMagnitude > 0.001f &&
            Vector3.Angle(transform.forward, planarDirection) > sightAngle * 0.5f)
            return false;

        if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, sightMask, QueryTriggerInteraction.Ignore))
            return false;

        return hit.transform == target || hit.transform.IsChildOf(target);
    }

    Transform FindNearestPlayer()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

        Transform nearest = null;
        float nearestDistance = Mathf.Infinity;

        foreach (GameObject playerObject in players)
        {
            PlayerController playerController = playerObject.GetComponent<PlayerController>();

            if (playerController != null && playerController.IsDeadState)
            {
                continue;
            }

            PlayerStealth stealth = playerObject.GetComponent<PlayerStealth>();

            if (stealth != null && stealth.isStealth)
            {
                continue;
            }

            if (!CanSeePlayer(playerObject.transform))
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, playerObject.transform.position);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = playerObject.transform;
            }
        }

        return nearest;
    }

    void ChasePlayer()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;

        agent.isStopped = false;
        agent.SetDestination(player.position);

        wasAttacking = false;

        NetworkSpeed = agent.velocity.magnitude;
        NetworkIsWalking = true;
        NetworkIsAttacking = false;

        if (animator != null)
        {
            animator.SetFloat("Speed", 1f);
            animator.SetBool("isWalk", true);
            animator.SetBool("isAttack", false);
        }
    }

    void AttackPlayer()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        Vector3 dir = player.position - transform.position;
        dir.y = 0f;

        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                Time.deltaTime * rotateSpeed
            );
        }

        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
            animator.SetBool("isWalk", false);
            animator.SetBool("isAttack", true);

            if (!wasAttacking)
            {
                animator.SetTrigger("Attack");
                wasAttacking = true;
            }
        }

        NetworkSpeed = 0f;
        NetworkIsWalking = false;
        NetworkIsAttacking = true;

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            int targetPlayerId = GetPlayerIdFromTarget(player);

            if (targetPlayerId != -1)
            {
                // [오프라인 모드 대응] 서버가 없을 때는 로컬 플레이어 체력을 즉시 깎음
                if (NetworkClient.Instance != null && NetworkClient.Instance.offlineMode)
                {
                    PlayerController pc = player.GetComponent<PlayerController>();
                    if (pc != null) pc.TakeDamage(damage);
                }

                lastAttackTime = Time.time;
            }
        }
    }

    int GetPlayerIdFromTarget(Transform target)
    {
        if (target == null)
            return -1;

        PlayerRoleSetup roleSetup = target.GetComponent<PlayerRoleSetup>();

        if (roleSetup != null)
        {
            return roleSetup.ownerClientId;
        }

        Debug.LogWarning("공격 대상에서 PlayerRoleSetup을 찾지 못했습니다: " + target.name);
        return -1;
    }

    void StopMonster()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        wasAttacking = false;

        NetworkSpeed = 0f;
        NetworkIsWalking = false;
        NetworkIsAttacking = false;

        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
            animator.SetBool("isWalk", false);
            animator.SetBool("isAttack", false);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
