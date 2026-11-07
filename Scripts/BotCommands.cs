using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Pilotage des bots (« carbonPlayers » du jeu). Seule classe qui connaît la syntaxe de ces commandes : le reste du mod
    /// n'appelle que les méthodes ci-dessous, avec des entiers (GameIds) et des valeurs Unity.
    /// Syntaxe relevée dans le code du serveur (docs/CARBONPLAYERS.md §1) :
    /// - sous-commandes sensibles à la casse ; enums par leur nom (insensible à la casse) ; nombres au point décimal ;
    /// - l'id du bot est le DERNIER argument pour la plupart des sous-commandes. Sans id, la commande vise tous les bots et devient
    ///   le défaut des bots créés ensuite : ce mod donne toujours l'id (sauf dans Setup, voulu global) ;
    /// - id en PREMIER pour instrumentPlay/Stop, pickupWeapon, dropWeapon, dropAllWeapons, highCommandOrder, combinedInput ;
    /// - teleportToPlayer et meleeAttack n'existent pas : placement par « teleport &lt;id&gt; x,y,z » (ServerConsole).
    /// Certaines sous-commandes marchent aussi sur un vrai joueur (pitch, voicePhrase, switchWeapon, instrumentPlay…) :
    /// n'appeler ces méthodes qu'avec l'id d'un bot du mod.
    /// </summary>
    public static class BotCommands
    {
        private const string Root = "carbonPlayers ";
        // Le jeu coupe les noms à 32 caractères.
        private const int MaxNameLength = 32;
        // Tag bidon : un argument vide disparaît, il en faut un pour pouvoir donner l'uniforme. Un tag n'est appliqué que si un vrai
        // joueur connecté le porte : « x » reste donc sans effet.
        private const string NoTag = "x";

        // Id du bot créé pendant Spawn, fourni par OnPlayerConnected appelé pendant ExecuteConsoleCommand.
        private static bool _capturing;
        private static int _capturedId = -1;

        /// <summary>
        /// Réglages au démarrage et à chaque manche : pas de réapparition automatique (au hasard de faction et de classe),
        /// axe et rotation imposés par défaut, pas de marche au hasard. sendFullCameraData (visée des lignes des joueurs) n'est pas
        /// réglé ici : coûteux (chaque vrai joueur envoie sa caméra à chaque paquet), il n'est activé que tant qu'un joueur commande
        /// une ligne (LineManager.TickCamera).
        /// </summary>
        public static void Setup()
        {
            Run("allowAutoRespawn false");
            // Sans id : volontairement global, et défaut des bots créés ensuite. Repris par bot dans TakeControl.
            // Sans bot sur le serveur, le jeu répond success=false avec « Updating setting » alors que le défaut est bien
            // enregistré (newSpawnInputAxis…) : pas d'avertissement.
            Run("forceInputAxis true", false);
            Run("forceInputRotation true", false);
            ServerConsole.Execute("set botsAutoMove false", "Réglage");
        }

        // ---- Création --------------------------------------------------------------------------

        /// <summary>Vrai pendant un Spawn : CommanderMod transmet alors OnPlayerConnected (backendId vide) à OnBotConnected.</summary>
        public static bool Capturing => _capturing;

        public static void OnBotConnected(int playerId)
        {
            if (_capturing && _capturedId < 0) _capturedId = playerId;
        }

        /// <summary>
        /// Crée un bot (un par commande). Renvoie son id, ou -1 si la commande échoue ou si aucun bot n'a été annoncé pendant l'appel.
        /// Le bot n'est pas encore apparu : attendre OnPlayerSpawned, qui donne la faction et la classe réelles (le jeu peut les changer).
        /// Position : section d'apparition au hasard de la faction ; le mod le téléporte ensuite.
        /// tag : appliqué seulement si un vrai joueur connecté le porte. uniformId &lt; 1 : uniforme 1 de la classe.
        /// </summary>
        public static int Spawn(int faction, int playerClass, string name, string tag, int uniformId)
        {
            // Un argument vide disparaît et décale les suivants : le nom n'est jamais vide.
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) name = "Soldat";
            var command = "spawnSpecific " + GameIds.FactionName(faction) + " " + GameIds.ClassName(playerClass) + " " + Quote(name, MaxNameLength);
            var hasTag = !string.IsNullOrEmpty(tag) && tag.Trim().Length > 0;
            if (hasTag || uniformId >= 1) command += " " + Quote(hasTag ? tag.Trim() : NoTag, MaxNameLength);
            if (uniformId >= 1) command += " " + ServerConsole.Id(uniformId);

            _capturing = true;
            _capturedId = -1;
            bool ok;
            try
            {
                ok = Run(command);
            }
            finally
            {
                _capturing = false;
            }
            // Un bot annoncé est bien créé, même si la commande a renvoyé un échec : son id est gardé pour pouvoir le retirer.
            if (_capturedId >= 0) return _capturedId;
            if (ok) CmdLog.Warn("Bot demandé mais aucun OnPlayerConnected pendant l'appel : " + command);
            return -1;
        }

        /// <summary>Juste après l'apparition : axe et rotation imposés pour ce bot, immobile, tir à l'horizontale.</summary>
        public static void TakeControl(int playerId)
        {
            var id = " " + ServerConsole.Id(playerId);
            Run("forceInputAxis true" + id);
            Run("forceInputRotation true" + id);
            Run("inputAxis 0 0" + id);
            Run("pitch 0" + id);
        }

        // ---- Pilotage --------------------------------------------------------------------------

        /// <summary>Axe de déplacement, relatif au corps : x = pas de côté (droite +), y = avant (+) / arrière (-). Une valeur sous 1 ralentit.</summary>
        public static bool Move(int playerId, float x, float y)
        {
            return RunQuiet("inputAxis " + ServerConsole.Num(x) + " " + ServerConsole.Num(y) + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Cap absolu du corps en degrés (angle Y d'Unity : 0 = +z, 90 = +x).</summary>
        public static bool Rotate(int playerId, float yaw)
        {
            return RunQuiet("inputRotation " + ServerConsole.Num(yaw) + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Inclinaison du tir : pas des degrés, de -1.5 (60° vers le bas) à 2 (60° vers le haut), 0 = horizontal.</summary>
        public static bool Pitch(int playerId, float pitch)
        {
            return RunQuiet("pitch " + ServerConsole.Num(Mathf.Clamp(pitch, -1.5f, 2f)) + " " + ServerConsole.Id(playerId));
        }

        // ---- Chevaux (« vehiclesManager ») ----------------------------------------------------------

        private static readonly Dictionary<int, int> HorseByRider = new Dictionary<int, int>();
        private static float _horsesListedAt = -1000f;

        /// <summary>
        /// Cheval dont le bot est le cavalier d'origine, -1 si aucun. « vehiclesManager listVehicles » répond une ligne par
        /// véhicule : « Vehicle: &lt;id&gt; - Original Rider: &lt;joueur&gt; » ; la liste est relue au plus une fois par seconde.
        /// </summary>
        public static int FindHorse(int playerId)
        {
            if (Clock.Now - _horsesListedAt >= 1f)
            {
                _horsesListedAt = Clock.Now;
                HorseByRider.Clear();
                if (ServerConsole.Execute("vehiclesManager listVehicles", "Cheval", false, out var output) && output != null)
                {
                    foreach (var raw in output.Split('\n'))
                    {
                        var line = raw.Trim();
                        var v = line.IndexOf("Vehicle:", System.StringComparison.Ordinal);
                        var r = line.IndexOf("Original Rider:", System.StringComparison.Ordinal);
                        if (v < 0 || r < 0) continue;
                        var vText = line.Substring(v + 8, Mathf.Max(0, line.IndexOf('-', v) - v - 8)).Trim();
                        var rText = line.Substring(r + 15).Trim();
                        if (int.TryParse(vText, out var vehicle) && int.TryParse(rText, out var rider)) HorseByRider[rider] = vehicle;
                    }
                }
            }
            return HorseByRider.TryGetValue(playerId, out var id) ? id : -1;
        }

        public static bool ForceHorse(int vehicleId, bool force)
        {
            return ServerConsole.Execute("vehiclesManager forceInputAxis " + Bool(force) + " " + ServerConsole.Id(vehicleId), "Cheval", false);
        }

        /// <summary>Commande imposée au cheval : x tourner (-1 / 0 / 1), y avancer (-1 / 0 / 1) ; le jeu arrondit à l'entier.</summary>
        public static bool HorseAxis(int vehicleId, float x, float y)
        {
            return ServerConsole.Execute("vehiclesManager inputAxis " + ServerConsole.Num(x) + " " + ServerConsole.Num(y) + " "
                                         + ServerConsole.Id(vehicleId), "Cheval", false);
        }

        /// <summary>Course (vrai) ou marche (faux), si la classe et l'arme le permettent.</summary>
        public static bool SetRunning(int playerId, bool running)
        {
            return RunQuiet("setRunning " + Bool(running) + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Action ponctuelle (GameIds.PlayerActions), envoyée une fois au prochain FixedUpdate : un appui, pas un état.</summary>
        public static bool Action(int playerId, int playerAction)
        {
            return Run("playerAction " + GameIds.ActionName(playerAction) + " " + ServerConsole.Id(playerId));
        }

        /// <summary>
        /// Plusieurs actions dans le même paquet. Important [CODE] : chaque « playerAction » REMPLACE les actions en attente du bot
        /// (queuedPlayerActions), lues une seule fois au FixedUpdate suivant ; deux commandes avant ce FixedUpdate = la première
        /// est perdue. Le jeu traite ensuite le paquet dans un ordre fixe (tir, épauler, désépauler, baïonnette, recharger…).
        /// </summary>
        public static bool Actions(int playerId, List<int> playerActions)
        {
            if (playerActions == null || playerActions.Count == 0) return true;
            var sb = new StringBuilder("playerAction");
            foreach (var a in playerActions) sb.Append(' ').Append(GameIds.ActionName(a));
            sb.Append(' ').Append(ServerConsole.Id(playerId));
            return Run(sb.ToString());
        }

        /// <summary>Frappes de mêlée en boucle dans une direction au hasard (vrai), ou arrêt de la boucle (faux).</summary>
        public static bool MeleeLoop(int playerId, bool strike)
        {
            return Run("playerAction " + (strike ? "MeleeStrikeRandom" : "None") + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Retire les armes d'un auxiliaire qui ne doit porter que son objet de rôle.</summary>
        /// <summary>
        /// « startHealing &lt;soigneur&gt; &lt;blessé&gt; OthersHealing » [CODE, ServerPlayerHealingManager] : refusé sans message si le
        /// soigneur n'est pas stable (immobile), porte un objet, vise/recharge, ou est à plus de 4 m (2 × MaxDistanceAllowedForHealing),
        /// si le blessé est d'un autre camp, à pleine vie ou instable. Le soin s'arrête seul quand il n'est plus permis (pleine vie).
        /// </summary>
        public static bool StartHealing(int healerId, int healedId)
        {
            return Run("startHealing " + ServerConsole.Id(healerId) + " " + ServerConsole.Id(healedId) + " OthersHealing");
        }

        public static bool StopHealing(int healerId)
        {
            return Run("stopHealing " + ServerConsole.Id(healerId), false);
        }

        public static bool DropAllWeapons(int playerId)
        {
            return Run("dropAllWeapons " + ServerConsole.Id(playerId));
        }

        /// <summary>
        /// Fait jouer un morceau au bot par la commande serveur « playSong &lt;id&gt; &lt;morceau&gt; » [CODE,
        /// InstrumentSongPlayerServerConsoleCommand] : le serveur vérifie que le morceau existe dans son répertoire
        /// (InstrumentSongOptionsRepository) et le diffuse avec l'un des instruments permis pour ce morceau, attaché au bot.
        /// « instrumentPlay » ne vérifie pas le numéro : un numéro inconnu des clients ne produit aucun son. Renvoie vrai si le
        /// morceau existe ; output = réponse exacte du jeu (« Broadcasted song playing… Song name: … » ou « Song does not exist »).
        /// </summary>
        public static bool PlaySong(int playerId, int songId, out string output)
        {
            var ok = ServerConsole.Execute("playSong " + ServerConsole.Id(playerId) + " " + ServerConsole.Id(songId), "Musique", false, out output);
            return ok && output.IndexOf("Song name", System.StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// « switchWeapon &lt;WeaponType&gt; &lt;id&gt; » [CODE, SwitchCarbonPlayerWeapon] : l'arme doit être dans l'inventaire, pas déjà
        /// en main, et le bot apparu depuis au moins 1 s ; le jeu répond toujours « Updating setting » (confirmation par
        /// OnPlayerWeaponSwitch).
        /// </summary>
        public static bool SwitchWeapon(int playerId, string weaponType)
        {
            return Run("switchWeapon " + weaponType + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Met un objet en main (CarryableObjectType), par exemple l'instrument d'un musicien.</summary>
        public static bool EquipCarryable(int playerId, int carryable)
        {
            return Run("equipCarryableItem " + GameIds.CarryableName(carryable) + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Joue un morceau (GameIds.InstrumentType) ou arrête l'instrument. Donne le buff Musician. songId : [TEST].</summary>
        public static bool Instrument(int playerId, bool play, int instrumentType, int songId, out string output)
        {
            // Préfixe carbonPlayers indispensable : seul, « instrumentPlay » n'est pas une commande (vu en jeu : « not recognized »).
            return ServerConsole.Execute(Root + "instrumentPlay " + ServerConsole.Id(playerId) + " " + GameIds.InstrumentName(instrumentType) + " "
                                         + ServerConsole.Id(songId), "Musique", false, out output);
        }

        public static bool Instrument(int playerId, bool play, int instrumentType = GameIds.InstrumentType.MarchingDrum, int songId = 1)
        {
            // Pas d'avertissement : le jeu répond « ok » même en cas de refus (instrument non tenu, demande à moins de 3 s).
            return play
                ? RunQuiet("instrumentPlay " + ServerConsole.Id(playerId) + " " + GameIds.InstrumentName(instrumentType) + " " + ServerConsole.Id(songId))
                : RunQuiet("instrumentStop " + ServerConsole.Id(playerId));
        }

        /// <summary>Fait crier une phrase (GameIds.CharacterVoicePhrase). Déclenche OnPlayerShout pour le bot : à ignorer.</summary>
        public static bool Shout(int playerId, int voicePhrase)
        {
            return Run("voicePhrase " + GameIds.PhraseName(voicePhrase) + " " + ServerConsole.Id(playerId));
        }

        // ---- Fin -------------------------------------------------------------------------------

        /// <summary>Tue le bot (avec allowAutoRespawn false, il reste mort et connecté).</summary>
        public static bool Kill(int playerId)
        {
            return ServerConsole.Slay(playerId);
        }

        /// <summary>
        /// Retire le bot du serveur. Aucune sous-commande carbonPlayers ne le fait : « serverAdmin kick » est le seul moyen relevé.
        /// Effets de bord : ligne dans logs_adminactions, OnAdminPlayerAction chez les mods et message au webhook Discord admin
        /// s'il est configuré ; la sortie dit « Unable to kick » mais le bot est retiré [TEST]. À utiliser le moins possible :
        /// les bots disparaissent seuls au changement de manche.
        /// </summary>
        public static bool Remove(int playerId)
        {
            // Le texte de sortie n'est pas fiable (« Unable to kick ») : pas d'avertissement sur échec.
            ServerConsole.Execute("serverAdmin kick " + ServerConsole.Id(playerId), "Retrait de bot", false);
            return true;
        }

        // ---- Outils ----------------------------------------------------------------------------

        private static bool RunQuiet(string subCommand)
        {
            return Run(subCommand, false);
        }

        private static bool Run(string subCommand, bool warnOnFailure = true)
        {
            return ServerConsole.Execute(Root + subCommand, "Bot", warnOnFailure);
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        /// <summary>Argument entre guillemets (espaces permis) ; guillemets retirés du texte, longueur bornée.</summary>
        private static string Quote(string text, int max)
        {
            text = (text ?? "").Replace("\"", "").Replace("“", "").Replace("”", "").Replace("„", "").Trim();
            if (text.Length > max) text = text.Substring(0, max).Trim();
            return "\"" + text + "\"";
        }
    }
}
