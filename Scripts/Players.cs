using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>Ce que le mod sait d'un joueur ou d'un bot présent sur le serveur.</summary>
    public class PlayerInfo
    {
        public int Id;
        public ulong SteamId;
        public string Name = "";
        public string Regiment = "";
        public bool IsBot;
        /// <summary>Section d'apparition de la dernière apparition (OnPlayerSpawned).</summary>
        public int SpawnSection = -1;
        public bool IsAdmin;
        /// <summary>Entré dans la manche (OnPlayerJoined) : sert au compte des vrais joueurs (plafond selon le remplissage).</summary>
        public bool Joined;
        public bool Alive;
        // Valeurs entières des enums du jeu (GameIds), connues à l'apparition.
        public int Faction;
        public int Class;
        public int Uniform = -1;
        // Objet du personnage reçu par OnPlayerSpawned : position lue directement sur son transform.
        public GameObject Body;
        // Repli : dernière position et orientation reçues par OnPlayerPacket (bots du mod et officiers seulement).
        public bool HasPacket;
        public Vector3 PacketPosition;
        public bool HasAim;
        public float AimAt;
        public Vector3 CameraPosition;
        public Vector3 CameraForward;
        public float PacketYaw;
        // Uniformes choisis par l'officier (lot B, !skin) : classe → id, gardés d'une manche à l'autre.
        public readonly Dictionary<int, int> Skins = new Dictionary<int, int>();
        // Anti-spam des commandes du chat.
        public float LastCommand = -1000f;

        /// <summary>Position actuelle, si connue.</summary>
        public bool TryGetPosition(out Vector3 position)
        {
            // Comparaison Unity : un objet détruit vaut null.
            if (Body != null)
            {
                position = Body.transform.position;
                return true;
            }
            position = PacketPosition;
            return HasPacket;
        }

        /// <summary>Orientation actuelle (lacet en degrés), si connue.</summary>
        public bool TryGetYaw(out float yaw)
        {
            if (HasPacket)
            {
                yaw = PacketYaw;
                return true;
            }
            if (Body != null)
            {
                yaw = Body.transform.eulerAngles.y;
                return true;
            }
            yaw = 0f;
            return false;
        }

        public override string ToString()
        {
            return Name + " #" + Id;
        }
    }

    /// <summary>Joueurs et bots connus, par identifiant de joueur.</summary>
    public static class Players
    {
        private static readonly Dictionary<int, PlayerInfo> Table = new Dictionary<int, PlayerInfo>();

        public static IEnumerable<PlayerInfo> All => Table.Values;

        public static PlayerInfo Get(int playerId)
        {
            return Table.TryGetValue(playerId, out var p) ? p : null;
        }

        /// <summary>Le joueur, créé s'il n'était pas encore connu (événement arrivé avant OnPlayerJoined).</summary>
        public static PlayerInfo GetOrAdd(int playerId)
        {
            if (!Table.TryGetValue(playerId, out var p))
            {
                p = new PlayerInfo { Id = playerId, Name = "#" + playerId };
                Table[playerId] = p;
            }
            return p;
        }

        public static void Remove(int playerId)
        {
            Table.Remove(playerId);
        }

        /// <summary>Changement de manche : les bots sont supprimés par le jeu sans OnPlayerLeft, leurs fiches aussi.</summary>
        public static void RemoveBots()
        {
            var bots = new List<int>();
            foreach (var p in Table.Values) if (p.IsBot) bots.Add(p.Id);
            foreach (var id in bots) Table.Remove(id);
        }

        /// <summary>Nouvelle origine de l'horloge (Clock.Reset).</summary>
        public static void ResetClock()
        {
            foreach (var p in Table.Values) p.LastCommand = -1000f;
        }

        /// <summary>Fiche neuve pour une connexion : un id peut être réutilisé (droits admin, statut de bot à ne pas hériter).</summary>
        public static PlayerInfo Reset(int playerId)
        {
            var p = new PlayerInfo { Id = playerId, Name = "#" + playerId };
            Table[playerId] = p;
            return p;
        }

        public static void Clear()
        {
            Table.Clear();
        }

        /// <summary>Vrais joueurs entrés dans la manche (bots exclus). Les spectateurs ne sont pas distingués.</summary>
        public static int RealPlayerCount()
        {
            var n = 0;
            foreach (var p in Table.Values) if (p.Joined && !p.IsBot) n++;
            return n;
        }

        public static string NameOf(int playerId)
        {
            var p = Get(playerId);
            return p != null ? p.Name : "#" + playerId;
        }
    }
}
