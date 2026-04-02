using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3f;
    public float runSpeed = 6f;

    [Header("Jump & Gravity")]
    public float jumpHeight = 1.5f;
    public float gravity = -9.81f;

    [Header("Jump Tuning")]
    public float coyoteTime = 0.1f;
    private float coyoteTimer;

    [Header("Health")]
    public int maxHealth = 100;
    private int currentHealth;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float currentStamina = 100f;
    public float staminaDrainPerSecond = 20f;
    public float staminaRecoveryPerSecond = 15f;

    private CharacterController controller;
    private Animator animator;

    private Vector3 velocity;
    private bool isGrounded;
    private bool isRunning;
    private bool isCrouching;
    private bool isDead;
    private bool canRun = true;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        currentHealth = maxHealth;
        currentStamina = maxStamina;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!isDead)
        {
            HandleCrouch();
            HandleMovement();
            HandleStamina();

            if (Input.GetKeyDown(KeyCode.H))
            {
                TakeDamage(10);
            }

            if (Input.GetKeyDown(KeyCode.K))
            {
                Die();
            }
        }

        HandleJumpAndGravity();
    }

    void HandleMovement()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        Vector3 move = transform.right * x + transform.forward * z;

        bool hasInput = move.magnitude > 0.1f;
        bool runInput = Input.GetKey(KeyCode.LeftShift);

        isRunning = runInput && hasInput && canRun && currentStamina > 0f;

        float speed = isRunning ? runSpeed : walkSpeed;
        controller.Move(move * speed * Time.deltaTime);

        float inputMagnitude = Mathf.Clamp01(move.magnitude);
        animator.SetFloat("Speed", inputMagnitude);
        animator.SetBool("IsRunning", isRunning);
    }

    void HandleStamina()
    {
        if (isRunning)
        {
            currentStamina -= staminaDrainPerSecond * Time.deltaTime;

            if (currentStamina <= 0f)
            {
                currentStamina = 0f;
                canRun = false;
                isRunning = false;
            }
        }
        else
        {
            currentStamina += staminaRecoveryPerSecond * Time.deltaTime;

            if (currentStamina >= maxStamina)
            {
                currentStamina = maxStamina;
            }

            if (currentStamina > 0f)
            {
                canRun = true;
            }
        }
    }

    void HandleJumpAndGravity()
    {
        isGrounded = controller.isGrounded;

        if (isGrounded)
        {
            coyoteTimer = coyoteTime;

            if (velocity.y < 0)
                velocity.y = -2f;
        }
        else
        {
            coyoteTimer -= Time.deltaTime;
        }

        if (Input.GetKeyDown(KeyCode.Space) && coyoteTimer > 0f)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            coyoteTimer = 0f;
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    void HandleCrouch()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            isCrouching = !isCrouching;
            animator.SetBool("isCrouching", isCrouching);
        }
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        int previousHealth = currentHealth;
        currentHealth -= damage;

        if (currentHealth < 0)
            currentHealth = 0;

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        if (currentHealth < previousHealth)
        {
            animator.SetTrigger("Hit");
            Debug.Log("Player Hit! Current Health: " + currentHealth);
        }
    }

    void Die()
    {
        if (isDead) return;

        isDead = true;

        isRunning = false;
        isCrouching = false;

        velocity = Vector3.zero;
        velocity.y = -2f;

        animator.SetBool("IsRunning", false);
        animator.SetBool("isCrouching", false);
        animator.SetBool("isDead", true);

        Debug.Log("Player Died");
    }
}