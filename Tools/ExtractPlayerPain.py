"""Cut two short human grunts from the CC0 source preview into Unity WAV assets.

Requires ``pip install miniaudio``. See CombatAudioSources.md for provenance.
"""

from pathlib import Path
import math
import struct
import wave

import miniaudio


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Tools/SourceAudio/Grunts_aphexx_347337_hq.mp3"
OUTPUT = ROOT / "Assets/Resources/Audio/Combat/Pain"
RATE = 48000
SEGMENTS = ((0.64, 0.94), (2.22, 2.52))


def extract(samples, start, end):
    selected = samples[round(start * RATE):round(end * RATE)]
    # Remove recording rumble while preserving the low, constricted voice.
    rc = 1 / (2 * math.pi * 80)
    alpha = rc / (rc + 1 / RATE)
    previous_input = 0.0
    previous_output = 0.0
    filtered = []
    for value in selected:
        current = alpha * (previous_output + value - previous_input)
        previous_input = value
        previous_output = current
        filtered.append(current)

    peak = max(abs(value) for value in filtered)
    fade_in = round(0.008 * RATE)
    fade_out = round(0.04 * RATE)
    pcm = bytearray()
    for index, value in enumerate(filtered):
        envelope = min(1.0, index / fade_in, (len(filtered) - index - 1) / fade_out)
        sample = max(-1.0, min(1.0, value * 0.82 / peak * envelope))
        pcm.extend(struct.pack("<h", round(sample * 32767)))
    return pcm


def main():
    decoded = miniaudio.decode_file(
        str(SOURCE), output_format=miniaudio.SampleFormat.FLOAT32,
        nchannels=1, sample_rate=RATE,
    )
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for index, (start, end) in enumerate(SEGMENTS, 1):
        path = OUTPUT / f"PlayerPain_{index:02}.wav"
        with wave.open(str(path), "wb") as result:
            result.setnchannels(1)
            result.setsampwidth(2)
            result.setframerate(RATE)
            result.writeframes(extract(decoded.samples, start, end))
        print(path)


if __name__ == "__main__":
    main()
