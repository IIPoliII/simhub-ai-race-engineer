"""Synthèse vocale : voix neuronales Microsoft (en ligne, gratuites), Kokoro (local) ou ElevenLabs (payant)."""
import asyncio
import importlib.metadata
import io
import os
import re
import shutil
import stat
import time
import uuid
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
# Après un échec de la voix en ligne, la voix locale parle seule un moment (sinon chaque phrase attend l'échec)
ONLINE_RETRY_S = 60


# Langue des messages (celle des réglages, choisie par engineer.py)
lang = "fr"


def _t(fr: str, en: str) -> str:
    return fr if lang == "fr" else en


def progress(message: str, code: str = "loading"):
    """Remplacé par engineer.py pour afficher la progression dans la page du plugin SimHub.
    code : état indépendant de la langue (loading, warn), lu par la page."""
    print(message, flush=True)


def _reason(e: Exception) -> str:
    """Cause courte d'un échec, sur une ligne, pour la page du plugin."""
    if isinstance(e, httpx.HTTPStatusError):
        return f"HTTP {e.response.status_code}"
    lines = str(e).strip().splitlines()
    return lines[0] if lines else type(e).__name__


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


class EspeakPathError(RuntimeError):
    """Données d'espeak inutilisables : la voix locale ne peut pas marcher sur ce PC."""


def _sid_string(psid) -> str:
    import ctypes
    from ctypes import wintypes
    advapi32, kernel32 = ctypes.WinDLL("advapi32", use_last_error=True), ctypes.WinDLL("kernel32", use_last_error=True)
    advapi32.ConvertSidToStringSidW.argtypes = [ctypes.c_void_p, ctypes.POINTER(wintypes.LPWSTR)]
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    s = wintypes.LPWSTR()
    if not advapi32.ConvertSidToStringSidW(psid, ctypes.byref(s)):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        return s.value
    finally:
        kernel32.LocalFree(ctypes.cast(s, ctypes.c_void_p))


def _user_sid() -> str:
    """SID du compte Windows qui lance le client (S-1-5-21-...) : toujours en ASCII, contrairement au nom."""
    import ctypes
    from ctypes import wintypes
    advapi32, kernel32 = ctypes.WinDLL("advapi32", use_last_error=True), ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.GetCurrentProcess.restype = wintypes.HANDLE
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    advapi32.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
    advapi32.GetTokenInformation.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD,
                                             ctypes.POINTER(wintypes.DWORD)]
    TOKEN_QUERY, TOKEN_USER = 0x0008, 1
    token = wintypes.HANDLE()
    if not advapi32.OpenProcessToken(kernel32.GetCurrentProcess(), TOKEN_QUERY, ctypes.byref(token)):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        size = wintypes.DWORD()
        advapi32.GetTokenInformation(token, TOKEN_USER, None, 0, ctypes.byref(size))
        buf = ctypes.create_string_buffer(size.value)
        if not advapi32.GetTokenInformation(token, TOKEN_USER, buf, size, ctypes.byref(size)):
            raise ctypes.WinError(ctypes.get_last_error())
        return _sid_string(ctypes.c_void_p.from_buffer(buf).value)  # TOKEN_USER commence par le pointeur du SID
    finally:
        kernel32.CloseHandle(token)


def _owner_sid(path: Path) -> str:
    import ctypes
    from ctypes import wintypes
    advapi32, kernel32 = ctypes.WinDLL("advapi32", use_last_error=True), ctypes.WinDLL("kernel32", use_last_error=True)
    advapi32.GetNamedSecurityInfoW.argtypes = [wintypes.LPCWSTR, ctypes.c_int, wintypes.DWORD,
                                               ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p),
                                               ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
    advapi32.GetNamedSecurityInfoW.restype = wintypes.DWORD
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION = 1, 1
    owner, sd = ctypes.c_void_p(), ctypes.c_void_p()
    err = advapi32.GetNamedSecurityInfoW(str(path), SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION, ctypes.byref(owner),
                                         None, None, None, ctypes.byref(sd))
    if err:
        raise ctypes.WinError(err)
    try:
        return _sid_string(owner)
    finally:
        kernel32.LocalFree(sd)


