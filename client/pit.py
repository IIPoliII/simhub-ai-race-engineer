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

    def absorb(self, answer: str) -> str:
        """Retire la ligne PLAN de la réponse de l'IA et met le programme à jour. Renvoie le texte à dire."""
        m = PLAN_LINE.search(answer)
        if not m:
            return answer.strip()
        try:
            data = json.loads(m.group(1))
        except ValueError:
            data = {}
        with self.lock:
            for key in ("tyres", "fuel", "repair"):
                if key in data:
                    self.plan[key] = data[key]
            fuel, most = self.plan.get("fuel"), self.proposal.get("fill_up")
            if isinstance(fuel, (int, float)) and not isinstance(fuel, bool) and most is not None and fuel > most:
                self.plan["fuel"] = most  # jamais plus que ce que le réservoir peut prendre
        return answer[:m.start()].strip()

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

    def reminder(self) -> str:
        fr = self.language == "fr"
        with self.lock:
            plan = dict(self.plan)
        unit = (self.proposal.get("unit") or "").lower()
        gallons = unit.startswith("gal")
        parts = ["Box, box." if fr else "Box, box."]
        tyres = plan.get("tyres")
        if tyres:
            parts.append(f"Pneus : {tyres}." if fr else f"Tyres: {tyres}.")
        fuel = plan.get("fuel")
        if isinstance(fuel, (int, float)) and not isinstance(fuel, bool):
            n = int(round(fuel))
            if n <= 0:
                parts.append("Pas d'essence." if fr else "No fuel.")
            elif fr:
                parts.append(f"Essence : {n} {'gallons' if gallons else 'litres'}.")
            else:
                parts.append(f"Fuel: {n} {'gallons' if gallons else 'litres'}.")
        if plan.get("repair"):
            parts.append("On répare." if fr else "Repairs on.")
        parts.append("Limiteur !" if fr else "Pit limiter!")
        return " ".join(parts)


def cancelled(language: str) -> str:
    return "Compris, on annule le box, tu restes en piste." if language == "fr" else "Copy, box cancelled, stay out."
