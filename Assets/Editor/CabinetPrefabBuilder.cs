using UnityEditor;
using UnityEngine;

// Builds the reusable cabinet asset from editable Unity primitives.
public static class CabinetPrefabBuilder
{
    private const string Folder = "Assets/5. Prefab/Props";
    private const string PrefabPath = Folder + "/SearchCabinet.prefab";
    private const string MaterialFolder = "Assets/5. Prefab/Props/CabinetMaterials";

    [MenuItem("NOA/Place Search Cabinet In Room313")]
    public static void PlaceInRoom313()
    {
        const string roomPath = "Assets/5. Prefab/Room/Room313.prefab";
        GameObject cabinet = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (cabinet == null) { Build(); cabinet = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
        GameObject room = PrefabUtility.LoadPrefabContents(roomPath);
        try
        {
            if (room.transform.Find("SearchCabinet") != null) return;
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(cabinet, room.transform);
            instance.name = "SearchCabinet";
            instance.transform.localPosition = new Vector3(-14.3f, 0f, 0f);
            instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            PrefabUtility.SaveAsPrefabAsset(room, roomPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[CabinetPrefabBuilder] Placed cabinet inside Room313.");
        }
        finally { PrefabUtility.UnloadPrefabContents(room); }
    }

    public static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new System.InvalidOperationException("Cabinet prefab is missing.");
        CabinetInteraction cabinet = prefab.GetComponent<CabinetInteraction>();
        if (cabinet == null || cabinet.leftHinge == null || cabinet.rightHinge == null ||
            cabinet.rewardPoint == null || prefab.GetComponentsInChildren<Renderer>(true).Length < 20)
            throw new System.InvalidOperationException("Cabinet model or interaction references are incomplete.");
        GameObject room = PrefabUtility.LoadPrefabContents("Assets/5. Prefab/Room/Room313.prefab");
        try
        {
            CabinetInteraction[] placed = room.GetComponentsInChildren<CabinetInteraction>(true);
            if (placed.Length != 1 || placed[0].transform.parent != room.transform ||
                Vector3.Distance(placed[0].transform.localPosition, new Vector3(-14.3f, 0f, 0f)) > 0.01f)
                throw new System.InvalidOperationException("Room313 cabinet placement is incorrect.");
            Debug.Log("[CabinetPrefabBuilder] PASS prefab geometry, doors, reward point, Room313 placement.");
        }
        finally { PrefabUtility.UnloadPrefabContents(room); }
    }

    [MenuItem("NOA/Build Search Cabinet")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/5. Prefab", "Props");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(Folder, "CabinetMaterials");

        Material frame = MakeMaterial("CabinetFrame", new Color(0.12f, 0.17f, 0.19f), 0.65f, 0.28f);
        Material door = MakeMaterial("CabinetDoor", new Color(0.24f, 0.32f, 0.34f), 0.5f, 0.22f);
        Material edge = MakeMaterial("CabinetEdge", new Color(0.40f, 0.48f, 0.47f), 0.7f, 0.38f);
        Material dark = MakeMaterial("CabinetInset", new Color(0.045f, 0.075f, 0.085f), 0.45f, 0.15f);
        Material accent = MakeMaterial("CabinetAccent", new Color(0.12f, 0.72f, 0.66f), 0.3f, 0.6f, true);

        GameObject root = new GameObject("SearchCabinet");
        BoxCollider body = root.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, 1.1f, 0f);
        body.size = new Vector3(1.34f, 2.2f, 0.62f);
        CabinetInteraction interaction = root.AddComponent<CabinetInteraction>();

        Cube("Back", root.transform, new Vector3(0f, 1.1f, -0.29f), new Vector3(1.32f, 2.14f, 0.06f), frame);
        Cube("Left Side", root.transform, new Vector3(-0.64f, 1.1f, 0f), new Vector3(0.08f, 2.16f, 0.62f), frame);
        Cube("Right Side", root.transform, new Vector3(0.64f, 1.1f, 0f), new Vector3(0.08f, 2.16f, 0.62f), frame);
        Cube("Top", root.transform, new Vector3(0f, 2.18f, 0f), new Vector3(1.34f, 0.08f, 0.64f), edge);
        Cube("Bottom", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(1.34f, 0.08f, 0.64f), edge);
        Cube("Shelf", root.transform, new Vector3(0f, 0.93f, -0.03f), new Vector3(1.20f, 0.05f, 0.50f), edge);
        Cube("Inner Back", root.transform, new Vector3(0f, 1.45f, -0.24f), new Vector3(1.17f, 1.30f, 0.012f), dark);
        Cube("Base Plinth", root.transform, new Vector3(0f, 0.10f, 0.30f), new Vector3(1.18f, 0.15f, 0.05f), dark);

        Transform left = Hinge("Left Door Hinge", root.transform, new Vector3(-0.60f, 0f, 0.34f));
        Transform right = Hinge("Right Door Hinge", root.transform, new Vector3(0.60f, 0f, 0.34f));
        interaction.leftHinge = left;
        interaction.rightHinge = right;
        BuildDoor(left, true, door, edge, dark, accent);
        BuildDoor(right, false, door, edge, dark, accent);

        GameObject rewardPoint = new GameObject("Item Position");
        rewardPoint.transform.SetParent(root.transform, false);
        rewardPoint.transform.localPosition = new Vector3(0f, 1.08f, 0.05f);
        interaction.rewardPoint = rewardPoint.transform;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        if (prefab == null) throw new System.InvalidOperationException("Could not save SearchCabinet prefab.");
        Debug.Log("[CabinetPrefabBuilder] Created " + PrefabPath);
    }

