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
    private int networkId;

    public string PresentationState => state.ToString();
    public int PresentationAttackSequence { get; private set; }
    public int NetworkId => networkId;
    public void SetNetworkId(int id) => networkId = id;

    private void Awake()
    {
        if (GetComponent<MonsterPresentation>() == null) gameObject.AddComponent<MonsterPresentation>();
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
        NetworkClient network = NetworkClient.Instance;
        bool online = network != null && !network.offlineMode;
        if (online && (!network.GameStarted || networkId <= 0)) return;

        if (online && network.playerId != 1)
        {
            UpdateLight();
            return;
        }

        findPlayersTimer -= Time.deltaTime;

        if (players == null || findPlayersTimer <= 0f)
        {
            players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            findPlayersTimer = 0.5f;
        }

        PlayerController nearby = FindNearestPlayer(detectionRadius, online);

        switch (state)
        {
            case State.Idle:
                if (nearby != null)
                {
                    target = nearby;
                    attackTimer = 0f;
                    SetState(State.Warning);
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
                        SetState(State.Repeating);
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

        UpdateLight();
    }

    private void UpdateLight()
    {
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
            || Vector3.Distance(transform.position, target.transform.position) > detectionRadius
            || !HasLineOfSight(target);
    }

    private void ResetAttack()
    {
        SetState(State.Idle);
        target = null;
        attackTimer = 0f;
    }

    private PlayerController FindNearestPlayer(float radius, bool online)
    {
        PlayerController nearest = null;
        float bestSqr = radius * radius;

        if (players == null)
            return null;

        foreach (PlayerController player in players)
        {
            if (player == null || (online && !player.gameObject.activeInHierarchy) ||
                (!online && !player.isLocalPlayer) || player.IsDeadState)
                continue;

            float sqr = (player.transform.position - transform.position).sqrMagnitude;

            if (sqr > bestSqr)
                continue;

            if (!HasLineOfSight(player))
                continue;

            bestSqr = sqr;
            nearest = player;
        }

        return nearest;
    }

    private bool HasLineOfSight(PlayerController player)
    {
        Vector3 start = projectileSpawn != null ? projectileSpawn.position : transform.position + Vector3.up * 0.5f;
        CharacterController capsule = player.GetComponent<CharacterController>();
        Vector3 aim = capsule != null ? capsule.bounds.center : player.transform.position + Vector3.up * 1.2f;
        Vector3 delta = aim - start;
        float distance = delta.magnitude;
        if (distance < 0.01f) return true;

        RaycastHit? nearest = null;
        foreach (RaycastHit hit in Physics.RaycastAll(start, delta / distance, distance,
                                                       ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (!nearest.HasValue || hit.distance < nearest.Value.distance) nearest = hit;
        }
        return nearest.HasValue && nearest.Value.collider.GetComponentInParent<PlayerController>() == player;
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

        NetworkClient network = NetworkClient.Instance;
        bool online = network != null && !network.offlineMode;
        int targetPlayerId = online && player.transform == network.player2Object?.transform ? 2 : 1;
        int shotId = online
            ? network.SendMimicFire(networkId, targetPlayerId, start, direction, projectileSpeed, damage)
            : 0;
        projectile.Launch(direction, projectileSpeed, damage, transform, shotId, targetPlayerId);
        PresentationAttackSequence++;
    }

    private void SetState(State next)
    {
        if (state == next) return;
        state = next;
        NetworkClient network = NetworkClient.Instance;
        if (network != null && !network.offlineMode && network.playerId == 1)
            network.SendMimicState(networkId, (int)next);
    }

    public void ApplyNetworkState(int value)
    {
        if (value < 0 || value > (int)State.Repeating) return;
        state = (State)value;
    }

    public void SpawnNetworkProjectile(int shotId, int targetPlayerId, Vector3 start,
                                       Vector3 direction, float speed, int hitDamage)
    {
        if (direction.sqrMagnitude < 0.01f) return;
        GameObject shot = projectilePrefab != null
            ? Instantiate(projectilePrefab, start, Quaternion.LookRotation(direction))
            : MimicProjectile.CreateDefault(start, direction);
        MimicProjectile projectile = shot.GetComponent<MimicProjectile>();
        if (projectile == null) projectile = shot.AddComponent<MimicProjectile>();
        projectile.Launch(direction, speed, hitDamage, transform, shotId, targetPlayerId);
        PresentationAttackSequence++;
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
