# Notes de version

[English](CHANGELOG.md) · **Français**

Tous les changements notables du projet sont notés ici. Le format suit
[Keep a Changelog](https://keepachangelog.com/fr/1.1.0/) et les versions suivent le [versionnage sémantique](https://semver.org/lang/fr/).

## [Non publié]

### Ajouté
- Réglages à la voix (nouvelle carte « Ingé de piste », coupée par défaut) : « mets le frein à 56 », « brake balance
  60 avant 40 arrière », « ABS 3 », « un cran de TC en moins ». L'ingé appuie pour toi sur les rôles du Control Mapper
  de SimHub que tes boutons utilisent déjà (ABS+, TractionControl-, Brake Bias +...) et relit la valeur dans la
  télémétrie après chaque appui, jusqu'à la valeur demandée : rien à associer en plus, dans tous les jeux reliés au
  Control Mapper et qui donnent la valeur à SimHub (Automobilista 2, ACC, iRacing, LMU, rFactor 2...). Sans Control
  Mapper, des touches clavier du jeu peuvent servir à la place.
- Conseils spontanés, comme un vrai ingé de piste (jamais, rarement ou parfois, au choix) : en passant la ligne, si
  les pneus avant sont bien plus chauds que l'arrière (ou l'inverse) ou si les pressions sont très inégales, l'ingé
  le signale et propose un réglage. Jamais deux fois le même sujet de suite.
- « Essence pour dix tours » : l'ingé calcule ce qu'il faut rajouter avec la consommation des derniers tours.
- « Je rentre dans deux tours » : le programme d'arrêt attend le bon tour.
- iRacing : pendant « box box », l'essence, les pneus et la réparation rapide vont directement dans le menu des
  stands. Les autres jeux n'ont pas de moyen sûr de remplir ce menu : l'ingé dit quoi régler.
- Conseils de réglage : « je sous-vire, tu proposes quoi ? » donne une ou deux pistes, d'abord en roulant (frein,
  ABS, TC), puis au garage.

### Corrigé
- Ingé muet après un changement de jeu dans SimHub : le client vocal recevait une copie du socket du port 8889, donc
  quand SimHub recréait le plugin, le port restait pris sans que personne ne réponde (« Une seule utilisation de
  chaque adresse de socket » dans le journal) jusqu'au redémarrage de SimHub. Le port passe maintenant d'une instance
  du plugin à la suivante sans être refermé, et il est réessayé toutes les 5 secondes s'il est pris.

### Corrigé
- Dégâts : l'ingé dit où est le dégât au lieu d'une seule moyenne. Dans Automobilista 2 (et Project CARS 2), il
  reçoit l'aéro, le moteur, la suspension et les freins roue par roue : « Suspension avant droite à quarante pour
  cent » plutôt que « dégâts moyens à sept pour cent, rien de critique ». Une suspension touchée explique un volant
  plus droit, et au « box box » l'ingé propose de la réparer même si la moyenne est basse. Les autres jeux ne
  donnent que la moyenne : l'ingé le dit.

## [0.6.2] - 2026-11-25

### Corrigé
- Places gagnées ou perdues : plus d'avalanche d'annonces au départ ou dans une bagarre. Rien pendant le premier
  tour, puis un seul bilan depuis la grille (« P7, tu gagnes 5 places. ») ; ensuite au plus une annonce toutes les
  20 secondes, avec le bilan depuis la précédente. Une place perdue puis reprise n'est pas annoncée.

## [0.6.1] - 2026-11-25

### Ajouté
- Push-to-talk « Appuyer pour parler, réappuyer pour envoyer », au lieu de maintenir le bouton : un réglage dans la
  carte Push-to-talk. Il marche avec tous les types d'appui de SimHub, et la radio se referme seule après 30 secondes.
- Test du push-to-talk dans la page, sans lancer de jeu : maintiens ton bouton, dis une phrase et relâche. La carte
  Push-to-talk montre le micro utilisé, son niveau et la phrase comprise, ou ce qui bloque : bouton relâché trop
  vite, micro muet, client vocal arrêté.
- Un mapping du push-to-talk en « ShortPress » est repéré : SimHub envoie alors l'appui et le relâchement d'un coup
  et rien n'est enregistré. La page dit de choisir « During » (ou le mode appuyer / réappuyer).

### Corrigé
- Temps intermédiaires : un écart de quelques millièmes n'est plus annoncé « même temps ». L'ingé dit « quatre
  millièmes » ou « deux centièmes d'avance », et « même temps » seulement à égalité au millième. Pareil pour l'écart
  au tour précédent.

