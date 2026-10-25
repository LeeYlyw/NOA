using UnityEngine;

public class MouseLook : MonoBehaviour
{
    public float mouseSensitivity = 150f;
    public Transform playerBody;

    float xRotation = 0f;
    PlayerController playerController;

    void Start()
    {
        if (playerBody != null) playerController = playerBody.GetComponent<PlayerController>();
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
}