def _private_dir(path: Path, sid: str) -> bool:
    """Crée le dossier accessible à ce compte seul (plus SYSTEM et les administrateurs). S'il existe déjà, il doit
    appartenir à ce compte (ou aux administrateurs) : sinon un autre compte du PC a pu y préparer ses fichiers."""
    import ctypes
    from ctypes import wintypes
    advapi32, kernel32 = ctypes.WinDLL("advapi32", use_last_error=True), ctypes.WinDLL("kernel32", use_last_error=True)

    class SECURITY_ATTRIBUTES(ctypes.Structure):
        _fields_ = [("nLength", wintypes.DWORD), ("lpSecurityDescriptor", ctypes.c_void_p), ("bInheritHandle", wintypes.BOOL)]

    advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW.argtypes = [
        wintypes.LPCWSTR, wintypes.DWORD, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.ULONG)]
    kernel32.CreateDirectoryW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(SECURITY_ATTRIBUTES)]
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    # Accès complet pour ce compte, SYSTEM et les administrateurs, rien d'hérité de ProgramData (« P »)
    sddl = f"D:P(A;OICI;FA;;;{sid})(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)"
    sd = ctypes.c_void_p()
    if not advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, ctypes.byref(sd), None):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        sa = SECURITY_ATTRIBUTES(ctypes.sizeof(SECURITY_ATTRIBUTES), sd, False)
        if not kernel32.CreateDirectoryW(str(path), ctypes.byref(sa)):
            ERROR_ALREADY_EXISTS = 183
            if ctypes.get_last_error() != ERROR_ALREADY_EXISTS:
                raise ctypes.WinError(ctypes.get_last_error())
    finally:
        kernel32.LocalFree(sd)
    st = os.lstat(path)  # sans suivre une jonction ou un lien posé à sa place
    if not stat.S_ISDIR(st.st_mode) or st.st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
        return False
    return _owner_sid(path) in (sid, "S-1-5-32-544", "S-1-5-18")  # ce compte, Administrateurs, SYSTEM


def _espeak_data() -> str:
    """Dossier des données d'espeak (la phonétique de Kokoro).
    La DLL d'espeak lit ce chemin dans la page de code ANSI : avec des accents (C:\\Users\\Jérôme),
    elle quitte tout le processus sans exception. On en garde alors une copie au chemin ASCII
    C:\\ProgramData\\AIRaceEngineer-<SID du compte>, un dossier que les autres comptes du PC ne peuvent ni lire ni
    modifier : espeak lit ces fichiers dans le processus du client, ils ne doivent venir que de nous."""
    import espeakng_loader
    src = espeakng_loader.get_data_path()
    if src.isascii():
        return src
    ver = importlib.metadata.version("espeakng-loader")  # les données doivent aller avec la DLL
    try:
        sid = _user_sid()
        root = Path(os.environ.get("ProgramData") or r"C:\ProgramData") / f"AIRaceEngineer-{sid}"
        if str(root).isascii() and _private_dir(root, sid):
            dst = root / f"espeak-ng-data-{ver}"
            if not (dst / "phontab").is_file():
                tmp = root / f"{dst.name}.{uuid.uuid4().hex[:8]}.part"
                shutil.copytree(src, tmp)
                try:
                    tmp.rename(dst)  # d'un coup : jamais de copie à moitié faite
                except OSError:
                    shutil.rmtree(tmp, ignore_errors=True)  # un autre processus l'a faite avant
            if (dst / "phontab").is_file():
                return str(dst)
    except OSError:
        pass
    # espeak quitterait tout le processus : une exception, elle, est rattrapée et affichée
    raise EspeakPathError(_t("voix locale indisponible : le chemin du profil Windows contient des accents",
                             "local voice unavailable: the Windows profile path has non-ASCII characters"))


def check_espeak():
    """Essai de la phonétique, lancé dans un processus à part par engineer.py --prepare :
    si espeak échoue, il quitte le processus au lieu de lever une exception."""
    from kokoro_onnx.config import EspeakConfig
    from kokoro_onnx.tokenizer import Tokenizer
    Tokenizer(EspeakConfig(data_path=_espeak_data())).phonemize("ok", "fr-fr")


