using UnityEngine;
using UnityEngine.AI;

public class SoundMonsterAI : MonoBehaviour
{
    public enum State
    {
        Idle,
        Investigate,
        Chase,
        Return
    }

    public NavMeshAgent agent;
    public Transform player;

    [Header("Hearing")]
    public float hearingMultiplier = 2f;
    public float chaseNoiseThreshold = 7f;

    [Header("Vision")]
    public float sightRange = 6f;
    public float loseSightTime = 3f;

    [Header("Investigate")]
    public float investigateWaitTime = 2f;

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

        startPosition = transform.position;
    }

    void Update()
    {
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
            case State.Return:
                UpdateReturn();
                break;
        }
    }

    void UpdateIdle()
    {
        if (CanSeePlayer())
            currentState = State.Chase;
    }

    void UpdateInvestigate()
    {
        if (CanSeePlayer())
        {
            currentState = State.Chase;
            return;
        }

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

    void UpdateChase()
    {
        if (player == null) return;

        agent.SetDestination(player.position);

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

    void UpdateReturn()
    {
        if (CanSeePlayer())
        {
            currentState = State.Chase;
            return;
        }

        agent.SetDestination(startPosition);

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
        {
            currentState = State.Idle;
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
}