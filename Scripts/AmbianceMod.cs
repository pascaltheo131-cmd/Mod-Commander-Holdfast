using System.Collections.Generic;
using HoldfastSharedMethods;
using UMod;
using UnityEngine;

namespace HoldfastAmbiance
{
    /// <summary>
    /// Mod d'ambiance côté joueur : rumeur de bataille (tirs de mousquet en 3D, atténués et retardés avec la distance) et
    /// fumée de tir plus dense. Ne fait rien sur le serveur. Aucun envoi réseau, aucune statistique.
    /// Les sons (Sounds/tir_01 … tir_40) sont des ressources du mod, chargées par leur nom.
    /// </summary>
    public class AmbianceMod : ModScript, IHoldfastSharedMethods
    {
        private static bool _client;
        private readonly Dictionary<int, Transform> _bodies = new Dictionary<int, Transform>();

        public void OnIsClient(bool client, ulong steamId)
        {
            _client = client;
            _mySteamId = steamId;
            Debug.Log("[AMB] OnIsClient(" + client + ", SteamId " + steamId + ").");
            if (!client) return;
            var clips = new List<AudioClip>();
            if (ModAssets == null)
            {
                Debug.Log("[AMB] Ressources du mod introuvables : pas de son ajouté (la fumée reste).");
                AmbianceLoop.Ensure(clips);
                return;
            }
            // Tous les sons du mod, par numéro de ressource (le nom ou le chemin attendu par uMod peut varier).
            for (var i = 0; i < ModAssets.AssetCount; i++)
            {
                AudioClip clip = null;
                try { clip = ModAssets.Load<AudioClip>(i); } catch { }
                if (clip != null) clips.Add(clip);
            }
            if (clips.Count == 0)
                for (var i = 1; i <= 60; i++)
                {
                    var clip = ModAssets.Load<AudioClip>("tir_" + i.ToString("00"));
                    if (clip != null) clips.Add(clip);
                }
            Debug.Log("[AMB] Ressources du mod : " + ModAssets.AssetCount + ", sons trouvés : " + clips.Count
                      + (clips.Count > 0 ? " (ex. " + clips[0].name + ")" : "") + ".");
            AmbianceLoop.Ensure(clips);
            Debug.Log("[AMB] Ambiance chargée : " + clips.Count + " tirs.");
        }

        public void OnRoundDetails(int roundId, string serverName, string mapName, FactionCountry attackingFaction,
            FactionCountry defendingFaction, GameplayMode gameplayMode, GameType gameType)
        {
            _bodies.Clear();
        }

        public void OnPlayerSpawned(int playerId, int spawnSectionId, FactionCountry playerFaction, PlayerClass playerClass,
            int uniformId, GameObject playerObject)
        {
            if (!_client || playerObject == null) return;
            _bodies[playerId] = playerObject.transform;
        }

        public void OnPlayerLeft(int playerId) => _bodies.Remove(playerId);

        // ---- Commande du joueur (chez lui seulement) ----
        private ulong _mySteamId;
        private int _me = -1;
        // Tous les joueurs vus (id → SteamId) : l'arrivée du joueur local peut précéder OnIsClient.
        private readonly Dictionary<int, ulong> _steamIds = new Dictionary<int, ulong>();

        public void OnPlayerJoined(int playerId, ulong steamId, string name, string regimentTag, bool isBot)
        {
            if (isBot) return;
            _steamIds[playerId] = steamId;
            if (steamId != 0 && steamId == _mySteamId)
            {
                _me = playerId;
                Debug.Log("[AMB] Joueur local : #" + playerId + ".");
            }
        }

        private bool IsMe(int playerId)
        {
            if (_me < 0 && _mySteamId != 0)
                foreach (var kv in _steamIds) if (kv.Value == _mySteamId) { _me = kv.Key; break; }
            return playerId == _me;
        }

        /// <summary>
        /// Deux commandes, valables sur le PC de ce joueur seulement : !musket on = tirs du mod joués et son de tir du jeu baissé
        /// de 40 dB ; !musket off = plus de tirs du mod, son du jeu normal. Par défaut : off.
        /// </summary>
        public void OnTextMessage(int playerId, TextChatChannel channel, string text)
        {
            if (!_client || string.IsNullOrEmpty(text)) return;
            var t = text.Trim().ToLowerInvariant();
            if (!t.StartsWith("!musket")) return;
            Debug.Log("[AMB] Commande reçue de #" + playerId + " (joueur local #" + _me + ", SteamId " + _mySteamId + ") : " + t);
            if (!IsMe(playerId)) return;
            if (t == "!musket on")
            {
                AmbianceLoop.Enabled = true;
                AmbianceLoop.SetGameShotCut(40f);
            }
            else if (t == "!musket off")
            {
                AmbianceLoop.Enabled = false;
                AmbianceLoop.SetGameShotCut(0f);
            }
        }

