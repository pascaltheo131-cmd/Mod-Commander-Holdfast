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
        /// <summary>Plafond de bots du mod sur tout le serveur (toutes lignes confondues).</summary>
        public static int MaxBots = 60;
        /// <summary>Plafond de bots par officier.</summary>
        public static int MaxPerOfficer = 20;
        /// <summary>Classes que l'officier peut demander (valeurs GameIds.PlayerClass).</summary>
        public static readonly HashSet<int> AllowedClasses = new HashSet<int>
        {
            GameIds.PlayerClass.ArmyLineInfantry, GameIds.PlayerClass.Guard, GameIds.PlayerClass.Grenadier,
            GameIds.PlayerClass.LightInfantry, GameIds.PlayerClass.Rifleman,
        };
        public static int DefaultClass = GameIds.PlayerClass.ArmyLineInfantry;
        /// <summary>Distance entre deux hommes d'un même rang, en mètres.</summary>
        public static float Spacing = 1.0f;
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
        /// Retirer (kick) les bots morts. Faux par défaut : chaque kick est noté dans le journal admin et part au webhook Discord ;
        /// un bot mort reste connecté jusqu'au changement de manche, où le jeu supprime tous les bots.
        /// </summary>
        public static bool KickDead;
        /// <summary>Intervalle minimal entre deux demandes d'apparition de bot (étale la charge).</summary>
        public static float SpawnInterval = 0.25f;

        // ---- Lot A, n° 3 : succession ----------------------------------------------------------
        public static bool Succession = true;
        /// <summary>Taille minimale (bots vivants) pour qu'un sergent tienne la ligne ; en dessous, dispersion directe.</summary>
        public static int SuccessionMin = 6;
        public static float SuccessionSeconds = 20f;
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
        public static int FillMax = 60;
        /// <summary>Plafond à partir de FillFull vrais joueurs.</summary>
        public static int FillMin = 10;
        /// <summary>Nombre de vrais joueurs pour lequel le plafond vaut FillMin ; 0 = fonction désactivée (plafond = maxbots).</summary>
        public static int FillFull = 40;
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
        public static bool LoadCapEnabled = true;
        /// <summary>Seuil de la durée moyenne d'une image, en ms ; 0 = automatique (1,5 × l'intervalle normal estimé).</summary>
        public static float LoadThreshold;
        /// <summary>Plancher du plafond de charge.</summary>
        public static int LoadMin = 10;
        /// <summary>Baisse du plafond à chaque mesure au-dessus du seuil.</summary>
        public static int LoadStep = 5;
        /// <summary>Intervalle entre deux décisions, en secondes (le plafond remonte de 1 par intervalle quand tout va bien).</summary>
        public static float LoadInterval = 5f;

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
            // Plafond admin (!cmd max) ou maxbots : plafond de tous les autres.
            var ceiling = EffectiveMaxBots;
            if (FillFull <= 0) return ceiling;
            var t = Mathf.Clamp01(realPlayers / (float)FillFull);
            return Mathf.Min(ceiling, Mathf.RoundToInt(Mathf.Lerp(FillMax, FillMin, t)));
        }

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
        private static readonly string[] Reserved =
        {
            "lignesia",
        };

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
                case "maxperofficer": case "plafondofficier": MaxPerOfficer = ParseInt(key, value, 1, 100, MaxPerOfficer); break;
                case "allowedclasses": case "classes": ParseClasses(value); break;
                case "defaultclass": case "classe":
                    if (GameIds.TryParseClass(value, out var c)) DefaultClass = c;
                    else CmdLog.Info("Classe inconnue pour " + key + " : " + value);
                    break;
                case "spacing": case "espacement": Spacing = ParseFloat(key, value, 0.5f, 5f, Spacing); break;
                case "rankdistance": case "distancerangs": RankDistance = ParseFloat(key, value, 0.5f, 5f, RankDistance); break;
                case "doublerank": case "rangdouble": DoubleRank = ParseBool(key, value, DoubleRank); break;
                case "aux": Aux = ParseBool(key, value, Aux); break;
                case "auxmin": AuxMin = ParseInt(key, value, 1, 100, AuxMin); break;
                case "drummin": DrumMin = ParseInt(key, value, 1, 100, DrumMin); break;
                case "dedupwindow": DedupWindow = ParseFloat(key, value, 0f, 5f, DedupWindow); break;
                case "disperseseconds": DisperseSeconds = ParseFloat(key, value, 0f, 60f, DisperseSeconds); break;
                case "kickdead": KickDead = ParseBool(key, value, KickDead); break;
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
                case "skin": ParseSkin(value); break;
                case "skinlibre": SkinFree = ParseBool(key, value, SkinFree); break;
                case "carre": case "carré": Square = ParseBool(key, value, Square); break;
                case "carredeuxrangs": SquareTwoRanks = ParseInt(key, value, 4, 200, SquareTwoRanks); break;
                case "carreportee": SquareRange = ParseFloat(key, value, 5f, 150f, SquareRange); break;
                case "carreporteecavalerie": SquareCavalryRange = ParseFloat(key, value, 5f, 150f, SquareCavalryRange); break;
                case "charge": LoadCapEnabled = ParseBool(key, value, LoadCapEnabled); break;
                case "chargeseuil": LoadThreshold = ParseFloat(key, value, 0f, 1000f, LoadThreshold); break;
                case "chargemin": LoadMin = ParseInt(key, value, 0, 250, LoadMin); break;
                case "chargepas": LoadStep = ParseInt(key, value, 1, 50, LoadStep); break;
                case "chargeintervalle": LoadInterval = ParseFloat(key, value, 1f, 120f, LoadInterval); break;
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
            if (AllowedClasses.Contains(DefaultClass)) return;
            var fallback = AllowedClasses.Contains(GameIds.PlayerClass.ArmyLineInfantry) ? GameIds.PlayerClass.ArmyLineInfantry : AllowedClasses.Min();
            CmdLog.Info("Classe par défaut " + GameIds.ClassName(DefaultClass) + " non permise, remplacée par " + GameIds.ClassName(fallback) + ".");
            DefaultClass = fallback;
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
                + "aux={8} (drapeau dès {9}, tambour dès {10}), dedupwindow={11}, disperseseconds={12}, spawninterval={13}, kickdead={14} ; "
                + "succession={15} (min {16}, {17} s, recul {18} m, rayon {19} m) ; moral={20} (bas {21}, recul {22}, perte {23}, officier {24}, "
                + "feu {25}, regen {26}/s après {27} s, rallie {28} toutes les {29} s, rayon {30} m) ; renforts={31} ({32} hommes, {33} s, {34} m) ; "
                + "remplissage : fillmax={35}, fillmin={36}, fillfull={37}{38}, marge {39}, toutes les {40} s ; "
                + "skins={41} (skinlibre={42}) ; carre={43} (2 rangs dès {44}, portée {45} m, cavalerie {46} m) ; "
                + "charge={47} (seuil {48}, min {49}, pas {50}, toutes les {51} s)",
                IsEnabled, EffectiveMaxBots, EffectiveMaxPerOfficer, ClassList(), GameIds.ClassName(DefaultClass), Spacing, RankDistance,
                DoubleRank, Aux, AuxMin, DrumMin, DedupWindow, DisperseSeconds, SpawnInterval, KickDead,
                Succession, SuccessionMin, SuccessionSeconds, SuccessionRetreat, TakeoverRadius,
                MoraleEnabled, MoraleLow, MoraleWaver, MoraleLoss, MoraleOfficer, MoraleFire, MoraleRegen, MoraleCalm, MoraleRally,
                MoraleRallyCooldown, RallyRadius, Reinforcements, ReinforceCount, ReinforceCooldown, ReinforceDistance,
                FillMax, FillMin, FillFull, FillFull <= 0 ? " (désactivé)" : "", FillMargin, FillInterval,
                Uniforms.DefaultCount, SkinFree, Square, SquareTwoRanks, SquareRange, SquareCavalryRange,
                LoadCapEnabled, LoadThreshold > 0f ? LoadThreshold.ToString("0.#", CultureInfo.InvariantCulture) + " ms" : "auto",
                LoadMin, LoadStep, LoadInterval);
        }
    }
}
