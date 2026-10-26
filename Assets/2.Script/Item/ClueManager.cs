using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ClueManager : MonoBehaviour
{
    public static ClueManager instance;

    [Header("Clue")]
    public int clueCount = 0;
    public int needClueCount = 3;

    [Header("UI")]
    public Image clueIcon;
    public TextMeshProUGUI clueCountText;

    [Header("Ending")]
    public GameObject endingPanel;

    private bool isEnding = false;
    private bool canRestartTogether = true;
    private bool waitingForRestart;
    private string resultTitle = "임무 완료";
    private GUIStyle resultBoxStyle;
    private GUIStyle resultLabelStyle;
    private GUIStyle resultButtonStyle;
    private string cluePickupNotice;
    private float cluePickupNoticeUntil;
    private readonly bool[] recordedClues = new bool[4];
    private readonly List<int> clueOrder = new List<int>();
    private Canvas evidenceCanvas;
    private Font evidenceFont;
    private Text evidenceText;
    private Canvas victoryCanvas;
    private Image victoryFade;
    private CanvasGroup victoryQuestion;
    private CanvasGroup victoryReport;
    private Text victoryStatus;
    private Button victoryRestart;
    private float victoryStartedAt;
    private float offlineStartedAt = -1f;
    private Text resultQuestionText;
    private Text resultHeadingText;
    private Text resultDescriptionText;
    private Text resultClueHeadingText;
    private readonly Text[] resultEvidenceLines = new Text[3];
    private Image resultAccent;
    private bool journalOpen;
    public bool IsEnding => isEnding;
    public bool IsVictory { get; private set; }

    void Awake()
    {
        instance = this;
        GamePresentation.Ensure();

        if (clueIcon != null)
        {
            clueIcon.enabled = true;
            Color color = clueIcon.color;
            color.a = 0.4f;
            clueIcon.color = color;
        }

        RefreshClueUI();

        if (endingPanel != null)
            endingPanel.SetActive(false);

        if (GetComponent<ObjectiveHUD>() == null) gameObject.AddComponent<ObjectiveHUD>();
        // The mission HUD owns the shared clue counter.
        if (clueIcon != null) clueIcon.enabled = false;
        if (clueCountText != null) clueCountText.enabled = false;
        CreateEvidenceJournal();
    }

    void Update()
    {
        NetworkClient network = NetworkClient.Instance;
        if (!isEnding && network != null && network.GameStarted && Input.GetKeyDown(KeyCode.J))
            journalOpen = !journalOpen;
        if (evidenceCanvas != null)
            evidenceCanvas.enabled = network != null && network.GameStarted &&
                journalOpen && !isEnding;
        if (!isEnding && network != null && network.offlineMode && network.localPlayerTransform != null)
        {
            if (offlineStartedAt < 0f) offlineStartedAt = Time.unscaledTime;
            PlayerController localPlayer = network.localPlayerTransform.GetComponent<PlayerController>();
            if (localPlayer != null && localPlayer.IsDeadState)
                ShowDefeat("ALL_DOWN");
            else if (Time.unscaledTime - offlineStartedAt >= 600f)
                ShowDefeat("TIME");
        }
        if (victoryCanvas != null && victoryCanvas.enabled)
            UpdateVictoryPresentation();
    }

    public void RecordClue(int itemId)
    {
        if (itemId < 1 || itemId > 3 || recordedClues[itemId]) return;
        recordedClues[itemId] = true;
        clueOrder.Add(itemId);
        RefreshEvidenceJournal();
    }

    public void ShowCluePickupNotice(int itemId, int pickedPlayerId, int sharedCount)
    {
        if (itemId < 1 || itemId > 3) return;
        NetworkClient network = NetworkClient.Instance;
        string who = network != null && pickedPlayerId == network.playerId ? "단서 획득" : "동료가 단서 획득";
        cluePickupNotice = who + " · " + EvidenceNames[itemId] + "  (" + sharedCount + " / " + needClueCount + ")";
        cluePickupNoticeUntil = Time.unscaledTime + 2.5f;
    }

    static readonly string[] EvidenceNames = {
        "", "현장 문서", "녹음 기록", "봉인된 증거"
    };
    static readonly string[] EvidenceDetails = {
        "",
        "잠긴 방의 장치는 두 발판에 한 명씩 서야 작동한다.",
        "문양은 외곽 모양, 색, 중앙 표시의 순서로 복잡해진다.",
        "마지막 기록은 협동 장치로 연 방 안에서 발견됐다."
    };

    void CreateEvidenceJournal()
    {
        evidenceFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Arial" }, 18);
        GameObject root = new GameObject("Evidence Journal", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        evidenceCanvas = root.GetComponent<Canvas>();
        evidenceCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        evidenceCanvas.sortingOrder = 30;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = new GameObject("Journal panel", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(root.transform, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-28f, -28f);
        panel.sizeDelta = new Vector2(430f, 340f);
        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.025f, 0.045f, 0.065f, 0.95f);
        background.raycastTarget = false;
        evidenceText = new GameObject("Evidence list", typeof(RectTransform)).AddComponent<Text>();
        evidenceText.transform.SetParent(panel, false);
        RectTransform textRect = (RectTransform)evidenceText.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(20f, 16f);
        textRect.offsetMax = new Vector2(-20f, -16f);
        evidenceText.font = evidenceFont;
        evidenceText.fontSize = 17;
        evidenceText.lineSpacing = 1.25f;
        evidenceText.color = Color.white;
        evidenceText.raycastTarget = false;
        evidenceText.supportRichText = false;
        evidenceText.horizontalOverflow = HorizontalWrapMode.Wrap;
        evidenceText.verticalOverflow = VerticalWrapMode.Overflow;
        RefreshEvidenceJournal();
        evidenceCanvas.enabled = false;
    }

    void RefreshEvidenceJournal()
    {
        if (evidenceText == null) return;
        System.Text.StringBuilder text = new System.Text.StringBuilder("공유 수집 기록  [J]\n");
        text.Append("확보한 단서: ").Append(clueOrder.Count).Append(" / 3\n\n");
        if (clueOrder.Count == 0) text.Append("아직 확보한 단서가 없습니다.\n");
        foreach (int id in clueOrder)
            text.Append(clueOrder.IndexOf(id) + 1).Append(". ").Append(EvidenceNames[id])
                .Append("\n   ").Append(EvidenceDetails[id]).Append("\n\n");
        evidenceText.text = text.ToString();
    }

    public void SetClueCount(int count, int needCount)
    {
        clueCount = count;
        needClueCount = needCount;

        Debug.Log("서버 단서 개수 반영: " + clueCount + " / " + needClueCount);
        RefreshClueUI();
    }

    private void RefreshClueUI()
    {
        if (clueIcon != null)
        {
            Color color = clueIcon.color;
            color.a = clueCount > 0 ? 1.0f : 0.4f;
            clueIcon.color = color;
        }

        if (clueCountText != null)
            clueCountText.text = clueCount + " / " + needClueCount;
    }

    // 서버가 없는 단독 테스트용 fallback.
    // 멀티 서버 권한 구조에서는 서버의 S_CLUE_COUNT 패킷으로 SetClueCount가 호출된다.
    public void AddClue()
    {
        if (isEnding)
            return;

        clueCount++;
        Debug.Log("ClueManager :: 단서 획득 성공! 현재 개수: " + clueCount);

        RefreshClueUI();

        if (clueCount >= needClueCount)
            ShowEnding();
    }

    public void ShowEnding()
    {
        if (isEnding)
            return;

        isEnding = true;
        IsVictory = true;
        resultTitle = "임무 완료";
        Debug.Log("엔딩 화면 활성화");
        if (endingPanel != null)
            endingPanel.SetActive(false);
        CreateVictoryPresentation();
        victoryStartedAt = Time.unscaledTime;
        UpdateVictoryActions();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }
    public void ShowDefeat(string reason = "ALL_DOWN")
    {
        if (isEnding) return;
        isEnding = true;
        IsVictory = false;
        resultTitle = reason == "TIME" ? "임무 실패 · 제한 시간 종료" :
            "임무 실패 · 두 플레이어 모두 쓰러졌습니다";
        if (endingPanel != null) endingPanel.SetActive(false);
        CreateVictoryPresentation();
        ConfigureDefeatPresentation(reason);
        victoryStartedAt = Time.unscaledTime;
        UpdateVictoryActions();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }

    public void ShowConnectionLost(string reason)
    {
        isEnding = true;
        IsVictory = false;
        canRestartTogether = false;
        waitingForRestart = false;
        resultTitle = reason;
        if (endingPanel != null) endingPanel.SetActive(false);
        if (victoryCanvas != null) victoryCanvas.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }

    private void CreateVictoryPresentation()
    {
        if (victoryCanvas != null) { victoryCanvas.enabled = true; return; }

        GameObject root = new GameObject("Ending Report", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        victoryCanvas = root.GetComponent<Canvas>();
        victoryCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        victoryCanvas.sortingOrder = 100;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform shade = FullScreenRect(root.transform, "Fade to dark");
        victoryFade = shade.gameObject.AddComponent<Image>();
        victoryFade.color = new Color(0.015f, 0.025f, 0.035f, 0f);

        RectTransform question = FullScreenRect(root.transform, "Opening question");
        victoryQuestion = question.gameObject.AddComponent<CanvasGroup>();
        victoryQuestion.alpha = 0f;
        victoryQuestion.blocksRaycasts = false;
        resultQuestionText = AddText(question, "탈출한 건가?", 55, Color.white,
            0f, 0f, 900f, 110f, TextAnchor.MiddleCenter);
        resultQuestionText.rectTransform.anchorMin = resultQuestionText.rectTransform.anchorMax =
            resultQuestionText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        resultQuestionText.rectTransform.anchoredPosition = Vector2.zero;

        RectTransform report = FullScreenRect(root.transform, "Investigation report");
        victoryReport = report.gameObject.AddComponent<CanvasGroup>();
        victoryReport.alpha = 0f;
        victoryReport.interactable = false;
        victoryReport.blocksRaycasts = false;
        RectTransform card = new GameObject("Report card", typeof(RectTransform), typeof(Image))
            .GetComponent<RectTransform>();
        card.SetParent(report, false);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(820f, 650f);
        card.GetComponent<Image>().color = new Color(0.045f, 0.075f, 0.09f, 0.98f);

        resultAccent = AddBar(card, 0f, 0f, 7f, 650f, new Color(0.30f, 0.85f, 0.78f));
        AddText(card, "NOA  /  조사 기록", 21, new Color(0.40f, 0.88f, 0.81f),
            46f, 38f, 720f, 35f);
        resultHeadingText = AddText(card, "탈출한 건가?", 48, Color.white, 46f, 83f, 720f, 75f);
        resultDescriptionText = AddText(card, "단서 세 개를 확보했습니다. 그러나 이곳의 모든 진실이 밝혀진 것은 아닙니다.",
            22, new Color(0.78f, 0.84f, 0.85f), 48f, 173f, 720f, 58f);
        AddBar(card, 46f, 247f, 728f, 2f, new Color(0.22f, 0.42f, 0.44f));
        resultClueHeadingText = AddText(card, "확보한 단서    " + clueCount + " / " + needClueCount,
            24, Color.white, 46f, 264f, 720f, 40f);

        for (int i = 0; i < 3; i++)
        {
            int id = i < clueOrder.Count ? clueOrder[i] : i + 1;
            resultEvidenceLines[i] = AddText(card, (i + 1).ToString("00") + "   " + EvidenceNames[id] +
                "\n       " + EvidenceDetails[id], 20, new Color(0.78f, 0.91f, 0.89f),
                48f, 309f + i * 65f, 718f, 60f);
        }

        victoryStatus = AddText(card, "두 플레이어가 함께 재시작하거나 로비로 돌아갈 수 있습니다.",
            19, new Color(0.68f, 0.76f, 0.78f), 48f, 515f, 720f, 32f);
        victoryRestart = AddButton(card, "함께 재시작", 48f, 558f, 345f, 58f,
            new Color(0.20f, 0.55f, 0.51f));
        victoryRestart.onClick.AddListener(RequestVictoryRestart);
        Button lobbyButton = AddButton(card, "로비로 돌아가기", 425f, 558f, 345f, 58f,
            new Color(0.18f, 0.25f, 0.28f));
        lobbyButton.onClick.AddListener(ReturnFromVictoryToLobby);
    }

    private void ConfigureDefeatPresentation(string reason)
    {
        bool timedOut = reason == "TIME";
        bool offline = NetworkClient.Instance != null && NetworkClient.Instance.offlineMode;
        resultQuestionText.text = timedOut ? "시간이 끝났다." : "우리는 여기서 멈췄다.";
        resultHeadingText.text = "임무 실패";
        resultDescriptionText.text = timedOut
            ? "10분이 지났습니다. 확보한 단서를 확인하고 다시 도전하세요."
            : (offline ? "플레이어가 쓰러졌습니다. 확보한 단서를 확인하고 다시 도전하세요."
                : "두 플레이어가 모두 쓰러졌습니다. 확보한 단서를 확인하고 다시 도전하세요.");
        resultAccent.color = new Color(0.82f, 0.30f, 0.30f);
        resultClueHeadingText.text = "확보한 단서    " + clueCount + " / " + needClueCount;
        for (int i = 0; i < resultEvidenceLines.Length; i++)
        {
            int id = i < clueOrder.Count ? clueOrder[i] : 0;
            resultEvidenceLines[i].text = (i + 1).ToString("00") + "   " +
                (id == 0 ? "미확보 단서" : EvidenceNames[id] + "\n       " + EvidenceDetails[id]);
        }
    }

    private void UpdateVictoryPresentation()
    {
        float elapsed = Time.unscaledTime - victoryStartedAt;
        float darkness = Mathf.SmoothStep(0f, 0.96f, Mathf.Clamp01(elapsed / 2f));
        victoryFade.color = new Color(0.015f, 0.025f, 0.035f, darkness);
        victoryQuestion.alpha = Mathf.Clamp01((elapsed - 0.35f) / 0.7f) *
            (1f - Mathf.Clamp01((elapsed - 1.8f) / 0.6f));
        victoryReport.alpha = Mathf.Clamp01((elapsed - 2.3f) / 0.9f);
        bool ready = elapsed >= 3.2f;
        victoryReport.interactable = ready;
        victoryReport.blocksRaycasts = ready;
        UpdateVictoryActions();
    }

    private void UpdateVictoryActions()
    {
        if (victoryRestart == null) return;
        NetworkClient network = NetworkClient.Instance;
        victoryRestart.interactable = victoryReport.interactable && canRestartTogether &&
            !waitingForRestart && network != null && (network.IsConnected || network.offlineMode);
        victoryStatus.text = waitingForRestart ? "동료가 재시작을 누르기를 기다리는 중입니다." :
            canRestartTogether ? "두 플레이어가 함께 재시작하거나 로비로 돌아갈 수 있습니다." :
            "동료의 연결이 종료됐습니다. 로비로 돌아가세요.";
    }

    private void RequestVictoryRestart()
    {
        NetworkClient network = NetworkClient.Instance;
        if (network == null || waitingForRestart || !canRestartTogether) return;
        waitingForRestart = true;
        UpdateVictoryActions();
        network.RequestRestart();
    }

    private void ReturnFromVictoryToLobby()
    {
        NetworkClient network = NetworkClient.Instance;
        if (network != null) network.ReturnToLobby();
    }

    private static RectTransform FullScreenRect(Transform parent, string name)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private Text AddText(Transform parent, string value, int size, Color color,
        float x, float y, float width, float height, TextAnchor alignment = TextAnchor.UpperLeft)
    {
        RectTransform rect = new GameObject("Report text", typeof(RectTransform), typeof(Text))
            .GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        Text text = rect.GetComponent<Text>();
        text.font = evidenceFont;
        text.fontSize = size;
        text.color = color;
        text.text = value;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static Image AddBar(Transform parent, float x, float y, float width, float height, Color color)
    {
        RectTransform rect = new GameObject("Report accent", typeof(RectTransform), typeof(Image))
            .GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        Image image = rect.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private Button AddButton(Transform parent, string label, float x, float y,
        float width, float height, Color color)
    {
        RectTransform rect = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button))
            .GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.GetComponent<Image>().color = color;
        Button button = rect.GetComponent<Button>();
        Text text = AddText(rect, label, 22, Color.white, 0f, 0f, width, height, TextAnchor.MiddleCenter);
        text.raycastTarget = false;
        return button;
    }

    private void OnGUI()
    {
        if (resultBoxStyle == null)
        {
            resultBoxStyle = new GUIStyle(GUI.skin.box) { font = evidenceFont };
            resultLabelStyle = new GUIStyle(GUI.skin.label) { font = evidenceFont, wordWrap = true };
            resultButtonStyle = new GUIStyle(GUI.skin.button) { font = evidenceFont };
        }
        if (!isEnding && Time.unscaledTime < cluePickupNoticeUntil && !string.IsNullOrEmpty(cluePickupNotice))
            GUI.Box(new Rect((Screen.width - 480f) / 2f, 28f, 480f, 54f), cluePickupNotice, resultBoxStyle);
        if (!isEnding || (victoryCanvas != null && victoryCanvas.enabled)) return;
        NetworkClient network = NetworkClient.Instance;
        Rect panel = new Rect((Screen.width - 460f) / 2f, Screen.height - 225f, 460f, 210f);
        GUI.Box(panel, resultTitle, resultBoxStyle);
        if (network == null) return;
        GUI.Label(new Rect(panel.x + 20f, panel.y + 36f, 420f, 27f),
            "수집 단서  " + clueCount + " / " + needClueCount, resultLabelStyle);
        string guidance = waitingForRestart ? "동료가 재시작을 누르기를 기다리는 중입니다." :
            canRestartTogether ? "두 명 모두 '함께 재시작'을 누르거나 로비로 돌아가세요." :
            network.ConnectionStatus + "\n로비로 돌아가 다시 접속하세요.";
        GUI.Label(new Rect(panel.x + 20f, panel.y + 67f, 420f, 55f), guidance, resultLabelStyle);
        GUI.enabled = canRestartTogether && !waitingForRestart && (network.IsConnected || network.offlineMode);
        if (GUI.Button(new Rect(panel.x + 20f, panel.y + 135f, 195f, 46f), "함께 재시작", resultButtonStyle))
        {
            waitingForRestart = true;
            network.RequestRestart();
        }
        GUI.enabled = true;
        if (GUI.Button(new Rect(panel.x + 245f, panel.y + 135f, 195f, 46f), "로비로 돌아가기", resultButtonStyle))
            network.ReturnToLobby();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (evidenceCanvas != null) Destroy(evidenceCanvas.gameObject);
        if (victoryCanvas != null) Destroy(victoryCanvas.gameObject);
        if (evidenceFont != null) Destroy(evidenceFont);
    }
}
