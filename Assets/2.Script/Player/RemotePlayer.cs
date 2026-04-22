using UnityEngine;

public class RemotePlayer : MonoBehaviour
{
    public float smoothSpeed = 10f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool isInitialized = false;

    void Start()
    {
        targetPosition = transform.position;
        targetRotation = transform.rotation;
    }

    void Update()
    {
        if (!isInitialized) return;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            Time.deltaTime * smoothSpeed
        );

        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * smoothSpeed
        );
    }

    public void SetState(Vector3 newPosition, Quaternion newRotation)
    {
        targetPosition = newPosition;
        targetRotation = newRotation;

        if (!isInitialized)
        {
            transform.position = newPosition;
            transform.rotation = newRotation;
            isInitialized = true;
        }
    }
}