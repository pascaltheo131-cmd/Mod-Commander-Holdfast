using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Cœur du mod : levée des lignes, apparition et rattachement des bots, boucle de pilotage, fin de ligne et nettoyage.
    /// Lot A : succession (n° 3), moral (n° 4, voir Morale), renforts (n° 5), plafond selon le remplissage (n° 11).
    /// Tout l'état est statique (le jeu crée une instance du mod par interface).
    /// Attention : le jeu rappelle le mod de façon synchrone pendant une commande console (OnPlayerLeft pendant un kick,
    /// OnPlayerHurt et OnPlayerKilledPlayer pendant un slay, OnPlayerConnected pendant un spawnSpecific…). Toute boucle qui
    /// envoie une commande parcourt donc une copie, et l'état est mis à jour avant la commande.
    /// </summary>
    public static class LineManager
    {
        // Un bot créé qui n'est pas apparu après ce délai (apparition refusée : places de section ou de classe) est retiré.
        // Un spawnSpecific accepté fait apparaître le bot en moins d'une seconde : au-delà de 4 s, la place est relancée.
        // Délai d'apparition : 4 s d'habitude ; pendant la première minute de manche le jeu fait attendre la vague initiale
        // (souvent plus de 4 s) : sans ce délai plus long, chaque bot en attente serait kické et relancé pour rien.
        private static float SpawnTimeout => Clock.Now < 60f ? 20f : 4f;
        // Pilotage : chaque bot au plus toutes les BotTickInterval secondes, au plus MaxBotTicksPerFrame bots par image.
        private const float BotTickInterval = 0.1f;
        private const float SettledTickInterval = 0.4f;
        private const int MaxBotTicksPerFrame = 60;
        // Plancher : même si les autres tâches ont déjà envoyé beaucoup de commandes dans l'image, quelques bots sont pilotés.
        private const int MinBotTicksPerFrame = 8;
        // Avec CMD:kickdead, le corps d'un bot tué reste un moment avant le retrait.
        private const float DeadBotRemoveDelay = 5f;

        private static readonly Dictionary<int, Commander> Commanders = new Dictionary<int, Commander>();
        private static readonly Dictionary<int, Bot> BotsByPlayer = new Dictionary<int, Bot>();
        private static readonly List<Line> Dispersing = new List<Line>();
        // Lignes sans officier tenues par un sergent (succession), en attente d'une reprise.
        private static readonly List<Line> Orphans = new List<Line>();
        // Deux files d'apparition : celle des joueurs est toujours servie avant celle des lignes IA.
        private static readonly Queue<Bot> SpawnQueue = new Queue<Bot>();
        private static readonly Queue<Bot> AiSpawnQueue = new Queue<Bot>();
        /// <summary>Places relancées (mauvaise classe, non apparu) : servies en premier, hors lot, une par image.</summary>
        private static readonly Queue<Bot> RelaunchQueue = new Queue<Bot>();
        // Kicks à envoyer, par lots de KickBatch par seconde pour tout le mod (KickInterval) : bots du mod (vérifiés au moment du kick),
        // et ids gardés après un !cmd vider/off (l'état est déjà vidé).
        private const float KickInterval = 1f;
        // Kicks envoyés par lot : au plus KickBatch par KickInterval (10 par seconde).
        private const int KickBatch = 10;
        private const int KickPerFrame = 2;
        private static readonly List<Bot> KickQueue = new List<Bot>();
        private static readonly List<int> ClearedKicks = new List<int>();
        private static float _nextKickAt;
        private static readonly List<Bot> AwaitingSpawn = new List<Bot>();
        // Retraits (kick) prévus : la référence du bot est gardée, pour ne jamais kicker le nouveau titulaire d'un id réutilisé.
        private static readonly List<KeyValuePair<float, Bot>> Removals = new List<KeyValuePair<float, Bot>>();
        // Bots dispersés laissés sur place (CMD:tuerdisperses faux) : plus pilotés, retirés en premier si le plafond baisse.
        private static readonly List<Bot> Leftovers = new List<Bot>();

        private static readonly List<Bot> TickList = new List<Bot>();
        private static readonly List<Line> LineList = new List<Line>();
        private static int _cursor;
        private static float _nextSpawn;
        private static float _lastTick = -1f;
        // Plafond global courant (selon le remplissage), recalculé une fois par seconde.
        private static int _cap = -1;

        /// <summary>Plafond à recalculer (changement de manche, échauffement).</summary>
        public static void InvalidateCap() => _cap = -1;
        private static float _nextCapUpdate;
        private static float _nextFillRemoval;
        // Début de la série de retraits progressifs en cours (message à l'officier seulement au premier).
        private const float CapSeriesGap = 3f;
        private const float OfficerNoticeRadius = 100f;

        /// <summary>Nouvelle origine de l'horloge (Clock.Reset, après Clear) : les échéances repartent de zéro.</summary>
        public static void ResetClock()
        {
            _nextSpawn = 0f;
            _lastTick = -1f;
            _nextCapUpdate = 0f;
            _nextFillRemoval = 0f;
            _nextKickAt = 0f;
            _kickBudget = 0;
            _spawnBudget = 0;
            _nextFallCheck = 0f;
            _nextCameraCheck = 0f;
            _stuckLogs = 0;
            _musicLogs = 0;
            DetourCount = GaveUpCount = RescueCount = 0;
        }

        /// <summary>Bots comptés dans le plafond : ceux des lignes (y compris en attente d'apparition ou en dispersion).</summary>
        public static int TotalBots
        {
            get
            {
                var n = 0;
                foreach (var c in Commanders.Values) if (c.Line != null) n += c.Line.Count;
                foreach (var l in Dispersing) n += l.Count;
                foreach (var l in Orphans) n += l.Count;
                return n + Leftovers.Count;
            }
        }

        /// <summary>Bots qui comptent pour une levée ou des renforts de joueur : ni lignes IA, ni dispersés laissés sur place.</summary>
        private static int PlayerBots()
        {
            return TotalBots - AiBots() - Leftovers.Count;
        }

        /// <summary>
        /// Plafond global effectif : le plus petit de maxbots (ou du plafond admin), du plafond selon le remplissage (CMD:fillfull)
        /// et du plafond selon la charge (LoadMonitor).
        /// </summary>
        public static int Cap
        {
            get
            {
                if (_cap < 0) _cap = ComputeCap();
                return _cap;
            }
        }

        private static int ComputeCap()
        {
            // Échauffement : plafond dédié, seule la charge du serveur le réduit (pas de remplissage ni de maxbots).
            if (CommanderConfig.IsWarmup) return Mathf.Min(CommanderConfig.WarmupMax, LoadMonitor.Cap);
            if (CommanderConfig.EffectiveMaxBots <= 0) return int.MaxValue;
            return Mathf.Min(CommanderConfig.CapFor(Players.RealPlayerCount()), LoadMonitor.Cap);
        }

        private static string CapText => Cap == int.MaxValue ? "illimité" : Cap.ToString(CultureInfo.InvariantCulture);

        /// <summary>Bots vivants ou attendus, hors dispersion en cours (les dispersés laissés sur place comptent).</summary>
        private static int ActiveBots()
        {
            var n = TotalBots;
            foreach (var l in Dispersing) n -= l.Count;
            return n;
        }

        public static Commander GetCommander(int playerId)
        {
            return TryGetCommander(playerId, out var c) ? c : null;
        }

        /// <summary>
        /// Officier connu sous cet id. Une entrée d'officier IA dont l'id n'est plus celui d'un bot du mod (id réutilisé par un
        /// joueur) est retirée : un vrai joueur n'hérite jamais d'une ligne IA.
        /// </summary>
        private static bool TryGetCommander(int playerId, out Commander c)
        {
            if (!Commanders.TryGetValue(playerId, out c)) return false;
            if (!c.IsAi || BotsByPlayer.ContainsKey(playerId)) return true;
            Commanders.Remove(playerId);
            c = null;
            return false;
        }

        /// <summary>Met une demande d'apparition dans la file des joueurs ou dans celle des lignes IA.</summary>
        /// <summary>Places demandées mais pas encore apparues (elles consommeront le quota) dans la ligne de cet officier.</summary>
        private static int PendingPlaces(Commander c)
        {
            if (c == null || c.Line == null) return 0;
            var n = 0;
            foreach (var b in c.Line.Bots) if (!b.Alive && (b.PlayerId < 0 || AwaitingSpawn.Contains(b))) n++;
            return n;
        }

        /// <summary>Reste du quota de ce joueur, places en attente déduites (pour !quota).</summary>
        public static string QuotaText(int playerId)
        {
            var text = Quota.Describe(playerId);
            var pending = PendingPlaces(GetCommander(playerId));
            return pending > 0 && Quota.Enabled ? text + " (" + pending + " en route)" : text;
        }

        /// <summary>Essais par place quand le jeu fait apparaître une autre classe ou n'arrive pas à faire apparaître le bot.</summary>
        private const int MaxSpawnAttempts = 6;
        /// <summary>Essais quand le bot tombe sur une section qui refuse sa classe (artillerie…) : les sections d'artillerie restent
        /// réservées aux artilleurs, on relance donc jusqu'à tirer une section d'infanterie.</summary>
        private const int MaxSectionAttempts = 25;

        /// <summary>Classes de ligne à arme à feu acceptées à la place de la classe demandée après tous les essais.</summary>
        private static bool IsMusketClass(int c) =>
            c == GameIds.PlayerClass.ArmyLineInfantry || c == GameIds.PlayerClass.Rifleman || c == GameIds.PlayerClass.LightInfantry
            || c == GameIds.PlayerClass.Grenadier || c == GameIds.PlayerClass.Guard;

        /// <summary>Retire ce bot (kick, débit limité) et redemande la même place : même rôle, même classe, même uniforme.</summary>
        private static void Relaunch(Bot bot, float now)
        {
            var line = bot.Line;
            AwaitingSpawn.Remove(bot);
            line.Remove(bot);
            Retire(bot, now, true);
            var again = new Bot(line, bot.Role, bot.Class, bot.Uniform)
            {
                Reinforcement = bot.Reinforcement,
                Attempts = bot.Attempts + 1,
            };
            line.Add(again);
            RelaunchQueue.Enqueue(again);
        }

        /// <summary>Fin de levée (plus aucune demande en attente) : bilan à l'officier.</summary>
        private static void ReportRaises()
        {
            var any = false;
            foreach (var c in Commanders.Values) if (c.Line != null && c.Line.RaiseActive) { any = true; break; }
            if (!any) return;
            // Copie : le message à l'officier passe par une commande qui peut rappeler le mod.
            foreach (var c in new List<Commander>(Commanders.Values))
            {
                var line = c.Line;
                if (line == null || !line.RaiseActive || c.IsAi) continue;
                var pending = false;
                foreach (var b in line.Bots)
                    if (b.PlayerId < 0 || AwaitingSpawn.Contains(b)) { pending = true; break; }
                if (pending) continue;
                line.RaiseActive = false;
                var text = line.RaiseOk + " hommes levés";
                if (line.RaiseFailed > 0)
                    text += ", " + line.RaiseFailed + " n'ont pas pu apparaître (trop de tirages sur des sections qui refusent la classe, "
                            + "artillerie par exemple ; refaites !ligne pour compléter)";
                CmdLog.Info("Levée de " + c.Name + " : " + text + ".");
                if (LineAlive(line)) TellOwner(line, text + ".");
            }
        }

        private static void Enqueue(Bot bot)
        {
            if (bot.Line.Owner.IsAi) AiSpawnQueue.Enqueue(bot);
            else SpawnQueue.Enqueue(bot);
        }

        /// <summary>
        /// Avant une levée ou des renforts de joueur de n bots : si le total dépasserait le plafond, la place est faite tout de
        /// suite aux dépens des lignes IA (demandes IA en file annulées, puis bots IA retirés par kick, au débit limité).
        /// </summary>
        private static void MakeRoomForPlayers(int n, float now)
        {
            var over = TotalBots + n - Cap;
            while (over > 0 && CancelQueued(true)) over--;
            while (over > 0)
            {
                var bot = FarthestBot(true);
                if (bot == null) break;
                DropBot(bot, now);
                over--;
            }
        }

        /// <summary>Retire un bot vivant de sa ligne et le met en file de kick (immobile en attendant).</summary>
        private static void DropBot(Bot bot, float now)
        {
            var line = bot.Line;
            bot.ClearActions();
            if (bot.Alive) bot.SetMovement(new StandStillTask(), now);
            // État mis à jour avant toute commande : les rappels synchrones du jeu ne doivent pas compter cette perte.
            bot.Alive = false;
            line.Remove(bot);
            if (line.Sergeant == bot) PickSergeant(line);
            Retire(bot, now, true);
        }

        public static bool IsOurBot(int playerId)
        {
            return BotsByPlayer.ContainsKey(playerId);
        }

        /// <summary>Joueur dont le mod suit la position : bots du mod et officiers qui ont une ligne.</summary>
        public static bool IsTracked(int playerId)
        {
            return BotsByPlayer.ContainsKey(playerId) || Commanders.ContainsKey(playerId);
        }

        // ---- Levée ---------------------------------------------------------------------------

        /// <summary>Lève (ou complète) la ligne d'un officier. Renvoie le message à lui envoyer.</summary>
        /// <summary>Taille d'un escadron de cavalerie.</summary>
        public const int CavalryMax = 8;

        public static string Raise(PlayerInfo officer, int count, int playerClass, int uniform, string namePrefix = null)
        {
            if (!CommanderConfig.IsEnabled) return "Les lignes de bots sont désactivées sur ce serveur.";
            var cavalry = GameIds.IsCavalry(officer.Class);
            if (!officer.Alive || (!GameIds.IsInfantryOfficer(officer.Class) && !cavalry))
                return "Seul un officier d'infanterie ou un cavalier (Hussar, Dragoon) en vie peut lever une ligne.";
            if (officer.Faction == GameIds.FactionCountry.None) return "Faction inconnue : réapparaissez puis réessayez.";
            // Pas d'uniforme d'infanterie de terre pour ces factions : le jeu tirerait la classe au hasard.
            if (officer.Faction == GameIds.FactionCountry.Privateer || officer.Faction == GameIds.FactionCountry.Spanish)
                return "Pas de ligne possible pour cette faction.";

            var c = GetCommander(officer.Id);
            if (c != null && c.IsAi) c = null;
            var pause = c != null ? Mathf.CeilToInt(c.RaisePausedUntil - Clock.Now) : 0;
            if (pause > 0) return "Levées en pause (apparitions refusées par le jeu) : réessayez dans " + pause + " s.";
            var current = c != null && c.Line != null ? c.Line.Count : 0;
            if (c != null && c.Line != null && c.Line.Count > 0 && c.Line.IsCavalry != cavalry)
                return cavalry ? "Votre ligne est d'infanterie : !renvoyer d'abord." : "Votre escadron est de cavalerie : !renvoyer d'abord.";
            var maxHere = cavalry ? Mathf.Min(CavalryMax, CommanderConfig.EffectiveMaxPerOfficer) : CommanderConfig.EffectiveMaxPerOfficer;
            // Les lignes IA (lot C) et les dispersés laissés sur place passent après les lignes des joueurs : ils ne comptent
            // pas ici, TickFill les retire d'abord.
            var available = Mathf.Min(maxHere - current, Cap - PlayerBots());
            if (available <= 0)
            {
                return current > 0 && current >= maxHere
                    ? "Plafond atteint : votre ligne compte déjà " + current + " hommes."
                    : "Plafond de bots du serveur atteint (" + Cap + " avec " + Players.RealPlayerCount() + " joueurs).";
            }
            var quotaLeft = Quota.Remaining(officer.Id) - PendingPlaces(c);
            if (quotaLeft <= 0) return Quota.Refusal(officer.Id);
            var n = Mathf.Min(Mathf.Min(count, available), quotaLeft);

            var notice = "";
            // Escadron : les cavaliers prennent la classe de l'officier (le cheval vient avec la classe).
            if (cavalry) playerClass = officer.Class;
            else if (!CommanderConfig.AllowedClasses.Contains(playerClass))
            {
                notice = " Classe " + GameIds.ClassName(playerClass) + " non permise : " + GameIds.ClassName(CommanderConfig.DefaultClass) + ".";
                playerClass = CommanderConfig.DefaultClass;
            }
            // Uniforme donné à la levée : vérifié, puis retenu comme choix de l'officier pour cette classe (renforts compris).
            // 0 : retour à l'uniforme par défaut, comme !skin.
            if (uniform == 0) officer.Skins.Remove(playerClass);
            else if (uniform > 0)
            {
                if (!Uniforms.IsValid(officer.Faction, playerClass, uniform))
                    return "Uniforme " + uniform + " inconnu pour " + GameIds.ClassName(playerClass) + " (" + GameIds.FactionName(officer.Faction)
                           + "). !skins " + GameIds.ClassName(playerClass) + " pour la liste.";
                officer.Skins[playerClass] = uniform;
            }

            if (c == null)
            {
                c = new Commander(officer.Id);
                Commanders[officer.Id] = c;
            }
            if (c.Line == null) c.Line = new Line(c, officer.Faction);
            c.HadLine = true;
            var line = c.Line;
            if (cavalry && !line.IsCavalry) line.Ranks = 1;
            line.IsCavalry = cavalry;
            line.RaisedClass = playerClass;
            if (!line.RaiseActive) { line.RaiseOk = 0; line.RaiseFailed = 0; }
            line.RaiseActive = true;
            if (namePrefix != null) line.NamePrefix = namePrefix;

            // Les lignes des joueurs passent d'abord : place faite tout de suite aux dépens des lignes IA.
            MakeRoomForPlayers(n, Clock.Now);

            // Auxiliaires pris sur le nombre demandé, un seul de chaque par ligne.
            var total = line.Count + n;
            var wantFlag = !cavalry && CommanderConfig.Aux && total >= CommanderConfig.AuxMin && !HasRole(line, BotRole.FlagBearer);
            var wantDrum = !cavalry && CommanderConfig.Aux && total >= CommanderConfig.DrumMin && !HasRole(line, BotRole.Drummer);
            var wantMedic = cavalry ? 0 : MedicsWanted(line, total, true);
            for (var i = 0; i < n; i++)
            {
                Bot bot;
                if (wantMedic > 0 && !wantFlag && !wantDrum)
                {
                    bot = new Bot(line, BotRole.Surgeon, GameIds.PlayerClass.Surgeon, -1);
                    wantMedic--;
                }
                else if (wantFlag)
                {
                    bot = new Bot(line, BotRole.FlagBearer, GameIds.PlayerClass.FlagBearer, -1);
                    wantFlag = false;
                }
                else if (wantDrum)
                {
                    bot = new Bot(line, BotRole.Drummer, GameIds.PlayerClass.Musician, -1);
                    wantDrum = false;
                }
                else
                {
                    bot = new Bot(line, BotRole.Line, playerClass, -1);
                }
                line.Add(bot);
                Enqueue(bot);
            }

            CmdLog.Info(officer + " lève " + n + " bots (" + GameIds.ClassName(playerClass) + (uniform > 0 ? ", uniforme " + uniform : "")
                        + ", " + GameIds.FactionName(officer.Faction) + ") ; ligne " + line.Count + ", total " + TotalBots + "/"
                        + Cap + ".");
            var capped = n < count ? " (plafond : " + n + " sur " + count + ")" : "";
            return n + " hommes arrivent" + capped + ". Ligne : " + line.Count + "." + notice;
        }

        /// <summary>Médecins à ajouter : CMD:medecins par ligne dès CMD:medecinmin hommes, moins ceux déjà présents.</summary>
        private static int MedicsWanted(Line line, int total, bool withAux)
        {
            if (!withAux || !CommanderConfig.Aux || CommanderConfig.MedicMin <= 0 || total < CommanderConfig.MedicMin) return 0;
            var have = 0;
            foreach (var b in line.Bots) if (b.Role == BotRole.Surgeon) have++;
            return Mathf.Max(0, CommanderConfig.Medics - have);
        }

        private static bool HasRole(Line line, BotRole role)
        {
            foreach (var b in line.Bots) if (b.Role == role) return true;
            return false;
        }

        // ---- Événements des joueurs ----------------------------------------------------------

        /// <summary>
        /// Un joueur ou un bot vient d'apparaître (PlayerInfo déjà à jour). L'id de nos bots est connu dès leur création
        /// (OnPlayerConnected pendant spawnSpecific) : ici, on vérifie la faction et la classe réelles et on prend le contrôle.
        /// </summary>
        public static void OnPlayerSpawned(PlayerInfo p, float now)
        {
            // Officier qui réapparaît dans une autre classe ou une autre faction alors qu'il a une ligne : il la perd.
            if (TryGetCommander(p.Id, out var c) && c.HasLine && !c.IsAi
                && (!GameIds.CanLead(p.Class) || p.Faction != c.Line.Faction))
                OfficerLost(c, true, now);

            if (!p.IsBot) OnLeaderRespawned(p, now);

            if (!BotsByPlayer.TryGetValue(p.Id, out var bot)) return;
            AwaitingSpawn.Remove(bot);
            var line = bot.Line;

            if (line.Mode == LineMode.Dispersing || line.Ended || (line.Orphaned && !bot.Successor) || !line.Bots.Contains(bot))
            {
                // La ligne a pris fin pendant l'apparition : le bot vivant est retiré (sinon il resterait sans maître).
                line.Remove(bot);
                Retire(bot, now, true);
                return;
            }
            if (p.Faction != line.Faction)
            {
                // Faction absente de la manche : le jeu en a tiré une autre. Un bot dans l'autre camp ne sert à rien.
                CmdLog.Warn(bot + " apparu en " + GameIds.FactionName(p.Faction) + " au lieu de " + GameIds.FactionName(line.Faction) + ", retiré.");
                line.Remove(bot);
                Retire(bot, now, true);
                return;
            }
            // Bots tous au QG (CMD:spawnsection) : un bot apparu dans une autre section est relancé, même avec la bonne classe.
            var wrongSection = CommanderConfig.SpawnSection > 0 && p.SpawnSection != CommanderConfig.SpawnSection
                               && !bot.Successor && bot.Role != BotRole.Officer;
            if (wrongSection && p.Class == bot.Class)
            {
                if (bot.Attempts < MaxSectionAttempts)
                {
                    if (bot.Attempts < 3)
                        CmdLog.Info(bot + " apparu en section " + p.SpawnSection + " au lieu du QG (section " + CommanderConfig.SpawnSection
                                    + ") : retiré, nouvel essai " + (bot.Attempts + 1) + "/" + MaxSectionAttempts + ".");
                    Relaunch(bot, now);
                    return;
                }
                CmdLog.Info(bot + " toujours hors du QG après " + MaxSectionAttempts + " essais : gardé en section " + p.SpawnSection + ".");
            }
            if (p.Class != bot.Class && !bot.Successor && bot.Role != BotRole.Officer)
            {
                // spawnSpecific tire une section d'apparition au hasard dans la faction (aucun moyen de l'imposer) ; si la classe
                // n'y est pas permise, le jeu en prend une au hasard (Cannoneer en section d'artillerie…). On relance la place.
                var where = " (section " + p.SpawnSection + " : classe " + GameIds.ClassName(bot.Class) + " non permise)";
                if (bot.Attempts < MaxSectionAttempts)
                {
                    CmdLog.Info(bot + " apparu en " + GameIds.ClassName(p.Class) + where + " : retiré, nouvel essai "
                                + (bot.Attempts + 1) + "/" + MaxSectionAttempts + ".");
                    Relaunch(bot, now);
                    return;
                }
                if (bot.Role == BotRole.Line && IsMusketClass(p.Class))
                    CmdLog.Info(bot + " apparu en " + GameIds.ClassName(p.Class) + where + " après " + MaxSectionAttempts
                                + " essais : gardé (classe à mousquet voisine).");
                else
                {
                    CmdLog.Warn(bot + " apparu en " + GameIds.ClassName(p.Class) + where + " après " + MaxSectionAttempts
                                + " essais : place abandonnée.");
                    line.RaiseFailed++;
                    line.Remove(bot);
                    Retire(bot, now, true);
                    return;
                }
            }
            if (line.RaiseActive) line.RaiseOk++;
            // Quota par manche : chaque bot effectivement apparu dans la ligne d'un officier joueur (auxiliaires compris).
            if (!line.Owner.IsAi && !bot.Successor && bot.Role != BotRole.Officer) Quota.Consume(line.Owner.PlayerId);

            bot.Alive = true;
            bot.ResetSent();
            Formation.MeasureBody(p.Body);
            // État réel de l'arme à l'apparition : baïonnette au canon selon le réglage du jeu (StartBayonetAttached), chargée.
            bot.Bayonet = CommanderConfig.StartBayonet;
            bot.BayonetBusy = false;
            CmdLog.Info(bot.Name + " #" + p.Id + " apparu, " + GameIds.ClassName(p.Class) + " " + GameIds.FactionName(p.Faction)
                        + ", uniforme " + p.Uniform + " (ligne de " + line.Owner.Name + ").");
            BotCommands.TakeControl(p.Id);
            // Renfort : placé loin derrière l'officier, il rejoint sa place à pied. L'officier bot d'une ligne IA reste où il apparaît
            // (section d'apparition de son camp) : c'est le point de rassemblement de sa ligne.
            if (bot.Successor)
            {
                line.Sergeant = bot;
                // Placement juste après l'apparition (permis) : au centre de la ligne, sur un sol plausible (hauteur d'un bot
                // voisin) ; sinon sur le bot le plus proche du centre.
                if (TryLineFrame(line, out var successorCenter, out var successorYaw))
                {
                    var near = NearestLineBot(line, successorCenter, bot);
                    if (near != null && near.TryGetPosition(out var nearPos))
                    {
                        if (Ground.TryFind(successorCenter, nearPos.y, out var ground))
                            ServerConsole.TeleportToPosition(bot.PlayerId, ground + Vector3.up * 0.3f);
                        else ServerConsole.TeleportToPlayer(bot.PlayerId, near.PlayerId);
                    }
                    bot.SetYaw(successorYaw);
                }
            }
            else if (bot.Role != BotRole.Officer) PlaceNearOfficer(bot, line, bot.Reinforcement ? CommanderConfig.ReinforceDistance : 2f);
            if (bot.Role == BotRole.FlagBearer)
            {
                // Le drapeau du porte-drapeau est une ARME (WeaponType.BearingFlag, dans son inventaire : « Musket BearingFlag
                // SwordLong Pistol »), pas un objet porté : on ne jette rien (dropAllWeapons le perdrait) et on la passe en main
                // par switchWeapon, au plus tôt 1,2 s après l'apparition (le jeu refuse avant 1 s), avec relances (TickFlags).
                bot.FlagConfirmed = false;
                bot.FlagAttempts = 0;
                bot.FlagNextTry = now + 1.2f;
            }
            // Musicien : à l'apparition il ne tient rien (l'instrument se sort par une touche) ; le mod le lui met en main.
            // Le mousquet restait en main : à OnPlayerSpawned l'inventaire n'est pas encore utilisable (switchWeapon refusé avant
            // 1 s) et porter un tambour n'oblige pas à ranger l'arme (CarryableObjectManager.CanUseWeapon : le mousquet est
            // permis avec l'instrument). Après 1,2 s : dropAllWeapons, tambour en main, puis switchWeapon Unarmed (mains libres
            // pour les baguettes), relancé tant que OnPlayerWeaponSwitch ne confirme pas Unarmed (TickInstruments).
            if (bot.Role == BotRole.Drummer)
            {
                bot.InstrumentConfirmed = false;
                bot.MusicPlaying = false;
                bot.EquipAttempts = 0;
                bot.EquipRequestedAt = -1f;
                bot.UnarmedConfirmed = false;
                bot.UnarmedAttempts = 0;
                bot.UnarmedGaveUp = false;
                bot.MusicBuffActive = false;
                bot.UnarmedNextTry = now + 1.2f;
            }
            line.OnBotReady(bot, now);
            if (line.Owner.IsAi) AiDirector.OnBotSpawned(bot, now);
        }

        /// <summary>
        /// Réapparition d'un joueur qui a commandé une ligne. Officier d'infanterie dans la même faction : il retrouve
        /// automatiquement sa ligne restée sans officier (succession en cours). Autre classe : la ligne reste en succession
        /// (!reprendre plus tard). Ligne dispersée entre-temps : message clair, une fois.
        /// </summary>
        private static void OnLeaderRespawned(PlayerInfo p, float now)
        {
            if (!TryGetCommander(p.Id, out var c) || c.IsAi || c.HasLine) return;
            Line own = null;
            foreach (var l in Orphans) if (l.Owner == c && !l.Ended) { own = l; break; }
            if (own != null)
            {
                if (!GameIds.IsInfantryOfficer(p.Class) || p.Faction != own.Faction) return;
                Orphans.Remove(own);
                c.Line = own;
                own.Orphaned = false;
                own.Sergeant = null;
                own.FireAtWill = false;
                own.HasAimPoint = false;
                own.Halt(now);
                // Porte-drapeau vivant : l'officier réapparaît à sa place et le porte-drapeau est tué (il « cède » sa place).
                Bot flag = null;
                foreach (var b in own.Bots) if (b.Role == BotRole.FlagBearer && b.Alive) { flag = b; break; }
                if (flag != null && flag.TryGetPosition(out var flagPos))
                {
                    ServerConsole.TeleportToPosition(p.Id, flagPos + Vector3.up * 0.3f);
                    BotCommands.Kill(flag.PlayerId);
                    CmdLog.Info(p + " réapparu à la place de son porte-drapeau " + flag.Name + " #" + flag.PlayerId + " (tué).");
                }
                CmdLog.Info(p + " réapparu : reprend sa ligne (" + own.Count + " bots).");
                ServerConsole.Private(p.Id, "Vous reprenez votre ligne (" + own.Count + " hommes). « Rompre et suivre » pour qu'elle vous rejoigne.");
                return;
            }
            if (!c.HadLine) return;
            c.HadLine = false;
            if (GameIds.CanLead(p.Class)) ServerConsole.Private(p.Id, "Votre ligne a été dispersée : !ligne pour en lever une nouvelle.");
        }

        /// <summary>
        /// Le bot apparaît sur une section au hasard : il est placé à distance derrière l'officier, à la hauteur de l'officier
        /// [TEST : sol en pente, obstacles, renforts à 30 m].
        /// </summary>
        private static void PlaceNearOfficer(Bot bot, Line line, float distance)
        {
            // Seule téléportation de déplacement gardée : le placement juste après l'apparition. Le jeu fait apparaître le bot sur
            // une section d'apparition AU HASARD de sa faction (CARBONPLAYERS.md §2.2), parfois à des centaines de mètres : sans
            // ce placement, la ligne mettrait des minutes à se former. Sol cherché sans les arbres (Ground) ; rien de plausible :
            // le bot est posé sur l'officier lui-même (sol sûr) plutôt qu'en l'air.
            var officer = Players.Get(line.Owner.PlayerId);
            if (officer == null || !officer.Alive || !officer.TryGetPosition(out var pos)) return;
            var yaw = officer.TryGetYaw(out var y) ? y : 0f;
            var spread = Mathf.Max(2f, distance * 0.2f);
            var target = pos - Formation.Forward(yaw) * distance + Formation.Right(yaw) * Random.Range(-spread, spread);
            var found = Ground.TryFind(target, pos.y, out var ground);
            if (found) ServerConsole.TeleportToPosition(bot.PlayerId, ground + Vector3.up * 0.5f);
            else ServerConsole.TeleportToPlayer(bot.PlayerId, officer.Id);
            BotCommands.Rotate(bot.PlayerId, yaw);
            // Contrôle 1 s après (TickPlacementChecks) : sous la carte → replacé une fois sur l'officier.
            bot.PlaceCheckAt = Clock.Now + 1f;
            bot.PlacedTargetY = found ? ground.y + 0.5f : pos.y;
            NotePlace(bot, "placé : y officier " + ServerConsole.Num(pos.y) + ", sol " + (found ? ServerConsole.Num(ground.y) : "introuvable (posé sur l'officier)"));
        }

        /// <summary>Bot vivant de la ligne le plus proche d'un point (référence de hauteur, point de repli), ou null.</summary>
        private static Bot NearestLineBot(Line line, Vector3 at, Bot except)
        {
            Bot best = null;
            var bestSq = float.MaxValue;
            foreach (var b in line.Bots)
            {
                if (b == except || !b.Alive || !b.TryGetPosition(out var p)) continue;
                var sq = Line.Flat(p - at).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = b;
            }
            return best;
        }

        // ---- Blocages et chutes (diagnostic, sans téléportation de déplacement) ---------------------

        private const int MaxStuckLogs = 30;
        private static int _stuckLogs;
        /// <summary>Compteurs de la manche : détours, renoncements, bots tombés sous la carte replacés.</summary>
        public static int DetourCount, GaveUpCount, RescueCount;

        public static void NoteDetour(Bot bot, float dist)
        {
            DetourCount++;
            if (_stuckLogs >= MaxStuckLogs) return;
            _stuckLogs++;
            CmdLog.Info(bot.Name + " #" + bot.PlayerId + " bloqué à " + ServerConsole.Num(dist) + " m de sa place : détour"
                        + (_stuckLogs == MaxStuckLogs ? " (journal des blocages arrêté pour cette manche)." : "."));
        }

        public static void NoteGaveUp(Bot bot)
        {
            GaveUpCount++;
            if (_stuckLogs >= MaxStuckLogs) return;
            _stuckLogs++;
            CmdLog.Info(bot.Name + " #" + bot.PlayerId + " recalcule son trajet après plusieurs détours ; sa place reste réservée.");
        }

        private static float _nextFallCheck;

        /// <summary>
        /// Seul recalage permis en jeu : un bot tombé sous la carte (plus de FallDepth m sous son chef) est replacé sur son chef
        /// (teleport sur un joueur, sol sûr). Un bot pris dans un arbre n'est pas déplacé : la gravité le fait redescendre.
        /// </summary>
        private const float FallDepth = 5f;

        private static int _placeLogs;

        private static void NotePlace(Bot bot, string text)
        {
            if (_placeLogs >= 60) return;
            _placeLogs++;
            CmdLog.Info("Placement : " + bot.Name + " #" + bot.PlayerId + " " + text + ".");
        }

        /// <summary>1 s après le placement : y réel ; plus de 3 m sous l'officier → replacé une fois sur lui (sol sûr).</summary>
        private static void TickPlacementChecks(float now)
        {
            foreach (var b in BotsByPlayer.Values)
            {
                if (b.PlaceCheckAt < 0f || now < b.PlaceCheckAt) continue;
                b.PlaceCheckAt = -1f;
                if (!b.Alive || !b.TryGetPosition(out var p)) continue;
                var officer = Players.Get(b.Line.Owner.PlayerId);
                var oy = officer != null && officer.Alive && officer.TryGetPosition(out var op) ? op.y : float.NaN;
                var under = !float.IsNaN(oy) && p.y < oy - 3f;
                NotePlace(b, "1 s après : y visé " + ServerConsole.Num(b.PlacedTargetY) + ", réel " + ServerConsole.Num(p.y)
                             + (float.IsNaN(oy) ? "" : ", officier " + ServerConsole.Num(oy)) + (under ? " → sous la carte, replacé sur l'officier" : ""));
                if (under && !b.FallRescued)
                {
                    b.FallRescued = true;
                    RescueCount++;
                    ServerConsole.TeleportToPlayer(b.PlayerId, officer.Id);
                }
            }
        }

        private static void TickFallen(float now)
        {
            TickPlacementChecks(now);
            if (now < _nextFallCheck) return;
            _nextFallCheck = now + 1f;
            foreach (var line in new List<Line>(AllActiveLines()))
            {
                var leader = line.Orphaned ? line.Sergeant?.Info : Players.Get(line.Owner.PlayerId);
                if (leader == null || !leader.Alive || !leader.TryGetPosition(out var lp)) continue;
                foreach (var b in line.Bots.ToArray())
                {
                    if (!b.Alive || b.PlayerId < 0 || b.PlayerId == leader.Id || !b.TryGetPosition(out var p)) continue;
                    if (p.y > lp.y - FallDepth || b.FallRescued) continue;
                    // Une seule fois par bot : sur une pente, un bot peut être légitimement plus bas que son chef.
                    b.FallRescued = true;
                    RescueCount++;
                    CmdLog.Info(b.Name + " #" + b.PlayerId + " tombé sous la carte (y " + ServerConsole.Num(p.y) + ") : replacé sur son chef.");
                    ServerConsole.TeleportToPlayer(b.PlayerId, leader.Id);
                }
            }
        }

        // Caméra complète des vrais joueurs (visée des lignes) : coûteuse, activée seulement tant qu'un joueur commande une ligne.
        private static int _cameraState = -1;
        private static float _nextCameraCheck;

        private static void TickCamera()
        {
            var want = 0;
            foreach (var c in Commanders.Values) if (!c.IsAi && c.HasLine) { want = 1; break; }
            if (want == _cameraState) return;
            _cameraState = want;
            ServerConsole.Execute("set sendFullCameraData " + (want == 1 ? "true" : "false"), "Visée officier");
        }

        /// <summary>Mort d'un joueur ou d'un bot (OnPlayerKilledPlayer, ou PV à 0).</summary>
        public static void OnPlayerDied(int playerId, float now)
        {
            // Le bot d'abord (l'officier d'une ligne IA est aussi un bot de sa ligne), puis l'officier.
            ReleasePatient(playerId, now);
            if (BotsByPlayer.TryGetValue(playerId, out var dying)) dying.HealTarget = -1;
            BotDied(playerId, now);
            if (TryGetCommander(playerId, out var c) && c.HasLine) OfficerLost(c, true, now);
        }

        private static void BotDied(int playerId, float now)
        {
            if (!BotsByPlayer.TryGetValue(playerId, out var bot) || !bot.Alive) return;
            bot.Alive = false;
            bot.ClearActions();
            // SetMovement(null) arrête aussi la boucle de frappes d'une charge.
            bot.SetMovement(null, now);
            if (bot.Parked)
            {
                Leftovers.Remove(bot);
                Retire(bot, now + DeadBotRemoveDelay);
                return;
            }
            var line = bot.Line;
            var sizeBefore = line.Count;
            line.Remove(bot);
            if (line.Mode != LineMode.Dispersing) Morale.OnLoss(line, sizeBefore, now);
            if (line.Sergeant == bot) PickSergeant(line);
            // Avec allowAutoRespawn false, le bot reste mort et connecté jusqu'au changement de manche.
            Retire(bot, now + DeadBotRemoveDelay);
        }

        /// <summary>Départ d'un joueur ou d'un bot.</summary>
        public static void OnPlayerLeft(int playerId, float now)
        {
            ReleasePatient(playerId, now);
            if (TryGetCommander(playerId, out var c))
            {
                // Officier joueur qui QUITTE le serveur (la mort et le changement de classe passent par OfficerLost) : pas de
                // succession, sa ligne se disperse puis tous ses bots sont retirés du serveur (kicks par lots, TickDispersing).
                if (c.IsAi)
                {
                    if (c.HasLine) OfficerLost(c, false, now);
                }
                else
                {
                    if (c.HasLine)
                    {
                        c.Line.KickAfterDisperse = true;
                        EndLine(c, "parti", now, false);
                    }
                    // Ligne laissée à un sergent (officier mort puis déconnecté) : même sort.
                    foreach (var orphan in Orphans.ToArray())
                    {
                        if (orphan.Owner != c || orphan.Ended) continue;
                        orphan.Ended = true;
                        orphan.KickAfterDisperse = true;
                        CmdLog.Info("Officier " + c.Name + " déconnecté : sa ligne tenue par un sergent se disperse (" + orphan.Count + " bots).");
                        DisperseLine(orphan, now);
                    }
                }
                Commanders.Remove(playerId);
            }
            if (BotsByPlayer.TryGetValue(playerId, out var bot)) Forget(bot);
        }

        /// <summary>Le bot n'est plus sur le serveur : retiré de partout, sans commande console.</summary>
        public static void ForgetMissing(int playerId)
        {
            if (!BotsByPlayer.TryGetValue(playerId, out var bot)) return;
            Forget(bot);
            Players.Remove(playerId);
        }

        private static void Forget(Bot bot)
        {
            bot.Alive = false;
            bot.HealTarget = -1;
            bot.MeleeExitAt = -1f;
            bot.Line.Remove(bot);
            if (bot.Line.Sergeant == bot) PickSergeant(bot.Line);
            AwaitingSpawn.Remove(bot);
            Leftovers.Remove(bot);
            if (BotsByPlayer.TryGetValue(bot.PlayerId, out var known) && known == bot) BotsByPlayer.Remove(bot.PlayerId);
        }

        /// <summary>
        /// Connexion sur un id déjà connu (réutilisé par le jeu : bot supprimé sans OnPlayerLeft). L'ancien bot est oublié, sans
        /// commande console (on peut être à l'intérieur d'un spawnSpecific).
        /// </summary>
        public static void ForgetStale(int playerId)
        {
            if (BotsByPlayer.TryGetValue(playerId, out var bot)) Forget(bot);
        }

        /// <summary>Un bot du mod est touché sans mourir : le moral de sa ligne baisse un peu.</summary>
        private const float MeleeContact = 3.5f;

        public static void OnBotHurt(int playerId, float now)
        {
            if (!BotsByPlayer.TryGetValue(playerId, out var bot) || !bot.Alive || bot.Line.Mode == LineMode.Dispersing) return;
            Morale.OnHit(bot.Line, now);
            // Ennemi au contact au moment du coup : attaque au corps à corps, la ligne se défend.
            if (bot.TryGetPosition(out var pos) && NearestEnemy(bot.Line.Faction, pos, MeleeContact) >= 0)
                bot.Line.DefendMelee(pos, now);
        }

        // ---- Fin de ligne et nettoyage -------------------------------------------------------

        /// <summary>
        /// Fin d'une ligne : dispersion pendant DisperseSeconds, puis les bots sont laissés sur place ou tués (CMD:tuerdisperses, voir TickDispersing). announce : mort ou départ de l'officier
        /// (annonce publique) ; sinon l'officier a renvoyé sa ligne.
        /// </summary>
        /// <summary>Renvoi volontaire : retrait des bots connectés, sans dispersion laissée sur la carte.</summary>
        public static int Dismiss(Commander commander, float now)
        {
            var line = commander.Line;
            if (line == null) return 0;
            var count = line.Count;
            commander.Line = null;
            line.Ended = true;
            line.FireAtWill = false;
            DropPending(line, now);
            Orphans.Remove(line);
            // Inclut les morts encore connectés (KickDead=false), pas seulement les survivants.
            var remove = new List<Bot>();
            foreach (var bot in BotsByPlayer.Values) if (bot.Line == line) remove.Add(bot);
            foreach (var bot in remove) DropBot(bot, now);
            foreach (var bot in line.Bots.ToArray())
            {
                line.Remove(bot); // demandes d'apparition annulées par Ended + retrait de la ligne
                if (bot.PlayerId >= 0) Retire(bot, now, true);
            }
            CmdLog.Info("Ligne de " + commander.Name + " renvoyée : retrait des bots connectés.");
            return count;
        }

        public static int EndLine(Commander c, string reason, float now, bool announce)
        {
            var line = c.Line;
            c.Line = null;
            if (line == null) return 0;
            line.Ended = true;
            var n = line.Count;
            CmdLog.Info("Fin de la ligne de " + c.Name + " (" + reason + ") : " + n + " bots.");
            if (announce && !c.IsAi && line.Count > 0) ServerConsole.Broadcast("L'officier " + c.Name + " est " + reason + " : sa ligne se disperse.");
            DisperseLine(line, now);
            return n;
        }

        /// <summary>Dispersion d'une ligne (avec ou sans officier) : les bots pas encore apparus sont retirés tout de suite.</summary>
        private static void DisperseLine(Line line, float now)
        {
            Orphans.Remove(line);
            line.Orphaned = false;
            line.Sergeant = null;
            DropPending(line, now);
            if (line.Count == 0) return;
            line.Disperse(now);
            Dispersing.Add(line);
        }

        private static void DropPending(Line line, float now)
        {
            foreach (var b in line.Bots.ToArray())
            {
                if (b.Alive) continue;
                line.Remove(b);
                AwaitingSpawn.Remove(b);
                if (b.PlayerId >= 0) Retire(b, now, true);
            }
        }

        // ---- Succession (lot A, n° 3) ---------------------------------------------------------

        /// <summary>
        /// Mort (died) ou départ de l'officier. Si la ligne compte assez de bots vivants, un sergent bot la tient : recul en ordre,
        /// feu à volonté, en attendant qu'un officier du camp la reprenne ; sinon, dispersion.
        /// </summary>
        private static void OfficerLost(Commander c, bool died, float now)
        {
            var line = c.Line;
            if (line == null) return;
            var reason = died ? "tombé" : "parti";
            if (died) Morale.OnOfficerLost(line, now);
            var alive = 0;
            foreach (var b in line.Bots) if (b.Alive && b.IsLineSoldier) alive++;
            if (!CommanderConfig.Succession || alive < CommanderConfig.SuccessionMin || line.Morale <= 0f)
            {
                EndLine(c, reason, now, died);
                return;
            }

            c.Line = null;
            DropPending(line, now);
            line.Orphaned = true;
            line.SuccessionEndsAt = now + CommanderConfig.SuccessionSeconds;
            Orphans.Add(line);
            PickSergeant(line);
            // Un vrai bot de classe Sergeant, en remplacement d'un soldat pour garder le plafond.
            if (!c.IsAi && line.Sergeant != null && line.Sergeant.Class != GameIds.PlayerClass.Sergeant)
            {
                var previous = line.Sergeant;
                DropBot(previous, now);
                var successor = new Bot(line, BotRole.Line, GameIds.PlayerClass.Sergeant, -1)
                { Successor = true, SpawnAfter = now + 2f };
                line.Add(successor);
                Enqueue(successor);
                PickSergeant(line);
            }
            CmdLog.Info("Officier " + c.Name + " " + reason + " : le sergent " + line.Sergeant + " tient sa ligne (" + line.Count + " bots).");

            // Recul en ordre, face à l'ennemi, puis la ligne tient et tire à volonté. Sans repère (aucune position) : halte.
            var hasFrame = TryLineFrame(line, out var center, out var yaw);
            if (hasFrame) line.FormAt(center - Formation.Forward(yaw) * CommanderConfig.SuccessionRetreat, yaw, now);
            else line.Halt(now);
            line.StartFireAtWill(now);
            if (line.Sergeant != null) line.Sergeant.Shout(GameIds.CharacterVoicePhrase.Retreat);

            if (c.IsAi) return;
            // Seulement s'il est mort (parti : il n'est plus sur le serveur).
            if (died) ServerConsole.Private(c.PlayerId, "Un sergent tient votre ligne : réapparaissez en officier dans les "
                + Mathf.RoundToInt(CommanderConfig.SuccessionSeconds) + " s pour la retrouver (en sergent : !reprendre).");
            if (died) ServerConsole.Broadcast("L'officier " + c.Name + " est tombé : un sergent tient sa ligne.");
            // Message privé (aussi envoyé au webhook Discord) : seulement aux officiers du camp assez proches pour la reprendre.
            // Mort : l'ancien officier est prioritaire pendant tout le délai (il la retrouve à sa réapparition) ; les autres
            // officiers ne sont prévenus que s'il est parti.
            if (hasFrame && !died) TellOfficers(line.Faction, c.PlayerId, center, OfficerNoticeRadius, "Ligne de " + c.Name + " sans officier (" + line.Count + " hommes) : approchez à "
                + Mathf.RoundToInt(CommanderConfig.TakeoverRadius) + " m et tapez !reprendre (ou criez Follow) dans les "
                + Mathf.RoundToInt(CommanderConfig.SuccessionSeconds) + " s.");
        }

        /// <summary>Sergent : de préférence un bot de classe Sergeant, sinon le soldat le plus proche du centre de la ligne.</summary>
        private static void PickSergeant(Line line)
        {
            line.Sergeant = null;
            if (!line.Orphaned) return;
            var hasCenter = TryLineFrame(line, out var center, out _);
            var best = float.MaxValue;
            foreach (var b in line.Bots)
            {
                if (!b.Alive || b.PlayerId < 0) continue;
                var info = b.Info;
                var score = info != null && info.Class == GameIds.PlayerClass.Sergeant ? -1f : float.MaxValue - 1f;
                if (score > 0f && hasCenter && b.TryGetPosition(out var pos)) score = Line.Flat(pos - center).sqrMagnitude;
                if (!b.IsLineSoldier) score += 1e6f;
                if (score >= best) continue;
                best = score;
                line.Sergeant = b;
            }
        }

        /// <summary>Centre et orientation de la ligne : l'ancre si elle est formée, sinon la moyenne des bots et le cap de son chef.</summary>
        private static bool TryLineFrame(Line line, out Vector3 center, out float yaw)
        {
            if (line.HasAnchor && line.IsFormation)
            {
                center = line.Anchor;
                yaw = line.Yaw;
                return true;
            }
            var sum = Vector3.zero;
            var n = 0;
            yaw = line.Yaw;
            foreach (var b in line.Bots)
            {
                if (!b.Alive || !b.TryGetPosition(out var pos)) continue;
                sum += pos;
                n++;
            }
            center = n > 0 ? sum / n : Vector3.zero;
            // Cap du chef : le sergent d'une ligne sans officier, sinon l'officier.
            var info = line.Orphaned ? line.Sergeant?.Info : Players.Get(line.Owner.PlayerId);
            if (info != null && info.TryGetYaw(out var y)) yaw = y;
            return n > 0;
        }

        /// <summary>
        /// Un officier reprend la ligne sans officier la plus proche de son camp, à moins de TakeoverRadius. follow : la ligne le suit.
        /// silent : pas de réponse en cas d'échec (reprise par une phrase vocale). Renvoie le message pour l'officier, ou null.
        /// </summary>
        public static string TryTakeOver(PlayerInfo officer, bool follow, bool silent, float now)
        {
            if (!officer.Alive || !GameIds.CanLead(officer.Class))
                return silent ? null : "Seul un officier ou un sergent en vie peut reprendre une ligne.";
            var c = GetCommander(officer.Id);
            if (c != null && c.HasLine) return silent ? null : "Vous commandez déjà une ligne.";
            if (!officer.TryGetPosition(out var pos)) return silent ? null : "Position inconnue, réessayez.";

            Line found = null;
            var best = CommanderConfig.TakeoverRadius * CommanderConfig.TakeoverRadius;
            foreach (var line in Orphans)
            {
                // Une ligne IA sans officier ne se reprend pas.
                if (line.Owner.IsAi || line.Faction != officer.Faction) continue;
                // Priorité à l'ancien officier tant qu'il est sur le serveur (délai de succession).
                if (line.Owner.PlayerId != officer.Id && Players.Get(line.Owner.PlayerId) != null) continue;
                foreach (var b in line.Bots)
                {
                    if (!b.Alive || !b.TryGetPosition(out var bp)) continue;
                    var d = Line.Flat(bp - pos).sqrMagnitude;
                    if (line.Owner.PlayerId == officer.Id) d = -1f;
                    if (d > best) continue;
                    best = d;
                    found = line;
                }
            }
            if (found == null) return silent ? null : "Aucune ligne sans officier à moins de " + Mathf.RoundToInt(CommanderConfig.TakeoverRadius) + " m.";

            var previous = found.Owner.Name;
            // Au-delà du plafond par officier, les hommes les plus éloignés se dispersent.
            var excess = found.Count - CommanderConfig.EffectiveMaxPerOfficer;
            if (excess > 0)
            {
                var split = new Line(found.Owner, found.Faction) { NamePrefix = found.NamePrefix };
                var byDistance = new List<Bot>(found.Bots);
                byDistance.Sort((a, b) => DistanceTo(b, pos).CompareTo(DistanceTo(a, pos)));
                for (var i = 0; i < excess && i < byDistance.Count; i++)
                {
                    found.Remove(byDistance[i]);
                    byDistance[i].Line = split;
                    split.Add(byDistance[i]);
                }
                DisperseLine(split, now);
            }

            Orphans.Remove(found);
            if (c == null)
            {
                c = new Commander(officer.Id);
                Commanders[officer.Id] = c;
            }
            c.Line = found;
            c.HadLine = true;
            found.Owner = c;
            found.HasAimPoint = false;
            found.Orphaned = false;
            found.Sergeant = null;
            found.FireAtWill = false;
            if (follow) found.Follow(now);
            else found.Halt(now);
            CmdLog.Info(officer + " reprend la ligne de " + previous + " (" + found.Count + " bots" + (excess > 0 ? ", " + excess + " dispersés" : "") + ").");
            return "Vous prenez la ligne de " + previous + " : " + found.Count + " hommes" + (excess > 0 ? " (" + excess + " au-delà de votre plafond se dispersent)" : "") + ".";
        }

        private static float DistanceTo(Bot bot, Vector3 pos)
        {
            return bot.TryGetPosition(out var p) ? Line.Flat(p - pos).sqrMagnitude : float.MaxValue;
        }

        /// <summary>Message privé aux officiers d'infanterie vivants d'une faction (sauf except) à moins de radius de center.</summary>
        private static void TellOfficers(int faction, int except, Vector3 center, float radius, string text)
        {
            var targets = new List<int>();
            foreach (var p in Players.All)
            {
                if (p.IsBot || !p.Alive || p.Id == except || p.Faction != faction || !GameIds.CanLead(p.Class)) continue;
                if (!p.TryGetPosition(out var pos) || Line.Flat(pos - center).sqrMagnitude > radius * radius) continue;
                targets.Add(p.Id);
            }
            // Copie : chaque message est une commande console.
            foreach (var id in targets) ServerConsole.Private(id, text);
        }

        private static void TickOrphans(float now)
        {
            for (var i = Orphans.Count - 1; i >= 0; i--)
            {
                var line = Orphans[i];
                if (line.Count == 0)
                {
                    // Ligne vidée : terminée (une ligne IA libère ainsi sa vague).
                    line.Ended = true;
                    Orphans.RemoveAt(i);
                    continue;
                }
                if (now < line.SuccessionEndsAt) continue;
                CmdLog.Info("Personne n'a repris la ligne de " + line.Owner.Name + " : dispersion.");
                DisperseLine(line, now);
            }
        }

        // ---- Moral (lot A, n° 4) --------------------------------------------------------------

        /// <summary>Ralliement d'une ligne ; renvoie faux pendant le délai de recharge.</summary>
        public static bool Rally(Line line, string source, float now)
        {
            if (line == null || line.Mode == LineMode.Dispersing || !Morale.Rally(line, now)) return false;
            CmdLog.Info("Ralliement de la ligne de " + line.Owner.Name + " (" + source + ") : moral " + Mathf.RoundToInt(line.Morale) + ".");
            return true;
        }

        /// <summary>Ordre SergeantRally d'un sergent joueur : les lignes de son camp à moins de RallyRadius se rallient.</summary>
        public static void OnSergeantRally(int sergeantId, Vector3 position, float now)
        {
            var sergeant = Players.Get(sergeantId);
            if (sergeant == null || sergeant.IsBot) return;
            if (sergeant.TryGetPosition(out var pos)) position = pos;
            var radiusSq = CommanderConfig.RallyRadius * CommanderConfig.RallyRadius;
            foreach (var line in AllActiveLines())
            {
                if (line.Faction != sergeant.Faction) continue;
                foreach (var b in line.Bots)
                {
                    if (!b.Alive || !b.TryGetPosition(out var bp) || Line.Flat(bp - position).sqrMagnitude > radiusSq) continue;
                    Rally(line, "sergent " + sergeant.Name, now);
                    break;
                }
            }
        }

        /// <summary>Buff SergeantLineRally reçu par un bot du mod : sa ligne se rallie (une fois par délai de recharge).</summary>
        public static void OnRallyBuff(int botId, float now)
        {
            if (BotsByPlayer.TryGetValue(botId, out var bot) && bot.Alive) Rally(bot.Line, "buff SergeantLineRally", now);
        }

        /// <summary>Remontée du moral, recul sous le seuil, dispersion à 0.</summary>
        // Listes de travail réutilisées à chaque image (moins d'allocations, donc moins de passages du ramasse-miettes Mono).
        private static readonly List<Line> MoraleScratch = new List<Line>();
        private static readonly List<Bot> BotScratch = new List<Bot>();
        private static readonly List<int> MeleeExit = new List<int>(2);

        private static void TickMorale(float now, float dt)
        {
            if (!Morale.Enabled) return;
            // Copie locale : une ligne en déroute envoie des commandes qui rappellent le mod.
            MoraleScratch.Clear();
            foreach (var l in AllActiveLines()) MoraleScratch.Add(l);
            foreach (var line in MoraleScratch)
            {
                if (line.Ended || line.Mode == LineMode.Dispersing) continue;
                Morale.Tick(line, now, dt);
                if (line.Morale <= 0f)
                {
                    CmdLog.Info("Ligne de " + line.Owner.Name + " en déroute.");
                    if (line.Orphaned) DisperseLine(line, now);
                    else
                    {
                        TellOwner(line, "Votre ligne est en déroute et se disperse.");
                        EndLine(line.Owner, "en déroute", now, false);
                    }
                    continue;
                }
                if (line.Morale >= CommanderConfig.MoraleWaver) continue;
                // La ligne flanche : elle recule, puis de nouveau toutes les 20 s tant que le moral reste bas.
                if (line.Wavering && now - line.LastWaverAt < 20f) continue;
                // Une ligne IA qui vient de charger va au bout de sa charge (au moins 5 s).
                if (line.Mode == LineMode.Charge && line.Owner.IsAi && now - line.ChargeStartedAt < 5f) continue;
                line.Wavering = true;
                line.LastWaverAt = now;
                if (!TryLineFrame(line, out var center, out var yaw))
                {
                    line.Halt(now);
                }
                else if (line.Mode == LineMode.Square)
                {
                    // Le carré n'est pas défait : il se reforme en arrière.
                    line.FormSquare(center - Formation.Forward(yaw) * CommanderConfig.MoraleRetreat, yaw, now);
                }
                else
                {
                    line.FormAt(center - Formation.Forward(yaw) * CommanderConfig.MoraleRetreat, yaw, now);
                }
                // Message une seule fois jusqu'au ralliement (chaque message privé part aussi au webhook Discord).
                if (!line.Orphaned && !line.WaverNotified)
                {
                    line.WaverNotified = true;
                    TellOwner(line, "Votre ligne flanche et recule (moral " + Mathf.RoundToInt(line.Morale) + "). Criez StandGround pour la rallier.");
                }
            }
        }

        private static IEnumerable<Line> AllActiveLines()
        {
            foreach (var c in Commanders.Values) if (c.HasLine) yield return c.Line;
            foreach (var l in Orphans) yield return l;
        }

        // ---- Renforts (lot A, n° 5) -----------------------------------------------------------

        /// <summary>Ordre « Renfort » (menu officier ou phrase Reinforce) : quelques bots rejoignent la ligne. Renvoie la réponse.</summary>
        public static string Reinforce(Commander c, float now)
        {
            if (!CommanderConfig.Reinforcements) return null;
            if (c == null || !c.HasLine) return null;
            var pause = Mathf.CeilToInt(c.RaisePausedUntil - now);
            if (pause > 0) return "Renforts en pause (apparitions refusées par le jeu) : réessayez dans " + pause + " s.";
            var wait = Mathf.CeilToInt(CommanderConfig.ReinforceCooldown - (now - c.LastReinforceAt));
            if (wait > 0) return "Renforts pas encore prêts : encore " + wait + " s.";
            var line = c.Line;
            var available = Mathf.Min(CommanderConfig.EffectiveMaxPerOfficer - line.Count, Cap - PlayerBots());
            if (available <= 0)
                return line.Count >= CommanderConfig.EffectiveMaxPerOfficer ? "Pas de renforts : votre ligne est au complet." : "Pas de renforts : plafond de bots du serveur atteint.";
            var quotaLeft = Quota.Remaining(c.PlayerId) - PendingPlaces(c);
            if (quotaLeft <= 0) return Quota.Refusal(c.PlayerId);
            var n = Mathf.Min(Mathf.Min(CommanderConfig.ReinforceCount, available), quotaLeft);
            MakeRoomForPlayers(n, now);
            for (var i = 0; i < n; i++)
            {
                var bot = new Bot(line, BotRole.Line, line.RaisedClass, -1) { Reinforcement = true };
                line.Add(bot);
                Enqueue(bot);
            }
            c.LastReinforceAt = now;
            CmdLog.Info(c.Name + " reçoit " + n + " renforts ; ligne " + line.Count + ", total " + TotalBots + "/" + CapText + ".");
            return n + " hommes en renfort arrivent par l'arrière.";
        }

        // ---- Remplissage (lot A, n° 11) -------------------------------------------------------

        /// <summary>
        /// Plafond baissé (vrais joueurs plus nombreux, charge du serveur, admin) : les levées et les renforts sont déjà refusés.
        /// Toutes les FillInterval secondes :
        /// - s'il reste des dispersés laissés sur place et que le total dépasse le plafond, l'un d'eux est retiré (kick) ;
        /// - sinon, si l'écart dépasse FillMargin : d'abord les demandes pas encore créées, puis les bots IA, puis les bots des
        ///   joueurs (le plus loin de son chef). Un bot à la fois, ou excès/5 quand c'est la charge qui limite.
        /// </summary>
        private static void TickFill(float now)
        {
            if (_roundOver) return;
            if (now < _nextFillRemoval) return;
            // Les bots en cours de dispersion vont disparaître d'eux-mêmes : ils ne comptent pas dans l'écart.
            var excess = ActiveBots() - Cap;
            if (excess <= 0) return;

            if (Leftovers.Count > 0)
            {
                _nextFillRemoval = now + CommanderConfig.FillInterval;
                var left = Leftovers[0];
                Leftovers.RemoveAt(0);
                left.Alive = false;
                Retire(left, now, true);
                CmdLog.Info("Plafond " + Cap + " dépassé : " + left + " (dispersé laissé sur place) retiré.");
                return;
            }
            // Bots IA : retirés dès que le total dépasse le plafond (pas de marge) ; bots des joueurs : au-delà de FillMargin.
            var ai = AiBots() > 0 || AiSpawnQueue.Count > 0;
            if (!ai && excess <= CommanderConfig.FillMargin) return;
            _nextFillRemoval = now + CommanderConfig.FillInterval;

            // Plus vite quand c'est la charge du serveur qui limite.
            var count = LoadMonitor.Cap < CommanderConfig.CapFor(Players.RealPlayerCount()) ? Mathf.Max(1, excess / 5) : 1;
            for (var k = 0; k < count; k++)
            {
                if (!RemoveOneForCap(now, excess, ai)) break;
            }
        }

        /// <summary>Retire une demande en file ou un bot de la catégorie donnée (IA ou joueurs). Faux s'il n'y a plus rien.</summary>
        private static bool RemoveOneForCap(float now, int excess, bool ai)
        {
            // Demandes en file : annulées sans rien envoyer au jeu.
            if (CancelQueued(ai))
            {
                CmdLog.Info("Plafond " + Cap + " dépassé : une demande " + (ai ? "IA " : "") + "en attente annulée.");
                return true;
            }
            var farthest = FarthestBot(ai);
            if (farthest == null) return false;
            var from = farthest.Line;
            // Retrait du serveur par kick (une seule commande, pas de slay ni de corps qui reste connecté), au débit limité.
            DropBot(farthest, now);
            CmdLog.Info("Plafond " + Cap + " dépassé de " + excess + " : " + farthest + " retiré de la ligne de " + from.Owner.Name + ".");
            // Message seulement au premier retrait d'une série.
            if (!from.Orphaned && now - from.LastCapRemovalAt > CommanderConfig.FillInterval * CapSeriesGap)
                TellOwner(from, "Plafond de bots abaissé (serveur plus rempli ou chargé) : des hommes quittent votre ligne.");
            from.LastCapRemovalAt = now;
            return true;
        }

        /// <summary>Annule la première demande d'apparition encore en file (lignes IA ou lignes des joueurs).</summary>
        private static bool CancelQueued(bool ai)
        {
            foreach (var queued in ai ? AiSpawnQueue : SpawnQueue)
            {
                if (queued.PlayerId >= 0 || !queued.Line.Bots.Contains(queued)) continue;
                // Les bots retirés de leur ligne sont ignorés au moment de leur tour dans la file.
                queued.Line.Remove(queued);
                return true;
            }
            return false;
        }

        /// <summary>Bot vivant le plus loin de son chef (officier, ou sergent d'une ligne sans officier), hors officier et sergent.</summary>
        private static Bot FarthestBot(bool ai)
        {
            Bot farthest = null;
            var best = -1f;
            foreach (var line in AllActiveLines())
            {
                if (line.Owner.IsAi != ai) continue;
                var leader = line.Orphaned ? line.Sergeant?.Info : Players.Get(line.Owner.PlayerId);
                if (leader == null || !leader.TryGetPosition(out var lp)) continue;
                foreach (var b in line.Bots)
                {
                    if (!b.Alive || b == line.Sergeant || b.Role == BotRole.Officer) continue;
                    var d = DistanceTo(b, lp);
                    if (d == float.MaxValue || d <= best) continue;
                    best = d;
                    farthest = b;
                }
            }
            return farthest;
        }

        /// <summary>
        /// Oublie tous les bots du mod. kick faux (fin ou début de manche) : le jeu les supprime lui-même au changement de manche,
        /// aucun kick n'est envoyé. kick vrai (commande admin) : chaque bot est retiré du serveur.
        /// </summary>
        public static void Clear(string reason, bool kick)
        {
            // Copie d'abord, état vidé ensuite, kick en dernier : chaque kick rappelle OnPlayerLeft de façon synchrone.
            var toKick = new List<int>();
            // Les retraits prévus sont encore dans BotsByPlayer (Retire les y garde jusqu'au kick).
            if (kick)
            {
                foreach (var pair in BotsByPlayer) toKick.Add(pair.Key);
                foreach (var k in KickQueue) if (k.PlayerId >= 0 && !toKick.Contains(k.PlayerId)) toKick.Add(k.PlayerId);
            }
            var n = BotsByPlayer.Count;
            var pending = SpawnQueue.Count + AiSpawnQueue.Count;
            Leftovers.Clear();
            Commanders.Clear();
            Orphans.Clear();
            AiDirector.Forget(Clock.Now);
            BotsByPlayer.Clear();
            Dispersing.Clear();
            SpawnQueue.Clear();
            AiSpawnQueue.Clear();
            RelaunchQueue.Clear();
            AwaitingSpawn.Clear();
            KickQueue.Clear();
            Removals.Clear();
            TickList.Clear();
            _cursor = 0;
            if (n > 0 || pending > 0)
                CmdLog.Info("Nettoyage (" + reason + ") : " + n + " bots " + (kick ? "retirés" : "oubliés") + ", " + pending + " demandes annulées.");
            // Kicks au débit limité (TickRemovals). Fin ou début de manche : le jeu supprime lui-même les bots, rien à kicker.
            if (kick)
            {
                foreach (var id in toKick) if (!ClearedKicks.Contains(id)) ClearedKicks.Add(id);
            }
            else ClearedKicks.Clear();
        }

        /// <summary>
        /// Le mod lâche un bot. Le retrait du serveur passe par un kick (journal admin, webhook Discord) : seulement si force
        /// (bot jamais apparu, mauvais camp) ou si CMD:kickdead est actif. Sinon le bot mort reste jusqu'au changement de manche.
        /// </summary>
        private static void Retire(Bot bot, float at, bool force = false)
        {
            bot.HealTarget = -1;
            bot.MeleeExitAt = -1f;
            if (bot.PlayerId < 0) return;
            // Bots des lignes IA : toujours retirés par kick (sinon leurs morts resteraient connectés vague après vague,
            // et occuperaient les places de section et de classe du camp).
            if (force || CommanderConfig.KickDead || bot.Line.Owner.IsAi)
            {
                foreach (var k in KickQueue) if (k == bot) return;
                // Pas de doublon : un slay rappelle OnPlayerHurt puis OnPlayerKilledPlayer, qui pourraient retirer deux fois.
                foreach (var r in Removals) if (r.Value == bot) return;
                Removals.Add(new KeyValuePair<float, Bot>(at, bot));
            }
            else if (BotsByPlayer.TryGetValue(bot.PlayerId, out var known) && known == bot)
            {
                BotsByPlayer.Remove(bot.PlayerId);
                CmdLog.Info(bot.Name + " #" + bot.PlayerId + " lâché par le mod (reste jusqu'à la fin de manche).");
            }
        }

        /// <summary>Objet porté (CarryableObjectType, en entier) → instrument du bot musicien.</summary>
        public static void OnBotCarry(int playerId, int carryable)
        {
            if (!BotsByPlayer.TryGetValue(playerId, out var bot)) return;
            var instrument = GameIds.InstrumentOfCarryable(carryable);
            if (instrument == GameIds.InstrumentType.None) return;
            bot.Instrument = instrument;
            bot.InstrumentConfirmed = true;
            bot.NextMusicRequest = 0f;
            NoteMusic(bot, "instrument détecté : " + GameIds.InstrumentName(instrument) + " (objet " + carryable + ")");
        }

        /// <summary>
        /// Met le tambour de la faction en main du musicien avec « equipCarryableItem » ; la confirmation arrive par OnPlayerStartCarry.
        /// </summary>
        private static void EquipInstrument(Bot bot, float now)
        {
            var carryable = GameIds.InstrumentCarryableFor(bot.Line.Faction, false);
            bot.EquipAttempts++;
            bot.EquipRequestedAt = now;
            BotCommands.EquipCarryable(bot.PlayerId, carryable);
            NoteMusic(bot, "instrument sorti : " + GameIds.CarryableName(carryable));
        }

        // ---- Médecins ----------------------------------------------------------------------------

        private static float _nextMedicTick;
        private static int _healLogs;
        private const float HealReach = 1.8f;       // distance de soin visée (le jeu accepte jusqu'à 4 m)
        private const float HealMaxTime = 8f;       // soin complet : 4 à 6 s selon le jeu
        private const float HealWalkMax = 20f;      // marche vers un blessé abandonnée au-delà
        private const float MedicLeash = 20f;       // le médecin ne s'éloigne pas de plus de 20 m de sa place
        private const float AllyRadius = 15f;       // vrais joueurs alliés soignés à moins de 15 m de la ligne

        private static void NoteHeal(Bot bot, string text)
        {
            if (_healLogs >= 40) return;
            _healLogs++;
            CmdLog.Info("Soins : " + bot.Name + " #" + bot.PlayerId + " " + text + ".");
        }

        /// <summary>
        /// Médecins : choisissent un blessé (bots de leur ligne, puis vrais joueurs alliés près de la ligne), y marchent, le
        /// soignent à l'arrêt (startHealing), puis reviennent à leur place. Jamais pendant une charge ou une dispersion.
        /// </summary>
        private static readonly List<Bot> MedicScratch = new List<Bot>();
        /// <summary>Patients abandonnés (marche trop longue, soin sans effet) : pas repris avant PatientCooldown s.</summary>
        private static readonly Dictionary<int, float> PatientCooldownUntil = new Dictionary<int, float>();
        private const float PatientCooldown = 15f;
        private const float StillBeforeHeal = 1f;

        private static void TickMedics(float now)
        {
            MedicScratch.Clear();
            foreach (var b in BotsByPlayer.Values) if (b.Role == BotRole.Surgeon && b.Alive) MedicScratch.Add(b);
            foreach (var medic in MedicScratch)
            {
                var line = medic.Line;
                var busyMode = line.Mode == LineMode.Charge || line.Mode == LineMode.Dispersing || line.Ended || !line.MedicOn || line.Celebrating;
                if (medic.HealTarget >= 0)
                {
                    var target = Players.Get(medic.HealTarget);
                    var healing = medic.HealStartedAt >= 0f;
                    var tp = Vector3.zero;
                    var mp = Vector3.zero;
                    var lost = busyMode || target == null || !target.Alive || !target.TryGetPosition(out tp) || !medic.TryGetPosition(out mp)
                               || Line.Flat(tp - medic.SlotPosition).magnitude > MedicLeash;
                    if (!lost && healing)
                    {
                        // Fin : soin confirmé par le jeu (OnScorableAction PlayerHealing → HealConfirmed), délai écoulé, ou blessé
                        // parti (> 4 m). Sans confirmation, une seule nouvelle tentative, puis le patient est mis de côté.
                        var far = Line.Flat(tp - mp).sqrMagnitude > 16f;
                        if (medic.HealConfirmed || now - medic.HealStartedAt >= HealMaxTime || far)
                        {
                            BotCommands.StopHealing(medic.PlayerId);
                            if (medic.HealConfirmed)
                            {
                                NoteHeal(medic, "a soigné " + target.Name + " #" + target.Id);
                                target.Hp = 100;
                                EndHeal(medic, line, now);
                            }
                            else if (!far && medic.HealTries < 2)
                            {
                                NoteHeal(medic, "aucun soin constaté sur " + target.Name + " #" + target.Id + " : nouvel essai");
                                medic.HealStartedAt = -1f;
                                medic.StillSince = now;
                            }
                            else
                            {
                                NoteHeal(medic, "abandonne " + target.Name + " #" + target.Id + (far ? " (blessé parti)" : " (le jeu refuse le soin)"));
                                PatientCooldownUntil[target.Id] = now + PatientCooldown;
                                EndHeal(medic, line, now);
                            }
                        }
                        continue;
                    }
                    if (lost || now - medic.HealWalkSince > HealWalkMax)
                    {
                        if (healing) BotCommands.StopHealing(medic.PlayerId);
                        if (target != null && !lost) PatientCooldownUntil[target.Id] = now + PatientCooldown;
                        EndHeal(medic, line, now);
                        continue;
                    }
                    // En marche vers le blessé : arrivé → arrêt, puis soin après StillBeforeHeal s d'immobilité (le jeu exige
                    // un soigneur stable).
                    if (Line.Flat(tp - mp).magnitude <= HealReach)
                    {
                        if (!(medic.Movement is StandStillTask)) { medic.SetMovement(new StandStillTask(), now); medic.StillSince = now; continue; }
                        if (now - medic.StillSince < StillBeforeHeal) continue;
                        medic.HealStartedAt = now;
                        medic.HealConfirmed = false;
                        medic.HealTries++;
                        BotCommands.StartHealing(medic.PlayerId, target.Id);
                        NoteHeal(medic, "soigne " + target.Name + " #" + target.Id + " (" + target.Hp + " PV)");
                    }
                    else if (medic.Movement is GoToPointTask go) go.Retarget(tp);
                    else medic.SetMovement(new GoToPointTask(tp, false), now);
                    continue;
                }
                // Pas de patient pendant que la ligne marche (le médecin ne la quitte pas en route).
                if (busyMode || line.IsMoving) continue;
                var pick = PickPatient(medic, line, now);
                if (pick == null || !pick.TryGetPosition(out var pos)) continue;
                medic.HealTarget = pick.Id;
                medic.HealWalkSince = now;
                medic.HealStartedAt = -1f;
                medic.HealTries = 0;
                medic.SetMovement(new GoToPointTask(pos, false), now);
            }
        }

        /// <summary>Soin terminé et compté par le jeu (OnScorableAction PlayerHealing du soigneur).</summary>
        public static void OnHealScored(int healerId)
        {
            if (BotsByPlayer.TryGetValue(healerId, out var bot) && bot.Role == BotRole.Surgeon && bot.HealTarget >= 0) bot.HealConfirmed = true;
        }

        /// <summary>Un joueur meurt ou s'en va : les médecins qui le visaient le lâchent.</summary>
        private static void ReleasePatient(int playerId, float now)
        {
            MedicScratch.Clear();
            foreach (var b in BotsByPlayer.Values) if (b.Role == BotRole.Surgeon && b.HealTarget == playerId) MedicScratch.Add(b);
            foreach (var m in MedicScratch)
            {
                if (m.HealStartedAt >= 0f && m.Alive) BotCommands.StopHealing(m.PlayerId);
                EndHeal(m, m.Line, now);
            }
        }

        private static void EndHeal(Bot medic, Line line, float now)
        {
            medic.HealTarget = -1;
            medic.HealStartedAt = -1f;
            medic.HealWalkSince = -1f;
            medic.HealConfirmed = false;
            if (!medic.Alive || !line.Bots.Contains(medic) || line.Mode == LineMode.Dispersing) return;
            if (line.Celebrating) medic.SetMovement(new StandStillTask(), now);
            else line.ResumeMovement(medic, now);
        }

        /// <summary>Blessé le plus atteint de la ligne (bots), sinon vrai joueur allié blessé à moins de AllyRadius m de la ligne.</summary>
        private static PlayerInfo PickPatient(Bot medic, Line line, float now)
        {
            PlayerInfo best = null;
            foreach (var b in line.Bots)
            {
                if (b == medic || !b.Alive || b.PlayerId < 0) continue;
                var p = b.Info;
                if (p == null || p.Hp >= 100 || !p.TryGetPosition(out var pos) || Line.Flat(pos - medic.SlotPosition).magnitude > MedicLeash) continue;
                if (TakenByOtherMedic(p.Id, medic) || OnCooldown(p.Id, now)) continue;
                if (best == null || p.Hp < best.Hp) best = p;
            }
            if (best != null) return best;
            foreach (var p in Players.All)
            {
                if (p.IsBot || !p.Alive || p.Faction != line.Faction || p.Hp >= 100 || !p.TryGetPosition(out var pos)) continue;
                if (Line.Flat(pos - medic.SlotPosition).magnitude > AllyRadius || TakenByOtherMedic(p.Id, medic) || OnCooldown(p.Id, now)) continue;
                if (best == null || p.Hp < best.Hp) best = p;
            }
            return best;
        }

        private static bool OnCooldown(int playerId, float now) => PatientCooldownUntil.TryGetValue(playerId, out var until) && now < until;

        private static bool TakenByOtherMedic(int playerId, Bot medic)
        {
            foreach (var b in BotsByPlayer.Values) if (b != medic && b.Alive && b.Role == BotRole.Surgeon && b.HealTarget == playerId) return true;
            return false;
        }

        // ---- Fête de victoire ----------------------------------------------------------------------

        private static int _attacker = -1, _defender = -1;
        private static int _winner = -1;
        private static bool _roundOver;
        private static float _nextVictoryCheck;
        private static int _celebrateCursor;
        private const int CelebratePerFrame = 4;
        private static readonly HashSet<int> SpawnedFactions = new HashSet<int>();
        private static readonly List<Line> CelebrationLines = new List<Line>();
        private static readonly int[] CelebrationGestures = { 75, 36, 35, 85, 55, 60, 59, 54 }; // Cheer, HipHop, GanGam, Jig, Silly, Swing, Funny, Wave
        private static readonly int[] CelebrationShouts = { GameIds.CharacterVoicePhrase.Cheer, GameIds.CharacterVoicePhrase.HurrahCheer,
            GameIds.CharacterVoicePhrase.PatrioticCheer, GameIds.CharacterVoicePhrase.Warcry };

        public static void OnRoundFactions(int attacker, int defender, int gameplayMode)
        {
            _attacker = attacker;
            _defender = defender;
            _winner = -1;
            _roundOver = false;
            _wipedSince = -1f;
            _wipedFaction = -1;
            _noRespawnMode = IsNoRespawnMode(gameplayMode);
            SpawnedFactions.Clear();
        }

        /// <summary>
        /// Modes où un camp entièrement mort ne revient pas (pas de vagues) : la fête peut partir en cours de manche. Ailleurs
        /// (vagues de réapparition), seulement à la fin de manche (OnRoundWinner). Valeurs de GameplayMode.
        /// </summary>
        private static bool IsNoRespawnMode(int mode)
        {
            switch (mode)
            {
                case 9:  // MeleeArena
                case 18: // CommanderBattles
                case 20: // Tournament
                case 22: // Linebattle
                case 26: // Scrim
                case 27: // Pickup
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Fin de manche : plus de créations ni de remplissage ; le camp vainqueur fête jusqu'à la manche suivante.</summary>
        public static void OnRoundWinner(int faction, float now)
        {
            _roundOver = true;
            SpawnQueue.Clear();
            AiSpawnQueue.Clear();
            RelaunchQueue.Clear();
            _spawnBudget = 0;
            LineList.Clear();
            foreach (var l in AllActiveLines()) LineList.Add(l);
            foreach (var l in LineList) DropPending(l, now);
            if (faction != _winner) StartCelebration(faction, now);
        }

        /// <summary>Vrai après la fin de manche (fête) : créations, remplissage et lignes IA arrêtés.</summary>
        public static bool RoundOver => _roundOver;

        private static void StartCelebration(int faction, float now)
        {
            if (faction == GameIds.FactionCountry.None) return;
            if (_winner >= 0) StopCelebration(now);
            _winner = faction;
            var n = 0;
            // Copies : les commandes envoyées peuvent rappeler le mod et modifier les lignes.
            CelebrationLines.Clear();
            foreach (var line in AllActiveLines())
                if (line.Faction == faction && !line.Ended && line.Mode != LineMode.Dispersing) CelebrationLines.Add(line);
            foreach (var line in CelebrationLines)
            {
                line.Celebrating = true;
                // Feu à volonté mémorisé (rétabli à la fin de la fête) ; arrêt du feu sans CeaseFire (qui l'efface).
                line.FireAtWillBeforeCelebration = line.FireAtWill;
                line.FireAtWill = false;
                BotScratch.Clear();
                BotScratch.AddRange(line.Bots);
                foreach (var b in BotScratch)
                {
                    if (!b.Alive || b.PlayerId < 0 || b.Role == BotRole.Officer) continue;
                    if (b.Role == BotRole.Surgeon && b.HealTarget >= 0) continue;
                    b.ClearActions(true);
                    b.Enqueue(new StopAimTask());
                    b.SetMovement(new StandStillTask(), now);
                    b.NextCelebrate = now + Random.Range(0.2f, 3f); // étalé : pas tous dans la même image
                    n++;
                }
            }
            CmdLog.Info("Victoire de " + GameIds.FactionName(faction) + " : " + n + " bots fêtent.");
        }

        /// <summary>
        /// Nouvelle manche : fête arrêtée AVANT d'oublier les bots. Les gestes de fête (danses) bouclent tant qu'on ne les arrête
        /// pas, et des bots peuvent rester d'une manche à l'autre : sans cet arrêt, ils continuaient d'acclamer à la manche suivante.
        /// </summary>
        public static void EndCelebrationForNewRound()
        {
            if (_winner < 0 && CelebrationLines.Count == 0) return;
            foreach (var b in BotsByPlayer.Values)
                if (b.PlayerId >= 0) BotCommands.Action(b.PlayerId, GameIds.PlayerActions.StopGestureAnimation);
            foreach (var line in AllActiveLines()) line.Celebrating = false;
            CelebrationLines.Clear();
            _winner = -1;
            CmdLog.Info("Nouvelle manche : fête arrêtée.");
        }

        private static void StopCelebration(float now)
        {
            CelebrationLines.Clear();
            foreach (var line in AllActiveLines()) if (line.Celebrating) CelebrationLines.Add(line);
            foreach (var line in CelebrationLines)
            {
                line.Celebrating = false;
                line.FireAtWill = line.FireAtWillBeforeCelebration;
                BotScratch.Clear();
                BotScratch.AddRange(line.Bots);
                foreach (var b in BotScratch)
                    if (b.Alive && b.PlayerId >= 0 && b.Role != BotRole.Officer && b.HealTarget < 0)
                        BotCommands.Action(b.PlayerId, GameIds.PlayerActions.StopGestureAnimation);
                if (!line.Ended && line.Mode != LineMode.Dispersing) line.ResumeAll(now);
            }
            CmdLog.Info("Fin de la fête : des ennemis sont revenus, les lignes reprennent leurs ordres.");
            _winner = -1;
        }

        private static float _wipedSince = -1f;
        private static int _wipedFaction = -1;
        private static bool _noRespawnMode;
        private const float WipeDelay = 15f;

        /// <summary>
        /// Toutes les secondes. En cours de manche, seulement dans un mode sans réapparition : un camp sans personne en vie
        /// pendant WipeDelay s (après y avoir vu une apparition) → l'autre fête ; ennemi revenu → fin de la fête. Sinon la fête
        /// ne part qu'à la fin de manche. Gestes et cris : au plus CelebratePerFrame bots par image, toutes les 4 à 8 s par bot.
        /// </summary>
        private static void TickCelebration(float now)
        {
            if (!CommanderConfig.Celebration) return;
            if (now >= _nextVictoryCheck && _attacker >= 0 && !_roundOver)
            {
                _nextVictoryCheck = now + 1f;
                int att = 0, def = 0;
                foreach (var p in Players.All)
                {
                    if (!p.Alive || p.Faction == GameIds.FactionCountry.None) continue;
                    SpawnedFactions.Add(p.Faction);
                    if (p.Faction == _attacker) att++;
                    else if (p.Faction == _defender) def++;
                }
                if (_winner < 0)
                {
                    var wiped = !_noRespawnMode ? -1
                        : att == 0 && def > 0 && SpawnedFactions.Contains(_attacker) ? _attacker
                        : def == 0 && att > 0 && SpawnedFactions.Contains(_defender) ? _defender : -1;
                    if (wiped < 0) { _wipedSince = -1f; _wipedFaction = -1; }
                    else if (wiped != _wipedFaction) { _wipedFaction = wiped; _wipedSince = now; }
                    else if (now - _wipedSince >= WipeDelay) StartCelebration(wiped == _attacker ? _defender : _attacker, now);
                }
                else
                {
                    var enemies = _winner == _attacker ? def : att;
                    if (enemies > 0) StopCelebration(now);
                }
            }
            if (_winner < 0) return;
            BotScratch.Clear();
            foreach (var b in BotsByPlayer.Values)
                if (b.Alive && b.Line.Celebrating && b.Role != BotRole.Officer && b.Role != BotRole.Drummer && b.HealTarget < 0
                    && now >= b.NextCelebrate) BotScratch.Add(b);
            if (BotScratch.Count == 0) return;
            if (_celebrateCursor >= BotScratch.Count) _celebrateCursor = 0;
            for (var k = 0; k < BotScratch.Count && k < CelebratePerFrame; k++)
            {
                var b = BotScratch[(_celebrateCursor + k) % BotScratch.Count];
                b.NextCelebrate = now + Random.Range(4f, 8f);
                BotCommands.Action(b.PlayerId, CelebrationGestures[Random.Range(0, CelebrationGestures.Length)]);
                if (Random.value < 0.35f) BotCommands.Shout(b.PlayerId, CelebrationShouts[Random.Range(0, CelebrationShouts.Length)]);
            }
            _celebrateCursor += CelebratePerFrame;
        }

        // ---- Témoin du musicien ------------------------------------------------------------------

        /// <summary>Buff Musician reçu par un de nos bots : le jeu joue bien le morceau (ServerInstrumentSongManager).</summary>
        public static void OnMusicBuff(int playerId)
        {
            if (!BotsByPlayer.TryGetValue(playerId, out var bot) || bot.Role != BotRole.Drummer) return;
            if (!bot.MusicBuffSeen) NoteMusic(bot, "buff Musician reçu : le morceau est joué");
            bot.MusicBuffSeen = true;
            bot.MusicBuffActive = true;
            bot.SongFailures = 0;
        }

        /// <summary>Fin du buff Musician (fin du morceau ou arrêt).</summary>
        public static void OnMusicBuffStop(int playerId)
        {
            if (BotsByPlayer.TryGetValue(playerId, out var bot)) bot.MusicBuffActive = false;
        }

        /// <summary>
        /// instrumentPlay répond toujours « ok ». Sans buff Musician 5 s après la demande, le jeu n'a pas lancé le morceau :
        /// on reprend tout le chemin (armes jetées, instrument remis en main, mains libres), puis le morceau est redemandé.
        /// </summary>
        private static void TickMusicWitness(float now)
        {
            foreach (var bot in BotsByPlayer.Values)
            {
                // Buff encore actif (relance pendant un morceau : pas de nouvel OnBuffStart) : le jeu joue, témoin satisfait.
                if (bot.Role != BotRole.Drummer || !bot.Alive || !bot.MusicPlaying || bot.MusicBuffSeen || bot.MusicBuffActive || bot.SongRequestedAt < 0f) continue;
                if (now - bot.SongRequestedAt < 5f) continue;
                bot.SongRequestedAt = -1f;
                bot.SongFailures++;
                NoteMusic(bot, "le jeu n'a pas lancé le morceau (pas de buff Musician après 5 s), essai " + bot.SongFailures);
                if (bot.SongFailures > 3) continue; // on cesse de reprendre le chemin ; la relance normale continue
                bot.MusicPlaying = false;
                bot.InstrumentConfirmed = false;
                bot.EquipAttempts = 0;
                bot.EquipRequestedAt = -1f;
                bot.UnarmedConfirmed = false;
                bot.UnarmedAttempts = 0;
                bot.UnarmedGaveUp = false;
                bot.UnarmedNextTry = now + 0.5f;
            }
        }

        /// <summary>Porte-drapeau sans drapeau confirmé en main : switchWeapon BearingFlag toutes les 2 s, 10 essais au plus.</summary>
        private static void TickFlags(float now)
        {
            BotScratch.Clear();
            BotScratch.AddRange(BotsByPlayer.Values);
            foreach (var bot in BotScratch)
            {
                if (bot.Role != BotRole.FlagBearer || !bot.Alive || bot.FlagConfirmed || bot.FlagNextTry < 0f || now < bot.FlagNextTry) continue;
                if (bot.FlagAttempts >= 10)
                {
                    bot.FlagNextTry = -1f;
                    CmdLog.Warn(bot + " : le jeu n'a pas confirmé le drapeau en main après 10 essais (BearingFlag absent de son inventaire ?).");
                    continue;
                }
                bot.FlagAttempts++;
                bot.FlagNextTry = now + 2f;
                BotCommands.SwitchWeapon(bot.PlayerId, "BearingFlag");
            }
        }

        /// <summary>Changement d'arme d'un bot : le porte-drapeau doit garder le drapeau, sinon il le reprend.</summary>
        public static void OnBotWeapon(int playerId, string weapon, float now)
        {
            if (!BotsByPlayer.TryGetValue(playerId, out var bot)) return;
            bot.Weapon = weapon;
            if (bot.Role == BotRole.Drummer)
            {
                var unarmed = weapon != null && weapon.Equals("Unarmed", System.StringComparison.OrdinalIgnoreCase);
                if (unarmed && !bot.UnarmedConfirmed)
                {
                    bot.UnarmedConfirmed = true;
                    bot.UnarmedNextTry = -1f;
                    bot.MusicPlaying = false; // relance du morceau, mains libres
                    NoteMusic(bot, "mains libres confirmées");
                }
                else if (!unarmed)
                {
                    bot.UnarmedConfirmed = false;
                    NoteMusic(bot, "arme reprise en main : " + weapon);
                    // Relance seulement si on n'a pas déjà renoncé (sinon boucle sans fin) : remis à faux à l'apparition ou par le témoin.
                    if (bot.UnarmedNextTry < 0f && !bot.UnarmedGaveUp) { bot.UnarmedAttempts = 0; bot.UnarmedNextTry = now + 0.5f; }
                }
                return;
            }
            if (bot.Role != BotRole.FlagBearer) return;
            var isFlag = weapon != null && weapon.IndexOf("BearingFlag", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (isFlag)
            {
                if (!bot.FlagConfirmed) CmdLog.Info(bot + " : drapeau en main.");
                bot.FlagConfirmed = true;
                bot.FlagNextTry = -1f;
                return;
            }
            bot.FlagConfirmed = false;
            if (bot.FlagNextTry < 0f) { bot.FlagAttempts = 0; bot.FlagNextTry = now + 1f; }
        }

        /// <summary>Musicien sans confirmation : réessaie le même tambour après 4 s, jusqu'à 5 tentatives.</summary>
        private static void TickInstruments(float now)
        {
            BotScratch.Clear();
            BotScratch.AddRange(BotsByPlayer.Values);
            foreach (var bot in BotScratch)
            {
                if (bot.Role == BotRole.Drummer && bot.Alive && !bot.UnarmedConfirmed && bot.UnarmedNextTry >= 0f && now >= bot.UnarmedNextTry)
                {
                    if (bot.UnarmedAttempts >= 10)
                    {
                        bot.UnarmedNextTry = -1f;
                        bot.UnarmedGaveUp = true;
                        NoteMusic(bot, "mains libres (Unarmed) non confirmées après 10 essais");
                    }
                    else
                    {
                        bot.UnarmedAttempts++;
                        bot.UnarmedNextTry = now + 2f;
                        // dropAllWeapons ne fait tomber que les armes (WeaponHolder.DropAllWeapons), pas l'instrument : une fois suffit.
                        if (bot.UnarmedAttempts == 1) BotCommands.DropAllWeapons(bot.PlayerId);
                        // Instrument déjà en main (celui de l'uniforme : fifre ou tambour) : on le garde, sinon le tambour de la faction.
                        if (bot.EquipAttempts == 0 && !bot.InstrumentConfirmed) EquipInstrument(bot, now);
                        BotCommands.SwitchWeapon(bot.PlayerId, "Unarmed");
                        NoteMusic(bot, "armes jetées, switchWeapon Unarmed (essai " + bot.UnarmedAttempts + ")");
                    }
                }
                if (bot.Role != BotRole.Drummer || !bot.Alive || bot.InstrumentConfirmed || bot.EquipRequestedAt < 0f) continue;
                if (now - bot.EquipRequestedAt < 4f) continue;
                if (bot.EquipAttempts >= 5)
                {
                    bot.EquipRequestedAt = -1f;
                    NoteMusic(bot, "échec : le jeu n'a confirmé aucun instrument après 5 tentatives, musique impossible");
                    continue;
                }
                EquipInstrument(bot, now);
            }
        }

        private static int _musicLogs;

        /// <summary>Journal musique, toujours actif mais limité (40 lignes par manche).</summary>
        public static void NoteMusic(Bot bot, string text)
        {
            if (_musicLogs >= 40) return;
            _musicLogs++;
            CmdLog.Info("Musique : " + bot.Name + " #" + bot.PlayerId + " " + text + ".");
        }

        // ---- Diagnostic -----------------------------------------------------------------------

        /// <summary>!cmd debug on|off : état de chaque bot des lignes, toutes les DebugInterval secondes, dans le journal [CMD].</summary>
        public static bool DebugEnabled;
        private const float DebugInterval = 2f;
        private static float _nextDebug;

        /// <summary>Un bot du mod a tiré (OnPlayerShoot) : l'arme est vide ; un tir à vide veut dire qu'elle n'était pas chargée.</summary>
        public static void OnBotShot(int playerId, bool dryShot)
        {
            if (!BotsByPlayer.TryGetValue(playerId, out var bot) || !bot.Alive) return;
            bot.Loaded = false;
            if (dryShot && DebugEnabled) CmdLog.Info(bot.Name + " #" + playerId + " : tir à vide (arme non chargée), rechargement.");
        }

        private static string ProgressText(Line line)
        {
            line.CountProgress(out var arrived, out var moving, out var yielding);
            return " (" + arrived + " arrivés, " + moving + " en route, " + yielding + " cèdent)";
        }

        private static void TickDebug(float now)
        {
            if (!DebugEnabled || now < _nextDebug) return;
            _nextDebug = now + DebugInterval;
            foreach (var line in AllActiveLines())
            {
                CmdLog.Info("Debug compteurs de la manche : " + DetourCount + " détours, " + GaveUpCount + " renoncements, " + RescueCount + " replacés (chute).");
                CmdLog.Info("Debug ligne de " + line.Owner.Name + " : " + Line.ModeName(line.Mode) + ", ancre " + ServerConsole.Num(line.Anchor.x)
                            + "," + ServerConsole.Num(line.Anchor.z) + " cap " + ServerConsole.Num(line.Yaw) + (line.IsMoving ? ", en mouvement" : "")
                            + ", " + line.Count + " bots" + ProgressText(line) + ".");
                foreach (var b in line.Bots)
                {
                    if (b.PlayerId < 0) continue;
                    var info = b.Info;
                    var hasPos = b.TryGetPosition(out var pos);
                    var realYaw = info != null && info.Body != null ? info.Body.transform.eulerAngles.y : float.NaN;
                    var dist = hasPos ? Line.Flat(b.SlotPosition - pos).magnitude : -1f;
                    CmdLog.Info("  n°" + b.Number + " " + b.Name + " #" + b.PlayerId + " " + Bot.RoleName(b.Role) + (b.Alive ? "" : " (pas en vie)")
                                + " tâche " + b.TaskSummary()
                                + " place (" + ServerConsole.Num(b.SlotLocal.x) + "," + ServerConsole.Num(b.SlotLocal.y) + ") " + ServerConsole.Num(b.SlotPosition.x) + "," + ServerConsole.Num(b.SlotPosition.z)
                                + " pos " + (hasPos ? ServerConsole.Num(pos.x) + "," + ServerConsole.Num(pos.z) : "?")
                                + " dist " + ServerConsole.Num(dist)
                                + " y visé " + ServerConsole.Num(b.SlotPosition.y) + " réel " + (hasPos ? ServerConsole.Num(pos.y) : "?")
                                + " détours " + b.Detours + ", cédé " + b.YieldCount + ", pas de côté " + b.StepAsideCount + (b.Line.IsCavalry ? ", allure " + b.HorseGear + " cheval " + b.VehicleId : "")
                                + (b.GaveUp ? ", a renoncé" : "") + (b.Crouched ? ", accroupi" : "")
                                + " cap visé " + ServerConsole.Num(b.SlotYaw) + " envoyé " + ServerConsole.Num(b.SentYaw)
                                + " réel " + ServerConsole.Num(realYaw)
                                + " arme " + (b.Loaded ? "chargée" : "vide") + (b.Aiming ? ", en joue" : "") + (b.Reloading ? ", recharge" : "")
                                + (b.Settled ? ", arrivé" : ""));
                }
            }
        }

        /// <summary>Ligne commandée et en service : ni terminée, ni sans officier, ni en dispersion.</summary>
        private static bool LineAlive(Line line)
        {
            return line != null && !line.Ended && !line.Orphaned && line.Mode != LineMode.Dispersing;
        }

        /// <summary>Message privé à l'officier de la ligne, s'il est un joueur (rien pour un officier bot).</summary>
        private static void TellOwner(Line line, string text)
        {
            if (line == null || line.Owner.IsAi || line.Owner.PlayerId < 0) return;
            ServerConsole.Private(line.Owner.PlayerId, text);
        }

        // ---- Lignes IA (lot C, n° 12) ----------------------------------------------------------

        /// <summary>Bots des lignes menées par l'IA (y compris sans officier ou en dispersion).</summary>
        public static int AiBots()
        {
            var n = 0;
            foreach (var c in Commanders.Values) if (c.IsAi && c.Line != null) n += c.Line.Count;
            foreach (var l in Orphans) if (l.Owner.IsAi) n += l.Count;
            foreach (var l in Dispersing) if (l.Owner.IsAi) n += l.Count;
            return n;
        }

        /// <summary>Places libres sous le plafond effectif, pour les lignes IA.</summary>
        public static int FreeSlots => Cap - TotalBots;

        /// <summary>
        /// Crée une ligne IA : d'abord son officier bot (ArmyInfantryOfficer de la faction). Les soldats sont demandés par
        /// AiDirector quand l'officier est apparu (EnqueueSoldiers), pour se rassembler autour de lui.
        /// </summary>
        public static Line CreateAiLine(int faction, float now)
        {
            var c = new Commander(-1) { IsAi = true };
            var line = new Line(c, faction);
            c.Line = line;
            var officer = new Bot(line, BotRole.Officer, GameIds.PlayerClass.ArmyInfantryOfficer, -1);
            line.Add(officer);
            Enqueue(officer);
            return line;
        }

        /// <summary>
        /// Ajoute n soldats à une ligne, en file d'apparition (avec porte-drapeau et tambour selon la taille si withAux ; les
        /// lignes IA n'en ont pas, pour ne pas prendre les places de classe limitées du camp).
        /// </summary>
        public static void EnqueueSoldiers(Line line, int n, int playerClass, bool withAux)
        {
            line.RaisedClass = playerClass;
            var total = line.Count + n;
            var wantFlag = withAux && CommanderConfig.Aux && total >= CommanderConfig.AuxMin && !HasRole(line, BotRole.FlagBearer);
            var wantDrum = withAux && CommanderConfig.Aux && total >= CommanderConfig.DrumMin && !HasRole(line, BotRole.Drummer);
            var wantMedic = MedicsWanted(line, total, withAux);
            for (var i = 0; i < n; i++)
            {
                Bot bot;
                if (wantMedic > 0 && !wantFlag && !wantDrum)
                {
                    bot = new Bot(line, BotRole.Surgeon, GameIds.PlayerClass.Surgeon, -1);
                    wantMedic--;
                }
                else if (wantFlag)
                {
                    bot = new Bot(line, BotRole.FlagBearer, GameIds.PlayerClass.FlagBearer, -1);
                    wantFlag = false;
                }
                else if (wantDrum)
                {
                    bot = new Bot(line, BotRole.Drummer, GameIds.PlayerClass.Musician, -1);
                    wantDrum = false;
                }
                else bot = new Bot(line, BotRole.Line, playerClass, -1);
                line.Add(bot);
                Enqueue(bot);
            }
        }

        /// <summary>
        /// Fin d'une ligne IA (retrait quand un joueur rejoint le camp, désactivation, abandon) : retrait direct par kick, au
        /// débit limité (pas de dispersion ni de slay). Les bots restent immobiles en attendant leur kick. L'entrée d'officier
        /// est retirée (un joueur qui reprendrait l'id n'hérite de rien).
        /// </summary>
        public static void EndAiLine(Line line, string reason, float now)
        {
            if (line == null || line.Ended) return;
            line.Ended = true;
            var owner = line.Owner;
            if (owner.Line == line) owner.Line = null;
            if (Commanders.TryGetValue(owner.PlayerId, out var known) && known == owner) Commanders.Remove(owner.PlayerId);
            Orphans.Remove(line);
            Dispersing.Remove(line);
            line.Orphaned = false;
            line.Sergeant = null;
            var n = 0;
            foreach (var b in line.Bots.ToArray())
            {
                AwaitingSpawn.Remove(b);
                if (b.PlayerId < 0)
                {
                    line.Remove(b);
                    continue;
                }
                DropBot(b, now);
                n++;
            }
            CmdLog.Info("Fin de la ligne IA " + GameIds.FactionName(line.Faction) + " (" + reason + ") : " + n + " bots à retirer.");
        }

        // ---- Boucle --------------------------------------------------------------------------

        public static void Tick(float now)
        {
            var dt = _lastTick < 0f ? 0f : Mathf.Clamp(now - _lastTick, 0f, 0.5f);
            _lastTick = now;

            if (now >= _nextCapUpdate)
            {
                _nextCapUpdate = now + 1f;
                LoadMonitor.Decide(now, ActiveBots());
                var cap = ComputeCap();
                if (_cap >= 0 && cap != _cap) CmdLog.Info("Plafond de bots : " + _cap + " → " + cap + " (" + Players.RealPlayerCount() + " joueurs, " + LoadMonitor.Status() + ").");
                _cap = cap;
            }

            if (!_roundOver) TickSpawns(now);
            TickRemovals(now);
            TickDispersing(now);
            TickOrphans(now);
            TickMorale(now, dt);
            TickFill(now);
            if (!_roundOver) AiDirector.Tick(now);
            TickDebug(now);
            TickFallen(now);
            TickInstruments(now);
            TickFlags(now);
            if (now >= _nextMedicTick) { _nextMedicTick = now + 0.5f; TickMedics(now); }
            TickMusicWitness(now);
            TickCelebration(now);
            if (now >= _nextCameraCheck)
            {
                _nextCameraCheck = now + 1f;
                TickCamera();
            }

            LineList.Clear();
            foreach (var l in Orphans) LineList.Add(l);
            foreach (var c in Commanders.Values) if (c.Line != null) LineList.Add(c.Line);
            foreach (var line in LineList) line.Tick(now, dt);

            TickBots(now);
        }

        /// <summary>Crée le prochain bot en file (joueurs avant lignes IA).</summary>
        private static void SpawnNext(float now)
        {
            // Les demandes des joueurs passent avant celles des lignes IA.
            var bot = RelaunchQueue.Count > 0 ? RelaunchQueue.Dequeue() : SpawnQueue.Count > 0 ? SpawnQueue.Dequeue() : AiSpawnQueue.Dequeue();
            var line = bot.Line;
            if (now < bot.SpawnAfter) Enqueue(bot);
            else if ((LineAlive(line) || (bot.Successor && line.Orphaned && !line.Ended)) && line.Bots.Contains(bot))
            {
                var officer = Players.Get(line.Owner.PlayerId);
                // Le tag de l'officier n'est appliqué que parce qu'un vrai joueur connecté (lui) le porte.
                var tag = officer != null ? officer.Regiment : "";
                // Uniforme choisi au moment de la création : un !skin donné entre-temps s'applique (lot B, n° 15).
                var uniform = bot.Uniform >= 1 ? bot.Uniform : Uniforms.Resolve(line.Owner, line.Faction, bot.Class);
                bot.Name = line.NextBotName(bot.Role);
                var id = BotCommands.Spawn(line.Faction, bot.Class, bot.Name, tag, uniform);
                if (id < 0)
                {
                    line.Remove(bot);
                }
                else
                {
                    bot.PlayerId = id;
                    bot.RequestedAt = now;
                    BotsByPlayer[id] = bot;
                    AwaitingSpawn.Add(bot);
                    Players.GetOrAdd(id).IsBot = true;
                    // Officier bot d'une ligne IA : la ligne est désormais connue sous son id.
                    if (bot.Role == BotRole.Officer && line.Owner.IsAi)
                    {
                        line.Owner.PlayerId = id;
                        Commanders[id] = line.Owner;
                    }
                }
            }
        }

        /// <summary>
        /// Créations ligne par ligne : toute la levée d'une ligne d'un coup, une ligne toutes les SpawnInterval secondes ;
        /// retrait des bots restés sans apparition.
        /// </summary>
        private static int _spawnBudget;
        private static int _kickBudget;

        /// <summary>Nombre de demandes consécutives de la même ligne en tête de file (une levée est mise en file d'un bloc).</summary>
        private static int HeadLineCount(Queue<Bot> queue)
        {
            Line head = null;
            var n = 0;
            foreach (var b in queue)
            {
                if (head == null) head = b.Line;
                else if (b.Line != head) break;
                if (++n >= 100) break;
            }
            return Mathf.Max(1, n);
        }

        private static void TickSpawns(float now)
        {
            ReportRaises();
            if ((SpawnQueue.Count > 0 || AiSpawnQueue.Count > 0) && now >= _nextSpawn)
            {
                _nextSpawn = now + CommanderConfig.SpawnInterval;
                // Ligne par ligne : tout ce qui attend pour la ligne en tête de file part d'un coup (plafonné à 100 par sécurité),
                // la ligne suivante à l'intervalle suivant.
                _spawnBudget = HeadLineCount(SpawnQueue.Count > 0 ? SpawnQueue : AiSpawnQueue);
            }
            // Au plus une création par image (chaque spawnSpecific instancie un personnage complet) : un lot de 15 apparaît en
            // une quinzaine d'images (~0,25 s), sans pic.
            if (RelaunchQueue.Count > 0)
            {
                FrameStats.Spawns++;
                SpawnNext(now);
            }
            else if (_spawnBudget > 0 && (SpawnQueue.Count > 0 || AiSpawnQueue.Count > 0))
            {
                _spawnBudget--;
                FrameStats.Spawns++;
                SpawnNext(now);
            }
            if (SpawnQueue.Count == 0 && AiSpawnQueue.Count == 0) _spawnBudget = 0;

            // Apparition refusée (places de section ou de classe) : ce bot est retiré (il resterait dans les limbes), le reste de
            // la demande de sa ligne est abandonné sans être créé, l'officier est prévenu une fois, et ses levées sont en pause.
            for (var i = AwaitingSpawn.Count - 1; i >= 0; i--)
            {
                var bot = AwaitingSpawn[i];
                if (now - bot.RequestedAt < SpawnTimeout) continue;
                AwaitingSpawn.RemoveAt(i);
                var line = bot.Line;
                // Section pleine pour la classe (spawn_override) : nouvel essai, une autre section sera peut-être tirée. Les
                // autres demandes continuent d'être servies.
                if (bot.Attempts < MaxSpawnAttempts && !bot.Successor && bot.Role != BotRole.Officer)
                {
                    CmdLog.Info(bot + " créé mais non apparu après " + SpawnTimeout + " s : retiré, nouvel essai "
                                + (bot.Attempts + 1) + "/" + MaxSpawnAttempts + ".");
                    Relaunch(bot, now);
                    continue;
                }
                line.Remove(bot);
                Retire(bot, now, true);
                line.RaiseFailed++;
                if (!bot.Successor && bot.Role != BotRole.Officer)
                {
                    CmdLog.Warn(bot + " jamais apparu après " + MaxSpawnAttempts + " essais : place abandonnée.");
                    continue;
                }
                var dropped = 0;
                foreach (var queued in line.Owner.IsAi ? AiSpawnQueue : SpawnQueue)
                {
                    if (queued.Line != line || queued.PlayerId >= 0 || !line.Bots.Contains(queued)) continue;
                    line.Remove(queued);
                    dropped++;
                }
                CmdLog.Warn(bot + " créé mais non apparu après " + SpawnTimeout + " s (places de section ou de classe ?) : retiré, "
                            + dropped + " demande(s) de la même ligne abandonnée(s).");
                var owner = line.Owner;
                if (now >= owner.RaisePausedUntil)
                {
                    owner.RaisePausedUntil = now + CommanderConfig.RaisePause;
                    if (LineAlive(line)) TellOwner(line, "Le jeu refuse l'apparition de vos hommes (places de classe ?). Levées en pause "
                                                         + Mathf.RoundToInt(CommanderConfig.RaisePause) + " s.");
                }
            }
        }

        /// <summary>
        /// Retraits échus → file de kicks ; au plus KickBatch kicks par KickInterval pour tout le mod (journal admin, webhook Discord,
        /// et le jeu rappelle OnPlayerLeft pendant chaque kick).
        /// </summary>
        private static void TickRemovals(float now)
        {
            for (var i = Removals.Count - 1; i >= 0; i--)
            {
                if (now < Removals[i].Key) continue;
                KickQueue.Add(Removals[i].Value);
                Removals.RemoveAt(i);
            }
            if (now >= _nextKickAt && _kickBudget <= 0 && (KickQueue.Count > 0 || ClearedKicks.Count > 0))
            {
                _kickBudget = KickBatch;
                _nextKickAt = now + KickInterval;
            }
            if (_kickBudget <= 0) return;
            // Au plus KickPerFrame kicks par image (le lot de KickBatch par seconde est étalé sur plusieurs images).
            var kicked = 0;
            while (KickQueue.Count > 0 && kicked < KickPerFrame && _kickBudget > 0)
            {
                var bot = KickQueue[0];
                KickQueue.RemoveAt(0);
                var id = bot.PlayerId;
                // Seulement si l'id appartient toujours à ce bot (un id peut être réutilisé) et que la fiche est celle d'un bot.
                if (!BotsByPlayer.TryGetValue(id, out var known) || known != bot) continue;
                BotsByPlayer.Remove(id);
                var p = Players.Get(id);
                if (p == null || !p.IsBot) continue;
                kicked++;
                _kickBudget--;
                FrameStats.Kicks++;
                CmdLog.Info(bot.Name + " #" + id + " retiré du serveur (kick).");
                BotCommands.Remove(id);
            }
            while (ClearedKicks.Count > 0 && kicked < KickPerFrame && _kickBudget > 0)
            {
                var id = ClearedKicks[0];
                ClearedKicks.RemoveAt(0);
                // Après !cmd vider/off : seulement si l'id est toujours un bot, et pas un nouveau bot du mod.
                var p = Players.Get(id);
                if (p == null || !p.IsBot || BotsByPlayer.ContainsKey(id)) continue;
                kicked++;
                _kickBudget--;
                FrameStats.Kicks++;
                BotCommands.Remove(id);
            }
            if (KickQueue.Count == 0 && ClearedKicks.Count == 0) _kickBudget = 0;
        }

        /// <summary>
        /// Fin de la dispersion. CMD:tuerdisperses vrai : chaque bot encore en vie est tué (slay : journal admin, webhook Discord).
        /// Faux (défaut) : les bots restent immobiles, ne sont plus pilotés ni comptés dans le moral, jusqu'à la fin de manche ;
        /// ils comptent toujours dans le total et sont retirés en premier si le plafond baisse (TickFill).
        /// </summary>
        private static void TickDispersing(float now)
        {
            for (var i = Dispersing.Count - 1; i >= 0; i--)
            {
                var line = Dispersing[i];
                if (now < line.DisperseEndsAt) continue;
                Dispersing.RemoveAt(i);
                // Copie : le slay rappelle le mod de façon synchrone.
                foreach (var b in line.Bots.ToArray())
                {
                    line.Remove(b);
                    if (b.PlayerId < 0) continue;
                    b.ClearActions();
                    // Ligne IA, ou officier déconnecté : toujours retirée par kick (tuerdisperses ne la concerne pas).
                    if (line.Owner.IsAi || line.KickAfterDisperse)
                    {
                        if (b.Alive) b.SetMovement(new StandStillTask(), now);
                        b.Alive = false;
                        Retire(b, now, true);
                        continue;
                    }
                    if (CommanderConfig.KillDispersed || !b.Alive)
                    {
                        var wasAlive = b.Alive;
                        // État mis à jour avant la commande : OnPlayerHurt synchrone ne doit pas retirer une deuxième fois.
                        b.Alive = false;
                        b.SetMovement(null, now);
                        if (wasAlive) BotCommands.Kill(b.PlayerId);
                        Retire(b, now + 1f);
                        continue;
                    }
                    b.SetMovement(new StandStillTask(), now);
                    b.Parked = true;
                    Leftovers.Add(b);
                }
            }
        }

        /// <summary>Pilotage en tourniquet : la charge de commandes est étalée sur plusieurs images.</summary>
        private static void TickBots(float now)
        {
            if (BotsByPlayer.Count == 0) return;
            TickList.Clear();
            foreach (var b in BotsByPlayer.Values) if (b.Alive && !b.Parked) TickList.Add(b);
            if (TickList.Count == 0) return;
            if (_cursor >= TickList.Count) _cursor = 0;

            var done = 0;
            for (var k = 0; k < TickList.Count && done < MaxBotTicksPerFrame; k++)
            {
                // Plafond de commandes console par image : les bots suivants (tourniquet) seront pilotés à l'image suivante.
                if (done >= MinBotTicksPerFrame && FrameStats.CommandsThisFrame >= FrameStats.CommandBudget) break;
                var bot = TickList[(_cursor + k) % TickList.Count];
                if (bot.MeleeExitAt >= 0f && (!bot.Alive || bot.PlayerId < 0)) bot.MeleeExitAt = -1f;
                if (bot.MeleeExitAt >= 0f)
                {
                    // Sortie de mêlée : paquet à part, puis seulement le bot reprend sa marche.
                    if (now < bot.MeleeExitAt) continue;
                    bot.MeleeExitAt = -1f;
                    MeleeExit.Clear();
                    MeleeExit.Add(GameIds.PlayerActions.StopMeleeWeaponHold);
                    MeleeExit.Add(GameIds.PlayerActions.DisableCombatStance);
                    BotCommands.Actions(bot.PlayerId, MeleeExit);
                    bot.NextTick = now + 0.15f;
                    done++;
                    continue;
                }
                if (now < bot.NextTick) continue;
                bot.Tick(now);
                // Bot arrivé à sa place, sans action en cours : piloté moins souvent.
                bot.NextTick = now + (bot.Settled && !bot.Line.Following && bot.Actions.Count == 0 ? SettledTickInterval : BotTickInterval);
                done++;
            }
            _cursor = (_cursor + done) % TickList.Count;
        }

        // ---- Outils --------------------------------------------------------------------------

        /// <summary>Joueur ennemi vivant le plus proche dans radius (bots ennemis compris), ou -1.</summary>
        public static int NearestEnemy(int faction, Vector3 position, float radius)
        {
            var best = -1;
            var bestSq = radius * radius;
            foreach (var p in Players.All)
            {
                if (!p.Alive || p.Faction == GameIds.FactionCountry.None || p.Faction == faction) continue;
                if (!p.TryGetPosition(out var pos)) continue;
                var sq = Line.Flat(pos - position).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = p.Id;
            }
            return best;
        }

        /// <summary>
        /// Carré : ennemi vivant le plus proche dans l'arc ±halfArc autour de faceYaw, à moins de range (cavalryRange pour Hussar
        /// et Dragoon), ou -1.
        /// </summary>
        public static int NearestEnemyInArc(int faction, Vector3 position, float faceYaw, float halfArc, float range, float cavalryRange)
        {
            var best = -1;
            var bestSq = float.MaxValue;
            var maxRange = Mathf.Max(range, cavalryRange);
            foreach (var p in Players.All)
            {
                if (!p.Alive || p.Faction == GameIds.FactionCountry.None || p.Faction == faction) continue;
                if (!p.TryGetPosition(out var pos)) continue;
                var d = Line.Flat(pos - position);
                var sq = d.sqrMagnitude;
                if (sq >= bestSq || sq > maxRange * maxRange) continue;
                var cavalry = p.Class == GameIds.PlayerClass.Hussar || p.Class == GameIds.PlayerClass.Dragoon;
                var r = cavalry ? cavalryRange : range;
                if (sq > r * r) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(faceYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg)) > halfArc) continue;
                bestSq = sq;
                best = p.Id;
            }
            return best;
        }

        public static string Status()
        {
            var lines = 0;
            foreach (var c in Commanders.Values) if (c.HasLine) lines++;
            return "Commander : " + (CommanderConfig.IsEnabled ? "actif" : "désactivé") + (CommanderConfig.IsWarmup ? " (manche d'échauffement)" : "") + ", " + lines + " ligne(s) + " + Orphans.Count
                   + " sans officier, " + TotalBots + "/" + CapText + " bots (" + Players.RealPlayerCount() + " joueurs ; "
                   + CommanderConfig.EffectiveMaxPerOfficer + " par officier), "
                   + (SpawnQueue.Count + AiSpawnQueue.Count + AwaitingSpawn.Count) + " en attente, " + Dispersing.Count + " en dispersion, "
                   + Leftovers.Count + " dispersés sur place, " + (Removals.Count + KickQueue.Count + ClearedKicks.Count) + " retraits prévus, " + ServerConsole.Sent + " commandes envoyées ; " + LoadMonitor.Status() + ".";
        }
    }
}
