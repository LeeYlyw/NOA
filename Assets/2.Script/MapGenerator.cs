using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

[System.Serializable]
public class RoomData
{
    public GameObject prefab;
    public int sizeX = 1;
    public int sizeZ = 1;
}

public class MapGenerator : MonoBehaviour
{
    [Header("맵 규격 세팅")]
    public int mapSize = 20;
    public float tileSize = 10f;

    [Header("복도 모듈 프리팹 (1:1 매칭용)")]
    public GameObject prefabI;
    public GameObject prefabL;
    public GameObject prefabU;
    public GameObject prefabO;

    [Header("배치할 방 목록")]
    public List<RoomData> roomsToSpawn;

    [Header("스폰할 프리팹 설정")]
    public GameObject player1Prefab;
    public GameObject player2Prefab;
    public GameObject player3Prefab;
    public GameObject player4Prefab;
    public GameObject monsterPrefab;
    public GameObject monster2Prefab;
    public GameObject[] itemPrefabs;

    [Header("스폰 설정")]
    public float minMonsterDistance = 30f;
    public int itemSpawnCount = 20;

    [Header("Spawn Clearance")]
    [Min(0.1f)] public float spawnRadius = 0.45f;
    [Min(0.5f)] public float spawnHeight = 1.8f;
    [Range(1, 50)] public int maxGenerationAttempts = 12;

    [Header("Runtime Navigation")]
    public NavMeshSurface navMeshSurface;

    // 맵 상태: 0 = 벽/빈공간, 1 = 복도, 2 = 방 내부, 3 = 방 문
    private int[,] map;
    private List<Vector2Int> roomDoors = new List<Vector2Int>();
    private List<Vector3> validSpawnPoints = new List<Vector3>();
    private readonly List<Vector3> walkableCenters = new List<Vector3>();
    private readonly List<Vector3> corridorMonsterSpawns = new List<Vector3>();
    private readonly HashSet<Vector2Int> oneByOneRoomCells = new HashSet<Vector2Int>();
    private readonly HashSet<long> openEmptyEdges = new HashSet<long>();
    private readonly List<List<long>> emptyRegionCandidates = new List<List<long>>();
    private readonly List<GameObject> spawnedWalls = new List<GameObject>();
    private readonly List<Bounds> bedVisualBounds = new List<Bounds>();
    private readonly List<GameObject> spawnedEntities = new List<GameObject>();
    private readonly List<DoorInteraction> spawnedDoors = new List<DoorInteraction>();
    private Transform roomsRoot;
    private Transform wallsRoot;
    private Transform gameplayRoot;
    private Transform playersRoot;
    private Transform monstersRoot;
    private Transform puzzleRoot;
    private Transform cluesRoot;
    private Transform itemsRoot;
    private Vector3 puzzleRoomCenter;
    private bool hasPuzzleRoom;

    void Start()
    {
        if (NetworkClient.Instance != null && NetworkClient.Instance.offlineMode)
        {
            GenerateMap(Random.Range(1, 999999));
        }
    }

    [ContextMenu("Generate Map")]
    public void GenerateMap(int seed)
    {
        if (mapSize < 5 || tileSize <= 0f || roomsToSpawn == null || roomsToSpawn.Count == 0)
        {
            Debug.LogError("[MapGenerator] Map size, tile size, and room prefabs must be configured.");
            ReportGenerationFailure("Invalid map configuration");
            return;
        }
        for (int i = 0; i < roomsToSpawn.Count; i++)
        {
            if (roomsToSpawn[i] == null || roomsToSpawn[i].prefab == null)
            {
                Debug.LogError($"[MapGenerator] Room entry {i} has a missing prefab; map generation cancelled.");
                ReportGenerationFailure("Missing room prefab");
                return;
            }
        }

        foreach (GameObject entity in spawnedEntities)
            DisposeGeneratedObject(entity);
        spawnedEntities.Clear();

        bool generated = false;
        int selectedSeed = seed;
        int roomFailures = 0, topologyFailures = 0, emptyFailures = 0;
        int openingFailures = 0, spawnFailures = 0, navigationFailures = 0;
        for (int attempt = 0; attempt < maxGenerationAttempts; attempt++)
        {
            ClearMapChildren();
            roomsRoot = CreateGroup("Rooms", transform);
            wallsRoot = CreateGroup("Corridor Walls", transform);
            selectedSeed = seed + attempt;
            Random.InitState(selectedSeed);
            map = new int[mapSize, mapSize];
            roomDoors.Clear();
            oneByOneRoomCells.Clear();
            validSpawnPoints.Clear();
            walkableCenters.Clear();
            corridorMonsterSpawns.Clear();
            openEmptyEdges.Clear();
            emptyRegionCandidates.Clear();
            spawnedWalls.Clear();
            bedVisualBounds.Clear();
            spawnedDoors.Clear();
            hasPuzzleRoom = false;

            if (!PlaceRooms()) { roomFailures++; continue; }
            Physics.SyncTransforms();
            BuildFullMaze();
            EnsureGlobalConnectivity();
            LoopDeadEnds();
            if (FindConnectedAreas().Count != 1) { topologyFailures++; continue; }
            if (!OpenEmptyAreasToCorridors()) { emptyFailures++; continue; }

            SpawnPrefabsSafely();
            Physics.SyncTransforms();
            if (!RepairBlockedEmptyOpenings()) { openingFailures++; continue; }
            if (!CollectReachableSpawnPoints() ||
                validSpawnPoints.Count < 6 + RequiredSupplySpawnCount())
            { spawnFailures++; continue; }
            RebuildNavMesh();
            if (!ValidateNavMeshConnectivity() || !ValidatePuzzleRoomAccess()) { navigationFailures++; continue; }
            // Reserve two player locations and two genuinely clear monster
            // locations before accepting a layout. Never fall back into a wall.
            int clearMonsterPoints = 0;
            foreach (Vector3 point in corridorMonsterSpawns)
                if (IsMonsterSpawnClear(point)) clearMonsterPoints++;
            if (clearMonsterPoints < 2)
            {
                Debug.LogWarning($"[MapGenerator] Seed {selectedSeed}: only {clearMonsterPoints} monster spawns have edge and collider clearance; retrying layout.");
                spawnFailures++;
                continue;
            }

            generated = true;
            break;
        }

        if (!generated)
        {
            ClearMapChildren();
            if (navMeshSurface != null) navMeshSurface.RemoveData();
            Debug.LogError($"[MapGenerator] Seed {seed}: all {maxGenerationAttempts} layout attempts failed " +
                           $"(rooms {roomFailures}, topology {topologyFailures}, empty regions {emptyFailures}, " +
                           $"blocked openings {openingFailures}, spawn points {spawnFailures}, " +
                           $"navigation {navigationFailures}).");
            ReportGenerationFailure("All map layout attempts failed");
            return;
        }

        Debug.Log($"[MapGenerator] Seed {selectedSeed}: {roomDoors.Count} rooms, {validSpawnPoints.Count} safe spawn points, {bedVisualBounds.Count} bed visual blockers.");
        NetworkClient network = NetworkClient.Instance;
        if (network != null) network.RegisterDoors(spawnedDoors.ToArray());
        string navigationMask = network != null && !network.offlineMode && network.IsConnected
            ? BuildServerNavigationMask() : null;
        if (!SpawnEntities())
        {
            ReportGenerationFailure("Required map entities could not be spawned");
            return;
        }
        SendServerNavigationMap(selectedSeed, navigationMask);
    }

    void ReportGenerationFailure(string reason)
    {
        foreach (GameObject entity in spawnedEntities)
            DisposeGeneratedObject(entity);
        spawnedEntities.Clear();
        if (gameplayRoot != null) DisposeGeneratedObject(gameplayRoot.gameObject);
        gameplayRoot = null;
        NetworkClient network = NetworkClient.Instance;
        if (network != null) network.ReportMapPreparationFailure(reason);
    }