class KokoroVoice:
    def __init__(self, cfg: dict):
        for name, url in KOKORO_FILES.items():
            if not (MODELS_DIR / name).exists():
                _download(url, MODELS_DIR / name)
        data = _espeak_data()
        import onnxruntime as rt
        from kokoro_onnx import Kokoro
        from kokoro_onnx.config import EspeakConfig
        # Même règle que Whisper (stt.py) : la moitié des cœurs au plus (4 au maximum), sans attente active.
        # Le reste est pour le jeu.
        so = rt.SessionOptions()
        so.intra_op_num_threads = max(1, min(4, (os.cpu_count() or 2) // 2))
        so.inter_op_num_threads = 1
        so.add_session_config_entry("session.intra_op.allow_spinning", "0")
        session = rt.InferenceSession(str(MODELS_DIR / "kokoro-v1.0.onnx"), so, providers=["CPUExecutionProvider"])
        self.kokoro = Kokoro.from_session(session, str(MODELS_DIR / "voices-v1.0.bin"),
                                          espeak_config=EspeakConfig(data_path=data))
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
        # Case Voice ID vidée : la voix par défaut plutôt qu'une adresse invalide
        self.voice_id = cfg.get("voice_id") or "JBFqnCBsd6RMkjVDRZzb"
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
        # Délais courts : sur une connexion qui flanche, la voix locale prend vite le relais
        async for chunk in edge_tts.Communicate(text, self.voice, rate=self.rate,
                                                connect_timeout=5, receive_timeout=10).stream():
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
        self._online_after = 0.0  # voix en ligne réessayée à partir de cet instant (time.monotonic)
        # Avertissement en cours (fr, en, code), affiché par engineer.py quand l'ingé se tait
        self.warning: tuple[str, str, str] | None = None
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

    @staticmethod
    def _kokoro_synth(k: KokoroVoice, text: str) -> tuple[np.ndarray, int]:
        return k.synth(spoken_fr(text) if k.lang == "fr-fr" else text)

    def _online_failed(self, e: Exception):
        reason = _reason(e)
        status = e.response.status_code if isinstance(e, httpx.HTTPStatusError) else 0
        if isinstance(self.engine, ElevenLabsVoice) and 400 <= status < 500 and status not in (408, 429):
            # Clé refusée, quota épuisé, Voice ID inconnu : inutile de réessayer avant un changement de réglage
            self._online_after = float("inf")
            self.warning = (f"ElevenLabs a refusé la demande ({reason} : clé, quota ou Voice ID), voix locale utilisée.",
                            f"ElevenLabs refused the request ({reason}: key, quota or Voice ID), using the local voice.", "warn")
            return
        self._online_after = time.monotonic() + ONLINE_RETRY_S
        if isinstance(self.engine, ElevenLabsVoice):
            self.warning = (f"ElevenLabs indisponible ({reason}), voix locale utilisée.",
                            f"ElevenLabs unavailable ({reason}), using the local voice.", "warn")
        else:
            self.warning = (f"Voix en ligne indisponible ({reason}), voix locale utilisée.",
                            f"Online voice unavailable ({reason}), using the local voice.", "warn")

    def _synth(self, text: str) -> tuple[np.ndarray, int]:
        if isinstance(self.engine, KokoroVoice):
            return self._kokoro_synth(self.engine, text)
        if time.monotonic() >= self._online_after:
            try:
                result = self.engine.synth(text)
                self.warning = None
                return result
            except Exception as e:  # pas d'internet, service indisponible, clé refusée : la voix locale prend le relais
                self._online_failed(e)
        return self._kokoro_synth(self._kokoro(), text)

    def render(self, text: str) -> tuple[np.ndarray, int]:
        if not text.strip():
            raise ValueError(_t("texte vide, rien à dire", "empty text, nothing to say"))
        samples, rate = self._synth(text)
        if self.radio:
            samples = audio.radio_fx(samples, rate, track=self.track)
        return samples, rate

    def say(self, text: str):
        samples, rate = self.render(text)
        audio.play(samples, rate)
