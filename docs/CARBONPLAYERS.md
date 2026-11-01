# Bots du jeu (`carbonPlayers`) : référence pour le mod

Établi le 31/10/2026 en lisant le code décompilé du **serveur Linux installé** (`Assembly-CSharp.dll`, `HoldfastEnums.Runtime.dll`,
`Uniforms.Runtime.dll`, `uLink.Runtime.dll`) et les données de la scène `level1` du serveur. Rien n'a été lancé.

Légende : **[CODE]** = lu dans le code décompilé (fichier et méthode cités). **[TEST]** = à vérifier en jeu.
Les chemins `HoldfastGame/X.cs` sont relatifs au dossier décompilé `acsproj/`.

---

## 0. Attention : valeurs entières des enums

Le fichier [`ref/HoldfastEnums.Runtime.cs.txt`](ref/HoldfastEnums.Runtime.cs.txt) donne les noms **dans l'ordre**, mais plusieurs enums
du jeu ont des **valeurs explicites avec des trous**. Compter les positions donne des valeurs fausses [CODE] (`HoldfastEnums.Runtime.dll`,
décompilé type par type ; mêmes valeurs dans `HoldfastSharedMethods.dll`, donc le cast du jeu vers le mod reste juste) :

| Enum | Valeurs réelles (extrait) |
|---|---|
| `PlayerClass` (byte) | None 0, ArmyLineInfantry 1, NavalMarine 2, NavalCaptain 3, NavalSailor 4, NavalSailor2 5, ArmyInfantryOfficer 6, CoastGuard 7, Carpenter 8, Surgeon 9, Rifleman 10, LightInfantry 11, FlagBearer 12, Customs 13, **Guard 16, Grenadier 18, Musician 19, Sergeant 20, Cannoneer 21, Rocketeer 22, Sapper 23, Hussar 24, Dragoon 25** |
| `FactionCountry` | None 0, British 1, French 2, Prussian 3, Russian 4, Italian 5, Allied 6, Central 7, Austrian 8, Privateer 9, Spanish 10, ARBritish 11, ARAmerican 12, Internal_Frontline_* 100 à 106 |
| `CharacterVoicePhrase` | trous à 14, 28, 32, 91 à 94. Ex. CeaseFire 7, Charge 8, **Fire 17, FireAtWill 18**, FirstRankFire 19, FixBayonet 20, GetDown 23, Help 29, **MakeReady 46**, RemoveBayonet 63, **Retreat 67**, SecondRankFire 72, **StandGround 77**, TakeAim 82, Warcry 95, PatrioticCheer 97, HurrahCheer 98, **FormColumn 99, ForwardMarch 100, Follow 101, Halt 102, AtTheDouble 103, CloseRanks 104, DoubleRank 105, SingleRank 106, Reinforce 107** |
| `BuffType` | trous à 5 et 28. StandardBearer 1, StandardBearerCompanion 2, Musician 3, MusicianCompanion 4, **Guardsman 6, GuardsmanCompanion 7, OfficerInLine 8, OfficerLineCharge 9, OfficerLineFire 10, OfficerLineFireAtWill 11, SergeantInLine 12, SergeantLineRally 13, SergeantLineFire 14, SergeantLineFireAtWill 15**, CarpenterBerserk 16, … Dash 29, SteadyAim 30 |
| `WeaponType` (ushort) | nombreux trous (ex. BearingFlag 22, Musket_Land_Pattern_Musket_Brown_Bess 39) |
| Sans trous | `HighCommandOrderType`, `PlayerActions`, `CarryableObjectType`, `InstrumentType` (valeurs = position) |

Conséquences pour [API.md](API.md) §2 : « 19 = Deprecated_Rocketeer » est faux (19 = Musician, Rocketeer = 22) ; `FactionCountry` n'est pas
« décalé » côté mod, il lui manque seulement les noms 9 à 12. **Dans le mod, comparer `PlayerClass` et `CharacterVoicePhrase` par nom
(enum du mod) ou avec ces valeurs, jamais par position.**

---

## 1. La commande `carbonPlayers`

### 1.1 Analyse de la ligne [CODE]
`HoldfastGame/GameConsolePanel.cs` `ExecuteInput`, `ParseCommand`, `SplitArgsWithQuotes` :
- nom de commande et sous-commandes **sensibles à la casse** (`Dictionary` et `switch` sur la chaîne) ;
- arguments séparés par des espaces, **guillemets acceptés** (`"..."`, `“...”`, `„`) pour un argument avec espaces ;
  un argument vide (`""`) **disparaît** (filtré), il décale les suivants ;
- noms d'enum lus par `Enum.Parse(..., ignoreCase: true)` : nom insensible à la casse **ou** valeur numérique ;
- nombres décimaux lus par `Convert.ToSingle`/`float.Parse` selon la culture du serveur [TEST : utiliser le point `1.5`] ;
- vecteurs `x,y,z` sans espace (`GetVector3`).

Classe : `HoldfastGame/ServerCarbonPlayersConsoleCommand.cs` `Execute`. Logique : `HoldfastGame/ServerCarbonPlayersManager.cs`.
Commande toujours enregistrée, pas réservée au mode développeur (`ServerConsoleCommandsInitializer`, liste `second`).

### 1.2 Désignation de la cible [CODE]
- La cible est **l'id de joueur** (le même que `OnPlayerJoined.playerId`).
- Pour la plupart des sous-commandes, l'id est **le dernier argument, facultatif**. **Absent = tous les bots**, et pour `inputAxis`,
  `inputRotation`, `forceInputAxis`, `forceInputRotation` cela devient aussi **la valeur par défaut des bots créés ensuite**
  (`SetInputAxis(..., -1)` écrit `newSpawnInputAxis`, etc.). **Le mod doit toujours donner l'id.**
- Les réglages par bot (`inputAxis`, `inputRotation`, `force*`, `setRunning`, `playerAction`, `autoWeaponSwitchToggle`) passent par
  `CarbonPlayers.TryGetValue(id)` : **bots seulement** (sur un vrai joueur : `success=false`, sortie « Updating setting »).
- Certaines passent par `ResolveServerRoundPlayer(id)` et marchent donc **sur n'importe quel joueur**, vrai compris :
  `pitch`, `yaw`, `voicePhrase`, `switchWeapon`, `switchNextWeapon`, `equipCarryableItem`, `instrumentPlay/Stop`, `highCommandOrder`,
  `pickupWeapon`, `dropWeapon`, `dropAllWeapons`, `mountVehicle`, `dismountVehicle`, `grantScore`, `startHealing`. **Ne jamais les envoyer
  avec l'id d'un vrai joueur par erreur.**
- `success` (3ᵉ valeur de `OnConsoleCommand`) dit si l'action a été appliquée ; le texte de sortie est souvent fixe (« Updating setting »,
  « ok ») et n'est pas fiable.

### 1.3 Toutes les sous-commandes [CODE]
`Parameters` liste 45 noms, mais deux n'ont **aucun** `case` : **`teleportToPlayer` et `meleeAttack` n'existent pas**
(réponse « Unable to parse command », `success=false`). Deux `case` existent hors de la liste : `randomVoicePhrase`, `interactableObjectUpdateMG`.