    int RequiredSupplySpawnCount()
    {
        bool hasHeal = false, hasRevive = false;
        if (itemPrefabs != null)
            foreach (GameObject prefab in itemPrefabs)
            {
                PotionItem pickup = prefab != null ? prefab.GetComponent<PotionItem>() : null;
                if (pickup == null || pickup.itemData == null) continue;
                hasHeal |= pickup.itemData.type == ItemData.ItemType.Heal;
                hasRevive |= pickup.itemData.type == ItemData.ItemType.Resurrection;
            }
        return (hasHeal ? 2 : 0) + (hasRevive ? 1 : 0);
    }

    // Send the actual baked walkable geometry, rather than asking the C++ server to
    // guess Unity prefab collision from the random seed. Each hex digit describes
    // the four traversable edges of one metre navigation cell.
    void SendServerNavigationMap(int seed, string edgeMask)
    {
        NetworkClient network = NetworkClient.Instance;
        if (network == null || network.offlineMode || !network.IsConnected ||
            !network.HasAssignedPlayerId) return;

        Vector3[] monsterPositions = new Vector3[2];
        bool[] foundMonster = new bool[2];
        foreach (GameObject entity in spawnedEntities)
        {
            if (entity == null) continue;
            MonsterNetworkSetup setup = entity.GetComponent<MonsterNetworkSetup>();
            if (setup == null || setup.monsterId < 1 || setup.monsterId > 2) continue;
            monsterPositions[setup.monsterId - 1] = entity.transform.position;
            foundMonster[setup.monsterId - 1] = true;
        }
        if (!foundMonster[0] || !foundMonster[1])
        {
            Debug.LogError("[MapGenerator] Cannot send navigation map without both monster spawns.");
            ReportGenerationFailure("Monster spawn missing from navigation map");
            return;
        }

        const float step = 1f;
        float worldSize = mapSize * tileSize;
        int width = Mathf.CeilToInt(worldSize / step);
        int height = width;
        float origin = -worldSize * 0.5f + step * 0.5f;
        if (string.IsNullOrEmpty(edgeMask) || edgeMask.Length != width * height)
        {
            ReportGenerationFailure("Navigation map could not be built");
            return;
        }
        Vector3 gate = puzzleRoomCenter + Vector3.back * (tileSize * 0.5f);
        network.SendNavigationMap(seed, width, height, origin, origin, step,
                                  gate.x, gate.z, tileSize * 0.5f - 0.2f,
                                  monsterPositions[0], monsterPositions[1], edgeMask, spawnedDoors.ToArray());
    }

    string BuildServerNavigationMask()
    {
        // The NavMesh was baked with door leaves disabled. Keep the same
        // doorway geometry in the server grid; live door state blocks it later.
        List<Collider> doorLeaves = new List<Collider>();
        foreach (DoorInteraction door in spawnedDoors)
        {
            if (door == null || door.doorHinge == null) continue;
            foreach (Collider leaf in door.doorHinge.GetComponentsInChildren<Collider>(true))
                if (leaf.enabled && !leaf.isTrigger)
                {
                    leaf.enabled = false;
                    doorLeaves.Add(leaf);
                }
        }
        Physics.SyncTransforms();
        try { return BuildServerNavigationMaskWithoutDoorLeaves(); }
        finally
        {
            foreach (Collider leaf in doorLeaves)
                if (leaf != null) leaf.enabled = true;
            Physics.SyncTransforms();
        }
    }

    string BuildServerNavigationMaskWithoutDoorLeaves()
    {
        const float step = 1f;
        float worldSize = mapSize * tileSize;
        int width = Mathf.CeilToInt(worldSize / step);
        int height = width;
        float origin = -worldSize * 0.5f + step * 0.5f;
        if (width > 200 || height > 200)
        {
            Debug.LogError("[MapGenerator] Navigation map exceeds server grid limit.");
            return null;
        }
        bool[] walkable = new bool[width * height];
        Vector3[] points = new Vector3[walkable.Length];
        for (int z = 0; z < height; z++)
        for (int x = 0; x < width; x++)
        {
            int index = z * width + x;
            Vector3 center = new Vector3(origin + x * step, 0f, origin + z * step);
            if (NavMesh.SamplePosition(center, out NavMeshHit hit, 0.4f, NavMesh.AllAreas) &&
                Vector2.Distance(new Vector2(center.x, center.z),
                                 new Vector2(hit.position.x, hit.position.z)) <= 0.4f &&
                IsMonsterBodyClear(hit.position, 0.65f))
            {
                walkable[index] = true;
                points[index] = hit.position;
            }
        }
        byte[] edges = new byte[walkable.Length];
        for (int z = 0; z < height; z++)
        for (int x = 0; x < width; x++)
        {
            int index = z * width + x;
            if (!walkable[index]) continue;
            if (x + 1 < width && walkable[index + 1] &&
                IsMonsterBodyClear((points[index] + points[index + 1]) * 0.5f, 0.65f) &&
                !NavMesh.Raycast(points[index], points[index + 1], out _, NavMesh.AllAreas))
            {
                edges[index] |= 1;
                edges[index + 1] |= 2;
            }
            if (z + 1 < height && walkable[index + width] &&
                IsMonsterBodyClear((points[index] + points[index + width]) * 0.5f, 0.65f) &&
                !NavMesh.Raycast(points[index], points[index + width], out _, NavMesh.AllAreas))
            {
                edges[index] |= 4;
                edges[index + width] |= 8;
            }
        }
        const string hex = "0123456789ABCDEF";
        System.Text.StringBuilder mask = new System.Text.StringBuilder(edges.Length);
        foreach (byte edge in edges) mask.Append(hex[edge]);
        return mask.ToString();
    }

    void RebuildNavMesh()
    {
        if (navMeshSurface == null)
            navMeshSurface = GetComponent<NavMeshSurface>();

        if (navMeshSurface == null)
            navMeshSurface = gameObject.AddComponent<NavMeshSurface>();

        navMeshSurface.collectObjects = CollectObjects.All;
        navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        // Bake the potential route through each room door. The server applies
        // the live closed/open state to these edges after both maps agree.
        List<Collider> doorLeaves = new List<Collider>();
        foreach (DoorInteraction door in spawnedDoors)
        {
            if (door == null || door.doorHinge == null) continue;
            foreach (Collider leaf in door.doorHinge.GetComponentsInChildren<Collider>(true))
                if (leaf.enabled && !leaf.isTrigger)
                {
                    leaf.enabled = false;
                    doorLeaves.Add(leaf);
                }
        }
        Physics.SyncTransforms();
        try
        {
            navMeshSurface.RemoveData();
            navMeshSurface.BuildNavMesh();
        }
        finally
        {
            foreach (Collider leaf in doorLeaves)
                if (leaf != null) leaf.enabled = true;
            Physics.SyncTransforms();
        }
    }

