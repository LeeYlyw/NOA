using UnityEngine;

/// <summary>
/// Straight, dodgeable shot. A sphere sweep prevents the shot crossing thin walls.
/// </summary>
public class MimicProjectile : MonoBehaviour
{
    [SerializeField] private float radius = 0.06f;
    [SerializeField] private float lifetime = 4f;

    private Vector3 direction;
    private float speed;
    private int damage;
    private Transform owner;
    private bool launched;
    private Material runtimeMaterial;
    private int networkShotId;
    private int targetPlayerId;

    public static GameObject CreateDefault(Vector3 position, Vector3 direction)
    {
        GameObject shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = "Mimic Red Projectile";
        shot.transform.position = position;
        shot.transform.rotation = Quaternion.LookRotation(direction);
        shot.transform.localScale = Vector3.one * 0.12f;

        // Movement/collision is handled by SphereCastAll below, not by this collider.
        Collider collider = shot.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;

        Renderer renderer = shot.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader != null && renderer != null)
        {
            Material material = new Material(shader);
            Color color = new Color(1f, 0.08f, 0.05f, 1f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            renderer.material = material;
            MimicProjectile component = shot.AddComponent<MimicProjectile>();
            component.runtimeMaterial = material;
        }
        return shot;
    }

    public void Launch(Vector3 travelDirection, float travelSpeed, int hitDamage, Transform source,
                       int shotId = 0, int targetId = 0)
    {
        foreach (Collider ownCollider in GetComponentsInChildren<Collider>())
            ownCollider.enabled = false;
        direction = travelDirection.normalized;
        speed = Mathf.Max(0.1f, travelSpeed);
        damage = Mathf.Max(1, hitDamage);
        owner = source;
        networkShotId = shotId;
        targetPlayerId = targetId;
        launched = true;
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        if (!launched) return;
        float distance = speed * Time.deltaTime;
        RaycastHit[] hits = Physics.SphereCastAll(transform.position, radius,
            direction, distance, ~0, QueryTriggerInteraction.Ignore);

        RaycastHit? nearest = null;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (owner != null && hit.collider.transform.IsChildOf(owner)) continue;
            if (!nearest.HasValue || hit.distance < nearest.Value.distance) nearest = hit;
        }

        if (nearest.HasValue)
        {
            PlayerController player = nearest.Value.collider.GetComponentInParent<PlayerController>();
            if (player != null && player.isLocalPlayer && !player.IsDeadState)
            {
                NetworkClient network = NetworkClient.Instance;
                if (network != null && !network.offlineMode)
                {
                    if (networkShotId > 0 && network.playerId == targetPlayerId)
                        network.SendMimicHit(networkShotId);
                }
                else
                {
                    player.TakeDamage(damage);
                }
            }
            Destroy(gameObject);
            return;
        }
        transform.position += direction * distance;
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }
}
