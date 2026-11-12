"""Exercise defeat snapshots with a separately built, short-duration server."""
import pathlib
import shutil
import socket
import subprocess
import tempfile
import time

from test_reconnect import Peer, peers, pump, restore, resume, upload


def start_match():
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
        upload(peer, index, seed)
    for peer in group:
        peer.wait("S_NAV_READY")
    return group, tokens


def main():
    source = pathlib.Path(__file__).parent / "GameServer"
    msbuild = pathlib.Path(r"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe")
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 7777))
    with tempfile.TemporaryDirectory(dir=pathlib.Path(__file__).parent) as temp:
        directory = pathlib.Path(temp)
        for path in source.glob("*.h"):
            shutil.copy2(path, directory / path.name)
        shutil.copy2(source / "GameServer.vcxproj", directory)
        code = (source / "GameServer.cpp").read_text(encoding="utf-8-sig")
        original = "static constexpr double MatchDurationSeconds = 10.0 * 60.0;"
        assert code.count(original) == 1
        (directory / "GameServer.cpp").write_text(code.replace(original, "static constexpr double MatchDurationSeconds = 8.0;"), encoding="utf-8")
        subprocess.run([str(msbuild), str(directory / "GameServer.vcxproj"), "/p:Configuration=Release", "/p:Platform=x64", "/p:PlatformToolset=v145", "/v:minimal"], check=True)
        with tempfile.TemporaryFile() as log:
            server = subprocess.Popen([str(directory / "x64/Release/GameServer.exe")], stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                time.sleep(0.5)
                group, tokens = start_match()
                for index, peer in enumerate(group, 1):
                    peer.send(f"MOVE|{index}|{index * 3}|0|1|0|0|0|0|1|0")
                pump(0.2)
                for target in (2, 1):
                    for shot in range(1, 5):
                        shot_id = target * 10 + shot
                        group[0].send(f"C_MIMIC_FIRE|1|1|{shot_id}|{target}|{target * 3}|1|1|1|0|0|10|30")
                        pump(0.05)
                        group[target - 1].send(f"C_MIMIC_HIT|{target}|{shot_id}")
                        group[0].wait(f"S_PLAYER_HP|{target}|{max(0, 100 - shot * 30)}")
                group[0].wait("S_GAME_OVER|ALL_DOWN")
                group[1].close()
                pump(9)
                recovered = resume(tokens[1])
                restore(recovered)
                recovered.wait("S_GAME_OVER|ALL_DOWN")
                print("PASS all-down reason remains after time limit", flush=True)
                for peer in (group[0], recovered):
                    peer.close()
                pump(61)
                group, tokens = start_match()
                group[0].wait("S_GAME_OVER|TIME", timeout=12)
                group[1].close()
                group[0].wait("S_CONNECTION|2|0")
                recovered = resume(tokens[1])
                restore(recovered)
                recovered.wait("S_GAME_OVER|TIME")
                print("PASS timeout reason survives reconnect", flush=True)
            finally:
                for peer in peers:
                    peer.close()
                server.terminate()
                server.wait(timeout=5)


if __name__ == "__main__":
    main()
