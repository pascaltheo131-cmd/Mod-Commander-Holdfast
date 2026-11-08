using System.Collections.Generic;
using System;
using System.Globalization;
using HoldfastBridge;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Seul point d'appel de ExecuteConsoleCommand dans le mod : toutes les actions (messages, téléportations, bots) passent ici.
    /// Chaque commande rappelle OnConsoleCommand chez tous les mods, y compris celui-ci (docs/API.md §2.4) :
    /// le mod n'agit jamais en réponse à OnConsoleCommand, et IsExecuting permet de reconnaître ses propres commandes.
    /// </summary>
    public static class ServerConsole
    {
        private const int MaxMessageLength = 250;

        // Statique : le jeu crée une instance du mod par interface ; OnGameMethodsInitialized arrive sur l'instance IHoldfastGame.
        public static IHoldfastGameMethods Methods;

        /// <summary>Vrai pendant l'exécution d'une commande du mod.</summary>
        public static bool IsExecuting { get; private set; }

        /// <summary>Nombre de commandes envoyées (diagnostic de charge, affiché par « !cmd etat »).</summary>
        public static long Sent { get; private set; }

        /// <summary>
        /// Exécute une commande console ; journalise l'échec ([CMD-WARN] préfixé par label, sauf warnOnFailure faux) et renvoie false.
        /// Toujours avec adminId -1 : une commande au nom d'un joueur serait notée dans logs_adminactions à chaque appel.
        /// </summary>
        public static bool Execute(string command, string label, bool warnOnFailure = true)
        {
            return Execute(command, label, warnOnFailure, out _);
        }

        /// <summary>Comme Execute, avec le texte renvoyé par le jeu (vide en cas d'erreur).</summary>
        public static bool Execute(string command, string label, bool warnOnFailure, out string result)
        {
            result = "";
            if (Methods == null)
            {
                CmdLog.Warn(label + " : IHoldfastGameMethods indisponible");
                return false;
            }
            if (IsExecuting)
            {
                // Appel réentrant (un mod qui réagit à OnConsoleCommand par une commande) : refusé, pour éviter une boucle.
                CmdLog.Warn(label + " : commande réentrante ignorée");
                return false;
            }
            IsExecuting = true;
            try
            {
                Sent++;
                CountKind(command);
                // adminId -1 : commande exécutée par le serveur lui-même (pas par un admin connecté).
                var ok = Methods.ExecuteConsoleCommand(command, out var output, out var exception, -1);
                result = output ?? "";
                if (exception != null) CmdLog.Warn(label + " : " + exception.Message);
                else if (!ok)
                {
                    const string missing = "Player with ID does not exist:";
                    if (output != null && output.StartsWith(missing, StringComparison.Ordinal)
                        && int.TryParse(output.Substring(missing.Length).Trim(), out var missingId))
                        LineManager.ForgetMissing(missingId);
                    if (warnOnFailure && !string.Equals(output?.Trim(), "ok", StringComparison.OrdinalIgnoreCase))
                        CmdLog.Warn(label + " : " + Short(output));
                }
                return ok && exception == null;
            }
            catch (Exception e)
            {
                CmdLog.Warn(label + " : " + e.Message);
                return false;
            }
            finally
            {
                IsExecuting = false;
            }
        }

        /// <summary>Commandes envoyées par type depuis le dernier bilan (FrameStats) : sous-commande de carbonPlayers, sinon premier mot.</summary>
        public static readonly Dictionary<string, int> SentByKind = new Dictionary<string, int>();

        private static void CountKind(string command)
        {
            var start = command.StartsWith("carbonPlayers ", StringComparison.Ordinal) ? 14 : 0;
            var end = command.IndexOf(' ', start);
            var kind = end < 0 ? command.Substring(start) : command.Substring(start, end - start);
            SentByKind[kind] = SentByKind.TryGetValue(kind, out var n) ? n + 1 : 1;
        }

        /// <summary>Les types les plus envoyés, par seconde, puis remise à zéro.</summary>
        public static string TakeKindSummary(float seconds)
        {
            if (SentByKind.Count == 0) return "";
            var list = new List<KeyValuePair<string, int>>(SentByKind);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder(" Par type :");
            for (var i = 0; i < list.Count && i < 8; i++)
                sb.Append(i == 0 ? " " : ", ").Append(list[i].Key).Append(' ').Append((list[i].Value / seconds).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append("/s");
            SentByKind.Clear();
            return sb.ToString();
        }

        // ---- Messages ------------------------------------------------------------------------

        /// <summary>Bandeau central et chat pour tous (visible sur console, y compris comptes à communication restreinte [TEST]).</summary>
        public static bool Broadcast(string text)
        {
            return Execute("broadcast " + Clean(text), "Annonce");
        }

        /// <summary>Bandeau et chat pour un seul joueur. Part aussi au webhook Discord admin s'il est configuré.</summary>
        public static bool Private(int playerId, string text)
        {
            return Execute("serverAdmin privateMessage " + Id(playerId) + " " + Clean(text), "Message privé");
        }

        // ---- Joueurs -------------------------------------------------------------------------

        /// <summary>Téléporte un joueur (ou un bot) sur un autre joueur (« teleport », exactement 2 arguments).</summary>
        public static bool TeleportToPlayer(int playerId, int targetPlayerId)
        {
            return Execute("teleport " + Id(playerId) + " " + Id(targetPlayerId), "Téléportation");
        }

        /// <summary>Téléporte un joueur (ou un bot) à une position. Hauteur à fournir : pas de recalage au sol [TEST].</summary>
        public static bool TeleportToPosition(int playerId, Vector3 position)
        {
            return Execute("teleport " + Id(playerId) + " " + Num(position.x) + "," + Num(position.y) + "," + Num(position.z), "Téléportation");
        }

        /// <summary>Tue un joueur (ou un bot) sans le kicker.</summary>
        public static bool Slay(int playerId)
        {
            return Execute("serverAdmin slay " + Id(playerId), "Slay");
        }

        /// <summary>Ranime un joueur mort (serverAdmin revive) ; vrai si le jeu l'a accepté.</summary>
        public static bool Revive(int playerId)
        {
            return Execute("serverAdmin revive " + Id(playerId) + " Porte-drapeau", "Revive", false, out var result)
                   && result.IndexOf("Succes", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- Formatage -----------------------------------------------------------------------

        public static string Id(int playerId)
        {
            return playerId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Nombre au point décimal, 2 décimales : la console du jeu ne connaît pas la virgule.</summary>
        public static string Num(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Clean(string text)
        {
            text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length > MaxMessageLength ? text.Substring(0, MaxMessageLength - 1) + "…" : text;
        }

        private static string Short(string text)
        {
            text = text ?? "";
            return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
        }
    }
}
