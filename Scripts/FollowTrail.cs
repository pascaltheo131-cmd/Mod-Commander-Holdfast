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
        /// suit le chemin de l'officier à partir de là, au lieu de couper droit vers sa place.
        /// </summary>
        public bool LeadFrom(Vector3 pos, float ahead, out Vector3 point)
        {
            point = pos;
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
            point = _points[bestI] + (_points[bestI + 1] - _points[bestI]) * bestT;
            var left = ahead;
            for (var i = bestI; i + 1 < _points.Count && left > 0f; i++)
            {
                var from = i == bestI ? point : _points[i];
                var seg = _points[i + 1] - from;
                var len = Line.Flat(seg).magnitude;
                if (len >= left)
                {
                    point = from + seg * (left / Mathf.Max(len, 1e-4f));
                    return true;
                }
                left -= len;
                point = _points[i + 1];
            }
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
