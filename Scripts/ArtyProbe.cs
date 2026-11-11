using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Étape 1 de l'artillerie : observation seulement. Journalise les actions des joueurs sur les objets de la carte
    /// (canons…) et les protections posées par les sapeurs, pour connaître la suite exacte des actions à faire jouer
    /// aux bots. Ne fait rien d'autre. Réglage : CMD:arty_journal on|off (on par défaut).
    /// </summary>
    public static class ArtyProbe
    {
        public static bool Enabled = true;

        // Objets déjà décrits (composants, hiérarchie) : une seule fois par manche.
        private static readonly HashSet<int> Described = new HashSet<int>();
        // ContinueInteraction (visée…) arrive en rafale : compté, résumé à la fin de l'interaction.
        private static readonly Dictionary<long, int> Continues = new Dictionary<long, int>();

        public static void Clear()
        {
            Described.Clear();
            Continues.Clear();
        }

        public static void Interaction(int playerId, int objectId, GameObject obj, int activation, int transition)
        {
            if (!Enabled) return;
            var key = ((long)playerId << 32) | (uint)objectId;
            if (activation == 3) // ContinueInteraction
            {
                Continues.TryGetValue(key, out var n);
                Continues[key] = n + 1;
                return;
            }
            var extra = "";
            if (Continues.TryGetValue(key, out var count))
            {
                extra = " (" + count + " suites avant)";
                Continues.Remove(key);
            }
            Log("Objet " + objectId + " " + Describe(obj) + " par " + Who(playerId) + " : action " + ActivationName(activation)
                + " transition " + transition + extra + ".");
            if (obj != null && Described.Add(objectId)) Log("Objet " + objectId + " détail : " + Detail(obj));
        }

        public static void EmplacementPlaced(int itemId, GameObject obj, int type)
        {
            _placed++;
            if (!Enabled) return;
            Log("Protection " + itemId + " posée : type " + type + " " + Describe(obj) + ".");
        }

        public static void EmplacementConstructed(int itemId)
        {
            if (!Enabled) return;
            Log("Protection " + itemId + " terminée.");
        }

        // ---- Essai : construction par le sapeur bot ------------------------------------------
        // Code du jeu (ServerEmplacementManager.ClientRequestingPlaceEmplacement) : « sapperBuildItem <bot> <type> <x,y,z>
        // <rx,ry,rz> <parent> <socket> ». parent -1 = posé au sol ; socket est un octet (0 à 255 : « -1 » était refusé, vu à
        // l'essai z60). Le jeu exige l'outil du type en main (marteau ou bêche), assez de matériaux, la limite de
        // constructions et un emplacement libre ; la commande répond « ok » même refusée : seule la ligne « Protection …
        // posée » prouve la pose. Avec CMD:arty_essai (sapeur ou tout), le sapeur d'une pièce posée depuis 10 s essaie tour à
        // tour un outil et un type, toutes les 2 s, jusqu'à la première pose. Le journal dit laquelle a marché.

        /// <summary>« », « sapeur » ou « tout ».</summary>
        public static string Trial = "";

        private static readonly string[] TrialTools = { "DiggingSpade", "ConstructingHammer", "DiggingSpade", "ConstructingHammer", "ConstructingHammer" };
        private static readonly string[] TrialTypes = { "EarthworksArtillery", "EarthworksArtillery", "EarthworksCrouching", "EarthworksCrouching", "Gabion" };
        private static int _placed;

        public static void SapperTrial(Bot bot, Battery b, float now)
        {
            if (Trial != "sapeur" && Trial != "tout") return;
            // Trois temps par forme k, 2 s entre chacun : 3k outil en main, 3k+1 construction envoyée, 3k+2 résultat.
            if (b.SapperStep >= TrialTools.Length * 3 || b.Moving || now - b.SetDownAt < 10f || now - b.SapperAt < 2f) return;
            b.SapperAt = now;
            var k = b.SapperStep / 3;
            switch (b.SapperStep % 3)
            {
                case 0:
                    if (bot.Weapon != TrialTools[k]) Try("carbonPlayers switchWeapon " + TrialTools[k] + " " + ServerConsole.Id(bot.PlayerId));
                    b.SapperStep++;
                    return;
                case 1:
                    Log("Essai sapeur " + (k + 1) + "/" + TrialTools.Length + " : " + TrialTypes[k] + ", arme en main « " + bot.Weapon + " ».");
                    _placed = 0;
                    Build(bot, b, k);
                    b.SapperStep++;
                    return;
                default:
                    if (_placed > 0)
                    {
                        Log("Essai sapeur : pose réussie avec " + TrialTools[k] + " et " + TrialTypes[k] + ".");
                        b.SapperStep = TrialTools.Length * 3;
                        return;
                    }
                    b.SapperStep++;
                    if (b.SapperStep >= TrialTools.Length * 3) Log("Essai sapeur : aucune forme n'a posé de protection.");
                    return;
            }
        }

        private static void Build(Bot bot, Battery b, int k)
        {
            // À 3 m devant la pièce, sur le côté de la ligne de tir.
            var spot = b.CannonPos + b.Forward * 3f + b.Right * 2f;
            if (Mathf.Abs(spot.x) > 100000f || Mathf.Abs(spot.z) > 100000f) { Log("Essai sapeur : position du canon aberrante, rien envoyé."); return; }
            if (Ground.TryFind(spot, b.CannonPos.y, out var ground)) spot = ground;
            Try("carbonPlayers sapperBuildItem " + ServerConsole.Id(bot.PlayerId) + " " + TrialTypes[k] + " "
                + ServerConsole.Num(spot.x) + "," + ServerConsole.Num(spot.y) + "," + ServerConsole.Num(spot.z)
                + " 0," + ServerConsole.Num(b.FaceYaw) + ",0 -1 0");
        }

        private static bool Try(string command)
        {
            var ok = ServerConsole.Execute(command, "Essai arty", false, out var output);
            CmdLog.Info("[ARTY] Essai « " + command + " » : " + (ok ? "accepté" : "refusé") + ", réponse « "
                        + (output ?? "").Trim() + " ».");
            return ok;
        }

        private static void Log(string message) => CmdLog.Info("[ARTY] " + message);

        private static string Who(int playerId)
        {
            var p = Players.Get(playerId);
            if (p == null) return "joueur " + playerId;
            return (p.IsBot ? "bot " : "joueur ") + playerId + " « " + p.Name + " » classe " + p.Class + " camp " + p.Faction;
        }

        private static string Describe(GameObject obj)
        {
            if (obj == null) return "(sans objet)";
            var pos = obj.transform.position;
            var rot = obj.transform.eulerAngles;
            return "« " + obj.name + " » en " + V(pos) + " cap " + rot.y.ToString("0");
        }

        private static string Detail(GameObject obj)
        {
            var sb = new StringBuilder();
            sb.Append("chemin ");
            var t = obj.transform;
            var path = new List<string>();
            for (var i = 0; t != null && i < 6; i++, t = t.parent) path.Add(t.name);
            path.Reverse();
            sb.Append(string.Join("/", path.ToArray()));
            sb.Append(" ; composants ");
            var names = new List<string>();
            foreach (var c in obj.GetComponents<Component>())
                // Pas de GetType().Name : System.Reflection est refusé par la vérification du SDK. ToString donne « nom (Type) ».
                if (c != null) names.Add(c.ToString());
            sb.Append(string.Join(", ", names.ToArray()));
            sb.Append(" ; enfants ").Append(obj.transform.childCount);
            return sb.ToString();
        }

        private static string V(Vector3 v) => v.x.ToString("0.0") + "," + v.y.ToString("0.0") + "," + v.z.ToString("0.0");

        private static string ActivationName(int a)
        {
            switch (a)
            {
                case 1: return "unique";
                case 2: return "début";
                case 3: return "suite";
                case 4: return "fin";
                case 5: return "locale";
                default: return a.ToString();
            }
        }
    }
}
