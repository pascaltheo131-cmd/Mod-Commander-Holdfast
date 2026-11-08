using UnityEngine;

namespace HoldfastCommander
{
    public static class Shooting
    {
        /// <summary>Portée du feu à volonté (m) : au-delà de la portée de visée habituelle, comme un tir de harcèlement.</summary>
        public const float FireAtWillRange = 250f;
        /// <summary>Demi-arc (°) du feu à volonté autour du front de la ligne.</summary>
        public const float FireAtWillArc = 80f;
        /// <summary>Dernière cible du feu à volonté, pour le journal de débogage.</summary>
        public static string LastTarget = "";
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

        /// <summary>Vitesse de la balle (m/s) pour la hausse du feu à volonté (CMD:balle_vitesse).</summary>
        public static float BulletSpeed = 250f;

        /// <summary>Angle (°) à ajouter pour qu'une balle à BulletSpeed atteigne une cible à cette distance horizontale.</summary>
        public static float Hausse(float distance)
        {
            var k = Physics.gravity.magnitude * distance / (BulletSpeed * BulletSpeed);
            return k >= 1f ? 45f : 0.5f * Mathf.Asin(k) * Mathf.Rad2Deg;
        }

        public static void Solution(Bot bot, out float yaw, out float pitch)
        {
            yaw = bot.SlotYaw;
            var elevation = 0f;
            // Feu à volonté : même visée que le carré (ennemi le plus proche dans l'arc, cap corrigé vers lui), qui marche en jeu.
            var fireAtWill = bot.Line.FireAtWill && !bot.Line.Owner.IsAi && bot.Line.Mode != LineMode.Square;
            var arc = bot.Line.Mode == LineMode.Square || fireAtWill ? 50f : CommanderConfig.AimArc;
            if (!fireAtWill && !bot.Line.Owner.IsAi && bot.Line.HasAimPoint && bot.TryGetPosition(out var playerOrigin))
            {
                var delta = bot.Line.AimPoint - (playerOrigin + Vector3.up * (bot.Crouched ? 1f : 1.4f));
                var flat = Line.Flat(delta).magnitude;
                if (flat > 0.1f)
                {
                    yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                    elevation = Mathf.Atan2(delta.y, flat) * Mathf.Rad2Deg;
                }
            }
            else if ((bot.Line.Owner.IsAi || bot.Line.Mode == LineMode.Square || fireAtWill) && bot.TryGetPosition(out var origin))
            {
                var range = fireAtWill ? FireAtWillRange : CommanderConfig.AimRange;
                var id = LineManager.NearestEnemyInArc(bot.Line.Faction, origin, yaw, arc, range, range);
                var target = id >= 0 ? Players.Get(id) : null;
                LastTarget = "aucune cible";
                if (target != null && target.TryGetPosition(out var position))
                {
                    // Visée au torse depuis l'épaule.
                    var delta = position + Vector3.up * 1.2f - (origin + Vector3.up * (bot.Crouched ? 1f : 1.4f));
                    var flat = Line.Flat(delta).magnitude;
                    LastTarget = "cible " + target.Name + " à " + ServerConsole.Num(flat) + " m";
                    if (flat > 0.1f)
                    {
                        yaw += Mathf.Clamp(Mathf.DeltaAngle(yaw, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg), -arc, arc);
                        if (CommanderConfig.AimHeight || fireAtWill) elevation = Mathf.Atan2(delta.y, flat) * Mathf.Rad2Deg;
                        // Hausse du mousquet : la balle tombe, on vise plus haut avec la distance (tir parabolique).
                        if (fireAtWill) elevation += Hausse(flat);
                    }
                }
            }
            // Feu à volonté : seule la dispersion propre au mousquet (celle du jeu, comme pour un joueur) ; pas d'écart ajouté.
            if (fireAtWill)
            {
                elevation += Random.Range(-9f, 9f) * Morale.Shake(bot.Line);
                pitch = Mathf.Clamp(elevation >= 0f ? elevation / 30f : elevation / 40f, -1.5f, 2f);
                yaw = Mathf.Repeat(yaw, 360f);
                return;
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
