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
        // Vitesses de l'ancre en marche, en m/s [TEST : à caler sur la vitesse réelle des bots].
        private const float WalkSpeed = 2.2f;
        private const float RunSpeed = 4.5f;
        private const float RetreatDistance = 20f;
        private const float ChargeDistance = 40f;
        private const float VolleySpread = 0.4f;
        private const float AimSpread = 0.4f;
        private const float FollowRadius = 6f;

        /// <summary>Officier de la ligne. Pendant une succession, l'ancien officier (pour son nom) ; changé à la reprise.</summary>
        public Commander Owner;
        public readonly int Faction;
        public readonly List<Bot> Bots = new List<Bot>();

        public LineMode Mode = LineMode.Follow;
        public bool HasAnchor;
        public Vector3 Anchor;
        public float Yaw;
        public int Ranks;
        public bool Running;
        public bool FireAtWill;
        public float DisperseEndsAt;
        /// <summary>Ligne terminée (dispersée, renvoyée ou abandonnée) : plus aucun bot ne doit la rejoindre.</summary>
        public bool Ended;

        // Marche : distance moyenne aux places au-delà de laquelle l'ancre attend les hommes.
        private const float MaxLag = 4f;
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
        private bool _drumming;

        public Line(Commander owner, int faction)
        {
            Owner = owner;
            Faction = faction;
            Ranks = CommanderConfig.DoubleRank ? 2 : 1;
        }

        public int Count => Bots.Count;

        public bool IsFormation => Mode == LineMode.Formed || Mode == LineMode.Marching || Mode == LineMode.Column || Mode == LineMode.Square;

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
            if (Mode == LineMode.Column) Reassign();
        }

        /// <summary>Un bot vient d'apparaître : il prend la place et l'état courants de la ligne.</summary>
        public void OnBotReady(Bot bot, float now)
        {
            bot.FollowOffset = RandomOffset();
            if (Mode == LineMode.Dispersing) return;
            if (IsFormation) Reassign();
            ApplyMovement(bot, now);
        }

        /// <summary>Nom du prochain bot (le tag de régiment de l'officier s'y ajoute s'il en a un).</summary>
        public string NextBotName(BotRole role)
        {
            switch (role)
            {
                case BotRole.FlagBearer: return "Porte-drapeau";
                case BotRole.Drummer: return "Tambour";
                case BotRole.Officer: return "Officier";
                default: return "Soldat " + (++_botNumber);
            }
        }

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
                }
                if (!_lagging) Anchor += Formation.Forward(Yaw) * ((Running ? RunSpeed : WalkSpeed) * dt);
            }
            else if (Mode == LineMode.Column)
            {
                // La colonne suit l'officier : l'ancre est sur lui, orientée comme lui.
                var officer = Players.Get(Owner.PlayerId);
                if (officer != null && officer.TryGetPosition(out var pos))
                {
                    Anchor = pos;
                    HasAnchor = true;
                    if (officer.TryGetYaw(out var yaw)) Yaw = yaw;
                }
            }
            if (IsFormation) UpdateSlotPositions();
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
            var column = Mode == LineMode.Column;
            var slots = column ? Formation.ColumnSlots(soldiers.Count) : Formation.LineSlots(soldiers.Count, Ranks);
            var assigned = Formation.Assign(soldiers, slots, Anchor, Yaw);
            var ranksUsed = column ? 0 : Mathf.Min(Ranks, Mathf.Max(1, soldiers.Count));
            for (var i = 0; i < soldiers.Count; i++)
            {
                var slot = assigned[i] >= 0 ? slots[assigned[i]] : Vector2.zero;
                // En colonne, la première rangée est réservée aux auxiliaires.
                soldiers[i].SlotLocal = column ? slot + Vector2.up : slot;
                soldiers[i].Rank = column ? 0 : (int)slot.y;
            }
            // Auxiliaires : derrière le dernier rang au centre (en ligne), en tête de colonne (en colonne).
            if (flag != null) flag.SlotLocal = new Vector2(drum != null ? -0.75f : 0f, ranksUsed);
            if (drum != null) drum.SlotLocal = new Vector2(flag != null ? 0.75f : 0f, ranksUsed);
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
                botAngle[i] = soldiers[i].TryGetPosition(out var p) ? Mathf.Atan2(p.x - Anchor.x, p.z - Anchor.z) : i;
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
                    bot.SetMovement(new HoldSlotTask(), now);
                    break;
                case LineMode.Square:
                    if (bot.IsLineSoldier) bot.SetMovement(new SquareHoldTask(), now);
                    else bot.SetMovement(new HoldSlotTask(), now);
                    break;
                case LineMode.Follow:
                    // L'officier bot ne se suit pas lui-même : il attend que les hommes se rassemblent autour de lui.
                    if (bot.Role == BotRole.Officer) bot.SetMovement(new StandStillTask(), now);
                    else bot.SetMovement(new FollowOfficerTask(), now);
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
            foreach (var b in Bots)
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
            LeaveSquare(now);
            Mode = LineMode.Column;
            Running = false;
            Tick(now, 0f);
            Reassign();
            ApplyMovementToAll(now);
        }

        public void Follow(float now)
        {
            LeaveSquare(now);
            Mode = LineMode.Follow;
            FireAtWill = false;
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
            if (Mode == LineMode.Marching || Mode == LineMode.Formed) Mode = LineMode.Formed;
            else Mode = LineMode.Halted;
            ApplyMovementToAll(now);
            Drum(false);
        }

        /// <summary>Retraite : la ligne se reforme RetreatDistance mètres en arrière, face à la même direction.</summary>
        public void Retreat(float now)
        {
            if (!HasAnchor && !FormOnOfficer(now)) return;
            FormAt(Anchor - Formation.Forward(Yaw) * RetreatDistance, Yaw, now);
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
            foreach (var b in Soldiers())
            {
                b.ClearActions();
                b.Enqueue(new AimTask(Random.Range(0f, AimSpread)));
            }
        }

        /// <summary>Feu de salve. rank = -1 : toute la ligne ; 0 ou 1 : un seul rang.</summary>
        public void Fire(int rank, float now)
        {
            FireAtWill = false;
            foreach (var b in Soldiers())
            {
                if (rank >= 0 && b.Rank != rank) continue;
                b.ClearActions();
                b.Enqueue(new FireTask(Random.Range(0f, VolleySpread)));
            }
        }

        public void StartFireAtWill(float now)
        {
            FireAtWill = true;
        }

        public void CeaseFire(float now)
        {
            FireAtWill = false;
            foreach (var b in Soldiers())
            {
                b.ClearActions();
                b.Enqueue(new StopAimTask());
            }
        }

        /// <summary>Charge vers target (ou droit devant si null), baïonnette au canon.</summary>
        public void Charge(Vector3? target, float now)
        {
            LeaveSquare(now);
            if (!HasAnchor) FormOnOfficer(now);
            var point = target ?? Anchor + Formation.Forward(Yaw) * ChargeDistance;
            Mode = LineMode.Charge;
            FireAtWill = false;
            Running = true;
            Drum(false);
            var right = Formation.Right(Yaw);
            var shouts = 0;
            foreach (var b in Bots)
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
            Running = false;
            FireAtWill = false;
            Drum(false);
            Reassign();
            foreach (var b in Bots)
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
            foreach (var b in Bots)
            {
                if (b.PlayerId < 0 || !b.TryGetPosition(out var pos)) continue;
                b.ClearActions();
                var dir = Random.insideUnitCircle.normalized * Random.Range(25f, 40f);
                b.SetMovement(new GoToPointTask(pos + new Vector3(dir.x, 0f, dir.y), true), now);
                if (Random.value < 0.4f) b.Shout(phrases[Random.Range(0, phrases.Length)]);
            }
        }

        // ---- Outils --------------------------------------------------------------------------

        /// <summary>Distance moyenne (au sol) des soldats vivants à leur place.</summary>
        public float AverageSlotDistance()
        {
            var sum = 0f;
            var n = 0;
            foreach (var b in Bots)
            {
                if (!b.Alive || !b.IsLineSoldier || !b.TryGetPosition(out var pos)) continue;
                sum += Flat(b.SlotPosition - pos).magnitude;
                n++;
            }
            return n > 0 ? sum / n : 0f;
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
            foreach (var b in Bots)
            {
                if (b.Role == BotRole.Drummer && b.PlayerId >= 0 && b.Alive) BotCommands.Instrument(b.PlayerId, play);
            }
        }

        private IEnumerable<Bot> Soldiers()
        {
            foreach (var b in Bots)
            {
                if (b.PlayerId >= 0 && b.Alive && b.IsLineSoldier) yield return b;
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
