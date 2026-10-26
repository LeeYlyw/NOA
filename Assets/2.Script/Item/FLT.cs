using UnityEngine;

public class FLT : MonoBehaviour
{
    public GameObject flashlight;
    private PlayerController player;

    void Awake()
    {
        player = GetComponentInParent<PlayerController>();
        if (flashlight != null && flashlight != gameObject) return;
        foreach (Light light in GetComponentsInChildren<Light>(true))
        {
            if (light.name != "Flashlight") continue;
            flashlight = light.gameObject;
            break;
        }
    }

    void Start()
    {
        if (flashlight != null) flashlight.SetActive(false);
    }

    void Update()
    {
        if (player != null && !player.isLocalPlayer) return;
        if (Input.GetKeyDown(KeyCode.U) && flashlight != null)
            flashlight.SetActive(!flashlight.activeSelf);
    }
}