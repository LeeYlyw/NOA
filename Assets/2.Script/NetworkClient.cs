using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class NetworkClient : MonoBehaviour
{
    public static NetworkClient Instance;

    // [추가된 부분] 에디터에서 혼자 테스트할 때 체크하세요.
    [Header("Testing Option")]
    [Tooltip("체크 시 C++ 서버 연결 없이 유니티 에디터에서 바로 싱글 테스트를 수행합니다.")]
    public bool offlineMode = false;

    [Header("Network")]
    public string serverIP = "127.0.0.1";
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
    public float sendInterval = 0.05f;

    [Header("Item Sync")]
    public PotionItem[] potionItems;

    private MonsterNetworkSetup[] monsterSetups;
    private TcpClient client;
    private NetworkStream stream;
    private bool isConnected = false;

    private float sendTimer = 0f;
    private string receiveBuffer = "";
    private PlayerController localPlayerController;

    private Vector3 targetRemotePosition;
    private Quaternion targetRemoteRotation;
    private bool hasRemoteState = false;
    private float targetRemoteSpeed;
    private bool targetRemoteIsRunning;
    private bool targetRemoteIsCrouching;

    void Awake()
    {
        Instance = this;
        Application.runInBackground = true;
    }

    void Start()
    {
        SetupPlayersByPlayerId();
        monsterSetups = FindObjectsOfType<MonsterNetworkSetup>();
        potionItems = FindObjectsOfType<PotionItem>(true);

        // [수정된 부분] 오프라인 모드면 서버 연결 패스
        if (offlineMode)
        {
            Debug.Log("<color=yellow>[NetworkClient] 오프라인 싱글 테스트 모드로 실행됩니다. 서버 연결을 건너뜁니다.</color>");
            return;
        }

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
        // [수정된 부분] 오프라인 모드면 수신/송신 루프 패스
        if (offlineMode) return;

        if (!isConnected || stream == null) return;

        SendLocalPlayerTransform();
        ReceivePackets();
        ApplyRemotePlayerTransform();
    }

    void SetupPlayersByPlayerId()
    {
        if (!autoSetupPlayers || player1Object == null) return;

        // [수정된 부분] 오프라인 모드에서는 무조건 로컬 플레이어로 강제 셋업
        bool isPlayer1Local = offlineMode || (playerId == 1);

        RemotePlayer player1Remote = player1Object.GetComponent<RemotePlayer>();
        if (player1Remote != null) player1Remote.SetupPlayer(isPlayer1Local);

        if (player2Object != null)
        {
            RemotePlayer player2Remote = player2Object.GetComponent<RemotePlayer>();
            if (player2Remote != null) player2Remote.SetupPlayer(!isPlayer1Local);

            // [추가된 부분] 싱글 플레이 시 시야를 가리는 더미(2P)를 꺼버림
            if (offlineMode) player2Object.SetActive(false);
        }

        PlayerRoleSetup player1Role = player1Object.GetComponent<PlayerRoleSetup>();
        if (player1Role != null)
        {
            player1Role.Setup(1, 1, PlayerRole.Explorer);
        }

        localPlayerTransform = player1Object.transform;
        localPlayerController = localPlayerTransform.GetComponent<PlayerController>();
    }

    void ConnectToServer()
    {
        try
        {
            client = new TcpClient();
            client.Connect(serverIP, serverPort);
            stream = client.GetStream();
            isConnected = true;
            Debug.Log("[Network] 서버 연결 성공");
        }
        catch (Exception e)
        {
            Debug.LogError("[Network] 서버 연결 실패: " + e.Message);
        }
    }

    void SendLocalPlayerTransform()
    {
        if (localPlayerTransform == null) return;
        sendTimer += Time.deltaTime;
        if (sendTimer < sendInterval) return;
        sendTimer = 0f;

        Vector3 pos = localPlayerTransform.position;
        float rotY = localPlayerTransform.eulerAngles.y;
        float animSpeed = localPlayerController != null ? localPlayerController.CurrentAnimSpeed : 0f;
        bool isRunning = localPlayerController != null && localPlayerController.IsRunningState;
        bool isCrouching = localPlayerController != null && localPlayerController.IsCrouchingState;

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "MOVE|{0}|{1:F2}|{2:F2}|{3:F2}|{4:F2}|{5:F2}|{6}|{7}\n",
            playerId, pos.x, pos.y, pos.z, rotY, animSpeed,
            isRunning ? 1 : 0, isCrouching ? 1 : 0
        );
        SendMessageToServer(message, "플레이어 위치 전송 실패");
    }

    public void SendNoise(Vector3 position, float noiseAmount)
    {
        if (offlineMode) return;
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "C_NOISE|{0}|{1:F2}|{2:F2}|{3:F2}|{4:F2}\n",
            playerId, position.x, position.y, position.z, noiseAmount
        );
        SendMessageToServer(message, "소음 정보 전송 실패");
    }

    public void SendItemPickupRequest(int itemId, ItemData itemData)
    {
        if (offlineMode)
        {
            // 오프라인 모드에서는 서버 응답 없이 로컬에서 즉시 획득 처리
            ApplyOfflineItemPickup(itemId);
            return;
        }

        string itemType = itemData != null ? itemData.type.ToString() : "Unknown";
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "C_ITEM_PICKUP|{0}|{1}|{2}\n",
            playerId, itemId, itemType
        );
        SendMessageToServer(message, "아이템 획득 요청 전송 실패");
    }

    public void SendItemUseRequest(string itemType)
    {
        if (offlineMode)
        {
            // 오프라인 모드에서는 서버 응답 없이 로컬에서 효과 즉시 적용
            if (itemType == "Heal" && localPlayerController != null)
            {
                localPlayerController.SetHp(100);
            }
            else if (itemType == "Stealth" && player1Object != null)
            {
                PlayerStealth stealth = player1Object.GetComponent<PlayerStealth>();
                if (stealth != null) stealth.ActivateStealth();
            }
            return;
        }

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "C_ITEM_USE|{0}|{1}\n",
            playerId, itemType
        );
        SendMessageToServer(message, "아이템 사용 요청 전송 실패");
    }

    public void SendPlayerReviveRequest(int targetPlayerId)
    {
        if (offlineMode) return;
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "C_PLAYER_REVIVE|{0}|{1}\n",
            playerId, targetPlayerId
        );
        SendMessageToServer(message, "부활 요청 전송 실패");
    }

    void ApplyOfflineItemPickup(int itemId)
    {
        if (potionItems == null || potionItems.Length == 0)
            potionItems = FindObjectsOfType<PotionItem>(true);

        foreach (PotionItem item in potionItems)
        {
            if (item != null && item.itemId == itemId)
            {
                item.ApplyServerPickup(1);
                return;
            }
        }
    }

    void SendMessageToServer(string message, string errorMessage)
    {
        if (!isConnected || stream == null) return;
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
                byte[] buffer = new byte[1024];
                int length = stream.Read(buffer, 0, buffer.Length);
                if (length <= 0) break;
                receiveBuffer += Encoding.UTF8.GetString(buffer, 0, length);
            }
            ProcessReceiveBuffer();
        }
        catch (Exception e)
        {
            Debug.LogError("[Network] 수신 실패: " + e.Message);
            isConnected = false;
        }
    }

    void ProcessReceiveBuffer()
    {
        while (true)
        {
            int newlineIndex = receiveBuffer.IndexOf('\n');
            if (newlineIndex < 0) break;
            string packet = receiveBuffer.Substring(0, newlineIndex).Trim();
            receiveBuffer = receiveBuffer.Substring(newlineIndex + 1);

            if (!string.IsNullOrEmpty(packet)) ProcessPacket(packet);
        }
    }

    void ProcessPacket(string packet)
    {
        string[] parts = packet.Split('|');
        if (parts.Length < 1) return;

        switch (parts[0])
        {
            case "MOVE": ProcessMovePacket(parts); break;
            case "S_MONSTER_STATE": ProcessMonsterStatePacket(parts); break;
            case "S_PLAYER_DAMAGE": ProcessPlayerDamagePacket(parts); break;
            case "S_PLAYER_HP": ProcessPlayerHpPacket(parts); break;
            case "S_ITEM_PICKUP": ProcessItemPickupPacket(parts); break;
            case "S_CLUE_COUNT": ProcessClueCountPacket(parts); break;
            case "S_PLAYER_REVIVE": ProcessPlayerRevivePacket(parts); break;
            case "S_GAME_CLEAR":
                if (ClueManager.instance != null) ClueManager.instance.ShowEnding();
                break;
            case "COUNT":
                if (parts.Length >= 2 && int.TryParse(parts[1], out int count))
                    if (LobbyManager.Instance != null) LobbyManager.Instance.SetPlayerCount(count);
                break;
        }
    }

    void ProcessMovePacket(string[] parts)
    {
        if (parts.Length < 9) return;
        if (!int.TryParse(parts[1], out int receivedPlayerId) || receivedPlayerId == playerId) return;

        if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
            float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float rotY))
        {
            targetRemotePosition = new Vector3(x, y, z);
            targetRemoteRotation = Quaternion.Euler(0f, rotY, 0f);
            float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out targetRemoteSpeed);
            targetRemoteIsRunning = parts[7] == "1";
            targetRemoteIsCrouching = parts[8] == "1";
            hasRemoteState = true;
        }
    }

    void ProcessMonsterStatePacket(string[] parts)
    {
        if (parts.Length < 11) return;
        if (!int.TryParse(parts[1], out int monsterId)) return;

        if (float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
            float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float rotY) &&
            float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed))
        {
            bool isWalk = parts[9] == "1";
            bool isAttack = parts[10] == "1";
            ApplyRemoteMonsterTransform(monsterId, new Vector3(x, y, z), Quaternion.Euler(0f, rotY, 0f), speed, isWalk, isAttack);
        }
    }

    void ProcessPlayerDamagePacket(string[] parts)
    {
        if (parts.Length < 3) return;
        if (int.TryParse(parts[1], out int targetPlayerId) && int.TryParse(parts[2], out int damage))
        {
            ApplyPlayerDamage(targetPlayerId, damage);
        }
    }

    void ProcessPlayerHpPacket(string[] parts)
    {
        if (parts.Length < 3) return;
        if (int.TryParse(parts[1], out int targetPlayerId) && int.TryParse(parts[2], out int hp))
        {
            GameObject targetObject = (targetPlayerId == 1) ? player1Object : player2Object;
            if (targetObject != null)
            {
                PlayerController controller = targetObject.GetComponent<PlayerController>();
                if (controller != null) controller.SetHp(hp);
            }
        }
    }

    void ApplyPlayerDamage(int targetPlayerId, int damage)
    {
        GameObject targetObject = (targetPlayerId == 1) ? player1Object : player2Object;
        if (targetObject == null) return;

        PlayerController playerController = targetObject.GetComponent<PlayerController>();
        if (playerController == null) return;

        bool wasDead = playerController.IsDeadState;
        playerController.TakeDamage(damage);
        bool isDeadNow = playerController.IsDeadState;

        RemotePlayer remotePlayer = targetObject.GetComponent<RemotePlayer>();
        if (remotePlayer != null && !playerController.isLocalPlayer)
        {
            if (isDeadNow) remotePlayer.PlayDeathAnimation();
            else if (!wasDead) remotePlayer.PlayHitAnimation();
        }
    }

    void ProcessItemPickupPacket(string[] parts)
    {
        if (parts.Length < 3) return;
        if (int.TryParse(parts[1], out int itemId) && int.TryParse(parts[2], out int pickedPlayerId))
        {
            if (potionItems == null || potionItems.Length == 0)
                potionItems = FindObjectsOfType<PotionItem>(true);

            foreach (PotionItem item in potionItems)
            {
                if (item != null && item.itemId == itemId)
                {
                    item.ApplyServerPickup(pickedPlayerId);
                    return;
                }
            }
        }
    }

    void ProcessClueCountPacket(string[] parts)
    {
        if (parts.Length < 3) return;
        if (int.TryParse(parts[1], out int clueCount) && int.TryParse(parts[2], out int needCount))
        {
            if (ClueManager.instance != null)
            {
                ClueManager.instance.SetClueCount(clueCount, needCount);
            }
        }
    }

    void ProcessPlayerRevivePacket(string[] parts)
    {
        if (parts.Length < 2) return;
        if (int.TryParse(parts[1], out int targetPlayerId))
        {
            GameObject targetObj = (targetPlayerId == 1) ? player1Object : player2Object;
            if (targetObj != null)
            {
                PlayerController controller = targetObj.GetComponent<PlayerController>();
                if (controller != null) controller.Revive();
            }
        }
    }

    void ApplyRemotePlayerTransform()
    {
        if (!hasRemoteState || remotePlayerTransform == null) return;
        RemotePlayer remotePlayer = remotePlayerTransform.GetComponent<RemotePlayer>();
        if (remotePlayer != null)
        {
            remotePlayer.SetState(targetRemotePosition, targetRemoteRotation);
            remotePlayer.SetAnimationState(targetRemoteSpeed, targetRemoteIsRunning, targetRemoteIsCrouching);
        }
    }

    void ApplyRemoteMonsterTransform(int monsterId, Vector3 pos, Quaternion rot, float speed, bool isWalk, bool isAttack)
    {
        if (monsterSetups == null || monsterSetups.Length == 0)
            monsterSetups = FindObjectsOfType<MonsterNetworkSetup>();

        foreach (MonsterNetworkSetup monster in monsterSetups)
        {
            if (monster != null && monster.monsterId == monsterId)
            {
                RemoteMonster remoteMonster = monster.GetComponent<RemoteMonster>();
                if (remoteMonster != null)
                {
                    remoteMonster.SetState(pos, rot);
                    remoteMonster.SetAnimationState(speed, isWalk, isAttack);
                }
                return;
            }
        }
    }

    private void OnApplicationQuit()
    {
        if (stream != null) stream.Close();
        if (client != null) client.Close();
    }
}