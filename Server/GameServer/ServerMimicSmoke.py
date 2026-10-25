"""Local TCP check for mimic interception and server hit validation."""

import socket
import subprocess
import time

from ServerNavigationSmoke import SERVER, read_until, send


def main():
    process = subprocess.Popen(
        [str(SERVER)], cwd=str(SERVER.parent),
        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    sockets = []
    try:
        for _ in range(2):
            for attempt in range(30):
                try:
                    sockets.append(socket.create_connection(("127.0.0.1", 7777), timeout=1))
                    break
                except OSError:
                    if attempt == 29:
                        raise
                    time.sleep(0.1)
        read_until(sockets, lambda line: line == "ID|2")
        send(sockets[0], "C_ROLE|1")
        send(sockets[1], "C_ROLE|2")
        time.sleep(0.1)
        send(sockets[0], "C_READY|1")
        send(sockets[1], "C_READY|1")
        time.sleep(0.1)
        send(sockets[0], "C_START")
        read_until(sockets, lambda line: line.startswith("START|"))
        send(sockets[0], "MOVE|1|2|0|0|0|0|0|0|1|0")
        send(sockets[1], "MOVE|2|4|0|0|0|0|0|0|1|0")
        time.sleep(0.1)
        send(sockets[0], "C_MIMIC_FIRE|1|1|100|2|0|1|0|1|0|0|10|8")
        time.sleep(0.25)
        send(sockets[0], "C_MIMIC_HIT|1|100")
        read_until(sockets, lambda line: line == "S_PLAYER_DAMAGE|1|8|1", 3)
        send(sockets[1], "C_MIMIC_HIT|2|100")
        send(sockets[0], "C_MIMIC_FIRE|1|1|101|2|0|1|0|0|0|1|10|8")
        send(sockets[1], "C_MIMIC_HIT|2|101")
        try:
            line, _ = read_until(sockets, lambda item: item.startswith("S_PLAYER_DAMAGE|2|"), 0.5)
            raise AssertionError(f"off-trajectory hit was accepted: {line}")
        except AssertionError as error:
            if "timed out" not in str(error):
                raise
        print("PASS: teammate intercepts mimic shot; wrong trajectory is rejected")
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
