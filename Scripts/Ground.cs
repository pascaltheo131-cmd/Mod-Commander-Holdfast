using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Recherche du sol pour les seuls placements permis (bot juste apparu, sergent de succession, bot tombé sous la carte).
    /// Aucun recalage ni téléportation pendant les déplacements : les bots marchent.
    /// Un seul rayon vers le bas touche souvent la cime des arbres : on prend tous les impacts (RaycastAll), on écarte la
    /// végétation reconnaissable à son nom, et on garde le plus bas des impacts plausibles, c'est-à-dire à moins de
    /// MaxHeightGap m de la hauteur de référence (un bot voisin arrivé, ou l'officier). Rien de plausible : pas de placement.
    /// </summary>
    public static class Ground
    {
        private const float MaxHeightGap = 3f;
        private const float CastHeight = 30f;
        private const float CastLength = 80f;

        public static bool TryFind(Vector3 at, float referenceY, out Vector3 point)
        {
            point = at;
            var origin = new Vector3(at.x, referenceY + CastHeight, at.z);
            var found = false;
            var bestY = float.MinValue;
            // Impact plausible le plus HAUT (à moins de MaxHeightGap m de la référence, végétation exclue) : le plus bas pouvait
            // être un collider sous le terrain (grottes, fonds, volumes de carte), d'où des bots posés sous la carte.
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, CastLength, ~0, QueryTriggerInteraction.Ignore))
            {
                if (IsVegetation(hit.collider)) continue;
                var y = hit.point.y;
                if (Mathf.Abs(y - referenceY) > MaxHeightGap) continue;
                if (y <= bestY) continue;
                bestY = y;
                found = true;
            }
            if (!found) return false;
            point = new Vector3(at.x, bestY, at.z);
            return true;
        }

        /// <summary>
        /// Le bot est sous le terrain : un rayon descendant d'au-dessus de lui touche le terrain (objet nommé « Terrain »)
        /// plus haut que ses pieds. Une simple différence de hauteur avec son chef ne suffit pas (vu en jeu : 119 bots
        /// « tombés » sur des pentes et téléportés pour rien).
        /// </summary>
        public static bool IsUnderTerrain(Vector3 feet)
        {
            var origin = feet + Vector3.up * CastHeight;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, CastHeight, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.point.y < feet.y + 1f) continue;
                var t = hit.collider != null ? hit.collider.transform : null;
                for (var depth = 0; t != null && depth < 3; depth++, t = t.parent)
                    if (t.name.IndexOf("terrain", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>Arbres, feuillages, buissons : jamais pris pour le sol (nom de l'objet ou de son parent) [TEST : noms réels].</summary>
        private static readonly char[] Separators = { '_', ' ', '-', '.', '(', ')', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        private static readonly string[] VegetationWords = { "tree", "pine", "fir", "leaf", "leaves", "foliage", "bush", "branch", "canopy" };

        private static bool IsVegetation(Collider collider)
        {
            if (collider == null) return false;
            var t = collider.transform;
            for (var depth = 0; t != null && depth < 3; depth++, t = t.parent)
            {
                var name = t.name;
                if (string.IsNullOrEmpty(name)) continue;
                // Par mots (début de mot) : « street » ou « first » ne sont pas des arbres.
                foreach (var word in name.ToLowerInvariant().Split(Separators, System.StringSplitOptions.RemoveEmptyEntries))
                    foreach (var v in VegetationWords)
                        if (word.StartsWith(v, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
