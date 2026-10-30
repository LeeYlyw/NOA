#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <queue>
#include <string>
#include <vector>

// A compact, server-owned copy of the actual Unity NavMesh connectivity.
// Each cell stores edges to +X, -X, +Z, -Z. The server never asks a client
// where a monster should go after this map has been accepted.
struct NavigationDoor
{
    int id = 0;
    Vec3 position;
    bool open = false;
    int direction = 1;
    double openingUntil = 0.0;
};

struct ServerNavigation
{
    int width = 0;
    int height = 0;
    float originX = 0.0f;
    float originZ = 0.0f;
    float step = 2.0f;
    float gateX = 0.0f;
    float gateZ = 0.0f;
    float gateHalfWidth = 0.0f;
    bool gateOpen = false;
    bool ready = false;
    std::vector<uint8_t> edges;
    std::vector<NavigationDoor> doors;

    int Index(int x, int z) const
    {
        return x >= 0 && z >= 0 && x < width && z < height ? z * width + x : -1;
    }

    Vec3 Center(int index) const
    {
        int x = index % width;
        int z = index / width;
        return { originX + step * x, 0.0f, originZ + step * z };
    }

    int ClosestCell(const Vec3& position) const
    {
        if (!ready) return -1;
        int baseX = static_cast<int>(std::round((position.x - originX) / step));
        int baseZ = static_cast<int>(std::round((position.z - originZ) / step));
        int best = -1;
        float bestDistance = std::numeric_limits<float>::infinity();
        for (int dz = -2; dz <= 2; ++dz)
        for (int dx = -2; dx <= 2; ++dx)
        {
            int index = Index(baseX + dx, baseZ + dz);
            if (index < 0 || edges[index] == 0) continue;
            Vec3 center = Center(index);
            float distance = (center.x - position.x) * (center.x - position.x) +
                             (center.z - position.z) * (center.z - position.z);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }
        return bestDistance <= step * step * 6.0f ? best : -1;
    }

    bool CanStep(int from, int to, bool ignoreDoors = false) const
    {
        if (!ready || from < 0 || to < 0 || from >= static_cast<int>(edges.size()) ||
            to >= static_cast<int>(edges.size())) return false;
        int fx = from % width, fz = from / width;
        int tx = to % width, tz = to / width;
        int bit = 0;
        if (tx == fx + 1 && tz == fz) bit = 1;
        else if (tx == fx - 1 && tz == fz) bit = 2;
        else if (tz == fz + 1 && tx == fx) bit = 4;
        else if (tz == fz - 1 && tx == fx) bit = 8;
        if (bit == 0 || (edges[from] & bit) == 0) return false;

        if (!gateOpen && (bit == 4 || bit == 8))
        {
            Vec3 a = Center(from), b = Center(to);
            if ((a.z - gateZ) * (b.z - gateZ) <= 0.0f &&
                std::fabs(a.x - gateX) <= gateHalfWidth + step * 0.5f)
                return false;
        }
        Vec3 a = Center(from), b = Center(to);
        for (const NavigationDoor& door : doors)
        {
            if (ignoreDoors || door.open) continue;
            float dx = b.x - a.x, dz = b.z - a.z;
            float lengthSquared = dx * dx + dz * dz;
            float t = lengthSquared > 0.0f
                ? std::max(0.0f, std::min(1.0f,
                    ((door.position.x - a.x) * dx +
                     (door.position.z - a.z) * dz) / lengthSquared))
                : 0.0f;
            float distanceX = a.x + t * dx - door.position.x;
            float distanceZ = a.z + t * dz - door.position.z;
            if (distanceX * distanceX + distanceZ * distanceZ < 1.5f * 1.5f)
                return false;
        }
        return true;
    }

    bool CanTraverse(int from, int to, bool ignoreDoors = false) const
    {
        if (CanStep(from, to, ignoreDoors)) return true;
        if (from < 0 || to < 0 || from >= width * height || to >= width * height)
            return false;
        int fx = from % width, fz = from / width;
        int tx = to % width, tz = to / width;
        if (std::abs(tx - fx) != 1 || std::abs(tz - fz) != 1) return false;
        int viaX = Index(tx, fz), viaZ = Index(fx, tz);
        // Require both routes around the corner, so a diagonal cannot cut a wall.
        return CanStep(from, viaX, ignoreDoors) && CanStep(viaX, to, ignoreDoors) &&
               CanStep(from, viaZ, ignoreDoors) && CanStep(viaZ, to, ignoreDoors);
    }

