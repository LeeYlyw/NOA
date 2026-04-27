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
        PlayerStealth pStealth = player.GetComponentInParent<PlayerStealth>();

        if (pStealth != null && pStealth.isStealth)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero; // 관성 제거

            if (animator != null)
            {
                animator.SetBool("isWalk", false);
                animator.SetBool("isAttack", false);
                // 몬스터가 즉시 멈추도록 애니메이션 강제 전환
                animator.Play("Idle");
            }
            return;
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