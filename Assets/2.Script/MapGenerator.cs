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

    [Header("복도 모듈 프리팹 (10m 기준)")]
    public GameObject prefabI;    // 일자
    public GameObject prefabL;    // ㄱ자
                                  // T자 프리팹 제거됨

    [Header("배치할 방 목록 (6개 등록 및 크기 설정 필수)")]
    public List<RoomData> roomsToSpawn;

    private int[,] map; // 0: 빈공간, 1: 복도, 2: 방
    private List<Vector2Int> roomDoors = new List<Vector2Int>();

    void Start()
    {
        GenerateMap();
    }

    void GenerateMap()
    {
        map = new int[mapSize, mapSize];
        roomDoors.Clear();

        PlaceRooms();
        ConnectRoomsBFS();
        SpawnPrefabs();
    }

    void PlaceRooms()
    {
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);

        foreach (var room in roomsToSpawn)
        {
            bool placed = false;
            int attempts = 0;

            while (!placed && attempts < 200)
            {
                int rx = Random.Range(2, mapSize - room.sizeX - 2);
                int rz = Random.Range(2, mapSize - room.sizeZ - 2);

                if (IsAreaClear(rx, rz, room.sizeX, room.sizeZ))
                {
                    for (int x = rx; x < rx + room.sizeX; x++)
                    {
                        for (int z = rz; z < rz + room.sizeZ; z++)
                        {
                            map[x, z] = 2;
                        }
                    }

                    float posX = startOffset + (rx * tileSize) + ((room.sizeX - 1) * tileSize / 2f);
                    float posZ = startOffset + (rz * tileSize) + ((room.sizeZ - 1) * tileSize / 2f);
                    Instantiate(room.prefab, new Vector3(posX, 0, posZ), Quaternion.identity, this.transform);

                    Vector2Int doorPos = new Vector2Int(rx + room.sizeX / 2, rz - 1);
                    roomDoors.Add(doorPos);

                    placed = true;
                }
                attempts++;
            }
        }
    }

    bool IsAreaClear(int startX, int startZ, int sizeX, int sizeZ)
    {
        for (int x = startX - 2; x <= startX + sizeX + 1; x++)
        {
            for (int z = startZ - 2; z <= startZ + sizeZ + 1; z++)
            {
                if (x < 1 || x >= mapSize - 1 || z < 1 || z >= mapSize - 1) return false;
                if (map[x, z] != 0) return false;
            }
        }
        return true;
    }

    void ConnectRoomsBFS()
    {
        for (int i = 0; i < roomDoors.Count - 1; i++)
        {
            BFSConnect(roomDoors[i], roomDoors[i + 1]);
        }
    }

    void BFSConnect(Vector2Int start, Vector2Int end)
    {
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();

        queue.Enqueue(start);
        cameFrom[start] = start;

        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();
            if (curr == end) break;

            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            foreach (var dir in dirs)
            {
                Vector2Int next = curr + dir;
                if (next.x > 0 && next.x < mapSize - 1 && next.y > 0 && next.y < mapSize - 1)
                {
                    if (!cameFrom.ContainsKey(next))
                    {
                        if (map[next.x, next.y] == 0 || map[next.x, next.y] == 1 || next == end)
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
            if (map[start.x, start.y] == 0) map[start.x, start.y] = 1;
        }
    }

    void SpawnPrefabs()
    {
        float startOffset = -(mapSize * tileSize) / 2f + (tileSize / 2f);

        for (int x = 1; x < mapSize - 1; x++)
        {
            for (int z = 1; z < mapSize - 1; z++)
            {
                if (map[x, z] == 1)
                {
                    bool top = map[x, z + 1] > 0;
                    bool bottom = map[x, z - 1] > 0;
                    bool left = map[x - 1, z] > 0;
                    bool right = map[x + 1, z] > 0;

                    Vector3 pos = new Vector3(startOffset + (x * tileSize), 0, startOffset + (z * tileSize));
                    Quaternion rot = Quaternion.identity;
                    GameObject selectedPrefab = null;

                    int connections = (top ? 1 : 0) + (bottom ? 1 : 0) + (left ? 1 : 0) + (right ? 1 : 0);

                    // 3방향 이상이 나와도 무조건 ㄱ자 또는 일자로 강제 마감
                    if ((top && bottom) || (left && right) || connections == 1)
                    {
                        selectedPrefab = prefabI;
                        if (left || right) rot = Quaternion.Euler(0, 90, 0);
                    }
                    else
                    {
                        // 3, 4방향일 경우에도 2방향만 뚫린 ㄱ자를 배치해버림 (의도적인 막다른 길 생성)
                        selectedPrefab = prefabL;
                        if (top && right) rot = Quaternion.Euler(0, 0, 0);
                        else if (right && bottom) rot = Quaternion.Euler(0, 90, 0);
                        else if (bottom && left) rot = Quaternion.Euler(0, 180, 0);
                        else if (left && top) rot = Quaternion.Euler(0, 270, 0);
                        else rot = Quaternion.Euler(0, 0, 0);
                    }

                    if (selectedPrefab != null)
                    {
                        Instantiate(selectedPrefab, pos, rot, this.transform);
                    }
                }
            }
        }
    }
}