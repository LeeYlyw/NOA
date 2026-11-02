using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    public GameObject inventoryPanel;
    private bool activeInventory;
    private InventoryManager inventory;
    private Text capacity;
    private Text[] slotNames;
    private Font font;
    private readonly Color ink = new Color(0.035f, 0.065f, 0.075f, 0.97f);
    private readonly Color mint = new Color(0.36f, 0.87f, 0.76f);

    void Start()
    {
        inventory = GetComponent<InventoryManager>();
        if (inventoryPanel == null) return;
        BuildPresentation();
        inventoryPanel.SetActive(false);
    }

    void Update()
    {
        NetworkClient network = NetworkClient.Instance;
        bool ending = ClueManager.instance != null && ClueManager.instance.IsEnding;
        bool playing = network != null && (network.offlineMode || (network.GameStarted && network.IsConnected));
        if (inventoryPanel == null || ending || !playing)
        {
            inventoryPanel?.SetActive(false);
            activeInventory = false;
            return;
        }
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            activeInventory = !activeInventory;
            inventoryPanel.SetActive(activeInventory);
            Cursor.lockState = activeInventory ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = activeInventory;
        }
        if (activeInventory) RefreshLabels();
    }

    void BuildPresentation()
    {
        RectTransform panel = inventoryPanel.GetComponent<RectTransform>();
        if (panel == null || inventory == null || inventory.slots == null) return;
        GridLayoutGroup oldGrid = inventoryPanel.GetComponent<GridLayoutGroup>();
        if (oldGrid != null) oldGrid.enabled = false;
        Image shade = inventoryPanel.GetComponent<Image>();
        if (shade != null) { shade.color = new Color(0.01f, 0.025f, 0.035f, 0.78f); shade.raycastTarget = true; }
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Arial" }, 20);

        RectTransform card = Rect("Inventory dossier", panel, new Vector2(780, 610), Vector2.zero);
        card.localScale = Vector3.one * Mathf.Min(1f, Screen.width / 900f, Screen.height / 700f);
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.color = ink;
        cardImage.raycastTarget = false;
        Bar(card, new Vector2(-383, 0), new Vector2(7, 610), mint);
        Label(card, "NOA   /   FIELD EQUIPMENT", new Vector2(0, 258), new Vector2(690, 36), 20, mint, TextAnchor.MiddleLeft);
        Label(card, "소지품", new Vector2(0, 205), new Vector2(690, 64), 42, Color.white, TextAnchor.MiddleLeft);
        capacity = Label(card, "", new Vector2(285, 205), new Vector2(120, 48), 20, mint, TextAnchor.MiddleRight);
        Bar(card, new Vector2(0, 160), new Vector2(690, 2), new Color(0.2f, 0.36f, 0.37f));
        Label(card, "아이템을 클릭해 사용하세요", new Vector2(0, -205), new Vector2(690, 32), 19,
            new Color(0.68f, 0.79f, 0.8f), TextAnchor.MiddleLeft);
        Label(card, "TAB  닫기", new Vector2(0, -260), new Vector2(690, 32), 17, mint, TextAnchor.MiddleRight);

        slotNames = new Text[inventory.slots.Length];
        for (int i = 0; i < inventory.slots.Length; i++)
        {
            Slot slot = inventory.slots[i];
            if (slot == null) continue;
            int column = i % 3, row = i / 3;
            RectTransform frame = Rect("Slot frame " + (i + 1), card, new Vector2(210, 132),
                new Vector2(-230 + column * 230, 65 - row * 148));
            Image frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.color = new Color(0.085f, 0.135f, 0.15f, 1f);
            frameImage.raycastTarget = false;
            RectTransform slotRect = slot.GetComponent<RectTransform>();
            slotRect.SetParent(frame, false);
            slotRect.anchorMin = slotRect.anchorMax = new Vector2(0.5f, 0.5f);
            slotRect.pivot = new Vector2(0.5f, 0.5f);
            slotRect.anchoredPosition = new Vector2(0, 12);
            slotRect.sizeDelta = new Vector2(78, 78);
            Image slotBackground = slot.GetComponent<Image>();
            if (slotBackground != null) slotBackground.color = new Color(0.12f, 0.20f, 0.21f, 1f);
            if (slot.iconImage != null)
            {
                slot.iconImage.preserveAspect = true;
                slot.iconImage.raycastTarget = false;
                RectTransform icon = slot.iconImage.rectTransform;
                icon.anchorMin = Vector2.zero; icon.anchorMax = Vector2.one;
                icon.offsetMin = new Vector2(8, 8); icon.offsetMax = new Vector2(-8, -8);
            }
            Label(frame, (i + 1).ToString("00"), new Vector2(-82, 44), new Vector2(34, 24), 15, mint, TextAnchor.MiddleLeft);
            slotNames[i] = Label(frame, "빈 슬롯", new Vector2(0, -48), new Vector2(190, 28), 16,
                new Color(0.68f, 0.79f, 0.8f), TextAnchor.MiddleCenter);
        }
        RefreshLabels();
    }

    void RefreshLabels()
    {
        if (inventory == null || slotNames == null) return;
        int occupied = 0;
        for (int i = 0; i < slotNames.Length; i++)
        {
            Slot slot = i < inventory.slots.Length ? inventory.slots[i] : null;
            if (slot == null || slotNames[i] == null) continue;
            bool filled = slot.item != null;
            if (filled) occupied++;
            string name = filled ? slot.item.itemName : "빈 슬롯";
            slotNames[i].text = string.IsNullOrEmpty(name) ? "아이템" : name;
            slotNames[i].color = filled ? Color.white : new Color(0.55f, 0.65f, 0.67f);
        }
        if (capacity != null) capacity.text = occupied + " / " + slotNames.Length;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    Text Label(Transform parent, string value, Vector2 position, Vector2 size, int fontSize,
        Color color, TextAnchor alignment)
    {
        RectTransform rect = Rect("Label", parent, size, position);
        Text label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = fontSize;
        label.text = value;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        return label;
    }

    static void Bar(Transform parent, Vector2 position, Vector2 size, Color color)
    {
        Image bar = Rect("Accent", parent, size, position).gameObject.AddComponent<Image>();
        bar.color = color;
        bar.raycastTarget = false;
    }

    void OnDestroy()
    {
        if (font != null) Destroy(font);
    }
}
