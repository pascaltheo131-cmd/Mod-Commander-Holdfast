using System;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>Horloge unique du mod (secondes depuis le lancement du serveur, insensible à Time.timeScale).</summary>
    public static class Clock
    {
        public static float Now => Time.realtimeSinceStartup;
    }

    /// <summary>
    /// Boucle par image. L'objet est créé dans le code (CommanderMod.OnIsServer), sans scène : une scène dans le mod
    /// rendrait le serveur incompatible console (docs/API.md §2.3). À vérifier en jeu : Update bien appelé [TEST].
    /// </summary>
    public class CommanderLoop : MonoBehaviour
    {
        private static GameObject _host;

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
            try
            {
                LineManager.Tick(Clock.Now);
            }
            catch (Exception e)
            {
                // Une exception ici ne doit pas arrêter la boucle aux images suivantes.
                CmdLog.Warn("Boucle : " + e.GetType().Name + " " + e.Message);
            }
        }

        private void OnDestroy()
        {
            if (_host == gameObject) _host = null;
        }
    }
}
