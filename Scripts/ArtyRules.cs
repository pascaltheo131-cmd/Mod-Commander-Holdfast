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
        /// <summary>Tir par les bots quand l'artilleur n'est pas à sa pièce (pointeur, CMD:arty_tir_bots).</summary>
        public static bool BotFire;
        /// <summary>Portée du pointeur, et distance en dessous de laquelle il ne tire pas.</summary>
        public static float FireRange = 350f;
        public const float FireMinRange = 40f;
        /// <summary>Vitesse du boulet supposée pour la hausse (donnée du jeu non lisible par le mod) : à régler en jeu.</summary>
        public static float ShotSpeed = 150f;
        /// <summary>
        /// Construction de l'ébauche par le sapeur : « auto » (coups par interaction, puis frappes de pelle si rien ne
        /// progresse), « interaction », « pelle », ou « non » (ébauche laissée telle quelle).
        /// </summary>
        public static string SapperBuild = "auto";
        /// <summary>Le pourvoyeur cherche l'id d'une caisse jamais touchée (PurveyorTask.Probe).</summary>
        public static bool BoxProbe = true;

        // Camps où le jeu a refusé la classe Sapper cette manche. spawnSpecific tire une section d'apparition au hasard dans
        // le camp, sans moyen de l'imposer ; vu en jeu (TahirDesert, ArmyAssault) : une seule section, qui ne permet pas le
        // sapeur, 25 essais et 25 retraits visibles. Désormais un seul essai, puis plus de sapeur pour ce camp jusqu'à la
        // manche suivante.
        private static readonly System.Collections.Generic.HashSet<int> NoSapper = new System.Collections.Generic.HashSet<int>();

        /// <summary>
        /// Construction instantanée (CMD:construction_instantanee) : variable console du jeu « instantBuildMode »
        /// (ServerConsoleCommandsInitializer → CommonGlobalVariables.InstantBuilding, remise à faux à chaque carte par
        /// ResetMapSpecific) : une protection posée naît construite (EmplacementInteractableObjectBehaviour). Envoyée à chaque manche.
        /// </summary>
        public static bool InstantBuild = true;

        public static void ApplyInstantBuild()
        {
            if (!InstantBuild) return;
            ServerConsole.Execute("set instantBuildMode true", "Construction instantanée", false, out var output);
            CmdLog.Info("[ARTY] Construction instantanée : " + (output ?? "").Trim());
        }

        public static void Clear() => NoSapper.Clear();

        public static int SappersFor(int faction) => SapperEnabled && !NoSapper.Contains(faction) ? 1 : 0;

        /// <summary>Le sapeur d'une pièce est apparu dans une autre classe : pas de nouvel essai, l'artilleur est prévenu.</summary>
        public static void SapperRefused(Line line, int section)
        {
            if (!NoSapper.Add(line.Faction)) return;
            CmdLog.Info("[ARTY] Sapeur non permis à l'apparition (section " + section + ") : plus de sapeur pour ce camp cette manche.");
            if (!line.Owner.IsAi) ServerConsole.Private(line.Owner.PlayerId, "Pas de sapeur sur cette carte pour votre camp : le jeu ne permet pas cette classe à l'apparition.");
        }

        /// <summary>Le joueur artilleur vient de prendre un canon : ses servants sont levés (ou complétés).</summary>
        public static void RaiseFor(PlayerInfo gunner)
        {
            var reply = LineManager.RaiseCrew(gunner, Servants, SappersFor(gunner.Faction));
            if (reply != null) ServerConsole.Private(gunner.Id, reply);
        }
    }
}
