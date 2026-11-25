"""Conseils spontanés de l'ingé, comme un ingé de piste : il prend la parole de lui-même quand la télémétrie montre
quelque chose qui se règle (pneus avant bien plus chauds que l'arrière, pressions très inégales...).

Ce qui déclenche un conseil, et le conseil lui-même, sont décidés ici avec des seuils prudents ; l'IA ne fait que le
dire avec ses mots (un petit modèle inventait des chiffres ou raisonnait à l'envers). Au plus un conseil
par intervalle choisi dans la page du plugin, jamais deux fois le même sujet de suite, et seulement en passant la ligne
(pas au milieu d'un virage, pas au stand, pas pendant les premiers tours où les pneus chauffent).
"""
import math
import time

# Écart minimal entre deux conseils, selon le réglage de la page (« off » : jamais)
INTERVAL_S = {"rare": 15 * 60, "normal": 6 * 60}
# Un même sujet ne revient pas avant trois intervalles
SAME_TOPIC_FACTOR = 3
# Tours bouclés depuis le début du suivi avant le premier conseil (pneus à température)
WARMUP_LAPS = 2

# Écarts de température avant / arrière qui valent un conseil, en °C
TYRE_BALANCE_C = 10.0
# Écart de pression entre le pneu le plus gonflé et le moins gonflé, par unité du jeu
PRESSURE_SPREAD = {"psi": 1.5, "bar": 0.1, "kpa": 10.0}


def _avg(*values):
    values = [v for v in values if isinstance(v, (int, float))]
    return sum(values) / len(values) if values else None


def observations(snap: dict) -> list[dict]:
    """Ce qui mérite un conseil dans la télémétrie, du plus utile au moins utile."""
    found = []
    tyres = snap.get("tyres") or {}
    temps = tyres.get("temperature") or {}
    fahrenheit = str((snap.get("session") or {}).get("temp_unit") or "").lower().startswith("f")
    scale = 1.8 if fahrenheit else 1.0
    front, rear = _avg(temps.get("fl"), temps.get("fr")), _avg(temps.get("rl"), temps.get("rr"))
    if front is not None and rear is not None and front > 0 and rear > 0:
        gap = (front - rear) / scale
        if gap >= TYRE_BALANCE_C:
            found.append({"topic": "front_hot",
                          "fr": f"pneus avant bien plus chauds que l'arrière ({front:.0f} contre {rear:.0f} degrés) : l'avant "
                                "travaille plus, la voiture a tendance à sous-virer. Conseil : un demi-point de frein vers "
                                "l'arrière en roulant ; au garage, barre antiroulis avant un peu plus souple.",
                          "en": f"front tyres much hotter than the rears ({front:.0f} vs {rear:.0f} degrees): the front works "
                                "harder, the car tends to understeer. Advice: half a point of brake bias rearward while "
                                "driving; in the garage, a slightly softer front anti-roll bar."})
        elif -gap >= TYRE_BALANCE_C:
            found.append({"topic": "rear_hot",
                          "fr": f"pneus arrière bien plus chauds que l'avant ({rear:.0f} contre {front:.0f} degrés) : l'arrière "
                                "glisse, la voiture a tendance à survirer en sortie. Conseil : un cran de TC en plus si la "
                                "voiture en a ; au garage, barre antiroulis arrière un peu plus souple.",
                          "en": f"rear tyres much hotter than the fronts ({rear:.0f} vs {front:.0f} degrees): the rear slides, "
                                "the car tends to oversteer on exit. Advice: one more click of TC if the car has it; in the "
                                "garage, a slightly softer rear anti-roll bar."})

    pressures = {k: v for k, v in (tyres.get("pressure") or {}).items() if isinstance(v, (int, float)) and v > 0}
    unit = str(tyres.get("pressure_unit") or "").lower()
    limit = PRESSURE_SPREAD.get(unit)
    if limit and len(pressures) == 4:
        high = max(pressures, key=pressures.get)
        low = min(pressures, key=pressures.get)
        if pressures[high] - pressures[low] >= limit:
            names_fr = {"fl": "avant gauche", "fr": "avant droit", "rl": "arrière gauche", "rr": "arrière droit"}
            names_en = {"fl": "front left", "fr": "front right", "rl": "rear left", "rr": "rear right"}
            found.append({"topic": "pressure_spread",
                          "fr": f"pressions inégales : le plus gonflé est l'{names_fr[high]} à {pressures[high]:.1f}, le moins "
                                f"gonflé l'{names_fr[low]} à {pressures[low]:.1f} {unit}. Conseil : au prochain passage au "
                                f"garage, rapproche ces deux pressions.",
                          "en": f"uneven pressures: the highest is the {names_en[high]} at {pressures[high]:.1f}, the lowest "
                                f"the {names_en[low]} at {pressures[low]:.1f} {unit}. Advice: at the next garage visit, bring "
                                f"these two pressures closer."})
    return found


class Coach:
    """Décide quand l'ingé prend la parole de lui-même. check() est appelé souvent ; il ne répond qu'en passant la ligne."""

    def __init__(self):
        self.last_lap = None
        self.laps_seen = 0
        self.last_tip = -math.inf
        self.topics: dict[str, float] = {}
        self.session = None

    def check(self, snap: dict | None, level: str) -> dict | None:
        interval = INTERVAL_S.get(level)
        if not interval or not snap or not snap.get("game_running"):
            return None
        session = snap.get("session") or {}
        key = (snap.get("game"), session.get("track"), session.get("car"), session.get("type"))
        if key != self.session:
            # Autre session, piste ou voiture : on repart de zéro (pneus froids, autres sujets)
            self.session, self.last_lap, self.laps_seen, self.topics = key, None, 0, {}
        me = snap.get("me") or {}
        lap = me.get("lap")
        if not isinstance(lap, int):
            return None
        crossed = self.last_lap is not None and lap > self.last_lap
        self.last_lap = lap
        if not crossed or me.get("in_pit_lane"):
            return None
        self.laps_seen += 1
        now = time.monotonic()
        if self.laps_seen <= WARMUP_LAPS or now - self.last_tip < interval:
            return None
        for obs in observations(snap):
            if now - self.topics.get(obs["topic"], -math.inf) < SAME_TOPIC_FACTOR * interval:
                continue
            self.last_tip = now
            self.topics[obs["topic"]] = now
            return obs
        return None