## [0.6.0] - 2026-11-24

### Ajouté
- Volume de la voix et volume des bruits de piste : deux curseurs dans « Langue et voix ». La voix monte jusqu'à
  200 % sans saturer (les crêtes sont arrondies), le fond va de 0 à 200 %. Double-clic : retour à 100 %. Changer un
  volume ne recharge pas la voix.
- Annonce « Tour invalidé. » dès que le jeu invalide ton tour (hors limites, coupe...), et le temps du tour le
  rappelle : « Tour en 1 minute 47 secondes 3, invalidé. »
- Temps intermédiaires hors course (essais, qualifs, hotlap) : à la fin de chaque secteur, l'ingé te dit ton avance
  ou ton retard sur ton meilleur tour, par exemple « Secteur 2 : trois dixièmes d'avance. » Rien en course, ni pour
  un tour déjà invalidé ou parti des stands.

### Corrigé
- IA « Autre » (Ollama, LM Studio) : une URL sans `/v1` (`http://10.0.0.5:11434/`) répondait « Adresse introuvable
  (404) » et la liste des modèles restait vide. Le `/v1` est maintenant ajouté s'il manque.
- IA « Autre » : les modèles qui réfléchissent avant de répondre (Qwen3...) répondaient vide, leur réflexion prenant
  toute la place. L'ingé leur demande maintenant de répondre directement ; un serveur qui ne connaît pas ce réglage
  est réinterrogé sans.
- Après un arrêt imprévu du client vocal, sa relance automatique ne dit plus « Radio check, je t'entends » en pleine
  course (à la place du temps au tour). La page montre en orange qu'il a été relancé et pourquoi, et le journal
  garde ce qui s'est passé avant l'arrêt.
- Le client vocal ne s'arrête plus quand SimHub met plus de 20 secondes à répondre : il attend tant que SimHub
  tourne. Un souci ponctuel dans sa boucle est noté dans le journal au lieu de l'arrêter.

## [0.5.5] - 2026-11-23

### Corrigé
- « Box box » : les noms de pneus sont mieux compris (« hard » était entendu « arbre », « mets des hards »
  « Mediards »), et un choix de pneus qui n'a pas de sens est redemandé au lieu d'être accepté. L'ingé ne choisit
  plus les pneus à ta place.
- « Box box » : l'essence et les pneus que tu annonces sont gardés même quand l'IA oublie de les noter, et tes mots
  passent avant son résumé.
- Les réponses de l'ingé ne contiennent plus d'astérisques de mise en forme, et une réponse vide pendant un arrêt
  est remplacée par le récapitulatif du programme.

## [0.5.4] - 2026-11-23

### Corrigé
- La reconnaissance vocale entend maintenant « box box » et « pit pit » (elle entendait « Bix, Bix » ou « Pete, Pete »),
  et l'ingé reconnaît l'appel même mal entendu.
- Quand l'ingé n'a pas compris la question, il le dit et te demande de répéter, au lieu de répondre à côté. Il ne
  donne plus d'ordres que tu n'as pas demandés (comme « reste en stand »).
- « Box box » en début de course, avant que l'essence puisse être calculée : l'ingé te demande combien tu en veux au
  lieu de proposer le plein.

### Modifié
- Le journal indique si la voix vient du service en ligne ou de la voix locale, et combien de temps elle a pris.

## [0.5.3] - 2026-11-23

### Ajouté
- « Box box » : dis-le à la radio et l'ingé prépare ton arrêt. Il donne l'essence à remettre pour finir avec un tour
  de marge, te demande quels pneus tu veux, récapitule le programme, puis te le rappelle à l'entrée des stands.
  « Annule le box » l'annule. Marche avec tous les jeux ; les réglages du menu des stands restent à faire toi-même.
- Les notes de version dans SimHub : le bouton « Notes de version » de « Client vocal et mises à jour » les affiche
  sans navigateur, et après une mise à jour une fenêtre « Nouveautés » montre ce qui a changé depuis ta version
  d'avant.

## [0.5.2] - 2026-11-23

### Ajouté
- Nombres à la suisse (en français, dans Langue et voix) : l'ingé dit septante, huitante, nonante, dans ses réponses
  comme dans les annonces, quelle que soit la voix. Coché d'office si Windows est réglé sur la Suisse.
- Deux voix suisses parmi les voix en ligne : Fabrice et Ariane. Quand Windows est réglé sur la Suisse, Ariane
  devient la voix française par défaut (la mise à jour te la met si tu avais encore Henri).

