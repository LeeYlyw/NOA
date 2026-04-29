using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class NetworkClient : MonoBehaviour
{
    public static NetworkClient Instance;

    [Header("Network")]
    public string serverIP = "192.168.54.179";
    public int serverPort = 7777;
    public int playerId = 1;

    [Header("Players")]
    public Transform localPlayerTransform;
    public Transform remotePlayerTransform;

    [Header("Auto Setup")]
    public bool autoSetupPlayers = true;
    public GameObject player1Object;
    public GameObject player2Object;

    [Header("Sync")]
    public float sendInterval = 0.1f;

    [Header("Monster Sync")]
    public float monsterSendInterval = 0.1f;

    private TcpClient client;
    private NetworkStream stream;
    private bool isConnected = false;

    private float sendTimer = 0f;
    private float monsterSendTimer = 0f;

    private string receiveBuffer = "";

    private PlayerController localPlayerController;

    private Vector3 targetRemotePosition;
    private Quaternion targetRemoteRotation;
    private bool hasRemoteState = false;

    private float targetRemoteSpeed;
    private bool targetRemoteIsRunning;
    private bool targetRemoteIsCrouching;

    private MonsterNetworkSetup[] monsterSetups;


    [Header("Item Sync")]
    public PotionItem[] potionItems;

    void Awake()
    {
        Instance = this;
        Application.runInBackground = true;
    }

    void Start()
    {
        SetupPlayersByPlayerId();
        SetupMonstersByPlayerId();

        monsterSetups = FindObjectsOfType<MonsterNetworkSetup>();
        potionItems = FindObjectsOfType<PotionItem>(true);

        ConnectToServer();

        if (remotePlayerTransform != null)
        {
            targetRemotePosition = remotePlayerTransform.position;
            targetRemoteRotation = remotePlayerTransform.rotation;
        }

        if (localPlayerTransform != null)
        {
            localPlayerController = localPlayerTransform.GetComponent<PlayerController>();
        }
    }

    void Update()
    {
        if (!isConnected || stream == null)
            return;

        SendLocalPlayerTransform();
        SendMonsterTransforms();

        ReceivePackets();

        ApplyRemotePlayerTransform();
    }

    void SetupPlayersByPlayerId()
    {
        if (!autoSetupPlayers)
            return;

        if (player1Object == null || player2Object == null)
        {
            Debug.LogWarning("Player1 Object 또는 Player2 Object가 비어 있습니다.");
            return;
        }

        bool isPlayer1Local = playerId == 1;
        bool isPlayer2Local = playerId == 2;

        RemotePlayer player1Remote = player1Object.GetComponent<RemotePlayer>();
        RemotePlayer player2Remote = player2Object.GetComponent<RemotePlayer>();

        if (player1Remote != null)
            player1Remote.SetupPlayer(isPlayer1Local);

        if (player2Remote != null)
            player2Remote.SetupPlayer(isPlayer2Local);

        if (playerId == 1)
        {
            localPlayerTransform = player1Object.transform;
            remotePlayerTransform = player2Object.transform;
        }
        else if (playerId == 2)
        {
            localPlayerTransform = player2Object.transform;
            remotePlayerTransform = player1Object.transform;
        }
    }

    void SetupMonstersByPlayerId()
    {
        bool hasMonsterAuthority = playerId == 1;

        MonsterNetworkSetup[] monsters = FindObjectsOfType<MonsterNetworkSetup>();

        foreach (MonsterNetworkSetup monster in monsters)
        {
            monster.SetupMonster(hasMonsterAuthority);
        }

        Debug.Log("몬스터 권한 설정 완료 / PlayerId: " + playerId + " / Authority: " + hasMonsterAuthority);
    }

    public void SendItemPickup(int itemId)
    {
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "ITEM_PICKUP|{0}|{1}\n",
            itemId,
            playerId
        );

        SendMessageToServer(message, "아이템 획득 전송 실패");
        Debug.Log("아이템 획득 전송: " + message);
    }

    void ConnectToServer()
    {
        try
        {
            client = new TcpClient();
            client.Connect(serverIP, serverPort);
            stream = client.GetStream();
            isConnected = true;

            Debug.Log("서버 연결 성공");
        }
        catch (Exception e)
        {
            Debug.LogError("서버 연결 실패: " + e.Message);
        }
    }

    void SendLocalPlayerTransform()
    {
        if (localPlayerTransform == null)
            return;

        sendTimer += Time.deltaTime;

        if (sendTimer < sendInterval)
            return;

        sendTimer = 0f;

        Vector3 pos = localPlayerTransform.position;
        float rotY = localPlayerTransform.eulerAngles.y;

        float animSpeed = 0f;
        bool isRunning = false;
        bool isCrouching = false;

        if (localPlayerController != null)
        {
            animSpeed = localPlayerController.CurrentAnimSpeed;
            isRunning = localPlayerController.IsRunningState;
            isCrouching = localPlayerController.IsCrouchingState;
        }

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "MOVE|{0}|{1:F2}|{2:F2}|{3:F2}|{4:F2}|{5:F2}|{6}|{7}\n",
            playerId,
            pos.x,
            pos.y,
            pos.z,
            rotY,
            animSpeed,
            isRunning ? 1 : 0,
            isCrouching ? 1 : 0
        );

        SendMessageToServer(message, "플레이어 위치 전송 실패");
    }

    void SendMonsterTransforms()
    {
        if (playerId != 1)
            return;

        if (monsterSetups == null || monsterSetups.Length == 0)
            return;

        monsterSendTimer += Time.deltaTime;

        if (monsterSendTimer < monsterSendInterval)
            return;

        monsterSendTimer = 0f;

        foreach (MonsterNetworkSetup monster in monsterSetups)
        {
            if (monster == null)
                continue;

            Transform monsterTransform = monster.transform;

            Vector3 pos = monsterTransform.position;
            float rotY = monsterTransform.eulerAngles.y;

            float speed = 0f;
            bool isWalk = false;
            bool isAttack = false;

            Animator animator = monster.GetComponent<Animator>();

            if (animator != null)
            {
                speed = animator.GetFloat("Speed");
                isWalk = animator.GetBool("isWalk");
                isAttack = animator.GetBool("isAttack");
            }

            string message = string.Format(
                CultureInfo.InvariantCulture,
                "MONSTER_MOVE|{0}|{1:F2}|{2:F2}|{3:F2}|{4:F2}|{5:F2}|{6}|{7}\n",
                monster.monsterId,
                pos.x,
                pos.y,
                pos.z,
                rotY,
                speed,
                isWalk ? 1 : 0,
                isAttack ? 1 : 0
            );

            SendMessageToServer(message, "몬스터 위치/애니메이션 전송 실패");
        }
    }

    public void SendPlayerDamage(int targetPlayerId, int damage)
    {
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "PLAYER_DAMAGE|{0}|{1}\n",
            targetPlayerId,
            damage
        );

        SendMessageToServer(message, "플레이어 데미지 전송 실패");
        Debug.Log("플레이어 데미지 전송: " + message);

        // 현재 서버는 보낸 클라이언트에게 다시 패킷을 돌려주지 않음.
        // 그래서 내가 보낸 데미지 패킷도 내 화면에 즉시 반영해야 함.
        if (targetPlayerId != playerId)
        {
            ApplyPlayerDamage(targetPlayerId, damage);
        }
    }

    void SendMessageToServer(string message, string errorMessage)
    {
        if (!isConnected || stream == null)
            return;

        byte[] data = Encoding.UTF8.GetBytes(message);

        try
        {
            stream.Write(data, 0, data.Length);
        }
        catch (Exception e)
        {
            Debug.LogError(errorMessage + ": " + e.Message);
            isConnected = false;
        }
    }

    void ReceivePackets()
    {
        try
        {
            while (stream.DataAvailable)
            {
                byte[] buffer = new byte[512];
                int length = stream.Read(buffer, 0, buffer.Length);

                if (length <= 0)
                    break;

                receiveBuffer += Encoding.UTF8.GetString(buffer, 0, length);
            }

            ProcessReceiveBuffer();
        }
        catch (Exception e)
        {
            Debug.LogError("수신 실패: " + e.Message);
            isConnected = false;
        }
    }

    void ProcessReceiveBuffer()
    {
        while (true)
        {
            int newlineIndex = receiveBuffer.IndexOf('\n');

            if (newlineIndex < 0)
                break;

            string packet = receiveBuffer.Substring(0, newlineIndex).Trim();
            receiveBuffer = receiveBuffer.Substring(newlineIndex + 1);

            if (string.IsNullOrEmpty(packet))
                continue;

            ProcessPacket(packet);
        }
    }

    void ProcessPacket(string packet)
    {
        Debug.Log("클라가 받은 패킷: " + packet);

        string[] parts = packet.Split('|');

        if (parts.Length < 2)
            return;

        if (parts[0] == "COUNT")
        {
            if (int.TryParse(parts[1], out int count))
            {
                if (LobbyManager.Instance != null)
                    LobbyManager.Instance.SetPlayerCount(count);
            }

            return;
        }

        if (parts[0] == "MOVE")
        {
            ProcessMovePacket(parts, packet);
            return;
        }

        if (parts[0] == "MONSTER_MOVE")
        {
            ProcessMonsterMovePacket(parts, packet);
            return;
        }

        if (parts[0] == "PLAYER_DAMAGE")
        {
            ProcessPlayerDamagePacket(parts, packet);
            return;
        }

        if (parts[0] == "ITEM_PICKUP")
        {
            ProcessItemPickupPacket(parts, packet);
            return;
        }
    }

    void ProcessMovePacket(string[] parts, string packet)
    {
        if (parts.Length != 9)
        {
            Debug.LogWarning("MOVE 패킷 형식이 맞지 않음: " + packet);
            return;
        }

        if (!int.TryParse(parts[1], out int receivedPlayerId))
            return;

        if (receivedPlayerId == playerId)
            return;

        bool okX = float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
        bool okY = float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
        bool okZ = float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
        bool okRot = float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float rotY);
        bool okSpeed = float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed);

        bool okRunning = int.TryParse(parts[7], out int runningValue);
        bool okCrouching = int.TryParse(parts[8], out int crouchingValue);

        if (!okX || !okY || !okZ || !okRot || !okSpeed || !okRunning || !okCrouching)
            return;

        targetRemotePosition = new Vector3(x, y, z);
        targetRemoteRotation = Quaternion.Euler(0f, rotY, 0f);

        targetRemoteSpeed = speed;
        targetRemoteIsRunning = runningValue == 1;
        targetRemoteIsCrouching = crouchingValue == 1;

        hasRemoteState = true;
    }

    void ProcessMonsterMovePacket(string[] parts, string packet)
    {
        if (parts.Length != 9)
        {
            Debug.LogWarning("MONSTER_MOVE 패킷 형식이 맞지 않음: " + packet);
            return;
        }

        if (!int.TryParse(parts[1], out int receivedMonsterId))
            return;

        bool okX = float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
        bool okY = float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
        bool okZ = float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
        bool okRot = float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float rotY);
        bool okSpeed = float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed);

        bool okWalk = int.TryParse(parts[7], out int walkValue);
        bool okAttack = int.TryParse(parts[8], out int attackValue);

        if (!okX || !okY || !okZ || !okRot || !okSpeed || !okWalk || !okAttack)
            return;

        ApplyRemoteMonsterTransform(
            receivedMonsterId,
            new Vector3(x, y, z),
            Quaternion.Euler(0f, rotY, 0f),
            speed,
            walkValue == 1,
            attackValue == 1
        );
    }

    void ProcessPlayerDamagePacket(string[] parts, string packet)
    {
        if (parts.Length != 3)
        {
            Debug.LogWarning("PLAYER_DAMAGE 패킷 형식이 맞지 않음: " + packet);
            return;
        }

        if (!int.TryParse(parts[1], out int targetPlayerId))
            return;

        if (!int.TryParse(parts[2], out int damage))
            return;

        ApplyPlayerDamage(targetPlayerId, damage);
    }

    void ProcessItemPickupPacket(string[] parts, string packet)
    {
        if (parts.Length != 3)
        {
            Debug.LogWarning("ITEM_PICKUP 패킷 형식이 맞지 않음: " + packet);
            return;
        }

        if (!int.TryParse(parts[1], out int itemId))
            return;

        if (!int.TryParse(parts[2], out int pickedPlayerId))
            return;

        if (pickedPlayerId == playerId)
            return;

        if (potionItems == null || potionItems.Length == 0)
            potionItems = FindObjectsOfType<PotionItem>(true);

        foreach (PotionItem item in potionItems)
        {
            if (item == null)
                continue;

            if (item.itemId != itemId)
                continue;

            item.ApplyRemotePickup();
            Debug.Log("상대 아이템 획득 반영 완료 / itemId: " + itemId);
            return;
        }

        Debug.LogWarning("itemId에 해당하는 아이템을 찾지 못함: " + itemId);
    }

    void ApplyRemotePlayerTransform()
    {
        if (!hasRemoteState || remotePlayerTransform == null)
            return;

        RemotePlayer remotePlayer = remotePlayerTransform.GetComponent<RemotePlayer>();

        if (remotePlayer != null)
        {
            remotePlayer.SetState(targetRemotePosition, targetRemoteRotation);
            remotePlayer.SetAnimationState(
                targetRemoteSpeed,
                targetRemoteIsRunning,
                targetRemoteIsCrouching
            );
        }
        else
        {
            remotePlayerTransform.position = targetRemotePosition;
            remotePlayerTransform.rotation = targetRemoteRotation;
        }
    }

    void ApplyRemoteMonsterTransform(
        int monsterId,
        Vector3 position,
        Quaternion rotation,
        float speed,
        bool isWalk,
        bool isAttack
    )
    {
        if (playerId == 1)
            return;

        if (monsterSetups == null || monsterSetups.Length == 0)
            monsterSetups = FindObjectsOfType<MonsterNetworkSetup>();

        foreach (MonsterNetworkSetup monster in monsterSetups)
        {
            if (monster == null)
                continue;

            if (monster.monsterId != monsterId)
                continue;

            RemoteMonster remoteMonster = monster.GetComponent<RemoteMonster>();

            if (remoteMonster != null)
            {
                remoteMonster.SetState(position, rotation);
                remoteMonster.SetAnimationState(speed, isWalk, isAttack);
            }
            else
            {
                monster.transform.position = position;
                monster.transform.rotation = rotation;
            }

            return;
        }

        Debug.LogWarning("monsterId에 해당하는 몬스터를 찾지 못함: " + monsterId);
    }

    void ApplyPlayerDamage(int targetPlayerId, int damage)
    {
        GameObject targetObject = null;

        if (targetPlayerId == 1)
            targetObject = player1Object;
        else if (targetPlayerId == 2)
            targetObject = player2Object;

        if (targetObject == null)
        {
            Debug.LogWarning("데미지 적용 대상 플레이어 오브젝트를 찾지 못함: " + targetPlayerId);
            return;
        }

        PlayerController playerController = targetObject.GetComponent<PlayerController>();

        if (playerController == null)
        {
            Debug.LogWarning("데미지 적용 대상에 PlayerController가 없음: " + targetObject.name);
            return;
        }

        bool wasDead = playerController.IsDeadState;

        playerController.TakeDamage(damage);

        bool isDeadNow = playerController.IsDeadState;

        RemotePlayer remotePlayer = targetObject.GetComponent<RemotePlayer>();

        if (remotePlayer != null && !playerController.isLocalPlayer)
        {
            if (isDeadNow)
            {
                remotePlayer.PlayDeathAnimation();
            }
            else if (!wasDead)
            {
                remotePlayer.PlayHitAnimation();
            }
        }

        Debug.Log("PLAYER_DAMAGE 적용 완료 / target: " + targetPlayerId + " / damage: " + damage);
    }

    void OnApplicationQuit()
    {
        if (stream != null)
            stream.Close();

        if (client != null)
            client.Close();
    }
}