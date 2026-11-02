using UnityEngine;
using UnityEngine.UI;

public class VitalsHUDStyle : MonoBehaviour
{
    Slider hp;
    Slider stamina;
    Text hpText;
    Text staminaText;
    Font font;

    public void Bind(Slider health, Slider energy)
    {
        hp = health;
        stamina = energy;
        if (hp == null || stamina == null) return;
        if (font == null) font = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Arial" }, 18);
        hpText = Style(hp, "체력", new Color(0.9f, 0.28f, 0.32f), -28f);
        staminaText = Style(stamina, "스태미나", new Color(0.28f, 0.83f, 0.72f), -92f);
        Refresh();
    }

    Text Style(Slider slider, string title, Color tint, float y)
    {
        slider.interactable = false;
        RectTransform rect = slider.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(30f, y);
        rect.sizeDelta = new Vector2(300f, 52f);
        Image background = slider.GetComponent<Image>();
        if (background == null) background = slider.gameObject.AddComponent<Image>();
        background.color = new Color(0.025f, 0.045f, 0.065f, 0.92f);
        background.raycastTarget = false;
        Transform oldBackground = rect.Find("Background");
        if (oldBackground != null) oldBackground.gameObject.SetActive(false);
        Image fill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
        if (fill != null) { fill.color = tint; fill.raycastTarget = false; }
        RectTransform fillArea = slider.fillRect != null ? slider.fillRect.parent as RectTransform : null;
        if (fillArea != null)
        {
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = new Vector2(12f, 9f);
            fillArea.offsetMax = new Vector2(-12f, -33f);
        }
        MakeText(rect, title, 12f, TextAnchor.MiddleLeft);
        return MakeText(rect, "", 178f, TextAnchor.MiddleRight);
    }

    Text MakeText(RectTransform parent, string value, float x, TextAnchor alignment)
    {
        Text text = new GameObject("Vital text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = font;
        text.fontSize = 16;
        text.color = Color.white;
        text.text = value;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0f, 1f);
        text.rectTransform.pivot = new Vector2(0f, 1f);
        text.rectTransform.anchoredPosition = new Vector2(x, -3f);
        text.rectTransform.sizeDelta = new Vector2(110f, 24f);
        return text;
    }

    void LateUpdate() { Refresh(); }

    void Refresh()
    {
        if (hp != null && hpText != null)
            hpText.text = Mathf.CeilToInt(hp.value) + " / " + Mathf.CeilToInt(hp.maxValue);
        if (stamina != null && staminaText != null)
            staminaText.text = Mathf.CeilToInt(stamina.value) + " / " + Mathf.CeilToInt(stamina.maxValue);
    }

    void OnDestroy() { if (font != null) Destroy(font); }
}
