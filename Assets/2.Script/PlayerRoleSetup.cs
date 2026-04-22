using UnityEngine;

public class PlayerRoleSetup : MonoBehaviour
{
    public int ownerClientId;   // 이 플레이어의 주인 번호
    public int myClientId;      // 현재 실행중인 내 클라이언트 번호

    public PlayerController playerController;
    public GameObject playerCamera;

    void Start()
    {
        bool isLocalPlayer = (ownerClientId == myClientId);

        if (playerController != null)
            playerController.enabled = isLocalPlayer;

        if (playerCamera != null)
            playerCamera.SetActive(isLocalPlayer);
    }
}   