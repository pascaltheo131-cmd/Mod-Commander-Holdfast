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
        /// axe et rotation imposés par défaut, pas de marche au hasard. (sendFullCameraData n'est pas activé : inutile au mod
        /// et coûteux, chaque vrai joueur enverrait sa caméra à chaque paquet.)
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
            return Run("inputAxis " + ServerConsole.Num(x) + " " + ServerConsole.Num(y) + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Cap absolu du corps en degrés (angle Y d'Unity : 0 = +z, 90 = +x).</summary>
        public static bool Rotate(int playerId, float yaw)
        {
            return Run("inputRotation " + ServerConsole.Num(yaw) + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Inclinaison du tir : pas des degrés, de -1.5 (60° vers le bas) à 2 (60° vers le haut), 0 = horizontal.</summary>
        public static bool Pitch(int playerId, float pitch)
        {
            return Run("pitch " + ServerConsole.Num(Mathf.Clamp(pitch, -1.5f, 2f)) + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Course (vrai) ou marche (faux), si la classe et l'arme le permettent.</summary>
        public static bool SetRunning(int playerId, bool running)
        {
            return Run("setRunning " + Bool(running) + " " + ServerConsole.Id(playerId));
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

        /// <summary>Change d'arme, par son nom de WeaponType (par exemple « BearingFlag ») ; seulement une arme que le bot possède.</summary>
        public static bool Equip(int playerId, string weaponType)
        {
            return Run("switchWeapon " + weaponType + " " + ServerConsole.Id(playerId));
        }

        /// <summary>Joue un morceau (GameIds.InstrumentType) ou arrête l'instrument. Donne le buff Musician. songId : [TEST].</summary>
        public static bool Instrument(int playerId, bool play, int instrumentType = GameIds.InstrumentType.MarchingDrum, int songId = 1)
        {
            return play
                ? Run("instrumentPlay " + ServerConsole.Id(playerId) + " " + GameIds.InstrumentName(instrumentType) + " " + ServerConsole.Id(songId))
                : Run("instrumentStop " + ServerConsole.Id(playerId));
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
