using UnityEngine;
using UnityEngine.UI;

// Reads the same server-confirmed state used by gameplay. It never awards progress.
public class ObjectiveHUD : MonoBehaviour
{
    Canvas canvas;
    Text objective;
    Text clues;
    Text puzzle;
    Text instruction;
    Image[] clueMarks;
    Image[] roundMarks;
    Text bluePad;
    Text orangePad;
    Font font;
    readonly Color accent = new Color(0.32f, 0.88f, 0.81f);
    readonly Color muted = new Color(0.53f, 0.59f, 0.65f);

    void Awake()
    {
        font = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 20);
        GameObject root = new GameObject("Mission HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = Rect(root.transform, "Objective panel", 28, 28, 350, 306);
        panel.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.045f, 0.065f, 0.9f);
        panel.GetComponent<Image>().raycastTarget = false;
        Bar(panel, "Accent", 0, 0, 4, 306, accent);
        Label(panel, "TEAM OBJECTIVE", 20, 16, 310, 23, 14, accent);
        objective = Label(panel, "", 20, 43, 310, 54, 21, Color.white);
        clues = Label(panel, "", 20, 106, 310, 25, 18, Color.white);
        clueMarks = Marks(panel, "Clue", 140);
        puzzle = Label(panel, "", 20, 162, 310, 25, 18, Color.white);
        roundMarks = Marks(panel, "Round", 196);
        bluePad = Label(panel, "", 20, 216, 150, 24, 15, muted);
        orangePad = Label(panel, "", 175, 216, 155, 24, 15, muted);
        instruction = Label(panel, "", 20, 248, 310, 48, 15, muted);
        canvas.enabled = false;
    }

    void Update()
    {
        ClueManager manager = GetComponent<ClueManager>();
        NetworkClient network = NetworkClient.Instance;
        bool playing = manager != null && network != null &&
            (network.offlineMode || network.GameStarted);
        canvas.enabled = playing && !(manager.IsEnding && manager.endingPanel != null);
        if (!canvas.enabled) return;

        CoopShapePuzzle lockPuzzle = CoopShapePuzzle.Instance;
        int count = Mathf.Clamp(manager.clueCount, 0, 3);
        bool solved = lockPuzzle != null && lockPuzzle.IsSolved;
        int completed = lockPuzzle == null ? 0 : lockPuzzle.CompletedRounds;
        int rounds = lockPuzzle == null ? 3 : lockPuzzle.TotalRounds;
        clues.text = "공유 단서    " + count + " / 3";
        puzzle.text = solved ? "협동 잠금 장치    해제 완료" : "협동 잠금 장치    " + completed + " / " + rounds;
        for (int i = 0; i < 3; i++)
        {
            clueMarks[i].color = i < count ? accent : new Color(0.17f, 0.22f, 0.27f);
            roundMarks[i].color = i < completed ? accent : new Color(0.17f, 0.22f, 0.27f);
        }
        bool blue = lockPuzzle != null && lockPuzzle.BluePadOccupied;
        bool orange = lockPuzzle != null && lockPuzzle.OrangePadOccupied;
        bluePad.text = solved ? "잠긴 방 개방" : "파란 발판 · " + (blue ? "준비" : "대기");
        orangePad.text = solved ? "" : "주황 발판 · " + (orange ? "준비" : "대기");
        bluePad.color = blue || solved ? new Color(0.3f, 0.75f, 1f) : muted;
        orangePad.color = orange ? new Color(1f, 0.66f, 0.3f) : muted;

        if (manager.IsEnding)
        {
            objective.text = "임무 완료";
            instruction.text = "모든 단서를 모았습니다.";
        }
        else if (count >= 3)
        {
            objective.text = "모든 단서 확보";
            instruction.text = "임무 완료를 확인하고 있습니다.";
        }
        else if (solved)
        {
            objective.text = "남은 단서를 찾으세요";
            instruction.text = "잠긴 방이 열렸습니다.\n방 안과 맵의 단서를 모두 수집하세요.";
        }
        else if (blue && orange)
        {
            objective.text = "동료와 도형을 맞추세요";
            instruction.text = network.playerId == 1
                ? "도형의 모양·색·중앙 표시를\n동료에게 설명하세요."
                : "동료의 설명을 듣고\n숫자 1~4로 도형을 선택하세요.";
        }
        else
        {
            objective.text = count >= 2 ? "협동 장치로 방을 여세요" : "단서를 찾고 잠금을 해제하세요";
            if (network.offlineMode)
                instruction.text = "협동 장치는 두 플레이어가 필요합니다.\n단서 1개는 잠긴 방 안에 있습니다.";
            else if (lockPuzzle != null && network.localPlayerTransform != null)
            {
                float distance = Vector3.Distance(network.localPlayerTransform.position,
                    lockPuzzle.GetAssignedPad(network.playerId));
                instruction.text = (network.playerId == 1 ? "파란" : "주황") + " 발판까지 " + distance.ToString("F0") +
                    "m\n두 사람이 각자 발판에 올라가세요.";
            }
            else instruction.text = "단서 1개는 잠긴 방 안에 있습니다.";
        }
    }

    Image[] Marks(Transform parent, string name, float y)
    {
        Image[] result = new Image[3];
        for (int i = 0; i < 3; i++) result[i] = Bar(parent, name + i, 20 + i * 105, y, 98, 5, muted);
        return result;
    }

    static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    static Image Bar(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        Image image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text Label(Transform parent, string value, float x, float y, float w, float h, int size, Color color)
    {
        Text label = Rect(parent, "Label", x, y, w, h).gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = size;
        label.color = color;
        label.text = value;
        label.raycastTarget = false;
        label.supportRichText = false;
        return label;
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
        if (font != null) Destroy(font);
    }
}
