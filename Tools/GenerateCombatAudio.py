"""Generate deterministic, original combat cues as mono 48 kHz PCM WAV files."""

from pathlib import Path
import math
import random
import struct
import wave


RATE = 48000
OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/Audio/Combat"


def write_wave(name, samples, directory=OUTPUT):
    peak = max(abs(value) for value in samples)
    pcm = b"".join(
        struct.pack("<h", round(max(-1.0, min(1.0, value * 0.84 / peak)) * 32767))
        for value in samples
    )
    with wave.open(str(directory / name), "wb") as output:
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


def player_pain(variant):
    """A breathy, pitched nonverbal grunt with two vowel formants."""
    rng = random.Random(4700 + variant)
    duration = 0.43 if variant == 1 else 0.5
    start_pitch = 173 if variant == 1 else 155
    formant_one = 690 if variant == 1 else 580
    formant_two = 1230 if variant == 1 else 1040
    phase = 0.0
    breath = 0.0
    samples = []
    for index in range(round(RATE * duration)):
        t = index / RATE
        active = max(0, t - 0.045)
        progress = min(1, active / (duration - 0.045))
        pitch = start_pitch * (1 - 0.34 * progress) * (1 + 0.014 * math.sin(2 * math.pi * 8.5 * t))
        phase += 2 * math.pi * pitch / RATE
        voiced = 0.0
        for harmonic in range(1, 23):
            frequency = harmonic * pitch
            vowel = (1.35 * math.exp(-((frequency - formant_one) / 280) ** 2)
                     + 0.8 * math.exp(-((frequency - formant_two) / 360) ** 2)
                     + 0.2 * math.exp(-((frequency - 2450) / 520) ** 2))
            voiced += math.sin(phase * harmonic) * vowel / harmonic**0.72
        noise = rng.random() * 2 - 1
        breath = 0.81 * breath + 0.19 * noise
        envelope = min(1, active / 0.045) * (1 - progress) ** 1.6
        inhale = breath * 0.22 * math.exp(-((t - 0.035) / 0.027) ** 2)
        samples.append(math.tanh(voiced * 0.68) * envelope + inhale)
    return samples


if __name__ == "__main__":
    OUTPUT.mkdir(parents=True, exist_ok=True)
    write_wave("MonsterAttack.wav", monster_attack())
    write_wave("PlayerHit.wav", player_hit())
    pain_output = OUTPUT / "Pain"
    pain_output.mkdir(parents=True, exist_ok=True)
    write_wave("PlayerPain_01.wav", player_pain(1), pain_output)
    write_wave("PlayerPain_02.wav", player_pain(2), pain_output)
    print(f"Created combat audio in {OUTPUT}")
