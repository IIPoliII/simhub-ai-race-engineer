"""AI Race Engineer : client vocal. Normalement lancé tout seul par le plugin SimHub.

Tous les réglages (IA, langue, voix, annonces) se font dans la page du plugin dans SimHub.
Le push-to-talk est l'action « PushToTalk » à associer à un bouton dans Controls and events.

    uv run engineer.py                 # se connecte au plugin SimHub
    uv run engineer.py --prepare       # télécharge les modèles (voix + reconnaissance) puis quitte
    uv run engineer.py --say "Box box"
    uv run engineer.py --ask "écart devant ?" --snapshot sample_snapshot.json
"""
import argparse
import faulthandler
import json
import os
import queue
import subprocess
import sys
import threading
import time
import traceback
from pathlib import Path

import httpx
import numpy as np

import audio

PLUGIN_URL = "http://127.0.0.1:8889"
POLL_S = 0.03
MIN_RECORDING_S = 0.3
MAX_EVENT_AGE_MS = 15000
PLUGIN_TIMEOUT_S = 20   # lancé par SimHub : on s'arrête si SimHub ne répond plus
# SimHub encore là mais muet (gros chargement, plugin désactivé) : on attend bien plus longtemps avant de s'arrêter.
# Avant, 20 s sans réponse arrêtaient le client, relancé ensuite par le plugin avec son « Radio check »
SIMHUB_SILENT_S = 300
RETRY_S = 60            # réglages qui n'ont pas pu se charger : nouvel essai

GREETING = {"fr": "Radio check, je t'entends.", "en": "Radio check, loud and clear."}
# Voix locale en échec à la mise en route (--prepare), gardée à côté du repère « air-prepared » du même environnement
LOCAL_VOICE_WARNING = Path(sys.prefix) / "air-local-voice.txt"


def _windows_language() -> str:
    """Langue de Windows, quand le plugin ne l'a pas donnée (lancement à la main)."""
    try:
        import ctypes
        return "fr" if ctypes.windll.kernel32.GetUserDefaultUILanguage() & 0x3FF == 0x0C else "en"
    except (AttributeError, OSError):
        return "fr"


# Langue des messages affichés dans la page du plugin : celle des réglages
# (avant de les recevoir, celle que le plugin passe au lancement)
LANG = os.environ.get("AIR_LANG") or _windows_language()
# Plugin 0.4.8 encore chargé juste après une mise à jour : il ignore les codes d'état et reconnaît
# les messages à leur début en français. Seul le plugin 0.5.0+ passe AIR_LANG (réglé dans main()).
LEGACY_PLUGIN = False


def t(fr: str, en: str) -> str:
    return fr if LANG == "fr" else en


def set_language(language: str):
    global LANG
    import tts
    LANG = tts.lang = "fr" if (language == "fr" or LEGACY_PLUGIN) else "en"


def log(message: str):
    """Journal lu par le plugin. Sortie fermée (SimHub disparu) : on ne plante pas pour ça."""
    try:
        print(message, flush=True)
    except (OSError, ValueError):
        pass


def default_config() -> dict:
    """Réglages utilisés quand SimHub ne tourne pas (--say, --prepare), dans la langue de LANG."""
    return {
        "rev": 0,
        "language": LANG,
        "llm": {"configured": False, "base_url": "http://localhost", "api_key": "none", "model": ""},
        "tts": {"engine": "kokoro", "kokoro_voice": "ff_siwis" if LANG == "fr" else "bm_george",
                "kokoro_speed": 1.1, "radio_effect": True},
        "elevenlabs": {},
        "stt": {"model": "small", "device": "cpu", "compute_type": "int8"},
    }


