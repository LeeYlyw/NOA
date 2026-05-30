using UnityEngine;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    [System.Serializable]
    public struct RoomData
    {
        public string roomName;
        public GameObject prefab;
        public int width;
        public int length;
    }

    [Header("방 프리팹 설정")]
    public List<RoomData> roomList = new List<RoomData>();

    [Header(" 복도/오피스 구역 프리팹 설정")]
    public GameObject floorPrefab;       //  빈 곳에 깔아줄 1x1 바닥 프리팹
    public GameObject partitionPrefab;   //  바닥 위에 세울 파티션 벽 프리팹
    [Range(0f, 100f)]
    public float partitionSpawnChance = 30f; //  파티션 벽이 생성될 확률 (30% 추천)

    [Header("가상 맵 그리드 설정")]
    public int gridWidth = 10;
    public int gridLength = 10;
    public float unitSize = 15f;

    private bool[,] mapGrid;
    private List<Vector3> spawnedRoomPositions = new List<Vector3>();

    void Start()
    {
        Invoke("GenerateProceduralMap", 0.2f);
    }

    void GenerateProceduralMap()
    {
        mapGrid = new bool[gridWidth, gridLength];
        spawnedRoomPositions.Clear();

        ShuffleRoomList(roomList);

        // 1. 방 우선 배치
        foreach (RoomData room in roomList)
        {
            bool isPlaced = false;
            int maxAttempts = 200;
            int attempts = 0;

            while (!isPlaced && attempts < maxAttempts)
            {
                attempts++;

                int randomX = Random.Range(0, gridWidth - room.width + 1);
                int randomZ = Random.Range(0, gridLength - room.length + 1);

                if (CheckSpaceAvailable(randomX, randomZ, room.width, room.length))
                {
                    ReserveSpace(randomX, randomZ, room.width, room.length);
                    Vector3 roomPos = SpawnRoom(room, randomX, randomZ);
                    spawnedRoomPositions.Add(roomPos);
                    isPlaced = true;
                }
            }
        }

        // 2.  [수정] 빈 공간에 바닥을 깔고, 확률적으로 파티션 벽 세우기
        CreateOfficeMaze();

        // 3. 캐릭터 랜덤 배치
        SpawnCharactersRandomly();
    }

    //  빈 그리드에 오피스 미로를 조성하는 알고리즘
    void CreateOfficeMaze()
    {
        if (floorPrefab == null)
        {
            Debug.LogWarning("Floor Prefab이 없어서 복도 생성을 건너뜁니다.");
            return;
        }

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridLength; z++)
            {
                // 방이 없는 빈 공간이라면
                if (!mapGrid[x, z])
                {
                    float offsetX = (x + 0.5f) * unitSize;
                    float offsetZ = (z + 0.5f) * unitSize;
                    Vector3 spawnPosition = new Vector3(offsetX, 0, offsetZ);

                    // A. 일단 플레이어가 걸어 다닐 수 있게 '바닥'은 무조건 깝니다.
                    GameObject spawnedFloor = Instantiate(floorPrefab, spawnPosition, Quaternion.identity);
                    spawnedFloor.name = $"OfficeFloor_{x}_{z}";
                    spawnedFloor.transform.parent = this.transform;

                    // B. 설정한 확률(예: 30%)에 걸리면 그 바닥 위에 파티션 벽을 세웁니다.
                    if (partitionPrefab != null && Random.Range(0f, 100f) < partitionSpawnChance)
                    {
                        // 랜덤하게 0도, 90도, 180도, 270도 회전시켜서 자연스러운 미로 느낌 연출
                        float[] rotations = { 0f, 90f, 180f, 270f };
                        float randomRot = rotations[Random.Range(0, rotations.Length)];
                        Quaternion wallRotation = Quaternion.Euler(0, randomRot, 0);

                        GameObject spawnedPartition = Instantiate(partitionPrefab, spawnPosition, wallRotation);
                        spawnedPartition.name = $"Partition_{x}_{z}";
                        spawnedPartition.transform.parent = this.transform;
                    }

                    mapGrid[x, z] = true;
                }
            }
        }
        Debug.Log("[알고리즘] 바닥 및 랜덤 파티션 미로 생성 완료!");
    }

    bool CheckSpaceAvailable(int startX, int startZ, int width, int length)
    {
        for (int x = startX; x < startX + width; x++)
        {
            for (int z = startZ; z < startZ + length; z++)
            {
                if (mapGrid[x, z]) return false;
            }
        }
        return true;
    }

    void ReserveSpace(int startX, int startZ, int width, int length)
    {
        for (int x = startX; x < startX + width; x++)
        {
            for (int z = startZ; z < startZ + length; z++)
            {
                mapGrid[x, z] = true;
            }
        }
    }

    Vector3 SpawnRoom(RoomData room, int gridX, int gridZ)
    {
        float offsetX = (gridX + (room.width / 2f)) * unitSize;
        float offsetZ = (gridZ + (room.length / 2f)) * unitSize;

        Vector3 spawnPosition = new Vector3(offsetX, 0, offsetZ);

        GameObject spawnedRoom = Instantiate(room.prefab, spawnPosition, Quaternion.identity);
        spawnedRoom.name = room.roomName;
        spawnedRoom.transform.parent = this.transform;

        return spawnPosition;
    }

    void SpawnCharactersRandomly()
    {
        if (spawnedRoomPositions.Count == 0) return;

        Transform playerTransform = null;

        if (NetworkClient.Instance != null && NetworkClient.Instance.localPlayerTransform != null)
        {
            playerTransform = NetworkClient.Instance.localPlayerTransform;
        }
        else
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
        }

        if (playerTransform != null)
        {
            int randomRoomIndex = Random.Range(0, spawnedRoomPositions.Count);
            Vector3 targetSpawnPos = spawnedRoomPositions[randomRoomIndex];
            targetSpawnPos.y = 1.5f;

            CharacterController cc = playerTransform.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            playerTransform.position = targetSpawnPos;

            if (cc != null) cc.enabled = true;
            Debug.Log($"[알고리즘] 플레이어를 {randomRoomIndex}번 방에 무작위 배치 완료!");
        }
    }

    void ShuffleRoomList(List<RoomData> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int rnd = Random.Range(0, i + 1);
            RoomData temp = list[i];
            list[i] = list[rnd];
            list[rnd] = temp;
        }
    }
}