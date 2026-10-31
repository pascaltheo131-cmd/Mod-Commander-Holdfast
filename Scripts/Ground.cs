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
            var bestY = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, CastLength, ~0, QueryTriggerInteraction.Ignore))
            {
                if (IsVegetation(hit.collider)) continue;
                var y = hit.point.y;
                if (Mathf.Abs(y - referenceY) > MaxHeightGap) continue;
                if (y >= bestY) continue;
                bestY = y;
                found = true;
            }
            if (!found) return false;
            point = new Vector3(at.x, bestY, at.z);
            return true;
        }

        /// <summary>Arbres, feuillages, buissons : jamais pris pour le sol (nom de l'objet ou de son parent) [TEST : noms réels].</summary>
        private static bool IsVegetation(Collider collider)
        {
            if (collider == null) return false;
            var t = collider.transform;
            for (var depth = 0; t != null && depth < 3; depth++, t = t.parent)
            {
                var name = t.name;
                if (string.IsNullOrEmpty(name)) continue;
                var lower = name.ToLowerInvariant();
                if (lower.Contains("tree") || lower.Contains("pine") || lower.Contains("fir") || lower.Contains("leaf")
                    || lower.Contains("leaves") || lower.Contains("foliage") || lower.Contains("bush") || lower.Contains("branch")
                    || lower.Contains("canopy")) return true;
            }
            return false;
        }
    }
}
