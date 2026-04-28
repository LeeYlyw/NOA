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

    private bool previousIsAttack;

    void Awake()
    {
        animator = GetComponent<Animator>();

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
            animator.SetBool("isAttack", targetIsAttack);

            if (targetIsAttack && !previousIsAttack)
            {
                animator.SetTrigger("Attack");
            }
        }

        previousIsAttack = targetIsAttack;
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

    public void SetAnimationState(float speed, bool isWalk, bool isAttack)
    {
        targetSpeed = speed;
        targetIsWalk = isWalk;
        targetIsAttack = isAttack;
    }
}