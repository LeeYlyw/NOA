using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class NetworkClient : MonoBehaviour
{
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
    public float sendInterval = 0.1f;

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

    void Start()
    {
        SetupPlayersByPlayerId();
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
        ReceivePackets();
        ApplyRemotePlayerTransform();
    }

    void SetupPlayersByPlayerId()
    {
        if (!autoSetupPlayers)
            return;

        if (player1Object == null || player2Object == null)
        {
            Debug.LogWarning("Auto Setup Players가 켜져 있지만 player1Object 또는 player2Object가 비어 있습니다.");
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
        else
        {
            Debug.LogWarning("playerId는 현재 1 또는 2만 처리하도록 되어 있습니다.");
        }
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

        byte[] data = Encoding.UTF8.GetBytes(message);

        try
        {
            stream.Write(data, 0, data.Length);
        }
        catch (Exception e)
        {
            Debug.LogError("전송 실패: " + e.Message);
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
            // MOVE|playerId|x|y|z|rotY|speed|isRunning|isCrouching
            if (parts.Length != 9)
            {
                Debug.LogWarning("MOVE 패킷 형식이 맞지 않음: " + packet);
                return;
            }

            if (!int.TryParse(parts[1], out int receivedPlayerId))
                return;

            // 내가 보낸 내 정보는 무시
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

    void OnApplicationQuit()
    {
        if (stream != null)
            stream.Close();

        if (client != null)
            client.Close();
    }
}