#define NOMINMAX

#include <iostream>
#include <WinSock2.h>
#include <string>
#include <vector>
#include <cmath>
#include <chrono>
#include <sstream>
#include <iomanip>
#include <algorithm>
#include <cstdlib>

#ifdef min
#undef min
#endif

#ifdef max
#undef max
#endif

#pragma comment(lib, "ws2_32.lib")

struct Vec3
{
    float x = 0.0f;
    float y = 0.0f;
    float z = 0.0f;
};

enum class MonsterType
{
    Vision,
    Sound
};

enum class MonsterAIState
{
    Idle,
    Investigate,
    Chase,
    Attack,
    Return
};

struct PlayerState
{
    int playerId = 0;
    Vec3 position;
    float rotY = 0.0f;
    float speed = 0.0f;
    bool isRunning = false;
    bool isCrouching = false;
    bool isDead = false;
    int hp = 100;
    bool hasState = false;
};

struct MonsterState
{
    int monsterId = 0;
    MonsterType type = MonsterType::Vision;
    MonsterAIState state = MonsterAIState::Idle;

    Vec3 position;
    Vec3 spawnPosition;
    Vec3 targetPosition;
    Vec3 lastKnownPosition;
    Vec3 lastHeardPosition;

    float rotY = 0.0f;
    float currentSpeed = 0.0f;
    float moveSpeed = 2.0f;

    int targetPlayerId = 0;

    float sightRange = 8.0f;
    float sightAngle = 120.0f;
    float hearingRange = 8.0f;
    float attackRange = 2.0f;

    int damage = 10;
    double attackCooldown = 1.5;
    double lastAttackTime = -100.0;

    bool isWalk = false;
    bool isAttack = false;
};

static const float PI = 3.1415926535f;

double GetTimeSeconds()
{
    using namespace std::chrono;
    return duration<double>(steady_clock::now().time_since_epoch()).count();
}

float DegToRad(float degree)
{
    return degree * PI / 180.0f;
}

float Distance2D(const Vec3& a, const Vec3& b)
{
    float dx = a.x - b.x;
    float dz = a.z - b.z;
    return std::sqrt(dx * dx + dz * dz);
}

float Clamp(float value, float minValue, float maxValue)
{
    if (value < minValue)
        return minValue;

    if (value > maxValue)
        return maxValue;

    return value;
}

std::vector<std::string> Split(const std::string& text, char delimiter)
{
    std::vector<std::string> result;
    std::stringstream ss(text);
    std::string item;

    while (std::getline(ss, item, delimiter))
        result.push_back(item);

    return result;
}

bool StartsWith(const std::string& text, const std::string& prefix)
{
    return text.rfind(prefix, 0) == 0;
}

float ToFloat(const std::string& value)
{
    return std::strtof(value.c_str(), nullptr);
}

int ToInt(const std::string& value)
{
    return std::atoi(value.c_str());
}

std::string MonsterTypeToString(MonsterType type)
{
    if (type == MonsterType::Vision)
        return "Vision";

    return "Sound";
}

std::string MonsterStateToString(MonsterAIState state)
{
    switch (state)
    {
    case MonsterAIState::Idle:
        return "Idle";
    case MonsterAIState::Investigate:
        return "Investigate";
    case MonsterAIState::Chase:
        return "Chase";
    case MonsterAIState::Attack:
        return "Attack";
    case MonsterAIState::Return:
        return "Return";
    default:
        return "Idle";
    }
}

void SendPacket(SOCKET socket, const std::string& packet)
{
    std::string message = packet;

    if (message.empty() || message.back() != '\n')
        message += "\n";

    send(socket, message.c_str(), static_cast<int>(message.size()), 0);
}

void BroadcastPacket(SOCKET clientSocket1, SOCKET clientSocket2, const std::string& packet)
{
    SendPacket(clientSocket1, packet);
    SendPacket(clientSocket2, packet);
}

SOCKET GetSocketByPlayerId(int playerId, SOCKET clientSocket1, SOCKET clientSocket2)
{
    if (playerId == 1)
        return clientSocket1;

    if (playerId == 2)
        return clientSocket2;

    return INVALID_SOCKET;
}

SOCKET GetOtherSocket(int playerId, SOCKET clientSocket1, SOCKET clientSocket2)
{
    if (playerId == 1)
        return clientSocket2;

    if (playerId == 2)
        return clientSocket1;

    return INVALID_SOCKET;
}

