using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

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
    // Assigned by the server after connecting; never configured per build.
    public int playerId { get; private set; }
    public bool HasAssignedPlayerId => playerId == 1 || playerId == 2;

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
    private DoorInteraction[] networkDoors = Array.Empty<DoorInteraction>();

    private MonsterNetworkSetup[] monsterSetups;
    private TcpClient client;
    private NetworkStream stream;
    private bool isConnected = false;

    private float sendTimer = 0f;
    private int nextMimicShotId = 1;
    private string receiveBuffer = "";
    private PlayerController localPlayerController;

    private Vector3 targetRemotePosition;
    private Quaternion targetRemoteRotation;
    private bool hasRemoteState = false;
    private float targetRemoteSpeed;
    private bool targetRemoteIsRunning;
    private bool targetRemoteIsCrouching;
    private bool targetRemoteIsGrounded;
    private int targetRemoteJumpSequence;

    void Awake()
    {
        Instance = this;
        playerId = offlineMode ? 1 : 0;
        Application.runInBackground = true;
    }

    public bool IsConnected => isConnected;
    public bool HasRemotePlayerState => hasRemoteState;
    public bool GameStarted { get; private set; }
    public PlayerRole Role1 { get; private set; } = PlayerRole.Healer;
    public PlayerRole Role2 { get; private set; } = PlayerRole.Runner;
    public string ConnectionStatus { get; private set; } = "Connecting...";
    public bool autoStartLocalServer = true;

    void Start()
    {
        if (!offlineMode)
        {
            ConnectionStatus = "Connecting to " + serverIP + ":" + serverPort + "...";
            if (GetComponent<LobbyManager>() == null) gameObject.AddComponent<LobbyManager>();
            StartCoroutine(ConnectAutomatically());
        }
    }

    void Update()
    {
        // 오프라인 모드면 수신/송신 루프 패스
        if (offlineMode) return;

        if (!isConnected || stream == null) return;

        if (client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0)
        { isConnected = false; ConnectionStatus = "Disconnected. Restart the game."; return; }
        ReceivePackets();
        if (!isConnected || !HasAssignedPlayerId || !GameStarted) return;
        SendLocalPlayerTransform();
        ApplyRemotePlayerTransform();
    }

    System.Collections.IEnumerator ConnectAutomatically()
    {
        bool launched = false;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            client = new TcpClient();
            var task = client.ConnectAsync(serverIP, serverPort);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && client.Connected)
            {
                stream = client.GetStream(); isConnected = true; ConnectionStatus = "Connected";
                yield break;
            }
            client.Close();
            if (!launched && autoStartLocalServer && (serverIP == "127.0.0.1" || serverIP == "localhost"))
            {
                launched = true;
                string root = System.IO.Path.GetDirectoryName(Application.dataPath);
                string path = System.IO.Path.Combine(root, "Server", "x64", "Release", "GameServer.exe");
                if (!System.IO.File.Exists(path))
                    path = System.IO.Path.Combine(root, "Server", "GameServer.exe");
                try
                {
                    if (!System.IO.File.Exists(path))
                        throw new System.IO.FileNotFoundException("GameServer.exe missing. Run Server/BuildServer.ps1 before multiplayer testing.");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = System.IO.Path.GetDirectoryName(path) });
                    ConnectionStatus = "Starting server...";
                }
                catch (Exception e) { ConnectionStatus = e.Message; yield break; }
            }
            yield return new WaitForSecondsRealtime(0.3f);
        }
        ConnectionStatus = "Connection failed. Check server address and restart.";
    }

    public void SelectRole(int role) => SendMessageToServer("C_ROLE|" + role + "\n", "Role failed");
    public void SetReady(bool value) => SendMessageToServer("C_READY|" + (value ? 1 : 0) + "\n", "Ready failed");
    public void RequestStart() => SendMessageToServer("C_START\n", "Start failed");

    public void SendPuzzlePad(bool active) =>
        SendMessageToServer("C_PUZZLE_PAD|" + playerId + "|" + (active ? 1 : 0) + "\n", "Puzzle pad failed");

    public void SendPuzzleChoice(int index) =>
        SendMessageToServer("C_PUZZLE_CHOOSE|" + playerId + "|" + index + "\n", "Puzzle choice failed");

    public void SendMimicState(int mimicId, int state)
    {
        if (offlineMode || playerId != 1 || mimicId <= 0) return;
        SendMessageToServer($"C_MIMIC_STATE|{playerId}|{mimicId}|{state}\n", "Mimic state failed");
    }

    public int SendMimicFire(int mimicId, int targetId, Vector3 start, Vector3 direction,
                             float speed, int damage)
    {
        if (offlineMode || playerId != 1 || mimicId <= 0 || !isConnected) return 0;
        int shotId = nextMimicShotId++;
        string message = string.Format(CultureInfo.InvariantCulture,
            "C_MIMIC_FIRE|{0}|{1}|{2}|{3}|{4:F3}|{5:F3}|{6:F3}|{7:F3}|{8:F3}|{9:F3}|{10:F2}|{11}\n",
            playerId, mimicId, shotId, targetId, start.x, start.y, start.z,
            direction.x, direction.y, direction.z, speed, damage);
        SendMessageToServer(message, "Mimic projectile failed");
        return shotId;
    }

    public void SendMimicHit(int shotId)
    {
        if (offlineMode || shotId <= 0) return;
        SendMessageToServer($"C_MIMIC_HIT|{playerId}|{shotId}\n", "Mimic hit failed");
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
        bool isGrounded = localPlayerController != null && localPlayerController.IsGroundedState;
        int jumpSequence = localPlayerController != null ? localPlayerController.JumpSequence : 0;

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "MOVE|{0}|{1:F2}|{2:F2}|{3:F2}|{4:F2}|{5:F2}|{6}|{7}|{8}|{9}\n",
            playerId, pos.x, pos.y, pos.z, rotY, animSpeed,
            isRunning ? 1 : 0, isCrouching ? 1 : 0, isGrounded ? 1 : 0, jumpSequence
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

    public void SendNavigationMap(int seed, int width, int height, float originX,
                                  float originZ, float step, float gateX, float gateZ,
                                  float gateHalfWidth, Vector3 monster1, Vector3 monster2,
                                  string edgeMask, DoorInteraction[] doors)
    {
        if (offlineMode || !isConnected || !HasAssignedPlayerId ||
            string.IsNullOrEmpty(edgeMask) || edgeMask.Length != width * height) return;

        string header = string.Format(CultureInfo.InvariantCulture,
            "C_NAV_BEGIN|{0}|{1}|{2}|{3}|{4:F2}|{5:F2}|{6:F2}|{7:F2}|{8:F2}|{9:F2}|{10:F2}|{11:F2}|{12:F2}|{13:F2}|{14:F2}|{15:F2}\n",
            playerId, seed, width, height, originX, originZ, step, gateX, gateZ,
            gateHalfWidth, monster1.x, monster1.y, monster1.z,
            monster2.x, monster2.y, monster2.z);
        SendMessageToServer(header, "Navigation map header failed");

        if (doors != null)
            foreach (DoorInteraction door in doors)
            {
                if (door == null) continue;
                Vector3 anchor = door.GetNetworkAnchor();
                string doorPacket = string.Format(CultureInfo.InvariantCulture,
                    "C_NAV_DOOR|{0}|{1}|{2:F2}|{3:F2}|{4:F2}\n",
                    playerId, door.NetworkId, anchor.x, anchor.y, anchor.z);
                SendMessageToServer(doorPacket, "Navigation door upload failed");
            }

        const int chunkSize = 1000;
        for (int offset = 0, sequence = 0; offset < edgeMask.Length; offset += chunkSize, sequence++)
            SendMessageToServer("C_NAV_CHUNK|" + playerId + "|" + sequence + "|" +
                                edgeMask.Substring(offset, Mathf.Min(chunkSize, edgeMask.Length - offset)) +
                                "\n", "Navigation map chunk failed");
        SendMessageToServer("C_NAV_END|" + playerId + "\n", "Navigation map end failed");
    }

    public void RegisterDoors(DoorInteraction[] doors)
    {
        networkDoors = doors ?? Array.Empty<DoorInteraction>();
    }

    public void SendDoorToggle(int doorId, int openingDirection)
    {
        if (offlineMode || doorId <= 0) return;
        SendMessageToServer($"C_DOOR_TOGGLE|{playerId}|{doorId}|{openingDirection}\n",
            "Door toggle failed");
    }

    public bool SendItemPickupRequest(int itemId, ItemData itemData)
    {
        if (offlineMode)
        {
            // 오프라인 모드에서는 서버 응답 없이 로컬에서 즉시 획득 처리
            ApplyOfflineItemPickup(itemId);
            return true;
        }

        string itemType = itemData != null ? itemData.type.ToString() : "Unknown";
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "C_ITEM_PICKUP|{0}|{1}|{2}\n",
            playerId, itemId, itemType
        );
        if (!isConnected || !HasAssignedPlayerId || stream == null) return false;
        SendMessageToServer(message, "아이템 획득 요청 전송 실패");
        return isConnected;
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

    // [추가] 힐러 스킬: 서버로 특정 플레이어의 체력 회복을 요청합니다.
    public void SendPlayerHealRequest(int targetPlayerId, int healAmount)
    {
        if (offlineMode)
        {
            // 오프라인 모드에서는 서버 없이 즉시 로컬 적용
            GameObject targetObj = (targetPlayerId == 1) ? player1Object : player2Object;
            if (targetObj != null)
            {
                PlayerController controller = targetObj.GetComponent<PlayerController>();
                if (controller != null)
                {
                    controller.SetHp(controller.CurrentHp + healAmount);
                }
            }
            return;
        }

        // C_PLAYER_HEAL | 시전자ID | 대상ID | 회복량
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "C_PLAYER_HEAL|{0}|{1}|{2}\n",
            playerId, targetPlayerId, healAmount
        );
        SendMessageToServer(message, "힐 요청 전송 실패");
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
        if (!isConnected || !HasAssignedPlayerId || stream == null) return;
        byte[] data = Encoding.UTF8.GetBytes(message);
        try
        {
            stream.Write(data, 0, data.Length);
        }
        catch (Exception e)
        {
            Debug.LogError(errorMessage + ": " + e.Message);
            isConnected = false;
            ConnectionStatus = "Disconnected. Restart the game.";
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
            ConnectionStatus = "Disconnected. Restart the game.";
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
            case "ID":
                if (!HasAssignedPlayerId && parts.Length == 2 &&
                    int.TryParse(parts[1], out int assignedPlayerId) && assignedPlayerId >= 1 && assignedPlayerId <= 2)
                {
                    playerId = assignedPlayerId;
                    ConnectionStatus = "Connected - Player " + playerId;
                    Debug.Log("[Network] Server assigned Player " + playerId);
                }
                break;
            case "START":
                // 서버로부터 "START|시드번호" 를 받으면 맵 생성기에 전달
                if (parts.Length >= 2 && int.TryParse(parts[1], out int seed))
                {
                    MapGenerator mg = FindObjectOfType<MapGenerator>();
                    if (HasAssignedPlayerId && !GameStarted && mg != null && parts.Length >= 4 &&
                        int.TryParse(parts[2], out int r1) && int.TryParse(parts[3], out int r2) &&
                        r1 >= 0 && r1 <= 3 && r2 >= 0 && r2 <= 3)
                    {
                        Role1 = (PlayerRole)r1; Role2 = (PlayerRole)r2;
                        GameStarted = true; mg.GenerateMap(seed);
                    }
                }
                break;
            case "LOBBY":
                if (LobbyManager.Instance != null) LobbyManager.Instance.ApplyState(parts);
                break;
            case "ERROR":
                ConnectionStatus = parts.Length > 1 ? parts[1] : "Server error";
                break;
            case "S_NAV_READY":
                Debug.Log("[Network] Server navigation map confirmed by both clients; server AI active.");
                break;
            case "S_NAV_ERROR":
                ConnectionStatus = parts.Length > 1 ? parts[1] : "Navigation map rejected";
                Debug.LogError("[Network] " + ConnectionStatus);
                break;
            case "S_PUZZLE_PADS":
                if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.SetPads(parts);
                break;
            case "S_PUZZLE_CLUE":
                if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.SetClue(parts);
                break;
            case "S_PUZZLE_CHOICES":
                if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.SetChoices(parts);
                break;
            case "S_PUZZLE_PROGRESS":
                if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.SetProgress(parts);
                break;
            case "S_PUZZLE_WRONG":
                if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.SetWrong();
                break;
            case "S_PUZZLE_SOLVED":
                if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.SetSolved();
                break;
            case "MOVE": ProcessMovePacket(parts); break;
            case "S_MONSTER_STATE": ProcessMonsterStatePacket(parts); break;
            case "S_MIMIC_STATE": ProcessMimicStatePacket(parts); break;
            case "S_MIMIC_FIRE": ProcessMimicFirePacket(parts); break;
            case "S_NOISE": ProcessNoisePacket(parts); break;
            case "S_PLAYER_DAMAGE": ProcessPlayerDamagePacket(parts); break;
            case "S_PLAYER_HP": ProcessPlayerHpPacket(parts); break;
            case "S_ITEM_PICKUP": ProcessItemPickupPacket(parts); break;
            case "S_DOOR_STATE": ProcessDoorStatePacket(parts); break;
            case "S_ITEM_PICKUP_DENIED": ProcessItemPickupDeniedPacket(parts); break;
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
        if (parts.Length < 11) return;
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
            targetRemoteIsGrounded = parts[9] == "1";
            if (!int.TryParse(parts[10], out targetRemoteJumpSequence) || targetRemoteJumpSequence < 0) return;
            hasRemoteState = true;
        }
    }

    void ProcessMonsterStatePacket(string[] parts)
    {
        if (parts.Length < 12) return;
        if (!int.TryParse(parts[1], out int monsterId)) return;

        if (float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
            float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float rotY) &&
            float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed) &&
            int.TryParse(parts[11], out int attackSequence))
        {
            bool isWalk = parts[9] == "1";
            bool isAttack = parts[10] == "1";
            ApplyRemoteMonsterTransform(monsterId, new Vector3(x, y, z), Quaternion.Euler(0f, rotY, 0f), speed, isWalk, isAttack, attackSequence, parts[3]);
        }
    }

    MimicLaptop FindMimic(int mimicId)
    {
        foreach (MimicLaptop mimic in FindObjectsByType<MimicLaptop>(FindObjectsSortMode.None))
            if (mimic.NetworkId == mimicId) return mimic;
        return null;
    }

    void ProcessMimicStatePacket(string[] parts)
    {
        if (parts.Length != 3 || !int.TryParse(parts[1], out int mimicId) ||
            !int.TryParse(parts[2], out int state)) return;
        MimicLaptop mimic = FindMimic(mimicId);
        if (mimic != null) mimic.ApplyNetworkState(state);
    }

    void ProcessMimicFirePacket(string[] parts)
    {
        if (parts.Length != 12 || !int.TryParse(parts[1], out int mimicId) ||
            !int.TryParse(parts[2], out int shotId) ||
            !int.TryParse(parts[3], out int targetId) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx) ||
            !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float sy) ||
            !float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float sz) ||
            !float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float dx) ||
            !float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float dy) ||
            !float.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out float dz) ||
            !float.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed) ||
            !int.TryParse(parts[11], out int damage)) return;
        MimicLaptop mimic = FindMimic(mimicId);
        if (mimic != null)
            mimic.SpawnNetworkProjectile(shotId, targetId, new Vector3(sx, sy, sz),
                                         new Vector3(dx, dy, dz), speed, damage);
    }

    void ProcessNoisePacket(string[] parts)
    {
        if (parts.Length < 6 || playerId != 1) return;
        if (!int.TryParse(parts[1], out int sourcePlayerId)) return;
        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
            !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float amount))
            return;

        GameObject source = sourcePlayerId == 1 ? player1Object : player2Object;
        if (source == null) return;
        foreach (SoundMonsterAI soundMonster in FindObjectsOfType<SoundMonsterAI>())
            soundMonster.HearNetworkNoise(new Vector3(x, y, z), amount, source);
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

        playerController.TakeDamage(damage);
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

    void ProcessDoorStatePacket(string[] parts)
    {
        if (parts.Length != 4 || !int.TryParse(parts[1], out int doorId) ||
            !int.TryParse(parts[2], out int open) || !int.TryParse(parts[3], out int direction)) return;
        foreach (DoorInteraction door in networkDoors)
            if (door != null && door.NetworkId == doorId)
            {
                door.ApplyServerState(open == 1, direction);
                return;
            }
    }

    void ProcessItemPickupDeniedPacket(string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int itemId)) return;
        if (potionItems == null || potionItems.Length == 0)
            potionItems = FindObjectsOfType<PotionItem>(true);
        foreach (PotionItem item in potionItems)
            if (item != null && item.itemId == itemId) { item.DenyPickup(); return; }
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
            remotePlayer.SetAnimationState(targetRemoteSpeed, targetRemoteIsRunning,
                                           targetRemoteIsCrouching, targetRemoteIsGrounded,
                                           targetRemoteJumpSequence);
        }
    }

    void ApplyRemoteMonsterTransform(int monsterId, Vector3 pos, Quaternion rot, float speed,
                                     bool isWalk, bool isAttack, int attackSequence, string aiState)
    {
        if (monsterSetups == null || monsterSetups.Length == 0)
            monsterSetups = FindObjectsOfType<MonsterNetworkSetup>();

        foreach (MonsterNetworkSetup monster in monsterSetups)
        {
            if (monster != null && monster.monsterId == monsterId)
            {
                if (monster.IsAuthority)
                    return;

                RemoteMonster remoteMonster = monster.GetComponent<RemoteMonster>();
                if (remoteMonster != null)
                {
                    remoteMonster.PresentationState = aiState;
                    remoteMonster.SetState(pos, rot);
                    remoteMonster.SetAnimationState(speed, isWalk, isAttack, attackSequence);
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

    // [수정] 외부(로비/맵생성기)에서 호출할 때 PlayerRole도 함께 전달받도록 수정
    public void SetupNetworkEntities(GameObject p1, GameObject p2, GameObject[] spawnedMonsters, PotionItem[] spawnedItems, PlayerRole p1Role, PlayerRole p2Role)
    {
        player1Object = p1;
        player2Object = p2;
        potionItems = spawnedItems;

        if (spawnedMonsters != null && spawnedMonsters.Length > 0)
        {
            monsterSetups = new MonsterNetworkSetup[spawnedMonsters.Length];
            for (int i = 0; i < spawnedMonsters.Length; i++)
            {
                monsterSetups[i] = spawnedMonsters[i].GetComponent<MonsterNetworkSetup>();
                if (monsterSetups[i] != null)
                    monsterSetups[i].SetupMonster(offlineMode);
            }
        }

        AssignPlayerRolesAndAuthority(p1Role, p2Role);
    }

    // [수정] 매개변수로 역할을 전달받아 동적으로 할당하고 스탯을 초기화
    private void AssignPlayerRolesAndAuthority(PlayerRole role1, PlayerRole role2)
    {
        if (player1Object == null || player2Object == null) return;

        bool isPlayer1Local = offlineMode || (playerId == 1);

        if (isPlayer1Local)
        {
            localPlayerTransform = player1Object.transform;
            remotePlayerTransform = player2Object.transform;

            if (remotePlayerTransform != null)
            {
                targetRemotePosition = remotePlayerTransform.position;
                targetRemoteRotation = remotePlayerTransform.rotation;
            }
        }
        else
        {
            localPlayerTransform = player2Object.transform;
            remotePlayerTransform = player1Object.transform;

            if (remotePlayerTransform != null)
            {
                targetRemotePosition = remotePlayerTransform.position;
                targetRemoteRotation = remotePlayerTransform.rotation;
            }
        }

        localPlayerController = localPlayerTransform.GetComponent<PlayerController>();

        RemotePlayer p1Remote = player1Object.GetComponent<RemotePlayer>();
        if (p1Remote != null) p1Remote.SetupPlayer(isPlayer1Local);

        RemotePlayer p2Remote = player2Object.GetComponent<RemotePlayer>();
        if (p2Remote != null) p2Remote.SetupPlayer(!isPlayer1Local);

        if (offlineMode) player2Object.SetActive(false);

        // [수정] 동적으로 역할 셋업
        PlayerRoleSetup p1RoleSetup = player1Object.GetComponent<PlayerRoleSetup>();
        if (p1RoleSetup != null) p1RoleSetup.Setup(1, playerId, role1);

        PlayerRoleSetup p2RoleSetup = player2Object.GetComponent<PlayerRoleSetup>();
        if (p2RoleSetup != null) p2RoleSetup.Setup(2, playerId, role2);

        if (role2 == PlayerRole.Detector && player2Object.GetComponent<ClueDetector>() == null)
        {
            ClueDetector detector = player2Object.AddComponent<ClueDetector>();
            AudioSource source = player2Object.AddComponent<AudioSource>();
            ClueDetector firstDetector = player1Object.GetComponent<ClueDetector>();
            source.clip = firstDetector != null && firstDetector.beepSound != null
                ? firstDetector.beepSound.clip : null;
            detector.beepSound = source;
            detector.maxDetectionDist = firstDetector != null ? firstDetector.maxDetectionDist : 40f;
        }

        // [추가] 각 캐릭터의 직업(역할)에 따른 스탯 적용 (러너 스탯 등)
        PlayerController p1Controller = player1Object.GetComponent<PlayerController>();
        if (p1Controller != null) p1Controller.InitializeStatsByRole(role1);

        PlayerController p2Controller = player2Object.GetComponent<PlayerController>();
        if (p2Controller != null) p2Controller.InitializeStatsByRole(role2);

        // Prefer the local player's camera. The scene camera otherwise leaves a
        // second AudioListener active after the player prefab is spawned.
        Camera localCamera = localPlayerTransform.GetComponentInChildren<Camera>(true);
        if (localCamera != null)
        {
            localCamera.enabled = true;
            AudioListener localListener = localCamera.GetComponent<AudioListener>();
            if (localListener != null) localListener.enabled = true;
            foreach (Camera camera in FindObjectsOfType<Camera>(true))
            {
                if (camera == localCamera || !camera.CompareTag("MainCamera")) continue;
                camera.enabled = false;
                AudioListener listener = camera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }
        else if (Camera.main != null)
        {
            Camera.main.transform.position = localPlayerTransform.position + new Vector3(0, 3f, -4f);
            Camera.main.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
            Camera.main.transform.SetParent(localPlayerTransform);
        }

        // UI 바인딩
        if (localPlayerTransform != null)
        {
            PlayerController localController = localPlayerTransform.GetComponent<PlayerController>();
            if (localController != null)
            {
                Slider hpSl = GameObject.Find("HPBar")?.GetComponent<Slider>();
                Slider stSl = GameObject.Find("StaminaBar")?.GetComponent<Slider>();
                localController.BindUI(hpSl, stSl);
            }
        }
    }
}
