namespace HoldfastCommander
{
    /// <summary>
    /// Règles de l'artillerie servie par des bots.
    /// - Effectif d'une pièce : CMD:arty_servants canonniers (3 : chargeur, pourvoyeur, aide au déplacement) et un sapeur
    ///   (CMD:arty_sapeur), donnés au joueur Cannoneer qui prend un canon. Les servants apparaissent sur une section de leur
    ///   camp (le QG) puis sont amenés auprès de l'artilleur par le placement d'apparition commun à tous les bots
    ///   (LineManager) : aucun point d'apparition d'artillerie n'est nécessaire.
    /// - Horaire de tir : c'est le réglage existant CMD:arty_debut (Automod), qui interdit puis rouvre le tir d'artillerie
    ///   du jeu pour tout le monde. Les servants chargent avant l'heure ; le coup ne part qu'une fois le tir ouvert.
    /// - Artilleur tombé : ranimé à la place d'un de ses servants (LineManager, inchangé).
    /// </summary>
    public static class ArtyRules
    {
        /// <summary>Canonniers par pièce (0 : artillerie des bots désactivée).</summary>
        public static int Servants = 3;
        /// <summary>Un sapeur en plus par pièce.</summary>
        public static bool SapperEnabled = true;
        /// <summary>Durées tenues entre le début et la fin du chargement et du refoulement (celles du jeu : 4 s).</summary>
        public static float LoadTime = 4f;
        public static float RamTime = 4f;
        /// <summary>Le pourvoyeur cherche l'id d'une caisse jamais touchée (PurveyorTask.Probe).</summary>
        public static bool BoxProbe = true;

        public static int Sappers => SapperEnabled ? 1 : 0;

        /// <summary>Le joueur artilleur vient de prendre un canon : ses servants sont levés (ou complétés).</summary>
        public static void RaiseFor(PlayerInfo gunner)
        {
            var reply = LineManager.RaiseCrew(gunner, Servants, Sappers);
            if (reply != null) ServerConsole.Private(gunner.Id, reply);
        }
    }
}
