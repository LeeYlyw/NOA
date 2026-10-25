using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DoorInteraction : MonoBehaviour
{
    [Header("UI & References")]
    public GameObject interactionUI;
    public Transform doorHinge;

    [Header("Door Settings")]
    public float openAngle = 90f;
    public float rotateSpeed = 2f;

    [Header("Door Sounds")]
    public AudioClip openSound;
    public AudioClip closeSound;

    public int NetworkId { get; private set; }
    public void SetNetworkId(int id) { NetworkId = id; }
    public Vector3 GetNetworkAnchor() => doorHinge != null ? doorHinge.position : transform.position;

    private AudioSource audioSource;
    private bool isNear;
    private bool isOpen;
    private Coroutine doorCoroutine;
    private Quaternion closedRotation;
    private Transform nearbyPlayer;
    private Vector3 leafCenterOffsetWorld;
    private Vector3 closedUpWorld;

    private void Start()
    {
        if (interactionUI != null)
            interactionUI.SetActive(false);

        if (doorHinge == null)
        {
            Debug.LogError("DoorInteraction: Door Hinge를 연결해 주세요.", this);
            enabled = false;
            return;
        }

        closedRotation = doorHinge.localRotation;
        closedUpWorld = doorHinge.up;

        Renderer leafRenderer = doorHinge.GetComponentInChildren<Renderer>();
        if (leafRenderer != null)
            leafCenterOffsetWorld = leafRenderer.bounds.center - doorHinge.position;
        else
            Debug.LogWarning("DoorInteraction: Door Hinge 아래에 문짝 Renderer가 없습니다.", this);

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
        }
        audioSource.playOnAwake = false;

        BuildPrompt();
    }

    private void Update()
    {
        if (!isNear || !Input.GetKeyDown(KeyCode.F))
            return;

        int direction = 1;
        if (!isOpen)
        {
            Quaternion positive = closedRotation * Quaternion.Euler(0f, Mathf.Abs(openAngle), 0f);
            direction = Quaternion.Angle(GetAwayRotation(), positive) < 1f ? 1 : -1;
        }
        NetworkClient network = NetworkClient.Instance;
        if (network != null && !network.offlineMode)
        {
            network.SendDoorToggle(NetworkId, direction);
            return;
        }
        ApplyServerState(!isOpen, direction);
    }

    public void ApplyServerState(bool open, int direction)
    {
        if (doorHinge == null || isOpen == open) return;
        isOpen = open;
        if (doorCoroutine != null) StopCoroutine(doorCoroutine);
        if (isOpen && openSound != null && audioSource != null)
            audioSource.PlayOneShot(openSound);
        Quaternion targetRotation = isOpen
            ? closedRotation * Quaternion.Euler(0f, Mathf.Abs(openAngle) * (direction >= 0 ? 1 : -1), 0f)
            : closedRotation;
        doorCoroutine = StartCoroutine(RotateDoor(targetRotation, isOpen));
    }

    private Quaternion GetAwayRotation()
    {
        float angle = Mathf.Abs(openAngle);
        Quaternion positive = closedRotation * Quaternion.Euler(0f, angle, 0f);
        Quaternion negative = closedRotation * Quaternion.Euler(0f, -angle, 0f);

        if (nearbyPlayer == null || leafCenterOffsetWorld.sqrMagnitude < 0.001f)
            return positive;

        Vector3 positiveCenter = doorHinge.position +
            Quaternion.AngleAxis(angle, closedUpWorld) * leafCenterOffsetWorld;

        Vector3 negativeCenter = doorHinge.position +
            Quaternion.AngleAxis(-angle, closedUpWorld) * leafCenterOffsetWorld;

        Vector3 playerPosition = nearbyPlayer.position;
        playerPosition.y = positiveCenter.y;
        positiveCenter.y = playerPosition.y;
        negativeCenter.y = playerPosition.y;

        float positiveDistance = (positiveCenter - playerPosition).sqrMagnitude;
        float negativeDistance = (negativeCenter - playerPosition).sqrMagnitude;

        return positiveDistance >= negativeDistance ? positive : negative;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || !player.isLocalPlayer)
            return;

        isNear = true;
        nearbyPlayer = player.transform;

        if (interactionUI != null)
            interactionUI.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || !player.isLocalPlayer || nearbyPlayer != player.transform)
            return;

        isNear = false;
        nearbyPlayer = null;

        if (interactionUI != null)
            interactionUI.SetActive(false);
    }

    private IEnumerator RotateDoor(Quaternion targetRotation, bool opening)
    {
        Quaternion startRotation = doorHinge.localRotation;
        float progress = 0f;

        while (progress < 1f)
        {
            progress += Time.deltaTime * rotateSpeed;
            doorHinge.localRotation =
                Quaternion.Slerp(startRotation, targetRotation, progress);
            yield return null;
        }

        doorHinge.localRotation = targetRotation;
        doorCoroutine = null;

        if (!opening && closeSound != null)
            audioSource.PlayOneShot(closeSound);
    }

    private void BuildPrompt()
    {
        if (interactionUI == null)
            return;

        TMP_Text keyLabel = interactionUI.GetComponent<TMP_Text>();
        if (keyLabel == null)
        {
            Debug.LogWarning(
                "DoorInteraction: Interaction UI에 TMP 텍스트를 연결해 주세요.",
                this
            );
            return;
        }

        Transform canvas = interactionUI.transform.parent;
        if (canvas == null)
            return;

        GameObject oldKeyObject = interactionUI;

        GameObject border = NewImage(
            "F Key Prompt",
            canvas,
            new Color(0.72f, 0.76f, 0.79f, 0.9f)
        );

        RectTransform borderRect = border.GetComponent<RectTransform>();
        borderRect.anchorMin = borderRect.anchorMax = new Vector2(0.5f, 0.5f);
        borderRect.anchoredPosition = new Vector2(0f, -82f);
        borderRect.sizeDelta = new Vector2(34f, 34f);

        GameObject keyBackground = NewImage(
            "Key Background",
            border.transform,
            new Color(0.12f, 0.14f, 0.17f, 1f)
        );

        RectTransform backgroundRect =
            keyBackground.GetComponent<RectTransform>();

        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = new Vector2(1.5f, 1.5f);
        backgroundRect.offsetMax = new Vector2(-1.5f, -1.5f);

        oldKeyObject.transform.SetParent(keyBackground.transform, false);
        oldKeyObject.SetActive(true);

        RectTransform keyRect = oldKeyObject.GetComponent<RectTransform>();
        keyRect.anchorMin = Vector2.zero;
        keyRect.anchorMax = Vector2.one;
        keyRect.offsetMin = Vector2.zero;
        keyRect.offsetMax = Vector2.zero;

        keyLabel.text = "F";
        keyLabel.fontSize = 21f;
        keyLabel.color = Color.white;
        keyLabel.alignment = TextAlignmentOptions.Center;
        keyLabel.raycastTarget = false;

        interactionUI = border;
        border.SetActive(false);
    }

    private static GameObject NewImage(
        string objectName,
        Transform parent,
        Color color
    )
    {
        GameObject go = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image)
        );

        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        return go;
    }
}