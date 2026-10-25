using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The lobby overlays the configured map scene; nothing spawns until START arrives.
public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;
    public TMP_Text statusText;
    public Button startButton;
    public string gameSceneName = "MapGeneration_Test";
    readonly bool[] connected = new bool[3];
    readonly bool[] ready = new bool[3];
    readonly int[] roles = { -1, -1, -1 };
    int host;
    static readonly string[] names = { "Detector", "Explorer", "Healer", "Runner" };
    static readonly string[] descriptions = {
        "Detector: hear a stronger signal near hidden clues.",
        "Explorer: find and collect all three clues.",
        "Healer: press E to restore nearby teammates; 10 second cooldown.",
        "Runner: move faster and use a larger stamina pool."
    };
    void Awake() { Instance = this; }
    void Start() { if (startButton != null) startButton.onClick.AddListener(StartGame); }
    void OnDestroy() { if (Instance == this) Instance = null; }
    public void SetPlayerCount(int count) { if (statusText != null) statusText.text = count + " / 2"; }
    public void ApplyState(string[] parts)
    {
        if (parts.Length != 8 || !int.TryParse(parts[1], out int h)) return;
        int[] values = new int[6];
        for (int i = 0; i < 6; i++) if (!int.TryParse(parts[i + 2], out values[i])) return;
        host = h;
        for (int i = 1; i <= 2; i++)
        { connected[i] = values[(i-1)*3] == 1; roles[i] = values[(i-1)*3+1]; ready[i] = values[(i-1)*3+2] == 1; }
    }
    public void StartGame() { if (NetworkClient.Instance != null) NetworkClient.Instance.RequestStart(); }
    void OnGUI()
    {
        var net = NetworkClient.Instance;
        if (net == null || net.offlineMode || net.GameStarted) return;
        GUI.ModalWindow(GetInstanceID(), new Rect((Screen.width-520)/2f, (Screen.height-370)/2f, 520, 370), DrawLobby, "NOA - Lobby");
    }
    void DrawLobby(int id)
    {
        var net = NetworkClient.Instance;
        GUILayout.Space(16);
        GUILayout.Label(net.ConnectionStatus);
        for (int i = 1; i <= 2; i++)
            GUILayout.Label("Player " + i + (host == i ? " [Host]" : "") + (i == net.playerId ? " [You]" : "") + ": " +
                (!connected[i] ? "Waiting for player" : (roles[i] >= 0 && roles[i] < names.Length ? names[roles[i]] : "Choose a class") + (ready[i] ? " - Ready" : " - Not ready")));
        GUILayout.Space(20);
        bool localReady = ready[Mathf.Clamp(net.playerId, 1, 2)];
        GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && !net.GameStarted;
        GUILayout.Label("Choose your class (changing class cancels ready)");
        GUILayout.BeginHorizontal();
        for (int i = 0; i < names.Length; i++) if (GUILayout.Button(names[i], GUILayout.Height(40))) net.SelectRole(i);
        GUILayout.EndHorizontal();
        int selectedRole = roles[Mathf.Clamp(net.playerId, 1, 2)];
        if (selectedRole >= 0 && selectedRole < descriptions.Length)
            GUILayout.Label(descriptions[selectedRole]);
        GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && !net.GameStarted && roles[Mathf.Clamp(net.playerId,1,2)] >= 0;
        if (GUILayout.Button(localReady ? "Cancel ready" : "Ready", GUILayout.Height(40))) net.SetReady(!localReady);
        bool hasExplorer = roles[1] == (int)PlayerRole.Explorer || roles[2] == (int)PlayerRole.Explorer;
        GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && !net.GameStarted && net.playerId == host && connected[1] && connected[2] && ready[1] && ready[2] && hasExplorer;
        if (GUILayout.Button("Start game (Host)", GUILayout.Height(40))) StartGame();
        GUI.enabled = true;
        GUILayout.Label(hasExplorer ? "Both players must choose a class and be ready." : "At least one player must be Explorer to collect clues.");
    }
}
