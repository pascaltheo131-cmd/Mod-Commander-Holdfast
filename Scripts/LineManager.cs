using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Cœur du mod : levée des lignes, apparition et rattachement des bots, boucle de pilotage, fin de ligne et nettoyage.
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
        private static readonly Queue<Bot> SpawnQueue = new Queue<Bot>();
        private static readonly List<Bot> AwaitingSpawn = new List<Bot>();
        private static readonly List<KeyValuePair<float, int>> Removals = new List<KeyValuePair<float, int>>();

        private static readonly List<Bot> TickList = new List<Bot>();
        private static readonly List<Line> LineList = new List<Line>();
        private static int _cursor;
        private static float _nextSpawn;
        private static float _lastTick = -1f;

        /// <summary>Bots comptés dans le plafond : ceux des lignes (y compris en attente d'apparition ou en dispersion).</summary>
        public static int TotalBots
        {
            get
            {
                var n = 0;
                foreach (var c in Commanders.Values) if (c.Line != null) n += c.Line.Count;
                foreach (var l in Dispersing) n += l.Count;
                return n;
            }
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
            var available = Mathf.Min(CommanderConfig.EffectiveMaxPerOfficer - current, CommanderConfig.EffectiveMaxBots - TotalBots);
            if (available <= 0)
            {
                return current > 0
                    ? "Plafond atteint : votre ligne compte déjà " + current + " hommes."
                    : "Plafond de bots du serveur atteint (" + CommanderConfig.EffectiveMaxBots + ").";
            }
            var n = Mathf.Min(count, available);

            var notice = "";
            if (!CommanderConfig.AllowedClasses.Contains(playerClass))
            {
                notice = " Classe " + GameIds.ClassName(playerClass) + " non permise : " + GameIds.ClassName(CommanderConfig.DefaultClass) + ".";
                playerClass = CommanderConfig.DefaultClass;
            }

            if (c == null)
            {
                c = new Commander(officer.Id);
                Commanders[officer.Id] = c;
            }
            if (c.Line == null) c.Line = new Line(c, officer.Faction);
            var line = c.Line;

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
                    bot = new Bot(line, BotRole.Line, playerClass, uniform);
                }
                line.Add(bot);
                SpawnQueue.Enqueue(bot);
            }

            CmdLog.Info(officer + " lève " + n + " bots (" + GameIds.ClassName(playerClass) + (uniform >= 0 ? ", uniforme " + uniform : "")
                        + ", " + GameIds.FactionName(officer.Faction) + ") ; ligne " + line.Count + ", total " + TotalBots + "/"
                        + CommanderConfig.EffectiveMaxBots + ".");
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

            if (line.Mode == LineMode.Dispersing || !LineAlive(line) || !line.Bots.Contains(bot))
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
            PlaceNearOfficer(bot, line);
            if (bot.Role == BotRole.FlagBearer) BotCommands.Equip(p.Id, "BearingFlag"); // [TEST]
            line.OnBotReady(bot, now);
        }

        /// <summary>Le bot apparaît sur une section au hasard : il est placé juste derrière l'officier [TEST : hauteur du sol].</summary>
        private static void PlaceNearOfficer(Bot bot, Line line)
        {
            var officer = Players.Get(line.Owner.PlayerId);
            if (officer == null || !officer.Alive || !officer.TryGetPosition(out var pos)) return;
            var yaw = officer.TryGetYaw(out var y) ? y : 0f;
            var target = pos - Formation.Forward(yaw) * 2f + Formation.Right(yaw) * Random.Range(-2f, 2f) + Vector3.up * 0.3f;
            ServerConsole.TeleportToPosition(bot.PlayerId, target);
            BotCommands.Rotate(bot.PlayerId, yaw);
        }

        /// <summary>Mort d'un joueur ou d'un bot (OnPlayerKilledPlayer, ou PV à 0).</summary>
        public static void OnPlayerDied(int playerId, float now)
        {
            if (Commanders.TryGetValue(playerId, out var c) && c.HasLine)
            {
                EndLine(c, "tombé", now, true);
                return;
            }
            if (!BotsByPlayer.TryGetValue(playerId, out var bot) || !bot.Alive) return;
            bot.Alive = false;
            bot.ClearActions();
            bot.Movement = null;
            bot.Line.Remove(bot);
            // Avec allowAutoRespawn false, le bot reste mort et connecté jusqu'au changement de manche.
            Retire(bot, now + DeadBotRemoveDelay);
        }

        /// <summary>Départ d'un joueur ou d'un bot.</summary>
        public static void OnPlayerLeft(int playerId, float now)
        {
            if (Commanders.TryGetValue(playerId, out var c))
            {
                if (c.HasLine) EndLine(c, "parti", now, true);
                Commanders.Remove(playerId);
            }
            if (BotsByPlayer.TryGetValue(playerId, out var bot))
            {
                bot.Alive = false;
                bot.Line.Remove(bot);
                AwaitingSpawn.Remove(bot);
                BotsByPlayer.Remove(playerId);
            }
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
            // Les bots pas encore apparus sont retirés tout de suite.
            foreach (var b in line.Bots.ToArray())
            {
                if (b.Alive) continue;
                line.Remove(b);
                AwaitingSpawn.Remove(b);
                if (b.PlayerId >= 0) Retire(b, now, true);
            }
            if (line.Count == 0) return n;
            if (announce) ServerConsole.Broadcast("L'officier " + c.Name + " est " + reason + " : sa ligne se disperse.");
            line.Disperse(now);
            Dispersing.Add(line);
            return n;
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

            TickSpawns(now);
            TickRemovals(now);
            TickDispersing(now);

            LineList.Clear();
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
                    var id = BotCommands.Spawn(line.Faction, bot.Class, line.NextBotName(bot.Role), tag, bot.Uniform);
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

        public static string Status()
        {
            var lines = 0;
            foreach (var c in Commanders.Values) if (c.HasLine) lines++;
            return "Commander : " + (CommanderConfig.IsEnabled ? "actif" : "désactivé") + ", " + lines + " ligne(s), " + TotalBots + "/"
                   + CommanderConfig.EffectiveMaxBots + " bots (" + CommanderConfig.EffectiveMaxPerOfficer + " par officier), "
                   + (SpawnQueue.Count + AwaitingSpawn.Count) + " en attente, " + Dispersing.Count + " en dispersion, "
                   + Removals.Count + " retraits prévus, " + ServerConsole.Sent + " commandes envoyées.";
        }
    }
}
