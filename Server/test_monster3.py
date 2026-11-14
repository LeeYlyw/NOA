"""Three-monster navigation, state broadcast and reconnect regression."""
import pathlib
import socket
import subprocess
import tempfile
import time
from test_reconnect import Peer, peers, pump, upload, resume, restore


def upload_three(peer, player_id, seed):
    original = peer.send
    def send(packet):
        if packet.startswith("C_NAV_BEGIN|"):
            packet += "|8|0|8"
        elif packet.startswith("C_NAV_DOOR|"):
            # Keep the unrelated closed-door fixture away from monster 3's sightline.
            packet = f"C_NAV_DOOR|{player_id}|1|0|0|0"
        original(packet)
    peer.send = send
    try:
        upload(peer, player_id, seed)
    finally:
        peer.send = original


def run():
    group = [Peer(), Peer()]
    for i, peer in enumerate(group):
        peer.wait("LOBBY")
        peer.send(f"C_ROLE|{i}")
        peer.send("C_READY|1")
        peer.heartbeat = True
    pump(0.2)
    group[0].send("C_START")
    tokens = []
    for peer in group:
        tokens.append(peer.wait("S_SESSION").split("|")[1])
        seed = peer.wait("START").split("|")[1]
    for i, peer in enumerate(group, 1):
        upload_three(peer, i, seed)
    for peer in group:
        peer.wait("S_NAV_READY")
        for monster_id, kind in ((1, "Vision"), (2, "Sound"), (3, "Vision")):
            peer.wait(f"S_MONSTER_STATE|{monster_id}|{kind}")
    group[0].send("MOVE|1|7|0|8|0|0|0|0|1|0")
    group[1].send("MOVE|2|1|0|1|0|0|0|0|1|0")
    attacks = [peer.wait("S_MONSTER_STATE|3|Vision|Attack") for peer in group]
    assert attacks[0] == attacks[1], "Attack pose/sequence differs between peers"
    for peer in group:
        peer.wait("S_PLAYER_HP|1|90")
    pump(0.3)
    states = [{m for m in peer.messages if m.startswith("S_MONSTER_STATE|3|")} for peer in group]
    assert len(states[0] & states[1]) >= 2, "Shared monster poses not received"
    for peer in group:
        peer.messages = [m for m in peer.messages if not m.startswith("S_MONSTER_STATE|3|")]
    group[0].send("MOVE|1|8|0|4|0|0|0|0|1|0")
    for peer in group:
        peer.wait("S_MONSTER_STATE|3|Vision|Chase")
    pump(0.6)
    moving = [{m for m in peer.messages if m.startswith("S_MONSTER_STATE|3|Vision|Chase|")}
              for peer in group]
    shared = moving[0] & moving[1]
    positions = {tuple(m.split("|")[4:7]) for m in shared}
    assert len(positions) >= 2, "Moving positions were not synchronized"
    group[1].close()
    group[0].wait("S_CONNECTION|2|0")
    group[1] = resume(tokens[1])
    restore(group[1])
    group[1].wait("S_MONSTER_STATE|3|Vision")
    assert any(m.startswith("S_SNAPSHOT_PLAYER|1|") for m in group[1].messages)
    print("PASS three spawns, identical chase positions/attack sequence/damage on two peers, reconnect snapshot", flush=True)


if __name__ == "__main__":
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 7777))
    with tempfile.TemporaryFile() as log:
        server = subprocess.Popen([str(pathlib.Path(__file__).with_name("GameServer.exe"))],
                                  stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            time.sleep(0.5)
            run()
        finally:
            for peer in peers:
                peer.close()
            server.terminate()
            server.wait(timeout=5)
