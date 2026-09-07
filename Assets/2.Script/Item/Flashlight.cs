using UnityEngine;

public class Flashlight : MonoBehaviour
{
    [SerializeField] private Light flashlightLight; // 손전등에 달린 Spot Light 컴포넌트 연결
    private bool isLightOn = false;

    public void ToggleLight()
    {
        isLightOn = !isLightOn;
        if (flashlightLight != null)
        {
            flashlightLight.enabled = isLightOn;
        }
    }

    public void TurnOn()
    {
        isLightOn = true;
        if (flashlightLight != null) flashlightLight.enabled = true;
    }
}