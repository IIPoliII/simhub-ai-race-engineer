"""« Box box » : préparation de l'arrêt au stand, avec tous les jeux.

Le pilote annonce qu'il rentre ; l'ingé propose l'essence (calculée ici, pas par l'IA), demande les pneus,
puis rappelle le programme à l'entrée des stands. Rien n'est réglé dans le jeu : le pilote fait les réglages.
"""
import json
import math
import re
import threading

# Ce que Whisper entend pour « box box » / « pit pit » : « Box, box. », « Boxe boxe », « Boxbox », « Bix, Bix »,
# « Pete, Pete », « p't p't », « Box tour »...
_BOX = r"(?:box|boxe|boxes|bocks?|boks|bix|bax|pit|pits|pitt|pete|bit|p't)"
BOX_CALL = re.compile(
    rf"\b{_BOX}\b[\s,.!-]*\b{_BOX}(?!\w)"
    r"|\bboxbox\b"
    rf"|\bbox[\s,]+(?:ce\s+|cette\s+|c'est\s+|this\s+)?(?:tour|lap)\b"
    r"|\b(?:je|on)\s+(?:rentre|rentrons|s'arrête|m'arrête)\b(?:\s+(?:au|aux)\s+stands?)?"
    r"|\bpit(?:ting)?\s+(?:this\s+lap|now|in)\b|\bboxing\b|\bwe(?:'re| are)\s+pitting\b",
    re.I)
CANCEL = re.compile(
    r"\bannul\w*\b.*\b(?:box|boxe|arrêt|stand)\b|\bon\s+reste\s+(?:dehors|en\s+piste)\b|\bpas\s+de\s+box\b"
    r"|\bcancel\b.*\b(?:box|stop|pit)\b|\bstay(?:ing)?\s+out\b|\bno\s+(?:box|stop)\b",
    re.I)
# Pneus mal entendus par Whisper (small) pendant un arrêt : « hard » à la française devient « arbre »,
# « mets des hards » devient « Mediards », « mediums neufs » devient « medium 9 »...
TYRE_FIXES = [
    (re.compile(r"\bm[eé]d(?:i|e|es)\s?(softs?|hards?)\b", re.I), r"mets des \1"),
    (re.compile(r"\b(?:mediards?|mediar|medeards?)\b", re.I), "mets des hards"),
    (re.compile(r"\barbres?\b", re.I), "hard"),
    (re.compile(r"\bards\b", re.I), "hards"),
    (re.compile(r"\b(mediums?|softs?|hards?|slicks?|pluie|inters?)\s+9\b", re.I), r"\1 neufs"),
]


def fix_tyre_words(question: str) -> str:
    for pattern, repl in TYRE_FIXES:
        question = pattern.sub(repl, question)
    return question


def normalize_call(question: str) -> str:
    """Remplace l'appel mal transcrit (« Bix, bix », « p't p't ») par « Box, box ! ». Ce qui précède l'appel est
    écarté (souvent du bruit : « on va tester »), ce qui suit est gardé (« box box, mets des softs »)."""
    m = BOX_CALL.search(question)
    if not m:
        return question
    rest = question[m.end():].strip(" ,.!?")
    return "Box, box !" + (" " + rest if rest else "")


# Ligne de programme que l'IA ajoute à la fin de sa réponse en mode arrêt
PLAN_LINE = re.compile(r"\s*PLAN\s*:?\s*(\{.*?\})\s*$", re.S)

# Marge : un tour de plus que le strict nécessaire
MARGIN_LAPS = 1.0
# Sans arrêt deux tours après l'appel, le programme est abandonné
EXPIRE_LAPS = 2


def _seconds(t: str | None) -> float | None:
    """« 1:47.300 » ou « 1:02:03 » (format du plugin) en secondes."""
    if not t:
        return None
    try:
        parts = [float(p) for p in t.split(":")]
    except ValueError:
        return None
    s = 0.0
    for p in parts:
        s = s * 60 + p
    return s if s > 0 else None


