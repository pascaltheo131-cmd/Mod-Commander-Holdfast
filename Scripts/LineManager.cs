using System.Collections.Generic;
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
        private const float SpawnTimeout = 15f;
        // Pilotage : chaque bot au plus toutes les BotTickInterval secondes, au plus MaxBotTicksPerFrame bots par image.
        private const float BotTickInterval = 0.1f;
        private const float SettledTickInterval = 0.4f;
        private const int MaxBotTicksPerFrame = 60;
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
        // Kicks à envoyer, au plus un par seconde pour tout le mod (KickInterval) : bots du mod (vérifiés au moment du kick),
        // et ids gardés après un !cmd vider/off (l'état est déjà vidé).
        private const float KickInterval = 1f;
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
            return Mathf.Min(CommanderConfig.CapFor(Players.RealPlayerCount()), LoadMonitor.Cap);
        }

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
        public static string Raise(PlayerInfo officer, int count, int playerClass, int uniform)
        {
            if (!CommanderConfig.IsEnabled) return "Les lignes de bots sont désactivées sur ce serveur.";
            if (!officer.Alive || !GameIds.IsInfantryOfficer(officer.Class)) return "Seul un officier d'infanterie en vie peut lever une ligne.";
            if (officer.Faction == GameIds.FactionCountry.None) return "Faction inconnue : réapparaissez puis réessayez.";
            // Pas d'uniforme d'infanterie de terre pour ces factions : le jeu tirerait la classe au hasard.
            if (officer.Faction == GameIds.FactionCountry.Privateer || officer.Faction == GameIds.FactionCountry.Spanish)
                return "Pas de ligne possible pour cette faction.";

            var c = GetCommander(officer.Id);
            if (c != null && c.IsAi) c = null;
            var pause = c != null ? Mathf.CeilToInt(c.RaisePausedUntil - Clock.Now) : 0;
            if (pause > 0) return "Levées en pause (apparitions refusées par le jeu) : réessayez dans " + pause + " s.";
            var current = c != null && c.Line != null ? c.Line.Count : 0;
            // Les lignes IA (lot C) et les dispersés laissés sur place passent après les lignes des joueurs : ils ne comptent
            // pas ici, TickFill les retire d'abord.
            var available = Mathf.Min(CommanderConfig.EffectiveMaxPerOfficer - current, Cap - PlayerBots());
            if (available <= 0)
            {
                return current > 0 && current >= CommanderConfig.EffectiveMaxPerOfficer
                    ? "Plafond atteint : votre ligne compte déjà " + current + " hommes."
                    : "Plafond de bots du serveur atteint (" + Cap + " avec " + Players.RealPlayerCount() + " joueurs).";
            }
            var n = Mathf.Min(count, available);

            var notice = "";
            if (!CommanderConfig.AllowedClasses.Contains(playerClass))
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
            var line = c.Line;
            line.RaisedClass = playerClass;

            // Les lignes des joueurs passent d'abord : place faite tout de suite aux dépens des lignes IA.
            MakeRoomForPlayers(n, Clock.Now);

            // Auxiliaires pris sur le nombre demandé, un seul de chaque par ligne.
            var total = line.Count + n;
            var wantFlag = CommanderConfig.Aux && total >= CommanderConfig.AuxMin && !HasRole(line, BotRole.FlagBearer);
            var wantDrum = CommanderConfig.Aux && total >= CommanderConfig.DrumMin && !HasRole(line, BotRole.Drummer);
            for (var i = 0; i < n; i++)
            {
                Bot bot;
                if (wantFlag)
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
                && (!GameIds.IsInfantryOfficer(p.Class) || p.Faction != c.Line.Faction))
                OfficerLost(c, true, now);

            if (!BotsByPlayer.TryGetValue(p.Id, out var bot)) return;
            AwaitingSpawn.Remove(bot);
            var line = bot.Line;

            if (line.Mode == LineMode.Dispersing || line.Orphaned || !LineAlive(line) || !line.Bots.Contains(bot))
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
            if (p.Class != bot.Class)
                CmdLog.Info(bot + " apparu en " + GameIds.ClassName(p.Class) + " (classe non permise dans sa section d'apparition).");

            bot.Alive = true;
            bot.ResetSent();
            CmdLog.Info(bot.Name + " #" + p.Id + " apparu, " + GameIds.ClassName(p.Class) + " " + GameIds.FactionName(p.Faction)
                        + ", uniforme " + p.Uniform + " (ligne de " + line.Owner.Name + ").");
            BotCommands.TakeControl(p.Id);
            // Renfort : placé loin derrière l'officier, il rejoint sa place à pied. L'officier bot d'une ligne IA reste où il apparaît
            // (section d'apparition de son camp) : c'est le point de rassemblement de sa ligne.
            if (bot.Role != BotRole.Officer) PlaceNearOfficer(bot, line, bot.Reinforcement ? CommanderConfig.ReinforceDistance : 2f);
            if (bot.Role == BotRole.FlagBearer) BotCommands.Equip(p.Id, "BearingFlag"); // [TEST]
            line.OnBotReady(bot, now);
            if (line.Owner.IsAi) AiDirector.OnBotSpawned(bot, now);
        }

        /// <summary>
        /// Le bot apparaît sur une section au hasard : il est placé à distance derrière l'officier, à la hauteur de l'officier
        /// [TEST : sol en pente, obstacles, renforts à 30 m].
        /// </summary>
        private static void PlaceNearOfficer(Bot bot, Line line, float distance)
        {
            var officer = Players.Get(line.Owner.PlayerId);
            if (officer == null || !officer.Alive || !officer.TryGetPosition(out var pos)) return;
            var yaw = officer.TryGetYaw(out var y) ? y : 0f;
            var spread = Mathf.Max(2f, distance * 0.2f);
            var target = pos - Formation.Forward(yaw) * distance + Formation.Right(yaw) * Random.Range(-spread, spread) + Vector3.up * 0.3f;
            ServerConsole.TeleportToPosition(bot.PlayerId, target);
            BotCommands.Rotate(bot.PlayerId, yaw);
        }

        /// <summary>Mort d'un joueur ou d'un bot (OnPlayerKilledPlayer, ou PV à 0).</summary>
        public static void OnPlayerDied(int playerId, float now)
        {
            // Le bot d'abord (l'officier d'une ligne IA est aussi un bot de sa ligne), puis l'officier.
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
            if (TryGetCommander(playerId, out var c))
            {
                if (c.HasLine) OfficerLost(c, false, now);
                Commanders.Remove(playerId);
            }
            if (BotsByPlayer.TryGetValue(playerId, out var bot)) Forget(bot);
        }

        /// <summary>Le bot n'est plus sur le serveur : retiré de partout, sans commande console.</summary>
        private static void Forget(Bot bot)
        {
            bot.Alive = false;
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
        public static void OnBotHurt(int playerId, float now)
        {
            if (BotsByPlayer.TryGetValue(playerId, out var bot) && bot.Alive && bot.Line.Mode != LineMode.Dispersing)
                Morale.OnHit(bot.Line, now);
        }

        // ---- Fin de ligne et nettoyage -------------------------------------------------------

        /// <summary>
        /// Fin d'une ligne : dispersion pendant DisperseSeconds, puis les bots sont laissés sur place ou tués (CMD:tuerdisperses, voir TickDispersing). announce : mort ou départ de l'officier
        /// (annonce publique) ; sinon l'officier a renvoyé sa ligne.
        /// </summary>
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
            CmdLog.Info("Officier " + c.Name + " " + reason + " : le sergent " + line.Sergeant + " tient sa ligne (" + line.Count + " bots).");

            // Recul en ordre, face à l'ennemi, puis la ligne tient et tire à volonté. Sans repère (aucune position) : halte.
            var hasFrame = TryLineFrame(line, out var center, out var yaw);
            if (hasFrame) line.FormAt(center - Formation.Forward(yaw) * CommanderConfig.SuccessionRetreat, yaw, now);
            else line.Halt(now);
            line.StartFireAtWill(now);
            if (line.Sergeant != null) line.Sergeant.Shout(GameIds.CharacterVoicePhrase.Retreat);

            if (c.IsAi) return;
            if (died) ServerConsole.Broadcast("L'officier " + c.Name + " est tombé : un sergent tient sa ligne.");
            // Message privé (aussi envoyé au webhook Discord) : seulement aux officiers du camp assez proches pour la reprendre.
            if (hasFrame) TellOfficers(line.Faction, c.PlayerId, center, OfficerNoticeRadius, "Ligne de " + c.Name + " sans officier (" + line.Count + " hommes) : approchez à "
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
            if (!officer.Alive || !GameIds.IsInfantryOfficer(officer.Class))
                return silent ? null : "Seul un officier d'infanterie en vie peut reprendre une ligne.";
            var c = GetCommander(officer.Id);
            if (c != null && c.HasLine) return silent ? null : "Vous commandez déjà une ligne.";
            if (!officer.TryGetPosition(out var pos)) return silent ? null : "Position inconnue, réessayez.";

            Line found = null;
            var best = CommanderConfig.TakeoverRadius * CommanderConfig.TakeoverRadius;
            foreach (var line in Orphans)
            {
                // Une ligne IA sans officier ne se reprend pas.
                if (line.Owner.IsAi || line.Faction != officer.Faction) continue;
                foreach (var b in line.Bots)
                {
                    if (!b.Alive || !b.TryGetPosition(out var bp)) continue;
                    var d = Line.Flat(bp - pos).sqrMagnitude;
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
                var split = new Line(found.Owner, found.Faction);
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
            found.Owner = c;
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
                if (p.IsBot || !p.Alive || p.Id == except || p.Faction != faction || !GameIds.IsInfantryOfficer(p.Class)) continue;
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
        private static void TickMorale(float now, float dt)
        {
            if (!Morale.Enabled) return;
            // Copie locale : une ligne en déroute envoie des commandes qui rappellent le mod.
            var lines = new List<Line>(AllActiveLines());
            foreach (var line in lines)
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
            var n = Mathf.Min(CommanderConfig.ReinforceCount, available);
            MakeRoomForPlayers(n, now);
            for (var i = 0; i < n; i++)
            {
                var bot = new Bot(line, BotRole.Line, line.RaisedClass, -1) { Reinforcement = true };
                line.Add(bot);
                Enqueue(bot);
            }
            c.LastReinforceAt = now;
            CmdLog.Info(c.Name + " reçoit " + n + " renforts ; ligne " + line.Count + ", total " + TotalBots + "/" + Cap + ".");
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

        private static void TickDebug(float now)
        {
            if (!DebugEnabled || now < _nextDebug) return;
            _nextDebug = now + DebugInterval;
            foreach (var line in AllActiveLines())
            {
                CmdLog.Info("Debug ligne de " + line.Owner.Name + " : " + Line.ModeName(line.Mode) + ", ancre " + ServerConsole.Num(line.Anchor.x)
                            + "," + ServerConsole.Num(line.Anchor.z) + " cap " + ServerConsole.Num(line.Yaw) + (line.IsMoving ? ", en mouvement" : "")
                            + ", " + line.Count + " bots.");
                foreach (var b in line.Bots)
                {
                    if (b.PlayerId < 0) continue;
                    var info = b.Info;
                    var hasPos = b.TryGetPosition(out var pos);
                    var realYaw = info != null && info.Body != null ? info.Body.transform.eulerAngles.y : float.NaN;
                    var dist = hasPos ? Line.Flat(b.SlotPosition - pos).magnitude : -1f;
                    CmdLog.Info("  " + b.Name + " #" + b.PlayerId + " " + Bot.RoleName(b.Role) + (b.Alive ? "" : " (pas en vie)")
                                + " tâche " + b.TaskSummary()
                                + " place " + ServerConsole.Num(b.SlotPosition.x) + "," + ServerConsole.Num(b.SlotPosition.z)
                                + " pos " + (hasPos ? ServerConsole.Num(pos.x) + "," + ServerConsole.Num(pos.z) : "?")
                                + " dist " + ServerConsole.Num(dist)
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
            for (var i = 0; i < n; i++)
            {
                Bot bot;
                if (wantFlag)
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

            TickSpawns(now);
            TickRemovals(now);
            TickDispersing(now);
            TickOrphans(now);
            TickMorale(now, dt);
            TickFill(now);
            AiDirector.Tick(now);
            TickDebug(now);

            LineList.Clear();
            foreach (var l in Orphans) LineList.Add(l);
            foreach (var c in Commanders.Values) if (c.Line != null) LineList.Add(c.Line);
            foreach (var line in LineList) line.Tick(now, dt);

            TickBots(now);
        }

        /// <summary>Une création de bot toutes les SpawnInterval secondes ; retrait des bots restés sans apparition.</summary>
        private static void TickSpawns(float now)
        {
            if ((SpawnQueue.Count > 0 || AiSpawnQueue.Count > 0) && now >= _nextSpawn)
            {
                _nextSpawn = now + CommanderConfig.SpawnInterval;
                // Les demandes des joueurs passent avant celles des lignes IA.
                var bot = SpawnQueue.Count > 0 ? SpawnQueue.Dequeue() : AiSpawnQueue.Dequeue();
                var line = bot.Line;
                if (LineAlive(line) && line.Bots.Contains(bot))
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

            // Apparition refusée (places de section ou de classe) : ce bot est retiré (il resterait dans les limbes), le reste de
            // la demande de sa ligne est abandonné sans être créé, l'officier est prévenu une fois, et ses levées sont en pause.
            for (var i = AwaitingSpawn.Count - 1; i >= 0; i--)
            {
                var bot = AwaitingSpawn[i];
                if (now - bot.RequestedAt < SpawnTimeout) continue;
                AwaitingSpawn.RemoveAt(i);
                var line = bot.Line;
                line.Remove(bot);
                Retire(bot, now, true);
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
        /// Retraits échus → file de kicks ; au plus un kick par KickInterval pour tout le mod (journal admin, webhook Discord,
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
            if (now < _nextKickAt) return;
            while (KickQueue.Count > 0)
            {
                var bot = KickQueue[0];
                KickQueue.RemoveAt(0);
                var id = bot.PlayerId;
                // Seulement si l'id appartient toujours à ce bot (un id peut être réutilisé) et que la fiche est celle d'un bot.
                if (!BotsByPlayer.TryGetValue(id, out var known) || known != bot) continue;
                BotsByPlayer.Remove(id);
                var p = Players.Get(id);
                if (p == null || !p.IsBot) continue;
                _nextKickAt = now + KickInterval;
                CmdLog.Info(bot.Name + " #" + id + " retiré du serveur (kick).");
                BotCommands.Remove(id);
                return;
            }
            while (ClearedKicks.Count > 0)
            {
                var id = ClearedKicks[0];
                ClearedKicks.RemoveAt(0);
                // Après !cmd vider/off : seulement si l'id est toujours un bot, et pas un nouveau bot du mod.
                var p = Players.Get(id);
                if (p == null || !p.IsBot || BotsByPlayer.ContainsKey(id)) continue;
                _nextKickAt = now + KickInterval;
                BotCommands.Remove(id);
                return;
            }
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
                    // Ligne IA : toujours retirée par kick (tuerdisperses ne la concerne pas).
                    if (line.Owner.IsAi)
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
                var bot = TickList[(_cursor + k) % TickList.Count];
                if (now < bot.NextTick) continue;
                bot.Tick(now);
                // Bot arrivé à sa place, sans action en cours : piloté moins souvent.
                bot.NextTick = now + (bot.Settled && bot.Actions.Count == 0 ? SettledTickInterval : BotTickInterval);
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
            return "Commander : " + (CommanderConfig.IsEnabled ? "actif" : "désactivé") + ", " + lines + " ligne(s) + " + Orphans.Count
                   + " sans officier, " + TotalBots + "/" + Cap + " bots (" + Players.RealPlayerCount() + " joueurs ; "
                   + CommanderConfig.EffectiveMaxPerOfficer + " par officier), "
                   + (SpawnQueue.Count + AiSpawnQueue.Count + AwaitingSpawn.Count) + " en attente, " + Dispersing.Count + " en dispersion, "
                   + Leftovers.Count + " dispersés sur place, " + (Removals.Count + KickQueue.Count + ClearedKicks.Count) + " retraits prévus, " + ServerConsole.Sent + " commandes envoyées ; " + LoadMonitor.Status() + ".";
        }
    }
}
