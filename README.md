# AI Race Engineer pour SimHub

Un ingénieur de course vocal pour **tous les jeux supportés par SimHub** (ACC, iRacing, LMU, rFactor 2, AC, F1...).
Tu maintiens un bouton du volant, tu poses ta question comme tu veux (« il est à combien le mec devant ? »,
« mes pneus ça va ? », « j'ai assez d'essence ? »), et l'ingé te répond à la radio.

```
Jeu ──► SimHub ──► plugin AI Race Engineer ──► http://127.0.0.1:8889/snapshot (JSON)
                                                     │
 bouton volant ─► micro ─► Whisper (local) ─► LLM (Infomaniak / OpenAI / Claude / Ollama)
                                                     │
                              haut-parleurs ◄─ voix Kokoro (local) ou ElevenLabs + effet radio
```

- **Plugin SimHub (C#)** : lit la télémétrie déjà unifiée par SimHub et l'expose en JSON en local.
  Un seul code pour tous les jeux.
- **Client (Python)** : push-to-talk, reconnaissance vocale, LLM et voix.

## Ce que ça coûte

| Brique | Par défaut | Coût |
|---|---|---|
| Reconnaissance vocale | Whisper local (faster-whisper) | gratuit |
| Voix | Kokoro local, voix réaliste | gratuit |
| Cerveau (LLM) | Infomaniak, 1M de crédits offerts | quelques centimes par course |
| Option voix premium | ElevenLabs Flash | payant |
| Option LLM 100 % local | Ollama | gratuit, mais partage la carte graphique avec le jeu |

## Installation

### Prérequis

- Windows et [SimHub](https://www.simhubdash.com/)
- [uv](https://docs.astral.sh/uv/) pour le client vocal (il installe tout seul le bon Python) :
  ```powershell
  powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
  ```

### 1. Le plugin SimHub

Télécharge **`Installer-AIRaceEngineer.cmd`** depuis la [dernière release](https://github.com/IIPoliII/simhub-ai-race-engineer/releases/latest)
et double-clique dessus. Tu peux aussi coller cette commande dans PowerShell :

```powershell
irm https://raw.githubusercontent.com/IIPoliII/simhub-ai-race-engineer/main/install.ps1 | iex
```

L'installateur trouve SimHub, télécharge la dernière version du plugin, attend que SimHub soit fermé, puis copie
la DLL. Windows demande les droits admin, car SimHub est installé dans Program Files. Rien à compiler.

Relance SimHub et accepte d'activer **AI Race Engineer**. Le plugin apparaît alors dans le menu de gauche de
SimHub, avec son statut en direct. S'il n'y est pas, active-le dans Settings → Plugins.
Lance une session en jeu : le statut passe au vert et <http://127.0.0.1:8889/snapshot> affiche ta télémétrie en JSON.

**Mises à jour** : au démarrage de SimHub, le plugin vérifie s'il existe une nouvelle version. Si oui, clique sur
« Installer la version X » dans sa page, accepte la demande d'autorisation et redémarre SimHub.

### 2. Le client vocal

```powershell
cd client
copy config.example.toml config.toml
```

Dans `config.toml`, remplis au minimum la section `[llm]` (clé API et modèle). Pour Infomaniak : crée un token
avec le scope `ai-tools` et récupère ton Product ID dans Manager → AI Tools.

Ensuite, trouve le bouton de ton volant :

```powershell
uv run engineer.py --list-controls
```

Appuie sur le bouton voulu, puis reporte `joystick_button` (et `joystick_name`) dans `[ptt]`.

Teste la voix, puis l'ingé sans lancer de jeu :

```powershell
uv run engineer.py --say "Box box, box box."
uv run engineer.py --ask "il est à combien le mec devant ?" --snapshot sample_snapshot.json
```

Au premier lancement, le client télécharge la voix Kokoro (~330 Mo) et le modèle Whisper (~480 Mo).

### 3. En course

```powershell
uv run engineer.py
```

Maintiens le bouton, parle, relâche. Si tu appuies pendant que l'ingé parle, ça le coupe.

> Sans uv : `py -3.12 -m venv .venv`, puis `.venv\Scripts\pip install -r requirements.txt` et
> `.venv\Scripts\python engineer.py`. Utilise Python 3.11 ou 3.12 : certaines dépendances n'existent pas
> encore pour les versions plus récentes.

## Configuration

Tout se règle dans `client/config.toml` (voir les commentaires de [config.example.toml](client/config.example.toml)).

- **Langue** : `language = "fr"` ou `"en"`. En anglais, la voix `bm_george` sonne très « ingé britannique ».
- **Voix réaliste payante** : `[tts] engine = "elevenlabs"`, puis remplis `[elevenlabs]`.
- **Changer de LLM** : il suffit de modifier `base_url` et `model`. Toute API compatible OpenAI fonctionne.
- **Effet radio** : `radio_effect = false` pour une voix propre.

## Latence

Mesurée sur CPU, sans GPU :

| Étape | Temps |
|---|---|
| Whisper `small` (question de 3-4 s) | ~1,3 s |
| Whisper `base` | ~0,4 s, un peu moins précis |
| LLM cloud (petit modèle) | ~0,5-1 s |
| Voix Kokoro (une phrase) | ~0,8 s |

Pour aller plus vite : `model = "base"`, ou Whisper sur GPU (`device = "cuda"`, `compute_type = "float16"`).
Le temps de chaque étape s'affiche après chaque réponse.

## Ce que l'ingé sait

Session (type, piste, temps et tours restants, météo, drapeau), position, temps au tour (dernier, meilleur, delta),
écart au leader, carburant et tours restants estimés, consommation des derniers tours, pneus (pressions,
températures, usure), températures des freins, dégâts, TC/ABS/répartition de freinage,
et la voiture devant et derrière (nom, écart, derniers tours, passages aux stands).

Les données dépendent du jeu. Si un jeu ne fournit pas une valeur (par exemple l'usure des pneus dans ACC),
elle vaut `null` et l'ingé te le dit au lieu d'inventer. En multiclasse, devant et derrière désignent
les voitures de ta classe.

## Structure

```
plugin/                 Plugin SimHub (C#, .NET Framework 4.8)
  AIRaceEnginePlugin.cs   construit l'instantané JSON (4 fois par seconde)
  SnapshotServer.cs       mini serveur HTTP sur 127.0.0.1:8889
  SettingsControl.cs      panneau de statut et de mises à jour dans le menu de SimHub
  Updater.cs              vérifie et installe les nouvelles releases GitHub
install.ps1             installateur (télécharge la dernière release)
Installer-AIRaceEngineer.cmd   la même chose en double-clic
dev-install.ps1         [dev] compile depuis les sources et installe (.NET SDK requis)
release.ps1             [mainteneur] publie une release avec la DLL
client/                 Client vocal (Python)
  engineer.py             point d'entrée et boucle push-to-talk
  stt.py / llm.py / tts.py / ptt.py / audio.py / telemetry.py
  sample_snapshot.json    télémétrie d'exemple pour tester sans jeu
```

## Développement

Pour compiler le plugin, il faut le [.NET SDK](https://dotnet.microsoft.com/download) 8 ou plus récent
(Visual Studio n'est pas nécessaire). SimHub doit être fermé :

```powershell
.\dev-install.ps1
```

Pour publier une nouvelle version, monte `<Version>` dans `plugin/AIRaceEngineer.csproj`, commite, pousse, puis
lance `.\release.ps1`. Les plugins installés la proposeront au prochain démarrage de SimHub.

## Idées pour la suite

- Annonces automatiques sans question : carburant bas, dernier tour, voiture qui revient fort derrière.
- Réponses directes sans LLM pour les questions simples (écart devant), pour gagner en latence.
- Bouton push-to-talk directement dans SimHub (Controls & Events).

## Licence

MIT. SimHub est un logiciel de SHWotever : ce projet n'est pas affilié à SimHub et ne redistribue aucune de ses DLL.
