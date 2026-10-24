using UnityEngine;
using UnityEngine.AI;

public class SoundMonsterAI : MonoBehaviour
{
    public enum State
    {
        Idle,
        Investigate,
        Chase,
        Attack
    }

    public NavMeshAgent agent;
    public Transform player;
    private Animator animator; // 애니메이터 추가

    [Header("Hearing")]
    public float hearingMultiplier = 2f;
    public float chaseNoiseThreshold = 7f;

    [Header("Vision")]
    public float sightRange = 3.5f;
    [Range(0f, 360f)] public float sightAngle = 60f;
    public float loseSightTime = 3f;

    [Header("Patrol and Movement")]
    public float patrolRadius = 12f;
    public float patrolPause = 1.5f;
    public float patrolSpeed = 1.3f;
    public float chaseSpeed = 3.5f;
    public float searchSpeed = 2f;

    [Header("Investigate")]
    public float investigateWaitTime = 2f;
    public float searchDuration = 6f;
    public float searchRadius = 3f;

    [Header("Attack (Newly Added)")]
    public float attackRange = 2f;
    public int damage = 10;
    public float attackCooldown = 1.5f;
    public float rotateSpeed = 5f;
    private float lastAttackTime = 0f;

    private State currentState = State.Idle;
    private Vector3 investigatePosition;
    private float investigateTimer;
    private float activeSearchDuration;
    private float lostSightTimer;
    private Vector3 lastSeenPosition;
    private bool searchingAround;
    private bool patrolWaiting = true;
    private float pauseUntil;

