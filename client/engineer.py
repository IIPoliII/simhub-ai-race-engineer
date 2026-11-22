"""AI Race Engineer : client vocal. Normalement lancé tout seul par le plugin SimHub.

Tous les réglages (IA, langue, voix, annonces) se font dans la page du plugin dans SimHub.
Le push-to-talk est l'action « PushToTalk » à associer à un bouton dans Controls and events.

    uv run engineer.py                 # se connecte au plugin SimHub
    uv run engineer.py --prepare       # télécharge les modèles (voix + reconnaissance) puis quitte
    uv run engineer.py --say "Box box"
    uv run engineer.py --ask "écart devant ?" --snapshot sample_snapshot.json
"""
import argparse
import json
import os
import queue
import sys
import threading
import time
from pathlib import Path

import httpx

import audio

PLUGIN_URL = "http://127.0.0.1:8889"
POLL_S = 0.03
MIN_RECORDING_S = 0.3
MAX_EVENT_AGE_MS = 15000
PLUGIN_TIMEOUT_S = 20   # lancé par SimHub : on s'arrête si SimHub ne répond plus

GREETING = {"fr": "Radio check, je t'entends.", "en": "Radio check, loud and clear."}

# Utilisé quand SimHub ne tourne pas (--say, --prepare)
DEFAULT_CONFIG = {
    "rev": 0,
    "language": "fr",
    "llm": {"configured": False, "base_url": "http://localhost", "api_key": "none", "model": ""},
    "tts": {"engine": "kokoro", "kokoro_voice": "ff_siwis", "kokoro_speed": 1.1, "radio_effect": True},
    "elevenlabs": {},
    "stt": {"model": "small", "device": "cpu", "compute_type": "int8"},
}


class Plugin:
    """Dialogue avec le plugin SimHub (serveur local)."""

    # Jeton secret écrit par le plugin à chaque démarrage de SimHub, exigé sauf pour /snapshot
    TOKEN_FILE = Path(os.environ.get("LOCALAPPDATA", "")) / "AIRaceEngineer" / "api-token"

    def __init__(self, url: str = PLUGIN_URL):
        self.http = httpx.Client(base_url=url, timeout=1.0)
        self.token = self._read_token()

    def _read_token(self) -> str:
        try:
            return self.TOKEN_FILE.read_text(encoding="utf-8").strip()
        except OSError:
            return ""

    def get(self, path: str, **params):
        r = self.http.get(path, params=params, headers={"Authorization": f"Bearer {self.token}"})
        if r.status_code == 401:
            # SimHub a redémarré : nouveau jeton
            self.token = self._read_token()
            r = self.http.get(path, params=params, headers={"Authorization": f"Bearer {self.token}"})
        r.raise_for_status()
        return r.json()

    def config(self) -> dict:
        return self.get("/config")

    def poll(self, since: int) -> dict:
        return self.get("/poll", since=since)

    def snapshot(self) -> dict | None:
        try:
            return self.get("/snapshot")
        except (httpx.HTTPError, ValueError):
            return None

    def status(self, message: str):
        """Affiché dans la page du plugin, et dans le journal."""
        print(message, flush=True)
        try:
            self.get("/client-status", m=message)
        except (httpx.HTTPError, ValueError):
            pass


def timed(fn, *args):
    t = time.perf_counter()
    result = fn(*args)
    return result, time.perf_counter() - t


class Engine:
    """Voix, reconnaissance et IA, rechargées quand les réglages changent dans SimHub."""

    def __init__(self, plugin: Plugin, need_stt=True, snapshot_file: str | None = None):
        self.plugin = plugin
        self.need_stt = need_stt
        self.snapshot_file = Path(snapshot_file) if snapshot_file else None
        self.cfg: dict = {}
        self.voice = self.stt = self.engineer = None

    def apply(self, cfg: dict):
        import tts
        tts.progress = self.plugin.status
        old = self.cfg

        def changed(*keys):
            return any(old.get(k) != cfg.get(k) for k in keys)

        if self.voice is None or changed("tts", "elevenlabs"):
            self.plugin.status("Chargement de la voix...")
            try:
                self.voice = tts.Voice(cfg["tts"], cfg.get("elevenlabs", {}))
            except ValueError as e:
                # ElevenLabs mal configuré : on garde une voix locale plutôt que rester muet
                self.plugin.status(f"ElevenLabs : {e}. Voix locale utilisée.")
                self.voice = tts.Voice({**cfg["tts"], "engine": "kokoro"}, {})

        if self.need_stt and (self.stt is None or changed("stt", "language")):
            self.plugin.status("Chargement de la reconnaissance vocale (premier lancement : téléchargement)...")
            from stt import Transcriber
            self.stt = Transcriber(cfg["stt"], cfg["language"])

        if self.engineer is None or changed("llm", "language"):
            from llm import Engineer
            self.engineer = Engineer(cfg["llm"], cfg["language"])

        self.cfg = cfg

    def snapshot(self) -> dict | None:
        if self.snapshot_file:
            return json.loads(self.snapshot_file.read_text(encoding="utf-8"))
        return self.plugin.snapshot()

    def say(self, text: str):
        # « À la radio » bloque les boutons Radio check / Écouter de la page jusqu'à la fin de la phrase
        self.plugin.status("À la radio")
        try:
            samples, rate = self.voice.render(text)
            audio.play(samples, rate)
        finally:
            self.plugin.status("Prêt")

    def answer_audio(self, samples):
        text, t_stt = timed(self.stt.transcribe, samples)
        if not text:
            print("  (rien compris)", flush=True)
            return
        self.answer_text(text, t_stt)

    def answer_text(self, question: str, t_stt: float = 0.0):
        print(f"\n> {question}", flush=True)
        try:
            answer, t_llm = timed(self.engineer.ask, question, self.snapshot())
        except Exception as e:  # réseau, clé invalide, quota...
            self.plugin.status(f"Erreur IA : {e}")
            return
        (samples, rate), t_tts = timed(self.voice.render, answer)
        print(f"< {answer}\n  [stt {t_stt:.2f}s | ia {t_llm:.2f}s | voix {t_tts:.2f}s]", flush=True)
        audio.play(samples, rate)


