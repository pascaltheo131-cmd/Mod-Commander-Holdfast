using System.Collections.Generic;

namespace HoldfastCommander
{
    /// <summary>
    /// Sanctions (lot 3) : infractions par joueur humain, comptées pour la manche seulement (remises à zéro à chaque manche).
    /// Échelle CMD:sanctions (« avertir,public,slay ») : la n-ième infraction applique la n-ième action, puis la dernière se répète.
    /// Actions : avertir (message privé), public (annonce), renvoi (ligne renvoyée + levées bloquées pour la manche), slay, kick.
    /// Admins exemptés. Tout est journalisé dans CmdLog ; rien n'est conservé entre manches.
    /// </summary>
    public static class Discipline
    {
        /// <summary>Échelle des sanctions (seul réglage actif par défaut ; il ne sert que si une infraction est activée).</summary>
        public static string[] Ladder = { "avertir", "public", "slay" };
        /// <summary>Tir allié mortel (CMD:tir_allie on|off).</summary>
        public static bool FriendlyFire;
        /// <summary>Bots de sa propre ligne tués par l'officier avant infraction (CMD:tue_ses_bots, 0 = désactivé).</summary>
        public static int OwnBotsLimit;

        private static readonly Dictionary<int, int> Offences = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> OwnBotsKilled = new Dictionary<int, int>();
        private static readonly HashSet<int> RaiseBan = new HashSet<int>();
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            CommanderConfig.ExtraApply.Add(Apply);
        }

        private static bool Apply(string key, string value)
        {
            switch (key)
            {
                case "sanctions":
                    var list = new List<string>();
                    foreach (var part in value.Split(','))
                    {
                        var a = part.Trim().ToLowerInvariant();
                        if (a == "avertir" || a == "public" || a == "renvoi" || a == "slay" || a == "kick") list.Add(a);
                        else if (a.Length > 0) CmdLog.Info("Sanction inconnue ignorée : " + a);
                    }
                    if (list.Count > 0) Ladder = list.ToArray();
                    return true;
                case "tir_allie": case "tir_allié": FriendlyFire = CommanderConfig.ParseBool(key, value, FriendlyFire); return true;
                case "tue_ses_bots": OwnBotsLimit = CommanderConfig.ParseInt(key, value, 0, 100, OwnBotsLimit); return true;
            }
            return false;
        }

        public static void ResetRound()
        {
            Offences.Clear();
            OwnBotsKilled.Clear();
            RaiseBan.Clear();
        }

        public static bool RaiseBlocked(int playerId) => RaiseBan.Contains(playerId);

        /// <summary>Mort donnée par un joueur : tir allié et bots de sa propre ligne.</summary>
        public static void OnKill(int killerId, int victimId)
        {
            if (killerId == victimId || killerId < 0 || LineManager.IsOurBot(killerId)) return;
            var killer = Players.Get(killerId);
            var victim = Players.Get(victimId);
            if (killer == null || killer.IsBot || victim == null) return;
            var victimLine = LineManager.LineOfBot(victimId);
            if (victimLine != null && victimLine.Owner != null && !victimLine.Owner.IsAi && victimLine.Owner.PlayerId == killerId)
            {
                if (OwnBotsLimit <= 0) return;
                OwnBotsKilled.TryGetValue(killerId, out var n);
                OwnBotsKilled[killerId] = ++n;
                if (n > OwnBotsLimit) Offend(killerId, "a tué " + n + " bots de sa propre ligne");
                return;
            }
            if (!FriendlyFire || killer.Faction == GameIds.FactionCountry.None || killer.Faction != victim.Faction) return;
            Offend(killerId, "tir allié mortel sur " + Players.NameOf(victimId));
        }

        /// <summary>Infraction d'un joueur humain : action suivante de l'échelle.</summary>
        public static void Offend(int playerId, string reason)
        {
            var p = Players.Get(playerId);
            if (p == null || p.IsBot || LineManager.IsOurBot(playerId)) return;
            if (p.IsAdmin)
            {
                CmdLog.Info("Infraction ignorée (admin) : " + p.Name + " — " + reason);
                return;
            }
            Offences.TryGetValue(playerId, out var n);
            Offences[playerId] = ++n;
            var action = Ladder[System.Math.Min(n, Ladder.Length) - 1];
            CmdLog.Info("Infraction " + n + " de " + p.Name + " (" + reason + ") : " + action + ".");
            switch (action)
            {
                case "avertir":
                    ServerConsole.Private(playerId, "Avertissement : " + reason + ".");
                    break;
                case "public":
                    ServerConsole.Broadcast(p.Name + " : " + reason + " (infraction " + n + ").");
                    break;
                case "renvoi":
                    RaiseBan.Add(playerId);
                    var c = LineManager.GetCommander(playerId);
                    if (c != null && !c.IsAi && c.Line != null) LineManager.Dismiss(c, Clock.Now);
                    ServerConsole.Private(playerId, "Sanction : " + reason + ". Ligne renvoyée, levées interdites jusqu'à la fin de la manche.");
                    break;
                case "slay":
                    ServerConsole.Private(playerId, "Sanction : " + reason + ".");
                    ServerConsole.Slay(playerId);
                    break;
                case "kick":
                    ServerConsole.Execute("serverAdmin kick " + ServerConsole.Id(playerId) + " " + reason.Replace('\n', ' '), "Kick");
                    break;
            }
        }
    }
}
