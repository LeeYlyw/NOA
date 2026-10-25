using UnityEngine;
using UnityEngine.AI;
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
    Text teammateStatus;
    Text roleHint;
    Font font;
    InventoryManager inventory;
    NavMeshPath roomPath;
    float nextRoomRouteUpdate;
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
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = Rect(root.transform, "Objective panel", 28, 28, 350, 382);
        panel.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.045f, 0.065f, 0.9f);
        panel.GetComponent<Image>().raycastTarget = false;
        Bar(panel, "Accent", 0, 0, 4, 382, accent);
        Label(panel, "TEAM OBJECTIVE", 20, 16, 310, 23, 14, accent);
        objective = Label(panel, "", 20, 43, 310, 54, 21, Color.white);
        clues = Label(panel, "", 20, 106, 310, 25, 18, Color.white);
        clueMarks = Marks(panel, "Clue", 140);
        puzzle = Label(panel, "", 20, 162, 310, 25, 18, Color.white);
        roundMarks = Marks(panel, "Round", 196);
        bluePad = Label(panel, "", 20, 216, 150, 24, 15, muted);
        orangePad = Label(panel, "", 175, 216, 155, 24, 15, muted);
        instruction = Label(panel, "", 20, 248, 310, 48, 15, muted);
        teammateStatus = Label(panel, "", 20, 306, 310, 30, 16, Color.white);
        roleHint = Label(panel, "", 20, 340, 310, 31, 14, muted);
        canvas.enabled = false;
    }

    void Update()
    {
        ClueManager manager = GetComponent<ClueManager>();
        NetworkClient network = NetworkClient.Instance;
        bool playing = manager != null && network != null &&
            (network.offlineMode || network.GameStarted);
        canvas.enabled = playing && !manager.IsEnding;
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
            instruction.text = network.playerId == 1
                ? detail + "\n동료에게 설명하세요."
                : detail + " 듣고\n숫자 1~4로 선택하세요.";
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
                    "m\n둘이 올라 잠긴 방을 여세요.";
            }
            else instruction.text = "단서 1개는 잠긴 방 안에 있습니다.";
        }

        UpdateTeamStatus(network);
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
        Transform teammate = network.remotePlayerTransform;
        PlayerController teammateController = teammate != null ? teammate.GetComponent<PlayerController>() : null;
        if (network.offlineMode || teammateController == null)
            teammateStatus.text = "동료 상태 · 연결 대기";
        else if (teammateController.IsDeadState)
        {
            if (inventory == null) inventory = FindObjectOfType<InventoryManager>();
            bool hasRevive = inventory != null && inventory.HasItemType(ItemData.ItemType.Resurrection);
            bool near = network.localPlayerTransform != null &&
                Vector3.Distance(network.localPlayerTransform.position, teammate.position) <= 5f;
            teammateStatus.text = hasRevive
                ? (near ? "동료 쓰러짐 · 부활 아이템 사용 가능" : "동료 쓰러짐 · 5m 이내 접근")
                : "동료 쓰러짐 · 부활 아이템 필요";
        }
        else
            teammateStatus.text = "동료 체력 · " + teammateController.CurrentHp + " / " + teammateController.maxHealth;
        teammateStatus.color = teammateController != null && teammateController.IsDeadState
            ? new Color(1f, 0.48f, 0.4f) : Color.white;

        PlayerRole role = network.playerId == 1 ? network.Role1 : network.Role2;
        switch (role)
        {
            case PlayerRole.Detector: roleHint.text = "감지자 · 단서 근처에서 신호음"; break;
            case PlayerRole.Explorer: roleHint.text = "탐색자 · 단서 획득 가능"; break;
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
