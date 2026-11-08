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
            if (!Enabled) return;
            Log("Protection " + itemId + " posée : type " + type + " " + Describe(obj) + ".");
        }

        public static void EmplacementConstructed(int itemId)
        {
            if (!Enabled) return;
            Log("Protection " + itemId + " terminée.");
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
                if (c != null) names.Add(c.GetType().Name);
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
