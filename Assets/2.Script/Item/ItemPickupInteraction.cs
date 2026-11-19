using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// One local prompt handles every PotionItem. The server still decides whether
// the pickup succeeds; holding F only delays the existing request.
public class ItemPickupInteraction : MonoBehaviour
{
    private const float HoldSeconds = 1f;
    private static readonly HashSet<PotionItem> Nearby = new HashSet<PotionItem>();
    private static ItemPickupInteraction instance;

    private PlayerController localPlayer;
    private PotionItem focusedItem;
    private GameObject visualRoot;
    private Image progressRing;
    private Texture2D circleTexture;
    private Texture2D ringTexture;
    private Sprite circleSprite;
    private Sprite ringSprite;
    private Font font;
    private float heldSeconds;
    private bool casting;
    private bool waitForRelease;

    public static void Register(PotionItem item)
    {
        if (item == null) return;
        Nearby.Add(item);
        if (instance == null)
        {
            GameObject root = new GameObject("Item Pickup Interaction");
            instance = root.AddComponent<ItemPickupInteraction>();
        }
        if (item.NearbyLocalPlayer != null) instance.localPlayer = item.NearbyLocalPlayer;
    }

    public static void Unregister(PotionItem item)
    {
        Nearby.Remove(item);
    }

    public static bool BlocksDoorInteraction => instance != null && instance.FindClosestItem() != null;

    void Awake()
    {
        BuildPrompt();
    }

    void Update()
    {
        PotionItem candidate = FindClosestItem();
        bool gameplayReady = NetworkClient.Instance != null &&
            (NetworkClient.Instance.offlineMode ||
             (NetworkClient.Instance.GameStarted && NetworkClient.Instance.IsConnected));
        if (!gameplayReady || (ClueManager.instance != null && ClueManager.instance.IsEnding) ||
            Cursor.lockState != CursorLockMode.Locked)
            candidate = null;

        if (candidate != focusedItem)
        {
            focusedItem = candidate;
            ResetCast();
            if (Input.GetKey(KeyCode.F)) waitForRelease = true;
        }

        visualRoot.SetActive(false);
        if (candidate == null) return;
        if (!Input.GetKey(KeyCode.F))
        {
            ResetCast();
            waitForRelease = false;
        }
        else if (!waitForRelease)
        {
            if (Input.GetKeyDown(KeyCode.F)) casting = true;
            if (casting)
            {
                heldSeconds = Mathf.Min(HoldSeconds, heldSeconds + Time.deltaTime);
                if (heldSeconds >= HoldSeconds)
                {
                    candidate.TryPickup(localPlayer);
                    ResetCast();
                    waitForRelease = true;
                }
            }
        }
        progressRing.fillAmount = heldSeconds / HoldSeconds;
    }

    private PotionItem FindClosestItem()
    {
        if (localPlayer == null || !localPlayer.isLocalPlayer || localPlayer.IsDeadState)
            return null;
        PotionItem closest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (PotionItem item in Nearby)
        {
            if (item == null || !item.CanInteract(localPlayer)) continue;
            float distance = (item.transform.position - localPlayer.transform.position).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            closest = item;
        }
        return closest;
    }

    void OnGUI()
    {
        if (focusedItem != null && focusedItem.CanInteract(localPlayer) && Cursor.lockState == CursorLockMode.Locked)
            InteractionPrompt.Draw("F", "길게 눌러 획득", heldSeconds / HoldSeconds);
    }

    private void ResetCast()
    {
        heldSeconds = 0f;
        casting = false;
        if (progressRing != null) progressRing.fillAmount = 0f;
    }

    private void BuildPrompt()
    {
        GameObject canvasObject = new GameObject("Pickup Prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        visualRoot = canvasObject;

        circleSprite = MakeSprite(0f, 0.48f, out circleTexture);
        ringSprite = MakeSprite(0.37f, 0.47f, out ringTexture);
        RectTransform circle = CreateRect("Key", canvasObject.transform, new Vector2(76f, 76f), new Vector2(0f, -105f));
        Image background = circle.gameObject.AddComponent<Image>();
        background.sprite = circleSprite;
        background.color = new Color(0.025f, 0.055f, 0.065f, 0.88f);
        background.raycastTarget = false;

        Image track = CreateRect("Track", circle, new Vector2(76f, 76f), Vector2.zero).gameObject.AddComponent<Image>();
        track.sprite = ringSprite;
        track.color = new Color(0.43f, 0.55f, 0.57f, 0.9f);
        track.raycastTarget = false;

        progressRing = CreateRect("Hold Progress", circle, new Vector2(76f, 76f), Vector2.zero).gameObject.AddComponent<Image>();
        progressRing.sprite = ringSprite;
        progressRing.color = new Color(0.37f, 0.93f, 0.79f, 1f);
        progressRing.type = Image.Type.Filled;
        progressRing.fillMethod = Image.FillMethod.Radial360;
        progressRing.fillOrigin = (int)Image.Origin360.Top;
        progressRing.fillClockwise = true;
        progressRing.fillAmount = 0f;
        progressRing.raycastTarget = false;

        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 28);
        Text key = CreateRect("F", circle, new Vector2(50f, 50f), Vector2.zero).gameObject.AddComponent<Text>();
        key.font = font;
        key.fontSize = 31;
        key.fontStyle = FontStyle.Bold;
        key.alignment = TextAnchor.MiddleCenter;
        key.color = Color.white;
        key.text = "F";
        key.raycastTarget = false;

        visualRoot.SetActive(false);
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static Sprite MakeSprite(float inner, float outer, out Texture2D texture)
    {
        const int size = 128;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float radius = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f).magnitude;
                float alpha = Mathf.Clamp01((radius - inner) * size) * Mathf.Clamp01((outer - radius) * size);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        Nearby.Clear();
        if (circleSprite != null) Destroy(circleSprite);
        if (ringSprite != null) Destroy(ringSprite);
        if (circleTexture != null) Destroy(circleTexture);
        if (ringTexture != null) Destroy(ringTexture);
        if (font != null) Destroy(font);
    }
}
