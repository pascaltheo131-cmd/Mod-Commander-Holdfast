using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Journal du mod dans l'outputlog du serveur (via Debug.Log) : « [CMD] … » pour le diagnostic, « [CMD-WARN] … » pour
    /// les erreurs absorbées. Rien d'autre n'est écrit nulle part : ni fichier, ni réseau, ni statistique.
    /// Ne jamais y passer un mot de passe (OnRCLogin) ni une ligne de commande « rc login ».
    /// </summary>
    public static class CmdLog
    {
        // Avertissements : au plus MaxWarnings par fenêtre de WarnWindow secondes, pour ne pas inonder le journal
        // quand une commande de bot échoue à chaque image.
        private const int MaxWarnings = 20;
        private const float WarnWindow = 60f;

        private static float _windowStart = -1000f;
        private static int _warnings;
        private static int _hidden;

        /// <summary>Nouvelle origine de l'horloge (Clock.Reset) : la fenêtre des avertissements repart.</summary>
        public static void ResetClock()
        {
            _windowStart = -1000f;
            _warnings = 0;
        }

        public static void Info(string message)
        {
            Debug.Log("[CMD] " + message);
        }

        public static void Warn(string message)
        {
            var now = Clock.Now;
            if (now - _windowStart >= WarnWindow)
            {
                if (_hidden > 0) Debug.Log("[CMD-WARN] " + _hidden + " avertissement(s) masqué(s) pendant la dernière minute");
                _windowStart = now;
                _warnings = 0;
                _hidden = 0;
            }
            if (_warnings >= MaxWarnings)
            {
                _hidden++;
                return;
            }
            _warnings++;
            Debug.Log("[CMD-WARN] " + message);
        }
    }
}
