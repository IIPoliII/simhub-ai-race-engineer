# Notes de version

[English](CHANGELOG.md) · **Français**

Tous les changements notables du projet sont notés ici. Le format suit
[Keep a Changelog](https://keepachangelog.com/fr/1.1.0/) et les versions suivent le [versionnage sémantique](https://semver.org/lang/fr/).

## [0.4.8] - 2026-11-23

### Ajouté
- Fenêtre de bienvenue au premier démarrage : ce que fait l'ingé, quelle IA et quelle voix choisir,
  comment régler le push-to-talk. Tu la revois quand tu veux avec le bouton ? en haut de la page du plugin.
- Boutons ? à côté de chaque fournisseur d'IA et d'ElevenLabs : des guides pas à pas (où créer le compte,
  quoi copier dans le plugin, combien ça coûte).
- Notes de version : ce fichier, en anglais et en français. Les notes des releases GitHub en sont tirées.

### Modifié
- Le lien vers les clés Claude mène à la Console Claude (platform.claude.com).

## [0.4.7] - 2026-11-22

### Ajouté
- Installateur Windows (MSI) en français et en anglais, sans droits admin. Il demande de fermer SimHub s'il est
  ouvert et propose de le lancer à la fin. La désinstallation depuis les Paramètres Windows retire le plugin,
  le client vocal et ses modèles, et garde tes réglages.
- Au premier démarrage, le plugin met en route le client vocal tout seul (uv, Python, dépendances, modèles
  de voix), avec l'avancement dans sa page. Une mise en route interrompue reprend au démarrage suivant.
- README en anglais et en français.

### Corrigé
- Le client vocal pouvait ne pas démarrer quand SimHub rechargeait le plugin.

## [0.4.6] - 2026-11-22

### Sécurité
- Les mises à jour ne donnent plus à ton compte le droit de réécrire le fichier du plugin. Une mise à jour
  préparée n'est copiée que si son empreinte SHA-256 correspond au fichier téléchargé.

## [0.4.5] - 2026-11-22

### Modifié
- Les mises à jour s'installent sans demande admin quand le dossier de SimHub est ouvert en écriture (cas habituel) :
  la nouvelle version est préparée dans ton profil et copiée à la fermeture de SimHub.
- Plus léger pour la machine : le client vocal attend les événements au lieu d'interroger en boucle, la télémétrie
  n'est mise en forme qu'au besoin, le client tourne en priorité « inférieure à la normale » et la reconnaissance
  vocale utilise au plus la moitié des cœurs.
- Bruits de piste : trafic au hasard, parfois une voiture, parfois un groupe, parfois rien.

## [0.4.4] - 2026-11-22

### Modifié
- Bruits de piste plus discrets et plus fluides.

## [0.4.3] - 2026-11-22

### Ajouté
- Bruits de piste : des F1 passent parfois derrière la voix de l'ingé. Chaque voiture est synthétisée à chaque
  message, donc jamais deux fois le même son, et la voix reste devant.

## [0.4.2] - 2026-11-22

### Ajouté
- Bouton « Écouter » pour entendre la voix choisie. Les boutons de voix attendent la fin de la phrase.
- « Comment l'associer » et « Configurer » amènent la bonne carte à l'écran.

### Modifié
- Le modèle Infomaniak par défaut porte son vrai nom (`mistralai/Ministral-3-14B-Instruct-2512`).

## [0.4.1] - 2026-11-22

### Ajouté
- La liste des modèles est récupérée chez le fournisseur, les conseillés en tête.

## [0.4.0] - 2026-11-22

### Ajouté
- Voix neuronales Microsoft (en ligne, gratuites), dont des voix françaises.

### Modifié
- Page du plugin en colonnes, avec une bande d'état.

## [0.3.0] - 2026-11-22

### Ajouté
- Proposition de redémarrer SimHub après une mise à jour.

### Modifié
- Nouvelle page du plugin.

## [0.2.0] - 2026-11-22

### Ajouté
- Tous les réglages dans SimHub : fournisseur d'IA, voix, annonces.
- Annonces automatiques : temps au tour, meilleur tour, écarts, positions, carburant.
- Le client vocal démarre et s'arrête avec SimHub.

### Sécurité
- La télémétrie est échappée avant d'être envoyée à l'IA.

## [0.1.0] - 2026-11-22

### Ajouté
- Première version : plugin SimHub avec télémétrie JSON, client vocal Python (Whisper local, IA, voix), push-to-talk.
- Installateur en une ligne et mises à jour depuis SimHub.

[0.4.8]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.7...v0.4.8
[0.4.7]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.6...v0.4.7
[0.4.6]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.5...v0.4.6
[0.4.5]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.4...v0.4.5
[0.4.4]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.3...v0.4.4
[0.4.3]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.2...v0.4.3
[0.4.2]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.1...v0.4.2
[0.4.1]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/releases/tag/v0.1.0
