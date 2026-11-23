"""Generate deterministic, original combat cues as mono 48 kHz PCM WAV files."""

from pathlib import Path
import math
import random
import struct
import wave


RATE = 48000
OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/Audio/Combat"


def write_wave(name, samples):
    peak = max(abs(value) for value in samples)
    pcm = b"".join(
        struct.pack("<h", round(max(-1.0, min(1.0, value * 0.84 / peak)) * 32767))
        for value in samples
    )
    with wave.open(str(OUTPUT / name), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(pcm)


def monster_attack():
    rng = random.Random(62134)
    duration = 0.64
    low_noise = 0.0
    samples = []
    for index in range(round(RATE * duration)):
        t = index / RATE
        noise = rng.random() * 2 - 1
        low_noise = low_noise * 0.91 + noise * 0.09
        # A rising scrape precedes a bass-heavy lunge and short impact.
        sweep = (noise * 0.17 + low_noise * 0.43) * math.sin(math.pi * min(t / 0.27, 1)) ** 2
        growl_pitch = 87 - 47 * min(t / duration, 1)
        growl = math.sin(2 * math.pi * growl_pitch * t + 0.7 * math.sin(2 * math.pi * 13 * t))
        growl *= 0.42 * min(1, t / 0.035) * math.exp(-2.8 * t)
        impact_time = t - 0.23
        impact = 0.0
        if impact_time >= 0:
            impact = (0.52 * math.sin(2 * math.pi * (77 * impact_time - 23 * impact_time**2))
                      + 0.24 * low_noise) * math.exp(-13 * impact_time)
        samples.append(math.tanh((sweep + growl + impact) * 1.5) * min(1, t / 0.004))
    return samples


def player_hit():
    rng = random.Random(9271)
    duration = 0.34
    low_noise = 0.0
    samples = []
    for index in range(round(RATE * duration)):
        t = index / RATE
        noise = rng.random() * 2 - 1
        low_noise = low_noise * 0.87 + noise * 0.13
        thud = 0.62 * math.sin(2 * math.pi * (116 * t - 125 * t * t)) * math.exp(-20 * t)
        crack = 0.31 * noise * math.exp(-72 * t)
        body = 0.36 * low_noise * math.exp(-13 * t)
        samples.append((thud + crack + body) * min(1, t / 0.002))
    return samples


if __name__ == "__main__":
    OUTPUT.mkdir(parents=True, exist_ok=True)
    write_wave("MonsterAttack.wav", monster_attack())
    write_wave("PlayerHit.wav", player_hit())
    print(f"Created combat audio in {OUTPUT}")
