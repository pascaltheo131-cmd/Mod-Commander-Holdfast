using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Tâche d'un bot. Tâches de déplacement : Tick appelé tant qu'elle est en place (sa valeur de retour est ignorée).
    /// Actions : exécutées une à la fois dans la file du bot, Tick renvoie vrai quand l'action est finie.
    /// Les durées marquées [TEST] sont des suppositions à régler en jeu.
    /// </summary>
    public abstract class BotTask
    {
        public bool Started { get; private set; }

        public void Start(Bot bot, float now)
        {
            Started = true;
            OnStart(bot, now);
        }

        /// <summary>Nom court pour le diagnostic (écrit à la main : GetType().Name passe par la réflexion).</summary>
        public virtual string Name => "tâche";

        protected virtual void OnStart(Bot bot, float now) { }
        public abstract bool Tick(Bot bot, float now);
        public virtual void Stop(Bot bot) { }
    }

    // ---- Déplacements ----------------------------------------------------------------------

    /// <summary>Tenir sa place dans la formation (la place suit l'ancre de la ligne : arrêt, marche, colonne).</summary>
    public class HoldSlotTask : BotTask
    {
        /// <summary>Aucun autre bot de la ligne à moins de 0,8 m du segment entre le bot et sa place.</summary>
        private static bool PathClear(Bot bot, Vector3 from, Vector3 to)
        {
            return FormationNavigation.Direct(bot,from,to);
        }

        public override string Name => "place";

        // Ligne en mouvement : le bot vise un point LeadDistance m devant sa place, pour marcher au pas de la ligne au lieu de
        // s'arrêter et repartir ; ligne arrêtée : sa place exacte, rayon d'arrivée serré.
        private const float LeadDistance = 1.5f;
        private const float ArriveRadius = 0.15f;

        // Mise en formation sur place, en deux temps (trois si le bot est devant la ligne) : 0 = contourner par l'aile la plus
        // proche, 1 = rejoindre son couloir (point derrière tous les rangs, dans l'axe de sa place), 2 = avancer droit vers sa
        // place. Le second rang arrive ainsi par l'arrière sans traverser le premier, et les trajets ne se croisent pas (places
        // affectées de gauche à droite). Départ échelonné du centre vers les ailes.
        private Vector3 _routeSlot;
        private float _startAt;
        private System.Collections.Generic.List<Vector3> _route;
        private int _routeIndex, _routeAttempt;
        private float _routeCheckAt, _routeBest;
        private float _routeYaw;
        private float _nextShortcutAt;


        protected override void OnStart(Bot bot, float now)
        {

            _route=null; _routeIndex=0; _routeAttempt=0;
            // Tout le monde part en même temps (plus d'échelonnement : il allongeait la mise en formation).
            _startAt = now;
            bot.FormYields = 0;
            // Colonne qui se déroule : départ dans l'ordre de la file (Line.Follow).
            if (bot.StartDelay > 0f)
            {
                _startAt = now + bot.StartDelay;
                bot.StartDelay = 0f;
            }
        }

        public override bool Tick(Bot bot, float now)
        {
            var line = bot.Line;
            if(bot.WaitForStanding){bot.Stop();return false;}
            // Désépauler ou se relever d'abord : jamais en joue ni accroupi en marchant.
            if (bot.Actions.Count > 0 && (bot.Actions[0] is StopAimTask || bot.Actions[0] is CrouchTask))
            {
                bot.Stop();
                return false;
            }
            if (now < _startAt)
            {
                bot.Stop();
                return false;
            }
            var hasPos = bot.TryGetPosition(out var pos);
            if (line.SideShifting && hasPos)
            {
                bot.Steer(bot.SlotPosition, bot.SlotYaw, false, ArriveRadius, true, true);
                return false;
            }
            if (!line.Forming || bot.SlotFace >= 0 || !hasPos)
            {
    
                var target = bot.SlotPosition;
                // Colonne : un bot en retard (plus de 4 m) reprend la trace de l'officier au point le plus proche de lui et
                // la suit en gardant sa file et sans dépasser son rang.
                if (line.Following && line.IsMoving && hasPos && Line.Flat(target - pos).magnitude > 4f
                    && line.TrailLead(pos, 3f, bot, out var lead))
                {
                    bot.Steer(lead, bot.SlotYaw, true, 0.3f);
                    return false;
                }
                if (line.IsMoving && !line.Following) target += Formation.Forward(bot.SlotYaw) * LeadDistance;
                bot.Steer(target, bot.SlotYaw, line.Running, ArriveRadius);
                return false;
            }
            var slot=bot.SlotPosition;
            if(_route!=null && (Line.Flat(slot-_routeSlot).sqrMagnitude>0.01f || Mathf.Abs(Mathf.DeltaAngle(_routeYaw,line.Yaw))>1f))
            { _route=null; _routeAttempt=0; }
            if(Line.Flat(slot-pos).magnitude<=ArriveRadius) { bot.Steer(slot,bot.SlotYaw,false,ArriveRadius); return false; }
            if(_route==null)
            {
                _routeSlot=slot;_routeYaw=line.Yaw;
                _route=PathClear(bot,pos,slot) && _routeAttempt==0
                    ? new System.Collections.Generic.List<Vector3>{slot}
                    : DeploymentRoute.Build(pos,slot,line.Anchor,line.Yaw,line.FrontHalfWidth,Mathf.Max(line.RanksDepth,bot.Rank*Formation.RankGap),bot.Rank==0,_routeAttempt);
                _routeIndex=0;_routeBest=float.MaxValue;_routeCheckAt=now;
            }
            // Les camarades se déplacent : supprimer les détours devenus
            // inutiles, seulement si tout le segment est libre.
            if(now>=_nextShortcutAt && _routeIndex<_route.Count-1)
            {
                _nextShortcutAt=now+0.4f;
                for(var j=_route.Count-1;j>_routeIndex;j--)
                {
                    if(!PathClear(bot,pos,_route[j]))continue;
                    _routeIndex=j;_routeBest=float.MaxValue;_routeCheckAt=now;break;
                }
            }
            if(_route.Count==1 && Line.Flat(slot-pos).magnitude>0.75f && !PathClear(bot,pos,slot))
            { _route=null;_routeAttempt=1;bot.Stop();return false; }
            var waypoint=_route[_routeIndex];
            var distance=Line.Flat(waypoint-pos).magnitude;
            if(distance<_routeBest-0.1f){_routeBest=distance;_routeCheckAt=now;}
            if(now-_routeCheckAt>1.2f)
            {
                // Nouvelle route même en foule : ne jamais rester bloqué sur
                // l'approche arrière d'un premier rang barré par le second.
                _route=null;_routeAttempt++;bot.Stop();return false;
            }
            var last=_routeIndex==_route.Count-1;
            var preserveFacing = last && _route.Count == 1 && line.Mode == LineMode.Formed;
            if(bot.Steer(waypoint,bot.SlotYaw,false,last?ArriveRadius:0.25f,preserveFacing) && !last)
            { _routeIndex++;_routeBest=float.MaxValue;_routeCheckAt=now; }
            if(!last)bot.Settled=false;

            return false;
        }
    }

    /// <summary>
    /// Carré (lot B, n° 7) : tenir sa place face à l'extérieur ; toutes les ScanInterval secondes, chercher l'ennemi le plus proche
    /// dans l'arc de sa face (cavalerie de plus loin), se tourner vers lui et tirer si l'arme est chargée. Le rechargement
    /// est relancé par Line.OnBotIdle.
    /// </summary>
    public class SquareHoldTask : BotTask
    {
        public override string Name => "carré";

        private const float ScanInterval = 0.5f;
        private const float HalfArc = 50f;
        private float _nextScan;
        private float _targetYaw = float.NaN;

        public override bool Tick(Bot bot, float now)
        {
            if (now >= _nextScan && bot.TryGetPosition(out var pos))
            {
                _nextScan = now + ScanInterval + Random.Range(0f, 0.2f);
                var id = LineManager.NearestEnemyInArc(bot.Line.Faction, pos, bot.SlotYaw, HalfArc,
                    CommanderConfig.SquareRange, CommanderConfig.SquareCavalryRange);
                var enemy = id >= 0 ? Players.Get(id) : null;
                if (enemy != null && enemy.TryGetPosition(out var ep))
                {
                    var d = Line.Flat(ep - pos);
                    _targetYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    if (bot.Loaded && bot.Actions.Count == 0) bot.Enqueue(new FireTask(Random.Range(0.1f, 0.8f)));
                }
                else _targetYaw = float.NaN;
            }
            bot.Steer(bot.SlotPosition, float.IsNaN(_targetYaw) ? bot.SlotYaw : _targetYaw, false, 0.5f);
            return false;
        }
    }

    /// <summary>Rester sur place (halte hors formation).</summary>
    public class StandStillTask : BotTask
    {
        public override string Name => "immobile";

        protected override void OnStart(Bot bot, float now)
        {
            bot.Stop();
        }

        public override bool Tick(Bot bot, float now)
        {
            bot.SetAxis(0f, 0f);
            return false;
        }
    }

    /// <summary>Suivre l'officier en ordre lâche (rompre et suivre), chacun à son décalage, avec une marge pour ne pas piétiner.</summary>
    public class FollowOfficerTask : BotTask
    {
        public override string Name => "suit";

        private const float StartDistance = 4f;
        private const float StopDistance = 1.5f;
        private bool _moving;

        public override bool Tick(Bot bot, float now)
        {
            // Ligne sans officier (succession) ou reprise par un autre : plus personne à suivre.
            if (bot.Line.Orphaned || bot.Line.Owner.Line != bot.Line)
            {
                bot.SetAxis(0f, 0f);
                return false;
            }
            var officer = Players.Get(bot.Line.Owner.PlayerId);
            if (officer == null || !officer.TryGetPosition(out var officerPos) || !bot.TryGetPosition(out var pos))
            {
                bot.SetAxis(0f, 0f);
                return false;
            }
            var target = officerPos + bot.FollowOffset;
            var dist = Line.Flat(target - pos).magnitude;
            if (!_moving && dist > StartDistance) _moving = true;
            if (!_moving) return false;
            var faceYaw = officer.TryGetYaw(out var y) ? y : bot.SlotYaw;
            // Course si l'officier s'éloigne vite, ou si la ligne est au pas de course.
            if (bot.Steer(target, faceYaw, bot.Line.Running || dist > 12f, StopDistance)) _moving = false;
            return false;
        }
    }

    /// <summary>
    /// Charge : course vers le point de charge ; en chemin, le bot prend pour cible l'ennemi vivant le plus proche
    /// (dans ChaseRadius) et, au contact, lance la boucle de frappes du jeu (« MeleeStrikeRandom »), arrêtée dès qu'il n'y a
    /// plus personne à portée ou que la tâche prend fin.
    /// </summary>
    public class ChargeTask : BotTask
    {
        public override string Name => "charge";

        private const float ChaseRadius = 25f;
        private const float StrikeDistance = 2.2f;
        private const float MountedStrikeDistance = 3.5f;
        private const float RetargetInterval = 1f;

        private readonly Vector3 _point;
        private int _targetId = -1;
        private float _nextRetarget;
        private bool _striking;
        // Garde de mêlée prise dès l'ordre de charge (après la pose de la baïonnette, que la garde interromprait).
        private bool _stance;
        // Cavalier : sabre sorti avant de frapper (la boucle de frappes du jeu ignore une arme à feu).
        private float _nextSwitch;
        private int _switches;
        private const int MaxSwitches = 4;

        // Cavalerie : sabre de plus loin et sans s'arrêter, le cheval passe au travers.
        private readonly bool _mounted;
        // Cavaliers (à cheval ou à pied) : sabre au lieu de la baïonnette.
        private readonly bool _sabre;

        public ChargeTask(Vector3 point, bool mounted = false, bool sabre = false)
        {
            _point = point;
            _mounted = mounted;
            _sabre = sabre || mounted;
        }

        public override bool Tick(Bot bot, float now)
        {
            if (!bot.TryGetPosition(out var pos)) return false;
            if (_sabre && !bot.HasSword && _switches < MaxSwitches && now >= _nextSwitch)
            {
                _switches++;
                _nextSwitch = now + 1f;
                var sabre = BotCommands.DrawSabre(bot.PlayerId, bot.SabreType);
                if (sabre != null) bot.SabreType = sabre;
                else if (_switches == 1) CmdLog.Info(bot + " : aucun sabre accepté par le jeu.");
            }
            var armed = !_sabre || bot.HasSword;
            if (armed && !_stance && !_striking && bot.MeleeExitAt < 0f && !bot.BayonetBusy && bot.Actions.Count == 0)
            {
                _stance = true;
                BotCommands.Action(bot.PlayerId, GameIds.PlayerActions.EnableCombatStance);
            }
            if (now >= _nextRetarget)
            {
                _nextRetarget = now + RetargetInterval;
                if (_mounted) { if (_phase == 0) _targetId = PickMountedTarget(bot, pos); }
                else _targetId = LineManager.NearestEnemy(bot.Line.Faction, pos, ChaseRadius);
            }

            // Cavalerie : après un passage, le cavalier file droit devant puis revient pour un nouveau passage (pas de
            // demi-tour sur place au milieu de la mêlée, où les chevaux s'entassent).
            if (_mounted && _phase == 1)
            {
                SetStriking(bot, false);
                if (Line.Flat(_rideOutPoint - pos).magnitude < 6f || now >= _rideOutUntil)
                {
                    _phase = 0;
                    _contactAt = -1f;
                    _nextRetarget = 0f;
                }
                else
                {
                    bot.Steer(_rideOutPoint, bot.SlotYaw, true, 3f);
                    return false;
                }
            }
            var enemy = _targetId >= 0 ? Players.Get(_targetId) : null;
            if (enemy != null && enemy.Alive && enemy.TryGetPosition(out var enemyPos))
            {
                var delta = Line.Flat(enemyPos - pos);
                var yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                if (_mounted && _phase == 0)
                {
                    SetStriking(bot, armed && delta.magnitude <= MountedStrikeDistance);
                    if (delta.magnitude <= MountedStrikeDistance && _contactAt < 0f) _contactAt = now;
                    // Passage fini : ennemi dépassé (derrière le cheval, tout près) ou contact tenu PassContact s.
                    var passed = bot.Info != null && bot.Info.TryGetYaw(out var rideYaw)
                                 && Mathf.Abs(Mathf.DeltaAngle(rideYaw, yaw)) > 100f && delta.magnitude < 10f;
                    if (passed || (_contactAt >= 0f && now - _contactAt > PassContact))
                    {
                        StartRideOut(bot, pos, now);
                        return false;
                    }
                    // Baïonnettes de face : un cavalier qui n'est pas au grand galop refuse l'obstacle et ressort.
                    if (delta.magnitude < BayonetRefuse && bot.HorseGear < 4 && FacesWithBayonet(_targetId, pos))
                    {
                        StartRideOut(bot, pos, now);
                        return false;
                    }
                    // Vise un peu au-delà de l'ennemi : le cavalier le traverse au galop au lieu de piler devant.
                    bot.Steer(enemyPos + delta.normalized * 4f, yaw, true, 1f);
                    return false;
                }
                if (delta.magnitude > StrikeDistance)
                {
                    SetStriking(bot, false);
                    bot.Steer(enemyPos, yaw, true, StrikeDistance);
                    return false;
                }
                bot.Stop();
                bot.SetYaw(yaw);
                // Pas de frappe pendant la pose de la baïonnette : la garde de mêlée l'interromprait (retrait de la baïonnette).
                SetStriking(bot, !bot.BayonetBusy && bot.Actions.Count == 0);
                return false;
            }
            SetStriking(bot, false);
            bot.Steer(_point, bot.SlotYaw, true, _mounted ? 3f : 1.5f);
            return false;
        }

        // ---- Passages de cavalerie ----
        public int TargetId => _targetId;
        private int _phase;
        private float _contactAt = -1f;
        private Vector3 _rideOutPoint;
        private float _rideOutUntil;
        private const float PassContact = 1.5f;
        private const float RideOutDistance = 35f;
        private const float RideOutTime = 6f;
        // Poids d'un camarade qui vise déjà la même cible, en mètres de distance supplémentaire.
        private const float ClaimPenalty = 15f;
        // Distance à laquelle un cheval refuse une baïonnette pointée vers lui.
        private const float BayonetRefuse = 12f;

        /// <summary>
        /// L'ennemi est un fantassin bot, baïonnette au canon, tourné vers le cavalier (à 45° près). Les vrais joueurs ne sont pas
        /// concernés : le mod ne connaît pas l'état de leur baïonnette.
        /// </summary>
        private static bool FacesWithBayonet(int enemyId, Vector3 riderPos)
        {
            var foe = LineManager.GetBot(enemyId);
            if (foe == null || foe.Line.Mounted || !foe.Bayonet) return false;
            var info = foe.Info;
            if (info == null || !info.TryGetPosition(out var fp) || !info.TryGetYaw(out var fy)) return false;
            var toRider = Line.Flat(riderPos - fp);
            return Mathf.Abs(Mathf.DeltaAngle(fy, Mathf.Atan2(toRider.x, toRider.z) * Mathf.Rad2Deg)) < 45f;
        }

        private void StartRideOut(Bot bot, Vector3 pos, float now)
        {
            var forward = bot.Info != null && bot.Info.TryGetYaw(out var y) ? Formation.Forward(y) : Formation.Forward(bot.SlotYaw);
            // Sortie un peu en biais selon la place : les cavaliers s'écartent au lieu de repartir en paquet.
            var side = Formation.Right(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg) * (bot.SlotLocal.x * 4f);
            _rideOutPoint = pos + forward * RideOutDistance + side;
            _rideOutUntil = now + RideOutTime;
            _phase = 1;
            _targetId = -1;
            SetStriking(bot, false);
        }

        /// <summary>Cible d'un cavalier : l'ennemi le plus proche, en évitant ceux que ses camarades visent déjà.</summary>
        private static int PickMountedTarget(Bot bot, Vector3 pos)
        {
            var best = -1;
            var bestScore = float.MaxValue;
            foreach (var p in Players.All)
            {
                if (!p.Alive || p.Faction == GameIds.FactionCountry.None || p.Faction == bot.Line.Faction) continue;
                if (!p.TryGetPosition(out var ep)) continue;
                var d = Line.Flat(ep - pos).magnitude;
                if (d > ChaseRadius) continue;
                var claims = 0;
                foreach (var mate in bot.Line.Bots)
                    if (mate != bot && mate.Movement is ChargeTask other && other.TargetId == p.Id) claims++;
                var score = d + claims * ClaimPenalty;
                if (score >= bestScore) continue;
                bestScore = score;
                best = p.Id;
            }
            return best;
        }

        public override void Stop(Bot bot)
        {
            // Fin de la charge : sortie de la garde, même sans frappe en cours.
            if (_stance && !_striking) bot.MeleeExitAt = Clock.Now + 0.05f;
            SetStriking(bot, false);
        }

        private void SetStriking(Bot bot, bool strike)
        {
            if (_striking == strike) return;
            _striking = strike;
            // La sortie des frappes quitte aussi la garde : elle sera reprise une fois la sortie faite.
            if (!strike) _stance = false;
            BotCommands.MeleeLoop(bot.PlayerId, strike);
            // Fin des frappes (cible morte ou hors de portée, nouvel ordre, dispersion, fête) : sortie de garde à l'image suivante.
            bot.MeleeExitAt = strike ? -1f : Clock.Now + 0.05f;
        }
    }

    /// <summary>
    /// Défense en mêlée : la ligne attaquée au corps à corps se défend. Le bot frappe l'ennemi le plus proche à moins de
    /// DefendRadius m (crosse ou baïonnette déjà en place, pas de pose : trop lente) ; sans ennemi proche pendant CalmDelay s,
    /// il reprend l'ordre de sa ligne (sa place).
    /// </summary>
    public class MeleeDefendTask : BotTask
    {
        public override string Name => "défense en mêlée";

        private const float DefendRadius = 8f;
        private const float StrikeDistance = 2.2f;
        private const float RetargetInterval = 0.5f;
        private const float CalmDelay = 4f;

        private int _targetId = -1;
        private float _nextRetarget;
        private float _calmSince = -1f;
        private bool _striking;

        public override bool Tick(Bot bot, float now)
        {
            if (!bot.TryGetPosition(out var pos)) return false;
            if (now >= _nextRetarget)
            {
                _nextRetarget = now + RetargetInterval;
                _targetId = LineManager.NearestEnemy(bot.Line.Faction, pos, DefendRadius);
            }
            var enemy = _targetId >= 0 ? Players.Get(_targetId) : null;
            if (enemy != null && enemy.Alive && enemy.TryGetPosition(out var enemyPos))
            {
                _calmSince = -1f;
                var delta = Line.Flat(enemyPos - pos);
                var yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                if (delta.magnitude > StrikeDistance)
                {
                    SetStriking(bot, false);
                    bot.Steer(enemyPos, yaw, true, StrikeDistance);
                    return false;
                }
                bot.Stop();
                bot.SetYaw(yaw);
                SetStriking(bot, !bot.BayonetBusy && bot.Actions.Count == 0);
                return false;
            }
            SetStriking(bot, false);
            bot.Stop();
            if (_calmSince < 0f) _calmSince = now;
            else if (now - _calmSince >= CalmDelay) bot.Line.ResumeBot(bot, now);
            return false;
        }

        public override void Stop(Bot bot)
        {
            SetStriking(bot, false);
        }

        private void SetStriking(Bot bot, bool strike)
        {
            if (_striking == strike) return;
            _striking = strike;
            BotCommands.MeleeLoop(bot.PlayerId, strike);
            bot.MeleeExitAt = strike ? -1f : Clock.Now + 0.05f;
        }
    }

    /// <summary>Aller à un point (dispersion) puis s'arrêter.</summary>
    public class GoToPointTask : BotTask
    {
        public override string Name => "va";

        private Vector3 _point;
        private readonly bool _run;

        /// <summary>Nouvelle cible (blessé qui bouge) : changée seulement au-delà de 1 m.</summary>
        public void Retarget(Vector3 point)
        {
            if (Line.Flat(point - _point).sqrMagnitude > 1f) _point = point;
        }

        public GoToPointTask(Vector3 point, bool run)
        {
            _point = point;
            _run = run;
        }

        public override bool Tick(Bot bot, float now)
        {
            if (!bot.TryGetPosition(out var pos)) return false;
            var delta = Line.Flat(_point - pos);
            var yaw = delta.sqrMagnitude > 0.01f ? Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg : bot.SlotYaw;
            return bot.Steer(_point, yaw, _run, 1.5f);
        }
    }

    // ---- Actions ---------------------------------------------------------------------------

    /// <summary>Épauler, après un délai (les hommes ne lèvent pas l'arme tous à la même image).</summary>
    public class AimTask : BotTask
    {
        public override string Name => "épaule";

        public const float AimTime = 0.8f; // [TEST] temps pour épauler
        private readonly float _delay;
        private float _at;
        private float _ready;

        public AimTask(float delay)
        {
            _delay = delay;
        }

        protected override void OnStart(Bot bot, float now)
        {
            _at = now + _delay;
            _ready = float.MaxValue;
        }

        public override bool Tick(Bot bot, float now)
        {
            if (now < _at) return false;
            if (_ready == float.MaxValue)
            {
                // Déjà en joue, ou rien à tirer : rien à faire.
                if (bot.Aiming || !bot.Loaded) return true;
                bot.Act(GameIds.PlayerActions.StartAimingFirearm);
                bot.Aiming = true;
                // La visée est visible dès l'ordre Viser, avant même l'ordre Feu.
                Shooting.Solution(bot, out var aimYaw, out var aimPitch);
                bot.SetYaw(aimYaw);
                bot.SetPitch(aimPitch);
                _ready = now + AimTime;
                return false;
            }
            return now >= _ready;
        }
    }

    /// <summary>
    /// Tirer après un délai ; épaule d'abord si besoin. Sans balle, l'action se termine sans rien faire.
    /// Moral bas (Morale.Shake) : délai aléatoire en plus et inclinaison du tir bruitée.
    /// </summary>
    public class FireTask : BotTask
    {
        public override string Name => "tir";

        private readonly float _delay;
        private float _at;

        public FireTask(float delay)
        {
            _delay = delay;
        }

        private bool _sighted;
        private float _shotYaw;
        private float _shotPitch;
        private const float ShakenDelay = 2f;
        protected override void OnStart(Bot bot, float now)
        {
            _at = now + _delay + Random.Range(0f, ShakenDelay * Morale.Shake(bot.Line));
        }

        public override bool Tick(Bot bot, float now)
        {
            if (!bot.Loaded) return true;
            if (!bot.Aiming)
            {
                bot.Act(GameIds.PlayerActions.StartAimingFirearm);
                bot.Aiming = true;
                _at = Mathf.Max(_at, now + AimTask.AimTime);
                return false;
            }
            if (now < _at) return false;
            if (!_sighted)
            {
                Shooting.Solution(bot, out _shotYaw, out _shotPitch);
                _sighted = true;
                if (LineManager.DebugEnabled && bot.Line.FireAtWill) CmdLog.Info(bot + " : tir à volonté, " + Shooting.LastTarget + ".");
                _at = now + 0.2f; // Laisser passer un FixedUpdate avant le tir.
                bot.SetYaw(_shotYaw);
                bot.SetPitch(_shotPitch);
                return false;
            }
            bot.SetYaw(_shotYaw);
            bot.SetPitch(_shotPitch);
            bot.Act(GameIds.PlayerActions.FireFirearm);
            bot.Loaded = false;
            return true;
        }
    }

    public class ReloadTask : BotTask
    {
        public override string Name => "recharge";

        /// <summary>
        /// Attente avant de considérer l'arme rechargée [TEST]. Le jeu finit le rechargement seulement si FinishReloadFirearm
        /// est reçu, et pas avant reloadTime × 0,75 (0,5 pour une arme à projectile physique) [CODE, CreatorWeaponHolder.
        /// HandleQueuedFinishedFirearmReload] : la demande de fin est envoyée tout de suite, le jeu la garde jusqu'au bon moment.
        /// Un tir à vide (OnPlayerShoot dryShot) relance de toute façon un rechargement.
        /// </summary>
        public const float ReloadTime = 9f;
        private const float StopAimDelay = 0.2f;

        // 0 : désépauler ; 1 : commencer le rechargement ; 2 : demander la fin ; 3 : attendre.
        private int _phase;
        private float _next;
        private float _done;

        protected override void OnStart(Bot bot, float now)
        {
            _phase = 0;
            _next = now;
        }

        public override bool Tick(Bot bot, float now)
        {
            if (now < _next) return false;
            switch (_phase)
            {
                case 0:
                    // Le jeu refuse de recharger en joue (WeaponHolder.CanReloadFirearm) : désépauler d'abord, dans un paquet à part.
                    if (bot.Aiming)
                    {
                        bot.Act(GameIds.PlayerActions.StopAimingFirearm);
                        bot.Aiming = false;
                        _next = now + StopAimDelay;
                    }
                    _phase = 1;
                    return false;
                case 1:
                    bot.Act(GameIds.PlayerActions.StartReloadFirearm);
                    bot.Reloading = true;
                    _done = now + ReloadTime;
                    _phase = 2;
                    // Paquet suivant pour la fin (deux playerAction avant le même FixedUpdate s'écrasent ; ici, un passage plus tard).
                    _next = now + 0.1f;
                    return false;
                case 2:
                    bot.Act(GameIds.PlayerActions.FinishReloadFirearm);
                    _phase = 3;
                    return false;
                default:
                    if (now < _done) return false;
                    bot.Reloading = false;
                    bot.Loaded = true;
                    return true;
            }
        }

        public override void Stop(Bot bot)
        {
            // Rechargement abandonné (charge, nouvel ordre) : le jeu l'arrête aussi, sinon le bot finit son geste.
            if (bot.Reloading && bot.Alive && bot.PlayerId >= 0) bot.Act(GameIds.PlayerActions.InterruptReloadFirearm);
            bot.Reloading = false;
        }
    }

    /// <summary>Baisser l'arme.</summary>
    /// <summary>
    /// Charge en coin (cavalerie, !coin) : chaque cavalier tient sa place dans le triangle derrière l'officier, sabre en main,
    /// et frappe tout ennemi à portée sans quitter la formation. L'officier (joueur) mène la pointe.
    /// </summary>
    public class WedgeTask : BotTask
    {
        public override string Name => "coin";
        private const float StrikeDistance = 3.5f;
        private const int MaxSwitches = 4;
        private int _switches;
        private float _nextSwitch;
        private bool _striking;

        public override bool Tick(Bot bot, float now)
        {
            if (!bot.TryGetPosition(out var pos)) return false;
            if (!bot.HasSword && _switches < MaxSwitches && now >= _nextSwitch)
            {
                _switches++;
                _nextSwitch = now + 1f;
                var sabre = BotCommands.DrawSabre(bot.PlayerId, bot.SabreType);
                if (sabre != null) bot.SabreType = sabre;
            }
            var near = LineManager.NearestEnemy(bot.Line.Faction, pos, StrikeDistance) >= 0;
            SetStriking(bot, bot.HasSword && near);
            bot.Steer(bot.SlotPosition, bot.SlotYaw, true, 1.5f);
            return false;
        }

        public override void Stop(Bot bot) => SetStriking(bot, false);

        private void SetStriking(Bot bot, bool strike)
        {
            if (_striking == strike) return;
            _striking = strike;
            BotCommands.MeleeLoop(bot.PlayerId, strike);
            bot.MeleeExitAt = strike ? -1f : Clock.Now + 0.05f;
        }
    }

    public class StopAimTask : BotTask
    {
        public override string Name => "désépaule";

        public override bool Tick(Bot bot, float now)
        {
            if (bot.Aiming)
            {
                bot.Act(GameIds.PlayerActions.StopAimingFirearm);
                bot.Aiming = false;
            }
            bot.SetPitch(0f);
            return true;
        }
    }

    /// <summary>S'accroupir ou se relever.</summary>
    public class CrouchTask : BotTask
    {
        public override string Name => "accroupi";

        private readonly bool _down;
        private float _standReady=-1f;

        public CrouchTask(bool down)
        {
            _down = down;
        }

        public override bool Tick(Bot bot, float now)
        {
            if(!_down && _standReady>=0f){if(now<_standReady)return false;bot.WaitForStanding=false;return true;}
            if(bot.Crouched==_down){if(!_down)bot.WaitForStanding=false;return true;}
            bot.Act(_down?GameIds.PlayerActions.StartCrouching:GameIds.PlayerActions.StopCrouching);
            bot.Crouched=_down;
            if(!_down){_standReady=now+0.25f;return false;}
            return true;
        }
    }

    /// <summary>Mettre ou retirer la baïonnette (arme baissée).</summary>
    public class BayonetTask : BotTask
    {
        public override string Name => "baïonnette";

        /// <summary>
        /// Durée laissée à l'animation avant la demande de fin [TEST]. Le jeu ne termine la pose (ou le retrait) que sur
        /// FinishBayonetAttach (67) / FinishBayonetDetach (68), et la termine dès l'Update suivant, sans attendre l'animation
        /// [CODE, CreatorWeaponHolder.HandleQueuedFinishBayonetAttach] : c'est donc au mod d'attendre. Sans fin, l'arme reste
        /// « en cours de pose » ; le moindre passage en garde de mêlée (EnableCombatStance, ajouté par toute frappe) ou parade
        /// l'interrompt ALORS en terminant l'opération inverse (FinishBayonetDetaching(true), _InterruptFirearmActions /
        /// ToggleCombatStance) : d'où le va-et-vient vu en charge.
        /// </summary>
        public const float BayonetTime = 2.5f;
        private const float StopAimDelay = 0.2f;
        private readonly bool _fix;
        // 0 : désépauler ; 1 : commencer ; 2 : attendre puis demander la fin ; 3 : laisser passer un Update.
        private int _phase;
        private float _next;

        public BayonetTask(bool fix)
        {
            _fix = fix;
        }

        protected override void OnStart(Bot bot, float now)
        {
            _phase = bot.Bayonet == _fix ? 4 : 0;
            _next = now;
        }

        public override bool Tick(Bot bot, float now)
        {
            if (_phase == 4) return true;
            if (now < _next) return false;
            switch (_phase)
            {
                case 0:
                    // Pose refusée en joue ou en rechargement (WeaponHolder.CanAttachBayonet) : désépauler d'abord.
                    if (bot.Aiming)
                    {
                        bot.Act(GameIds.PlayerActions.StopAimingFirearm);
                        bot.Aiming = false;
                        _next = now + StopAimDelay;
                    }
                    _phase = 1;
                    return false;
                case 1:
                    bot.Act(_fix ? GameIds.PlayerActions.StartBayonetAttach : GameIds.PlayerActions.StartBayonetDetach);
                    bot.BayonetBusy = true;
                    _next = now + BayonetTime;
                    _phase = 2;
                    return false;
                case 2:
                    bot.Act(_fix ? GameIds.PlayerActions.FinishBayonetAttach : GameIds.PlayerActions.FinishBayonetDetach);
                    _next = now + 0.2f;
                    _phase = 3;
                    return false;
                default:
                    bot.Bayonet = _fix;
                    bot.BayonetBusy = false;
                    return true;
            }
        }

        public override void Stop(Bot bot)
        {
            bot.BayonetBusy = false;
        }
    }
}
