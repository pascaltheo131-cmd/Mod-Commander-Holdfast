using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HoldfastCommander
{
    /// <summary>
    /// Uniformes des bots (lot B, n° 15). L'id d'uniforme est propre à chaque couple faction/classe et commence à 1 ;
    /// un id inconnu du jeu donne l'uniforme 1 (docs/CARBONPLAYERS.md §4). La table embarquée est générée depuis
    /// CARBONPLAYERS.md §4.2 (scène level1 du serveur, 31/10/2026) ; les uniformes ajoutés par des mods n'y sont pas
    /// (CMD:skinlibre pour les accepter). Nom vide : classe relevée sans le détail des noms.
    /// Choix de l'uniforme d'un bot : choix de l'officier (!skin, ou !ligne … &lt;uniforme&gt;), sinon CMD:skin, sinon 1.
    /// </summary>
    public static class Uniforms
    {
        private struct Entry
        {
            public string Name;
        }

        private static readonly Dictionary<long, Entry> Table = new Dictionary<long, Entry>();
        private static readonly Dictionary<long, List<int>> IdsByPair = new Dictionary<long, List<int>>();
        // CMD:skin:<Faction>:<Classe>:<uniformId>
        private static readonly Dictionary<long, int> ConfigDefaults = new Dictionary<long, int>();

        static Uniforms()
        {
            Add(1, 1, 1, "23rd Royal Welch Fusiliers");
            Add(1, 1, 2, "92nd Gordon Highlanders");
            Add(1, 1, 3, "Loyal Lusitanian Legion", true);
            Add(1, 1, 4, "Loyal Inverness Fencible Highlanders", true);
            Add(1, 6, 1, "23rd Royal Welch Fusiliers");
            Add(1, 6, 2, "92nd Gordon Highlanders", true);
            Add(1, 6, 3, "Loyal Inverness Fencible Highlanders", true);
            Add(1, 20, 1, "23rd Royal Welch Fusiliers");
            Add(1, 12, 1, "23rd Royal Welch Fusiliers");
            Add(1, 12, 2, "Loyal Inverness Fencible Highlanders", true);
            Add(1, 19, 1, "Royal Navy");
            Add(1, 19, 2, "Royal Navy");
            Add(1, 19, 3, "56th West Essex");
            Add(1, 19, 4, "56th West Essex");
            Add(1, 19, 5, "92nd Gordon Highlanders");
            Add(1, 19, 6, "Royal Navy", true);
            Add(1, 19, 7, "56th West Essex", true);
            Add(1, 16, 1, "1st Foot Guards, St James’ Palace");
            Add(1, 16, 2, "Coldstream Regiment of Foot Guards", true);
            Add(1, 18, 1, "41st Regiment of Foot");
            Add(1, 18, 2, "Belgian 7th Line Battalion", true);
            Add(1, 11, 1, "93rd Sutherland Highlanders");
            Add(1, 10, 1, "95th Rifle Regiment");
            Add(1, 2, 1, "");
            Add(1, 2, 2, "");
            Add(1, 3, 1, "");
            Add(1, 3, 2, "");
            Add(1, 4, 1, "");
            Add(1, 4, 2, "");
            Add(1, 4, 3, "");
            Add(1, 4, 4, "");
            Add(1, 4, 5, "");
            Add(1, 5, 1, "");
            Add(1, 7, 1, "");
            Add(1, 8, 1, "");
            Add(1, 9, 1, "");
            Add(1, 9, 2, "");
            Add(1, 13, 1, "");
            Add(1, 21, 1, "");
            Add(1, 21, 2, "");
            Add(1, 23, 1, "");
            Add(1, 24, 1, "");
            Add(1, 25, 1, "");
            Add(2, 1, 1, "85ème Régiment d'Infanterie");
            Add(2, 1, 2, "Légion de la Vistule");
            Add(2, 1, 3, "6th ‘Herzog Wilhelm’ Bavarian Fusiliers", true);
            Add(2, 1, 4, "18e Demi-Brigade d'Infanterie 'Les Braves'", true);
            Add(2, 6, 1, "85ème Régiment d'Infanterie");
            Add(2, 6, 2, "Polish Vistula Legion", true);
            Add(2, 6, 3, "Napoleon I", true);
            Add(2, 20, 1, "85ème Régiment d'Infanterie");
            Add(2, 12, 1, "85ème Régiment d'Infanterie");
            Add(2, 19, 1, "French Navy");
            Add(2, 19, 2, "French Navy");
            Add(2, 19, 3, "67e Régiment d'Infanterie");
            Add(2, 19, 4, "67e Régiment d'Infanterie");
            Add(2, 19, 5, "French Navy", true);
            Add(2, 19, 6, "67e Régiment d'Infanterie", true);
            Add(2, 16, 1, "2e Régiment de la Garde Impériale");
            Add(2, 16, 2, "1er Grenadiers de la Garde Impériale", true);
            Add(2, 18, 1, "3ème Régiment d'Infanterie");
            Add(2, 18, 2, "Polish 4th Infantry Regiment", true);
            Add(2, 11, 1, "10ème Régiment d'Infanterie");
            Add(2, 11, 2, "Guides à Pied de Bonaparte", true);
            Add(2, 10, 1, "Tirailleurs Corses");
            Add(2, 2, 1, "");
            Add(2, 2, 2, "");
            Add(2, 3, 1, "");
            Add(2, 4, 1, "");
            Add(2, 4, 2, "");
            Add(2, 4, 3, "");
            Add(2, 5, 1, "");
            Add(2, 7, 1, "");
            Add(2, 8, 1, "");
            Add(2, 9, 1, "");
            Add(2, 9, 2, "");
            Add(2, 13, 1, "");
            Add(2, 21, 1, "");
            Add(2, 21, 2, "");
            Add(2, 23, 1, "");
            Add(2, 24, 1, "");
            Add(2, 24, 2, "");
            Add(2, 25, 1, "");
            Add(3, 1, 1, "Colbergsches Infanterie-Regiment");
            Add(3, 1, 2, "Lützow Freikorps");
            Add(3, 1, 3, "11. Reserve Regiment", true);
            Add(3, 6, 1, "Colbergsches Infanterie-Regiment");
            Add(3, 6, 2, "Lützow Freikorps", true);
            Add(3, 20, 1, "Colbergsches Infanterie-Regiment");
            Add(3, 12, 1, "Colbergsches Infanterie-Regiment");
            Add(3, 19, 1, "Colbergsches Infanterie-Regiment");
            Add(3, 19, 2, "Colbergsches Infanterie-Regiment");
            Add(3, 19, 3, "Colbergsches Infanterie-Regiment", true);
            Add(3, 19, 4, "Colbergsches Infanterie-Regiment", true);
            Add(3, 16, 1, "Garde-Regiment Zu Fuss");
            Add(3, 16, 2, "Zweites Garde-Regiment Zu Fuss", true);
            Add(3, 18, 1, "1. Berg Grenadiere");
            Add(3, 18, 2, "1. Schlesisches Infanterie-Regiment", true);
            Add(3, 11, 1, "Schlesische Landwehr");
            Add(3, 10, 1, "Schlesische Schützen");
            Add(3, 8, 1, "");
            Add(3, 9, 1, "");
            Add(3, 9, 2, "");
            Add(3, 21, 1, "");
            Add(3, 21, 2, "");
            Add(3, 23, 1, "");
            Add(3, 24, 1, "");
            Add(3, 25, 1, "");
            Add(4, 1, 1, "Mogilev Infantry Regiment");
            Add(4, 1, 2, "Interior Guard Battalion");
            Add(4, 1, 3, "Pavlovsk Fusiliers", true);
            Add(4, 6, 1, "Mogilev Infantry Regiment");
            Add(4, 6, 2, "Interior Guard Battalion", true);
            Add(4, 20, 1, "Mogilev Infantry Regiment");
            Add(4, 12, 1, "Mogilev Infantry Regiment");
            Add(4, 19, 1, "Mogilev Infantry Regiment");
            Add(4, 19, 2, "Mogilev Infantry Regiment");
            Add(4, 19, 3, "Mogilev Infantry Regiment", true);
            Add(4, 19, 4, "Mogilev Infantry Regiment", true);
            Add(4, 16, 1, "Semyonovsky Regiment");
            Add(4, 16, 2, "Leib-Gvardii Litovskii Polk", true);
            Add(4, 18, 1, "Pavlovsk Grenadiers");
            Add(4, 18, 2, "Nizhnii-Novgorod Opolcheniye", true);
            Add(4, 11, 1, "24th Jaegers");
            Add(4, 10, 1, "Russian-German Legion");
            Add(4, 8, 1, "");
            Add(4, 9, 1, "");
            Add(4, 9, 2, "");
            Add(4, 21, 1, "");
            Add(4, 21, 2, "");
            Add(4, 23, 1, "");
            Add(4, 24, 1, "");
            Add(4, 25, 1, "");
            Add(5, 1, 1, "7° Reggimento Fanteria di Linea");
            Add(5, 1, 2, "Reggimento Real Veterani");
            Add(5, 1, 3, "Legione Reale Dalmata", true);
            Add(5, 6, 1, "7° Reggimento Fanteria di Linea");
            Add(5, 6, 2, "Battaglione Guardia Alla Città di Milano", true);
            Add(5, 20, 1, "7° Reggimento Fanteria di Linea");
            Add(5, 12, 1, "7° Reggimento Fanteria di Linea");
            Add(5, 19, 1, "7° Reggimento Fanteria di Linea");
            Add(5, 19, 2, "7° Reggimento Fanteria di Linea");
            Add(5, 19, 3, "7° Reggimento Fanteria di Linea", true);
            Add(5, 19, 4, "7° Reggimento Fanteria di Linea", true);
            Add(5, 16, 1, "Guardia Reale Italiana");
            Add(5, 16, 2, "Marinai Della Guardia Reale", true);
            Add(5, 18, 1, "6° Reggimento Fanteria di Linea");
            Add(5, 18, 2, "Battaglione Guardia Alla Città di Milano", true);
            Add(5, 11, 1, "1° Reggimento Fanteria Leggera");
            Add(5, 10, 1, "Bersaglieri Bresciani");
            Add(5, 8, 1, "");
            Add(5, 9, 1, "");
            Add(5, 9, 2, "");
            Add(5, 21, 1, "");
            Add(5, 21, 2, "");
            Add(5, 23, 1, "");
            Add(5, 24, 1, "");
            Add(5, 25, 1, "");
            Add(8, 1, 1, "K.K. IR 22 'Coburg'");
            Add(8, 1, 2, "K.K. IR 62 'Banat'");
            Add(8, 1, 3, "Ungarische Insurrection", true);
            Add(8, 6, 1, "K.K. IR 22 'Coburg'");
            Add(8, 6, 2, "K.K. IR 22 'Coburg' Overcoat", true);
            Add(8, 20, 1, "K.K. IR 22 'Coburg'");
            Add(8, 12, 1, "K.K. IR 22 'Coburg'");
            Add(8, 19, 1, "K.K. IR 22 'Coburg'");
            Add(8, 19, 2, "K.K. IR 22 'Coburg'");
            Add(8, 19, 3, "K.K. IR 22 'Coburg'", true);
            Add(8, 19, 4, "K.K. IR 22 'Coburg'", true);
            Add(8, 16, 1, "K.K. Grenadier Battalion 'Janusch'");
            Add(8, 16, 2, "Herzoglich Braunschweigisches Feldkorps", true);
            Add(8, 18, 1, "K.K. Grenadier Battalion 'Locher'");
            Add(8, 18, 2, "Serbische Freikorps", true);
            Add(8, 11, 1, "3rd Battalion 'Königgrätz'");
            Add(8, 10, 1, "Feldjägerbataillon N° 'Arno' Jägers");
            Add(8, 8, 1, "");
            Add(8, 9, 1, "");
            Add(8, 9, 2, "");
            Add(8, 21, 1, "");
            Add(8, 21, 2, "");
            Add(8, 23, 1, "");
            Add(8, 24, 1, "");
            Add(8, 25, 1, "");
            Add(11, 1, 1, "33rd Regiment of Foot");
            Add(11, 1, 2, "42nd Black Watch Highlanders", true);
            Add(11, 1, 3, "New Hampshire Line", true);
            Add(11, 1, 4, "Hessian Musketers", true);
            Add(11, 6, 1, "33rd Regiment of Foot");
            Add(11, 6, 2, "General Cornwallis", true);
            Add(11, 20, 1, "33rd Regiment of Foot");
            Add(11, 12, 1, "33rd Regiment of Foot");
            Add(11, 12, 2, "42nd Black Watch Highlanders", true);
            Add(11, 19, 1, "33rd Regiment of Foot");
            Add(11, 19, 2, "33rd Regiment of Foot");
            Add(11, 19, 3, "33rd Regiment of Foot");
            Add(11, 19, 4, "33rd Regiment of Foot");
            Add(11, 19, 5, "42nd Black Watch Highlanders");
            Add(11, 19, 6, "33rd Regiment of Foot", true);
            Add(11, 19, 7, "33rd Regiment of Foot", true);
            Add(11, 16, 1, "1st Foot Guards");
            Add(11, 18, 1, "Land-Grenadier-Regiment Rall");
            Add(11, 11, 1, "1st Light Infantry Battalion");
            Add(11, 11, 2, "Butler's Rangers", true);
            Add(11, 11, 3, "Mohawk Warrior Native", true);
            Add(11, 10, 1, "Ansbach-Bayreuth Jäger");
            Add(11, 8, 1, "");
            Add(11, 9, 1, "");
            Add(11, 9, 2, "");
            Add(11, 21, 1, "");
            Add(11, 23, 1, "");
            Add(11, 24, 1, "");
            Add(11, 25, 1, "");
            Add(12, 1, 1, "Hall's Delaware Regiment");
            Add(12, 1, 2, "North Carolina Continental Line", true);
            Add(12, 1, 3, "Connecticut Minute Company", true);
            Add(12, 1, 4, "Green Mountain Boys", true);
            Add(12, 6, 1, "Hall's Delaware Regiment");
            Add(12, 6, 2, "General Washington", true);
            Add(12, 20, 1, "Hall's Delaware Regiment");
            Add(12, 12, 1, "Hall's Delaware Regiment");
            Add(12, 12, 2, "1st Rhode Island Regiment", true);
            Add(12, 19, 1, "Hall's Delaware Regiment");
            Add(12, 19, 2, "Hall's Delaware Regiment");
            Add(12, 19, 3, "Hall's Delaware Regiment");
            Add(12, 19, 4, "Hall's Delaware Regiment", true);
            Add(12, 19, 5, "Hall's Delaware Regiment", true);
            Add(12, 16, 1, "Washington's Guard");
            Add(12, 18, 1, "Régiment De Soissonnais");
            Add(12, 18, 2, "George Washington Grenadiers", true);
            Add(12, 11, 1, "4th Massachusetts Light Infantry");
            Add(12, 11, 2, "Quaker Blues", true);
            Add(12, 11, 3, "Frontiersman", true);
            Add(12, 11, 4, "Oneida Native American Warrior", true);
            Add(12, 10, 1, "Kentucky County Riflemen");
            Add(12, 10, 2, "Morgan's Riflemen", true);
            Add(12, 8, 1, "");
            Add(12, 9, 1, "");
            Add(12, 9, 2, "");
            Add(12, 21, 1, "");
            Add(12, 23, 1, "");
            Add(12, 24, 1, "");
            Add(12, 25, 1, "");
            Add(6, 1, 1, "Durham Light Infantry");
            Add(6, 1, 2, "103rd Infantry Regiment", true);
            Add(6, 1, 3, "6th Victoria Battalion", true);
            Add(6, 1, 4, "Durham Light Infantry Winter Uniform", true);
            Add(6, 6, 1, "Durham Light Infantry");
            Add(6, 6, 2, "103rd Infantry Regiment", true);
            Add(6, 6, 3, "6th Victoria Battalion", true);
            Add(6, 8, 1, "");
            Add(6, 9, 1, "");
            Add(6, 21, 1, "");
            Add(6, 23, 1, "");
            Add(7, 1, 1, "2. Westfälisches Nr. 15");
            Add(7, 1, 2, "57'nci Piyade Alayı", true);
            Add(7, 1, 3, "2. Westfälisches Nr. 15 Winter Uniform", true);
            Add(7, 6, 1, "2. Westfälisches Nr. 15");
            Add(7, 6, 2, "57'nci Piyade Alayı", true);
            Add(7, 8, 1, "");
            Add(7, 9, 1, "");
            Add(7, 21, 1, "");
            Add(7, 23, 1, "");
        }

        private static long Pair(int faction, int playerClass)
        {
            return (long)faction * 1000L + playerClass;
        }

        private static long Key(int faction, int playerClass, int id)
        {
            return Pair(faction, playerClass) * 100000L + id;
        }

        // unlock : uniforme lié à un DLC ou à un ordre dans le jeu ; noté pour mémoire, sans effet pour un bot (pas de contrôle).
        private static void Add(int faction, int playerClass, int id, string name, bool unlock = false)
        {
            Table[Key(faction, playerClass, id)] = new Entry { Name = name };
            if (!IdsByPair.TryGetValue(Pair(faction, playerClass), out var ids))
            {
                ids = new List<int>();
                IdsByPair[Pair(faction, playerClass)] = ids;
            }
            ids.Add(id);
        }

        /// <summary>Id connu pour ce couple faction/classe (ou accepté sans contrôle avec CMD:skinlibre).</summary>
        public static bool IsValid(int faction, int playerClass, int id)
        {
            if (id < 1) return false;
            return CommanderConfig.SkinFree || Table.ContainsKey(Key(faction, playerClass, id));
        }

        public static string NameOf(int faction, int playerClass, int id)
        {
            return Table.TryGetValue(Key(faction, playerClass, id), out var e) && e.Name.Length > 0 ? e.Name : "uniforme " + id;
        }

        public static bool HasAny(int faction, int playerClass)
        {
            return IdsByPair.ContainsKey(Pair(faction, playerClass));
        }

        /// <summary>Uniforme par défaut d'une faction et d'une classe (CMD:skin) ; faux si l'id n'est pas valide.</summary>
        public static bool SetDefault(int faction, int playerClass, int id)
        {
            if (!IsValid(faction, playerClass, id)) return false;
            ConfigDefaults[Pair(faction, playerClass)] = id;
            return true;
        }

        public static int DefaultCount => ConfigDefaults.Count;

        public static void ClearDefaults()
        {
            ConfigDefaults.Clear();
        }

        /// <summary>Uniforme d'un bot à sa création : choix de l'officier, sinon CMD:skin, sinon -1 (uniforme 1 du jeu).</summary>
        public static int Resolve(Commander owner, int faction, int playerClass)
        {
            var officer = owner != null ? Players.Get(owner.PlayerId) : null;
            if (officer != null && officer.Skins.TryGetValue(playerClass, out var chosen) && IsValid(faction, playerClass, chosen)) return chosen;
            if (ConfigDefaults.TryGetValue(Pair(faction, playerClass), out var id)) return id;
            // Aucun choix : sans uniforme passé, le jeu en tire un au hasard pour chaque bot et la ligne serait dépareillée.
            // Défaut fixe = premier uniforme de la faction et de la classe qui n'est pas de la marine (l'uniforme 1 est souvent
            // « Navy » chez British et French), sinon le premier ; ainsi tous les bots d'une même classe ont le même uniforme.
            if (IdsByPair.TryGetValue(Pair(faction, playerClass), out var ids) && ids.Count > 0)
            {
                foreach (var candidate in ids)
                {
                    var name = Table[Key(faction, playerClass, candidate)].Name;
                    if (name.IndexOf("Navy", System.StringComparison.OrdinalIgnoreCase) < 0) return candidate;
                }
                return ids[0];
            }
            return -1;
        }

        /// <summary>« 1 23rd Royal Welch Fusiliers, 2 92nd Gordon Highlanders… » pour !skins &lt;classe&gt;.</summary>
        public static string Describe(int faction, int playerClass)
        {
            if (!IdsByPair.TryGetValue(Pair(faction, playerClass), out var ids)) return GameIds.ClassName(playerClass) + " : aucun uniforme connu pour cette faction.";
            var sb = new StringBuilder(GameIds.ClassName(playerClass)).Append(" :");
            for (var i = 0; i < ids.Count; i++)
            {
                sb.Append(i == 0 ? " " : ", ").Append(ids[i].ToString(CultureInfo.InvariantCulture));
                var name = Table[Key(faction, playerClass, ids[i])].Name;
                if (name.Length > 0) sb.Append(' ').Append(name);
            }
            return sb.ToString();
        }

        /// <summary>« ArmyLineInfantry 1-4 » pour la liste courte de !skins.</summary>
        public static string Range(int faction, int playerClass)
        {
            if (!IdsByPair.TryGetValue(Pair(faction, playerClass), out var ids) || ids.Count == 0) return null;
            var max = 0;
            foreach (var id in ids) if (id > max) max = id;
            return GameIds.ClassName(playerClass) + (max > 1 ? " 1-" + max : " 1");
        }
    }
}
