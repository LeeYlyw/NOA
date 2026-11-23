using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Connects the two door leaves already placed in MapGeneration_Test.
[DisallowMultipleComponent]
public class SlidingDoorInteraction : MonoBehaviour
{
    [Min(1f)] public float interactionRange = 3f;
    [Min(0.05f)] public float slideDuration = 0.65f;
    [Min(0.1f)] public float slideDistance = 2.5f;
    [Range(0f, 1f)] public float openingVolume = 0.9f;
    public Transform leftLeaf;
    public Transform rightLeaf;
    public Transform frame;

    private Vector3 leftClosed;
    private Vector3 rightClosed;
    private Vector3 slideAxis;
    private Vector3 interactionPoint;
    private Coroutine motion;
    private AudioSource openingAudio;
    private AudioClip openingClip;
    private bool isOpen;
    public Vector3 NetworkAnchor => interactionPoint;
    private static int lastUseFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ConnectTestSceneDoor()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "MapGeneration_Test") return;
        Transform left = null, right = null, doorFrame = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "SM_Door_Left") left = root.transform;
            else if (root.name == "SM_Door_Right") right = root.transform;
            else if (root.name == "SM_Door_Frame") doorFrame = root.transform;
        }
        if (left == null || right == null || doorFrame == null)
        {
            Debug.LogWarning("[SlidingDoor] Test scene door leaves or frame are missing.");
            return;
        }
        GameObject controller = new GameObject("Sliding Door Interaction");
        SlidingDoorInteraction door = controller.AddComponent<SlidingDoorInteraction>();
        door.leftLeaf = left;
        door.rightLeaf = right;
        door.frame = doorFrame;
        door.Initialize();
    }

    private void Initialize()
    {
        leftClosed = leftLeaf.position;
        rightClosed = rightLeaf.position;
        Bounds leftBounds = VisualBounds(leftLeaf);
        Bounds rightBounds = VisualBounds(rightLeaf);
        slideAxis = rightBounds.center - leftBounds.center;
        slideAxis.y = 0f;
        if (slideAxis.sqrMagnitude < 0.01f) slideAxis = frame.right;
        slideAxis.Normalize();
        interactionPoint = (leftBounds.center + rightBounds.center) * 0.5f;
        // 두 문짝의 중앙에 3D 소리의 발음점을 둔다.
        transform.position = interactionPoint;
        openingClip = Resources.Load<AudioClip>("Audio/EscapeDoor_HeavySlide");
        if (openingClip == null)
            Debug.LogWarning("[SlidingDoor] Opening sound is missing from Resources/Audio.", this);
        openingAudio = gameObject.AddComponent<AudioSource>();
        openingAudio.playOnAwake = false;
        openingAudio.spatialBlend = 1f;
        openingAudio.rolloffMode = AudioRolloffMode.Logarithmic;
        openingAudio.minDistance = 3f;
        openingAudio.maxDistance = 18f;
        openingAudio.dopplerLevel = 0f;
        EnsureSolidCollider(leftLeaf, leftBounds);
        EnsureSolidCollider(rightLeaf, rightBounds);
    }

    private static Bounds VisualBounds(Transform leaf)
    {
        Renderer[] renderers = leaf.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(leaf.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void EnsureSolidCollider(Transform leaf, Bounds bounds)
    {
        if (leaf.GetComponentInChildren<Collider>(true) != null) return;
        BoxCollider collider = leaf.gameObject.AddComponent<BoxCollider>();
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
        {
            Vector3 corner = new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z);
            Vector3 local = leaf.InverseTransformPoint(corner);
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }
        collider.center = (min + max) * 0.5f;
        collider.size = max - min;
    }

    private bool CanUse()
    {
        NetworkClient network = NetworkClient.Instance;
        // 온라인 개방과 승리는 서버가 확정한다.
        if (network == null || network.localPlayerTransform == null ||
            (!network.offlineMode && (!network.IsConnected || !network.GameStarted)) ||
            (ClueManager.instance != null && ClueManager.instance.IsEnding)) return false;
        PlayerController player = network.localPlayerTransform.GetComponent<PlayerController>();
        if (player == null || player.IsDeadState) return false;
        Vector3 offset = network.localPlayerTransform.position - interactionPoint;
        offset.y = 0f;
        return offset.sqrMagnitude <= interactionRange * interactionRange;
    }

    private void Update()
    {
        // 단서·전원·협동 잠금 조건을 모두 충족해야 개방을 요청한다.
        if (!CanUse() || isOpen || !HasAllClues() || !Input.GetKeyDown(KeyCode.F) ||
            lastUseFrame == Time.frameCount ||
            ItemPickupInteraction.BlocksDoorInteraction || CassetteInteraction.BlocksOtherInteraction ||
            BarrelInteraction.BlocksOtherInteraction || BedInteraction.BlocksOtherInteraction ||
            ChairInteraction.BlocksDoorInteraction || CabinetInteraction.BlocksOtherInteraction) return;
        lastUseFrame = Time.frameCount;
        if (!NetworkClient.Instance.offlineMode)
        {
            NetworkClient.Instance.SendPropUse("ExitDoor", 1);
            return;
        }
        ApplyServerOpen();
    }

    public void ApplyServerOpen()
    {
        if (isOpen) return;
        isOpen = true;
        // 열림이 확정된 순간에만 묵직한 미닫이문 소리를 한 번 재생한다.
        if (openingClip != null) openingAudio.PlayOneShot(openingClip, openingVolume);
        if (motion != null) StopCoroutine(motion);
        motion = StartCoroutine(Slide());
    }

    private static bool HasAllClues()
    {
        ClueManager clues = ClueManager.instance;
        return clues != null && clues.needClueCount > 0 && clues.clueCount >= clues.needClueCount &&
            (CoopShapePuzzle.Instance == null || CoopShapePuzzle.Instance.IsSolved) &&
            (PowerRestoration.Instance == null || PowerRestoration.Instance.IsComplete);
    }

    private IEnumerator Slide()
    {
        Vector3 leftStart = leftLeaf.position, rightStart = rightLeaf.position;
        Vector3 leftTarget = leftClosed - slideAxis * (isOpen ? slideDistance : 0f);
        Vector3 rightTarget = rightClosed + slideAxis * (isOpen ? slideDistance : 0f);
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideDuration));
            leftLeaf.position = Vector3.LerpUnclamped(leftStart, leftTarget, t);
            rightLeaf.position = Vector3.LerpUnclamped(rightStart, rightTarget, t);
            yield return null;
        }
        leftLeaf.position = leftTarget;
        rightLeaf.position = rightTarget;
        motion = null;
        // 문이 실제로 다 열린 뒤 오프라인 엔딩을 보여 준다.
        if (isOpen && ClueManager.instance != null && NetworkClient.Instance != null &&
            NetworkClient.Instance.offlineMode)
            ClueManager.instance.ShowEnding();
    }

    private void OnGUI()
    {
        if (!CanUse() || isOpen || ItemPickupInteraction.BlocksDoorInteraction) return;
        // 부족한 동안에는 F 배지 없이 안내만 표시한다.
        if (HasAllClues()) InteractionPrompt.Draw("F", "문 열기");
        else
        {
            ClueManager clues = ClueManager.instance;
            string missing = clues == null || clues.clueCount < clues.needClueCount ? "단서 수집 필요" : "";
            if (PowerRestoration.Instance != null && !PowerRestoration.Instance.IsComplete)
                missing += (missing.Length > 0 ? " · " : "") + "전원 복구 필요";
            if (CoopShapePuzzle.Instance != null && !CoopShapePuzzle.Instance.IsSolved)
                missing += (missing.Length > 0 ? " · " : "") + "협동 잠금 해제 필요";
            InteractionPrompt.Draw("", missing);
        }
    }

    public static bool BlocksOtherInteraction => lastUseFrame == Time.frameCount;
}
