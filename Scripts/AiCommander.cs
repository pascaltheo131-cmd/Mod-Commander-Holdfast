using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>États du cerveau d'une ligne IA.</summary>
    public enum AiState { Gathering, Forming, Advancing, Engaging, Charging, Holding, Done }

    /// <summary>
    /// Cerveau d'une ligne ennemie menée par l'IA (lot C, n° 12). La ligne est une Line ordinaire dont l'officier est un bot :
    /// le cerveau ne fait que donner les mêmes ordres qu'un officier joueur (FormAt, March, Halt, Aim, Fire, Charge).
    /// Rassemblement autour de l'officier → formation face à l'ennemi → marche vers le groupe de vrais joueurs ennemis le plus
    /// proche → halte à portée → salves → charge si l'ennemi est tout près ou si la ligne a nettement l'avantage.
    /// Recul sous moral bas et succession à la mort de l'officier : lot A (LineManager), sans rien de propre à l'IA.
    /// Pas de NavMesh : marche en ligne droite ; une ligne bloquée repart avec une orientation un peu différente.
    /// </summary>
    public class AiCommander
    {
        private const float ThinkInterval = 0.5f;
        private const float GatherTimeout = 25f;
        private const float OfficerTimeout = 25f;
        private const float FormTime = 8f;
        private const float ReorientAngle = 25f;
        private const float ReorientInterval = 6f;
        private const float VolleyInterval = 15f; // [TEST] environ le temps d'un rechargement et d'une remise en joue
        private const float AimToFire = 2.5f;
        private const float ChargeMaxTime = 25f;
        private const float GroupRadius = 40f;

        public readonly Line Line;
        public readonly int Faction;
        public readonly int EnemyFaction;
        public AiState State = AiState.Gathering;
        /// <summary>Retirée parce qu'un vrai joueur a rejoint son camp (pas de délai de vague après).</summary>
        public bool Withdrawn;

        private readonly int _size;
        private readonly int _class;
        private readonly float _createdAt;
        private float _stateSince;
        private bool _soldiersQueued;
        private float _nextThink;
        private float _nextVolley;
        private float _fireAt = float.NaN;
        private float _nextReorient;
        private float _nextProgressCheck;
        private Vector3 _lastCentroid;

        public AiCommander(Line line, int faction, int enemyFaction, int size, int playerClass, float now)
        {
            Line = line;
            Faction = faction;
            EnemyFaction = enemyFaction;
            _size = size;
            _class = playerClass;
            _createdAt = now;
            _stateSince = now;
        }

        public string Name => GameIds.FactionName(Faction) + " (" + Line.Owner.Name + ")";

        /// <summary>L'officier bot est apparu : les soldats sont demandés et se rassemblent autour de lui.</summary>
        public void OnOfficerSpawned(float now)
        {
            if (_soldiersQueued) return;
            _soldiersQueued = true;
            LineManager.EnqueueSoldiers(Line, _size, _class);
            Enter(AiState.Gathering, now);
        }

        public void Tick(float now)
        {
            if (State == AiState.Done || now < _nextThink) return;
            _nextThink = now + ThinkInterval;

            if (Line.Ended || Line.Mode == LineMode.Dispersing)
            {
                Enter(AiState.Done, now);
                return;
            }
            // Sans officier (succession du lot A) : le sergent tient, puis la ligne se disperse. Rien à décider ici.
            if (Line.Orphaned) return;
            if (!_soldiersQueued)
            {
                if (now - _createdAt > OfficerTimeout)
                {
                    CmdLog.Warn("Ligne IA " + Name + " : officier bot non apparu, abandon.");
                    LineManager.EndAiLine(Line, "officier non apparu", now);
                    Enter(AiState.Done, now);
                }
                return;
            }

            var officer = Players.Get(Line.Owner.PlayerId);
            if (officer == null || !officer.Alive || !officer.TryGetPosition(out var officerPos)) return;
            var center = Line.TryGetCentroid(out var c) ? c : officerPos;

            if (!TryFindTarget(center, out var target, out var enemiesNear, out var nearest))
            {
                // Plus d'ennemi vivant : la ligne s'arrête et attend.
                if (State == AiState.Advancing || State == AiState.Charging) Line.Halt(now);
                if (State != AiState.Gathering) Enter(AiState.Holding, now);
                return;
            }
            var toTarget = Line.Flat(target - center);
            var dist = toTarget.magnitude;
            var yawTo = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            var range = CommanderConfig.AiRange;

            // Moral bas (lot A) : LineManager fait reculer la ligne ; elle attend de se reprendre.
            if (Line.Wavering)
            {
                if (State != AiState.Holding) Enter(AiState.Holding, now);
                return;
            }

            switch (State)
            {
                case AiState.Gathering:
                    if (Line.AliveSoldiers() >= Mathf.CeilToInt(_size * 0.8f) || now - _stateSince > GatherTimeout)
                    {
                        Line.FormAt(officerPos + Formation.Forward(yawTo) * 3f, yawTo, now);
                        Enter(AiState.Forming, now);
                    }
                    break;

                case AiState.Holding:
                    Line.FormAt(Line.HasAnchor ? Line.Anchor : center, yawTo, now);
                    Enter(AiState.Forming, now);
                    break;

                case AiState.Forming:
                    if (now - _stateSince < FormTime && Line.AverageSlotDistance() > 2f) break;
                    if (dist > range) StartMarch(now);
                    else Enter(AiState.Engaging, now);
                    break;

                case AiState.Advancing:
                    if (dist <= range)
                    {
                        Line.Halt(now);
                        Enter(AiState.Engaging, now);
                        break;
                    }
                    if (now >= _nextReorient && Mathf.Abs(Mathf.DeltaAngle(Line.Yaw, yawTo)) > ReorientAngle)
                    {
                        Line.FormAt(Line.Anchor, yawTo, now);
                        StartMarch(now);
                        break;
                    }
                    CheckBlocked(center, yawTo, now);
                    break;

                case AiState.Engaging:
                    if (dist > range * 1.3f)
                    {
                        Line.CeaseFire(now);
                        StartMarch(now);
                        break;
                    }
                    var soldiers = Line.AliveSoldiers();
                    if (nearest < CommanderConfig.AiChargeDistance || (enemiesNear > 0 && soldiers >= 2 * enemiesNear && soldiers >= 6))
                    {
                        Line.Charge(target, now);
                        Enter(AiState.Charging, now);
                        CmdLog.Info("Ligne IA " + Name + " charge (" + soldiers + " contre " + enemiesNear + ", ennemi à " + Mathf.RoundToInt(nearest) + " m).");
                        break;
                    }
                    if (Mathf.Abs(Mathf.DeltaAngle(Line.Yaw, yawTo)) > 30f)
                    {
                        Line.FormAt(Line.Anchor, yawTo, now);
                        _nextVolley = Mathf.Max(_nextVolley, now + 3f);
                        break;
                    }
                    Volley(now);
                    break;

                case AiState.Charging:
                    if (now - _stateSince > ChargeMaxTime || nearest > 30f)
                    {
                        Line.FormAt(center, yawTo, now);
                        Enter(AiState.Forming, now);
                    }
                    break;
            }
        }

        /// <summary>Salve : en joue, puis feu AimToFire secondes plus tard, toutes les VolleyInterval secondes.</summary>
        private void Volley(float now)
        {
            if (!float.IsNaN(_fireAt))
            {
                if (now < _fireAt) return;
                Line.Fire(-1, now);
                _fireAt = float.NaN;
                return;
            }
            if (now < _nextVolley) return;
            Line.Aim(now);
            _fireAt = now + AimToFire;
            _nextVolley = now + VolleyInterval;
        }

        private void StartMarch(float now)
        {
            Line.March(false, now);
            _nextReorient = now + ReorientInterval;
            _nextProgressCheck = now + CommanderConfig.AiStuckSeconds;
            Line.TryGetCentroid(out _lastCentroid);
            Enter(AiState.Advancing, now);
        }

        /// <summary>Ligne bloquée (le centre n'a presque pas bougé) : elle se reforme sur place avec un cap un peu différent.</summary>
        private void CheckBlocked(Vector3 center, float yawTo, float now)
        {
            if (now < _nextProgressCheck) return;
            var moved = Line.Flat(center - _lastCentroid).magnitude;
            _lastCentroid = center;
            _nextProgressCheck = now + CommanderConfig.AiStuckSeconds;
            if (moved >= 2f) return;
            var yaw = yawTo + Random.Range(-35f, 35f);
            CmdLog.Info("Ligne IA " + Name + " bloquée (" + moved.ToString("0.0") + " m en " + CommanderConfig.AiStuckSeconds + " s) : nouveau cap.");
            Line.FormAt(center, yaw, now);
            StartMarch(now);
        }

        /// <summary>
        /// Groupe de vrais joueurs ennemis le plus proche : le joueur ennemi vivant le plus proche, puis le centre des ennemis
        /// à moins de GroupRadius de lui. enemiesNear : vrais ennemis à portée du centre de la ligne.
        /// </summary>
        private bool TryFindTarget(Vector3 from, out Vector3 target, out int enemiesNear, out float nearestDistance)
        {
            target = Vector3.zero;
            enemiesNear = 0;
            nearestDistance = float.MaxValue;
            PlayerInfo nearest = null;
            var nearestPos = Vector3.zero;
            var rangeSq = (CommanderConfig.AiRange + 20f) * (CommanderConfig.AiRange + 20f);
            foreach (var p in Players.All)
            {
                if (p.IsBot || !p.Alive || p.Faction != EnemyFaction || !p.TryGetPosition(out var pos)) continue;
                var d = Line.Flat(pos - from).sqrMagnitude;
                if (d <= rangeSq) enemiesNear++;
                if (d >= nearestDistance) continue;
                nearestDistance = d;
                nearest = p;
                nearestPos = pos;
            }
            if (nearest == null) return false;
            nearestDistance = Mathf.Sqrt(nearestDistance);
            var sum = Vector3.zero;
            var n = 0;
            foreach (var p in Players.All)
            {
                if (p.IsBot || !p.Alive || p.Faction != EnemyFaction || !p.TryGetPosition(out var pos)) continue;
                if (Line.Flat(pos - nearestPos).sqrMagnitude > GroupRadius * GroupRadius) continue;
                sum += pos;
                n++;
            }
            target = n > 0 ? sum / n : nearestPos;
            return true;
        }

        private void Enter(AiState state, float now)
        {
            if (state == State && state != AiState.Gathering) return;
            State = state;
            _stateSince = now;
        }
    }

    /// <summary>
    /// Lignes IA (lot C, n° 12). Règle : un camp de la manche reçoit des lignes IA seulement s'il n'a aucun vrai joueur alors
    /// que l'autre camp en a ; elles se retirent (dispersion, sans kick) dès qu'un vrai joueur rejoint ce camp.
    /// Le camp d'un vrai joueur est la faction de sa dernière apparition dans la manche (remise à zéro à chaque manche).
    /// Les lignes IA restent sous le plafond effectif et passent après les lignes des joueurs (LineManager).
    /// </summary>
    public static class AiDirector
    {
        private const float EvaluateInterval = 1f;

        private static readonly List<AiCommander> Brains = new List<AiCommander>();
        private static readonly Dictionary<int, float> EmptySince = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> LastWaveEnd = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> LastReal = new Dictionary<int, int>();
        private static readonly HashSet<int> Unsupported = new HashSet<int>();
        private static int _attacker, _defender;
        private static float _roundStart;
        private static float _nextEvaluate;

        public static bool IsEnabled => CommanderConfig.AiOverride ?? CommanderConfig.AiLines;

        /// <summary>Nouvelle manche : factions en présence (entiers, voir GameIds.FactionCountry).</summary>
        public static void OnRound(int attacker, int defender, float now)
        {
            Brains.Clear();
            EmptySince.Clear();
            LastWaveEnd.Clear();
            LastReal.Clear();
            Unsupported.Clear();
            _attacker = attacker;
            _defender = defender;
            _roundStart = now;
        }

        /// <summary>LineManager a tout oublié (fin de manche, commande admin) : les cerveaux aussi.</summary>
        public static void Forget()
        {
            Brains.Clear();
        }

        public static void OnBotSpawned(Bot bot, float now)
        {
            if (bot.Role != BotRole.Officer) return;
            foreach (var b in Brains)
            {
                if (b.Line != bot.Line) continue;
                b.OnOfficerSpawned(now);
                return;
            }
        }

        public static void Tick(float now)
        {
            for (var i = Brains.Count - 1; i >= 0; i--)
            {
                var brain = Brains[i];
                brain.Tick(now);
                if (brain.State != AiState.Done) continue;
                Brains.RemoveAt(i);
                if (!brain.Withdrawn) LastWaveEnd[brain.Faction] = now;
                CmdLog.Info("Ligne IA " + brain.Name + " terminée" + (brain.Withdrawn ? " (retrait)." : "."));
            }

            if (now < _nextEvaluate) return;
            _nextEvaluate = now + EvaluateInterval;
            if (!IsEnabled)
            {
                if (Brains.Count > 0) WithdrawAll("lignes IA désactivées", now, false);
                return;
            }
            Evaluate(_attacker, _defender, now);
            Evaluate(_defender, _attacker, now);
        }

        private static void Evaluate(int side, int other, float now)
        {
            if (side == GameIds.FactionCountry.None || other == GameIds.FactionCountry.None || side == other) return;
            var real = RealPlayersIn(side);
            var realOther = RealPlayersIn(other);
            var before = LastReal.TryGetValue(side, out var r) ? r : -1;
            LastReal[side] = real;

            if (real > 0 || realOther == 0)
            {
                EmptySince.Remove(side);
                if (CountFor(side) > 0)
                {
                    var why = real > 0 ? "un joueur rejoint le camp" : "plus aucun joueur en face";
                    Withdraw(side, why, now);
                    if (real > 0) ServerConsole.Broadcast("Des joueurs rejoignent " + GameIds.FactionName(side) + " : les lignes ennemies se retirent.");
                }
                return;
            }
            // Camp vide alors que l'autre camp joue : délai depuis le début de manche ou le départ du dernier joueur du camp.
            if (!EmptySince.ContainsKey(side)) EmptySince[side] = before > 0 ? now : _roundStart;
            if (now < EmptySince[side] + CommanderConfig.AiDelay) return;
            if (LastWaveEnd.TryGetValue(side, out var end) && now < end + CommanderConfig.AiWaveDelay) return;

            var wanted = CommanderConfig.AiPerPlayers > 0
                ? Mathf.CeilToInt(realOther / (float)CommanderConfig.AiPerPlayers)
                : CommanderConfig.AiLineCount;
            if (CountFor(side) >= wanted) return;
            if (!Supported(side)) return;

            // Sous le plafond effectif, après les lignes des joueurs : seulement avec des places libres.
            var size = Mathf.Min(CommanderConfig.AiLineSize, LineManager.FreeSlots - 1);
            if (size < 4) return;
            var line = LineManager.CreateAiLine(side, now);
            Brains.Add(new AiCommander(line, side, other, size, CommanderConfig.AiClass, now));
            CmdLog.Info("Ligne IA pour " + GameIds.FactionName(side) + " : " + size + " soldats (" + GameIds.ClassName(CommanderConfig.AiClass)
                        + "), " + realOther + " joueurs en face, " + CountFor(side) + "/" + wanted + " ligne(s).");
            ServerConsole.Broadcast("Une ligne ennemie (" + GameIds.FactionName(side) + ") approche.");
        }

        /// <summary>Faction sans uniforme d'officier ou de la classe IA (Privateer, Spanish…) : pas de ligne IA.</summary>
        private static bool Supported(int side)
        {
            if (Uniforms.HasAny(side, CommanderConfig.AiClass) && Uniforms.HasAny(side, GameIds.PlayerClass.ArmyInfantryOfficer)) return true;
            if (Unsupported.Add(side))
                CmdLog.Info("Pas de ligne IA pour " + GameIds.FactionName(side) + " : aucun uniforme d'officier ou de " + GameIds.ClassName(CommanderConfig.AiClass) + ".");
            return false;
        }

        private static int RealPlayersIn(int faction)
        {
            var n = 0;
            foreach (var p in Players.All) if (!p.IsBot && p.Joined && p.Faction == faction) n++;
            return n;
        }

        private static int CountFor(int side)
        {
            var n = 0;
            foreach (var b in Brains) if (b.Faction == side && !b.Withdrawn) n++;
            return n;
        }

        private static void Withdraw(int side, string reason, float now)
        {
            foreach (var b in Brains)
            {
                if (b.Faction != side || b.Withdrawn) continue;
                b.Withdrawn = true;
                LineManager.EndAiLine(b.Line, reason, now);
            }
        }

        /// <summary>Retire toutes les lignes IA (commande admin, désactivation).</summary>
        public static void WithdrawAll(string reason, float now, bool announce)
        {
            if (Brains.Count == 0) return;
            foreach (var b in Brains)
            {
                if (b.Withdrawn) continue;
                b.Withdrawn = true;
                LineManager.EndAiLine(b.Line, reason, now);
            }
            if (announce) ServerConsole.Broadcast("Les lignes ennemies se retirent.");
        }

        public static string Status()
        {
            var text = "Lignes IA : " + (IsEnabled ? "actives" : "désactivées") + " ; manche " + GameIds.FactionName(_attacker) + " ("
                       + RealPlayersIn(_attacker) + " joueurs) contre " + GameIds.FactionName(_defender) + " (" + RealPlayersIn(_defender) + " joueurs) ; "
                       + LineManager.AiBots() + " bots IA";
            foreach (var b in Brains) text += " ; " + b.Name + " : " + b.State + ", " + b.Line.Count + " bots";
            return text + ".";
        }
    }
}
