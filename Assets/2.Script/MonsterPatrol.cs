using UnityEngine;
using UnityEngine.AI;

public class MonsterPatrol : MonoBehaviour
{
    public Transform[] patrolPoints;

    private NavMeshAgent agent;
    private Animator animator;
    private int currentIndex = 0;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        if (patrolPoints.Length > 0)
        {
            agent.SetDestination(patrolPoints[currentIndex].position);
        }
    }

    void Update()
    {
        if (patrolPoints.Length == 0) return;

        // 애니메이션 속도
        animator.SetFloat("Speed", agent.velocity.magnitude);

        // 목적지 도착
        if (!agent.pathPending && agent.remainingDistance < 0.3f)
        {
            currentIndex = (currentIndex + 1) % patrolPoints.Length;
            agent.SetDestination(patrolPoints[currentIndex].position);
        }
    }
}