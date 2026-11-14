using UnityEngine;

public class PowerRestoration : MonoBehaviour
{
    public static PowerRestoration Instance { get; private set; }
    readonly GameObject[] objects = new GameObject[6];
    readonly int[] owners = new int[3];
    readonly bool[] repaired = new bool[3];
    GUIStyle label;

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

    void Update()
    {
        NetworkClient network = NetworkClient.Instance;
        if (network == null || network.localPlayerTransform == null || !network.GameStarted || !Input.GetKeyDown(KeyCode.E)) return;
        for (int i = 0; i < objects.Length; i++)
        {
            if ((i < 3 && owners[i] != 0) || (i >= 3 && repaired[i - 3])) continue;
            if (Vector3.Distance(network.localPlayerTransform.position, objects[i].transform.position) > 2f) continue;
            if (!network.offlineMode) network.SendPowerRequest(240 + i);
            else
            {
                if (i < 3) owners[i] = 1;
                else
                    for (int fuse = 0; fuse < 3; fuse++)
                        if (owners[fuse] == 1) { owners[fuse] = -1; repaired[i - 3] = true; break; }
                string[] state = { "S_POWER", owners[0].ToString(), owners[1].ToString(), owners[2].ToString(),
                    repaired[0] ? "1" : "0", repaired[1] ? "1" : "0", repaired[2] ? "1" : "0" };
                ApplyState(state);
            }
            break;
        }
    }

    void OnGUI()
    {
        NetworkClient network = NetworkClient.Instance;
        if (network == null || !network.GameStarted || network.localPlayerTransform == null) return;
        if (label == null) label = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = Color.white } };
        int count = 0, held = 0;
        for (int i = 0; i < 3; i++) { if (repaired[i]) count++; if (owners[i] == network.playerId) held++; }
        GUI.Label(new Rect(20, 90, 450, 30), $"전원 복구 {count}/3  |  보유 퓨즈 {held}", label);
        for (int i = 0; i < objects.Length; i++)
        {
            if ((i < 3 && owners[i] != 0) || (i >= 3 && repaired[i - 3])) continue;
            if (Vector3.Distance(network.localPlayerTransform.position, objects[i].transform.position) <= 2f)
            {
                GUI.Label(new Rect(20, 120, 500, 30), i < 3 ? "E: 퓨즈 획득" : "E: 퓨즈로 차단기 복구", label);
                break;
            }
        }
    }
}
