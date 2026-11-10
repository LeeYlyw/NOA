using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Exercises the real Unity connection, scene reload, map rebuild and snapshot handlers.
// Run in batch mode with -executeMethod ReconnectSmoke.Run -serverPort 17777.
[InitializeOnLoad]
public static class ReconnectSmoke
{
    const string PendingKey = "NOA.ReconnectSmoke.Pending";
    const string TokenKey = "NOA.Resume.127.0.0.1.17777";
    static TcpListener listener;
    static Task transport;
    static readonly ManualResetEventSlim snapshotReady = new ManualResetEventSlim();
    static string snapshot;
    static bool connected;
    static bool snapshotPrepared;
    static double deadline;
    static Vector3 expectedPosition;
    static int expectedItem;

    static ReconnectSmoke() { EditorApplication.update += Tick; }

    public static void Run()
    {
        SessionState.SetBool(PendingKey + ".HadAddress", PlayerPrefs.HasKey("NOA.ServerAddress"));
        SessionState.SetString(PendingKey + ".Address", PlayerPrefs.GetString("NOA.ServerAddress", ""));
        SessionState.SetString(PendingKey + ".Token", PlayerPrefs.GetString(TokenKey, ""));
        PlayerPrefs.DeleteKey(TokenKey);
        PlayerPrefs.Save();
        EditorSceneManager.OpenScene("Assets/1.Scenes/MapGeneration_Test.unity");
        NetworkClient network = UnityEngine.Object.FindFirstObjectByType<NetworkClient>();
        network.offlineMode = false;
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    static void Serve()
    {
        using (TcpClient first = listener.AcceptTcpClient())
        using (var reader = new StreamReader(first.GetStream()))
        using (var writer = new StreamWriter(first.GetStream()) { AutoFlush = true, NewLine = "\n" })
        {
            writer.WriteLine("ID|2");
            writer.WriteLine("S_SESSION|unity-reconnect-smoke");
            writer.WriteLine("START|522364|2|0|2");
            string line;
            do { line = reader.ReadLine(); } while (line != null && !line.StartsWith("C_NAV_END|"));
            if (line == null) throw new Exception("Initial map upload failed");
        }
        // Drop the first reconnect before authentication and require another attempt.
        using (TcpClient interrupted = listener.AcceptTcpClient()) { }
        using (TcpClient restored = listener.AcceptTcpClient())
        using (var reader = new StreamReader(restored.GetStream()))
        using (var writer = new StreamWriter(restored.GetStream()) { AutoFlush = true, NewLine = "\n" })
        {
            writer.WriteLine("S_RESUME_REQUIRED");
            if (reader.ReadLine() != "C_RESUME|unity-reconnect-smoke")
                throw new Exception("Client did not resume its saved session");
            writer.WriteLine("ID|2");
            writer.WriteLine("S_RESUME_ACCEPTED");
            writer.WriteLine("START|522364|2|0|2");
            string line;
            do { line = reader.ReadLine(); } while (line != null && line != "C_RESUME_READY");
            if (line == null || !snapshotReady.Wait(TimeSpan.FromSeconds(30)))
                throw new Exception("Client did not finish rebuilding its map");
            writer.Write(snapshot);
            do { line = reader.ReadLine(); } while (line != null && line != "C_RESUME_ACK");
            if (line == null) throw new Exception("Client did not acknowledge its snapshot");
            writer.WriteLine("S_RESUME_COMPLETE");
            while ((line = reader.ReadLine()) != null)
                if (line == "C_PING") writer.WriteLine("S_PONG");
        }
    }

    static void Tick()
    {
        if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying) return;
        try
        {
            NetworkClient network = NetworkClient.Instance;
            if (network == null) return;
            if (!connected)
            {
                connected = true;
                deadline = EditorApplication.timeSinceStartup + 120;
                listener = new TcpListener(IPAddress.Loopback, 17777);
                listener.Start();
                transport = Task.Run(Serve);
                network.serverPort = 17777;
                network.ConnectToServer("127.0.0.1");
            }
            if (transport.IsFaulted) throw transport.Exception;
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Reconnect smoke timed out");
            if (network.IsRestoringSession && network.localPlayerTransform != null && !snapshotPrepared)
            {
                PotionItem item = UnityEngine.Object.FindObjectsByType<PotionItem>(FindObjectsSortMode.None)
                    .First(candidate => candidate.itemId > 0 && candidate.itemData != null &&
                        candidate.itemData.type == ItemData.ItemType.Heal);
                expectedItem = item.itemId;
                expectedPosition = network.localPlayerTransform.position;
                snapshot = string.Format(CultureInfo.InvariantCulture,
                    "S_SNAPSHOT_BEGIN\nS_AUTHORITY|2|2\n" +
                    "S_SNAPSHOT_PLAYER|2|{0:R}|{1:R}|{2:R}|90|42|0|7|1\n" +
                    "S_CONNECTION|1|0\nS_SNAPSHOT_ITEM|{3}|2|Heal\n" +
                    "S_CLUE_COUNT|2|3\nS_TIME_LEFT|450\nS_NAV_READY\nS_SNAPSHOT_END\n",
                    expectedPosition.x, expectedPosition.y, expectedPosition.z, expectedItem);
                snapshotPrepared = true;
                snapshotReady.Set();
            }
            if (!snapshotPrepared || network.IsRestoringSession || !network.IsConnected) return;
            PlayerController player = network.GetPlayerObject(2).GetComponent<PlayerController>();
            if (player.CurrentHp != 42) throw new Exception("HP was not restored");
            if (Vector3.Distance(expectedPosition, player.transform.position) > 0.5f)
                throw new Exception("Position was not restored");
            if (network.GetPlayerObject(1).activeSelf) throw new Exception("Disconnected peer remains visible");
            if (network.AuthorityPlayerId != 2 || network.SecondsLeft != 450)
                throw new Exception("Authority or timer was not restored");
            InventoryManager inventory = UnityEngine.Object.FindFirstObjectByType<InventoryManager>();
            if (!inventory.slots.Any(slot => slot != null && slot.ItemId == expectedItem))
                throw new Exception("Inventory was not restored");
            if (player.GetComponent<HealerSkill>().RemainingCooldown < 6f)
                throw new Exception("Healer cooldown was not restored");
            Finish(0, "PASS automatic reconnect, scene/map rebuild, token, position, HP, inventory, peer visibility, authority, timer, healer cooldown");
        }
        catch (Exception error) { Finish(1, error.ToString()); }
    }

    static void Finish(int code, string result)
    {
        SessionState.SetBool(PendingKey, false);
        if (SessionState.GetBool(PendingKey + ".HadAddress", false))
            PlayerPrefs.SetString("NOA.ServerAddress", SessionState.GetString(PendingKey + ".Address", ""));
        else PlayerPrefs.DeleteKey("NOA.ServerAddress");
        string token = SessionState.GetString(PendingKey + ".Token", "");
        if (token.Length == 0) PlayerPrefs.DeleteKey(TokenKey);
        else PlayerPrefs.SetString(TokenKey, token);
        PlayerPrefs.Save();
        listener?.Stop();
        File.WriteAllText("unity-reconnect-smoke.log", result);
        Debug.Log(result);
        EditorApplication.Exit(code);
    }
}
