using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Presentation only: gameplay and network messages remain the source of truth.
public class GamePresentation : MonoBehaviour
{
    public static GamePresentation Instance { get; private set; }
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    CanvasGroup group;
    CanvasGroup damageGroup;
    float damageUntil;
    Texture2D damageVignette;
    RawImage damageImage;
    Texture2D sideWound, verticalWound;
    RawImage spatter, reflectedSpatter;
    CanvasGroup onset;
    float hitStrength;
    const float DamageFadeSeconds = 0.65f;
    Text caption;
    AudioSource speaker;
    AudioClip itemUseClip;
    Font font;
    float noticeUntil;
    int lastClues;
    bool ended;
    public int NotificationCount { get; private set; }

    public static GamePresentation Ensure()
    {
        if (Instance == null) new GameObject("Game presentation").AddComponent<GamePresentation>();
        return Instance;
    }

    void Awake()
    {
        Instance = this;
        speaker = gameObject.AddComponent<AudioSource>();
        speaker.playOnAwake = false;
        speaker.volume = 0.22f;
        itemUseClip = Resources.Load<AudioClip>("Audio/UI/ItemUse");
        GameObject ui = new GameObject("Event notice", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        ui.transform.SetParent(transform, false);
        ui.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        ui.GetComponent<Canvas>().sortingOrder = 25;
        CanvasScaler scaler = ui.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        group = ui.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0;
        GameObject box = new GameObject("Notice", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(ui.transform, false);
        RectTransform rect = box.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.anchoredPosition = new Vector2(0, 130);
        rect.sizeDelta = new Vector2(650, 65);
        box.GetComponent<Image>().color = new Color(0.025f, 0.055f, 0.065f, 0.9f);
        box.GetComponent<Image>().raycastTarget = false;
        GameObject text = new GameObject("Caption", typeof(RectTransform), typeof(Text));
        text.transform.SetParent(box.transform, false);
        RectTransform tr = text.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(20, 5); tr.offsetMax = new Vector2(-20, -5);
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 22);
        caption = text.GetComponent<Text>();
        caption.font = font; caption.fontSize = 22;
        caption.alignment = TextAnchor.MiddleCenter;
        caption.color = new Color(0.75f, 0.96f, 0.92f);
        caption.raycastTarget = false;

        GameObject damageUi = new GameObject("Damage feedback", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        damageUi.transform.SetParent(transform, false);
        damageUi.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        damageUi.GetComponent<Canvas>().sortingOrder = 90;
        CanvasScaler damageScaler = damageUi.GetComponent<CanvasScaler>();
        damageScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        damageScaler.referenceResolution = new Vector2(1920, 1080);
        damageGroup = damageUi.GetComponent<CanvasGroup>();
        damageGroup.alpha = 0f;
        damageGroup.interactable = false;
        damageGroup.blocksRaycasts = false;
        CreateDamageVignette(damageUi.transform);
    }

    void CreateDamageVignette(Transform parent)
    {
        const int resolution = 256;
        var neutral = new Color32[resolution * resolution];
        var side = new Color32[neutral.Length];
        var vertical = new Color32[neutral.Length];
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float u = (float)x / (resolution - 1);
            float v = (float)y / (resolution - 1);
            float cloud = Mathf.PerlinNoise(u * 9f + 3f, v * 7f + 4f);
            float grain = Mathf.PerlinNoise(u * 48f + 2f, v * 32f);
            float edge = Mathf.Max(Mathf.Abs(2f * u - 1f), Mathf.Abs(2f * v - 1f));
            Color tint = new Color(0.33f + 0.27f * grain, 0.022f, 0.034f);
            tint.a = Mathf.SmoothStep(0f, 0.55f, Mathf.InverseLerp(0.78f, 1.08f, edge + (cloud - 0.5f) * 0.13f));
            neutral[y * resolution + x] = tint;

            // A narrow impact on one edge, tapering towards the corners.
            float taper = Mathf.Clamp01(1f - Mathf.Abs(v - 0.52f) * 1.85f);
            float depth = taper * (0.042f + 0.11f * cloud + 0.022f * grain);
            float penetration = Mathf.Clamp01(1f - u / Mathf.Max(depth, 0.001f));
            tint.a = penetration * taper * (0.42f + 0.50f * grain);
            side[y * resolution + x] = tint;
            vertical[x * resolution + y] = tint;
        }
        damageVignette = MakeHitTexture(resolution, neutral);
        sideWound = MakeHitTexture(resolution, side);
        verticalWound = MakeHitTexture(resolution, vertical);
        RectTransform rect = new GameObject("Soft damage edges", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        RawImage image = rect.GetComponent<RawImage>();
        damageImage = image;
        image.texture = damageVignette;
        image.raycastTarget = false;
        onset = image.gameObject.AddComponent<CanvasGroup>();
        spatter = new GameObject("Impact spatter", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        spatter.transform.SetParent(parent, false);
        spatter.raycastTarget = false;
        spatter.texture = Resources.Load<Texture2D>("ImpactSpatter");
        reflectedSpatter = Instantiate(spatter, parent);
        reflectedSpatter.name = "Diffuse impact spatter";
    }

    static Texture2D MakeHitTexture(int resolution, Color32[] pixels)
    {
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
    public void FlashDamage(Vector2 direction = default, float severity = 0.1f)
    {
        damageUntil = Time.unscaledTime + DamageFadeSeconds;
        bool horizontal = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y);
        damageImage.texture = direction.sqrMagnitude < 0.0001f ? damageVignette : horizontal ? sideWound : verticalWound;
        bool flipX = horizontal && direction.x > 0f;
        bool flipY = !horizontal && direction.y > 0f;
        damageImage.uvRect = new Rect(flipX ? 1f : 0f, flipY ? 1f : 0f, flipX ? -1f : 1f, flipY ? -1f : 1f);
        hitStrength = Mathf.Lerp(0.68f, 1f, Mathf.Clamp01(severity * 3f));
        onset.alpha = 1f;
        // The initial pressure mark clears first. Individual wet flecks linger at the contact edge.
        bool diffuse = direction.sqrMagnitude < 0.0001f;
        spatter.enabled = spatter.texture != null;
        reflectedSpatter.enabled = spatter.enabled && diffuse;
        if (spatter.enabled)
        {
            Vector2 outward = diffuse ? new Vector2(-1f, -0.7f) : direction.normalized;
            outward /= Mathf.Max(Mathf.Abs(outward.x), Mathf.Abs(outward.y));
            RectTransform area = (RectTransform)spatter.transform.parent;
            RectTransform decal = spatter.rectTransform;
            float width = Mathf.Min(area.rect.width * 0.38f, area.rect.height * 0.58f);
            decal.sizeDelta = new Vector2(width, width * spatter.texture.height / spatter.texture.width);
            decal.anchorMin = decal.anchorMax = Vector2.one * 0.5f + outward * 0.49f;
            decal.pivot = new Vector2(0.12f, 0.30f);
            decal.anchoredPosition = Vector2.zero;
            decal.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-outward.y, -outward.x) * Mathf.Rad2Deg);
            decal.localScale = Vector3.one;
            spatter.color = new Color(1f, 1f, 1f, diffuse ? 0.60f : 1f);
            if (diffuse)
            {
                RectTransform mirror = reflectedSpatter.rectTransform;
                mirror.anchorMin = mirror.anchorMax = new Vector2(1f - decal.anchorMin.x, decal.anchorMin.y);
                mirror.pivot = decal.pivot;
                mirror.anchoredPosition = Vector2.zero;
                mirror.sizeDelta = decal.sizeDelta;
                mirror.localRotation = Quaternion.Euler(0f, 0f, -decal.localEulerAngles.z);
                mirror.localScale = new Vector3(-1f, 1f, 1f);
                reflectedSpatter.color = spatter.color;
            }
        }
        damageGroup.alpha = hitStrength;
    }

    public void Notify(string message, string sound)
    {
        // Show the latest result immediately instead of queueing stale puzzle hints.
        caption.text = message;
        noticeUntil = Time.unscaledTime + 2.8f;
        NotificationCount++;
        if (!string.IsNullOrEmpty(sound)) speaker.PlayOneShot(Tone(sound));
    }

    // Play only after the local player's pickup has actually been applied.
    public void PlayPickupSound()
    {
        speaker.PlayOneShot(Tone("pickup"));
    }

    public void PlayItemUseSound()
    {
        if (itemUseClip != null) speaker.PlayOneShot(itemUseClip, 1.4f);
    }

    void Update()
    {
        ClueManager manager = ClueManager.instance;
        if (manager != null)
        {
            if (manager.clueCount > lastClues && !manager.IsEnding)
                Notify("단서 확보 · " + manager.clueCount + " / " + manager.needClueCount + "  —  동료와 공유되었습니다", "clue");
            lastClues = manager.clueCount;
            if (manager.IsEnding && manager.IsVictory && !ended)
            {
                Notify("모든 단서 확보 · 임무 완료", "solved");
            }
            ended = manager.IsEnding;
        }
        float alpha = Time.unscaledTime < noticeUntil ? 1 : 0;
        group.alpha = Mathf.MoveTowards(group.alpha, alpha, Time.unscaledDeltaTime * 5);
        float remainingDamage = Mathf.Clamp01((damageUntil - Time.unscaledTime) / DamageFadeSeconds);
        if (damageGroup.alpha > 0f)
        {
            float age = 1f - remainingDamage;
            onset.alpha = Mathf.Exp(-6f * age);
            if (spatter.enabled)
                spatter.rectTransform.localScale = Vector3.one * (1f + 0.045f * (1f - Mathf.Exp(-12f * age)));
            damageGroup.alpha = ended ? 0f : hitStrength * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, remainingDamage * 1.35f));
        }
    }

    // Lightweight fallback sounds. Dedicated authored clips can override monster cues.
    public AudioClip Tone(string kind)
    {
        if (clips.TryGetValue(kind, out AudioClip cached)) return cached;
        const int rate = 22050;
        float duration = kind == "solved" ? 1.05f : kind == "alert" ? 0.65f :
            kind == "pickup" ? 0.22f : 0.3f;
        float[] samples = new float[(int)(rate * duration)];
        uint noiseState = 1027;
        float filteredNoise = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / rate, p = t / duration;
            float frequency = kind == "wrong" ? 140 : kind == "step" ? 65 : kind == "attack" ? 95 :
                kind == "alert" ? 110 + 170 * p : kind == "search" ? 90 + 30 * Mathf.Sin(p * 6) :
                kind == "pad" ? 440 : 520 + Mathf.Floor(p * 3) * 130;
            float envelope = Mathf.Min(t * 80, 1) * Mathf.Pow(1 - p, 2);
            float wave = Mathf.Sin(t * frequency * Mathf.PI * 2);
            noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
            float noise = (noiseState & 65535) / 32767.5f - 1f;
            filteredNoise = Mathf.Lerp(filteredNoise, noise, 0.12f);
            if (kind == "step") wave = wave * 0.45f + filteredNoise * 1.8f;
            else if (kind == "attack") wave = filteredNoise * 2f + wave * 0.2f;
            else if (kind == "alert" || kind == "search")
                wave = (wave + Mathf.Sin(t * frequency * 3.03f * Mathf.PI)) * 0.3f + filteredNoise;
            else if (kind == "pickup")
            {
                // A soft click followed by two rising notes for a quick acquisition cue.
                float note = p < 0.48f ? 659.25f : 880f;
                wave = Mathf.Sin(t * note * Mathf.PI * 2) * 0.7f +
                    filteredNoise * Mathf.Exp(-t * 90f) * 0.35f;
            }
            samples[i] = wave * envelope * 0.55f;
        }
        AudioClip clip = AudioClip.Create("Feedback " + kind, samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        clips.Add(kind, clip);
        return clip;
    }

    void OnDestroy()
    {
        if (damageVignette != null) Destroy(damageVignette);
        if (sideWound != null) Destroy(sideWound);
        if (verticalWound != null) Destroy(verticalWound);
        if (Instance == this) Instance = null;
        foreach (AudioClip clip in clips.Values) if (clip != null) Destroy(clip);
        if (font != null) Destroy(font);
    }
}
