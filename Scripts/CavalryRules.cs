using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Équilibre de la cavalerie : au plus un cavalier joueur pour CMD:cavalerie_ratio joueurs de son camp (le surnombre est
    /// renvoyé au choix de classe), et pas de charge de cavalerie avant CMD:cavalerie_charge_debut secondes de manche.
    /// </summary>
    public static class CavalryRules
    {
        public static int PlayersPerRider = 5;
        public static float ChargeStart = 60f;
        /// <summary>Cavaliers bots par escadron (CMD:cavalerie_taille).</summary>
        public static int SquadronSize = 8;
        /// <summary>Quota de bots par cavalier et par manche (CMD:cavalerie_quota) ; -1 = le quota des officiers, 0 = aucun.</summary>
        public static int SquadronQuota = -1;
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
                case "cavalerie_ratio": PlayersPerRider = CommanderConfig.ParseInt(key, value, 0, 100, PlayersPerRider); return true;
                case "cavalerie_taille": SquadronSize = CommanderConfig.ParseInt(key, value, 1, 30, SquadronSize); return true;
                case "cavalerie_quota": SquadronQuota = CommanderConfig.ParseInt(key, value, -1, 1000, SquadronQuota); return true;
                case "cavalerie_charge_debut": ChargeStart = CommanderConfig.ParseFloat(key, value, 0f, 3600f, ChargeStart); return true;
            }
            return false;
        }

        /// <summary>Vrai joueur apparu à cheval : s'il dépasse la part de cavalerie de son camp, il est tué et prévenu.</summary>
        public static void OnPlayerSpawned(PlayerInfo p)
        {
            if (PlayersPerRider <= 0 || p.IsBot || !GameIds.IsCavalry(p.Class) || p.IsAdmin) return;
            var players = 0;
            var riders = 0;
            foreach (var o in Players.All)
            {
                if (o.IsBot || o.Faction != p.Faction) continue;
                players++;
                if (o.Id != p.Id && o.Alive && GameIds.IsCavalry(o.Class)) riders++;
            }
            var allowed = Mathf.Max(1, Mathf.CeilToInt(players / (float)PlayersPerRider));
            if (riders < allowed) return;
            ServerConsole.Private(p.Id, "Cavalerie complète pour votre camp (" + allowed + " cavalier(s) pour " + players
                                        + " joueurs, 1 pour " + PlayersPerRider + ") : reprenez en Officier, une ligne de bots vous attend.");
            ServerConsole.Slay(p.Id);
            CmdLog.Info(p.Name + " : cavalerie complète (" + riders + "/" + allowed + "), renvoyé au choix de classe.");
        }
    }
}