class Plugin:
    """Dialogue avec le plugin SimHub (serveur local)."""

    # Jeton secret écrit par le plugin à chaque démarrage de SimHub, exigé sauf pour /snapshot
    TOKEN_FILE = Path(os.environ.get("LOCALAPPDATA", "")) / "AIRaceEngineer" / "api-token"

    def __init__(self, url: str = PLUGIN_URL, reread_token: bool = True):
        # trust_env=False : le proxy système de Windows ne doit jamais voir le jeton (et ne joint pas 127.0.0.1)
        self.http = httpx.Client(base_url=url, timeout=1.0, trust_env=False)
        self.reread_token = reread_token
        self.token = self._read_token()

    def _read_token(self) -> str:
        try:
            return self.TOKEN_FILE.read_text(encoding="utf-8").strip()
        except OSError:
            return ""

    def get(self, path: str, timeout: float | None = None, **params):
        t = httpx.USE_CLIENT_DEFAULT if timeout is None else timeout
        r = self.http.get(path, params=params, headers={"Authorization": f"Bearer {self.token}"}, timeout=t)
        if r.status_code == 401 and self.reread_token:
            # SimHub a redémarré : nouveau jeton (lancé à la main seulement ; lancé par le plugin,
            # un nouveau jeton veut dire un autre SimHub, qui lance son propre client)
            self.token = self._read_token()
            r = self.http.get(path, params=params, headers={"Authorization": f"Bearer {self.token}"}, timeout=t)
        r.raise_for_status()
        return r.json()

    def config(self) -> dict:
        return self.get("/config")

    def poll(self, since: int, ptt: bool, rev: int) -> dict:
        # Attente longue : le plugin ne répond que quand le bouton, une annonce ou les réglages changent (au plus 1 s)
        return self.get("/poll", timeout=3.0, since=since, wait=1, ptt=int(ptt), rev=rev)

    def snapshot(self) -> dict | None:
        try:
            return self.get("/snapshot")
        except (httpx.HTTPError, ValueError):
            return None

    def mic(self, kind: str, **params):
        """Test du push-to-talk dans la page : ce qui a été enregistré (short, rec), puis compris (heard)."""
        try:
            self.get("/client-mic", k=kind, **params)
        except (httpx.HTTPError, ValueError):
            pass  # plugin d'avant le test du micro

    def control(self, **params) -> dict | None:
        """Réglage dans le jeu (répartition de freinage, ABS, TC) : le plugin appuie sur les touches du jeu jusqu'à
        la valeur, ce qui peut prendre quelques secondes. None : plugin d'avant les réglages à la voix."""
        try:
            return self.get("/control", timeout=15.0, **params)
        except (httpx.HTTPError, ValueError):
            return None

    def pit_menu(self, **params) -> dict | None:
        """Programme d'arrêt mis dans le menu des stands du jeu (iRacing seulement)."""
        try:
            return self.get("/pit", timeout=3.0, **params)
        except (httpx.HTTPError, ValueError):
            return None

    def status(self, message: str, code: str = "loading"):
        """Affiché dans la page du plugin, et dans le journal.
        code : état indépendant de la langue, lu par la page (ready, speaking, loading, warn, error)."""
        log(message)
        try:
            self.get("/client-status", m=message, c=code)
        except (httpx.HTTPError, ValueError):
            pass


def process_alive(pid: int) -> bool:
    """Le processus pid tourne-t-il encore ? (Windows ; ailleurs, on le croit vivant)"""
    if sys.platform != "win32":
        return True
    import ctypes
    from ctypes import wintypes
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    SYNCHRONIZE, WAIT_TIMEOUT = 0x00100000, 0x102
    handle = kernel32.OpenProcess(SYNCHRONIZE, False, pid)
    if not handle:
        return False  # plus de processus avec ce numéro
    try:
        return kernel32.WaitForSingleObject(handle, 0) == WAIT_TIMEOUT
    finally:
        kernel32.CloseHandle(handle)


