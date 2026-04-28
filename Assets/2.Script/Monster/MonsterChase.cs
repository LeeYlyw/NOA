using UnityEngine;
using UnityEngine.AI;

public class MonsterChase : MonoBehaviour
{
    [Header("Target")]
    public Transform player;

    [Header("Range")]
    public float detectRange = 10f;
    public float attackRange = 2f;

    [Header("Attack")]
    public int damage = 10;
    public float attackCooldown = 1.5f;
    private float lastAttackTime = 0f;

    [Header("Rotation")]
    public float rotateSpeed = 5f;

    private NavMeshAgent agent;
    private Animator animator;

    private bool wasAttacking = false;

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

    Transform FindNearestPlayer()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

        Transform nearest = null;
        float nearestDistance = Mathf.Infinity;

        foreach (GameObject playerObject in players)
        {
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

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            int targetPlayerId = GetPlayerIdFromTarget(player);

            if (targetPlayerId != -1)
            {
                if (NetworkClient.Instance != null)
                {
                    NetworkClient.Instance.SendPlayerDamage(targetPlayerId, damage);
                }

                lastAttackTime = Time.time;
            }
        }
    }

    int GetPlayerIdFromTarget(Transform target)
    {
        if (target == null)
            return -1;

        string targetName = target.name.ToLower();

        if (targetName.Contains("player1"))
            return 1;

        if (targetName.Contains("player2"))
            return 2;

        return -1;
    }

    void StopMonster()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        wasAttacking = false;

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