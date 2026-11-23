"""Reconnaissance vocale locale avec faster-whisper."""
import os

os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")

import numpy as np  # noqa: E402
from faster_whisper import WhisperModel  # noqa: E402

# Vocabulaire de course pour aider Whisper sur les mots techniques
# « Box, box ! Pit, pit. » écrits tels quels : sans eux, Whisper entendait « Bix, Bix », « Pete, Pete » ou « Bit, bit »
HINTS = {
    "fr": "Radio de course. Box, box ! Pit, pit. Je rentre au stand. Combien d'essence ? Pneus pluie, slicks, softs, "
          "mediums, hards, inters, carburant, écart, devant, derrière, tour, stint, pression, température, freins, dégâts.",
    "en": "Race radio. Box, box! Pit, pit. Boxing this lap. How much fuel? Wets, slicks, softs, mediums, hards, inters, "
          "tyres, gap, ahead, behind, lap, stint, pit stop, pressures, temperatures, brakes, damage.",
}
# Mots poussés à chaque phrase (faster-whisper 1.0+)
HOTWORDS = "box box, pit pit"


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
        # Première passe à vide, au plus juste (une passe, sans repli de température) : la vraie première
        # question n'attend pas la mise en route du modèle. Les segments se calculent quand on les lit.
        segments, _ = self.model.transcribe(np.zeros(16000, dtype=np.float32), language=language, beam_size=1,
                                            temperature=0.0, vad_filter=False, condition_on_previous_text=False)
        list(segments)

    def transcribe(self, audio: np.ndarray) -> str:
        segments, _ = self.model.transcribe(
            audio,
            language=self.language,
            beam_size=1,
            vad_filter=True,                  # coupe les silences -> moins d'hallucinations
            condition_on_previous_text=False,
            initial_prompt=HINTS.get(self.language),
            hotwords=HOTWORDS,
        )
        return " ".join(s.text.strip() for s in segments).strip()