        public void OnPlayerShoot(int playerId, bool dryShot)
        {
            if (!_client || dryShot) return;
            // Son propre tir : joué à la position de l'écouteur (sa caméra), même sans corps connu.
            var local = IsMe(playerId);
            if (!_bodies.TryGetValue(playerId, out var body) || body == null)
            {
                if (local) AmbianceLoop.Shot(Vector3.zero, Vector3.forward, true);
                return;
            }
            AmbianceLoop.Shot(body.position, body.forward, local);
        }

        public void OnSyncValueState(int value) { }
        public void OnUpdateSyncedTime(double time) { }
        public void OnUpdateElapsedTime(float time) { }
        public void OnUpdateTimeRemaining(float time) { }
        public void OnIsServer(bool server) { }
        public void PassConfigVariables(string[] value) { }
        public void OnPlayerHurt(int playerId, byte oldHp, byte newHp, EntityHealthChangedReason reason) { }
        public void OnPlayerKilledPlayer(int killerPlayerId, int victimPlayerId, EntityHealthChangedReason reason, string details) { }
        public void OnScorableAction(int playerId, int score, ScorableActionType reason) { }
        public void OnShotInfo(int playerId, int shotCount, Vector3[][] shotsPointsPositions, float[] trajectileDistances, float[] distanceFromFiringPositions, float[] horizontalDeviationAngles, float[] maxHorizontalDeviationAngles, float[] muzzleVelocities, float[] gravities, float[] damageHitBaseDamages, float[] damageRangeUnitValues, float[] damagePostTraitAndBuffValues, float[] totalDamages, Vector3[] hitPositions, Vector3[] hitDirections, int[] hitPlayerIds, int[] hitDamageableObjectIds, int[] hitShipIds, int[] hitVehicleIds) { }
        public void OnPlayerBlock(int attackingPlayerId, int defendingPlayerId) { }
        public void OnPlayerMeleeStartSecondaryAttack(int playerId) { }
        public void OnPlayerWeaponSwitch(int playerId, string weapon) { }
        public void OnPlayerStartCarry(int playerId, CarryableObjectType carryableObject) { }
        public void OnPlayerEndCarry(int playerId) { }
        public void OnPlayerShout(int playerId, CharacterVoicePhrase voicePhrase) { }
        public void OnConsoleCommand(string input, string output, bool success) { }
        public void OnRCLogin(int playerId, string inputPassword, bool isLoggedIn) { }
        public void OnRCCommand(int playerId, string input, string output, bool success) { }
        public void OnAdminPlayerAction(int playerId, int adminId, ServerAdminAction action, string reason) { }
        public void OnDamageableObjectDamaged(GameObject damageableObject, int damageableObjectId, int shipId, int oldHp, int newHp) { }
        public void OnInteractableObjectInteraction(int playerId, int interactableObjectId, GameObject interactableObject, InteractionActivationType interactionActivationType, int nextActivationStateTransitionIndex) { }
        public void OnEmplacementPlaced(int itemId, GameObject objectBuilt, EmplacementType emplacementType) { }
        public void OnEmplacementConstructed(int itemId) { }
        public void OnCapturePointCaptured(int capturePoint) { }
        public void OnCapturePointOwnerChanged(int capturePoint, FactionCountry factionCountry) { }
        public void OnCapturePointDataUpdated(int capturePoint, int defendingPlayerCount, int attackingPlayerCount) { }
        public void OnBuffStart(int playerId, BuffType buff) { }
        public void OnBuffStop(int playerId, BuffType buff) { }
        public void OnRoundEndFactionWinner(FactionCountry factionCountry, FactionRoundWinnerReason reason) { }
        public void OnRoundEndPlayerWinner(int playerId) { }
        public void OnVehicleSpawned(int vehicleId, FactionCountry vehicleFaction, PlayerClass vehicleClass, GameObject vehicleObject, int ownerPlayerId) { }
        public void OnVehicleHurt(int vehicleId, byte oldHp, byte newHp, EntityHealthChangedReason reason) { }
        public void OnPlayerKilledVehicle(int killerPlayerId, int victimVehicleId, EntityHealthChangedReason reason, string details) { }
        public void OnShipSpawned(int shipId, GameObject shipObject, FactionCountry shipfaction, ShipType shipType, int shipName) { }
        public void OnShipDamaged(int shipId, int oldHp, int newHp) { }
    }
}
