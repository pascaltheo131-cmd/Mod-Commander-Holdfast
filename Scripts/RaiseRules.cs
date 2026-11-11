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
        /// <summary>Secondes de manche avant que !ligne soit permis (CMD:levee_manuelle) ; négatif = comme levee_attente.</summary>
        public static float ManualWait = -1f;
        private static float ManualOpenAt => ManualWait >= 0f ? Mathf.Min(ManualWait, Wait) : Wait;
        private static bool _manualAnnounced;
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
                case "levee_manuelle": ManualWait = CommanderConfig.ParseFloat(key, value, -1f, 1800f, -1f); return true;
                case "levee_auto": AutoRaise = CommanderConfig.ParseBool(key, value, AutoRaise); return true;
                case "levee_auto_taille": AutoSize = CommanderConfig.ParseInt(key, value, 1, 100, AutoSize); return true;
                case "tirailleurs_nombre": SkirmishSize = CommanderConfig.ParseInt(key, value, 1, 100, SkirmishSize); return true;
                case "bonus_max": BonusMax = CommanderConfig.ParseFloat(key, value, 1f, 5f, 2f); return true;
                default: return false;
            }
        }

        /// <summary>Levée automatique (CMD:levee_auto) : AutoDelay s après l'apparition d'un officier ou d'un cavalier sans ligne
        /// (pas avant l'ouverture des levées, CMD:levee_attente).</summary>
        public static bool AutoRaise = false;
        /// <summary>Tirailleurs levés par !skirm (CMD:tirailleurs_nombre).</summary>
        public static int SkirmishSize = 9;
        /// <summary>Hommes d'une ligne d'infanterie levée automatiquement (CMD:levee_auto_taille) ; un cavalier lève son escadron.</summary>
        public static int AutoSize = 15;
        private static readonly System.Collections.Generic.Dictionary<int, float> AutoPending = new System.Collections.Generic.Dictionary<int, float>();
        private static readonly System.Collections.Generic.List<int> AutoDue = new System.Collections.Generic.List<int>();
        // Délai après l'apparition du joueur (le temps que sa position soit connue) : sa ligne arrive « directement ».
        private const float AutoDelay = 2f;

        /// <summary>Demande !ligne faite avant l'heure des levées : servie à l'heure avec ses paramètres.</summary>
        private struct Request { public int Count, Class, Uniform; public string Prefix; }
        private static readonly System.Collections.Generic.Dictionary<int, Request> Requests = new System.Collections.Generic.Dictionary<int, Request>();

        public static void Remember(int playerId, int count, int playerClass, int uniform, string prefix) =>
            Requests[playerId] = new Request { Count = count, Class = playerClass, Uniform = uniform, Prefix = prefix };

        public static void ClearRequests() => Requests.Clear();

        public static bool IsOpen => RoundTime.Elapsed >= Wait;

        /// <summary>Annonce publique à l'ouverture des levées, et levées automatiques (appelé à chaque image).</summary>
        public static void Tick()
        {
            if (!CommanderConfig.IsEnabled) return;
            if (RoundTime.Elapsed < ManualOpenAt) _manualAnnounced = false;
            else if (!_manualAnnounced)
            {
                _manualAnnounced = true;
                if (ManualOpenAt > 0f && ManualOpenAt < Wait) ServerConsole.Broadcast("Levées ouvertes : !ligne pour lever vos hommes (sinon, levée automatique au LIVE).");
            }
            var open = IsOpen;
            if (!open) { _announced = false; return; }
            if (!_announced)
            {
                _announced = true;
                if (Wait > 0f) ServerConsole.Broadcast(AutoRaise ? "Les lignes arrivent : officiers et cavaliers reçoivent leurs hommes." : "Les levées sont ouvertes : !ligne");
                // Heure des levées : tout officier ou cavalier déjà en jeu et sans ligne reçoit ses hommes (s'il n'est pas déjà en attente).
                if (AutoRaise)
                    foreach (var p in Players.All)
                        if (!p.IsBot && p.Alive && !AutoPending.ContainsKey(p.Id) && (GameIds.IsCavalry(p.Class) || GameIds.IsInfantryOfficer(p.Class)))
                            AutoPending[p.Id] = Clock.Now;
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
                if (Requests.TryGetValue(id, out var r))
                {
                    Requests.Remove(id);
                    reply = LineManager.Raise(p, r.Count, GameIds.IsCavalry(p.Class) ? p.Class : r.Class, r.Uniform, r.Prefix);
                }
                else if (GameIds.IsCavalry(p.Class)) reply = LineManager.Raise(p, LineManager.CavalryMax, p.Class, -1);
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
            var left = Mathf.CeilToInt(ManualOpenAt - RoundTime.Elapsed);
            if (left > 0) return "Levées dans " + left.ToString(CultureInfo.InvariantCulture) + " s.";
            var remaining = RoundTime.Remaining;
            if (EndCutoff > 0f && remaining >= 0f && remaining < EndCutoff) return "Levées fermées pour cette manche.";
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
            Quota.EnabledFor(playerId) ? Mathf.Max(0, Scale(Quota.LimitFor(playerId), faction) - Quota.UsedBy(playerId)) : int.MaxValue;

        /// <summary>Message à l'officier quand sa ligne est grossie (vide sinon).</summary>
        public static string BonusNotice(int baseMax, int max)
        {
            return max > baseMax ? " Votre camp a moins de joueurs : ligne jusqu'à " + max + " hommes." : "";
        }
    }
}
