using UnityEngine;

public class RemotePlayer : MonoBehaviour
{
    public float smoothSpeed = 10f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool isInitialized = false;

    private Animator animator;

    private float targetSpeed;
    private bool targetIsRunning;
    private bool targetIsCrouching;
    private bool targetIsGrounded;
    private int lastJumpSequence;

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
            animator.SetBool("IsRunning", targetIsRunning);
            animator.SetBool("isCrouching", targetIsCrouching);
            animator.SetBool("isGrounded", targetIsGrounded);
        }
    }

    public void SetState(Vector3 newPosition, Quaternion newRotation)
    {
        targetPosition = newPosition;
        targetRotation = newRotation;

        if (!isInitialized)
        {
            transform.position = newPosition;
            transform.rotation = newRotation;
            isInitialized = true;
        }
    }

    public void SetAnimationState(float speed, bool isRunning, bool isCrouching,
                                  bool isGrounded, int jumpSequence)
    {
        targetSpeed = speed;
        targetIsRunning = isRunning;
        targetIsCrouching = isCrouching;
        targetIsGrounded = isGrounded;
        if (jumpSequence > lastJumpSequence && animator != null)
            animator.SetTrigger("Jump");
        lastJumpSequence = jumpSequence;
    }

    public void PlayHitAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger("Hit");
        }
    }

    public void PlayDeathAnimation()
    {
        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
            animator.SetBool("IsRunning", false);
            animator.SetBool("isCrouching", false);
            animator.SetBool("isDead", true);
        }
    }

    public void SetupPlayer(bool isLocal)
    {
        PlayerController controller = GetComponent<PlayerController>();

        if (controller != null)
        {
            controller.isLocalPlayer = isLocal;

            // 중요:
            // PlayerController를 꺼버리면 리모트 플레이어의 TakeDamage/Die/Hit 처리가 꼬일 수 있음.
            // 입력은 PlayerController.Update()의 isLocalPlayer 검사로 막고,
            // 컴포넌트 자체는 항상 켜둔다.
            controller.enabled = true;
        }

        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        foreach (Camera cam in cameras)
        {
            cam.gameObject.SetActive(isLocal);
        }

        AudioListener[] audioListeners = GetComponentsInChildren<AudioListener>(true);
        foreach (AudioListener listener in audioListeners)
        {
            listener.enabled = isLocal;
        }

        this.enabled = !isLocal;
    }
}