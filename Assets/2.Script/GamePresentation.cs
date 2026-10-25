using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Presentation only: gameplay and network messages remain the source of truth.
public class GamePresentation : MonoBehaviour
{
    public static GamePresentation Instance { get; private set; }
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    CanvasGroup group;
    Text caption;
    AudioSource speaker;
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
    }

    public void Notify(string message, string sound)
    {
        // Show the latest result immediately instead of queueing stale puzzle hints.
        caption.text = message;
        noticeUntil = Time.unscaledTime + 2.8f;
        NotificationCount++;
        if (!string.IsNullOrEmpty(sound)) speaker.PlayOneShot(Tone(sound));
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
    }

    // Lightweight fallback sounds. Dedicated authored clips can override monster cues.
    public AudioClip Tone(string kind)
    {
        if (clips.TryGetValue(kind, out AudioClip cached)) return cached;
        const int rate = 22050;
        float duration = kind == "solved" ? 1.05f : kind == "alert" ? 0.65f : 0.3f;
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
            samples[i] = wave * envelope * 0.55f;
        }
        AudioClip clip = AudioClip.Create("Feedback " + kind, samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        clips.Add(kind, clip);
        return clip;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (AudioClip clip in clips.Values) if (clip != null) Destroy(clip);
        if (font != null) Destroy(font);
    }
}
