using UnityEngine;
using UnityEngine.AI;

// One shared puzzle spawned from deterministic map positions on both clients.
// Player 1 reads a compound sigil; player 2 identifies it while both hold the pads.
public class CoopShapePuzzle : MonoBehaviour
{
    public static CoopShapePuzzle Instance { get; private set; }

    static readonly string[] ShapeNames = { "SQUARE", "TRIANGLE", "CIRCLE", "DIAMOND" };
    static readonly string[] ColorNames = { "RED", "BLUE", "YELLOW", "GREEN" };
    static readonly string[] MarkNames = { "DOT", "BAR", "CROSS" };
    static readonly Color[] ShapeColors = { new Color(0.95f, 0.2f, 0.2f), new Color(0.2f, 0.55f, 1f), new Color(1f, 0.85f, 0.1f), new Color(0.25f, 0.85f, 0.4f) };

    readonly bool[] pads = new bool[3];
    readonly int[] choices = { -1, -1, -1, -1 };
    readonly Texture2D[] shapeImages = new Texture2D[48];
    Vector3 firstPad;
    Vector3 secondPad;
    Transform roomGate;
    NavMeshObstacle gateObstacle;
    Vector3 closedGatePosition;
    GameObject lockedClue;
    bool configured;
    bool locallyOnPad;
    bool solved;
    int clue = -1;
    int round;
    int totalRounds = 3;
    float wrongUntil;

    public int WrongFeedbackVersion { get; private set; }
    public bool IsSolved => solved;
    public bool BluePadOccupied => pads[1];
    public bool OrangePadOccupied => pads[2];
    public int CompletedRounds => solved ? totalRounds : Mathf.Clamp(round - 1, 0, totalRounds);
    public int TotalRounds => totalRounds;
    public Vector3 GetAssignedPad(int playerId) => playerId == 1 ? firstPad : secondPad;

    public static GameObject Create(Vector3 player1Spawn, Vector3 player2Spawn, Vector3 roomCenter, float tileSize)
    {
        GameObject root = new GameObject("Cooperative Shape Puzzle");
        CoopShapePuzzle puzzle = root.AddComponent<CoopShapePuzzle>();
        puzzle.Configure(player1Spawn, player2Spawn, roomCenter, tileSize);
        return root;
    }

