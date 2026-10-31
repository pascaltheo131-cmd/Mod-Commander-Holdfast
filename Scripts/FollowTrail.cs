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
