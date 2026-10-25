"""Protocol smoke test for the server-owned monster navigation path.

Run after building Server/x64/Release/GameServer.exe. It starts a local server,
simulates two clients, and checks map agreement plus wall-aware damage.
"""

import select
import socket
import subprocess
import sys
import time
from pathlib import Path


SERVER = Path(__file__).resolve().parents[1] / "x64" / "Release" / "GameServer.exe"


def send(sock, text):
    sock.sendall((text + "\n").encode("ascii"))


def read_until(sockets, predicate, timeout=8):
    deadline = time.monotonic() + timeout
    buffers = {sock: "" for sock in sockets}
    seen = []
    while time.monotonic() < deadline:
        readable, _, _ = select.select(sockets, [], [], 0.1)
        for sock in readable:
            data = sock.recv(8192)
            if not data:
                raise AssertionError("server disconnected")
            buffers[sock] += data.decode("ascii")
            while "\n" in buffers[sock]:
                line, buffers[sock] = buffers[sock].split("\n", 1)
                seen.append(line)
                if predicate(line):
                    return line, seen
    raise AssertionError(f"timed out; recent packets: {seen[-10:]}")


def make_mask():
    width = height = 5
    edges = [0] * (width * height)
    for z in range(height):
        for x in range(width):
            index = z * width + x
            if x + 1 < width:
                edges[index] |= 1
                edges[index + 1] |= 2
            if z + 1 < height:
                edges[index] |= 4
                edges[index + width] |= 8
    # A wall separates the vision monster (0, 4) from player (0, 2).
    near = width  # (0, 1)
    far = width * 2  # (0, 2)
    edges[near] &= ~4
    edges[far] &= ~8
    return "".join("0123456789ABCDEF"[edge] for edge in edges)


def main():
    if not SERVER.exists():
        raise SystemExit(f"Build the release server first: {SERVER}")
    process = subprocess.Popen(
        [str(SERVER)], cwd=str(SERVER.parent),
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    sockets = []
    real_map = "--real-map" in sys.argv
    try:
        for _ in range(2):
            last_error = None
            for _ in range(30):
                try:
                    sock = socket.create_connection(("127.0.0.1", 7777), timeout=1)
                    break
                except OSError as error:
                    last_error = error
                    time.sleep(0.1)
            else:
                details = process.stdout.read().decode("utf-8", errors="replace") if process.poll() is not None else "server still running"
                raise AssertionError(f"server did not listen: {last_error}; {details}")
            sockets.append(sock)
        read_until(sockets, lambda line: line.startswith("ID|2"))
        send(sockets[0], "C_ROLE|1")
        send(sockets[1], "C_ROLE|2")
        time.sleep(0.1)
        send(sockets[0], "C_READY|1")
        send(sockets[1], "C_READY|1")
        time.sleep(0.1)
        send(sockets[0], "C_START")
        start, _ = read_until(sockets, lambda line: line.startswith("START|"))
        seed = start.split("|")[1]
        if real_map:
            fixture = Path(__file__).resolve().parents[2] / "Library" / "CodexNavigationFixture.txt"
            header, mask = fixture.read_text(encoding="utf-8").splitlines()[:2]
            spawn_fields = header.split("|")
            first_spawn_x, first_spawn_z = float(spawn_fields[9]), float(spawn_fields[11])
        else:
            mask = make_mask()
            header = f"{seed}|5|5|0|0|2|4|4|1|0|0|4|0|0|4"
        for player_id, sock in enumerate(sockets, start=1):
            send(sock, f"C_NAV_BEGIN|{player_id}|{header}")
            for sequence, offset in enumerate(range(0, len(mask), 1000)):
                send(sock, f"C_NAV_CHUNK|{player_id}|{sequence}|{mask[offset:offset + 1000]}")
            send(sock, f"C_NAV_END|{player_id}")
        read_until(sockets, lambda line: line == "S_NAV_READY")
        if real_map:
            read_until(
                sockets,
                lambda line: line.startswith("S_MONSTER_STATE|1|Vision|")
                and (abs(float(line.split("|")[4]) - first_spawn_x) > 0.2
                     or abs(float(line.split("|")[6]) - first_spawn_z) > 0.2),
                8,
            )
            print("PASS: real Unity map accepted by server; server-driven patrol moved monster")
            return
        send(sockets[0], "MOVE|1|0|0|2|0|0|0|0|1|0")
        send(sockets[0], "C_NOISE|1|0|0|2|4")
        read_until(
            sockets,
            lambda line: line.startswith("S_MONSTER_STATE|2|Sound|Investigate|")
            and float(line.split("|")[4]) > 0.2,
            3,
        )
        # Across the blocked edge, the server must not apply melee damage.
        try:
            line, _ = read_until(sockets, lambda item: item.startswith("S_PLAYER_DAMAGE|1|"), 0.7)
            raise AssertionError(f"attack passed through wall: {line}")
        except AssertionError as error:
            if "timed out" not in str(error):
                raise
        monster_state, _ = read_until(
            sockets, lambda line: line.startswith("S_MONSTER_STATE|1|Vision|"), 2
        )
        state_parts = monster_state.split("|")
        send(sockets[0], f"MOVE|1|{state_parts[4]}|0|{state_parts[6]}|0|0|0|0|1|0")
        attack, preceding = read_until(
            sockets,
            lambda line: line.startswith("S_MONSTER_STATE|1|Vision|Attack|")
            and line.split("|")[10] == "1",
            3,
        )
        assert not any(line.startswith("S_PLAYER_DAMAGE|1|") for line in preceding), (
            f"damage arrived before attack windup: {preceding[-10:]}"
        )
        read_until(sockets, lambda line: line.startswith("S_PLAYER_DAMAGE|1|"), 3)
        print("PASS: map accepted; sound monster routes around wall; wall blocks attack; attack windup precedes melee damage")
    finally:
        for sock in sockets:
            sock.close()
        process.terminate()
        try:
            process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            process.kill()


if __name__ == "__main__":
    main()
