"""Generate a single sci-fi activation sweep with a light glass finish."""

from pathlib import Path
import math
import random
import struct
import wave


RATE = 48000
DURATION = 0.32
OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/Audio/UI/ItemUse.wav"


def make_samples():
    rng = random.Random(6409)
    low_noise = 0.0
    samples = []
    for index in range(round(RATE * DURATION)):
        t = index / RATE
        noise = rng.random() * 2 - 1
        low_noise = low_noise * 0.85 + noise * 0.15
        high_noise = noise - low_noise

        # One continuous rising movement instead of separate popping notes.
        sweep_progress = min(1.0, t / 0.19)
        sweep_shape = math.sin(math.pi * sweep_progress) ** 2
        sweep = (0.26 * low_noise + 0.10 * high_noise) * sweep_shape
        chirp_phase = 2 * math.pi * (390 * t + 2350 * t * t)
        chirp = 0.16 * math.sin(chirp_phase) * sweep_shape

        # A small glassy finish marks the moment the item activates.
        bell_age = t - 0.12
        bell = 0.0
        if bell_age >= 0:
            bell_envelope = min(1.0, bell_age / 0.003) * math.exp(-18 * bell_age)
            bell = (math.sin(2 * math.pi * 1240 * bell_age) * 0.33
                    + math.sin(2 * math.pi * 2080 * bell_age) * 0.11) * bell_envelope
        samples.append((sweep + chirp + bell) * min(1.0, t / 0.002)
                       * min(1.0, (DURATION - t) / 0.012))
    return samples


def main():
    samples = make_samples()
    peak = max(abs(value) for value in samples)
    data = b"".join(
        struct.pack("<h", round(max(-1.0, min(1.0, value / peak * 0.8)) * 32767))
        for value in samples
    )
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUTPUT), "wb") as result:
        result.setnchannels(1)
        result.setsampwidth(2)
        result.setframerate(RATE)
        result.writeframes(data)
    print(OUTPUT)


if __name__ == "__main__":
    main()
