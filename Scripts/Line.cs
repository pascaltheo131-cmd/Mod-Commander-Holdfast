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

        public readonly Commander Owner;
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
        private int _botNumber;
        private bool _drumming;

        public Line(Commander owner, int faction)
        {
            Owner = owner;
            Faction = faction;
            Ranks = CommanderConfig.DoubleRank ? 2 : 1;
        }

        public int Count => Bots.Count;

        public bool IsFormation => Mode == LineMode.Formed || Mode == LineMode.Marching || Mode == LineMode.Column;

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
                default: return "Soldat " + (++_botNumber);
            }
        }

        // ---- Boucle --------------------------------------------------------------------------

        public void Tick(float now, float dt)
        {
            if (Mode == LineMode.Marching)
            {
                Anchor += Formation.Forward(Yaw) * ((Running ? RunSpeed : WalkSpeed) * dt);
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
            Bot flag = null, drum = null;
            foreach (var b in Bots)
            {
                if (b.Role == BotRole.FlagBearer) flag = b;
                else if (b.Role == BotRole.Drummer) drum = b;
                else soldiers.Add(b);
            }

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

        private void UpdateSlotPositions()
        {
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
                case LineMode.Follow:
                    bot.SetMovement(new FollowOfficerTask(), now);
                    break;
                case LineMode.Halted:
                    bot.SetMovement(new StandStillTask(), now);
                    break;
                case LineMode.Charge:
                    // Arrivé pendant une charge : il suit l'officier.
                    bot.SetMovement(new FollowOfficerTask(), now);
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
            Mode = LineMode.Column;
            Running = false;
            Tick(now, 0f);
            Reassign();
            ApplyMovementToAll(now);
        }

        public void Follow(float now)
        {
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
