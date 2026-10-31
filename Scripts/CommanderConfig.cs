using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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

        // ---- Modifiés par les commandes admin jusqu'au redémarrage (null = valeur du serverconfig) ----
        public static bool? EnabledOverride;
        public static int? MaxBotsOverride;
        public static int? MaxPerOfficerOverride;

        public static bool IsEnabled => EnabledOverride ?? Enabled;
        public static int EffectiveMaxBots => MaxBotsOverride ?? MaxBots;
        public static int EffectiveMaxPerOfficer => MaxPerOfficerOverride ?? MaxPerOfficer;

        // Clés prévues pour les lots suivants (docs/PLAN.md, étape 2) : reconnues pour ne pas être signalées comme inconnues,
        // mais ignorées tant que la fonction n'existe pas. Ajouter ici la clé, puis son « case » dans Apply quand elle est codée.
        private static readonly string[] Reserved =
        {
            "succession", "moral", "renforts", "renfortsdelai", "remplissage", "lignesia", "skin", "carre", "plafondcharge",
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
                + "aux={8} (drapeau dès {9}, tambour dès {10}), dedupwindow={11}, disperseseconds={12}, spawninterval={13}, kickdead={14}",
                IsEnabled, EffectiveMaxBots, EffectiveMaxPerOfficer, ClassList(), GameIds.ClassName(DefaultClass), Spacing, RankDistance,
                DoubleRank, Aux, AuxMin, DrumMin, DedupWindow, DisperseSeconds, SpawnInterval, KickDead);
        }
    }
}
