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
            animator.SetBool("IsRunning", targetIsRunning);
            animator.SetBool("isCrouching", targetIsCrouching);
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

    public void SetAnimationState(float speed, bool isRunning, bool isCrouching)
    {
        targetSpeed = speed;
        targetIsRunning = isRunning;
        targetIsCrouching = isCrouching;
    }

    public void SetupPlayer(bool isLocal)
    {
        PlayerController controller = GetComponent<PlayerController>();

        if (controller != null)
        {
            controller.isLocalPlayer = isLocal;
            controller.enabled = isLocal;
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