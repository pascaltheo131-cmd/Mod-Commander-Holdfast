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
        // Officier bot : abandon s'il n'est pas apparu OfficerTimeout s après l'envoi de son spawnSpecific, ou s'il est resté
        // en file plus de QueueTimeout s.
        private const float OfficerTimeout = 25f;
        private const float QueueTimeout = 120f;
        // Ligne bloquée : après MaxBlocks blocages de suite, elle tire de là où elle est (si assez près) ou attend BlockedPause s.
        private const int MaxBlocks = 3;
        private const float BlockedPause = 30f;
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
        private int _blocks;
        private float _pauseUntil;
        private int _volleys;
        // Feu ouvert de plus loin après des blocages : la ligne ne repart pas aussitôt en marche.
        private bool _engagedFromBlock;

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

        /// <summary>Places réservées tant que les soldats ne sont pas en file (voir AiDirector.FreeSlots).</summary>
        public int Reserved => State == AiState.Done || _soldiersQueued ? 0 : _size;

        /// <summary>
        /// L'officier bot est apparu : les soldats sont demandés et se rassemblent autour de lui. La taille est revue selon les
        /// places libres à ce moment (une autre ligne a pu les prendre) ; sous 4 soldats, la ligne est abandonnée.
        /// </summary>
        public void OnOfficerSpawned(float now)
        {
            if (_soldiersQueued || State == AiState.Done) return;
            _soldiersQueued = true;
            var size = Mathf.Min(_size, AiDirector.FreeSlotsExcept(this));
            if (size < 4)
            {
                CmdLog.Info("Ligne IA " + Name + " : plus assez de place (" + size + "), abandon.");
                Finish(now, "plus de place");
                return;
            }
            LineManager.EnqueueSoldiers(Line, size, _class, false);
            Enter(AiState.Gathering, now);
        }

        /// <summary>Fin de la ligne (retrait par kick) et du cerveau.</summary>
        public void Finish(float now, string reason)
        {
            LineManager.EndAiLine(Line, reason, now);
            Enter(AiState.Done, now);
        }

        public void Tick(float now)
        {
            if (State == AiState.Done || now < _nextThink) return;
            _nextThink = now + ThinkInterval;

            if (Line.Ended || Line.Mode == LineMode.Dispersing || Line.Count == 0)
            {
                Enter(AiState.Done, now);
                return;
            }
            // Sans officier (succession du lot A) : le sergent tient, puis la ligne se disperse. Rien à décider ici.
            if (Line.Orphaned) return;
            if (!_soldiersQueued)
            {
                // Délai compté depuis l'envoi réel du spawnSpecific de l'officier.
                var officerBot = OfficerBot();
                var late = officerBot == null
                    || (officerBot.PlayerId >= 0 && now - officerBot.RequestedAt > OfficerTimeout)
                    || (officerBot.PlayerId < 0 && now - _createdAt > QueueTimeout);
                if (late)
                {
                    CmdLog.Warn("Ligne IA " + Name + " : officier bot non apparu, abandon.");
                    Finish(now, "officier non apparu");
                }
                return;
            }
            // Officier qui n'est plus un bot du mod (id repris par le jeu) : la ligne est retirée.
            if (!LineManager.IsOurBot(Line.Owner.PlayerId))
            {
                Finish(now, "officier perdu");
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
                    // Pause après des blocages répétés.
                    if (now < _pauseUntil) break;
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
                        _engagedFromBlock = false;
                        Enter(AiState.Engaging, now);
                        break;
                    }
                    if (now >= _nextReorient && Mathf.Abs(Mathf.DeltaAngle(Line.Yaw, yawTo)) > ReorientAngle)
                    {
                        Line.FormAt(Line.Anchor, yawTo, now);
                        StartMarch(now);
                        break;
                    }
                    CheckBlocked(center, yawTo, dist, now);
                    break;

                case AiState.Engaging:
                    if (dist > range * (_engagedFromBlock ? 1.6f : 1.3f))
                    {
                        Line.CeaseFire(now);
                        StartMarch(now);
                        break;
                    }
                    var soldiers = Line.AliveSoldiers();
                    // Charge : ennemi tout près, ou net avantage numérique mais seulement à distance de charge et après une salve.
                    var advantage = enemiesNear > 0 && soldiers >= 2 * enemiesNear && soldiers >= 6
                                    && nearest < 2f * CommanderConfig.AiChargeDistance && _volleys > 0;
                    if (nearest < CommanderConfig.AiChargeDistance || advantage)
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
                _volleys++;
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

        /// <summary>
        /// Ligne bloquée (le centre n'a presque pas bougé) : elle se reforme sur place avec un cap un peu différent. Après
        /// MaxBlocks blocages de suite : feu de là où elle est si la cible est à moins de 1,5 × la portée, sinon attente de
        /// BlockedPause s. Journal au premier blocage d'une série seulement.
        /// </summary>
        private void CheckBlocked(Vector3 center, float yawTo, float dist, float now)
        {
            if (now < _nextProgressCheck) return;
            var moved = Line.Flat(center - _lastCentroid).magnitude;
            _lastCentroid = center;
            _nextProgressCheck = now + CommanderConfig.AiStuckSeconds;
            if (moved >= 2f)
            {
                _blocks = 0;
                return;
            }
            _blocks++;
            if (_blocks == 1)
                CmdLog.Info("Ligne IA " + Name + " bloquée (moins de 2 m en " + CommanderConfig.AiStuckSeconds + " s) : nouveau cap.");
            if (_blocks >= MaxBlocks)
            {
                _blocks = 0;
                Line.Halt(now);
                if (dist <= CommanderConfig.AiRange * 1.5f)
                {
                    _engagedFromBlock = true;
                    Enter(AiState.Engaging, now);
                }
                else
                {
                    _pauseUntil = now + BlockedPause;
                    Enter(AiState.Holding, now);
                }
                return;
            }
            Line.FormAt(center, yawTo + Random.Range(-35f, 35f), now);
            StartMarch(now);
        }

        private Bot OfficerBot()
        {
            foreach (var b in Line.Bots) if (b.Role == BotRole.Officer) return b;
            return null;
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
                if (p.IsBot || LineManager.IsOurBot(p.Id) || !p.Alive || p.Faction != EnemyFaction || !p.TryGetPosition(out var pos)) continue;
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
                if (p.IsBot || LineManager.IsOurBot(p.Id) || !p.Alive || p.Faction != EnemyFaction || !p.TryGetPosition(out var pos)) continue;
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
    /// que l'autre camp en a ; elles se retirent dès qu'un vrai joueur rejoint ce camp, par kick au débit limité (pas de
    /// dispersion ni de slay ; CMD:tuerdisperses ne les concerne pas). Leurs morts sont aussi retirés par kick.
    /// Rien en fin de manche (jusqu'à la manche suivante), rien si le mod est désactivé, ni hors des modes CMD:iamodes.
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
        // Camps déjà annoncés dans la manche (une seule annonce publique à la première vague).
        private static readonly HashSet<int> Announced = new HashSet<int>();
        private static int _attacker, _defender;
        private static int _mode = -1;
        private static float _roundStart;
        private static float _nextEvaluate;
        private static bool _roundOver;

        /// <summary>Lignes IA permises : mod actif, lignesia (ou !cmd ia), et mode de jeu de la manche dans iamodes (vide = tous).</summary>
        public static bool IsEnabled => CommanderConfig.IsEnabled && (CommanderConfig.AiOverride ?? CommanderConfig.AiLines) && ModeAllowed;

        private static bool ModeAllowed => CommanderConfig.AiModes.Count == 0 || CommanderConfig.AiModes.Contains(_mode);

        /// <summary>Nouvelle manche : factions en présence et mode de jeu (entiers, voir GameIds).</summary>
        public static void OnRound(int attacker, int defender, int gameplayMode, float now)
        {
            Brains.Clear();
            EmptySince.Clear();
            LastWaveEnd.Clear();
            LastReal.Clear();
            Unsupported.Clear();
            Announced.Clear();
            _attacker = attacker;
            _defender = defender;
            _mode = gameplayMode;
            _roundStart = now;
            _roundOver = false;
            // Appelé juste après Clock.Reset : l'échéance repart de zéro.
            _nextEvaluate = 0f;
            if ((CommanderConfig.AiOverride ?? CommanderConfig.AiLines) && !ModeAllowed)
                CmdLog.Info("Lignes IA : mode " + gameplayMode + " absent de iamodes, pas de ligne IA cette manche.");
        }

        /// <summary>Fin de manche : plus aucune vague jusqu'à la manche suivante.</summary>
        public static void OnRoundEnd()
        {
            _roundOver = true;
        }

        /// <summary>
        /// LineManager a tout oublié (manche, !cmd vider/off) : les cerveaux aussi. Les deux camps repartent pour un délai iadelai.
        /// </summary>
        public static void Forget(float now)
        {
            Brains.Clear();
            EmptySince.Clear();
            if (_attacker != GameIds.FactionCountry.None) EmptySince[_attacker] = now;
            if (_defender != GameIds.FactionCountry.None) EmptySince[_defender] = now;
        }

        /// <summary>Places libres pour une ligne IA : plafond moins total, moins les places réservées par les autres lignes IA.</summary>
        public static int FreeSlotsExcept(AiCommander except)
        {
            var free = LineManager.FreeSlots;
            foreach (var b in Brains) if (b != except) free -= b.Reserved;
            return free;
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
            if (_roundOver) return;
            for (var i = Brains.Count - 1; i >= 0; i--)
            {
                if (i >= Brains.Count) continue;
                var brain = Brains[i];
                try
                {
                    brain.Tick(now);
                }
                catch (System.Exception e)
                {
                    // Un cerveau en erreur est arrêté et sa ligne retirée proprement.
                    CmdLog.Warn("Ligne IA " + brain.Name + " : " + e.Message + " ; ligne retirée.");
                    try
                    {
                        brain.Finish(now, "erreur");
                    }
                    catch (System.Exception e2)
                    {
                        CmdLog.Warn("Ligne IA, retrait : " + e2.Message);
                    }
                    brain.State = AiState.Done;
                }
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
                    // Une annonce publique au retrait seulement quand un joueur rejoint le camp.
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
            var size = Mathf.Min(CommanderConfig.AiLineSize, FreeSlotsExcept(null) - 1);
            if (size < 4) return;
            var line = LineManager.CreateAiLine(side, now);
            Brains.Add(new AiCommander(line, side, other, size, CommanderConfig.AiClass, now));
            CmdLog.Info("Ligne IA pour " + GameIds.FactionName(side) + " : " + size + " soldats (" + GameIds.ClassName(CommanderConfig.AiClass)
                        + "), " + realOther + " joueurs en face, " + CountFor(side) + "/" + wanted + " ligne(s).");
            // Une seule annonce publique par camp et par manche, à la première vague.
            if (Announced.Add(side)) ServerConsole.Broadcast("Une ligne ennemie (" + GameIds.FactionName(side) + ") approche.");
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
            foreach (var p in Players.All) if (!p.IsBot && !LineManager.IsOurBot(p.Id) && p.Joined && p.Faction == faction) n++;
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
            // Copie : chaque retrait envoie des commandes qui rappellent le mod.
            foreach (var b in Brains.ToArray())
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
            foreach (var b in Brains.ToArray())
            {
                if (b.Withdrawn) continue;
                b.Withdrawn = true;
                LineManager.EndAiLine(b.Line, reason, now);
            }
            if (announce) ServerConsole.Broadcast("Les lignes ennemies se retirent.");
        }

        /// <summary>Écrit à la main : Enum.ToString passe par la réflexion.</summary>
        private static string StateName(AiState state)
        {
            switch (state)
            {
                case AiState.Gathering: return "rassemblement";
                case AiState.Forming: return "formation";
                case AiState.Advancing: return "marche";
                case AiState.Engaging: return "feu";
                case AiState.Charging: return "charge";
                case AiState.Holding: return "attente";
                default: return "terminée";
            }
        }

        public static string Status()
        {
            var text = "Lignes IA : " + (IsEnabled ? "actives" : "désactivées") + (_roundOver ? " (fin de manche)" : "")
                       + (ModeAllowed ? "" : " (mode " + _mode + " hors iamodes)") + " ; manche " + GameIds.FactionName(_attacker) + " ("
                       + RealPlayersIn(_attacker) + " joueurs) contre " + GameIds.FactionName(_defender) + " (" + RealPlayersIn(_defender) + " joueurs) ; "
                       + LineManager.AiBots() + " bots IA";
            foreach (var b in Brains) text += " ; " + b.Name + " : " + StateName(b.State) + ", " + b.Line.Count + " bots";
            return text + ".";
        }
    }
}
