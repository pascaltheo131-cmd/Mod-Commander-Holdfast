using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Réglages lus depuis les lignes « mod_variable CMD:&lt;clé&gt;:&lt;valeur&gt; » du serverconfig (préfixe à confirmer, docs/PLAN.md).
    /// Attention : dans serverconfig, « # » commence un commentaire ; ne pas l'utiliser dans les valeurs.
    /// Les commandes admin (!cmd) peuvent changer certains réglages jusqu'au redémarrage : voir les champs *Override.
    /// </summary>
    public static class CommanderConfig
    {
        public const string Prefix = "CMD";

        // ---- Cœur (étape 1) ------------------------------------------------------------------
        public static bool Enabled = true;
        public static int FollowColumns = 2;
        public static bool RandomNames = true;
        /// <summary>
        /// Les bots apparaissent baïonnette au canon : réglage du jeu StartBayonetAttached, vrai par défaut [CODE, GameDetails].
        /// À mettre à faux si le serveur l'a changé, sinon le mod croirait la baïonnette absente et la reposerait.
        /// </summary>
        public static bool StartBayonet = true;
        /// <summary>Musique des lignes : 0 jamais, 1 en marche seulement, 2 toujours (sauf charge, dispersion, feu à volonté).</summary>
        public static int MusicMode = 2;
        /// <summary>Relance du morceau toutes les N s (0 : jamais ; le serveur ne termine pas un morceau de lui-même) [TEST].</summary>
        public static float MusicRelaunch;
        /// <summary>Obsolète : les morceaux viennent de SongCatalog (accepté pour compatibilité, sans effet).</summary>
        public static int MusicSongs = 40;
        /// <summary>
        /// Gestes (PlayerActions en entiers) qui font suivre la ligne / la font faire halte (bascule). Vide = tous les gestes ;
        /// GestureOff = aucun. Les gestes reçus sont journalisés avec !cmd debug pour trouver celui de la touche voulue.
        /// </summary>
        public static readonly HashSet<int> FollowGestures = new HashSet<int>();
        public static bool GestureOff = true;
        /// <summary>Durée supposée d'un morceau en mode hasard avant d'en enchaîner un autre (si musiquerelance vaut 0).</summary>
        public const float RandomSongLength = 90f;
        public static bool AimHeight = true;
        public static float AimArc = 15f;
        public static float AimRange = 100f;
        // Ecart maximal en degrés, ajouté à la dispersion native du jeu.
        public static float AccuracySpread = 2f;
        /// <summary>Plafond global : 0 = illimité ; la charge et le remplissage ne plafonnent pas non plus.</summary>
        public static int MaxBots;
        /// <summary>Plafond strict de bots par officier.</summary>
        public static int MaxPerOfficer = 20;
        /// <summary>Classes que l'officier peut demander (valeurs GameIds.PlayerClass).</summary>
        public static readonly HashSet<int> AllowedClasses = new HashSet<int>
        {
            GameIds.PlayerClass.ArmyLineInfantry, GameIds.PlayerClass.Guard, GameIds.PlayerClass.Grenadier,
            GameIds.PlayerClass.LightInfantry, GameIds.PlayerClass.Rifleman,
        };
        public static int DefaultClass = GameIds.PlayerClass.ArmyLineInfantry;
        /// <summary>Distance entre deux hommes d'un même rang, en mètres.</summary>
        public static float Spacing = 0f; // 0 : écart serré calculé sur le rayon (≈ 0,73 m)
        /// <summary>Distance entre deux rangs, en mètres.</summary>
        public static float RankDistance = 1.2f;
        public static bool DoubleRank = true;
        /// <summary>Auxiliaires : porte-drapeau dès AuxMin bots, tambour dès DrumMin bots (comptés dans le plafond).</summary>
        public static bool Aux = true;
        public static int AuxMin = 8;
        public static int DrumMin = 20;
        /// <summary>Fenêtre de la règle anti-doublon : phrase vocale ignorée si elle suit un ordre du même type (docs/PLAN.md).</summary>
        public static float DedupWindow = 1f;
        /// <summary>Durée de la dispersion après la mort ou le départ de l'officier, avant la suppression des bots.</summary>
        public static float DisperseSeconds = 8f;
        /// <summary>
        /// Fin de la dispersion : vrai = les bots sont tués (serverAdmin slay : journal admin, webhook Discord, OnAdminPlayerAction) ;
        /// faux (défaut) = ils restent immobiles, plus pilotés, jusqu'à la fin de manche, et sont retirés en premier si le plafond baisse.
        /// </summary>
        public static bool KillDispersed;
        /// <summary>Pause des levées et renforts d'un officier après une apparition refusée (secondes).</summary>
        public static float RaisePause = 30f;
        /// <summary>Taille de la ligne levée sans chat (ordre Renfort ou phrase Reinforce d'un officier sans ligne) ; 0 = désactivé.</summary>
        public static int DefaultLineSize = 12;
        /// <summary>Recul de la ligne sur la phrase Retreat, en mètres.</summary>
        public static float RetreatDistance = 20f;
        /// <summary>
        /// Retirer (kick) les bots morts. Faux par défaut : chaque kick est noté dans le journal admin et part au webhook Discord ;
        /// un bot mort reste connecté jusqu'au changement de manche, où le jeu supprime tous les bots.
        /// </summary>
        public static bool KickDead;
        /// <summary>Intervalle minimal entre deux demandes d'apparition de bot (étale la charge).</summary>
        public static float SpawnInterval = 0.25f;

        // ---- Lot A, n° 3 : succession ----------------------------------------------------------
        public static bool Succession = true;
        /// <summary>Taille minimale (bots vivants) pour qu'un sergent tienne la ligne ; en dessous, dispersion directe.</summary>
        public static int SuccessionMin = 1;
        public static float SuccessionSeconds = 60f;
        /// <summary>Recul en ordre au début de la succession, en mètres.</summary>
        public static float SuccessionRetreat = 15f;
        /// <summary>Distance à laquelle un officier peut reprendre une ligne sans officier (!reprendre, Follow, StandGround).</summary>
        public static float TakeoverRadius = 10f;

        // ---- Lot A, n° 4 : moral -------------------------------------------------------------
        public static bool MoraleEnabled = true;
        /// <summary>Sous ce seuil, tir moins précis (délai aléatoire, inclinaison bruitée).</summary>
        public static float MoraleLow = 40f;
        /// <summary>Sous ce seuil, la ligne recule.</summary>
        public static float MoraleWaver = 20f;
        /// <summary>Recul d'une ligne qui flanche, en mètres.</summary>
        public static float MoraleRetreat = 15f;
        /// <summary>Points perdus pour la perte de toute la ligne (chaque homme : MoraleLoss / taille).</summary>
        public static float MoraleLoss = 80f;
        public static float MoraleOfficer = 30f;
        /// <summary>Points perdus quand un bot de la ligne est touché sans mourir.</summary>
        public static float MoraleFire = 1f;
        /// <summary>Points regagnés par seconde quand la ligne n'est pas touchée depuis MoraleCalm secondes.</summary>
        public static float MoraleRegen = 1f;
        public static float MoraleCalm = 10f;
        public static float MoraleRally = 25f;
        public static float MoraleRallyCooldown = 60f;
        /// <summary>Rayon du ralliement d'un sergent joueur (ordre SergeantRally) autour de sa position.</summary>
        public static float RallyRadius = 25f;

        // ---- Lot A, n° 5 : renforts ----------------------------------------------------------
        public static bool Reinforcements = true;
        public static int ReinforceCount = 5;
        public static float ReinforceCooldown = 90f;
        /// <summary>Distance derrière l'officier où les renforts sont placés avant de rejoindre la ligne à pied.</summary>
        public static float ReinforceDistance = 30f;

        // ---- Lot A, n° 11 : plafond selon le remplissage ----------------------------------------
        /// <summary>Plafond quand le serveur est vide.</summary>
        public static int FillMax;
        /// <summary>Plafond à partir de FillFull vrais joueurs.</summary>
        public static int FillMin;
        /// <summary>Nombre de vrais joueurs pour lequel le plafond vaut FillMin ; 0 = fonction désactivée (plafond = maxbots).</summary>
        public static int FillFull;
        /// <summary>Écart toléré au-dessus du plafond avant de retirer des bots.</summary>
        public static int FillMargin = 5;
        /// <summary>Un bot retiré toutes les FillInterval secondes tant que l'écart dépasse FillMargin.</summary>
        public static float FillInterval = 10f;

        // ---- Lot B, n° 15 : uniformes --------------------------------------------------------
        /// <summary>Accepter les ids d'uniforme absents de la table embarquée (uniformes ajoutés par des mods). Défaut : non.</summary>
        public static bool SkinFree;

        // ---- Lot B, n° 7 : carré -------------------------------------------------------------
        public static bool Square = true;
        /// <summary>Nombre de soldats à partir duquel le carré a 2 rangs par face.</summary>
        public static int SquareTwoRanks = 16;
        /// <summary>Portée du tir du carré sur l'infanterie, et sur la cavalerie (Hussar, Dragoon), en mètres.</summary>
        public static float SquareRange = 40f;
        public static float SquareCavalryRange = 60f;

        // ---- Lot B, n° 16 : plafond selon la charge -------------------------------------------
        public static bool LoadCapEnabled;
        /// <summary>Seuil de la durée moyenne d'une image, en ms ; 0 = automatique (1,5 × l'intervalle normal estimé).</summary>
        public static float LoadThreshold;
        /// <summary>Plancher du plafond de charge.</summary>
        public static int LoadMin = 10;
        /// <summary>Baisse du plafond à chaque mesure au-dessus du seuil.</summary>
        public static int LoadStep = 5;
        /// <summary>Intervalle entre deux décisions, en secondes (le plafond remonte de 1 par intervalle quand tout va bien).</summary>
        public static float LoadInterval = 5f;

        // ---- Lot C, n° 12 : lignes ennemies menées par l'IA --------------------------------------
        /// <summary>Faux par défaut tant que ce n'est pas essayé en jeu.</summary>
        public static bool AiLines;
        /// <summary>!cmd ia on|off, jusqu'au redémarrage (null = valeur du serverconfig).</summary>
        public static bool? AiOverride;
        /// <summary>Nombre de lignes IA par camp vide (si AiPerPlayers vaut 0).</summary>
        public static int AiLineCount = 1;
        /// <summary>Une ligne IA pour AiPerPlayers vrais joueurs d'en face ; 0 = nombre fixe AiLineCount.</summary>
        public static int AiPerPlayers;
        public static int AiLineSize = 15;
        public static int AiClass = GameIds.PlayerClass.ArmyLineInfantry;
        /// <summary>Attente avant la première ligne : après le début de manche ou le départ du dernier joueur du camp.</summary>
        public static float AiDelay = 30f;
        /// <summary>Attente entre deux vagues, quand une ligne IA a été détruite.</summary>
        public static float AiWaveDelay = 60f;
        /// <summary>Distance à laquelle la ligne IA s'arrête pour tirer.</summary>
        public static float AiRange = 60f;
        /// <summary>Distance de l'ennemi le plus proche en dessous de laquelle la ligne IA charge.</summary>
        public static float AiChargeDistance = 20f;
        /// <summary>Ligne considérée bloquée si son centre bouge de moins de 2 m pendant ce délai.</summary>
        public static float AiStuckSeconds = 10f;
        /// <summary>Modes de jeu (GameplayMode, en entiers) où les lignes IA sont permises ; vide = tous (non vérifiés).</summary>
        public static readonly HashSet<int> AiModes = new HashSet<int>();

        // ---- Modifiés par les commandes admin jusqu'au redémarrage (null = valeur du serverconfig) ----
        public static bool? EnabledOverride;
        public static int? MaxBotsOverride;
        public static int? MaxPerOfficerOverride;

        public static bool IsEnabled => EnabledOverride ?? Enabled;
        public static int EffectiveMaxBots => MaxBotsOverride ?? MaxBots;

        /// <summary>
        /// Plafond global selon le nombre de vrais joueurs (lot A, n° 11) : de FillMax (serveur vide) à FillMin (FillFull joueurs
        /// ou plus), en ligne droite, sans dépasser maxbots. Le plafond fixé par un admin (!cmd max) passe avant.
        /// </summary>
        public static int CapFor(int realPlayers)
        {
            // Zéro signifie réellement illimité : aucune courbe de remplissage ou charge ne réintroduit un plafond global.
            var ceiling = EffectiveMaxBots;
            if (ceiling <= 0) return int.MaxValue;
            if (FillFull <= 0) return ceiling;
            var t = Mathf.Clamp01(realPlayers / (float)FillFull);
            return Mathf.Min(ceiling, Mathf.RoundToInt(Mathf.Lerp(FillMax, FillMin, t)));
        }

        // Lignes CMD:skin brutes : appliquées dans Validate, après toutes les variables (skinlibre peut venir après).
        private static readonly List<string> SkinLines = new List<string>();

        /// <summary>« Faction:Classe:uniformId » (noms ou numéros), par exemple « British:Guard:2 ».</summary>
        private static void ParseSkin(string value)
        {
            var p = value.Split(':');
            if (p.Length != 3 || !GameIds.TryParseFaction(p[0], out var faction) || !GameIds.TryParseClass(p[1], out var playerClass)
                || !int.TryParse(p[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                CmdLog.Info("skin invalide (Faction:Classe:uniforme attendu) : " + value);
                return;
            }
            if (!Uniforms.SetDefault(faction, playerClass, id))
                CmdLog.Info("skin : uniforme " + id + " inconnu pour " + GameIds.FactionName(faction) + " " + GameIds.ClassName(playerClass) + ", ignoré.");
        }
        public static int EffectiveMaxPerOfficer => MaxPerOfficerOverride ?? MaxPerOfficer;

        // Clés prévues pour les lots suivants (docs/PLAN.md, étape 2) : reconnues pour ne pas être signalées comme inconnues,
        // mais ignorées tant que la fonction n'existe pas. Ajouter ici la clé, puis son « case » dans Apply quand elle est codée.
        private static readonly string[] Reserved = new string[0];

        /// <summary>Applique une valeur brute « CMD:clé:valeur ». Renvoie false si elle ne concerne pas ce mod.</summary>
        public static bool Apply(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            var parts = raw.Split(new[] { ':' }, 3);
            if (parts.Length < 3 || !string.Equals(parts[0].Trim(), Prefix, StringComparison.OrdinalIgnoreCase)) return false;

            var key = parts[1].Trim().ToLowerInvariant();
            var value = parts[2].Trim();
            switch (key)
            {
                case "enabled": case "actif": Enabled = ParseBool(key, value, Enabled); break;
                case "maxbots": case "plafond": MaxBots = ParseInt(key, value, 0, 250, MaxBots); break;
                case "maxperofficer": case "plafondofficier": MaxPerOfficer = ParseInt(key, value, 1, 20, MaxPerOfficer); break;
                case "allowedclasses": case "classes": ParseClasses(value); break;
                case "defaultclass": case "classe":
                    if (GameIds.TryParseClass(value, out var c)) DefaultClass = c;
                    else CmdLog.Info("Classe inconnue pour " + key + " : " + value);
                    break;
                case "colonnes": FollowColumns = ParseInt(key, value, 2, 3, FollowColumns); break;
                case "nomsaleatoires": RandomNames = ParseBool(key, value, RandomNames); break;
                case "musique":
                    switch (value.ToLowerInvariant())
                    {
                        case "jamais": case "never": case "off": MusicMode = 0; break;
                        case "marche": case "march": MusicMode = 1; break;
                        case "toujours": case "always": case "on": MusicMode = 2; break;
                        default: CmdLog.Info("musique invalide (jamais, marche ou toujours attendu) : " + value); break;
                    }
                    break;
                case "gestesuivre":
                    FollowGestures.Clear();
                    GestureOff = false;
                    var gv = value.Trim().ToLowerInvariant();
                    if (gv == "aucun" || gv == "off" || gv == "non") GestureOff = true;
                    else if (gv != "tous" && gv != "all")
                        foreach (var item in value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = item.Trim();
                            var found = -1;
                            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)) found = num;
                            else foreach (var pair in GameIds.PlayerActions.Names) if (string.Equals(pair.Value, t, StringComparison.OrdinalIgnoreCase)) found = pair.Key;
                            if (found >= 0 && GameIds.IsGesture(found)) FollowGestures.Add(found);
                            else CmdLog.Info("Geste inconnu dans gestesuivre : " + t);
                        }
                    break;
                case "musiquemorceaux": MusicSongs = ParseInt(key, value, 1, 200, MusicSongs); break;
                case "musiquerelance": MusicRelaunch = ParseFloat(key, value, 0f, 600f, MusicRelaunch); break;
                case "baionnettedepart": case "baïonnettedépart": StartBayonet = ParseBool(key, value, StartBayonet); break;
                case "viseehauteur": AimHeight = ParseBool(key, value, AimHeight); break;
                case "viseearc": AimArc = ParseFloat(key, value, 0f, 15f, AimArc); break;
                case "viseeportee": AimRange = ParseFloat(key, value, 10f, 200f, AimRange); break;
                case "precision": AccuracySpread = ParseFloat(key, value, 0f, 15f, AccuracySpread); break;
                case "spacing": case "espacement": Spacing = ParseFloat(key, value, 0f, 5f, Spacing); break;
                case "rankdistance": case "distancerangs": RankDistance = ParseFloat(key, value, 0.5f, 5f, RankDistance); break;
                case "doublerank": case "rangdouble": DoubleRank = ParseBool(key, value, DoubleRank); break;
                case "aux": Aux = ParseBool(key, value, Aux); break;
                case "auxmin": AuxMin = ParseInt(key, value, 1, 100, AuxMin); break;
                case "drummin": DrumMin = ParseInt(key, value, 1, 100, DrumMin); break;
                case "dedupwindow": DedupWindow = ParseFloat(key, value, 0f, 5f, DedupWindow); break;
                case "disperseseconds": DisperseSeconds = ParseFloat(key, value, 0f, 60f, DisperseSeconds); break;
                case "kickdead": KickDead = ParseBool(key, value, KickDead); break;
                case "tuerdisperses": KillDispersed = ParseBool(key, value, KillDispersed); break;
                case "pauselevee": case "pauselevée": RaisePause = ParseFloat(key, value, 0f, 600f, RaisePause); break;
                case "lignedefaut": case "lignedéfaut": DefaultLineSize = ParseInt(key, value, 0, 100, DefaultLineSize); break;
                case "retraitem": RetreatDistance = ParseFloat(key, value, 0f, 100f, RetreatDistance); break;
                case "moralreculm": MoraleRetreat = ParseFloat(key, value, 0f, 100f, MoraleRetreat); break;
                case "succession": Succession = ParseBool(key, value, Succession); break;
                case "successionmin": SuccessionMin = ParseInt(key, value, 1, 100, SuccessionMin); break;
                case "successionseconds": case "successiondelai": SuccessionSeconds = ParseFloat(key, value, 3f, 120f, SuccessionSeconds); break;
                case "successionrecul": SuccessionRetreat = ParseFloat(key, value, 0f, 50f, SuccessionRetreat); break;
                case "reprendrerayon": TakeoverRadius = ParseFloat(key, value, 2f, 50f, TakeoverRadius); break;
                case "moral": MoraleEnabled = ParseBool(key, value, MoraleEnabled); break;
                case "moralbas": MoraleLow = ParseFloat(key, value, 0f, 100f, MoraleLow); break;
                case "moralrecul": MoraleWaver = ParseFloat(key, value, 0f, 100f, MoraleWaver); break;
                case "moralperte": MoraleLoss = ParseFloat(key, value, 0f, 200f, MoraleLoss); break;
                case "moralofficier": MoraleOfficer = ParseFloat(key, value, 0f, 100f, MoraleOfficer); break;
                case "moralfeu": MoraleFire = ParseFloat(key, value, 0f, 20f, MoraleFire); break;
                case "moralregen": MoraleRegen = ParseFloat(key, value, 0f, 20f, MoraleRegen); break;
                case "moralcalme": MoraleCalm = ParseFloat(key, value, 0f, 120f, MoraleCalm); break;
                case "moralrallie": MoraleRally = ParseFloat(key, value, 0f, 100f, MoraleRally); break;
                case "moralrallierecharge": MoraleRallyCooldown = ParseFloat(key, value, 0f, 600f, MoraleRallyCooldown); break;
                case "rallyradius": case "rallierayon": RallyRadius = ParseFloat(key, value, 2f, 100f, RallyRadius); break;
                case "renforts": Reinforcements = ParseBool(key, value, Reinforcements); break;
                case "renfortsnombre": ReinforceCount = ParseInt(key, value, 1, 50, ReinforceCount); break;
                case "renfortsdelai": ReinforceCooldown = ParseFloat(key, value, 0f, 1800f, ReinforceCooldown); break;
                case "renfortsdistance": ReinforceDistance = ParseFloat(key, value, 2f, 100f, ReinforceDistance); break;
                case "fillmax": FillMax = ParseInt(key, value, 0, 250, FillMax); break;
                case "fillmin": FillMin = ParseInt(key, value, 0, 250, FillMin); break;
                case "fillfull": FillFull = ParseInt(key, value, 0, 250, FillFull); break;
                case "fillmarge": case "fillmargin": FillMargin = ParseInt(key, value, 0, 100, FillMargin); break;
                case "fillintervalle": case "fillinterval": FillInterval = ParseFloat(key, value, 1f, 600f, FillInterval); break;
                case "skin": SkinLines.Add(value); break;
                case "skinlibre": SkinFree = ParseBool(key, value, SkinFree); break;
                case "carre": case "carré": Square = ParseBool(key, value, Square); break;
                case "carredeuxrangs": SquareTwoRanks = ParseInt(key, value, 4, 200, SquareTwoRanks); break;
                case "carreportee": SquareRange = ParseFloat(key, value, 5f, 150f, SquareRange); break;
                case "carreporteecavalerie": SquareCavalryRange = ParseFloat(key, value, 5f, 150f, SquareCavalryRange); break;
                case "charge": LoadCapEnabled = ParseBool(key, value, LoadCapEnabled); break;
                case "chargeseuil": LoadThreshold = ParseFloat(key, value, 0f, 1000f, LoadThreshold); break;
                case "chargemin": LoadMin = ParseInt(key, value, 1, 250, LoadMin); break;
                case "chargepas": LoadStep = ParseInt(key, value, 1, 50, LoadStep); break;
                case "chargeintervalle": LoadInterval = ParseFloat(key, value, 1f, 120f, LoadInterval); break;
                case "lignesia": AiLines = ParseBool(key, value, AiLines); break;
                case "ianombre": AiLineCount = ParseInt(key, value, 0, 10, AiLineCount); break;
                case "iaparjoueurs": AiPerPlayers = ParseInt(key, value, 0, 100, AiPerPlayers); break;
                case "iataille": AiLineSize = ParseInt(key, value, 4, 20, AiLineSize); break;
                case "iaclasse":
                    if (GameIds.TryParseClass(value, out var aiClass)) AiClass = aiClass;
                    else CmdLog.Info("Classe inconnue pour iaclasse : " + value);
                    break;
                case "iadelai": AiDelay = ParseFloat(key, value, 0f, 600f, AiDelay); break;
                case "iaattente": AiWaveDelay = ParseFloat(key, value, 0f, 1800f, AiWaveDelay); break;
                case "iaportee": AiRange = ParseFloat(key, value, 10f, 200f, AiRange); break;
                case "iacharge": AiChargeDistance = ParseFloat(key, value, 0f, 100f, AiChargeDistance); break;
                case "iabloque": AiStuckSeconds = ParseFloat(key, value, 3f, 120f, AiStuckSeconds); break;
                case "iamodes": ParseModes(value); break;
                case "spawninterval": SpawnInterval = ParseFloat(key, value, 0.05f, 5f, SpawnInterval); break;
                default:
                    if (Reserved.Contains(key)) CmdLog.Info("Variable " + key + " prévue pour un lot suivant, ignorée.");
                    else CmdLog.Info("Variable inconnue ignorée : " + key);
                    break;
            }
            return true;
        }

        /// <summary>Cohérence après lecture de toutes les variables (l'ordre des lignes du serverconfig est libre).</summary>
        public static void Validate()
        {
            // Classes de ligne : infanterie à pied armée d'un fusil ou d'un mousquet. Porte-drapeau et musicien sont des
            // auxiliaires (rôle à part), officier, marine, cavalerie, artillerie et soutien (chirurgien, sapeur, charpentier)
            // n'ont rien à faire en rang ou ne tirent pas.
            var removed = AllowedClasses.Where(c => !IsLineClass(c)).ToList();
            foreach (var c in removed)
            {
                AllowedClasses.Remove(c);
                CmdLog.Warn("allowedclasses : " + GameIds.ClassName(c) + " n'est pas une classe de ligne, retirée.");
            }
            if (AllowedClasses.Count == 0) AllowedClasses.Add(GameIds.PlayerClass.ArmyLineInfantry);
            if (!AllowedClasses.Contains(DefaultClass))
            {
                var fallback = AllowedClasses.Contains(GameIds.PlayerClass.ArmyLineInfantry) ? GameIds.PlayerClass.ArmyLineInfantry : AllowedClasses.Min();
                CmdLog.Info("Classe par défaut " + GameIds.ClassName(DefaultClass) + " non permise, remplacée par " + GameIds.ClassName(fallback) + ".");
                DefaultClass = fallback;
            }
            if (!IsLineClass(AiClass))
            {
                CmdLog.Warn("iaclasse : " + GameIds.ClassName(AiClass) + " n'est pas une classe de ligne, ArmyLineInfantry à la place.");
                AiClass = GameIds.PlayerClass.ArmyLineInfantry;
            }
            if (FillMin > FillMax)
            {
                CmdLog.Warn("fillmin (" + FillMin + ") > fillmax (" + FillMax + ") : valeurs échangées.");
                var t = FillMin;
                FillMin = FillMax;
                FillMax = t;
            }
            if (DrumMin < AuxMin)
            {
                CmdLog.Warn("drummin (" + DrumMin + ") < auxmin (" + AuxMin + ") : drummin ramené à " + AuxMin + ".");
                DrumMin = AuxMin;
            }
            if (LoadMin < 1) LoadMin = 1;
            if (AiLines && AiModes.Count == 0)
                CmdLog.Warn("lignesia actif sans iamodes : lignes IA permises dans tous les modes de jeu (modes non vérifiés).");
            // Uniformes par défaut : contrôlés maintenant que skinlibre est connu.
            Uniforms.ClearDefaults();
            foreach (var line in SkinLines) ParseSkin(line);
        }

        /// <summary>« 1,3,20 » : numéros de GameplayMode (ArmyAssault 1, ArmyBattlefield 2, ArmyConquest 3, … Linebattle 22).</summary>
        private static void ParseModes(string value)
        {
            AiModes.Clear();
            foreach (var item in value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(item.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) && m > 0) AiModes.Add(m);
                else CmdLog.Info("Mode ignoré dans iamodes (numéro attendu) : " + item.Trim());
            }
        }

        /// <summary>Classe acceptée pour les soldats d'une ligne (allowedclasses, iaclasse).</summary>
        public static bool IsLineClass(int playerClass)
        {
            switch (playerClass)
            {
                case GameIds.PlayerClass.ArmyLineInfantry:
                case GameIds.PlayerClass.Guard:
                case GameIds.PlayerClass.Grenadier:
                case GameIds.PlayerClass.LightInfantry:
                case GameIds.PlayerClass.Rifleman:
                case GameIds.PlayerClass.Sergeant:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Liste séparée par des virgules (noms du jeu, alias ou numéros), par exemple « ArmyLineInfantry,Guard,15 ».</summary>
        private static void ParseClasses(string value)
        {
            var parsed = new HashSet<int>();
            foreach (var item in value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (GameIds.TryParseClass(item, out var c) && c != GameIds.PlayerClass.ArmyInfantryOfficer) parsed.Add(c);
                else CmdLog.Info("Classe ignorée dans allowedclasses : " + item.Trim());
            }
            if (parsed.Count == 0)
            {
                CmdLog.Info("allowedclasses vide ou invalide, liste précédente gardée.");
                return;
            }
            AllowedClasses.Clear();
            AllowedClasses.UnionWith(parsed);
        }

        private static bool ParseBool(string key, string value, bool fallback)
        {
            switch (value.ToLowerInvariant())
            {
                case "1": case "true": case "yes": case "on": case "oui": return true;
                case "0": case "false": case "no": case "off": case "non": return false;
                default:
                    CmdLog.Info(key + " : booléen invalide : " + value);
                    return fallback;
            }
        }

        private static int ParseInt(string key, string value, int min, int max, int fallback)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max) return n;
            CmdLog.Info(key + " invalide (" + min + " à " + max + " attendu) : " + value);
            return fallback;
        }

        // Virgule ou point décimal acceptés.
        private static float ParseFloat(string key, string value, float min, float max, float fallback)
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= min && f <= max) return f;
            CmdLog.Info(key + " invalide (" + min.ToString(CultureInfo.InvariantCulture) + " à " + max.ToString(CultureInfo.InvariantCulture)
                        + " attendu) : " + value);
            return fallback;
        }

        public static string ClassList()
        {
            return string.Join(", ", AllowedClasses.OrderBy(c => c).Select(GameIds.ClassName));
        }

        public static string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "actif={0}, maxbots={1}, maxperofficer={2}, classes=[{3}], defaultclass={4}, spacing={5}, rankdistance={6}, doublerank={7}, "
                + "aux={8} (drapeau dès {9}, tambour dès {10}), dedupwindow={11}, disperseseconds={12}, spawninterval={13}, kickdead={14}, "
                + "tuerdisperses={62}, pauselevee={63} s, lignedefaut={64}, retraitem={65} m, moralreculm={66} m ; "
                + "succession={15} (min {16}, {17} s, recul {18} m, rayon {19} m) ; moral={20} (bas {21}, recul {22}, perte {23}, officier {24}, "
                + "feu {25}, regen {26}/s après {27} s, rallie {28} toutes les {29} s, rayon {30} m) ; renforts={31} ({32} hommes, {33} s, {34} m) ; "
                + "remplissage : fillmax={35}, fillmin={36}, fillfull={37}{38}, marge {39}, toutes les {40} s ; "
                + "skins={41} (skinlibre={42}) ; carre={43} (2 rangs dès {44}, portée {45} m, cavalerie {46} m) ; "
                + "charge={47} (seuil {48}, min {49}, pas {50}, toutes les {51} s) ; "
                + "lignesia={52} ({53} ligne(s){54}, {55} {56}, après {57} s, vagues {58} s, portée {59} m, charge {60} m, bloquée {61} s, "
                + "modes {67})",
                IsEnabled, EffectiveMaxBots <= 0 ? "illimité" : EffectiveMaxBots.ToString(CultureInfo.InvariantCulture), EffectiveMaxPerOfficer, ClassList(), GameIds.ClassName(DefaultClass), Spacing, RankDistance,
                DoubleRank, Aux, AuxMin, DrumMin, DedupWindow, DisperseSeconds, SpawnInterval, KickDead,
                Succession, SuccessionMin, SuccessionSeconds, SuccessionRetreat, TakeoverRadius,
                MoraleEnabled, MoraleLow, MoraleWaver, MoraleLoss, MoraleOfficer, MoraleFire, MoraleRegen, MoraleCalm, MoraleRally,
                MoraleRallyCooldown, RallyRadius, Reinforcements, ReinforceCount, ReinforceCooldown, ReinforceDistance,
                FillMax, FillMin, FillFull, FillFull <= 0 ? " (désactivé)" : "", FillMargin, FillInterval,
                Uniforms.DefaultCount, SkinFree, Square, SquareTwoRanks, SquareRange, SquareCavalryRange,
                LoadCapEnabled, LoadThreshold > 0f ? LoadThreshold.ToString("0.#", CultureInfo.InvariantCulture) + " ms" : "auto",
                LoadMin, LoadStep, LoadInterval,
                AiOverride ?? AiLines, AiLineCount, AiPerPlayers > 0 ? " ou 1 pour " + AiPerPlayers + " joueurs" : "", AiLineSize,
                GameIds.ClassName(AiClass), AiDelay, AiWaveDelay, AiRange, AiChargeDistance, AiStuckSeconds,
                KillDispersed, RaisePause, DefaultLineSize, RetreatDistance, MoraleRetreat,
                AiModes.Count == 0 ? "tous" : string.Join(",", AiModes.OrderBy(m => m)));
        }
    }
}