def wait_for_plugin(plugin: Plugin, timeout: float) -> dict | None:
    start = time.time()
    while time.time() - start < timeout:
        try:
            return plugin.config()
        except httpx.HTTPError:
            time.sleep(1)
    return None


def run(plugin: Plugin, managed: bool):
    cfg = wait_for_plugin(plugin, PLUGIN_TIMEOUT_S if managed else 3)
    if cfg is None:
        sys.exit("Plugin SimHub injoignable sur 127.0.0.1:8889 : lance SimHub avec le plugin AI Race Engineer.")

    engine = Engine(plugin)
    jobs: queue.Queue = queue.Queue()

    def worker():
        # Une seule chose à la fois : recharger, parler, ou répondre à une question
        while True:
            kind, payload = jobs.get()
            try:
                if kind == "config":
                    engine.apply(payload)
                    plugin.status("Prêt")
                elif kind == "say":
                    engine.say(payload)
                elif kind == "audio":
                    engine.answer_audio(payload)
            except Exception as e:
                plugin.status(f"Erreur : {e}")

    threading.Thread(target=worker, daemon=True).start()
    jobs.put(("config", cfg))
    jobs.put(("say", GREETING.get(cfg.get("language"), GREETING["en"])))

    recorder = audio.Recorder()
    rev = cfg["rev"]
    last_id = None
    recording, started = False, 0.0
    last_ok = time.time()

    while True:
        try:
            p = plugin.poll(-1 if last_id is None else last_id)
            last_ok = time.time()
        except httpx.HTTPError:
            if recording:
                recorder.stop()
                recording = False
            if managed and time.time() - last_ok > PLUGIN_TIMEOUT_S:
                print("SimHub ne répond plus, arrêt.", flush=True)
                return
            time.sleep(0.5)
            continue

        if last_id is None:
            last_id = p["last_id"]  # on ignore les annonces d'avant le démarrage

        if p["rev"] != rev:
            try:
                cfg = plugin.config()
                rev = cfg["rev"]
                jobs.put(("config", cfg))
            except httpx.HTTPError:
                pass

        for event in p["events"]:
            last_id = max(last_id, event["id"])
            if event.get("age_ms", 0) < MAX_EVENT_AGE_MS:
                jobs.put(("say", event["text"]))

        down = p["ptt"]
        if down and not recording:
            audio.stop()          # coupe l'ingé s'il parlait encore
            audio.beep(1400)
            try:
                recorder.start()
                recording, started = True, time.time()
            except Exception as e:
                plugin.status(f"Micro : {e}")
        elif not down and recording:
            samples = recorder.stop()
            recording = False
            audio.beep(900)
            if time.time() - started >= MIN_RECORDING_S:
                jobs.put(("audio", samples))

        time.sleep(POLL_S)


def prepare():
    """Télécharge les modèles à l'installation, pour que le premier démarrage soit rapide."""
    import tts
    from faster_whisper import download_model
    tts.KokoroVoice(DEFAULT_CONFIG["tts"])
    for size in ("small", "base"):
        print(f"Téléchargement de la reconnaissance vocale ({size})...", flush=True)
        download_model(size)
    print("Modèles prêts.", flush=True)


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    ap = argparse.ArgumentParser(description="AI Race Engineer - client vocal pour SimHub")
    ap.add_argument("--managed", action="store_true", help="lancé par le plugin : s'arrête avec SimHub")
    ap.add_argument("--prepare", action="store_true", help="télécharge les modèles puis quitte")
    ap.add_argument("--say", metavar="TEXTE", help="fait parler la voix puis quitte")
    ap.add_argument("--ask", metavar="QUESTION", help="pose une question en texte (sans micro) puis quitte")
    ap.add_argument("--snapshot", metavar="FICHIER", help="avec --ask : télémétrie lue dans un fichier JSON")
    args = ap.parse_args()

    if args.prepare:
        prepare()
        return

    plugin = Plugin()
    if args.say or args.ask:
        cfg = wait_for_plugin(plugin, 2) or DEFAULT_CONFIG
        engine = Engine(plugin, need_stt=False, snapshot_file=args.snapshot)
        engine.apply(cfg)
        if args.say:
            engine.say(args.say)
        else:
            engine.answer_text(args.ask)
        return

    try:
        run(plugin, managed=args.managed)
    except KeyboardInterrupt:
        print("\nFin de session.")


if __name__ == "__main__":
    main()
