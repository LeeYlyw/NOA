using UnityEngine;

public class PlayerNoiseEmitter : MonoBehaviour
{
    public static System.Action<Vector3, float, GameObject> OnNoiseEmitted;

    [Header("Noise Values")]
    public float walkNoise = 4f;
    public float runNoise = 8f;
    public float jumpNoise = 6f;
    public float landingNoise = 7f;

    [Header("Move Check")]
    public float moveCheckInterval = 0.3f;

    private CharacterController controller;
    private PlayerController playerController;

    private float timer;
    private bool wasGrounded;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
    }

    void Update()
    {
        HandleMoveNoise();
        HandleLandingNoise();
    }

    void HandleMoveNoise()
    {
        timer += Time.deltaTime;
        if (timer < moveCheckInterval) return;
        timer = 0f;

        Vector3 horizontalVelocity = controller.velocity;
        horizontalVelocity.y = 0f;

        if (horizontalVelocity.magnitude > 0.1f && controller.isGrounded)
        {
            float noiseAmount = playerController != null && playerController.IsRunning()
                ? runNoise
                : walkNoise;

            EmitNoise(noiseAmount);
        }
    }

    void HandleLandingNoise()
    {
        if (!wasGrounded && controller.isGrounded)
        {
            EmitNoise(landingNoise);
        }

        wasGrounded = controller.isGrounded;
    }

    public void EmitJumpNoise()
    {
        EmitNoise(jumpNoise);
    }

    public void EmitNoise(float amount)
    {
        OnNoiseEmitted?.Invoke(transform.position, amount, gameObject);
    }
}