def simhub_gone(managed: bool, silent_s: float) -> bool:
    """Lancé par SimHub, qui ne répond plus depuis silent_s secondes : faut-il s'arrêter ?"""
    if not managed:
        return False
    try:
        pid = int(os.environ.get("AIR_SIMHUB_PID", ""))
    except ValueError:
        return silent_s > PLUGIN_TIMEOUT_S  # plugin d'avant la 0.6.0 : comme avant
    return not process_alive(pid) or silent_s > SIMHUB_SILENT_S


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
        # Problèmes qui durent, (fr, en, code) : affichés au repos à la place de « Prêt »
        self.broken = None       # réglages pas chargés
        self.ai_issue = None     # dernière question à l'IA
        self.voice_issue = None  # voix choisie remplacée par la voix locale
        self.mic_issue = None    # dernier essai d'enregistrement
        self.pit = None          # programme d'arrêt après « box box » (pit.PitPlan)
        # Voix locale en échec à la mise en route (--prepare) : la voix en ligne marche, mais plus de secours hors ligne
        self.local_voice_issue = None
        try:
            detail = LOCAL_VOICE_WARNING.read_text(encoding="utf-8").strip()
            self.local_voice_issue = (f"Voix locale indisponible ({detail}) : la voix en ligne marche quand même.",
                                      f"Local voice unavailable ({detail}): the online voice still works.", "warn")
        except OSError:
            pass

    def apply(self, cfg: dict):
        import tts
        tts.progress = self.plugin.status
        set_language(cfg.get("language", "fr"))
        tts.swiss = bool(cfg.get("swiss_numbers"))
        levels = cfg.get("audio") or {}  # absent avec un plugin d'avant la 0.6.0
        tts.voice_volume = min(2.0, max(0.5, float(levels.get("voice_volume", 1.0))))
        tts.noise_volume = min(2.0, max(0.0, float(levels.get("noise_volume", 1.0))))

        def changed(*keys):
            return any(self.cfg.get(k) != cfg.get(k) for k in keys)

        def done(*keys):
            # Partie chargée : un nouvel essai après une erreur ne la recharge pas
            self.cfg = {**self.cfg, **{k: cfg.get(k) for k in keys}}

        if self.voice is None or changed("tts", "elevenlabs"):
            self.plugin.status(t("Chargement de la voix...", "Loading the voice..."))
            self.voice_issue = None
            try:
                self.voice = tts.Voice(cfg["tts"], cfg.get("elevenlabs", {}))
            except ValueError as e:
                # ElevenLabs mal configuré : on garde une voix locale plutôt que rester muet
                self.voice_issue = (f"ElevenLabs : {e}. Voix locale utilisée.", f"ElevenLabs: {e}. Using the local voice.", "warn")
                self.plugin.status(t(*self.voice_issue[:2]), "warn")
                self.voice = tts.Voice({**cfg["tts"], "engine": "kokoro"}, {})
            done("tts", "elevenlabs")

        if self.need_stt and (self.stt is None or changed("stt")):
            # Modèles déjà téléchargés par --prepare
            self.plugin.status(t("Chargement de la reconnaissance vocale...", "Loading speech recognition..."))
            from stt import Transcriber
            self.stt = Transcriber(cfg["stt"], cfg["language"])
            done("stt")
        elif self.need_stt and changed("language"):
            self.stt.language = cfg["language"]  # la langue ne sert qu'à chaque question : pas de rechargement

        if self.engineer is None or changed("llm", "language", "swiss_numbers"):
            from llm import Engineer
            self.engineer = Engineer(cfg["llm"], cfg["language"], bool(cfg.get("swiss_numbers")))
            self.ai_issue = None

        self.cfg = cfg

    def idle(self):
        """État de repos de la page : « Prêt », ou le problème qui dure (sinon « Prêt » le cacherait)."""
        for issue in (self.broken, self.mic_issue, self.ai_issue, self.voice_issue, self.voice and self.voice.warning,
                      self.local_voice_issue):
            if issue:
                fr, en, code = issue
                self.plugin.status(t(fr, en), code)
                return
        self.plugin.status(t("Prêt", "Ready"), "ready")

    def snapshot(self) -> dict | None:
        if self.snapshot_file:
            return json.loads(self.snapshot_file.read_text(encoding="utf-8"))
        return self.plugin.snapshot()

    def say(self, text: str):
        if self.voice is None:
            return  # réglages pas chargés : l'erreur reste affichée
        # « speaking » bloque les boutons Radio check / Écouter de la page jusqu'à la fin de la phrase
        self.plugin.status(t("À la radio", "On the radio"), "speaking")
        try:
            samples, rate = self.voice.render(text)
            audio.play(samples, rate)
        finally:
            self.idle()

    def answer_audio(self, samples):
        if self.stt is None:
            self.idle()  # reconnaissance pas chargée : on affiche pourquoi
            return
        text, t_stt = timed(self.stt.transcribe, samples)
        self.plugin.mic("heard", t=text)
        if not text:
            log(t("  (rien compris)", "  (didn't catch that)"))
            return
        self.answer_text(text, t_stt)

    def pit_in_game(self, snap: dict) -> bool:
        """Le programme d'arrêt va-t-il tout seul dans le menu des stands ? (iRacing, réglages à la voix activés)"""
        import controls
        return bool(self.cfg.get("game_control")) and snap.get("game") in controls.PIT_MENU_GAMES

    def adjust(self, question: str, snap: dict, language: str) -> bool:
        """Réglage demandé à la voix (frein, ABS, TC, essence pour N tours) : fait sans l'IA. False si ce n'en est pas un."""
        import controls
        cmd = controls.parse(question)
        if not cmd:
            return False
        if cmd["kind"] == "fuel_laps":
            plan = controls.fuel_for_laps(snap, cmd["laps"])
            reply = None
            if self.pit and plan["to_add"] is not None:
                # Pendant un « box box » : c'est l'essence du programme d'arrêt (rappelée à l'entrée des stands)
                with self.pit.lock:
                    self.pit.plan["fuel"] = plan["to_add"]
            if plan["to_add"] is not None and self.pit_in_game(snap):
                reply = self.plugin.pit_menu(fuel=plan["to_add"])
            text = controls.spoken_fuel(cmd["laps"], plan, reply, language)
        else:
            self.plugin.status(t("Réglage en cours", "Adjusting"), "speaking")
            params = {"k": cmd["kind"]}
            if "target" in cmd:
                params["v"] = cmd["target"]
            else:
                params["s"] = cmd["steps"]
            reply = self.plugin.control(**params)
            text = controls.spoken(cmd, reply, language)
        log(f"< {text}\n  [réglage {cmd} : {reply}]")
        self.say(text)
        return True

    def answer_text(self, question: str, t_stt: float = 0.0):
        if self.engineer is None or self.voice is None:
            self.idle()
            return
        log(f"\n> {question}")
        import pit
        language = self.engineer.language
        if self.pit and pit.CANCEL.search(question):
            self.pit = None
            self.say(pit.cancelled(language))
            return
        snap = self.snapshot()
        running = bool(snap and snap.get("game_running"))
        boxing = pit.BOX_CALL.search(question)
        if running and not boxing and self.adjust(question, snap, language):
            return
        if boxing and running:
            # nouvel appel : programme repris de zéro, mis dans le menu des stands quand le jeu le permet
            self.pit = pit.PitPlan(snap, language, laps_later=pit.laps_later(question), in_game=self.pit_in_game(snap))
            # Whisper écrit parfois « Bix, bix » : l'IA, elle, doit voir l'appel tel que le pilote l'a dit
            question = pit.normalize_call(question)
        if self.pit:
            question = pit.fix_tyre_words(question)
        try:
            answer, t_llm = timed(self.engineer.ask, question, snap, self.pit)
            self.ai_issue = None
        except Exception as e:  # réseau, clé invalide, quota...
            self.ai_issue = (f"Erreur IA : {e}", f"AI error: {e}", "error")
            self.idle()
            return
        if self.pit and self.pit.in_game:
            reply = self.plugin.pit_menu(**self.pit.menu_params())
            log(f"  [menu des stands : {(reply or {}).get('result')}]")
        if not answer:
            # Modèle qui réfléchit et épuise max_tokens : on le dit à la radio plutôt que rester muet
            from llm import EMPTY_ANSWER
            model = self.engineer.model
            self.ai_issue = (f"{model} n'a rien répondu (modèle qui réfléchit avant de parler ?)",
                             f"{model} gave an empty answer (a model that thinks before speaking?)", "warn")
            answer = EMPTY_ANSWER[self.engineer.language]
        self.plugin.status(t("À la radio", "On the radio"), "speaking")
        try:
            (samples, rate), t_tts = timed(self.voice.render, answer)
            detail = getattr(self.voice, "last_detail", "")
            log(f"< {answer}\n  [stt {t_stt:.2f}s | ia {t_llm:.2f}s | voix {t_tts:.2f}s{f' ({detail})' if detail else ''}]")
            audio.play(samples, rate)
        finally:
            self.idle()


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
        sys.exit(t("Plugin SimHub injoignable sur 127.0.0.1:8889 : lance SimHub avec le plugin AI Race Engineer.",
                   "SimHub plugin unreachable on 127.0.0.1:8889: start SimHub with the AI Race Engineer plugin."))

    engine = Engine(plugin)
    # (type, contenu, instant de l'annonce en time.monotonic(), ou None si elle ne périme pas)
    jobs: queue.Queue = queue.Queue()

    def worker():
        # Une seule chose à la fois : recharger, parler, ou répondre à une question
        while True:
            kind, payload, at = jobs.get()
            try:
                if kind == "config":
                    try:
                        engine.apply(payload)
                        engine.broken = None
                    except Exception as e:
                        engine.broken = (f"Erreur : {e}", f"Error: {e}", "error")
                    engine.idle()
                elif kind == "say":
                    # Annonce restée en file derrière une réponse lente : périmée, on ne la lit pas
                    if at is None or time.monotonic() - at < MAX_EVENT_AGE_MS / 1000:
                        engine.say(payload)
                elif kind == "audio":
                    engine.answer_audio(payload)
            except Exception as e:
                log(traceback.format_exc().rstrip())  # dans client.log, pour savoir d'où ça vient
                plugin.status(t(f"Erreur : {e}", f"Error: {e}"), "error")

    threading.Thread(target=worker, daemon=True).start()
    jobs.put(("config", cfg, None))
    # Relancé par le plugin après un arrêt imprévu : pas de « Radio check » en pleine course
    if os.environ.get("AIR_QUIET_START") != "1":
        jobs.put(("say", GREETING.get(cfg.get("language"), GREETING["en"]), None))
    else:
        log(t("Relancé après un arrêt : pas de radio check.", "Restarted after a stop: no radio check."))

    recorder = audio.Recorder()
    rev = cfg["rev"]
    last_id = None
    recording, started = False, 0.0
    last_ok = time.time()
    ptt_seen = False
    retry_at = time.monotonic() + RETRY_S

    while True:
        try:
            p = plugin.poll(-1 if last_id is None else last_id, ptt_seen, rev)
            last_ok = time.time()
        except httpx.HTTPError as e:
            if recording:
                recorder.stop()
                recording = False
            if managed and isinstance(e, httpx.HTTPStatusError) and e.response.status_code == 401:
                # Jeton refusé : SimHub a redémarré (après un plantage) et lance son propre client
                log(t("SimHub a redémarré avec un nouveau jeton : arrêt de ce client.",
                      "SimHub restarted with a new token: this client is exiting."))
                return
            if simhub_gone(managed, time.time() - last_ok):
                log(t("SimHub ne répond plus, arrêt.", "SimHub stopped responding, exiting."))
                return
            time.sleep(0.5)
            continue

        try:
            if last_id is None:
                last_id = p["last_id"]  # on ignore les annonces d'avant le démarrage

            if p["rev"] != rev:
                try:
                    cfg = plugin.config()
                    rev = cfg["rev"]
                    jobs.put(("config", cfg, None))
                except httpx.HTTPError:
                    pass
            elif engine.broken and time.monotonic() >= retry_at:
                retry_at = time.monotonic() + RETRY_S
                jobs.put(("config", cfg, None))

            for event in p["events"]:
                last_id = max(last_id, event["id"])
                age_ms = event.get("age_ms", 0)
                if age_ms < MAX_EVENT_AGE_MS:
                    jobs.put(("say", event["text"], time.monotonic() - age_ms / 1000))

            # Après « box box » : rappel du programme à l'entrée des stands (seulement pendant un arrêt prévu)
            plan = engine.pit
            if plan is not None:
                text, done = plan.watch(plugin.snapshot())
                if text:
                    jobs.put(("say", text, time.monotonic()))
                if done and engine.pit is plan:
                    engine.pit = None

            down = ptt_seen = p["ptt"]
            if down and not recording:
                audio.stop()          # coupe l'ingé s'il parlait encore
                audio.beep(1400)
                try:
                    recorder.start()
                    recording, started = True, time.time()
                    engine.mic_issue = None  # la page se met à jour au prochain état du fil de travail (pas d'ici : il peut parler)
                except Exception as e:
                    # Gardé jusqu'au prochain essai réussi : la prochaine annonce n'efface pas le problème
                    engine.mic_issue = (f"Micro : {e}", f"Microphone: {e}", "error")
                    plugin.status(t(f"Micro : {e}", f"Microphone: {e}"), "error")
            elif not down and recording:
                samples = recorder.stop()
                recording = False
                audio.beep(900)
                seconds = time.time() - started
                peak = float(np.max(np.abs(samples))) if samples.size else 0.0
                long_enough = seconds >= MIN_RECORDING_S
                # 0 % seulement pour un silence total (micro coupé) : un micro très faible affiche au moins 1 %
                plugin.mic("rec" if long_enough else "short", s=f"{seconds:.2f}",
                           p=max(1, round(peak * 100)) if peak > 0 else 0, dev=recorder.device_name())
                if long_enough:
                    jobs.put(("audio", samples, None))
        except Exception:
            # Un souci ponctuel (réponse inattendue, carte son) ne doit pas couper la radio : on le note et on continue
            log(traceback.format_exc().rstrip())
            time.sleep(0.5)
            continue

        if not p.get("wait"):
            time.sleep(POLL_S)  # plugin d'avant l'attente longue : il répond tout de suite


