using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

// One shared puzzle spawned from deterministic map positions on both clients.
// Player 1 reads a compound sigil; player 2 identifies it while both hold the pads.
public class CoopShapePuzzle : MonoBehaviour
{
    public static CoopShapePuzzle Instance { get; private set; }

    static readonly string[] ShapeNames = { "사각형", "삼각형", "원", "마름모" };
    static readonly string[] ColorNames = { "빨강", "파랑", "노랑", "초록" };
    static readonly string[] MarkNames = { "점", "막대", "십자" };
    static readonly Color[] ShapeColors = { new Color(0.95f, 0.2f, 0.2f), new Color(0.2f, 0.55f, 1f), new Color(1f, 0.85f, 0.1f), new Color(0.25f, 0.85f, 0.4f) };

    readonly bool[] pads = new bool[3];
    readonly int[] choices = { -1, -1, -1, -1 };
    readonly Texture2D[] shapeImages = new Texture2D[48 * 3];
    Canvas puzzleCanvas;
    Font panelFont;
    Text roundLabel;
    Text directionLabel;
    Text warningLabel;
    GameObject clueView;
    GameObject choicesView;
    RawImage clueImage;
    Text clueDescription;
    readonly RawImage[] choiceImages = new RawImage[4];
    readonly Text[] choiceDescriptions = new Text[4];
    Vector3 firstPad;
    Vector3 secondPad;
    Transform roomGate;
    NavMeshObstacle gateObstacle;
    Vector3 closedGatePosition;
    GameObject lockedClue;
    bool configured;
    bool locallyOnPad;
    float nextPadReportAt;
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
    public Vector3 LockedRoomApproach { get; private set; }

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
        if (panelFont != null) Destroy(panelFont);
    }

    void Configure(Vector3 p1, Vector3 p2, Vector3 roomCenter, float tileSize)
    {
        LockedRoomApproach = roomCenter + Vector3.back * tileSize;
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
        for (int stage = 1; stage <= 3; stage++)
            for (int id = 0; id < 48; id++)
                shapeImages[(stage - 1) * 48 + id] = MakeShapeImage(id, stage);
        CreatePuzzlePanel();
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
        UpdatePuzzlePanel(network);
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
            nextPadReportAt = 0f;
        }
        // An entry can be rejected while the server still has the previous
        // MOVE position. Retry until its S_PUZZLE_PADS state confirms it.
        if (pads[network.playerId] != onPad && Time.time >= nextPadReportAt)
        {
            network.SendPuzzlePad(onPad);
            nextPadReportAt = Time.time + 0.2f;
        }

        if (network.playerId == 2 && pads[1] && pads[2] && locallyOnPad &&
            choices[0] >= 0 && Time.time >= wrongUntil)
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
        { round = newRound; clue = value; }
    }

    public void SetChoices(string[] parts)
    {
        if (parts.Length != 6 || !int.TryParse(parts[1], out int newRound) || newRound < round) return;
        round = newRound;
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(parts[i + 2], out choices[i]) || choices[i] < 0 || choices[i] >= 48) return;
    }

    public void SetProgress(string[] parts)
    {
        if (parts.Length == 3 && int.TryParse(parts[1], out int newRound) &&
            int.TryParse(parts[2], out int count) && newRound >= round && count > 0)
        { round = newRound; totalRounds = count; }
    }

    public void SetWrong()
    {
        if (solved) return;
        round = 0;
        clue = -1;
        for (int i = 0; i < choices.Length; i++) choices[i] = -1;
        wrongUntil = Time.time + 1.5f;
        WrongFeedbackVersion++;
    }
    public void SetSolved()
    {
        solved = true;
        if (lockedClue != null) lockedClue.SetActive(true);
    }

    void CreatePuzzlePanel()
    {
        panelFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Arial" }, 18);
        GameObject root = new GameObject("Puzzle HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        puzzleCanvas = root.GetComponent<Canvas>();
        puzzleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        puzzleCanvas.sortingOrder = 25;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = new GameObject("Sigil panel", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(root.transform, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -12f);
        panel.sizeDelta = new Vector2(660f, 260f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.045f, 0.065f, 0.94f);
        panel.GetComponent<Image>().raycastTarget = false;
        Block(panel, "Accent", 0, 0, 660, 4, new Color(0.32f, 0.88f, 0.81f));
        roundLabel = Label(panel, "", 20, 15, 620, 30, 21, Color.white);
        directionLabel = Label(panel, "", 20, 49, 620, 30, 17,
            new Color(0.7f, 0.84f, 0.88f));
        warningLabel = Label(panel, "", 20, 229, 620, 25, 16,
            new Color(1f, 0.45f, 0.35f));

        clueView = Block(panel, "Description view", 20, 87, 620, 132,
            new Color(0.08f, 0.12f, 0.15f)).gameObject;
        clueImage = Picture(clueView.transform, 22, 18, 96, 96);
        clueDescription = Label(clueView.transform, "", 140, 34, 455, 65, 23, Color.white);

        choicesView = Block(panel, "Selection view", 20, 87, 620, 132, Color.clear).gameObject;
        for (int i = 0; i < 4; i++)
        {
            Transform option = Block(choicesView.transform, "Option " + (i + 1), i * 155, 0, 150, 132,
                new Color(0.08f, 0.12f, 0.15f)).transform;
            Label(option, (i + 1).ToString(), 9, 8, 25, 24, 19,
                new Color(0.32f, 0.88f, 0.81f));
            choiceImages[i] = Picture(option, 45, 10, 65, 65);
            choiceDescriptions[i] = Label(option, "", 8, 82, 136, 42, 13, Color.white);
        }
        puzzleCanvas.enabled = false;
    }

    void UpdatePuzzlePanel(NetworkClient network)
    {
        if (puzzleCanvas == null) return;
        bool visible = configured && network != null && network.GameStarted &&
            network.HasAssignedPlayerId && !solved && pads[1] && pads[2] &&
            (ClueManager.instance == null || !ClueManager.instance.IsEnding);
        puzzleCanvas.enabled = visible;
        if (!visible) return;

        roundLabel.text = "협동 문양 잠금 장치   " + Mathf.Max(1, round) + " / " + totalRounds;
        bool describing = network.playerId == 1;
        clueView.SetActive(describing);
        choicesView.SetActive(!describing);
        directionLabel.text = describing
            ? "모양·색·중앙 표시를 동료에게 설명하세요."
            : "모양·색·중앙 표시를 비교하세요. 숫자 1~4";
        if (describing)
        {
            clueImage.texture = clue >= 0 ? StageImage(clue) : null;
            clueDescription.text = clue >= 0 ? Describe(clue, round) : "단서를 기다리는 중...";
        }
        else
            for (int i = 0; i < 4; i++)
            {
                choiceImages[i].texture = choices[i] >= 0 ? StageImage(choices[i]) : null;
                choiceDescriptions[i].text = choices[i] >= 0 ? Describe(choices[i], round) : "";
            }
        warningLabel.text = Time.time < wrongUntil ? "오답입니다. 1라운드부터 다시 시작합니다." : "";
    }

    Texture2D StageImage(int id) => shapeImages[(Mathf.Clamp(round, 1, 3) - 1) * 48 + id];

    static RectTransform PanelRect(Transform parent, string name, float x, float y, float width, float height)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    static Image Block(Transform parent, string name, float x, float y,
        float width, float height, Color color)
    {
        Image image = PanelRect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static RawImage Picture(Transform parent, float x, float y, float width, float height)
    {
        RawImage image = PanelRect(parent, "Sigil", x, y, width, height).gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        return image;
    }

    Text Label(Transform parent, string value, float x, float y,
        float width, float height, int size, Color color)
    {
        Text label = PanelRect(parent, "Label", x, y, width, height).gameObject.AddComponent<Text>();
        label.font = panelFont;
        label.fontSize = size;
        label.color = color;
        label.text = value;
        label.raycastTarget = false;
        label.supportRichText = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    static string Describe(int id, int stage)
    {
        return ColorNames[(id / 3) % 4] + " " + ShapeNames[id / 12] +
            " / " + MarkNames[id % 3];
    }

    static Texture2D MakeShapeImage(int id, int stage)
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
                pixels[y * 64 + x] = !inside ? Color.clear :
                    center ? Color.black : ShapeColors[color];
            }
        image.SetPixels(pixels);
        image.Apply();
        return image;
    }
}
