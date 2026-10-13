#!/usr/bin/env python3
"""Loudness-normalises Dr Birkin's voice lines.

Reads the untouched recordings from VoiceOriginals/Birkin/ and writes normalised copies, with the same names, to
Assets/_Audio/Voice/Birkin/ (mono, 44.1 kHz, mp3). Each clip is brought to TARGET_LUFS integrated loudness by one
fixed gain, and a limiter holds the few peaks that would go over PEAK_DB. A fixed gain keeps the natural dynamics of
the performance (no pumping in the pauses); the limiter only touches the loudest syllables.

    python3 VoiceOriginals/normalise_voice.py            normalise every clip and print the before/after table
    python3 VoiceOriginals/normalise_voice.py --measure  only measure what is in both folders

Needs ffmpeg on the PATH and numpy.
"""
import json, os, subprocess, sys
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "VoiceOriginals", "Birkin")
DST = os.path.join(ROOT, "Assets", "_Audio", "Voice", "Birkin")
TARGET_LUFS = -16.0
PEAK_DB = -1.5          # the limiter's ceiling; the mp3 encoder can overshoot it a little, so the files land under -1
MAX_PEAK_DB = -1.0     # no sample of a finished file may be above this
RATE = 44100
NOISE_FLAG_DB = -50.0


def decode(path):
    """The file as mono float samples at 44.1 kHz."""
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(RATE), "-f", "f32le", "-"],
                         check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.float32)


def db(x):
    return 20.0 * np.log10(max(float(x), 1e-9))


def lufs(path):
    """Integrated loudness (EBU R128), from ffmpeg's loudnorm analysis."""
    err = subprocess.run(["ffmpeg", "-hide_banner", "-i", path, "-af", "loudnorm=print_format=json", "-f", "null", "-"],
                         capture_output=True, text=True).stderr
    return float(json.loads(err[err.rindex("{"):err.rindex("}") + 1])["input_i"])


def measure(path):
    x = decode(path)
    window = int(RATE * 0.05)
    frames = x[: len(x) // window * window].reshape(-1, window)
    rms = np.sqrt((frames.astype(np.float64) ** 2).mean(axis=1))
    peak = np.abs(x).max()
    quiet = peak * 0.06
    loud = np.nonzero(np.abs(x) >= quiet)[0]
    return {
        "length": len(x) / RATE,
        "peak": db(peak),
        "lufs": lufs(path),
        # the silent parts: the quietest tenth of the 50 ms windows (the gaps between words and at both ends)
        "noise": db(np.percentile(rms, 10)),
        "lead": loud[0] / RATE,
        "spoken": loud[-1] / RATE,
    }


def encode(src, dst, gain, ceiling):
    # one fixed gain, then a look-ahead limiter for the peaks it pushes over the ceiling
    chain = "volume=%.2fdB,alimiter=limit=%.4f:attack=5:release=60:level=false" % (gain, 10 ** (ceiling / 20.0))
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", src, "-af", chain, "-ac", "1", "-ar", str(RATE),
                    "-c:a", "libmp3lame", "-q:a", "2", dst], check=True)


def normalise(src, dst):
    before = measure(src)
    gain = TARGET_LUFS - before["lufs"]
    ceiling = PEAK_DB
    tmp = dst + ".tmp.mp3"
    for _ in range(6):
        encode(src, tmp, gain, ceiling)
        miss = TARGET_LUFS - lufs(tmp)
        over = db(np.abs(decode(tmp)).max()) - MAX_PEAK_DB
        if over > 0.0:
            ceiling -= over + 0.2  # the mp3 encoder overshot the ceiling: lower it by that much and go again
        elif abs(miss) >= 0.3:
            gain += miss           # the limiter took a little loudness off: make it up and go again
        else:
            break
    os.replace(tmp, dst)
    return before, measure(dst), gain


def main():
    only_measure = "--measure" in sys.argv
    names = sorted(n for n in os.listdir(SRC) if n.lower().endswith(".mp3"))
    print("%-18s %7s %7s %7s | %7s %7s %7s | %6s %5s %6s" % ("clip", "peak", "LUFS", "noise", "peak", "LUFS", "noise", "gain", "lead", "len"))
    for name in names:
        src, dst = os.path.join(SRC, name), os.path.join(DST, name)
        if only_measure:
            before, after, gain = measure(src), measure(dst), float("nan")
        else:
            before, after, gain = normalise(src, dst)
        flag = "  <-- noise above %.0f dBFS" % NOISE_FLAG_DB if after["noise"] > NOISE_FLAG_DB else ""
        print("%-18s %7.1f %7.1f %7.1f | %7.1f %7.1f %7.1f | %+6.1f %5.2f %6.2f%s" % (
            name, before["peak"], before["lufs"], before["noise"], after["peak"], after["lufs"], after["noise"],
            gain, after["lead"], after["length"], flag))


if __name__ == "__main__":
    main()
