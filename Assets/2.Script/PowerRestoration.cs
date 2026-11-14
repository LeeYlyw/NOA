using UnityEngine;
using System.Globalization;

[DefaultExecutionOrder(-100)]
public class PowerRestoration : MonoBehaviour
{
    public static PowerRestoration Instance { get; private set; }
    readonly GameObject[] objects = new GameObject[6];
    readonly int[] owners = new int[3];
    readonly bool[] repaired = new bool[3];
    GUIStyle label;
    VitalsHUDStyle vitals;
    int repairObject = -1, repairSequence, repairProgress, inputFrame = -1;
    float repairStart, repairTarget, repairWindow = 0.16f, repairDuration = 2.4f;
    bool waitingForRepair;
    string repairFeedback = "";
    float feedbackUntil;
    public bool BlocksJump => repairObject >= 0 || inputFrame == Time.frameCount;
    public bool UsesInteraction
    {
        get
        {
            var network = NetworkClient.Instance;
            return BlocksJump || (network != null && network.localPlayerTransform != null &&
                FindNearbyObject(network.localPlayerTransform.position) >= 0);
        }
    }

    public void ApplyRepairChallenge(string[] parts)
    {
        if (parts.Length != 8 || repairObject < 0) return;
        if (!int.TryParse(parts[1], out int objectId) || objectId != repairObject + 240 ||
            !int.TryParse(parts[2], out repairSequence) || !int.TryParse(parts[3], out repairProgress) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out repairTarget) ||
            !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out repairWindow) ||
            !float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out repairDuration)) return;
        repairStart = Time.unscaledTime;
        waitingForRepair = false;
        repairFeedback = parts[7] == "MISS" ? "실패! 진행도 감소 · 소음 발생" :
            parts[7] == "OK" ? "성공!" : "초록 구간에서 Space";
    }

    public void EndRepair(string reason)
    {
        inputFrame = Time.frameCount;
        repairObject = -1;
        waitingForRepair = false;
        repairFeedback = reason == "DONE" ? "차단기 복구 완료" :
            reason == "BUSY" ? "동료가 수리 중입니다" : "수리 중단 · 퓨즈는 유지됩니다";
        feedbackUntil = Time.unscaledTime + 2f;
    }

    void CancelRepair(NetworkClient network)
    {
        if (!network.offlineMode) network.SendRepairCancel();
        EndRepair("CANCEL");
    }

    void NextOfflineChallenge()
    {
        repairTarget = Random.Range(0.35f, 0.7f);
        repairStart = Time.unscaledTime;
        waitingForRepair = false;
    }

    public bool IsComplete => repaired[0] && repaired[1] && repaired[2];

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void CreateObject(int index, Vector3 position)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = index < 3 ? "Fuse " + (index + 1) : "Breaker " + (index - 2);
        cube.transform.SetParent(transform, false);
        cube.transform.position = position + Vector3.up * (index < 3 ? 0.35f : 0.7f);
        cube.transform.localScale = index < 3 ? new Vector3(0.4f, 0.4f, 0.4f) : new Vector3(0.8f, 1.2f, 0.35f);
        Destroy(cube.GetComponent<Collider>());
        cube.GetComponent<Renderer>().material.color = index < 3 ? Color.yellow : Color.red;
        objects[index] = cube;
    }

    public Vector3 Position(int index) => objects[index].transform.position;

    public void ApplyState(string[] parts)
    {
        if (parts.Length != 7) return;
        int[] values = new int[6];
        for (int i = 0; i < values.Length; i++)
            if (!int.TryParse(parts[i + 1], out values[i])) return;
        for (int i = 0; i < 3; i++)
        {
            owners[i] = values[i];
            repaired[i] = values[i + 3] == 1;
            objects[i].SetActive(owners[i] == 0);
            objects[i + 3].GetComponent<Renderer>().material.color = repaired[i] ? Color.green : Color.red;
        }
        if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.RefreshLockedClue();
    }

    int FindNearbyObject(Vector3 position)
    {
        int nearest = -1;
        float nearestDistance = 2f;
        for (int i = 0; i < objects.Length; i++)
        {
            if ((i < 3 && owners[i] != 0) || (i >= 3 && repaired[i - 3])) continue;
            float distance = Vector3.Distance(position, objects[i].transform.position);
            if (distance <= nearestDistance) { nearest = i; nearestDistance = distance; }
        }
        return nearest;
    }

    int HeldFuses(int playerId)
    {
        int held = 0;
        for (int i = 0; i < owners.Length; i++)
            if (owners[i] == playerId) held++;
        return held;
    }

    void Update()
    {
        NetworkClient network = NetworkClient.Instance;
        if (network == null || network.localPlayerTransform == null || !network.GameStarted) return;
        if (ClueManager.instance != null && ClueManager.instance.IsEnding)
        {
            if (repairObject >= 0) CancelRepair(network);
            return;
        }
        if (!network.offlineMode && (!network.IsConnected || network.IsRestoringSession))
        {
            if (repairObject >= 0) EndRepair("CANCEL");
            return;
        }
        PlayerController player = network.localPlayerTransform.GetComponent<PlayerController>();
        if (player == null) return;
        if (player.IsDeadState)
        {
            if (repairObject >= 0) CancelRepair(network);
            if (network.offlineMode)
                for (int fuse = 0; fuse < 3; fuse++)
                    if (owners[fuse] == network.playerId) { owners[fuse] = 0; objects[fuse].SetActive(true); }
            return;
        }
        if (repairObject >= 0)
        {
            inputFrame = Time.frameCount;
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape) ||
                Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f ||
                Vector3.Distance(network.localPlayerTransform.position, Position(repairObject)) > 2f)
            { CancelRepair(network); return; }
            float elapsed = Time.unscaledTime - repairStart;
            if (waitingForRepair)
            {
                if (elapsed > 5f) CancelRepair(network);
                return;
            }
            if (!Input.GetKeyDown(KeyCode.Space) && elapsed < repairDuration) return;
            if (!network.offlineMode)
            {
                network.SendRepairHit(repairSequence, elapsed);
                waitingForRepair = true;
                repairStart = Time.unscaledTime;
                return;
            }
            bool success = elapsed <= repairDuration && elapsed / repairDuration >= repairTarget &&
                elapsed / repairDuration <= repairTarget + repairWindow;
            repairProgress = success ? repairProgress + 1 : Mathf.Max(0, repairProgress - 1);
            repairFeedback = success ? "성공!" : "실패! 진행도 감소 · 소음 발생";
            if (!success) PlayerNoiseEmitter.OnNoiseEmitted?.Invoke(Position(repairObject), 10f, player.gameObject);
            if (repairProgress >= 3)
            {
                for (int fuse = 0; fuse < 3; fuse++)
                    if (owners[fuse] == network.playerId)
                    { owners[fuse] = -1; repaired[repairObject - 3] = true; break; }
                objects[repairObject].GetComponent<Renderer>().material.color = Color.green;
                if (CoopShapePuzzle.Instance != null) CoopShapePuzzle.Instance.RefreshLockedClue();
                EndRepair("DONE");
            }
            else NextOfflineChallenge();
            return;
        }
        if (!Input.GetKeyDown(KeyCode.E)) return;
        int i = FindNearbyObject(network.localPlayerTransform.position);
        if (i < 0 || (i >= 3 && HeldFuses(network.playerId) == 0)) return;
        inputFrame = Time.frameCount;
        if (i >= 3)
        {
            repairObject = i;
            repairProgress = 0;
            inputFrame = Time.frameCount;
            repairStart = Time.unscaledTime;
            repairFeedback = "수리 시작 중…";
            waitingForRepair = !network.offlineMode;
            if (network.offlineMode) { NextOfflineChallenge(); repairFeedback = "초록 구간에서 Space"; }
            else network.SendPowerRequest(240 + i);
            return;
        }
        if (!network.offlineMode) network.SendPowerRequest(240 + i);
        else
        {
            owners[i] = network.playerId;
            string[] state = { "S_POWER", owners[0].ToString(), owners[1].ToString(), owners[2].ToString(),
                repaired[0] ? "1" : "0", repaired[1] ? "1" : "0", repaired[2] ? "1" : "0" };
            ApplyState(state);
        }
    }

    void OnGUI()
    {
        NetworkClient network = NetworkClient.Instance;
        if (network == null || !network.GameStarted || network.localPlayerTransform == null) return;
        if (ClueManager.instance != null && ClueManager.instance.IsEnding) return;
        if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = Color.white } };
        int count = 0, held = HeldFuses(network.playerId);
        for (int i = 0; i < 3; i++) if (repaired[i]) count++;
        if (vitals == null) vitals = network.localPlayerTransform.GetComponentInChildren<VitalsHUDStyle>();
        if (vitals == null) vitals = FindFirstObjectByType<VitalsHUDStyle>();
        Vector2 origin = new Vector2(30, 156);
        if (vitals != null && vitals.TryGetScreenBottom(out Vector2 bottomLeft))
            origin = bottomLeft + new Vector2(0, 12);
        float width = Mathf.Max(1, Screen.width - origin.x - 20);
        label.wordWrap = true;
        string progress = $"전원 복구 {count}/3  |  보유 퓨즈 {held}";
        float height = label.CalcHeight(new GUIContent(progress), width);
        GUI.Label(new Rect(origin.x, origin.y, width, height), progress, label);
        PlayerController player = network.localPlayerTransform.GetComponent<PlayerController>();
        if (player == null || player.IsDeadState) return;
        if (repairObject >= 0) { DrawRepair(); return; }
        if (Time.unscaledTime < feedbackUntil)
            GUI.Label(new Rect(origin.x, origin.y + height + 4, width, label.CalcHeight(new GUIContent(repairFeedback), width)), repairFeedback, label);
        int nearby = FindNearbyObject(network.localPlayerTransform.position);
        if (nearby >= 0 && Time.unscaledTime >= feedbackUntil)
        {
            string prompt = nearby < 3 ? "E: 퓨즈 획득" :
                held > 0 ? "E: 차단기 수리 시작" : "차단기 복구에 퓨즈가 필요합니다";
            float promptHeight = label.CalcHeight(new GUIContent(prompt), width);
            GUI.Label(new Rect(origin.x, origin.y + height + 4, width, promptHeight), prompt, label);
        }
    }

    void DrawRepair()
    {
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        float scale = Mathf.Min(1f, Screen.width / 460f, Screen.height / 500f);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 420 * scale) / 2f,
            Screen.height - 260 * scale, 0), Quaternion.identity, new Vector3(scale, scale, 1));
        GUI.color = new Color(0.025f, 0.045f, 0.065f, 0.96f);
        GUI.DrawTexture(new Rect(0, 0, 420, 244), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
        GUI.Label(new Rect(10, 4, 400, 28), $"차단기 수리 {repairProgress}/3", centered);
        Vector2 center = new Vector2(210, 111);
        Matrix4x4 panelMatrix = GUI.matrix;
        // Compose local dial transforms before the panel's screen translation/scale.
        // RotateAroundPivot mixes the translated GUI coordinates with this local pivot.
        for (int i = 0; i < 180; i++)
        {
            float phase = i / 180f;
            bool successZone = phase >= repairTarget && phase <= repairTarget + repairWindow;
            GUI.color = successZone
                ? new Color(0.3f, 1f, 0.4f) : new Color(0.3f, 0.35f, 0.4f);
            GUI.matrix = panelMatrix * Matrix4x4.TRS(center,
                Quaternion.Euler(0, 0, phase * 360), Vector3.one);
            GUI.DrawTexture(successZone ? new Rect(-2, -66, 4, 20) : new Rect(-2, -62, 4, 12), Texture2D.whiteTexture);
        }
        if (!waitingForRepair)
        {
            float angle = Mathf.Clamp01((Time.unscaledTime - repairStart) / repairDuration) * 360;
            GUI.matrix = panelMatrix * Matrix4x4.TRS(center,
                Quaternion.Euler(0, 0, angle), Vector3.one);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(-2, -60, 4, 60), Texture2D.whiteTexture);
        }
        GUI.matrix = panelMatrix;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(center.x - 4, center.y - 4, 8, 8), Texture2D.whiteTexture);
        GUI.Label(new Rect(10, 178, 400, 30), waitingForRepair ? "서버 확인 중…" : repairFeedback, centered);
        centered.fontSize = 15;
        GUI.Label(new Rect(10, 208, 400, 30), "Space: 타이밍 맞추기 · 이동 / E / Esc: 중단", centered);
        GUI.color = oldColor;
        GUI.matrix = oldMatrix;
    }
}
