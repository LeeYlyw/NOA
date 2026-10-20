using UnityEngine;

// One shared puzzle spawned from deterministic map positions on both clients.
// Player 1 describes the colored shape; player 2 selects it while both stand on pads.
public class CoopShapePuzzle : MonoBehaviour
{
    public static CoopShapePuzzle Instance { get; private set; }

    static readonly string[] ShapeNames = { "SQUARE", "TRIANGLE", "CIRCLE" };
    static readonly string[] ColorNames = { "RED", "BLUE", "YELLOW" };
    static readonly Color[] ShapeColors = { new Color(0.95f, 0.2f, 0.2f), new Color(0.2f, 0.55f, 1f), new Color(1f, 0.85f, 0.1f) };

    readonly bool[] pads = new bool[3];
    readonly int[] choices = { -1, -1, -1, -1 };
    readonly Texture2D[] shapeImages = new Texture2D[9];
    Vector3 firstPad;
    Vector3 secondPad;
    Transform cabinetDoor;
    Vector3 closedDoorPosition;
    bool configured;
    bool locallyOnPad;
    bool solved;
    int clue = -1;
    float wrongUntil;

    public static GameObject Create(Vector3 player1Spawn, Vector3 player2Spawn)
    {
        GameObject root = new GameObject("Cooperative Shape Puzzle");
        CoopShapePuzzle puzzle = root.AddComponent<CoopShapePuzzle>();
        puzzle.Configure(player1Spawn, player2Spawn);
        return root;
    }

    void Awake() { Instance = this; }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (Texture2D image in shapeImages) if (image != null) Destroy(image);
    }

    void Configure(Vector3 p1, Vector3 p2)
    {
        firstPad = FindPadPosition(p1);
        secondPad = FindPadPosition(p2);
        CreatePad("Blue description pad", firstPad, new Color(0.1f, 0.7f, 1f));
        CreatePad("Orange selection pad", secondPad, new Color(1f, 0.45f, 0.08f));

        // A visible cabinet opens on both clients when the server accepts the answer.
        Vector3 direction = (p1 - p2).normalized;
        Vector3 cabinetPosition = secondPad + Vector3.Cross(Vector3.up, direction) * 2.7f;
        cabinetPosition.y = secondPad.y - 0.1f;
        GameObject cabinet = CreatePrimitive("Puzzle cabinet", PrimitiveType.Cube,
            cabinetPosition + Vector3.up * 0.8f, new Vector3(1.8f, 1.6f, 0.45f), new Color(0.13f, 0.17f, 0.22f));
        GameObject door = CreatePrimitive("Cabinet door", PrimitiveType.Cube,
            cabinetPosition + Vector3.up * 0.8f + Vector3.back * 0.28f,
            new Vector3(1.5f, 1.3f, 0.08f), new Color(0.9f, 0.2f, 0.2f));
        cabinet.transform.SetParent(transform);
        door.transform.SetParent(transform);
        GameObject evidence = CreatePrimitive("Puzzle evidence", PrimitiveType.Sphere,
            cabinetPosition + Vector3.up * 0.8f + Vector3.back * 0.36f,
            Vector3.one * 0.45f, new Color(1f, 0.8f, 0.12f));
        evidence.transform.SetParent(transform);
        cabinetDoor = door.transform;
        closedDoorPosition = cabinetDoor.position;
        for (int i = 0; i < shapeImages.Length; i++) shapeImages[i] = MakeShapeImage(i);
        configured = true;
    }

    Vector3 FindPadPosition(Vector3 spawn)
    {
        // The players start next to the pads, so stepping on them starts the puzzle.
        Vector3[] offsets = { Vector3.right * 2.5f, Vector3.left * 2.5f, Vector3.forward * 2.5f, Vector3.back * 2.5f };
        foreach (Vector3 offset in offsets)
        {
            Vector3 point = spawn + offset;
            if (UnityEngine.AI.NavMesh.SamplePosition(point, out UnityEngine.AI.NavMeshHit hit, 1f, UnityEngine.AI.NavMesh.AllAreas))
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
            if (cabinetDoor != null)
                cabinetDoor.position = Vector3.MoveTowards(cabinetDoor.position,
                    closedDoorPosition + Vector3.down * 1.4f, Time.deltaTime * 2f);
            return;
        }

        Vector3 pad = network.playerId == 1 ? firstPad : secondPad;
        Vector3 player = network.localPlayerTransform.position;
        bool onPad = new Vector2(player.x - pad.x, player.z - pad.z).magnitude <= 1.2f;
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
        if (parts.Length == 2 && int.TryParse(parts[1], out int value) && value >= 0 && value < 9)
            clue = value;
    }

    public void SetChoices(string[] parts)
    {
        if (parts.Length != 5) return;
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(parts[i + 1], out choices[i]) || choices[i] < 0 || choices[i] >= 9) return;
    }

    public void SetWrong() { wrongUntil = Time.time + 2f; }
    public void SetSolved() { solved = true; }

    void OnGUI()
    {
        NetworkClient network = NetworkClient.Instance;
        if (!configured || network == null || !network.GameStarted || !network.HasAssignedPlayerId) return;

        float width = Mathf.Min(560f, Screen.width - 20f);
        Rect panel = new Rect((Screen.width - width) * 0.5f, 12f, width, 215f);
        GUI.Box(panel, "COOPERATIVE SHAPE PUZZLE");
        float x = panel.x + 12f;
        GUI.Label(new Rect(x, 36f, width - 24f, 25f), "Blue pad: " + (pads[1] ? "occupied" : "empty") +
            "     Orange pad: " + (pads[2] ? "occupied" : "empty"));
        if (solved)
        {
            GUI.Label(new Rect(x, 72f, width - 24f, 35f), "Correct! Cabinet open; one clue collected for the team.");
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
            GUI.Label(new Rect(x, 62f, width - 24f, 28f), "Describe BOTH the shape and its color to your partner:");
            if (clue >= 0) DrawShape(clue, new Rect(x + 10f, 94f, 72f, 72f));
            if (clue >= 0) GUI.Label(new Rect(x + 100f, 107f, width - 125f, 45f),
                ColorNames[clue % 3] + " " + ShapeNames[clue / 3]);
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
                    ColorNames[choices[i] % 3] + " " + ShapeNames[choices[i] / 3]);
            }
        }
        if (Time.time < wrongUntil)
            GUI.Label(new Rect(x, 190f, width - 24f, 22f), "Wrong answer. Check the color AND shape.");
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
        int shape = id / 3;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float nx = (x - 31.5f) / 28f;
                float ny = (y - 31.5f) / 28f;
                bool inside = shape == 0 ? Mathf.Abs(nx) < 0.82f && Mathf.Abs(ny) < 0.82f :
                    shape == 1 ? ny > -0.82f && ny < 0.82f && Mathf.Abs(nx) < (0.82f - ny) * 0.52f :
                    nx * nx + ny * ny < 0.72f;
                pixels[y * 64 + x] = inside ? ShapeColors[id % 3] : Color.clear;
            }
        image.SetPixels(pixels);
        image.Apply();
        return image;
    }
}
