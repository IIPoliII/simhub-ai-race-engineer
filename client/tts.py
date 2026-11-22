"""Synthèse vocale : Kokoro (local, gratuit) ou ElevenLabs (cloud)."""
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


def _download(url: str, dest: Path):
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    print(f"Téléchargement de {dest.name}...")
    with httpx.stream("GET", url, follow_redirects=True, timeout=60) as r:
        r.raise_for_status()
        total = int(r.headers.get("content-length", 0))
        done = 0
        with open(tmp, "wb") as f:
            for chunk in r.iter_bytes(1 << 20):
                f.write(chunk)
                done += len(chunk)
                if total:
                    print(f"\r  {done * 100 // total:3d} %", end="", flush=True)
    print()
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
            raise SystemExit("ElevenLabs : renseigne [elevenlabs] api_key dans config.toml")
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


class Voice:
    def __init__(self, tts_cfg: dict, elevenlabs_cfg: dict):
        engine = tts_cfg.get("engine", "kokoro")
        self.engine = ElevenLabsVoice(elevenlabs_cfg) if engine == "elevenlabs" else KokoroVoice(tts_cfg)
        self.radio = bool(tts_cfg.get("radio_effect", True))

    def render(self, text: str) -> tuple[np.ndarray, int]:
        samples, rate = self.engine.synth(text)
        if self.radio:
            samples = audio.radio_fx(samples, rate)
        return samples, rate

    def say(self, text: str):
        samples, rate = self.render(text)
        audio.play(samples, rate)