void FaceTarget(MonsterState& monster, const Vec3& target)
{
    float dx = target.x - monster.position.x;
    float dz = target.z - monster.position.z;

    if (std::fabs(dx) < 0.001f && std::fabs(dz) < 0.001f)
        return;

    // Unity 기준: Y 회전 0도는 +Z 방향이다.
    monster.rotY = std::atan2(dx, dz) * 180.0f / PI;
}

bool MoveToward(MonsterState& monster, const Vec3& target, float deltaTime)
{
    float dx = target.x - monster.position.x;
    float dz = target.z - monster.position.z;
    float distance = std::sqrt(dx * dx + dz * dz);

    FaceTarget(monster, target);

    if (distance < 0.15f)
    {
        monster.isWalk = false;
        monster.currentSpeed = 0.0f;
        return true;
    }

    float dirX = dx / distance;
    float dirZ = dz / distance;
    float moveAmount = monster.moveSpeed * deltaTime;

    if (moveAmount > distance)
        moveAmount = distance;

    monster.position.x += dirX * moveAmount;
    monster.position.z += dirZ * moveAmount;

    monster.isWalk = true;
    monster.currentSpeed = monster.moveSpeed;

    return false;
}

bool IsPlayerVisible(const MonsterState& monster, const PlayerState& player)
{
    if (!player.hasState || player.isDead)
        return false;

    float distance = Distance2D(monster.position, player.position);
    if (distance > monster.sightRange)
        return false;

    float yawRad = DegToRad(monster.rotY);
    float forwardX = std::sin(yawRad);
    float forwardZ = std::cos(yawRad);

    float toPlayerX = player.position.x - monster.position.x;
    float toPlayerZ = player.position.z - monster.position.z;
    float toPlayerLength = std::sqrt(toPlayerX * toPlayerX + toPlayerZ * toPlayerZ);

    if (toPlayerLength < 0.001f)
        return true;

    toPlayerX /= toPlayerLength;
    toPlayerZ /= toPlayerLength;

    float dot = forwardX * toPlayerX + forwardZ * toPlayerZ;
    dot = Clamp(dot, -1.0f, 1.0f);

    float angle = std::acos(dot) * 180.0f / PI;
    return angle <= monster.sightAngle * 0.5f;
}

PlayerState* FindVisiblePlayer(MonsterState& monster, PlayerState players[])
{
    PlayerState* bestPlayer = nullptr;
    float bestDistance = 999999.0f;

    for (int i = 1; i <= 2; ++i)
    {
        if (!IsPlayerVisible(monster, players[i]))
            continue;

        float distance = Distance2D(monster.position, players[i].position);
        if (distance < bestDistance)
        {
            bestDistance = distance;
            bestPlayer = &players[i];
        }
    }

    return bestPlayer;
}

PlayerState* FindNearestAttackTarget(MonsterState& monster, PlayerState players[])
{
    PlayerState* bestPlayer = nullptr;
    float bestDistance = 999999.0f;

    for (int i = 1; i <= 2; ++i)
    {
        if (!players[i].hasState || players[i].isDead)
            continue;

        float distance = Distance2D(monster.position, players[i].position);
        if (distance <= monster.attackRange && distance < bestDistance)
        {
            bestDistance = distance;
            bestPlayer = &players[i];
        }
    }

    return bestPlayer;
}

bool IsCurrentTargetDead(const MonsterState& monster, PlayerState players[])
{
    if (monster.targetPlayerId < 1 || monster.targetPlayerId > 2)
        return false;

    const PlayerState& target = players[monster.targetPlayerId];
    return target.hasState && target.isDead;
}

void ClearDeadTargetAggro(MonsterState& monster)
{
    monster.targetPlayerId = 0;
    monster.state = MonsterAIState::Return;
    monster.targetPosition = monster.spawnPosition;
    monster.lastKnownPosition = monster.position;
    monster.lastHeardPosition = monster.position;
    monster.isAttack = false;
    monster.isWalk = false;
    monster.currentSpeed = 0.0f;
}

