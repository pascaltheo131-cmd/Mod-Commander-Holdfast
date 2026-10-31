using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>Rôle d'un bot dans la ligne.</summary>
    public enum BotRole { Line, FlagBearer, Drummer }

    /// <summary>
    /// Un bot de la ligne. Il a une tâche de déplacement (tenir sa place, suivre, charger, fuir) et une file d'actions
    /// exécutées l'une après l'autre (viser, tirer, recharger, s'accroupir, baïonnette).
    /// Les commandes ne partent que si la valeur change, pour limiter le nombre de commandes console.
    /// </summary>
    public class Bot
    {
        // Seuils de renvoi des commandes de pilotage.
        private const float AxisStep = 0.1f;
        private const float YawStep = 3f;

        // Change seulement quand une ligne est partagée (reprise par un officier au-delà de son plafond).
        public Line Line;
        public readonly BotRole Role;
        public readonly int Class;
        public readonly int Uniform;

        /// <summary>Identifiant de joueur du bot ; -1 tant que le jeu ne l'a pas fait apparaître.</summary>
        public int PlayerId = -1;
        public float RequestedAt;
        /// <summary>Renfort : placé loin derrière l'officier à l'apparition, puis rejoint sa place à pied.</summary>
        public bool Reinforcement;
        public bool Alive;

        // Place dans la formation (calculée par Line) : SlotLocal en files (x) et en rangs (y), SlotPosition dans le monde.
        public Vector2 SlotLocal;
        /// <summary>Face du carré (0 à 3), -1 hors carré.</summary>
        public int SlotFace = -1;
        public int Rank;
        public Vector3 SlotPosition;
        public float SlotYaw;
        // Décalage propre au bot quand il suit l'officier sans formation (rompre et suivre).
        public Vector3 FollowOffset;

        // État de l'arme et de la posture, tel que le mod l'a commandé.
        public bool Loaded = true;
        public bool Aiming;
        public bool Crouched;
        public bool Bayonet;

        public BotTask Movement;
        public readonly List<BotTask> Actions = new List<BotTask>();
        public float NextTick;

        // Dernières valeurs envoyées (NaN = jamais).
        private float _sentX = float.NaN, _sentY = float.NaN, _sentYaw = float.NaN;
        private int _sentRunning = -1;
        private float _sentPitch;

        public Bot(Line line, BotRole role, int playerClass, int uniform)
        {
            Line = line;
            Role = role;
            Class = playerClass;
            Uniform = uniform;
        }

        public bool IsLineSoldier => Role == BotRole.Line;

        public PlayerInfo Info => PlayerId >= 0 ? Players.Get(PlayerId) : null;

        public bool TryGetPosition(out Vector3 position)
        {
            var p = Info;
            if (p != null) return p.TryGetPosition(out position);
            position = Vector3.zero;
            return false;
        }

        // ---- Tâches ---------------------------------------------------------------------------

        public void SetMovement(BotTask task, float now)
        {
            Movement?.Stop(this);
            Movement = task;
            Movement?.Start(this, now);
        }

        public void Enqueue(BotTask task)
        {
            Actions.Add(task);
        }

        /// <summary>Vide la file d'actions (cessez-le-feu, nouvel ordre qui annule le précédent).</summary>
        public void ClearActions()
        {
            foreach (var a in Actions) a.Stop(this);
            Actions.Clear();
        }

        public void Tick(float now)
        {
            if (!Alive || PlayerId < 0) return;
            Movement?.Tick(this, now);
            // Une seule action à la fois : la suivante commence quand la précédente est finie.
            while (Actions.Count > 0)
            {
                var action = Actions[0];
                if (!action.Started) action.Start(this, now);
                if (!action.Tick(this, now)) break;
                Actions.RemoveAt(0);
            }
            Line.OnBotIdle(this, now);
        }

        // ---- Pilotage ------------------------------------------------------------------------

        /// <summary>
        /// Amène le bot vers target. Loin : il se tourne vers le but et avance. Près : il fait face à faceYaw et
        /// rectifie en pas de côté. Arrivé (à arriveRadius près) : il s'arrête face à faceYaw. Renvoie vrai à l'arrivée.
        /// </summary>
        public bool Steer(Vector3 target, float faceYaw, bool run, float arriveRadius)
        {
            if (!TryGetPosition(out var pos)) return false;
            var dx = target.x - pos.x;
            var dz = target.z - pos.z;
            var dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist <= arriveRadius)
            {
                SetAxis(0f, 0f);
                SetYaw(faceYaw);
                SetRunning(false);
                return true;
            }
            if (dist > 3f)
            {
                SetYaw(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg);
                SetAxis(0f, 1f);
                // Rattrapage en courant quand le bot est loin derrière.
                SetRunning(run || dist > 20f);
                return false;
            }
            // Dans le repère du bot orienté à faceYaw : droite = (cos, -sin), avant = (sin, cos).
            var rad = faceYaw * Mathf.Deg2Rad;
            var localX = dx * Mathf.Cos(rad) - dz * Mathf.Sin(rad);
            var localY = dx * Mathf.Sin(rad) + dz * Mathf.Cos(rad);
            var scale = Mathf.Min(1f, dist) / dist;
            SetYaw(faceYaw);
            SetAxis(localX * scale, localY * scale);
            SetRunning(false);
            return false;
        }

        public void Stop()
        {
            SetAxis(0f, 0f);
            SetRunning(false);
        }

        public void SetAxis(float x, float y)
        {
            x = Quantize(Mathf.Clamp(x, -1f, 1f));
            y = Quantize(Mathf.Clamp(y, -1f, 1f));
            if (x == _sentX && y == _sentY) return;
            if (BotCommands.Move(PlayerId, x, y))
            {
                _sentX = x;
                _sentY = y;
            }
        }

        public void SetYaw(float yaw)
        {
            yaw = Mathf.Repeat(yaw, 360f);
            if (!float.IsNaN(_sentYaw) && Mathf.Abs(Mathf.DeltaAngle(_sentYaw, yaw)) < YawStep) return;
            if (BotCommands.Rotate(PlayerId, yaw)) _sentYaw = yaw;
        }

        /// <summary>Inclinaison du tir (voir BotCommands.Pitch) ; 0 après TakeControl.</summary>
        public void SetPitch(float pitch)
        {
            if (Mathf.Abs(pitch - _sentPitch) < 0.02f) return;
            if (BotCommands.Pitch(PlayerId, pitch)) _sentPitch = pitch;
        }

        public void SetRunning(bool running)
        {
            var value = running ? 1 : 0;
            if (value == _sentRunning) return;
            if (BotCommands.SetRunning(PlayerId, running)) _sentRunning = value;
        }

        public void Act(int playerAction)
        {
            BotCommands.Action(PlayerId, playerAction);
        }

        public void Shout(int voicePhrase)
        {
            BotCommands.Shout(PlayerId, voicePhrase);
        }

        /// <summary>Oublie les valeurs envoyées : tout sera renvoyé (nouveau bot, réapparition).</summary>
        public void ResetSent()
        {
            _sentX = _sentY = _sentYaw = float.NaN;
            _sentRunning = -1;
            _sentPitch = 0f;
        }

        private static float Quantize(float v)
        {
            return Mathf.Round(v / AxisStep) * AxisStep;
        }

        public override string ToString()
        {
            return "bot #" + PlayerId + " (" + Role + ", " + GameIds.ClassName(Class) + ")";
        }
    }
}
