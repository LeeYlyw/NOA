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

        float distance = Vector3.Distance(transform.position, player.position);

        // 공격 범위 안
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

            // 애니메이션이 아직 없어도 null 체크 때문에 문제 없음
            if (animator != null)
            {
                animator.SetBool("isWalk", false);
                animator.SetBool("isAttack", true);
            }

            if (Time.time >= lastAttackTime + attackCooldown)
            {
                Health Health = player.GetComponent<Health>();
                if (Health != null)
                {
                    Health.TakeDamage(damage);
                    lastAttackTime = Time.time;
                }
            }
        }
        // 감지 범위 안
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
        // 감지 범위 밖
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