void SendPlayerDamageIfPossible(
    MonsterState& monster,
    PlayerState& target,
    SOCKET clientSocket1,
    SOCKET clientSocket2,
    double now)
{
    if (target.isDead)
        return;

    float distance = Distance2D(monster.position, target.position);
    if (distance > monster.attackRange)
        return;

    if (now - monster.lastAttackTime < monster.attackCooldown)
        return;

    monster.lastAttackTime = now;
    monster.isAttack = true;
    monster.state = MonsterAIState::Attack;

    std::ostringstream oss;
    oss << "S_PLAYER_DAMAGE|"
        << target.playerId << "|"
        << monster.damage << "|"
        << monster.monsterId;

    BroadcastPacket(clientSocket1, clientSocket2, oss.str());

    std::cout << "[SERVER DAMAGE] monster "
        << monster.monsterId
        << " -> player "
        << target.playerId
        << " damage "
        << monster.damage
        << std::endl;
}

void UpdateVisionMonster(
    MonsterState& monster,
    PlayerState players[],
    SOCKET clientSocket1,
    SOCKET clientSocket2,
    float deltaTime,
    double now)
{
    monster.isAttack = false;

    if (IsCurrentTargetDead(monster, players))
    {
        ClearDeadTargetAggro(monster);
        return;
    }

    PlayerState* visiblePlayer = FindVisiblePlayer(monster, players);

    if (visiblePlayer != nullptr)
    {
        monster.targetPlayerId = visiblePlayer->playerId;
        monster.lastKnownPosition = visiblePlayer->position;

        float distance = Distance2D(monster.position, visiblePlayer->position);

        if (distance <= monster.attackRange)
        {
            monster.state = MonsterAIState::Attack;
            monster.isWalk = false;
            monster.currentSpeed = 0.0f;
            FaceTarget(monster, visiblePlayer->position);
            SendPlayerDamageIfPossible(monster, *visiblePlayer, clientSocket1, clientSocket2, now);
            return;
        }

        monster.state = MonsterAIState::Chase;
        MoveToward(monster, visiblePlayer->position, deltaTime);
        return;
    }

    if (monster.state == MonsterAIState::Chase || monster.state == MonsterAIState::Attack)
    {
        monster.state = MonsterAIState::Investigate;
        monster.targetPosition = monster.lastKnownPosition;
    }

    if (monster.state == MonsterAIState::Investigate)
    {
        bool arrived = MoveToward(monster, monster.targetPosition, deltaTime);
        if (arrived)
            monster.state = MonsterAIState::Return;

        return;
    }

    if (monster.state == MonsterAIState::Return)
    {
        bool arrived = MoveToward(monster, monster.spawnPosition, deltaTime);
        if (arrived)
            monster.state = MonsterAIState::Idle;

        return;
    }

    // 1차 구현에서는 순찰 경로가 없으므로 Idle 상태에서 천천히 회전하며 시야 탐색만 한다.
    monster.state = MonsterAIState::Idle;
    monster.rotY += 30.0f * deltaTime;
    if (monster.rotY >= 360.0f)
        monster.rotY -= 360.0f;

    monster.isWalk = false;
    monster.currentSpeed = 0.0f;
}

void UpdateSoundMonster(
    MonsterState& monster,
    PlayerState players[],
    SOCKET clientSocket1,
    SOCKET clientSocket2,
    float deltaTime,
    double now)
{
    monster.isAttack = false;

    if (IsCurrentTargetDead(monster, players))
    {
        ClearDeadTargetAggro(monster);
        return;
    }

    PlayerState* attackTarget = FindNearestAttackTarget(monster, players);
    if (attackTarget != nullptr)
    {
        monster.state = MonsterAIState::Attack;
        monster.isWalk = false;
        monster.currentSpeed = 0.0f;
        FaceTarget(monster, attackTarget->position);
        SendPlayerDamageIfPossible(monster, *attackTarget, clientSocket1, clientSocket2, now);
        return;
    }

    if (monster.state == MonsterAIState::Investigate)
    {
        bool arrived = MoveToward(monster, monster.lastHeardPosition, deltaTime);

        if (arrived)
            monster.state = MonsterAIState::Return;

        return;
    }

    if (monster.state == MonsterAIState::Return)
    {
        bool arrived = MoveToward(monster, monster.spawnPosition, deltaTime);

        if (arrived)
            monster.state = MonsterAIState::Idle;

        return;
    }

    monster.state = MonsterAIState::Idle;
    monster.isWalk = false;
    monster.currentSpeed = 0.0f;
}