    bool ValidateNavMeshConnectivity()
    {
        if (walkableCenters.Count == 0) return false;
        if (!NavMesh.SamplePosition(walkableCenters[0], out NavMeshHit origin, 1f, NavMesh.AllAreas))
        {
            Debug.LogWarning($"[MapGenerator] NavMesh has no surface near origin {walkableCenters[0]}.");
            return false;
        }

        float mapLimit = mapSize * tileSize / 2f - tileSize / 2f;
        NavMeshPath path = new NavMeshPath();
        foreach (Vector3 center in walkableCenters)
        {
            if (!NavMesh.SamplePosition(center, out NavMeshHit destination, 1f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[MapGenerator] NavMesh has no surface near {center}.");
                return false;
            }
            if (!NavMesh.CalculatePath(origin.position, destination.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete)
            {
                Debug.LogWarning($"[MapGenerator] NavMesh path to {center} is {path.status}.");
                return false;
            }

            foreach (Vector3 corner in path.corners)
            {
                if (Mathf.Abs(corner.x) > mapLimit || Mathf.Abs(corner.z) > mapLimit)
                {
                    Debug.LogWarning($"[MapGenerator] NavMesh path to {center} leaves the map at {corner}.");
                    return false;
                }
            }
        }
        return true;
    }

    bool ValidatePuzzleRoomAccess()
    {
        if (!hasPuzzleRoom) return false;
        if (walkableCenters.Count == 0 ||
            !NavMesh.SamplePosition(walkableCenters[0], out NavMeshHit outside, 1.5f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(puzzleRoomCenter, out NavMeshHit inside, 1.5f, NavMesh.AllAreas))
            return false;
        NavMeshPath path = new NavMeshPath();
        return NavMesh.CalculatePath(outside.position, inside.position, NavMesh.AllAreas, path) &&
               path.status == NavMeshPathStatus.PathComplete;
    }

    void DisposeGeneratedObject(GameObject obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
        {
            obj.SetActive(false);
            obj.transform.SetParent(null);
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    void ClearMapChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DisposeGeneratedObject(transform.GetChild(i).gameObject);
        roomsRoot = wallsRoot = gameplayRoot = null;
        playersRoot = monstersRoot = puzzleRoot = cluesRoot = itemsRoot = null;
    }

    static Transform CreateGroup(string label, Transform parent)
    {
        GameObject group = new GameObject(label);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    void CreateGameplayGroups()
    {
        gameplayRoot = CreateGroup("Gameplay", transform);
        playersRoot = CreateGroup("Players", gameplayRoot);
        monstersRoot = CreateGroup("Monsters", gameplayRoot);
        puzzleRoot = CreateGroup("Puzzle", gameplayRoot);
        cluesRoot = CreateGroup("Clues", gameplayRoot);
        itemsRoot = CreateGroup("Items", gameplayRoot);
    }

    bool PlaceRooms()
    {
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);
        bool centralRoomPlaced = false;
        int nextMimicId = 1;
        int nextDoorId = 1;

        for (int i = 0; i < roomsToSpawn.Count; i++)
        {
            var room = roomsToSpawn[i];
            if (room.sizeX < 1 || room.sizeZ < 1 ||
                mapSize - room.sizeX - 1 <= 1 || mapSize - room.sizeZ - 1 <= 2)
            {
                Debug.LogError($"[MapGenerator] Room entry {i} ({room.prefab.name}) does not fit the map.");
                return false;
            }

            bool placed = false;
            int attempts = 0;

            int maxX = mapSize - room.sizeX - 1;
            int maxZ = mapSize - room.sizeZ - 1;

            while (!placed && attempts < 500)
            {
                int rx = !centralRoomPlaced
                    ? Mathf.Clamp((mapSize - room.sizeX) / 2 + Random.Range(-2, 3), 1, maxX - 1)
                    : Random.Range(1, maxX);
                int rz = !centralRoomPlaced
                    ? Mathf.Clamp((mapSize - room.sizeZ) / 2 + Random.Range(-2, 3), 2, maxZ - 1)
                    : Random.Range(2, maxZ);

                if (IsAreaClear(rx, rz, room.sizeX, room.sizeZ))
                {
                    for (int x = rx; x < rx + room.sizeX; x++)
                    {
                        for (int z = rz; z < rz + room.sizeZ; z++)
                        {
                            map[x, z] = 2;
                        }
                    }

                    int doorX = rx + (room.sizeX / 2);
                    int doorZ = rz - 1;
                    map[doorX, doorZ] = 3;
                    roomDoors.Add(new Vector2Int(doorX, doorZ));
                    if (room.sizeX == 1 && room.sizeZ == 1)
                        oneByOneRoomCells.Add(new Vector2Int(rx, rz));

                    float posX = startOffset + (rx * tileSize) + ((room.sizeX - 1) * tileSize / 2f);
                    float posZ = startOffset + (rz * tileSize) + ((room.sizeZ - 1) * tileSize / 2f);
                    if (!hasPuzzleRoom && room.sizeX == 1 && room.sizeZ == 1)
                    {
                        puzzleRoomCenter = new Vector3(posX, 0f, posZ);
                        hasPuzzleRoom = true;
                    }
                    GameObject roomInstance = Instantiate(room.prefab, new Vector3(posX, 0, posZ),
                                                          Quaternion.identity, roomsRoot);
                    CollectBedVisualBounds(roomInstance);
                    EnsureBarrelCollision(roomInstance);
                    foreach (MimicLaptop mimic in roomInstance.GetComponentsInChildren<MimicLaptop>(true))
                        mimic.SetNetworkId(nextMimicId++);
                    foreach (DoorInteraction door in roomInstance.GetComponentsInChildren<DoorInteraction>(true))
                    {
                        door.SetNetworkId(nextDoorId++);
                        spawnedDoors.Add(door);
                    }

                    placed = true;
                    centralRoomPlaced = true;
                }
                attempts++;
            }
            if (!placed)
            {
                Debug.LogWarning($"[MapGenerator] Could not place room {i} ({room.prefab.name}) in this layout.");
                return false;
            }
        }
        return roomDoors.Count > 0;
    }

    bool IsAreaClear(int startX, int startZ, int sizeX, int sizeZ)
    {
        for (int x = startX - 1; x <= startX + sizeX; x++)
        {
            for (int z = startZ - 1; z <= startZ + sizeZ; z++)
            {
                if (x < 0 || x >= mapSize || z < 0 || z >= mapSize) return false;
                if (map[x, z] != 0) return false;
            }
        }
        return true;
    }

    // 맵 전체를 빈틈없이 채우는 완전 미로 생성
    void BuildFullMaze()
    {
        // 방 문에서 출발하여 미로 확장
        foreach (var door in roomDoors)
        {
            CarveMaze(door.x, door.y);
        }

        // 미로가 도달하지 못한 빈 공간들도 남김없이 채움
        for (int x = 1; x < mapSize - 1; x++)
        {
            for (int z = 1; z < mapSize - 1; z++)
            {
                if (map[x, z] == 0 && GetAdjacentPathCount(x, z) == 0)
                {
                    CarveMaze(x, z);
                }
            }
        }
    }

    void CarveMaze(int startX, int startZ)
    {
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        stack.Push(new Vector2Int(startX, startZ));

        if (map[startX, startZ] == 0) map[startX, startZ] = 1;

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (stack.Count > 0)
        {
            Vector2Int curr = stack.Peek();

            // 방향 셔플
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2Int temp = dirs[i];
                int randomIndex = Random.Range(i, dirs.Length);
                dirs[i] = dirs[randomIndex];
                dirs[randomIndex] = temp;
            }

            bool moved = false;
            foreach (var dir in dirs)
            {
                int nx = curr.x + dir.x;
                int nz = curr.y + dir.y;

                // 외곽 테두리(0, mapSize-1)는 벽으로 보존
                if (nx > 0 && nx < mapSize - 1 && nz > 0 && nz < mapSize - 1)
                {
                    if (map[nx, nz] == 0)
                    {
                        // 인접한 길이 1개 이하일 때만 파서 자연스러운 미로 형성
                        if (GetAdjacentPathCount(nx, nz) <= 1)
                        {
                            map[nx, nz] = 1;
                            stack.Push(new Vector2Int(nx, nz));
                            moved = true;
                            break;
                        }
                    }
                }
            }

            if (!moved) stack.Pop();
        }
    }

    int GetAdjacentPathCount(int x, int z)
    {
        int count = 0;
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var dir in dirs)
        {
            int nx = x + dir.x;
            int nz = z + dir.y;
            if (nx >= 0 && nx < mapSize && nz >= 0 && nz < mapSize)
            {
                if (map[nx, nz] != 0) count++;
            }
        }
        return count;
    }

    // [개선] 1자 도로 없이 최소 거리로만 자연스럽게 섬 연결
    // Rooms connect to corridors only through their south door cell.
    bool IsConnectedEdge(Vector2Int from, Vector2Int to)
    {
        if (to.x < 0 || to.x >= mapSize || to.y < 0 || to.y >= mapSize)
            return false;

        int current = map[from.x, from.y];
        int neighbor = map[to.x, to.y];
        if (current == 0 || neighbor == 0) return false;
        if (current == 2 || neighbor == 2)
        {
            if (current == 2 && neighbor == 2) return true;
            return (current == 3 && neighbor == 2 && to == from + Vector2Int.up) ||
                   (current == 2 && neighbor == 3 && to == from + Vector2Int.down);
        }
        return true;
    }

    List<List<Vector2Int>> FindConnectedAreas()
    {
        bool[,] visited = new bool[mapSize, mapSize];
        List<List<Vector2Int>> areas = new List<List<Vector2Int>>();
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (map[x, z] == 0 || visited[x, z]) continue;
                List<Vector2Int> area = new List<Vector2Int>();
                Queue<Vector2Int> queue = new Queue<Vector2Int>();
                queue.Enqueue(new Vector2Int(x, z));
                visited[x, z] = true;
                while (queue.Count > 0)
                {
                    Vector2Int current = queue.Dequeue();
                    area.Add(current);
                    foreach (var dir in dirs)
                    {
                        Vector2Int next = current + dir;
                        if (!IsConnectedEdge(current, next) || visited[next.x, next.y]) continue;
                        visited[next.x, next.y] = true;
                        queue.Enqueue(next);
                    }
                }
                areas.Add(area);
            }
        }
        return areas;
    }

