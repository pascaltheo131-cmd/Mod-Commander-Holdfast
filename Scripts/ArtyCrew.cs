using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Artillerie, étape 2 : servants de pièce. Un joueur artilleur (Cannoneer) qui prend un canon reçoit CMD:arty_servants
    /// bots Cannoneer de son camp. Le joueur vise et tire ; un servant à la fois recharge quand le joueur a lâché le canon :
    /// boulet pris dans la caisse, mis dans le canon, refoulé. Les autres attendent à leur poste.
    ///
    /// Suite relevée en jeu (journal [ARTY], 2026-11-08), identique canon et obusier, activation 1 unique, 2 début, 4 fin :
    /// caisse « unique 0 » = prendre un boulet ; canon début/fin 3 = charger ; début/fin 5 = refouler ; début 1 = prendre
    /// en main (visée), unique 7 = feu, fin 1 = lâcher ; début/fin 2 = déplacer. Pas d'écouvillon.
    /// La caisse n'a pas d'id connu tant que personne ne l'a touchée : elle est repérée au premier événement sur une caisse à
    /// moins de AmmoSearch m du canon (le chargement en produit un, « unique 2 » du jeu).
    ///
    /// Vu dans le code du jeu (décompilé) :
    /// - « unique 0 » sur la caisse ne donne rien côté serveur (ServerAmmoBoxInteractableObjectBehaviour._ExecuteInteraction
    ///   est vide) : un joueur prend son boulet par une demande de port du client (ClientRequestingStartCarryRPC). Le servant
    ///   reçoit donc le boulet par equipCarryableItem (CannonBall, HowitzerHEShell pour l'obusier) : visible en main pour tous
    ///   (paquet « psc ») et exigé par le chargement (_IsPlayerAllowedInteraction, transition 3 : ResolvePlayerCarryableAmmo).
    /// - equipCarryableItem &lt;type&gt; &lt;bot&gt; &lt;id&gt; : TryEquipCarryableObjectToPlayer passe l'id comme
    ///   environmentInteractableObjectIndex de BroadcastPlayerStartCarry : l'objet de la carte est ramassé (ToggleActive false,
    ///   porté par le bot). Le changement d'arme (switchWeapon) le pose : WeaponHolder retire le port si l'arme est
    ///   inutilisable avec, puis _RemovePlayerCarryableObject le dépose devant le joueur (paquet « pdpi »).
    /// - Gestes du chargement : joués par le client du seul joueur qui charge (son propre écran) ; pour les autres, seul le
    ///   refouloir apparaît (ClientMoveableCannonInteractableObjectBehaviour, transition 5 : EquipWeaponModel
    ///   CannonRamRodRammingObject) et la pièce joue son animation « Load » à la fin du refoulement. Aucune commande serveur ne
    ///   déclenche une animation de bras : le boulet en main est le seul signe visible possible avant le refouloir.
    /// </summary>
    public static class ArtyCrew
    {
        public static int Servants = 3;
        public static float LoadTime = 4f;
        public static float RamTime = 4f;

        public const int OneTime = 1, Start = 2, End = 4;
        public const int TrTakeAmmo = 0, TrLoad = 3, TrRam = 5, TrMan = 1, TrFire = 7, TrMove = 2;
        private const float AmmoSearch = 15f;

        public enum Shot { Unknown, Empty, Loaded, Rammed }

        public class Battery
        {
            public int OwnerId;
            public int CannonId;
            public GameObject Cannon;
            public int AmmoId = -1;
            public GameObject Ammo;
            public bool Manned;
            public Shot State = Shot.Unknown;
            public Bot Loader;
            public int Step;
            public float StepAt;
            public int Tries;
            // Approche du canon : meilleure distance atteinte et son instant (bot coincé contre l'affût).
            public float BestDist = float.MaxValue;
            public float BestAt;
            public float PauseUntil;
            public bool AmmoHintSent;
            // Canon déplacé loin de sa caisse : un servant la porte près du canon (prise en main, marche, arme reprise = posée).
            public bool MoveBox;
            public Bot Carrier;
            public int CarryStep;
            public float CarryAt;
            // Dernier événement de chaque servant : (objet, transition) → instant, pour confirmer une action.
            public readonly Dictionary<long, float> Seen = new Dictionary<long, float>();
        }

        private static readonly Dictionary<int, Battery> ByOwner = new Dictionary<int, Battery>();
        // Caisse de chaque canon, par carte : les ids des objets d'une carte ne changent pas d'une manche à l'autre. Repérée une
        // fois (n'importe qui y prend un boulet), elle est connue des servants dès la manche suivante sur cette carte.
        // Données de la carte, pas des joueurs. Vu en jeu : canon 51 → caisse 57, obusier 5 → caisse 2 (désert de Tahir).
        private static readonly Dictionary<string, int> KnownAmmo = new Dictionary<string, int>();
        private static readonly Dictionary<int, GameObject> AmmoObjects = new Dictionary<int, GameObject>();

        private static string MapKey(int cannonId) => (CommanderConfig.LastMapName ?? "?") + "|" + cannonId;

        public static void Clear()
        {
            ByOwner.Clear();
            AmmoObjects.Clear();
        }

        public static Battery Of(Line line) =>
            line != null && line.IsCrew && ByOwner.TryGetValue(line.Owner.PlayerId, out var b) ? b : null;

        public static void Forget(int ownerId) => ByOwner.Remove(ownerId);

        /// <summary>
        /// Caisse rangée dans la même batterie que le canon (vu en jeu : « Arty Battrey2/Ammobox Cannon… » à côté de
        /// « Arty Battrey2/Dynamic Artillery Spawn Section FieldGun/MovableCannon… ») : la plus proche, null sinon.
        /// </summary>
        private static GameObject FindAmmoNear(GameObject cannon)
        {
            var root = cannon.transform;
            for (var i = 0; i < 2 && root.parent != null; i++) root = root.parent;
            GameObject best = null;
            var bestD = AmmoSearch * 2f;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsAmmo(t.gameObject)) continue;
                var d = Line.Flat(t.position - cannon.transform.position).magnitude;
                if (d < bestD) { bestD = d; best = t.gameObject; }
            }
            return best;
        }

        private static bool IsCannon(GameObject o) => o != null && o.name.StartsWith("MovableCannon");
        private static bool IsAmmo(GameObject o) => o != null && o.name.StartsWith("Ammobox Cannon");

        /// <summary>Projectile porté par le servant (CarryableObjectType) : obus pour l'obusier, boulet sinon.</summary>
        public static string ShotCarry(Battery b) =>
            b.Cannon != null && b.Cannon.name.Contains("Howitzer") ? "HowitzerHEShell" : "CannonBall";

        private static long Key(int playerId, int objectId, int transition) =>
            ((long)playerId << 40) ^ ((long)objectId << 8) ^ (uint)transition;

        /// <summary>Toute action sur un objet de la carte (OnInteractableObjectInteraction).</summary>
        public static void OnInteraction(int playerId, int objectId, GameObject obj, int activation, int transition, float now)
        {
            if (Servants <= 0 || obj == null) return;
            var p = playerId >= 0 ? Players.Get(playerId) : null;

            // Joueur artilleur qui touche un canon : sa batterie (créée ou déplacée sur ce canon).
            if (IsCannon(obj) && p != null && !p.IsBot && p.Class == GameIds.PlayerClass.Cannoneer)
            {
                if (!ByOwner.TryGetValue(playerId, out var own) || own.CannonId != objectId)
                {
                    own = new Battery { OwnerId = playerId, CannonId = objectId, Cannon = obj };
                    if (KnownAmmo.TryGetValue(MapKey(objectId), out var known))
                    {
                        own.AmmoId = known;
                        if (!AmmoObjects.TryGetValue(known, out own.Ammo)) own.Ammo = FindAmmoNear(obj);
                    }
                    ByOwner[playerId] = own;
                    CmdLog.Info("[ARTY] Batterie de " + p.Name + " sur le canon " + objectId + " « " + obj.name + " ».");
                    var reply = LineManager.RaiseCrew(p, Servants);
                    if (reply != null) ServerConsole.Private(playerId, reply);
                }
            }

            if (IsAmmo(obj)) AmmoObjects[objectId] = obj;
            foreach (var b in ByOwner.Values)
            {
                // Caisse connue par son id (carte déjà vue) mais objet pas encore vu cette manche.
                if (b.AmmoId == objectId && b.Ammo == null) b.Ammo = obj;
                // Caisse de la pièce : première caisse touchée près du canon.
                if (b.AmmoId < 0 && IsAmmo(obj) && b.Cannon != null
                    && Line.Flat(obj.transform.position - b.Cannon.transform.position).magnitude <= AmmoSearch)
                {
                    b.AmmoId = objectId;
                    b.Ammo = obj;
                    KnownAmmo[MapKey(b.CannonId)] = objectId;
                    CmdLog.Info("[ARTY] Caisse " + objectId + " retenue pour le canon " + b.CannonId + ".");
                }
                if (objectId == b.CannonId)
                {
                    if (transition == TrMove && activation == End) b.MoveBox = true;
                    if (transition == TrMan && activation == Start) b.Manned = true;
                    else if (transition == TrMan && activation == End) b.Manned = false;
                    else if (transition == TrFire) b.State = Shot.Empty;
                    else if (transition == TrLoad && activation == End) b.State = Shot.Loaded;
                    else if (transition == TrRam && activation == End) b.State = Shot.Rammed;
                }
                if (playerId >= 0) b.Seen[Key(playerId, objectId, transition)] = now;
            }
        }

        public static bool Interact(Bot bot, int objectId, int activation, int transition) =>
            BotCommands.Interact(bot.PlayerId, objectId, activation, transition);

        /// <summary>Le servant a bien fait l'action (événement du jeu reçu depuis since).</summary>
        public static bool Confirmed(Battery b, Bot bot, int objectId, int transition, float since) =>
            b.Seen.TryGetValue(Key(bot.PlayerId, objectId, transition), out var at) && at >= since;
    }

    /// <summary>
    /// Servant de pièce. Le premier servant vivant recharge (étapes ci-dessous) ; les autres tiennent leur poste derrière le
    /// canon. Rien pendant que le joueur tient le canon (un début d'action d'un bot le lui retirerait).
    /// </summary>
    public class ArtyCrewTask : BotTask
    {
        public override string Name => "servant";

        private const float ConfirmWait = 2.5f;
        // Distance au centre du canon : on charge sans chercher plus près ; et au plus loin si le bot est coincé.
        private const float NearCannon = 2.6f;
        private const float MaxReach = 3.5f;
        private const int MaxTries = 3;

        public override bool Tick(Bot bot, float now)
        {
            var b = ArtyCrew.Of(bot.Line);
            if (b == null || b.Cannon == null || !bot.TryGetPosition(out var pos)) { bot.Stop(); return false; }
            var cannonPos = b.Cannon.transform.position;
            var fwd = Line.Flat(b.Cannon.transform.forward).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var right = new Vector3(fwd.z, 0f, -fwd.x);
            var faceYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

            if (b.Loader == null || !b.Loader.Alive || !bot.Line.Bots.Contains(b.Loader))
            {
                b.Loader = bot;
                b.Step = 0;
            }
            if (b.Loader != bot && CarryBox(bot, b, pos, cannonPos, fwd, right, faceYaw, now)) return false;
            if (b.Loader != bot)
            {
                // Poste : derrière le canon, à gauche ou à droite selon le rang dans l'équipe.
                var i = bot.Line.Bots.IndexOf(bot);
                var post = cannonPos - fwd * 3.5f + right * ((i % 2 == 0 ? 1.8f : -1.8f) * (1 + i / 2));
                bot.Steer(post, faceYaw, false, 1.5f);
                return false;
            }
            Load(bot, b, pos, cannonPos, fwd, faceYaw, now);
            return false;
        }

        private static void Load(Bot bot, ArtyCrew.Battery b, Vector3 pos, Vector3 cannonPos, Vector3 fwd, float faceYaw, float now)
        {
            // Rien à faire : déjà chargé, joueur à la pièce, caisse inconnue, ou pause après des refus.
            if (b.Step == 0 && (b.State == ArtyCrew.Shot.Rammed || b.Manned || b.AmmoId < 0 || b.Ammo == null || b.Carrier != null
                                || now < b.PauseUntil))
            {
                if (b.AmmoId < 0 && !b.AmmoHintSent)
                {
                    b.AmmoHintSent = true;
                    ServerConsole.Private(b.OwnerId, "Servants : prenez une fois un boulet dans la caisse de la pièce pour qu'ils la trouvent.");
                }
                bot.Steer(cannonPos - fwd * 2f, faceYaw, false, 0.8f);
                return;
            }
            // Le joueur tient le canon : le servant attend sur place, boulet en main s'il l'a déjà (pas de nouveau boulet).
            if (b.Manned && b.Step > 0)
            {
                if (b.Step > 2) b.Step = 2;
                bot.Stop();
                return;
            }
            switch (b.Step)
            {
                case 0: // aller à la caisse et prendre un boulet
                {
                    var ammoPos = b.Ammo.transform.position;
                    if (Line.Flat(ammoPos - pos).magnitude > 1.4f) { bot.Steer(ammoPos, faceYaw, true, 1.2f); return; }
                    bot.Stop();
                    ArtyCrew.Interact(bot, b.AmmoId, ArtyCrew.OneTime, ArtyCrew.TrTakeAmmo);
                    // Le boulet en main (sans id : rien n'est ramassé sur la carte), visible et nécessaire au chargement.
                    BotCommands.CarryItem(bot.PlayerId, ArtyCrew.ShotCarry(b), -1);
                    b.Step = 1;
                    b.StepAt = now;
                    return;
                }
                case 1:
                    if (ArtyCrew.Confirmed(b, bot, b.AmmoId, ArtyCrew.TrTakeAmmo, b.StepAt))
                    {
                        b.Step = 2;
                        b.Tries = 0;
                        b.BestDist = float.MaxValue;
                        b.BestAt = now;
                        return;
                    }
                    if (now - b.StepAt > ConfirmWait) Refused(bot, b, "boulet refusé par la caisse", now);
                    return;
                case 2: // au canon, chargement
                {
                    // On vise le canon lui-même : le bot s'arrête contre l'affût. Vu en jeu : coincé à 2,8 m du centre en
                    // cherchant un poste à la bouche. Assez près, ou plus de progrès depuis 2 s à moins de MaxReach m : on charge.
                    var d = Line.Flat(cannonPos - pos).magnitude;
                    if (d < b.BestDist - 0.3f) { b.BestDist = d; b.BestAt = now; }
                    var stuck = now - b.BestAt > 2f && d <= MaxReach;
                    if (d > NearCannon && !stuck)
                    {
                        var dir = Line.Flat(cannonPos - pos);
                        bot.Steer(cannonPos, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, true, NearCannon - 0.2f);
                        return;
                    }
                    bot.Stop();
                    ArtyCrew.Interact(bot, b.CannonId, ArtyCrew.Start, ArtyCrew.TrLoad);
                    b.Step = 3;
                    b.StepAt = now;
                    return;
                }
                case 3:
                    if (now - b.StepAt < ArtyCrew.LoadTime) return;
                    ArtyCrew.Interact(bot, b.CannonId, ArtyCrew.End, ArtyCrew.TrLoad);
                    b.Step = 4;
                    b.StepAt = now;
                    return;
                case 4:
                    if (ArtyCrew.Confirmed(b, bot, b.CannonId, ArtyCrew.TrLoad, b.StepAt - ArtyCrew.LoadTime - 0.1f)
                        && b.State == ArtyCrew.Shot.Loaded)
                    {
                        ArtyCrew.Interact(bot, b.CannonId, ArtyCrew.Start, ArtyCrew.TrRam);
                        b.Step = 5;
                        b.StepAt = now;
                        b.Tries = 0;
                        return;
                    }
                    if (now - b.StepAt > ConfirmWait) Refused(bot, b, "chargement refusé par le canon", now);
                    return;
                case 5:
                    if (now - b.StepAt < ArtyCrew.RamTime) return;
                    ArtyCrew.Interact(bot, b.CannonId, ArtyCrew.End, ArtyCrew.TrRam);
                    b.Step = 6;
                    b.StepAt = now;
                    return;
                default:
                    if (b.State == ArtyCrew.Shot.Rammed)
                    {
                        CmdLog.Info("[ARTY] " + bot + " : canon " + b.CannonId + " chargé et refoulé.");
                        b.Step = 0;
                        b.Tries = 0;
                        return;
                    }
                    if (now - b.StepAt > ConfirmWait) Refused(bot, b, "refoulement refusé par le canon", now);
                    return;
            }
        }

        // Caisse plus loin que tant du canon après un déplacement : on la rapproche ; posée à côté de l'affût.
        private const float BoxFar = 6f;
        private const float CarryTimeout = 40f;

        /// <summary>
        /// Transport de la caisse par un servant qui ne recharge pas. Vrai tant qu'il s'en occupe. Prise : equipCarryableItem
        /// AmmoBox avec l'id de la caisse ; pose : le servant reprend son mousquet (comme un joueur qui change d'arme).
        /// </summary>
        private static bool CarryBox(Bot bot, ArtyCrew.Battery b, Vector3 pos, Vector3 cannonPos, Vector3 fwd, Vector3 right,
            float faceYaw, float now)
        {
            if (b.Carrier != null && (b.Carrier != bot || !b.Carrier.Alive)) return b.Carrier == bot;
            if (b.Carrier == null)
            {
                if (!b.MoveBox) return false;
                if (b.Ammo == null || b.AmmoId < 0)
                {
                    if (!b.AmmoHintSent)
                    {
                        b.AmmoHintSent = true;
                        ServerConsole.Private(b.OwnerId, "Servants : prenez une fois un boulet dans la caisse de la pièce pour qu'ils la trouvent.");
                    }
                    return false;
                }
                b.MoveBox = false;
                if (Line.Flat(b.Ammo.transform.position - cannonPos).magnitude <= BoxFar) return false;
                b.Carrier = bot;
                b.CarryStep = 0;
                b.CarryAt = now;
                CmdLog.Info("[ARTY] " + bot + " va chercher la caisse " + b.AmmoId + " pour la rapprocher du canon.");
            }
            var drop = cannonPos - fwd * 1.5f + right * 2f;
            if (now - b.CarryAt > CarryTimeout)
            {
                CmdLog.Info("[ARTY] " + bot + " : transport de la caisse abandonné (trop long).");
                EndCarry(bot, b);
                return false;
            }
            switch (b.CarryStep)
            {
                case 0:
                {
                    var boxPos = b.Ammo.transform.position;
                    if (Line.Flat(boxPos - pos).magnitude > 1.4f) { bot.Steer(boxPos, faceYaw, true, 1.2f); return true; }
                    bot.Stop();
                    bot.CarryingBox = true;
                    BotCommands.CarryItem(bot.PlayerId, "AmmoBox", b.AmmoId);
                    b.CarryStep = 1;
                    return true;
                }
                case 1:
                    if (Line.Flat(drop - pos).magnitude > 1f) { bot.Steer(drop, faceYaw, false, 0.8f); return true; }
                    bot.Stop();
                    // Reprise de l'arme : le jeu pose la caisse par terre.
                    if (bot.Musket != null) BotCommands.SwitchWeapon(bot.PlayerId, bot.Musket);
                    else CmdLog.Info("[ARTY] " + bot + " : arme de départ inconnue, caisse non posée.");
                    b.CarryStep = 2;
                    b.CarryAt = now;
                    return true;
                default:
                    if (now - b.CarryAt < 1.5f) return true;
                    CmdLog.Info("[ARTY] " + bot + " a posé la caisse " + b.AmmoId + " : elle est à "
                                + ServerConsole.Num(Line.Flat(b.Ammo.transform.position - cannonPos).magnitude) + " m du canon.");
                    EndCarry(bot, b);
                    return false;
            }
        }

        private static void EndCarry(Bot bot, ArtyCrew.Battery b)
        {
            bot.CarryingBox = false;
            b.Carrier = null;
            b.CarryStep = 0;
        }

        private static void Refused(Bot bot, ArtyCrew.Battery b, string what, float now)
        {
            b.Step = 0;
            if (++b.Tries < MaxTries) return;
            b.Tries = 0;
            b.PauseUntil = now + 15f;
            CmdLog.Warn("[ARTY] " + bot + " : " + what + " (" + MaxTries + " essais), pause 15 s.");
        }
    }
}
