using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>Trace bornée en distance et en mémoire ; chaque rang suit le même chemin.</summary>
    public sealed class FollowTrail
    {
        private readonly List<Vector3> _points = new List<Vector3>();
        private float _length;
        public void Clear() { _points.Clear(); _length = 0f; }

        public void Record(Vector3 position, float yaw, float keepDistance)
        {
            if (_points.Count == 0)
            {
                _points.Add(position - Formation.Forward(yaw) * keepDistance);
                _points.Add(position);
                _length = keepDistance;
                return;
            }
            var distance = Line.Flat(position - _points[_points.Count - 1]).magnitude;
            if (distance > 30f) { Clear(); Record(position, yaw, keepDistance); return; }
            if (distance < 0.15f) return;
            _points.Add(position);
            _length += distance;
            while (_points.Count > 2)
            {
                var first = Line.Flat(_points[1] - _points[0]).magnitude;
                if (_length - first < keepDistance && _points.Count <= 4096) break;
                _length -= first;
                _points.RemoveAt(0);
            }
        }

        /// <summary>
        /// Point de la trace le plus proche de pos, avancé de ahead m vers l'officier (le bout le plus récent) : un bot en retard
        /// suit le chemin à partir de là, sans dépasser son rang et en conservant sa file.
        /// </summary>
        public bool LeadFrom(Vector3 pos, float ahead, float slotBehind, float lateral, out Vector3 point)
        {
            point = pos;
            if (!BehindOf(pos, out var behind, out _)) return false;
            LeadAt(behind, ahead, slotBehind, lateral, out point);
            return true;
        }

        /// <summary>Comme LeadFrom, à partir d'une distance déjà mesurée par BehindOf (un seul parcours de la trace par bot).</summary>
        public void LeadAt(float behind, float ahead, float slotBehind, float lateral, out Vector3 point)
        {
            // Progression bornée par le rang attribué : ne jamais dépasser sa place
            // vers l'officier. Chaque file conserve aussi son décalage latéral.
            Sample(Mathf.Max(slotBehind, behind - ahead), _points[_points.Count - 1], 0f,
                out var center, out var direction);
            point = center + Formation.Right(direction) * lateral;
        }

        /// <summary>
        /// Distance le long de la trace (m) entre l'officier (le bout le plus récent) et le point de la trace le plus proche
        /// de pos ; off : écart de pos à la trace (m). Faux tant que la trace est vide.
        /// </summary>
        public bool BehindOf(Vector3 pos, out float behind, out float off)
        {
            behind = 0f;
            off = 0f;
            if (_points.Count < 2) return false;
            var bestSq = float.MaxValue;
            var bestI = 0;
            var bestT = 0f;
            for (var i = 0; i + 1 < _points.Count; i++)
            {
                var a = _points[i];
                var seg = Line.Flat(_points[i + 1] - a);
                var len2 = seg.sqrMagnitude;
                var t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(Line.Flat(pos - a), seg) / len2) : 0f;
                var p = a + (_points[i + 1] - a) * t;
                var d = Line.Flat(p - pos).sqrMagnitude;
                if (d >= bestSq) continue;
                bestSq = d;
                bestI = i;
                bestT = t;
            }
            for (var i = bestI + 1; i + 1 < _points.Count; i++)
                behind += Line.Flat(_points[i + 1] - _points[i]).magnitude;
            behind += Line.Flat(_points[bestI + 1] - _points[bestI]).magnitude * (1f - bestT);
            off = Mathf.Sqrt(bestSq);
            return true;
        }

        public void Sample(float behind, Vector3 fallback, float fallbackYaw, out Vector3 point, out float yaw)
        {
            yaw = fallbackYaw;
            point = fallback - Formation.Forward(yaw) * behind;
            for (var i = _points.Count - 1; i > 0; i--)
            {
                var delta = _points[i] - _points[i - 1];
                var length = Line.Flat(delta).magnitude;
                if (length < 0.001f) continue;
                yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                if (behind <= length)
                {
                    point = Vector3.Lerp(_points[i], _points[i - 1], behind / length);
                    return;
                }
                behind -= length;
                point = _points[i - 1] - Formation.Forward(yaw) * behind;
            }
        }
    }
}