### Corrigé
- Au stand, l'ingé ne donne plus le chrono du tour en cours comme un temps au tour.
- En essais et en qualifs, l'ingé ne dit plus que tu es en tête avant que tu aies fait un temps.
- L'ingé répond seulement à la question posée, au lieu d'ajouter des chiffres parfois faux, et dit « essais libres »
  plutôt que « pratique ».
- « Pas de télémétrie » devient « Je ne reçois rien du jeu » quand SimHub tourne mais que tu n'es pas en piste.

## [0.5.1] - 2026-11-23

### Modifié
- Mises à jour : la section « Client vocal et mises à jour », en bas de la page, affiche maintenant « Installer la
  X.Y.Z » quand une mise à jour attend, puis « Redémarrer SimHub » une fois installée, comme le bandeau du haut.
  Avant, elle ne proposait que « Rechercher une mise à jour », même avec une mise à jour en attente. Le bouton
  arrive avec cette version : pour passer à la 0.5.1 elle-même, utilise « Installer » dans le bandeau du haut.

## [0.5.0] - 2026-11-23

### Ajouté
- La page du plugin, la fenêtre de bienvenue et les guides ? en français et en anglais. Ils suivent le réglage Langue,
  comme les messages du client vocal : changer de langue redessine la page.
- `AIRaceEngineer-Setup.exe` demande la langue de l'installation, français ou anglais (celle de ton installation
  actuelle est présélectionnée, sinon celle de ton Windows), puis installe. Avec `/passive` ou `/quiet`, il installe
  sans rien demander.
- Un seul `AIRaceEngineer-Setup.msi` avec les deux langues, pour le déploiement silencieux (`ProductLanguage=1036`
  pour le français, `1033` pour l'anglais). Il remplace les MSI français et anglais séparés, et la mise à jour marche
  quelle que soit la langue installée avant.
- L'installation en une ligne parle français ou anglais, selon Windows.
- GitHub Actions construit et publie chaque release, et vérifie chaque jour que le plugin se charge encore dans
  la dernière version de SimHub.
- Les lecteurs d'écran lisent le nom de chaque interrupteur et de chaque champ de la page du plugin.

### Modifié
- La page du plugin suit maintenant la langue de l'ingé (Langue et voix). Les réglages de la 0.4.x gardent leur
  langue ; la langue choisie dans Setup.exe vaut pour les nouvelles installations.
- OpenAI : le modèle conseillé est maintenant gpt-6-luna, et la mise à jour te le met si tu utilisais gpt-4.1-nano
  (retiré), gpt-4.1-mini ou gpt-4o-mini (retirés tous les deux le 14 janvier 2027). Tu peux reprendre ces deux-là
  dans la liste « Modèle » jusque-là.
- IA sur ton PC (Ollama) : le guide conseille maintenant qwen3:4b-instruct ; qwen3:4b télécharge désormais un modèle
  qui réfléchit, trop lent pour la radio. gemma3:4b remplace mistral-small3.2 parmi les modèles conseillés.
- Cloudflare : Gemma 4 26B remplace Llama 3.1 8B parmi les modèles conseillés. Llama 3.1 8B n'est plus proposé :
  si tu l'utilisais, la mise à jour te met Llama 3.3 70B.
- Le client vocal installe exactement les versions de dépendances testées pour la release.
- Plus léger pour la machine : la page arrête de se rafraîchir quand SimHub est réduit et ralentit quand SimHub est
  en arrière-plan, et les champs de texte sont enregistrés quand tu t'arrêtes de taper.
- Première mise en route : la case « Client vocal » affiche « Installation 1/2 », puis « 2/2 », avec l'étape
  en cours et la taille des téléchargements de la reconnaissance vocale (le texte complet au survol). Si la voix hors
  ligne ne peut pas marcher sur le PC, la case le dit une fois la mise en route finie.
- Le bouton « Nouveautés » du bandeau de mise à jour devient « Notes de version » et ouvre ces notes dans ta langue.
- Réparation : relance `AIRaceEngineer-Setup.exe` et choisis « Réparer » (Programmes et fonctionnalités ne la
  propose plus).
- L'installateur en français te tutoie sur tous les écrans, et sa page de licence commence par une note en français.
- Infomaniak : « Obtenir une clé » ouvre la page des tokens API utilisée dans le guide.

### Corrigé
- Claude : les questions n'échouent plus avec l'erreur 400, et « Tester la connexion » envoie maintenant la même
  requête que l'ingé (pas de temperature, de la marge pour la réflexion).
- Un modèle retiré ou inconnu n'est plus pris pour un mauvais Account ID ou Product ID : « Tester la connexion »
  affiche le message du fournisseur et propose les modèles conseillés.
- Les réglages sont enregistrés de façon sûre : une coupure pendant un enregistrement ne les perd plus
  (settings.json.bak), et un fichier illisible est gardé en settings.json.bad au lieu d'être remplacé par les valeurs
  par défaut. La page te prévient quand ça arrive.
- La voix hors ligne marche avec un nom de profil Windows accentué, et utilise au plus la moitié des cœurs,
  comme la reconnaissance vocale.
- ElevenLabs : si la clé, le quota ou le Voice ID est refusé, la voix hors ligne prend le relais au lieu de se taire.
  Un Voice ID vide donne la voix par défaut.
- Quand la voix en ligne tombe, la voix hors ligne parle tout de suite pendant une minute au lieu de réessayer avant
  chaque phrase, et les annonces trop anciennes sont abandonnées au lieu d'arriver en retard.
- Une réponse vide de l'IA (un modèle qui réfléchit avant de parler) ne passe plus pour une panne de voix :
  l'ingé te le dit, et la page donne le nom du modèle.
- La case « Client vocal » garde les erreurs et les avertissements au lieu d'afficher « Prêt à répondre » (un souci
  de micro compris), et « Radio check » et « Écouter » restent bloqués pendant que l'ingé répond.
