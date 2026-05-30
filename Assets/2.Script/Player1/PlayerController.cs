using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private PlayerNoiseEmitter noiseEmitter;

    [Header("Network")]
    public bool isLocalPlayer = true;

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
    [SerializeField] private int currentHp;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float currentStamina = 100f;
    public float staminaDrainPerSecond = 20f;
    public float staminaRecoveryPerSecond = 15f;

    [Header("UI")]
    public Slider hpSlider;
    public Slider staminaSlider;

    private CharacterController controller;
    private Animator animator;

    private Vector3 horizontalMove; //  [변경] 수평 이동 속도를 임시 저장할 변수
    private Vector3 velocity;
    private bool isGrounded;
    private bool isRunning;
    private bool isCrouching;
    private bool isDead;
    private bool canRun = true;

    // NetworkClient가 읽을 애니메이션/상태값
    public float CurrentAnimSpeed { get; private set; }
    public bool IsRunningState => isRunning;
    public bool IsCrouchingState => isCrouching;
    public bool IsDeadState => isDead;
    public int CurrentHp => currentHp;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        noiseEmitter = GetComponent<PlayerNoiseEmitter>();

        currentHp = maxHealth;
        currentStamina = maxStamina;

        if (hpSlider != null)
        {
            hpSlider.maxValue = maxHealth;
            hpSlider.value = currentHp;
        }

        if (staminaSlider != null)
        {
            staminaSlider.maxValue = maxStamina;
            staminaSlider.value = currentStamina;
        }

        UpdateUI();
    }

    void Start()
    {
        if (isLocalPlayer)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        if (!isLocalPlayer)
            return;

        if (!isDead)
        {
            HandleCrouch();
            HandleMovement();
            HandleStamina();

            if (Input.GetKeyDown(KeyCode.H))
                TakeDamage(10);

            if (Input.GetKeyDown(KeyCode.K))
                Die();
        }
        else
        {
            horizontalMove = Vector3.zero;
        }

        // 수평 이동과 수직 이동을 계산한 뒤, 항상 마지막에 최종 Move()를 처리합니다.
        HandleJumpAndGravity();
    }

    void UpdateUI()
    {
        if (hpSlider != null)
            hpSlider.value = currentHp;

        if (staminaSlider != null)
            staminaSlider.value = currentStamina;
    }

    void HandleMovement()
    {
        if (controller == null || !controller.enabled)
            return;

        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        Vector3 move = transform.right * x + transform.forward * z;

        bool hasInput = move.magnitude > 0.1f;
        bool runInput = Input.GetKey(KeyCode.LeftShift);

        isRunning = runInput && hasInput && canRun && currentStamina > 0f && !isCrouching;

        float speed = isRunning ? runSpeed : walkSpeed;

        //  [수정] 여기서 직접 Move하지 않고, 속도만 계산해서 저장합니다.
        horizontalMove = move * speed;

        CurrentAnimSpeed = Mathf.Clamp01(move.magnitude);

        if (animator != null)
        {
            animator.SetFloat("Speed", CurrentAnimSpeed);
            animator.SetBool("IsRunning", isRunning);
            animator.SetBool("isCrouching", isCrouching);
        }
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
                currentStamina = maxStamina;

            if (currentStamina > 0f)
                canRun = true;
        }

        UpdateUI();
    }

    void HandleJumpAndGravity()
    {
        if (controller == null || !controller.enabled)
            return;

        // 단 한 번만 Move가 돌기 때문에 이 값이 정확하게 체크됩니다.
        isGrounded = controller.isGrounded;

        if (animator != null)
        {
            //  [추가] 애니메이터에 현재 땅에 닿아있는지 상태를 계속 쏴줍니다.
            animator.SetBool("isGrounded", isGrounded);
        }

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

        // 점프 입력
        if (isLocalPlayer && Input.GetKeyDown(KeyCode.Space) && coyoteTimer > 0f)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            coyoteTimer = 0f;

            //  [추가] 점프하는 순간 애니메이터에 Jump 트리거를 날려줍니다.
            if (animator != null)
            {
                animator.SetTrigger("Jump");
            }

            if (noiseEmitter != null)
            {
                noiseEmitter.EmitJumpNoise();
            }
        }

        velocity.y += gravity * Time.deltaTime;

        //  [수정] 수평 이동 속도와 중력/점프 속도를 합쳐서 프레임당 딱 한 번만 Move를 실행합니다.
        Vector3 finalMove = horizontalMove + velocity;
        controller.Move(finalMove * Time.deltaTime);
    }

    void HandleCrouch()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            isCrouching = !isCrouching;

            if (isCrouching)
                isRunning = false;

            if (animator != null)
                animator.SetBool("isCrouching", isCrouching);
        }
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        int previousHealth = currentHp;
        currentHp -= damage;

        if (currentHp < 0)
            currentHp = 0;

        if (currentHp <= 0)
        {
            Die();
            return;
        }

        if (currentHp < previousHealth && animator != null)
        {
            animator.SetTrigger("Hit");
            Debug.Log("Player Hit! Current Health: " + currentHp);
        }

        UpdateUI();
    }

    public void HealToFull()
    {
        if (isDead) return;

        currentHp = maxHealth;
        UpdateUI();

        Debug.Log(gameObject.name + " 체력 전부 회복: " + currentHp);
    }

    void Die()
    {
        if (isDead) return;

        isDead = true;
        isRunning = false;
        isCrouching = false;
        CurrentAnimSpeed = 0f;

        horizontalMove = Vector3.zero;
        velocity = Vector3.zero;
        velocity.y = -2f;

        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
            animator.SetBool("IsRunning", false);
            animator.SetBool("isCrouching", false);
            animator.SetBool("isDead", true);
        }

        Debug.Log("Player Died");
        UpdateUI();
    }

    public bool IsRunning()
    {
        return isRunning;
    }

    public bool IsDead()
    {
        return isDead;
    }

    public void Revive()
    {
        isDead = false;
        currentHp = maxHealth;
        currentStamina = maxStamina;

        if (animator != null)
        {
            animator.SetBool("isDead", false);
            animator.Play("Idle", 0, 0f);
            animator.SetFloat("Speed", 0f);
        }

        horizontalMove = Vector3.zero;
        velocity = Vector3.zero;

        UpdateUI();

        Debug.Log(gameObject.name + "이(가) 완전히 부활하여 다시 움직일 수 있습니다!");
    }
}