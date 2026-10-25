# Server-owned monster navigation

The server owns vision and sound monster decisions, pathfinding, movement, and damage in multiplayer. Offline mode still runs the Unity AI components.

## Map handoff

1. The server sends a seed. Both Unity clients generate the same layout and build a runtime NavMesh from Physics Colliders.
2. Before spawning the dynamic puzzle gate, each client samples the static NavMesh at 1 m spacing. Each cell sends its four traversable edges. Both clients upload the mask, gate position, and monster spawns in `C_NAV_BEGIN`, `C_NAV_CHUNK`, and `C_NAV_END` packets.
3. The server compares both complete uploads. It refuses to start monster AI if they differ, and sends `S_NAV_READY` only after validation.
4. The server runs eight-direction A* over this graph. Diagonal movement requires both adjacent cardinal routes, preventing corner cuts. The puzzle gate blocks matching edges until its opening animation finishes. The same graph gates monster sight and melee attacks.

The mask represents navigability at 1 m resolution. It does not reproduce Unity's complete Collider geometry or animation physics. Player movement and mimic behavior still use their existing authority paths. This is a server-owned monster simulation based on a map snapshot verified by both clients, not a fully trustless server-generated world.

## Build and test

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Server/BuildServer.ps1` before multiplayer testing. Unity Editor prefers `Server/x64/Release/GameServer.exe`; a packaged player can use `Server/GameServer.exe`. The old Debug executable is intentionally not selected.

Checks performed:

- `Assembly-CSharp.csproj` builds with no errors.
- Unity 6000.0.44f1 Play Mode map smoke validates seeds 522364, 12345, and 98765, deterministic grid output, wall edges, and monster spawn connectivity.
- Release C++ server builds.
- `python Server/GameServer/ServerNavigationSmoke.py` simulates two clients and checks map agreement, pathfinding around a wall, blocked melee through a wall, and successful unobstructed melee.

Run a final two-PC session before using this branch for a demonstration. Check that both clients show `[Network] Server navigation map confirmed`, that monsters move on both screens, and that the closed puzzle gate cannot be crossed by a monster. For the previous demonstration version, switch back to `main`; keep this branch until the two-PC test passes.
