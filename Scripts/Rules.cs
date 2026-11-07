using System.Collections.Generic;

namespace HoldfastCommander
{
    /// <summary>Règlement des joueurs (CMD:regle1…regle8) affiché par !regles ; rappel au début de manche (CMD:rappelregles).</summary>
    public static class Rules
    {
        private static readonly string[] Defaults =
        {
            "Respectez les autres joueurs, pas d'insultes.",
            "Pas de tir allié volontaire.",
            "Suivez les ordres de votre officier et restez en ligne.",
            "Une ligne de bots par officier, pas de levée pour gêner les autres.",
            "", "", "", ""
        };

        private static readonly string[] Lines = (string[])Defaults.Clone();
        private static bool _registered;

        /// <summary>Rappel « Règlement : !regles » dans l'annonce de début de manche.</summary>
        public static bool Reminder = true;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            CommanderConfig.ExtraApply.Add(Apply);
        }

        private static bool Apply(string key, string value)
        {
            if (key == "rappelregles")
            {
                Reminder = CommanderConfig.ParseBool(key, value, true);
                return true;
            }
            if (key.Length == 6 && key.StartsWith("regle") && key[5] >= '1' && key[5] <= '8')
            {
                // Les « _ » du fichier de configuration valent des espaces (mod_variable n'accepte pas d'espace).
                Lines[key[5] - '1'] = (value ?? "").Replace('_', ' ').Trim();
                return true;
            }
            return false;
        }

        /// <summary>Règles non vides, numérotées, une par message (séparées par « \n »).</summary>
        public static string Describe()
        {
            var parts = new List<string>();
            foreach (var rule in Lines)
                if (rule.Length > 0) parts.Add((parts.Count + 1) + ". " + rule);
            return parts.Count == 0 ? "Pas de règlement particulier sur ce serveur." : "Règlement : \n" + string.Join("\n", parts);
        }
    }
}
