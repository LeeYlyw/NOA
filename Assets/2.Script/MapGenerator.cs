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
        BuildNonLinearMaze();
        SpawnPrefabsSafely();  // <- 이 부분이 완전히 새로 작성되었습니다.
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

        for (int i = 0; i < roomDoors.Count; i++)
        {
            Vector2Int start = roomDoors[i];
            Vector2Int end = roomDoors[(i + 1) % roomDoors.Count];
            ConnectWithRandomTurn(start, end);
        }

        Vector2Int[] borderPoints = new Vector2Int[]
        {
            new Vector2Int(0, Random.Range(1, mapSize - 1)),
            new Vector2Int(mapSize - 1, Random.Range(1, mapSize - 1)),
            new Vector2Int(Random.Range(1, mapSize - 1), 0),
            new Vector2Int(Random.Range(1, mapSize - 1), mapSize - 1)
        };

        foreach (var border in borderPoints)
        {
            Vector2Int nearest = FindNearestCorridor(border);
            if (nearest.x != -1)
            {
                ConnectWithRandomTurn(nearest, border);
            }
        }

        GrowMazeBranches(15);
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
            int steps = Random.Range(3, 7);

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

    // =========================================================
    // 여기서부터 완전히 바뀐 벽 생성 로직입니다.
    // =========================================================

    void SpawnPrefabsSafely()
    {
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                // 통로(1)이거나 문(3)인 곳에만 복도 벽을 세움
                if (map[x, z] == 1 || map[x, z] == 3)
                {
                    // 주변 4방향이 뚫려있는 '길'인지 확인합니다.
                    bool pathTop = IsPath(x, z, x, z + 1);
                    bool pathBottom = IsPath(x, z, x, z - 1);
                    bool pathLeft = IsPath(x, z, x - 1, z);
                    bool pathRight = IsPath(x, z, x + 1, z);

                    // 길이 아니라면(허공이나 막힌 곳) 그 방향에 무조건 벽을 쳐야 합니다.
                    bool wallTop = !pathTop;
                    bool wallBottom = !pathBottom;
                    bool wallLeft = !pathLeft;
                    bool wallRight = !pathRight;

                    Vector3 pos = new Vector3(startOffset + (x * tileSize), 0, startOffset + (z * tileSize));

                    // 1. 코너(L자 벽) 우선 배치 (두 면을 동시에 막아줍니다)
                    if (wallTop && wallRight)
                    {
                        Instantiate(prefabL, pos, Quaternion.Euler(0, 0, 0), transform); // 북, 동 커버
                        wallTop = false; wallRight = false;
                    }
                    if (wallRight && wallBottom)
                    {
                        Instantiate(prefabL, pos, Quaternion.Euler(0, 90, 0), transform); // 동, 남 커버
                        wallRight = false; wallBottom = false;
                    }
                    if (wallBottom && wallLeft)
                    {
                        Instantiate(prefabL, pos, Quaternion.Euler(0, 180, 0), transform); // 남, 서 커버
                        wallBottom = false; wallLeft = false;
                    }
                    if (wallLeft && wallTop)
                    {
                        Instantiate(prefabL, pos, Quaternion.Euler(0, 270, 0), transform); // 서, 북 커버
                        wallLeft = false; wallTop = false;
                    }

                    // 2. 남은 뚫린 곳은 I자 벽(단면 벽)을 여러 개 배치하여 각각 막아줍니다.
                    // (일자 통로라면 좌/우를 막기 위해 I자 벽이 2개 생성됩니다!)
                    if (wallTop)
                    {
                        Instantiate(prefabI, pos, Quaternion.Euler(0, 0, 0), transform); // 북쪽 벽
                    }
                    if (wallRight)
                    {
                        Instantiate(prefabI, pos, Quaternion.Euler(0, 90, 0), transform); // 동쪽 벽
                    }
                    if (wallBottom)
                    {
                        Instantiate(prefabI, pos, Quaternion.Euler(0, 180, 0), transform); // 남쪽 벽
                    }
                    if (wallLeft)
                    {
                        Instantiate(prefabI, pos, Quaternion.Euler(0, 270, 0), transform); // 서쪽 벽
                    }
                }
            }
        }
    }

    // 특정 좌표(nx, nz)가 갈 수 있는 길인지 확인하는 함수
    bool IsPath(int cx, int cz, int nx, int nz)
    {
        // 맵 밖은 허공이므로 벽을 쳐야함 (길 아님 = false)
        if (nx < 0 || nx >= mapSize || nz < 0 || nz >= mapSize) return false;

        int neighbor = map[nx, nz];

        // 1(복도)이거나 3(문)이면 무조건 길
        if (neighbor == 1 || neighbor == 3) return true;

        // 2(방)일 경우: 방 안으로는 침범하면 안 되지만, 
        // 현재 내가 서 있는 곳(cx, cz)이 문(3)이라면 방으로 연결되는 길이 맞음
        if (neighbor == 2 && map[cx, cz] == 3) return true;

        return false;
    }
}