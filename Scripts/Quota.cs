using System.Collections.Generic;
using System.Globalization;

namespace HoldfastCommander
{
    /// <summary>
    /// Quota de bots par officier et par manche, cumulé : !renvoyer, la mort ou la dispersion de la ligne, une reprise ne le
    /// remettent pas à zéro. Clé = compte Steam (gardé après déconnexion/reconnexion), sinon id du joueur. Remis à zéro à
    /// chaque manche. Ne compte que les bots effectivement apparus dans la ligne d'un officier joueur (pas les relances, pas
    /// les places abandonnées, pas les lignes IA).
    /// </summary>
    public static class Quota
    {
        private static readonly Dictionary<string, int> Used = new Dictionary<string, int>();

        public static bool Enabled => CommanderConfig.OfficerQuota > 0 && !CommanderConfig.IsWarmup;

        /// <summary>Quota du joueur : celui des cavaliers (CMD:cavalerie_quota) s'il est à cheval et qu'il est réglé, sinon celui des officiers.</summary>
        public static int LimitFor(int playerId)
        {
            var p = Players.Get(playerId);
            if (p != null && GameIds.IsCavalry(p.Class) && CavalryRules.SquadronQuota >= 0) return CavalryRules.SquadronQuota;
            return CommanderConfig.OfficerQuota;
        }

        public static bool EnabledFor(int playerId) => LimitFor(playerId) > 0 && !CommanderConfig.IsWarmup;

        private static string Key(int playerId)
        {
            var p = Players.Get(playerId);
            if (p != null && p.SteamId != 0) return "s" + p.SteamId.ToString(CultureInfo.InvariantCulture);
            return "i" + playerId.ToString(CultureInfo.InvariantCulture);
        }

        public static int UsedBy(int playerId) => Used.TryGetValue(Key(playerId), out var n) ? n : 0;

        /// <summary>Reste du quota (sans les places déjà demandées) ; int.MaxValue sans quota.</summary>
        public static int Remaining(int playerId) =>
            EnabledFor(playerId) ? System.Math.Max(0, LimitFor(playerId) - UsedBy(playerId)) : int.MaxValue;

        public static void Consume(int playerId)
        {
            if (playerId < 0) return;
            var key = Key(playerId);
            Used[key] = (Used.TryGetValue(key, out var n) ? n : 0) + 1;
        }

        public static void ResetPlayer(int playerId) => Used.Remove(Key(playerId));

        public static void ResetAll() => Used.Clear();

        public static string Refusal(int playerId) =>
            "Quota atteint : " + LimitFor(playerId) + " bots par manche (déjà levés : " + UsedBy(playerId) + ").";

        public static string Describe(int playerId)
        {
            if (!EnabledFor(playerId)) return "Pas de quota de bots sur ce serveur.";
            return "Quota : " + UsedBy(playerId) + " bots levés sur " + LimitFor(playerId) + " par manche, reste "
                   + Remaining(playerId) + ".";
        }
    }
}
