"""Real TCP regression for timed monster waves (run with a test server)."""
import pathlib
import shutil
import socket
import subprocess
import tempfile
import time

from test_reconnect import Peer, peers, pump, restore, resume


def run():
    group = [Peer(), Peer()]
    tokens = []
    for i, peer in enumerate(group):
        peer.wait("LOBBY")
        peer.send(f"C_ROLE|{i}")
        peer.send("C_READY|1")
        peer.heartbeat = True
    pump(0.2)
    group[0].send("C_START")
    for peer in group:
        tokens.append(peer.wait("S_SESSION").split("|")[1])
        seed = peer.wait("START").split("|")[1]
    width = 40
    edges = [0] * (width * width)
    for z in range(width):
        for x in range(width):
            cell = z * width + x
            if x + 1 < width:
                edges[cell] |= 1
                edges[cell + 1] |= 2
            if z + 1 < width:
                edges[cell] |= 4
                edges[cell + width] |= 8
    mask = "".join(format(edge, "X") for edge in edges)
    monsters = "|".join(f"{4 + 4 * i}|0|4" for i in range(6))
    for i, peer in enumerate(group, 1):
        peer.send(f"C_NAV_BEGIN|{i}|{seed}|40|40|0|0|2|4|4|1|{monsters}")
        for clue in range(1, 4):
            peer.send(f"C_NAV_ITEM|{i}|{clue}|Clue|1|0.85|1")
        for offset in range(0, len(mask), 1000):
            peer.send(f"C_NAV_CHUNK|{i}|{offset // 1000}|{mask[offset:offset + 1000]}")
        peer.send(f"C_NAV_END|{i}")
    for peer in group:
        peer.wait("S_NAV_READY")
    for i, peer in enumerate(group, 1):
        peer.send(f"MOVE|{i}|{i}|0|1|0|0|0|0|1|0")
    for peer in group:
        for monster in range(7, 13):
            state = peer.wait(f"S_MONSTER_STATE|{monster}", timeout=10).split("|")
            assert state[2] == ("Sound" if monster % 3 == 2 else "Vision"), state
            assert (float(state[4]) ** 2 + float(state[6]) ** 2) ** 0.5 >= 25, state
    group[1].close()
    group[0].wait("S_CONNECTION|2|0")
    recovered = resume(tokens[1])
    restore(recovered)
    for monster in range(7, 13):
        recovered.wait(f"S_MONSTER_STATE|{monster}")
    print("PASS two waves, safe distance, types and reconnect snapshot", flush=True)


if __name__ == "__main__":
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 7777))
    source = pathlib.Path(__file__).parent / "GameServer"
    msbuild = pathlib.Path(r"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe")
    with tempfile.TemporaryDirectory(dir=source.parent) as temp:
        directory = pathlib.Path(temp)
        for path in source.glob("*.h"):
            shutil.copy2(path, directory / path.name)
        shutil.copy2(source / "GameServer.vcxproj", directory)
        code = (source / "GameServer.cpp").read_text(encoding="utf-8-sig")
        original = "(activeMonsterCount < 9 ? 180.0 : 360.0)"
        assert code.count(original) == 1
        (directory / "GameServer.cpp").write_text(code.replace(original, "(activeMonsterCount < 9 ? 2.0 : 4.0)"), encoding="utf-8-sig")
        subprocess.run([str(msbuild), str(directory / "GameServer.vcxproj"), "/p:Configuration=Release",
                        "/p:Platform=x64", "/p:PlatformToolset=v145", "/v:minimal"], check=True)
        with tempfile.TemporaryFile() as log:
            server = subprocess.Popen([str(directory / "x64/Release/GameServer.exe")], stdout=log, stderr=log,
                                      creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                time.sleep(0.5)
                run()
            finally:
                for peer in peers:
                    peer.close()
                server.terminate()
                server.wait(timeout=5)
