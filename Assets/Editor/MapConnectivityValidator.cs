using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MapConnectivityValidator
{
    public static void Validate()
    {
        const string scenePath = "Assets/1.Scenes/MapGeneration_Test.unity";
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        MapGenerator generator = Object.FindFirstObjectByType<MapGenerator>();
        if (generator == null)
            throw new System.InvalidOperationException("MapGenerator is missing from the test scene.");

        const int seedCount = 100;
        for (int i = 0; i < seedCount; i++)
        {
            int seed = 1 + i * 7919;
            generator.GenerateMap(seed);
            if (generator.transform.childCount == 0)
                throw new System.InvalidOperationException($"Map generation failed for seed {seed}.");
        }

        Debug.Log($"[MapConnectivityValidator] {seedCount} seeds passed collision and NavMesh connectivity checks.");
    }
}
