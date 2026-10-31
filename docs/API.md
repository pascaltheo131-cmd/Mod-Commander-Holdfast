# Ce qu'un mod serveur Holdfast peut faire

Référence établie le 31/10/2026 en lisant le code décompilé du **serveur Linux** installé (`Assembly-CSharp.dll` et les
assemblies `HoldfastSharedMethods*`, `HoldfastGameMethods`). Les signatures brutes sont dans [`ref/`](ref/).

Légende : **[CODE]** = confirmé dans le code du serveur. **[TEST]** = à vérifier en jeu.

## 1. Interfaces

| Assembly | Contenu |
|---|---|
| `HoldfastSharedMethods.dll` | `IHoldfastSharedMethods` et ses enums |
| `HoldfastSharedMethods2.dll` | `IHoldfastSharedMethods2` |
| `HoldfastSharedMethods3.dll` | `IHoldfastSharedMethods3` |
| `HoldfastGameMethods.dll` | `HoldfastBridge.IHoldfastGame`, `HoldfastBridge.IHoldfastGameMethods` |

### Seul moyen d'action
```csharp
void IHoldfastGame.OnGameMethodsInitialized(IHoldfastGameMethods m);
bool IHoldfastGameMethods.ExecuteConsoleCommand(string command, out string output, out Exception exception, int adminId);
```
Implémenté par `HoldfastBridgeManager` → `gameConsolePanel.ExecuteInput(command, adminId, …)` [CODE].

### Chargement [CODE] (`BaseModLoaderManager.CheckLoadedStatus`)
- **Une instance par interface** implémentée : garder les méthodes du jeu et tout l'état dans des champs **statiques**.
- Ordre : `OnGameMethodsInitialized` → `OnSyncValueState` → `PassConfigVariables` → `OnIsServer(true)` → `OnIsClient(false, 0)`.

### Événements `IHoldfastSharedMethods`
- Temps : `OnSyncValueState(int)`, `OnUpdateSyncedTime(double)`, `OnUpdateElapsedTime(float)`, `OnUpdateTimeRemaining(float)`
- Partie : `OnIsServer(bool)`, `OnIsClient(bool, ulong)`,
  `OnRoundDetails(int roundId, string serverName, string mapName, FactionCountry attacking, FactionCountry defending, GameplayMode, GameType)`,
  `PassConfigVariables(string[])`
- Joueurs : `OnPlayerJoined(int playerId, ulong steamId, string name, string regimentTag, bool isBot)`, `OnPlayerLeft(int)`,
  `OnPlayerSpawned(int playerId, int spawnSectionId, FactionCountry, PlayerClass, int uniformId, GameObject playerObject)`,
  `OnPlayerHurt(int, byte oldHp, byte newHp, EntityHealthChangedReason)`,
  `OnPlayerKilledPlayer(int killer, int victim, EntityHealthChangedReason, string details)`,
  `OnScorableAction(int, int score, ScorableActionType)`
- Combat : `OnPlayerShoot(int, bool dryShot)`, `OnShotInfo(…)`, `OnPlayerBlock(int att, int def)`,
  `OnPlayerMeleeStartSecondaryAttack(int)`, `OnPlayerWeaponSwitch(int, string)`,
  `OnPlayerStartCarry(int, CarryableObjectType)`, `OnPlayerEndCarry(int)`, `OnPlayerShout(int, CharacterVoicePhrase)`
- Console et chat : `OnConsoleCommand(string input, string output, bool success)`,
  `OnRCLogin(int, string inputPassword, bool isLoggedIn)`, `OnRCCommand(int, string input, string output, bool success)`,
  `OnTextMessage(int playerId, TextChatChannel, string text)`, `OnAdminPlayerAction(int, int adminId, ServerAdminAction, string reason)`
- Carte : objets endommagés et interactions, emplacements, points de capture, `OnBuffStart/Stop(int, BuffType)`,
  `OnRoundEndFactionWinner(FactionCountry, FactionRoundWinnerReason)`, `OnRoundEndPlayerWinner(int)`, véhicules et navires.

### Événements `IHoldfastSharedMethods2`
- `OnPlayerPacket(int playerId, byte? instance, Vector3? ownerPosition, double? packetTimestamp, Vector2? ownerInputAxis, float? ownerRotationY, float? ownerPitch, float? ownerYaw, PlayerActions[] actionCollection, Vector3? cameraPosition, Vector3? cameraForward, ushort? shipID, bool swimming)`
- `OnVehiclePacket(…)`
- **`OnOfficerOrderStart(int officerPlayerId, HighCommandOrderType, Vector3 orderPosition, float orderRotationY, int voicePhraseRandomIndex)`**
  : appelé côté serveur, seulement si le jeu a accepté l'ordre [CODE].
