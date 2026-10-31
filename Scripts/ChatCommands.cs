using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HoldfastCommander
{
    /// <summary>
    /// Commandes du chat. Officiers : !ligne, !renvoyer, !former, !suivre, !rangs, !baionnette, !accroupi, !pas,
    /// !reprendre (ligne sans officier), !moral, !renforts, !carre, !skin, !skins.
    /// Admins (connectés par rc login) : !cmd …, ou « rc cmd … » depuis la console. Réponses en message privé (bandeau).
    /// Les messages du mod ne commencent jamais par « ! » : ils ne peuvent pas être pris pour une commande.
    /// </summary>
    public static class ChatCommands
    {
        private const float CommandCooldown = 1f;
        private const int MaxPerCommand = 100;
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        private const string Help = "!ligne <nombre> [classe] [uniforme] lève une ligne. Puis : !former, !suivre, !rangs [1|2], "
                                    + "!baionnette, !accroupi, !pas, !carre, !moral, !renforts, !renvoyer. !reprendre : ligne sans officier proche. "
                                    + "Uniformes : !skins [classe], !skin <classe> <uniforme>.";

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
            // Une réponse longue (liste d'uniformes) peut tenir sur plusieurs messages, séparés par « \n ».
            if (string.IsNullOrEmpty(reply)) return;
            foreach (var part in reply.Split('\n'))
            {
                if (part.Trim().Length > 0) ServerConsole.Private(playerId, part);
            }
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
                case "reprendre": case "moral": case "renforts":
                case "carre": case "carré": case "skin": case "skins":
                    return true;
                default:
                    return false;
            }
        }

        // ---- Officiers -----------------------------------------------------------------------

        private static string Officer(PlayerInfo p, string command, string[] args, float now)
        {
            if (command == "ligne") return Raise(p, args, now);
            if (command == "reprendre") return LineManager.TryTakeOver(p, false, false, now);
            if (command == "skin") return Skin(p, args);
            if (command == "skins") return Skins(p, args);

            var c = LineManager.GetCommander(p.Id);
            if (c == null || !c.HasLine) return "Vous n'avez pas de ligne. " + Help;
            var line = c.Line;
            switch (command)
            {
                case "moral":
                    return Morale.Describe(line);
                case "renforts":
                    return LineManager.Reinforce(c, now) ?? "Renforts désactivés sur ce serveur.";
                case "renvoyer":
                    return LineManager.EndLine(c, "renvoyée", now, false) + " hommes renvoyés : la ligne se disperse.";
                case "former":
                    return line.FormOnOfficer(now) ? "La ligne se forme devant vous." : "Position inconnue, réessayez.";
                case "carre":
                case "carré":
                    if (!CommanderConfig.Square) return "Le carré est désactivé sur ce serveur.";
                    return line.SquareOnOfficer(now) ? "Formez le carré ! (sortie : !former ou ordre « former la ligne »)" : "Position inconnue, réessayez.";
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

        /// <summary>!ligne &lt;nombre&gt; [classe] [uniforme] ; en carré, !ligne fait d'abord reformer la ligne.</summary>
        private static string Raise(PlayerInfo p, string[] args, float now)
        {
            var c = LineManager.GetCommander(p.Id);
            var leftSquare = c != null && c.HasLine && c.Line.Mode == LineMode.Square && c.Line.FormOnOfficer(now);
            if (leftSquare && args.Length == 0) return "La ligne se reforme devant vous.";
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

        // ---- Uniformes (lot B, n° 15) --------------------------------------------------------

        /// <summary>!skin &lt;classe&gt; &lt;uniforme&gt; : uniforme des prochains bots de cette classe (0 : retour au défaut).</summary>
        private static string Skin(PlayerInfo p, string[] args)
        {
            if (p.Faction == GameIds.FactionCountry.None) return "Faction inconnue : réapparaissez puis réessayez.";
            if (args.Length < 2 || !GameIds.TryParseClass(args[0], out var playerClass)
                || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return "!skin <classe> <uniforme> (0 : uniforme par défaut). !skins pour la liste.";
            if (id == 0)
            {
                p.Skins.Remove(playerClass);
                return GameIds.ClassName(playerClass) + " : uniforme par défaut.";
            }
            if (!Uniforms.IsValid(p.Faction, playerClass, id))
                return "Uniforme " + id + " inconnu pour " + GameIds.ClassName(playerClass) + " (" + GameIds.FactionName(p.Faction) + "). "
                       + "!skins " + GameIds.ClassName(playerClass) + " pour la liste.";
            p.Skins[playerClass] = id;
            return GameIds.ClassName(playerClass) + " : vos prochains bots porteront l'uniforme " + id + " (" + Uniforms.NameOf(p.Faction, playerClass, id) + ").";
        }

        /// <summary>!skins [classe] : uniformes valides pour la faction du joueur (liste courte, ou noms d'une classe).</summary>
        private static string Skins(PlayerInfo p, string[] args)
        {
            if (p.Faction == GameIds.FactionCountry.None) return "Faction inconnue : réapparaissez puis réessayez.";
            if (args.Length > 0)
            {
                if (!GameIds.TryParseClass(args[0], out var playerClass)) return "Classe inconnue : " + args[0] + ".";
                return Chunk(Uniforms.Describe(p.Faction, playerClass), 240);
            }
            var parts = new System.Collections.Generic.List<string>();
            var classes = new System.Collections.Generic.List<int>(CommanderConfig.AllowedClasses);
            classes.Sort();
            classes.Add(GameIds.PlayerClass.FlagBearer);
            classes.Add(GameIds.PlayerClass.Musician);
            foreach (var cls in classes)
            {
                var range = Uniforms.Range(p.Faction, cls);
                if (range != null) parts.Add(range);
            }
            if (parts.Count == 0) return "Aucun uniforme d'infanterie connu pour " + GameIds.FactionName(p.Faction) + ".";
            return GameIds.FactionName(p.Faction) + " : " + string.Join(", ", parts) + ". Noms : !skins <classe>.";
        }

        /// <summary>Coupe un texte en messages d'au plus max caractères (aux virgules), séparés par « \n ».</summary>
        private static string Chunk(string text, int max)
        {
            var sb = new System.Text.StringBuilder();
            var line = "";
            foreach (var piece in text.Split(new[] { ", " }, StringSplitOptions.None))
            {
                var add = line.Length == 0 ? piece : line + ", " + piece;
                if (add.Length > max && line.Length > 0)
                {
                    sb.Append(line).Append(",\n");
                    line = piece;
                }
                else line = add;
            }
            return sb.Append(line).ToString();
        }

        // ---- Admins --------------------------------------------------------------------------

        /// <summary>!cmd [etat | on | off | max &lt;n&gt; | maxofficier &lt;n&gt; | vider | config | ia on|off|etat] ; jusqu'au redémarrage.</summary>
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
                case "ia":
                    var aiSub = args.Length > 1 ? args[1].ToLowerInvariant() : "etat";
                    if (aiSub == "on" || aiSub == "off")
                    {
                        CommanderConfig.AiOverride = aiSub == "on";
                        if (aiSub == "off") AiDirector.WithdrawAll("désactivation par " + admin, now, true);
                        CmdLog.Info("Admin " + admin + " : cmd ia " + aiSub);
                        return aiSub == "on" ? "Lignes IA activées (camps sans joueur face à des joueurs)." : "Lignes IA désactivées, elles se retirent.";
                    }
                    return AiDirector.Status();
                case "vider":
                    LineManager.Clear("commande de " + admin, true);
                    return "Tous les bots du mod sont supprimés.";
                default:
                    return "!cmd etat | on | off | max <n> | maxofficier <n> | vider | config | ia on|off|etat";
            }
        }

        private static bool TryParseCount(string text, int min, int max, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }
    }
}