void BroadcastMonsterState(SOCKET clientSocket1, SOCKET clientSocket2, const MonsterState& monster)
{
    std::ostringstream oss;
    oss << std::fixed << std::setprecision(2);
    oss << "S_MONSTER_STATE|"
        << monster.monsterId << "|"
        << MonsterTypeToString(monster.type) << "|"
        << MonsterStateToString(monster.state) << "|"
        << monster.position.x << "|"
        << monster.position.y << "|"
        << monster.position.z << "|"
        << monster.rotY << "|"
        << monster.currentSpeed << "|"
        << (monster.isWalk ? 1 : 0) << "|"
        << (monster.isAttack ? 1 : 0);

    BroadcastPacket(clientSocket1, clientSocket2, oss.str());
}

void ProcessMovePacket(const std::vector<std::string>& parts, PlayerState players[])
{
    // 현재 Unity 클라이언트 형식:
    // MOVE|playerId|x|y|z|rotY|speed|isRunning|isCrouching
    // 향후 확장 형식:
    // MOVE|playerId|x|y|z|rotY|speed|isRunning|isCrouching|isDead
    if (parts.size() < 9)
        return;

    int playerId = ToInt(parts[1]);

    if (playerId < 1 || playerId > 2)
        return;

    PlayerState& player = players[playerId];

    player.playerId = playerId;
    player.position.x = ToFloat(parts[2]);
    player.position.y = ToFloat(parts[3]);
    player.position.z = ToFloat(parts[4]);
    player.rotY = ToFloat(parts[5]);
    player.speed = ToFloat(parts[6]);
    player.isRunning = ToInt(parts[7]) == 1;
    player.isCrouching = ToInt(parts[8]) == 1;

    if (parts.size() >= 10)
        player.isDead = ToInt(parts[9]) == 1;

    player.hasState = true;
}

void ProcessNoisePacket(const std::vector<std::string>& parts, PlayerState players[], MonsterState monsters[])
{
    // C_NOISE|playerId|x|y|z|noiseAmount
    if (parts.size() != 6)
        return;

    int playerId = ToInt(parts[1]);

    if (playerId < 1 || playerId > 2)
        return;

    if (players[playerId].hasState && players[playerId].isDead)
        return;

    Vec3 noisePosition;
    noisePosition.x = ToFloat(parts[2]);
    noisePosition.y = ToFloat(parts[3]);
    noisePosition.z = ToFloat(parts[4]);

    float noiseAmount = ToFloat(parts[5]);

    for (int i = 0; i < 2; ++i)
    {
        MonsterState& monster = monsters[i];

        if (monster.type != MonsterType::Sound)
            continue;

        float distance = Distance2D(monster.position, noisePosition);

        // noiseAmount가 클수록 더 먼 소리도 들을 수 있게 처리한다.
        if (distance <= monster.hearingRange + noiseAmount)
        {
            monster.state = MonsterAIState::Investigate;
            monster.targetPlayerId = playerId;
            monster.lastHeardPosition = noisePosition;
            monster.targetPosition = noisePosition;

            std::cout << "[NOISE] player "
                << playerId
                << " noiseAmount "
                << noiseAmount
                << " -> sound monster investigate"
                << std::endl;
        }
    }
}

void ProcessClientPacket(
    int senderPlayerId,
    const std::string& packet,
    SOCKET clientSocket1,
    SOCKET clientSocket2,
    PlayerState players[],
    MonsterState monsters[])
{
    if (packet.empty())
        return;

    std::vector<std::string> parts = Split(packet, '|');
    if (parts.empty())
        return;

    const std::string& type = parts[0];

    if (type == "MOVE")
    {
        ProcessMovePacket(parts, players);

        SOCKET otherSocket = GetOtherSocket(senderPlayerId, clientSocket1, clientSocket2);
        if (otherSocket != INVALID_SOCKET)
            SendPacket(otherSocket, packet);

        return;
    }

    if (type == "C_NOISE")
    {
        ProcessNoisePacket(parts, players, monsters);
        return;
    }

    if (type == "PLAYER_DAMAGE")
    {
        // 서버 권한 구조에서는 클라이언트가 데미지를 확정하지 않는다.
        std::cout << "[IGNORED CLIENT DAMAGE] " << packet << std::endl;
        return;
    }

    // 아이템, 부활, 게임 클리어 등 아직 서버 판정으로 옮기지 않은 패킷은
    // 기존 시연 기능 보존을 위해 임시로 상대 클라이언트에 전달한다.
    SOCKET otherSocket = GetOtherSocket(senderPlayerId, clientSocket1, clientSocket2);
    if (otherSocket != INVALID_SOCKET)
        SendPacket(otherSocket, packet);
}

