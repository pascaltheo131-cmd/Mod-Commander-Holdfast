using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Automod du mod (lot A), à la place de celui du jeu : il compte les bots du mod comme voisins de ligne.
    /// 1. tir en ligne (FOL) ; 2. charge générale ; 3. artillerie (début des tirs, contre-batterie) ; 4. annonces programmées.
    /// Tout est désactivé tant que la configuration ne l'active pas. Les infractions passent par Discipline.
    /// </summary>
    public static class Automod
    {
        // ---- Réglages ----
        public static bool FolEnabled;
        public static int FolMin = 2;
        public static float FolRadius = 4f;
        public static readonly HashSet<int> FolExempt = new HashSet<int>
        {
            GameIds.PlayerClass.ArmyInfantryOfficer, GameIds.PlayerClass.Sergeant, GameIds.PlayerClass.Rifleman,
            GameIds.PlayerClass.LightInfantry, GameIds.PlayerClass.Hussar, GameIds.PlayerClass.Dragoon,
            GameIds.PlayerClass.Cannoneer, GameIds.PlayerClass.Sapper, GameIds.PlayerClass.Musician,
            GameIds.PlayerClass.FlagBearer, GameIds.PlayerClass.Surgeon, GameIds.PlayerClass.Carpenter,
        };
        /// <summary>Instant (s de manche) de la charge générale, 0 = jamais au temps.</summary>
        public static float ChargeTime;
        /// <summary>Part (%) d'humains vivants sous laquelle la charge part, 0 = jamais.</summary>
        public static float ChargePercent;
        /// <summary>Humains en jeu nécessaires au déclenchement par pourcentage.</summary>
        public static int ChargeMinPlayers = 10;
        /// <summary>Compte à rebours annoncé avant la charge (s).</summary>
        public static float ChargeDelay = 30f;
        /// <summary>Début des tirs d'artillerie (s de manche), 0 = le mod n'y touche pas.</summary>
        public static float ArtyStart;
        /// <summary>Contre-batterie interdite avant cet instant (s de manche), 0 = permise.</summary>
        public static float ArtyOnArty;
        private static readonly SortedDictionary<int, KeyValuePair<float, string>> Announces = new SortedDictionary<int, KeyValuePair<float, string>>();

        // ---- État de la manche ----
        public static bool GeneralChargeActive { get; private set; }
        private static float _chargeAt = -1f;
        private static float _nextCountdown;
        private static float _nextCheck;
        private static bool _artyBlocked, _artyReleased, _artyTouched, _cavalryCapped;
        private static readonly HashSet<int> AnnouncesDone = new HashSet<int>();
        private static bool _registered;

        private const float ArtyBlockDelay = 3f;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            CommanderConfig.ExtraApply.Add(Apply);
        }

        private static bool Apply(string key, string value)
        {
            if (key.StartsWith("annonce") && int.TryParse(key.Substring(7), out var n) && n >= 1 && n <= 10)
            {
                var i = value.IndexOf(':');
                if (string.IsNullOrWhiteSpace(value) || value.Trim() == "off") Announces.Remove(n);
                else if (i > 0 && float.TryParse(value.Substring(0, i).Trim(), System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var at) && at >= 0f)
                    Announces[n] = new KeyValuePair<float, string>(at, value.Substring(i + 1).Trim());
                else CmdLog.Info("Annonce " + n + " ignorée : format <secondes>:<texte>.");
                return true;
            }
            switch (key)
            {
                case "fol": FolEnabled = CommanderConfig.ParseBool(key, value, FolEnabled); return true;
                case "fol_min": FolMin = CommanderConfig.ParseInt(key, value, 1, 20, FolMin); return true;
                case "fol_rayon": FolRadius = CommanderConfig.ParseFloat(key, value, 0.5f, 20f, FolRadius); return true;
                case "fol_exempt":
                    FolExempt.Clear();
                    foreach (var part in value.Split(','))
                    {
                        var t = part.Trim();
                        if (t.Length == 0 || t == "aucune") continue;
                        if (GameIds.TryParseClass(t, out var c)) FolExempt.Add(c);
                        else CmdLog.Info("fol_exempt : classe inconnue " + t);
                    }
                    return true;
                case "charge_generale_temps": ChargeTime = CommanderConfig.ParseFloat(key, value, 0f, 36000f, ChargeTime); return true;
                case "charge_generale_pourcent": ChargePercent = CommanderConfig.ParseFloat(key, value, 0f, 100f, ChargePercent); return true;
                case "charge_generale_min": ChargeMinPlayers = CommanderConfig.ParseInt(key, value, 1, 500, ChargeMinPlayers); return true;
                case "charge_generale_delai": ChargeDelay = CommanderConfig.ParseFloat(key, value, 0f, 300f, ChargeDelay); return true;
                case "arty_debut": ArtyStart = CommanderConfig.ParseFloat(key, value, 0f, 36000f, ArtyStart); return true;
                case "arty_contre_arty": ArtyOnArty = CommanderConfig.ParseFloat(key, value, 0f, 36000f, ArtyOnArty); return true;
            }
            return false;
        }

        /// <summary>Nouvelle manche : rien ne reste de la précédente (les commandes de rétablissement partent au Tick).</summary>
        public static void ResetRound()
        {
            GeneralChargeActive = false;
            _chargeAt = -1f;
            _nextCountdown = 0f;
            _nextCheck = 0f;
            _artyBlocked = false;
            _artyReleased = false;
            AnnouncesDone.Clear();
        }

        public static void Tick(float now)
        {
            var t = RoundTime.Elapsed;
            TickArty(t);
            TickCharge(now, t);
            foreach (var kv in Announces)
            {
                if (AnnouncesDone.Contains(kv.Key) || t < kv.Value.Key) continue;
                AnnouncesDone.Add(kv.Key);
                if (kv.Value.Value.Length > 0) ServerConsole.Broadcast(kv.Value.Value);
            }
        }

        private static void SetArty(bool allowed)
        {
            ServerConsole.Execute("set allowArtyFiring " + (allowed ? "true" : "false"), "Artillerie");
        }

        private static void TickArty(float t)
        {
            if (_artyBlocked || t < ArtyBlockDelay) return;
            _artyBlocked = true;
            if (_cavalryCapped)
            {
                // Chevaux ralentis par la charge générale de la manche précédente : vitesse rendue.
                ServerConsole.Execute("vehiclesManager forceCappedSpeed false", "Charge générale");
                _cavalryCapped = false;
            }
            if (ArtyStart > 0f)
            {
                SetArty(false);
                _artyTouched = true;
                CmdLog.Info("Artillerie interdite jusqu'à " + ArtyStart + " s.");
            }
            else if (_artyTouched)
            {
                // Interdite à la manche précédente (charge générale) : rendue.
                SetArty(true);
                _artyTouched = false;
            }
            if (ArtyStart <= 0f) _artyReleased = true;
        }

        private static void TickArtyRelease(float t)
        {
            if (_artyReleased || !_artyBlocked || t < ArtyStart) return;
            _artyReleased = true;
            if (GeneralChargeActive) return;
            SetArty(true);
            ServerConsole.Broadcast("L'artillerie peut ouvrir le feu.");
            CmdLog.Info("Artillerie autorisée.");
        }

        private static void TickCharge(float now, float t)
        {
            TickArtyRelease(t);
            if (GeneralChargeActive) return;
            if (_chargeAt < 0f)
            {
                if (now < _nextCheck) return;
                _nextCheck = now + 1f;
                string why = null;
                if (ChargeTime > 0f && t >= ChargeTime - ChargeDelay) why = "temps";
                else if (ChargePercent > 0f)
                {
                    int total = 0, alive = 0;
                    foreach (var p in Players.All)
                    {
                        if (p.IsBot || LineManager.IsOurBot(p.Id) || p.Faction == GameIds.FactionCountry.None) continue;
                        total++;
                        if (p.Alive) alive++;
                    }
                    if (total >= ChargeMinPlayers && alive * 100f < ChargePercent * total) why = alive + "/" + total + " vivants";
                }
                if (why == null) return;
                _chargeAt = now + ChargeDelay;
                _nextCountdown = now;
                CmdLog.Info("Charge générale dans " + ChargeDelay + " s (" + why + ").");
            }
            if (now >= _chargeAt)
            {
                StartCharge(now);
                return;
            }
            if (now < _nextCountdown) return;
            var left = Mathf.CeilToInt(_chargeAt - now);
            ServerConsole.Broadcast("Charge générale dans " + left + " s !");
            _nextCountdown = now + (left > 10 ? 10f : 5f);
        }

        private static void StartCharge(float now)
        {
            GeneralChargeActive = true;
            ServerConsole.Broadcast("CHARGE GÉNÉRALE ! Pistolets et artillerie interdits.");
            CmdLog.Info("Charge générale.");
            SetArty(false);
            _artyTouched = true;
            ServerConsole.Execute("vehiclesManager forceCappedSpeed true", "Charge générale");
            ServerConsole.Execute("vehiclesManager cappedSpeed 2", "Charge générale");
            _cavalryCapped = true;
            foreach (var line in LineManager.ActiveLines())
            {
                if (line.Ended || line.Mode == LineMode.Dispersing) continue;
                line.Charge(null, now);
            }
        }

        /// <summary>Tir d'un humain : tir en ligne, et pistolet pendant la charge générale.</summary>
        public static void OnShoot(int playerId, bool dryShot)
        {
            if (dryShot) return;
            var p = Players.Get(playerId);
            if (p == null || p.IsBot || !p.Alive) return;
            if (GeneralChargeActive && p.Weapon != null && p.Weapon.IndexOf("Pistol", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Discipline.Offend(playerId, "tir au pistolet pendant la charge générale");
                return;
            }
            if (!FolEnabled || FolExempt.Contains(p.Class) || !p.TryGetPosition(out var pos)) return;
            var r2 = FolRadius * FolRadius;
            var n = 0;
            foreach (var o in Players.All)
            {
                if (o.Id == playerId || !o.Alive || o.Faction != p.Faction || !o.TryGetPosition(out var op)) continue;
                if ((op - pos).sqrMagnitude > r2) continue;
                if (++n >= FolMin) return;
            }
            Discipline.Offend(playerId, "tir hors de la ligne (" + n + "/" + FolMin + " voisins)");
        }

        /// <summary>Contre-batterie trop tôt : Cannoneer tué par un Cannoneer adverse.</summary>
        public static void OnKill(int killerId, int victimId)
        {
            if (ArtyOnArty <= 0f || RoundTime.Elapsed >= ArtyOnArty) return;
            var k = Players.Get(killerId);
            var v = Players.Get(victimId);
            if (k == null || v == null || k.IsBot || k.Faction == v.Faction) return;
            if (k.Class != GameIds.PlayerClass.Cannoneer || v.Class != GameIds.PlayerClass.Cannoneer) return;
            Discipline.Offend(killerId, "contre-batterie avant " + Mathf.RoundToInt(ArtyOnArty) + " s");
        }
    }
}
