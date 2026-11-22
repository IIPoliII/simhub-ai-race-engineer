"""Reconnaissance vocale locale avec faster-whisper."""
import os

os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")

import numpy as np  # noqa: E402
from faster_whisper import WhisperModel  # noqa: E402

# Vocabulaire de course pour aider Whisper sur les mots techniques
HINTS = {
    "fr": "Radio course automobile. Pneus, carburant, essence, écart, devant, derrière, tour, stint, box, arrêt aux stands, pression, température, freins, dégâts.",
    "en": "Race radio. Tyres, fuel, gap, ahead, behind, lap, stint, box, pit stop, pressures, temperatures, brakes, damage.",
}


class Transcriber:
    def __init__(self, cfg: dict, language: str):
        self.language = language
        # Pas plus de la moitié des cœurs (4 au maximum) : le reste est pour le jeu
        threads = max(1, min(4, (os.cpu_count() or 2) // 2))
        self.model = WhisperModel(
            cfg.get("model", "small"),
            device=cfg.get("device", "cpu"),
            compute_type=cfg.get("compute_type", "int8"),
            cpu_threads=threads,
        )
        # Première passe à vide : la vraie première question n'attend pas le chargement
        self.model.transcribe(np.zeros(16000, dtype=np.float32), language=language, vad_filter=False)

    def transcribe(self, audio: np.ndarray) -> str:
        segments, _ = self.model.transcribe(
            audio,
            language=self.language,
            beam_size=1,
            vad_filter=True,                  # coupe les silences -> moins d'hallucinations
            condition_on_previous_text=False,
            initial_prompt=HINTS.get(self.language),
        )
        return " ".join(s.text.strip() for s in segments).strip()
