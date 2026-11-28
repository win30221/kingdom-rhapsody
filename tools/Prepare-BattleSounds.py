#!/usr/bin/env python3
"""Generate the small mono PCM cues used by the battle presentation."""
import hashlib
import math
import random
import struct
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIRECTORY = ROOT / "unity/Assets/Battle/Resources/Battle/Sounds"
TEMPLATE = ROOT / "unity/Assets/Lobby/Resources/Lobby/Sounds/deploy.wav.meta"
RATE = 22050


def pcm(seconds, partials=(), texture=0, seed=0):
    count = round(RATE * seconds)
    rng = random.Random(seed)
    filtered = 0
    audio = bytearray()
    for i in range(count):
        t = i / RATE
        filtered += .22 * (rng.uniform(-1, 1) - filtered)
        value = texture * filtered * math.exp(-t * 22)
        for start, duration, frequency, sweep, gain in partials:
            elapsed = t - start
            if 0 <= elapsed < duration:
                shape = min(1, elapsed / .008) * math.exp(-4 * elapsed / duration)
                phase = math.tau * (frequency * elapsed + sweep * elapsed ** 2 / (2 * duration))
                value += gain * shape * math.sin(phase)
        envelope = min(1, max(0, (seconds - t) / .009), t / .006)
        audio.extend(struct.pack("<h", round(32767 * max(-1, min(1, envelope * value)))))
    return bytes(audio)


def melody(notes):
    return [(i * .115, .24, note, 0, .35) for i, note in enumerate(notes)]


def main():
    DIRECTORY.mkdir(parents=True, exist_ok=True)
    source_meta = TEMPLATE.read_text(encoding="utf-8")
    cues = {
        "shoot": pcm(.17, [(0, .14, 820, -560, .16)], .32, 1),
        "hit": pcm(.20, [(0, .16, 255, -140, .31), (.005, .09, 1200, -450, .13)], .52, 2),
        "spell": pcm(.36, [(0, .30, 131, -70, .38), (.06, .22, 660, -420, .16)], .43, 3),
        "shield-break": pcm(.32, [(0, .26, 1480, -690, .28), (.024, .18, 760, -230, .16)], .40, 4),
        "support": pcm(.30, melody((523.25, 783.99))),
        "defeated": pcm(.26, [(0, .24, 140, -65, .29)], .29, 5),
        "victory": pcm(.62, melody((392, 493.88, 587.33, 783.99))),
        "defeat": pcm(.62, melody((523.25, 440, 349.23, 293.66))),
        "draw": pcm(.43, melody((392, 523.25, 392))),
    }
    for name, data in cues.items():
        path = DIRECTORY / f"{name}.wav"
        with wave.open(str(path), "wb") as clip:
            clip.setnchannels(1)
            clip.setsampwidth(2)
            clip.setframerate(RATE)
            clip.writeframes(data)
        version = hashlib.md5(("kingdom-rhapsody/battle/audio/" + name).encode()).hexdigest()
        first, _, body = source_meta.split("\n", 2)
        path.with_suffix(".wav.meta").write_text(f"{first}\nguid: {version}\n{body}", encoding="utf-8")


if __name__ == "__main__":
    main()
