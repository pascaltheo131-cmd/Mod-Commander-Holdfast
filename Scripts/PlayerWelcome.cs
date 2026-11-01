using System.Collections.Generic;

namespace HoldfastCommander
{
    /// <summary>Message privé envoyé une fois par connexion, dès l'arrivée avant le choix de faction.</summary>
    public static class PlayerWelcome
    {
        private sealed class Pending
        {
            public int PlayerId;
            public float NextAt;
            public bool JoinedRound;
            public int NextMessage;
        }

        private static readonly Dictionary<int, Pending> PendingByPlayer = new Dictionary<int, Pending>();
        private static readonly Dictionary<int, string> BackendByPlayer = new Dictionary<int, string>();
        private static readonly HashSet<string> WelcomedBackends = new HashSet<string>();

        private static readonly string[] Messages =
        {
            "Bienvenue ! Choisis ta faction puis la classe Officier pour commander. !ligne <nombre> [préfixe] [uniforme] lève ta ligne. Pour reformer/suivre : !former, !suivre, !rangs 1|2.",
            "Autres commandes : !baionnette, !accroupi, !pas, !carre, !moral, !renforts, !renvoyer, !reprendre. Uniformes : !skins [classe], !skin <classe> <id>."
        };

        public static void OnConnected(int playerId, string backendId, float now)
        {
            if (string.IsNullOrEmpty(backendId)) return; // bots
            BackendByPlayer[playerId] = backendId;
            if (WelcomedBackends.Contains(backendId)) return;
            WelcomedBackends.Add(backendId);
            PendingByPlayer[playerId] = new Pending
            {
                PlayerId = playerId,
                NextAt = now + 0.15f
            };
        }

        public static void OnJoinedRound(int playerId)
        {
            if (!PendingByPlayer.TryGetValue(playerId, out var pending)) return;
            pending.JoinedRound = true;
            pending.NextAt = 0f; // Repli si la console a refusé le message pré-faction.
        }

        public static void OnDisconnected(int playerId)
        {
            PendingByPlayer.Remove(playerId);
            if (!BackendByPlayer.TryGetValue(playerId, out var backendId)) return;
            BackendByPlayer.Remove(playerId);
            WelcomedBackends.Remove(backendId);
        }

        public static void Tick(float now)
        {
            if (PendingByPlayer.Count == 0) return;
            var done = new List<int>();
            foreach (var pair in PendingByPlayer)
            {
                var pending = pair.Value;
                if (now < pending.NextAt) continue;
                var player = Players.Get(pending.PlayerId);
                if (player == null || player.IsBot)
                {
                    done.Add(pair.Key);
                    continue;
                }

                while (pending.NextMessage < Messages.Length)
                {
                    if (!ServerConsole.Private(pending.PlayerId, Messages[pending.NextMessage]))
                    {
                        if (pending.JoinedRound) done.Add(pair.Key);
                        else pending.NextAt = float.MaxValue; // Réessayer une seule fois après OnPlayerJoined.
                        break;
                    }
                    pending.NextMessage++;
                }
                if (pending.NextMessage >= Messages.Length) done.Add(pair.Key);
            }
            foreach (var id in done) PendingByPlayer.Remove(id);
        }
    }
}
