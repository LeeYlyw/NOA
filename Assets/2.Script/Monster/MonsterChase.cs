using UnityEngine;
using UnityEngine.AI;

public class MonsterChase : MonoBehaviour
{
    private enum State { Patrol, Chase, Search, Attack }
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

    [Header("Patrol and Search")]
    public float patrolRadius = 14f;
    public float patrolPause = 1.5f;
    public float loseSightDelay = 1.5f;
    public float searchDuration = 7f;
    public float searchRadius = 3f;

    [Header("Movement Speed")]
    public float patrolSpeed = 1.5f;
    public float chaseSpeed = 3.8f;
    public float searchSpeed = 2.3f;

    [Header("Rotation")]
    public float rotateSpeed = 5f;

    private NavMeshAgent agent;
    private Animator animator;
    private State state = State.Patrol;
    private Vector3 lastSeenPosition;
    private float lastSeenTime;
    private float searchEndTime;
    private bool searchingAround;
    private float pauseUntil;
    private bool patrolWaiting = true;

    public float NetworkSpeed { get; private set; }
    public bool NetworkIsWalking { get; private set; }
    public bool NetworkIsAttacking { get; private set; }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        pauseUntil = Time.time + patrolPause;
    }

    void Update()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        agent.speed = state == State.Chase || state == State.Attack ? chaseSpeed :
                      state == State.Search ? searchSpeed : patrolSpeed;

        Transform visible = FindNearestPlayer();
        if (visible != null)
        {
            player = visible;
            lastSeenPosition = visible.position;
            lastSeenTime = Time.time;
            state = Vector3.Distance(transform.position, visible.position) <= attackRange
                ? State.Attack : State.Chase;
        }
        else if (state == State.Attack)
        {
            state = State.Chase;
        }

        if (state == State.Chase && Time.time - lastSeenTime > loseSightDelay)
        {
            state = State.Search;
            searchingAround = false;
            player = null;
            MoveTo(lastSeenPosition);
        }

        switch (state)
        {
            case State.Patrol: UpdatePatrol(); break;
            case State.Chase: ChasePlayer(); break;
            case State.Search: UpdateSearch(); break;
            case State.Attack: AttackPlayer(); break;
        }
    }

    bool CanSeePlayer(Transform target)
    {
        if (target == null) return false;

        PlayerStealth stealth = target.GetComponent<PlayerStealth>();
        if (stealth != null && stealth.isStealth) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPoint = target.position + Vector3.up * 1.1f;
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

    void UpdatePatrol()
    {
        if (patrolWaiting)
        {
            StopMonster();
            transform.Rotate(0f, 35f * Time.deltaTime, 0f);
            if (Time.time >= pauseUntil)
            {
                patrolWaiting = false;
                Vector3 destination;
                if (TryFindReachablePoint(transform.position, patrolRadius, out destination))
                    MoveTo(destination);
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
            StopMonster();
            return;
        }

        SetWalking(!agent.isStopped && agent.velocity.sqrMagnitude > 0.01f);
    }

    void UpdateSearch()
    {
        if (searchingAround && Time.time >= searchEndTime)
        {
            state = State.Patrol;
            pauseUntil = 0f;
            patrolWaiting = true;
            return;
        }

        if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.3f))
        {
            if (!searchingAround)
            {
                searchingAround = true;
                searchEndTime = Time.time + searchDuration;
            }
            Vector3 destination;
            if (TryFindReachablePoint(lastSeenPosition, searchRadius, out destination))
                MoveTo(destination);
            else
                StopMonster();
        }
        SetWalking(!agent.isStopped && agent.velocity.sqrMagnitude > 0.01f);
    }

    bool TryFindReachablePoint(Vector3 center, float radius, out Vector3 destination)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, 1.5f, agent.areaMask)) continue;
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

    void MoveTo(Vector3 destination)
    {
        agent.isStopped = false;
        agent.SetDestination(destination);
    }

    void SetWalking(bool walking)
    {
        NetworkSpeed = walking ? agent.velocity.magnitude : 0f;
        NetworkIsWalking = walking;
        NetworkIsAttacking = false;
        if (animator == null) return;
        animator.SetFloat("Speed", walking ? 1f : 0f);
        animator.SetBool("isWalk", walking);
        animator.SetBool("isAttack", false);
    }

    void ChasePlayer()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;

        MoveTo(lastSeenPosition);
        SetWalking(true);
    }

    void AttackPlayer()
    {
        if (player == null || !CanSeePlayer(player) ||
            Vector3.Distance(transform.position, player.position) > attackRange)
        {
            state = State.Chase;
            SetWalking(false);
            return;
        }

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

        }

        NetworkSpeed = 0f;
        NetworkIsWalking = false;
        NetworkIsAttacking = true;

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            int targetPlayerId = GetPlayerIdFromTarget(player);

            if (targetPlayerId != -1)
            {
                if (animator != null) animator.SetTrigger("Attack");
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
