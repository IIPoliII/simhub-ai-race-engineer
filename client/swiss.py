"""Nombres à la suisse romande : septante, huitante, nonante.

Appliqué au texte juste avant la voix, quand le réglage est coché : les voix françaises lisent « 72 » soixante-douze,
et l'IA écrit parfois « soixante-dix » malgré la consigne. On ne touche qu'aux nombres où la Suisse dit autre chose.
"""
import re

UNITS = ["zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
         "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"]
TENS = {2: "vingt", 3: "trente", 4: "quarante", 5: "cinquante", 6: "soixante", 7: "septante", 8: "huitante", 9: "nonante"}


def _below_100(n: int, fem: bool = False) -> str:
    if n < 20:
        return "une" if (n == 1 and fem) else UNITS[n]
    tens, unit = divmod(n, 10)
    word = TENS[tens]
    if unit == 1:
        return word + ("-et-une" if fem else "-et-un")
    return word + ("-" + UNITS[unit] if unit else "")


def _below_1000(n: int, fem: bool = False) -> str:
    hundreds, rest = divmod(n, 100)
    if not hundreds:
        return _below_100(rest, fem)
    head = "cent" if hundreds == 1 else UNITS[hundreds] + "-cent" + ("s" if not rest else "")
    return head + ("-" + _below_100(rest, fem) if rest else "")


def words(n: int, fem: bool = False) -> str:
    """0 à 999 999 en toutes lettres, à la suisse (orthographe rectifiée, avec traits d'union)."""
    thousands, rest = divmod(n, 1000)
    if not thousands:
        return _below_1000(rest, fem)
    head = "mille" if thousands == 1 else _below_1000(thousands) + "-mille"
    return head + ("-" + _below_1000(rest, fem) if rest else "")


def _differs(n: int) -> bool:
    """Vrai si la Suisse ne dit pas ce nombre comme la France (un groupe de trois chiffres finit entre 70 et 99)."""
    while n:
        if 70 <= n % 100 <= 99:
            return True
        n //= 1000
    return False


# Chiffres isolés, avec décimale éventuelle (« 72 », « 71,5 », « 0.75 ») ; pas les chronos « 1:47.3 »
_NUMBER = re.compile(r"(?<![\w:.,])(\d{1,6})(?:([,.])(\d{1,3}))?(?![\w:]|[.,]\d)")

# Positions « P72 » : lues « P soixante-douze »
_POSITION = re.compile(r"\bP(\d{1,3})\b")

_UNIT_WORDS = {"un": 1, "une": 1, "deux": 2, "trois": 3, "quatre": 4, "cinq": 5, "six": 6, "sept": 7, "huit": 8,
               "neuf": 9, "dix": 10, "onze": 11, "douze": 12, "treize": 13, "quatorze": 14, "quinze": 15, "seize": 16,
               "dix-sept": 17, "dix-huit": 18, "dix-neuf": 19}
_SEP = r"[- ]"
_TEEN = r"dix[- ]sept|dix[- ]huit|dix[- ]neuf|onze|douze|treize|quatorze|quinze|seize|dix"
_UNIT = r"une?|deux|trois|quatre|cinq|six|sept|huit|neuf"
_SOIXANTE_DIX = re.compile(rf"\bsoixante{_SEP}(?:et{_SEP})?({_TEEN})\b", re.I)
_QUATRE_VINGT = re.compile(rf"\bquatre{_SEP}vingts?(?:{_SEP}(?:et{_SEP})?({_TEEN}|{_UNIT}))?\b", re.I)


def _unit_value(word: str) -> int:
    return _UNIT_WORDS[re.sub(r"\s+", "-", word.lower())]


def _keep_case(original: str, replacement: str) -> str:
    return replacement[0].upper() + replacement[1:] if original[:1].isupper() else replacement


def _digits(m: re.Match) -> str:
    whole, sep, frac = m.group(1), m.group(2), m.group(3)
    n = int(whole)
    frac_differs = bool(frac) and not frac.startswith("0") and _differs(int(frac))
    if not _differs(n) and not frac_differs:
        return m.group(0)
    text = words(n) if len(whole) == 1 or not whole.startswith("0") else whole
    if frac:
        text += " virgule " + (words(int(frac)) if frac_differs else frac)
    return text


def _soixante_dix(m: re.Match) -> str:
    n = 60 + _unit_value(m.group(1))
    return _keep_case(m.group(0), words(n))


def _quatre_vingt(m: re.Match) -> str:
    extra = m.group(1)
    fem = bool(extra) and extra.lower() == "une"
    n = 80 + (_unit_value(extra) if extra else 0)
    return _keep_case(m.group(0), words(n, fem))


def swiss_fr(text: str) -> str:
    text = _SOIXANTE_DIX.sub(_soixante_dix, text)
    text = _QUATRE_VINGT.sub(_quatre_vingt, text)
    text = _POSITION.sub(lambda m: "P " + words(int(m.group(1))) if _differs(int(m.group(1))) else m.group(0), text)
    return _NUMBER.sub(_digits, text)
