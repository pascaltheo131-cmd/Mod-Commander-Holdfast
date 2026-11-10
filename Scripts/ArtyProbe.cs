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

        // ---- Essais z60 : pointage et digues par un bot --------------------------------------
        // « interactableObjectUpdateCannon » et « sapperBuildItem » n'ont jamais été envoyées pour un bot. Avec
        // CMD:arty_essai (pointeur, sapeur ou tout), un servant les essaie une fois par pièce, canon posé depuis 10 s, et la
        // réponse du jeu est journalisée ([ARTY] Essai…). Aucun tir. Éteint par défaut.

        /// <summary>« », « pointeur », « sapeur » ou « tout ».</summary>
        public static string Trial = "";

        private static bool Wants(string what) => Trial == what || Trial == "tout";

        private static bool Settled(Battery b, float now) => !b.Moving && !b.Manned && now - b.SetDownAt >= 10f;

        /// <summary>
        /// Le sapeur de la pièce essaie de construire une digue à 3 m devant le canon, sur le côté de la ligne de tir. Les
        /// noms de type sont essayés dans l'ordre jusqu'au premier accepté ; une protection posée est aussi journalisée par
        /// EmplacementPlaced.
        /// </summary>
        public static void SapperTrial(Bot bot, Battery b, float now)
        {
            if (!Wants("sapeur") || b.SapperTried || !Settled(b, now)) return;
            b.SapperTried = true;
            var spot = b.CannonPos + b.Forward * 3f + b.Right * 2f;
            if (Ground.TryFind(spot, b.CannonPos.y, out var ground)) spot = ground;
            var place = ServerConsole.Num(spot.x) + "," + ServerConsole.Num(spot.y) + "," + ServerConsole.Num(spot.z)
                        + " 0," + ServerConsole.Num(b.FaceYaw) + ",0 -1 -1";
            foreach (var type in new[] { "EarthworksArtillery", "EarthworksCrouching", "Deprecated_Earthworks" })
                if (Try("carbonPlayers sapperBuildItem " + ServerConsole.Id(bot.PlayerId) + " " + type + " " + place)) break;
        }

        public static bool WantsPointerTrial(Battery b, float now) => Wants("pointeur") && !b.PointerTried && Settled(b, now);

        /// <summary>
        /// Un servant, contre la pièce, la prend en main, envoie la commande de pointage sous trois formes (sans argument :
        /// le jeu répond en général par la syntaxe attendue), puis la lâche. Il ne tire pas.
        /// </summary>
        public static void PointerTrial(Bot bot, Battery b, float now)
        {
            if (!WantsPointerTrial(b, now)) return;
            b.PointerTried = true;
            var id = ServerConsole.Id(bot.PlayerId);
            var man = "carbonPlayers interactableObject " + id + " " + b.CannonId + " -1 ";
            Try(man + Battery.Start + " " + Battery.TrMan);
            Try("carbonPlayers interactableObjectUpdateCannon");
            Try("carbonPlayers interactableObjectUpdateCannon " + id + " " + b.CannonId);
            Try("carbonPlayers interactableObjectUpdateCannon " + id + " " + b.CannonId + " -1 " + ServerConsole.Num(b.FaceYaw) + " 5");
            Try(man + Battery.End + " " + Battery.TrMan);
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