- **`OnOfficerOrderStop(int officerPlayerId, HighCommandOrderType)`**

### Événements `IHoldfastSharedMethods3`
`OnStartSpectate/OnStopSpectate`, `OnStartFreeflight/OnStopFreeflight`, `OnMeleeArenaRoundEndFactionWinner`,
`OnPlayerConnected(int, bool isAutoAdmin, string backendId)` (`isAutoAdmin` toujours false ; `backendId` = PlayFabId),
`OnPlayerDisconnected(int)`.

## 2. Pièges [CODE]

1. **Enums en retard sur le jeu** (le jeu caste en entier) :
   - `FactionCountry` : pas de décalage, mais les valeurs 9 à 12 (Privateer, Spanish, ARBritish, ARAmerican) n'ont pas de nom côté mod ;
   - `GameplayMode` décalé à partir de 12 (NavalConquest, AROffensive, ARInvasion ajoutés) ;
   - `HighCommandOrderType` 17 à 34 sans nom côté mod (OfficerSquadCamp, Raid, Captain*, Midshipman*, Dash, SteadyAim,
     HandGrenade, SatchelCharge) ; `BuffType` 16 à 28 sans nom ;
   - aussi : `CarryableObjectType`, `EmplacementType`, `ShipType`, `CharacterVoicePhrase`, `TextChatChannel` (+Friends=13),
     `PlayerActions`, `ScorableActionType`.
   - Inchangés : `PlayerClass`, `FactionRoundWinnerReason`, `GameType`, `InteractionActivationType`.
   - **Attention** : plusieurs enums ont des valeurs explicites avec des trous (Guard=16, Grenadier=18, Musician=19, Sergeant=20, Rocketeer=22 ;
     Fire=17, MakeReady=46, StandGround=77 ; SergeantLineRally=13). Ne jamais déduire une valeur de la position du nom. Voir [CARBONPLAYERS.md](CARBONPLAYERS.md) §0.

   → **Travailler sur les valeurs entières** avec notre propre table de noms, reprise de [`ref/HoldfastEnums.Runtime.cs.txt`](ref/HoldfastEnums.Runtime.cs.txt).
2. **System.IO et System.Reflection interdits** (sauf serveur lancé avec `allowRestrictedMods`). UnityEngine est utilisable.
3. **Consoles** : seul un mod qui contient des **scènes** rend le serveur incompatible (plateforme forcée à Steam, joueurs console
   déconnectés). Un mod de scripts seuls garde `consoleCompatible = true` (`ServerModLoaderManager._OnModLoadedComplete`).
   → **Pas de scène dans notre mod.** Pour avoir `Update`, créer l'objet dans le code :
   `new GameObject("Commander").AddComponent<CommanderLoop>()` [TEST].
4. Chaque `ExecuteConsoleCommand` déclenche `OnConsoleCommand` chez tous les mods, y compris le sien : éviter les boucles.
   Seuls `quietBroadcastMessage`/`quietPrivateMessage` déclenchent `OnTextMessage(-1, …)`.
5. Chat et console :
   - `OnTextMessage` arrive **avant** le filtre de mute ; un mod ne peut pas supprimer un message ;
   - dans le canal Admin (12), `/bc ` et `/mr ` sont exécutés et n'arrivent pas aux mods ;
   - `OnRCCommand` reçoit la ligne entière (« a;b » → deux appels avec le texte complet) ;
   - `OnRCLogin` reçoit le mot de passe en clair : **ne jamais le journaliser**.

## 3. Commandes console

### Droits [CODE] (`GameConsolePanel.ExecuteInput`)
- `adminId = -1` : aucune vérification, et `ExecuteInput` ne journalise rien (il ne le fait que si `adminID > 0`) [CODE].
  **Mais** `serverAdmin slay`, `kick` et `privateMessage` journalisent eux-mêmes : `logs_adminactions` (admin vide),
  webhook Discord admin s'il est configuré, et `OnAdminPlayerAction` chez tous les mods. À utiliser le moins possible.
- `adminId = <playerId>` : pas de vérification non plus ; `me` désigne ce joueur (`teleport me …`), `broadcast` passe en type Admin,
  l'action est notée à son nom [TEST].