void ProcessReceiveBuffer(
    int playerId,
    std::string& receiveBuffer,
    SOCKET clientSocket1,
    SOCKET clientSocket2,
    PlayerState players[],
    MonsterState monsters[])
{
    while (true)
    {
        size_t newlineIndex = receiveBuffer.find('\n');

        if (newlineIndex == std::string::npos)
            break;

        std::string packet = receiveBuffer.substr(0, newlineIndex);
        receiveBuffer.erase(0, newlineIndex + 1);

        if (!packet.empty() && packet.back() == '\r')
            packet.pop_back();

        if (packet.empty())
            continue;

        ProcessClientPacket(playerId, packet, clientSocket1, clientSocket2, players, monsters);
    }
}

void InitializeMonsters(MonsterState monsters[])
{
    // TODO:
    // 아래 초기 좌표는 서버 권한 구조 테스트용 기본값이다.
    // Unity 씬의 실제 몬스터 시작 위치에 맞게 수정해야 한다.

    monsters[0].monsterId = 1;
    monsters[0].type = MonsterType::Vision;
    monsters[0].state = MonsterAIState::Idle;
    monsters[0].position = { 0.0f, 0.0f, 8.0f };
    monsters[0].spawnPosition = monsters[0].position;
    monsters[0].targetPosition = monsters[0].position;
    monsters[0].lastKnownPosition = monsters[0].position;
    monsters[0].rotY = 180.0f;
    monsters[0].moveSpeed = 2.5f;
    monsters[0].sightRange = 10.0f;
    monsters[0].sightAngle = 120.0f;
    monsters[0].attackRange = 2.0f;
    monsters[0].attackCooldown = 1.5;
    monsters[0].damage = 10;

    monsters[1].monsterId = 2;
    monsters[1].type = MonsterType::Sound;
    monsters[1].state = MonsterAIState::Idle;
    monsters[1].position = { 8.0f, 0.0f, 0.0f };
    monsters[1].spawnPosition = monsters[1].position;
    monsters[1].targetPosition = monsters[1].position;
    monsters[1].lastHeardPosition = monsters[1].position;
    monsters[1].rotY = 270.0f;
    monsters[1].moveSpeed = 2.2f;
    monsters[1].hearingRange = 6.0f;
    monsters[1].attackRange = 2.0f;
    monsters[1].attackCooldown = 1.5;
    monsters[1].damage = 10;
}

void UpdateMonsters(
    MonsterState monsters[],
    PlayerState players[],
    SOCKET clientSocket1,
    SOCKET clientSocket2,
    float deltaTime,
    double now)
{
    for (int i = 0; i < 2; ++i)
    {
        if (monsters[i].type == MonsterType::Vision)
        {
            UpdateVisionMonster(monsters[i], players, clientSocket1, clientSocket2, deltaTime, now);
        }
        else if (monsters[i].type == MonsterType::Sound)
        {
            UpdateSoundMonster(monsters[i], players, clientSocket1, clientSocket2, deltaTime, now);
        }
    }
}

void BroadcastAllMonsterStates(SOCKET clientSocket1, SOCKET clientSocket2, MonsterState monsters[])
{
    for (int i = 0; i < 2; ++i)
        BroadcastMonsterState(clientSocket1, clientSocket2, monsters[i]);
}

