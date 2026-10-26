using UnityEngine;

public class RemoteMonster : MonoBehaviour
{
    public float smoothSpeed = 10f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
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
    }

    void Update()
    {
        if (!isInitialized)
            return;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            Time.deltaTime * smoothSpeed
        );

        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * smoothSpeed
        );

        if (animator != null)
        {
            animator.SetFloat("Speed", targetSpeed);
            animator.SetBool("isWalk", targetIsWalk);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            bool finishingSwing = !targetIsWalk && Time.time < attackVisualUntil &&
                state.fullPathHash == AttackState && state.normalizedTime < 1f;
            animator.SetBool("isAttack", targetIsAttack || finishingSwing);

        }
    }

    public void SetState(Vector3 position, Quaternion rotation)
    {
        targetPosition = position;
        targetRotation = rotation;

        if (!isInitialized)
        {
            transform.position = position;
            transform.rotation = rotation;
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
            attackVisualUntil = Time.time + 2f;
            if (animator.HasState(0, AttackState))
                animator.Play(AttackState, 0, 0f);
            else
                animator.SetTrigger("Attack");
        }
        lastAttackSequence = attackSequence;
    }
}
