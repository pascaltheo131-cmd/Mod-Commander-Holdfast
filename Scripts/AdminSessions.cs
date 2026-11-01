using System.Collections.Generic;

namespace HoldfastCommander
{
    /// <summary>
    /// Retient les droits obtenus par rc login pendant la connexion au serveur.
    /// Le SteamID reste stable quand Holdfast réattribue un playerId à la rotation.
    /// </summary>
    public static class AdminSessions
    {
        private static readonly Dictionary<int, string> Identities = new Dictionary<int, string>();
        private static readonly Dictionary<int, ulong> SteamIdentities = new Dictionary<int, ulong>();
        private static readonly HashSet<string> Authenticated = new HashSet<string>();
        private static readonly HashSet<ulong> AuthenticatedSteamIds = new HashSet<ulong>();

        public static bool Connect(int id, string backend, bool autoAdmin)
        {
            // L'id peut changer à la rotation. Le droit RC reste attaché au SteamID.
            // Le lien steam↔id est conservé jusqu'à OnPlayerJoined ou la vraie déconnexion.
            if (Identities.TryGetValue(id, out var old) && old != backend)
            {
                Identities.Remove(id);
                Authenticated.Remove(old);
            }

            if (string.IsNullOrEmpty(backend))
            {
                Identities.Remove(id);
                return false;
            }

            var obsolete = new List<int>();
            foreach (var pair in Identities)
                if (pair.Key != id && pair.Value == backend) obsolete.Add(pair.Key);
            foreach (var prior in obsolete) Identities.Remove(prior);

            Identities[id] = backend;
            if (autoAdmin) Authenticated.Add(backend);
            return IsAdmin(id);
        }

        public static void BindSteam(int id, ulong steamId, bool admin)
        {
            if (steamId == 0) return;

            // Si le jeu réutilise un playerId pour un autre joueur, l'ancien droit ne suit pas.
            if (SteamIdentities.TryGetValue(id, out var oldSteamId) && oldSteamId != steamId)
                AuthenticatedSteamIds.Remove(oldSteamId);

            SteamIdentities[id] = steamId;
            if (admin) AuthenticatedSteamIds.Add(steamId);
        }

        public static bool IsSteamAdmin(ulong steamId)
        {
            return steamId != 0 && AuthenticatedSteamIds.Contains(steamId);
        }

        public static bool IsAdmin(int id)
        {
            return Identities.TryGetValue(id, out var key) && Authenticated.Contains(key);
        }

        public static void Login(int id, ulong steamId)
        {
            if (Identities.TryGetValue(id, out var key)) Authenticated.Add(key);
            BindSteam(id, steamId, true);
        }

        public static void Disconnect(int id)
        {
            if (Identities.TryGetValue(id, out var key))
            {
                Identities.Remove(id);
                Authenticated.Remove(key);
            }

            if (SteamIdentities.TryGetValue(id, out var steamId))
            {
                SteamIdentities.Remove(id);
                AuthenticatedSteamIds.Remove(steamId);
            }
        }
    }
}
