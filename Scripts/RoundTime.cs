namespace HoldfastCommander
{
    /// <summary>
    /// Temps de la manche donné par le jeu (OnUpdateElapsedTime, OnUpdateTimeRemaining), en secondes. Tant que le jeu n'a rien
    /// envoyé : Elapsed = Clock.Now (depuis le chargement de la manche), Remaining = -1 (inconnu).
    /// </summary>
    public static class RoundTime
    {
        private static float _elapsed = -1f;
        private static float _remaining = -1f;

        public static float Elapsed => _elapsed >= 0f ? _elapsed : Clock.Now;
        public static float Remaining => _remaining;

        public static void Reset()
        {
            _elapsed = -1f;
            _remaining = -1f;
        }

        public static void OnElapsed(float time) => _elapsed = time;
        public static void OnRemaining(float time) => _remaining = time;
    }
}