| Sous-commande | Syntaxe exacte | Effet / remarques |
|---|---|---|
| `spawn` | `spawn [nombre=1] [intervalle_s=0]` | bots au hasard (faction et classe). Refusé si un `spawn` est déjà en cours. |
| **`spawnSpecific`** | `spawnSpecific <faction> <classe> [nom] [tag] [uniformId]` | **un** bot. Voir §2. |
| `spawnDeterministic` | `spawnDeterministic <nombre> <faction> [classe]` | nom « DeterministicPlayer », uniforme 1, sans hasard ; sans classe : toutes les classes en boucle. |
| `devMode` | `devMode <nombre>` | force axe et rotation (tous) puis `spawn`. |
| `disperse` | `disperse` | téléporte chaque bot à un point au hasard de la carte. |
| `join` | `join` | regroupe les bots au centre du terrain (au hasard). |
| `group` | `group [distance=0.0075]` | force axe et rotation (tous) et range les bots en grille au centre. |
| `mimicPlayer` | `mimicPlayer` | **refusé depuis le serveur (`adminId<0`)**. Tous les bots copient les paquets de l'admin ; bascule. |
| `ignoreAutoControls` | `ignoreAutoControls <true\|false>` | global : axe nul et rotation figée pour tous les bots. |
| `combinedInput` | `combinedInput <id> <x> <y> <rotationY> [action ...]` | axe + rotation + actions en un appel (**id en premier**). |
| `forceInputAxis` | `forceInputAxis <true\|false> [id]` | utilise l'axe imposé au lieu de l'axe automatique. |
| `forceInputRotation` | `forceInputRotation <true\|false> [id]` | utilise la rotation imposée au lieu de la rotation automatique. |
| `inputAxis` | `inputAxis <x> <y> [id]` | axe de déplacement (§7). |
| `inputRotation` | `inputRotation <degrés> [id]` | cap du corps (§7). |
| `pitch` | `pitch <valeur> [id]` | borné à **[-1.5 ; 2]** (§7). |
| `yaw` | `yaw <degrés> [id]` | borné à [-90 ; 90] : tête/visée par rapport au corps (§7). |
| `setRunning` | `setRunning <true\|false> [id]` | ajoute l'action `Run` (8) à chaque paquet. |
| `playerAction` | `playerAction <action> [action ...] [id]` | §1.4. |
| `switchWeapon` | `switchWeapon <WeaponType> [id]` | seulement une arme que le bot possède (`AvailableWeapons.GetWeapon`). |
| `switchNextWeapon` | `switchNextWeapon [id]` | passe à une autre arme possédée. |
| `autoWeaponSwitchToggle` | `autoWeaponSwitchToggle <true\|false> [id]` | change d'arme toutes les 0,5 s. |
| `voicePhrase` | `voicePhrase <CharacterVoicePhrase> [id]` | fait crier le bot. **Déclenche `OnPlayerShout(id, …)`** (§5). |
| `randomVoicePhrase` | `randomVoicePhrase [id]` | phrase au hasard. |
| `equipCarryableItem` | `equipCarryableItem <CarryableObjectType> [id] [itemId=-1]` | met un objet en main (drapeau, tambour…), sans contrôle de classe côté commande. |
| `pickupWeapon` | `pickupWeapon <id> <WeaponType>` | **id obligatoire, en premier**. |
| `dropWeapon` | `dropWeapon <id> <WeaponType>` | id en premier. |
| `dropAllWeapons` | `dropAllWeapons <id>` | id obligatoire. |
| `instrumentPlay` | `instrumentPlay <id> <InstrumentType> <songId>` | **id en premier**. `InstrumentType` : MarchingDrum 3, Fife 4, Violin 5, Bagpipes 6, … Bugle 11. `songId` : [TEST]. Donne le buff `Musician` (§6). |
| `instrumentStop` | `instrumentStop <id>` | arrête le morceau en cours. |
| `highCommandOrder` | `highCommandOrder <id> <HighCommandOrderType> <x,y,z> <rotationY>` | **le joueur `id` donne un ordre du menu** (même file que les vrais ordres : contrôle de classe, délai). Déclenche `OnOfficerOrderStart` si accepté. |
| `startHealing` | `startHealing <soigneur> <soigné> <HealingMode>` | `HealingMode` : None, OthersHealing, SelfHealing, DiseaseOthers, SleepingHeal. |
| `stopHealing` | `stopHealing <soigneur>` | |
| `mountVehicle` | `mountVehicle <id> <vehicleId> [mountTriggerId=0]` | |
| `dismountVehicle` | `dismountVehicle <id>` | |
| `sapperChangeMode` | `sapperChangeMode <id> <SapperModes>` | |
| `sapperBuildItem` | `sapperBuildItem <id> <EmplacementType> <x,y,z> <rx,ry,rz> <parentId> <socketId>` | |
| `interactableObject*` | `interactableObject <id> <objet> <navire> <activation> <transition>` et variantes `UpdateCannon`, `UpdateMovingCannon`, `UpdateMovingLadder`, `UpdateMovingRocket`, `UpdateMG` | canons, échelles, fusées : hors sujet pour la v1. |
| `allowAutoRespawn` | `allowAutoRespawn <true\|false>` | **global**, défaut `true` (§2.4). |
| `reverseDirectionWhenHitObstacle` | `reverseDirectionWhenHitObstacle <true\|false>` | global : obstacle à moins de 1,5 m devant → demi-tour forcé (§7). |
| `grantScore` | `grantScore <id> <points> [raison…]` | ajoute du score. |

### 1.4 `playerAction` [CODE]
- Chaque argument est un nom de `PlayerActions` ; **le dernier argument, s'il est un entier, est l'id**. Donner donc les actions **par nom**.
- Mot spécial `MeleeStrikeRandom` : frappes de mêlée en boucle dans une direction au hasard.
- Les actions sont envoyées **une seule fois**, au prochain `FixedUpdate` (`queuedPlayerActions` remis à `null`). Ce sont des appuis,
  pas des états maintenus. Le mod renvoie l'action quand il veut la répéter.
- Ajouts automatiques (`UpdateCarbonPlayerInput`) : `StartAimingFirearm` ajoute `DisableCombatStance` ; `MeleeBlock*` ajoute
  `EnableCombatStance` + `StartMeleeBlock` ; `ExecuteSecondaryAttack` ajoute `EnableCombatStance` ; `FireFirearm` calcule le point
  et la direction du tir à partir de `pitch`/`yaw`.
- `MeleeStrikeHigh/Low/Left/Right` lancent une **boucle de frappes** (tenue 0,5 s → frappe → 0,8 s → recommence) jusqu'au prochain
  `playerAction` sans frappe (par exemple `playerAction None <id>`). Si l'arme active n'est pas une arme de mêlée, la boucle s'arrête.

Actions utiles (valeur = position, pas de trous) : `Jump` 2, `StartAimingFirearm` 3, `StopAimingFirearm` 4, `FireFirearm` 5, `Run` 8,
`Crouch` 9, `StartReloadFirearm` 12, `InterruptReloadFirearm` 13, `EnableCombatStance` 15, `DisableCombatStance` 16,
`MeleeStrikeHigh/Low/Left/Right` 20 à 23, `MeleeBlockHigh/Low/Left/Right` 27 à 30, `StartCrouching` 32, `StopCrouching` 33,
`ExecuteSecondaryAttack` 48 (coup de crosse), `StartBayonetAttach` 63, `StartBayonetDetach` 64, gestes `PlayerGestureSalute` 44,
`PlayerGestureSurrender` 52, `PlayerGestureCharge` 71, `PlayerGestureCheer` 75, `PlayerGesturePanic` 69.
[TEST] : rechargement complet avec `StartReloadFirearm` seul ; baïonnette avec `StartBayonetAttach` seul ; accroupi : `Crouch` (bascule ?)
ou `StartCrouching`/`StopCrouching`.

Précisions relevées après le premier essai en jeu [CODE] :
- **Une commande `playerAction` remplace la précédente** : `SetPlayerActions` écrit `queuedPlayerActions = actions`, lu une seule fois
  au `FixedUpdate` suivant puis remis à `null` (`ServerCarbonPlayersManager.FixedUpdate`). Deux commandes envoyées avant ce
  `FixedUpdate` : seule la seconde est jouée. Pour plusieurs actions à la fois, une seule commande `playerAction A B <id>`.
