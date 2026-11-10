"""Real TCP regression for retained matches. Run after BuildServer.ps1."""
import pathlib
import select
import socket
import subprocess
import tempfile
import time


peers = []


class Peer:
    def __init__(self):
        self.socket = socket.create_connection(("127.0.0.1", 7777))
        self.buffer = b""
        self.messages = []
        self.heartbeat = False
        self.next_ping = 0
        peers.append(self)

    def send(self, packet):
        self.socket.sendall((packet + "\n").encode())

    def wait(self, prefix, timeout=5):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            for index, packet in enumerate(self.messages):
                if packet == prefix or packet.startswith(prefix + "|"):
                    return self.messages.pop(index)
            pump(0.02)
        raise AssertionError(f"Missing {prefix}, recent packets {self.messages[-8:]}")

    def close(self):
        if self.socket is not None:
            self.socket.close()
            self.socket = None


def pump(seconds):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        active = [peer for peer in peers if peer.socket is not None]
        now = time.monotonic()
        for peer in active:
            if peer.heartbeat and now >= peer.next_ping:
                peer.send("C_PING")
                peer.next_ping = now + 2
        if not active:
            time.sleep(0.01)
            continue
        readable, _, _ = select.select([peer.socket for peer in active], [], [], 0.01)
        for peer in active:
            if peer.socket not in readable:
                continue
            data = peer.socket.recv(65536)
            if not data:
                peer.close()
                continue
            peer.buffer += data
            while b"\n" in peer.buffer:
                line, peer.buffer = peer.buffer.split(b"\n", 1)
                peer.messages.append(line.decode().strip())


def upload(peer, player_id, seed):
    edges = [0] * 25
    for z in range(5):
        for x in range(5):
            i = z * 5 + x
            if x < 4:
                edges[i] |= 1
                edges[i + 1] |= 2
            if z < 4:
                edges[i] |= 4
                edges[i + 5] |= 8
    edges[5] &= ~4
    edges[10] &= ~8
    mask = "".join(format(edge, "X") for edge in edges)
    peer.send(f"C_NAV_BEGIN|{player_id}|{seed}|5|5|0|0|2|4|4|1|0|0|4|0|0|4")
    peer.send(f"C_NAV_PAD|{player_id}|1|1|0|1")
    peer.send(f"C_NAV_PAD|{player_id}|2|3|0|1")
    for item in range(1, 4):
        peer.send(f"C_NAV_ITEM|{player_id}|{item}|Clue|1|0.85|1")
    peer.send(f"C_NAV_ITEM|{player_id}|4|Stealth|1|0.85|1")
    peer.send(f"C_NAV_ITEM|{player_id}|5|Heal|1|0.85|1")
    peer.send(f"C_NAV_DOOR|{player_id}|1|7|0|7")
    peer.send(f"C_NAV_CHUNK|{player_id}|0|{mask}")
    peer.send(f"C_NAV_END|{player_id}")


def resume(token):
    peer = Peer()
    peer.wait("S_RESUME_REQUIRED")
    peer.send("C_RESUME|" + token)
    peer.wait("S_RESUME_ACCEPTED")
    peer.wait("START")
    peer.heartbeat = True
    return peer


def restore(peer):
    peer.send("C_RESUME_READY")
    peer.wait("S_SNAPSHOT_END")
    peer.send("C_RESUME_ACK")
    peer.wait("S_RESUME_COMPLETE")


