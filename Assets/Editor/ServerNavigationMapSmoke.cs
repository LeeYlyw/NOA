using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class ServerNavigationMapSmoke
{
    const string PendingKey = "NOA.ServerNavigationMapSmoke.Pending";
    static int playFrames;
    static bool running;

    static ServerNavigationMapSmoke()
    {
        EditorApplication.update += OnUpdate;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/1.Scenes/MapGeneration_Test.unity");
        NetworkClient network = UnityEngine.Object.FindFirstObjectByType<NetworkClient>();
        if (network == null) throw new Exception("Test scene has no NetworkClient");
        network.offlineMode = true;
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    static void OnUpdate()
    {
        if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying || running)
            return;
        if (++playFrames < 20) return;
        running = true;
        try
        {
            MapGenerator generator = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
            if (generator == null) throw new Exception("Test scene has no MapGenerator");
            generator.GenerateMap(522364);
            MethodInfo build = typeof(MapGenerator).GetMethod("BuildServerNavigationMask",
                BindingFlags.Instance | BindingFlags.NonPublic);
            string mask = (string)build.Invoke(generator, null);
            const int gridSize = 200;
            const float step = 1f;
            const float origin = -99.5f;
            if (string.IsNullOrEmpty(mask) || mask.Length != gridSize * gridSize)
                throw new Exception("Navigation mask is missing or has unexpected dimensions");
            int walkable = 0;
            foreach (char cell in mask) if (cell != '0') ++walkable;
            if (walkable < 100) throw new Exception("Navigation mask contains too few walkable cells");
            int blockedAdjacentPairs = 0;
            for (int z = 0; z < gridSize; z++)
            for (int x = 0; x < gridSize; x++)
            {
                int index = z * gridSize + x;
                int edge = Convert.ToInt32(mask[index].ToString(), 16);
                if (x + 1 < gridSize && edge != 0 && mask[index + 1] != '0' && (edge & 1) == 0)
                    ++blockedAdjacentPairs;
                if (z + 1 < gridSize && edge != 0 && mask[index + gridSize] != '0' && (edge & 4) == 0)
                    ++blockedAdjacentPairs;
            }
            if (blockedAdjacentPairs < 50)
                throw new Exception("Navigation mask does not represent maze walls");
            MonsterNetworkSetup[] monsters = UnityEngine.Object.FindObjectsByType<MonsterNetworkSetup>(
                FindObjectsSortMode.None);
            if (monsters.Length < 2) throw new Exception("Both monsters were not spawned");
            foreach (MonsterNetworkSetup monster in monsters)
                if (!NavMesh.SamplePosition(monster.transform.position, out _, 2f, NavMesh.AllAreas))
                    throw new Exception(monster.name + " spawned outside NavMesh");
            CheckGraphConnectivity(mask, monsters, gridSize, origin, step);
            GameObject gate = GameObject.Find("Sigil locked room gate");
            if (gate == null) throw new Exception("Puzzle gate is missing");
            Vector3 firstSpawn = monsters[0].monsterId == 1
                ? monsters[0].transform.position : monsters[1].transform.position;
            Vector3 secondSpawn = monsters[0].monsterId == 2
                ? monsters[0].transform.position : monsters[1].transform.position;
            string fixtureHeader = string.Format(CultureInfo.InvariantCulture,
                "522364|200|200|-99.5|-99.5|1|{0:F2}|{1:F2}|4.8|{2:F2}|{3:F2}|{4:F2}|{5:F2}|{6:F2}|{7:F2}",
                gate.transform.position.x, gate.transform.position.z,
                firstSpawn.x, firstSpawn.y, firstSpawn.z,
                secondSpawn.x, secondSpawn.y, secondSpawn.z);
            File.WriteAllText(Path.Combine(Application.dataPath, "../Library/CodexNavigationFixture.txt"),
                              fixtureHeader + "\n" + mask);
            generator.GenerateMap(522364);
            string repeatedMask = (string)build.Invoke(generator, null);
            if (mask != repeatedMask)
                throw new Exception("Same seed produced different navigation masks");
            foreach (int otherSeed in new[] { 12345, 98765 })
            {
                generator.GenerateMap(otherSeed);
                string otherMask = (string)build.Invoke(generator, null);
                if (string.IsNullOrEmpty(otherMask) || otherMask.Length != gridSize * gridSize)
                    throw new Exception("Navigation mask failed for seed " + otherSeed);
                CheckGraphConnectivity(otherMask,
                    UnityEngine.Object.FindObjectsByType<MonsterNetworkSetup>(FindObjectsSortMode.None),
                    gridSize, origin, step);
            }
            Debug.Log("[NAV_SMOKE] PASS seed=522364 walkable=" + walkable +
                      " blockedPairs=" + blockedAdjacentPairs +
                      " monsters=" + monsters.Length + " deterministic=true extraSeeds=2");
            Finish(0);
        }
        catch (Exception error)
        {
            Debug.LogError("[NAV_SMOKE] FAIL " + error);
            Finish(1);
        }
    }

    static void Finish(int exitCode)
    {
        SessionState.SetBool(PendingKey, false);
        EditorApplication.Exit(exitCode);
    }

    static void CheckGraphConnectivity(string mask, MonsterNetworkSetup[] monsters,
                                       int gridSize, float origin, float step)
    {
        if (monsters.Length < 2) throw new Exception("Missing monster spawns");
        int first = ClosestMaskCell(mask, monsters[0].transform.position, gridSize, origin, step);
        int second = ClosestMaskCell(mask, monsters[1].transform.position, gridSize, origin, step);
        if (first < 0 || second < 0) throw new Exception("Monster spawn missing from grid");
        bool[] seen = new bool[mask.Length];
        Queue<int> queue = new Queue<int>();
        seen[first] = true;
        queue.Enqueue(first);
        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % gridSize, z = index / gridSize;
            int edge = Convert.ToInt32(mask[index].ToString(), 16);
            if ((edge & 1) != 0 && x + 1 < gridSize) Add(index + 1);
            if ((edge & 2) != 0 && x > 0) Add(index - 1);
            if ((edge & 4) != 0 && z + 1 < gridSize) Add(index + gridSize);
            if ((edge & 8) != 0 && z > 0) Add(index - gridSize);
        }
        if (!seen[second]) throw new Exception("Monster spawns disconnected in server grid");

        void Add(int index)
        {
            if (seen[index]) return;
            seen[index] = true;
            queue.Enqueue(index);
        }
    }

    static int ClosestMaskCell(string mask, Vector3 position,
                               int gridSize, float origin, float step)
    {
        int centerX = Mathf.RoundToInt((position.x - origin) / step);
        int centerZ = Mathf.RoundToInt((position.z - origin) / step);
        int result = -1;
        float best = float.PositiveInfinity;
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int x = centerX + dx, z = centerZ + dz;
            if (x < 0 || z < 0 || x >= gridSize || z >= gridSize) continue;
            int index = z * gridSize + x;
            if (mask[index] == '0') continue;
            float px = origin + x * step, pz = origin + z * step;
            float distance = (position.x - px) * (position.x - px) +
                             (position.z - pz) * (position.z - pz);
            if (distance >= best) continue;
            best = distance;
            result = index;
        }
        return result;
    }
}
