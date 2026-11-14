"""Exercise power quest ownership, range checks and reconnect over TCP."""
import pathlib
import socket
import subprocess
import tempfile
import time
import argparse

from test_reconnect import Peer, peers, pump, restore, resume, upload


def upload_with_power(peer, player_id, seed):
    original_send = peer.send

    def send(packet):
        # Keep monsters in a separate two-cell room so timed repair assertions
        # are not interrupted by combat; sound can still reach that room.
        if packet.startswith("C_NAV_BEGIN|"):
            parts = packet.split("|")
            parts[11:17] = ["8", "0", "8", "6", "0", "8"]
            packet = "|".join(parts)
        elif packet.startswith("C_NAV_CHUNK|"):
            parts = packet.split("|")
            edges = [int(value, 16) for value in parts[3]]
            edges[18] &= ~4
            edges[19] &= ~4
            edges[22] &= ~1
            edges[23] = 1
            edges[24] = 2
            parts[3] = "".join(format(edge, "X") for edge in edges)
            packet = "|".join(parts)
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


def finish_repair(peer, exercise_failure=False):
    challenge = peer.wait("S_REPAIR").split("|")
    if exercise_failure:
        # A valid-looking hit sent before its server deadline must fail.
        elapsed = (float(challenge[4]) + float(challenge[5]) / 2) * float(challenge[6])
        peer.send(f"C_REPAIR_HIT|{challenge[2]}|{elapsed:.4f}")
        failed = peer.wait("S_REPAIR").split("|")
        assert failed[3] == "0" and failed[7] == "MISS"
        # Replaying the old sequence must not produce a new challenge.
        peer.send(f"C_REPAIR_HIT|{challenge[2]}|{elapsed:.4f}")
        pump(0.1)
        assert not any(m.startswith("S_REPAIR|") for m in peer.messages)
        peer.send(f"C_REPAIR_HIT|{failed[2]}|0")
        challenge = peer.wait("S_REPAIR").split("|")
    checked_loss = False
    while True:
        elapsed = (float(challenge[4]) + float(challenge[5]) / 2) * float(challenge[6])
        pump(elapsed)
        peer.send(f"C_REPAIR_HIT|{challenge[2]}|{elapsed:.4f}")
        if int(challenge[3]) == 2:
            peer.wait("S_REPAIR_END|DONE")
            return
        challenge = peer.wait("S_REPAIR").split("|")
        assert challenge[7] == "OK"
        if exercise_failure and not checked_loss:
            assert challenge[3] == "1"
            peer.send(f"C_REPAIR_HIT|{challenge[2]}|0")
            challenge = peer.wait("S_REPAIR").split("|")
            assert challenge[3] == "0" and challenge[7] == "MISS"
            checked_loss = True


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
    # Starting and cancelling must preserve the fuse and leave the breaker closed.
    group[0].wait("S_REPAIR")
    assert not any(m.startswith("S_POWER|-1") for m in group[0].messages)
    group[0].send("C_REPAIR_CANCEL")
    group[0].wait("S_REPAIR_END|CANCEL")
    group[0].send("C_POWER|1|243")
    finish_repair(group[0], exercise_failure=True)
    for peer in group:
        peer.wait("S_POWER|-1|0|0|1|0|0")
    group[1].send("MOVE|2|2|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|241")
    group[1].wait("S_POWER|-1|2|0|1|0|0")
    group[1].send("MOVE|2|5|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|244")
    finish_repair(group[1])
    group[1].wait("S_POWER|-1|-1|0|1|1|0")
    group[1].send("MOVE|2|3|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|242")
    group[1].wait("S_POWER|-1|-1|2|1|1|0")
    group[1].send("MOVE|2|6|0|1|0|0|0|0|1|0")
    group[1].send("C_POWER|2|245")
    finish_repair(group[1])
    for peer in group:
        peer.wait("S_POWER|-1|-1|-1|1|1|1")
    group[0].send("MOVE|1|1|0|1|0|0|0|0|1|0")
    group[0].send("C_ITEM_PICKUP|1|3|Clue")
    group[0].wait("S_ITEM_PICKUP|3|1|Clue")
    print("PASS power quest range, ownership, reconnect, three repairs and clue gate", flush=True)


def run_cancel():
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
    for index, peer in enumerate(group, 1):
        peer.send(f"MOVE|{index}|2|0|1|0|0|0|0|1|0")
        peer.send(f"C_POWER|{index}|{239 + index}")
    group[0].wait("S_POWER|1|2|0|0|0|0")
    for index, peer in enumerate(group, 1):
        peer.send(f"MOVE|{index}|4|0|1|0|0|0|0|1|0")
    group[0].send("C_POWER|1|243")
    old = group[0].wait("S_REPAIR").split("|")
    group[1].send("C_POWER|2|243")
    group[1].wait("S_REPAIR_END|BUSY")
    group[0].send("MOVE|1|0|0|1|0|0|0|0|1|0")
    group[0].wait("S_REPAIR_END|CANCEL")
    group[1].send("C_POWER|2|243")
    group[1].wait("S_REPAIR")
    group[1].close()
    group[0].wait("S_CONNECTION|2|0")
    group[1] = resume(tokens[1])
    restore(group[1])
    group[1].wait("S_POWER|1|2|0|0|0|0")
    group[1].send("C_POWER|2|243")
    fresh = group[1].wait("S_REPAIR").split("|")
    assert fresh[3] == "0" and fresh[2] != old[2]
    group[1].send("C_REPAIR_CANCEL")
    group[1].wait("S_REPAIR_END|CANCEL")
    print("PASS exclusive repair, range cancellation and reconnect releases repair without consuming fuse", flush=True)


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
    parser = argparse.ArgumentParser()
    parser.add_argument("--scenario", choices=("all", "repair", "cancel", "death"), default="all")
    selected = parser.parse_args().scenario
    scenarios = {"repair": run, "cancel": run_cancel, "death": run_death}
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 7777))
    with tempfile.TemporaryFile() as log:
        for scenario in (scenarios.values() if selected == "all" else [scenarios[selected]]):
            server = subprocess.Popen([str(pathlib.Path(__file__).with_name("GameServer.exe"))],
                                      stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                time.sleep(0.5)
                scenario()
                if scenario == run:
                    log.seek(0)
                    assert b"-> sound monster investigate" in log.read(), "Repair failure did not attract sound monster"
            finally:
                for peer in peers:
                    peer.close()
                server.terminate()
                server.wait(timeout=5)
