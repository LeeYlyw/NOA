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

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        if (player == null)
        {
            GameObject target = GameObject.FindGameObjectWithTag("Player");
            if (target != null)
            {
                player = target.transform;
            }
        }

        if (player == null)
        {
            Debug.LogError("Player 태그를 가진 오브젝트를 찾지 못했습니다.");
        }
    }

    void Update()
    {
        if (player == null || agent == null) return;

        // --- [추가] 은신 여부 확인 ---
        PlayerStealth pStealth = player.GetComponent<PlayerStealth>();
        if (pStealth != null && pStealth.isStealth)
        {
            // 플레이어가 은신 중이면 추격/공격을 멈추고 대기 상태로 전환
            agent.isStopped = true;
            if (animator != null)
            {
                animator.SetBool("isWalk", false);
                animator.SetBool("isAttack", false);
            }
            return; // 아래 추격 로직을 실행하지 않고 나감
        }

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= attackRange)
        {
            agent.isStopped = true;

            Vector3 dir = player.position - transform.position;
            dir.y = 0f;

            if (dir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotateSpeed);
            }

            if (animator != null)
            {
                animator.SetBool("isWalk", false);
                animator.SetBool("isAttack", true);
            }

            if (Time.time >= lastAttackTime + attackCooldown)
            {
                PlayerController playerController = player.GetComponent<PlayerController>();
                if (playerController != null)
                {
                    playerController.TakeDamage(damage);
                    lastAttackTime = Time.time;
                }
            }
        }
        else if (distance <= detectRange)
        {
            agent.isStopped = false;
            agent.SetDestination(player.position);

            if (animator != null)
            {
                animator.SetBool("isWalk", true);
                animator.SetBool("isAttack", false);
            }
        }
        else
        {
            agent.isStopped = true;

            if (animator != null)
            {
                animator.SetBool("isWalk", false);
                animator.SetBool("isAttack", false);
            }
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