int main()
{
    WSADATA wsaData;
    SOCKET listenSocket = INVALID_SOCKET;
    SOCKET clientSocket1 = INVALID_SOCKET;
    SOCKET clientSocket2 = INVALID_SOCKET;

    int result = WSAStartup(MAKEWORD(2, 2), &wsaData);
    if (result != 0)
    {
        std::cout << "WSAStartup failed: " << result << std::endl;
        return 1;
    }

    listenSocket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listenSocket == INVALID_SOCKET)
    {
        std::cout << "socket failed: " << WSAGetLastError() << std::endl;
        WSACleanup();
        return 1;
    }

    sockaddr_in serverAddr;
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_port = htons(7777);
    serverAddr.sin_addr.s_addr = htonl(INADDR_ANY);

    result = bind(listenSocket, (sockaddr*)&serverAddr, sizeof(serverAddr));
    if (result == SOCKET_ERROR)
    {
        std::cout << "bind failed: " << WSAGetLastError() << std::endl;
        closesocket(listenSocket);
        WSACleanup();
        return 1;
    }

    result = listen(listenSocket, SOMAXCONN);
    if (result == SOCKET_ERROR)
    {
        std::cout << "listen failed: " << WSAGetLastError() << std::endl;
        closesocket(listenSocket);
        WSACleanup();
        return 1;
    }

    std::cout << "Server Start" << std::endl;
    std::cout << "Listen Port : 7777" << std::endl;
    std::cout << "Server Model : select based two-client TCP server" << std::endl;

    std::cout << "Waiting for client 1..." << std::endl;
    clientSocket1 = accept(listenSocket, nullptr, nullptr);
    if (clientSocket1 == INVALID_SOCKET)
    {
        std::cout << "accept client 1 failed: " << WSAGetLastError() << std::endl;
        closesocket(listenSocket);
        WSACleanup();
        return 1;
    }

    std::cout << "Client 1 connected!" << std::endl;
    SendPacket(clientSocket1, "ID:1");
    SendPacket(clientSocket1, "COUNT|1");

    std::cout << "Waiting for client 2..." << std::endl;
    clientSocket2 = accept(listenSocket, nullptr, nullptr);
    if (clientSocket2 == INVALID_SOCKET)
    {
        std::cout << "accept client 2 failed: " << WSAGetLastError() << std::endl;
        closesocket(clientSocket1);
        closesocket(listenSocket);
        WSACleanup();
        return 1;
    }

    std::cout << "Client 2 connected!" << std::endl;
    SendPacket(clientSocket2, "ID:2");

    BroadcastPacket(clientSocket1, clientSocket2, "COUNT|2");
    BroadcastPacket(clientSocket1, clientSocket2, "START");

    PlayerState players[3];
    players[1].playerId = 1;
    players[2].playerId = 2;

    MonsterState monsters[2];
    InitializeMonsters(monsters);

    std::string receiveBuffer1;
    std::string receiveBuffer2;

    std::cout << "Start Server Authority Monster Loop." << std::endl;

    double lastUpdateTime = GetTimeSeconds();
    double lastMonsterBroadcastTime = GetTimeSeconds();

    char buffer[1024];

    while (true)
    {
        fd_set readSet;
        FD_ZERO(&readSet);
        FD_SET(clientSocket1, &readSet);
        FD_SET(clientSocket2, &readSet);

        timeval timeout;
        timeout.tv_sec = 0;
        timeout.tv_usec = 50000; // 0.05초마다 서버 몬스터 루프 갱신

        int selectResult = select(0, &readSet, nullptr, nullptr, &timeout);
        if (selectResult == SOCKET_ERROR)
        {
            std::cout << "select failed: " << WSAGetLastError() << std::endl;
            break;
        }

        if (selectResult > 0 && FD_ISSET(clientSocket1, &readSet))
        {
            int recvLength = recv(clientSocket1, buffer, sizeof(buffer) - 1, 0);

            if (recvLength > 0)
            {
                buffer[recvLength] = '\0';
                receiveBuffer1.append(buffer, recvLength);
                ProcessReceiveBuffer(1, receiveBuffer1, clientSocket1, clientSocket2, players, monsters);
            }
            else
            {
                std::cout << "Client 1 disconnected or recv failed." << std::endl;
                break;
            }
        }

        if (selectResult > 0 && FD_ISSET(clientSocket2, &readSet))
        {
            int recvLength = recv(clientSocket2, buffer, sizeof(buffer) - 1, 0);

            if (recvLength > 0)
            {
                buffer[recvLength] = '\0';
                receiveBuffer2.append(buffer, recvLength);
                ProcessReceiveBuffer(2, receiveBuffer2, clientSocket1, clientSocket2, players, monsters);
            }
            else
            {
                std::cout << "Client 2 disconnected or recv failed." << std::endl;
                break;
            }
        }

        double now = GetTimeSeconds();
        float deltaTime = static_cast<float>(now - lastUpdateTime);
        lastUpdateTime = now;

        if (deltaTime > 0.2f)
            deltaTime = 0.2f;

        UpdateMonsters(monsters, players, clientSocket1, clientSocket2, deltaTime, now);

        if (now - lastMonsterBroadcastTime >= 0.1)
        {
            BroadcastAllMonsterStates(clientSocket1, clientSocket2, monsters);
            lastMonsterBroadcastTime = now;
        }
    }

    closesocket(clientSocket2);
    closesocket(clientSocket1);
    closesocket(listenSocket);
    WSACleanup();

    std::cout << "Server Closed." << std::endl;
    std::cout << "Press Enter to quit." << std::endl;
    std::cin.get();

    return 0;
}
