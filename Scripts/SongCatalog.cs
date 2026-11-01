using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Numéros de morceaux reconnus par le serveur, appris en jouant (playSong répond « Song name » ou « Song does not exist »).
    /// Le répertoire est dans une scène du jeu, pas dans le code : on tire au hasard parmi les numéros connus valides, sinon
    /// parmi les numéros pas encore essayés de 1 à CMD:musiquemorceaux.
    /// </summary>
    public static class SongCatalog
    {
        private static readonly List<int> Valid = new List<int>();
        private static readonly HashSet<int> Invalid = new HashSet<int>();

        public static bool IsValid(int id) => Valid.Contains(id);

        public static void MarkValid(int id)
        {
            if (!Valid.Contains(id)) Valid.Add(id);
        }

        public static void MarkInvalid(int id)
        {
            Invalid.Add(id);
            Valid.Remove(id);
        }

        /// <summary>Morceau au hasard, différent du précédent si possible.</summary>
        public static int Pick(int previous)
        {
            var max = CommanderConfig.MusicSongs;
            var untried = new List<int>();
            for (var i = 1; i <= max; i++) if (!Invalid.Contains(i) && !Valid.Contains(i)) untried.Add(i);
            // Tant qu'on connaît moins de 3 morceaux valides, on essaie aussi des numéros nouveaux.
            if (untried.Count > 0 && (Valid.Count < 3 || Random.value < 0.3f)) return untried[Random.Range(0, untried.Count)];
            if (Valid.Count == 0) return Mathf.Max(1, previous);
            if (Valid.Count == 1) return Valid[0];
            var next = previous;
            while (next == previous) next = Valid[Random.Range(0, Valid.Count)];
            return next;
        }
    }
}
