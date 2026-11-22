# AI Race Engineer pour SimHub

Un ingénieur de course vocal pour **tous les jeux supportés par SimHub** (ACC, iRacing, LMU, rFactor 2, AC, F1...).
Tu maintiens un bouton du volant, tu poses ta question comme tu veux (« il est à combien le mec devant ? »,
« mes pneus ça va ? », « j'ai assez d'essence ? »), et l'ingé te répond à la radio. Il t'annonce aussi tout seul
ton temps au tour, tes écarts, tes changements de place et quand le carburant devient juste.

```
Jeu ──► SimHub ──► plugin AI Race Engineer (réglages, annonces, push-to-talk, télémétrie JSON)
                                │
             client vocal (lancé tout seul) : micro ─► Whisper local ─► IA ─► voix neuronale / Kokoro / ElevenLabs
```

## Ce que ça coûte

| Brique | Par défaut | Coût |
|---|---|---|
| Reconnaissance vocale | Whisper local (faster-whisper) | gratuit |
| Voix | voix neuronales Microsoft (en ligne), Kokoro en secours hors ligne | gratuit |
| Annonces automatiques | générées par le plugin, sans IA | gratuit |
| Questions (IA) | Infomaniak (1M de crédits offerts), Cloudflare (quota gratuit quotidien), OpenAI ou Claude | quelques centimes par course |
| Option voix premium | ElevenLabs Flash | payant |
| Option IA 100 % locale | Ollama (fournisseur « Autre ») | gratuit, mais partage la carte graphique avec le jeu |

## Installation

