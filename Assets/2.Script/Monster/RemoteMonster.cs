using UnityEngine;

public class RemoteMonster : MonoBehaviour
{
    public float smoothSpeed = 10f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 displayedPosition;
    private Quaternion displayedRotation;
    private bool isInitialized = false;

    private Animator animator;

    private float targetSpeed;
    private bool targetIsWalk;
    private bool targetIsAttack;

    private int lastAttackSequence;
    private float attackVisualUntil;
    private static readonly int AttackState = Animator.StringToHash("Base Layer.MonsterAttack");
    public string PresentationState { get; set; } = "Idle";
    public int PresentationAttackSequence => lastAttackSequence;

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        targetPosition = transform.position;
        targetRotation = transform.rotation;
        displayedPosition = targetPosition;
        displayedRotation = targetRotation;
    }

    void Update()
    {
        if (!isInitialized)
            return;

        displayedPosition = Vector3.Lerp(
            displayedPosition,
            targetPosition,
            Time.deltaTime * smoothSpeed
        );

        displayedRotation = Quaternion.Lerp(
            displayedRotation,
            targetRotation,
            Time.deltaTime * smoothSpeed
        );

        transform.SetPositionAndRotation(displayedPosition, displayedRotation);

        if (animator != null)
        {
            animator.SetFloat("Speed", targetSpeed);
            animator.SetBool("isWalk", targetIsWalk);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            animator.speed = state.fullPathHash == AttackState ? 2f : 1f;
            bool finishingSwing = !targetIsWalk && Time.time < attackVisualUntil &&
                state.fullPathHash == AttackState && state.normalizedTime < 1f;
            animator.SetBool("isAttack", targetIsAttack || finishingSwing);

        }
    }

    void LateUpdate()
    {
        if (!isInitialized) return;
        // Monster animation clips contain curves on the root Transform. Unity
        // evaluates them after Update, so they can pull both server monsters
        // back near the scene origin. Restore the interpolated server pose last.
        transform.SetPositionAndRotation(displayedPosition, displayedRotation);
    }

    public void SetState(Vector3 position, Quaternion rotation)
    {
        targetPosition = position;
        targetRotation = rotation;

        if (!isInitialized)
        {
            displayedPosition = position;
            displayedRotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            isInitialized = true;
        }
    }

    public void SetAnimationState(float speed, bool isWalk, bool isAttack, int attackSequence)
    {
        targetSpeed = speed;
        targetIsWalk = isWalk;
        targetIsAttack = isAttack;
        if (attackSequence > lastAttackSequence && animator != null)
        {
            // The server can start another swing while the Animator is still in
            // MonsterAttack. A trigger alone does not restart that same state.
            animator.SetBool("isAttack", true);
            attackVisualUntil = Time.time + 1.5f;
            if (animator.HasState(0, AttackState))
                animator.Play(AttackState, 0, 0f);
            else
                animator.SetTrigger("Attack");
        }
        lastAttackSequence = attackSequence;
    }

    void OnDisable()
    {
        if (animator != null) animator.speed = 1f;
    }
}