def check_local_voice() -> str | None:
    """espeak (phonétique de la voix locale) quitte tout le processus quand il échoue : on l'essaie
    dans un processus à part, pour que la mise en route le signale au lieu d'un client qui s'arrête en course.
    Renvoie la cause de l'échec, None si la voix locale marche."""
    try:
        r = subprocess.run([sys.executable, "-c", "import tts; tts.check_espeak()"], cwd=Path(__file__).parent,
                           capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
                           creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        if r.returncode == 0:
            return None
        lines = (r.stderr or r.stdout or "").strip().splitlines()
        return lines[-1] if lines else f"code {r.returncode}"
    except (OSError, subprocess.SubprocessError) as e:
        return str(e)


def prepare():
    """Télécharge les modèles à l'installation, pour que le premier démarrage soit rapide."""
    # Avant huggingface_hub : sans liens symboliques (Windows), son avertissement resterait
    # la dernière ligne affichée dans la page pendant tout le téléchargement de Whisper
    os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")
    import tts
    from faster_whisper import download_model
    try:
        tts.KokoroVoice(default_config()["tts"])
        problem = check_local_voice()
    except tts.EspeakPathError as e:
        problem = str(e)
    # Modèles téléchargés, mais pas de voix locale sur ce PC : la voix en ligne reste utilisable. Gardé dans un
    # fichier, pour que le client l'affiche au repos (ici, la ligne suivante l'effacerait aussitôt de la page)
    if problem:
        print(t(f"Attention : voix locale indisponible ({problem}). La voix en ligne marche quand même.",
                f"Warning: local voice unavailable ({problem}). The online voice still works."), flush=True)
        LOCAL_VOICE_WARNING.write_text(problem, encoding="utf-8")
    else:
        LOCAL_VOICE_WARNING.unlink(missing_ok=True)
    # Taille indiquée : faster_whisper n'affiche pas de pourcentage
    for size, mb in (("small", 460), ("base", 140)):
        print(t(f"Téléchargement de la reconnaissance vocale ({size}, ~{mb} Mo)...",
                f"Downloading speech recognition ({size}, ~{mb} MB)..."), flush=True)
        download_model(size)
    # Repère lu par le plugin : la mise en route est faite (sinon il la relance au démarrage de SimHub)
    (Path(sys.prefix) / "air-prepared").write_text("ok")
    if problem:  # redit à la fin : c'est la dernière ligne qui reste affichée dans la page
        print(t("Modèles prêts. Voix locale indisponible : la voix en ligne marche quand même.",
                "Models ready. Local voice unavailable: the online voice still works."), flush=True)
    else:
        print(t("Modèles prêts.", "Models ready."), flush=True)


def lower_priority():
    """Lancé avec SimHub : priorité « inférieure à la normale », le jeu passe toujours avant.
    Le son reste fluide : les fils audio gardent leur propre priorité haute."""
    if sys.platform == "win32":
        import ctypes
        BELOW_NORMAL_PRIORITY_CLASS = 0x4000
        ctypes.windll.kernel32.SetPriorityClass(ctypes.windll.kernel32.GetCurrentProcess(), BELOW_NORMAL_PRIORITY_CLASS)


def main():
    global LEGACY_PLUGIN
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    ap = argparse.ArgumentParser(description="AI Race Engineer - voice client for SimHub")
    ap.add_argument("--managed", action="store_true", help="started by the plugin: exits with SimHub")
    ap.add_argument("--prepare", action="store_true", help="downloads the models then exits")
    ap.add_argument("--say", metavar="TEXT", help="speaks TEXT with the voice then exits")
    ap.add_argument("--ask", metavar="QUESTION", help="asks a text question (no mic) then exits; needs SimHub with an AI set up")
    ap.add_argument("--snapshot", metavar="FILE", help="with --ask: telemetry read from a JSON FILE instead of the game")
    args = ap.parse_args()

    if args.prepare:
        set_language(LANG)
        prepare()
        return

    if args.managed:
        # Plantage hors Python (voix, carte son...) : la pile de chaque fil part dans client.log, qui la garde
        # quand le plugin relance le client
        faulthandler.enable()
        LEGACY_PLUGIN = "AIR_LANG" not in os.environ
        set_language(LANG)
        lower_priority()

    # Lancé par le plugin : jamais de relecture du jeton (voir Plugin.get)
    plugin = Plugin(reread_token=not args.managed)
    if args.say or args.ask:
        cfg = wait_for_plugin(plugin, 2) or default_config()
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
        print(t("\nFin de session.", "\nSession ended."))


if __name__ == "__main__":
    main()
