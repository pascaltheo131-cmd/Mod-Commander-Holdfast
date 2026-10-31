using System.Globalization;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Plafond selon la charge (lot B, n° 16). Mesure indirecte : durée moyenne d'une image (Time.unscaledDeltaTime lissé)
    /// et temps passé dans la boucle du mod. Si la durée moyenne dépasse le seuil, le plafond de charge baisse de LoadStep
    /// (au moins jusqu'à LoadMin) à chaque décision ; quand tout va bien, il remonte de 1 par décision, puis disparaît.
    /// Le plafond effectif du mod est le plus petit de : maxbots ou plafond admin, remplissage, charge (LineManager.Cap).
    /// </summary>
    public static class LoadMonitor
    {
        // Lissage exponentiel : environ les 100 dernières images.
        private const float Smoothing = 0.01f;
        // Une image plus longue est un chargement (changement de carte) : ignorée, et la moyenne repart.
        private const float LoadingFrame = 1f;
        // Pas de décision pendant les premières secondes (démarrage, chargement de carte).
        private const float Warmup = 30f;
        // Remontée seulement nettement sous le seuil, pour ne pas osciller.
        private const float RecoverRatio = 0.8f;

        /// <summary>Pas de plafond de charge.</summary>
        public const int Unlimited = int.MaxValue;

        public static float FrameMs { get; private set; }
        public static float ModMs { get; private set; }
        public static int Cap { get; private set; } = Unlimited;

        private static bool _hasSample;
        private static float _warmupUntil = -1f;
        private static float _nextDecision;
        private static float _autoThreshold;

        /// <summary>Seuil en ms : CMD:chargeseuil, sinon 1,5 × l'intervalle normal estimé.</summary>
        public static float Threshold => CommanderConfig.LoadThreshold > 0f ? CommanderConfig.LoadThreshold : AutoThreshold();

        /// <summary>
        /// Intervalle normal : 1000 / Application.targetFrameRate si le serveur en fixe un, sinon Time.fixedDeltaTime
        /// (la simulation des bots tourne à ce rythme) [TEST : valeurs réelles sur le serveur].
        /// </summary>
        private static float AutoThreshold()
        {
            if (_autoThreshold > 0f) return _autoThreshold;
            var target = Application.targetFrameRate;
            var normalMs = target > 0 ? 1000f / target : Time.fixedDeltaTime * 1000f;
            if (normalMs <= 0f) normalMs = 20f;
            _autoThreshold = 1.5f * normalMs;
            CmdLog.Info("Charge : intervalle normal estimé " + normalMs.ToString("0.#", CultureInfo.InvariantCulture) + " ms (targetFrameRate "
                        + target + ", fixedDeltaTime " + Time.fixedDeltaTime.ToString("0.###", CultureInfo.InvariantCulture) + " s), seuil "
                        + _autoThreshold.ToString("0.#", CultureInfo.InvariantCulture) + " ms.");
            return _autoThreshold;
        }

        /// <summary>Une image : durée de l'image (s) et temps passé dans la boucle du mod (ms).</summary>
        public static void Sample(float frameSeconds, float modMs, float now)
        {
            if (_warmupUntil < 0f) _warmupUntil = now + Warmup;
            if (frameSeconds <= 0f) return;
            if (frameSeconds > LoadingFrame)
            {
                _hasSample = false;
                _warmupUntil = now + Warmup;
                return;
            }
            var ms = frameSeconds * 1000f;
            if (!_hasSample)
            {
                FrameMs = ms;
                ModMs = modMs;
                _hasSample = true;
                return;
            }
            FrameMs += (ms - FrameMs) * Smoothing;
            ModMs += (modMs - ModMs) * Smoothing;
        }

        /// <summary>Décision toutes les LoadInterval secondes. activeBots : bots vivants ou attendus (hors dispersion).</summary>
        public static void Decide(float now, int activeBots)
        {
            if (!CommanderConfig.LoadCapEnabled)
            {
                Cap = Unlimited;
                return;
            }
            if (now < _nextDecision || now < _warmupUntil || !_hasSample) return;
            _nextDecision = now + CommanderConfig.LoadInterval;

            var threshold = Threshold;
            var before = Cap;
            if (FrameMs > threshold)
            {
                // Baisse à partir du nombre réel de bots, sinon un plafond encore haut ne changerait rien.
                var from = Mathf.Min(Cap, activeBots);
                Cap = Mathf.Max(CommanderConfig.LoadMin, from - CommanderConfig.LoadStep);
            }
            else if (FrameMs < threshold * RecoverRatio && Cap != Unlimited)
            {
                Cap++;
                if (Cap >= CommanderConfig.EffectiveMaxBots) Cap = Unlimited;
            }
            if (Cap == before) return;
            // Journal : chaque baisse, et la remontée seulement par paliers de 5 ou à la levée du plafond.
            if (Cap < before || Cap == Unlimited || Cap % 5 == 0)
                CmdLog.Info("Charge : image " + Describe() + " → plafond de charge " + (Cap == Unlimited ? "levé" : Cap.ToString(CultureInfo.InvariantCulture)) + ".");
        }

        public static string Describe()
        {
            return FrameMs.ToString("0.#", CultureInfo.InvariantCulture) + " ms (seuil " + Threshold.ToString("0.#", CultureInfo.InvariantCulture)
                   + "), mod " + ModMs.ToString("0.##", CultureInfo.InvariantCulture) + " ms";
        }

        public static string Status()
        {
            if (!CommanderConfig.LoadCapEnabled) return "charge non surveillée";
            return "image " + Describe() + ", plafond de charge " + (Cap == Unlimited ? "aucun" : Cap.ToString(CultureInfo.InvariantCulture));
        }
    }
}
