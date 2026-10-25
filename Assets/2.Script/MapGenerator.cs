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
    private readonly HashSet<Vector2Int> oneByOneRoomCells = new HashSet<Vector2Int>();
    private readonly HashSet<long> openEmptyEdges = new HashSet<long>();
    private readonly List<List<long>> emptyRegionCandidates = new List<List<long>>();
    private readonly List<GameObject> spawnedWalls = new List<GameObject>();
    private readonly List<GameObject> spawnedEntities = new List<GameObject>();
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
            return;
        }
        for (int i = 0; i < roomsToSpawn.Count; i++)
        {
            if (roomsToSpawn[i] == null || roomsToSpawn[i].prefab == null)
            {
                Debug.LogError($"[MapGenerator] Room entry {i} has a missing prefab; map generation cancelled.");
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
            selectedSeed = seed + attempt;
            Random.InitState(selectedSeed);
            map = new int[mapSize, mapSize];
            roomDoors.Clear();
            oneByOneRoomCells.Clear();
            validSpawnPoints.Clear();
            walkableCenters.Clear();
            openEmptyEdges.Clear();
            emptyRegionCandidates.Clear();
            spawnedWalls.Clear();
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
            if (!CollectReachableSpawnPoints() || validSpawnPoints.Count < 6) { spawnFailures++; continue; }
            RebuildNavMesh();
            if (!ValidateNavMeshConnectivity() || !ValidatePuzzleRoomAccess()) { navigationFailures++; continue; }

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
            return;
        }

        Debug.Log($"[MapGenerator] Seed {selectedSeed}: {roomDoors.Count} rooms, {validSpawnPoints.Count} safe spawn points.");
        NetworkClient network = NetworkClient.Instance;
        string navigationMask = network != null && !network.offlineMode && network.IsConnected
            ? BuildServerNavigationMask() : null;
        SpawnEntities();
        SendServerNavigationMap(selectedSeed, navigationMask);
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
            return;
        }

        const float step = 1f;
        float worldSize = mapSize * tileSize;
        int width = Mathf.CeilToInt(worldSize / step);
        int height = width;
        float origin = -worldSize * 0.5f + step * 0.5f;
        if (string.IsNullOrEmpty(edgeMask) || edgeMask.Length != width * height) return;
        Vector3 gate = puzzleRoomCenter + Vector3.back * (tileSize * 0.5f);
        network.SendNavigationMap(seed, width, height, origin, origin, step,
                                  gate.x, gate.z, tileSize * 0.5f - 0.2f,
                                  monsterPositions[0], monsterPositions[1], edgeMask);
    }

    string BuildServerNavigationMask()
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
                                 new Vector2(hit.position.x, hit.position.z)) <= 0.4f)
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
                !NavMesh.Raycast(points[index], points[index + 1], out _, NavMesh.AllAreas))
            {
                edges[index] |= 1;
                edges[index + 1] |= 2;
            }
            if (z + 1 < height && walkable[index + width] &&
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
        navMeshSurface.RemoveData();
        navMeshSurface.BuildNavMesh();
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
    }

    bool PlaceRooms()
    {
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);
        bool centralRoomPlaced = false;
        int nextMimicId = 1;

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
                                                          Quaternion.identity, transform);
                    foreach (MimicLaptop mimic in roomInstance.GetComponentsInChildren<MimicLaptop>(true))
                        mimic.SetNetworkId(nextMimicId++);

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
            spawnedWalls.Add(Instantiate(prefab, pos, rot, transform));
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

    void SpawnEntities()
    {
        if (validSpawnPoints.Count < 4)
        {
            Debug.LogError("[MapGenerator] 유효한 스폰 포인트가 부족합니다.");
            return;
        }

        Vector3 p1Pos = GetRandomSpawnPoint(removePoint: true);
        GameObject p1 = Instantiate(player1Prefab, p1Pos, Quaternion.identity);
        p1.name = "Player1";
        spawnedEntities.Add(p1);

        // Keep both players close enough to find the cooperative puzzle during a demo.
        Vector3 p2Pos = GetNearbySpawnPoint(p1Pos);
        GameObject p2 = Instantiate(player2Prefab, p2Pos, Quaternion.identity);
        p2.name = "Player2";
        spawnedEntities.Add(p2);

        Vector3 monsterPos = GetFarthestSpawnPoint(p1Pos, minMonsterDistance);
        if (NavMesh.SamplePosition(monsterPos, out NavMeshHit monsterHit, tileSize, NavMesh.AllAreas))
            monsterPos = monsterHit.position;
        else
            Debug.LogWarning($"[MapGenerator] No NavMesh position found near monster spawn {monsterPos}.");
        GameObject monster = Instantiate(monsterPrefab, monsterPos, Quaternion.identity);
        monster.name = "Monster1";
        spawnedEntities.Add(monster);

        if (monster2Prefab == null)
        {
            Debug.LogError("[MapGenerator] Monster2 prefab is not assigned.");
            return;
        }

        Vector3 monster2Pos = GetFarthestSpawnPointFromAll(p1Pos, p2Pos, monsterPos);
        if (NavMesh.SamplePosition(monster2Pos, out NavMeshHit monster2Hit, tileSize, NavMesh.AllAreas))
            monster2Pos = monster2Hit.position;
        GameObject monster2 = Instantiate(monster2Prefab, monster2Pos, Quaternion.identity);
        monster2.name = "Monster2";
        spawnedEntities.Add(monster2);
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
            return;
        }

        List<PotionItem> spawnedPotions = new List<PotionItem>();
        for (int clueId = 1; clueId <= 2; clueId++)
        {
            Vector3 position = GetRandomSpawnPoint(removePoint: true);
            PotionItem clue = CreateCluePickup(position, clueData, clueId);
            spawnedEntities.Add(clue.gameObject);
            spawnedPotions.Add(clue);
        }

        GameObject puzzle = CoopShapePuzzle.Create(p1Pos, p2Pos, puzzleRoomCenter, tileSize);
        spawnedEntities.Add(puzzle);
        PotionItem lockedClue = CreateCluePickup(puzzleRoomCenter, clueData, 3);
        spawnedEntities.Add(lockedClue.gameObject);
        spawnedPotions.Add(lockedClue);
        puzzle.GetComponent<CoopShapePuzzle>().SetLockedClue(lockedClue.gameObject);

        if (ordinaryItemPrefabs.Count > 0)
        {
            for (int i = 0; i < itemSpawnCount; i++)
            {
                if (validSpawnPoints.Count == 0)
                {
                    Debug.LogWarning($"[MapGenerator] Only {i} of {itemSpawnCount} items could be placed safely.");
                    break;
                }

                Vector3 itemPos = GetRandomSpawnPoint(removePoint: true);
                GameObject randomItemPrefab = ordinaryItemPrefabs[Random.Range(0, ordinaryItemPrefabs.Count)];
                GameObject itemObj = Instantiate(randomItemPrefab, itemPos, Quaternion.identity);
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

    }

    PotionItem CreateCluePickup(Vector3 floorPosition, ItemData clueData, int id)
    {
        GameObject clue = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        clue.name = id == 3 ? "Locked room clue" : "Field clue " + id;
        clue.tag = "Clue";
        clue.transform.position = floorPosition + Vector3.up * 0.85f;
        clue.transform.localScale = Vector3.one * 0.55f;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = new Color(1f, 0.78f, 0.12f);
        clue.GetComponent<Renderer>().material = material;
        SphereCollider collider = clue.GetComponent<SphereCollider>();
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

    Vector3 GetFarthestSpawnPoint(Vector3 fromPosition, float minRequiredDistance)
    {
        Vector3 bestPos = validSpawnPoints[0];
        float maxDist = -1f;
        int bestIndex = 0;

        for (int i = 0; i < validSpawnPoints.Count; i++)
        {
            float dist = Vector3.Distance(fromPosition, validSpawnPoints[i]);
            if (dist > maxDist)
            {
                maxDist = dist;
                bestPos = validSpawnPoints[i];
                bestIndex = i;
            }
        }

        if (maxDist < minRequiredDistance)
        {
            Debug.LogWarning($"최소 스폰 거리({minRequiredDistance}) 불만족. 가장 먼 곳에 스폰.");
        }

        validSpawnPoints.RemoveAt(bestIndex);
        return bestPos;
    }

    Vector3 GetFarthestSpawnPointFromAll(params Vector3[] positions)
    {
        int bestIndex = 0;
        float bestMinimumDistance = -1f;
        for (int i = 0; i < validSpawnPoints.Count; i++)
        {
            float minimumDistance = float.MaxValue;
            foreach (Vector3 position in positions)
                minimumDistance = Mathf.Min(minimumDistance, Vector3.Distance(position, validSpawnPoints[i]));

            if (minimumDistance > bestMinimumDistance)
            {
                bestMinimumDistance = minimumDistance;
                bestIndex = i;
            }
        }

        Vector3 result = validSpawnPoints[bestIndex];
        validSpawnPoints.RemoveAt(bestIndex);
        return result;
    }
}
