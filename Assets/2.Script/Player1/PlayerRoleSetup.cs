using UnityEngine;

public enum PlayerRole
{
    Detector, // 감지자
    Explorer  // 탐색자
}

public class PlayerRoleSetup : MonoBehaviour
{
    [Header("Network Owner")]
    public int ownerClientId;   // 이 캐릭터의 주인 번호
    public int myClientId;      // 현재 실행 중인 클라이언트 번호

    [Header("Role")]
    public PlayerRole role;

    public bool IsLocalOwner
    {
        get { return ownerClientId == myClientId; }
    }

    public bool IsDetector
    {
        get { return role == PlayerRole.Detector; }
    }

    public bool IsExplorer
    {
        get { return role == PlayerRole.Explorer; }
    }

    public void Setup(int ownerId, int myId, PlayerRole newRole)
    {
        ownerClientId = ownerId;
        myClientId = myId;
        role = newRole;

        Debug.Log(
            gameObject.name +
            " 역할 설정 완료 / Owner: " + ownerClientId +
            " / My: " + myClientId +
            " / Role: " + role
        );
    }
}