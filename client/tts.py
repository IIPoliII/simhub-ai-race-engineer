"""Synthèse vocale : voix neuronales Microsoft (en ligne, gratuites), Kokoro (local) ou ElevenLabs (payant)."""
import asyncio
import io
import re
from pathlib import Path

import httpx
import numpy as np

import audio

MODELS_DIR = Path(__file__).parent / "models"
KOKORO_FILES = {
    "kokoro-v1.0.onnx": "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx",
    "voices-v1.0.bin": "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin",
}
# La première lettre du nom de voix Kokoro donne la langue
KOKORO_LANG = {"a": "en-us", "b": "en-gb", "f": "fr-fr", "e": "es", "i": "it", "p": "pt-br", "h": "hi", "j": "ja", "z": "cmn"}


# Langue des messages (celle des réglages, choisie par engineer.py)
lang = "fr"


def _t(fr: str, en: str) -> str:
    return fr if lang == "fr" else en


def progress(message: str, code: str = "loading"):
    """Remplacé par engineer.py pour afficher la progression dans la page du plugin SimHub.
    code : état indépendant de la langue (loading, warn), lu par la page."""
    print(message, flush=True)


def _download(url: str, dest: Path):
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    progress(_t(f"Téléchargement de la voix ({dest.name})...", f"Downloading the voice ({dest.name})..."))
    last_step = -1
    with httpx.stream("GET", url, follow_redirects=True, timeout=60) as r:
        r.raise_for_status()
        total = int(r.headers.get("content-length", 0))
        done = 0
        with open(tmp, "wb") as f:
            for chunk in r.iter_bytes(1 << 20):
                f.write(chunk)
                done += len(chunk)
                step = done * 10 // total if total else 0
                if step != last_step:
                    last_step = step
                    progress(_t(f"Téléchargement de la voix ({dest.name}) : {step * 10} %", f"Downloading the voice ({dest.name}): {step * 10}%"))
    tmp.replace(dest)


class KokoroVoice:
    def __init__(self, cfg: dict):
        for name, url in KOKORO_FILES.items():
            if not (MODELS_DIR / name).exists():
                _download(url, MODELS_DIR / name)
        from kokoro_onnx import Kokoro
        self.kokoro = Kokoro(str(MODELS_DIR / "kokoro-v1.0.onnx"), str(MODELS_DIR / "voices-v1.0.bin"))
        self.voice = cfg.get("kokoro_voice", "ff_siwis")
        self.speed = float(cfg.get("kokoro_speed", 1.1))
        self.lang = KOKORO_LANG.get(self.voice[0], "en-us")

    def synth(self, text: str) -> tuple[np.ndarray, int]:
        samples, rate = self.kokoro.create(text, voice=self.voice, speed=self.speed, lang=self.lang)
        return samples, rate


class ElevenLabsVoice:
    RATE = 24000

    def __init__(self, cfg: dict):
        if not cfg.get("api_key"):
            raise ValueError(_t("clé API ElevenLabs manquante dans la page du plugin", "ElevenLabs API key missing in the plugin page"))
        self.http = httpx.Client(
            base_url="https://api.elevenlabs.io",
            headers={"xi-api-key": cfg["api_key"]},
            timeout=15,
        )
        self.voice_id = cfg.get("voice_id", "JBFqnCBsd6RMkjVDRZzb")
        self.model = cfg.get("model", "eleven_flash_v2_5")

    def synth(self, text: str) -> tuple[np.ndarray, int]:
        # pcm_24000 = PCM 16 bits brut : pas besoin de décoder du MP3
        r = self.http.post(
            f"/v1/text-to-speech/{self.voice_id}",
            params={"output_format": f"pcm_{self.RATE}"},
            json={"text": text, "model_id": self.model},
        )
        r.raise_for_status()
        samples = np.frombuffer(r.content, dtype=np.int16).astype(np.float32) / 32768
        return samples, self.RATE


class NeuralVoice:
    """Voix neuronales Microsoft (celles de la lecture à voix haute d'Edge) : gratuites, très naturelles, en ligne."""

    def __init__(self, cfg: dict):
        import edge_tts  # noqa: F401  (vérifie la dépendance tout de suite)
        self.voice = cfg.get("neural_voice") or "fr-FR-HenriNeural"
        pct = round((float(cfg.get("speed", 1.1)) - 1) * 100)
        self.rate = f"{pct:+d}%"

    async def _mp3(self, text: str) -> bytes:
        import edge_tts
        buf = bytearray()
        async for chunk in edge_tts.Communicate(text, self.voice, rate=self.rate).stream():
            if chunk["type"] == "audio":
                buf += chunk["data"]
        return bytes(buf)

    def synth(self, text: str) -> tuple[np.ndarray, int]:
        import soundfile as sf
        data, rate = sf.read(io.BytesIO(asyncio.run(self._mp3(text))), dtype="float32")
        if data.ndim > 1:
            data = data.mean(axis=1)
        return data, rate


# Kokoro lit le français avec la phonétique française : on réécrit les mots d'anglais de course
# (vérifié en repassant la voix dans Whisper : « box box » était compris « inbox box free »)
FR_SPOKEN = [
    (re.compile(r"\bbox\b", re.I), "boxe"),
    (re.compile(r"\bpush\b", re.I), "pouche"),
]


def spoken_fr(text: str) -> str:
    for pattern, repl in FR_SPOKEN:
        text = pattern.sub(repl, text)
    return text


class Voice:
    def __init__(self, tts_cfg: dict, elevenlabs_cfg: dict):
        engine = tts_cfg.get("engine", "neural")
        self.radio = bool(tts_cfg.get("radio_effect", True))
        self.track = bool(tts_cfg.get("track_noise", True))
        self._tts_cfg = tts_cfg
        self._fallback = None
        if engine == "elevenlabs":
            self.engine = ElevenLabsVoice(elevenlabs_cfg)
        elif engine == "neural":
            self.engine = NeuralVoice({**tts_cfg, "speed": tts_cfg.get("kokoro_speed", 1.1)})
        else:
            self.engine = KokoroVoice(tts_cfg)

    def _kokoro(self) -> KokoroVoice:
        if self._fallback is None:
            self._fallback = KokoroVoice(self._tts_cfg)
        return self._fallback

    def _synth(self, text: str) -> tuple[np.ndarray, int]:
        if isinstance(self.engine, KokoroVoice):
            if self.engine.lang == "fr-fr":
                text = spoken_fr(text)
            return self.engine.synth(text)
        try:
            return self.engine.synth(text)
        except Exception as e:  # pas d'internet, service indisponible : la voix locale prend le relais
            if not isinstance(self.engine, NeuralVoice):
                raise
            progress(_t(f"Voix en ligne indisponible ({e}), voix locale utilisée.", f"Online voice unavailable ({e}), using the local voice."), "warn")
            k = self._kokoro()
            return k.synth(spoken_fr(text) if k.lang == "fr-fr" else text)

    def render(self, text: str) -> tuple[np.ndarray, int]:
        samples, rate = self._synth(text)
        if self.radio:
            samples = audio.radio_fx(samples, rate, track=self.track)
        return samples, rate

    def say(self, text: str):
        samples, rate = self.render(text)
        audio.play(samples, rate)
