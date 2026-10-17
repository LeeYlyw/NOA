using UnityEngine;

public class MimicLaptop : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Light laptopLight;
    [SerializeField] private Transform projectileSpawn;
    [SerializeField] private GameObject projectilePrefab;

    [Header("Detection / Attack")]
    [Min(0.1f)][SerializeField] private float detectionRadius = 3.5f;
    [Min(0.1f)][SerializeField] private float warningSeconds = 3f;
    [Min(0.1f)][SerializeField] private float repeatSeconds = 2f;
    [Min(0.1f)][SerializeField] private float projectileSpeed = 7f;
    [Min(1)][SerializeField] private int damage = 15;

    [Header("Light")]
    [SerializeField] private Color warningColor = new Color(1f, 0.07f, 0.04f);
    [SerializeField] private float warningIntensityMultiplier = 1.5f;
    [SerializeField] private float colorChangeSpeed = 7f;

    private enum State { Idle, Warning, Repeating }

    private State state;
    private float attackTimer;
    private float findPlayersTimer;
    private PlayerController[] players;
    private PlayerController target;
    private Color idleColor;
    private float idleIntensity;

    private void Awake()
    {
        if (laptopLight == null)
            laptopLight = GetComponentInChildren<Light>();

        if (laptopLight != null)
        {
            idleColor = laptopLight.color;
            idleIntensity = laptopLight.intensity;
        }
    }

    private void Update()
    {
        findPlayersTimer -= Time.deltaTime;

        if (players == null || findPlayersTimer <= 0f)
        {
            players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            findPlayersTimer = 0.5f;
        }

        PlayerController nearby = FindNearestLocalPlayer(detectionRadius);

        switch (state)
        {
            case State.Idle:
                if (nearby != null)
                {
                    target = nearby;
                    attackTimer = 0f;
                    state = State.Warning;
                }
                break;

            case State.Warning:
                if (TargetLeftRange())
                {
                    ResetAttack();
                }
                else
                {
                    attackTimer += Time.deltaTime;

                    if (attackTimer >= warningSeconds)
                    {
                        FireAt(target);
                        attackTimer = 0f;
                        state = State.Repeating;
                    }
                }
                break;

            case State.Repeating:
                if (TargetLeftRange())
                {
                    ResetAttack();
                }
                else
                {
                    attackTimer += Time.deltaTime;

                    if (attackTimer >= repeatSeconds)
                    {
                        FireAt(target);
                        attackTimer -= repeatSeconds;
                    }
                }
                break;
        }

        if (laptopLight != null)
        {
            bool red = state != State.Idle;
            float blend = 1f - Mathf.Exp(-colorChangeSpeed * Time.deltaTime);

            laptopLight.color = Color.Lerp(
                laptopLight.color,
                red ? warningColor : idleColor,
                blend
            );

            laptopLight.intensity = Mathf.Lerp(
                laptopLight.intensity,
                red ? idleIntensity * warningIntensityMultiplier : idleIntensity,
                blend
            );
        }
    }

    private bool TargetLeftRange()
    {
        return target == null
            || target.IsDeadState
            || !target.isLocalPlayer
            || Vector3.Distance(transform.position, target.transform.position) > detectionRadius;
    }

    private void ResetAttack()
    {
        state = State.Idle;
        target = null;
        attackTimer = 0f;
    }

    private PlayerController FindNearestLocalPlayer(float radius)
    {
        PlayerController nearest = null;
        float bestSqr = radius * radius;

        if (players == null)
            return null;

        foreach (PlayerController player in players)
        {
            if (player == null || !player.isLocalPlayer || player.IsDeadState)
                continue;

            float sqr = (player.transform.position - transform.position).sqrMagnitude;

            if (sqr > bestSqr)
                continue;

            bestSqr = sqr;
            nearest = player;
        }

        return nearest;
    }

    private void FireAt(PlayerController player)
    {
        Vector3 start = projectileSpawn != null
            ? projectileSpawn.position
            : transform.position + Vector3.up * 0.25f;

        CharacterController capsule = player.GetComponent<CharacterController>();

        Vector3 aim = capsule != null
            ? capsule.bounds.center + Vector3.up * 0.12f
            : player.transform.position + Vector3.up * 1.2f;

        Vector3 direction = (aim - start).normalized;

        GameObject shot = projectilePrefab != null
            ? Instantiate(projectilePrefab, start, Quaternion.LookRotation(direction))
            : MimicProjectile.CreateDefault(start, direction);

        MimicProjectile projectile = shot.GetComponent<MimicProjectile>();

        if (projectile == null)
            projectile = shot.AddComponent<MimicProjectile>();

        projectile.Launch(direction, projectileSpeed, damage, transform);
    }

    private void OnValidate()
    {
        detectionRadius = Mathf.Max(0.1f, detectionRadius);
        warningSeconds = Mathf.Max(0.1f, warningSeconds);
        repeatSeconds = Mathf.Max(0.1f, repeatSeconds);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}