using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class Monster2PrefabBuilder
{
    public static void Build()
    {
        const string modelPath = "Assets/3.Model/monster2.fbx";
        const string controllerPath = "Assets/4.Anim/Monster2/Monster2.controller";
        const string prefabPath = "Assets/5. Prefab/play/Monster2.prefab";
        const string scenePath = "Assets/1.Scenes/MapGeneration_Test.unity";

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
        if (model == null || controller == null)
            throw new System.InvalidOperationException("Monster2 model or animator controller is missing.");

        GameObject root = Object.Instantiate(model);
        root.name = "Monster2";
        Animator animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        CapsuleCollider collider = root.GetComponent<CapsuleCollider>();
        if (collider == null) collider = root.AddComponent<CapsuleCollider>();
        collider.radius = 0.5f;
        collider.height = 2f;
        collider.center = Vector3.up;

        NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
        if (agent == null) agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.5f;
        agent.height = 2f;
        agent.speed = 3.5f;
        agent.acceleration = 8f;
        agent.angularSpeed = 120f;

        SoundMonsterAI ai = root.GetComponent<SoundMonsterAI>();
        if (ai == null) ai = root.AddComponent<SoundMonsterAI>();
        ai.agent = agent;
        ai.hearingMultiplier = 2f;
        ai.chaseNoiseThreshold = 7f;
        ai.sightRange = 6f;
        ai.attackRange = 2f;

        RemoteMonster remote = root.GetComponent<RemoteMonster>();
        if (remote == null) remote = root.AddComponent<RemoteMonster>();

        MonsterNetworkSetup setup = root.GetComponent<MonsterNetworkSetup>();
        if (setup == null) setup = root.AddComponent<MonsterNetworkSetup>();
        setup.monsterId = 2;
        setup.navMeshAgent = agent;
        setup.remoteMonster = remote;
        setup.aiScripts = new MonoBehaviour[] { ai };

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        if (prefab == null)
            throw new System.InvalidOperationException("Failed to save Monster2 prefab.");

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        MapGenerator generator = Object.FindFirstObjectByType<MapGenerator>();
        if (generator == null)
            throw new System.InvalidOperationException("MapGenerator was not found in MapGeneration_Test.");
        generator.monster2Prefab = prefab;
        EditorUtility.SetDirty(generator);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Monster2PrefabBuilder] Monster2 prefab and map assignment completed.");
    }
}
