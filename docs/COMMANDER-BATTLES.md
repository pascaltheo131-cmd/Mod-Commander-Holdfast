# Analyse du mod Workshop « Commander Battles »

Notre mod s'en **inspire** : on reprend ses idées, pas son code. Il n'a ni licence ni code source publié.

- Workshop : https://steamcommunity.com/sharedfiles/filedetails/?id=2654672970 (id 2654672970)
- Auteur : Alexaroth, d'après un mod de CoreDumper. 21 000 abonnés. Version 1.7 (09/2024), mise à jour « AOS ».
- **État au 31/10/2026 : retiré du Workshop** (« violates Steam Community & Content Guidelines ») **et marqué incompatible** avec le jeu.
  Il casse à chaque mise à jour de Holdfast.

Analyse faite sans rien exécuter : métadonnées de la DLL (classes, méthodes, chaînes) et contenu des AssetBundles. Pas de décompilation.

## Format du fichier
`.umod` = en-tête « UMOD » de 12 octets + archive ZIP :
- `modinfo.dat` : uMod 2.9.4, **Unity 2022.3.62f1** (à comparer avec la version du jeu avant de compiler) ;
- `assemblymodules.dat` : DLL .NET (netstandard 2.1) ;
- `sharedassets.bin` et `sceneassets.bin` : AssetBundles Unity (UnityFS, LZMA), avec une **scène**.

## Ce qu'il fait
Un officier lève sa propre ligne de **bots** et la commande. Les bots sont les « carbonPlayers » du jeu.

### Commandes
- `!addBots <nombre> [préfixe] [classe] [uniformID]` : officiers seulement. Préfixe de 12 caractères au plus.
  Si la classe n'est pas permise, ArmyLineInfantry. Noms d'époque tirés d'une liste, selon la faction.
- Admin : `!setmax X`, `!allowAnyBotClass true|false`, et deux commandes de débogage cachées (`!addfakeline`, `!dumpdebug`).

### Variables (`mod_variable CB:<clé>:<valeur>`)
`maxbots` (20), `allowAnyBotClass`, `defaultbotclass` (Guard), `disableaux`, `disableSlapHealthBalance`,
`customHealthBalance` (+100 PV au commandant, minimum -99), `disableshortcutbotspawn`.
Le README demande aussi `game_mode ArmyAssault` et des `class_system_orders_*_cooldown` courts.

### Ordres
| Ordre | Déclencheur | Côté |
|---|---|---|
| Former la ligne | Q1 (natif) | serveur (`OnOfficerOrderStart`) |
| Viser | Q2 (natif) | serveur |
| Feu | Q3 (natif) | serveur |
| Charge (ligne ou commandant ennemi visé) | Q6 (natif) | serveur |
| Rompre les rangs | Q7 (natif) | serveur |
| Suivre le commandant | Alt+1 ou B | **client** |
| Rang simple / double | Alt+2 ou N | **client** |
| Accroupir | Alt+3 | **client** |
| Baïonnette | Alt+4 | **client** |
| Marcher / courir | Alt+5 | **client** |
| Bouton « Spawn Bots » | Alt+S | **client** |

### Autres mécaniques
- Auxiliaires : porte-drapeau, et tambour à partir de 20 bots (`MarchingDrum`, suivi des buffs).
- Mort ou départ du commandant : annonce publique, les bots se dispersent en criant (help, surrender, retreat), puis `serverAdmin slay`.
- Bonus de vie du commandant à l'apparition par `serverAdmin slap <id> <valeur négative>`.
- **Télémétrie cachée** vers un Google Forms en fin de manche. **À ne pas reproduire** : notre mod n'envoie rien sur le réseau.

## Architecture
- `ModInterface` : `IHoldfastSharedMethods`, `IHoldfastSharedMethods2`, `IHoldfastGame` → relaie à `CBManager`,
  un MonoBehaviour **posé dans une scène** (d'où le risque console, voir [API.md](API.md) §2.3).
- Événements utilisés : `OnIsServer/OnIsClient`, `OnGameMethodsInitialized`, `PassConfigVariables`, `OnPlayerJoined/Left/Spawned`,
  `OnTextMessage`, `OnOfficerOrderStart`, `OnPlayerShout`, `OnRCLogin/OnRCCommand`, `OnConsoleCommand`, `OnPlayerPacket`,
  `OnPlayerKilledPlayer`, `OnPlayerHurt`, `OnRoundDetails`, `OnRoundEndFactionWinner`, `OnBuffStart/Stop`.
- Modèle : `Commander`, `Line`, `Bot`. Chaque bot a une file de tâches à priorités (`GoToPointTask`, `FollowTargetTask`,
  `AimTask`, `FireTask`, `ChargeTask`, `CrouchTask`, `BreakRankTask`, `InstrumentTask`, `ScatterAndDieTask`).
- Visée et cible de charge calculées sur le serveur (`Physics.Raycast`/`SphereCastAll`) à partir de la caméra du commandant
  (`set sendFullCameraData true` + `OnPlayerPacket`).
- Canal client → serveur : les touches lancent `rc login CommnaderOrderOne…Five` ; le serveur les lit dans `OnRCLogin`.
  Détournement à ne pas reprendre (n'existe pas sur console).

## Commandes `carbonPlayers` utilisées (toutes présentes dans le serveur installé)
- Démarrage : `carbonPlayers forceInputAxis true`, `forceInputRotation true`, `allowAutoRespawn false`, `set sendFullCameraData true`.
- Apparition et équipement : `carbonPlayers spawnSpecific …`, `pickupWeapon`, `dropAllWeapons`, `switchWeapon BearingFlag`,
  `switchNextWeapon`, `equipCarryableItem`.
- Pilotage : `inputAxis`, `inputRotation`, `pitch`, `setRunning`, `playerAction` (viser, tirer, recharger, baïonnette,
  s'accroupir, mêlée), `instrumentPlay/Stop`, `voicePhrase`.
- Admin : `serverAdmin slay`, `slap`, `kick`, `revive`, `broadcast`, `privateMessage`, `quietPrivateMessage`, `teleport`.

Inconnu (pas de décompilation) : ordre exact des arguments de `spawnSpecific`, formules de formation, vitesses, distances, délais.
À retrouver dans le code du serveur (`CarbonPlayers…`) ou par essais.

## Risques à retenir
- Charge CPU : chaque bot est un joueur simulé, piloté à chaque image. Prévoir un plafond global et par officier.
- Places : vérifier si les bots occupent des places de joueurs.
- Console : sans les raccourcis, il faut d'autres déclencheurs (roue d'ordres, ordres vocaux, chat).
