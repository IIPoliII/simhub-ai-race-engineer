"""Bruits de piste derrière la voix : des F1 qui passent devant le muret des stands.

Rien n'est enregistré : chaque voiture est synthétisée à chaque message, avec son régime, ses passages
de rapport, sa vitesse, sa distance au micro et son effet Doppler tirés au hasard. Deux messages ne sonnent
donc jamais pareil, et parfois on n'entend rien du tout, comme à la vraie radio.
"""
import numpy as np

C = 343.0  # vitesse du son, m/s

# Ordres moteur d'un V6 quatre temps : l'ordre 3 (trois explosions par tour) domine
ORDERS = np.array([1.5, 3.0, 4.5, 6.0, 7.5, 9.0, 12.0])
WEIGHTS = np.array([0.3, 1.0, 0.3, 0.35, 0.12, 0.15, 0.05])  # peu d'aigus : moins « synthé »


def _box(x: np.ndarray, width: int) -> np.ndarray:
    """Moyenne glissante centrée, en temps linéaire (somme cumulée) ; les bords reprennent la valeur extrême."""
    if width < 2:
        return x
    left = width // 2
    padded = np.concatenate([np.full(left, x[0]), x, np.full(width - left, x[-1])])
    c = np.concatenate([[0.0], np.cumsum(padded)])
    return (c[width:width + len(x)] - c[:len(x)]) / width


def _noises(rng, n: int, rate: int, *bands):
    """Bruits blancs filtrés dans chaque bande (lo, hi), tirés directement dans le spectre.
    Longueur en puissance de 2 : une FFT de taille quelconque (voire première) est jusqu'à 10 fois plus lente."""
    m = 1 << (n - 1).bit_length()
    f = np.fft.rfftfreq(m, 1 / rate)
    spec = (rng.normal(0, 1, len(f)) + 1j * rng.normal(0, 1, len(f))) * np.sqrt(m / 2)
    return [np.fft.irfft(spec * ((f > lo) & (f < hi)), m)[:n] for lo, hi in bands]


def _rpm(rng, t: np.ndarray, rate: int) -> np.ndarray:
    """Régime en dents de scie : il monte puis chute au rapport supérieur, ou descend avec des coups de gaz au rétrogradage."""
    low, high = rng.uniform(10300, 10900), rng.uniform(11700, 12100)
    if rng.random() < 0.8:
        # pleine accélération dans la ligne droite, sur les grands rapports : un passage toutes les 2 à 4 s
        speed = rng.uniform(350, 700)
        saw = (t * speed + rng.uniform(0, high - low)) % (high - low)
        rpm = low + saw
    else:
        # freinage : le régime tombe, chaque rétrogradage le fait remonter d'un coup
        speed = rng.uniform(1200, 2000)
        saw = (t * speed + rng.uniform(0, high - low)) % (high - low)
        rpm = high - saw
    # un passage de rapport dure quelques centièmes, pas zéro
    return _box(rpm, int(rate * 0.06))


