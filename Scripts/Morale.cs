using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Moral d'une ligne (lot A, n° 4), de 0 à 100. Il baisse avec les pertes (en proportion de la taille de la ligne), à la mort
    /// de l'officier et sous le feu ; il remonte doucement quand la ligne n'est plus touchée, et d'un coup au ralliement
    /// (StandGround crié par l'officier, ordre ou buff de ralliement d'un sergent joueur), avec un délai de recharge.
    /// Effets (appliqués par LineManager et FireTask) : sous MoralLow, tir moins précis ; sous MoralWaver, la ligne recule ;
    /// à 0, elle se disperse.
    /// </summary>
    public static class Morale
    {
        // Sortie de l'état « flanche » : quelques points au-dessus du seuil, pour ne pas osciller.
        private const float WaverHysteresis = 10f;

        public static bool Enabled => CommanderConfig.MoraleEnabled;

        /// <summary>Perte d'un homme : MoraleLoss points pour la ligne entière, répartis sur sa taille avant la perte.</summary>
        public static void OnLoss(Line line, int sizeBefore, float now)
        {
            if (!Enabled) return;
            Drop(line, CommanderConfig.MoraleLoss / Mathf.Max(1, sizeBefore), now);
        }

        public static void OnOfficerLost(Line line, float now)
        {
            if (!Enabled) return;
            Drop(line, CommanderConfig.MoraleOfficer, now);
        }

        /// <summary>Un bot de la ligne est touché sans mourir.</summary>
        public static void OnHit(Line line, float now)
        {
            if (!Enabled) return;
            Drop(line, CommanderConfig.MoraleFire, now);
        }

        /// <summary>Ralliement ; renvoie faux pendant le délai de recharge.</summary>
        public static bool Rally(Line line, float now)
        {
            if (!Enabled || now - line.LastRallyAt < CommanderConfig.MoraleRallyCooldown) return false;
            line.LastRallyAt = now;
            line.Morale = Mathf.Min(100f, line.Morale + CommanderConfig.MoraleRally);
            if (line.Morale >= CommanderConfig.MoraleWaver + WaverHysteresis) line.Wavering = false;
            return true;
        }

        /// <summary>Remontée lente quand la ligne n'a pas été touchée depuis MoraleCalm secondes.</summary>
        public static void Tick(Line line, float now, float dt)
        {
            if (!Enabled || line.Morale >= 100f) return;
            if (now - line.LastHitAt < CommanderConfig.MoraleCalm) return;
            line.Morale = Mathf.Min(100f, line.Morale + CommanderConfig.MoraleRegen * dt);
            if (line.Wavering && line.Morale >= CommanderConfig.MoraleWaver + WaverHysteresis) line.Wavering = false;
        }

        /// <summary>Tir moins précis sous le seuil bas.</summary>
        public static bool IsShaken(Line line)
        {
            return Enabled && line.Morale < CommanderConfig.MoraleLow;
        }

        /// <summary>Gravité de 0 (au seuil bas) à 1 (moral nul), pour doser le délai et le bruit du tir.</summary>
        public static float Shake(Line line)
        {
            if (!IsShaken(line) || CommanderConfig.MoraleLow <= 0f) return 0f;
            return Mathf.Clamp01(1f - line.Morale / CommanderConfig.MoraleLow);
        }

        public static string Describe(Line line)
        {
            if (!Enabled) return "Moral désactivé sur ce serveur.";
            var value = Mathf.RoundToInt(line.Morale);
            string state;
            if (value <= 0) state = "en déroute";
            else if (line.Morale < CommanderConfig.MoraleWaver) state = "flanche";
            else if (line.Morale < CommanderConfig.MoraleLow) state = "ébranlé, tir moins précis";
            else state = "bon";
            var rally = Mathf.CeilToInt(CommanderConfig.MoraleRallyCooldown - (Clock.Now - line.LastRallyAt));
            return "Moral : " + value + "/100 (" + state + "). " + (rally > 0 ? "Ralliement possible dans " + rally + " s." : "Ralliement possible (StandGround).");
        }

        private static void Drop(Line line, float amount, float now)
        {
            line.LastHitAt = now;
            line.Morale = Mathf.Max(0f, line.Morale - amount);
        }
    }
}
