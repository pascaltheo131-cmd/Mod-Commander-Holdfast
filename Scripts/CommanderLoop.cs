using System;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Horloge unique du mod, en secondes, insensible à Time.timeScale. Comptée depuis une origine remise à zéro à chaque
    /// manche (Reset) : un float garde ainsi sa précision même après des jours de fonctionnement du serveur.
    /// Après Reset, tous les instants mémorisés (prochaines échéances) doivent être remis à zéro aussi.
    /// </summary>
    public static class Clock
    {
        private static double _origin;

        public static float Now => (float)(Time.realtimeSinceStartupAsDouble - _origin);

        public static void Reset()
        {
            _origin = Time.realtimeSinceStartupAsDouble;
        }
    }

    /// <summary>
    /// Boucle par image. L'objet est créé dans le code (CommanderMod.OnIsServer), sans scène : une scène dans le mod
    /// rendrait le serveur incompatible console (docs/API.md §2.3). À vérifier en jeu : Update bien appelé [TEST].
    /// </summary>
    public class CommanderLoop : MonoBehaviour
    {
        private static GameObject _host;
        /// <summary>Vrai pendant Update du mod (pour ne pas compter deux fois le temps des rappels synchrones).</summary>
        public static bool InUpdate;

        /// <summary>Crée l'objet porteur une seule fois ; il survit aux changements de carte.</summary>
        public static void Ensure()
        {
            if (_host != null) return;
            _host = new GameObject("CommanderModLoop");
            DontDestroyOnLoad(_host);
            _host.AddComponent<CommanderLoop>();
            CmdLog.Info("Boucle créée.");
        }

        private void Update()
        {
            FrameStats.BeginFrame(Time.unscaledDeltaTime, Clock.Now);
            var start = Time.realtimeSinceStartupAsDouble;
            InUpdate = true;
            try
            {
                PlayerWelcome.Tick(Clock.Now);
                LineManager.Tick(Clock.Now);
                RoundEvents.Tick();
            }
            catch (Exception e)
            {
                // Une exception ici ne doit pas arrêter la boucle aux images suivantes.
                // e.Message seulement : e.GetType().Name passe par System.Reflection, refusé par uMod.
                CmdLog.Warn("Boucle : " + e.Message);
            }
            InUpdate = false;
            FrameStats.ModMs += (Time.realtimeSinceStartupAsDouble - start) * 1000d;
            // Mesure de charge (lot B, n° 16) : durée de l'image et temps passé dans le mod.
            LoadMonitor.Sample(Time.unscaledDeltaTime, (float)((Time.realtimeSinceStartupAsDouble - start) * 1000d), Clock.Now, LineManager.TotalBots == 0);
        }

        private void OnDestroy()
        {
            if (_host == gameObject) _host = null;
        }
    }
}
