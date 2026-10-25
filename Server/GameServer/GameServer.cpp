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
#include <cerrno>
#include <climits>
#include <map>
#include <iterator>
#include <random>

#pragma comment(lib, "ws2_32.lib")

struct Vec3
{
    float x = 0.0f;
    float y = 0.0f;
    float z = 0.0f;
};

#include "ServerNavigation.h"

struct NavigationUpload
{
    int width = 0;
    int height = 0;
    int nextSequence = 0;
    bool begun = false;
    bool complete = false;
    std::string header;
    std::string mask;
    Vec3 monsterSpawns[2];
    float originX = 0.0f, originZ = 0.0f, step = 2.0f;
    float gateX = 0.0f, gateZ = 0.0f, gateHalfWidth = 0.0f;
};

static ServerNavigation navigation;
static NavigationUpload navigationUploads[3];
static double gateOpeningTime = 0.0;
static int matchRoles[3] = {-1, -1, -1};

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


enum class ItemType
{
    Unknown,
    Teleport,
    Stealth,
    Heal,
    Resurrection,
    Clue
};

struct ItemState
{
    int itemId = 0;
    ItemType type = ItemType::Unknown;
    int pickedByPlayerId = 0;
    bool isPicked = false;
};

struct MimicShot
{
    int mimicId = 0;
    int targetPlayerId = 0;
    int damage = 0;
    double expiresAt = 0.0;
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
    int maxHp = 100;
    bool hasState = false;

