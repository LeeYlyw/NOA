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

    [Header("복도 모듈 프리팹 (단면 I벽, 모서리 L벽)")]
    public GameObject prefabI;    // 일자 (단면 벽 1개)
    public GameObject prefabL;    // ㄱ자 (코너 벽)

    [Header("배치할 방 목록")]
    public List<RoomData> roomsToSpawn;

    [Header("스폰할 프리팹 설정")]
    public GameObject player1Prefab;
    public GameObject player2Prefab;
    public GameObject monsterPrefab;
    public GameObject[] itemPrefabs;

    [Header("스폰 설정")]
    public float minMonsterDistance = 30f; // 몬스터가 플레이어로부터 떨어져야 하는 최소 거리
    public int itemSpawnCount = 5;         // 생성할 아이템 개수

    // 스폰 위치 저장을 위한 리스트
    private List<Vector3> validSpawnPoints = new List<Vector3>();

    // 0: 빈공간, 1: 복도, 2: 방 본체, 3: 방 출입문(Door)
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

        // 1. 방 사이를 연결하고 맵 전체로 복도를 뻗어냄
        BuildNonLinearMaze();

        // 2. 혹시라도 남은 3x3 이상의 큰 공터가 있다면 억지로 복도를 뚫어버림
        ForceFillPockets();

        // 3. 맵 내부에 갇힌 1~2칸짜리 찌꺼기 빈 공간을 플러드 필로 완벽히 메움
        CleanUpMap();

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

    void BuildNonLinearMaze()
    {
        if (roomDoors.Count < 2) return;

        // 1. 출입구 연결
        for (int i = 0; i < roomDoors.Count; i++)
        {
            Vector2Int start = roomDoors[i];
            Vector2Int end = roomDoors[(i + 1) % roomDoors.Count];
            ConnectWithRandomTurn(start, end);
        }

        // 2. 맵의 4개 외곽 모서리 끝까지 강제로 뼈대를 끌고 감 (외곽 빈공간 차단)
        Vector2Int[] cornerPoints = new Vector2Int[]
        {
            new Vector2Int(1, 1),
            new Vector2Int(mapSize - 2, 1),
            new Vector2Int(1, mapSize - 2),
            new Vector2Int(mapSize - 2, mapSize - 2)
        };

        foreach (var corner in cornerPoints)
        {
            if (map[corner.x, corner.y] == 0) // 모서리가 비어있다면 억지로 연결
            {
                Vector2Int nearest = FindNearestCorridor(corner);
                if (nearest.x != -1) ConnectWithRandomTurn(nearest, corner);
            }
        }

        // 3. 기존 15번 -> 300번으로 무한 증식시켜서 남는 잉여 공간을 꽉꽉 채움
        GrowMazeBranches(300);
    }

    void ConnectWithRandomTurn(Vector2Int start, Vector2Int end)
    {
        Vector2Int mid = new Vector2Int(
            Random.value > 0.5f ? start.x : end.x,
            Random.value > 0.5f ? end.y : start.y
        );

        mid.x = Mathf.Clamp(mid.x, 0, mapSize - 1);
        mid.y = Mathf.Clamp(mid.y, 0, mapSize - 1);

        BFSConnect(start, mid);
        BFSConnect(mid, end);
    }

    void GrowMazeBranches(int branchCount)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        for (int i = 0; i < branchCount; i++)
        {
            List<Vector2Int> corridors = new List<Vector2Int>();
            for (int x = 0; x < mapSize; x++)
            {
                for (int z = 0; z < mapSize; z++)
                {
                    if (map[x, z] == 1) corridors.Add(new Vector2Int(x, z));
                }
            }

            if (corridors.Count == 0) break;

            Vector2Int curr = corridors[Random.Range(0, corridors.Count)];
            int steps = Random.Range(5, 16); // 기존 3~7에서 길이를 늘려 구석까지 파고들게 함

            for (int s = 0; s < steps; s++)
            {
                Vector2Int dir = dirs[Random.Range(0, 4)];
                Vector2Int next = curr + dir;

                if (next.x >= 0 && next.x < mapSize && next.y >= 0 && next.y < mapSize)
                {
                    if (map[next.x, next.y] == 0)
                    {
                        map[next.x, next.y] = 1;
                        curr = next;
                    }
                    else break;
                }
                else break;
            }
        }
    }

    Vector2Int FindNearestCorridor(Vector2Int point)
    {
        Vector2Int nearest = new Vector2Int(-1, -1);
        float minDist = float.MaxValue;

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (map[x, z] == 1)
                {
                    float dist = Vector2Int.Distance(point, new Vector2Int(x, z));
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = new Vector2Int(x, z);
                    }
                }
            }
        }
        return nearest;
    }

    void BFSConnect(Vector2Int start, Vector2Int end)
    {
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();

        queue.Enqueue(start);
        cameFrom[start] = start;

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();
            if (curr == end) break;

            foreach (var dir in dirs)
            {
                Vector2Int next = curr + dir;
                if (next.x >= 0 && next.x < mapSize && next.y >= 0 && next.y < mapSize)
                {
                    if (!cameFrom.ContainsKey(next))
                    {
                        if (map[next.x, next.y] != 2)
                        {
                            queue.Enqueue(next);
                            cameFrom[next] = curr;
                        }
                    }
                }
            }
        }

        if (cameFrom.ContainsKey(end))
        {
            Vector2Int curr = end;
            while (curr != start)
            {
                if (map[curr.x, curr.y] == 0) map[curr.x, curr.y] = 1;
                curr = cameFrom[curr];
            }
        }
    }

    // --- [신규 추가] 외곽의 남는 공터를 찾아내서 확정적으로 뚫어버림 ---
    void ForceFillPockets()
    {
        for (int x = 1; x < mapSize - 1; x++)
        {
            for (int z = 1; z < mapSize - 1; z++)
            {
                if (map[x, z] == 0)
                {
                    bool completelyEmpty = true;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (map[x + dx, z + dz] != 0) completelyEmpty = false;
                        }
                    }

                    if (completelyEmpty)
                    {
                        map[x, z] = 1;
                        Vector2Int nearest = FindNearestCorridor(new Vector2Int(x, z));
                        if (nearest.x != -1) ConnectWithRandomTurn(new Vector2Int(x, z), nearest);
                    }
                }
            }
        }
    }

    // --- [신규 추가] 맵 내부에 갇힌 찌꺼기 구멍 완벽 제거 ---
    void CleanUpMap()
    {
        bool[,] visited = new bool[mapSize, mapSize];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (x == 0 || x == mapSize - 1 || z == 0 || z == mapSize - 1)
                {
                    if (map[x, z] == 0)
                    {
                        queue.Enqueue(new Vector2Int(x, z));
                        visited[x, z] = true;
                    }
                }
            }
        }

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();
            foreach (var dir in dirs)
            {
                Vector2Int next = curr + dir;
                if (next.x >= 0 && next.x < mapSize && next.y >= 0 && next.y < mapSize)
                {
                    if (map[next.x, next.y] == 0 && !visited[next.x, next.y])
                    {
                        visited[next.x, next.y] = true;
                        queue.Enqueue(next);
                    }
                }
            }
        }

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (map[x, z] == 0 && !visited[x, z]) map[x, z] = 1;
            }
        }
    }

    // =========================================================
    // 비트마스크 + 미세 스케일 조정을 통한 겹침 방지 벽 생성 로직
    // =========================================================
    void SpawnPrefabsSafely()
    {
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                if (map[x, z] == 1 || map[x, z] == 3)
                {
                    // 질문자님의 완벽한 IsPath 함수를 사용
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

                    // 이가 빠진 벽이 없도록 16가지 완벽한 경우의 수 적용
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

                        case 7:
                            SpawnWall(prefabL, pos, Quaternion.Euler(0, 0, 0));
                            SpawnWall(prefabL, pos, Quaternion.Euler(0, 90, 0));
                            break;
                        case 14:
                            SpawnWall(prefabL, pos, Quaternion.Euler(0, 90, 0));
                            SpawnWall(prefabL, pos, Quaternion.Euler(0, 180, 0));
                            break;
                        case 13:
                            SpawnWall(prefabL, pos, Quaternion.Euler(0, 180, 0));
                            SpawnWall(prefabL, pos, Quaternion.Euler(0, 270, 0));
                            break;
                        case 11:
                            SpawnWall(prefabL, pos, Quaternion.Euler(0, 270, 0));
                            SpawnWall(prefabL, pos, Quaternion.Euler(0, 0, 0));
                            break;

                        case 15:
                            SpawnWall(prefabI, pos, Quaternion.Euler(0, 0, 0));
                            SpawnWall(prefabI, pos, Quaternion.Euler(0, 90, 0));
                            SpawnWall(prefabI, pos, Quaternion.Euler(0, 180, 0));
                            SpawnWall(prefabI, pos, Quaternion.Euler(0, 270, 0));
                            break;
                    }
                }
            }
        }
    }

    // --- [Z-fighting 해결의 핵심] 벽을 미세하게 작게 생성합니다 ---
    void SpawnWall(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        GameObject wall = Instantiate(prefab, pos, rot, transform);

        // 두께를 99.5%로 줄여서 방 외벽(10)이나 외곽벽(100)과 위치가 같아도 면이 겹치지 않게 만듭니다!
        Vector3 originalScale = prefab.transform.localScale;
        wall.transform.localScale = new Vector3(originalScale.x * 0.995f, originalScale.y, originalScale.z * 0.995f);
    }

    // 질문자님의 원본 함수 그대로 사용
    bool IsPath(int cx, int cz, int nx, int nz)
    {
        if (nx < 0 || nx >= mapSize || nz < 0 || nz >= mapSize) return false;

        int neighbor = map[nx, nz];
        if (neighbor == 1 || neighbor == 3) return true;
        if (neighbor == 2 && map[cx, cz] == 3) return true;

        return false;
    }

    // 1. 맵 내에서 걸어 다닐 수 있는(복도=1, 방=2) 바닥 좌표를 모두 수집합니다.
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

    // 2. 플레이어, 몬스터, 아이템을 순서대로 스폰합니다.
    void SpawnEntities()
    {
        if (validSpawnPoints.Count == 0)
        {
            Debug.LogError("[MapGenerator] 스폰할 수 있는 타일이 없습니다!");
            return;
        }

        // --- 1. 플레이어 1 스폰 (랜덤 위치) ---
        Vector3 p1Pos = GetRandomSpawnPoint(removePoint: true);
        GameObject p1 = Instantiate(player1Prefab, p1Pos, Quaternion.identity);
        p1.name = "Player1";

        // --- 2. 플레이어 2 스폰 (플레이어 1 근처) ---
        // 멀티플레이어 환경이므로 일단 p1과 같은 곳에 스폰시킵니다. (나중에 NetworkClient가 분리함)
        Vector3 p2Pos = GetRandomSpawnPoint(removePoint: true);
        GameObject p2 = Instantiate(player2Prefab, p2Pos, Quaternion.identity);
        p2.name = "Player2";

        // --- 3. 몬스터 스폰 (플레이어와 멀리 떨어진 곳) ---
        Vector3 monsterPos = GetFarthestSpawnPoint(p1Pos, minMonsterDistance);
        GameObject monster = Instantiate(monsterPrefab, monsterPos, Quaternion.identity);
        monster.name = "Monster";

        Debug.Log($"[MapGenerator] 플레이어({p1Pos})와 몬스터({monsterPos}) 스폰 완료. 거리: {Vector3.Distance(p1Pos, monsterPos)}");

        // --- 4. 아이템 스폰 ---
        if (itemPrefabs != null && itemPrefabs.Length > 0)
        {
            for (int i = 0; i < itemSpawnCount; i++)
            {
                Vector3 itemPos = GetRandomSpawnPoint(removePoint: true);
                GameObject randomItemPrefab = itemPrefabs[Random.Range(0, itemPrefabs.Length)];
                Instantiate(randomItemPrefab, itemPos, Quaternion.identity);
            }
        }
    }

    // 랜덤한 스폰 포인트를 하나 뽑아냅니다. 중복 배치를 막으려면 removePoint를 true로 줍니다.
    Vector3 GetRandomSpawnPoint(bool removePoint)
    {
        int index = Random.Range(0, validSpawnPoints.Count);
        Vector3 pos = validSpawnPoints[index];
        if (removePoint) validSpawnPoints.RemoveAt(index);
        return pos;
    }

    // 특정 위치(플레이어)로부터 최대한 멀리 떨어지거나, 최소 거리(minDist)를 만족하는 스폰 포인트를 찾습니다.
    Vector3 GetFarthestSpawnPoint(Vector3 fromPosition, float minRequiredDistance)
    {
        Vector3 bestPos = validSpawnPoints[0];
        float maxDist = -1f;
        int bestIndex = 0;

        for (int i = 0; i < validSpawnPoints.Count; i++)
        {
            float dist = Vector3.Distance(fromPosition, validSpawnPoints[i]);

            // 가장 멀리 있는 점을 기록해 둠
            if (dist > maxDist)
            {
                maxDist = dist;
                bestPos = validSpawnPoints[i];
                bestIndex = i;
            }
        }

        // 만약 맵이 너무 작아서 최소 거리를 만족 못 시킨다면 경고를 띄우고 그냥 가장 먼 곳에 배치
        if (maxDist < minRequiredDistance)
        {
            Debug.LogWarning($"[MapGenerator] 몬스터 최소 스폰 거리({minRequiredDistance})를 만족하는 공간이 없습니다. 가장 먼 곳({maxDist})에 스폰합니다.");
        }

        validSpawnPoints.RemoveAt(bestIndex);
        return bestPos;
    }
}