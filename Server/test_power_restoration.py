"""Exercise power quest ownership, range checks and reconnect over TCP."""
import pathlib
import socket
import subprocess
import tempfile
import time

from test_reconnect import Peer, peers, pump, restore, resume, upload


def upload_with_power(peer, player_id, seed):
    original_send = peer.send

    def send(packet):
        if packet.startswith("C_NAV_END|"):
            for index in range(6):
                kind = "PowerFuse" if index < 3 else "PowerBreaker"
                original_send(f"C_NAV_ITEM|{player_id}|{240 + index}|{kind}|{index + 1}|0.5|1")
        original_send(packet)

    peer.send = send
    try:
        upload(peer, player_id, seed)
    finally:
        peer.send = original_send


def run():
    group = [Peer(), Peer()]
    for index, peer in enumerate(group):
        peer.wait("LOBBY")
        peer.send(f"C_ROLE|{index}")
        peer.send("C_READY|1")
        peer.heartbeat = True
    pump(0.2)
    group[0].send("C_START")
    tokens = []
    for peer in group:
        tokens.append(peer.wait("S_SESSION").split("|")[1])
        seed = peer.wait("START").split("|")[1]
    for index, peer in enumerate(group, 1):
        upload_with_power(peer, index, seed)
    for peer in group:
        peer.wait("S_NAV_READY")
    group[0].send("MOVE|1|1|0|1|0|0|0|0|1|0")
    group[1].send("MOVE|2|3|0|1|0|0|0|0|1|0")
    group[0].send("C_PUZZLE_PAD|1|1|1")
    group[1].send("C_PUZZLE_PAD|2|2|1")
    for round_number in range(1, 4):
        answer = group[0].wait(f"S_PUZZLE_CLUE|{round_number}").split("|")[2]
        options = group[1].wait(f"S_PUZZLE_CHOICES|{round_number}").split("|")[2:]
        group[1].send(f"C_PUZZLE_CHOOSE|2|{options.index(answer)}")
    group[0].wait("S_PUZZLE_SOLVED")
    group[0].send("C_ITEM_PICKUP|1|3|Clue")
    group[0].wait("S_ITEM_PICKUP_DENIED|3")
    group[1].send("MOVE|2|6|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|240")
    pump(0.2)
    assert not any(message.startswith("S_POWER|") for message in group[1].messages)
    group[0].send("C_POWER|1|240")
    for peer in group:
        peer.wait("S_POWER|1|0|0|0|0|0")
    group[0].send("C_POWER|1|243")
    pump(0.2)
    assert not any(message.startswith("S_POWER|") for message in group[0].messages)
    group[0].close()
    group[1].wait("S_CONNECTION|1|0")
    group[0] = resume(tokens[0])
    restore(group[0])
    group[0].wait("S_POWER|1|0|0|0|0|0")
    group[0].send("MOVE|1|4|0|1|0|0|0|0|1|0")
    group[0].send("C_POWER|1|243")
    for peer in group:
        peer.wait("S_POWER|-1|0|0|1|0|0")
    group[1].send("MOVE|2|2|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|241")
    group[1].wait("S_POWER|-1|2|0|1|0|0")
    group[1].send("MOVE|2|5|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|244")
    group[1].wait("S_POWER|-1|-1|0|1|1|0")
    group[1].send("MOVE|2|3|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|242")
    group[1].wait("S_POWER|-1|-1|2|1|1|0")
    group[1].send("MOVE|2|6|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|245")
    for peer in group:
        peer.wait("S_POWER|-1|-1|-1|1|1|1")
    group[0].send("MOVE|1|1|0|1|0|0|0|0|1|0")
    group[0].send("C_ITEM_PICKUP|1|3|Clue")
    group[0].wait("S_ITEM_PICKUP|3|1|Clue")
    print("PASS power quest range, ownership, reconnect, three repairs and clue gate", flush=True)


def run_death():
    group = [Peer(), Peer()]
    for index, peer in enumerate(group):
        peer.wait("LOBBY")
        peer.send(f"C_ROLE|{index}")
        peer.send("C_READY|1")
        peer.heartbeat = True
    pump(0.2)
    group[0].send("C_START")
    for peer in group:
        peer.wait("S_SESSION")
        seed = peer.wait("START").split("|")[1]
    for index, peer in enumerate(group, 1):
        upload_with_power(peer, index, seed)
    for peer in group:
        peer.wait("S_NAV_READY")
    group[0].send("MOVE|1|1|0|1|0|0|0|0|1|0")
    group[1].send("MOVE|2|6|0|1|0|0|0|0|1|0")
    group[0].send("C_POWER|1|240")
    group[1].wait("S_POWER|1|0|0|0|0|0")
    for shot in range(1, 5):
        group[0].send(f"C_MIMIC_FIRE|1|1|{shot}|1|1|1|1|1|0|0|10|30")
        pump(0.05)
        group[0].send(f"C_MIMIC_HIT|1|{shot}")
        group[1].wait(f"S_PLAYER_HP|1|{max(0, 100 - shot * 30)}")
    group[1].wait("S_POWER|0|0|0|0|0|0")
    group[1].send("MOVE|2|1|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|240")
    group[1].wait("S_POWER|2|0|0|0|0|0")
    print("PASS defeated player drops fuse and teammate can pick it up", flush=True)


if __name__ == "__main__":
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 7777))
    with tempfile.TemporaryFile() as log:
        for scenario in (run, run_death):
            server = subprocess.Popen([str(pathlib.Path(__file__).with_name("GameServer.exe"))],
                                      stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                time.sleep(0.5)
                scenario()
            finally:
                for peer in peers:
                    peer.close()
                server.terminate()
                server.wait(timeout=5)
