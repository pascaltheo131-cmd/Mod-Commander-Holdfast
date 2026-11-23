using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Carré automatique : une charge de cavalerie ennemie (au moins GroupMin cavaliers à moins de GroupRadius m les uns des
    /// autres, à plus de MinSpeed m/s vers la ligne) qui arrive à moins de Distance m fait former le carré (le carré de
    /// !carre). Plus aucun cavalier ennemi à moins de ClearDistance m depuis ClearSeconds s : retour à la formation
    /// précédente, sauf si l'officier a donné un autre ordre entre-temps. Activé par !carreauto (CMD:carre_auto pour tous) ;
    /// toujours actif pour les lignes IA. Détection toutes les Interval s sur la liste des joueurs, sans parcours de scène.
    /// </summary>
    public static class AutoSquare
    {
        /// <summary>Carré automatique actif par défaut pour les officiers joueurs (CMD:carre_auto).</summary>
        public static bool DefaultOn;
        /// <summary>Distance de déclenchement en mètres (CMD:carre_auto_distance).</summary>
        public static float Distance = 60f;

        private const float ClearDistance = 80f;
        private const float ClearSeconds = 10f;
        private const float MinSpeed = 5f;
        private const float GroupRadius = 25f;
        private const int GroupMin = 3;
        private const float Interval = 0.5f;
        // Cap vers la ligne : à moins de 45° de la direction de la ligne.
        private const float HeadingCos = 0.7071f;

        private static bool _registered;
        private static float _next;
        private static float _lastSample = -1f;
        private static readonly Dictionary<int, bool> Choice = new Dictionary<int, bool>();
        private static Dictionary<int, Vector3> _lastPos = new Dictionary<int, Vector3>();
        private static Dictionary<int, Vector3> _newPos = new Dictionary<int, Vector3>();

        private struct Rider { public int Faction; public Vector3 Pos, Vel; public bool Fast; }
        private static readonly List<Rider> Riders = new List<Rider>();

        private class State { public LineMode Mode; public Vector3 Anchor; public float Yaw; public int Serial; public float ClearSince = -1f; }
        private static readonly Dictionary<Line, State> Active = new Dictionary<Line, State>();

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            CommanderConfig.ExtraApply.Add((key, value) =>
            {
                switch (key)
                {
                    case "carre_auto": case "auto_square": DefaultOn = CommanderConfig.ParseBool(key, value, false); return true;
                    case "carre_auto_distance": case "auto_square_distance": Distance = CommanderConfig.ParseFloat(key, value, 10f, 200f, 60f); return true;
                    default: return false;
                }
            });
        }

        /// <summary>Nouvelle manche : plus de carré en cours ni de positions anciennes (les choix des officiers restent).</summary>
        public static void Reset()
        {
            Active.Clear();
            _lastPos.Clear();
            _lastSample = -1f;
        }

        public static bool EnabledFor(Line line) =>
            line.Owner == null || line.Owner.IsAi || (Choice.TryGetValue(line.Owner.PlayerId, out var on) ? on : DefaultOn);

        /// <summary>!carreauto on|off (sans argument : bascule).</summary>
        public static string Command(PlayerInfo p, string[] args)
        {
            var on = Choice.TryGetValue(p.Id, out var cur) ? cur : DefaultOn;
            if (args.Length == 0) on = !on;
            else
            {
                var a = args[0].ToLowerInvariant();
                if (a == "on" || a == "oui" || a == "1") on = true;
                else if (a == "off" || a == "non" || a == "0") on = false;
                else return "FR : !carreauto on|off | EN: !autosquare on|off";
            }
            Choice[p.Id] = on;
            return on
                ? "FR : Carré automatique activé : votre ligne forme le carré si une charge de cavalerie arrive à moins de " + Mathf.RoundToInt(Distance) + " m. | EN: Auto square on: your line forms square when a cavalry charge comes within " + Mathf.RoundToInt(Distance) + " m."
                : "FR : Carré automatique désactivé. | EN: Auto square off.";
        }

        public static void Tick(float now)
        {
            if (now < _next) return;
            _next = now + Interval;
            SampleRiders(now);
            foreach (var line in LineManager.ActiveLines())
            {
                if (Active.TryGetValue(line, out var st))
                {
                    TickActive(line, st, now);
                    continue;
                }
                if (!Eligible(line) || !Center(line, out var center)) continue;
                if (!ChargeComing(line.Faction, center)) continue;
                Trigger(line, center, now);
            }
            // Lignes terminées : oubliées.
            if (Active.Count > 0)
            {
                List<Line> ended = null;
                foreach (var kv in Active) if (kv.Key.Ended) (ended ?? (ended = new List<Line>())).Add(kv.Key);
                if (ended != null) foreach (var l in ended) Active.Remove(l);
            }
        }

        private static void SampleRiders(float now)
        {
            var dt = _lastSample < 0f ? 0f : now - _lastSample;
            _lastSample = now;
            Riders.Clear();
            _newPos.Clear();
            foreach (var p in Players.All)
            {
                if (!p.Alive || !GameIds.IsCavalry(p.Class) || !p.TryGetPosition(out var pos)) continue;
                _newPos[p.Id] = pos;
                var vel = Vector3.zero;
                if (dt > 0.05f && dt < 2f && _lastPos.TryGetValue(p.Id, out var last)) vel = Line.Flat(pos - last) / dt;
                Riders.Add(new Rider { Faction = p.Faction, Pos = pos, Vel = vel, Fast = vel.magnitude > MinSpeed });
            }
            var swap = _lastPos;
            _lastPos = _newPos;
            _newPos = swap;
        }

        private static bool Eligible(Line line)
        {
            if (line.Ended || line.Count == 0 || line.IsCavalry || line.Mounted || line.IsCrew || line.Skirmishers) return false;
            if (line.Mode == LineMode.Square || line.Mode == LineMode.Charge || line.Mode == LineMode.Dispersing) return false;
            if (line.Morale <= 0f || !EnabledFor(line)) return false;
            foreach (var b in line.Bots) if (b.Movement is MeleeDefendTask) return false;
            return true;
        }

        private static bool Center(Line line, out Vector3 center)
        {
            center = line.Anchor;
            if (line.HasAnchor && line.Mode == LineMode.Formed) return true;
            var officer = line.Owner != null ? Players.Get(line.Owner.PlayerId) : null;
            if (officer != null && officer.TryGetPosition(out center)) return true;
            center = line.Anchor;
            return line.HasAnchor;
        }

        /// <summary>Un cavalier ennemi rapide, à moins de Distance m et tourné vers la ligne, avec au moins GroupMin cavaliers
        /// ennemis rapides à moins de GroupRadius m de lui (lui compris).</summary>
        private static bool ChargeComing(int faction, Vector3 center)
        {
            var reach = Distance * Distance;
            for (var i = 0; i < Riders.Count; i++)
            {
                var r = Riders[i];
                if (!r.Fast || r.Faction == faction) continue;
                var to = Line.Flat(center - r.Pos);
                if (to.sqrMagnitude > reach || to.sqrMagnitude < 0.01f) continue;
                if (Vector3.Dot(r.Vel.normalized, to.normalized) < HeadingCos) continue;
                var group = 0;
                for (var j = 0; j < Riders.Count && group < GroupMin; j++)
                {
                    var o = Riders[j];
                    if (!o.Fast || o.Faction != r.Faction) continue;
                    if (Line.Flat(o.Pos - r.Pos).sqrMagnitude <= GroupRadius * GroupRadius) group++;
                }
                if (group >= GroupMin) return true;
            }
            return false;
        }

        private static bool EnemyRiderNear(int faction, Vector3 center, float radius)
        {
            foreach (var r in Riders)
                if (r.Faction != faction && Line.Flat(r.Pos - center).sqrMagnitude <= radius * radius) return true;
            return false;
        }

        private static void Trigger(Line line, Vector3 center, float now)
        {
            var st = new State { Mode = line.Mode, Anchor = line.Anchor, Yaw = line.Yaw };
            if (!line.SquareOnOfficer(now)) line.FormSquare(center, line.Yaw, now);
            st.Serial = line.OrderSerial;
            Active[line] = st;
            CmdLog.Info("Carré automatique de " + line.Owner.Name + " : charge de cavalerie à moins de " + Mathf.RoundToInt(Distance) + " m.");
            if (!line.Owner.IsAi)
                ServerConsole.Private(line.Owner.PlayerId, "FR : Charge de cavalerie : votre ligne forme le carré. | EN: Cavalry charge: your line forms square.");
        }

        private static void TickActive(Line line, State st, float now)
        {
            // Autre ordre de l'officier (ou sortie du carré par un autre chemin) : son ordre l'emporte.
            if (line.Ended || line.Mode != LineMode.Square || line.OrderSerial != st.Serial)
            {
                Active.Remove(line);
                return;
            }
            if (EnemyRiderNear(line.Faction, line.Anchor, ClearDistance)) { st.ClearSince = -1f; return; }
            if (st.ClearSince < 0f) { st.ClearSince = now; return; }
            if (now - st.ClearSince < ClearSeconds) return;
            Active.Remove(line);
            switch (st.Mode)
            {
                case LineMode.Follow: line.Follow(now); break;
                case LineMode.Column: line.Column(now); break;
                default: line.FormAt(st.Anchor, st.Yaw, now); break;
            }
            CmdLog.Info("Carré automatique de " + line.Owner.Name + " : plus de cavalerie à moins de " + Mathf.RoundToInt(ClearDistance) + " m, retour à la formation précédente.");
            if (!line.Owner.IsAi)
                ServerConsole.Private(line.Owner.PlayerId, "FR : La cavalerie s'est éloignée : la ligne reprend sa formation. | EN: The cavalry has gone: the line returns to its formation.");
        }
    }
}
