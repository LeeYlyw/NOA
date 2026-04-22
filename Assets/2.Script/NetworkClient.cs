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

    [Header("Sync")]
    public float sendInterval = 0.1f;

    private TcpClient client;
    private NetworkStream stream;
    private bool isConnected = false;
    private float sendTimer = 0f;
    private string receiveBuffer = "";

    private Vector3 targetRemotePosition;
    private Quaternion targetRemoteRotation;
    private bool hasRemoteState = false;

    void Start()
    {
        ConnectToServer();

        if (remotePlayerTransform != null)
        {
            targetRemotePosition = remotePlayerTransform.position;
            targetRemoteRotation = remotePlayerTransform.rotation;
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

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "MOVE|{0}|{1:F2}|{2:F2}|{3:F2}|{4:F2}\n",
            playerId, pos.x, pos.y, pos.z, rotY
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

        if (parts.Length != 6)
            return;

        if (parts[0] != "MOVE")
            return;

        if (!int.TryParse(parts[1], out int receivedPlayerId))
            return;

        if (receivedPlayerId == playerId)
            return;

        bool okX = float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
        bool okY = float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
        bool okZ = float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
        bool okRot = float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float rotY);

        if (!okX || !okY || !okZ || !okRot)
        {
            Debug.LogWarning("패킷 파싱 실패: " + packet);
            return;
        }

        targetRemotePosition = new Vector3(x, y, z);
        targetRemoteRotation = Quaternion.Euler(0f, rotY, 0f);
        hasRemoteState = true;

        Debug.Log("리모트 목표 위치 설정: " + targetRemotePosition);
    }

    void ApplyRemotePlayerTransform()
    {
        if (!hasRemoteState || remotePlayerTransform == null)
            return;

        remotePlayerTransform.position = targetRemotePosition;
        remotePlayerTransform.rotation = targetRemoteRotation;

        Debug.Log("리모트 실제 위치 적용: " + remotePlayerTransform.position);
    }

    void OnApplicationQuit()
    {
        if (stream != null)
            stream.Close();

        if (client != null)
            client.Close();
    }
}