#!/usr/bin/env python3
"""Synthesises AVAPrince's sound effects and tunes into ../src/AVAPrince/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, saw, sine and noise voices. The music
is new: an oud wandering the Hijaz mode over a drone and a drum. The cries are made by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPrince", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




HIJAZ = ["D", "Eb", "F#", "G", "A", "Bb", "C#"]


def hijaz(degree, octave=4):
    """A note of the Hijaz mode on D, by its degree (0 is D)."""
    o, d = divmod(degree, 7)
    name = HIJAZ[d]
    return name + str(octave + o)


def oud(note, seconds, gain=0.6):
    """A plucked string: a bright start, dying away, with a little shimmer."""
    f = hz(note)
    return mix(tone(seconds, f, kind="saw", attack=0.003, release=2.4, gain=gain * 0.6), tone(seconds, f * 2, kind="tri", attack=0.002, release=3.0, gain=gain * 0.3))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Jump.wav", tone(0.12, 300, 520, "tri", attack=0.002, release=0.6, gain=0.5), 0.1)
    write("Land.wav", mix(tone(0.08, 140, 70, "sine", attack=0.001, release=1.0), tone(0.05, 600, 200, "noise", release=1.0, gain=0.4)), 0.22)
    write("Bump.wav", tone(0.08, 110, 70, "square", attack=0.001, release=1.0, gain=0.5), 0.18)
    write("Grab.wav", tone(0.06, 400, 250, "noise", attack=0.001, release=1.0, gain=0.5), 0.14)
    write("Climb.wav", cat(tone(0.08, 300, 240, "noise", release=1.0, gain=0.4), silence(0.1), tone(0.08, 320, 260, "noise", release=1.0, gain=0.4)), 0.12)
    write("Shake.wav", cat(*[tone(0.04, 260 + (i % 2) * 60, kind="square", release=0.6, gain=0.4) for i in range(8)]), 0.16)
    write("Crash.wav", mix(tone(0.5, 180, 50, "noise", attack=0.001, release=1.0), tone(0.4, 90, 40, "sine", release=1.0)), 0.36)
    write("Gate.wav", cat(*[tone(0.05, 180 + (i % 3) * 40, kind="square", release=0.5, gain=0.4) for i in range(10)]), 0.18)
    write("ExitOpen.wav", mix(tone(1.2, 70, 50, "saw", attack=0.05, release=0.4, vibrato=0.08, vib_rate=14), tone(1.2, 300, 120, "noise", attack=0.05, release=0.6, gain=0.4)), 0.3)
    write("Enter.wav", cat(*[oud(hijaz(d, 4), 0.14, 0.7) for d in [0, 2, 4, 7]], oud(hijaz(9, 4), 0.6, 0.8)), 0.4)
    write("Drink.wav", cat(*[tone(0.07, 300 - k * 25, 200, "sine", attack=0.01, release=0.8) for k in range(4)]), 0.24)
    write("Sword.wav", tone(0.35, 2600, 3400, "saw", attack=0.002, release=0.8, gain=0.4), 0.18)
    write("Swing.wav", tone(0.16, 1800, 600, "noise", attack=0.01, release=0.7, gain=0.6), 0.16)
    write("Clash.wav", mix(tone(0.25, 2900, kind="sine", attack=0.001, release=2.0), tone(0.25, 4100, kind="sine", attack=0.001, release=2.5, gain=0.6), tone(0.05, 3000, 1000, "noise", gain=0.6)), 0.26)
    write("Hit.wav", mix(tone(0.12, 220, 110, "square", attack=0.001, release=1.0), tone(0.08, 900, 300, "noise", gain=0.5)), 0.26)
    write("Hurt.wav", mix(say("AH1 !", rate=RATE, pitch=120, speed=1.3, size=1.0), tone(0.08, 200, 120, "noise", gain=0.4)), 0.4)
    write("GuardDies.wav", cat(say("OW1 !", rate=RATE, pitch=95, speed=0.9, size=1.1), tone(0.3, 120, 60, "sine", release=1.0)), 0.4)
    write("Die.wav", cat(say("AA1 AA1 !", rate=RATE, pitch=150, speed=0.8, size=1.0, bend=lambda t: 1.0 - 0.4 * t), tone(0.3, 90, 40, "sine", release=1.0)), 0.42)
    write("Chop.wav", mix(tone(0.12, 1800, 900, "noise", attack=0.001, release=1.0), tone(0.1, 160, 80, "square", release=1.0, gain=0.5)), 0.24)

    # The dungeons and the palace: a drone, and an oud wandering the Hijaz mode (an original composition).
    beat = 60 / 84
    phrase = [0, 1, 2, 3, 4, 3, 2, 1, 2, 3, 1, 0, -1, 0, 2, 1, 4, 5, 4, 3, 2, 3, 1, 0]
    lengths = [1, 0.5, 0.5, 1, 1.5, 0.5, 0.5, 0.5, 1, 1, 1, 2, 0.5, 0.5, 1, 1, 1, 0.5, 0.5, 1, 1, 0.5, 0.5, 2]
    mel = []
    for d, b in zip(phrase, lengths):
        mel += oud(hijaz(d + 7, 3), b * beat, 0.6)
    drone = tone(len(mel) / RATE, hz("D2"), kind="saw", attack=0.5, release=0.05, vibrato=0.003, vib_rate=3, gain=0.18)
    drum = []
    while len(drum) < len(mel):
        drum += mix(tone(0.25, 120, 60, "sine", attack=0.002, release=1.4, gain=0.5), tone(0.03, 1500, 600, "noise", gain=0.2)) + silence(beat - 0.25)
        drum += tone(0.1, 300, 200, "sine", attack=0.002, release=1.5, gain=0.25) + silence(beat - 0.1)
    write("Music.wav", mix(mel, drone, drum[:len(mel)]), 0.3)
    title = [7, 9, 10, 11, 10, 9, 8, 7, 6, 7, 9, 8, 7, 4, 5, 6, 7]
    tl = [0.5, 0.5, 1, 1, 0.5, 0.5, 1, 1, 0.5, 0.5, 1, 1, 2, 0.5, 0.5, 1, 3]
    tm = []
    for d, b in zip(title, tl):
        tm += oud(hijaz(d, 3), b * 60 / 100, 0.7)
    write("TitleTune.wav", cat(mix(tm, tone(len(tm) / RATE, hz("D2"), kind="saw", attack=0.4, release=0.1, gain=0.2)), silence(1.0)), 0.34)
    win = []
    for d, b in zip([7, 9, 11, 14, 13, 14], [0.5, 0.5, 0.5, 1.5, 0.5, 3]):
        win += oud(hijaz(d, 3), b * 60 / 120, 0.8)
    write("Victory.wav", win, 0.42)
    over = []
    for d, b in zip([7, 6, 5, 4, 3, 1, 0], [1, 1, 1, 1, 1, 1, 3]):
        over += oud(hijaz(d, 3), b * 60 / 70, 0.7)
    write("GameOver.wav", over, 0.42)


if __name__ == "__main__":
    main()
