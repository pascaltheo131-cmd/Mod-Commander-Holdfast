using System;
using HoldfastBridge;
using HoldfastSharedMethods;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Mod serveur « Commander » : un officier lève une ligne de bots et la commande (menu d'ordres, phrases vocales, chat).
    /// Côté serveur seulement, scripts sans scène ni asset ; aucun envoi réseau, aucune statistique.
    /// Le jeu crée une instance de cette classe par interface implémentée : tout l'état est statique.
    /// IHoldfastSharedMethods3 sert à OnPlayerConnected, qui donne l'id d'un bot pendant sa création.
    /// Les enums reçues sont converties en entiers dès l'événement (voir GameIds).
    /// </summary>
    public class CommanderMod : IHoldfastSharedMethods, IHoldfastSharedMethods2, IHoldfastSharedMethods3, IHoldfastGame
    {
        private const string ModVersion = "0.2.0";

        private static bool _isServer;
        private static bool _started;

        private static bool Active => _isServer;

        // ---- Initialisation ----------------------------------------------------------------
        // Ordre du jeu : OnGameMethodsInitialized → OnSyncValueState → PassConfigVariables → OnIsServer → OnIsClient.

        public void OnGameMethodsInitialized(IHoldfastGameMethods holdfastGameMethods)
        {
            ServerConsole.Methods = holdfastGameMethods;
        }

        public void PassConfigVariables(string[] value)
        {
            if (value == null) return;
            foreach (var raw in value) CommanderConfig.Apply(raw);
            CommanderConfig.Validate();
            CmdLog.Info("Configuration : " + CommanderConfig.Describe());
        }

        public void OnIsServer(bool server)
        {
            if (!server) return;
            _isServer = true;
            if (_started) return;
            _started = true;
            CmdLog.Info("Commander " + ModVersion + " chargé.");
            CommanderLoop.Ensure();
            if (CommanderConfig.IsEnabled) BotCommands.Setup();
        }

        // Le mod est prévu pour load_mod_server_only ; chargé par un client, il ne fait rien (_isServer reste faux).
        public void OnIsClient(bool client, ulong steamId) { }

        // ---- Manches -----------------------------------------------------------------------

        public void OnRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction,
            FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
        {
            if (!Active) return;
            Guard("OnRoundDetails", () =>
            {
                // Nouvelle manche ou nouvelle carte : il ne reste rien de la précédente. Le jeu a supprimé les bots sans
                // OnPlayerLeft : leurs fiches sont retirées (leurs ids seront réutilisés).
                LineManager.Clear("nouvelle manche", false);
                Players.RemoveBots();
                // Nouvelle origine de l'horloge, et toutes les échéances mémorisées avec elle.
                Clock.Reset();
                CmdLog.ResetClock();
                Players.ResetClock();
                LineManager.ResetClock();
                LoadMonitor.ResetClock();
                foreach (var p in Players.All)
                {
                    p.Alive = false;
                    p.Body = null;
                    p.HasPacket = false;
                    p.HasAim = false;
                    // Le camp de chacun se relit à sa prochaine apparition (lignes IA, lot C).
                    p.Faction = GameIds.FactionCountry.None;
                }
                AiDirector.OnRound((int)attackingFaction, (int)defendingFaction, (int)gameplayMode, Clock.Now);
                CmdLog.Info("Manche " + roundId + " sur " + mapName + " (mode " + (int)gameplayMode + ").");
                if (CommanderConfig.IsEnabled) BotCommands.Setup();
            });
        }

        public void OnRoundEndFactionWinner(FactionCountry factionCountry, FactionRoundWinnerReason reason)
        {
            if (!Active) return;
            AiDirector.OnRoundEnd();
            Guard("OnRoundEndFactionWinner", () => LineManager.Clear("fin de manche", false));
        }

        public void OnRoundEndPlayerWinner(int playerId)
        {
            if (!Active) return;
            AiDirector.OnRoundEnd();
            Guard("OnRoundEndPlayerWinner", () => LineManager.Clear("fin de manche", false));
        }

        // ---- Joueurs -----------------------------------------------------------------------

        /// <summary>
        /// Connexion. Pour un bot (backendId vide), appelé pendant la commande spawnSpecific du mod : BotCommands y prend l'id.
        /// Aucune commande console ici (on est à l'intérieur d'un ExecuteConsoleCommand).
        /// </summary>
        public void OnPlayerConnected(int playerId, bool isAutoAdmin, string backendId)
        {
            if (!Active) return;
            // Un id peut être réutilisé (bots supprimés au changement de carte, bot jamais apparu puis retiré) : l'ancien
            // titulaire est oublié sans commande console, et la fiche repart de zéro (pas de droit admin hérité).
            LineManager.ForgetStale(playerId);
            var isBot = string.IsNullOrEmpty(backendId);
            Players.Reset(playerId).IsBot = isBot;
            if (isBot && BotCommands.Capturing) BotCommands.OnBotConnected(playerId);
        }

        /// <summary>Déconnexion : seul événement pour un bot jamais apparu puis retiré. Même traitement qu'un départ.</summary>
        public void OnPlayerDisconnected(int playerId)
        {
            if (!Active) return;
            Guard("OnPlayerDisconnected", () => LineManager.OnPlayerLeft(playerId, Clock.Now));
            Players.Remove(playerId);
        }

        public void OnPlayerJoined(int playerId, ulong steamId, string name, string regimentTag, bool isBot)
        {
            if (!Active) return;
            var p = Players.GetOrAdd(playerId);
            p.Name = string.IsNullOrEmpty(name) ? "#" + playerId : name;
            p.Regiment = regimentTag ?? "";
            p.IsBot = isBot;
            p.Joined = true;
        }

        public void OnPlayerLeft(int playerId)
        {
            if (!Active) return;
            Guard("OnPlayerLeft", () => LineManager.OnPlayerLeft(playerId, Clock.Now));
            Players.Remove(playerId);
        }

        public void OnPlayerSpawned(int playerId, int spawnSectionId, FactionCountry playerFaction, PlayerClass playerClass,
            int uniformId, GameObject playerObject)
        {
            if (!Active) return;
            var p = Players.GetOrAdd(playerId);
            p.Alive = true;
            p.Faction = (int)playerFaction;
            p.Class = (int)playerClass;
            p.Uniform = uniformId;
            p.Body = playerObject;
            p.HasPacket = false;
            p.HasAim = false;
            Guard("OnPlayerSpawned", () => LineManager.OnPlayerSpawned(p, Clock.Now));
        }

        public void OnPlayerHurt(int playerId, byte oldHp, byte newHp, EntityHealthChangedReason reason)
        {
            if (!Active) return;
            if (newHp == 0) Died(playerId);
            else if (newHp < oldHp) Guard("OnPlayerHurt", () => LineManager.OnBotHurt(playerId, Clock.Now));
        }

        public void OnPlayerKilledPlayer(int killerPlayerId, int victimPlayerId, EntityHealthChangedReason reason, string details)
        {
            if (!Active) return;
            Died(victimPlayerId);
        }

        private static void Died(int playerId)
        {
            var p = Players.Get(playerId);
            if (p != null)
            {
                if (!p.Alive) return;
                p.Alive = false;
            }
            Guard("Mort", () => LineManager.OnPlayerDied(playerId, Clock.Now));
        }

        /// <summary>
        /// Position et orientation : repli quand l'objet du personnage n'est pas disponible. Appelé à chaque FixedUpdate pour chaque
        /// joueur et chaque bot : sortie immédiate pour les ids que le mod ne suit pas.
        /// </summary>
        public void OnPlayerPacket(int playerId, byte? instance, Vector3? ownerPosition, double? packetTimestamp, Vector2? ownerInputAxis,
            float? ownerRotationY, float? ownerPitch, float? ownerYaw, PlayerActions[] actionCollection, Vector3? cameraPosition,
            Vector3? cameraForward, ushort? shipID, bool swimming)
        {
            if (!Active || !ownerPosition.HasValue || !LineManager.IsTracked(playerId)) return;
            var p = Players.Get(playerId);
            if (p == null) return;
            p.PacketPosition = ownerPosition.Value;
            if (ownerRotationY.HasValue) p.PacketYaw = ownerRotationY.Value;
            p.HasPacket = true;
            if (!p.IsBot && cameraPosition.HasValue && cameraForward.HasValue && cameraForward.Value.sqrMagnitude > 0.1f)
            {
                p.CameraPosition = cameraPosition.Value;
                p.CameraForward = cameraForward.Value.normalized;
                p.HasAim = true;
                p.AimAt = Clock.Now;
            }
        }

        // ---- Ordres ------------------------------------------------------------------------

        public void OnOfficerOrderStart(int officerPlayerId, HighCommandOrderType highCommandOrderType, Vector3 orderPosition,
            float orderRotationY, int voicePhraseRandomIndex)
        {
            if (!Active || !CommanderConfig.IsEnabled || IsBot(officerPlayerId)) return;
            var order = (int)highCommandOrderType;
            Guard("OnOfficerOrderStart", () => Orders.OnOfficerOrder(officerPlayerId, order, orderPosition, orderRotationY, Clock.Now));
        }

        public void OnOfficerOrderStop(int officerPlayerId, HighCommandOrderType highCommandOrderType) { }

        public void OnPlayerShout(int playerId, CharacterVoicePhrase voicePhrase)
        {
            // Les bots crient aussi (acclamations de la ligne sur les ordres, voicePhrase du mod) : ignorés.
            if (!Active || !CommanderConfig.IsEnabled || IsBot(playerId)) return;
            var phrase = (int)voicePhrase;
            Guard("OnPlayerShout", () => Orders.OnShout(playerId, phrase, Clock.Now));
        }

        // ---- Chat et console ---------------------------------------------------------------

        public void OnTextMessage(int playerId, TextChatChannel channel, string text)
        {
            // playerId -1 : message émis par une commande console (quiet*), jamais une commande de joueur.
            if (!Active || playerId < 0) return;
            Guard("OnTextMessage", () => ChatCommands.OnText(playerId, text, Clock.Now));
        }

        /// <summary>Connexion admin. Le mot de passe n'est ni lu ni journalisé.</summary>
        public void OnRCLogin(int playerId, string inputPassword, bool isLoggedIn)
        {
            if (!Active || !isLoggedIn) return;
            Players.GetOrAdd(playerId).IsAdmin = true;
        }

        public void OnRCCommand(int playerId, string input, string output, bool success)
        {
            if (!Active) return;
            Guard("OnRCCommand", () => ChatCommands.OnRcCommand(playerId, input, Clock.Now));
        }

        /// <summary>Rappelé pour chaque commande console, y compris celles du mod : le mod n'y réagit jamais (pas de boucle).</summary>
        public void OnConsoleCommand(string input, string output, bool success) { }

        private static bool IsBot(int playerId)
        {
            if (LineManager.IsOurBot(playerId)) return true;
            var p = Players.Get(playerId);
            return p != null && p.IsBot;
        }

        // ---- Garde -------------------------------------------------------------------------

        /// <summary>Une exception du mod ne doit jamais remonter dans le jeu.</summary>
        private static void Guard(string label, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                // e.Message seulement : e.GetType().Name passe par System.Reflection, refusé par uMod.
                CmdLog.Warn(label + " : " + e.Message);
            }
        }

        // ---- Événements non utilisés -------------------------------------------------------

        public void OnSyncValueState(int value) { }
        public void OnUpdateSyncedTime(double time) { }
        public void OnUpdateElapsedTime(float time) { }
        public void OnUpdateTimeRemaining(float time) { }
        public void OnScorableAction(int playerId, int score, ScorableActionType reason) { }
        /// <summary>Tir d'un bot du mod : son arme est vide (et un tir à vide montre qu'elle n'était pas chargée) [TEST : appelé pour les bots ?].</summary>
        public void OnPlayerShoot(int playerId, bool dryShot)
        {
            if (!Active || !LineManager.IsOurBot(playerId)) return;
            LineManager.OnBotShot(playerId, dryShot);
        }
        public void OnShotInfo(int playerId, int shotCount, Vector3[][] shotsPointsPositions, float[] trajectileDistances,
            float[] distanceFromFiringPositions, float[] horizontalDeviationAngles, float[] maxHorizontalDeviationAngles,
            float[] muzzleVelocities, float[] gravities, float[] damageHitBaseDamages, float[] damageRangeUnitValues,
            float[] damagePostTraitAndBuffValues, float[] totalDamages, Vector3[] hitPositions, Vector3[] hitDirections,
            int[] hitPlayerIds, int[] hitDamageableObjectIds, int[] hitShipIds, int[] hitVehicleIds) { }
        public void OnPlayerBlock(int attackingPlayerId, int defendingPlayerId) { }
        public void OnPlayerMeleeStartSecondaryAttack(int playerId) { }
        public void OnPlayerWeaponSwitch(int playerId, string weapon) { }
        /// <summary>Objet porté par un bot du mod : son instrument (tambour, fifre…) pour instrumentPlay.</summary>
        public void OnPlayerStartCarry(int playerId, CarryableObjectType carryableObject)
        {
            if (!Active || !LineManager.IsOurBot(playerId)) return;
            LineManager.OnBotCarry(playerId, (int)carryableObject);
        }
        public void OnPlayerEndCarry(int playerId) { }
        public void OnAdminPlayerAction(int playerId, int adminId, ServerAdminAction action, string reason) { }
        public void OnDamageableObjectDamaged(GameObject damageableObject, int damageableObjectId, int shipId, int oldHp, int newHp) { }
        public void OnInteractableObjectInteraction(int playerId, int interactableObjectId, GameObject interactableObject,
            InteractionActivationType interactionActivationType, int nextActivationStateTransitionIndex) { }
        public void OnEmplacementPlaced(int itemId, GameObject objectBuilt, EmplacementType emplacementType) { }
        public void OnEmplacementConstructed(int itemId) { }
        public void OnCapturePointCaptured(int capturePoint) { }
        public void OnCapturePointOwnerChanged(int capturePoint, FactionCountry factionCountry) { }
        public void OnCapturePointDataUpdated(int capturePoint, int defendingPlayerCount, int attackingPlayerCount) { }
        public void OnBuffStart(int playerId, BuffType buff)
        {
            // Ralliement d'un sergent joueur reçu par un bot de nos lignes : moral (lot A, n° 4).
            if (!Active || (int)buff != GameIds.BuffType.SergeantLineRally || !LineManager.IsOurBot(playerId)) return;
            Guard("OnBuffStart", () => LineManager.OnRallyBuff(playerId, Clock.Now));
        }
        public void OnBuffStop(int playerId, BuffType buff) { }
        public void OnVehicleSpawned(int vehicleId, FactionCountry vehicleFaction, PlayerClass vehicleClass, GameObject vehicleObject, int ownerPlayerId) { }
        public void OnVehicleHurt(int vehicleId, byte oldHp, byte newHp, EntityHealthChangedReason reason) { }
        public void OnPlayerKilledVehicle(int killerPlayerId, int victimVehicleId, EntityHealthChangedReason reason, string details) { }
        public void OnShipSpawned(int shipId, GameObject shipObject, FactionCountry shipfaction, ShipType shipType, int shipName) { }
        public void OnShipDamaged(int shipId, int oldHp, int newHp) { }
        public void OnStartSpectate(int playerId, int spectatedPlayerId) { }
        public void OnStopSpectate(int playerId, int spectatedPlayerId) { }
        public void OnStartFreeflight(int playerId) { }
        public void OnStopFreeflight(int playerId) { }
        public void OnMeleeArenaRoundEndFactionWinner(int roundId, bool attackers) { }
        public void OnVehiclePacket(int vehicleId, Vector2 inputAxis, bool shift, bool strafe, PlayerVehicleActions[] actionCollection) { }
    }
}
