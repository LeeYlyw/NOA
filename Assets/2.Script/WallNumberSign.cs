using TMPro;
using UnityEngine;

// Visual-only sign for Wall I 3. The wall mesh and all its colliders stay intact.
public sealed class WallNumberSign : MonoBehaviour
{
    [SerializeField] Vector3 localCenter = new Vector3(-5f, 3f, -4.96f);
    [SerializeField] Color plateColor = new Color(0.055f, 0.09f, 0.11f);

    int number = 1;
    Mesh signMesh;
    Material plateMaterial;

    public void SetNumber(int value) => number = Mathf.Max(1, value);

    void Start()
    {
        Vector3 center = ResolveWallCenter();
        signMesh = new Mesh { name = "Wall number plate quad" };
        signMesh.vertices = new[] {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
        };
        signMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        signMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        signMesh.RecalculateNormals();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        plateMaterial = MakeMaterial(shader, "Wall number plate", plateColor);
        AddQuad("Dark plate", center, new Vector2(2.7f, 1.05f), plateMaterial);

        string code = FormatNumber(number);
        GameObject label = new GameObject(code);
        label.transform.SetParent(transform, false);
        label.transform.localPosition = center + Vector3.forward * 0.012f;
        label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        label.transform.localScale = Vector3.one * 0.5f;
        TextMeshPro text = label.AddComponent<TextMeshPro>();
        text.text = code;
        text.fontSize = 4f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 2.2f;
        text.fontSizeMax = 4f;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(0.92f, 0.94f, 0.88f);
        text.rectTransform.sizeDelta = new Vector2(5f, 1.8f);
    }

    Vector3 ResolveWallCenter()
    {
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.name.StartsWith("SM_Wall")) continue;
            Bounds bounds = filter.sharedMesh.bounds;
            Matrix4x4 toRoot = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                Vector3 point = toRoot.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
            // The wall sits on the negative-Z edge of this module; positive Z
            // faces the walkable tile. Place the sign just outside that face.
            return new Vector3((min.x + max.x) * 0.5f, localCenter.y, max.z + 0.015f);
        }
        return localCenter;
    }

    static string FormatNumber(int value)
    {
        int group = (value - 1) / 10;
        int withinGroup = (value - 1) % 10 + 1;
        string letters = "";
        do
        {
            letters = (char)('A' + group % 26) + letters;
            group = group / 26 - 1;
        }
        while (group >= 0);
        return letters + "-" + withinGroup;
    }

    static Material MakeMaterial(Shader shader, string label, Color color)
    {
        Material material = new Material(shader) { name = label };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        return material;
    }

    void AddQuad(string label, Vector3 center, Vector2 size, Material material)
    {
        GameObject quad = new GameObject(label);
        quad.transform.SetParent(transform, false);
        quad.transform.localPosition = center;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        quad.AddComponent<MeshFilter>().sharedMesh = signMesh;
        quad.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    void OnDestroy()
    {
        if (signMesh != null) Destroy(signMesh);
        if (plateMaterial != null) Destroy(plateMaterial);
    }
}
