using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Calcul des places. Convention Unity : lacet 0 = +z, avant = (sin, 0, cos), droite = (cos, 0, -sin).
    /// L'ancre est le centre du premier rang ; les rangs suivants sont derrière, les auxiliaires derrière le dernier rang.
    /// </summary>
    public static class Formation
    {
        /// <summary>Largeur de la colonne (hommes par rang) et distance entre l'officier et la tête de colonne.</summary>
        public const int ColumnWidth = 3;
        public const float ColumnGap = 3f;

        public static Vector3 Forward(float yaw)
        {
            var r = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        public static Vector3 Right(float yaw)
        {
            var r = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r));
        }

        /// <summary>
        /// Places en ligne : count hommes sur ranks rangs, centrés sur l'ancre. Chaque place est donnée par son décalage
        /// latéral (x) et sa profondeur (y, en rangs), dans le repère de la ligne.
        /// </summary>
        public static List<Vector2> LineSlots(int count, int ranks)
        {
            var slots = new List<Vector2>(count);
            if (count <= 0) return slots;
            ranks = Mathf.Clamp(ranks, 1, count);
            var perRank = (count + ranks - 1) / ranks;
            for (var i = 0; i < count; i++)
            {
                var rank = i / perRank;
                var file = i % perRank;
                // Le dernier rang, s'il est incomplet, reste centré.
                var inThisRank = rank == ranks - 1 ? count - rank * perRank : perRank;
                slots.Add(new Vector2(file - (inThisRank - 1) * 0.5f, rank));
            }
            return slots;
        }

        /// <summary>Places en colonne derrière l'ancre (l'officier) : ColumnWidth hommes de front.</summary>
        public static List<Vector2> ColumnSlots(int count)
        {
            var slots = new List<Vector2>(count);
            for (var i = 0; i < count; i++)
            {
                var row = i / ColumnWidth;
                var file = i % ColumnWidth;
                var inRow = Mathf.Min(ColumnWidth, count - row * ColumnWidth);
                slots.Add(new Vector2(file - (inRow - 1) * 0.5f, row));
            }
            return slots;
        }

        /// <summary>Position dans le monde d'une place (x en files, y en rangs).</summary>
        public static Vector3 ToWorld(Vector3 anchor, float yaw, Vector2 slot, float spacing, float rankDistance, float extraBack)
        {
            return anchor + Right(yaw) * (slot.x * spacing) - Forward(yaw) * (slot.y * rankDistance + extraBack);
        }

        /// <summary>
        /// Affecte les bots aux places en limitant les croisements : bots et places sont triés de gauche à droite
        /// (puis de l'avant vers l'arrière) dans le repère de la ligne, et appariés dans cet ordre.
        /// Renvoie pour chaque bot l'indice de sa place. Un bot dont la position est inconnue garde l'ordre de la liste.
        /// </summary>
        public static int[] Assign(IList<Bot> bots, IList<Vector2> slots, Vector3 anchor, float yaw)
        {
            var n = Mathf.Min(bots.Count, slots.Count);
            var right = Right(yaw);
            var forward = Forward(yaw);

            var botOrder = new List<int>(n);
            var botKeys = new Vector2[n];
            for (var i = 0; i < n; i++)
            {
                botOrder.Add(i);
                botKeys[i] = bots[i].TryGetPosition(out var p)
                    ? new Vector2(Vector3.Dot(p - anchor, right), -Vector3.Dot(p - anchor, forward))
                    : new Vector2(i, 0f);
            }
            var slotOrder = new List<int>(n);
            for (var i = 0; i < n; i++) slotOrder.Add(i);

            // Tris stricts (comparateurs cohérents) : bots de gauche à droite, places par file puis par rang.
            botOrder.Sort((a, b) => botKeys[a].x.CompareTo(botKeys[b].x));
            slotOrder.Sort((a, b) => slots[a].x != slots[b].x ? slots[a].x.CompareTo(slots[b].x) : slots[a].y.CompareTo(slots[b].y));
            // Dans une même file, le bot le plus en avant prend la place du premier rang.
            for (var i = 0; i + 1 < n; i++)
            {
                if (slots[slotOrder[i]].x != slots[slotOrder[i + 1]].x) continue;
                if (botKeys[botOrder[i]].y <= botKeys[botOrder[i + 1]].y) continue;
                var t = botOrder[i];
                botOrder[i] = botOrder[i + 1];
                botOrder[i + 1] = t;
            }

            var result = new int[bots.Count];
            for (var i = 0; i < result.Length; i++) result[i] = -1;
            for (var i = 0; i < n; i++) result[botOrder[i]] = slotOrder[i];
            return result;
        }
    }
}
