"""Réglages demandés à la voix : répartition de freinage, ABS, TC, essence pour N tours.

Reconnus ici par des expressions simples (pas par l'IA) : une commande comprise de travers ne doit jamais toucher la
voiture. Le plugin fait ensuite le réglage dans le jeu (touches du jeu, ou menu des stands d'iRacing).
"""
import math
import re

# Jeux dont le plugin sait remplir le menu des stands (nom du jeu dans SimHub). Les autres (ACC, AMS2, LMU...) n'ont
# pas de moyen sûr de le faire : l'ingé dit ce qu'il faut régler.
PIT_MENU_GAMES = {"IRacing"}

_WORDS = {"un": 1, "une": 1, "deux": 2, "trois": 3, "quatre": 4, "cinq": 5, "six": 6, "sept": 7, "huit": 8, "neuf": 9,
          "dix": 10, "dits": 10, "onze": 11, "douze": 12, "quinze": 15, "vingt": 20, "trente": 30,
          "one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "seven": 7, "eight": 8, "nine": 9, "ten": 10,
          "eleven": 11, "twelve": 12, "fifteen": 15, "twenty": 20, "thirty": 30}
_NUM = r"(\d{1,3}(?:[.,]\d{1,2})?|" + "|".join(sorted((w.strip() for w in _WORDS), key=len, reverse=True)) + r")"


def _number(word: str) -> float | None:
    word = word.lower().strip()
    if word in _WORDS:
        return float(_WORDS[word])
    try:
        return float(word.replace(",", "."))
    except ValueError:
        return None


# Whisper entend « brake balance » en « brick balance », « break bias »...
_BB = r"(?:br[ae]ke?\s*(?:bias|balance)|break\s*(?:bias|balance)|brick\s*(?:bias|balance)|bb|r[ée]partition(?:\s+de\s+freinage)?" \
      r"|balance\s+(?:de\s+|des\s+)?freins?|frein(?:age)?)"
_ABS = r"(?:abs|a\s*b\s*s|anti-?blocage)"
_TC = r"(?:tc|t\s*c|traction(?:\s+control)?|contr[ôo]le\s+de\s+traction|anti-?patinage|antipatinage)"
_SET = r"(?:mets|met|mettre|règle|régler|passe|passer|set|put|change|à|a|at|to|sur|en)"

_UP = re.compile(r"\b(?:plus|monte|augmente|ajoute|up|more|increase|raise)\b", re.I)
_DOWN = re.compile(r"\b(?:moins|baisse|descend|diminue|enlève|retire|down|less|decrease|lower|reduce)\b", re.I)
_FRONT = re.compile(r"\b(?:avant|devant|forward|front|vers\s+l'avant)\b", re.I)
_REAR = re.compile(r"\b(?:arrière|derrière|rear|back|rearward|vers\s+l'arrière)\b", re.I)
_CLICKS = re.compile(_NUM + r"\s+(?:cran|crans|clic|clics|click|clicks|step|steps|point|points|niveau|niveaux)\b", re.I)
_ONE_CLICK = re.compile(r"\b(?:un\s+cran|un\s+clic|un\s+point|a\s+click|one\s+click|a\s+notch|one\s+notch|a\s+step)\b", re.I)

# « brake balance 60 avant 40 arrière », « répartition à 56,5 », « frein à 58 % vers l'avant »
_BB_VALUE = re.compile(_BB + r"\D{0,25}?(\d{2}(?:[.,]\d{1,2})?)\s*(?:%|pour\s*cent|percent)?\s*(avant|arrière|front|rear)?", re.I)
_ABS_VALUE = re.compile(_ABS + r"\s*(?:" + _SET + r"\s*)?(?:niveau\s*|level\s*)?" + _NUM + r"\b(?!\s*(?:cran|crans|clic|clics|click|clicks|step|steps|tours?|laps?))", re.I)
_ABS_VALUE_BEFORE = re.compile(r"\b(?:niveau|level)\s*" + _NUM + r"\s*(?:d'|de\s+|of\s+)?" + _ABS, re.I)
_TC_VALUE = re.compile(_TC + r"\s*(?:" + _SET + r"\s*)?(?:niveau\s*|level\s*)?" + _NUM + r"\b(?!\s*(?:cran|crans|clic|clics|click|clicks|step|steps|tours?|laps?))", re.I)
_TC_VALUE_BEFORE = re.compile(r"\b(?:niveau|level)\s*" + _NUM + r"\s*(?:d'|de\s+|of\s+)?" + _TC, re.I)

