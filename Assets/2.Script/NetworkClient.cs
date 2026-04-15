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

    [Header("Local Player")]
    public Transform playerTransform;
    public float sendInterval = 0.1f;

    [Header("Remote Player")]
    public Transform remotePlayerTransform;
    public float positionLerpSpeed = 10f;
    public float rotationLerpSpeed = 10f;
    public float remoteTimeout = 2.0f;

    private TcpClient client;
    private NetworkStream stream;
    private float sendTimer = 0f;
    private bool isConnected = false;

    private Vector3 targetRemotePosition;
    private Quaternion targetRemoteRotation;
    private bool hasReceivedRemoteData = false;

    private string receiveBuffer = "";
    private float lastRemotePacketTime = -999f;

    void Start()
    {
        try
        {
            client = new TcpClient();
            client.Connect(serverIP, serverPort);

            stream = client.GetStream();
            isConnected = true;

            if (remotePlayerTransform != null)
            {
                targetRemotePosition = remotePlayerTransform.position;
                targetRemoteRotation = remotePlayerTransform.rotation;
                remotePlayerTransform.gameObject.SetActive(false);
            }

            Debug.Log($"Server connected! Player ID: {playerId}");
        }
        catch (Exception e)
        {
            Debug.LogError("Connect failed: " + e.Message);
        }
    }

    void Update()
    {
        if (!isConnected || stream == null)
            return;

        UpdateSend();
        UpdateReceive();
        UpdateRemoteInterpolation();
        UpdateRemoteVisibility();
    }

    void UpdateSend()
    {
        if (playerTransform == null)
        {
            Debug.LogWarning("Player Transform is not assigned.");
            return;
        }

        sendTimer += Time.deltaTime;

        if (sendTimer >= sendInterval)
        {
            sendTimer = 0f;

            Vector3 pos = playerTransform.position;
            float rotY = playerTransform.eulerAngles.y;

            string message = string.Format(
                CultureInfo.InvariantCulture,
                "MOVE|{0}|{1:F2}|{2:F2}|{3:F2}|{4:F2}\n",
                playerId, pos.x, pos.y, pos.z, rotY);

            byte[] sendData = Encoding.UTF8.GetBytes(message);

            try
            {
                stream.Write(sendData, 0, sendData.Length);
            }
            catch (Exception e)
            {
                Debug.LogError("Send failed: " + e.Message);
                isConnected = false;
            }
        }
    }

    void UpdateReceive()
    {
        try
        {
            if (stream.DataAvailable)
            {
                byte[] recvBuffer = new byte[512];
                int recvLength = stream.Read(recvBuffer, 0, recvBuffer.Length);

                if (recvLength > 0)
                {
                    string chunk = Encoding.UTF8.GetString(recvBuffer, 0, recvLength);
                    receiveBuffer += chunk;
                    ProcessReceiveBuffer();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Receive failed: " + e.Message);
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

            if (string.IsNullOrWhiteSpace(packet))
                continue;

            ApplyRemoteMove(packet);
        }
    }

    void ApplyRemoteMove(string message)
    {
        if (remotePlayerTransform == null)
        {
            Debug.LogWarning("Remote Player Transform is not assigned.");
            return;
        }

        string[] parts = message.Split('|');

        if (parts.Length != 6)
            return;

        if (parts[0] != "MOVE")
            return;

        if (!int.TryParse(parts[1], out int receivedPlayerId))
            return;

        if (receivedPlayerId == playerId)
            return;

        bool parsedX = float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
        bool parsedY = float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
        bool parsedZ = float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
        bool parsedRotY = float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float rotY);

        if (!parsedX || !parsedY || !parsedZ || !parsedRotY)
            return;

        targetRemotePosition = new Vector3(x, y, z);
        targetRemoteRotation = Quaternion.Euler(0f, rotY, 0f);
        hasReceivedRemoteData = true;
        lastRemotePacketTime = Time.time;

        if (!remotePlayerTransform.gameObject.activeSelf)
        {
            remotePlayerTransform.gameObject.SetActive(true);
        }
    }

    void UpdateRemoteInterpolation()
    {
        if (!hasReceivedRemoteData || remotePlayerTransform == null)
            return;

        if (!remotePlayerTransform.gameObject.activeSelf)
            return;

        remotePlayerTransform.position = Vector3.Lerp(
            remotePlayerTransform.position,
            targetRemotePosition,
            Time.deltaTime * positionLerpSpeed);

        remotePlayerTransform.rotation = Quaternion.Lerp(
            remotePlayerTransform.rotation,
            targetRemoteRotation,
            Time.deltaTime * rotationLerpSpeed);
    }

    void UpdateRemoteVisibility()
    {
        if (remotePlayerTransform == null)
            return;

        if (!remotePlayerTransform.gameObject.activeSelf)
            return;

        if (Time.time - lastRemotePacketTime > remoteTimeout)
        {
            remotePlayerTransform.gameObject.SetActive(false);
            hasReceivedRemoteData = false;
        }
    }

    void OnApplicationQuit()
    {
        if (stream != null)
        {
            stream.Close();
            stream = null;
        }

        if (client != null)
        {
            client.Close();
            client = null;
        }
    }
}