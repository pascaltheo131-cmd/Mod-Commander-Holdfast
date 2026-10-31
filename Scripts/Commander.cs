namespace HoldfastCommander
{
    /// <summary>
    /// Officier qui commande une ligne de bots : un joueur, ou un bot officier pour une ligne ennemie menée par l'IA
    /// (lot C, IsAi ; son id n'est connu qu'à la création du bot, d'où PlayerId modifiable).
    /// </summary>
    public class Commander
    {
        public int PlayerId;
        public bool IsAi;
        public Line Line;

        // Règle anti-doublon (docs/PLAN.md) : un ordre du menu fait aussi crier une phrase au personnage ;
        // si OnPlayerShout arrive pour cette phrase juste après, elle est ignorée.
        public int[] LastOrderPhrases = new int[0];
        public float LastOrderAt = -1000f;
        // Renforts (lot A) : délai de recharge par officier.
        public float LastReinforceAt = -1000f;
        // Dernier nom connu : la ligne garde le nom de son officier après son départ (succession).
        private string _name;

        public Commander(int playerId)
        {
            PlayerId = playerId;
        }

        public string Name
        {
            get
            {
                var p = Players.Get(PlayerId);
                if (p != null) _name = p.Name;
                return _name ?? "#" + PlayerId;
            }
        }

        public bool HasLine => Line != null && Line.Mode != LineMode.Dispersing;
    }
}
