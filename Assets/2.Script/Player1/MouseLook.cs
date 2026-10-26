using UnityEngine;

public class MouseLook : MonoBehaviour
{
    public float mouseSensitivity = 150f;
    public Transform playerBody;

    float xRotation = 0f;
    PlayerController playerController;
    private Camera viewCamera;
    private Vector3 restingLocalPosition;
    private readonly RaycastHit[] cameraHits = new RaycastHit[32];

    void Start()
    {
        if (playerBody != null) playerController = playerBody.GetComponent<PlayerController>();
        viewCamera = GetComponent<Camera>();
        restingLocalPosition = transform.localPosition;
        if (viewCamera != null) viewCamera.nearClipPlane = Mathf.Min(viewCamera.nearClipPlane, 0.05f);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (playerBody == null || (playerController != null && !playerController.isLocalPlayer) ||
            Cursor.lockState != CursorLockMode.Locked)
            return;

        // Mouse axes already report movement for this frame. Multiplying by the
        // frame time makes a pickup hitch turn the camera farther than intended.
        float degreesPerAxisUnit = mouseSensitivity / 60f;
        float mouseX = Input.GetAxis("Mouse X") * degreesPerAxisUnit;
        float mouseY = Input.GetAxis("Mouse Y") * degreesPerAxisUnit;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseX);
    }

    void LateUpdate()
    {
        if (playerBody == null || transform.parent == null || viewCamera == null ||
            playerController == null || !playerController.isLocalPlayer) return;

        Vector3 desired = transform.parent.TransformPoint(restingLocalPosition);
        Vector3 anchor = playerBody.position;
        anchor.y = desired.y;
        Vector3 offset = desired - anchor;
        float distance = offset.magnitude;
        if (distance < 0.001f) return;

        // Keep the near-plane corners outside walls, ignoring our own body.
        float halfHeight = viewCamera.nearClipPlane * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float radius = Mathf.Max(0.08f, halfHeight * Mathf.Sqrt(1f + viewCamera.aspect * viewCamera.aspect));
        int count = Physics.SphereCastNonAlloc(anchor, radius, offset / distance,
            cameraHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float clearDistance = distance;
        for (int i = 0; i < count; i++)
        {
            if (cameraHits[i].collider.transform.IsChildOf(playerBody)) continue;
            clearDistance = Mathf.Min(clearDistance, Mathf.Max(0f, cameraHits[i].distance - 0.02f));
        }
        if (count == cameraHits.Length) clearDistance = 0f;
        transform.position = anchor + offset / distance * clearDistance;
    }
}