# « essence pour dix tours », « fuel for 10 laps »
_FUEL_LAPS = re.compile(r"\b(?:essence|carburant|fuel|gas)\b.{0,25}?\b(?:pour|for)\s+" + _NUM + r"\s+(?:tours?|laps?)\b", re.I)


def _clicks(text: str) -> int:
    m = _CLICKS.search(text)
    if m:
        n = _number(m.group(1))
        if n and 0 < n <= 20:
            return int(n)
    return 1


# Question ou demande de conseil (« j'ai le frein qui bloque à l'avant, tu proposes quoi ? ») : pour l'IA, on ne touche à rien
_ASKING = re.compile(r"\?|\b(?:qu'est-ce|quoi|quel|quelle|propose\w*|conseil\w*|pourquoi|comment|devrais|faudrait"
                     r"|penses?|bloquent|bloque|sous-vir\w*|survir\w*|what|which|should|why|how|suggest|think"
                     r"|locking|lock)\b", re.I)


def parse(question: str) -> dict | None:
    """La commande dite par le pilote, ou None. {"kind": "bb"|"abs"|"tc", "target": x} ou {"kind", "steps": ±n},
    ou {"kind": "fuel_laps", "laps": n}."""
    q = question.strip()
    if _ASKING.search(q):
        return None

    m = _FUEL_LAPS.search(q)
    if m:
        laps = _number(m.group(1))
        if laps and 0 < laps <= 200:
            return {"kind": "fuel_laps", "laps": int(laps)}

    if re.search(r"\b" + _BB + r"\b", q, re.I):
        m = _BB_VALUE.search(q)
        if m:
            value = _number(m.group(1))
            # « 40 arrière » : la valeur dite est l'arrière
            if value is not None and m.group(2) and m.group(2).lower() in ("arrière", "rear"):
                value = 100 - value
            if value is not None and 30 <= value <= 80:
                return {"kind": "bb", "target": value}
        # « un cran de frein vers l'avant », « brake bias one click rearward »
        sign = 1 if _FRONT.search(q) else -1 if _REAR.search(q) else 0
        if sign:
            return {"kind": "bb", "steps": sign * _clicks(q)}

    for kind, word, value, before in (("abs", _ABS, _ABS_VALUE, _ABS_VALUE_BEFORE), ("tc", _TC, _TC_VALUE, _TC_VALUE_BEFORE)):
        if not re.search(r"\b" + word + r"\b", q, re.I):
            continue
        sign = 1 if _UP.search(q) else -1 if _DOWN.search(q) else 0
        if sign and (_CLICKS.search(q) or _ONE_CLICK.search(q) or not value.search(q)):
            return {"kind": kind, "steps": sign * _clicks(q)}
        m = value.search(q) or before.search(q)
        if m:
            n = _number(m.group(1))
            if n is not None and 0 <= n <= 20 and n == int(n):
                return {"kind": kind, "target": int(n)}
        if sign:
            return {"kind": kind, "steps": sign}
    return None


def fuel_for_laps(snap: dict, laps: int) -> dict:
    """Essence à mettre pour rouler « laps » tours (un tour de marge), d'après la conso des tours récents."""
    fuel = snap.get("fuel") or {}
    used = [lap["fuel_used"] for lap in snap.get("recent_laps") or [] if lap.get("fuel_used")]
    per_lap = sum(used) / len(used) if used else None
    level = fuel.get("level")
    total = math.ceil((laps + 1) * per_lap) if per_lap else None
    to_add = max(0, math.ceil(total - level)) if total is not None and level is not None else None
    capacity = fuel.get("capacity")
    if to_add is not None and capacity and level is not None:
        to_add = min(to_add, math.floor(capacity - level))
    return {"per_lap": per_lap, "total": total, "to_add": to_add, "unit": fuel.get("unit")}


def _say_number(x: float, fr: bool) -> str:
    if abs(x - round(x)) < 0.05:
        return str(int(round(x)))
    s = f"{x:.1f}"
    return s.replace(".", ",") if fr else s


