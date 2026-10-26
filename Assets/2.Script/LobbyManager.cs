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
        if (labelStyle == null)
        {
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 18);
            labelStyle = new GUIStyle(GUI.skin.label) { font = uiFont, wordWrap = true };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = uiFont };
            windowStyle = new GUIStyle(GUI.skin.window) { font = uiFont };
        }
        GUI.ModalWindow(GetInstanceID(), new Rect((Screen.width-520)/2f, (Screen.height-370)/2f, 520, 370), DrawLobby, "NOA - 로비", windowStyle);
    }
    void DrawLobby(int id)
    {
        var net = NetworkClient.Instance;
        GUILayout.Space(16);
        GUILayout.Label(net.ConnectionStatus, labelStyle);
        for (int i = 1; i <= 2; i++)
            GUILayout.Label("플레이어 " + i + (host == i ? " [방장]" : "") + (i == net.playerId ? " [나]" : "") + ": " +
                (!connected[i] ? "접속 대기" : (roles[i] >= 0 && roles[i] < names.Length ? names[roles[i]] : "직업 선택 대기") + (ready[i] ? " · 준비 완료" : " · 준비 전")), labelStyle);
        GUILayout.Space(20);
        bool localReady = ready[Mathf.Clamp(net.playerId, 1, 2)];
        GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && !net.GameStarted;
        GUILayout.Label("직업을 선택하세요. 변경하면 준비 상태가 해제됩니다.", labelStyle);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < names.Length; i++) if (GUILayout.Button(names[i], buttonStyle, GUILayout.Height(40))) net.SelectRole(i);
        GUILayout.EndHorizontal();
        int selectedRole = roles[Mathf.Clamp(net.playerId, 1, 2)];
        if (selectedRole >= 0 && selectedRole < descriptions.Length)
            GUILayout.Label(descriptions[selectedRole], labelStyle);
        GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && !net.GameStarted && roles[Mathf.Clamp(net.playerId,1,2)] >= 0;
        if (GUILayout.Button(localReady ? "준비 취소" : "준비", buttonStyle, GUILayout.Height(40))) net.SetReady(!localReady);
        GUI.enabled = net.IsConnected && net.HasAssignedPlayerId && !net.GameStarted && net.playerId == host && connected[1] && connected[2] && ready[1] && ready[2];
        if (GUILayout.Button("게임 시작 (방장)", buttonStyle, GUILayout.Height(40))) StartGame();
        GUI.enabled = true;
        GUILayout.Label("두 플레이어가 직업을 선택하고 준비하면 시작할 수 있습니다. 단서는 누구나 획득할 수 있습니다.", labelStyle);
    }
}