    void Awake() { Instance = this; }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (Texture2D image in shapeImages) if (image != null) Destroy(image);
    }

    void Configure(Vector3 p1, Vector3 p2, Vector3 roomCenter, float tileSize)
    {
        firstPad = FindPadPosition(p1, Vector3.zero);
        secondPad = FindPadPosition(p2, firstPad);
        CreatePad("Blue description pad", firstPad, new Color(0.1f, 0.7f, 1f));
        CreatePad("Orange selection pad", secondPad, new Color(1f, 0.45f, 0.08f));

        // The 1x1 room has an open south side. Seal it until the server solves the puzzle.
        GameObject gate = CreatePrimitive("Sigil locked room gate", PrimitiveType.Cube,
            roomCenter + Vector3.back * (tileSize * 0.5f) + Vector3.up * 2f,
            new Vector3(tileSize - 0.4f, 4f, 0.35f), new Color(0.8f, 0.13f, 0.17f));
        gate.transform.SetParent(transform);
        gate.AddComponent<BoxCollider>();
        // The gate is created after the runtime NavMesh is built. Carve its closed
        // footprint so agents cannot plan through it until the puzzle is solved.
        gateObstacle = gate.AddComponent<NavMeshObstacle>();
        gateObstacle.shape = NavMeshObstacleShape.Box;
        gateObstacle.size = Vector3.one;
        gateObstacle.carving = true;
        gateObstacle.carveOnlyStationary = false;
        roomGate = gate.transform;
        closedGatePosition = roomGate.position;
        for (int i = 0; i < shapeImages.Length; i++) shapeImages[i] = MakeShapeImage(i);
        configured = true;
        gameObject.AddComponent<PuzzlePresentation>();
    }

    public void SetLockedClue(GameObject clue)
    {
        lockedClue = clue;
        if (lockedClue != null) lockedClue.SetActive(solved);
    }

    Vector3 FindPadPosition(Vector3 spawn, Vector3 otherPad)
    {
        // The players start next to the pads, so stepping on them starts the puzzle.
        Vector3[] offsets = { Vector3.right * 2.5f, Vector3.left * 2.5f, Vector3.forward * 2.5f, Vector3.back * 2.5f,
            (Vector3.right + Vector3.forward).normalized * 2.5f,
            (Vector3.left + Vector3.back).normalized * 2.5f };
        if (!UnityEngine.AI.NavMesh.SamplePosition(spawn, out UnityEngine.AI.NavMeshHit start, 1.5f, UnityEngine.AI.NavMesh.AllAreas))
            return spawn;
        UnityEngine.AI.NavMeshPath path = new UnityEngine.AI.NavMeshPath();
        foreach (Vector3 offset in offsets)
        {
            Vector3 point = spawn + offset;
            if (UnityEngine.AI.NavMesh.SamplePosition(point, out UnityEngine.AI.NavMeshHit hit, 0.8f, UnityEngine.AI.NavMesh.AllAreas) &&
                (otherPad == Vector3.zero || Vector3.Distance(hit.position, otherPad) >= 3f) &&
                UnityEngine.AI.NavMesh.CalculatePath(start.position, hit.position, UnityEngine.AI.NavMesh.AllAreas, path) &&
                path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                return hit.position;
        }
        return spawn;
    }

    void CreatePad(string label, Vector3 position, Color color)
    {
        GameObject pad = CreatePrimitive(label, PrimitiveType.Cylinder,
            position + Vector3.up * 0.08f, new Vector3(1.6f, 0.08f, 1.6f), color);
        pad.transform.SetParent(transform);
        GameObject beacon = CreatePrimitive(label + " beacon", PrimitiveType.Cylinder,
            position + Vector3.up * 1.25f, new Vector3(0.07f, 1.1f, 0.07f), color);
        beacon.transform.SetParent(transform);
    }

    static GameObject CreatePrimitive(string label, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = label;
        obj.transform.position = position;
        obj.transform.localScale = scale;
        Collider collider = obj.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        obj.GetComponent<Renderer>().material = material;
        return obj;
    }

    void Update()
    {
        NetworkClient network = NetworkClient.Instance;
        if (!configured || network == null || !network.IsConnected || !network.GameStarted ||
            !network.HasAssignedPlayerId || network.localPlayerTransform == null) return;

        if (solved)
        {
            if (roomGate != null)
            {
                roomGate.position = Vector3.MoveTowards(roomGate.position,
                    closedGatePosition + Vector3.down * 4.2f, Time.deltaTime * 4f);
                if (gateObstacle != null && roomGate.position.y <= closedGatePosition.y - 4f)
                    gateObstacle.enabled = false;
            }
            return;
        }

        Vector3 pad = network.playerId == 1 ? firstPad : secondPad;
        Vector3 player = network.localPlayerTransform.position;
        PlayerController controller = network.localPlayerTransform.GetComponent<PlayerController>();
        bool onPad = controller != null && !controller.IsDeadState &&
            Mathf.Abs(player.y - pad.y) <= 1f &&
            new Vector2(player.x - pad.x, player.z - pad.z).magnitude <= 1.2f;
        if (onPad != locallyOnPad)
        {
            locallyOnPad = onPad;
            network.SendPuzzlePad(onPad);
        }

        if (network.playerId == 2 && pads[1] && pads[2] && locallyOnPad && choices[0] >= 0)
        {
            for (int i = 0; i < 4; i++)
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)) ||
                    Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + i)))
                    network.SendPuzzleChoice(i);
        }
    }

    public void SetPads(string[] parts)
    {
        if (parts.Length != 3) return;
        pads[1] = parts[1] == "1";
        pads[2] = parts[2] == "1";
    }

    public void SetClue(string[] parts)
    {
        if (parts.Length == 3 && int.TryParse(parts[1], out int newRound) &&
            int.TryParse(parts[2], out int value) && newRound >= round && value >= 0 && value < 48)
        { round = newRound; clue = value; wrongUntil = 0f; }
    }

    public void SetChoices(string[] parts)
    {
        if (parts.Length != 6 || !int.TryParse(parts[1], out int newRound) || newRound < round) return;
        round = newRound;
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(parts[i + 2], out choices[i]) || choices[i] < 0 || choices[i] >= 48) return;
        wrongUntil = 0f;
    }

    public void SetProgress(string[] parts)
    {
        if (parts.Length == 3 && int.TryParse(parts[1], out int newRound) &&
            int.TryParse(parts[2], out int count) && newRound >= round && count > 0)
        { round = newRound; totalRounds = count; }
    }

    public void SetWrong() { if (solved) return; wrongUntil = Time.time + 2f; WrongFeedbackVersion++; }
    public void SetSolved()
    {
        solved = true;
        if (lockedClue != null) lockedClue.SetActive(true);
    }

    void OnGUI()
    {
        NetworkClient network = NetworkClient.Instance;
        if (!configured || network == null || !network.GameStarted || !network.HasAssignedPlayerId) return;
        // Persistent status and directions are supplied by ObjectiveHUD.
        // Only show the private puzzle contents while both players are on their pads.
        if (solved || !pads[1] || !pads[2]) return;

        float width = Mathf.Min(560f, Screen.width - 20f);
        Rect panel = new Rect((Screen.width - width) * 0.5f, 12f, width, 215f);
        GUI.Box(panel, "COOPERATIVE SIGIL LOCK  -  " + Mathf.Max(1, round) + "/" + totalRounds);
        float x = panel.x + 12f;
        GUI.Label(new Rect(x, 36f, width - 24f, 25f), "Blue pad: " + (pads[1] ? "occupied" : "empty") +
            "     Orange pad: " + (pads[2] ? "occupied" : "empty"));
        if (solved)
        {
            GUI.Label(new Rect(x, 72f, width - 24f, 35f), "Lock released! Find and collect the clue inside the opened room.");
            return;
        }
        if (!pads[1] || !pads[2])
        {
            GUI.Label(new Rect(x, 72f, width - 24f, 55f), network.playerId == 1
                ? "Stand on the BLUE pad. Ask your partner to stand on ORANGE."
                : "Stand on the ORANGE pad. Ask your partner to stand on BLUE.");
            Vector3 target = network.playerId == 1 ? firstPad : secondPad;
            if (network.localPlayerTransform != null)
                GUI.Label(new Rect(x, 110f, width - 24f, 35f), "Your pad: " +
                    Vector3.Distance(network.localPlayerTransform.position, target).ToString("F1") + " m away");
            return;
        }
        if (network.playerId == 1)
        {
            GUI.Label(new Rect(x, 62f, width - 24f, 28f), "Describe the outline, color, and center mark to your partner:");
            if (clue >= 0) DrawShape(clue, new Rect(x + 10f, 94f, 72f, 72f));
            if (clue >= 0) GUI.Label(new Rect(x + 100f, 107f, width - 125f, 45f),
                Describe(clue));
        }
        else
        {
            GUI.Label(new Rect(x, 61f, width - 24f, 25f), "Listen to your partner. Press 1, 2, 3, or 4 to choose:");
            for (int i = 0; i < 4; i++)
            {
                float optionX = x + i * (width - 24f) / 4f;
                if (choices[i] < 0) continue;
                GUI.Box(new Rect(optionX, 89f, (width - 24f) / 4f - 4f, 99f), (i + 1).ToString());
                DrawShape(choices[i], new Rect(optionX + 26f, 111f, 52f, 52f));
                GUI.Label(new Rect(optionX + 10f, 163f, 110f, 22f),
                    Describe(choices[i]));
            }
        }
        if (Time.time < wrongUntil)
            GUI.Label(new Rect(x, 190f, width - 24f, 22f), "Wrong sigil. Compare all three details; retry shortly.");
    }

    static string Describe(int id)
    {
        return ColorNames[(id / 3) % 4] + " " + ShapeNames[id / 12] + " / " + MarkNames[id % 3];
    }

    void DrawShape(int id, Rect area)
    {
        if (id >= 0 && id < shapeImages.Length && shapeImages[id] != null)
            GUI.DrawTexture(area, shapeImages[id], ScaleMode.ScaleToFit, true);
    }

    static Texture2D MakeShapeImage(int id)
    {
        Texture2D image = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        image.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[64 * 64];
        int shape = id / 12;
        int color = (id / 3) % 4;
        int mark = id % 3;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float nx = (x - 31.5f) / 28f;
                float ny = (y - 31.5f) / 28f;
                bool inside = shape == 0 ? Mathf.Abs(nx) < 0.82f && Mathf.Abs(ny) < 0.82f :
                    shape == 1 ? ny > -0.82f && ny < 0.82f && Mathf.Abs(nx) < (0.82f - ny) * 0.52f :
                    shape == 2 ? nx * nx + ny * ny < 0.72f :
                    Mathf.Abs(nx) + Mathf.Abs(ny) < 0.98f;
                bool center = mark == 0 ? nx * nx + ny * ny < 0.055f :
                    mark == 1 ? Mathf.Abs(nx) < 0.075f && Mathf.Abs(ny) < 0.35f :
                    (Mathf.Abs(nx) < 0.065f && Mathf.Abs(ny) < 0.34f) ||
                    (Mathf.Abs(ny) < 0.065f && Mathf.Abs(nx) < 0.34f);
                pixels[y * 64 + x] = !inside ? Color.clear : center ? Color.black : ShapeColors[color];
            }
        image.SetPixels(pixels);
        image.Apply();
        return image;
    }
}
