# Plan de la v1

Objectif : un mod **côté serveur seulement**, inspiré de Commander Battles, jouable par **tous** les joueurs (PC, Xbox, PlayStation).
Rien à télécharger pour les joueurs, aucun envoi réseau, aucune statistique (c'est le rôle de Holdfast Points).

## Principes
- `load_mod_server_only`, **scripts seuls, sans scène ni asset** (une scène rendrait le serveur incompatible console, voir [API.md](API.md) §2.3).
- Boucle par image : `new GameObject(...).AddComponent<CommanderLoop>()` créé dans le code, `DontDestroyOnLoad`.
- Agit uniquement par `ExecuteConsoleCommand`. État en champs statiques.
- Enums manipulées en **entiers**, avec notre propre table de noms.
- Variables : `mod_variable CMD:<clé>:<valeur>` (préfixe à confirmer).
- Retour aux joueurs : `serverAdmin privateMessage` (bandeau + chat, visible sur console) pour les réponses,
  `broadcast` pour les annonces de manche. Pas de `say` pour l'important (masqué aux comptes console restreints).

## Étape 0 : vérifications avant d'écrire le mod
Certaines se font **sans mod**, depuis la console admin en jeu (`rc login <mdp>` puis `rc <commande>`) :
1. `carbonPlayers` : liste des sous-commandes et arguments exacts de `spawnSpecific` (à lire aussi dans le code du serveur).
2. Un bot créé occupe-t-il une place de joueur ? Coût CPU de 20, 40, 80 bots sur une instance de test.
3. `teleport`, `playSong`, `broadcast`/`privateMessage` vus depuis une console Xbox ou PS.

Puis un **mod d'essai minimal** (« hello ») compilé et chargé en `load_mod_server_only` sur une instance de test :
4. le serveur reste visible et joignable depuis une console ;
5. l'objet Unity créé dans le code reçoit bien `Update` ;
6. `OnOfficerOrderStart` arrive pour un officier sur console ; la saisie du chat sur console arrive dans `OnTextMessage`.

## Étape 1 : cœur du mod
- **Levée de ligne** : `!ligne <nombre> [classe] [uniforme]` pour un officier (le nom est à choisir), avec plafonds global et par officier,
  classes permises selon la rotation. Déclencheur sans chat pour la console (par exemple l'ordre natif « Renfort » à l'arrêt) : à décider après l'étape 0.
- **Formation** : rang simple ou double, espacement, la ligne se place sur la position et l'orientation de l'ordre.
- **Ordres natifs** (roue d'ordres, marche sur console) : former la ligne, viser, feu, feu à volonté, cessez-le-feu, charge, rompre et suivre.
- **Ordres en plus** (sans touche) : par ordres vocaux (`OnPlayerShout` : Halt, ForwardMarch, CloseRanks, Retreat…) et par chat
  (`!suivre`, `!rangs`, `!baionnette`, `!accroupi`, `!pas`).
- **Auxiliaires** : porte-drapeau, tambour selon la taille de la ligne.
- **Fin de ligne** : mort ou départ de l'officier → dispersion, puis suppression des bots. Nettoyage en fin de manche.
- **Admin** : plafonds et activation par `mod_variable`, commandes admin en chat admin.
- **Robustesse** : aucun appel bloquant, pas de boucle avec `OnConsoleCommand`, journal `[CMD]` pour le diagnostic.

### Ordres par phrases vocales du jeu (`OnPlayerShout`)
La vraie VoIP (micro) n'est **pas** accessible aux mods : aucun événement, et le flux Opus reste interne au jeu.
En revanche, les **phrases vocales** du jeu arrivent au serveur, sur toutes les plateformes. L'enum `CharacterVoicePhrase`
contient déjà les ordres utiles. Correspondance proposée :

| Phrase du jeu | Ordre pour la ligne de bots |
|---|---|
| `Follow` | suivre l'officier |
| `Halt` | halte |
| `ForwardMarch` | en avant |
| `AtTheDouble` | pas de course |
| `FormColumn` | colonne |
| `DoubleRank` / `SingleRank` | rang double / simple |
| `Insult` | bascule rang simple / double (raccourci assignable dans les contrôles) |
| `PatrioticCheer` | bascule suivre / halte (touche B par défaut) |
| `CloseRanks` | serrer les rangs |
| `RemoveBayonet` | remettre la baïonnette (`FixBayonet` n'est criable nulle part : baïonnette au canon par `!baionnette`) |
| `GetDown` | s'accroupir |
| `TakeAim`, `MakeReady` | viser |
| `FirstRankFire` / `SecondRankFire` | feu du premier / second rang |
| `Fire`, `FireAtWill`, `CeaseFire` | feu, feu à volonté, cessez-le-feu |
| `Charge` | charge |
| `StandGround` | tenez bon (rallie) |
| `Retreat` | retraite |
| `Reinforce` | renforts |
| `PresentArms` | former le carré (lot B) |

[TEST] : quelles phrases le menu vocal propose vraiment à un officier, sur PC et sur console.

### Répartition retenue (décision de l'utilisateur, 31/10/2026)
On garde **les deux** :
- **Menu d'ordres officier** (`OnOfficerOrderStart`) pour les ordres principaux : former la ligne (position et orientation de l'ordre),
  viser, feu, feu à volonté, cessez-le-feu, charge, rompre et suivre, renforts.
- **Phrases vocales** (`OnPlayerShout`) pour le reste : rang double ou simple, serrer les rangs, colonne, halte, en avant, pas de course,
  baïonnette, s'accroupir, feu par rang, tenez bon, retraite.

Un ordre du menu officier ne déclenche **pas** `OnPlayerShout` pour l'officier (la phrase est jouée par chaque client) [CODE].
En revanche, les soldats de la ligne, **bots compris**, acclament et déclenchent `OnPlayerShout` : **ignorer les cris des bots**.
L'anti-doublon reste utile pour les phrases criées à la main. Le menu vocal limite à 5 phrases par 30 s côté client.

## Étape 2 : nouveautés retenues (31/10/2026)

| # | Nouveauté | Détail | Dépend de |
|---|---|---|---|
| 3 | **Succession** | L'officier tombe → un sergent bot prend le relais quelques secondes, la ligne recule en ordre, puis se disperse si personne ne la reprend. | cœur |
| 4 | **Moral** | Baisse avec les pertes et la mort de l'officier. Trop bas : la ligne flanche et recule. Remonte avec `StandGround` ou le rallie du sergent (`SergeantLineRally`). | cœur |
| 5 | **Renforts** | Ordre officier « Renfort » → quelques bots arrivent depuis l'arrière, avec délai de recharge et sans dépasser le plafond. | cœur |
| 7 | **Carré** | Formation en carré contre la cavalerie (déclencheur à choisir parmi les phrases vocales). | formations |
| 11 | **Bots selon le remplissage** | Plafond de bots qui baisse quand de vrais joueurs arrivent et remonte quand ils partent. | cœur |
| 12 | **Lignes ennemies IA** | Lignes de bots menées par l'IA dans l'autre camp, **seulement s'il n'y a aucun joueur en face** (à confirmer : aucun joueur ennemi, ou serveur vide). Elles disparaissent quand de vrais joueurs arrivent. | 11, test du camp des bots |
| 15 | **Skins par classe** | Choix de l'uniforme des bots pour chaque classe (ligne, garde, grenadier, porte-drapeau, tambour…), par réglage et éventuellement par l'officier. | identifiants d'uniforme |
| 16 | **Plafond selon la charge** | Le mod réduit le nombre de bots si le serveur peine (mesure indirecte : régularité de `Update`). | 11 |
| 17 | **Réglages depuis le panneau** | Section Commander dans holdfast.tpsrv.fr qui écrit les `mod_variable` (plafonds, autorisations, skins, IA). **Projet du panneau**, donc son propre agent. | variables figées |

Non retenues pour l'instant : 1, 2, 6, 8, 9, 10, 13, 14.

### Ordre proposé
1. Étape 0 (vérifications) et cœur du mod.
2. Lot A : 3, 4, 5, 11. **Codé le 31/10/2026** (variables dans `Scripts/CommanderConfig.cs`), pas encore testé en jeu.
3. Lot B : 15, 7, 16. **Codé le 31/10/2026**, pas encore testé en jeu. Carré : `!carre` ou la phrase `PresentArms`.
4. Lot C : 12. **Codé le 31/10/2026**, désactivé par défaut (`CMD:lignesia`), pas encore testé en jeu.
5. Lot D : 17. **En service le 31/10/2026** : onglet « Commander » du panneau (holdfast-panel c01a823, table `public/commander-vars.js` à tenir à jour avec `CommanderConfig.cs`).

## Compilation et publication
Voir [BUILD.md](BUILD.md). Unity **à la version exacte du jeu** (2022.3.62f2 au 31/10/2026) + Holdfast Mod Tools,
**sur le PC de l'utilisateur** (pas possible sur ce serveur). Vérifier la version d'Unity avant : Commander Battles est compilé avec
2022.3.62f1, le README de Holdfast Points indique 2022.3.53f1.
Publication sur le Workshop en script, puis `load_mod_server_only <id>` dans la rotation via le panneau.
