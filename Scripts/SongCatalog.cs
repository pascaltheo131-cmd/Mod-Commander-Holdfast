using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Répertoire des morceaux du serveur (InstrumentSongOptionsRepository, relu dans les données du jeu, fichier level3) :
    /// numéro de morceau → instruments autorisés (numéros de InstrumentType : 1 piano, 2 synthé, 3 tambour, 4 fifre,
    /// 10 banjo). On ne tire QUE les morceaux qui contiennent l'instrument que le musicien tient : le client n'anime le
    /// musicien que si l'instrument du paquet est celui qu'il a en main.
    /// </summary>
    public static class SongCatalog
    {
        private struct Song
        {
            public int Id;
            public string Name;
            public int[] Instruments;
        }

        private static readonly int[] DrumSongs = { 2, 3 };
        private static readonly int[] FifeSongs = { 2, 4 };
        private static readonly int[] Shanties = { 10, 1 };

        private static readonly Song[] Songs =
        {
            new Song { Id = 20, Name = "La Victoire Est À Nous", Instruments = DrumSongs },
            new Song { Id = 22, Name = "Le Chant De L'Oignon", Instruments = DrumSongs },
            new Song { Id = 26, Name = "Pas Cadencé", Instruments = DrumSongs },
            new Song { Id = 31, Name = "Cadet Rousselle", Instruments = DrumSongs },
            new Song { Id = 32, Name = "La Marseillaise", Instruments = DrumSongs },
            new Song { Id = 33, Name = "Trois Tambours", Instruments = DrumSongs },
            new Song { Id = 34, Name = "Chant Du Départ", Instruments = DrumSongs },
            new Song { Id = 36, Name = "Marlbrough S'en Va-t-en Guerre", Instruments = DrumSongs },
            new Song { Id = 63, Name = "Fanchon", Instruments = FifeSongs },
            new Song { Id = 65, Name = "Percer Le Flanc", Instruments = FifeSongs },
            new Song { Id = 111, Name = "Wellerman", Instruments = Shanties },
            new Song { Id = 112, Name = "Drunken Sailor", Instruments = Shanties },
            new Song { Id = 113, Name = "Randy Dandy Oh", Instruments = Shanties },
            new Song { Id = 114, Name = "Leave Her Johnny", Instruments = Shanties },
        };

        private static bool Allows(Song s, int instrument)
        {
            foreach (var i in s.Instruments) if (i == instrument) return true;
            return false;
        }

        /// <summary>Le morceau existe-t-il pour cet instrument ?</summary>
        public static bool Fits(int songId, int instrument)
        {
            foreach (var s in Songs) if (s.Id == songId) return Allows(s, instrument);
            return false;
        }

        /// <summary>Nom du morceau pour le journal (« ? » s'il n'est pas dans la table).</summary>
        public static string Name(int songId)
        {
            foreach (var s in Songs) if (s.Id == songId) return s.Name;
            return "?";
        }

        /// <summary>Morceau au hasard jouable à l'instrument tenu, différent du précédent si possible ; -1 si aucun.</summary>
        public static int Pick(int instrument, int previous)
        {
            var list = new List<int>();
            foreach (var s in Songs) if (Allows(s, instrument) && s.Id != previous) list.Add(s.Id);
            if (list.Count == 0) return Fits(previous, instrument) ? previous : -1;
            return list[Random.Range(0, list.Count)];
        }
    }
}
