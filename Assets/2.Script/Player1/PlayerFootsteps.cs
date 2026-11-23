using UnityEngine;

// Plays audible footsteps only; PlayerNoiseEmitter keeps monster hearing rules.
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class PlayerFootsteps : MonoBehaviour
{
    [Range(0f, 1f)] public float walkVolume = 0.27f;
    [Range(0f, 1f)] public float runVolume = 0.43f;
    [Range(0f, 1f)] public float crouchVolume = 0.12f;
    [Min(0.1f)] public float walkInterval = 0.48f;
    [Min(0.1f)] public float runInterval = 0.29f;
    [Min(0.1f)] public float crouchInterval = 0.68f;

    private static AudioClip[] clips;
    private PlayerController player;
    private CharacterController character;
    private Animator animator;
    private AudioSource speaker;
    private Vector3 previousPosition;
    private float stepClock;
    private int clipIndex;
    private bool wasMoving;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        character = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (clips == null) clips = Resources.LoadAll<AudioClip>("Audio/Footsteps");
        if (clips.Length == 0)
            Debug.LogWarning("[PlayerFootsteps] No footstep clips found in Resources/Audio/Footsteps.", this);

        speaker = gameObject.AddComponent<AudioSource>();
        speaker.playOnAwake = false;
        speaker.spatialBlend = 1f;
        speaker.rolloffMode = AudioRolloffMode.Logarithmic;
        speaker.minDistance = 2.2f;
        speaker.maxDistance = 16f;
        speaker.dopplerLevel = 0f;
        previousPosition = transform.position;
        clipIndex = (GetInstanceID() & int.MaxValue) % Mathf.Max(1, clips.Length);
    }

    private void OnEnable()
    {
        previousPosition = transform.position;
        stepClock = 0f;
        wasMoving = false;
    }

    private void LateUpdate()
    {
        Vector3 displacement = transform.position - previousPosition;
        displacement.y = 0f;
        previousPosition = transform.position;

        if (clips.Length == 0 || Time.timeScale <= 0f ||
            player.IsDeadState || (ClueManager.instance != null && ClueManager.instance.IsEnding))
        {
            ResetSteps();
            return;
        }

        bool grounded, running, crouching;
        float horizontalSpeed;
        if (player.isLocalPlayer)
        {
            Vector3 velocity = character.velocity;
            velocity.y = 0f;
            horizontalSpeed = velocity.magnitude;
            grounded = player.IsGroundedState && character.isGrounded;
            running = player.IsRunningState;
            crouching = player.IsCrouchingState;
        }
        else
        {
            // RemotePlayer interpolates transforms directly, so CharacterController.velocity
            // is not useful for another player's footsteps.
            horizontalSpeed = displacement.magnitude / Mathf.Max(0.001f, Time.deltaTime);
            grounded = animator != null && animator.GetBool("isGrounded");
            running = animator != null && animator.GetBool("IsRunning");
            crouching = animator != null && animator.GetBool("isCrouching");
            if (animator == null || animator.GetFloat("Speed") <= 0.05f)
                horizontalSpeed = 0f;
        }

        // Check actual motion and ground contact so blocked movement, idle time,
        // jumping, and falling do not play walking sounds.
        if (!grounded || horizontalSpeed < 0.55f)
        {
            ResetSteps();
            return;
        }

        float interval = crouching ? crouchInterval : running ? runInterval : walkInterval;
        if (!wasMoving)
        {
            stepClock = Mathf.Max(0f, interval - 0.11f);
            wasMoving = true;
        }
        stepClock += Time.deltaTime;
        if (stepClock < interval) return;
        stepClock -= interval;

        AudioClip clip = clips[clipIndex++ % clips.Length];
        speaker.pitch = (running ? 1.08f : crouching ? 0.88f : 1f) +
            (clipIndex % 3 - 1) * 0.025f;
        speaker.PlayOneShot(clip, crouching ? crouchVolume : running ? runVolume : walkVolume);
    }

    private void ResetSteps()
    {
        stepClock = 0f;
        wasMoving = false;
    }
}
