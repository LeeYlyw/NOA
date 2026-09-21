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
    // (prefabParallel은 이전 논의대로 사용하지 않고 prefabI 2개로 막습니다)

    [Header("배치할 방 목록")]
    public List<RoomData> roomsToSpawn;

    [Header("스폰할 프리팹 설정")]
    public GameObject player1Prefab;
    public GameObject player2Prefab;
    public GameObject monsterPrefab;
    public GameObject[] itemPrefabs;

    [Header("스폰 설정")]
    public float minMonsterDistance = 30f;
    public int itemSpawnCount = 5;

    private List<Vector3> validSpawnPoints = new List<Vector3>();
    private int[,] map;
    private List<Vector2Int> roomDoors = new List<Vector2Int>();

    void Start()
    {
        GenerateMap();
    }

    void GenerateMap()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        map = new int[mapSize, mapSize];
        roomDoors.Clear();

        PlaceRooms();

        // 1. 미로 생성 (가장자리 평행 이동 금지 규칙 적용)
        BuildMaze();

        // 2. 3x3 거대 벽 덩어리 분쇄
        BreakThickWalls();

        // 3. 고립된 섬 연결 (부자연스러운 직선 제거, 구불구불하게 연결)
        EnsureGlobalConnectivity();

        // 4. 막다른 길 순환로 만들기
        LoopDeadEnds();

        SpawnPrefabsSafely();
        CollectValidSpawnPoints();
        SpawnEntities();
    }

    void PlaceRooms()
    {
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);

        for (int i = 0; i < roomsToSpawn.Count; i++)
        {
            var room = roomsToSpawn[i];
            if (room.prefab == null) continue;

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
        }
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

    void BuildMaze()
    {
        foreach (var door in roomDoors)
        {
            CarveMaze(door.x, door.y);
        }

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

    // ==========================================
    // [핵심 수정 1] 1자형 외곽 도로 원천 차단
    // ==========================================
    void CarveMaze(int startX, int startZ)
    {
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        stack.Push(new Vector2Int(startX, startZ));

        if (map[startX, startZ] == 0) map[startX, startZ] = 1;

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (stack.Count > 0)
        {
            Vector2Int curr = stack.Peek();

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

                if (nx > 0 && nx < mapSize - 1 && nz > 0 && nz < mapSize - 1)
                {
                    // ★ 외곽 평행 이동 금지 규칙 ★
                    // 현재 위치가 가장자리(x=1 or mapSize-2)인데, 다음 이동할 곳도 가장자리라면 이동 금지.
                    // 이 규칙 덕분에 미로가 벽에 닿자마자 멈추고 막다른 길(내부 벽)을 형성합니다.
                    bool currIsEdge = (curr.x == 1 || curr.x == mapSize - 2 || curr.y == 1 || curr.y == mapSize - 2);
                    bool nextIsEdge = (nx == 1 || nx == mapSize - 2 || nz == 1 || nz == mapSize - 2);

                    if (currIsEdge && nextIsEdge) continue;

                    if (map[nx, nz] == 0)
                    {
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

    void BreakThickWalls()
    {
        for (int x = 2; x < mapSize - 2; x++)
        {
            for (int z = 2; z < mapSize - 2; z++)
            {
                if (map[x, z] == 0)
                {
                    bool isThick = true;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (map[x + dx, z + dz] != 0) isThick = false;
                        }
                    }

                    if (isThick)
                    {
                        map[x, z] = 1;
                    }
                }
            }
        }
    }

    // ==========================================
    // [핵심 수정 2] 부자연스러운 직선 연결 방지
    // ==========================================
    void EnsureGlobalConnectivity()
    {
        bool[,] visited = new bool[mapSize, mapSize];
        List<List<Vector2Int>> islands = new List<List<Vector2Int>>();

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (map[x, z] != 0 && !visited[x, z])
                {
                    List<Vector2Int> newIsland = new List<Vector2Int>();
                    Queue<Vector2Int> queue = new Queue<Vector2Int>();
                    queue.Enqueue(new Vector2Int(x, z));
                    visited[x, z] = true;

                    while (queue.Count > 0)
                    {
                        Vector2Int curr = queue.Dequeue();
                        newIsland.Add(curr);

                        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
                        foreach (var dir in dirs)
                        {
                            Vector2Int next = curr + dir;
                            if (next.x >= 0 && next.x < mapSize && next.y >= 0 && next.y < mapSize)
                            {
                                if (map[next.x, next.y] != 0 && !visited[next.x, next.y])
                                {
                                    visited[next.x, next.y] = true;
                                    queue.Enqueue(next);
                                }
                            }
                        }
                    }
                    islands.Add(newIsland);
                }
            }
        }

        if (islands.Count <= 1) return;

        islands.Sort((a, b) => b.Count.CompareTo(a.Count));
        List<Vector2Int> mainIsland = islands[0];

        for (int i = 1; i < islands.Count; i++)
        {
            ConnectIslands(islands[i], mainIsland);
        }
    }

    void ConnectIslands(List<Vector2Int> islandA, List<Vector2Int> islandB)
    {
        Vector2Int bestA = islandA[0];
        Vector2Int bestB = islandB[0];
        float minDist = float.MaxValue;

        foreach (var a in islandA)
        {
            foreach (var b in islandB)
            {
                float dist = Vector2Int.Distance(a, b);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestA = a;
                    bestB = b;
                }
            }
        }

        Vector2Int curr = bestA;
        while (curr != bestB)
        {
            // ★ 일직선(레이저)으로 뚫지 않고 무작위로 꺾어가며 길을 냄 ★
            bool moveX = false;
            if (curr.x != bestB.x && curr.y != bestB.y)
                moveX = Random.value > 0.5f; // 대각선 방향일 땐 50% 확률로 X나 Z를 선택
            else
                moveX = (curr.x != bestB.x);

            if (moveX)
                curr.x += (curr.x < bestB.x) ? 1 : -1;
            else
                curr.y += (curr.y < bestB.y) ? 1 : -1;

            if (map[curr.x, curr.y] == 0) map[curr.x, curr.y] = 1;
        }
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

                        // prefabParallel 삭제 -> prefabI 2개로 양면 막기
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
                            map[x, z] = 0;
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
        if (nx < 0 || nx >= mapSize || nz < 0 || nz >= mapSize) return false;

        int neighbor = map[nx, nz];
        if (neighbor == 1 || neighbor == 3) return true;
        if (neighbor == 2) return true;

        return false;
    }

    void CollectValidSpawnPoints()
    {
        validSpawnPoints.Clear();
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (map[x, z] == 1 || map[x, z] == 2)
                {
                    Vector3 pos = new Vector3(startOffset + (x * tileSize), 1f, startOffset + (z * tileSize));
                    validSpawnPoints.Add(pos);
                }
            }
        }
    }

    void SpawnEntities()
    {
        Vector3 p1Pos = GetRandomSpawnPoint(removePoint: true);
        GameObject p1 = Instantiate(player1Prefab, p1Pos, Quaternion.identity);
        p1.name = "Player1";

        Vector3 p2Pos = GetRandomSpawnPoint(removePoint: true);
        GameObject p2 = Instantiate(player2Prefab, p2Pos, Quaternion.identity);
        p2.name = "Player2";

        Vector3 monsterPos = GetFarthestSpawnPoint(p1Pos, minMonsterDistance);
        GameObject monster = Instantiate(monsterPrefab, monsterPos, Quaternion.identity);
        monster.name = "Monster";
        GameObject[] spawnedMonsters = new GameObject[] { monster };

        List<PotionItem> spawnedPotions = new List<PotionItem>();
        if (itemPrefabs != null && itemPrefabs.Length > 0)
        {
            for (int i = 0; i < itemSpawnCount; i++)
            {
                Vector3 itemPos = GetRandomSpawnPoint(removePoint: true);
                GameObject randomItemPrefab = itemPrefabs[Random.Range(0, itemPrefabs.Length)];
                GameObject itemObj = Instantiate(randomItemPrefab, itemPos, Quaternion.identity);

                PotionItem potion = itemObj.GetComponent<PotionItem>();
                if (potion != null) spawnedPotions.Add(potion);
            }
        }

        if (NetworkClient.Instance != null)
        {
            NetworkClient.Instance.SetupNetworkEntities(p1, p2, spawnedMonsters, spawnedPotions.ToArray());
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