def fuel_proposal(snap: dict) -> dict:
    """Essence à remettre pour finir la course, avec un tour de marge. Champs à null quand on ne peut pas savoir."""
    fuel = snap.get("fuel") or {}
    me = snap.get("me") or {}
    session = snap.get("session") or {}
    used = [lap["fuel_used"] for lap in snap.get("recent_laps") or [] if lap.get("fuel_used")]
    per_lap = sum(used) / len(used) if used else None
    level = fuel.get("level")
    capacity = fuel.get("capacity")

    laps_to_go = session.get("remaining_laps")
    if laps_to_go is None:
        left = _seconds(session.get("time_left"))
        lap_time = _seconds(me.get("last_lap")) or _seconds(me.get("best_lap"))
        if left and lap_time:
            laps_to_go = math.ceil(left / lap_time) + 1  # le tour en cours au drapeau se finit

    to_add = None
    if per_lap and laps_to_go is not None and level is not None:
        to_add = max(0.0, (laps_to_go + MARGIN_LAPS) * per_lap - level)
        if capacity:
            # Au stand, le réservoir a déjà baissé d'environ un demi-tour
            to_add = min(to_add, max(0.0, capacity - max(0.0, level - per_lap / 2)))
        to_add = math.ceil(to_add)
    # « Le plein » : ce qui rentre au stand, le réservoir ayant baissé d'environ un demi-tour
    fill_up = None
    if capacity and level is not None:
        fill_up = math.floor(max(0.0, capacity - max(0.0, level - (per_lap or 0) / 2)))
    return {
        "unit": fuel.get("unit"),
        "level": level,
        "per_lap": round(per_lap, 2) if per_lap else None,
        "laps_to_go": laps_to_go,
        "fuel_to_add": to_add,
        "fill_up": fill_up,
    }


_NUMBER_WORDS = {"dix": 10, "quinze": 15, "vingt": 20, "vingt-cinq": 25, "trente": 30, "quarante": 40, "cinquante": 50,
                 "soixante": 60, "septante": 70, "huitante": 80, "nonante": 90, "cent": 100, "ten": 10, "fifteen": 15,
                 "twenty": 20, "thirty": 30, "forty": 40, "fifty": 50, "sixty": 60, "seventy": 70, "eighty": 80,
                 "ninety": 90, "hundred": 100}
_FUEL = re.compile(r"\b(\d{1,3}|" + "|".join(sorted(_NUMBER_WORDS, key=len, reverse=True)) + r")\s*(?:litres?|liters?|l|gallons?)\b", re.I)
_FILL = re.compile(r"\b(?:le plein|plein d'essence|fill(?: it)? up|full tank)\b", re.I)
_TYRE = re.compile(r"\b(softs?|mediums?|hards?|pluie|wets?|inters?|intermédiaires?|slicks?)\b", re.I)
_NEW = re.compile(r"\b(?:neufs?|new|fresh)\b", re.I)


def from_driver(question: str, fill_up) -> dict:
    """Essence et pneus dits par le pilote, quand l'IA n'a pas renvoyé son programme."""
    data = {}
    m = _FUEL.search(question)
    if m:
        word = m.group(1).lower()
        data["fuel"] = int(word) if word.isdigit() else _NUMBER_WORDS[word]
    elif _FILL.search(question) and fill_up is not None:
        data["fuel"] = fill_up
    t = _TYRE.search(question)
    if t:
        data["tyres"] = t.group(1).lower() + (" neufs" if _NEW.search(question) else "")
    return data


