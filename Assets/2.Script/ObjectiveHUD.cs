using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

// Reads the same server-confirmed state used by gameplay. It never awards progress.
public class ObjectiveHUD : MonoBehaviour
{
    Canvas canvas;
    RectTransform objectivePanel;
    Text objective;
    Text clues;
    Text puzzle;
    Text power;
    Image[] powerMarks;
    Text instruction;
    Image[] clueMarks;
    Image[] roundMarks;
    Text bluePad;
    Text orangePad;
    Text teammateStatus;
    Text roleHint;
    Text timer;
    int displayedSeconds = -1;
    Font font;
    NavMeshPath roomPath;
    float nextRoomRouteUpdate;
    bool visibilityLogged;
    string roomRouteHint = "잠긴 방 위치를 확인하세요.";
    readonly Color accent = new Color(0.32f, 0.88f, 0.81f);
    readonly Color muted = new Color(0.53f, 0.59f, 0.65f);

    void Awake()
    {
        roomPath = new NavMeshPath();
        font = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 20);
        GameObject root = new GameObject("Mission HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440, 810);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = Rect(root.transform, "Objective panel", 28, 28, 350, 454);
        objectivePanel = panel;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 1);
        panel.anchoredPosition = new Vector2(-28, -28);
        panel.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.045f, 0.065f, 0.9f);
        panel.GetComponent<Image>().raycastTarget = false;
        Bar(panel, "Accent", 0, 0, 3, 454, accent);
        Label(panel, "현재 목표  ·  [J] 수집 기록", 20, 16, 310, 23, 14, accent);
        objective = Label(panel, "", 20, 43, 310, 54, 21, Color.white);
        instruction = Label(panel, "", 20, 99, 310, 54, 15, muted);
        power = Label(panel, "", 20, 166, 310, 25, 17, Color.white);
        powerMarks = Marks(panel, "Power", 197);
        puzzle = Label(panel, "", 20, 212, 310, 25, 17, Color.white);
        roundMarks = Marks(panel, "Round", 243);
        bluePad = Label(panel, "", 20, 254, 150, 24, 14, muted);
        orangePad = Label(panel, "", 175, 254, 155, 24, 14, muted);
        clues = Label(panel, "", 20, 289, 310, 25, 17, Color.white);
        clueMarks = Marks(panel, "Clue", 320);
        teammateStatus = Label(panel, "", 20, 338, 310, 70, 14, Color.white);
        roleHint = Label(panel, "", 20, 414, 310, 36, 14, muted);
        RectTransform timerPanel = Rect(root.transform, "Time limit", 0, 28, 174, 54);
        timerPanel.anchorMin = timerPanel.anchorMax = timerPanel.pivot = new Vector2(0.5f, 1f);
        timerPanel.anchoredPosition = new Vector2(0, -28);
        Image timerBackground = timerPanel.gameObject.AddComponent<Image>();
        timerBackground.color = new Color(0.025f, 0.045f, 0.065f, 0.9f);
        timerBackground.raycastTarget = false;
        timer = Label(timerPanel, "남은 시간  10:00", 10, 10, 154, 34, 20, Color.white);
        timer.alignment = TextAnchor.MiddleCenter;
        canvas.enabled = false;
        Debug.Log("[ObjectiveHUD] Created mission panel and top-center timer.");
    }

    void Update()
    {
        ClueManager manager = GetComponent<ClueManager>();
        NetworkClient network = NetworkClient.Instance;
        bool playing = manager != null && network != null &&
            (network.offlineMode || network.GameStarted);
        canvas.enabled = playing && !manager.IsEnding;
        if (!canvas.enabled) return;
        if (!visibilityLogged)
        {
            visibilityLogged = true;
            Debug.Log("[ObjectiveHUD] Visible; offline=" + network.offlineMode +
                ", gameStarted=" + network.GameStarted + ", initialSeconds=" + network.SecondsLeft);
        }
        objectivePanel.gameObject.SetActive(!manager.JournalOpen);

        int secondsLeft = network.offlineMode ? manager.OfflineSecondsLeft :
            network.SecondsLeft < 0 ? 600 : network.DisplaySecondsLeft;
        if (secondsLeft != displayedSeconds)
        {
            displayedSeconds = secondsLeft;
            timer.text = "남은 시간  " + (secondsLeft / 60).ToString("00") + ":" +
                (secondsLeft % 60).ToString("00");
            timer.color = secondsLeft <= 60 ? new Color(1f, 0.45f, 0.38f) : Color.white;
            timer.SetAllDirty();
            Canvas.ForceUpdateCanvases();
        }

        CoopShapePuzzle lockPuzzle = CoopShapePuzzle.Instance;
        int count = Mathf.Clamp(manager.clueCount, 0, 3);
        bool solved = lockPuzzle != null && lockPuzzle.IsSolved;
        PowerRestoration restoration = PowerRestoration.Instance;
        bool powered = restoration == null || restoration.IsComplete;
        int powerCount = restoration != null ? restoration.RepairedCount : 3;
        int heldFuses = restoration != null ? restoration.LocalHeldFuses : 0;
        power.text = restoration == null ? "전원 복구    해당 없음" : powered ? "전원 복구    완료" :
            $"전원 복구   {powerCount}/3   ·   퓨즈 {heldFuses}";
        int completed = lockPuzzle == null ? 0 : lockPuzzle.CompletedRounds;
        int rounds = lockPuzzle == null ? 3 : lockPuzzle.TotalRounds;
        clues.text = "공유 단서    " + count + " / 3";
        puzzle.text = solved ? "협동 잠금 장치    해제 완료" : "협동 잠금 장치    " + completed + " / " + rounds;
        for (int i = 0; i < 3; i++)
        {
            clueMarks[i].color = i < count ? accent : new Color(0.17f, 0.22f, 0.27f);
            roundMarks[i].color = i < completed ? accent : new Color(0.17f, 0.22f, 0.27f);
            powerMarks[i].color = i < powerCount ? accent : new Color(0.17f, 0.22f, 0.27f);
        }
        bool blue = lockPuzzle != null && lockPuzzle.BluePadOccupied;
        bool orange = lockPuzzle != null && lockPuzzle.OrangePadOccupied;
        bluePad.text = solved ? (powered ? "기록실 개방 조건 충족" : "전원 복구 필요") : "파란 발판 · " + (blue ? "준비" : "대기");
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
        else if (solved && !powered)
        {
            objective.text = "기록실 전원을 복구하세요";
            instruction.text = heldFuses > 0 ? "차단기에 퓨즈를 사용해 수리하세요.\n전원이 복구되면 기록실이 열립니다." :
                "퓨즈를 찾아 차단기를 수리하세요.\n잠금 장치는 이미 해제했습니다.";
        }
        else if (solved)
        {
            objective.text = "남은 단서를 찾으세요";
            if (lockPuzzle != null && network.localPlayerTransform != null &&
                Time.unscaledTime >= nextRoomRouteUpdate)
            {
                nextRoomRouteUpdate = Time.unscaledTime + 0.75f;
                roomRouteHint = GetLockedRoomRoute(network.localPlayerTransform.position,
                    lockPuzzle.LockedRoomApproach);
            }
            instruction.text = roomRouteHint + "\n남은 단서를 모두 찾으세요.";
        }
        else if (blue && orange)
        {
            objective.text = "동료와 도형을 맞추세요";
            int activeRound = Mathf.Clamp(completed + 1, 1, 3);
            string detail = activeRound == 1 ? "외곽 모양을" :
                activeRound == 2 ? "모양과 색을" : "모양·색·중앙 표시를";
            instruction.text = network.playerId == lockPuzzle.BluePadOwner
                ? detail + "\n동료에게 설명하세요."
                : network.playerId == lockPuzzle.OrangePadOwner
                    ? detail + " 듣고\n숫자 1~4로 선택하세요."
                    : "발판 위 동료들이 잠금 장치를 풉니다.\n주변을 지키고 단서를 찾으세요.";
        }
        else
        {
            objective.text = count >= 2 ? "협동 장치로 방을 여세요" : "단서를 찾고 잠금을 해제하세요";
            if (network.offlineMode)
                instruction.text = "파란 발판에서 문양을 확인한 뒤\n주황 발판에서 숫자 1~4로 선택하세요.";
            else if (lockPuzzle != null && network.localPlayerTransform != null)
            {
                int targetPad = blue ? 2 : orange ? 1 :
                    Vector3.Distance(network.localPlayerTransform.position, lockPuzzle.GetAssignedPad(1)) <=
                    Vector3.Distance(network.localPlayerTransform.position, lockPuzzle.GetAssignedPad(2)) ? 1 : 2;
                float distance = Vector3.Distance(network.localPlayerTransform.position,
                    lockPuzzle.GetAssignedPad(targetPad));
                instruction.text = (targetPad == 1 ? "파란" : "주황") + " 발판까지 " + distance.ToString("F0") +
                    "m\n둘이 올라 잠긴 방을 여세요.";
            }
            else instruction.text = "단서 1개는 잠긴 방 안에 있습니다.";
        }

        UpdateTeamStatus(network);
        if (!solved && !powered && !(blue && orange))
        {
            objective.text = "기록실을 개방하세요";
            instruction.text = "전원 복구와 협동 잠금 해제가 필요합니다.\n" +
                (heldFuses > 0 ? "보유한 퓨즈로 차단기를 수리할 수 있습니다." : "퓨즈를 찾고, 동료와 발판을 확인하세요.");
        }
    }

    string GetLockedRoomRoute(Vector3 player, Vector3 entrance)
    {
        Vector3 direction = entrance - player;
        float distance = new Vector2(direction.x, direction.z).magnitude;
        if (NavMesh.SamplePosition(player, out NavMeshHit start, 1.5f, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(entrance, out NavMeshHit end, 1.5f, NavMesh.AllAreas) &&
            NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, roomPath) &&
            roomPath.status == NavMeshPathStatus.PathComplete)
        {
            distance = 0f;
            Vector3[] corners = roomPath.corners;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 step = corners[i] - corners[i - 1];
                distance += new Vector2(step.x, step.z).magnitude;
                if ((corners[i - 1] - player).sqrMagnitude <= 1f)
                    direction = corners[i] - player;
            }
        }
        string heading = Mathf.Abs(direction.x) > Mathf.Abs(direction.z)
            ? (direction.x >= 0f ? "동쪽" : "서쪽")
            : (direction.z >= 0f ? "북쪽" : "남쪽");
        return "잠긴 방 " + heading + " · 약 " + Mathf.CeilToInt(distance) + "m";
    }

    void UpdateTeamStatus(NetworkClient network)
    {
        if (network.offlineMode)
            teammateStatus.text = "오프라인 테스트";
        else
        {
            System.Text.StringBuilder team = new System.Text.StringBuilder();
            for (int id = 1; id <= network.PlayerCount; id++)
            {
                if (id == network.playerId) continue;
                GameObject teammate = network.GetPlayerObject(id);
                PlayerController controller = teammate != null ? teammate.GetComponent<PlayerController>() : null;
                if (team.Length > 0) team.Append('\n');
                team.Append("동료 ").Append(id).Append(" · ");
                team.Append(controller == null ? "연결 대기" :
                    controller.IsDeadState ? "쓰러짐" :
                    controller.CurrentHp + " / " + controller.maxHealth + " HP");
            }
            teammateStatus.text = team.ToString();
        }
        teammateStatus.color = Color.white;

        PlayerRole role = network.GetRole(network.playerId);
        switch (role)
        {
            case PlayerRole.Detector: roleHint.text = "감지자 · 단서를 소리로 탐지"; break;
            case PlayerRole.Explorer: roleHint.text = "탐색자 · 단서를 눈으로 확인"; break;
            case PlayerRole.Healer:
                HealerSkill healer = network.localPlayerTransform != null
                    ? network.localPlayerTransform.GetComponent<HealerSkill>() : null;
                roleHint.text = healer != null && healer.RemainingCooldown > 0f
                    ? "치유자 · E 회복 " + healer.RemainingCooldown.ToString("F0") + "초 후"
                    : "치유자 · E 회복 (5m)";
                break;
            case PlayerRole.Runner: roleHint.text = "러너 · 빠른 이동과 높은 지구력"; break;
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