    int BlockingDoor(int from, int to) const
    {
        if (!CanTraverse(from, to, true) || CanTraverse(from, to)) return 0;
        Vec3 a = Center(from), b = Center(to);
        float dx = b.x - a.x, dz = b.z - a.z;
        float lengthSquared = dx * dx + dz * dz;
        int bestId = 0;
        float bestDistanceSquared = 1.5f * 1.5f;
        for (const NavigationDoor& door : doors)
        {
            if (door.open) continue;
            float t = lengthSquared > 0.0f
                ? std::max(0.0f, std::min(1.0f,
                    ((door.position.x - a.x) * dx +
                     (door.position.z - a.z) * dz) / lengthSquared)) : 0.0f;
            float x = a.x + t * dx - door.position.x;
            float z = a.z + t * dz - door.position.z;
            float distanceSquared = x * x + z * z;
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestId = door.id;
            }
        }
        if (bestId == 0 && from % width != to % width && from / width != to / width)
        {
            int viaX = Index(to % width, from / width);
            int viaZ = Index(from % width, to / width);
            // A door can block one of the orthogonal edges required for a
            // diagonal step even when its anchor misses the diagonal itself.
            int blocked = BlockingDoor(from, viaX);
            if (blocked != 0) return blocked;
            blocked = BlockingDoor(viaX, to);
            if (blocked != 0) return blocked;
            blocked = BlockingDoor(from, viaZ);
            if (blocked != 0) return blocked;
            blocked = BlockingDoor(viaZ, to);
            if (blocked != 0) return blocked;
        }
        return bestId;
    }

    bool HasLineOfSight(const Vec3& from, const Vec3& to) const
    {
        int previous = ClosestCell(from);
        if (previous < 0 || ClosestCell(to) < 0) return false;
        float dx = to.x - from.x, dz = to.z - from.z;
        int samples = std::max(1, static_cast<int>(std::ceil(std::sqrt(dx * dx + dz * dz) /
                                                              (step * 0.25f))));
        for (int i = 1; i <= samples; ++i)
        {
            float t = static_cast<float>(i) / samples;
            Vec3 point = { from.x + dx * t, 0.0f, from.z + dz * t };
            int cell = ClosestCell(point);
            if (cell < 0) return false;
            if (cell != previous)
            {
                int px = previous % width, pz = previous / width;
                int cx = cell % width, cz = cell / width;
                if (px != cx && pz != cz)
                {
                    if (!CanTraverse(previous, cell)) return false;
                }
                else if (!CanStep(previous, cell)) return false;
            }
            previous = cell;
        }
        return true;
    }

    std::vector<int> FindPath(int start, int goal, bool ignoreDoors = false) const
    {
        if (!ready || start < 0 || goal < 0) return {};
        if (start == goal) return { start };
        const int count = width * height;
        std::vector<float> cost(count, std::numeric_limits<float>::infinity());
        std::vector<int> parent(count, -1);
        using QueueNode = std::pair<float, int>;
        std::priority_queue<QueueNode, std::vector<QueueNode>, std::greater<QueueNode>> open;
        auto heuristic = [&](int index) {
            int dx = std::abs(index % width - goal % width);
            int dz = std::abs(index / width - goal / width);
            return static_cast<float>(std::max(dx, dz)) + 0.41421356f * std::min(dx, dz);
        };
        cost[start] = 0.0f;
        open.push({ heuristic(start), start });
        while (!open.empty())
        {
            int current = open.top().second;
            float priority = open.top().first;
            open.pop();
            if (priority > cost[current] + heuristic(current) + 0.001f) continue;
            if (current == goal) break;
            int x = current % width, z = current / width;
            int neighbors[8] = { Index(x + 1, z), Index(x - 1, z),
                                 Index(x, z + 1), Index(x, z - 1),
                                 Index(x + 1, z + 1), Index(x + 1, z - 1),
                                 Index(x - 1, z + 1), Index(x - 1, z - 1) };
            for (int next : neighbors)
            {
                if (!CanTraverse(current, next, ignoreDoors)) continue;
                float travel = (next % width != x && next / width != z) ? 1.41421356f : 1.0f;
                if (cost[next] <= cost[current] + travel) continue;
                cost[next] = cost[current] + travel;
                parent[next] = current;
                open.push({ cost[next] + heuristic(next), next });
            }
        }
        if (parent[goal] < 0) return {};
        std::vector<int> path;
        for (int node = goal; node >= 0; node = parent[node])
        {
            path.push_back(node);
            if (node == start) break;
        }
        std::reverse(path.begin(), path.end());
        return path;
    }
};