### Utiles au mod
| Commande | Effet |
|---|---|
| `broadcast <texte>` | bandeau central + chat pour tous (HTML retiré) |
| `serverAdmin say <texte>` | chat seulement |
| `serverAdmin privateMessage <pid> <texte>` | bandeau + chat pour un joueur ; part aussi au webhook Discord admin s'il existe |
| `serverAdmin quietBroadcastMessage` / `quietPrivateMessage` | **mods clients seulement**, rien à l'écran |
| `serverAdmin slay\|slap <pid> <dégâts>\|revive\|kick\|ban <pid> [raison]` | sanctions et effets (raison envoyée en MP) |
| `serverAdmin chatMute\|chatUnmute\|voipMute\|voipUnmute <pid>` | mutes |
| `teleport <pid\|me\|attackers\|defenders\|all> <pid\|me\|x,y,z>` | téléportation |
| `ac <s>` | charge générale (0 = maintenant, -1 = annuler) |
| `delayed <s> <commande>` | commande quand il reste N s de manche |
| `playerlist [pid\|ingame\|connected\|not-ingame]` | liste des joueurs |
| `set <variable> <valeur>` / `get` | 245 variables, dont `orderSystemEnabled`, `allowFiring`, `characterGodMode`, `voiceChatEnabled`, `characterRespawnTime`, `vehicleHorseForOfficer`, `createDynamicSpawnSectionWithReinforceOrder`, `characterWeaponDamageScale` |
| `playSong <pid> <songId>` | joue un morceau à la position du joueur [TEST] |
| `carbonPlayers …` | bots seulement (voir [COMMANDER-BATTLES.md](COMMANDER-BATTLES.md)) |

Pas de RCON réseau sur le serveur.

## 4. Hiérarchie connue du mod

- **Officier** pour le jeu = `ArmyInfantryOfficer` ou `NavalCaptain` (`PlayerBase.IsOfficer`). Pas de « Capitaine » d'infanterie.
- Classe et faction : `OnPlayerSpawned`. Régiment : `OnPlayerJoined.regimentTag`.
- **Grade dans le régiment** : seulement dans le nom affiché, par exemple `GDE | Sgt | TheCrack` ; format propre à chaque régiment [TEST].
- Non exposé (Reflection interdite) : escouade, ClassRank, ordres actifs.
- Ordres : `OnOfficerOrderStart/Stop` (OfficerReinforce, Charge, FormLine, BreakRankAndFollow, FireAtWill, MakeReady, Fire, CeaseFire,
  Sergeant*), avec position et orientation. Buffs d'officier : `OnBuffStart/Stop`. Ordres vocaux : `OnPlayerShout`
  (MakeReady, Fire, Charge, FormColumn, ForwardMarch, Halt, CloseRanks, Reinforce, Retreat, StandGround…).
- Canaux de chat : Round, Team, Squad, Officers, Musicians, Skirmishers, Arty, Cavalry, Sappers, Regiment, Recruitment, Admin.
  Le mod lit le canal mais ne peut pas **répondre** dans un canal : soit à tous, soit en MP.

## 5. Ce que voit un joueur console sans rien télécharger

| Commande | Bandeau | Chat |
|---|---|---|
| `broadcast` | oui | oui |
| `serverAdmin say` | non | oui |
| `serverAdmin privateMessage` | oui | oui |
| `quiet*` | non | non |

- Un compte Xbox/PS dont la communication est restreinte ne voit **pas** le chat venant du serveur, mais voit les **bandeaux** [TEST].
- Effets visibles par tous : slay, slap, revive, teleport, mutes, réglages `set`, `ac`, `playSong`, changement de carte.
- Ce qu'un joueur console envoie au mod : roue d'ordres (`OnOfficerOrderStart`), ordres vocaux (`OnPlayerShout`), déplacements,
  et le chat si la saisie de texte est possible [TEST].
- **PC seulement** (mod client) : nouvelle interface, marqueurs, sons personnalisés, touches dédiées, données `quiet*`.

## 6. À tester en jeu
1. Bandeaux et chat sur Xbox et PS, y compris compte à communication restreinte.
2. `ExecuteConsoleCommand` avec `adminId` = un officier (`teleport me <pid>`).
3. `playSong` : identifiants, effet sur un non-musicien.
4. Valeurs de faction et de mode sur une carte Privateer, Spanish ou AR.
5. Format du nom affiché selon les régiments.
6. Saisie du chat sur console.
7. Serveur toujours visible sur console avec le mod en `load_mod_server_only`.
8. Objet Unity créé dans le code (sans scène) : `Update` appelé à chaque image.
