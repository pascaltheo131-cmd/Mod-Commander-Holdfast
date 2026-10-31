using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HoldfastCommander
{
    /// <summary>
    /// Commandes du chat. Officiers : !ligne, !renvoyer, !former, !suivre, !rangs, !baionnette, !accroupi, !pas.
    /// Admins (connectés par rc login) : !cmd …, ou « rc cmd … » depuis la console. Réponses en message privé (bandeau).
    /// Les messages du mod ne commencent jamais par « ! » : ils ne peuvent pas être pris pour une commande.
    /// </summary>
    public static class ChatCommands
    {
        private const float CommandCooldown = 1f;
        private const int MaxPerCommand = 100;
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        private const string Help = "!ligne <nombre> [classe] [uniforme] lève une ligne. Puis : !former, !suivre, !rangs [1|2], "
                                    + "!baionnette, !accroupi, !pas, !renvoyer.";

        public static void OnText(int playerId, string text, float now)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = Tags.Replace(text, "").Trim();
            if (text.Length < 2 || text[0] != '!') return;
            var p = Players.Get(playerId);
            if (p == null || p.IsBot) return;

            var space = text.IndexOf(' ');
            var command = (space < 0 ? text.Substring(1) : text.Substring(1, space - 1)).ToLowerInvariant();
            var args = space < 0 ? new string[0] : text.Substring(space + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!IsKnown(command)) return;

            if (now - p.LastCommand < CommandCooldown) return;
            p.LastCommand = now;
            CmdLog.Info("Commande de " + p + " : " + (text.Length > 80 ? text.Substring(0, 80) : text) + (p.IsAdmin ? " [admin]" : ""));

            string reply;
            try
            {
                reply = command == "cmd"
                    ? (p.IsAdmin ? Admin(p.Name, args, now) : "Commande réservée aux admins du serveur.")
                    : Officer(p, command, args, now);
            }
            catch (Exception e)
            {
                CmdLog.Warn("Commande !" + command + " : " + e.Message);
                reply = "Erreur du mod, commande ignorée.";
            }
            if (!string.IsNullOrEmpty(reply)) ServerConsole.Private(playerId, reply);
        }

        /// <summary>« rc cmd … » d'un admin connecté : le jeu ne connaît pas la commande mais la transmet aux mods.</summary>
        public static void OnRcCommand(int playerId, string input, float now)
        {
            if (string.IsNullOrEmpty(input)) return;
            var text = input.Trim();
            if (text.StartsWith("rc ", StringComparison.OrdinalIgnoreCase)) text = text.Substring(3).Trim();
            if (!text.StartsWith("cmd", StringComparison.OrdinalIgnoreCase) || (text.Length > 3 && text[3] != ' ')) return;
            var p = Players.Get(playerId);
            if (p == null) return;
            var args = text.Substring(3).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string reply;
            try
            {
                reply = p.IsAdmin ? Admin(p.Name, args, now) : "Commande réservée aux admins du serveur.";
            }
            catch (Exception e)
            {
                CmdLog.Warn("Commande rc cmd : " + e.Message);
                reply = "Erreur du mod, commande ignorée.";
            }
            ServerConsole.Private(playerId, reply);
        }

        private static bool IsKnown(string command)
        {
            switch (command)
            {
                case "cmd": case "ligne": case "renvoyer": case "former": case "suivre": case "rangs":
                case "baionnette": case "baïonnette": case "accroupi": case "pas":
                    return true;
                default:
                    return false;
            }
        }

        // ---- Officiers -----------------------------------------------------------------------

        private static string Officer(PlayerInfo p, string command, string[] args, float now)
        {
            if (command == "ligne") return Raise(p, args);

            var c = LineManager.GetCommander(p.Id);
            if (c == null || !c.HasLine) return "Vous n'avez pas de ligne. " + Help;
            var line = c.Line;
            switch (command)
            {
                case "renvoyer":
                    return LineManager.EndLine(c, "renvoyée", now, false) + " hommes renvoyés : la ligne se disperse.";
                case "former":
                    return line.FormOnOfficer(now) ? "La ligne se forme devant vous." : "Position inconnue, réessayez.";
                case "suivre":
                    line.Follow(now);
                    return "La ligne vous suit.";
                case "rangs":
                    var ranks = line.Ranks == 1 ? 2 : 1;
                    if (args.Length > 0 && (args[0] == "1" || args[0] == "2")) ranks = args[0] == "1" ? 1 : 2;
                    line.SetRanks(ranks, now);
                    return ranks == 1 ? "Rang simple." : "Rang double.";
                // Seul moyen de mettre la baïonnette : FixBayonet n'est dans aucun menu vocal (docs/CARBONPLAYERS.md §5.2).
                case "baionnette":
                case "baïonnette":
                    line.ToggleBayonet(now);
                    return "Baïonnette : ordre donné.";
                case "accroupi":
                    line.ToggleCrouch(now);
                    return "Accroupi / debout : ordre donné.";
                case "pas":
                    var run = !line.Running;
                    if (line.Mode == LineMode.Marching) line.March(run, now);
                    else line.SetRunning(run);
                    return run ? "Pas de course." : "Pas ordinaire.";
            }
            return null;
        }

        /// <summary>!ligne &lt;nombre&gt; [classe] [uniforme]</summary>
        private static string Raise(PlayerInfo p, string[] args)
        {
            if (args.Length == 0 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                || count < 1 || count > MaxPerCommand)
                return Help + " Classes : " + CommanderConfig.ClassList() + ".";

            var playerClass = CommanderConfig.DefaultClass;
            if (args.Length > 1 && !GameIds.TryParseClass(args[1], out playerClass))
                return "Classe inconnue : " + args[1] + ". Classes : " + CommanderConfig.ClassList() + ".";

            var uniform = -1;
            if (args.Length > 2 && (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uniform) || uniform < 0))
                return "Uniforme invalide : " + args[2] + " (numéro attendu).";

            return LineManager.Raise(p, count, playerClass, uniform);
        }

        // ---- Admins --------------------------------------------------------------------------

        /// <summary>!cmd [etat | on | off | max &lt;n&gt; | maxofficier &lt;n&gt; | vider | config] ; jusqu'au redémarrage.</summary>
        private static string Admin(string admin, string[] args, float now)
        {
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "etat";
            switch (sub)
            {
                case "etat":
                case "état":
                    return LineManager.Status();
                case "config":
                    return CommanderConfig.Describe();
                case "on":
                case "off":
                    CommanderConfig.EnabledOverride = sub == "on";
                    if (sub == "off") LineManager.Clear("désactivation par " + admin, true);
                    CmdLog.Info("Admin " + admin + " : cmd " + sub);
                    return sub == "on" ? "Lignes de bots activées." : "Lignes de bots désactivées, bots supprimés.";
                case "max":
                    if (args.Length < 2 || !TryParseCount(args[1], 0, 250, out var max)) return "!cmd max <0 à 250>";
                    CommanderConfig.MaxBotsOverride = max;
                    CmdLog.Info("Admin " + admin + " : plafond global " + max);
                    return "Plafond global : " + max + " (les lignes existantes ne sont pas réduites).";
                case "maxofficier":
                    if (args.Length < 2 || !TryParseCount(args[1], 1, 100, out var per)) return "!cmd maxofficier <1 à 100>";
                    CommanderConfig.MaxPerOfficerOverride = per;
                    CmdLog.Info("Admin " + admin + " : plafond par officier " + per);
                    return "Plafond par officier : " + per + ".";
                case "vider":
                    LineManager.Clear("commande de " + admin, true);
                    return "Tous les bots du mod sont supprimés.";
                default:
                    return "!cmd etat | on | off | max <n> | maxofficier <n> | vider | config";
            }
        }

        private static bool TryParseCount(string text, int min, int max, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }
    }
}
