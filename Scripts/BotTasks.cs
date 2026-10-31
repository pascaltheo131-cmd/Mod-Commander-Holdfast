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

        protected virtual void OnStart(Bot bot, float now) { }
        public abstract bool Tick(Bot bot, float now);
        public virtual void Stop(Bot bot) { }
    }

    // ---- Déplacements ----------------------------------------------------------------------

    /// <summary>Tenir sa place dans la formation (la place suit l'ancre de la ligne : arrêt, marche, colonne).</summary>
    public class HoldSlotTask : BotTask
    {
        public override bool Tick(Bot bot, float now)
        {
            bot.Steer(bot.SlotPosition, bot.SlotYaw, bot.Line.Running, 0.5f);
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
        private const float StartDistance = 4f;
        private const float StopDistance = 1.5f;
        private bool _moving;

        public override bool Tick(Bot bot, float now)
        {
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
        private const float ChaseRadius = 25f;
        private const float StrikeDistance = 2.2f;
        private const float RetargetInterval = 1f;

        private readonly Vector3 _point;
        private int _targetId = -1;
        private float _nextRetarget;
        private bool _striking;

        public ChargeTask(Vector3 point)
        {
            _point = point;
        }

        public override bool Tick(Bot bot, float now)
        {
            if (!bot.TryGetPosition(out var pos)) return false;
            if (now >= _nextRetarget)
            {
                _nextRetarget = now + RetargetInterval;
                _targetId = LineManager.NearestEnemy(bot.Line.Faction, pos, ChaseRadius);
            }

            var enemy = _targetId >= 0 ? Players.Get(_targetId) : null;
            if (enemy != null && enemy.Alive && enemy.TryGetPosition(out var enemyPos))
            {
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
                SetStriking(bot, true);
                return false;
            }
            SetStriking(bot, false);
            bot.Steer(_point, bot.SlotYaw, true, 1.5f);
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
        }
    }

    /// <summary>Aller à un point (dispersion) puis s'arrêter.</summary>
    public class GoToPointTask : BotTask
    {
        private readonly Vector3 _point;
        private readonly bool _run;

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
        private readonly float _delay;
        private float _at;

        public FireTask(float delay)
        {
            _delay = delay;
        }

        private const float ShakenDelay = 2f;
        private const float ShakenPitch = 0.3f; // environ 9° au pire (pitch 2 = 60°)

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
            var shake = Morale.Shake(bot.Line);
            bot.SetPitch(shake > 0f ? Random.Range(-ShakenPitch, ShakenPitch) * shake : 0f);
            bot.Act(GameIds.PlayerActions.FireFirearm);
            bot.Loaded = false;
            return true;
        }
    }

    /// <summary>Recharger (l'arme est baissée d'abord).</summary>
    public class ReloadTask : BotTask
    {
        public const float ReloadTime = 10f; // [TEST] durée d'un rechargement de mousquet
        private float _done;

        protected override void OnStart(Bot bot, float now)
        {
            if (bot.Aiming)
            {
                bot.Act(GameIds.PlayerActions.StopAimingFirearm);
                bot.Aiming = false;
            }
            bot.Act(GameIds.PlayerActions.StartReloadFirearm);
            _done = now + ReloadTime;
        }

        public override bool Tick(Bot bot, float now)
        {
            if (now < _done) return false;
            // [TEST] : le jeu termine-t-il seul le rechargement, ou faut-il FinishReloadFirearm ?
            bot.Loaded = true;
            return true;
        }
    }

    /// <summary>Baisser l'arme.</summary>
    public class StopAimTask : BotTask
    {
        public override bool Tick(Bot bot, float now)
        {
            if (bot.Aiming)
            {
                bot.Act(GameIds.PlayerActions.StopAimingFirearm);
                bot.Aiming = false;
            }
            return true;
        }
    }

    /// <summary>S'accroupir ou se relever.</summary>
    public class CrouchTask : BotTask
    {
        private readonly bool _down;

        public CrouchTask(bool down)
        {
            _down = down;
        }

        public override bool Tick(Bot bot, float now)
        {
            if (bot.Crouched == _down) return true;
            bot.Act(_down ? GameIds.PlayerActions.StartCrouching : GameIds.PlayerActions.StopCrouching);
            bot.Crouched = _down;
            return true;
        }
    }

    /// <summary>Mettre ou retirer la baïonnette (arme baissée).</summary>
    public class BayonetTask : BotTask
    {
        public const float BayonetTime = 2.5f; // [TEST]
        private readonly bool _fix;
        private float _done;

        public BayonetTask(bool fix)
        {
            _fix = fix;
        }

        protected override void OnStart(Bot bot, float now)
        {
            if (bot.Bayonet == _fix)
            {
                _done = now;
                return;
            }
            if (bot.Aiming)
            {
                bot.Act(GameIds.PlayerActions.StopAimingFirearm);
                bot.Aiming = false;
            }
            bot.Act(_fix ? GameIds.PlayerActions.StartBayonetAttach : GameIds.PlayerActions.StartBayonetDetach);
            _done = now + BayonetTime;
        }

        public override bool Tick(Bot bot, float now)
        {
            if (now < _done) return false;
            bot.Bayonet = _fix;
            return true;
        }
    }
}