- Le client vocal ne plante plus sans sortie audio, et un proxy Windows ne le coupe plus de SimHub.
- La reconnaissance vocale se prépare vraiment au démarrage, donc la première question n'est pas plus lente que
  les suivantes, et changer de langue ne la recharge plus.
- Multiclasse : ta position, l'écart au leader et les voitures devant et derrière sont maintenant tous ceux de ta
  classe (ils étaient mélangés avec le classement général) ; l'ingé connaît aussi ta place au général.
- Meilleur tour : les tours invalidés ne comptent plus, et démarrer SimHub en pleine session ne donne plus un faux
  meilleur tour.
- Carburant : l'alerte ne dit plus un tour de moins que le réglage et revient après chaque ravitaillement ;
  la consommation moyenne laisse de côté les tours aux stands et les tours incomplets ; l'ingé utilise l'unité
  de carburant de SimHub (litres ou gallons).
- Quand le jeu ne fournit pas la cartographie moteur, la répartition de freinage, la taille du réservoir, la position
  ou les températures de l'air et de la piste, l'ingé dit qu'il ne sait pas au lieu d'annoncer 0. Le leader n'est
  plus donné comme la voiture derrière quand ta position est inconnue.
- Changer de jeu dans SimHub ne redémarre plus le client vocal, n'interrompt plus sa première mise en route et
  ne vide plus son journal. Une mise à jour déjà préparée n'est plus perdue, et la proposition de redémarrer
  n'apparaît qu'une fois.
- Un client vocal resté après un plantage de SimHub s'arrête au lieu de répondre en double avec le nouveau.
- La première mise en route ne bloque plus sur certains PATH ou quand uv s'installe dans un dossier inhabituel,
  ne redémarre plus en boucle, et son message d'erreur dit quoi faire.
- La page du plugin ne se fige plus pendant le démarrage du client vocal, et la case « Client vocal » dit quand
  le port 8889 est occupé au lieu de promettre un démarrage automatique.
- Mises à jour : une copie ratée ne laisse plus SimHub sans plugin ; le bandeau dit « prête » pour une mise à jour
  qui s'installe au redémarrage et n'affiche plus « Installation » après une recherche ; relancer
  `AIRaceEngineer-Setup.exe` sur une version mise à jour depuis le plugin remplace tous les fichiers du client vocal.
  Un nom d'utilisateur Windows avec une apostrophe ne casse plus les mises à jour ni l'installation en une ligne.
- Liste « Modèle » : la rafraîchir n'écrase plus le modèle que tu tapes. Le résultat de « Tester la connexion »
  disparaît quand tu changes la clé, le compte ou le modèle.
- La fenêtre de bienvenue ne revient plus au démarrage suivant, et un guide ou la fenêtre de bienvenue ne s'ouvre
  plus en double.
- L'installateur ne promet plus de relancer SimHub quand tu répares ou désinstalles, et « Lancer SimHub » sur sa
  dernière page n'affiche plus de carré gris.
- Microsoft a renommé la voix William : ton choix est gardé.

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

[0.6.2]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.5...v0.6.0
[0.5.5]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.4...v0.5.5
[0.5.4]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.3...v0.5.4
[0.5.3]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.2...v0.5.3
[0.5.2]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.1...v0.5.2
[0.5.1]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/IIPoliII/simhub-ai-race-engineer/compare/v0.4.8...v0.5.0
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