    private static void BuildDoor(Transform hinge, bool left, Material door, Material edge,
        Material dark, Material accent)
    {
        float sign = left ? 1f : -1f;
        Cube("Door Panel", hinge, new Vector3(sign * 0.30f, 1.12f, 0f),
            new Vector3(0.60f, 2.08f, 0.06f), door);
        Cube("Inset", hinge, new Vector3(sign * 0.30f, 1.16f, 0.036f),
            new Vector3(0.47f, 1.63f, 0.012f), dark);
        Cube("Top Brace", hinge, new Vector3(sign * 0.30f, 2.02f, 0.05f),
            new Vector3(0.52f, 0.06f, 0.025f), edge);
        Cube("Lower Brace", hinge, new Vector3(sign * 0.30f, 0.31f, 0.05f),
            new Vector3(0.52f, 0.06f, 0.025f), edge);
        for (int i = 0; i < 4; i++)
            Cube("Vent " + (i + 1), hinge, new Vector3(sign * 0.30f, 0.51f + i * 0.075f, 0.052f),
                new Vector3(0.34f, 0.023f, 0.026f), edge);
        Cube("Handle Mount", hinge, new Vector3(sign * 0.52f, 1.16f, 0.075f),
            new Vector3(0.085f, 0.36f, 0.08f), edge);
        Cube("Handle Grip", hinge, new Vector3(sign * 0.52f, 1.16f, 0.13f),
            new Vector3(0.055f, 0.25f, 0.045f), dark);
        Cube("Status Mark", hinge, new Vector3(sign * 0.30f, 1.84f, 0.055f),
            new Vector3(0.21f, 0.035f, 0.015f), accent);
    }

    private static Transform Hinge(string name, Transform parent, Vector3 localPosition)
    {
        GameObject hinge = new GameObject(name);
        hinge.transform.SetParent(parent, false);
        hinge.transform.localPosition = localPosition;
        return hinge.transform;
    }

    private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = position;
        cube.transform.localScale = scale;
        Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        return cube;
    }

    private static Material MakeMaterial(string name, Color color, float metallic, float smoothness,
        bool emissive = false)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new System.InvalidOperationException("URP Lit shader is unavailable.");
        Material material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emissive)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.5f);
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
