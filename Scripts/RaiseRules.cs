using System.Globalization;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Règles de levée (lots 2 et 5) : attente en début de manche, ennemi trop proche, fin de manche, délai entre deux charges,
    /// et équilibre des camps (le camp qui a moins de joueurs lève des lignes plus grosses). Lignes des joueurs seulement.
    /// </summary>
    public static class RaiseRules
    {
        /// <summary>Secondes de manche avant les premières levées (CMD:levee_attente).</summary>
        public static float Wait = 60f;
        /// <summary>Refus si un ennemi vivant est à moins de N m de l'officier ; 0 = désactivé (CMD:levee_distance).</summary>
        public static float EnemyDistance;
        /// <summary>Refus s'il reste moins de N s de manche ; 0 = désactivé (CMD:levee_fin).</summary>
        public static float EndCutoff;
        /// <summary>Délai minimal entre deux charges d'une ligne de joueur ; 0 = désactivé (CMD:charge_delai).</summary>
        public static float ChargeDelay;
        /// <summary>Facteur maximal de l'équilibre des camps (CMD:bonus_max) ; 1 = désactivé.</summary>
        public static float BonusMax = 2f;

        private static bool _registered;
        private static bool _announced;

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
                case "levee_attente": Wait = CommanderConfig.ParseFloat(key, value, 0f, 1800f, 60f); return true;
                case "levee_distance": EnemyDistance = CommanderConfig.ParseFloat(key, value, 0f, 1000f, 0f); return true;
                case "levee_fin": EndCutoff = CommanderConfig.ParseFloat(key, value, 0f, 3600f, 0f); return true;
                case "charge_delai": ChargeDelay = CommanderConfig.ParseFloat(key, value, 0f, 600f, 0f); return true;
                case "levee_auto": AutoRaise = CommanderConfig.ParseBool(key, value, AutoRaise); return true;
                case "levee_auto_taille": AutoSize = CommanderConfig.ParseInt(key, value, 1, 100, AutoSize); return true;
                case "bonus_max": BonusMax = CommanderConfig.ParseFloat(key, value, 1f, 5f, 2f); return true;
                default: return false;
            }
        }

        /// <summary>Levée automatique (CMD:levee_auto) : AutoDelay s après l'apparition d'un officier ou d'un cavalier sans ligne
        /// (pas avant l'ouverture des levées, CMD:levee_attente).</summary>
        public static bool AutoRaise = true;
        /// <summary>Hommes d'une ligne d'infanterie levée automatiquement (CMD:levee_auto_taille) ; un cavalier lève son escadron.</summary>
        public static int AutoSize = 15;
        private static readonly System.Collections.Generic.Dictionary<int, float> AutoPending = new System.Collections.Generic.Dictionary<int, float>();
        private static readonly System.Collections.Generic.List<int> AutoDue = new System.Collections.Generic.List<int>();
        // Délai après l'apparition du joueur.
        private const float AutoDelay = 10f;

        public static bool IsOpen => RoundTime.Elapsed >= Wait;

        /// <summary>Annonce publique à l'ouverture des levées, et levées automatiques (appelé à chaque image).</summary>
        public static void Tick()
        {
            if (!CommanderConfig.IsEnabled) return;
            var open = IsOpen;
            if (!open) { _announced = false; return; }
            if (!_announced)
            {
                _announced = true;
                if (Wait > 0f) ServerConsole.Broadcast(AutoRaise ? "Les levées sont ouvertes : officiers et cavaliers reçoivent leurs hommes." : "Les levées sont ouvertes : !ligne");
            }
            if (AutoPending.Count == 0) return;
            AutoDue.Clear();
            foreach (var kv in AutoPending) if (Clock.Now >= kv.Value) AutoDue.Add(kv.Key);
            foreach (var id in AutoDue)
            {
                AutoPending.Remove(id);
                var p = Players.Get(id);
                if (p == null || p.IsBot || !p.Alive) continue;
                var c = LineManager.GetCommander(id);
                if (c != null && c.HasLine) continue;
                string reply = null;
                if (GameIds.IsCavalry(p.Class)) reply = LineManager.Raise(p, LineManager.CavalryMax, p.Class, -1);
                else if (GameIds.IsInfantryOfficer(p.Class)) reply = LineManager.Raise(p, AutoSize, CommanderConfig.DefaultClass, -1);
                if (reply == null) continue;
                ServerConsole.Private(id, reply);
                CmdLog.Info("Levée automatique de " + p.Name + " : " + reply);
            }
        }

        /// <summary>Officier ou cavalier apparu : levée automatique AutoDelay s plus tard s'il n'a toujours pas de ligne.</summary>
        public static void OnPlayerSpawned(PlayerInfo p)
        {
            if (!AutoRaise || p.IsBot || !CommanderConfig.IsEnabled) return;
            if (!GameIds.IsCavalry(p.Class) && !GameIds.IsInfantryOfficer(p.Class)) return;
            AutoPending[p.Id] = Clock.Now + AutoDelay;
        }

        /// <summary>Refus d'une levée ou de renforts par un joueur (null si autorisé).</summary>
        public static string Refusal(PlayerInfo officer)
        {
            var left = Mathf.CeilToInt(Wait - RoundTime.Elapsed);
            if (left > 0) return "Levées dans " + left.ToString(CultureInfo.InvariantCulture) + " s.";
            var remaining = RoundTime.Remaining;
            if (EndCutoff > 0f && remaining >= 0f && remaining < EndCutoff) return "Plus de levées : la manche s'achève.";
            if (EnemyDistance > 0f && officer != null && officer.TryGetPosition(out var pos)
                && LineManager.NearestEnemy(officer.Faction, pos, EnemyDistance) >= 0)
                return "Pas de levée avec un ennemi à moins de " + Mathf.RoundToInt(EnemyDistance) + " m.";
            return null;
        }

        /// <summary>Charge d'une ligne de joueur trop tôt après la précédente : secondes restantes, sinon 0.</summary>
        public static int ChargeWait(Line line, float now)
        {
            if (ChargeDelay <= 0f || line.Owner == null || line.Owner.IsAi) return 0;
            return Mathf.Max(0, Mathf.CeilToInt(ChargeDelay - (now - line.ChargeStartedAt)));
        }

        /// <summary>Joueurs humains du camp le plus nombreux en face ÷ joueurs du camp donné, borné entre 1 et BonusMax.</summary>
        public static float BalanceFactor(int faction)
        {
            if (BonusMax <= 1f || faction == GameIds.FactionCountry.None) return 1f;
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var p in Players.All)
            {
                if (p.IsBot || !p.Joined || p.Faction == GameIds.FactionCountry.None) continue;
                counts.TryGetValue(p.Faction, out var n);
                counts[p.Faction] = n + 1;
            }
            counts.TryGetValue(faction, out var mine);
            var other = 0;
            foreach (var kv in counts) if (kv.Key != faction && kv.Value > other) other = kv.Value;
            if (mine <= 0 || other <= mine) return 1f;
            return Mathf.Min(BonusMax, (float)other / mine);
        }

        /// <summary>Taille compensée par l'équilibre des camps (100 au plus).</summary>
        public static int Scale(int size, int faction)
        {
            var f = BalanceFactor(faction);
            return f <= 1f ? size : Mathf.Min(100, Mathf.FloorToInt(size * f));
        }

        /// <summary>Reste du quota de l'officier, quota compensé par l'équilibre des camps.</summary>
        public static int QuotaRemaining(int playerId, int faction) =>
            Quota.Enabled ? Mathf.Max(0, Scale(CommanderConfig.OfficerQuota, faction) - Quota.UsedBy(playerId)) : int.MaxValue;

        /// <summary>Message à l'officier quand sa ligne est grossie (vide sinon).</summary>
        public static string BonusNotice(int baseMax, int max)
        {
            return max > baseMax ? " Votre camp a moins de joueurs : ligne jusqu'à " + max + " hommes." : "";
        }
    }
}
