"""Check the three server-owned cooperative puzzle rounds with two TCP clients."""

import socket
import subprocess
import time

from ServerNavigationSmoke import SERVER, make_mask, read_until, send


def details(code):
    return code // 12, (code // 3) % 4, code % 3


def main():
    process = subprocess.Popen(
        [str(SERVER)], cwd=str(SERVER.parent), stdout=subprocess.DEVNULL,
        stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW,
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
        start, _ = read_until(sockets, lambda line: line.startswith("START|"))
        header = f"{start.split('|')[1]}|5|5|0|0|2|4|4|1|0|0|4|0|0|4"
        for player_id, sock in enumerate(sockets, start=1):
            send(sock, f"C_NAV_BEGIN|{player_id}|{header}")
            send(sock, f"C_NAV_PAD|{player_id}|1|1|0|1")
            send(sock, f"C_NAV_PAD|{player_id}|2|3|0|1")
            for item_id in range(1, 4):
                send(sock, f"C_NAV_ITEM|{player_id}|{item_id}|Clue|1|0.85|1")
            send(sock, f"C_NAV_CHUNK|{player_id}|0|{make_mask()}")
            send(sock, f"C_NAV_END|{player_id}")
        read_until(sockets, lambda line: line == "S_NAV_READY")
        send(sockets[0], "MOVE|1|1|0|1|0|0|0|0|1|0")
        send(sockets[1], "MOVE|2|3|0|1|0|0|0|0|1|0")
        time.sleep(0.1)
        send(sockets[0], "C_PUZZLE_PAD|1|1")
        send(sockets[1], "C_PUZZLE_PAD|2|1")

        for round_number in range(1, 4):
            clue, _ = read_until([sockets[0]], lambda line: line.startswith(f"S_PUZZLE_CLUE|{round_number}|"))
            options, _ = read_until([sockets[1]], lambda line: line.startswith(f"S_PUZZLE_CHOICES|{round_number}|"))
            answer = int(clue.split("|")[2])
            choices = [int(value) for value in options.split("|")[2:]]
            assert len(choices) == len(set(choices)) == 4 and answer in choices
            shape, color, mark = details(answer)
            if round_number == 1:
                assert {details(code)[0] for code in choices} == set(range(4))
                assert all(details(code)[1:] == (color, mark) for code in choices)
            elif round_number == 2:
                assert all(details(code)[2] == mark for code in choices)
                assert {details(code)[:2] for code in choices} == {
                    (shape, color), ((shape + 1) % 4, color),
                    (shape, (color + 1) % 4), ((shape + 1) % 4, (color + 1) % 4),
                }
            else:
                assert all(sum(a != b for a, b in zip(details(code), details(answer))) == 1
                           for code in choices if code != answer)
            send(sockets[1], f"C_PUZZLE_CHOOSE|2|{choices.index(answer)}")
        read_until(sockets, lambda line: line == "S_PUZZLE_SOLVED")
        print("PASS: three distinct cooperative puzzle rounds solve on two client sockets")
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
