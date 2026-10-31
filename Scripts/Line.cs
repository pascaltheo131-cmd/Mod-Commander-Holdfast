using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>Ce que fait la ligne dans son ensemble.</summary>
    public enum LineMode
    {
        /// <summary>En ligne, chacun à sa place autour de l'ancre.</summary>
        Formed,
        /// <summary>En ligne, l'ancre avance droit devant (en avant, pas de course).</summary>
        Marching,
        /// <summary>En colonne derrière l'officier.</summary>
        Column,
        /// <summary>Rompre et suivre : ordre lâche autour de l'officier.</summary>
        Follow,
        /// <summary>Halte hors formation : chacun reste où il est.</summary>
        Halted,
        Charge,
        /// <summary>Officier tombé ou parti : les bots s'enfuient avant d'être supprimés.</summary>
        Dispersing,
        /// <summary>Carré contre la cavalerie (lot B, n° 7) : 4 faces vers l'extérieur, officier et auxiliaires au centre.</summary>
        Square,
    }

    /// <summary>
    /// La ligne d'un officier : ses bots, sa formation et les ordres collectifs. Les ordres ne font que poser des tâches
    /// sur les bots ; le pilotage image par image est dans Bot et BotTasks.
    /// </summary>
    public class Line
    {
        // Vitesses de l'ancre en marche, en m/s : un peu sous celles du jeu (marche 1,5, course 3,36 par défaut, [CODE]
        // CommonGlobalVariables.MovementData, × characterWalkForwardSpeed / characterRunForwardSpeed) pour que les hommes suivent.
        private const float WalkSpeed = 1.4f;
        private const float RunSpeed = 3.2f;
        // Suivre en formation : la ligne se tient FollowGap m derrière l'officier, face à sa direction de marche, et pivote d'au
        // plus FollowTurnRate degrés par seconde ; au-delà de FollowRunSpeed m/s, elle court.
        private const float FollowGap = 2.5f;
        private const float FollowTurnRate = 45f;
        private const float FollowMoveSpeed = 0.6f;
        private const float FollowRunSpeed = 2.4f;
        private const float ChargeDistance = 40f;
        private const float VolleySpread = 0.4f;
        private const float AimSpread = 0.4f;
        private const float FollowRadius = 6f;

        /// <summary>Officier de la ligne. Pendant une succession, l'ancien officier (pour son nom) ; changé à la reprise.</summary>
        public Commander Owner;
        public readonly int Faction;
        public readonly List<Bot> Bots = new List<Bot>();

        public LineMode Mode = LineMode.Follow;
        /// <summary>La ligne se déplace (marche, ou suit un officier qui marche) : les hommes visent un peu devant leur place.</summary>
        public bool IsMoving;
        private bool _hasOfficerPos;
        private Vector3 _lastOfficerPos;
        private float _officerSpeed;
        private float _lastMoveAt = -1000f;
        public bool HasAnchor;
        public Vector3 Anchor;
        public float Yaw;
        public int Ranks;
        public bool Running;
        public bool FireAtWill;
        public float DisperseEndsAt;
        // Moral (corrections) : message « flanche » une seule fois jusqu'au ralliement ; début de la dernière charge.
        public bool WaverNotified;
        public float ChargeStartedAt = -1000f;
        // Retraits progressifs (plafond) : seul le premier d'une série est annoncé à l'officier.
        public float LastCapRemovalAt = -1000f;
        /// <summary>Ligne terminée (dispersée, renvoyée ou abandonnée) : plus aucun bot ne doit la rejoindre.</summary>
        public bool Ended;

        // Marche : distance moyenne aux places au-delà de laquelle l'ancre attend les hommes.
        private const float MaxLag = 4f;
        private const float StragglerDistance = 15f;
        private const float StragglerTime = 8f;
        private float _nextLagCheck;
        private bool _lagging;

        // Moral (lot A, voir Morale) : 0 à 100.
        public float Morale = 100f;
        public float LastHitAt = -1000f;
        public float LastRallyAt = -1000f;
        public float LastWaverAt = -1000f;
        public bool Wavering;

        // Succession (lot A) : la ligne n'a plus d'officier, un bot sergent la tient jusqu'à SuccessionEndsAt.
        public bool Orphaned;
        public Bot Sergeant;
        public float SuccessionEndsAt;

        // Renforts (lot A) : classe de la dernière levée (l'uniforme suit le choix de l'officier, voir Uniforms.Resolve).
        public int RaisedClass = GameIds.PlayerClass.ArmyLineInfantry;
        private int _botNumber;
        public string NamePrefix = "";
        public bool HasAimPoint;
        public Vector3 AimPoint;
        private bool CapturePlayerAim()
        {
            if (Owner.IsAi) return false;
            var officer = Players.Get(Owner.PlayerId);
            if (officer == null || !officer.HasAim || Clock.Now - officer.AimAt > 2f) return false;
            var origin = officer.CameraPosition;
            var forward = officer.CameraForward;
            var distance = 1000f;
            // Choisir le premier impact autre que le propre personnage de l'officier.
            foreach (var hit in Physics.RaycastAll(origin, forward, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (officer.Body != null && hit.collider != null && hit.collider.transform.IsChildOf(officer.Body.transform)) continue;
                if (hit.distance > 0.5f && hit.distance < distance) distance = hit.distance;
            }
            AimPoint = origin + forward * distance;
            HasAimPoint = true;
            return true;
        }
        private readonly HashSet<string> _usedNames = new HashSet<string>();
        private readonly FollowTrail _trail = new FollowTrail();
        public bool Following => Mode == LineMode.Follow || Mode == LineMode.Column;
        public float OfficerSpeed => _officerSpeed;
        private bool _drumming;

        public Line(Commander owner, int faction)
        {
            Owner = owner;
            Faction = faction;
            Ranks = CommanderConfig.DoubleRank ? 2 : 1;
        }

        public int Count => Bots.Count;

        public bool IsFormation => Mode == LineMode.Formed || Mode == LineMode.Marching || Mode == LineMode.Column || Mode == LineMode.Square
                                   || Mode == LineMode.Follow;

        // Carré : distance du centre au premier rang, calculée par Reassign.
        private float _squareHalf;
        private int _squareRanks = 1;

        /// <summary>Vecteur ramené au plan horizontal.</summary>
        public static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        // ---- Bots ----------------------------------------------------------------------------

        public void Add(Bot bot)
        {
            Bots.Add(bot);
        }

        public void Remove(Bot bot)
        {
            if (!Bots.Remove(bot)) return;
            // Les vides se referment seulement sur ordre (serrer les rangs), sauf en colonne où l'ordre suit l'officier.
            if (Following) Reassign();
        }

        /// <summary>Un bot vient d'apparaître : il prend la place et l'état courants de la ligne.</summary>
        public void OnBotReady(Bot bot, float now)
        {
            bot.FollowOffset = RandomOffset();
            if (Mode == LineMode.Dispersing) return;
            if (IsFormation) Reassign();
            ApplyMovement(bot, now);
            // Tambour arrivé pendant la marche : il joue tout de suite.
            if (_drumming && bot.Role == BotRole.Drummer) BotCommands.Instrument(bot.PlayerId, true);
        }

        /// <summary>Nom du prochain bot (le tag de régiment de l'officier s'y ajoute s'il en a un).</summary>
        public string NextBotName(BotRole role)
        {
            if (CommanderConfig.RandomNames)
                return PrefixName(Names.Random(Faction, Mathf.Max(4, 30 - NamePrefix.Length), _usedNames));
            switch (role)
            {
                case BotRole.FlagBearer: return PrefixName("Porte-drapeau");
                case BotRole.Drummer: return PrefixName("Tambour");
                case BotRole.Officer: return PrefixName("Officier");
                default: return PrefixName("Soldat " + (++_botNumber));
            }
        }

        private string PrefixName(string name) => NamePrefix.Length == 0 ? name : NamePrefix + " " + name;

        // ---- Boucle --------------------------------------------------------------------------

        public void Tick(float now, float dt)
        {
            if (Mode == LineMode.Marching)
            {
                // L'ancre n'avance que si les hommes suivent : sinon elle filerait seule devant (obstacle, pente).
                if (now >= _nextLagCheck)
                {
                    _nextLagCheck = now + 0.5f;
                    _lagging = AverageSlotDistance() > MaxLag;
                    RecoverStragglers(now);
                }
                if (!_lagging) Anchor += Formation.Forward(Yaw) * ((Running ? RunSpeed : WalkSpeed) * dt);
                IsMoving = !_lagging;
            }
            else if (Following) TickFollow(now, dt);
            if (Mode != LineMode.Marching && !Following) IsMoving = false;
            if (IsFormation) UpdateSlotPositions();
        }

        /// <summary>
        /// Suivre en formation : l'ancre (centre du premier rang) se tient derrière l'officier, la ligne face à sa direction de
        /// marche (pas à son regard, qui peut tourner sur place) ; elle pivote progressivement quand il tourne et s'arrête alignée
        /// quand il s'arrête. Les places restent les mêmes (pas de réaffectation en marche : personne ne coupe au travers).
        /// </summary>
        private void TickFollow(float now, float dt)
        {
            var officer = Players.Get(Owner.PlayerId);
            if (officer == null || !officer.TryGetPosition(out var op)) return;
            if (!_hasOfficerPos)
            {
                _hasOfficerPos = true;
                _lastOfficerPos = op;
                if (!HasAnchor && officer.TryGetYaw(out var y0)) Yaw = y0;
            }
            _trail.Record(op, Yaw, Mathf.Max(80f, Count * CommanderConfig.RankDistance * 1.5f + 10f));
            var delta = Flat(op - _lastOfficerPos);
            _lastOfficerPos = op;
            if (dt > 0f) _officerSpeed = Mathf.Lerp(_officerSpeed, delta.magnitude / dt, 1f - Mathf.Exp(-dt / 0.3f));
            if (_officerSpeed > FollowMoveSpeed && delta.sqrMagnitude > 1e-6f)
            {
                Yaw = Mathf.MoveTowardsAngle(Yaw, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, FollowTurnRate * dt);
                _lastMoveAt = now;
            }
            // Hystérésis : les paquets de déplacement ne doivent pas alterner marche et course.
            if (_officerSpeed > 2.5f) Running = true;
            else if (_officerSpeed < 1.9f) Running = false;
            Anchor = op - Formation.Forward(Yaw) * FollowGap;
            HasAnchor = true;
            IsMoving = now - _lastMoveAt < 0.5f;
            if (now >= _nextLagCheck)
            {
                _nextLagCheck = now + 0.5f;
                RecoverStragglers(now);
            }
            // Le tambour joue quand la ligne marche.
            Drum(IsMoving);
        }

        /// <summary>Appelé après chaque passage d'un bot : rechargement automatique et feu à volonté.</summary>
        public void OnBotIdle(Bot bot, float now)
        {
            if (bot.Actions.Count > 0 || !bot.IsLineSoldier) return;
            if (Mode == LineMode.Charge || Mode == LineMode.Dispersing) return;
            if (!bot.Loaded) bot.Enqueue(new ReloadTask());
            else if (FireAtWill) bot.Enqueue(new FireTask(Random.Range(0.5f, 3f)));
        }

        // ---- Formation -----------------------------------------------------------------------

        /// <summary>Recalcule les places (après un ordre de formation, un changement de rangs ou un renfort).</summary>
        public void Reassign()
        {
            var soldiers = new List<Bot>();
            Bot flag = null, drum = null, officer = null;
            foreach (var b in Bots)
            {
                if (b.Role == BotRole.FlagBearer) flag = b;
                else if (b.Role == BotRole.Drummer) drum = b;
                else if (b.Role == BotRole.Officer) officer = b;
                else soldiers.Add(b);
            }
            // Officier bot (ligne IA, lot C) : devant le centre de la ligne, au centre du carré.
            if (officer != null)
            {
                officer.SlotFace = -1;
                officer.SlotLocal = Mode == LineMode.Square ? Vector2.zero : new Vector2(0f, -1.5f);
            }

            if (Mode == LineMode.Square)
            {
                ReassignSquare(soldiers, flag, drum);
                return;
            }
            foreach (var b in Bots) b.SlotFace = -1;
            var column = Following;
            var slots = column ? Formation.ColumnSlots(soldiers.Count) : Formation.LineSlots(soldiers.Count, Ranks);
            var assigned = Formation.Assign(soldiers, slots, Anchor, Yaw);
            var ranksUsed = column ? 0 : Mathf.Min(Ranks, Mathf.Max(1, soldiers.Count));
            for (var i = 0; i < soldiers.Count; i++)
            {
                var slot = assigned[i] >= 0 ? slots[assigned[i]] : Vector2.zero;
                // En colonne, la première rangée est réservée aux auxiliaires.
                soldiers[i].SlotLocal = column ? slot + Vector2.up * ((flag != null ? 1 : 0) + (drum != null ? 1 : 0)) : slot;
                soldiers[i].Rank = column ? 0 : (int)slot.y;
            }
            // Auxiliaires : aux extrémités en ligne ; drapeau puis tambour en tête de colonne.
            var halfWidth = Mathf.CeilToInt(soldiers.Count / (float)Mathf.Max(1, Ranks)) * 0.5f + 0.5f;
            if (flag != null) flag.SlotLocal = column ? Vector2.zero : new Vector2(-halfWidth, 0f);
            if (drum != null) drum.SlotLocal = column ? new Vector2(0f, flag != null ? 1f : 0f) : new Vector2(halfWidth, 0f);
            UpdateSlotPositions();
        }

        /// <summary>
        /// Carré : chaque bot prend la place dont l'angle autour du centre est le plus proche du sien (bots et places triés par angle).
        /// 2 rangs par face à partir de CMD:carredeuxrangs soldats. Auxiliaires au centre, avec l'officier.
        /// </summary>
        private void ReassignSquare(List<Bot> soldiers, Bot flag, Bot drum)
        {
            _squareRanks = soldiers.Count >= CommanderConfig.SquareTwoRanks ? 2 : 1;
            var slots = Formation.SquareSlots(soldiers.Count, _squareRanks, out var files);
            var spacing = CommanderConfig.Spacing;
            var depth = CommanderConfig.RankDistance;
            _squareHalf = Formation.SquareHalf(files, _squareRanks, spacing, depth);

            var slotOrder = new List<int>();
            var slotAngle = new float[slots.Count];
            for (var i = 0; i < slots.Count; i++)
            {
                var pos = Formation.SquareToWorld(Vector3.zero, Yaw, slots[i], _squareHalf, spacing, depth, out _);
                slotAngle[i] = Mathf.Atan2(pos.x, pos.z);
                slotOrder.Add(i);
            }
            slotOrder.Sort((a, b) => slotAngle[a].CompareTo(slotAngle[b]));

            var botOrder = new List<int>();
            var botAngle = new float[soldiers.Count];
            for (var i = 0; i < soldiers.Count; i++)
            {
                // Bot sans position : trié après tous les autres (les angles vont de -π à π).
                botAngle[i] = soldiers[i].TryGetPosition(out var p) ? Mathf.Atan2(p.x - Anchor.x, p.z - Anchor.z) : 10f + i;
                botOrder.Add(i);
            }
            botOrder.Sort((a, b) => botAngle[a].CompareTo(botAngle[b]));

            for (var i = 0; i < botOrder.Count; i++)
            {
                var bot = soldiers[botOrder[i]];
                if (i >= slotOrder.Count)
                {
                    bot.SlotFace = -1;
                    bot.SlotLocal = Vector2.zero;
                    continue;
                }
                var slot = slots[slotOrder[i]];
                bot.SlotFace = (int)slot.z;
                bot.SlotLocal = new Vector2(slot.x, slot.y);
                bot.Rank = (int)slot.y;
            }
            if (flag != null)
            {
                flag.SlotFace = -1;
                flag.SlotLocal = new Vector2(drum != null ? -0.75f : 0f, 0f);
            }
            if (drum != null)
            {
                drum.SlotFace = -1;
                drum.SlotLocal = new Vector2(flag != null ? 0.75f : 0f, 0f);
            }
            UpdateSlotPositions();
        }

        private void UpdateSlotPositions()
        {
            if (Mode == LineMode.Square)
            {
                foreach (var b in Bots)
                {
                    if (b.SlotFace >= 0)
                    {
                        b.SlotPosition = Formation.SquareToWorld(Anchor, Yaw, new Vector3(b.SlotLocal.x, b.SlotLocal.y, b.SlotFace), _squareHalf,
                            CommanderConfig.Spacing, CommanderConfig.RankDistance, out var faceYaw);
                        b.SlotYaw = faceYaw;
                    }
                    else
                    {
                        b.SlotPosition = Formation.ToWorld(Anchor, Yaw, b.SlotLocal, CommanderConfig.Spacing, 0f, 0f);
                        b.SlotYaw = Yaw;
                    }
                }
                return;
            }
            if (Following)
            {
                foreach (var b in Bots)
                {
                    _trail.Sample(Formation.ColumnGap + b.SlotLocal.y * CommanderConfig.RankDistance * 1.5f,
                        Anchor, Yaw, out var point, out var direction);
                    b.SlotPosition = point + Formation.Right(direction) * b.SlotLocal.x * CommanderConfig.Spacing;
                    b.SlotYaw = direction;
                }
                return;
            }
            var column = Mode == LineMode.Column;
            var spacing = CommanderConfig.Spacing;
            // En colonne, les rangées sont un peu plus espacées pour la marche.
            var depth = column ? CommanderConfig.RankDistance * 1.5f : CommanderConfig.RankDistance;
            var extraBack = column ? Formation.ColumnGap : 0f;
            foreach (var b in Bots)
            {
                b.SlotPosition = Formation.ToWorld(Anchor, Yaw, b.SlotLocal, spacing, depth, extraBack);
                b.SlotYaw = Yaw;
            }
        }

        /// <summary>Pose la tâche de déplacement qui correspond au mode de la ligne.</summary>
        private void ApplyMovement(Bot bot, float now)
        {
            switch (Mode)
            {
                case LineMode.Formed:
                case LineMode.Marching:
                case LineMode.Column:
                    // L'officier bot d'une ligne IA ne suit pas sa propre colonne : il attend.
                    if (Mode == LineMode.Column && bot.Role == BotRole.Officer) bot.SetMovement(new StandStillTask(), now);
                    else bot.SetMovement(new HoldSlotTask(), now);
                    break;
                case LineMode.Square:
                    if (bot.IsLineSoldier) bot.SetMovement(new SquareHoldTask(), now);
                    else bot.SetMovement(new HoldSlotTask(), now);
                    break;
                case LineMode.Follow:
                    // Suivre en formation : chacun tient sa place derrière l'officier. L'officier bot (ligne IA) attend.
                    if (bot.Role == BotRole.Officer) bot.SetMovement(new StandStillTask(), now);
                    else bot.SetMovement(new HoldSlotTask(), now);
                    break;
                case LineMode.Halted:
                    bot.SetMovement(new StandStillTask(), now);
                    break;
                case LineMode.Charge:
                    // Arrivé pendant une charge : il suit l'officier.
                    if (bot.Role == BotRole.Officer) bot.SetMovement(new StandStillTask(), now);
                    else bot.SetMovement(new FollowOfficerTask(), now);
                    break;
            }
        }

        private void ApplyMovementToAll(float now)
        {
            foreach (var b in Bots.ToArray())
            {
                if (b.PlayerId >= 0) ApplyMovement(b, now);
            }
        }

        // ---- Ordres --------------------------------------------------------------------------

        /// <summary>Former la ligne à la position et à l'orientation données (ordre « Former la ligne » de l'officier).</summary>
        public void FormAt(Vector3 position, float yaw, float now)
        {
            LeaveSquare(now);
            Anchor = position;
            Yaw = yaw;
            HasAnchor = true;
            Mode = LineMode.Formed;
            HasAimPoint = false;
            Running = false;
            Reassign();
            ApplyMovementToAll(now);
            Drum(false);
        }

        /// <summary>Former la ligne devant l'officier, face à sa direction.</summary>
        public bool FormOnOfficer(float now)
        {
            var officer = Players.Get(Owner.PlayerId);
            if (officer == null || !officer.TryGetPosition(out var pos)) return false;
            var yaw = officer.TryGetYaw(out var y) ? y : Yaw;
            FormAt(pos + Formation.Forward(yaw) * 3f, yaw, now);
            return true;
        }

        public void Column(float now)
        {
            Follow(now);
            Mode = LineMode.Column;
        }

        /// <summary>
        /// Suivre l'officier en formation (rompre et suivre, Follow, !suivre) : la colonne suit sa trace derrière lui, arme à
        /// l'épaule (désépaulée si besoin, un rechargement en cours continue).
        /// </summary>
        public void Follow(float now)
        {
            LeaveSquare(now);
            Mode = LineMode.Follow;
            HasAimPoint = false;
            FireAtWill = false;
            _hasOfficerPos = false;
            _trail.Clear();
            var officer = Players.Get(Owner.PlayerId);
            if (officer != null && officer.TryGetPosition(out var op))
            {
                if (officer.TryGetYaw(out var y)) Yaw = y;
                Anchor = op - Formation.Forward(Yaw) * FollowGap;
                HasAnchor = true;
            }
            foreach (var b in Soldiers())
            {
                b.ClearActions(true);
                b.Enqueue(new StopAimTask());
            }
            Reassign();
            ApplyMovementToAll(now);
        }

        /// <summary>En avant (pas de course si run) : la ligne avance droit devant ; formée d'abord si besoin.</summary>
        public void March(bool run, float now)
        {
            if (Mode != LineMode.Formed && Mode != LineMode.Marching)
            {
                if (!FormOnOfficer(now)) return;
            }
            Mode = LineMode.Marching;
            Running = run;
            ApplyMovementToAll(now);
            Drum(true);
        }

        public void Halt(float now)
        {
            // En carré, les hommes ne bougent déjà pas : la halte n'en sort pas.
            if (Mode == LineMode.Square) return;
            Running = false;
            // En ligne, en marche ou en suivant : la ligne s'arrête formée, sur son ancre actuelle.
            if (Mode == LineMode.Marching || Mode == LineMode.Formed || Mode == LineMode.Follow) Mode = LineMode.Formed;
            else Mode = LineMode.Halted;
            ApplyMovementToAll(now);
            Drum(false);
        }

        /// <summary>Retraite (phrase Retreat) : la ligne se reforme CMD:retraitem mètres en arrière, face à la même direction.</summary>
        public void Retreat(float now)
        {
            // En suivre ou en colonne, l'ancre est périmée : la ligne se forme d'abord devant l'officier.
            if (!HasAnchor || !IsFormation || Mode == LineMode.Column)
            {
                if (!FormOnOfficer(now)) return;
            }
            FormAt(Anchor - Formation.Forward(Yaw) * CommanderConfig.RetreatDistance, Yaw, now);
        }

        /// <summary>Serrer les rangs : places recalculées sans les vides, sur l'ancre actuelle.</summary>
        public void CloseRanks(float now)
        {
            if (!HasAnchor || !IsFormation)
            {
                FormOnOfficer(now);
                return;
            }
            Reassign();
            ApplyMovementToAll(now);
        }

        public void SetRanks(int ranks, float now)
        {
            Ranks = Mathf.Clamp(ranks, 1, 2);
            if (IsFormation)
            {
                Reassign();
                ApplyMovementToAll(now);
            }
        }

        public void SetRunning(bool run)
        {
            Running = run;
        }

        /// <summary>Viser (apprêtez-vous / en joue).</summary>
        public void Aim(float now)
        {
            HasAimPoint = false;
            if (!Owner.IsAi && !CapturePlayerAim())
                ServerConsole.Private(Owner.PlayerId, "Visée du joueur indisponible : attendez un instant puis redonnez Viser.");
            foreach (var b in Soldiers())
            {
                b.ClearActions(true);
                b.Enqueue(new AimTask(Random.Range(0f, AimSpread)));
            }
        }

        /// <summary>Feu de salve. rank = -1 : toute la ligne ; 0 ou 1 : un seul rang.</summary>
        public void Fire(int rank, float now)
        {
            if (!HasAimPoint) CapturePlayerAim();
            FireAtWill = false;
            foreach (var b in Soldiers())
            {
                if (rank >= 0 && b.Rank != rank) continue;
                b.ClearActions(true);
                b.Enqueue(new FireTask(Random.Range(0f, VolleySpread)));
            }
        }

        public void StartFireAtWill(float now)
        {
            if (!HasAimPoint) CapturePlayerAim();
            FireAtWill = true;
        }

        public void CeaseFire(float now)
        {
            FireAtWill = false;
            foreach (var b in Soldiers())
            {
                // Un rechargement en cours n'est pas annulé.
                b.ClearActions(true);
                b.Enqueue(new StopAimTask());
            }
        }

        /// <summary>Charge vers target (ou droit devant si null), baïonnette au canon.</summary>
        public void Charge(Vector3? target, float now)
        {
            LeaveSquare(now);
            // En suivre ou en colonne, l'ancre est périmée : la ligne se forme d'abord devant l'officier.
            if (!HasAnchor || !IsFormation || Mode == LineMode.Column)
            {
                if (!FormOnOfficer(now)) return;
            }
            var point = target ?? Anchor + Formation.Forward(Yaw) * ChargeDistance;
            Mode = LineMode.Charge;
            ChargeStartedAt = now;
            FireAtWill = false;
            Running = true;
            Drum(false);
            var right = Formation.Right(Yaw);
            var shouts = 0;
            // Copie : une commande peut rappeler le mod (mort, départ) et modifier Bots.
            foreach (var b in Bots.ToArray())
            {
                if (b.PlayerId < 0) continue;
                b.ClearActions();
                if (b.Role == BotRole.Officer)
                {
                    // L'officier bot charge avec ses hommes, au centre.
                    b.SetMovement(new ChargeTask(point), now);
                    continue;
                }
                if (!b.IsLineSoldier)
                {
                    b.SetMovement(new FollowOfficerTask(), now);
                    continue;
                }
                b.Enqueue(new CrouchTask(false));
                if (!b.Bayonet) b.Enqueue(new BayonetTask(true));
                // Chacun garde à peu près sa file : la ligne ne se resserre pas en un point.
                b.SetMovement(new ChargeTask(point + right * (b.SlotLocal.x * CommanderConfig.Spacing)), now);
                if (shouts < 3 && Random.value < 0.3f)
                {
                    shouts++;
                    b.Shout(GameIds.CharacterVoicePhrase.Charge);
                }
            }
        }

        public void SetCrouch(bool down, float now)
        {
            foreach (var b in Soldiers()) b.Enqueue(new CrouchTask(down));
        }

        /// <summary>Accroupi si la majorité est debout, debout sinon.</summary>
        public void ToggleCrouch(float now)
        {
            var crouched = 0;
            var total = 0;
            foreach (var b in Soldiers())
            {
                total++;
                if (b.Crouched) crouched++;
            }
            SetCrouch(crouched * 2 < total, now);
        }

        public void SetBayonet(bool fix, float now)
        {
            foreach (var b in Soldiers()) b.Enqueue(new BayonetTask(fix));
        }

        public void ToggleBayonet(float now)
        {
            var fixedCount = 0;
            var total = 0;
            foreach (var b in Soldiers())
            {
                total++;
                if (b.Bayonet) fixedCount++;
            }
            SetBayonet(fixedCount * 2 < total, now);
        }

        /// <summary>Dispersion : chacun s'enfuit dans une direction au hasard, quelques-uns crient.</summary>
        /// <summary>
        /// Former le carré autour de position (l'officier au centre), une face vers yaw. Baïonnette au canon ; avec 2 rangs,
        /// le rang extérieur s'accroupit. Les soldats tirent seuls sur l'ennemi qui approche de leur face (SquareHoldTask).
        /// </summary>
        public void FormSquare(Vector3 position, float yaw, float now)
        {
            Anchor = position;
            Yaw = yaw;
            HasAnchor = true;
            Mode = LineMode.Square;
            HasAimPoint = false;
            Running = false;
            FireAtWill = false;
            Drum(false);
            Reassign();
            foreach (var b in Bots.ToArray())
            {
                if (b.PlayerId < 0) continue;
                ApplyMovement(b, now);
                if (!b.Alive || !b.IsLineSoldier) continue;
                b.ClearActions();
                if (!b.Bayonet) b.Enqueue(new BayonetTask(true));
                b.Enqueue(new CrouchTask(_squareRanks > 1 && b.Rank == 0));
            }
        }

        /// <summary>Carré centré sur l'officier.</summary>
        public bool SquareOnOfficer(float now)
        {
            var officer = Players.Get(Owner.PlayerId);
            if (officer == null || !officer.TryGetPosition(out var pos)) return false;
            FormSquare(pos, officer.TryGetYaw(out var y) ? y : Yaw, now);
            return true;
        }

        /// <summary>Sortie du carré : les hommes se relèvent (la baïonnette reste).</summary>
        private void LeaveSquare(float now)
        {
            if (Mode != LineMode.Square) return;
            Mode = LineMode.Formed;
            foreach (var b in Bots) b.SlotFace = -1;
            SetCrouch(false, now);
        }

        public void Disperse(float now)
        {
            Drum(false);
            Mode = LineMode.Dispersing;
            FireAtWill = false;
            DisperseEndsAt = now + CommanderConfig.DisperseSeconds;
            int[] phrases = { GameIds.CharacterVoicePhrase.Retreat, GameIds.CharacterVoicePhrase.Help,
                              GameIds.CharacterVoicePhrase.Surrender, GameIds.CharacterVoicePhrase.Scared };
            foreach (var b in Bots.ToArray())
            {
                if (b.PlayerId < 0) continue;
                b.ClearActions();
                if (!b.TryGetPosition(out var pos))
                {
                    // Position inconnue : il reste sur place.
                    b.SetMovement(new StandStillTask(), now);
                    continue;
                }
                var dir = Random.insideUnitCircle.normalized * Random.Range(25f, 40f);
                b.SetMovement(new GoToPointTask(pos + new Vector3(dir.x, 0f, dir.y), true), now);
                if (Random.value < 0.4f) b.Shout(phrases[Random.Range(0, phrases.Length)]);
            }
        }

        // ---- Outils --------------------------------------------------------------------------

        /// <summary>Distance moyenne (au sol) des soldats vivants à leur place.</summary>
        /// <summary>
        /// Distance moyenne (au sol) des soldats vivants à leur place, sans les hommes à plus de StragglerDistance (coincés :
        /// ils ne doivent pas arrêter toute la ligne, voir RecoverStragglers).
        /// </summary>
        public float AverageSlotDistance()
        {
            var sum = 0f;
            var n = 0;
            foreach (var b in Bots)
            {
                if (!b.Alive || !b.IsLineSoldier || !b.TryGetPosition(out var pos)) continue;
                var d = Flat(b.SlotPosition - pos).magnitude;
                if (d > StragglerDistance) continue;
                sum += d;
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>En marche : un homme à plus de StragglerDistance de sa place depuis StragglerTime s est téléporté sur sa place.</summary>
        private void RecoverStragglers(float now)
        {
            // La récupération dépend maintenant du progrès réel de chaque bot (Bot.Steer).
        }

        /// <summary>Centre des bots vivants de la ligne.</summary>
        public bool TryGetCentroid(out Vector3 center)
        {
            var sum = Vector3.zero;
            var n = 0;
            foreach (var b in Bots)
            {
                if (!b.Alive || !b.TryGetPosition(out var pos)) continue;
                sum += pos;
                n++;
            }
            center = n > 0 ? sum / n : Vector3.zero;
            return n > 0;
        }

        /// <summary>Soldats vivants.</summary>
        public int AliveSoldiers()
        {
            var n = 0;
            foreach (var b in Bots) if (b.Alive && b.IsLineSoldier) n++;
            return n;
        }

        /// <summary>Le tambour joue pendant la marche et se tait à l'arrêt.</summary>
        private void Drum(bool play)
        {
            if (_drumming == play) return;
            _drumming = play;
            foreach (var b in Bots.ToArray())
            {
                if (b.Role == BotRole.Drummer && b.PlayerId >= 0 && b.Alive) BotCommands.Instrument(b.PlayerId, play);
            }
        }

        /// <summary>Soldats vivants, copiés : les ordres envoient des commandes qui peuvent rappeler le mod et modifier Bots.</summary>
        private List<Bot> Soldiers()
        {
            var list = new List<Bot>();
            foreach (var b in Bots)
            {
                if (b.PlayerId >= 0 && b.Alive && b.IsLineSoldier) list.Add(b);
            }
            return list;
        }

        public static string ModeName(LineMode mode)
        {
            // Écrit à la main : Enum.ToString passe par la réflexion.
            switch (mode)
            {
                case LineMode.Formed: return "en ligne";
                case LineMode.Marching: return "en marche";
                case LineMode.Column: return "en colonne";
                case LineMode.Follow: return "suit";
                case LineMode.Halted: return "halte";
                case LineMode.Charge: return "charge";
                case LineMode.Dispersing: return "dispersion";
                case LineMode.Square: return "carré";
                default: return "?";
            }
        }

        private static Vector3 RandomOffset()
        {
            var c = Random.insideUnitCircle * FollowRadius;
            // Pas collé à l'officier : au moins 2 m.
            if (c.sqrMagnitude < 4f) c = c.sqrMagnitude < 0.01f ? new Vector2(0f, -2f) : c.normalized * 2f;
            return new Vector3(c.x, 0f, c.y);
        }
    }
}