    // Connect maze components without tunnelling through room prefabs.
    void EnsureGlobalConnectivity()
    {
        for (int attempt = 0; attempt < mapSize * mapSize; attempt++)
        {
            List<List<Vector2Int>> areas = FindConnectedAreas();
            if (areas.Count <= 1) return;
            areas.Sort((a, b) => b.Count.CompareTo(a.Count));
            if (!ConnectAreasThroughCorridor(areas[0], areas[1])) return;
        }
    }

    bool ConnectAreasThroughCorridor(List<Vector2Int> mainArea, List<Vector2Int> otherArea)
    {
        HashSet<Vector2Int> targets = new HashSet<Vector2Int>();
        foreach (var cell in mainArea)
            if (map[cell.x, cell.y] == 1 || map[cell.x, cell.y] == 3) targets.Add(cell);

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> parent = new Dictionary<Vector2Int, Vector2Int>();
        foreach (var cell in otherArea)
        {
            if (map[cell.x, cell.y] != 1 && map[cell.x, cell.y] != 3) continue;
            queue.Enqueue(cell);
            parent[cell] = cell;
        }

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (targets.Contains(current))
            {
                while (parent[current] != current)
                {
                    if (map[current.x, current.y] == 0) map[current.x, current.y] = 1;
                    current = parent[current];
                }
                return true;
            }

            foreach (var dir in dirs)
            {
                Vector2Int next = current + dir;
                if (next.x <= 0 || next.x >= mapSize - 1 || next.y <= 0 || next.y >= mapSize - 1) continue;
                if (map[next.x, next.y] == 2 || parent.ContainsKey(next)) continue;
                parent[next] = current;
                queue.Enqueue(next);
            }
        }
        return false;
    }

    void LoopDeadEnds()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        for (int x = 1; x < mapSize - 1; x++)
        {
            for (int z = 1; z < mapSize - 1; z++)
            {
                if (map[x, z] == 1)
                {
                    int pathCount = 0;
                    foreach (var dir in dirs)
                    {
                        int nx = x + dir.x;
                        int nz = z + dir.y;
                        if (map[nx, nz] == 1 || map[nx, nz] == 2 || map[nx, nz] == 3) pathCount++;
                    }

                    if (pathCount == 1)
                    {
                        List<Vector2Int> possibleSmashes = new List<Vector2Int>();

                        foreach (var dir in dirs)
                        {
                            int nx = x + dir.x;
                            int nz = z + dir.y;

                            if (nx > 0 && nx < mapSize - 1 && nz > 0 && nz < mapSize - 1 && map[nx, nz] == 0)
                            {
                                int farX = nx + dir.x;
                                int farZ = nz + dir.y;

                                if (farX > 0 && farX < mapSize - 1 && farZ > 0 && farZ < mapSize - 1)
                                {
                                    if (map[farX, farZ] == 1 || map[farX, farZ] == 3)
                                    {
                                        possibleSmashes.Add(new Vector2Int(nx, nz));
                                    }
                                }
                            }
                        }

                        if (possibleSmashes.Count > 0)
                        {
                            Vector2Int smashTarget = possibleSmashes[Random.Range(0, possibleSmashes.Count)];
                            map[smashTarget.x, smashTarget.y] = 1;
                        }
                    }
                }
            }
        }
    }

    // Start each unused floor region with one entrance. Add another only if
    // the placed wall geometry blocks it; never turn the region into open floor.
    bool OpenEmptyAreasToCorridors()
    {
        bool[,] visited = new bool[mapSize, mapSize];
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        for (int x = 1; x < mapSize - 1; x++)
        {
            for (int z = 1; z < mapSize - 1; z++)
            {
                if (map[x, z] != 0 || visited[x, z]) continue;

                List<long> possibleOpenings = new List<long>();
                Queue<Vector2Int> queue = new Queue<Vector2Int>();
                queue.Enqueue(new Vector2Int(x, z));
                visited[x, z] = true;
                while (queue.Count > 0)
                {
                    Vector2Int current = queue.Dequeue();
                    foreach (var dir in dirs)
                    {
                        Vector2Int next = current + dir;
                        // The outer ring is outside the maze. Never merge it with
                        // interior floor or create a doorway through the perimeter.
                        if (next.x <= 0 || next.x >= mapSize - 1 ||
                            next.y <= 0 || next.y >= mapSize - 1)
                            continue;

                        if (map[next.x, next.y] == 1 || map[next.x, next.y] == 3)
                        {
                            if (IsOpeningClear(next, current))
                                possibleOpenings.Add(EmptyEdgeKey(next.x, next.y, current.x, current.y));
                        }
                        else if (map[next.x, next.y] == 0 && !visited[next.x, next.y])
                        {
                            visited[next.x, next.y] = true;
                            queue.Enqueue(next);
                        }
                    }
                }

                if (possibleOpenings.Count == 0) return false;
                emptyRegionCandidates.Add(possibleOpenings);
                openEmptyEdges.Add(possibleOpenings[Random.Range(0, possibleOpenings.Count)]);
            }
        }
        return true;
    }

    bool IsOpeningClear(Vector2Int corridor, Vector2Int empty)
    {
        float startOffset = -(mapSize * tileSize) / 2f + tileSize / 2f;
        Vector3 corridorPos = new Vector3(startOffset + corridor.x * tileSize, 1f,
                                          startOffset + corridor.y * tileSize);
        Vector3 emptyPos = new Vector3(startOffset + empty.x * tileSize, 1f,
                                       startOffset + empty.y * tileSize);
        return IsSpawnClear(corridorPos) && IsSpawnClear(emptyPos) &&
               IsPassageClear(corridorPos, emptyPos);
    }

    bool IsOpeningClear(long edge)
    {
        int corridorIndex = (int)(edge >> 32);
        int emptyIndex = (int)(edge & 0xffffffffL);
        return IsOpeningClear(new Vector2Int(corridorIndex / mapSize, corridorIndex % mapSize),
                              new Vector2Int(emptyIndex / mapSize, emptyIndex % mapSize));
    }

    // Replace only generated wall pieces when a doorway is obstructed by their
    // colliders. Keep the rooms and maze cells unchanged, and stop at the first
    // physically usable opening for each interior empty region.
    bool RepairBlockedEmptyOpenings()
    {
        int extraOpenings = 0;
        while (true)
        {
            bool addedOpening = false;
            foreach (List<long> candidates in emptyRegionCandidates)
            {
                bool passable = false;
                foreach (long edge in candidates)
                {
                    if (openEmptyEdges.Contains(edge) && IsOpeningClear(edge))
                    {
                        passable = true;
                        break;
                    }
                }
                if (passable) continue;

                List<long> unused = new List<long>();
                foreach (long edge in candidates)
                    if (!openEmptyEdges.Contains(edge)) unused.Add(edge);
                if (unused.Count == 0) return false;

                openEmptyEdges.Add(unused[Random.Range(0, unused.Count)]);
                extraOpenings++;
                addedOpening = true;
            }

            if (!addedOpening)
            {
                if (extraOpenings > 0)
                    Debug.Log($"[MapGenerator] Opened {extraOpenings} additional maze edges to make enclosed regions passable.");
                return true;
            }

            foreach (GameObject wall in spawnedWalls)
                DisposeGeneratedObject(wall);
            spawnedWalls.Clear();
            SpawnPrefabsSafely();
            Physics.SyncTransforms();
        }
    }

    long EmptyEdgeKey(int corridorX, int corridorZ, int emptyX, int emptyZ)
    {
        long corridorIndex = corridorX * mapSize + corridorZ;
        long emptyIndex = emptyX * mapSize + emptyZ;
        return (corridorIndex << 32) | emptyIndex;
    }

    void SpawnPrefabsSafely()
    {
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (map[x, z] == 1 || map[x, z] == 3)
                {
                    bool wallTop = NeedsCorridorWall(x, z, x, z + 1);
                    bool wallRight = NeedsCorridorWall(x, z, x + 1, z);
                    bool wallBottom = NeedsCorridorWall(x, z, x, z - 1);
                    bool wallLeft = NeedsCorridorWall(x, z, x - 1, z);

                    int mask = 0;
                    if (wallTop) mask += 1;
                    if (wallRight) mask += 2;
                    if (wallBottom) mask += 4;
                    if (wallLeft) mask += 8;

                    Vector3 pos = new Vector3(startOffset + (x * tileSize), 0, startOffset + (z * tileSize));

                    switch (mask)
                    {
                        case 0: break;
                        case 1: SpawnWall(prefabI, pos, Quaternion.Euler(0, 180, 0)); break;
                        case 2: SpawnWall(prefabI, pos, Quaternion.Euler(0, 270, 0)); break;
                        case 4: SpawnWall(prefabI, pos, Quaternion.Euler(0, 0, 0)); break;
                        case 8: SpawnWall(prefabI, pos, Quaternion.Euler(0, 90, 0)); break;

                        case 5:
                            SpawnWall(prefabI, pos, Quaternion.Euler(0, 0, 0));
                            SpawnWall(prefabI, pos, Quaternion.Euler(0, 180, 0));
                            break;
                        case 10:
                            SpawnWall(prefabI, pos, Quaternion.Euler(0, 90, 0));
                            SpawnWall(prefabI, pos, Quaternion.Euler(0, 270, 0));
                            break;

                        case 3: SpawnWall(prefabL, pos, Quaternion.Euler(0, 0, 0)); break;
                        case 6: SpawnWall(prefabL, pos, Quaternion.Euler(0, 90, 0)); break;
                        case 12: SpawnWall(prefabL, pos, Quaternion.Euler(0, 180, 0)); break;
                        case 9: SpawnWall(prefabL, pos, Quaternion.Euler(0, 270, 0)); break;

                        case 7: SpawnWall(prefabU, pos, Quaternion.Euler(0, 90, 0)); break;
                        case 14: SpawnWall(prefabU, pos, Quaternion.Euler(0, 180, 0)); break;
                        case 13: SpawnWall(prefabU, pos, Quaternion.Euler(0, 270, 0)); break;
                        case 11: SpawnWall(prefabU, pos, Quaternion.Euler(0, 0, 0)); break;

                        case 15:
                            // Wall O's source pivot is at a corner, unlike the other wall modules.
                            SpawnWall(prefabO, pos + new Vector3(-tileSize / 2f, 0f, tileSize / 2f), Quaternion.identity);
                            break;
                    }
                }
            }
        }
    }

    void SpawnWall(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        if (prefab != null)
        {
            spawnedWalls.Add(Instantiate(prefab, pos, rot, wallsRoot));
        }
    }

    private static void EnsureBarrelCollision(GameObject room)
    {
        foreach (MeshFilter mesh in room.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.sharedMesh == null) continue;
            bool isBarrel = mesh.sharedMesh.name.ToLowerInvariant().Contains("barrel");
            bool hasSolidCollider = false;
            for (Transform node = mesh.transform; node != null && node != room.transform; node = node.parent)
            {
                isBarrel |= node.name.ToLowerInvariant().Contains("barrel");
                foreach (Collider collider in node.GetComponents<Collider>())
                    hasSolidCollider |= collider.enabled && !collider.isTrigger;
            }
            if (!isBarrel || hasSolidCollider) continue;
            // Static imported barrels may have only a mesh. Local bounds avoid
            // runtime mesh cooking and are included in the subsequent NavMesh bake.
            BoxCollider body = mesh.gameObject.AddComponent<BoxCollider>();
            body.center = mesh.sharedMesh.bounds.center;
            body.size = mesh.sharedMesh.bounds.size;
        }
    }

    private void CollectBedVisualBounds(GameObject room)
    {
        foreach (Transform child in room.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.StartsWith("Cama", System.StringComparison.OrdinalIgnoreCase)) continue;
            Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            Bounds visual = default;
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { visual = renderer.bounds; found = true; }
                else visual.Encapsulate(renderer.bounds);
            }
            if (!found) continue;
            bedVisualBounds.Add(visual);
            // The imported concave mattress collider leaves empty volume below
            // its surface. Bake a solid footprint so patrol paths avoid it too.
            float floorY = Mathf.Min(room.transform.position.y, visual.min.y);
            GameObject footprint = new GameObject("Runtime Bed Footprint");
            footprint.transform.SetParent(room.transform);
            footprint.transform.position = new Vector3(
                visual.center.x, (floorY + visual.max.y) * 0.5f, visual.center.z);
            BoxCollider obstacle = footprint.AddComponent<BoxCollider>();
            obstacle.size = new Vector3(visual.size.x,
                Mathf.Max(0.2f, visual.max.y - floorY), visual.size.z);
        }
    }

    bool NeedsCorridorWall(int cx, int cz, int nx, int nz)
    {
        // A 1x1 room prefab supplies its own perimeter walls. Do not spawn a
        // second corridor wall on that shared edge.
        if (oneByOneRoomCells.Contains(new Vector2Int(nx, nz)))
            return false;

        return !IsPath(cx, cz, nx, nz);
    }

    bool IsPath(int cx, int cz, int nx, int nz)
    {
        if (nx >= 0 && nx < mapSize && nz >= 0 && nz < mapSize && map[nx, nz] == 0 &&
            openEmptyEdges.Contains(EmptyEdgeKey(cx, cz, nx, nz)))
            return true;
        return IsConnectedEdge(new Vector2Int(cx, cz), new Vector2Int(nx, nz));
    }

    bool IsWalkableEdge(Vector2Int from, Vector2Int to)
    {
        if (to.x <= 0 || to.x >= mapSize - 1 || to.y <= 0 || to.y >= mapSize - 1)
            return false;

        int current = map[from.x, from.y];
        int neighbor = map[to.x, to.y];
        if (current == 0 && neighbor == 0) return true;
        if (current == 0 && (neighbor == 1 || neighbor == 3))
            return openEmptyEdges.Contains(EmptyEdgeKey(to.x, to.y, from.x, from.y));
        if (neighbor == 0 && (current == 1 || current == 3))
            return openEmptyEdges.Contains(EmptyEdgeKey(from.x, from.y, to.x, to.y));
        return IsConnectedEdge(from, to);
    }

    // Collider walls can divide a logically connected map into separate spaces.
    bool CollectReachableSpawnPoints()
    {
        validSpawnPoints.Clear();
        walkableCenters.Clear();
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);
        bool[,] clear = new bool[mapSize, mapSize];
        bool[,] visited = new bool[mapSize, mapSize];
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (x > 0 && x < mapSize - 1 && z > 0 && z < mapSize - 1)
                    clear[x, z] = IsSpawnClear(new Vector3(startOffset + x * tileSize, 1f,
                                                         startOffset + z * tileSize));
            }
        }

        List<Vector3> largestArea = new List<Vector3>();
        int corridorAreas = 0;
        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (!clear[x, z] || visited[x, z]) continue;
                List<Vector3> area = new List<Vector3>();
                List<Vector3> areaCenters = new List<Vector3>();
                Queue<Vector2Int> queue = new Queue<Vector2Int>();
                queue.Enqueue(new Vector2Int(x, z));
                visited[x, z] = true;
                bool containsCorridor = false;

                while (queue.Count > 0)
                {
                    Vector2Int curr = queue.Dequeue();
                    Vector3 worldPos = new Vector3(startOffset + curr.x * tileSize, 1f,
                                                   startOffset + curr.y * tileSize);
                    areaCenters.Add(worldPos);
                    if (map[curr.x, curr.y] == 1 || map[curr.x, curr.y] == 3)
                        containsCorridor = true;
                    if (map[curr.x, curr.y] != 0 && map[curr.x, curr.y] != 3)
                        area.Add(worldPos);

                    foreach (var dir in dirs)
                    {
                        Vector2Int next = curr + dir;
                        if (next.x < 0 || next.x >= mapSize || next.y < 0 || next.y >= mapSize ||
                            visited[next.x, next.y] || !clear[next.x, next.y] ||
                            !IsWalkableEdge(curr, next)) continue;

                        Vector3 nextPos = new Vector3(startOffset + next.x * tileSize, 1f,
                                                      startOffset + next.y * tileSize);
                        if (!IsPassageClear(worldPos, nextPos)) continue;
                        visited[next.x, next.y] = true;
                        queue.Enqueue(next);
                    }
                }
                if (containsCorridor)
                {
                    corridorAreas++;
                    if (area.Count > largestArea.Count)
                    {
                        largestArea = area;
                        walkableCenters.Clear();
                        walkableCenters.AddRange(areaCenters);
                    }
                }
            }
        }
        validSpawnPoints.AddRange(largestArea);
        // The general pool mixes corridors (1) and furnished rooms (2).
        // Reserve corridor cells for monsters so a bed cannot be their start.
        foreach (Vector3 point in walkableCenters)
        {
            int x = Mathf.RoundToInt((point.x - startOffset) / tileSize);
            int z = Mathf.RoundToInt((point.z - startOffset) / tileSize);
            if (x >= 0 && x < mapSize && z >= 0 && z < mapSize && map[x, z] == 1)
                corridorMonsterSpawns.Add(point);
        }
        // The locked room has its own fixed clue. No player, monster, field
        // clue, or random supply may start behind its closed gate.
        if (hasPuzzleRoom)
            validSpawnPoints.RemoveAll(point =>
                new Vector2(point.x - puzzleRoomCenter.x, point.z - puzzleRoomCenter.z).sqrMagnitude <
                tileSize * tileSize * 0.16f);
        if (corridorAreas != 1)
            Debug.LogWarning($"[MapGenerator] Found {corridorAreas} separate corridor areas; retrying this layout.");
        return corridorAreas == 1;
    }

    bool IsPassageClear(Vector3 from, Vector3 to)
    {
        float radius = Mathf.Max(0.1f, spawnRadius);
        float height = Mathf.Max(spawnHeight, radius * 2f);
        Vector3 direction = to - from;
        Vector3 bottom = from + Vector3.up * radius;
        Vector3 top = from + Vector3.up * (height - radius);
        return !Physics.CapsuleCast(bottom, top, radius, direction.normalized,
                                    direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
    }

    bool IsSpawnClear(Vector3 position)
    {
        float radius = Mathf.Max(0.1f, spawnRadius);
        float height = Mathf.Max(spawnHeight, radius * 2f);
        Vector3 bottom = position + Vector3.up * radius;
        Vector3 top = position + Vector3.up * (height - radius);
        return !Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
    }

    bool SpawnEntities()
    {
        if (validSpawnPoints.Count < 4)
        {
            Debug.LogError("[MapGenerator] 유효한 스폰 포인트가 부족합니다.");
            return false;
        }

        Vector3 p1Pos = GetRandomSpawnPoint(removePoint: true);
        NetworkClient network = NetworkClient.Instance;
        GameObject firstPrefab = PlayerPrefabForRole(network != null ? network.Role1 : PlayerRole.Detector,
            player1Prefab);
        GameObject secondPrefab = PlayerPrefabForRole(network != null ? network.Role2 : PlayerRole.Explorer,
            player2Prefab);
        if (firstPrefab == null || secondPrefab == null || monsterPrefab == null || monster2Prefab == null)
        {
            Debug.LogError("[MapGenerator] A required player or monster prefab is missing.");
            return false;
        }
        CreateGameplayGroups();
        GameObject p1 = Instantiate(firstPrefab, p1Pos, Quaternion.identity, playersRoot);
        p1.name = "Player1";
        AlignPlayerToFloor(p1, p1Pos);
        spawnedEntities.Add(p1);

        // Keep both players close enough to find the cooperative puzzle during a demo.
        Vector3 p2Pos = GetNearbySpawnPoint(p1Pos);
        GameObject p2 = Instantiate(secondPrefab, p2Pos, Quaternion.identity, playersRoot);
        p2.name = "Player2";
        AlignPlayerToFloor(p2, p2Pos);
        spawnedEntities.Add(p2);

        Vector3 monsterPos = GetFarthestSpawnPoint(p1Pos, minMonsterDistance);
        if (float.IsInfinity(monsterPos.x)) return false;
        if (NavMesh.SamplePosition(monsterPos, out NavMeshHit monsterHit, 2f, NavMesh.AllAreas) &&
            Vector2.Distance(new Vector2(monsterPos.x, monsterPos.z),
                             new Vector2(monsterHit.position.x, monsterHit.position.z)) <= 0.5f)
            monsterPos = monsterHit.position;
        else
        {
            Debug.LogError($"[MapGenerator] Monster spawn moved off its validated floor: {monsterPos}.");
            return false;
        }
        GameObject monster = Instantiate(monsterPrefab, monsterPos, Quaternion.identity, monstersRoot);
        monster.name = "Monster1";
        spawnedEntities.Add(monster);

        Vector3 monster2Pos = GetFarthestSpawnPointFromAll(p1Pos, p2Pos, monsterPos);
        if (float.IsInfinity(monster2Pos.x)) return false;
        if (NavMesh.SamplePosition(monster2Pos, out NavMeshHit monster2Hit, 2f, NavMesh.AllAreas) &&
            Vector2.Distance(new Vector2(monster2Pos.x, monster2Pos.z),
                             new Vector2(monster2Hit.position.x, monster2Hit.position.z)) <= 0.5f)
            monster2Pos = monster2Hit.position;
        else
        {
            Debug.LogError($"[MapGenerator] Monster 2 spawn moved off its validated floor: {monster2Pos}.");
            return false;
        }
        GameObject monster2 = Instantiate(monster2Prefab, monster2Pos, Quaternion.identity, monstersRoot);
        monster2.name = "Monster2";
        spawnedEntities.Add(monster2);
        Debug.Log($"[MapGenerator] Monster roots: 1={monster.transform.position}, 2={monster2.transform.position}, separation={Vector3.Distance(monster.transform.position, monster2.transform.position):F1}m.");
        LogMonsterVisualOffset(monster);
        LogMonsterVisualOffset(monster2);
        if (NavMesh.FindClosestEdge(monster2Pos, out NavMeshHit monster2Edge, NavMesh.AllAreas))
            Debug.Log($"[MapGenerator] Monster 2 spawn {monster2Pos}, NavMesh edge clearance {monster2Edge.distance:F2}m, bed blockers {bedVisualBounds.Count}.");
        GameObject[] spawnedMonsters = new GameObject[] { monster, monster2 };

        ItemData clueData = null;
        List<GameObject> ordinaryItemPrefabs = new List<GameObject>();
        if (itemPrefabs != null)
            foreach (GameObject itemPrefab in itemPrefabs)
            {
                if (itemPrefab == null) continue;
                PotionItem item = itemPrefab.GetComponent<PotionItem>();
                if (item != null && item.itemData != null && item.itemData.type == ItemData.ItemType.Clue)
                    clueData = item.itemData;
                else
                    ordinaryItemPrefabs.Add(itemPrefab);
            }
        if (clueData == null || !hasPuzzleRoom || validSpawnPoints.Count < 2)
        {
            Debug.LogError("[MapGenerator] Three fixed clues require a clue item prefab, a 1x1 room, and two free spawn points.");
            return false;
        }

        List<PotionItem> spawnedPotions = new List<PotionItem>();
        for (int clueId = 1; clueId <= 2; clueId++)
        {
            Vector3 position = GetFieldClueSpawnPoint(clueId, p1Pos, p2Pos);
            PotionItem clue = CreateCluePickup(position, clueData, clueId);
            spawnedEntities.Add(clue.gameObject);
            spawnedPotions.Add(clue);
        }

        GameObject puzzle = CoopShapePuzzle.Create(p1Pos, p2Pos, puzzleRoomCenter, tileSize);
        puzzle.transform.SetParent(puzzleRoot, true);
        spawnedEntities.Add(puzzle);
        PotionItem lockedClue = CreateCluePickup(puzzleRoomCenter, clueData, 3);
        spawnedEntities.Add(lockedClue.gameObject);
        spawnedPotions.Add(lockedClue);
        puzzle.GetComponent<CoopShapePuzzle>().SetLockedClue(lockedClue.gameObject);

        if (ordinaryItemPrefabs.Count > 0)
        {
            // Keep a recoverable run possible even when the remaining drops are random.
            List<GameObject> guaranteedItems = new List<GameObject>();
            GameObject healPrefab = ordinaryItemPrefabs.Find(prefab =>
            {
                PotionItem item = prefab.GetComponent<PotionItem>();
                return item != null && item.itemData != null && item.itemData.type == ItemData.ItemType.Heal;
            });
            GameObject revivePrefab = ordinaryItemPrefabs.Find(prefab =>
            {
                PotionItem item = prefab.GetComponent<PotionItem>();
                return item != null && item.itemData != null && item.itemData.type == ItemData.ItemType.Resurrection;
            });
            if (healPrefab != null) { guaranteedItems.Add(healPrefab); guaranteedItems.Add(healPrefab); }
            else Debug.LogWarning("[MapGenerator] No heal prefab configured for guaranteed supplies.");
            if (revivePrefab != null) guaranteedItems.Add(revivePrefab);
            else Debug.LogWarning("[MapGenerator] No resurrection prefab configured for guaranteed supplies.");

            for (int i = 0; i < Mathf.Max(itemSpawnCount, guaranteedItems.Count); i++)
            {
                if (validSpawnPoints.Count == 0)
                {
                    Debug.LogWarning($"[MapGenerator] Only {i} of {itemSpawnCount} items could be placed safely.");
                    break;
                }

                Vector3 itemPos = GetRandomSpawnPoint(removePoint: true);
                GameObject randomItemPrefab = i < guaranteedItems.Count
                    ? guaranteedItems[i]
                    : ordinaryItemPrefabs[Random.Range(0, ordinaryItemPrefabs.Count)];
                GameObject itemObj = Instantiate(randomItemPrefab, itemPos, Quaternion.identity, itemsRoot);
                spawnedEntities.Add(itemObj);

                PotionItem potion = itemObj.GetComponent<PotionItem>();
                if (potion != null)
                {
                    // ★ [추가된 핵심 코드] 복제된 아이템에 1번부터 순차적으로 고유 식별자 부여
                    potion.itemId = i + 4;
                    spawnedPotions.Add(potion);
                }
            }
        }

        if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.SetupNetworkEntities(p1, p2, spawnedMonsters, spawnedPotions.ToArray(), NetworkClient.Instance.Role1, NetworkClient.Instance.Role2);
            Debug.Log("[MapGenerator] 맵 생성 완료 및 네트워크 연동 완료");
        }
        return true;
    }

    GameObject PlayerPrefabForRole(PlayerRole role, GameObject fallback)
    {
        GameObject selected = role == PlayerRole.Detector ? player1Prefab :
            role == PlayerRole.Explorer ? player2Prefab :
            role == PlayerRole.Healer ? player3Prefab : player4Prefab;
        if (selected != null) return selected;
        Debug.LogError("[MapGenerator] Missing character prefab for role " + role + "; using slot fallback.");
        return fallback;
    }

    void AlignPlayerToFloor(GameObject player, Vector3 spawnPoint)
    {
        CharacterController capsule = player.GetComponent<CharacterController>();
        if (capsule == null) return;

        // Imported role 3/4 variants use a scaled root and a capsule centered
        // at zero. That puts half the capsule below the spawn point; Unity
        // resolves the overlap by lifting the whole player and camera.
        bool centeredAtOrigin = Mathf.Abs(capsule.center.y) < 0.1f;
        if (centeredAtOrigin)
        {
            Vector3 scale = player.transform.lossyScale;
            float heightScale = Mathf.Max(0.01f, Mathf.Abs(scale.y));
            float widthScale = Mathf.Max(0.01f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            capsule.height = 2f / heightScale;
            capsule.radius = Mathf.Min(0.5f / widthScale, capsule.height * 0.45f);
            capsule.center = Vector3.up * (capsule.height * 0.5f);
        }

        if (NavMesh.SamplePosition(spawnPoint, out NavMeshHit floor, 2f, NavMesh.AllAreas))
            player.transform.position = floor.position;
        if (centeredAtOrigin)
        {
            Camera view = player.GetComponentInChildren<Camera>(true);
            if (view != null)
            {
                Vector3 cameraPosition = view.transform.position;
                cameraPosition.y = player.transform.position.y + 1.65f;
                view.transform.position = cameraPosition;
            }
        }
    }

    PotionItem CreateCluePickup(Vector3 floorPosition, ItemData clueData, int id)
    {
        GameObject clue = new GameObject();
        clue.name = id == 3 ? "Locked room clue" : "Field clue " + id;
        clue.tag = "Clue";
        clue.transform.SetParent(cluesRoot, false);
        clue.transform.position = floorPosition + Vector3.up * 0.85f;
        AddCluePart(clue.transform, PrimitiveType.Cylinder, "Signal base",
            new Vector3(0f, -0.29f, 0f), new Vector3(0.48f, 0.025f, 0.48f),
            new Color(0.95f, 0.72f, 0.13f));
        if (id == 1)
        {
            AddCluePart(clue.transform, PrimitiveType.Cube, "Field document",
                Vector3.zero, new Vector3(0.45f, 0.055f, 0.57f), new Color(0.86f, 0.83f, 0.69f));
            AddCluePart(clue.transform, PrimitiveType.Cube, "Document spine",
                new Vector3(-0.2f, 0.035f, 0f), new Vector3(0.05f, 0.11f, 0.57f),
                new Color(0.08f, 0.4f, 0.55f));
        }
        else if (id == 2)
        {
            AddCluePart(clue.transform, PrimitiveType.Cube, "Voice recorder",
                Vector3.zero, new Vector3(0.5f, 0.17f, 0.32f), new Color(0.11f, 0.22f, 0.31f));
            AddCluePart(clue.transform, PrimitiveType.Cylinder, "Recorder reel left",
                new Vector3(-0.13f, 0.105f, 0f), new Vector3(0.1f, 0.025f, 0.1f), Color.gray);
            AddCluePart(clue.transform, PrimitiveType.Cylinder, "Recorder reel right",
                new Vector3(0.13f, 0.105f, 0f), new Vector3(0.1f, 0.025f, 0.1f), Color.gray);
        }
        else
        {
            AddCluePart(clue.transform, PrimitiveType.Cube, "Sealed evidence box",
                Vector3.zero, new Vector3(0.48f, 0.31f, 0.48f), new Color(0.58f, 0.39f, 0.13f));
            AddCluePart(clue.transform, PrimitiveType.Cube, "Evidence seal",
                new Vector3(0f, 0.165f, 0f), new Vector3(0.31f, 0.025f, 0.31f),
                new Color(0.76f, 0.15f, 0.13f));
        }
        SphereCollider collider = clue.AddComponent<SphereCollider>();
        collider.radius = 0.65f;
        collider.isTrigger = true;
        Rigidbody body = clue.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        PotionItem pickup = clue.AddComponent<PotionItem>();
        pickup.itemId = id;
        pickup.itemData = clueData;
        clue.AddComponent<ClueVisibilityByRole>();
        return pickup;
    }

    static void AddCluePart(Transform parent, PrimitiveType shape, string label,
        Vector3 localPosition, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(shape);
        part.name = label;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) { collider.enabled = false; Destroy(collider); }
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        part.GetComponent<Renderer>().material = material;
    }

    Vector3 GetNearbySpawnPoint(Vector3 origin)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.SamplePosition(origin, out NavMeshHit start, tileSize, NavMesh.AllAreas))
            return GetRandomSpawnPoint(removePoint: true);
        for (int i = 0; i < validSpawnPoints.Count; i++)
        {
            float distance = Vector3.Distance(origin, validSpawnPoints[i]);
            if (distance < tileSize * 0.75f || distance > tileSize * 3f || distance >= bestDistance) continue;
            if (!NavMesh.SamplePosition(validSpawnPoints[i], out NavMeshHit destination, tileSize, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(start.position, destination.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete) continue;
            best = i;
            bestDistance = distance;
        }
        if (best < 0) return GetRandomSpawnPoint(removePoint: true);
        Vector3 result = validSpawnPoints[best];
        validSpawnPoints.RemoveAt(best);
        return result;
    }

    Vector3 GetRandomSpawnPoint(bool removePoint)
    {
        int index = Random.Range(0, validSpawnPoints.Count);
        Vector3 pos = validSpawnPoints[index];
        if (removePoint) validSpawnPoints.RemoveAt(index);
        return pos;
    }

    Vector3 GetFieldClueSpawnPoint(int clueId, Vector3 player1, Vector3 player2)
    {
        Vector3 start = (player1 + player2) * 0.5f;
        Vector3 roomApproach = puzzleRoomCenter + Vector3.back * tileSize;
        int bestIndex = -1;
        float bestScore = float.MaxValue;
        float minimumStartDistance = tileSize * (clueId == 1 ? 1.25f : 2f);
        for (int i = 0; i < validSpawnPoints.Count; i++)
        {
            Vector3 candidate = validSpawnPoints[i];
            float fromStart = Vector3.Distance(start, candidate);
            if (fromStart < minimumStartDistance) continue;
            float toRoom = Vector3.Distance(candidate, roomApproach);
            float score = clueId == 1
                ? fromStart + toRoom * 0.15f
                : toRoom - fromStart * 0.2f;
            if (score >= bestScore) continue;
            bestIndex = i;
            bestScore = score;
        }
        if (bestIndex < 0) return GetRandomSpawnPoint(removePoint: true);
        Vector3 result = validSpawnPoints[bestIndex];
        validSpawnPoints.RemoveAt(bestIndex);
        return result;
    }

    Vector3 GetFarthestSpawnPoint(Vector3 fromPosition, float minRequiredDistance)
    {
        Vector3 bestPos = Vector3.positiveInfinity;
        float maxDist = -1f;
        int bestIndex = -1;

        for (int i = 0; i < corridorMonsterSpawns.Count; i++)
        {
            if (!IsMonsterSpawnClear(corridorMonsterSpawns[i])) continue;
            float dist = Vector3.Distance(fromPosition, corridorMonsterSpawns[i]);
            if (dist > maxDist)
            {
                maxDist = dist;
                bestPos = corridorMonsterSpawns[i];
                bestIndex = i;
            }
        }

        if (bestIndex < 0) { Debug.LogError("[MapGenerator] No clear corridor monster spawn remains."); return Vector3.positiveInfinity; }
        if (maxDist < minRequiredDistance)
        {
            Debug.LogWarning($"최소 스폰 거리({minRequiredDistance}) 불만족. 가장 먼 곳에 스폰.");
        }

        corridorMonsterSpawns.RemoveAt(bestIndex);
        return bestPos;
    }

    Vector3 GetFarthestSpawnPointFromAll(params Vector3[] positions)
    {
        int bestIndex = -1;
        float bestMinimumDistance = -1f;
        for (int i = 0; i < corridorMonsterSpawns.Count; i++)
        {
            if (!IsMonsterSpawnClear(corridorMonsterSpawns[i])) continue;
            float minimumDistance = float.MaxValue;
            foreach (Vector3 position in positions)
                minimumDistance = Mathf.Min(minimumDistance, Vector3.Distance(position, corridorMonsterSpawns[i]));

            if (minimumDistance > bestMinimumDistance)
            {
                bestMinimumDistance = minimumDistance;
                bestIndex = i;
            }
        }

        if (bestIndex < 0) { Debug.LogError("[MapGenerator] No clear second corridor monster spawn remains."); return Vector3.positiveInfinity; }
        Vector3 result = corridorMonsterSpawns[bestIndex];
        corridorMonsterSpawns.RemoveAt(bestIndex);
        return result;
    }

    bool IsMonsterSpawnClear(Vector3 candidate)
    {
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit floor, 2f, NavMesh.AllAreas))
            return false;
        if (new Vector2(candidate.x - floor.position.x, candidate.z - floor.position.z).magnitude > 0.5f)
            return false;
        // A valid NavMesh center can still put the monster's body or visual
        // mesh across the edge. Check footprint clearance, not just the pivot.
        const float edgeClearance = 1.25f;
        if (!NavMesh.FindClosestEdge(floor.position, out NavMeshHit edge, NavMesh.AllAreas) ||
            edge.distance < edgeClearance)
            return false;
        return IsMonsterBodyClear(floor.position, 1.25f);
    }

    // Server waypoints also need body clearance. NavMesh point/edge checks
    // alone can put a server-driven monster inside a bed or wall.
    bool IsMonsterBodyClear(Vector3 position, float bedClearance)
    {
        // Imported concave beds can leave walkable NavMesh under the visible
        // mattress, so exclude their full visual footprint as well.
        foreach (Bounds bed in bedVisualBounds)
        {
            if (position.y > bed.max.y + 0.1f ||
                position.y + 1.7f < bed.min.y) continue;
            float closestX = Mathf.Clamp(position.x, bed.min.x, bed.max.x);
            float closestZ = Mathf.Clamp(position.z, bed.min.z, bed.max.z);
            float dx = position.x - closestX;
            float dz = position.z - closestZ;
            if (dx * dx + dz * dz < bedClearance * bedClearance) return false;
        }
        const float radius = 0.6f;
        Vector3 bottom = position + Vector3.up * (radius + 0.1f);
        Vector3 top = position + Vector3.up * 1.5f;
        return !Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
    }

    static void LogMonsterVisualOffset(GameObject monster)
    {
        foreach (SkinnedMeshRenderer visual in monster.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            float offset = Vector3.Distance(visual.bounds.center, monster.transform.position);
            if (offset > 5f)
                Debug.LogWarning($"[MapGenerator] {monster.name} mesh '{visual.name}' is {offset:F1}m from its root at spawn; mesh center={visual.bounds.center}.");
        }
    }

}
