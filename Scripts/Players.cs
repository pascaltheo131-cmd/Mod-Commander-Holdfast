using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>Ce que le mod sait d'un joueur ou d'un bot présent sur le serveur.</summary>
    public class PlayerInfo
    {
        public int Id;
        public string Name = "";
        public string Regiment = "";
        public bool IsBot;
        public bool IsAdmin;
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
        public float PacketYaw;
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

        public static void Clear()
        {
            Table.Clear();
        }

        public static string NameOf(int playerId)
        {
            var p = Get(playerId);
            return p != null ? p.Name : "#" + playerId;
        }
    }
}
