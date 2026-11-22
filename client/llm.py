"""L'ingénieur : envoie la question + la télémétrie à un LLM compatible OpenAI."""
import json
from collections import deque

from openai import OpenAI

SYSTEM = {
    "fr": """Tu es l'ingénieur de course du pilote. Tu lui parles à la radio pendant qu'il roule.
- Réponds en français, en une ou deux phrases courtes, ton calme et pro de radio. Tutoie-le.
- Utilise uniquement la télémétrie fournie. Si une donnée est absente (null), dis-le simplement, n'invente rien.
- Le pilote parle vite et approximativement : interprète l'intention (« le mec devant » = car_ahead, « derrière » = car_behind).
- Arrondis : écarts au dixième, températures et pressions à l'unité ou au dixième, carburant en litres et en tours.
- Écris les nombres comme on les dit à la radio : « un virgule quatre seconde », temps au tour « une minute quarante-sept trois ».
- Pneus : fl = avant gauche, fr = avant droit, rl = arrière gauche, rr = arrière droit.
- Pas de markdown, pas de liste, pas d'emoji : ton texte est lu à voix haute.""",
    "en": """You are the driver's race engineer, talking on the radio while they drive.
- Answer in English, one or two short sentences, calm professional radio tone.
- Only use the telemetry provided. If a value is missing (null), say so briefly, never make things up.
- The driver speaks fast and loosely: infer intent ("the guy in front" = car_ahead, "behind" = car_behind).
- Round numbers: gaps to a tenth, temperatures and pressures to one or a tenth, fuel in litres and laps.
- Write numbers the way they are spoken on the radio: "one point four seconds", lap times "one forty-seven three".
- Tyres: fl = front left, fr = front right, rl = rear left, rr = rear right.
- No markdown, lists or emoji: your text is read aloud.""",
}

NO_DATA = {
    "fr": "Pas de télémétrie pour l'instant, vérifie que SimHub tourne avec le plugin.",
    "en": "No telemetry right now, check that SimHub is running with the plugin.",
}


class Engineer:
    def __init__(self, cfg: dict, language: str):
        self.language = language if language in SYSTEM else "en"
        self.client = OpenAI(base_url=cfg["base_url"], api_key=cfg.get("api_key") or "none", timeout=15)
        self.model = cfg["model"]
        self.max_tokens = int(cfg.get("max_tokens", 120))
        self.temperature = float(cfg.get("temperature", 0.3))
        self.history = deque(maxlen=6)  # 3 derniers échanges, pour « et derrière ? »

    def ask(self, question: str, snapshot: dict | None) -> str:
        if not snapshot or not snapshot.get("game_running"):
            return NO_DATA[self.language]

        telemetry = json.dumps(snapshot, ensure_ascii=False, separators=(",", ":"))
        messages = [{"role": "system", "content": SYSTEM[self.language]}]
        messages += list(self.history)
        messages.append({"role": "user", "content": f"TÉLÉMÉTRIE : {telemetry}\n\nPILOTE : {question}"})

        resp = self.client.chat.completions.create(
            model=self.model,
            messages=messages,
            max_tokens=self.max_tokens,
            temperature=self.temperature,
        )
        answer = (resp.choices[0].message.content or "").strip()
        answer = strip_thinking(answer)

        # On garde l'historique sans la télémétrie pour rester léger
        self.history.append({"role": "user", "content": question})
        self.history.append({"role": "assistant", "content": answer})
        return answer


def strip_thinking(text: str) -> str:
    """Certains modèles (Qwen3, DeepSeek...) renvoient un bloc <think>...</think>."""
    if "</think>" in text:
        text = text.split("</think>", 1)[1]
    return text.strip()
