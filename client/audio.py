"""Micro, lecture audio et effet radio."""
import numpy as np
import sounddevice as sd

STT_RATE = 16000  # Whisper attend du 16 kHz mono


class Recorder:
    def __init__(self, device=None):
        self.device = device if device not in ("", None) else None
        self._frames = []
        self._stream = None

    def start(self):
        self._frames = []
        self._stream = sd.InputStream(
            samplerate=STT_RATE, channels=1, dtype="float32",
            device=self.device, callback=self._callback,
        )
        self._stream.start()

    def _callback(self, indata, frames, time_info, status):
        self._frames.append(indata.copy())

    def stop(self) -> np.ndarray:
        if self._stream is not None:
            self._stream.stop()
            self._stream.close()
            self._stream = None
        if not self._frames:
            return np.zeros(0, dtype=np.float32)
        return np.concatenate(self._frames)[:, 0]


def play(samples: np.ndarray, rate: int, wait=True):
    sd.play(samples, rate)
    if wait:
        sd.wait()


def stop():
    sd.stop()


def beep(freq=1200.0, duration=0.06, rate=24000, volume=0.15):
    """Petit 'clic' radio quand on appuie / relâche le bouton."""
    t = np.arange(int(rate * duration)) / rate
    tone = np.sin(2 * np.pi * freq * t) * np.hanning(len(t)) * volume
    sd.play(tone.astype(np.float32), rate)


# Niveau d'une F1 qui passe à 12 m du micro, par rapport à la voix (crête à 1)
TRACK_LEVEL = 0.07


def radio_fx(x: np.ndarray, rate: int, track=False) -> np.ndarray:
    """Bande passante radio (300-3400 Hz), saturation légère, souffle et squelch.
    track : des voitures passent derrière l'ingé, captées par son micro avant le filtre radio."""
    x = x.astype(np.float32)
    x = x / (np.max(np.abs(x)) + 1e-9)
    if track:
        import ambience
        pad = np.zeros(int(rate * 0.12))  # le micro s'ouvre un instant avant que l'ingé parle
        x = np.concatenate([pad, x, pad])
        x = x + TRACK_LEVEL * ambience.track(len(x), rate) * ambience.duck(x, rate)
    n = len(x)
    spectrum = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(n, 1 / rate)
    # fenêtre douce plutôt qu'un coupe-net, pour éviter le son "métallique"
    low = 1 / (1 + (300 / np.maximum(freqs, 1)) ** 4)
    high = 1 / (1 + (freqs / 3400) ** 4)
    y = np.fft.irfft(spectrum * low * high, n)

    y = y / (np.max(np.abs(y)) + 1e-9)
    y = np.tanh(y * 2.2) / np.tanh(2.2)
    y += np.random.normal(0, 0.006, n)

    squelch_len = int(rate * 0.05)
    squelch = np.random.normal(0, 0.08, squelch_len) * np.linspace(1, 0, squelch_len)
    gap = np.zeros(0 if track else int(rate * 0.03))
    out = np.concatenate([squelch, gap, y, gap, squelch[::-1]])
    return (out * 0.8).astype(np.float32)