Télécharge **`Installer-AIRaceEngineer.cmd`** depuis la [dernière release](https://github.com/IIPoliII/simhub-ai-race-engineer/releases/latest)
et double-clique dessus. Tu peux aussi coller cette commande dans PowerShell :

```powershell
irm https://raw.githubusercontent.com/IIPoliII/simhub-ai-race-engineer/main/install.ps1 | iex
```

L'installateur :
1. trouve SimHub ;
2. installe le client vocal dans `%LOCALAPPDATA%\AIRaceEngineer`, avec [uv](https://docs.astral.sh/uv/) et Python
   si besoin, puis télécharge la voix et la reconnaissance vocale (~1 Go la première fois) ;
3. attend que SimHub soit fermé et copie le plugin. En général sans droits admin : l'installateur de SimHub laisse
   les utilisateurs écrire dans son dossier. Si ce dossier est protégé, Windows demande l'autorisation
   (les droits du dossier ne sont jamais modifiés).

Rien à compiler, rien à configurer en ligne de commande.

## Réglages

Relance SimHub et accepte d'activer **AI Race Engineer** : il apparaît dans le menu de gauche (sinon : Settings → Plugins).
Tout se règle dans sa page, rangée en colonnes pour tenir sur un écran. En haut, une bande d'état montre l'IA,
le client vocal, le push-to-talk et le jeu, avec ce qu'il reste à faire :

- **Langue et voix** : français ou anglais (par défaut, la langue de ton Windows), puis le moteur de voix :
  - **En ligne** (par défaut) : voix neuronales Microsoft, gratuites et très naturelles (Henri, Denise, Rémy, Antoine
    en français ; Ryan, Thomas, Sonia, Andrew, William en anglais). Sans internet, la voix hors ligne prend le relais ;
  - **Hors ligne** : Kokoro, sur ton PC (en anglais, `George` sonne très « ingé britannique ») ;
  - **ElevenLabs** : la plus réaliste, payante.

  Le bouton **Écouter** fait entendre la voix choisie. Effet radio et reconnaissance rapide ou précise en option.
- **Bruits de piste** : avec l'effet radio, des F1 passent parfois derrière l'ingé, comme au muret des stands.
  Rien n'est enregistré : chaque voiture est synthétisée à chaque message (régime, passages de rapport, distance,
  effet Doppler), donc jamais deux fois le même son, et parfois aucun. Le fond baisse quand l'ingé parle pour que
  sa voix reste parfaitement lisible.
- **Intelligence artificielle** : Infomaniak, Cloudflare, OpenAI, Claude, ou n'importe quelle API compatible
  OpenAI (Ollama...). Une fois la clé collée, la liste « Modèle » se remplit avec les modèles disponibles chez
  le fournisseur, les conseillés en tête. Le bouton **Tester la connexion** vérifie la clé et le modèle. Si l'API
  refuse le modèle, la page propose ceux qu'elle accepte en un clic.
  - Infomaniak : un token avec le scope `ai-tools` et le Product ID (Manager → AI Tools). Modèle par défaut : `mistralai/Ministral-3-14B-Instruct-2512`.
    Le plugin utilise l'API v2 d'Infomaniak, qui donne accès à tout leur catalogue (Mistral Small 4, Gemma 4...).
  - Cloudflare : un token API Workers AI et ton Account ID.
- **Annonces automatiques** : temps au tour comparé au précédent, meilleur tour, écarts devant/derrière,
  places gagnées ou perdues, alerte carburant (à 2, 3, 4 ou 5 tours). Chaque annonce montre un exemple de phrase.
- **Push-to-talk** : dans **Controls and events**, ajoute un mapping avec l'action
  `AIRaceEnginePlugin.PushToTalk` et appuie sur le bouton de ton volant. La page du plugin montre en direct
  si le bouton est détecté.

Les clés API sont enregistrées chiffrées (DPAPI Windows) dans `%LOCALAPPDATA%\AIRaceEngineer\settings.json`.

Le client vocal démarre et s'arrête avec SimHub, et redémarre tout seul s'il s'arrête. En cas de souci, la case
« Client vocal » affiche la cause, et le bouton « Journal » ouvre son journal.

**Mises à jour** : au démarrage, le plugin vérifie s'il existe une nouvelle version. Si oui, un bandeau apparaît en haut
de sa page : clique sur « Installer ». La nouvelle version est préparée dans ton profil, et elle remplace l'ancienne
quand SimHub se ferme (le plugin propose de le redémarrer tout de suite), après vérification de son empreinte.
Pas de demande d'autorisation Windows, sauf si le dossier de SimHub est protégé : elle est alors demandée à chaque
mise à jour. Le client vocal
est mis à jour en même temps.

**Performances** : le plugin est fait pour laisser la machine au jeu.
- Le client vocal tourne en priorité « inférieure à la normale », et la reconnaissance vocale utilise au plus
  la moitié des cœurs (4 maximum).
- Le client et SimHub ne s'échangent presque rien au repos : une requête qui attend qu'il se passe quelque chose
  (bouton, annonce), au lieu d'interroger en boucle.
- La télémétrie n'est mise en forme que quand l'ingé en a besoin, et les bruits de piste coûtent quelques
  dizaines de millisecondes par message.

## Latence

Mesurée sur CPU, sans GPU :

| Étape | Temps |
|---|---|
| Reconnaissance précise (Whisper `small`, question de 3-4 s) | ~1,3 s |
| Reconnaissance rapide (Whisper `base`) | ~0,4 s |
| IA cloud (petit modèle) | ~0,5-1 s |
| Voix en ligne (une phrase) | ~1-2 s |
| Voix Kokoro (une phrase) | ~0,8 s |

Les annonces automatiques n'ont pas besoin de reconnaissance vocale ni d'IA : elles partent directement.

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
  AIRaceEnginePlugin.cs   télémétrie JSON, file d'annonces, push-to-talk
  Callouts.cs             annonces automatiques (tour, écarts, positions, carburant)
  Settings.cs             réglages, fournisseurs d'IA, voix, config envoyée au client
  SettingsControl.cs      page du plugin dans SimHub
  Theme.cs                thème de la page (couleurs, boutons, interrupteurs, segments)
  ClientRunner.cs         lance le client vocal en arrière-plan
  SnapshotServer.cs       mini serveur HTTP sur 127.0.0.1:8889
  Updater.cs              mises à jour depuis les releases GitHub
  LlmTester.cs            bouton « Tester la connexion »
  ModelCatalog.cs         liste des modèles disponibles chez le fournisseur
client/                 Client vocal (Python)
  engineer.py             boucle principale (dialogue avec le plugin)
  stt.py / llm.py / tts.py / audio.py / ambience.py
  sample_snapshot.json    télémétrie d'exemple pour tester sans jeu
install.ps1             installateur (dernière release)
Installer-AIRaceEngineer.cmd   la même chose en double-clic
dev-install.ps1         [dev] compile le plugin depuis les sources et l'installe
release.ps1             [mainteneur] publie une release (DLL + client.zip + installateur)
```

## Développement

Plugin : [.NET SDK](https://dotnet.microsoft.com/download) 8 ou plus récent (Visual Studio n'est pas nécessaire).
SimHub fermé, puis `.\dev-install.ps1`.

Client : `cd client`, puis `uv run engineer.py` (SimHub doit tourner avec le plugin). Pour tester sans jeu :

```powershell
uv run engineer.py --say "Box box, box box."
uv run engineer.py --ask "il est à combien le mec devant ?" --snapshot sample_snapshot.json
```

Publier : monte `<Version>` dans `plugin/AIRaceEngineer.csproj`, commite, pousse, puis lance `.\release.ps1`.

## Idées pour la suite

- Réponses directes sans IA pour les questions simples (écart devant), pour gagner en latence.
- Annonces de stratégie : fenêtre d'arrêt aux stands, voiture qui revient fort derrière.

## Licence

MIT. SimHub est un logiciel de SHWotever : ce projet n'est pas affilié à SimHub et ne redistribue aucune de ses DLL.

## Crédits

La page du plugin a été retravaillée avec la méthode [unslop](https://github.com/heydamir/unslop) de Damir Sokolovsky (CC BY 4.0).
