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
        /// <summary>Déplacement latéral collectif d'une ligne déjà formée : pas de côté synchronisé, sans contourner les camarades.</summary>
        public bool SideShifting { get; private set; }
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
        /// <summary>Bilan de la levée en cours : hommes apparus, places abandonnées ; vrai tant qu'elle n'est pas annoncée.</summary>
        public bool RaiseActive;
        /// <summary>Officier déconnecté : après la dispersion, tous les bots sont retirés (kick) au lieu d'être laissés sur place.</summary>
        public bool KickAfterDisperse;
        private int _reassignFrame = -1;
        private readonly List<Bot> _musicScratch = new List<Bot>();
        private readonly List<Bot> _medics = new List<Bot>();
        private bool _reassignPending;
        public int RaiseOk;
        public int RaiseFailed;
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

        /// <summary>Colonne : point de la trace de l'officier le plus proche de pos, avancé de ahead m.</summary>
        public bool TrailLead(Vector3 pos, float ahead, Bot bot, out Vector3 point) => _trail.LeadFrom(pos, ahead,
            Formation.ColumnGap + bot.SlotLocal.y * Formation.FollowRowGap,
            bot.SlotLocal.x * Formation.FollowSpacing, out point);
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
            // Les vides se referment : la ligne se resserre vers le centre (l'ordre des autres est gardé), mais pas pendant un tir
            // (en joue ou salve en cours) : le resserrement attend la fin du tir (TickCompaction).
            if (IsFormation)
            {
                if (Firing()) _compactPending = true;
                else Reassign(true);
            }
        }

        /// <summary>Un bot vient d'apparaître : il prend la place et l'état courants de la ligne.</summary>
        public void OnBotReady(Bot bot, float now)
        {
            bot.FollowOffset = RandomOffset();
            // Numéro fixe dans la ligne, dans l'ordre d'apparition (renforts : numéros suivants).
            if (bot.Number == 0) bot.Number = ++_lastNumber;
            if (Mode == LineMode.Dispersing) return;
            if (IsFormation) Reassign(true);
            ApplyMovement(bot, now);
            // Tambour arrivé pendant la marche : il joue tout de suite.
            // Musicien : il sort d'abord son instrument (LineManager), la musique part quand c'est confirmé (UpdateMusic).
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
                case BotRole.Surgeon: return PrefixName("Médecin");
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
            if (_reassignPending && Time.frameCount != _reassignFrame) Reassign(true);
            if (IsFormation) { UpdateSlotPositions(); foreach (var b in Bots) b.ProjectSlot(now); }
            TryExchangeSlots(now);
            UpdateMusic(now);
            CheckFormed(now);
            if (_compactPending && !Firing())
            {
                _compactPending = false;
                if (IsFormation) Reassign(true);
            }
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
            _trail.Record(op, Yaw, Mathf.Max(80f, Count * Formation.FollowRowGap + 10f));
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
            var wasMoving = IsMoving;
            IsMoving = now - _lastMoveAt < 0.5f;
            if(IsMoving && !wasMoving)PrepareToMove();
            if (IsMoving != wasMoving) foreach(var b in Bots) { b.Settled=false; b.NextTick=0f; }
            if (now >= _nextLagCheck)
            {
                _nextLagCheck = now + 0.5f;
                RecoverStragglers(now);
            }
            // Le tambour joue quand la ligne marche.
        }

        /// <summary>Appelé après chaque passage d'un bot : rechargement automatique et feu à volonté.</summary>
        public void OnBotIdle(Bot bot, float now)
        {
            if (bot.Actions.Count > 0 || !bot.IsLineSoldier) return;
            if (Mode == LineMode.Charge || Mode == LineMode.Dispersing) return;
            if (IsFormation && (IsMoving || !bot.Settled)) return;
            if (!bot.Loaded) bot.Enqueue(new ReloadTask());
            else if (FireAtWill) bot.Enqueue(new FireTask(Random.Range(0.5f, 3f)));
        }

        // ---- Formation -----------------------------------------------------------------------

        /// <summary>Recalcule les places (après un ordre de formation, un changement de rangs ou un renfort).</summary>
        /// <summary>
        /// Recalcule les places. Seuls les soldats présents (apparus, vivants, qui n'ont pas renoncé) en reçoivent une : les
        /// absents ne laissent pas de trou, la ligne se resserre vers le centre. keepGaveUp faux (nouvel ordre) : les bots qui
        /// avaient renoncé à leur place réessaient. Places fixes par numéro (voir plus bas).
        /// </summary>
        public void Reassign(bool keepGaveUp = false)
        {
            // Au plus un recalcul par image et par ligne (affectation en O(n³)) : une deuxième demande dans la même image est
            // reportée à la suivante (Tick), jamais rejouée en boucle.
            if (!keepGaveUp) foreach (var b in Bots) { b.GaveUp = false; b.GiveUps = 0; }
            var frame = Time.frameCount;
            if (frame == _reassignFrame)
            {
                _reassignPending = true;
                // Bot ajouté dans cette image (jamais placé) : place provisoire en queue de ligne jusqu'au recalcul suivant.
                var provisional = false;
                foreach (var b in Bots)
                {
                    if (b.HasSlot || b.PlayerId < 0) continue;
                    b.SlotFace = -1;
                    b.SlotLocal = new Vector2(0f, Ranks + 2f);
                    provisional = true;
                }
                if (provisional) UpdateSlotPositions();
                return;
            }
            _reassignFrame = frame;
            _reassignPending = false;
            foreach (var b in Bots) if (b.PlayerId >= 0) b.HasSlot = true;
            FrameStats.Reassigns++;
            var soldiers = new List<Bot>();
            _medics.Clear();
            Bot flag = null, drum = null, officer = null;
            foreach (var b in Bots)
            {
                if (b.Role == BotRole.FlagBearer) flag = b;
                else if (b.Role == BotRole.Drummer) drum = b;
                else if (b.Role == BotRole.Officer) officer = b;
                else if (b.Role == BotRole.Surgeon) _medics.Add(b);
                else if (b.PlayerId >= 0 && b.Alive && !b.GaveUp) soldiers.Add(b);
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
            var oldSlots = new Dictionary<Bot, Vector3>();
            foreach (var b in Bots) oldSlots[b] = b.SlotPosition;
            var order = new List<Bot>(soldiers);
            order.Sort((a, c) => a.Number.CompareTo(c.Number));

            var positions = new List<Vector3>();
            var destinations = new List<Vector3>();
            for (var i = 0; i < slots.Count; i++)
            {

                Vector3 point;
                if (column)
                {
                    _trail.Sample(Formation.ColumnGap + slots[i].y * Formation.FollowRowGap,
                        Anchor, Yaw, out point, out var direction);
                    point += Formation.Right(direction) * slots[i].x * Formation.FollowSpacing;
                }
                else point = Formation.ToWorld(Anchor, Yaw, slots[i], Formation.Spacing, Formation.RankGap, 0f);
                destinations.Add(point);
                positions.Add(order[i].TryGetPosition(out var current) ? current : point);
            }
            // En ligne, conserver l'ordre gauche-droite des soldats évite qu'un
            // déplacement du marqueur ne les fasse échanger leurs places en courant
            // dans des directions opposées. En colonne, garder l'appariement par
            // distance, car les places suivent une trace qui peut tourner.
            // En ligne : hongrois sur la distance au carré (une affectation optimale au carré ne croise pas deux trajets :
            // échanger deux cibles croisées réduit toujours la somme des carrés), au lieu du simple tri gauche-droite.
            var assignment = SlotAssignment.Solve(positions, destinations, !column);
            for (var i = 0; i < order.Count; i++)
            {
                var slot = slots[assignment[i]];
                order[i].SlotLocal = slot;
                order[i].Rank = column ? 0 : (int)slot.y;
                order[i].FormPriority = order[i].Number;
            }
            _halfWidthFiles = 0f;
            foreach (var b in order) _halfWidthFiles = Mathf.Max(_halfWidthFiles, Mathf.Abs(b.SlotLocal.x));
            _halfWidthFiles += 1f; // marge extérieure de contournement
            var bothAux = flag != null && drum != null;
            if (flag != null) {
                flag.SlotLocal=AuxiliaryPlacement.Slot(soldiers.Count,Ranks,column,Formation.ColumnWidth,true,bothAux);
                flag.Rank=(int)flag.SlotLocal.y;
                flag.FormPriority=int.MaxValue-1;
            }
            if (drum != null) {
                drum.SlotLocal=AuxiliaryPlacement.Slot(soldiers.Count,Ranks,column,Formation.ColumnWidth,false,bothAux);
                drum.Rank=(int)drum.SlotLocal.y;
                drum.FormPriority=int.MaxValue;
            }
            // Médecins : une rangée derrière les auxiliaires (derrière le centre de la ligne ; en colonne, à la queue).
            var medicRow = AuxiliaryPlacement.Slot(soldiers.Count, Ranks, column, Formation.ColumnWidth, false, false).y + 1f;
            for (var i = 0; i < _medics.Count; i++)
            {
                var m = _medics[i];
                m.SlotLocal = new Vector2(i - (_medics.Count - 1) * 0.5f, medicRow);
                m.Rank = (int)medicRow;
                m.FormPriority = int.MaxValue - 2;
            }
            UpdateSlotPositions();
            // Les bots dont la place a bougé repartent (sinon un bot « arrivé » ne bougerait pas pour un décalage < 1 m).
            foreach (var b in Bots)
            {
                if (oldSlots.TryGetValue(b, out var old) && Flat(old - b.SlotPosition).sqrMagnitude > 0.04f) b.Settled = false;
            }
        }

        private float _nextExchangeCheck, _exchangeCooldown;
        private void TryExchangeSlots(float now)
        {
            if(!IsFormation || Mode==LineMode.Square || Firing() || now<_nextExchangeCheck)return;
            _nextExchangeCheck=now+1f;
            foreach(var a in Bots)
            {
                if(!a.Alive || !a.IsLineSoldier || a.GaveUp || a.Parked || a.WaitForStanding || !a.TryGetPosition(out var ap))continue;
                var oldA=Flat(ap-a.SlotPosition).magnitude;
                if(oldA<6f){a.SlotLateSince=-1f;continue;}
                if(a.SlotLateSince<0f){a.SlotLateSince=now;continue;}
                if(now-a.SlotLateSince<3f || now<_exchangeCooldown)continue;
                Bot best=null;float gain=2f;
                foreach(var b in Bots)
                {
                    if(b==a || !b.Alive || !b.IsLineSoldier || b.GaveUp || b.Parked || b.Settled || b.WaitForStanding || !b.TryGetPosition(out var bp))continue;
                    var oldB=Flat(bp-b.SlotPosition).magnitude;
                    var newA=Flat(ap-b.SlotPosition).magnitude;var newB=Flat(bp-a.SlotPosition).magnitude;
                    if(!SlotExchange.Worth(oldA,oldB,newA,newB))continue;
                    var improvement=oldA+oldB-newA-newB;
                    if(improvement>gain){best=b;gain=improvement;}
                }
                if(best==null)continue;
                var slot=a.SlotLocal;a.SlotLocal=best.SlotLocal;best.SlotLocal=slot;
                var rank=a.Rank;a.Rank=best.Rank;best.Rank=rank;
                a.SlotLateSince=best.SlotLateSince=-1f;
                a.Settled=best.Settled=false;a.NextTick=best.NextTick=0f;
                UpdateSlotPositions();_exchangeCooldown=now+4f;
                CmdLog.Info("Échange de places : #"+a.PlayerId+" / #"+best.PlayerId+", gain "+ServerConsole.Num(gain)+" m.");
                return;
            }
        }

        private bool _compactPending;

        /// <summary>Un soldat est en joue ou a une visée / un tir en attente.</summary>
        private bool Firing()
        {
            foreach (var b in Bots)
            {
                if (!b.Alive || !b.IsLineSoldier) continue;
                if (b.Aiming) return true;
                foreach (var a in b.Actions) if (a is AimTask || a is FireTask) return true;
            }
            return FireAtWill;
        }


        /// <summary>
        /// Avant de bouger : fin de la visée. Une recharge en cours continue pendant que le bot marche ; s'il est accroupi,
        /// il se relève après la fin de la recharge.
        /// </summary>
        private void PrepareToMove()
        {
            FireAtWill = false;
            foreach (var b in Soldiers())
            {
                var reloading = b.Reloading || (b.Actions.Count > 0 && b.Actions[0] is ReloadTask && b.Actions[0].Started);
                // Garder la tâche de recharge commencée. La vitesse de course est désactivée dans Bot.Steer
                // tant que Reloading reste vrai, donc le déplacement se fait au pas.
                b.ClearActions(true);
                b.WaitForStanding = b.Crouched && !reloading;
                if (b.Aiming) b.Enqueue(new StopAimTask());
                if (b.Crouched) b.Enqueue(new CrouchTask(false));
            }
        }

        // ---- Mise en formation : couloirs, durée ------------------------------------------------

        private float _halfWidthFiles;

        /// <summary>Demi-largeur du premier rang en mètres (ailes comprises).</summary>
        public float FrontHalfWidth => _halfWidthFiles * Formation.Spacing;

        /// <summary>Profondeur occupée par les rangs derrière le premier, en mètres.</summary>
        public float RanksDepth => (Mathf.Max(1, Ranks) - 1) * Formation.RankGap;

        /// <summary>La ligne se forme sur place (ligne ou carré) : les bots passent par leur couloir (HoldSlotTask).</summary>
        public bool Forming => (Mode == LineMode.Formed || Mode == LineMode.Square) && !IsMoving;

        private float _formStartedAt = -1f;
        private bool _formLogged = true;
        private float _nextFormCheck;

        private void StartForming(float now)
        {
            SideShifting = false;
            _formStartedAt = now;
            _formLogged = false;
        }

        /// <summary>Compte des bots arrivés, en route, qui cèdent le passage (diagnostic).</summary>
        public void CountProgress(out int arrived, out int moving, out int yielding)
        {
            arrived = moving = yielding = 0;
            foreach (var b in Bots)
            {
                if (!b.Alive || b.PlayerId < 0 || b.Role == BotRole.Officer) continue;
                if (b.Settled || b.GaveUp) arrived++;
                else if (b.Yielding) yielding++;
                else moving++;
            }
        }

        /// <summary>Journalise une fois la durée de mise en formation, quand tous les hommes présents sont arrivés.</summary>
        private void CheckFormed(float now)
        {
            if (_formLogged || now < _nextFormCheck) return;
            _nextFormCheck = now + 0.5f;
            if (Mode != LineMode.Formed && Mode != LineMode.Square)
            {
                _formLogged = true;
                return;
            }
            CountProgress(out var arrived, out var moving, out var yielding);
            if (moving + yielding > 0 || arrived == 0) return;
            _formLogged = true;
            CmdLog.Info("Ligne de " + Owner.Name + " formée en " + ServerConsole.Num(now - _formStartedAt) + " s (" + arrived + " hommes).");
            SideShifting = false;
            // Double rang : une fois tout le monde en place, le premier rang s'accroupit (et le reste pour viser, tirer, recharger).
            if (Mode == LineMode.Formed && Ranks > 1)
                foreach (var b in Soldiers()) if (b.Rank == 0 && !b.Crouched && !b.GaveUp) b.Enqueue(new CrouchTask(true));
        }



        /// <summary>Un bot renonce à sa place après des blocages répétés : il reste où il est, la ligne se resserre sans lui.</summary>
        public void OnBotGaveUp(Bot bot, float now)
        {
            // Conserver la réservation : un obstacle ne doit pas comprimer toute la ligne.
            bot.Settled = false;
            bot.GiveUps++;
            // Bloqué (soldat ou auxiliaire) : après 3 renoncements sans jamais atteindre sa place, il s'arrête où il est jusqu'au
            // prochain ordre au lieu de boucler sur des centaines de détours. Il ne compte plus comme « en route » (la ligne peut
            // se déclarer formée et le 1er rang s'accroupir) et sa place est réattribuée : la ligne se resserre sans lui.
            if (bot.GiveUps >= 3)
            {
                bot.SetMovement(new StandStillTask(), now); // SetMovement remet GaveUp à faux : posé après.
                bot.GaveUp = true;
                LineManager.NoteGaveUp(bot);
                if (bot.IsLineSoldier) Reassign(true);
                return;
            }
            bot.SetMovement(new HoldSlotTask(), now);
            LineManager.NoteGaveUp(bot);
        }

        /// <summary>
        /// Carré : chaque bot prend la place dont l'angle autour du centre est le plus proche du sien (bots et places triés par angle).
        /// 2 rangs par face à partir de CMD:carredeuxrangs soldats. Auxiliaires au centre, avec l'officier.
        /// </summary>
        private void ReassignSquare(List<Bot> soldiers, Bot flag, Bot drum)
        {
            _squareRanks = soldiers.Count >= CommanderConfig.SquareTwoRanks ? 2 : 1;
            var slots = Formation.SquareSlots(soldiers.Count, _squareRanks, out var files);
            var spacing = Formation.Spacing;
            var depth = Formation.RankGap;
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
            // Carré aussi : par numéro (places triées par angle, bots par numéro).
            botOrder.Sort((a, b) => soldiers[a].Number.CompareTo(soldiers[b].Number));

            for (var i = 0; i < botOrder.Count; i++)
            {
                var bot = soldiers[botOrder[i]];
                if (i >= slotOrder.Count)
                {
                    // Surplus (moins de 4, faces égales) : au centre, en réserve derrière l'officier, côte à côte.
                    var extras = botOrder.Count - slotOrder.Count;
                    bot.SlotFace = -1;
                    bot.SlotLocal = new Vector2(i - slotOrder.Count - (extras - 1) * 0.5f, 1.5f);
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
            // Médecins : au centre du carré, derrière le drapeau et le tambour.
            for (var i = 0; i < _medics.Count; i++)
            {
                _medics[i].SlotFace = -1;
                _medics[i].SlotLocal = new Vector2((i - (_medics.Count - 1) * 0.5f) * 0.75f, -0.75f);
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
                            Formation.Spacing, Formation.RankGap, out var faceYaw);
                        b.SlotYaw = faceYaw;
                    }
                    else
                    {
                        b.SlotPosition = Formation.ToWorld(Anchor, Yaw, b.SlotLocal, Formation.Spacing, 0f, 0f);
                        b.SlotYaw = Yaw;
                    }
                }
                return;
            }
            if (Following)
            {
                foreach (var b in Bots)
                {
                    if (!IsMoving)
                    {
                        // À l'arrêt, la colonne se redresse derrière l'officier.
                        // Les bots continuent vers ces points fixes jusqu'à l'arrivée.
                        b.SlotPosition = Formation.ToWorld(Anchor + Formation.Forward(Yaw) * FollowGap,
                            Yaw, b.SlotLocal, Formation.FollowSpacing, Formation.FollowRowGap, Formation.ColumnGap);
                        b.SlotYaw = Yaw;
                        continue;
                    }
                    _trail.Sample(Formation.ColumnGap + b.SlotLocal.y * Formation.FollowRowGap,
                        Anchor, Yaw, out var point, out var direction);
                    b.SlotPosition = point + Formation.Right(direction) * b.SlotLocal.x * Formation.FollowSpacing;
                    b.SlotYaw = direction;
                }
                return;
            }
            var column = Mode == LineMode.Column;
            var spacing = Formation.Spacing;
            // En colonne, les rangées sont un peu plus espacées pour la marche.
            var depth = column ? Formation.RankGap * 1.5f : Formation.RankGap;
            var extraBack = column ? Formation.ColumnGap : 0f;
            foreach (var b in Bots)
            {
                b.SlotPosition = Formation.ToWorld(Anchor, Yaw, b.SlotLocal, spacing, depth, extraBack);
                b.SlotYaw = Yaw;
            }
        }

        /// <summary>Pose la tâche de déplacement qui correspond au mode de la ligne.</summary>
        /// <summary>Remet un bot sur la tâche de déplacement du mode courant (médecin qui revient à sa place).</summary>
        public void ResumeMovement(Bot bot, float now) => ApplyMovement(bot, now);

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

        public void ResumeAll(float now) => ApplyMovementToAll(now);

        /// <summary>Un bot reprend l'ordre de la ligne (fin de la défense en mêlée).</summary>
        public void ResumeBot(Bot bot, float now)
        {
            if (bot.PlayerId >= 0 && bot.Alive) ApplyMovement(bot, now);
        }

        private const float MeleeDefendRadius = 15f;

        /// <summary>
        /// Un bot attaqué au corps à corps : les bots de la ligne à moins de MeleeDefendRadius m de l'attaque se défendent
        /// (MeleeDefendTask), puis reprennent leur place seuls. Sans effet pendant une charge, une dispersion ou la fête.
        /// </summary>
        public void DefendMelee(Vector3 at, float now)
        {
            if (Ended || Celebrating || Mode == LineMode.Charge || Mode == LineMode.Dispersing) return;
            var engaged = 0;
            foreach (var b in Bots.ToArray())
            {
                if (b.PlayerId < 0 || !b.Alive || b.Role == BotRole.Drummer || b.Role == BotRole.Surgeon || b.Role == BotRole.FlagBearer) continue;
                if (b.Movement is MeleeDefendTask) continue;
                if (!b.TryGetPosition(out var p) || Flat(p - at).magnitude > MeleeDefendRadius) continue;
                b.ClearActions(true);
                b.SetMovement(new MeleeDefendTask(), now);
                if (engaged++ < 2 && Random.value < 0.5f) b.Shout(GameIds.CharacterVoicePhrase.Warcry);
            }
            if (engaged > 0) CmdLog.Info("Ligne de " + Owner.Name + " attaquée en mêlée : " + engaged + " bot(s) se défendent.");
        }

        private void ApplyMovementToAll(float now)
        {
            // Nouvel ordre : les bots arrêtés après renoncement réessaient, avec une place (sinon ils viseraient une place périmée).
            var retry = false;
            foreach (var b in Bots) if (b.GaveUp) { retry = true; break; }
            if (retry && IsFormation) Reassign();
            foreach (var b in Bots.ToArray())
            {
                if (b.PlayerId >= 0) ApplyMovement(b, now);
            }
        }

        // ---- Ordres --------------------------------------------------------------------------

        /// <summary>Former la ligne à la position et à l'orientation données (ordre « Former la ligne » de l'officier).</summary>
        public void FormAt(Vector3 position, float yaw, float now)
        {
            var sideShift = IsStableSideShift(position, yaw);
            StartForming(now);
            PrepareToMove();
            LeaveSquare(now);
            Anchor = position;
            Yaw = yaw;
            HasAnchor = true;
            Mode = LineMode.Formed;
            SideShifting = sideShift;
            HasAimPoint = false;
            Running = false;
            Reassign();
            ApplyMovementToAll(now);
        }

        private bool IsStableSideShift(Vector3 position, float yaw)
        {
            if (Mode != LineMode.Formed || IsMoving || !HasAnchor || Firing()
                || Mathf.Abs(Mathf.DeltaAngle(Yaw, yaw)) > 8f) return false;

            var members = Soldiers();
            if (members.Count == 0) return false;
            foreach (var bot in members)
            {
                if (bot.GaveUp) continue;
                if (!bot.Settled || !bot.TryGetPosition(out var current)
                    || Flat(current - bot.SlotPosition).sqrMagnitude > 0.5f * 0.5f) return false;
            }

            var shift = Flat(position - Anchor);
            var lateral = Mathf.Abs(Vector3.Dot(shift, Formation.Right(Yaw)));
            var forward = Mathf.Abs(Vector3.Dot(shift, Formation.Forward(Yaw)));
            return lateral >= 0.25f && lateral <= 8f && forward <= 0.75f && lateral > forward * 1.5f;
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
            PrepareToMove();
            Reassign();
            CmdLog.Info("Suivi de " + Owner.Name + " à " + Formation.ColumnWidth
                + " de front, intervalle " + ServerConsole.Num(Formation.FollowSpacing) + " m × "
                + ServerConsole.Num(Formation.FollowRowGap) + " m ; ligne sur " + Ranks + " rang(s).");
            // Le délai doit être défini AVANT OnStart, sinon il reste en attente
            // et bloque le bot lors de l'ordre suivant.
            foreach (var b in Bots)
                b.StartDelay = b.Alive && b.PlayerId >= 0 ? Mathf.Min(0.6f, Mathf.Max(0f, b.SlotLocal.y) * 0.06f) : 0f;
            ApplyMovementToAll(now);
        }

        private int _lastNumber;

        /// <summary>En avant (pas de course si run) : la ligne avance droit devant ; formée d'abord si besoin.</summary>
        public void March(bool run, float now)
        {
            if (Mode != LineMode.Formed && Mode != LineMode.Marching)
            {
                if (!FormOnOfficer(now)) return;
            }
            Mode = LineMode.Marching;
            Running = run;
            PrepareToMove();
            ApplyMovementToAll(now);
        }

        public void Halt(float now)
        {
            // En carré, les hommes ne bougent déjà pas : la halte n'en sort pas.
            if (Mode == LineMode.Square) return;
            var fromColumn = Following;
            Running = false;
            IsMoving = false;
            if (Mode == LineMode.Marching || Mode == LineMode.Formed || fromColumn)
            {
                Mode = LineMode.Formed;
                if (fromColumn) Reassign();
                StartForming(now);
            }
            else Mode = LineMode.Halted;
            ApplyMovementToAll(now);
        }

        /// <summary>Retraite (phrase Retreat) : la ligne se reforme CMD:retraitem mètres en arrière, face à la même direction.</summary>
        public void Retreat(float now)
        {
            // En suivre ou en colonne, l'ancre est périmée : la ligne se forme d'abord devant l'officier.
            if (!HasAnchor || !IsFormation || Following)
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
                // Simple ↔ double : debout et arme baissée, nouvelles places sans croisement, mêmes règles de déplacement.
                PrepareToMove();
                StartForming(now);
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
            var aimed = 0;
            var skipped = 0;
            foreach (var b in Soldiers())
            {
                // Une mise en joue interrompt le déplacement. Les bots encore en route, en recharge ou en attente de se relever
                // restent donc sur leur tâche actuelle et ne recevront pas cet ordre rétroactivement à leur arrivée.
                if (!ReadyForVolley(b)) { skipped++; continue; }
                b.ClearActions(true);
                b.Enqueue(new AimTask(Random.Range(0f, AimSpread)));
                aimed++;
            }
            CmdLog.Info("En joue : " + aimed + " bot(s) prêt(s), " + skipped + " ignoré(s) (déplacement/recharge/posture ; attendre le prochain ordre).");
        }

        /// <summary>Feu de salve. rank = -1 : toute la ligne ; 0 ou 1 : un seul rang.</summary>
        public void Fire(int rank, float now)
        {
            if (!HasAimPoint) CapturePlayerAim();
            FireAtWill = false;
            var fired = 0;
            var skipped = 0;
            foreach (var b in Soldiers())
            {
                if (rank >= 0 && b.Rank != rank) continue;
                // Un bot qui n'a pas fini de rejoindre sa place ou de recharger ne doit ni s'arrêter ni tirer plus tard sans nouvel ordre.
                if (!ReadyForVolley(b)) { skipped++; continue; }
                b.ClearActions(true);
                b.Enqueue(new FireTask(Random.Range(0f, VolleySpread)));
                fired++;
            }
            CmdLog.Info("Feu : " + fired + " bot(s) prêt(s), " + skipped + " ignoré(s) (déplacement/recharge/posture ; attendre le prochain ordre).");
        }

        private static bool ReadyForVolley(Bot bot)
        {
            return bot.Alive && bot.PlayerId >= 0 && bot.Settled && !bot.GaveUp && !bot.Parked
                   && !bot.WaitForStanding && bot.Loaded && !bot.Reloading;
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
            if (!HasAnchor || !IsFormation || Following)
            {
                if (!FormOnOfficer(now)) return;
            }
            var point = target ?? Anchor + Formation.Forward(Yaw) * ChargeDistance;
            Mode = LineMode.Charge;
            ChargeStartedAt = now;
            FireAtWill = false;
            Running = true;
            var right = Formation.Right(Yaw);
            var shouts = 0;
            // Copie : une commande peut rappeler le mod (mort, départ) et modifier Bots.
            foreach (var b in Bots.ToArray())
            {
                if (b.PlayerId < 0) continue;
                // Une pose de baïonnette déjà commencée va à son terme (pas de seconde pose).
                b.ClearActions(true);
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
                if (!b.Bayonet && !b.BayonetBusy) b.Enqueue(new BayonetTask(true));
                // Chacun garde à peu près sa file : la ligne ne se resserre pas en un point.
                b.SetMovement(new ChargeTask(point + right * (b.SlotLocal.x * Formation.Spacing)), now);
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
            StartForming(now);
            Anchor = position;
            Yaw = yaw;
            HasAnchor = true;
            Mode = LineMode.Square;
            HasAimPoint = false;
            Running = false;
            FireAtWill = false;
            Reassign();
            foreach (var b in Bots.ToArray())
            {
                if (b.PlayerId < 0) continue;
                ApplyMovement(b, now);
                if (!b.Alive || !b.IsLineSoldier) continue;
                b.ClearActions(true);
                if (!b.Bayonet && !b.BayonetBusy) b.Enqueue(new BayonetTask(true));
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
        /// <summary>
        /// Musique (tambour, fifre…) selon CMD:musique et le choix de l'officier (!musique) : « marche » = en marche ou en
        /// suivant quand la ligne bouge ; « toujours » = aussi ligne formée, halte, carré, mais pas en charge, en dispersion ni au
        /// feu à volonté ; « jamais ». Le musicien reste à sa place (aile, tête de colonne). Une seule demande à chaque
        /// changement (le jeu refuse deux demandes du même joueur à moins de 3 s), relance éventuelle après CMD:musiquerelance s.
        /// </summary>
        private void UpdateMusic(float now)
        {
            bool want;
            switch (CommanderConfig.MusicMode)
            {
                case 0: want = false; break;
                case 1: want = IsMoving && (Mode == LineMode.Marching || Following); break;
                default: want = Mode != LineMode.Charge && Mode != LineMode.Dispersing && !FireAtWill; break;
            }
            want = (want || Celebrating) && MusicOn && !Orphaned;
            // Relance : intervalle réglé, ou en mode hasard un nouveau morceau après RandomSongLength s (le serveur ne signale pas
            // la fin d'un morceau).
            var interval = CommanderConfig.MusicRelaunch > 0f ? CommanderConfig.MusicRelaunch : (RandomSongs ? CommanderConfig.RandomSongLength : 0f);
            if (want && _drumming && interval > 0f && now - _musicStartedAt >= interval)
            {
                _drumming = false;
                foreach (var b in Bots) b.MusicPlaying = false;
            }
            if (want && !_drumming)
            {
                _drumming = true;
                _musicStartedAt = now;
                // Hasard : un morceau différent du précédent, choisi pour l'instrument du premier musicien ; les autres le
                // reprennent s'ils peuvent le jouer, sinon chacun tire un morceau de son instrument.
                if (RandomSongs)
                {
                    Bot first = null;
                    for (var i = 0; i < Bots.Count; i++)
                        if (Bots[i].Role == BotRole.Drummer && Bots[i].InstrumentConfirmed) { first = Bots[i]; break; }
                    if (first != null)
                    {
                        var pick = SongCatalog.Pick(first.Instrument, SongId);
                        if (pick > 0) SongId = pick;
                    }
                }
            }
            else if (!want) _drumming = false;
            // Par musicien : il faut qu'il TIENNE son instrument (confirmé par OnPlayerStartCarry) ; une demande au plus toutes
            // les 3,1 s par musicien (le jeu refuse deux demandes du même joueur à moins de 3 s). Toujours instrumentPlay avec
            // l'instrument tenu : le client n'anime le musicien que si l'instrument du paquet est celui qu'il a en main
            // (playSong en tire un au hasard parmi ceux du morceau, d'où l'absence d'animation).
            // Copie réutilisée (pas d'allocation par image) : les commandes peuvent rappeler le mod et modifier Bots.
            _musicScratch.Clear();
            _musicScratch.AddRange(Bots);
            foreach (var b in _musicScratch)
            {
                if (b.Role != BotRole.Drummer || b.PlayerId < 0 || !b.Alive || !b.InstrumentConfirmed) continue;
                // Mains libres d'abord (baguettes) ; après 10 essais sans confirmation, on joue quand même.
                if (!b.UnarmedConfirmed && b.UnarmedNextTry >= 0f) continue;
                if (b.MusicPlaying == want || now < b.NextMusicRequest) continue;
                b.NextMusicRequest = now + 3.1f;
                if (!want)
                {
                    b.MusicPlaying = false;
                    BotCommands.Instrument(b.PlayerId, false);
                    continue;
                }
                var song = SongId;
                if (RandomSongs && !SongCatalog.Fits(song, b.Instrument)) song = SongCatalog.Pick(b.Instrument, -1);
                if (song <= 0)
                {
                    LineManager.NoteMusic(b, "aucun morceau connu pour " + GameIds.InstrumentName(b.Instrument));
                    b.NextMusicRequest = now + 30f;
                    continue;
                }
                b.MusicPlaying = true;
                b.SongRequestedAt = now;
                b.MusicBuffSeen = false;
                BotCommands.Instrument(b.PlayerId, true, b.Instrument, song, out var played);
                LineManager.NoteMusic(b, "morceau " + song + " « " + SongCatalog.Name(song) + " », instrumentPlay "
                                         + GameIds.InstrumentName(b.Instrument) + " : « " + played + " »"
                                         + (SongCatalog.Fits(song, b.Instrument) ? "" : " (morceau hors table pour cet instrument)"));
            }
        }
        /// <summary>Officier : musique de sa ligne (!musique), et morceau choisi (!musique n).</summary>
        public bool MusicOn = true;
        /// <summary>Officier : son médecin soigne (!medecin).</summary>
        public bool MedicOn = true;
        /// <summary>Fête de victoire en cours (LineManager.TickCelebration).</summary>
        public bool Celebrating;
        public bool FireAtWillBeforeCelebration;
        public int SongId = 1;
        /// <summary>Morceaux au hasard (défaut) ; faux après !musique n (morceau fixe).</summary>
        public bool RandomSongs = true;
        private float _musicStartedAt;

        /// <summary>Change le morceau : relancé au prochain passage.</summary>
        public void SetSong(int songId)
        {
            foreach (var b in Bots) b.MusicPlaying = false;
            SongId = songId;
            RandomSongs = songId <= 0;
            MusicOn = true;
            _drumming = false;
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