class PitPlan:
    """Programme d'arrêt en cours. Partagé entre le fil de travail (questions) et la boucle principale (stands)."""

    def __init__(self, snap: dict | None, language: str):
        snap = snap or {}
        me = snap.get("me") or {}
        self.language = language
        self.lock = threading.Lock()
        self.start_lap = me.get("lap")
        self.in_pit = bool(me.get("in_pit_lane"))
        self.proposal = fuel_proposal(snap)
        self.proposal["damage_pct"] = (snap.get("car") or {}).get("damage_avg_pct")
        self.proposal["compound_now"] = (snap.get("tyres") or {}).get("compound")
        # null : pas encore décidé ; le rappel au stand ne dit que ce qui est décidé
        self.plan = {"tyres": None, "fuel": self.proposal["fuel_to_add"], "repair": None}
        self.reminded = False

    def context(self) -> dict:
        with self.lock:
            return {"proposal": self.proposal, "plan": dict(self.plan)}

    def absorb(self, answer: str, question: str = "") -> str:
        """Retire la ligne PLAN de la réponse de l'IA et met le programme à jour. Renvoie le texte à dire.
        Sans ligne PLAN (les petits modèles l'oublient parfois), on lit l'essence et les pneus dans la phrase du pilote."""
        m = PLAN_LINE.search(answer)
        if m:
            try:
                data = json.loads(m.group(1))
            except ValueError:
                data = {}
            spoken = answer[:m.start()].strip()
            # Les mots du pilote passent avant le résumé de l'IA (« mode hard » ne doit pas devenir « neufs »)
            data.update(from_driver(question, self.proposal.get("fill_up")))
        else:
            data = from_driver(question, self.proposal.get("fill_up"))
            spoken = answer.strip()
        with self.lock:
            for key in ("tyres", "fuel", "repair"):
                if key in data:
                    self.plan[key] = data[key]
            fuel, most = self.plan.get("fuel"), self.proposal.get("fill_up")
            if isinstance(fuel, (int, float)) and not isinstance(fuel, bool) and most is not None and fuel > most:
                self.plan["fuel"] = most  # jamais plus que ce que le réservoir peut prendre
        return spoken

    def watch(self, snap: dict | None) -> tuple[str | None, bool]:
        """Appelé chaque seconde : (texte à dire ou None, programme terminé)."""
        if not snap or not snap.get("game_running"):
            return None, False
        me = snap.get("me") or {}
        in_pit = bool(me.get("in_pit_lane"))
        was = self.in_pit
        self.in_pit = in_pit
        if in_pit and not was and not self.reminded:
            self.reminded = True
            return self.reminder(), False
        if was and not in_pit and self.reminded:
            return None, True  # sorti des stands : arrêt fait
        lap = me.get("lap")
        if not self.reminded and isinstance(lap, int) and isinstance(self.start_lap, int) \
                and lap >= self.start_lap + EXPIRE_LAPS:
            return (("Pas d'arrêt : j'annule le box." if self.language == "fr"
                     else "No stop: box cancelled."), True)
        return None, False

    def summary(self) -> str:
        """Récapitulatif quand l'IA n'a renvoyé que la ligne PLAN."""
        fr = self.language == "fr"
        items = self._items()
        if not items:
            return "Compris. Quels pneus ?" if fr else "Copy. Which tyres?"
        return ("Compris. " if fr else "Copy. ") + " ".join(items)

    def reminder(self) -> str:
        fr = self.language == "fr"
        return " ".join(["Box, box."] + self._items() + ["Limiteur !" if fr else "Pit limiter!"])

    def _items(self) -> list[str]:
        """Ce qui est décidé : pneus, essence, réparation (rien sur ce qui reste à choisir)."""
        fr = self.language == "fr"
        with self.lock:
            plan = dict(self.plan)
        gallons = (self.proposal.get("unit") or "").lower().startswith("gal")
        items = []
        tyres = plan.get("tyres")
        if tyres:
            items.append(f"Pneus : {tyres}." if fr else f"Tyres: {tyres}.")
        fuel = plan.get("fuel")
        if isinstance(fuel, (int, float)) and not isinstance(fuel, bool):
            n = int(round(fuel))
            unit = "gallons" if gallons else "litres"
            if n <= 0:
                items.append("Pas d'essence." if fr else "No fuel.")
            else:
                items.append(f"Essence : {n} {unit}." if fr else f"Fuel: {n} {unit}.")
        if plan.get("repair"):
            items.append("On répare." if fr else "Repairs on.")
        return items


def cancelled(language: str) -> str:
    return "Compris, on annule le box, tu restes en piste." if language == "fr" else "Copy, box cancelled, stay out."
