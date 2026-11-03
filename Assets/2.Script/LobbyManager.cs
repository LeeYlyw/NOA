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
    readonly bool[] connected = new bool[5];
    readonly bool[] ready = new bool[5];
    readonly int[] roles = { -1, -1, -1, -1, -1 };
    public bool HasCompatibleServerState { get; private set; }
    int host;
    Font uiFont;
    GUIStyle labelStyle;
    GUIStyle buttonStyle;
    GUIStyle windowStyle;
    static readonly string[] names = { "감지자", "탐색자", "치유자", "러너" };
    static readonly string[] descriptions = {
        "감지자: 단서에 가까워질수록 신호음이 강해집니다. 누구나 획득할 수 있습니다.",
        "탐색자: 단서를 눈으로 확인할 수 있습니다. 누구나 단서를 획득할 수 있습니다.",
        "치유자: E로 가까운 동료를 회복합니다. 단서 탐지는 할 수 없습니다.",
        "러너: 더 빠르고 지구력이 높습니다. 단서 탐지는 할 수 없습니다."
    };
    void Awake() { Instance = this; }
    void Start() { if (startButton != null) startButton.onClick.AddListener(StartGame); }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (uiFont != null) Destroy(uiFont);
    }
    public void SetPlayerCount(int count) { if (statusText != null) statusText.text = count + " / 4"; }
    public bool ApplyState(string[] parts)
    {
        if (parts.Length != 14 || !int.TryParse(parts[1], out int h)) return false;
        int[] values = new int[12];
        for (int i = 0; i < 12; i++) if (!int.TryParse(parts[i + 2], out values[i])) return false;
        host = h;
        for (int i = 1; i <= 4; i++)
        { connected[i] = values[(i-1)*3] == 1; roles[i] = values[(i-1)*3+1]; ready[i] = values[(i-1)*3+2] == 1; }
        int count = 0;
        for (int i = 1; i <= 4; i++) if (connected[i]) count++;
        SetPlayerCount(count);
        HasCompatibleServerState = true;
        return true;
    }
    public void StartGame() { if (NetworkClient.Instance != null) NetworkClient.Instance.RequestStart(); }
    void OnGUI()
    {
        var net = NetworkClient.Instance;
        if (net == null || net.offlineMode || net.GameStarted) return;
        if (labelStyle == null)
        {
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 18);
            labelStyle = new GUIStyle(GUI.skin.label) { font = uiFont, wordWrap = true };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = uiFont };
            windowStyle = new GUIStyle(GUI.skin.window) { font = uiFont };
        }
        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 800f, Screen.height / 600f), 0.7f, 1.5f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Screen.width / scale;
        float height = Screen.height / scale;
        float panelWidth = Mathf.Min(560f, width - 24f);
        float panelHeight = Mathf.Min(510f, height - 24f);
        GUI.ModalWindow(GetInstanceID(), new Rect((width - panelWidth) / 2f, (height - panelHeight) / 2f, panelWidth, panelHeight), DrawLobby, "NOA - 로비", windowStyle);
        GUI.matrix = previousMatrix;
    }
    void DrawLobby(int id)
    {
        var net = NetworkClient.Instance;
        GUILayout.Space(16);
        GUILayout.Label(net.ConnectionStatus, labelStyle);
        int participantCount = 0;
        for (int i = 1; i <= 4; i++) if (connected[i]) participantCount++;
        GUILayout.Label("참가자  " + participantCount + " / 4", labelStyle);
        for (int row = 0; row < 2; row++)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < 2; column++)
            {
                int slot = row * 2 + column + 1;
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(255f), GUILayout.Height(64f));
                GUILayout.Label("플레이어 " + slot + (host == slot ? "  [방장]" : "") +
                    (net.playerId == slot ? "  [나]" : ""), labelStyle);
                GUILayout.Label(!connected[slot] ? "접속 대기" :
                    (roles[slot] >= 0 && roles[slot] < names.Length ? names[roles[slot]] : "직업 선택 대기") +
                    (ready[slot] ? "  ·  준비 완료" : "  ·  준비 전"), labelStyle);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(12);
        bool localReady = ready[Mathf.Clamp(net.playerId, 1, 4)];
        GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && HasCompatibleServerState && !net.GameStarted;
        GUILayout.Label("직업을 선택하세요. 동료와 같은 직업은 선택할 수 없습니다.", labelStyle);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < names.Length; i++)
        {
            bool taken = false;
            for (int slot = 1; slot <= 4; slot++)
                if (slot != net.playerId && connected[slot] && roles[slot] == i) taken = true;
            GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && HasCompatibleServerState && !net.GameStarted &&
                !taken;
            if (GUILayout.Button(names[i], buttonStyle, GUILayout.Height(40))) net.SelectRole(i);
        }
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        int selectedRole = roles[Mathf.Clamp(net.playerId, 1, 4)];
        if (selectedRole >= 0 && selectedRole < descriptions.Length)
            GUILayout.Label(descriptions[selectedRole], labelStyle);
        GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && HasCompatibleServerState && !net.GameStarted && roles[Mathf.Clamp(net.playerId,1,4)] >= 0;
        if (GUILayout.Button(localReady ? "준비 취소" : "준비", buttonStyle, GUILayout.Height(40))) net.SetReady(!localReady);
        int connectedCount = 0;
        bool allReady = true;
        for (int slot = 1; slot <= 4; slot++) if (connected[slot])
        { connectedCount++; if (!ready[slot] || roles[slot] < 0) allReady = false; }
        GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && HasCompatibleServerState && !net.GameStarted &&
            net.playerId == host && connectedCount >= 2 && allReady;
        if (GUILayout.Button("게임 시작 (방장)", buttonStyle, GUILayout.Height(40))) StartGame();
        GUI.enabled = true;
        GUILayout.Label("2~4명이 서로 다른 직업을 선택하고 모두 준비하면 시작할 수 있습니다.", labelStyle);
    }
}
