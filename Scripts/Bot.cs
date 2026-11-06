using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>Rôle d'un bot dans la ligne.</summary>
    public enum BotRole { Line, FlagBearer, Drummer, Officer, Surgeon }

    /// <summary>
    /// Un bot de la ligne. Il a une tâche de déplacement (tenir sa place, suivre, charger, fuir) et une file d'actions
    /// exécutées l'une après l'autre (viser, tirer, recharger, s'accroupir, baïonnette).
    /// Les commandes ne partent que si la valeur change, pour limiter le nombre de commandes console.
    /// </summary>
    public class Bot
    {
        // Seuils de renvoi des commandes de pilotage : assez larges pour ne pas renvoyer une commande à chaque petit écart.
        private const float AxisStep = 0.15f;
        private const float YawStep = 4f;
        // Hystérésis à l'arrivée : arrêt à arriveRadius, reprise seulement au-delà de arriveRadius + ResumeMargin
        // (0,4 m puis 0,55 m pour une place de formation).
        private const float ResumeMargin = 0.08f;

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
        /// <summary>Relances déjà faites pour cette place (mauvaise classe ou apparition refusée).</summary>
        public int Attempts;
        public bool Alive;
        /// <summary>Dispersé et laissé sur place (CMD:tuerdisperses faux) : plus piloté, retiré en premier si le plafond baisse.</summary>
        public bool Parked;
        /// <summary>Arrivé et immobile (Steer) : piloté moins souvent (voir LineManager.TickBots).</summary>
        public bool Settled;
        /// <summary>Depuis quand le bot est loin de sa place pendant une marche (-1 : non).</summary>
        public float FarSince = -1f;

        // Place dans la formation (calculée par Line) : SlotLocal en files (x) et en rangs (y), SlotPosition dans le monde.
        public Vector2 SlotLocal;
        /// <summary>Face du carré (0 à 3), -1 hors carré.</summary>
        public int SlotFace = -1;
        public int Rank;
        public Vector3 SlotPosition;
        public float SlotYaw;
        private Vector3 _groundSlot;
        private float _nextGroundSlotAt;
        private bool _hasGroundSlot;
        /// <summary>Point au sol, mis en cache ; ne déplace jamais le personnage.</summary>
        public void ProjectSlot(float now)
        {
            // Budget de recherches de sol par image (RaycastAll) : au-delà, la hauteur précédente est gardée et la recherche
            // attend une image suivante (une ligne de 100 hommes en marche ne lance plus 100 rayons dans la même image).
            if ((_nextGroundSlotAt <= 0f || now >= _nextGroundSlotAt || Line.Flat(SlotPosition-_groundSlot).sqrMagnitude > 0.25f)
                && (FrameStats.GroundCasts < FrameStats.GroundBudget || !_hasGroundSlot))
            {
                FrameStats.GroundCasts++;
                _nextGroundSlotAt=now+1f;
                var reference=TryGetPosition(out var pos)?pos.y:SlotPosition.y;
                _hasGroundSlot=Ground.TryFind(SlotPosition,reference,out _groundSlot);
            }
            if (_hasGroundSlot) SlotPosition=new Vector3(SlotPosition.x,_groundSlot.y,SlotPosition.z);
        }

        public bool Successor;
        public float SpawnAfter;
        // Décalage propre au bot quand il suit l'officier sans formation (rompre et suivre).
        public Vector3 FollowOffset;

        // État de l'arme et de la posture, tel que le mod l'a commandé.
        public bool Loaded = true;
        public bool Aiming;
        public bool Crouched;
        public bool WaitForStanding;
        public float SlotLateSince=-1f;
        public bool Bayonet;
        /// <summary>Pose ou retrait de la baïonnette en cours : aucune action qui l'interromprait (garde, frappe, parade).</summary>
        public bool BayonetBusy;
        /// <summary>Instrument tenu (GameIds.InstrumentType), lu par OnPlayerStartCarry ; tambour par défaut.</summary>
        public int Instrument = GameIds.InstrumentType.MarchingDrum;
        /// <summary>Instrument en main confirmé par le jeu (OnPlayerStartCarry) ; avant, aucune demande de morceau.</summary>
        public bool InstrumentConfirmed;
        public bool MusicPlaying;
        public float NextMusicRequest;
        public float EquipRequestedAt = -1f;
        public int EquipAttempts;
        /// <summary>Rechargement en cours (diagnostic).</summary>
        public bool Reloading;

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
            Settled = false;
            GaveUp = false;
            _blockCheckAt = -1f;
            _detours = 0;
            _detourUntil = 0f;
            _trafficSince = -1f;
            _catchingUp = false;
            Movement?.Stop(this);
            Movement = task;
            Movement?.Start(this, now);
        }

        public void Enqueue(BotTask task)
        {
            Actions.Add(task);
        }

        /// <summary>
        /// Vide la file d'actions (cessez-le-feu, nouvel ordre qui annule le précédent). keepReload : un rechargement ou une
        /// pose / un retrait de baïonnette déjà commencés sont gardés (interrompus, ils laisseraient l'arme dans un état faux).
        /// </summary>
        public void ClearActions(bool keepReload = false, bool interruptReload = false)
        {
            WaitForStanding=false;
            BotTask kept = null;
            if (keepReload && Actions.Count > 0 && ((Actions[0] is ReloadTask && !interruptReload) || Actions[0] is BayonetTask) && Actions[0].Started) kept = Actions[0];
            var old = Actions.ToArray();
            Actions.Clear();
            foreach (var a in old)
            {
                if (a != kept) a.Stop(this);
            }
            if (kept != null) Actions.Add(kept);
        }

        public void Tick(float now)
        {
            if (!Alive || PlayerId < 0) return;
            Movement?.Tick(this, now);
            if (!Alive) return;
            if (Aiming && Line.HasAimPoint && !Line.Owner.IsAi && Line.Mode != LineMode.Charge)
            {
                // Sans nouveau bruit aléatoire entre les tirs.
                Shooting.PlayerDirection(this, out var aimYaw, out var aimPitch);
                SetYaw(aimYaw);
                SetPitch(aimPitch);
            }
            // Une seule action à la fois : la suivante commence quand la précédente est finie. Le jeu peut rappeler le mod
            // pendant une commande (mort, départ…) et vider la file : on vérifie que l'action est toujours en tête.
            while (Alive && Actions.Count > 0)
            {
                var action = Actions[0];
                if (!action.Started) action.Start(this, now);
                if (!action.Tick(this, now)) break;
                if (Actions.Count > 0 && Actions[0] == action) Actions.RemoveAt(0);
            }
            if (Alive && !Parked) Line.OnBotIdle(this, now);
            FlushActions();
        }

        // ---- Pilotage ------------------------------------------------------------------------

        /// <summary>
        /// Amène le bot vers target. En déplacement normal, il se tourne vers le but ; preserveFacing garde son cap
        /// pour les petits décalages latéraux de formation. Arrivé, il s'arrête face à faceYaw.
        /// </summary>
        public bool Steer(Vector3 target, float faceYaw, bool run, float arriveRadius, bool preserveFacing = false,
            bool ignoreLineTraffic = false)
        {
            if (!TryGetPosition(out var pos)) return false;
            if(!_hasSteerTarget || Line.Flat(target-_steerTarget).sqrMagnitude>0.25f){
                _hasSteerTarget=true;_steerTarget=target;Settled=false;
                _blockCheckAt=-1f;_trafficSince=-1f;_detourUntil=0f;_backUntil=0f;
            }
            var dx = target.x - pos.x;
            var dz = target.z - pos.z;
            var dist = Mathf.Sqrt(dx * dx + dz * dz);
            LastSteerDistance = dist;
            // Pas de côté pour laisser passer un autre bot (demandé par RequestStepAside), puis retour à sa place.
            if (Clock.Now < _stepUntil)
            {
                Settled = false;
                SetRunning(false);
                SetYaw(_stepYaw);
                SetAxis(0f, 0.6f);
                return false;
            }
            // Une place de formation ne peut être validée par un délai ou par
            // la présence d'un voisin : les bots doivent rejoindre leur propre place.
            if (dist <= arriveRadius || (Settled && dist <= arriveRadius + ResumeMargin))
            {
                // Arrivé : arrêt, puis seulement alors face à la direction voulue.
                _blockCheckAt = -1f;
                _detours = 0;
                _catchingUp = false;
                Yielding = false;
                Settled = true;
                GiveUps = 0;
                SetAxis(0f, 0f);
                SetYaw(faceYaw);
                SetRunning(false);
                return true;
            }
            Settled = false;
            var now = Clock.Now;
            var heading = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            var moveDirection = new Vector3(dx, 0f, dz) / dist;
            var strafeRight = Vector3.Dot(moveDirection, Formation.Right(faceYaw));
            var strafeForward = Vector3.Dot(moveDirection, Formation.Forward(faceYaw));
            // Pour un repositionnement principalement latéral, garder la face de la ligne
            // et faire un pas de côté au lieu de pivoter puis courir le long du front.
            var strafing = preserveFacing && Mathf.Abs(strafeRight) > Mathf.Abs(strafeForward) * 1.4f;

            // Blocage (rocher, pente, autre bot) : jamais de téléportation, le bot se débrouille en marchant. Si la distance au but
            // ne baisse pas d'au moins BlockGain m en BlockWindow s, détour court : pivot de 45 à 90° d'un côté (en alternant à chaque
            // essai), un pas en arrière à partir du deuxième essai, puis 1 à 2 s de marche ; puis nouvel essai vers le but. Après
            // MaxDetours échecs, le bot abandonne sa place (Line.OnBotGaveUp : il reste où il est, la ligne se resserre sans lui).
            if (_blockCheckAt < 0f)
            {
                _blockCheckAt = now + BlockWindow;
                _blockCheckDist = dist;
            }
            else if (now >= _blockCheckAt && now >= _detourUntil)
            {
                // En foule (un autre bot à moins de CrowdDistance m), pas de détour : la règle de priorité suffit.
                if (dist > BlockMinDistance && _blockCheckDist - dist < BlockGain
                    && (ignoreLineTraffic || NearestBotDistance(pos) >= CrowdDistance))
                {
                    if (_detours >= MaxDetours && Movement is HoldSlotTask)
                    {
                        _detours = 0;
                        _blockCheckAt = -1f;
                        Line.OnBotGaveUp(this, now);
                        return false;
                    }
                    _detours++;
                    Detours++;
                    if (!FormationNavigation.Detour(this, pos, heading, _detours, out _detourYaw))
                    { SetAxis(0f,0f); _blockCheckAt=now+1f; return false; }
                    _backUntil = _detours >= 2 ? now + 0.6f : now;
                    _detourUntil = _backUntil + 1f;
                    LineManager.NoteDetour(this, dist);
                }
                else if (_blockCheckDist - dist >= BlockGain) _detours = 0;
                _blockCheckAt = now + BlockWindow;
                _blockCheckDist = dist;
            }
            if (now < _detourUntil)
            {
                SetRunning(false);
                var escapeHeading=now<_backUntil?heading+180f:_detourYaw;
                if(!FormationNavigation.TrafficClear(this,pos,escapeHeading,0.5f))
                {
                    _detourUntil=now;
                    _backUntil=now;
                    SetAxis(0f,0f);
                    return false;
                }
                SetYaw(escapeHeading);
                SetAxis(0f,0.45f);
                return false;
            }

            // L'axe est relatif au corps et le cap est absolu [CODE] : on marche toujours droit devant (axe 0,y), le corps
            // tourné vers le but. Grand changement de cap : pivot sur place d'abord (le corps rejoint le cap en quelques
            // FixedUpdate, characterRotationInterpolator 0,6), pour ne pas partir de travers.
            var turn = float.IsNaN(_sentYaw) ? 180f : Mathf.Abs(Mathf.DeltaAngle(_sentYaw, heading));
            SetYaw(strafing ? faceYaw : heading);
            if (!strafing && turn > PivotAngle) _pivotUntil = now + PivotTime;
            if (!strafing && now < _pivotUntil)
            {
                SetAxis(0f, 0f);
                return false;
            }
            // Priorité fixe et passage par la droite. Le voisin en place conserve
            // sa réservation ; le bot en route choisit un cap libre et ralentit.
            Yielding = false;
            var slowForObstacle = false;
            var fwdDir = new Vector3(dx, 0f, dz) / dist;
            if (!ignoreLineTraffic) foreach (var other in Line.Bots)
            {
                if (other == this || !other.Alive || !other.TryGetPosition(out var op)) continue;
                var d = Line.Flat(op - pos);
                var forward = Vector3.Dot(d, fwdDir);
                var clearance = 2f * Formation.BodyRadius + 0.05f;
                var lateral = d - fwdDir * forward;
                if (forward <= 0f || forward > Mathf.Min(dist, 1.6f) || lateral.magnitude >= clearance) continue;
                var otherFirst = Line.Following
                    ? (other.SlotLocal.y < SlotLocal.y || (other.SlotLocal.y == SlotLocal.y && other.PlayerId < PlayerId))
                    : (other.Settled || other.FormPriority < FormPriority || (other.FormPriority == FormPriority && other.PlayerId < PlayerId));
                if (!otherFirst && forward > clearance) continue;
                slowForObstacle = true;
                // Même file en marche : ne pas doubler le rang de devant.
                if (Line.Following && Line.IsMoving && other.SlotLocal.y < SlotLocal.y && Mathf.Abs(SlotLocal.x-other.SlotLocal.x)<0.1f)
                {
                    if (forward < clearance + 0.15f) { Yielding = true; SetAxis(0f,0f); return false; }
                    break;
                }
                if (strafing)
                {
                    Yielding = true;
                    YieldCount++;
                    SetAxis(0f, 0f);
                    return false;
                }
                var passYaw = heading;
                var free = false;
                // Tenter les deux côtés : une aile occupée ne doit pas bloquer
                // définitivement un bot alors que l'autre côté est libre.
                for (var sideIndex=0; sideIndex<2 && !free; sideIndex++)
                {
                    passYaw=heading+(sideIndex==0?55f:-55f);
                    var pass=Formation.Forward(passYaw);
                    free=FormationNavigation.Clear(pos,passYaw,Mathf.Min(dist,1f),this);
                    foreach(var neighbor in Line.Bots)
                    {
                        if(neighbor==this || !neighbor.Alive || !neighbor.TryGetPosition(out var np))continue;
                        var rel=Line.Flat(np-pos);
                        var along=Vector3.Dot(rel,pass);
                        if(along>0f && along<Mathf.Min(dist,clearance+0.5f) && (rel-pass*along).magnitude<clearance){free=false;break;}
                    }
                }
                if(!free){
                    Yielding=true;YieldCount++;SetAxis(0f,0f);
                    if (strafing) return false;
                    if(_trafficSince<0f)_trafficSince=now;
                    if(now-_trafficSince>=0.2f){
                        _trafficSince=now;
                        if(FormationNavigation.Escape(this,pos,heading,out _detourYaw)){
                            _backUntil=now;_detourUntil=now+0.6f;
                        }
                    }
                    return false;
                }
                _trafficSince=-1f;
                heading = passYaw;
                SetYaw(heading);
                break;
            }
            if (!FormationNavigation.Clear(pos, heading, Mathf.Min(1.2f,dist), this))
            {
                if (now >= _detourUntil && FormationNavigation.Detour(this, pos, heading, _detours, out _detourYaw))
                { _backUntil=now; _detourUntil=now+0.7f; }
                SetAxis(0f,0f);
                return false;
            }
            // Allure : course au-delà de RunDistance m de la place (jusqu'à WalkDistance), marche ensuite, ralentissement seulement
            // dans les SlowDistance derniers mètres. En suivant / en colonne, l'allure suit celle de l'officier.
            if (dist > RunDistance) _catchingUp = true;
            else if (dist < WalkDistance) _catchingUp = false;
            var speed = dist >= SlowDistance ? 1f : Mathf.Max(MinSpeed, dist / SlowDistance);
            if (Line.Following && Line.IsMoving && dist < RunDistance)
                speed = Mathf.Clamp(Line.OfficerSpeed / (run ? 3.36f : 1.5f) + (dist - 1f) * 0.15f, MinSpeed, 1f);
            if (slowForObstacle) speed = Mathf.Min(speed, 0.5f);
            if (strafing)
            {
                SetAxis(strafeRight * speed, strafeForward * speed);
                run = false;
            }
            else SetAxis(0f, speed);
            // Jamais de course en rechargeant (le pas de course reprend ensuite).
            SetRunning((run || _catchingUp) && !slowForObstacle && !Reloading);
            return false;
        }

        /// <summary>Distance au bot vivant le plus proche de la même ligne.</summary>
        private float NearestBotDistance(Vector3 pos)
        {
            var best = float.MaxValue;
            foreach (var other in Line.Bots)
            {
                if (other == this || !other.Alive || !other.TryGetPosition(out var op)) continue;
                best = Mathf.Min(best, Line.Flat(op - pos).magnitude);
            }
            return best;
        }

        /// <summary>
        /// Un bot arrivé qui gêne : court pas de côté (perpendiculaire au passage de l'autre), puis il revient seul à sa place.
        /// Au plus une fois toutes les 2 s.
        /// </summary>
        public void RequestStepAside(float side, Vector3 passDir, float now)
        {
            if (now < _stepCooldown || !Alive) return;
            _stepCooldown = now + 2f;
            var perp = new Vector3(passDir.z, 0f, -passDir.x) * side;
            _stepYaw = Mathf.Atan2(perp.x, perp.z) * Mathf.Rad2Deg;
            _stepUntil = now + 0.6f;
            Settled = false;
            NextTick = 0f;
            StepAsideCount++;
        }

        private float _stepUntil;
        private float _stepCooldown;
        private float _stepYaw;
        /// <summary>Pas de côté faits pour laisser passer (diagnostic).</summary>
        public int StepAsideCount;

        /// <summary>Priorité fixe dans la mise en formation (0 = la plus haute, centre de la ligne) : Line.Reassign.</summary>
        public int FormPriority;
        /// <summary>Numéro fixe dans la ligne (ordre d'apparition), qui donne sa place dans toutes les formations.</summary>
        public int Number;
        /// <summary>Cessions dans la mise en formation en cours (remis à zéro par HoldSlotTask).</summary>
        public int FormYields;

        private const float CrowdDistance = 1.5f;
        /// <summary>Nombre de fois où le bot a cédé le passage (diagnostic).</summary>
        public int YieldCount;

        // Pilotage : pivot avant de marcher si le cap change de plus de PivotAngle ; ralentissement près du but.
        private const float PivotAngle = 60f;
        private const float PivotTime = 0.15f;
        private const float SlowDistance = 0.8f;
        private const float MinSpeed = 0.15f;
        // Allure choisie : course jusqu'à 1,5 m de la place, puis marche, ralentie seulement dans les 0,8 derniers mètres. Le bot
        // est piloté toutes les 0,1 s et la commande s'applique au FixedUpdate suivant : à 3,36 m/s (course) il parcourt
        // ~0,35–0,4 m entre deux corrections, autant que le rayon d'arrivée (0,4 m) → dépassements et oscillations ; à
        // 1,5 m/s (marche) ~0,15 m, précis. La marche finale ne dure donc qu'environ 1 s.
        private const float RunDistance = 1.5f;
        private const float WalkDistance = 1.2f;
        private const float BlockWindow = 0.8f;
        private const float BlockGain = 0.5f;
        private const float BlockMinDistance = 1.5f;
        private const int MaxDetours = 4;
        private float _pivotUntil;
        private bool _catchingUp;
        private float _blockCheckAt = -1f;
        private float _blockCheckDist;
        private int _detours;
        private float _detourYaw;
        private float _detourUntil;
        private float _trafficSince = -1f;
        private float _backUntil;
        /// <summary>Détours faits depuis l'apparition (diagnostic).</summary>
        public int Detours;
        /// <summary>A renoncé à sa place après des blocages répétés : reste où il est jusqu'au prochain ordre.</summary>
        public bool GaveUp;
        /// <summary>Renoncements successifs sans avoir rejoint sa place (remis à zéro à l'arrivée).</summary>
        public int GiveUps;
        /// <summary>Porte-drapeau : drapeau (arme BearingFlag) confirmé en main par OnPlayerWeaponSwitch.</summary>
        public bool FlagConfirmed;
        /// <summary>Musicien : mains libres (WeaponType Unarmed) confirmées par OnPlayerWeaponSwitch.</summary>
        public bool UnarmedConfirmed;
        /// <summary>Musicien : morceau demandé (instrumentPlay) à cet instant ; buff Musician reçu depuis (témoin que le jeu joue).</summary>
        public float SongRequestedAt = -1f;
        public bool MusicBuffSeen;
        public int SongFailures;
        /// <summary>Médecin : blessé soigné ou rejoint (-1 : aucun), début de la marche, début du soin.</summary>
        public int HealTarget = -1;
        /// <summary>Fête : prochain geste ou cri.</summary>
        public float NextCelebrate;
        /// <summary>
        /// Sortie de mêlée en attente (instant d'envoi, -1 : aucune). « playerAction None » arrête la boucle de frappes
        /// (ServerCarbonPlayersManager : meleeStrikeAction = None), mais un bot arrêté en phase 0 garde sa direction de garde
        /// et la posture de combat ; à l'image suivante, dans un paquet à part (chaque playerAction remplace le précédent),
        /// StopMeleeWeaponHold + DisableCombatStance. Le bot ne repart qu'après (TickBots).
        /// </summary>
        public float MeleeExitAt = -1f;
        public float HealWalkSince = -1f;
        public float HealStartedAt = -1f;
        public float UnarmedNextTry = -1f;
        public int UnarmedAttempts;
        public float FlagNextTry = -1f;
        public int FlagAttempts;
        /// <summary>Cède le passage à un autre bot (diagnostic).</summary>
        public bool Yielding;
        /// <summary>Retard de départ de la prochaine tâche de place (colonne qui se déroule).</summary>
        public float StartDelay;
        /// <summary>Dernière distance au but calculée par Steer (diagnostic).</summary>
        private Vector3 _steerTarget;
        private bool _hasSteerTarget;
        public float LastSteerDistance;
        /// <summary>Nom affiché (« Soldat 3 »), pour le journal.</summary>
        public string Name = "";

        // Échecs de pilotage d'affilée : le jeu ne connaît plus ce bot (« Updating setting » avec échec = id absent de
        // CarbonPlayers). Au-delà de MaxCommandFailures, le bot est oublié au lieu d'être relancé à chaque passage.
        private const int MaxCommandFailures = 5;
        private int _commandFailures;

        private bool Sent(bool ok)
        {
            if (ok) { _commandFailures = 0; return true; }
            if (++_commandFailures >= MaxCommandFailures && PlayerId >= 0)
            {
                CmdLog.Info(Name + " #" + PlayerId + " inconnu du jeu après " + MaxCommandFailures + " commandes refusées : oublié.");
                _commandFailures = 0;
                LineManager.ForgetMissing(PlayerId);
            }
            return false;
        }

        public void Stop()
        {
            SetAxis(0f, 0f);
            SetRunning(false);
        }

        public void SetAxis(float x, float y)
        {
            x = Mathf.Clamp(Quantize(x), -1f, 1f);
            y = Mathf.Clamp(Quantize(y), -1f, 1f);
            if (x == _sentX && y == _sentY) return;
            if (Sent(BotCommands.Move(PlayerId, x, y)))
            {
                _sentX = x;
                _sentY = y;
            }
        }

        public void SetYaw(float yaw)
        {
            yaw = Mathf.Repeat(yaw, 360f);
            if (!float.IsNaN(_sentYaw) && Mathf.Abs(Mathf.DeltaAngle(_sentYaw, yaw)) < YawStep) return;
            if (Sent(BotCommands.Rotate(PlayerId, yaw))) _sentYaw = yaw;
        }

        /// <summary>Inclinaison du tir (voir BotCommands.Pitch) ; 0 après TakeControl.</summary>
        public void SetPitch(float pitch)
        {
            if (Mathf.Abs(pitch - _sentPitch) < 0.02f) return;
            if (Sent(BotCommands.Pitch(PlayerId, pitch))) _sentPitch = pitch;
        }

        public void SetRunning(bool running)
        {
            var value = running ? 1 : 0;
            if (value == _sentRunning) return;
            if (Sent(BotCommands.SetRunning(PlayerId, running))) _sentRunning = value;
        }

        /// <summary>
        /// Action de joueur : mise en attente, puis envoyée en UNE commande à la fin du passage du bot (FlushActions). Deux
        /// commandes playerAction avant le FixedUpdate suivant s'écrasent [CODE, ServerCarbonPlayersManager.SetPlayerActions].
        /// </summary>
        public void Act(int playerAction)
        {
            if (!_pendingActions.Contains(playerAction)) _pendingActions.Add(playerAction);
        }

        public void FlushActions()
        {
            if (_pendingActions.Count == 0 || PlayerId < 0) return;
            BotCommands.Actions(PlayerId, _pendingActions);
            _pendingActions.Clear();
        }

        private readonly List<int> _pendingActions = new List<int>();

        /// <summary>Nom de la tâche de déplacement et de l'action en cours (diagnostic).</summary>
        public string TaskSummary()
        {
            var move = Movement != null ? Movement.Name : "aucune";
            return Actions.Count > 0 ? move + "+" + Actions[0].Name : move;
        }

        /// <summary>Cap envoyé au jeu (NaN si aucun), pour le diagnostic.</summary>
        public float SentYaw => _sentYaw;

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
            // Noms écrits à la main : Enum.ToString passe par la réflexion.
            return "bot #" + PlayerId + " (" + RoleName(Role) + ", " + GameIds.ClassName(Class) + ")";
        }

        public static string RoleName(BotRole role)
        {
            switch (role)
            {
                case BotRole.FlagBearer: return "porte-drapeau";
                case BotRole.Drummer: return "tambour";
                case BotRole.Officer: return "officier";
                case BotRole.Surgeon: return "médecin";
                default: return "soldat";
            }
        }
    }
}
