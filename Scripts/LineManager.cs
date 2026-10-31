using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Cœur du mod : levée des lignes, apparition et rattachement des bots, boucle de pilotage, fin de ligne et nettoyage.
    /// Lot A : succession (n° 3), moral (n° 4, voir Morale), renforts (n° 5), plafond selon le remplissage (n° 11).
    /// Tout l'état est statique (le jeu crée une instance du mod par interface).
    /// </summary>
    public static class LineManager
    {
        // Un bot créé qui n'est pas apparu après ce délai (apparition refusée : places de section ou de classe) est retiré.
        private const float SpawnTimeout = 15f;
        // Pilotage : chaque bot au plus toutes les BotTickInterval secondes, au plus MaxBotTicksPerFrame bots par image.
        private const float BotTickInterval = 0.1f;
        private const int MaxBotTicksPerFrame = 60;
        // Avec CMD:kickdead, le corps d'un bot tué reste un moment avant le retrait.
        private const float DeadBotRemoveDelay = 5f;

        private static readonly Dictionary<int, Commander> Commanders = new Dictionary<int, Commander>();
        private static readonly Dictionary<int, Bot> BotsByPlayer = new Dictionary<int, Bot>();
        private static readonly List<Line> Dispersing = new List<Line>();
        // Lignes sans officier tenues par un sergent (succession), en attente d'une reprise.
        private static readonly List<Line> Orphans = new List<Line>();
        private static readonly Queue<Bot> SpawnQueue = new Queue<Bot>();
        private static readonly List<Bot> AwaitingSpawn = new List<Bot>();
        private static readonly List<KeyValuePair<float, int>> Removals = new List<KeyValuePair<float, int>>();

        private static readonly List<Bot> TickList = new List<Bot>();
        private static readonly List<Line> LineList = new List<Line>();
        private static int _cursor;
        private static float _nextSpawn;
        private static float _lastTick = -1f;
        // Plafond global courant (selon le remplissage), recalculé une fois par seconde.
        private static int _cap = -1;
        private static float _nextCapUpdate;
        private static float _nextFillRemoval;

        /// <summary>Bots comptés dans le plafond : ceux des lignes (y compris en attente d'apparition ou en dispersion).</summary>
        public static int TotalBots
        {
            get
            {
                var n = 0;
                foreach (var c in Commanders.Values) if (c.Line != null) n += c.Line.Count;
                foreach (var l in Dispersing) n += l.Count;
                foreach (var l in Orphans) n += l.Count;
                return n;
            }
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

        /// <summary>Bots vivants ou attendus, hors dispersion.</summary>
        private static int ActiveBots()
        {
            var n = TotalBots;
            foreach (var l in Dispersing) n -= l.Count;
            return n;
        }

        public static Commander GetCommander(int playerId)
        {
            return Commanders.TryGetValue(playerId, out var c) ? c : null;
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
            var current = c != null && c.Line != null ? c.Line.Count : 0;
            var available = Mathf.Min(CommanderConfig.EffectiveMaxPerOfficer - current, Cap - TotalBots);
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
            if (uniform >= 0)
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
                SpawnQueue.Enqueue(bot);
            }

            CmdLog.Info(officer + " lève " + n + " bots (" + GameIds.ClassName(playerClass) + (uniform >= 0 ? ", uniforme " + uniform : "")
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
            if (!BotsByPlayer.TryGetValue(p.Id, out var bot)) return;
            AwaitingSpawn.Remove(bot);
            var line = bot.Line;

            if (line.Mode == LineMode.Dispersing || line.Orphaned || !LineAlive(line) || !line.Bots.Contains(bot))
            {
                // La ligne a pris fin pendant l'apparition.
                line.Remove(bot);
                Retire(bot, now);
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
            BotCommands.TakeControl(p.Id);
            // Renfort : placé loin derrière l'officier, il rejoint sa place à pied.
            PlaceNearOfficer(bot, line, bot.Reinforcement ? CommanderConfig.ReinforceDistance : 2f);
            if (bot.Role == BotRole.FlagBearer) BotCommands.Equip(p.Id, "BearingFlag"); // [TEST]
            line.OnBotReady(bot, now);
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
            if (Commanders.TryGetValue(playerId, out var c) && c.HasLine)
            {
                OfficerLost(c, true, now);
                return;
            }
            if (!BotsByPlayer.TryGetValue(playerId, out var bot) || !bot.Alive) return;
            bot.Alive = false;
            bot.ClearActions();
            bot.Movement = null;
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
            if (Commanders.TryGetValue(playerId, out var c))
            {
                if (c.HasLine) OfficerLost(c, false, now);
                Commanders.Remove(playerId);
            }
            if (BotsByPlayer.TryGetValue(playerId, out var bot))
            {
                bot.Alive = false;
                bot.Line.Remove(bot);
                if (bot.Line.Sergeant == bot) PickSergeant(bot.Line);
                AwaitingSpawn.Remove(bot);
                BotsByPlayer.Remove(playerId);
            }
        }

        /// <summary>Un bot du mod est touché sans mourir : le moral de sa ligne baisse un peu.</summary>
        public static void OnBotHurt(int playerId, float now)
        {
            if (BotsByPlayer.TryGetValue(playerId, out var bot) && bot.Alive && bot.Line.Mode != LineMode.Dispersing)
                Morale.OnHit(bot.Line, now);
        }

        // ---- Fin de ligne et nettoyage -------------------------------------------------------

        /// <summary>
        /// Fin d'une ligne : dispersion, puis les bots sont tués après DisperseSeconds. announce : mort ou départ de l'officier
        /// (annonce publique) ; sinon l'officier a renvoyé sa ligne.
        /// </summary>
        public static int EndLine(Commander c, string reason, float now, bool announce)
        {
            var line = c.Line;
            c.Line = null;
            if (line == null) return 0;
            var n = line.Count;
            CmdLog.Info("Fin de la ligne de " + c.Name + " (" + reason + ") : " + n + " bots.");
            if (announce && line.Count > 0) ServerConsole.Broadcast("L'officier " + c.Name + " est " + reason + " : sa ligne se disperse.");
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

            // Recul en ordre, face à l'ennemi, puis la ligne tient et tire à volonté.
            if (TryLineFrame(line, out var center, out var yaw))
                line.FormAt(center - Formation.Forward(yaw) * CommanderConfig.SuccessionRetreat, yaw, now);
            line.StartFireAtWill(now);
            if (line.Sergeant != null) line.Sergeant.Shout(GameIds.CharacterVoicePhrase.Retreat);

            if (died) ServerConsole.Broadcast("L'officier " + c.Name + " est tombé : un sergent tient sa ligne.");
            TellOfficers(line.Faction, c.PlayerId, "Ligne de " + c.Name + " sans officier (" + line.Count + " hommes) : approchez à "
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
                if (line.Faction != officer.Faction) continue;
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

        /// <summary>Message privé aux officiers d'infanterie vivants d'une faction (sauf except).</summary>
        private static void TellOfficers(int faction, int except, string text)
        {
            foreach (var p in Players.All)
            {
                if (p.IsBot || !p.Alive || p.Id == except || p.Faction != faction || !GameIds.IsInfantryOfficer(p.Class)) continue;
                ServerConsole.Private(p.Id, text);
            }
        }

        private static void TickOrphans(float now)
        {
            for (var i = Orphans.Count - 1; i >= 0; i--)
            {
                var line = Orphans[i];
                if (line.Count == 0)
                {
                    Orphans.RemoveAt(i);
                    continue;
                }
                if (now < line.SuccessionEndsAt) continue;
                CmdLog.Info("Personne n'a repris la ligne de " + line.Owner.Name + " : dispersion.");
                TellOfficers(line.Faction, -1, "La ligne de " + line.Owner.Name + " se disperse.");
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
            LineList.Clear();
            LineList.AddRange(AllActiveLines());
            foreach (var line in LineList)
            {
                Morale.Tick(line, now, dt);
                if (line.Morale <= 0f)
                {
                    CmdLog.Info("Ligne de " + line.Owner.Name + " en déroute.");
                    if (line.Orphaned) DisperseLine(line, now);
                    else
                    {
                        ServerConsole.Private(line.Owner.PlayerId, "Votre ligne est en déroute et se disperse.");
                        EndLine(line.Owner, "en déroute", now, false);
                    }
                    continue;
                }
                if (line.Morale >= CommanderConfig.MoraleWaver) continue;
                // La ligne flanche : elle recule, puis de nouveau toutes les 20 s tant que le moral reste bas.
                if (line.Wavering && now - line.LastWaverAt < 20f) continue;
                line.Wavering = true;
                line.LastWaverAt = now;
                if (TryLineFrame(line, out var center, out var yaw))
                {
                    line.FormAt(center - Formation.Forward(yaw) * CommanderConfig.SuccessionRetreat, yaw, now);
                    if (!line.Orphaned) ServerConsole.Private(line.Owner.PlayerId, "Votre ligne flanche et recule (moral " + Mathf.RoundToInt(line.Morale) + "). Criez StandGround pour la rallier.");
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
            var wait = Mathf.CeilToInt(CommanderConfig.ReinforceCooldown - (now - c.LastReinforceAt));
            if (wait > 0) return "Renforts pas encore prêts : encore " + wait + " s.";
            var line = c.Line;
            var available = Mathf.Min(CommanderConfig.EffectiveMaxPerOfficer - line.Count, Cap - TotalBots);
            if (available <= 0)
                return line.Count >= CommanderConfig.EffectiveMaxPerOfficer ? "Pas de renforts : votre ligne est au complet." : "Pas de renforts : plafond de bots du serveur atteint.";
            var n = Mathf.Min(CommanderConfig.ReinforceCount, available);
            for (var i = 0; i < n; i++)
            {
                var bot = new Bot(line, BotRole.Line, line.RaisedClass, -1) { Reinforcement = true };
                line.Add(bot);
                SpawnQueue.Enqueue(bot);
            }
            c.LastReinforceAt = now;
            CmdLog.Info(c.Name + " reçoit " + n + " renforts ; ligne " + line.Count + ", total " + TotalBots + "/" + Cap + ".");
            return n + " hommes en renfort arrivent par l'arrière.";
        }

        // ---- Remplissage (lot A, n° 11) -------------------------------------------------------

        /// <summary>
        /// Plafond baissé par l'arrivée de vrais joueurs : les levées et les renforts sont déjà refusés ; si l'écart dépasse FillMargin,
        /// un bot est retiré toutes les FillInterval secondes (d'abord les demandes pas encore créées, puis le bot le plus loin de son officier).
        /// </summary>
        private static void TickFill(float now)
        {
            if (now < _nextFillRemoval) return;
            // Les bots en dispersion vont disparaître d'eux-mêmes : ils ne comptent pas dans l'écart.
            var excess = ActiveBots() - Cap;
            if (excess <= CommanderConfig.FillMargin) return;
            _nextFillRemoval = now + CommanderConfig.FillInterval;

            // Demandes en file : annulées sans rien envoyer au jeu.
            while (SpawnQueue.Count > 0)
            {
                var queued = SpawnQueue.Dequeue();
                if (!queued.Line.Bots.Contains(queued)) continue;
                queued.Line.Remove(queued);
                CmdLog.Info("Plafond " + Cap + " dépassé : une levée en attente annulée.");
                return;
            }

            Bot farthest = null;
            var best = -1f;
            foreach (var line in AllActiveLines())
            {
                var leader = line.Orphaned ? line.Sergeant?.Info : Players.Get(line.Owner.PlayerId);
                if (leader == null || !leader.TryGetPosition(out var lp)) continue;
                foreach (var b in line.Bots)
                {
                    if (!b.Alive || b == line.Sergeant) continue;
                    var d = DistanceTo(b, lp);
                    if (d == float.MaxValue || d <= best) continue;
                    best = d;
                    farthest = b;
                }
            }
            if (farthest == null) return;
            var from = farthest.Line;
            // Marqué mort avant le slay : OnPlayerDied ne compte pas cette perte dans le moral.
            farthest.Alive = false;
            farthest.ClearActions();
            from.Remove(farthest);
            BotCommands.Kill(farthest.PlayerId);
            Retire(farthest, now + 1f);
            CmdLog.Info("Plafond " + Cap + " dépassé de " + excess + " : " + farthest + " retiré de la ligne de " + from.Owner.Name + ".");
            if (!from.Orphaned) ServerConsole.Private(from.Owner.PlayerId, "Plafond de bots abaissé (serveur plus rempli ou chargé) : un homme quitte votre ligne.");
        }

        /// <summary>
        /// Oublie tous les bots du mod. kick faux (fin ou début de manche) : le jeu les supprime lui-même au changement de manche,
        /// aucun kick n'est envoyé. kick vrai (commande admin) : chaque bot est retiré du serveur.
        /// </summary>
        public static void Clear(string reason, bool kick)
        {
            var n = BotsByPlayer.Count;
            if (kick)
            {
                foreach (var id in BotsByPlayer.Keys) BotCommands.Remove(id);
                foreach (var r in Removals)
                {
                    if (!BotsByPlayer.ContainsKey(r.Value)) BotCommands.Remove(r.Value);
                }
            }
            var pending = SpawnQueue.Count;
            Commanders.Clear();
            Orphans.Clear();
            BotsByPlayer.Clear();
            Dispersing.Clear();
            SpawnQueue.Clear();
            AwaitingSpawn.Clear();
            Removals.Clear();
            TickList.Clear();
            _cursor = 0;
            if (n > 0 || pending > 0)
                CmdLog.Info("Nettoyage (" + reason + ") : " + n + " bots " + (kick ? "retirés" : "oubliés") + ", " + pending + " demandes annulées.");
        }

        /// <summary>
        /// Le mod lâche un bot. Le retrait du serveur passe par un kick (journal admin, webhook Discord) : seulement si force
        /// (bot jamais apparu, mauvais camp) ou si CMD:kickdead est actif. Sinon le bot mort reste jusqu'au changement de manche.
        /// </summary>
        private static void Retire(Bot bot, float at, bool force = false)
        {
            if (bot.PlayerId < 0) return;
            if (force || CommanderConfig.KickDead) Removals.Add(new KeyValuePair<float, int>(at, bot.PlayerId));
            else BotsByPlayer.Remove(bot.PlayerId);
        }

        private static bool LineAlive(Line line)
        {
            return Commanders.TryGetValue(line.Owner.PlayerId, out var c) && c.Line == line;
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

            LineList.Clear();
            foreach (var l in Orphans) LineList.Add(l);
            foreach (var c in Commanders.Values) if (c.Line != null) LineList.Add(c.Line);
            foreach (var line in LineList) line.Tick(now, dt);

            TickBots(now);
        }

        /// <summary>Une création de bot toutes les SpawnInterval secondes ; retrait des bots restés sans apparition.</summary>
        private static void TickSpawns(float now)
        {
            if (SpawnQueue.Count > 0 && now >= _nextSpawn)
            {
                _nextSpawn = now + CommanderConfig.SpawnInterval;
                var bot = SpawnQueue.Dequeue();
                var line = bot.Line;
                if (LineAlive(line) && line.Bots.Contains(bot))
                {
                    var officer = Players.Get(line.Owner.PlayerId);
                    // Le tag de l'officier n'est appliqué que parce qu'un vrai joueur connecté (lui) le porte.
                    var tag = officer != null ? officer.Regiment : "";
                    // Uniforme choisi au moment de la création : un !skin donné entre-temps s'applique (lot B, n° 15).
                    var uniform = bot.Uniform >= 1 ? bot.Uniform : Uniforms.Resolve(line.Owner, line.Faction, bot.Class);
                    var id = BotCommands.Spawn(line.Faction, bot.Class, line.NextBotName(bot.Role), tag, uniform);
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
                    }
                }
            }

            var expired = 0;
            Line lastLine = null;
            for (var i = AwaitingSpawn.Count - 1; i >= 0; i--)
            {
                var bot = AwaitingSpawn[i];
                if (now - bot.RequestedAt < SpawnTimeout) continue;
                AwaitingSpawn.RemoveAt(i);
                bot.Line.Remove(bot);
                // Apparition refusée : le bot reste connecté dans les limbes, il faut le retirer.
                Retire(bot, now, true);
                lastLine = bot.Line;
                expired++;
            }
            if (expired > 0)
            {
                CmdLog.Warn(expired + " bot(s) créé(s) mais non apparu(s) après " + SpawnTimeout + " s (places de section ou de classe ?).");
                if (LineAlive(lastLine)) ServerConsole.Private(lastLine.Owner.PlayerId, expired + " homme(s) n'ont pas pu rejoindre la ligne.");
            }
        }

        private static void TickRemovals(float now)
        {
            for (var i = Removals.Count - 1; i >= 0; i--)
            {
                if (now < Removals[i].Key) continue;
                var id = Removals[i].Value;
                Removals.RemoveAt(i);
                BotsByPlayer.Remove(id);
                // Déjà parti du serveur : rien à retirer.
                if (Players.Get(id) != null) BotCommands.Remove(id);
            }
        }

        /// <summary>Fin de la dispersion : chaque bot encore en vie est tué.</summary>
        private static void TickDispersing(float now)
        {
            for (var i = Dispersing.Count - 1; i >= 0; i--)
            {
                var line = Dispersing[i];
                if (now < line.DisperseEndsAt) continue;
                Dispersing.RemoveAt(i);
                foreach (var b in line.Bots.ToArray())
                {
                    line.Remove(b);
                    if (b.PlayerId < 0) continue;
                    if (b.Alive) BotCommands.Kill(b.PlayerId);
                    b.Alive = false;
                    Retire(b, now + 1f);
                }
            }
        }

        /// <summary>Pilotage en tourniquet : la charge de commandes est étalée sur plusieurs images.</summary>
        private static void TickBots(float now)
        {
            if (BotsByPlayer.Count == 0) return;
            TickList.Clear();
            foreach (var b in BotsByPlayer.Values) if (b.Alive) TickList.Add(b);
            if (TickList.Count == 0) return;
            if (_cursor >= TickList.Count) _cursor = 0;

            var done = 0;
            for (var k = 0; k < TickList.Count && done < MaxBotTicksPerFrame; k++)
            {
                var bot = TickList[(_cursor + k) % TickList.Count];
                if (now < bot.NextTick) continue;
                bot.NextTick = now + BotTickInterval;
                bot.Tick(now);
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
                   + (SpawnQueue.Count + AwaitingSpawn.Count) + " en attente, " + Dispersing.Count + " en dispersion, "
                   + Removals.Count + " retraits prévus, " + ServerConsole.Sent + " commandes envoyées ; " + LoadMonitor.Status() + ".";
        }
    }
}