def _car(rng, n: int, rate: int, t0: float, far=False) -> np.ndarray:
    """Une voiture qui passe au plus près du micro à l'instant t0 (en secondes)."""
    f32 = np.float32
    t = np.arange(n) / rate
    v = rng.uniform(55, 90)                                           # m/s, ~200 à 320 km/h
    d = rng.uniform(150, 400) if far else rng.uniform(18, 60)         # distance au micro au plus près
    x = v * (t - t0)
    r = np.sqrt(x * x + d * d)
    cos = x / r
    doppler = C / (C + v * cos)                                       # plus aigu en arrivant, plus grave en repartant

    # La phase du vilebrequin s'accumule en double précision, le reste se calcule en float32 (deux fois plus vite).
    # Tous les ordres sont des multiples de 0,5 : modulo 4π, leurs phases restent exactes.
    crank = np.mod(np.cumsum(2 * np.pi * _rpm(rng, t, rate) / 60 * doppler / rate), 4 * np.pi).astype(f32)
    r = r.astype(f32)
    rough = (1 + 0.08 * _box(rng.normal(0, 1, n), int(rate / 120))).astype(f32)  # explosions jamais identiques
    weights = WEIGHTS * rng.uniform(0.6, 1.4, len(WEIGHTS))           # chaque moteur a son timbre

    sig = np.zeros(n, f32)
    base_hz = 11000 / 60 * 1.3
    step = np.exp(-1.5 * r / 600)                                     # l'air étouffe les aigus avec la distance
    air, k = np.ones(n, f32), 0
    for order, w in zip(ORDERS, weights):
        if order * base_hz > rate * 0.45:
            continue
        while k < round(order / 1.5):                                 # atténuation de l'ordre = step ** (ordre / 1,5)
            air *= step
            k += 1
        sig += f32(w) * air * np.sin(f32(order) * crank + f32(rng.uniform(0, 2 * np.pi)))
    sig *= rough

    # le grain de l'échappement : un souffle qui claque à chaque explosion
    pulse = (0.5 + 0.5 * np.sin(3 * crank)) ** 2
    exhaust, wind = _noises(rng, n, rate, (400, 2500), (200, 1500))
    sig += f32(0.25) * exhaust.astype(f32) * pulse * np.exp(-r / 250)

    # le souffle de l'air et des pneus, qui enfle au passage : c'est lui qui rend le son naturel
    sig += f32(0.35) * wind.astype(f32) * np.exp(-r / 120)

    # sifflement du turbo et du moteur électrique, très discret
    whine = np.mod(np.cumsum(2 * np.pi * rng.uniform(2200, 3000) * doppler / rate), 2 * np.pi).astype(f32)
    sig += f32(0.02) * np.exp(-r / 80) * np.sin(whine)

    level = (12 / r * (1 + 0.2 * cos)).astype(f32)                    # l'échappement est à l'arrière
    return sig * level


def track(n: int, rate: int, rng=None) -> np.ndarray:
    """Une scène de piste de n échantillons : des passages au hasard (voitures seules ou en groupe), parfois une au loin."""
    rng = rng or np.random.default_rng()
    dur = n / rate
    # la radio coupe au-dessus de 3,4 kHz : on synthétise vers 8 kHz (3 fois moins de calcul en 24 kHz)
    full_n, sub = n, max(1, rate // 8000)
    n, rate = n // sub + 2, rate // sub
    out = np.zeros(n)

    # Une suite de passages au hasard : une voiture seule puis tout un groupe, l'inverse, plusieurs, ou rien.
    # Chaque message tire sa propre densité de trafic : certains sont calmes, d'autres très chargés.
    traffic = rng.choice([0.0, rng.uniform(0.1, 0.3), rng.uniform(0.3, 0.7)], p=[0.2, 0.45, 0.35])
    cars = 0
    for _ in range(rng.poisson(traffic * (dur + 0.6))):
        if cars >= 8:                                                 # au-delà, ce n'est plus qu'un brouhaha (et du calcul)
            break
        t0 = rng.uniform(-0.3, dur + 0.3)
        if rng.random() < 0.6:
            out += _car(rng, n, rate, t0)                             # une voiture seule
            cars += 1
        else:
            # un groupe : côte à côte en pleine bagarre, ou en file dans le DRS
            for _ in range(rng.integers(2, 6)):
                out += 0.8 * _car(rng, n, rate, t0)
                t0 += rng.uniform(0.03, 0.5)
                cars += 1
    if rng.random() < 0.35:
        out += _car(rng, n, rate, rng.uniform(0, dur), far=True)

    # un peloton reste un bruit de fond : on arrondit les crêtes au lieu de les additionner
    out = 1.6 * np.tanh(out / 1.6)

    fade = min(n // 2, int(rate * 0.05))
    if fade:
        ramp = np.linspace(0, 1, fade)
        out[:fade] *= ramp
        out[-fade:] *= ramp[::-1]
    return np.interp(np.arange(full_n) / sub, np.arange(n), out) if sub > 1 else out


def duck(voice: np.ndarray, rate: int, depth=0.45) -> np.ndarray:
    """Gain du fond : baissé pendant que l'ingé parle, pour que sa voix reste parfaitement lisible.
    Il reste baissé entre les mots (fenêtre de 0,5 s) : sinon le fond remonte à chaque pause et le son paraît haché."""
    env = _box(np.abs(voice), int(rate * 0.04))
    active = np.clip(env / (0.25 * env.max() + 1e-9), 0, 1)
    held = np.clip(_box(active, int(rate * 0.5)) * 3, 0, 1)
    return 1 - depth * _box(held, int(rate * 0.3))
