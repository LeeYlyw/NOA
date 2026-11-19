using UnityEngine;

// Shared, resolution-aware presentation. Interaction rules remain with each object.
public static class InteractionPrompt
{
    public static void Draw(string key, string message, float progress = -1f)
    {
        NetworkClient network = NetworkClient.Instance;
        if (network == null || network.localPlayerTransform == null ||
            (ClueManager.instance != null && ClueManager.instance.IsEnding)) return;
        PlayerController player = network.localPlayerTransform.GetComponent<PlayerController>();
        if (player == null || player.IsDeadState) return;
        if (PowerRestoration.Instance != null && PowerRestoration.Instance.BlocksJump) return;
        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1440f, Screen.height / 810f), 0.55f, 1.5f);
        Matrix4x4 matrix = GUI.matrix;
        Color color = GUI.color;
        // E power actions can coexist with an F interaction; reserve a second row.
        float row = key == "E" || (key.Length == 0 && PowerRestoration.Instance != null && PowerRestoration.Instance.UsesInteraction) ? 132 : 78;
        GUI.matrix = Matrix4x4.TRS(new Vector3(Screen.width / 2f, Screen.height / 2f + row * scale, 0),
            Quaternion.identity, new Vector3(scale, scale, 1));
        GUI.color = new Color(0.025f, 0.045f, 0.065f, 0.9f);
        GUI.DrawTexture(new Rect(-170, 0, 340, 46), Texture2D.whiteTexture);
        GUI.color = new Color(0.32f, 0.88f, 0.81f);
        GUI.DrawTexture(new Rect(-170, 0, 2, 46), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleLeft };
        style.normal.textColor = Color.white;
        if (!string.IsNullOrEmpty(key))
        {
            GUI.color = new Color(0.14f, 0.22f, 0.25f);
            GUI.DrawTexture(new Rect(-157, 7, 32, 32), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var keyStyle = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(-157, 7, 32, 32), key, keyStyle);
        }
        GUI.Label(new Rect(string.IsNullOrEmpty(key) ? -150 : -113, 3,
            string.IsNullOrEmpty(key) ? 300 : 270, 40), message, style);
        if (progress >= 0f)
        {
            GUI.color = new Color(0.32f, 0.88f, 0.81f);
            GUI.DrawTexture(new Rect(-170, 44, 340 * Mathf.Clamp01(progress), 2), Texture2D.whiteTexture);
        }
        GUI.color = color;
        GUI.matrix = matrix;
    }

    public static void Draw(string message)
    {
        bool hasKey = message.StartsWith("F");
        Draw(hasKey ? "F" : "", hasKey ? message.Substring(1).Trim() : message);
    }
}
