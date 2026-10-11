using UnityEngine;
using UnityEngine.AI;

public class SoundMonsterAI : MonoBehaviour
{
    public enum State
    {
        Idle,
        Investigate,
        Chase,
        Attack, // 공격 상태 추가
        Return
    }

    public NavMeshAgent agent;
    public Transform player;
    private Animator animator; // 애니메이터 추가

    [Header("Hearing")]
    public float hearingMultiplier = 2f;
    public float chaseNoiseThreshold = 7f;

    [Header("Vision")]
    public float sightRange = 6f;
    public float loseSightTime = 3f;

    [Header("Investigate")]
    public float investigateWaitTime = 2f;

    [Header("Attack (Newly Added)")]
    public float attackRange = 2f;
    public int damage = 10;
    public float attackCooldown = 1.5f;
    public float rotateSpeed = 5f;
    private float lastAttackTime = 0f;
    private bool wasAttacking = false;

    private State currentState = State.Idle;
    private Vector3 startPosition;
    private Vector3 investigatePosition;
    private float investigateTimer;
    private float lostSightTimer;

    void OnEnable()
    {
        PlayerNoiseEmitter.OnNoiseEmitted += OnNoiseHeard;
    }

    void OnDisable()
    {
        PlayerNoiseEmitter.OnNoiseEmitted -= OnNoiseHeard;
    }

    void Start()
    {
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        animator = GetComponent<Animator>();
        startPosition = transform.position;
    }

    void Update()
    {
        if (agent == null) return;

        switch (currentState)
        {
            case State.Idle:
                UpdateIdle();
                break;
            case State.Investigate:
                UpdateInvestigate();
                break;
            case State.Chase:
                UpdateChase();
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Return:
                UpdateReturn();
                break;
        }
    }

    void UpdateIdle()
    {
        SetAnimationState(0f, false, false);

        if (CanSeePlayer())
            currentState = State.Chase;
    }

    void UpdateInvestigate()
    {
        SetAnimationState(1f, true, false);

        if (CanSeePlayer())
        {
            currentState = State.Chase;
            return;
        }

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(investigatePosition);

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            {
                investigateTimer += Time.deltaTime;
                if (investigateTimer >= investigateWaitTime)
                {
                    investigateTimer = 0f;
                    currentState = State.Return;
                }
            }
        }
    }

    void UpdateChase()
    {
        if (player == null)
        {
            currentState = State.Return;
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);

        // 공격 범위 내 진입 시 공격 상태로 전환
        if (distance <= attackRange)
        {
            currentState = State.Attack;
            return;
        }

        SetAnimationState(1f, true, false);

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(player.position);
        }

        if (CanSeePlayer())
        {
            lostSightTimer = 0f;
        }
        else
        {
            lostSightTimer += Time.deltaTime;
            if (lostSightTimer >= loseSightTime)
            {
                lostSightTimer = 0f;
                currentState = State.Return;
            }
        }
    }

    void UpdateAttack()
    {
        if (player == null)
        {
            currentState = State.Return;
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > attackRange)
        {
            wasAttacking = false;
            currentState = State.Chase;
            return;
        }

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        // 플레이어를 향해 부드럽게 회전
        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotateSpeed);
        }

        // 공격 애니메이션
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

        // 공격 쿨다운 및 데미지 로직 (오프라인 모드 대응 포함)
        if (Time.time >= lastAttackTime + attackCooldown)
        {
            PlayerRoleSetup roleSetup = player.GetComponent<PlayerRoleSetup>();
            if (roleSetup != null)
            {
                if (NetworkClient.Instance != null && NetworkClient.Instance.offlineMode)
                {
                    PlayerController pc = player.GetComponent<PlayerController>();
                    if (pc != null) pc.TakeDamage(damage);
                }
                lastAttackTime = Time.time;
            }
        }
    }

    void UpdateReturn()
    {
        SetAnimationState(1f, true, false);

        if (CanSeePlayer())
        {
            currentState = State.Chase;
            return;
        }

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(startPosition);

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            {
                currentState = State.Idle;
            }
        }
    }

    void OnNoiseHeard(Vector3 noisePos, float noiseAmount, GameObject source)
    {
        float distance = Vector3.Distance(transform.position, noisePos);
        float hearingRange = noiseAmount * hearingMultiplier;

        if (distance > hearingRange) return;

        investigatePosition = noisePos;
        investigateTimer = 0f;

        if (source.CompareTag("Player"))
            player = source.transform;

        if (noiseAmount >= chaseNoiseThreshold)
            currentState = State.Chase;
        else
            currentState = State.Investigate;
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        // 은신 중인 플레이어는 보지 못한 것으로 처리
        PlayerStealth stealth = player.GetComponent<PlayerStealth>();
        if (stealth != null && stealth.isStealth)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > sightRange) return false;

        Vector3 origin = transform.position + Vector3.up * 1.5f;
        Vector3 dir = (player.position - origin).normalized;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, sightRange))
        {
            return hit.transform.CompareTag("Player");
        }

        return false;
    }

    void SetAnimationState(float speed, bool isWalk, bool isAttack)
    {
        wasAttacking = false;
        if (animator != null)
        {
            animator.SetFloat("Speed", speed);
            animator.SetBool("isWalk", isWalk);
            animator.SetBool("isAttack", isAttack);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange); // 시야 범위

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange); // 공격 범위
    }
}