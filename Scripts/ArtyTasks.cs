using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Tâches des servants de pièce : une par rôle (chargeur, pourvoyeur, aide au déplacement, sapeur, sans rôle). Le rôle
    /// est donné par la batterie (Battery.Refresh) ; quand il change, le servant change de tâche. Aucun servant ne touche au
    /// canon tant qu'un joueur le tient en main (un début d'action d'un bot le lui retirerait).
    /// </summary>
    public static class ArtyTasks
    {
        public static BotTask Create(CrewRole role)
        {
            switch (role)
            {
                case CrewRole.Loader: return new LoaderTask();
                case CrewRole.Purveyor: return new PurveyorTask();
                case CrewRole.Assistant: return new AssistTask();
                case CrewRole.Sapper: return new SapperTask();
                default: return new CrewPostTask();
            }
        }
    }

    public abstract class CrewTask : BotTask
    {
        protected const float ConfirmWait = 2.5f;
        // Distance au centre du canon : on agit sans chercher plus près ; et au plus loin si le bot est coincé contre l'affût.
        protected const float NearCannon = 3.3f;
        protected const float MaxReach = 3.5f;
        protected const float NearBox = 1.6f;

        protected abstract CrewRole Role { get; }

        public override bool Tick(Bot bot, float now)
        {
            var b = Battery.Of(bot.Line);
            if (b == null || b.Cannon == null || !bot.TryGetPosition(out var pos)) { bot.Stop(); return false; }
            // Canon à une position aberrante (vu à l'essai z60 après une mauvaise commande de pointage) : personne n'y court.
            var cp = b.CannonPos;
            if (Mathf.Abs(cp.x) > 100000f || Mathf.Abs(cp.y) > 100000f || Mathf.Abs(cp.z) > 100000f) { bot.Stop(); return false; }
            b.Refresh(bot.Line, now);
            var role = b.RoleOf(bot);
            if (role != Role)
            {
                bot.SetMovement(ArtyTasks.Create(role), now);
                return false;
            }
            Work(bot, b, pos, now);
            return false;
        }

        protected abstract void Work(Bot bot, Battery b, Vector3 pos, float now);

        /// <summary>Dernier point visé par le servant (diagnostic LineManager : sa place n'est pas celle de la ligne).</summary>
        public Vector3 Goal;
        public bool HasGoal;

        protected void ToPost(Bot bot, Battery b) => Go(bot, b.Post(Role, bot.Line.Bots.IndexOf(bot)), b.FaceYaw);

        protected void Go(Bot bot, Vector3 at, float yaw)
        {
            Goal = at; HasGoal = true;
            bot.Steer(at, yaw, false, 1.2f);
        }

        /// <summary>
        /// Marche vers le canon. Vrai quand il est à portée (NearCannon m du centre). Le but est un point à 2,9 m du centre,
        /// du côté d'où vient le servant, et non le canon lui-même : vu en jeu, un but dans l'affût bloque le bot à 2,8 m et
        /// déclenche les détours d'obstacle de l'infanterie.
        /// </summary>
        protected static bool ReachCannon(Bot bot, Battery b, Vector3 pos) => Reach(bot, b.CannonPos, pos, NearCannon, 2.9f);

        /// <summary>Marche vers la caisse : but à 1,2 m d'elle, du côté d'où vient le servant. Vrai à moins de NearBox m.</summary>
        protected static bool ReachBox(Bot bot, Battery b, Vector3 pos) => Reach(bot, b.BoxPos, pos, NearBox, 1.2f);

        private static bool Reach(Bot bot, Vector3 target, Vector3 pos, float near, float stand)
        {
            var dir = Line.Flat(target - pos);
            var d = dir.magnitude;
            if (d <= near) return true;
            var spot = target - dir / d * stand;
            spot.y = pos.y;
            bot.Steer(spot, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, true, near - stand - 0.1f);
            return false;
        }
    }

    /// <summary>Servant sans rôle (au-delà de trois canonniers) : à son poste derrière la pièce.</summary>
    public class CrewPostTask : CrewTask
    {
        public override string Name => "servant";
        protected override CrewRole Role => CrewRole.None;
        protected override void Work(Bot bot, Battery b, Vector3 pos, float now) => ToPost(bot, b);
    }

    /// <summary>
    /// Sapeur de la pièce : quand le canon est posé depuis 10 s, deux digues d'artillerie (EarthworksArtillery, bêche en
    /// main : seule forme vue acceptée à l'essai z61) devant la pièce, une de chaque côté de la ligne de tir. Une seule
    /// fois par position : rien n'est refait tant que la pièce reste à moins de DigMove m d'une position déjà retranchée,
    /// et au plus MaxPositions positions par pièce et par manche. Un envoi par digue (le jeu répond « ok » même s'il
    /// refuse : seule l'annonce de la pose compte). Ensuite il reprend sa première arme et retourne à son poste.
    /// </summary>
    public class SapperTask : CrewTask
    {
        public override string Name => "sapeur";
        protected override CrewRole Role => CrewRole.Sapper;

        private const float DigMove = 15f;
        private const int MaxPositions = 6;
        private const string Tool = "DiggingSpade";
        private const string Kind = "EarthworksArtillery";

        protected override void Work(Bot bot, Battery b, Vector3 pos, float now)
        {
            if (b.DigIndex < 2 && b.Moving) { Finish(bot, b, "pièce déplacée, digues interrompues"); }
            if (b.DigIndex >= 2)
            {
                if (ArtyRules.SapperEnabled && !b.Moving && now - b.SetDownAt >= 10f && b.DugAt.Count < MaxPositions && !Dug(b))
                {
                    b.DugAt.Add(b.CannonPos);
                    b.DigIndex = 0; b.DigStep = 0; b.DigAt = now; b.DigPlaced = 0; b.DigTries = 0;
                    if (b.SapperWeapon == null) b.SapperWeapon = bot.Weapon ?? bot.FirstWeapon;
                }
                else { ToPost(bot, b); return; }
            }
            var spot = Spot(b, b.DigIndex);
            var stand = spot - b.Forward * 1.8f;
            switch (b.DigStep)
            {
                case 0: // aller derrière l'emplacement de la digue
                    if (Line.Flat(stand - pos).magnitude > 1.5f) { Go(bot, stand, b.FaceYaw); return; }
                    bot.Stop();
                    if (bot.Weapon != Tool) BotCommands.SwitchWeapon(bot.PlayerId, Tool);
                    b.DigStep = 1; b.DigAt = now;
                    return;
                case 1: // bêche en main (ou 3 s passées), puis construction
                    if (bot.Weapon != Tool && now - b.DigAt < 3f) return;
                    if (Ground.TryFind(spot, b.CannonPos.y, out var ground)) spot = ground;
                    b.DigPlacedBefore = ArtyProbe.Placed;
                    var cmd = "carbonPlayers sapperBuildItem " + ServerConsole.Id(bot.PlayerId) + " " + Kind + " "
                              + ServerConsole.Num(spot.x) + "," + ServerConsole.Num(spot.y) + "," + ServerConsole.Num(spot.z)
                              + " 0," + ServerConsole.Num(b.FaceYaw) + ",0 -1 0";
                    ServerConsole.Execute(cmd, "Sapeur", false, out _);
                    b.DigStep = 2; b.DigAt = now;
                    return;
                default: // la pose est annoncée par OnEmplacementPlaced
                    if (ArtyProbe.Placed > b.DigPlacedBefore) b.DigPlaced++;
                    else if (now - b.DigAt < 2f) return;
                    b.DigIndex++; b.DigStep = 0; b.DigAt = now;
                    if (b.DigIndex >= 2) Finish(bot, b, b.DigPlaced + " digue(s) posée(s) sur 2");
                    return;
            }
        }

        private static Vector3 Spot(Battery b, int i) => b.CannonPos + b.Forward * 3.5f + b.Right * (i == 0 ? 2.5f : -2.5f);

        private static bool Dug(Battery b)
        {
            foreach (var p in b.DugAt) if (Line.Flat(p - b.CannonPos).magnitude < DigMove) return true;
            return false;
        }

        private static void Finish(Bot bot, Battery b, string what)
        {
            b.DigIndex = 2; b.DigStep = 0;
            CmdLog.Info("[ARTY] Sapeur " + bot.Name + " : " + what + ".");
            if (b.SapperWeapon != null && b.SapperWeapon != Tool) BotCommands.SwitchWeapon(bot.PlayerId, b.SapperWeapon);
        }
    }

    /// <summary>
    /// Chargeur : prend un boulet à la caisse (visible en main), va au canon, charge puis refoule, avec les durées du jeu
    /// (CMD:arty_charge_duree, CMD:arty_refoule_duree) ; chaque étape est confirmée par l'événement du jeu. Sans caisse sur
    /// la carte, le boulet lui est donné directement près du canon.
    /// Étapes (Battery.LoadStep) : 0 aller à la caisse et prendre le boulet, 1 boulet confirmé, 2 aller au canon et commencer
    /// à charger, 3 fin du chargement, 4 chargement confirmé et début du refoulement, 5 fin du refoulement, 6 confirmé.
    /// </summary>
    public class LoaderTask : CrewTask
    {
        public override string Name => "chargeur";
        protected override CrewRole Role => CrewRole.Loader;

        private const int MaxTries = 3;

        public override void Stop(Bot bot) => bot.CarryingBox = false;

        protected override void Work(Bot bot, Battery b, Vector3 pos, float now)
        {
            // Rien à commencer : déjà prêt à tirer, pièce tenue ou en route, caisse en main du pourvoyeur ou en chemin,
            // pause après des refus.
            if (b.LoadStep == 0 && (b.Tube == TubeState.Rammed || b.Manned || b.Moving || b.BoxInHand || b.PointStep != 0
                                    || (b.Purveyor != null && b.BoxMustMove(now)) || now < b.PauseUntil))
            {
                ToPost(bot, b);
                return;
            }
            // Pièce tenue ou déplacée pendant le service : le chargeur attend sur place, boulet en main s'il l'a déjà.
            if (b.Manned || b.Moving)
            {
                if (b.LoadStep > 2) b.LoadStep = 2;
                bot.Stop();
                return;
            }
            switch (b.LoadStep)
            {
                case 0: // à la caisse (ou près du canon s'il n'y en a pas) : boulet en main
                {
                    if (b.HasBox)
                    {
                        if (!ReachBox(bot, b, pos)) return;
                    }
                    else
                    {
                        if (!b.NoBoxTold)
                        {
                            b.NoBoxTold = true;
                            CmdLog.Info("[ARTY] Canon " + b.CannonId + " : aucune caisse de munitions à portée, boulets donnés directement.");
                            ServerConsole.Private(b.OwnerId, "Aucune caisse de munitions près de votre pièce : vos servants reçoivent les boulets directement.");
                        }
                        if (Line.Flat(b.CannonPos - pos).magnitude > MaxReach + 1f && !ReachCannon(bot, b, pos)) return;
                    }
                    bot.Stop();
                    // Sans id d'objet : rien n'est ramassé sur la carte, le boulet apparaît en main (visible, exigé par le chargement).
                    bot.CarryingBox = true;
                    BotCommands.CarryItem(bot.PlayerId, b.ShotCarry, -1);
                    b.LoadStep = 1;
                    b.LoadAt = now;
                    return;
                }
                case 1:
                    if (b.Carried(bot, b.LoadAt))
                    {
                        b.LoadStep = 2;
                        b.BestDist = float.MaxValue;
                        b.BestAt = now;
                        return;
                    }
                    if (now - b.LoadAt > ConfirmWait) Refused(bot, b, "boulet non reçu en main", now);
                    return;
                case 2: // au canon, début du chargement
                {
                    // Vu en jeu : coincé à 2,8 m du centre. Assez près, ou plus de progrès depuis 2 s à moins de MaxReach m.
                    var d = Line.Flat(b.CannonPos - pos).magnitude;
                    if (d < b.BestDist - 0.3f) { b.BestDist = d; b.BestAt = now; }
                    var stuck = now - b.BestAt > 2f && d <= MaxReach;
                    if (!stuck && !ReachCannon(bot, b, pos)) return;
                    bot.Stop();
                    Battery.Interact(bot, b.CannonId, Battery.Start, Battery.TrLoad);
                    b.LoadStep = 3;
                    b.LoadAt = now;
                    return;
                }
                case 3:
                    if (now - b.LoadAt < ArtyRules.LoadTime) return;
                    Battery.Interact(bot, b.CannonId, Battery.End, Battery.TrLoad);
                    b.LoadStep = 4;
                    b.LoadAt = now;
                    return;
                case 4:
                    if (b.Confirmed(bot, b.CannonId, Battery.TrLoad, b.LoadAt - ArtyRules.LoadTime - 0.1f) && b.Tube == TubeState.Loaded)
                    {
                        Battery.Interact(bot, b.CannonId, Battery.Start, Battery.TrRam);
                        b.LoadStep = 5;
                        b.LoadAt = now;
                        b.LoadTries = 0;
                        return;
                    }
                    if (now - b.LoadAt > ConfirmWait) Refused(bot, b, "chargement refusé par le canon", now);
                    return;
                case 5:
                    if (now - b.LoadAt < ArtyRules.RamTime) return;
                    Battery.Interact(bot, b.CannonId, Battery.End, Battery.TrRam);
                    b.LoadStep = 6;
                    b.LoadAt = now;
                    return;
                default:
                    if (b.Tube == TubeState.Rammed)
                    {
                        CmdLog.Info("[ARTY] " + bot + " : canon " + b.CannonId + " chargé et refoulé.");
                        b.LoadStep = 0;
                        b.LoadTries = 0;
                        bot.CarryingBox = false;
                        return;
                    }
                    if (now - b.LoadAt > ConfirmWait) Refused(bot, b, "refoulement refusé par le canon", now);
                    return;
            }
        }

        private static void Refused(Bot bot, Battery b, string what, float now)
        {
            b.LoadStep = 0;
            bot.CarryingBox = false;
            if (++b.LoadTries < MaxTries) return;
            b.LoadTries = 0;
            b.PauseUntil = now + 15f;
            CmdLog.Warn("[ARTY] " + bot + " : " + what + " (" + MaxTries + " essais), pause 15 s.");
        }
    }

    /// <summary>
    /// Pourvoyeur : dès que le canon est posé à plus de Battery.BoxFar m de la caisse, il va la prendre (visible en main),
    /// la porte et la pose à Battery.BoxDrop m du canon. Si la pièce repart pendant le transport, il la suit caisse en main.
    /// Étapes (Battery.CarryStep) : 0 au poste, 1 aller à la caisse et la prendre, 2 prise confirmée, 3 porter et poser,
    /// 4 contrôle de la pose.
    /// Prise : equipCarryableItem AmmoBox + id de la caisse ; pose : reprise de l'arme (comme un joueur qui change d'arme).
    /// Id inconnu (caisse jamais touchée sur cette carte) : sonde, voir Probe.
    /// </summary>
    public class PurveyorTask : CrewTask
    {
        public override string Name => "pourvoyeur";
        protected override CrewRole Role => CrewRole.Purveyor;

        private const float CarryTimeout = 60f;
        // Sonde de l'id : ids essayés par passage, et dernier id essayé.
        private const int ProbePerTick = 12;
        private const int ProbeMax = 400;

        public override void Stop(Bot bot) => bot.CarryingBox = false;

        protected override void Work(Bot bot, Battery b, Vector3 pos, float now)
        {
            if (b.CarryStep == 0)
            {
                // Le chargeur a peut-être la main dans la caisse : on attend qu'il ait son boulet.
                if (!b.BoxMustMove(now) || b.LoadStep == 1)
                {
                    ToPost(bot, b);
                    return;
                }
                b.CarryStep = 1;
                b.CarryStartedAt = now;
                b.CarryTries = 0;
                CmdLog.Info("[ARTY] " + bot + " va chercher la caisse (à "
                            + ServerConsole.Num(Line.Flat(b.BoxPos - b.CannonPos).magnitude) + " m du canon " + b.CannonId + ").");
            }
            if (!b.HasBox)
            {
                GiveUp(bot, b, "caisse disparue");
                return;
            }
            if (now - b.CarryStartedAt > CarryTimeout)
            {
                if (b.CarryStep >= 2) Drop(bot);
                GiveUp(bot, b, "transport trop long");
                return;
            }
            switch (b.CarryStep)
            {
                case 1:
                {
                    if (!ReachBox(bot, b, pos)) return;
                    bot.Stop();
                    if (b.BoxId < 0 && !Probe(bot, b))
                    {
                        if (b.ProbeDone) GiveUp(bot, b, "id de la caisse introuvable");
                        return;
                    }
                    bot.CarryingBox = true;
                    BotCommands.CarryItem(bot.PlayerId, "AmmoBox", b.BoxId);
                    b.CarryStep = 2;
                    b.CarryAt = now;
                    return;
                }
                case 2:
                    if (b.Carried(bot, b.CarryAt))
                    {
                        b.CarryStep = 3;
                        // L'objet de la carte est porté : sa position redevient celle de l'objet.
                        b.BoxSpotKnown = false;
                        return;
                    }
                    if (now - b.CarryAt <= ConfirmWait) return;
                    bot.CarryingBox = false;
                    if (++b.CarryTries >= 2) GiveUp(bot, b, "prise de la caisse refusée par le jeu");
                    else b.CarryStep = 1;
                    return;
                case 3:
                {
                    // Debout un peu plus loin que l'endroit voulu, face au canon : le jeu pose la caisse devant le porteur.
                    var stand = b.DropSpot + b.Right * 0.9f;
                    var faceCannon = b.FaceYaw - 90f;
                    if (b.Moving)
                    {
                        bot.Steer(stand, faceCannon, true, 2f);
                        return;
                    }
                    if (Line.Flat(stand - pos).magnitude > 0.7f) { bot.Steer(stand, faceCannon, false, 0.5f); return; }
                    bot.Stop();
                    bot.SetYaw(faceCannon);
                    Drop(bot);
                    b.CarryStep = 4;
                    b.CarryAt = now;
                    return;
                }
                default:
                {
                    if (now - b.CarryAt < 1.5f) return;
                    bot.CarryingBox = false;
                    b.CarryStep = 0;
                    if (!b.Box.activeInHierarchy)
                    {
                        // Toujours portée : la reprise de l'arme n'a pas posé la caisse.
                        b.CarryGaveUpAt = b.Moves;
                        CmdLog.Warn("[ARTY] " + bot + " : caisse " + b.BoxId + " non posée par le jeu (arme " + bot.Weapon + ").");
                        return;
                    }
                    var d = Line.Flat(b.Box.transform.position - b.CannonPos).magnitude;
                    if (d > Battery.BoxFar)
                    {
                        // L'objet de la carte n'a pas suivi : on retient l'endroit de la pose (devant le porteur).
                        b.BoxSpot = pos - b.Right * 0.9f;
                        b.BoxSpotKnown = true;
                        CmdLog.Warn("[ARTY] " + bot + " : caisse " + b.BoxId + " posée, mais son objet est resté à "
                                    + ServerConsole.Num(d) + " m du canon ; place de la pose retenue.");
                        return;
                    }
                    CmdLog.Info("[ARTY] " + bot + " a posé la caisse " + b.BoxId + " à " + ServerConsole.Num(d) + " m du canon " + b.CannonId + ".");
                    return;
                }
            }
        }

        /// <summary>Reprise de l'arme : le jeu pose la caisse par terre.</summary>
        private static void Drop(Bot bot)
        {
            var weapon = bot.Musket ?? bot.FirstWeapon ?? bot.Weapon;
            if (weapon != null) BotCommands.SwitchWeapon(bot.PlayerId, weapon);
            else CmdLog.Warn("[ARTY] " + bot + " : arme de départ inconnue, caisse non posée.");
        }

        private static void GiveUp(Bot bot, Battery b, string why)
        {
            bot.CarryingBox = false;
            b.CarryStep = 0;
            b.CarryGaveUpAt = b.Moves;
            CmdLog.Warn("[ARTY] " + bot + " : caisse laissée où elle est (" + why + ") ; le chargeur ira y chercher les boulets.");
        }

        /// <summary>
        /// Id de la caisse inconnu : le jeu ne le donne que dans un événement. Debout contre la caisse, le pourvoyeur envoie
        /// « prendre un boulet » (unique 0, sans effet côté serveur sur une caisse) aux ids possibles ; l'événement renvoyé
        /// par le jeu porte l'objet, et Battery.OnInteraction retient l'id quand c'est notre caisse (même objet, donc même
        /// nom et même place). Les ids déjà vus sur autre chose (porte, drapeau, canon) sont sautés. Fait une fois par caisse et par carte (id retenu ensuite).
        /// Vrai quand l'id est connu. Réglage : CMD:arty_caisse_sonde.
        /// </summary>
        private static bool Probe(Bot bot, Battery b)
        {
            if (b.BoxId >= 0) return true;
            if (!ArtyRules.BoxProbe) { b.ProbeDone = true; return false; }
            if (b.ProbeNext == 0) CmdLog.Info("[ARTY] " + bot + " : id de la caisse « " + b.Box.name + " » inconnu, recherche.");
            // Du plus proche de l'id du canon vers le plus loin (+1, -1, +2, -2…) : la caisse d'une batterie a un id voisin de
            // celui de son canon (vu : canon 24 → caisse 31, 51 → 57, 5 → 2), elle est donc trouvée en touchant peu d'objets.
            for (var n = 0; n < ProbePerTick && b.ProbeNext < ProbeMax * 2 && b.BoxId < 0; b.ProbeNext++)
            {
                var step = b.ProbeNext / 2 + 1;
                var id = b.CannonId + (b.ProbeNext % 2 == 0 ? step : -step);
                if (id < 0 || id > ProbeMax || Battery.IsOtherId(id)) continue;
                n++;
                Battery.Interact(bot, id, Battery.OneTime, Battery.TrTakeAmmo);
            }
            if (b.BoxId >= 0) return true;
            if (b.ProbeNext >= ProbeMax * 2) b.ProbeDone = true;
            return false;
        }
    }

    /// <summary>
    /// Aide au déplacement : quand l'artilleur pousse la pièce, le servant la rejoint et prend la deuxième place (unique 8) ;
    /// quand l'artilleur lâche, il lâche aussi (unique 9) et retourne à son poste.
    /// </summary>
    public class AssistTask : CrewTask
    {
        public override string Name => "aide";
        protected override CrewRole Role => CrewRole.Assistant;

        public override void Stop(Bot bot)
        {
            var b = Battery.Of(bot.Line);
            if (b == null || b.Assistant != bot || !bot.Alive) return;
            if (b.PointStep >= 1)
            {
                Battery.Interact(bot, b.CannonId, Battery.End, Battery.TrMan);
                b.PointStep = 0;
            }
            if (!b.AssistOn) return;
            Battery.Interact(bot, b.CannonId, Battery.OneTime, Battery.TrStopAssist);
            b.AssistOn = false;
        }

        protected override void Work(Bot bot, Battery b, Vector3 pos, float now)
        {
            if (b.Moving)
            {
                if (b.PointStep >= 1) Release(bot, b, now, 5f, "elle est déplacée");
                if (b.AssistOn) return; // attaché au canon par le jeu : il pousse avec le joueur
                if (!ReachCannon(bot, b, pos)) return;
                bot.Stop();
                if (now - b.AssistAskedAt > 2f)
                {
                    b.AssistAskedAt = now;
                    Battery.Interact(bot, b.CannonId, Battery.OneTime, Battery.TrAssist);
                }
                return;
            }
            if (b.AssistOn)
            {
                Battery.Interact(bot, b.CannonId, Battery.OneTime, Battery.TrStopAssist);
                b.AssistOn = false;
            }
            if (Point(bot, b, pos, now)) return;
            ToPost(bot, b);
        }

        // ---- Pointeur ----
        // Le même servant pointe et tire quand l'artilleur n'est pas à sa pièce (CMD:arty_tir_bots). Étapes
        // (Battery.PointStep) : 0 rien, 1 pièce prise en main (début 1) à confirmer, 2 visée, 3 feu parti, lâcher.
        //
        // Commande du jeu pour un canon mobile (code du jeu, ServerMoveableCannonInteractableObjectBehaviour) :
        // interactableObjectUpdateMovingCannon <bot> <canon> -1 <lacet affût> <hausse tube> <lacet affût> <x,y,z> <rx,ry,rz>
        // lacet = angle local Y de l'affût, hausse = angle local X du tube (degrés), puis position et rotation du canon,
        // appliquées telles quelles : on renvoie toujours celles du canon lui-même, il ne bouge donc pas.
        // « interactableObjectUpdateCannon » (2 angles) est la forme des canons fixes : sur un canon mobile le jeu lit la
        // position dans des données absentes et envoie la pièce à des coordonnées aberrantes (vu à l'essai z60).
        private const float GunnerNear = 6f;
        private const float AimYawStep = 6f, AimPitchStep = 3f;
        private const int AimMaxTicks = 60;

        private static bool GunnerAway(Battery b)
        {
            var g = Players.Get(b.OwnerId);
            return g == null || !g.Alive || !g.TryGetPosition(out var gp) || Line.Flat(gp - b.CannonPos).magnitude > GunnerNear;
        }

        private static bool Target(Bot bot, Battery b, out Vector3 target)
        {
            target = Vector3.zero;
            var id = LineManager.NearestEnemy(bot.Line.Faction, b.CannonPos, ArtyRules.FireRange);
            var p = id >= 0 ? Players.Get(id) : null;
            if (p == null || !p.TryGetPosition(out target)) return false;
            target += Vector3.up * 1f;
            return Line.Flat(target - b.CannonPos).magnitude >= ArtyRules.FireMinRange;
        }

        /// <summary>Hausse (degrés au-dessus de l'horizontale) pour toucher à d m et h m plus haut ; NaN hors de portée.</summary>
        private static float Elevation(float d, float h)
        {
            const float g = 9.81f;
            var v2 = ArtyRules.ShotSpeed * ArtyRules.ShotSpeed;
            var disc = v2 * v2 - g * (g * d * d + 2f * h * v2);
            if (disc < 0f || d < 1f) return float.NaN;
            return Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * d)) * Mathf.Rad2Deg;
        }

        private static void Release(Bot bot, Battery b, float now, float pause, string why)
        {
            if (b.PointStep >= 1) Battery.Interact(bot, b.CannonId, Battery.End, Battery.TrMan);
            b.PointStep = 0;
            if (pause > 0f) b.PointPauseUntil = now + pause;
            if (why != null) CmdLog.Info("[ARTY] " + bot + " lâche la pièce " + b.CannonId + " : " + why + ".");
        }

        /// <summary>Vrai tant que le servant s'occupe du pointage.</summary>
        private static bool Point(Bot bot, Battery b, Vector3 pos, float now)
        {
            if (b.PointStep == 0)
            {
                if (!ArtyRules.BotFire || !Automod.ArtyOpen || b.Tube != TubeState.Rammed || b.Manned || b.LoadStep != 0
                    || now < b.PointPauseUntil || !GunnerAway(b) || !Target(bot, b, out _)) return false;
                b.FindParts();
                if (!b.PartsFound)
                {
                    b.PointPauseUntil = float.MaxValue;
                    CmdLog.Warn("[ARTY] Canon " + b.CannonId + " : affût ou tube introuvable, pas de tir par les bots sur cette pièce.");
                    return false;
                }
                if (!ReachCannon(bot, b, pos)) return true;
                bot.Stop();
                Battery.Interact(bot, b.CannonId, Battery.Start, Battery.TrMan);
                b.PointStep = 1;
                b.PointAt = now;
                return true;
            }
            // Un joueur prend la pièce, ou l'artilleur revient : le bot la lâche aussitôt.
            if (b.Manned || !GunnerAway(b)) { Release(bot, b, now, 5f, "un joueur la reprend"); return false; }
            if (!ArtyRules.BotFire || !Automod.ArtyOpen) { Release(bot, b, now, 5f, "tir fermé"); return false; }
            switch (b.PointStep)
            {
                case 1:
                    if (b.Confirmed(bot, b.CannonId, Battery.TrMan, b.PointAt))
                    {
                        b.PointStep = 2;
                        b.AimTicks = 0;
                        b.AimStartPos = b.CannonPos;
                        return true;
                    }
                    if (now - b.PointAt > ConfirmWait)
                    {
                        b.PointStep = 0;
                        b.PointPauseUntil = now + 15f;
                        CmdLog.Warn("[ARTY] " + bot + " : prise en main du canon " + b.CannonId + " refusée par le jeu, pause 15 s.");
                    }
                    return true;
                case 2:
                {
                    if (b.Tube != TubeState.Rammed) { Release(bot, b, now, 0f, "tube vide"); return false; }
                    if (!Target(bot, b, out var target)) { Release(bot, b, now, 5f, "plus de cible à portée"); return false; }
                    // Garde : le canon ne doit pas bouger quand on le pointe (position renvoyée telle quelle).
                    if (Line.Flat(b.CannonPos - b.AimStartPos).magnitude > 0.3f)
                    {
                        b.PointPauseUntil = float.MaxValue;
                        Release(bot, b, now, 0f, "le pointage l'a déplacée, tir par les bots arrêté sur cette pièce");
                        return false;
                    }
                    var muzzle = b.Exit != null ? b.Exit : b.Barrel;
                    var dir = muzzle.forward;
                    var to = target - muzzle.position;
                    var yawErr = Mathf.DeltaAngle(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                    var wanted = Elevation(Line.Flat(to).magnitude, to.y);
                    if (float.IsNaN(wanted)) { Release(bot, b, now, 10f, "cible hors de portée du boulet"); return false; }
                    var pitchErr = wanted - Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                    if (Mathf.Abs(yawErr) < 0.6f && Mathf.Abs(pitchErr) < 0.3f)
                    {
                        Battery.Interact(bot, b.CannonId, Battery.OneTime, Battery.TrFire);
                        CmdLog.Info("[ARTY] " + bot + " : feu du canon " + b.CannonId + " à "
                                    + ServerConsole.Num(Line.Flat(to).magnitude) + " m, hausse " + ServerConsole.Num(wanted) + "°.");
                        b.PointStep = 3;
                        b.PointAt = now;
                        return true;
                    }
                    if (++b.AimTicks > AimMaxTicks)
                    {
                        Release(bot, b, now, 20f, "visée impossible (écart lacet " + ServerConsole.Num(yawErr) + "°, hausse " + ServerConsole.Num(pitchErr) + "°)");
                        return false;
                    }
                    // Sens des axes inconnu d'avance : si l'écart a grandi après la correction précédente, on l'inverse.
                    if (b.AimTicks > 1)
                    {
                        if (Mathf.Abs(yawErr) > b.LastYawErr + 0.3f) b.YawSign = -b.YawSign;
                        if (Mathf.Abs(pitchErr) > b.LastPitchErr + 0.2f) b.PitchSign = -b.PitchSign;
                    }
                    b.LastYawErr = Mathf.Abs(yawErr);
                    b.LastPitchErr = Mathf.Abs(pitchErr);
                    // Première passe : valeurs actuelles renvoyées sans changement (contrôle que la pièce ne bouge pas).
                    var first = b.AimTicks == 1;
                    var yaw = b.Carriage.localEulerAngles.y + (first ? 0f : b.YawSign * Mathf.Clamp(yawErr, -AimYawStep, AimYawStep));
                    // Angle X d'Unity : positif vers le bas, d'où le signe moins pour monter.
                    var pitch = Mathf.DeltaAngle(0f, b.Barrel.localEulerAngles.x)
                                - (first ? 0f : b.PitchSign * Mathf.Clamp(pitchErr, -AimPitchStep, AimPitchStep));
                    var cp = b.Cannon.transform.position;
                    var cr = b.Cannon.transform.eulerAngles;
                    ServerConsole.Execute("carbonPlayers interactableObjectUpdateMovingCannon " + ServerConsole.Id(bot.PlayerId) + " " + b.CannonId
                                          + " -1 " + ServerConsole.Num(yaw) + " " + ServerConsole.Num(pitch) + " " + ServerConsole.Num(yaw) + " "
                                          + Vec(cp) + " " + Vec(cr), "Pointage", false);
                    return true;
                }
                default:
                    if (now - b.PointAt < 0.8f) return true;
                    if (b.Tube == TubeState.Rammed)
                    {
                        Release(bot, b, now, 15f, "feu refusé par le jeu");
                        return false;
                    }
                    Release(bot, b, now, 0f, null);
                    return false;
            }
        }

        // Trois décimales : la position renvoyée doit être celle du canon, sans le décaler.
        private static string Vec(Vector3 v) =>
            v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ","
            + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ","
            + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
