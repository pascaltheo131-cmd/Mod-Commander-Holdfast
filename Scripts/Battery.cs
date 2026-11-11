using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    public enum TubeState { Unknown, Empty, Loaded, Rammed }

    /// <summary>Rôle d'un servant : un seul à la fois, donné par la batterie (Battery.Refresh).</summary>
    public enum CrewRole { None, Loader, Purveyor, Assistant, Sapper }

    /// <summary>
    /// État d'une pièce d'artillerie servie par des bots : canon, caisse de munitions, artilleur (joueur), servants et leur
    /// rôle, état du tube. Créée quand un joueur Cannoneer touche un canon. Les tâches des servants sont dans ArtyTasks.cs,
    /// les règles (effectif, durées) dans ArtyRules.cs.
    ///
    /// Faits du jeu relevés (journal [ARTY], 2026-11-08), identiques canon et obusier. Activation : 1 unique, 2 début, 4 fin.
    /// Transitions du canon : début/fin 1 = prendre en main (visée) / lâcher, début/fin 2 = déplacer, début/fin 3 = charger,
    /// début/fin 5 = refouler, unique 7 = feu, unique 8 / 9 = aider au déplacement / cesser (deuxième place du canon, sans le
    /// retirer au joueur ; à deux, il roule plus vite). Pas d'écouvillon. Caisse : « unique 0 » = prendre un boulet.
    ///
    /// Vu dans le code du jeu (décompilé) :
    /// - « unique 0 » sur la caisse ne fait rien côté serveur : un joueur prend son boulet par une demande de port de son
    ///   client. Le servant reçoit donc le boulet par equipCarryableItem (CannonBall, HowitzerHEShell pour l'obusier), sans
    ///   id d'objet : visible en main pour tous, et exigé par le chargement (transition 3).
    /// - equipCarryableItem AmmoBox &lt;bot&gt; &lt;id&gt; : la caisse de la carte est ramassée et portée par le bot. Le
    ///   changement d'arme (switchWeapon) la pose devant lui.
    /// - Gestes du chargement : joués par le client du seul joueur qui charge. Pour les autres, seuls le boulet en main puis
    ///   le refouloir sont visibles. Aucune commande serveur ne déclenche une animation de bras.
    ///
    /// Caisse : les objets de la carte sont parcourus en début de manche (Scan) : la caisse est connue par son objet (donc sa
    /// position) sans que personne ne l'ait touchée. Son id (nécessaire seulement pour la porter) n'est donné par le jeu que
    /// dans un événement : il est retenu par carte dès qu'un événement la concerne, sinon le pourvoyeur le cherche une fois,
    /// debout contre la caisse (ArtyTasks, sonde). Noms vus : « Ammobox Cannon - Carryable (TwoSlot) »,
    /// « Ammobox - Carryable (TwoSlot) (7) », « Artillery_Ammobox_Carryable(Ball,SBuckShot)_Mod » ; canons :
    /// « MovableCannon_FieldGun_9PDR », « MovableCannon_FieldHowitzer », « Artillery_Movable_FieldGun_AREV_… ».
    /// </summary>
    public class Battery
    {
        public const int OneTime = 1, Start = 2, End = 4;
        public const int TrTakeAmmo = 0, TrMan = 1, TrMove = 2, TrLoad = 3, TrRam = 5, TrFire = 7, TrAssist = 8, TrStopAssist = 9;

        /// <summary>Caisse plus loin que tant du canon posé : le pourvoyeur la rapproche.</summary>
        public const float BoxFar = 6f;
        /// <summary>Distance du centre du canon où la caisse est posée.</summary>
        public const float BoxDrop = 2f;
        // Caisse cherchée jusqu'à cette distance du canon.
        private const float BoxSearch = 40f;

        public int OwnerId;
        public int CannonId;
        public GameObject Cannon;

        // ---- Caisse ----
        public GameObject Box;
        public int BoxId = -1;
        // Endroit où le pourvoyeur l'a posée, quand l'objet de la carte n'a pas suivi (voir PurveyorTask).
        public bool BoxSpotKnown;
        public Vector3 BoxSpot;
        private float _nextBoxSearch;
        public bool NoBoxTold;

        // ---- Canon ----
        /// <summary>Un joueur tient la pièce (visée) : aucun servant n'y touche.</summary>
        public bool Manned;
        /// <summary>L'artilleur déplace la pièce.</summary>
        public bool Moving;
        public float SetDownAt;
        /// <summary>Nombre de déplacements terminés : un abandon du pourvoyeur ne vaut que pour la pose en cours.</summary>
        public int Moves;
        public TubeState Tube = TubeState.Unknown;

        // ---- Servants ----
        public Bot Loader, Purveyor, Assistant, Sapper;
        private float _refreshedAt = -1f;

        // Chargeur.
        public int LoadStep;
        public float LoadAt;
        public int LoadTries;
        public float BestDist = float.MaxValue;
        public float BestAt;
        public float PauseUntil;

        // Pourvoyeur.
        public int CarryStep;
        public float CarryAt;
        public float CarryStartedAt;
        public int CarryTries;
        public int CarryGaveUpAt = -1;
        public int ProbeNext;
        public bool ProbeDone;

        // Aide au déplacement.
        public bool AssistOn;
        public float AssistAskedAt = -10f;

        // Essais z60 (ArtyProbe) : une fois par pièce.
        public bool SapperTried, PointerTried;

        // Dernier événement de chaque servant : (objet, transition) → instant ; dernier objet pris en main → instant.
        private readonly Dictionary<long, float> _seen = new Dictionary<long, float>();
        private readonly Dictionary<int, float> _carried = new Dictionary<int, float>();

        // ---- Géométrie ----

        public Vector3 CannonPos => Cannon.transform.position;

        public Vector3 Forward
        {
            get
            {
                var f = Line.Flat(Cannon.transform.forward);
                return f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized;
            }
        }

        public Vector3 Right
        {
            get
            {
                var f = Forward;
                return new Vector3(f.z, 0f, -f.x);
            }
        }

        public float FaceYaw
        {
            get
            {
                var f = Forward;
                return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }
        }

        public bool IsHowitzer => Cannon != null && Cannon.name.IndexOf("Howitzer", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Projectile porté par le chargeur (CarryableObjectType) : obus pour l'obusier, boulet sinon.</summary>
        public string ShotCarry => IsHowitzer ? "HowitzerHEShell" : "CannonBall";

        public bool HasBox => Box != null;

        public Vector3 BoxPos => BoxSpotKnown ? BoxSpot : Box.transform.position;

        /// <summary>Le pourvoyeur a la caisse en main ou va la prendre.</summary>
        public bool BoxInHand => CarryStep >= 2;

        /// <summary>Caisse trop loin du canon posé, et le pourvoyeur n'a pas renoncé pour cette pose.</summary>
        public bool BoxMustMove(float now) =>
            HasBox && Cannon != null && !Moving && now - SetDownAt >= 1f && CarryGaveUpAt != Moves
            && Line.Flat(BoxPos - CannonPos).magnitude > BoxFar;

        /// <summary>Endroit où la caisse doit être posée : à côté de l'affût, hors de l'axe de tir et du recul.</summary>
        public Vector3 DropSpot => CannonPos + Right * BoxDrop - Forward * 0.5f;

        /// <summary>Poste d'attente de chaque rôle autour de la pièce.</summary>
        public Vector3 Post(CrewRole role, int index)
        {
            var p = CannonPos;
            switch (role)
            {
                case CrewRole.Loader: return p + Forward * 0.5f - Right * 2f;
                case CrewRole.Purveyor: return p + Right * 3f - Forward * 1.5f;
                case CrewRole.Assistant: return p - Forward * 3.5f - Right * 1.5f;
                case CrewRole.Sapper: return p - Forward * 5f + Right * 1.5f;
                default: return p - Forward * 5f - Right * (1.5f + index);
            }
        }

        // ---- Rôles ----

        /// <summary>
        /// Rôles selon les servants en vie, dans l'ordre de la ligne : 1er canonnier chargeur, 2e pourvoyeur, 3e aide au
        /// déplacement ; le sapeur garde son rôle (réservé, z60). À deux canonniers, le second aide pendant que la pièce
        /// roule (s'il ne porte pas la caisse) et redevient pourvoyeur ensuite.
        /// </summary>
        public void Refresh(Line line, float now)
        {
            if (now == _refreshedAt) return;
            _refreshedAt = now;
            Bot l = null, p = null, a = null, s = null;
            var n = 0;
            foreach (var bot in line.Bots)
            {
                if (!bot.Alive || bot.PlayerId < 0) continue;
                if (bot.Class == GameIds.PlayerClass.Sapper) { if (s == null) s = bot; continue; }
                n++;
                if (n == 1) l = bot;
                else if (n == 2) p = bot;
                else if (n == 3) a = bot;
            }
            if (n == 2 && (Moving || AssistOn) && CarryStep == 0) { a = p; p = null; }
            if (l != Loader)
            {
                if (Loader != null) Loader.CarryingBox = false;
                Loader = l;
                LoadStep = 0;
            }
            if (p != Purveyor)
            {
                if (Purveyor != null) Purveyor.CarryingBox = false;
                Purveyor = p;
                CarryStep = 0;
            }
            if (a != Assistant)
            {
                Assistant = a;
                AssistOn = false;
            }
            Sapper = s;
            EnsureBox(now);
        }

        public CrewRole RoleOf(Bot bot) =>
            bot == Loader ? CrewRole.Loader : bot == Purveyor ? CrewRole.Purveyor : bot == Assistant ? CrewRole.Assistant
            : bot == Sapper ? CrewRole.Sapper : CrewRole.None;

        // ---- Événements du jeu ----

        private static long Key(int playerId, int objectId, int transition) =>
            ((long)playerId << 40) ^ ((long)objectId << 8) ^ (uint)transition;

        /// <summary>Le servant a bien fait l'action (événement du jeu reçu depuis since).</summary>
        public bool Confirmed(Bot bot, int objectId, int transition, float since) =>
            _seen.TryGetValue(Key(bot.PlayerId, objectId, transition), out var at) && at >= since;

        /// <summary>Le jeu a annoncé un objet en main du servant depuis since (OnPlayerStartCarry).</summary>
        public bool Carried(Bot bot, float since) => _carried.TryGetValue(bot.PlayerId, out var at) && at >= since;

        public static bool Interact(Bot bot, int objectId, int activation, int transition) =>
            BotCommands.Interact(bot.PlayerId, objectId, activation, transition);

        // ---- Toutes les batteries -------------------------------------------------------------

        private static readonly Dictionary<int, Battery> ByOwner = new Dictionary<int, Battery>();
        // Caisses de la carte (parcours des objets), leur clé (carte, nom, place d'origine) et leur id quand il est connu.
        private static readonly List<GameObject> Boxes = new List<GameObject>();
        private static readonly Dictionary<int, string> BoxKeys = new Dictionary<int, string>();
        private static readonly Dictionary<int, int> BoxIds = new Dictionary<int, int>();
        // Ids vus cette manche sur autre chose qu'une caisse (porte, drapeau, canon…) : jamais sondés.
        private static readonly HashSet<int> OtherIds = new HashSet<int>();
        // Id de chaque caisse, par carte : les ids des objets d'une carte ne changent pas d'une manche à l'autre.
        // Données de la carte, pas des joueurs.
        private static readonly Dictionary<string, int> KnownIds = new Dictionary<string, int>();
        private static bool _scanned;
        private static float _scanAt = -1f;
        private static float _lastScan = -1000f;
        private static int _scans;

        public static bool IsCannon(GameObject o)
        {
            if (o == null) return false;
            var n = o.name;
            if (n.IndexOf("Rocket", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return n.StartsWith("MovableCannon") || n.StartsWith("Artillery_Movable");
        }

        public static bool IsAmmo(GameObject o)
        {
            if (o == null) return false;
            var n = o.name;
            if (n.IndexOf("Ammobox", System.StringComparison.OrdinalIgnoreCase) < 0
                || n.IndexOf("Rocket", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            // Seul l'objet interactif compte : la carte a aussi des dizaines de maillages nommés « Ammobox… » (LOD, décor).
            // Vu en jeu : 55 objets au nom, une seule vraie caisse (composant HoldfastGame.AmmoBoxInteractableObject).
            // Pas de GetType : la réflexion est refusée par le SDK ; ToString donne « nom (Type) ».
            foreach (var c in o.GetComponents<Component>())
                if (c != null && c.ToString().IndexOf("AmmoBoxInteractableObject", System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        public static bool IsOtherId(int objectId) => OtherIds.Contains(objectId);

        /// <summary>Nouvelle manche : plus aucune batterie ; les objets de la carte seront parcourus dans 15 s.</summary>
        public static void Clear()
        {
            ByOwner.Clear();
            ArtyRules.Clear();
            Boxes.Clear();
            BoxKeys.Clear();
            BoxIds.Clear();
            OtherIds.Clear();
            _scanned = false;
            _scans = 0;
            _scanAt = Clock.Now + 15f;
        }

        public static Battery Of(Line line) =>
            line != null && line.IsCrew && ByOwner.TryGetValue(line.Owner.PlayerId, out var b) ? b : null;

        public static void Forget(int ownerId) => ByOwner.Remove(ownerId);

        /// <summary>Appelé à chaque image (LineManager.Tick) : parcours des objets en début de manche.</summary>
        public static void Tick(float now)
        {
            if (_scanned || _scanAt < 0f || now < _scanAt || ArtyRules.Servants <= 0) return;
            Scan(now);
        }

        /// <summary>Parcourt les objets de la carte et retient les caisses de munitions (objet et position).</summary>
        private static void Scan(float now)
        {
            _scanned = true;
            _lastScan = now;
            _scans++;
            Boxes.RemoveAll(o => o == null);
            var found = 0;
            foreach (var t in Object.FindObjectsOfType<Transform>())
            {
                var o = t.gameObject;
                if (!IsAmmo(o)) continue;
                if (AddBox(o)) found++;
            }
            CmdLog.Info("[ARTY] Carte parcourue : " + Boxes.Count + " caisse(s) de munitions (" + found + " nouvelle(s)).");
        }

        private static bool AddBox(GameObject o)
        {
            var iid = o.GetInstanceID();
            if (BoxKeys.ContainsKey(iid)) return false;
            var pos = o.transform.position;
            var key = (CommanderConfig.LastMapName ?? "?") + "|" + o.name + "|" + Mathf.RoundToInt(pos.x) + "," + Mathf.RoundToInt(pos.z);
            BoxKeys[iid] = key;
            Boxes.Add(o);
            if (KnownIds.TryGetValue(key, out var id)) BoxIds[iid] = id;
            return true;
        }

        /// <summary>Caisse la plus proche du canon qui n'est pas déjà celle d'une autre pièce.</summary>
        private void EnsureBox(float now)
        {
            if (Box != null || Cannon == null || now < _nextBoxSearch) return;
            _nextBoxSearch = now + 10f;
            BoxSpotKnown = false;
            BoxId = -1;
            // Jamais parcourue cette manche, ou aucune caisse trouvée : nouveau parcours (3 au plus par manche).
            if (!_scanned || (_scans < 3 && now - _lastScan > 30f)) Scan(now);
            GameObject best = null;
            var bestD = BoxSearch;
            foreach (var o in Boxes)
            {
                if (o == null || !o.activeInHierarchy) continue;
                var taken = false;
                foreach (var other in ByOwner.Values) if (other != this && other.Box == o) { taken = true; break; }
                if (taken) continue;
                var d = Line.Flat(o.transform.position - CannonPos).magnitude;
                if (d < bestD) { bestD = d; best = o; }
            }
            if (best == null) return;
            Box = best;
            if (BoxIds.TryGetValue(best.GetInstanceID(), out var id)) BoxId = id;
            ProbeNext = 0;
            ProbeDone = false;
            CmdLog.Info("[ARTY] Canon " + CannonId + " : caisse « " + best.name + " » à " + ServerConsole.Num(bestD) + " m"
                        + (BoxId >= 0 ? ", id " + BoxId + "." : ", id pas encore connu."));
        }

        /// <summary>Toute action sur un objet de la carte (OnInteractableObjectInteraction).</summary>
        public static void OnInteraction(int playerId, int objectId, GameObject obj, int activation, int transition, float now)
        {
            if (obj == null) return;
            var p = playerId >= 0 ? Players.Get(playerId) : null;
            var human = p != null && !p.IsBot;

            if (IsAmmo(obj))
            {
                // Id de la caisse : seul un événement le donne. Retenu pour la manche et pour la carte.
                AddBox(obj);
                var iid = obj.GetInstanceID();
                if (!BoxIds.TryGetValue(iid, out var old) || old != objectId)
                {
                    BoxIds[iid] = objectId;
                    KnownIds[BoxKeys[iid]] = objectId;
                    CmdLog.Info("[ARTY] Caisse « " + obj.name + " » : id " + objectId + ".");
                }
            }
            else OtherIds.Add(objectId);

            // Joueur artilleur qui touche un canon : sa batterie (créée ou déplacée sur ce canon).
            if (human && IsCannon(obj) && p.Class == GameIds.PlayerClass.Cannoneer && ArtyRules.Servants > 0)
            {
                if (!ByOwner.TryGetValue(playerId, out var own) || own.CannonId != objectId)
                {
                    own = new Battery { OwnerId = playerId, CannonId = objectId, Cannon = obj, SetDownAt = now };
                    ByOwner[playerId] = own;
                    CmdLog.Info("[ARTY] Batterie de " + p.Name + " sur le canon " + objectId + " « " + obj.name + " ».");
                    ArtyRules.RaiseFor(p);
                }
            }

            foreach (var b in ByOwner.Values)
            {
                if (b.Box == obj && b.BoxId != objectId) b.BoxId = objectId;
                if (objectId == b.CannonId)
                {
                    if (transition == TrMove && playerId == b.OwnerId)
                    {
                        if (activation == Start) b.Moving = true;
                        else if (activation == End)
                        {
                            b.Moving = false;
                            b.SetDownAt = now;
                            b.Moves++;
                        }
                    }
                    if (b.Assistant != null && playerId == b.Assistant.PlayerId)
                    {
                        if (transition == TrAssist) b.AssistOn = true;
                        else if (transition == TrStopAssist) b.AssistOn = false;
                    }
                    // Seul un joueur compte comme tenant la pièce (les essais de pointage d'un bot ne bloquent pas le chargeur).
                    if (transition == TrMan && human) b.Manned = activation == Start || (activation != End && b.Manned);
                    else if (transition == TrFire) b.Tube = TubeState.Empty;
                    else if (transition == TrLoad && activation == End) b.Tube = TubeState.Loaded;
                    else if (transition == TrRam && activation == End) b.Tube = TubeState.Rammed;
                }
                if (playerId >= 0) b._seen[Key(playerId, objectId, transition)] = now;
            }
        }

        /// <summary>Objet pris en main par un bot du mod (OnPlayerStartCarry) : confirme le boulet ou la caisse.</summary>
        public static void OnCarry(int playerId, float now)
        {
            foreach (var b in ByOwner.Values) b._carried[playerId] = now;
        }
    }
}
