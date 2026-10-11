using System.Collections.Generic;
using UnityEngine;

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
    public GameObject[] itemPrefabs;

    [Header("스폰 설정")]
    public float minMonsterDistance = 30f;
    public int itemSpawnCount = 20;

    [Header("Spawn Clearance")]
    [Min(0.1f)] public float spawnRadius = 0.45f;
    [Min(0.5f)] public float spawnHeight = 1.8f;
    [Range(1, 50)] public int maxGenerationAttempts = 12;

    // 맵 상태: 0 = 벽/빈공간, 1 = 복도, 2 = 방 내부, 3 = 방 문
    private int[,] map;
    private List<Vector2Int> roomDoors = new List<Vector2Int>();
    private List<Vector3> validSpawnPoints = new List<Vector3>();
    private readonly List<GameObject> spawnedEntities = new List<GameObject>();

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

        foreach (GameObject entity in spawnedEntities)
            DisposeGeneratedObject(entity);
        spawnedEntities.Clear();

        bool generated = false;
        int selectedSeed = seed;
        for (int attempt = 0; attempt < maxGenerationAttempts; attempt++)
        {
            ClearMapChildren();
            selectedSeed = seed + attempt;
            Random.InitState(selectedSeed);
            map = new int[mapSize, mapSize];
            roomDoors.Clear();
            validSpawnPoints.Clear();

            if (!PlaceRooms()) continue;
            BuildFullMaze();
            EnsureGlobalConnectivity();
            LoopDeadEnds();
            if (FindConnectedAreas().Count == 1)
            {
                generated = true;
                break;
            }
        }

        if (!generated)
        {
            ClearMapChildren();
            Debug.LogError($"[MapGenerator] Seed {seed}: all {maxGenerationAttempts} layout attempts failed.");
            return;
        }

        SpawnPrefabsSafely();
        Physics.SyncTransforms();
        CollectReachableSpawnPoints();
        Debug.Log($"[MapGenerator] Seed {selectedSeed}: {roomDoors.Count} rooms, {validSpawnPoints.Count} safe spawn points.");
        SpawnEntities();
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

        for (int i = 0; i < roomsToSpawn.Count; i++)
        {
            var room = roomsToSpawn[i];
            if (room == null || room.prefab == null)
            {
                Debug.LogWarning($"[MapGenerator] Room entry {i} has a missing prefab; check the scene reference.");
                continue;
            }
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
                int rx = Random.Range(1, maxX);
                int rz = Random.Range(2, maxZ);

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

                    float posX = startOffset + (rx * tileSize) + ((room.sizeX - 1) * tileSize / 2f);
                    float posZ = startOffset + (rz * tileSize) + ((room.sizeZ - 1) * tileSize / 2f);
                    Instantiate(room.prefab, new Vector3(posX, 0, posZ), Quaternion.identity, transform);

                    placed = true;
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

    void SpawnPrefabsSafely()
    {
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (map[x, z] == 1 || map[x, z] == 3)
                {
                    bool wallTop = !IsPath(x, z, x, z + 1);
                    bool wallRight = !IsPath(x, z, x + 1, z);
                    bool wallBottom = !IsPath(x, z, x, z - 1);
                    bool wallLeft = !IsPath(x, z, x - 1, z);

                    int mask = 0;
                    if (wallTop) mask += 1;
                    if (wallRight) mask += 2;
                    if (wallBottom) mask += 4;
                    if (wallLeft) mask += 8;

                    Vector3 pos = new Vector3(startOffset + (x * tileSize), 0, startOffset + (z * tileSize));

                    switch (mask)
                    {
                        case 0: break;
                        case 1: SpawnWall(prefabI, pos, Quaternion.Euler(0, 0, 0)); break;
                        case 2: SpawnWall(prefabI, pos, Quaternion.Euler(0, 90, 0)); break;
                        case 4: SpawnWall(prefabI, pos, Quaternion.Euler(0, 180, 0)); break;
                        case 8: SpawnWall(prefabI, pos, Quaternion.Euler(0, 270, 0)); break;

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

                        case 7: SpawnWall(prefabU, pos, Quaternion.Euler(0, 0, 0)); break;
                        case 14: SpawnWall(prefabU, pos, Quaternion.Euler(0, 90, 0)); break;
                        case 13: SpawnWall(prefabU, pos, Quaternion.Euler(0, 180, 0)); break;
                        case 11: SpawnWall(prefabU, pos, Quaternion.Euler(0, 270, 0)); break;

                        case 15:
                            SpawnWall(prefabO, pos, Quaternion.identity);
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
            Instantiate(prefab, pos, rot, transform);
        }
    }

    bool IsPath(int cx, int cz, int nx, int nz)
    {
        return IsConnectedEdge(new Vector2Int(cx, cz), new Vector2Int(nx, nz));
    }

    // 시작점에서 도달 가능한 타일만 BFS로 수집 (갇힌 공간 스폰 원천 차단)
    void CollectReachableSpawnPoints()
    {
        validSpawnPoints.Clear();
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);

        Vector2Int root = new Vector2Int(mapSize / 2, mapSize / 2);
        if (roomDoors.Count > 0)
        {
            root = roomDoors[0];
        }

        bool[,] visited = new bool[mapSize, mapSize];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        queue.Enqueue(root);
        visited[root.x, root.y] = true;

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();

            if (map[curr.x, curr.y] == 1 || map[curr.x, curr.y] == 2)
            {
                Vector3 worldPos = new Vector3(startOffset + (curr.x * tileSize), 1f, startOffset + (curr.y * tileSize));
                if (IsSpawnClear(worldPos)) validSpawnPoints.Add(worldPos);
            }

            foreach (var dir in dirs)
            {
                int nx = curr.x + dir.x;
                int nz = curr.y + dir.y;

                if (nx >= 0 && nx < mapSize && nz >= 0 && nz < mapSize)
                {
                    if (IsConnectedEdge(curr, new Vector2Int(nx, nz)) && !visited[nx, nz])
                    {
                        visited[nx, nz] = true;
                        queue.Enqueue(new Vector2Int(nx, nz));
                    }
                }
            }
        }
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
        if (validSpawnPoints.Count < 3)
        {
            Debug.LogError("[MapGenerator] 유효한 스폰 포인트가 부족합니다.");
            return;
        }

        Vector3 p1Pos = GetRandomSpawnPoint(removePoint: true);
        GameObject p1 = Instantiate(player1Prefab, p1Pos, Quaternion.identity);
        p1.name = "Player1";
        spawnedEntities.Add(p1);

        Vector3 p2Pos = GetRandomSpawnPoint(removePoint: true);
        GameObject p2 = Instantiate(player2Prefab, p2Pos, Quaternion.identity);
        p2.name = "Player2";
        spawnedEntities.Add(p2);

        Vector3 monsterPos = GetFarthestSpawnPoint(p1Pos, minMonsterDistance);
        GameObject monster = Instantiate(monsterPrefab, monsterPos, Quaternion.identity);
        monster.name = "Monster";
        spawnedEntities.Add(monster);
        GameObject[] spawnedMonsters = new GameObject[] { monster };

        List<PotionItem> spawnedPotions = new List<PotionItem>();
        if (itemPrefabs != null && itemPrefabs.Length > 0)
        {
            for (int i = 0; i < itemSpawnCount; i++)
            {
                if (validSpawnPoints.Count == 0)
                {
                    Debug.LogWarning($"[MapGenerator] Only {i} of {itemSpawnCount} items could be placed safely.");
                    break;
                }

                Vector3 itemPos = GetRandomSpawnPoint(removePoint: true);
                GameObject randomItemPrefab = itemPrefabs[Random.Range(0, itemPrefabs.Length)];
                GameObject itemObj = Instantiate(randomItemPrefab, itemPos, Quaternion.identity);
                spawnedEntities.Add(itemObj);

                PotionItem potion = itemObj.GetComponent<PotionItem>();
                if (potion != null)
                {
                    // ★ [추가된 핵심 코드] 복제된 아이템에 1번부터 순차적으로 고유 식별자 부여
                    potion.itemId = i + 1;
                    spawnedPotions.Add(potion);
                }
            }
        }

        if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.SetupNetworkEntities(p1, p2, spawnedMonsters, spawnedPotions.ToArray(), PlayerRole.Healer, PlayerRole.Runner);
            Debug.Log("[MapGenerator] 맵 생성 완료 및 네트워크 연동 완료");
        }
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
}