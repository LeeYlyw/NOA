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

    private TcpClient client;
    private NetworkStream stream;
    private float sendTimer = 0f;
    private bool isConnected = false;

    void Start()
    {
        try
        {
            client = new TcpClient();
            client.Connect(serverIP, serverPort);

            stream = client.GetStream();
            isConnected = true;

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
    }

    void UpdateSend()
    {
        if (playerTransform == null)
            return;

        sendTimer += Time.deltaTime;

        if (sendTimer >= sendInterval)
        {
            sendTimer = 0f;

            Vector3 pos = playerTransform.position;
            float rotY = playerTransform.eulerAngles.y;

            string message = string.Format(
                CultureInfo.InvariantCulture,
                "MOVE|{0}|{1:F2}|{2:F2}|{3:F2}|{4:F2}",
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
                    string message = Encoding.UTF8.GetString(recvBuffer, 0, recvLength);
                    ApplyRemoteMove(message);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Receive failed: " + e.Message);
            isConnected = false;
        }
    }

    void ApplyRemoteMove(string message)
    {
        if (remotePlayerTransform == null)
            return;

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

        remotePlayerTransform.position = new Vector3(x, y, z);
        remotePlayerTransform.rotation = Quaternion.Euler(0f, rotY, 0f);
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