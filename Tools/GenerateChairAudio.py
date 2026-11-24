"""Generate deterministic chair pull/push scrapes for the laboratory floor."""

from pathlib import Path
import math
import random
import struct
import wave


RATE = 48000
OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/Audio/Props"


def make_chair_sound(pulling):
    duration = 0.39 if pulling else 0.35
    scrape_end = 0.30 if pulling else 0.26
    impact_time = 0.31 if pulling else 0.27
    rng = random.Random(9301 if pulling else 9302)
    low_noise = 0.0
    samples = []
    for index in range(round(RATE * duration)):
        t = index / RATE
        noise = rng.random() * 2 - 1
        low_noise = low_noise * 0.90 + noise * 0.10
        roughness = low_noise * 0.64 + noise * 0.19
        progress = min(1.0, t / scrape_end)
        scrape = roughness * math.sin(math.pi * progress) ** 0.7 if t < scrape_end else 0.0
        scrape *= 0.63 if pulling else 0.5
        frame_rumble = 0.16 * math.sin(2 * math.pi * (112 * t - 68 * t * t))
        frame_rumble *= math.sin(math.pi * progress) if t < scrape_end else 0.0
        impact_age = t - impact_time
        impact = 0.0
        if impact_age >= 0:
            impact = (0.48 * math.sin(2 * math.pi * 92 * impact_age)
                      + 0.25 * low_noise + 0.12 * noise) * math.exp(-48 * impact_age)
        samples.append((scrape + frame_rumble + impact)
                       * min(1.0, t / 0.003) * min(1.0, (duration - t) / 0.01))
    return samples


def write(name, samples):
    peak = max(abs(value) for value in samples)
    data = b"".join(
        struct.pack("<h", round(max(-1.0, min(1.0, value / peak * 0.82)) * 32767))
        for value in samples
    )
    with wave.open(str(OUTPUT / name), "wb") as result:
        result.setnchannels(1)
        result.setsampwidth(2)
        result.setframerate(RATE)
        result.writeframes(data)


if __name__ == "__main__":
    OUTPUT.mkdir(parents=True, exist_ok=True)
    write("ChairPull.wav", make_chair_sound(True))
    write("ChairPush.wav", make_chair_sound(False))
    print(f"Created chair movement sounds in {OUTPUT}")
