using System.Globalization;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Diagnostic des gels : ce que le mod a fait pendant une image (créations, kicks, commandes console, recalculs de places,
    /// recherches de sol, temps passé dans le mod, rappels du jeu compris). Au début de chaque image, si la précédente a duré
    /// plus de SpikeMs, une ligne [CMD] (au plus une toutes les LogInterval s) dit si le pic vient du mod ou du jeu.
    /// </summary>
    public static class FrameStats
    {
        private const float SpikeMs = 100f;
        private const float LogInterval = 10f;

        public static int Spawns, Kicks, Reassigns, GroundCasts;
        /// <summary>Paquets reçus (OnPlayerPacket, joueurs et bots) depuis le dernier résumé.</summary>
        public static int Packets;

        // Résumé toutes les SummaryInterval s.
        private const float SummaryInterval = 60f;
        private static float _summaryStart = -1f;
        private static int _frames, _slow50, _slow100;
        private static double _modSum, _modMax, _frameMax;
        private static long _sentAtSummary;
        private static int _gcAtSummary = -1;
        public static double ModMs;
        private static long _sentAtStart;
        private static float _nextLog;
        private static int _skipped;

        /// <summary>Budget de recherches de sol pour l'image (ProjectSlot) : le reste attend les images suivantes.</summary>
        public const int GroundBudget = 16;

        /// <summary>Commandes console par image au-delà desquelles le pilotage des bots attend l'image suivante.</summary>
        public const int CommandBudget = 60;

        public static long CommandsThisFrame => ServerConsole.Sent - _sentAtStart;

        public static void Reset()
        {
            _nextLog = 0f;
            _summaryStart = -1f;
        }

        private static void Restart(float now)
        {
            _summaryStart = now;
            _frames = _slow50 = _slow100 = 0;
            _modSum = _modMax = _frameMax = 0d;
            Packets = 0;
            _sentAtSummary = ServerConsole.Sent;
            _gcAtSummary = System.GC.CollectionCount(0);
        }

        /// <summary>Résumé : commandes console/s, paquets/s, temps du mod par image (moyen, max), images lentes, mémoire et GC.</summary>
        private static void Summary(float now)
        {
            var seconds = Mathf.Max(1f, now - _summaryStart);
            var inv = CultureInfo.InvariantCulture;
            var gc = System.GC.CollectionCount(0);
            CmdLog.Info("Charge " + Mathf.RoundToInt(seconds) + " s : " + LineManager.TotalBots + " bots, "
                        + ((ServerConsole.Sent - _sentAtSummary) / seconds).ToString("0.#", inv) + " commandes console/s, "
                        + (Packets / seconds).ToString("0", inv) + " paquets/s ; mod " + (_frames > 0 ? _modSum / _frames : 0d).ToString("0.00", inv)
                        + " ms/image en moyenne, max " + _modMax.ToString("0.#", inv) + " ms ; " + _frames + " images, "
                        + _slow50 + " > 50 ms, " + _slow100 + " > 100 ms (max " + Mathf.RoundToInt((float)_frameMax) + " ms) ; mémoire gérée "
                        + (System.GC.GetTotalMemory(false) / 1048576d).ToString("0", inv) + " Mo, "
                        + (_gcAtSummary >= 0 ? (gc - _gcAtSummary).ToString(inv) : "?") + " passage(s) du GC.");
            Restart(now);
        }

        /// <summary>Début d'image : bilan de l'image précédente (durée = unscaledDeltaTime de celle-ci), puis remise à zéro.</summary>
        public static void BeginFrame(float frameSeconds, float now)
        {
            var frameMs = frameSeconds * 1000f;
            var commands = ServerConsole.Sent - _sentAtStart;
            if (frameMs > SpikeMs && frameMs < 20000f)
            {
                if (now >= _nextLog)
                {
                    _nextLog = now + LogInterval;
                    var share = ModMs >= frameMs * 0.5 ? "le mod" : "le jeu";
                    CmdLog.Info("Image lente : " + Mathf.RoundToInt(frameMs) + " ms, dont " + ModMs.ToString("0.#", CultureInfo.InvariantCulture)
                                + " ms dans le mod (" + Spawns + " création(s), " + Kicks + " kick(s), " + commands + " commande(s) console, "
                                + Reassigns + " recalcul(s) de places, " + GroundCasts + " recherche(s) de sol ; " + LineManager.TotalBots
                                + " bots) → pic venu surtout du " + share.Substring(3) + "."
                                + (_skipped > 0 ? " (" + _skipped + " autre(s) image(s) lente(s) non détaillée(s))" : ""));
                    _skipped = 0;
                }
                else _skipped++;
            }
            // Cumuls du résumé (image précédente).
            if (frameMs < 20000f)
            {
                _frames++;
                _modSum += ModMs;
                if (ModMs > _modMax) _modMax = ModMs;
                if (frameMs > _frameMax) _frameMax = frameMs;
                if (frameMs > 50f) _slow50++;
                if (frameMs > 100f) _slow100++;
            }
            if (_summaryStart < 0f || now < _summaryStart) Restart(now);
            else if (now - _summaryStart >= SummaryInterval) Summary(now);
            Spawns = Kicks = Reassigns = GroundCasts = 0;
            ModMs = 0d;
            _sentAtStart = ServerConsole.Sent;
        }
    }
}
