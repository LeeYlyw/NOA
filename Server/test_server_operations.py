"""Session lifecycle regressions. Run after BuildServer.ps1."""
import pathlib
import socket
import subprocess
import tempfile
import time

from test_reconnect import Peer, peers, pump, resume


def start_pair():
    pair = [Peer(), Peer()]
    for index, peer in enumerate(pair):
        peer.wait("LOBBY")
        peer.send(f"C_ROLE|{index}")
        peer.send("C_READY|1")
        peer.heartbeat = True
    pump(0.2)
    pair[0].send("C_START")
    tokens = []
    for peer in pair:
        tokens.append(peer.wait("S_SESSION").split("|")[1])
        peer.wait("START")
    return pair, tokens


def check_next_lobby():
    probe = Peer()
    probe.wait("LOBBY")
    probe.close()
    pump(0.1)


def run():
    first, host, departing = Peer(), Peer(), Peer()
    for peer in (first, host, departing):
        peer.wait("LOBBY")
    first.close()
    host.wait("LOBBY|2")
    replacement = Peer()
    replacement.wait("LOBBY|2")
    pump(0.1)
    host.messages.clear()
    departing.close()
    host.wait("LOBBY|2")
    replacement.close()
    host.close()
    pump(0.1)
    print("PASS lobby host stays assigned when a non-host leaves", flush=True)

    pair, tokens = start_pair()
    pair[0].send("C_NAV_FAIL|1")
    for peer in pair:
        peer.wait("S_NAV_ERROR")
        peer.wait("S_SESSION_END|MAP_FAILED")
        peer.close()
    check_next_lobby()
    print("PASS initial map failure releases match and accepts next lobby", flush=True)

    pair, tokens = start_pair()
    pair[0].close()
    pair[1].wait("S_CONNECTION|1|0")
    pair[0] = resume(tokens[0])
    pair[0].send("C_NAV_FAIL|1")
    for peer in pair:
        peer.wait("S_SESSION_END|MAP_FAILED")
        peer.close()
    check_next_lobby()
    print("PASS map failure during reconnect releases match", flush=True)

    pair, _ = start_pair()
    for peer in pair:
        peer.wait("S_SESSION_END|MAP_FAILED", timeout=35)
        peer.close()
    check_next_lobby()
    print("PASS map upload timeout releases match", flush=True)


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