_NAMES = {"bb": ("répartition de freinage", "brake bias"), "abs": ("ABS", "ABS"), "tc": ("TC", "TC")}


def spoken(cmd: dict, reply: dict | None, language: str) -> str:
    """Phrase de l'ingé après la réponse du plugin à /control."""
    fr = language == "fr"
    name = _NAMES[cmd["kind"]][0 if fr else 1]
    result = (reply or {}).get("result")
    value = (reply or {}).get("value")
    game = (reply or {}).get("game") or ""

    def val(v):
        if v is None:
            return "?"
        return _say_number(v, fr) + (" %" if cmd["kind"] == "bb" else "")

    if result in ("done", "limit"):
        tail = (" C'est le maximum." if fr else " That's the limit.") if result == "limit" else ""
        return (f"C'est fait, {name} à {val(value)}." if fr else f"Done, {name} {val(value)}.") + tail
    if result == "already":
        return f"Tu y es déjà : {name} à {val(value)}." if fr else f"Already there: {name} {val(value)}."
    if result == "disabled":
        return ("Les réglages à la voix sont coupés. Active-les dans la page du plugin dans SimHub." if fr
                else "Voice adjustments are off. Turn them on in the plugin page in SimHub.")
    if result == "no_keys":
        return (f"Je n'ai pas de rôle Control Mapper ni de touche pour le réglage {name}. Regarde la page du plugin." if fr
                else f"I have no Control Mapper role or key for {name}. Check the plugin page.")
    if result == "no_value":
        return (f"Le jeu ne me donne pas la valeur {name}, je ne peux pas le régler." if fr
                else f"The game doesn't give me the {name} value, I can't adjust it.")
    if result == "no_effect":
        # ABS ou TC à zéro qui ne bouge pas : la voiture n'en a pas (formules, voitures anciennes), pas une panne de bouton
        if cmd["kind"] in ("abs", "tc") and value is not None and abs(value) < 0.01:
            de = "d'" if name[0] in "AEIOU" else "de "   # « pas d'ABS », « pas de TC »
            return (f"Pas {de}{name} sur cette voiture, il reste à zéro." if fr
                    else f"No {name} on this car, it stays at zero.")
        if str((reply or {}).get("via", "")).startswith("mapper"):
            return (f"J'ai appuyé mais {name} ne bouge pas. Vérifie que le rôle est relié à ce jeu dans le Control Mapper." if fr
                    else f"I pressed but {name} doesn't move. Check the role is linked to this game in the Control Mapper.")
        return (f"J'ai appuyé mais {name} ne bouge pas. Vérifie les touches dans le jeu et dans SimHub." if fr
                else f"I pressed the keys but {name} doesn't move. Check the keys in the game and in SimHub.")
    if result == "not_focused":
        return ("Le jeu n'est pas au premier plan, je n'ai rien touché." if fr
                else "The game isn't in front, I didn't touch anything.")
    if result == "no_game":
        return "Je ne reçois rien du jeu pour l'instant." if fr else "Nothing from the game yet."
    return "Je n'ai pas pu faire le réglage." if fr else "I couldn't make the adjustment."


def spoken_fuel(laps: int, plan: dict, reply: dict | None, language: str) -> str:
    """Essence pour N tours : mise dans le menu des stands (iRacing) ou quantité à régler soi-même."""
    fr = language == "fr"
    if plan["to_add"] is None:
        return (f"Je n'ai pas encore de tour complet pour calculer l'essence de {laps} tours." if fr
                else f"No complete lap yet to work out the fuel for {laps} laps.")
    unit = "gallons" if (plan.get("unit") or "").lower().startswith("gal") else "litres"
    amount = plan["to_add"]
    if amount <= 0:
        return (f"Tu as déjà assez d'essence pour {laps} tours, pas besoin d'en rajouter." if fr
                else f"You already have enough fuel for {laps} laps, no need to add any.")
    if (reply or {}).get("result") == "done":
        return (f"C'est réglé : {amount} {unit} au prochain arrêt, pour {laps} tours." if fr
                else f"Set: {amount} {unit} at the next stop, for {laps} laps.")
    return (f"Pour {laps} tours, il te faut {plan['total']} {unit} en tout : rajoute {amount} {unit}." if fr
            else f"For {laps} laps you need {plan['total']} {unit} in total: add {amount} {unit}.")
