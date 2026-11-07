using UnityEngine;

namespace HoldfastCommander
{
    public static class Shooting
    {
        /// <summary>Portée du feu à volonté (m) : au-delà de la portée de visée habituelle, comme un tir de harcèlement.</summary>
        public const float FireAtWillRange = 250f;
        /// <summary>
        /// Cible devant la place du bot, sans sortir de l'arc de la ligne. Pas de compensation balistique supposée :
        /// la vitesse et la gravité dépendent de l'arme. On corrige ici la différence de hauteur.
        /// </summary>
        public static void PlayerDirection(Bot bot, out float yaw, out float pitch)
        {
            yaw = bot.SlotYaw;
            pitch = 0f;
            if (!bot.TryGetPosition(out var origin)) return;
            var delta = bot.Line.AimPoint - (origin + Vector3.up * (bot.Crouched ? 1f : 1.4f));
            var flat = Line.Flat(delta).magnitude;
            if (flat < 0.1f) return;
            yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            var angle = Mathf.Atan2(delta.y, flat) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(angle >= 0f ? angle / 30f : angle / 40f, -1.5f, 2f);
        }

        public static void Solution(Bot bot, out float yaw, out float pitch)
        {
            yaw = bot.SlotYaw;
            var elevation = 0f;
            var arc = bot.Line.Mode == LineMode.Square ? 50f : CommanderConfig.AimArc;
            // Feu à volonté : chaque bot vise l'ennemi le plus en face de lui, sans le réticule de l'officier.
            var fireAtWill = bot.Line.FireAtWill && !bot.Line.Owner.IsAi && bot.Line.Mode != LineMode.Square;
            if (fireAtWill && bot.TryGetPosition(out var willOrigin))
            {
                var id = LineManager.MostInFront(bot.Line.Faction, willOrigin, yaw, Mathf.Max(CommanderConfig.AimArc, 35f), FireAtWillRange);
                var target = id >= 0 ? Players.Get(id) : null;
                if (target != null && target.TryGetPosition(out var position))
                {
                    var delta = position - willOrigin;
                    var flat = Line.Flat(delta).magnitude;
                    if (flat > 0.1f)
                    {
                        yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                        elevation = Mathf.Atan2(delta.y, flat) * Mathf.Rad2Deg;
                    }
                }
            }
            else if (!bot.Line.Owner.IsAi && bot.Line.HasAimPoint && bot.TryGetPosition(out var playerOrigin))
            {
                var delta = bot.Line.AimPoint - (playerOrigin + Vector3.up * (bot.Crouched ? 1f : 1.4f));
                var flat = Line.Flat(delta).magnitude;
                if (flat > 0.1f)
                {
                    yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                    elevation = Mathf.Atan2(delta.y, flat) * Mathf.Rad2Deg;
                }
            }
            else if ((bot.Line.Owner.IsAi || bot.Line.Mode == LineMode.Square) && bot.TryGetPosition(out var origin))
            {
                var id = LineManager.NearestEnemyInArc(bot.Line.Faction, origin, yaw, arc,
                    CommanderConfig.AimRange, CommanderConfig.AimRange);
                var target = id >= 0 ? Players.Get(id) : null;
                if (target != null && target.TryGetPosition(out var position))
                {
                    var delta = position - origin;
                    var flat = Line.Flat(delta).magnitude;
                    if (flat > 0.1f)
                    {
                        yaw += Mathf.Clamp(Mathf.DeltaAngle(yaw, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg), -arc, arc);
                        if (CommanderConfig.AimHeight) elevation = Mathf.Atan2(delta.y, flat) * Mathf.Rad2Deg;
                    }
                }
            }
            var spread = CommanderConfig.AccuracySpread;
            yaw += Random.Range(-spread, spread);
            elevation += Random.Range(-spread, spread) + Random.Range(-9f, 9f) * Morale.Shake(bot.Line);
            // Pitch positif : 2 = +60°, négatif : -1,5 = -60° (API du jeu).
            pitch = Mathf.Clamp(elevation >= 0f ? elevation / 30f : elevation / 40f, -1.5f, 2f);
            yaw = Mathf.Repeat(yaw, 360f);
        }
    }
}