- Dans un paquet, `CreatorWeaponHolder.ProcessClientFirearmWeaponActions` traite dans cet ordre : tir (5), épauler (3), désépauler (4),
  baïonnette (63 à 68), recharger (12), interrompre (13), finir (14).
- **Recharger** (`WeaponHolder.CanReloadFirearm`) est refusé en joue (hors Frontlines), en baïonnette, en soin, à la nage, ou si un
  rechargement est déjà en cours ; il est permis en marchant ou en courant. Désépauler d'abord (paquet à part, ou avant dans le même).
- **Le rechargement ne se termine que sur `FinishReloadFirearm` (14)** : il met `queuedFinishFirearmReload`, et
  `HandleQueuedFinishedFirearmReload` termine (arme chargée, munitions) quand `reloadTime × 0,75` est écoulé (× 0,5 pour une arme à
  projectile physique). On peut donc envoyer 14 juste après 12 : le jeu attend le bon moment. Sans 14, l'arme reste vide.
- Durée : `reloadTime` de l'arme (12 s par défaut si inconnue), divisée par le multiplicateur de vitesse (buffs) (`GetReloadDuration`).
- **Baïonnette** (`CreatorWeaponHolder.ProcessClientFirearmWeaponActions`, `HandleQueuedFinishBayonetAttach/Detach`,
  `_InterruptFirearmActions`, `PlayerBase.ToggleCombatStance`) :
  - `StartBayonetAttach` (63) / `StartBayonetDetach` (64) ne vérifient **pas** l'état actuel de la baïonnette (`CanAttachBayonet` :
    capacité de classe, arme à feu en main, pas en joue, pas en rechargement, pas déjà en pose, pas en garde de mêlée) ;
  - la pose (ou le retrait) ne se termine **que** sur `FinishBayonetAttach` (67) / `FinishBayonetDetach` (68), appliqué dès l'Update
    suivant, **sans attendre l'animation** : au mod d'attendre la durée de l'animation avant d'envoyer 67/68 ;
  - une pose non terminée est interrompue par le passage en garde de mêlée (`EnableCombatStance` 15, ajouté par toute frappe
    `MeleeStrike*` et par `MeleeStrikeRandom`), une parade (24), un changement d'arme ou une interruption des actions d'arme : le jeu
    exécute alors l'interruption **et la fin de l'opération inverse** (`FinishBayonetAttaching(false)` puis
    `FinishBayonetDetaching(true)`) : la baïonnette est retirée ;
  - à l'apparition, l'arme a la baïonnette **au canon** si la rotation a `start_bayonet_attached` vrai (défaut du jeu : vrai,
    `GameDetails.DefaultStartBayonetAttached`) ; l'état n'est pas transmis aux mods (diffusé aux clients par
    `BroadcastBayonetStateChange`).
- **Instruments** : à l'apparition un musicien ne tient **rien** (le joueur sort son instrument par une touche ; l'instrument
  permis dépend de l'uniforme, donnée de scène `AllowedSpecialCarryableObject`). `carbonPlayers equipCarryableItem <objet> <id>`
  le met en main sans contrôle (`TryEquipCarryableObjectToPlayer` → `BroadcastPlayerStartCarry`), et le jeu appelle alors
  `OnPlayerStartCarry` chez les mods (`CarryableObjectManager`) [CODE].
- **Instruments** (`ServerInstrumentSongManager.TryRequestInstrumentSongPlayer`) : le bot doit **tenir** l'instrument demandé
  (objet porté, `ResolvePlayerCarryableInstrument`) ; deux demandes du même joueur à moins de **3 s** sont refusées ; la commande
  répond « ok » même en cas de refus. La liste des morceaux n'est pas côté serveur (données des clients) ; le serveur n'arrête un
  morceau que sur demande, mort du musicien ou instrument lâché (pas de fin à la durée) [CODE].
- Tir d'un bot : `CanShootFirearm` n'est pas exigé (`IsCarbonPlayer`) ; arme vide → tir à vide (31) au lieu de 5.
- Vitesses par défaut (`CommonGlobalVariables.MovementData`) : marche 1,5 m/s (de côté 1,5, arrière 1,5), course 3,36 m/s (de côté 2,4,
  arrière 2,1), multipliées par `characterWalkForwardSpeed` / `characterRunForwardSpeed` (1 par défaut) et les buffs.
- Rotation : à chaque paquet, le corps rejoint le cap par `Mathf.LerpAngle(…, characterRotationInterpolator)`, 0,6 par défaut
  (`ServerGlobalVariables`, `uLinkStrictPlatformerCreator`) : quasi immédiat (quelques `FixedUpdate`).
- Ordre « Former la ligne » : `orderPosition` et `orderRotationY` sont la position et le cap Y de l'aperçu du marqueur posé par
  l'officier (`UIHighCommandOrderPanelManager.HandlePlacingLineInput`, cap initial = celui de l'officier), et le serveur pose le
  marqueur exactement là (`ServerHighCommandOrderManager._HandleOfficerFormLine`). La forme du marqueur (trait centré sur le point,
  perpendiculaire au cap) est dans le prefab, pas dans le code [TEST]. Les ordres sans point (feu, charge…) ont une position (0,0,0).

---

## 2. Créer un bot : `spawnSpecific`

### 2.1 Syntaxe [CODE] (`ServerCarbonPlayersConsoleCommand.Execute`, `ServerCarbonPlayersManager.AddCarbonPlayers(FactionCountry, …)`)
```
carbonPlayers spawnSpecific <faction> <classe> [nom] [tag] [uniformId]
```
- `faction`, `classe` : nom ou valeur (`British` ou `1`, `ArmyLineInfantry` ou `1`, `Guard` ou `16`).
- `nom` : **pas de préfixe automatique**, c'est le nom complet. Espaces : guillemets, ou le caractère U+2002 (espace demi-cadratin) qui est
  remplacé par une espace. Coupé à 32 caractères (`SanitizePlayerName`). Vide → 10 lettres au hasard.
- `tag` : n'est appliqué **que s'il est déjà porté par un vrai joueur connecté** (le bot copie alors tag, bannière, score de régiment) ;
  sinon le bot n'a pas de tag. Comme un argument vide disparaît, **pour donner un `uniformId` il faut un tag bidon** (`x`).
- `uniformId` : voir §4. Inconnu → uniforme 1 de la classe (`TryResolveUniformFor`).
- **Position et rotation : aucun argument.** Le bot apparaît sur une section d'apparition **au hasard** de sa faction ; le mod le place
  ensuite avec `teleport <id> x,y,z` (et `inputRotation`).
