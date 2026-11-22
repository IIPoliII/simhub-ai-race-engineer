"""AI Race Engineer : maintiens le bouton, pose ta question, l'ingé te répond à la radio.

    uv run engineer.py                      # mode course (micro + push-to-talk)
    uv run engineer.py --list-controls      # trouver le numéro du bouton du volant
    uv run engineer.py --say "Box box"      # tester la voix
    uv run engineer.py --ask "écart devant ?" --snapshot sample_snapshot.json   # tester sans jeu
"""
import argparse
import queue
import sys
import threading
import time
import tomllib
from pathlib import Path

HERE = Path(__file__).parent
MIN_RECORDING_S = 0.3


def load_config(path: Path) -> dict:
    if not path.exists():
        example = HERE / "config.example.toml"
        sys.exit(f"Config introuvable : {path}\nCopie {example.name} en config.toml et remplis-le.")
    with open(path, "rb") as f:
        return tomllib.load(f)


def timed(fn, *args):
    t = time.perf_counter()
    result = fn(*args)
    return result, time.perf_counter() - t


class Pipeline:
    """Question (audio ou texte) -> télémétrie -> LLM -> voix."""

    def __init__(self, cfg: dict, snapshot_file: str | None, need_stt: bool):
        from llm import Engineer
        from telemetry import Telemetry
        from tts import Voice

        lang = cfg.get("language", "fr")
        print("Chargement de la voix...")
        self.voice = Voice(cfg.get("tts", {}), cfg.get("elevenlabs", {}))
        self.stt = None
        if need_stt:
            from stt import Transcriber
            print("Chargement de Whisper...")
            self.stt = Transcriber(cfg.get("stt", {}), lang)
        self.telemetry = Telemetry(cfg.get("snapshot_url", "http://127.0.0.1:8889/snapshot"), snapshot_file)
        self.engineer = Engineer(cfg["llm"], lang)

    def handle_audio(self, samples):
        text, t_stt = timed(self.stt.transcribe, samples)
        if not text:
            print("  (rien compris)")
            return
        self.handle_text(text, t_stt)

    def handle_text(self, question: str, t_stt: float = 0.0):
        print(f"\n> {question}")
        snapshot = self.telemetry.snapshot()
        try:
            answer, t_llm = timed(self.engineer.ask, question, snapshot)
        except Exception as e:  # réseau, clé invalide, quota...
            print(f"  ! LLM : {e}")
            return
        (samples, rate), t_tts = timed(self.voice.render, answer)
        print(f"< {answer}")
        print(f"  [stt {t_stt:.2f}s | llm {t_llm:.2f}s | voix {t_tts:.2f}s]")
        import audio
        audio.play(samples, rate)


def race_loop(cfg: dict, pipeline: Pipeline):
    import audio
    from ptt import PushToTalk

    stt_cfg = cfg.get("stt", {})
    recorder = audio.Recorder(stt_cfg.get("input_device"))
    ptt = PushToTalk(cfg.get("ptt", {}))

    # Les questions sont traitées une par une dans un thread, la boucle reste réactive
    jobs: queue.Queue = queue.Queue()

    def worker():
        while True:
            samples = jobs.get()
            try:
                pipeline.handle_audio(samples)
            except Exception as e:
                print(f"  ! erreur : {e}")

    threading.Thread(target=worker, daemon=True).start()

    if cfg.get("greeting"):
        pipeline.voice.say(cfg["greeting"])
    print(f"\nPrêt. Push-to-talk : {ptt.describe()}.  Ctrl+C pour quitter.")

    recording = False
    started = 0.0
    while True:
        down = ptt.pressed()
        if down and not recording:
            audio.stop()          # coupe l'ingé s'il parlait encore
            audio.beep(1400)
            recorder.start()
            recording, started = True, time.time()
        elif not down and recording:
            samples = recorder.stop()
            recording = False
            audio.beep(900)
            if time.time() - started >= MIN_RECORDING_S:
                jobs.put(samples)
        time.sleep(0.01)


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    ap = argparse.ArgumentParser(description="AI Race Engineer - client vocal pour SimHub")
    ap.add_argument("--config", type=Path, default=HERE / "config.toml")
    ap.add_argument("--list-controls", action="store_true", help="affiche les boutons pressés pour configurer le push-to-talk")
    ap.add_argument("--say", metavar="TEXTE", help="fait parler la voix puis quitte")
    ap.add_argument("--ask", metavar="QUESTION", help="pose une question en texte (sans micro) puis quitte")
    ap.add_argument("--snapshot", metavar="FICHIER", help="utilise un fichier JSON au lieu de SimHub")
    args = ap.parse_args()

    if args.list_controls:
        from ptt import list_controls
        list_controls()
        return

    cfg = load_config(args.config)

    if args.say:
        from tts import Voice
        Voice(cfg.get("tts", {}), cfg.get("elevenlabs", {})).say(args.say)
        return

    pipeline = Pipeline(cfg, args.snapshot, need_stt=not args.ask)
    if args.ask:
        pipeline.handle_text(args.ask)
        return

    try:
        race_loop(cfg, pipeline)
    except KeyboardInterrupt:
        print("\nFin de session.")


if __name__ == "__main__":
    main()