def run():
    first, second = Peer(), Peer()
    for index, peer in enumerate((first, second)):
        peer.wait("LOBBY")
        peer.send(f"C_ROLE|{index}")
        peer.send("C_READY|1")
    pump(0.2)
    first.send("C_START")
    tokens = []
    for peer in (first, second):
        tokens.append(peer.wait("S_SESSION").split("|")[1])
        seed = peer.wait("START").split("|")[1]
        peer.heartbeat = True
    # Reconnect before either initial navigation upload has completed.
    second.close()
    first.wait("S_CONNECTION|2|0")
    second = resume(tokens[1])
    upload(second, 2, seed)
    restore(second)
    assert not any(packet.startswith("S_TIME_LEFT|") for packet in second.messages)
    assert not any(packet.startswith("S_MONSTER_STATE|") for packet in second.messages)
    upload(first, 1, seed)
    first.wait("S_NAV_READY")
    second.wait("S_NAV_READY")
    print("PASS initial map preparation reconnect", flush=True)

    first.send("MOVE|1|1|0|1|42|0|0|0|1|0")
    second.send("MOVE|2|3|0|1|0|0|0|0|1|0")
    first.send("C_ITEM_PICKUP|1|1|Clue")
    first.wait("S_ITEM_PICKUP|1")
    first.send("C_ITEM_PICKUP|1|4|Stealth")
    first.wait("S_ITEM_PICKUP|4")
    first.send("C_ITEM_PICKUP|1|5|Heal")
    first.wait("S_ITEM_PICKUP|5")
    first.send("C_ITEM_USE|1|4|Stealth|1|1|0|1")
    first.wait("S_ITEM_USED|1|4")
    first.send("C_PUZZLE_PAD|1|1|1")
    second.send("C_PUZZLE_PAD|2|2|1")
    first.wait("S_PUZZLE_CLUE")
    first.close()
    second.wait("S_CONNECTION|1|0")
    authority = second.wait("S_AUTHORITY|2")
    second.wait("S_PUZZLE_RESET")
    second.send("C_MIMIC_STATE|2|1|1")
    # Wrong token and a new player cannot take a retained seat.
    stranger = Peer()
    stranger.wait("S_RESUME_REQUIRED")
    stranger.send("C_RESUME|invalid")
    stranger.wait("S_RESUME_REJECTED")
    stranger.close()
    first = resume(tokens[0])
    # Acknowledging without a snapshot must not reactivate the character.
    first.send("C_RESUME_ACK")
    pump(0.1)
    assert "S_RESUME_COMPLETE" not in first.messages
    restore(first)
    own = first.wait("S_SNAPSHOT_PLAYER|1").split("|")
    assert own[2:6] == ["1", "0", "1", "42"], own
    assert float(own[7]) > 0, own
    first.wait("S_SNAPSHOT_ITEM|4|0|Stealth")
    first.wait("S_SNAPSHOT_ITEM|5|1|Heal")
    first.wait("S_CLUE_COUNT|1|3")
    first.wait("S_AUTHORITY|1")
    first.wait("S_MONSTER_STATE")
    print("PASS host loss, authority transfer, puzzle release, token authentication, snapshot position/items/stealth/clues", flush=True)

    # Duplicate connections retain the current live connection.
    duplicate = Peer()
    duplicate.wait("S_RESUME_REQUIRED")
    duplicate.send("C_RESUME|" + tokens[0])
    duplicate.wait("S_RESUME_RETRY")
    duplicate.close()
    # The new authority can publish mimic state to its peer.
    first.send("C_MIMIC_STATE|1|1|2")
    second.wait("S_MIMIC_STATE|1|2")
    # An established connection with no traffic must time out even when select is idle.
    first.heartbeat = second.heartbeat = False
    first.wait("S_PONG")  # Drain at least one prior heartbeat response.
    pump(16)
    assert first.socket is None and second.socket is None
    first = resume(tokens[0])
    restore(first)
    print("PASS heartbeat loss and recovery with no other socket activity", flush=True)
    # Second seat expires while the recovered first participant keeps playing.
    pump(61)
    expired = Peer()
    expired.wait("S_RESUME_REQUIRED")
    expired.send("C_RESUME|" + tokens[1])
    expired.wait("S_RESUME_REJECTED")
    expired.close()
    first.send("C_PING")
    first.wait("S_PONG")
    print("PASS 60-second expiration while remaining player stays connected", flush=True)
    first.close()
    pump(61)
    lobby = Peer()
    lobby.wait("LOBBY")
    lobby.close()
    print("PASS all disconnected seats expire and same process returns to lobby", flush=True)


def run_four_players():
    group = [Peer() for _ in range(4)]
    tokens = []
    for index, peer in enumerate(group):
        peer.wait("LOBBY")
        peer.send(f"C_ROLE|{index}")
        peer.send("C_READY|1")
    pump(0.2)
    group[0].send("C_START")
    for peer in group:
        tokens.append(peer.wait("S_SESSION").split("|")[1])
        seed = peer.wait("START").split("|")[1]
        peer.heartbeat = True
    for index, peer in enumerate(group, 1):
        upload(peer, index, seed)
    for peer in group:
        peer.wait("S_NAV_READY")
    group[0].close()
    group[1].wait("S_AUTHORITY|2")
    group[2].send("MOVE|3|1|0|1|0|0|0|0|1|0")
    group[3].send("MOVE|4|3|0|1|0|0|0|0|1|0")
    group[2].send("C_PUZZLE_PAD|3|1|1")
    group[3].send("C_PUZZLE_PAD|4|2|1")
    for round_number in range(1, 4):
        answer = group[2].wait(f"S_PUZZLE_CLUE|{round_number}").split("|")[2]
        options = group[3].wait(f"S_PUZZLE_CHOICES|{round_number}").split("|")[2:]
        group[3].send(f"C_PUZZLE_CHOOSE|4|{options.index(answer)}")
    group[2].wait("S_PUZZLE_SOLVED")
    for item in range(1, 4):
        group[2].send(f"C_ITEM_PICKUP|3|{item}|Clue")
    for peer in group[1:]:
        peer.wait("S_GAME_CLEAR")
    group[0] = resume(tokens[0])
    restore(group[0])
    group[0].wait("S_GAME_CLEAR")
    group[0].wait("S_PUZZLE_SOLVED")
    group[0].wait("S_CLUE_COUNT|3|3")
    # A disconnected voter cannot leave the other three stuck at the result screen.
    for index in range(3):
        group[index].send(f"C_RESTART|{index + 1}")
    pump(0.1)
    group[3].close()
    for peer in group[:3]:
        peer.wait("S_RESTART")
        peer.close()
    print("PASS four-player host loss, remaining-player puzzle/clear, result reconnect, disconnected restart voter", flush=True)


if __name__ == "__main__":
    # Refuse to disturb an existing server.
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 7777))
    executable = pathlib.Path(__file__).with_name("GameServer.exe")
    with tempfile.TemporaryFile() as log:
        server = subprocess.Popen([str(executable)], stdout=log, stderr=log,
                                  creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            time.sleep(0.5)
            run()
            run_four_players()
        finally:
            for peer in peers:
                peer.close()
            server.terminate()
            server.wait(timeout=5)