- Pas de nombre : une commande = un bot. Sortie : « Attempting to spawn a carbon player of British ArmyLineInfantry » (**sans l'id**).
  `success=false` (« Error ») seulement si la carte n'est pas encore chargée.

### 2.2 Ce que fait le jeu [CODE] (`GenerateCarbonPlayerChosenSpawnSettings`)
1. **Faction** : gardée si c'est l'attaquant **ou** le défenseur de la manche ; sinon tirée au hasard entre les deux.
2. **Section d'apparition** : une au hasard parmi celles de la faction (`QuerySpawnSections`, `RequiresSpecifiedPlayerClasses`).
3. **Classe** : gardée si elle est permise dans **cette** section ; sinon classe au hasard de la section.
4. **Uniforme** : `ResolveUniformFor(faction, classe, uniformId)`, sinon uniforme au hasard de la classe.
5. Tête, voix (langue de l'uniforme), armes par défaut de l'uniforme, `ClassRank` 1 à 49, cheval si la classe monte à cheval.
6. Demande d'apparition « manuelle » (`IsManualRespawn = true`) mise en file : `ServerGameManager.QueueClientChosenSpawnSettings`.
   La **première** apparition passe à la boucle suivante (vague `Initial`, `HandlePlayerSpawningLoop`), pas à la vague de réapparition.
7. Contrôles normaux d'apparition (`ServerGameMode.CanPlayerSpawn`, `SpawnSectionManager.CanPlayerSpawnOnSpawnSection`) :
   uniforme existant, **places de la section et places de classe** (`ResolvePlayerClassMaxSlots`), renforts de faction, fermeture des
   apparitions, nombre maximal de réapparitions. **Refus = le bot reste connecté sans apparaître** (voir §2.4).

Toutes les DLC sont considérées comme possédées par le bot (`InitiateCarbonPlayer`) et les déblocages d'uniforme ne sont pas vérifiés.

### 2.3 Récupérer l'id du bot [CODE]
Ordre des événements pour un `spawnSpecific` (`InitiateCarbonPlayer` → `ServerGameManager.HandleAuthentication` → `SkipAuthentication`
→ `PostAuthentication`) :
1. **Pendant** l'appel `ExecuteConsoleCommand` (synchrone) : `IHoldfastSharedMethods3.OnPlayerConnected(id, false, backendId = "")`.
   Un `backendId` vide signale un bot. C'est le moyen le plus sûr : le mod note l'id juste avant le retour de `ExecuteConsoleCommand`.
2. Puis `OnConsoleCommand` (sortie sans id).
3. À l'apparition (boucle suivante si accepté) : `OnPlayerJoined(id, steamId=0, nomAffiché, tag, **isBot=true**)`
   (`InstantiatePlayerInRound`), puis `OnPlayerSpawned(id, section, faction, classe, uniformId, objet)`.
4. Si l'apparition est refusée : ni `OnPlayerJoined` ni `OnPlayerSpawned`.

Le nom choisi (préfixe propre au mod + numéro) permet de recouper dans `OnPlayerJoined`. `OnPlayerSpawned` donne la **faction et la
classe réelles** (peut-être différentes de la demande, §2.2) : toujours les vérifier.

### 2.4 Réapparition automatique : piège [CODE] (`ServerCarbonPlayersManager.FixedUpdate`, `SpawnCarbonPlayer`)
- `allowAutoRespawn` vaut **true** par défaut et est **global**.
- Un bot mort (5 s après la dernière blessure) ou jamais apparu est remis en file avec `SpawnCarbonPlayer(networkPlayer)` **sans faction
  ni classe** → **faction et classe au hasard**. La demande d'origine n'est pas mémorisée.
- Avec `allowAutoRespawn false` (ce que faisait Commander Battles) : un bot mort reste mort et connecté ; un bot dont la première
  apparition est refusée reste dans les limbes. **Le mod doit retirer lui-même ces bots** (délai sans `OnPlayerSpawned`).
- Retirer un bot : `serverAdmin kick <id>` → `ServerBannedPlayersManager.KickPlayer` → `ServerConnectionManager.DisconnectPlayer` ne
  trouve pas de connexion réseau → `ServerCarbonPlayersManager.DisconnectPlayer` → `PlayerDisconnected(isCarbonPlayer: true)` →
  `RemoveCarbonPlayer`. La sortie dit « Unable to kick » mais le bot est bien retiré [TEST]. **Effets de bord** : `OnAdminPlayerAction(Kick)`
  chez les mods, ligne dans le journal des actions admin et **message au webhook Discord admin s'il est configuré**. `OnPlayerLeft` arrive
  si le bot était apparu.
- Changement de manche ou de carte (`ServerGameManager.ChangeGameMode` → `UnloadFromMap`) : **tous les bots sont supprimés**.

---

## 3. Places, limites, camp

### 3.1 Un bot occupe-t-il une place de joueur ? [CODE]
**Non pour `max_players`** :
- Les bots ne passent pas par `uLink_OnPlayerConnected`, donc ne sont pas dans `uLinkNetworkConnectionsCollection.connectedConnections`.
- `InGamePlayers = connectedConnections.Count - queuedConnections.Count` (`uLink.Runtime.dll`) est ce que compare
  `ServerConnectionManager.CanPlayerEnterServer` à `MaxPlayers` : les bots n'empêchent pas un vrai joueur d'entrer.
- Le serveur annoncé (`ServerLobbyManager`, `CurrentPlayersTotal = connectedConnections.Count`) ne compte pas les bots.

**Mais ils prennent des places en jeu** :
- **places des sections d'apparition et places de classe** (`CanPlayerSpawnOnSpawnSection` compte tous les `RoundPlayer`) : des bots
  porte-drapeau, officiers ou gardes peuvent priver de vrais joueurs de ces classes limitées ;
- renforts de faction (`UsingTroopReinforcements`) et réapparitions limitées comme un joueur.

### 3.2 Limite de bots [CODE]
- **Aucune limite explicite** dans `ServerCarbonPlayersManager` (la liste est créée avec une capacité de 300, ce n'est pas une limite).
- Ids pris dans le même réservoir que les vrais joueurs (`uLink.NetworkClientAllocator.Allocate`, plage `minClient..maxClient`) ; en cas
  d'épuisement, `NetworkPlayer.unassigned` [TEST : jamais atteint en pratique].
- Limites réelles : places des sections et des classes, et le **CPU** (chaque bot simule un paquet joueur à chaque `FixedUpdate`).
- Chaque bot déclenche **`OnPlayerPacket` à chaque `FixedUpdate`** chez tous les mods (`uLinkStrictPlatformerCreator.HandleOwnerPacketToServer`) :
  le mod doit sortir tout de suite pour les ids qu'il n'observe pas.

### 3.3 Bots dans l'autre camp [CODE]
**Oui.** `spawnSpecific` accepte l'attaquant comme le défenseur de la manche (`GenerateCarbonPlayerChosenSpawnSettings`), quel que soit le
camp de l'officier. Une faction qui n'est pas dans la manche est remplacée au hasard. Aucun équilibrage d'équipe n'est appliqué aux bots.
Il faut que la faction ait une section d'apparition qui accepte la classe voulue. Les lignes ennemies menées par l'IA (n° 12) sont donc
possibles ; un bot officier ennemi peut même donner de vrais ordres avec `highCommandOrder` (§5.3).

---

## 4. Uniformes (`uniformId`)

### 4.1 Comment le jeu les désigne [CODE]
- `ModelUniformDataRepository` (`HoldfastGame/ModelUniformDataRepository.cs`) indexe les uniformes par **(faction, classe, uniformId)**.
  L'id est **propre à chaque couple faction/classe** et commence à 1.
- `spawnSpecific … <uniformId>` → `ResolveUniformFor` ; id absent → **uniforme 1** de la classe ; `-1` → au hasard.
- `OnPlayerSpawned.uniformId` = `CharacterUniformIdentifier` = l'uniforme choisi, ou l'un de ses `AltUniformIds` (aucun uniforme de base
  n'en a dans les données lues, donc égal à l'id demandé).
- Les données sont des composants `ModelUniformDataItem` (`Uniforms.Runtime.dll`) posés dans la scène du serveur, pas dans le code.
  Champs : `UniformId`, `FactionCountry`, `PlayerClass`, `Unlock`, `AltUniformIds`, `DefaultWeaponSlot1..3`, `HeadIds`, `RegimentName`…
- Les mods d'uniformes (`AddModdedUniformData`) ajoutent des ids, et une rotation avec `ExclusiveModdedUniforms` retire ceux du jeu de
  base pour la faction. Les uniformes d'Halloween ne sont chargés qu'en période d'Halloween.

### 4.2 Liste lue dans le serveur
Source : fichier `Holdfast NaW_Data/level1` du serveur, lu octet par octet (285 `ModelUniformDataItem`, script `pathID 0x16C`), noms d'enum
du §0. **[CODE] pour la lecture, [TEST] pour le rendu en jeu.** `*` = uniforme lié à un déblocage (DLC ou ordre) ; sans effet pour un
bot. Les musiciens ont plusieurs ids pour le même régiment (sans doute fifre, tambour, cornemuse… [TEST]).

**British (1)**

- Ligne (`ArmyLineInfantry`=1) : `1` 23rd Royal Welch Fusiliers · `2` 92nd Gordon Highlanders · `3` Loyal Lusitanian Legion* · `4` Loyal Inverness Fencible Highlanders*
- Officier (`ArmyInfantryOfficer`=6) : `1` 23rd Royal Welch Fusiliers · `2` 92nd Gordon Highlanders* · `3` Loyal Inverness Fencible Highlanders*
- Sergent (`Sergeant`=20) : `1` 23rd Royal Welch Fusiliers
- Porte-drapeau (`FlagBearer`=12) : `1` 23rd Royal Welch Fusiliers · `2` Loyal Inverness Fencible Highlanders*
- Musicien (`Musician`=19) : `1` Royal Navy · `2` Royal Navy · `3` 56th West Essex · `4` 56th West Essex · `5` 92nd Gordon Highlanders · `6` Royal Navy* · `7` 56th West Essex*
- Garde (`Guard`=16) : `1` 1st Foot Guards, St James’ Palace · `2` Coldstream Regiment of Foot Guards*
- Grenadier (`Grenadier`=18) : `1` 41st Regiment of Foot · `2` Belgian 7th Line Battalion*
- Infanterie légère (`LightInfantry`=11) : `1` 93rd Sutherland Highlanders
- Tirailleur (`Rifleman`=10) : `1` 95th Rifle Regiment
- Autres classes présentes : NavalMarine (2), NavalCaptain (2), NavalSailor (5), NavalSailor2 (1), CoastGuard (1), Carpenter (1), Surgeon (2), Customs (1), Cannoneer (2), Sapper (1), Hussar (1), Dragoon (1)

**French (2)**

- Ligne (`ArmyLineInfantry`=1) : `1` 85ème Régiment d'Infanterie · `2` Légion de la Vistule · `3` 6th ‘Herzog Wilhelm’ Bavarian Fusiliers* · `4` 18e Demi-Brigade d'Infanterie 'Les Braves'*
- Officier (`ArmyInfantryOfficer`=6) : `1` 85ème Régiment d'Infanterie · `2` Polish Vistula Legion* · `3` Napoleon I*
- Sergent (`Sergeant`=20) : `1` 85ème Régiment d'Infanterie
- Porte-drapeau (`FlagBearer`=12) : `1` 85ème Régiment d'Infanterie
- Musicien (`Musician`=19) : `1` French Navy · `2` French Navy · `3` 67e Régiment d'Infanterie · `4` 67e Régiment d'Infanterie · `5` French Navy* · `6` 67e Régiment d'Infanterie*
- Garde (`Guard`=16) : `1` 2e Régiment de la Garde Impériale · `2` 1er Grenadiers de la Garde Impériale*
- Grenadier (`Grenadier`=18) : `1` 3ème Régiment d'Infanterie · `2` Polish 4th Infantry Regiment*
- Infanterie légère (`LightInfantry`=11) : `1` 10ème Régiment d'Infanterie · `2` Guides à Pied de Bonaparte*
- Tirailleur (`Rifleman`=10) : `1` Tirailleurs Corses
- Autres classes présentes : NavalMarine (2), NavalCaptain (1), NavalSailor (3), NavalSailor2 (1), CoastGuard (1), Carpenter (1), Surgeon (2), Customs (1), Cannoneer (2), Sapper (1), Hussar (2), Dragoon (1)

**Prussian (3)**

- Ligne (`ArmyLineInfantry`=1) : `1` Colbergsches Infanterie-Regiment · `2` Lützow Freikorps · `3` 11. Reserve Regiment*
- Officier (`ArmyInfantryOfficer`=6) : `1` Colbergsches Infanterie-Regiment · `2` Lützow Freikorps*
- Sergent (`Sergeant`=20) : `1` Colbergsches Infanterie-Regiment
- Porte-drapeau (`FlagBearer`=12) : `1` Colbergsches Infanterie-Regiment
- Musicien (`Musician`=19) : `1` à `4` Colbergsches Infanterie-Regiment (`3`*, `4`*)
- Garde (`Guard`=16) : `1` Garde-Regiment Zu Fuss · `2` Zweites Garde-Regiment Zu Fuss*
- Grenadier (`Grenadier`=18) : `1` 1. Berg Grenadiere · `2` 1. Schlesisches Infanterie-Regiment*
- Infanterie légère (`LightInfantry`=11) : `1` Schlesische Landwehr
- Tirailleur (`Rifleman`=10) : `1` Schlesische Schützen
- Autres classes présentes : Carpenter (1), Surgeon (2), Cannoneer (2), Sapper (1), Hussar (1), Dragoon (1)

**Russian (4)**

- Ligne (`ArmyLineInfantry`=1) : `1` Mogilev Infantry Regiment · `2` Interior Guard Battalion · `3` Pavlovsk Fusiliers*
- Officier (`ArmyInfantryOfficer`=6) : `1` Mogilev Infantry Regiment · `2` Interior Guard Battalion*
- Sergent (`Sergeant`=20) : `1` Mogilev Infantry Regiment
- Porte-drapeau (`FlagBearer`=12) : `1` Mogilev Infantry Regiment
- Musicien (`Musician`=19) : `1` à `4` Mogilev Infantry Regiment (`3`*, `4`*)
- Garde (`Guard`=16) : `1` Semyonovsky Regiment · `2` Leib-Gvardii Litovskii Polk*
- Grenadier (`Grenadier`=18) : `1` Pavlovsk Grenadiers · `2` Nizhnii-Novgorod Opolcheniye*
- Infanterie légère (`LightInfantry`=11) : `1` 24th Jaegers
- Tirailleur (`Rifleman`=10) : `1` Russian-German Legion
- Autres classes présentes : Carpenter (1), Surgeon (2), Cannoneer (2), Sapper (1), Hussar (1), Dragoon (1)

**Italian (5)**

- Ligne (`ArmyLineInfantry`=1) : `1` 7° Reggimento Fanteria di Linea · `2` Reggimento Real Veterani · `3` Legione Reale Dalmata*
- Officier (`ArmyInfantryOfficer`=6) : `1` 7° Reggimento Fanteria di Linea · `2` Battaglione Guardia Alla Città di Milano*
- Sergent (`Sergeant`=20) : `1` 7° Reggimento Fanteria di Linea
- Porte-drapeau (`FlagBearer`=12) : `1` 7° Reggimento Fanteria di Linea
- Musicien (`Musician`=19) : `1` à `4` 7° Reggimento Fanteria di Linea (`3`*, `4`*)
- Garde (`Guard`=16) : `1` Guardia Reale Italiana · `2` Marinai Della Guardia Reale*
- Grenadier (`Grenadier`=18) : `1` 6° Reggimento Fanteria di Linea · `2` Battaglione Guardia Alla Città di Milano*
- Infanterie légère (`LightInfantry`=11) : `1` 1° Reggimento Fanteria Leggera
- Tirailleur (`Rifleman`=10) : `1` Bersaglieri Bresciani
- Autres classes présentes : Carpenter (1), Surgeon (2), Cannoneer (2), Sapper (1), Hussar (1), Dragoon (1)

**Austrian (8)**

- Ligne (`ArmyLineInfantry`=1) : `1` K.K. IR 22 'Coburg' · `2` K.K. IR 62 'Banat' · `3` Ungarische Insurrection*
- Officier (`ArmyInfantryOfficer`=6) : `1` K.K. IR 22 'Coburg' · `2` K.K. IR 22 'Coburg' Overcoat*
- Sergent (`Sergeant`=20) : `1` K.K. IR 22 'Coburg'
- Porte-drapeau (`FlagBearer`=12) : `1` K.K. IR 22 'Coburg'
- Musicien (`Musician`=19) : `1` à `4` K.K. IR 22 'Coburg' (`3`*, `4`*)
- Garde (`Guard`=16) : `1` K.K. Grenadier Battalion 'Janusch' · `2` Herzoglich Braunschweigisches Feldkorps*
- Grenadier (`Grenadier`=18) : `1` K.K. Grenadier Battalion 'Locher' · `2` Serbische Freikorps*
- Infanterie légère (`LightInfantry`=11) : `1` 3rd Battalion 'Königgrätz'
- Tirailleur (`Rifleman`=10) : `1` Feldjägerbataillon N° 'Arno' Jägers
- Autres classes présentes : Carpenter (1), Surgeon (2), Cannoneer (2), Sapper (1), Hussar (1), Dragoon (1)

**ARBritish (11)**

- Ligne (`ArmyLineInfantry`=1) : `1` 33rd Regiment of Foot · `2` 42nd Black Watch Highlanders* · `3` New Hampshire Line* · `4` Hessian Musketers*
- Officier (`ArmyInfantryOfficer`=6) : `1` 33rd Regiment of Foot · `2` General Cornwallis*
- Sergent (`Sergeant`=20) : `1` 33rd Regiment of Foot
- Porte-drapeau (`FlagBearer`=12) : `1` 33rd Regiment of Foot · `2` 42nd Black Watch Highlanders*
- Musicien (`Musician`=19) : `1` à `4` 33rd Regiment of Foot · `5` 42nd Black Watch Highlanders · `6`, `7` 33rd Regiment of Foot*
- Garde (`Guard`=16) : `1` 1st Foot Guards
- Grenadier (`Grenadier`=18) : `1` Land-Grenadier-Regiment Rall
- Infanterie légère (`LightInfantry`=11) : `1` 1st Light Infantry Battalion · `2` Butler's Rangers* · `3` Mohawk Warrior Native*
- Tirailleur (`Rifleman`=10) : `1` Ansbach-Bayreuth Jäger
- Autres classes présentes : Carpenter (1), Surgeon (2), Cannoneer (1), Sapper (1), Hussar (1), Dragoon (1)

**ARAmerican (12)**

- Ligne (`ArmyLineInfantry`=1) : `1` Hall's Delaware Regiment · `2` North Carolina Continental Line* · `3` Connecticut Minute Company* · `4` Green Mountain Boys*
- Officier (`ArmyInfantryOfficer`=6) : `1` Hall's Delaware Regiment · `2` General Washington*
- Sergent (`Sergeant`=20) : `1` Hall's Delaware Regiment
- Porte-drapeau (`FlagBearer`=12) : `1` Hall's Delaware Regiment · `2` 1st Rhode Island Regiment*
- Musicien (`Musician`=19) : `1` à `5` Hall's Delaware Regiment (`4`*, `5`*)
- Garde (`Guard`=16) : `1` Washington's Guard
- Grenadier (`Grenadier`=18) : `1` Régiment De Soissonnais · `2` George Washington Grenadiers*
- Infanterie légère (`LightInfantry`=11) : `1` 4th Massachusetts Light Infantry · `2` Quaker Blues* · `3` Frontiersman* · `4` Oneida Native American Warrior*
- Tirailleur (`Rifleman`=10) : `1` Kentucky County Riflemen · `2` Morgan's Riflemen*
- Autres classes présentes : Carpenter (1), Surgeon (2), Cannoneer (1), Sapper (1), Hussar (1), Dragoon (1)

**Allied (6)** (Frontlines) : Ligne `1` Durham Light Infantry · `2` 103rd Infantry Regiment* · `3` 6th Victoria Battalion* · `4` Durham Light Infantry Winter Uniform* ;
Officier `1` Durham Light Infantry · `2` 103rd Infantry Regiment* · `3` 6th Victoria Battalion* ; aussi Carpenter, Surgeon, Cannoneer, Sapper.

**Central (7)** (Frontlines) : Ligne `1` 2. Westfälisches Nr. 15 · `2` 57'nci Piyade Alayı* · `3` 2. Westfälisches Nr. 15 Winter Uniform* ;
Officier `1` 2. Westfälisches Nr. 15 · `2` 57'nci Piyade Alayı* ; aussi Carpenter, Surgeon, Cannoneer, Sapper.

**Privateer (9)** et **Spanish (10)** : **aucun uniforme d'infanterie de terre** (seulement marine, capitaine, marins, charpentier,
chirurgien, musicien). Une ligne de bots y est impossible : la classe serait remplacée au hasard.

---

## 5. Ordres d'officier et phrases criées

### 5.1 `OnOfficerOrderStart` déclenche-t-il `OnPlayerShout` pour l'officier ? [CODE] Non.
- `ServerHighCommandOrderManager.HandleRequestStartHighCommandOrder` choisit la phrase de l'ordre (`highCommandOrderVoicePhraseDictionary`)
  et seulement son **numéro de variante** (`ResolvePhraseRandomVariationIndex`) → `voicePhraseRandomIndex`.
- Ce numéro part avec l'ordre ; **chaque client** joue la phrase lui-même (`ClientHighCommandOrderManager`, `PlayCharacterVoicePhrase`).
- `OnPlayerShout` n'est appelé que par `ServerCharacterVoicesManager.HandleClientVoicePlayRequest`, que l'ordre n'appelle pas pour l'officier.
- **Mais** les soldats de la ligne réagissent : pour les ordres qui ont des `lineOrderReactCheers` (données de `HighCommandOrderInfoRepository`),
  chaque membre de la ligne (`playersInworldOrder` de l'ordre ou de la ligne formée) reçoit `QueueCheerPhrase` avec un délai, puis
  `ServerRoundPlayer._ManualUpdate` → `HandleClientVoicePlayRequest` → **`OnPlayerShout(idDuSoldat, acclamation)`**. Nos bots dans la
  ligne d'un officier vont donc crier et déclencher `OnPlayerShout` [TEST : quelles phrases].
- `carbonPlayers voicePhrase` déclenche aussi `OnPlayerShout` (même méthode).

→ **Règle pour le mod : ignorer tout `OnPlayerShout` venant d'un bot.** Le filtre « même ordre dans la seconde » prévu dans PLAN.md
n'est pas nécessaire pour l'officier lui-même ; il reste utile par prudence [TEST].

Phrase jouée pour chaque ordre (`HighCommandOrderManager.highCommandOrderVoicePhraseDictionary`) : OfficerReinforce → Help, OfficerSquadCamp → Help,
OfficerCharge → Charge, **OfficerFormLine → StandGround**, OfficerMakeReady → MakeReady, OfficerFire → Fire, OfficerFireAtWill → FireAtWill,
OfficerCeaseFire → CeaseFire, OfficerBreakRankAndFollow → Compliment, Sergeant* : idem (SergeantRally → PatrioticCheer),
Raid/Dash/SteadyAim/HandGrenade/SatchelCharge → aucune.

### 5.2 Phrases du menu vocal [CODE]
`HoldfastGame/CharacterPhrasesRepository.cs` `InitializePhrasesList`, `HoldfastGame/UIVoicesManager.cs` `CreateAndSortAllSpeechCategories` :
- **Aucune liste par classe.** Tout joueur a les mêmes catégories ; une phrase n'apparaît que si **sa voix** (langue de l'uniforme) l'a
  enregistrée (`ResolvePhrase(phrase, voix) != null`). « Navigation » est masquée en mode Frontlines.
- Le serveur accepte toute phrase d'un joueur vivant (`ServerCharacterVoicesManager._IsPlayerAllowedToSpeak` renvoie `true`).
- Le client limite : un délai entre deux phrases (`characterVoicePlayInterval`) et **5 phrases par 30 s** (`maxConsecutiveVoicePlayRequestsPerInterval`).
  Un officier ne peut donc pas enchaîner les ordres vocaux. À prendre en compte pour les ordres par phrases.

| Catégorie | Phrases |
|---|---|
| Exercice au mousquet (`musketDrillsPhrases`) | CeaseFire, Charge, Fire, FireAtWill, FirstRankFire, PresentArms, PrimeLoad, RemoveBayonet, SecondRankFire, SectionsFire, ShoulderArms, TakeAim, FormColumn, ForwardMarch, Follow, Halt, AtTheDouble, CloseRanks, DoubleRank, SingleRank, Reinforce |
| Interactions (`interactionPhrases`) | Cheer, Brains, Compliment, GetDown, GoodShot, Help, Insult, Medic, Mutiny, NoSir, Retreat, Salute, StandGround, StayCalm, Surrender, Taunt, Thanks, YesSir, Warcry, Duel, PatrioticCheer |
| Canons et fusées (`gunsAndRocketDrillsPhrases`) | AimHigher, AimLower, AimRockets, LoadCanisterShot, LoadCaseRocket, LoadChainShot, LoadRocket, LoadRoundShot, LoadShot, LowerGuns, LowerRockets, **MakeReady**, RaiseGuns, RaiseRockets, RamShot, ReadyGuns, RunOutGuns, SightGuns, Swivel |
| Navigation (`sailingAndNavigationPhrases`) | AbandonShip, BailShip, BelayOrder, … WetSail (31 phrases navales) |
| Touches directes | Insult, HurrahCheer, PatrioticCheer (`UIVoicesManager`) |

**`FixBayonet` n'est dans aucune catégorie** : impossible à crier depuis le menu. Correspondances de PLAN.md à revoir : baïonnette au canon
(utiliser autre chose, ou `RemoveBayonet` en bascule) ; `MakeReady` est dans la catégorie canons (mais criée par l'ordre officier) ;
`GetDown`, `StandGround`, `Retreat` sont dans « Interactions ». [TEST] : quelles phrases chaque voix (britannique, française…) possède.

### 5.3 Ordres donnés par un bot [CODE]
`carbonPlayers highCommandOrder <id> <ordre> <x,y,z> <rotY>` met la demande dans `serverHighCommandOrderManager.queuedStartOrderPackets`
comme un vrai ordre : contrôle de capacité de classe (`HasAbility`), `CanExecuteStartOrder`, délais. Un bot `ArmyInfantryOfficer` peut
donc **former une vraie ligne** (`OfficerFormLine`), donner feu, charge… et `OnOfficerOrderStart(idDuBot, …)` est appelé : le mod doit
le reconnaître comme un bot. Utile pour les lignes ennemies IA.

---

## 6. Buffs d'officier et de sergent : quand ils démarrent [CODE]
`OnBuffStart(id, buff)` est appelé pour **chaque joueur** de la liste (`BuffManager.HandleBuffStartPacket`).
Sources (`HoldfastGame/ServerHighCommandOrderManager.cs`) :

| Buff | Déclencheur |
|---|---|
| `OfficerInLine` (8) | un joueur entre dans la zone du marqueur de ligne d'un officier (`HighCommandLineOrderProximitySensor` → `HookPlayerToLine` → `_HookPlayerToLine`). Remplace `SergeantInLine`. |
| `SergeantInLine` (12) | idem pour la ligne d'un sergent (`SergeantFormLine`), si le joueur n'est pas déjà dans une ligne d'officier. |
| `OfficerLineFire` (10), `OfficerLineFireAtWill` (11) | ordre `OfficerFire` / `OfficerFireAtWill` : tous les membres de la ligne (`_HandleOfficerFire…`) ; aussi à l'entrée dans la ligne si c'était le dernier ordre. |
| `SergeantLineFire` (14), `SergeantLineFireAtWill` (15) | idem avec `SergeantFire` / `SergeantFireAtWill`. |
| `OfficerLineCharge` (9) | ordre `OfficerCharge` : l'officier, sa ligne et les joueurs proches de sa faction (`GetChargeNearbyPlayerIds`, grille de buffs). |
| **`SergeantLineRally` (13)** | **ordre `SergeantRally` d'un sergent** (`_HandleSergeantRally`) : le sergent, sa ligne, les joueurs proches. Effet sur la vitesse (`PlayerMovementSpeedManager`, `InLineRallyBuffProperties`). Pas d'autre source. |
| `StandardBearer` / `Companion` | porte-drapeau (`ServerPlayerAuraBuffManager`), compagnons dans son aura. |
| `Musician` / `Companion` | musicien qui joue (`ServerInstrumentSongManager`), y compris un bot avec `instrumentPlay` ; compagnons proches (vitesse). |
| `Guardsman` / `Companion` | à l'apparition d'un garde (`ServerPlayerSpawningHandler`). |
| `CarpenterBerserk`, `Raid`, `Dash`, `SteadyAim`, `CaptainBoardingAction` | ordres de même nom. |

Fins : `OnBuffStop` à l'arrêt de l'ordre et à la mort (`ServerBuffManager`, `TryBroadcastBuffStop` pour tous les buffs).
Pour le moral (n° 4) : `SergeantLineRally` n'arrive que si un **sergent** donne « Rally ». Un bot sergent peut le donner avec
`highCommandOrder <idSergent> SergeantRally <x,y,z> <rot>` [TEST].

---

## 7. Mouvement et orientation des bots [CODE]

Chaîne : `ServerCarbonPlayersManager.FixedUpdate` → `UpdateCarbonPlayerInput` (un paquet joueur simulé par bot et par `FixedUpdate`)
→ `uLinkStrictPlatformerCreator.HandleOwnerPacketToServer` / `ProcessQueuedOwnerPacket` → `RigidbodyCharacter.Move`.

- **`inputAxis x y`** : joystick. `x` = pas de côté (+ droite), `y` = avant (+) / arrière (−), **relatifs au corps**.
  Normalisé seulement si la longueur dépasse 1 ; une valeur plus petite **réduit la vitesse en proportion**
  (`GetDesiredMove` : `avant * y * vitesse + droite * x * vitesse_latérale`). Vitesses : `characterWalk*Speed`, `characterRun*Speed`
  (`set`), avec les bonus de buffs. Course : `setRunning true` (si la classe et l'arme le permettent).
- **`inputRotation <deg>`** : cap **absolu** du corps en degrés, angle Y d'Unity (0 = +Z, 90 = +X). Le corps tourne vers ce cap avec
  `Mathf.LerpAngle(…, characterRotationInterpolator)` à chaque `FixedUpdate`. Même convention que `OnOfficerOrderStart.orderRotationY`
  et `OnPlayerPacket.ownerRotationY` [TEST].
- **Sans `forceInputAxis true`/`forceInputRotation true`**, le bot suit l'`AutonomousPlayerInputHandler` : marche et regard **au hasard**
  (si `botsAutoMove` vaut `true`, défaut). Le mod force donc les deux, **par bot**.
- **`pitch`** : pas des degrés. Borné à [-1.5 ; 2]. Converti pour le tir (`UpdateCarbonPlayerInput`, branche `FireFirearm`) :
  `pitch > 0` → caméra levée jusqu'à **60° vers le haut à 2** ; `pitch < 0` → baissée jusqu'à **60° vers le bas à -1.5** ; 0 = horizontal.
  Ne sert qu'à la direction du tir et aux animations [TEST : effet réel sur la balle].
- **`yaw <deg>`** : décalage horizontal de la visée par rapport au corps, borné à ±90°. Rarement utile : tourner le corps suffit.
- `ignoreAutoControls true` ou `CanRotate = false` (par exemple en mêlée) : axe nul et rotation figée.
- `reverseDirectionWhenHitObstacle true` (global) : rayon de 1,5 m devant ; obstacle → cap +180° et `forceInputRotation` activé, 1 s de pause.
  Grossier : à éviter, le mod gère sa propre navigation.
- **Navigation automatique : aucune.** Pas de `NavMesh` dans `Assembly-CSharp` ; `teleportToPlayer` n'existe pas (§1.3).
  Seuls outils : axe + cap calculés par le mod (positions via `OnPlayerPacket.ownerPosition` ou `OnPlayerSpawned.playerObject.transform`),
  `Physics.Raycast` côté mod pour les obstacles, et **`teleport <id> <x,y,z | idJoueur>`** pour placer ou recaler un bot
  (`ServerPlayerTeleportConsoleCommand`, exactement 2 arguments).
- `mimicPlayer` : les bots copient l'admin, mais tant qu'il est actif **plus aucun bot n'est piloté** (`MimicPlayer > 0` → `return`).

---

## 8. Variables `set` utiles
Toutes dans `HoldfastGame/ServerConsoleCommandsInitializer.cs` (`set <nom> <valeur>`, `get <nom>`).

| Variable | Rôle pour les bots |
|---|---|
| `sendFullCameraData true` | les **vrais** joueurs envoient toujours position et direction de caméra → `OnPlayerPacket.cameraPosition/cameraForward` de l'officier (visée de la ligne, cible de charge). |
| `botsAutoMove false` | coupe la marche au hasard des bots créés **ensuite** (lu à la création, `PostHandlingInitialDetails`). Filet de sécurité si `forceInputAxis` est oublié. |
| `characterRotationInterpolator` (0 à 1) | vitesse à laquelle le corps rejoint `inputRotation`. Global, joueurs compris. |
| `serverHandleUsingQueue` | un paquet traité par `FixedUpdate` au lieu de tous : ne pas toucher. |
| `characterWalk*Speed`, `characterRun*Speed` | vitesses (globales). |
| `characterRespawnTime` | délai des vagues ; ne concerne pas la première apparition d'un bot. |
| `orderSystemEnabled` | menu d'ordres officier. |
| `allowCarbonPlayerOrderTracking` | statistiques d'ordres sur les morts de bots (test) : laisser à `false`. |
| `createDynamicSpawnSectionWithReinforceOrder` | l'ordre « Renfort » crée une section d'apparition. |
| `allowFiring`, `characterGodMode`, `characterWeaponDamageScale` | essais. |

Aussi, hors `carbonPlayers` : `teleport`, `serverAdmin kick|slay|slap|revive <id>`, `playerlist`.
Rappel : un `ExecuteConsoleCommand` avec `adminId > 0` est noté dans `logs_adminactions` à chaque appel (`GameConsolePanel.ExecuteInput`) ;
piloter les bots avec **`adminId = -1`**.

---

## 9. Conséquences pour le mod
1. Au démarrage : `carbonPlayers allowAutoRespawn false`, `set botsAutoMove false`, `set sendFullCameraData true`.
2. Créer : `carbonPlayers spawnSpecific <faction> <classe> "<préfixe> <n>" x <uniformId>`, noter l'id dans `OnPlayerConnected`
   (`backendId` vide) pendant l'appel, puis attendre `OnPlayerSpawned` (vérifier faction et classe) ; sans apparition après quelques
   secondes → `serverAdmin kick <id>` (attention au webhook Discord).
3. Tout de suite après l'apparition : `forceInputAxis true <id>`, `forceInputRotation true <id>`, `inputAxis 0 0 <id>`,
   `teleport <id> x,y,z`, `inputRotation <cap> <id>`.
4. Toujours passer l'id ; ignorer `OnPlayerShout`, `OnOfficerOrderStart`, `OnPlayerPacket` des bots sauf besoin.
5. Mort d'un bot : le retirer (kick) et en recréer un si la ligne doit être complétée.
6. Comparer classes et phrases avec les **vraies valeurs** (§0).

---

## 10. Tests en jeu (console admin)
Sur une **instance de test sans joueurs**, après `rc login <mdp>`. Remplacer `<id>` par l'id du bot (colonne de `rc playerlist`),
`<moi>` par votre id, la faction par celle de la carte.

1. Création et id : `rc carbonPlayers spawnSpecific British ArmyLineInfantry "CMD Test 1" x 2` puis `rc playerlist`.
   Le bot apparaît-il tout de suite, avec l'uniforme 92nd Gordon Highlanders ? Son nom ?
2. Camp adverse : `rc carbonPlayers spawnSpecific <factionAdverse> ArmyLineInfantry "CMD Ennemi" x 1` → bon camp ?
3. Places : avec `max_players` bas (ex. 2), créer 5 bots, puis se connecter avec un 2ᵉ compte → entrée acceptée ?
   Le serveur affiche-t-il 1 ou 6 joueurs dans la liste ?
4. Pilotage : `rc carbonPlayers forceInputAxis true <id>`, `rc carbonPlayers forceInputRotation true <id>`,
   `rc carbonPlayers inputRotation 90 <id>`, `rc carbonPlayers inputAxis 0 1 <id>`, `rc carbonPlayers setRunning true <id>`,
   puis `rc carbonPlayers inputAxis 0 0.5 <id>` (marche lente ?) et `rc carbonPlayers inputAxis 0 0 <id>`.
5. Placement : `rc teleport <id> <moi>` puis `rc teleport <id> 100,20,100`.
6. Tir : `rc carbonPlayers pitch 0 <id>`, `rc carbonPlayers playerAction StartAimingFirearm <id>`,
   `rc carbonPlayers playerAction FireFirearm <id>`, `rc carbonPlayers playerAction StartReloadFirearm <id>`,
   puis `rc carbonPlayers pitch 1 <id>` et tirer de nouveau (la balle monte-t-elle ?).
7. Baïonnette, accroupi, mêlée : `rc carbonPlayers playerAction StartBayonetAttach <id>`, `rc carbonPlayers playerAction Crouch <id>`
   (bascule ?), `rc carbonPlayers playerAction MeleeStrikeHigh <id>` puis `rc carbonPlayers playerAction None <id>`.
8. Phrase : `rc carbonPlayers voicePhrase Charge <id>` (le bot crie-t-il ?).
9. Musicien et drapeau : `rc carbonPlayers spawnSpecific British Musician "CMD Tambour" x 3`, puis
   `rc carbonPlayers instrumentPlay <id> MarchingDrum 1` et `rc carbonPlayers instrumentStop <id>` ;
   `rc carbonPlayers spawnSpecific British FlagBearer "CMD Drapeau" x 1` puis `rc carbonPlayers switchWeapon BearingFlag <id>`.
10. Ordre donné par un bot : `rc carbonPlayers spawnSpecific British ArmyInfantryOfficer "CMD Officier" x 1`, puis
    `rc carbonPlayers highCommandOrder <id> OfficerFormLine <x,y,z> 0` (marqueur de ligne visible ?).
11. Ralliement : `rc carbonPlayers spawnSpecific British Sergeant "CMD Sergent" x 1`, se placer près de lui, puis
    `rc carbonPlayers highCommandOrder <id> SergeantRally <x,y,z> 0` (buff de ralliement reçu ?).
12. Réapparition : `rc serverAdmin slay <id>` avec `rc carbonPlayers allowAutoRespawn true` (revient-il avec une autre classe ?),
    puis avec `false` (reste-t-il mort ?).
13. Retrait : `rc serverAdmin kick <id>` → bot retiré malgré « Unable to kick » ? Message Discord admin envoyé ?
14. Sous-commande absente : `rc carbonPlayers teleportToPlayer <id> <moi>` → « Unable to parse command » attendu.
15. Ordres d'officier (vrai joueur officier, ligne de 3 bots formée avec `teleport`) : les bots crient-ils une acclamation après
    « Feu » ou « Former la ligne » ? Le journal `[CMD]` du mod d'essai devra montrer ces `OnPlayerShout`.