    // [추가] 은신 상태 및 지속 시간
    bool isStealth = false;
    double stealthEndTime = 0.0;
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
    float patrolSpeed = 1.5f;
    float chaseSpeed = 3.5f;
    float searchSpeed = 2.0f;
    float patrolRadius = 12.0f;
    double searchDuration = 7.0;
    double loseSightDelay = 1.5;

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
    int attackSequence = 0;
    std::vector<int> path;
    int pathGoal = -1;
    size_t pathCursor = 0;
    double stateUntil = 0.0;
    double lostSightAt = 0.0;
    int searchVisits = 0;
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

bool TryParseFloat(const std::string& value, float& result)
{
    if (value.empty()) return false;
    char* end = nullptr;
    errno = 0;
    float parsed = std::strtof(value.c_str(), &end);
    if (errno == ERANGE || end == value.c_str() || *end != '\0' || !std::isfinite(parsed))
        return false;
    result = parsed;
    return true;
}

bool TryParseInt(const std::string& value, int& result)
{
    if (value.empty()) return false;
    char* end = nullptr;
    errno = 0;
    long parsed = std::strtol(value.c_str(), &end, 10);
    if (errno == ERANGE || end == value.c_str() || *end != '\0' || parsed < INT_MIN || parsed > INT_MAX)
        return false;
    result = static_cast<int>(parsed);
    return true;
}


ItemType StringToItemType(const std::string& value)
{
    if (value == "Teleport") return ItemType::Teleport;
    if (value == "Stealth") return ItemType::Stealth;
    if (value == "Heal") return ItemType::Heal;
    if (value == "Resurrection") return ItemType::Resurrection;
    if (value == "Clue") return ItemType::Clue;
    return ItemType::Unknown;
}

std::string ItemTypeToString(ItemType type)
{
    switch (type)
    {
    case ItemType::Teleport: return "Teleport";
    case ItemType::Stealth: return "Stealth";
    case ItemType::Heal: return "Heal";
    case ItemType::Resurrection: return "Resurrection";
    case ItemType::Clue: return "Clue";
    default: return "Unknown";
    }
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

    size_t sent = 0;
    while (sent < message.size())
    {
        int result = send(socket, message.c_str() + sent, static_cast<int>(message.size() - sent), 0);
        if (result <= 0)
        {
            std::cout << "[SEND FAILED] error " << WSAGetLastError() << std::endl;
            return;
        }
        sent += static_cast<size_t>(result);
    }
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
    if (!navigation.ready) return false;
    int start = navigation.ClosestCell(monster.position);
    int goal = navigation.ClosestCell(target);
    if (start < 0 || goal < 0)
    {
        monster.isWalk = false;
        monster.currentSpeed = 0.0f;
        return false;
    }
    if (!monster.path.empty() && monster.pathCursor < monster.path.size() &&
        start == monster.path[monster.pathCursor])
        ++monster.pathCursor;
    if (monster.pathGoal != goal || monster.path.empty() || monster.pathCursor == 0 ||
        start != monster.path[monster.pathCursor - 1])
    {
        monster.path = navigation.FindPath(start, goal);
        monster.pathGoal = goal;
        monster.pathCursor = monster.path.empty() ? 0 : 1;
    }
    if (monster.path.empty())
    {
        monster.isWalk = false;
        monster.currentSpeed = 0.0f;
        return false;
    }

    Vec3 waypoint = monster.pathCursor < monster.path.size()
        ? navigation.Center(monster.path[monster.pathCursor]) : target;
    if (monster.pathCursor >= monster.path.size() &&
        !navigation.HasLineOfSight(monster.position, target))
        waypoint = navigation.Center(monster.path.back());
    float dx = waypoint.x - monster.position.x;
    float dz = waypoint.z - monster.position.z;
    float distance = std::sqrt(dx * dx + dz * dz);
    if (distance < 0.2f && monster.pathCursor < monster.path.size())
    {
        ++monster.pathCursor;
        waypoint = monster.pathCursor < monster.path.size()
            ? navigation.Center(monster.path[monster.pathCursor]) : target;
        if (monster.pathCursor >= monster.path.size() &&
            !navigation.HasLineOfSight(monster.position, target))
            waypoint = navigation.Center(monster.path.back());
        dx = waypoint.x - monster.position.x;
        dz = waypoint.z - monster.position.z;
        distance = std::sqrt(dx * dx + dz * dz);
    }
    if (distance < 0.2f)
    {
        monster.isWalk = false;
        monster.currentSpeed = 0.0f;
        return true;
    }
    FaceTarget(monster, waypoint);
    float moveAmount = std::min(distance, monster.moveSpeed * deltaTime);
    monster.position.x += dx / distance * moveAmount;
    monster.position.z += dz / distance * moveAmount;
    monster.isWalk = true;
    monster.currentSpeed = monster.moveSpeed;
    return false;
}

bool IsPlayerVisible(const MonsterState& monster, const PlayerState& player)
{
    // [수정] 은신 상태(isStealth)면 감지 불가
    if (!player.hasState || player.isDead || player.isStealth)
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
    return angle <= monster.sightAngle * 0.5f &&
           navigation.HasLineOfSight(monster.position, player.position);
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
    if (!navigation.HasLineOfSight(monster.position, target.position))
        return;

    if (now - monster.lastAttackTime < monster.attackCooldown)
        return;

    monster.lastAttackTime = now;
    monster.attackSequence++;
    monster.isAttack = true;
    monster.state = MonsterAIState::Attack;

    // [수정된 부분] 서버 내부에서 직접 체력을 깎고 사망 여부를 확정합니다.
    target.hp -= monster.damage;
    if (target.hp <= 0)
    {
        target.hp = 0;
        target.isDead = true;
    }

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
        << " (Current HP: " << target.hp << ")" // 추가된 로그
        << std::endl;
}

Vec3 PickReachablePoint(const MonsterState& monster, const Vec3& center, float radius)
{
    static std::mt19937 random(std::random_device{}());
    int origin = navigation.ClosestCell(monster.position);
    int around = navigation.ClosestCell(center);
    if (origin < 0 || around < 0) return monster.position;
    int cellRadius = std::max(1, static_cast<int>(std::ceil(radius / navigation.step)));
    std::uniform_int_distribution<int> offset(-cellRadius, cellRadius);
    for (int attempt = 0; attempt < 24; ++attempt)
    {
        int dx = offset(random), dz = offset(random);
        if (dx * dx + dz * dz > cellRadius * cellRadius) continue;
        int index = navigation.Index(around % navigation.width + dx,
                                     around / navigation.width + dz);
        if (index < 0 || navigation.edges[index] == 0 || index == origin) continue;
        if (navigation.FindPath(origin, index).empty()) continue;
        Vec3 point = navigation.Center(index);
        point.y = monster.position.y;
        return point;
    }
    return monster.position;
}

void UpdatePatrol(MonsterState& monster, float deltaTime, double now)
{
    monster.state = MonsterAIState::Idle;
    monster.moveSpeed = monster.patrolSpeed;
    if (now < monster.stateUntil)
    {
        monster.isWalk = false;
        monster.currentSpeed = 0.0f;
        monster.rotY = std::fmod(monster.rotY + 30.0f * deltaTime, 360.0f);
        return;
    }
    if (Distance2D(monster.position, monster.targetPosition) < 0.3f || monster.pathGoal < 0)
        monster.targetPosition = PickReachablePoint(monster, monster.position, monster.patrolRadius);
    bool arrived = MoveToward(monster, monster.targetPosition, deltaTime);
    if (arrived || monster.path.empty())
    {
        monster.targetPosition = monster.position;
        monster.stateUntil = now + 1.5;
        monster.pathGoal = -1;
    }
}

void BeginSearch(MonsterState& monster, const Vec3& location)
{
    monster.state = MonsterAIState::Investigate;
    monster.targetPosition = location;
    monster.searchVisits = -1;
    monster.stateUntil = 0.0;
    monster.pathGoal = -1;
}

void UpdateSearch(MonsterState& monster, float deltaTime, double now)
{
    monster.moveSpeed = monster.searchSpeed;
    if (monster.searchVisits >= 0 && now >= monster.stateUntil)
    {
        monster.state = MonsterAIState::Idle;
        monster.targetPosition = monster.position;
        monster.stateUntil = now + 1.5;
        monster.pathGoal = -1;
        return;
    }
    bool arrived = MoveToward(monster, monster.targetPosition, deltaTime);
    if (!arrived && !monster.path.empty()) return;
    if (monster.searchVisits < 0)
    {
        monster.searchVisits = 0;
        monster.stateUntil = now + monster.searchDuration;
    }
    Vec3 center = monster.type == MonsterType::Vision
        ? monster.lastKnownPosition : monster.lastHeardPosition;
    monster.targetPosition = PickReachablePoint(monster, center, 3.0f);
    monster.pathGoal = -1;
    ++monster.searchVisits;
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
    PlayerState* visiblePlayer = FindVisiblePlayer(monster, players);
    if (visiblePlayer != nullptr)
    {
        monster.lostSightAt = 0.0;
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
        monster.moveSpeed = monster.chaseSpeed;
        MoveToward(monster, visiblePlayer->position, deltaTime);
        return;
    }
    if (monster.state == MonsterAIState::Chase || monster.state == MonsterAIState::Attack)
    {
        if (monster.lostSightAt == 0.0) monster.lostSightAt = now;
        if (now - monster.lostSightAt < monster.loseSightDelay)
        {
            monster.state = MonsterAIState::Chase;
            monster.moveSpeed = monster.chaseSpeed;
            MoveToward(monster, monster.lastKnownPosition, deltaTime);
            return;
        }
        BeginSearch(monster, monster.lastKnownPosition);
    }
    if (monster.state == MonsterAIState::Investigate)
    {
        UpdateSearch(monster, deltaTime, now);
        return;
    }
    UpdatePatrol(monster, deltaTime, now);
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
    PlayerState* visiblePlayer = FindVisiblePlayer(monster, players);
    if (visiblePlayer != nullptr)
    {
        monster.lostSightAt = 0.0;
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
        monster.moveSpeed = monster.chaseSpeed;
        MoveToward(monster, visiblePlayer->position, deltaTime);
        return;
    }
    if (monster.state == MonsterAIState::Chase || monster.state == MonsterAIState::Attack)
    {
        if (monster.lostSightAt == 0.0) monster.lostSightAt = now;
        if (now - monster.lostSightAt < monster.loseSightDelay)
        {
            monster.state = MonsterAIState::Chase;
            monster.moveSpeed = monster.chaseSpeed;
            MoveToward(monster, monster.lastKnownPosition, deltaTime);
            return;
        }
        monster.lastHeardPosition = monster.lastKnownPosition;
        BeginSearch(monster, monster.lastKnownPosition);
    }
    if (monster.state == MonsterAIState::Investigate)
    {
        UpdateSearch(monster, deltaTime, now);
        return;
    }
    UpdatePatrol(monster, deltaTime, now);
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
        << (monster.isAttack ? 1 : 0) << "|"
        << monster.attackSequence;

    BroadcastPacket(clientSocket1, clientSocket2, oss.str());
}

bool ProcessMovePacket(const std::vector<std::string>& parts, int senderPlayerId, PlayerState players[])
{
    if (parts.size() != 11)
        return false;

    int playerId = 0;
    if (!TryParseInt(parts[1], playerId) || playerId != senderPlayerId)
        return false;

    PlayerState& player = players[playerId];

    // [수정된 부분] 서버 판정으로 이미 죽은 상태면 클라이언트의 억지 이동 패킷을 무시합니다.
    if (player.isDead)
        return false;

    Vec3 position;
    float rotY = 0.0f;
    float speed = 0.0f;
    int running = 0;
    int crouching = 0;
    int grounded = 0;
    int jumpSequence = 0;
    if (!TryParseFloat(parts[2], position.x) || !TryParseFloat(parts[3], position.y) ||
        !TryParseFloat(parts[4], position.z) || !TryParseFloat(parts[5], rotY) ||
        !TryParseFloat(parts[6], speed) || !TryParseInt(parts[7], running) ||
        !TryParseInt(parts[8], crouching) || !TryParseInt(parts[9], grounded) ||
        !TryParseInt(parts[10], jumpSequence) || speed < 0.0f ||
        (running != 0 && running != 1) || (crouching != 0 && crouching != 1) ||
        (grounded != 0 && grounded != 1) || jumpSequence < 0)
        return false;

    player.playerId = playerId;
    player.position = position;
    player.rotY = rotY;
    player.speed = speed;
    player.isRunning = running == 1;
    player.isCrouching = crouching == 1;

    // [삭제된 부분] 클라이언트가 보내는 isDead(parts[9]) 값을 더 이상 신뢰하지 않음

    player.hasState = true;
    return true;
}

void ProcessNoisePacket(const std::vector<std::string>& parts, int senderPlayerId, PlayerState players[], MonsterState monsters[])
{
    // C_NOISE|playerId|x|y|z|noiseAmount
    if (parts.size() != 6)
        return;

    int playerId = 0;
    if (!TryParseInt(parts[1], playerId) || playerId < 1 || playerId > 2 ||
        playerId != senderPlayerId || players[playerId].isDead)
        return;

    Vec3 noisePosition;
    float noiseAmount = 0.0f;
    if (!TryParseFloat(parts[2], noisePosition.x) || !TryParseFloat(parts[3], noisePosition.y) ||
        !TryParseFloat(parts[4], noisePosition.z) || !TryParseFloat(parts[5], noiseAmount) ||
        noiseAmount < 0.0f || noiseAmount > 10.0f || !navigation.ready ||
        !players[playerId].hasState ||
        Distance2D(noisePosition, players[playerId].position) > 2.0f)
        return;

    for (int i = 0; i < 2; ++i)
    {
        MonsterState& monster = monsters[i];

        if (monster.type != MonsterType::Sound)
            continue;
        if (FindVisiblePlayer(monster, players) != nullptr)
            continue;

        float distance = Distance2D(monster.position, noisePosition);

        // noiseAmount가 클수록 더 먼 소리도 들을 수 있게 처리한다.
        if (distance <= noiseAmount * 2.0f)
        {
            monster.state = MonsterAIState::Investigate;
            monster.targetPlayerId = playerId;
            monster.lastHeardPosition = noisePosition;
            monster.targetPosition = noisePosition;
            monster.searchVisits = -1;
            monster.stateUntil = 0.0;
            monster.pathGoal = -1;

            std::cout << "[NOISE] player "
                << playerId
                << " noiseAmount "
                << noiseAmount
                << " -> sound monster investigate"
                << std::endl;
        }
    }
}

void ProcessItemPickupPacket(
    const std::vector<std::string>& parts,
    int senderPlayerId,
    SOCKET clientSocket1,
    SOCKET clientSocket2,
    PlayerState players[],
    std::map<int, ItemState>& items,
    int& clueCount,
    int needClueCount)
{
    // C_ITEM_PICKUP|playerId|itemId|itemType
    // legacy: ITEM_PICKUP|itemId|playerId
    int playerId = senderPlayerId;
    int itemId = 0;
    ItemType itemType = ItemType::Unknown;

    if (parts[0] == "C_ITEM_PICKUP")
    {
        if (parts.size() < 4)
            return;

        if (!TryParseInt(parts[1], playerId) || !TryParseInt(parts[2], itemId))
            return;
        itemType = StringToItemType(parts[3]);
    }
    else
    {
        if (parts.size() < 3)
            return;

        if (!TryParseInt(parts[1], itemId) || !TryParseInt(parts[2], playerId))
            return;
    }

    if (playerId != senderPlayerId || itemId <= 0)
        return;

    auto denyPickup = [&]() {
        SendPacket(GetSocketByPlayerId(senderPlayerId, clientSocket1, clientSocket2),
            "S_ITEM_PICKUP_DENIED|" + std::to_string(itemId));
    };

    if (!players[playerId].hasState || players[playerId].isDead)
    {
        std::cout << "[ITEM DENY] invalid or dead player request. player " << playerId << std::endl;
        denyPickup();
        return;
    }

    ItemState& item = items[itemId];
    if (item.itemId == 0)
    {
        item.itemId = itemId;
        item.type = itemType;
    }

    if (item.type == ItemType::Unknown && itemType != ItemType::Unknown)
        item.type = itemType;

    if (item.isPicked)
    {
        std::cout << "[ITEM DENY] already picked itemId " << itemId << std::endl;
        denyPickup();
        return;
    }

    if (item.type == ItemType::Clue && matchRoles[playerId] != 1)
    {
        std::cout << "[ITEM DENY] Only Explorer may collect clues. player " << playerId << std::endl;
        denyPickup();
        return;
    }

    item.isPicked = true;
    item.pickedByPlayerId = playerId;

    if (item.type == ItemType::Clue)
        clueCount++;

    std::ostringstream oss;
    oss << "S_ITEM_PICKUP|"
        << item.itemId << "|"
        << playerId << "|"
        << ItemTypeToString(item.type) << "|"
        << clueCount;

    BroadcastPacket(clientSocket1, clientSocket2, oss.str());

    if (item.type == ItemType::Clue)
    {
        std::ostringstream clueOss;
        clueOss << "S_CLUE_COUNT|" << clueCount << "|" << needClueCount;
        BroadcastPacket(clientSocket1, clientSocket2, clueOss.str());

        if (clueCount >= needClueCount)
            BroadcastPacket(clientSocket1, clientSocket2, "S_GAME_CLEAR");
    }

    std::cout << "[ITEM PICKUP] item " << itemId
        << " type " << ItemTypeToString(item.type)
        << " player " << playerId
        << " clue " << clueCount << "/" << needClueCount
        << std::endl;
}

void ProcessNavigationPacket(const std::vector<std::string>& parts, int senderPlayerId,
                             SOCKET clientSocket1, SOCKET clientSocket2, MonsterState monsters[])
{
    if (parts.size() < 2 || navigation.ready) return;
    int packetPlayerId = 0;
    if (!TryParseInt(parts[1], packetPlayerId) || packetPlayerId != senderPlayerId) return;
    NavigationUpload& upload = navigationUploads[senderPlayerId];

    if (parts[0] == "C_NAV_BEGIN")
    {
        if (parts.size() != 17) return;
        int seed = 0, width = 0, height = 0;
        NavigationUpload candidate;
        if (!TryParseInt(parts[2], seed) || !TryParseInt(parts[3], width) ||
            !TryParseInt(parts[4], height) || seed < 1 || width < 2 || height < 2 ||
            width > 200 || height > 200 ||
            !TryParseFloat(parts[5], candidate.originX) ||
            !TryParseFloat(parts[6], candidate.originZ) ||
            !TryParseFloat(parts[7], candidate.step) ||
            !TryParseFloat(parts[8], candidate.gateX) ||
            !TryParseFloat(parts[9], candidate.gateZ) ||
            !TryParseFloat(parts[10], candidate.gateHalfWidth) ||
            candidate.step < 0.5f || candidate.step > 4.0f ||
            candidate.gateHalfWidth < 0.0f || candidate.gateHalfWidth > 50.0f)
            return;
        for (int i = 0; i < 2; ++i)
        {
            int offset = 11 + i * 3;
            if (!TryParseFloat(parts[offset], candidate.monsterSpawns[i].x) ||
                !TryParseFloat(parts[offset + 1], candidate.monsterSpawns[i].y) ||
                !TryParseFloat(parts[offset + 2], candidate.monsterSpawns[i].z)) return;
        }
        candidate.width = width;
        candidate.height = height;
        candidate.begun = true;
        for (size_t i = 2; i < parts.size(); ++i)
            candidate.header += "|" + parts[i];
        upload = candidate;
        return;
    }

    if (parts[0] == "C_NAV_CHUNK")
    {
        int sequence = -1;
        if (parts.size() != 4 || !upload.begun || upload.complete ||
            !TryParseInt(parts[2], sequence) || sequence != upload.nextSequence ||
            parts[3].empty() || parts[3].size() > 1000 ||
            upload.mask.size() + parts[3].size() >
                static_cast<size_t>(upload.width * upload.height)) return;
        for (char digit : parts[3])
            if ((digit < '0' || digit > '9') && (digit < 'A' || digit > 'F')) return;
        upload.mask += parts[3];
        ++upload.nextSequence;
        return;
    }

    if (parts[0] != "C_NAV_END" || parts.size() != 2 || !upload.begun ||
        upload.mask.size() != static_cast<size_t>(upload.width * upload.height)) return;
    upload.complete = true;
    if (!navigationUploads[1].complete || !navigationUploads[2].complete) return;

    if (navigationUploads[1].header != navigationUploads[2].header ||
        navigationUploads[1].mask != navigationUploads[2].mask)
    {
        BroadcastPacket(clientSocket1, clientSocket2, "S_NAV_ERROR|Map geometry differs between clients");
        std::cout << "[NAV ERROR] Client maps differ" << std::endl;
        return;
    }

    const NavigationUpload& source = navigationUploads[1];
    navigation.width = source.width;
    navigation.height = source.height;
    navigation.originX = source.originX;
    navigation.originZ = source.originZ;
    navigation.step = source.step;
    navigation.gateX = source.gateX;
    navigation.gateZ = source.gateZ;
    navigation.gateHalfWidth = source.gateHalfWidth;
    navigation.edges.resize(source.mask.size());
    for (size_t i = 0; i < source.mask.size(); ++i)
    {
        char digit = source.mask[i];
        navigation.edges[i] = static_cast<uint8_t>(digit <= '9' ? digit - '0' : digit - 'A' + 10);
    }
    for (int z = 0; z < navigation.height; ++z)
    for (int x = 0; x < navigation.width; ++x)
    {
        int index = navigation.Index(x, z);
        uint8_t edge = navigation.edges[index];
        int right = navigation.Index(x + 1, z);
        int up = navigation.Index(x, z + 1);
        if (((edge & 1) != 0) != (right >= 0 && (navigation.edges[right] & 2) != 0) ||
            ((edge & 4) != 0) != (up >= 0 && (navigation.edges[up] & 8) != 0) ||
            (x == 0 && (edge & 2) != 0) || (z == 0 && (edge & 8) != 0))
        {
            BroadcastPacket(clientSocket1, clientSocket2, "S_NAV_ERROR|Invalid navigation edges");
            return;
        }
    }
    navigation.ready = true;
    for (int i = 0; i < 2; ++i)
    {
        if (navigation.ClosestCell(source.monsterSpawns[i]) < 0)
        {
            navigation.ready = false;
            BroadcastPacket(clientSocket1, clientSocket2, "S_NAV_ERROR|Monster spawn is not walkable");
            return;
        }
        monsters[i].position = source.monsterSpawns[i];
        monsters[i].spawnPosition = source.monsterSpawns[i];
        monsters[i].targetPosition = source.monsterSpawns[i];
        monsters[i].lastKnownPosition = source.monsterSpawns[i];
        monsters[i].state = MonsterAIState::Idle;
        monsters[i].stateUntil = GetTimeSeconds() + 1.5;
        monsters[i].path.clear();
        monsters[i].pathGoal = -1;
        monsters[i].pathCursor = 0;
    }
    BroadcastPacket(clientSocket1, clientSocket2, "S_NAV_READY");
    std::cout << "[NAV READY] " << source.width << "x" << source.height
              << " cells confirmed by both clients" << std::endl;
}

void ProcessClientPacket(
    int senderPlayerId,
    const std::string& packet,
    SOCKET clientSocket1,
    SOCKET clientSocket2,
    PlayerState players[],
    MonsterState monsters[],
    std::map<int, ItemState>& items,
    int& clueCount,
    int needClueCount)
{
    if (packet.empty())
        return;

    std::vector<std::string> parts = Split(packet, '|');
    if (parts.empty())
        return;

    const std::string& type = parts[0];
    if (StartsWith(type, "C_NAV_"))
    {
        ProcessNavigationPacket(parts, senderPlayerId, clientSocket1, clientSocket2, monsters);
        return;
    }
    static std::map<int, MimicShot> mimicShots;

    // One cooperative color/shape puzzle per match. Only the assigned client may
    // report its pad or submit a choice; the server owns the answer and result.
    static bool puzzlePad[3] = {};
    static bool puzzleStarted = false;
    static bool puzzleSolved = false;
    static int puzzleAnswer = -1;
    static int puzzleChoices[4] = {};
    static int puzzleRound = 0;
    static double puzzleRetryAt = 0.0;
    constexpr int puzzleRoundCount = 3;
    // Code = shape * 12 + color * 3 + center mark (4 x 4 x 3).
    auto sendPuzzleRound = [&]() {
        std::random_device random;
        puzzleAnswer = static_cast<int>(random() % 48);
        int shape = puzzleAnswer / 12;
        int color = (puzzleAnswer / 3) % 4;
        int mark = puzzleAnswer % 3;
        puzzleChoices[0] = puzzleAnswer;
        puzzleChoices[1] = shape * 12 + color * 3 + (mark + 1) % 3;
        puzzleChoices[2] = shape * 12 + ((color + 1) % 4) * 3 + mark;
        puzzleChoices[3] = ((shape + 1) % 4) * 12 + color * 3 + mark;
        std::shuffle(puzzleChoices, puzzleChoices + 4, std::mt19937(random()));
        SendPacket(clientSocket1, "S_PUZZLE_CLUE|" + std::to_string(puzzleRound) + "|" + std::to_string(puzzleAnswer));
        std::ostringstream options;
        options << "S_PUZZLE_CHOICES|" << puzzleRound;
        for (int choice : puzzleChoices) options << "|" << choice;
        SendPacket(clientSocket2, options.str());
        BroadcastPacket(clientSocket1, clientSocket2,
            "S_PUZZLE_PROGRESS|" + std::to_string(puzzleRound) + "|" + std::to_string(puzzleRoundCount));
    };
    if (type == "C_PUZZLE_PAD")
    {
        int id = 0, active = 0;
        if (parts.size() != 3 || !TryParseInt(parts[1], id) || id != senderPlayerId ||
            !TryParseInt(parts[2], active) || (active != 0 && active != 1) || puzzleSolved)
            return;
        if (puzzlePad[id] == (active == 1)) return;
        puzzlePad[id] = active == 1;
        BroadcastPacket(clientSocket1, clientSocket2,
            "S_PUZZLE_PADS|" + std::to_string(puzzlePad[1] ? 1 : 0) + "|" +
            std::to_string(puzzlePad[2] ? 1 : 0));
        if (puzzlePad[1] && puzzlePad[2] && !puzzleStarted)
        {
            puzzleStarted = true;
            puzzleRound = 1;
            sendPuzzleRound();
        }
        return;
    }
    if (type == "C_PUZZLE_CHOOSE")
    {
        int id = 0, index = -1;
        if (parts.size() != 3 || !TryParseInt(parts[1], id) || id != 2 || id != senderPlayerId ||
            !TryParseInt(parts[2], index) || index < 0 || index > 3 ||
            !puzzleStarted || puzzleSolved || !puzzlePad[1] || !puzzlePad[2] ||
            GetTimeSeconds() < puzzleRetryAt) return;
        if (puzzleChoices[index] == puzzleAnswer)
        {
            if (puzzleRound < puzzleRoundCount)
            {
                ++puzzleRound;
                sendPuzzleRound();
            }
            else
            {
                puzzleSolved = true;
                gateOpeningTime = GetTimeSeconds() + 1.05;
                BroadcastPacket(clientSocket1, clientSocket2, "S_PUZZLE_SOLVED");
            }
        }
        else
        {
            puzzleRetryAt = GetTimeSeconds() + 1.5;
            BroadcastPacket(clientSocket1, clientSocket2, "S_PUZZLE_WRONG");
        }
        return;
    }

    if (type == "MOVE")
    {
        if (!ProcessMovePacket(parts, senderPlayerId, players))
            return;

        SOCKET otherSocket = GetOtherSocket(senderPlayerId, clientSocket1, clientSocket2);
        if (otherSocket != INVALID_SOCKET)
            SendPacket(otherSocket, packet);

        return;
    }

    if (type == "C_MIMIC_STATE")
    {
        int playerId = 0, mimicId = 0, state = -1;
        if (senderPlayerId != 1 || parts.size() != 4 ||
            !TryParseInt(parts[1], playerId) || playerId != senderPlayerId ||
            !TryParseInt(parts[2], mimicId) || mimicId < 1 || mimicId > 128 ||
            !TryParseInt(parts[3], state) || state < 0 || state > 2) return;
        SendPacket(clientSocket2, "S_MIMIC_STATE|" + std::to_string(mimicId) + "|" +
                                  std::to_string(state));
        return;
    }

    if (type == "C_MIMIC_FIRE")
    {
        int playerId = 0, mimicId = 0, shotId = 0, targetId = 0, damage = 0;
        Vec3 start, direction;
        float speed = 0.0f;
        if (senderPlayerId != 1 || parts.size() != 13 ||
            !TryParseInt(parts[1], playerId) || playerId != senderPlayerId ||
            !TryParseInt(parts[2], mimicId) || mimicId < 1 || mimicId > 128 ||
            !TryParseInt(parts[3], shotId) || shotId < 1 ||
            !TryParseInt(parts[4], targetId) || targetId < 1 || targetId > 2 ||
            !TryParseFloat(parts[5], start.x) || !TryParseFloat(parts[6], start.y) ||
            !TryParseFloat(parts[7], start.z) || !TryParseFloat(parts[8], direction.x) ||
            !TryParseFloat(parts[9], direction.y) || !TryParseFloat(parts[10], direction.z) ||
            !TryParseFloat(parts[11], speed) || !TryParseInt(parts[12], damage) ||
            !std::isfinite(start.x) || !std::isfinite(start.y) || !std::isfinite(start.z) ||
            !std::isfinite(direction.x) || !std::isfinite(direction.y) || !std::isfinite(direction.z) ||
            !std::isfinite(speed) || speed < 0.1f || speed > 30.0f ||
            damage < 1 || damage > 30 || players[targetId].isDead ||
            direction.x * direction.x + direction.y * direction.y + direction.z * direction.z < 0.25f)
            return;

        double now = GetTimeSeconds();
        if (!navigation.gateOpen && gateOpeningTime > 0.0 && now >= gateOpeningTime)
        {
            navigation.gateOpen = true;
            for (int monsterIndex = 0; monsterIndex < 2; ++monsterIndex)
                monsters[monsterIndex].pathGoal = -1;
        }
        for (auto it = mimicShots.begin(); it != mimicShots.end(); )
            it = it->second.expiresAt < now ? mimicShots.erase(it) : std::next(it);
        if (mimicShots.find(shotId) != mimicShots.end()) return;
        mimicShots[shotId] = { mimicId, targetId, damage, now + 6.0 };

        std::ostringstream fire;
        fire << "S_MIMIC_FIRE|" << mimicId << "|" << shotId << "|" << targetId;
        for (size_t i = 5; i <= 12; ++i) fire << "|" << parts[i];
        SendPacket(clientSocket2, fire.str());
        return;
    }

    if (type == "C_MIMIC_HIT")
    {
        int playerId = 0, shotId = 0;
        if (parts.size() != 3 || !TryParseInt(parts[1], playerId) ||
            playerId != senderPlayerId || !TryParseInt(parts[2], shotId)) return;
        auto it = mimicShots.find(shotId);
        if (it == mimicShots.end() || it->second.targetPlayerId != playerId ||
            GetTimeSeconds() > it->second.expiresAt || players[playerId].isDead) return;
        MimicShot shot = it->second;
        mimicShots.erase(it);
        players[playerId].hp = std::max(0, players[playerId].hp - shot.damage);
        if (players[playerId].hp == 0) players[playerId].isDead = true;
        BroadcastPacket(clientSocket1, clientSocket2,
            "S_PLAYER_DAMAGE|" + std::to_string(playerId) + "|" +
            std::to_string(shot.damage) + "|" + std::to_string(shot.mimicId));
        BroadcastPacket(clientSocket1, clientSocket2,
            "S_PLAYER_HP|" + std::to_string(playerId) + "|" +
            std::to_string(players[playerId].hp));
        return;
    }

    if (type == "C_NOISE")
    {
        ProcessNoisePacket(parts, senderPlayerId, players, monsters);
        return;
    }

    if (type == "C_ITEM_PICKUP" || type == "ITEM_PICKUP")
    {
        int requestedId = 0;
        if (type == "C_ITEM_PICKUP" && parts.size() >= 4 && TryParseInt(parts[2], requestedId))
        {
            bool reportsClue = parts[3] == "Clue";
            if (reportsClue != (requestedId >= 1 && requestedId <= 3) ||
                (requestedId == 3 && !puzzleSolved)) {
                SendPacket(GetSocketByPlayerId(senderPlayerId, clientSocket1, clientSocket2),
                    "S_ITEM_PICKUP_DENIED|" + std::to_string(requestedId));
                return;
            }
        }
        ProcessItemPickupPacket(parts, senderPlayerId, clientSocket1, clientSocket2, players, items, clueCount, needClueCount);
        return;
    }

    if (type == "PLAYER_DAMAGE")
    {
        // 서버 권한 구조에서는 클라이언트가 데미지를 확정하지 않는다.
        std::cout << "[IGNORED CLIENT DAMAGE] " << packet << std::endl;
        return;
    }

    if (type == "C_ITEM_USE")
    {
        if (parts.size() < 3) return;

        int reqPlayerId = 0;
        std::string itemType = parts[2];

        if (!TryParseInt(parts[1], reqPlayerId) || reqPlayerId != senderPlayerId) return;

        if (itemType == "Heal")
        {
            players[reqPlayerId].hp = players[reqPlayerId].maxHp;
            std::ostringstream oss;
            oss << "S_PLAYER_HP|" << reqPlayerId << "|" << players[reqPlayerId].hp;
            BroadcastPacket(clientSocket1, clientSocket2, oss.str());
            std::cout << "[ITEM USE] Player " << reqPlayerId << " Heal." << std::endl;
        }
        else if (itemType == "Stealth")
        {
            players[reqPlayerId].isStealth = true;
            players[reqPlayerId].stealthEndTime = GetTimeSeconds() + 5.0;

            std::ostringstream oss;
            oss << "S_PLAYER_STEALTH|" << reqPlayerId << "|1";
            BroadcastPacket(clientSocket1, clientSocket2, oss.str());

            std::cout << "[ITEM USE] Player " << reqPlayerId << " Stealth Started (5s)." << std::endl;
        }
        return;
    }

    if (type == "C_PLAYER_HEAL")
    {
        if (parts.size() < 4) return;

        int reqPlayerId = 0;
        int targetPlayerId = 0;
        int healAmount = 0;

        if (!TryParseInt(parts[1], reqPlayerId) || reqPlayerId != senderPlayerId ||
            !TryParseInt(parts[2], targetPlayerId) || targetPlayerId < 1 || targetPlayerId > 2 ||
            !TryParseInt(parts[3], healAmount) || healAmount <= 0 || healAmount > 100)
            return;

        if (players[targetPlayerId].isDead)
        {
            std::cout << "[HEAL DENY] Target player " << targetPlayerId << " is dead." << std::endl;
            return;
        }

        players[targetPlayerId].hp += healAmount;
        if (players[targetPlayerId].hp > players[targetPlayerId].maxHp)
        {
            players[targetPlayerId].hp = players[targetPlayerId].maxHp;
        }

        std::ostringstream oss;
        oss << "S_PLAYER_HP|" << targetPlayerId << "|" << players[targetPlayerId].hp;
        BroadcastPacket(clientSocket1, clientSocket2, oss.str());

        std::cout << "[SKILL HEAL] Player " << reqPlayerId << " healed Player " << targetPlayerId
            << " (Amount: " << healAmount << ", Current HP: " << players[targetPlayerId].hp << ")" << std::endl;

        return;
    }

    if (type == "C_PLAYER_REVIVE")
    {
        if (parts.size() < 3) return;
        int reqPlayerId = 0;
        int targetPlayerId = 0;
        if (!TryParseInt(parts[1], reqPlayerId) || reqPlayerId != senderPlayerId ||
            !TryParseInt(parts[2], targetPlayerId) || targetPlayerId < 1 || targetPlayerId > 2)
            return;

        // 1. 서버 메모리에서 대상 플레이어를 부활시키고 체력을 100으로 리셋! (가장 중요)
        players[targetPlayerId].hp = players[targetPlayerId].maxHp;
        players[targetPlayerId].isDead = false;

        // 2. 부활시킨 사람과 부활한 사람 양쪽 화면 모두에 S_PLAYER_REVIVE 방송
        std::ostringstream oss;
        oss << "S_PLAYER_REVIVE|" << targetPlayerId;
        BroadcastPacket(clientSocket1, clientSocket2, oss.str());

        std::cout << "[REVIVE] Player " << reqPlayerId << " revived Player " << targetPlayerId << std::endl;
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
    MonsterState monsters[],
    std::map<int, ItemState>& items,
    int& clueCount,
    int needClueCount)
{
    constexpr size_t maxPacketLength = 4096;
    while (true)
    {
        size_t newlineIndex = receiveBuffer.find('\n');

        if (newlineIndex == std::string::npos)
        {
            if (receiveBuffer.size() > maxPacketLength)
            {
                std::cout << "[PACKET DROPPED] line too long" << std::endl;
                receiveBuffer.clear();
            }
            break;
        }

        if (newlineIndex > maxPacketLength)
        {
            std::cout << "[PACKET DROPPED] line too long" << std::endl;
            receiveBuffer.erase(0, newlineIndex + 1);
            continue;
        }

        std::string packet = receiveBuffer.substr(0, newlineIndex);
        receiveBuffer.erase(0, newlineIndex + 1);

        if (!packet.empty() && packet.back() == '\r')
            packet.pop_back();

        if (packet.empty())
            continue;

        ProcessClientPacket(playerId, packet, clientSocket1, clientSocket2, players, monsters, items, clueCount, needClueCount);
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
    monsters[0].moveSpeed = 1.5f;
    monsters[0].patrolSpeed = 1.5f;
    monsters[0].chaseSpeed = 3.8f;
    monsters[0].searchSpeed = 2.3f;
    monsters[0].patrolRadius = 14.0f;
    monsters[0].searchDuration = 7.0;
    monsters[0].loseSightDelay = 1.5;
    monsters[0].sightRange = 7.0f;
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
    monsters[1].moveSpeed = 1.3f;
    monsters[1].patrolSpeed = 1.3f;
    monsters[1].chaseSpeed = 3.5f;
    monsters[1].searchSpeed = 2.0f;
    monsters[1].patrolRadius = 12.0f;
    monsters[1].searchDuration = 6.0;
    monsters[1].loseSightDelay = 3.0;
    monsters[1].sightRange = 3.5f;
    monsters[1].sightAngle = 60.0f;
    monsters[1].hearingRange = 0.0f;
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
    {
        BroadcastMonsterState(clientSocket1, clientSocket2, monsters[i]);
    }
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


    SOCKET slots[3] = {INVALID_SOCKET, INVALID_SOCKET, INVALID_SOCKET};
    int roles[3] = {-1, -1, -1};
    bool ready[3] = {};
    std::string pending[3];
    int host = 0;
    bool started = false;
    auto publish = [&]() {
        std::ostringstream out;
        out << "LOBBY|" << host;
        for (int i=1; i<=2; ++i) out << "|" << (slots[i]!=INVALID_SOCKET) << "|" << roles[i] << "|" << ready[i];
        for (int i=1; i<=2; ++i) if (slots[i]!=INVALID_SOCKET) SendPacket(slots[i],out.str());
    };
    double emptySince = GetTimeSeconds();
    while (!started) {
        if (!host && GetTimeSeconds()-emptySince > 30.0) { closesocket(listenSocket); WSACleanup(); return 0; }
        fd_set reads; FD_ZERO(&reads); FD_SET(listenSocket,&reads);
        for (int i=1;i<=2;++i) if(slots[i]!=INVALID_SOCKET) FD_SET(slots[i],&reads);
        timeval wait = {0,50000};
        if(select(0,&reads,nullptr,nullptr,&wait)==SOCKET_ERROR) return 1;
        if(FD_ISSET(listenSocket,&reads)) {
            SOCKET incoming=accept(listenSocket,nullptr,nullptr);
            int id=slots[1]==INVALID_SOCKET?1:slots[2]==INVALID_SOCKET?2:0;
            if(incoming!=INVALID_SOCKET) {
                if(!id) { SendPacket(incoming,"ERROR|Lobby full"); closesocket(incoming); }
                else { slots[id]=incoming; if(!host) host=id; SendPacket(incoming,"ID|"+std::to_string(id)); publish(); }
            }
        }
        for(int i=1;i<=2;++i) {
            if(slots[i]==INVALID_SOCKET || !FD_ISSET(slots[i],&reads)) continue;
            char data[1024]; int n=recv(slots[i],data,sizeof(data),0);
            if(n<=0) {
                closesocket(slots[i]); slots[i]=INVALID_SOCKET; pending[i].clear(); roles[i]=-1; ready[i]=false;
                host=slots[1]!=INVALID_SOCKET?1:slots[2]!=INVALID_SOCKET?2:0; emptySince=GetTimeSeconds(); publish(); continue;
            }
            pending[i].append(data,n); size_t end;
            while((end=pending[i].find('\n'))!=std::string::npos) {
                auto parts=Split(pending[i].substr(0,end),'|'); pending[i].erase(0,end+1);
                if(parts.empty()) continue;
                int value;
                if(parts[0]=="C_ROLE" && parts.size()==2 && TryParseInt(parts[1],value) && value>=0 && value<=3) { roles[i]=value; ready[i]=false; publish(); }
                else if(parts[0]=="C_READY" && parts.size()==2 && TryParseInt(parts[1],value) && (value==0||value==1) && roles[i]>=0) { ready[i]=value==1; publish(); }
                else if(parts[0]=="C_START" && i==host && slots[1]!=INVALID_SOCKET && slots[2]!=INVALID_SOCKET && ready[1] && ready[2]) {
                    if (roles[1] != 1 && roles[2] != 1) {
                        SendPacket(slots[i], "ERROR|At least one Explorer is required to collect clues");
                    } else {
                        started=true; break;
                    }
                }
            }
            if(pending[i].size()>4096) pending[i].clear();
        }
    }
    clientSocket1=slots[1]; clientSocket2=slots[2];
    closesocket(listenSocket); listenSocket=INVALID_SOCKET;
    std::random_device rd;
    int mapSeed=1+rd()%999999;
    BroadcastPacket(clientSocket1,clientSocket2,"START|"+std::to_string(mapSeed)+"|"+std::to_string(roles[1])+"|"+std::to_string(roles[2]));
    matchRoles[1] = roles[1];
    matchRoles[2] = roles[2];

    PlayerState players[3];
    players[1].playerId = 1;
    players[2].playerId = 2;
    // Apply each lobby selection to server health.
    for (int i=1;i<=2;++i) players[i].hp = players[i].maxHp = roles[i]==3 ? 80 : 100;

    MonsterState monsters[2];
    InitializeMonsters(monsters);

    std::map<int, ItemState> items;
    int clueCount = 0;
    int needClueCount = 3;

    std::string receiveBuffer1;
    std::string receiveBuffer2;

    std::cout << "Start Server Authority Monster Loop." << std::endl;

    double lastUpdateTime = GetTimeSeconds();
    double lastMonsterBroadcastTime = GetTimeSeconds();
    double navigationDeadline = GetTimeSeconds() + 30.0;
    bool navigationTimeoutReported = false;

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
                ProcessReceiveBuffer(1, receiveBuffer1, clientSocket1, clientSocket2, players, monsters, items, clueCount, needClueCount);
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
                ProcessReceiveBuffer(2, receiveBuffer2, clientSocket1, clientSocket2, players, monsters, items, clueCount, needClueCount);
            }
            else
            {
                std::cout << "Client 2 disconnected or recv failed." << std::endl;
                break;
            }
        }

        double now = GetTimeSeconds();
        if (!navigation.ready && !navigationTimeoutReported && now >= navigationDeadline)
        {
            BroadcastPacket(clientSocket1, clientSocket2,
                "S_NAV_ERROR|Timed out waiting for matching navigation maps");
            navigationTimeoutReported = true;
        }
        float deltaTime = static_cast<float>(now - lastUpdateTime);
        lastUpdateTime = now;

        if (deltaTime > 0.2f)
            deltaTime = 0.2f;

        // ========================================================
        // [추가] 은신(Stealth) 지속 시간 체크 및 만료 시 자동 해제
        // ========================================================
        for (int i = 1; i <= 2; ++i)
        {
            if (players[i].isStealth && now >= players[i].stealthEndTime)
            {
                players[i].isStealth = false;

                // 양쪽 클라이언트에 은신 종료 알림 방송 (S_PLAYER_STEALTH|playerId|0)
                std::ostringstream oss;
                oss << "S_PLAYER_STEALTH|" << i << "|0";
                BroadcastPacket(clientSocket1, clientSocket2, oss.str());

                std::cout << "[STEALTH END] Player " << i << " Stealth Expired." << std::endl;
            }
        }

        // 몬스터 AI 판단 및 이동 (은신 여부가 반영된 상태로 시야 판정 진행)
        if (navigation.ready)
            UpdateMonsters(monsters, players, clientSocket1, clientSocket2, deltaTime, now);

        if (navigation.ready && now - lastMonsterBroadcastTime >= 0.1)
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


    return 0;
}
