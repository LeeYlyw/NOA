using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class NetworkClient : MonoBehaviour
{
    public static NetworkClient Instance;

    // [추가된 부분] 에디터에서 혼자 테스트할 때 체크하세요.
    [Header("Testing Option")]
    [Tooltip("체크 시 C++ 서버 연결 없이 유니티 에디터에서 바로 싱글 테스트를 수행합니다.")]
    public bool offlineMode = false;
    [Tooltip("오프라인 테스트에서 조작할 직업")]
    public PlayerRole offlineRole = PlayerRole.Detector;
    [Tooltip("0은 무작위, 다른 값은 같은 맵을 반복 테스트합니다.")]
    public int offlineMapSeed;

    [Header("Network")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 7777;
    // Assigned by the server after connecting; never configured per build.
    public int playerId { get; private set; }
    public bool HasAssignedPlayerId => playerId >= 1 && playerId <= 4;
    public int PlayerCount { get; private set; } = 2;
    readonly GameObject[] playerObjects = new GameObject[5];
    readonly PlayerRole[] matchPlayerRoles = new PlayerRole[5];
    public GameObject GetPlayerObject(int id) => id >= 1 && id <= PlayerCount ? playerObjects[id] : null;
    public Transform GetPlayerTransform(int id) => GetPlayerObject(id)?.transform;
    public PlayerRole GetRole(int id) => id >= 1 && id <= PlayerCount ? matchPlayerRoles[id] : PlayerRole.Detector;

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
    private bool isReloading;
    private bool restoringSession;
    private bool receivingSnapshot;
    private bool snapshotApplied;
    private float lastPacketAt;
    private float nextHeartbeatAt;
    private float resumeRetryDeadline;
    private int authorityEpoch = 1;
    private float connectionNoticeUntil;
    private GUIStyle connectionNoticeStyle;
    public int AuthorityPlayerId { get; private set; } = 1;
    public bool IsRestoringSession => restoringSession;
    private string SessionKey => "NOA.Resume." + serverIP + "." + serverPort;
    private string ResumeToken => PlayerPrefs.GetString(SessionKey, "");
    private void ForgetSession() { PlayerPrefs.DeleteKey(SessionKey); PlayerPrefs.Save(); }
    private Coroutine connectionRoutine;

    private float sendTimer = 0f;
    private int nextMimicShotId = 100000;
    private string receiveBuffer = "";
    private PlayerController localPlayerController;

    private readonly Vector3[] targetRemotePositions = new Vector3[5];
    private readonly Quaternion[] targetRemoteRotations = new Quaternion[5];
    private readonly bool[] hasRemoteStates = new bool[5];
    private readonly float[] targetRemoteSpeeds = new float[5];
    private readonly bool[] targetRemoteRunning = new bool[5];
    private readonly bool[] targetRemoteCrouching = new bool[5];
    private readonly bool[] targetRemoteGrounded = new bool[5];
    private readonly int[] targetRemoteJumpSequences = new int[5];

    void Awake()
    {
        Instance = this;
        // The demo map scenes have no serialized clue UI. Keep the server result
        // packets connected to the runtime ending report in those scenes.
        if (ClueManager.instance == null && FindObjectOfType<ClueManager>() == null)
            gameObject.AddComponent<ClueManager>();
        serverIP = PlayerPrefs.GetString("NOA.ServerAddress", serverIP);
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "-serverIP") serverIP = args[++i];
            else if (args[i] == "-serverPort" && int.TryParse(args[i + 1], out int port) &&
                     port > 0 && port <= 65535)
            {
                serverPort = port;
                i++;
            }
        }
        playerId = offlineMode ? 1 : 0;
        if (offlineMode) Role1 = offlineRole;
        matchPlayerRoles[1] = Role1;
        matchPlayerRoles[2] = Role2;
        Application.runInBackground = true;
    }

    public bool IsConnected => isConnected;
    public bool HasRemotePlayerState
    {
        get
        {
            for (int id = 1; id <= PlayerCount; id++)
                if (id != playerId && hasRemoteStates[id]) return true;
            return false;
        }
    }
    public bool GameStarted { get; private set; }
    public int SecondsLeft { get; private set; } = -1;
    private float timeLeftReceivedAt;
    public int DisplaySecondsLeft => SecondsLeft < 0 ? -1 :
        Mathf.Max(0, Mathf.CeilToInt(SecondsLeft - (Time.unscaledTime - timeLeftReceivedAt)));
    public PlayerRole Role1 { get; private set; } = PlayerRole.Healer;
    public PlayerRole Role2 { get; private set; } = PlayerRole.Runner;
    public string ConnectionStatus { get; private set; } = "서버에 연결하는 중...";

    public bool IsConnecting { get; private set; }

    void Start()
    {
        if (!offlineMode)
        {
            ConnectionStatus = "서버 연결 중: " + serverIP + ":" + serverPort;
            if (GetComponent<LobbyManager>() == null) gameObject.AddComponent<LobbyManager>();
            ConnectionStatus = "서버 PC 주소를 입력하고 접속하세요.";
            if (!string.IsNullOrEmpty(ResumeToken))
            {
                resumeRetryDeadline = Time.realtimeSinceStartup + 65f;
                connectionRoutine = StartCoroutine(ConnectAutomatically());
            }
        }
    }

    void Update()
    {
        // 오프라인 모드면 수신/송신 루프 패스
        if (offlineMode) return;

        if (!isConnected || stream == null) return;

        if (GameStarted && Time.realtimeSinceStartup - lastPacketAt > 15f)
        { HandleConnectionLost(); return; }
        if (GameStarted && Time.realtimeSinceStartup >= nextHeartbeatAt)
        {
            nextHeartbeatAt = Time.realtimeSinceStartup + 2f;
            SendMessageToServer("C_PING\n", "Heartbeat failed");
        }
        if (!isConnected || client == null) return;
        try
        {
            if (client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0)
            { HandleConnectionLost(); return; }
        }
        catch (SocketException)
        { HandleConnectionLost(); return; }
        ReceivePackets();
        if (!isConnected || !HasAssignedPlayerId || !GameStarted || restoringSession) return;
        SendLocalPlayerTransform();
        ApplyRemotePlayerTransform();
    }

    public void ConnectToServer(string address)
    {
        if (isConnected || IsConnecting || GameStarted || isReloading) return;
        address = address?.Trim();
        if (string.IsNullOrEmpty(address) || Uri.CheckHostName(address) == UriHostNameType.Unknown ||
            serverPort < 1 || serverPort > 65535)
        {
            ConnectionStatus = "올바른 서버 주소를 입력하세요. 예: 192.168.0.10";
            return;
        }
        serverIP = address;
        resumeRetryDeadline = Time.realtimeSinceStartup + 65f;
        CloseConnection();
        if (LobbyManager.Instance != null) LobbyManager.Instance.ResetState();
        connectionRoutine = StartCoroutine(ConnectAutomatically());
    }

    public void CancelConnection()
    {
        if (!IsConnecting) return;
        if (connectionRoutine != null) StopCoroutine(connectionRoutine);
        connectionRoutine = null;
        CloseConnection();
        ConnectionStatus = "접속을 취소했습니다. 주소를 수정하고 다시 접속할 수 있습니다.";
    }

    private void CloseConnection()
    {
        IsConnecting = false;
        isConnected = false;
        stream?.Close();
        client?.Close();
        stream = null;
        client = null;
        receiveBuffer = "";
        if (!GameStarted)
        {
            playerId = 0;
            restoringSession = receivingSnapshot = snapshotApplied = false;
        }
    }

    System.Collections.IEnumerator ConnectAutomatically()
    {
        IsConnecting = true;
        try
        {
            bool resuming = !string.IsNullOrEmpty(ResumeToken);
            float deadlineForAttempts = Time.realtimeSinceStartup + 60f;
            for (int attempt = 0; resuming ? Time.realtimeSinceStartup < deadlineForAttempts : attempt < 3; attempt++)
            {
                ConnectionStatus = "서버 연결 중: " + serverIP + ":" + serverPort + " (" + (attempt + 1) + "회)";
                client = new TcpClient();
                System.Threading.Tasks.Task task = null;
                try { task = client.ConnectAsync(serverIP, serverPort); }
                catch (Exception) { client.Close(); }
                if (task != null)
                {
                    // Observe errors even when a timed-out or cancelled connection finishes later.
                    _ = task.ContinueWith(t => { var ignored = t.Exception; },
                        System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                    float deadline = Time.realtimeSinceStartup + 2f;
                    while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                    if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && client.Connected)
                    {
                        stream = client.GetStream();
                        isConnected = true;
                        lastPacketAt = Time.realtimeSinceStartup;
                        ConnectionStatus = "서버에 연결됐습니다.";
                        PlayerPrefs.SetString("NOA.ServerAddress", serverIP);
                        PlayerPrefs.Save();
                        yield break;
                    }
                }
                client.Close();
                yield return new WaitForSecondsRealtime(resuming ? 1f : 0.3f);
            }
            CloseConnection();
            ConnectionStatus = "연결 실패: 서버 주소와 실행 상태를 확인한 뒤 다시 접속하세요.";
        }
        finally { IsConnecting = false; connectionRoutine = null; }
    }

    public void SelectRole(int role) => SendMessageToServer("C_ROLE|" + role + "\n", "Role failed");
    public void SetReady(bool value) => SendMessageToServer("C_READY|" + (value ? 1 : 0) + "\n", "Ready failed");
    public void RequestStart() => SendMessageToServer("C_START\n", "Start failed");
    public void RequestRestart()
    {
        if (offlineMode) { ReturnToLobby(); return; }
        SendMessageToServer("C_RESTART|" + playerId + "\n", "Restart request failed");
    }

    public void ReturnToLobby()
    {
        if (isReloading) return;
        ForgetSession();
        isReloading = true;
        StartCoroutine(ReloadLobby());
    }

    private System.Collections.IEnumerator ReloadLobby()
    {
        isConnected = false;
        if (stream != null) stream.Close();
        if (client != null) client.Close();
        Time.timeScale = 1f;
        yield return new WaitForSecondsRealtime(0.7f);
        // A defeat report opens an additive runtime scene; reload the match scene.
        SceneManager.LoadScene(gameObject.scene.name);
    }

    public void SendPuzzlePad(int padId, bool active)
    {
        // The server validates the pad against its most recent MOVE packet.
        // Send this position first so an entry is not rejected due to the
        // regular movement send interval.
        if (active) SendLocalPlayerTransform(true);
        SendMessageToServer("C_PUZZLE_PAD|" + playerId + "|" + padId + "|" +
            (active ? 1 : 0) + "\n", "Puzzle pad failed");
    }

    public void SendPuzzleChoice(int index) =>
        SendMessageToServer("C_PUZZLE_CHOOSE|" + playerId + "|" + index + "\n", "Puzzle choice failed");

    public void SendMimicState(int mimicId, int state)
    {
        if (offlineMode || playerId != AuthorityPlayerId || mimicId <= 0) return;
        SendMessageToServer($"C_MIMIC_STATE|{playerId}|{mimicId}|{state}\n", "Mimic state failed");
    }

    public int SendMimicFire(int mimicId, int targetId, Vector3 start, Vector3 direction,
                             float speed, int damage)
    {
        if (offlineMode || playerId != AuthorityPlayerId || mimicId <= 0 || !isConnected) return 0;
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

    void SendLocalPlayerTransform(bool force = false)
    {
        if (localPlayerTransform == null) return;
        if (!force)
        {
            sendTimer += Time.deltaTime;
            if (sendTimer < sendInterval) return;
        }
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

    public void ReportMapPreparationFailure(string reason)
    {
        ConnectionStatus = "맵 준비 실패: " + DescribeMapFailure(reason);
        if (ClueManager.instance != null)
            ClueManager.instance.ShowConnectionLost("맵 준비 실패");
        if (!offlineMode && isConnected && HasAssignedPlayerId)
            SendMessageToServer("C_NAV_FAIL|" + playerId + "\n", "Map failure report failed");
    }

    static string DescribeMapFailure(string reason)
    {
        switch (reason)
        {
            case "Invalid map configuration": return "맵 설정을 확인하세요.";
            case "Missing room prefab": return "방 프리팹이 누락됐습니다.";
            case "All map layout attempts failed": return "배치 가능한 맵을 만들지 못했습니다. 로비에서 다시 시작하세요.";
            case "Required map entities could not be spawned": return "플레이어·몬스터·단서를 안전한 위치에 배치하지 못했습니다.";
            case "Map or item layout differs between clients": return "두 플레이어의 맵 또는 아이템 배치가 다릅니다. 같은 빌드인지 확인하세요.";
            case "Invalid navigation edges": return "맵의 이동 경로 정보를 확인할 수 없습니다.";
            case "Monster spawn is not walkable": return "몬스터 시작 위치가 이동 가능한 바닥에 없습니다.";
            case "Timed out waiting for matching navigation maps": return "동료의 맵 준비가 제한 시간 안에 끝나지 않았습니다.";
        }
        if (!string.IsNullOrEmpty(reason) && reason.StartsWith("Map generation failed on Player ", StringComparison.Ordinal))
            return "플레이어 " + reason.Substring("Map generation failed on Player ".Length) + "의 맵 생성에 실패했습니다.";
        return "맵 생성 또는 동기화에 실패했습니다. 로비에서 다시 시작하세요.";
    }

    static string DescribeLobbyError(string reason)
    {
        switch (reason)
        {
            case "Lobby full": return "로비가 가득 찼습니다. 4인 빌드라면 서버 실행 파일 버전을 확인하세요.";
            case "Role taken": return "동료가 이미 선택한 직업입니다. 다른 직업을 선택하세요.";
            case "Match in progress. Try again after the session ends.": return "게임이 진행 중입니다. 종료 후 다시 접속하세요.";
            default: return "서버 요청이 거절됐습니다. 직업과 준비 상태를 확인하세요.";
        }
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

        if (CoopShapePuzzle.Instance != null)
            for (int padId = 1; padId <= 2; padId++)
            {
                Vector3 pad = CoopShapePuzzle.Instance.GetAssignedPad(padId);
                string padPacket = string.Format(CultureInfo.InvariantCulture,
                    "C_NAV_PAD|{0}|{1}|{2:F2}|{3:F2}|{4:F2}\n",
                    playerId, padId, pad.x, pad.y, pad.z);
                SendMessageToServer(padPacket, "Navigation pad upload failed");
            }

        if (potionItems != null)
            foreach (PotionItem item in potionItems)
            {
                if (item == null || item.itemData == null || item.itemId <= 0) continue;
                Vector3 position = item.transform.position;
                string itemPacket = string.Format(CultureInfo.InvariantCulture,
                    "C_NAV_ITEM|{0}|{1}|{2}|{3:F2}|{4:F2}|{5:F2}\n",
                    playerId, item.itemId, item.itemData.type,
                    position.x, position.y, position.z);
                SendMessageToServer(itemPacket, "Navigation item upload failed");
            }

        if (PowerRestoration.Instance != null)
            for (int i = 0; i < 6; i++)
            {
                Vector3 position = PowerRestoration.Instance.Position(i);
                SendMessageToServer(string.Format(CultureInfo.InvariantCulture,
                    "C_NAV_ITEM|{0}|{1}|{2}|{3:F2}|{4:F2}|{5:F2}\n", playerId, 240 + i,
                    i < 3 ? "PowerFuse" : "PowerBreaker", position.x, position.y, position.z),
                    "Power quest upload failed");
            }

        const int chunkSize = 1000;
        for (int offset = 0, sequence = 0; offset < edgeMask.Length; offset += chunkSize, sequence++)
            SendMessageToServer("C_NAV_CHUNK|" + playerId + "|" + sequence + "|" +
                                edgeMask.Substring(offset, Mathf.Min(chunkSize, edgeMask.Length - offset)) +
                                "\n", "Navigation map chunk failed");
        SendMessageToServer("C_NAV_END|" + playerId + "\n", "Navigation map end failed");
        if (restoringSession) StartCoroutine(RequestSnapshot());
    }

    public void SendPowerRequest(int objectId)
    {
        if (!offlineMode && !restoringSession && isConnected && HasAssignedPlayerId)
            SendMessageToServer($"C_POWER|{playerId}|{objectId}\n", "Power interaction failed");
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
        if (restoringSession || !isConnected || !HasAssignedPlayerId || stream == null) return false;
        SendMessageToServer(message, "아이템 획득 요청 전송 실패");
        return isConnected;
    }

    public bool SendItemUseRequest(int itemId, ItemData.ItemType itemType,
        int targetPlayerId, Vector3 destination)
    {
        if (offlineMode || restoringSession || !isConnected || !HasAssignedPlayerId || stream == null) return false;
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "C_ITEM_USE|{0}|{1}|{2}|{3}|{4:F2}|{5:F2}|{6:F2}\n",
            playerId, itemId, itemType, targetPlayerId,
            destination.x, destination.y, destination.z
        );
        SendMessageToServer(message, "Item use request failed");
        return isConnected;
    }

    // [추가] 힐러 스킬: 서버로 특정 플레이어의 체력 회복을 요청합니다.
    public bool SendPlayerHealRequest()
    {
        if (offlineMode || restoringSession || !isConnected || !HasAssignedPlayerId || stream == null)
            return false;
        SendMessageToServer("C_PLAYER_HEAL|" + playerId + "\n", "Heal request failed");
        return isConnected;
    }

    void ApplyOfflineItemPickup(int itemId)
    {
        if (potionItems == null || potionItems.Length == 0)
            potionItems = FindObjectsOfType<PotionItem>(true);

        foreach (PotionItem item in potionItems)
        {
            if (item != null && item.itemId == itemId)
            {
                if (!item.gameObject.activeInHierarchy) return;
                item.ApplyServerPickup(1);
                if (item.itemData != null && item.itemData.type == ItemData.ItemType.Clue && ClueManager.instance != null)
                {
                    ClueManager.instance.RecordClue(itemId);
                    ClueManager.instance.AddClue();
                    ClueManager.instance.ShowCluePickupNotice(itemId, 1, ClueManager.instance.clueCount);
                }
                return;
            }
        }
    }

    void SendMessageToServer(string message, string errorMessage)
    {
        if (!isConnected || !HasAssignedPlayerId || stream == null) return;
        if (restoringSession && !message.StartsWith("C_NAV_", StringComparison.Ordinal) &&
            !message.StartsWith("C_RESUME_", StringComparison.Ordinal) && message != "C_PING\n") return;
        byte[] data = Encoding.UTF8.GetBytes(message);
        try
        {
            stream.Write(data, 0, data.Length);
        }
        catch (Exception e)
        {
            Debug.LogError(errorMessage + ": " + e.Message);
            HandleConnectionLost();
        }
    }

    void HandleConnectionLost()
    {
        CloseConnection();
        if (!ConnectionStatus.StartsWith("로비가 가득", StringComparison.Ordinal) &&
            !ConnectionStatus.StartsWith("게임이 진행 중", StringComparison.Ordinal) &&
            !ConnectionStatus.StartsWith("서버 버전", StringComparison.Ordinal))
            ConnectionStatus = "서버 연결이 끊겼습니다.";
        if (GameStarted && !isReloading && !string.IsNullOrEmpty(ResumeToken))
        {
            isReloading = true;
            ConnectionStatus = "연결 복구 중입니다. 서버가 60초 동안 자리를 보관합니다.";
            StartCoroutine(ReloadLobby());
        }
        else if (!GameStarted && !isReloading && !string.IsNullOrEmpty(ResumeToken) &&
                 Time.realtimeSinceStartup < resumeRetryDeadline)
            connectionRoutine = StartCoroutine(RetryResume());
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
                lastPacketAt = Time.realtimeSinceStartup;
                receiveBuffer += Encoding.UTF8.GetString(buffer, 0, length);
            }
            ProcessReceiveBuffer();
        }
        catch (Exception e)
        {
            Debug.LogError("[Network] 수신 실패: " + e.Message);
            HandleConnectionLost();
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

        if (restoringSession && !receivingSnapshot && !snapshotApplied &&
            parts[0] != "START" && parts[0] != "S_SNAPSHOT_BEGIN" &&
            parts[0] != "S_PONG" && parts[0] != "S_NAV_ERROR" &&
            parts[0] != "S_SESSION_END") return;

        switch (parts[0])
        {
            case "S_POWER":
                if (PowerRestoration.Instance != null) PowerRestoration.Instance.ApplyState(parts);
                break;
            case "S_SESSION":
                if (parts.Length == 2) { PlayerPrefs.SetString(SessionKey, parts[1]); PlayerPrefs.Save(); }
                break;
            case "S_RESUME_REQUIRED":
                if (string.IsNullOrEmpty(ResumeToken))
                {
                    CloseConnection();
                    ConnectionStatus = "게임이 진행 중입니다. 기존 참가자만 재접속할 수 있습니다.";
                }
                else
                {
                    byte[] request = Encoding.UTF8.GetBytes("C_RESUME|" + ResumeToken + "\n");
                    stream.Write(request, 0, request.Length);
                }
                break;
            case "S_RESUME_RETRY":
                CloseConnection();
                if (Time.realtimeSinceStartup < resumeRetryDeadline)
                    connectionRoutine = StartCoroutine(RetryResume());
                else ConnectionStatus = "기존 연결 종료를 확인하지 못했습니다. 잠시 뒤 다시 접속하세요.";
                break;
            case "S_RESUME_REJECTED":
                ForgetSession();
                CloseConnection();
                ConnectionStatus = "재접속 유예가 끝났거나 이미 접속된 참가자입니다.";
                break;
            case "S_RESUME_ACCEPTED":
                restoringSession = true;
                ConnectionStatus = "맵과 게임 상태를 복원하는 중입니다.";
                break;
            case "S_SNAPSHOT_BEGIN":
                receivingSnapshot = true;
                break;
            case "S_SNAPSHOT_PLAYER":
                RestorePlayer(parts);
                break;
            case "S_SNAPSHOT_ITEM":
                ProcessItemPickupPacket(parts);
                break;
            case "S_SNAPSHOT_END":
                receivingSnapshot = false;
                snapshotApplied = true;
                SendMessageToServer("C_RESUME_ACK\n", "Resume confirmation failed");
                break;
            case "S_RESUME_COMPLETE":
                restoringSession = false;
                ConnectionStatus = "기존 경기에 재접속했습니다.";
                connectionNoticeUntil = Time.unscaledTime + 5f;
                SetLocalInputEnabled(true);
                break;
            case "S_AUTHORITY":
                if (parts.Length == 3 && int.TryParse(parts[1], out int authority) &&
                    int.TryParse(parts[2], out int epoch))
                {
                    AuthorityPlayerId = authority;
                    if (authorityEpoch != epoch)
                    {
                        authorityEpoch = epoch;
                        nextMimicShotId = epoch * 100000;
                        foreach (MimicLaptop mimic in FindObjectsByType<MimicLaptop>(FindObjectsSortMode.None))
                            mimic.ResetNetworkAuthority();
                    }
                }
                break;
            case "S_CONNECTION":
                if (parts.Length == 3 && int.TryParse(parts[1], out int connectionId))
                {
                    GameObject peer = GetPlayerObject(connectionId);
                    if (peer != null && connectionId != playerId) peer.SetActive(parts[2] == "1");
                    connectionNoticeUntil = Time.unscaledTime + 5f;
                    ConnectionStatus = "플레이어 " + connectionId + (parts[2] == "1" ? " 복귀" : " 연결 끊김 · 재접속 대기");
                }
                break;
            case "S_PONG":
                break;
            case "ID":
                if (!GameStarted && parts.Length == 2 &&
                    int.TryParse(parts[1], out int assignedPlayerId) && assignedPlayerId >= 1 && assignedPlayerId <= 4)
                {
                    playerId = assignedPlayerId;
                    ConnectionStatus = "서버 연결 완료 · 플레이어 " + playerId;
                    Debug.Log("[Network] Server assigned Player " + playerId);
                }
                break;
            case "START":
                // 서버로부터 "START|시드번호" 를 받으면 맵 생성기에 전달
                if (parts.Length >= 2 && int.TryParse(parts[1], out int seed))
                {
                    MapGenerator mg = FindObjectOfType<MapGenerator>();
                    if (HasAssignedPlayerId && !GameStarted && mg != null && parts.Length >= 5 &&
                        int.TryParse(parts[2], out int participantCount) && participantCount >= 2 && participantCount <= 4 &&
                        parts.Length == participantCount + 3)
                    {
                        for (int id = 1; id <= participantCount; id++)
                            if (!int.TryParse(parts[id + 2], out int role) || role < 0 || role > 3)
                                return;
                            else matchPlayerRoles[id] = (PlayerRole)role;
                        PlayerCount = participantCount;
                        Role1 = matchPlayerRoles[1]; Role2 = matchPlayerRoles[2];
                        GameStarted = true; mg.GenerateMap(seed);
                        lastPacketAt = Time.realtimeSinceStartup;
                        if (restoringSession) SetLocalInputEnabled(false);
                    }
                }
                break;
            case "LOBBY":
                ForgetSession();
                if (LobbyManager.Instance != null && !LobbyManager.Instance.ApplyState(parts))
                    ConnectionStatus = "서버 버전이 맞지 않습니다. 최신 Release 서버로 다시 빌드하세요.";
                break;
            case "ERROR":
                ConnectionStatus = parts.Length > 1 ? DescribeLobbyError(parts[1]) : "서버 오류가 발생했습니다.";
                break;
            case "S_NAV_READY":
                Debug.Log("[Network] Server navigation map confirmed by both clients; server AI active.");
                break;
            case "S_TIME_LEFT":
                if (parts.Length >= 2 && int.TryParse(parts[1], out int secondsLeft) &&
                    secondsLeft >= 0 && secondsLeft <= 600)
                {
                    SecondsLeft = secondsLeft;
                    timeLeftReceivedAt = Time.unscaledTime;
                }
                break;
            case "S_SESSION_END":
                ForgetSession();
                ConnectionStatus = "경기가 종료됐습니다. 로비로 돌아가 다시 시작할 수 있습니다.";
                break;
            case "S_NAV_ERROR":
                ConnectionStatus = "맵 준비 실패: " + DescribeMapFailure(parts.Length > 1 ? parts[1] : "");
                Debug.LogError("[Network] " + ConnectionStatus);
                if (ClueManager.instance != null)
                    ClueManager.instance.ShowConnectionLost("맵 준비 실패");
                break;
            case "S_PEER_LEFT":
                ConnectionStatus = "동료의 연결이 끊겼습니다.";
                if (ClueManager.instance != null)
                    ClueManager.instance.ShowConnectionLost("동료 연결 종료");
                break;
            case "S_PUZZLE_PADS":
                if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.SetPads(parts);
                break;
            case "S_PUZZLE_RESET":
                if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.ResetProgress();
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
            case "S_PLAYER_HEAL_RESULT": ProcessPlayerHealResultPacket(parts); break;
            case "S_ITEM_PICKUP": ProcessItemPickupPacket(parts); break;
            case "S_ITEM_USED": ProcessItemUsedPacket(parts); break;
            case "S_ITEM_USE_DENIED": ProcessItemUseDeniedPacket(parts); break;
            case "S_DOOR_STATE": ProcessDoorStatePacket(parts); break;
            case "S_ITEM_PICKUP_DENIED": ProcessItemPickupDeniedPacket(parts); break;
            case "S_CLUE_COUNT": ProcessClueCountPacket(parts); break;
            case "S_PLAYER_REVIVE": ProcessPlayerRevivePacket(parts); break;
            case "S_PLAYER_STEALTH": ProcessPlayerStealthPacket(parts); break;
            case "S_GAME_CLEAR":
                if (ClueManager.instance != null) ClueManager.instance.ShowEnding();
                break;
            case "S_GAME_OVER":
                if (ClueManager.instance != null)
                    ClueManager.instance.ShowDefeat(parts.Length > 1 ? parts[1] : "ALL_DOWN");
                break;
            case "S_RESTART":
                ReturnToLobby();
                break;
            case "COUNT":
                if (parts.Length >= 2 && int.TryParse(parts[1], out int count))
                    if (LobbyManager.Instance != null) LobbyManager.Instance.SetPlayerCount(count);
                break;
        }
    }

    System.Collections.IEnumerator RetryResume()
    {
        IsConnecting = true;
        yield return new WaitForSecondsRealtime(1f);
        connectionRoutine = StartCoroutine(ConnectAutomatically());
    }

    void OnGUI()
    {
        if (!GameStarted || (!restoringSession && Time.unscaledTime >= connectionNoticeUntil)) return;
        if (connectionNoticeStyle == null)
            connectionNoticeStyle = new GUIStyle(GUI.skin.box) {
                font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 16),
                fontSize = 16, wordWrap = true
            };
        GUI.Box(new Rect(20, 70, Mathf.Min(480, Screen.width - 40), 56), ConnectionStatus, connectionNoticeStyle);
    }

    System.Collections.IEnumerator RequestSnapshot()
    {
        yield return null;
        SendMessageToServer("C_RESUME_READY\n", "State restore failed");
    }

    void SetLocalInputEnabled(bool enabled)
    {
        if (localPlayerController != null) localPlayerController.enabled = enabled;
    }

    void RestorePlayer(string[] parts)
    {
        if (parts.Length != 10 || !int.TryParse(parts[1], out int id)) return;
        GameObject target = GetPlayerObject(id);
        if (target == null) return;
        float x = float.Parse(parts[2], CultureInfo.InvariantCulture);
        float y = float.Parse(parts[3], CultureInfo.InvariantCulture);
        float z = float.Parse(parts[4], CultureInfo.InvariantCulture);
        float rotation = float.Parse(parts[5], CultureInfo.InvariantCulture);
        CharacterController character = target.GetComponent<CharacterController>();
        if (character != null) character.enabled = false;
        if (parts[9] == "1")
            target.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0, rotation, 0));
        if (character != null) character.enabled = true;
        targetRemotePositions[id] = target.transform.position;
        targetRemoteRotations[id] = target.transform.rotation;
        hasRemoteStates[id] = id != playerId;
        PlayerController controller = target.GetComponent<PlayerController>();
        if (controller != null) controller.SetHp(int.Parse(parts[6]));
        float stealthSeconds = float.Parse(parts[7], CultureInfo.InvariantCulture);
        PlayerStealth stealth = target.GetComponent<PlayerStealth>();
        if (stealth != null && stealthSeconds > 0f)
        {
            float originalDuration = stealth.stealthDuration;
            bool localOnly = stealth.onlyLocalPlayerCanUse;
            stealth.onlyLocalPlayerCanUse = false;
            stealth.stealthDuration = stealthSeconds;
            stealth.ActivateStealth();
            stealth.stealthDuration = originalDuration;
            stealth.onlyLocalPlayerCanUse = localOnly;
        }
        HealerSkill healer = target.GetComponent<HealerSkill>();
        if (healer != null) healer.RestoreCooldown(float.Parse(parts[8], CultureInfo.InvariantCulture));
    }

    void ProcessMovePacket(string[] parts)
    {
        if (parts.Length < 11) return;
        if (!int.TryParse(parts[1], out int receivedPlayerId) ||
            receivedPlayerId < 1 || receivedPlayerId > PlayerCount || receivedPlayerId == playerId) return;

        if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
            float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float rotY))
        {
            targetRemotePositions[receivedPlayerId] = new Vector3(x, y, z);
            targetRemoteRotations[receivedPlayerId] = Quaternion.Euler(0f, rotY, 0f);
            float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture,
                out targetRemoteSpeeds[receivedPlayerId]);
            targetRemoteRunning[receivedPlayerId] = parts[7] == "1";
            targetRemoteCrouching[receivedPlayerId] = parts[8] == "1";
            targetRemoteGrounded[receivedPlayerId] = parts[9] == "1";
            if (!int.TryParse(parts[10], out targetRemoteJumpSequences[receivedPlayerId]) ||
                targetRemoteJumpSequences[receivedPlayerId] < 0) return;
            hasRemoteStates[receivedPlayerId] = true;
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
        if (parts.Length < 6 || playerId != AuthorityPlayerId) return;
        if (!int.TryParse(parts[1], out int sourcePlayerId)) return;
        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
            !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float amount))
            return;

        GameObject source = GetPlayerObject(sourcePlayerId);
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
            GameObject targetObject = GetPlayerObject(targetPlayerId);
            if (targetObject != null)
            {
                PlayerController controller = targetObject.GetComponent<PlayerController>();
                if (controller != null) controller.SetHp(hp);
            }
        }
    }

    void ProcessPlayerHealResultPacket(string[] parts)
    {
        if (parts.Length < 3 || !int.TryParse(parts[1], out int healerId) ||
            healerId != playerId || !int.TryParse(parts[2], out int accepted)) return;
        GameObject healer = GetPlayerObject(playerId);
        HealerSkill skill = healer != null ? healer.GetComponent<HealerSkill>() : null;
        if (skill != null) skill.OnServerHealResult(accepted == 1);
    }

    void ApplyPlayerDamage(int targetPlayerId, int damage)
    {
        GameObject targetObject = GetPlayerObject(targetPlayerId);
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
            if (parts.Length >= 4 && parts[3] == "Clue" && ClueManager.instance != null)
            {
                ClueManager.instance.RecordClue(itemId);
                if (parts.Length >= 5 && int.TryParse(parts[4], out int sharedClueCount))
                    ClueManager.instance.ShowCluePickupNotice(itemId, pickedPlayerId, sharedClueCount);
            }
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
        if ((parts.Length != 4 && parts.Length != 5) || !int.TryParse(parts[1], out int doorId) ||
            !int.TryParse(parts[2], out int open) || !int.TryParse(parts[3], out int direction)) return;
        float speed = -1f;
        if (parts.Length == 5 &&
            (!float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out speed) ||
             speed <= 0f)) return;
        foreach (DoorInteraction door in networkDoors)
            if (door != null && door.NetworkId == doorId)
            {
                door.ApplyServerState(open == 1, direction, speed);
                return;
            }
    }

    void ProcessItemUsedPacket(string[] parts)
    {
        if (parts.Length != 8 || !int.TryParse(parts[1], out int ownerId) ||
            !int.TryParse(parts[2], out int itemId) ||
            !int.TryParse(parts[4], out int targetId) ||
            !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return;

        GameObject owner = GetPlayerObject(ownerId);
        if (owner == null) return;
        if (parts[3] == "Teleport")
        {
            Vector3 destination = new Vector3(x, y, z);
            if (ItemEffectManager.Instance != null)
                ItemEffectManager.Instance.PlayTeleportEffect(owner.transform.position);
            CharacterController controller = owner.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            owner.transform.position = destination;
            if (controller != null) controller.enabled = true;
            if (ownerId != playerId) targetRemotePositions[ownerId] = destination;
            if (ItemEffectManager.Instance != null)
                ItemEffectManager.Instance.PlayTeleportEffect(destination);
        }
        else if (parts[3] == "Heal")
        {
            if (ItemEffectManager.Instance != null)
                ItemEffectManager.Instance.PlayHealEffect(owner.transform.position);
        }
        else if (parts[3] == "Stealth")
        {
            PlayerStealth stealth = owner.GetComponent<PlayerStealth>();
            if (stealth != null)
            {
                stealth.onlyLocalPlayerCanUse = false;
                stealth.ActivateStealth();
            }
            if (ItemEffectManager.Instance != null)
                ItemEffectManager.Instance.PlayStealthEffect(owner.transform.position);
        }
        else if (parts[3] == "Resurrection")
        {
            GameObject target = GetPlayerObject(targetId);
            if (target != null && ItemEffectManager.Instance != null)
                ItemEffectManager.Instance.PlayResurrectionEffect(target.transform.position);
        }
        if (ownerId == playerId)
        {
            InventoryManager inventory = FindObjectOfType<InventoryManager>();
            if (inventory != null) inventory.ApplyItemUse(itemId, true);
        }
    }

    void ProcessItemUseDeniedPacket(string[] parts)
    {
        if (parts.Length != 2 || !int.TryParse(parts[1], out int itemId)) return;
        InventoryManager inventory = FindObjectOfType<InventoryManager>();
        if (inventory != null) inventory.ApplyItemUse(itemId, false);
        Debug.LogWarning("[Item] Server rejected item use " + itemId);
    }

    void ProcessPlayerStealthPacket(string[] parts)
    {
        if (parts.Length != 3 || !int.TryParse(parts[1], out int targetId) || parts[2] != "0") return;
        GameObject target = GetPlayerObject(targetId);
        PlayerStealth stealth = target != null ? target.GetComponent<PlayerStealth>() : null;
        if (stealth != null) stealth.EndStealth();
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
            GameObject targetObj = GetPlayerObject(targetPlayerId);
            if (targetObj != null)
            {
                PlayerController controller = targetObj.GetComponent<PlayerController>();
                if (controller != null) controller.Revive();
            }
        }
    }

    void ApplyRemotePlayerTransform()
    {
        for (int id = 1; id <= PlayerCount; id++)
        {
            if (id == playerId || !hasRemoteStates[id] || playerObjects[id] == null) continue;
            RemotePlayer remotePlayer = playerObjects[id].GetComponent<RemotePlayer>();
            if (remotePlayer == null) continue;
            remotePlayer.SetState(targetRemotePositions[id], targetRemoteRotations[id]);
            remotePlayer.SetAnimationState(targetRemoteSpeeds[id], targetRemoteRunning[id],
                targetRemoteCrouching[id], targetRemoteGrounded[id], targetRemoteJumpSequences[id]);
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

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (stream != null) stream.Close();
        if (client != null) client.Close();
    }

    public void SetupNetworkEntities(GameObject[] players, GameObject[] spawnedMonsters, PotionItem[] spawnedItems)
    {
        for (int id = 1; id <= PlayerCount; id++) playerObjects[id] = players[id - 1];
        player1Object = playerObjects[1];
        player2Object = playerObjects[2];
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

        AssignPlayerRolesAndAuthority();
        if (offlineMode) GameStarted = localPlayerTransform != null;
    }

    private void AssignPlayerRolesAndAuthority()
    {
        if (playerObjects[playerId] == null) return;
        localPlayerTransform = playerObjects[playerId].transform;
        int firstOther = playerId == 1 ? 2 : 1;
        remotePlayerTransform = playerObjects[firstOther]?.transform;
        for (int id = 1; id <= PlayerCount; id++)
        {
            if (id == playerId || playerObjects[id] == null) continue;
            targetRemotePositions[id] = playerObjects[id].transform.position;
            targetRemoteRotations[id] = playerObjects[id].transform.rotation;
        }

        localPlayerController = localPlayerTransform.GetComponent<PlayerController>();

        // Hide only the local first-person body's pixels; keep its shadows and
        // the remote character's full mesh visible to the other client.
        foreach (SkinnedMeshRenderer body in localPlayerTransform.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

        for (int id = 1; id <= PlayerCount; id++)
        {
            RemotePlayer remote = playerObjects[id].GetComponent<RemotePlayer>();
            if (remote != null) remote.SetupPlayer(id == playerId);
            if (offlineMode && id != playerId) playerObjects[id].SetActive(false);
        }

        // [수정] 동적으로 역할 셋업
        MapGenerator map = FindObjectOfType<MapGenerator>();
        ClueDetector detectorTemplate = map != null && map.player1Prefab != null
            ? map.player1Prefab.GetComponent<ClueDetector>() : null;
        for (int id = 1; id <= PlayerCount; id++)
        {
            GameObject player = playerObjects[id];
            PlayerRoleSetup setup = player.GetComponent<PlayerRoleSetup>();
            if (setup == null) setup = player.AddComponent<PlayerRoleSetup>();
            setup.Setup(id, playerId, GetRole(id));
            EnsureRoleComponents(player, GetRole(id), detectorTemplate);
            PlayerController controller = player.GetComponent<PlayerController>();
            if (controller != null) controller.InitializeStatsByRole(GetRole(id));
        }

        // Prefer the local player's camera. The scene camera otherwise leaves a
        // second AudioListener active after the player prefab is spawned.
        Camera localCamera = localPlayerTransform.GetComponentInChildren<Camera>(true);
        if (localCamera != null)
        {
            // Role 4's prefab excludes the Player layer to hide its own mesh.
            // Restore that layer here so this camera can still see its teammate;
            // the local mesh is hidden separately with ShadowsOnly above.
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) localCamera.cullingMask |= 1 << playerLayer;
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
                if (hpSl != null && stSl != null)
                {
                    VitalsHUDStyle style = localController.GetComponent<VitalsHUDStyle>();
                    if (style == null) style = localController.gameObject.AddComponent<VitalsHUDStyle>();
                    style.Bind(hpSl, stSl);
                }
            }
        }
    }

    static void EnsureRoleComponents(GameObject player, PlayerRole role, ClueDetector detectorTemplate)
    {
        if (player == null) return;
        if (role == PlayerRole.Healer && player.GetComponent<HealerSkill>() == null)
            player.AddComponent<HealerSkill>();
        if (role != PlayerRole.Detector || player.GetComponent<ClueDetector>() != null) return;

        ClueDetector detector = player.AddComponent<ClueDetector>();
        AudioSource source = player.AddComponent<AudioSource>();
        source.clip = detectorTemplate != null && detectorTemplate.beepSound != null
            ? detectorTemplate.beepSound.clip : null;
        detector.beepSound = source;
        detector.maxDetectionDist = detectorTemplate != null ? detectorTemplate.maxDetectionDist : 40f;
    }
}