    public float NetworkSpeed { get; private set; }
    public bool NetworkIsWalking { get; private set; }
    public bool NetworkIsAttacking { get; private set; }

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
        if (animator == null) animator = GetComponentInChildren<Animator>();
        pauseUntil = Time.time + patrolPause;
    }

    void Update()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Transform visible = FindNearestVisiblePlayer();
        if (visible != null)
        {
            player = visible;
            lastSeenPosition = visible.position;
            lostSightTimer = 0f;
            currentState = Vector3.Distance(transform.position, visible.position) <= attackRange
                ? State.Attack : State.Chase;
        }

        agent.speed = currentState == State.Chase || currentState == State.Attack ? chaseSpeed :
                      currentState == State.Investigate ? searchSpeed : patrolSpeed;

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
        }
    }

    void UpdateIdle()
    {
        if (patrolWaiting)
        {
            agent.isStopped = true;
            SetAnimationState(0f, false, false);
            transform.Rotate(0f, 30f * Time.deltaTime, 0f);
            if (Time.time >= pauseUntil)
            {
                patrolWaiting = false;
                Vector3 destination;
                if (TryFindReachablePoint(transform.position, patrolRadius, out destination))
                {
                    agent.isStopped = false;
                    agent.SetDestination(destination);
                }
                else
                {
                    patrolWaiting = true;
                    pauseUntil = Time.time + patrolPause;
                }
            }
            return;
        }

        if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.2f))
        {
            patrolWaiting = true;
            pauseUntil = Time.time + patrolPause;
            agent.isStopped = true;
        }
        bool walking = !agent.isStopped && agent.velocity.sqrMagnitude > 0.01f;
        SetAnimationState(walking ? 1f : 0f, walking, false);
    }

    void UpdateInvestigate()
    {
        if (CanSeePlayer())
        {
            lastSeenPosition = player.position;
            lostSightTimer = 0f;
            currentState = State.Chase;
            return;
        }

        agent.isStopped = false;
        if (!searchingAround && !agent.pathPending &&
            (!agent.hasPath || Vector3.Distance(agent.destination, investigatePosition) > 0.3f))
            agent.SetDestination(investigatePosition);

        if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.2f))
        {
            if (!searchingAround)
            {
                searchingAround = true;
                investigateTimer = Time.time + Mathf.Max(investigateWaitTime, activeSearchDuration);
            }
            if (Time.time >= investigateTimer)
            {
                player = null;
                currentState = State.Idle;
                patrolWaiting = true;
                pauseUntil = Time.time + patrolPause;
                return;
            }
            Vector3 destination;
            if (TryFindReachablePoint(investigatePosition, searchRadius, out destination))
                agent.SetDestination(destination);
        }

        SetAnimationState(agent.velocity.sqrMagnitude > 0.01f ? 1f : 0f,
                          agent.velocity.sqrMagnitude > 0.01f, false);
    }

    void UpdateChase()
    {
        if (player == null)
        {
            currentState = State.Idle;
            patrolWaiting = true;
            return;
        }

        bool visible = CanSeePlayer();
        if (visible)
        {
            lastSeenPosition = player.position;
            lostSightTimer = 0f;
        }
        else
        {
            lostSightTimer += Time.deltaTime;
            if (lostSightTimer >= loseSightTime)
            {
                investigatePosition = lastSeenPosition;
                searchingAround = false;
                activeSearchDuration = searchDuration;
                currentState = State.Investigate;
                return;
            }
        }

        float distance = Vector3.Distance(transform.position, player.position);

        if (visible && distance <= attackRange)
        {
            currentState = State.Attack;
            return;
        }

        SetAnimationState(1f, true, false);

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(lastSeenPosition);
        }
    }

    void UpdateAttack()
    {
        if (player == null)
        {
            currentState = State.Idle;
            patrolWaiting = true;
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > attackRange || !CanSeePlayer())
        {
            currentState = State.Chase;
            SetAnimationState(0f, false, false);
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

        }

        NetworkSpeed = 0f;
        NetworkIsWalking = false;
        NetworkIsAttacking = true;

        // 공격 쿨다운 및 데미지 로직 (오프라인 모드 대응 포함)
        if (Time.time >= lastAttackTime + attackCooldown)
        {
            PlayerRoleSetup roleSetup = player.GetComponent<PlayerRoleSetup>();
            if (roleSetup != null)
            {
                if (animator != null) animator.SetTrigger("Attack");
                if (NetworkClient.Instance != null && NetworkClient.Instance.offlineMode)
                {
                    PlayerController pc = player.GetComponent<PlayerController>();
                    if (pc != null) pc.TakeDamage(damage);
                }
                lastAttackTime = Time.time;
            }
        }
    }

    void OnNoiseHeard(Vector3 noisePos, float noiseAmount, GameObject source)
    {
        float distance = Vector3.Distance(transform.position, noisePos);
        float hearingRange = noiseAmount * hearingMultiplier;

        if (distance > hearingRange) return;

        // Once the target is in sight, footsteps must not interrupt an attack or chase.
        if ((currentState == State.Chase || currentState == State.Attack) && CanSeePlayer()) return;

        investigatePosition = noisePos;
        searchingAround = false;
        activeSearchDuration = searchDuration + (noiseAmount >= chaseNoiseThreshold ? 2f : 0f);

        if (source != null && source.CompareTag("Player"))
            player = source.transform;

        // Noise provides a location, not continuous knowledge of the player's position.
        currentState = State.Investigate;
    }

    public void HearNetworkNoise(Vector3 noisePos, float noiseAmount, GameObject source)
    {
        OnNoiseHeard(noisePos, noiseAmount, source);
    }

    bool TryFindReachablePoint(Vector3 center, float radius, out Vector3 destination)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(center + new Vector3(offset.x, 0f, offset.y),
                                        out hit, 1.5f, agent.areaMask)) continue;
            NavMeshPath path = new NavMeshPath();
            if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                destination = hit.position;
                return true;
            }
        }
        destination = center;
        return false;
    }

    Transform FindNearestVisiblePlayer()
    {
        Transform nearest = null;
        float nearestDistance = Mathf.Infinity;
        foreach (GameObject candidate in GameObject.FindGameObjectsWithTag("Player"))
        {
            PlayerController controller = candidate.GetComponent<PlayerController>();
            if (controller == null || controller.IsDeadState) continue;
            if (!CanSeePlayer(candidate.transform)) continue;
            float distance = Vector3.Distance(transform.position, candidate.transform.position);
            if (distance >= nearestDistance) continue;
            nearest = candidate.transform;
            nearestDistance = distance;
        }
        return nearest;
    }

    bool CanSeePlayer()
    {
        return CanSeePlayer(player);
    }

    bool CanSeePlayer(Transform target)
    {
        if (target == null) return false;
        PlayerController targetController = target.GetComponent<PlayerController>();
        if (targetController == null || targetController.IsDeadState) return false;

        // 은신 중인 플레이어는 보지 못한 것으로 처리
        PlayerStealth stealth = target.GetComponent<PlayerStealth>();
        if (stealth != null && stealth.isStealth)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, target.position);
        if (distance > sightRange) return false;

        Vector3 origin = transform.position + Vector3.up * 1.5f;
        Vector3 targetPoint = target.position + Vector3.up * 1.1f;
        Vector3 delta = targetPoint - origin;
        Vector3 planar = Vector3.ProjectOnPlane(delta, Vector3.up);
        if (planar.sqrMagnitude > 0.001f &&
            Vector3.Angle(transform.forward, planar) > sightAngle * 0.5f) return false;

        if (Physics.Raycast(origin, delta.normalized, out RaycastHit hit, delta.magnitude,
                            ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.collider.GetComponentInParent<PlayerController>() == targetController;
        }

        return false;
    }

    void SetAnimationState(float speed, bool isWalk, bool isAttack)
    {
        NetworkSpeed = speed;
        NetworkIsWalking = isWalk;
        NetworkIsAttacking = isAttack;
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
