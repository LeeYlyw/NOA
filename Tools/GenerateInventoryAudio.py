"""Generate two short, original UI cues for the Tab inventory panel."""

from pathlib import Path
import math
import random
import struct
import wave


RATE = 48000
OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/Audio/UI"


def cue(opening):
    duration = 0.075 if opening else 0.11
    rng = random.Random(881 if opening else 973)
    noise_low = 0.0
    samples = []
    for index in range(round(RATE * duration)):
        t = index / RATE
        noise = rng.random() * 2 - 1
        noise_low = noise_low * 0.86 + noise * 0.14
        noise_high = noise - noise_low
        if opening:
            # A single short "chap" snap, without the previous drawn-out swipe.
            swipe_time = t - 0.004
            swipe = 0.0
            if swipe_time > 0:
                swipe_progress = min(1.0, swipe_time / 0.055)
                swipe = (0.31 * noise_low + 0.17 * noise_high) * math.sin(math.pi * swipe_progress) ** 2
            latch = (0.34 * noise_high + 0.27 * noise_low) * math.exp(-170 * t)
            value = latch + swipe
        else:
            # Dry "tuk": one muted impact with no chime or trailing pitch.
            thud = 0.45 * math.sin(2 * math.pi * (145 * t - 420 * t * t)) * math.exp(-65 * t)
            tap = (0.22 * noise_high + 0.38 * noise_low) * math.exp(-75 * t)
            value = thud + tap
        samples.append(value * min(1.0, t / 0.0015) * min(1.0, (duration - t) / 0.008))
    return samples


def write(name, samples):
    peak = max(abs(value) for value in samples)
    data = b"".join(
        struct.pack("<h", round(max(-1.0, min(1.0, value / peak * 0.78)) * 32767))
        for value in samples
    )
    with wave.open(str(OUTPUT / name), "wb") as result:
        result.setnchannels(1)
        result.setsampwidth(2)
        result.setframerate(RATE)
        result.writeframes(data)


if __name__ == "__main__":
    OUTPUT.mkdir(parents=True, exist_ok=True)
    write("InventoryOpen.wav", cue(True))
    write("InventoryClose.wav", cue(False))
    print(f"Created inventory UI cues in {OUTPUT}")
