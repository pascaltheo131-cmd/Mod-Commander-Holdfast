namespace HoldfastCommander
{
    /// <summary>Un officier (joueur) qui commande une ligne de bots.</summary>
    public class Commander
    {
        public readonly int PlayerId;
        public Line Line;

        // Règle anti-doublon (docs/PLAN.md) : un ordre du menu fait aussi crier une phrase au personnage ;
        // si OnPlayerShout arrive pour cette phrase juste après, elle est ignorée.
        public int[] LastOrderPhrases = new int[0];
        public float LastOrderAt = -1000f;

        public Commander(int playerId)
        {
            PlayerId = playerId;
        }

        public string Name => Players.NameOf(PlayerId);

        public bool HasLine => Line != null && Line.Mode != LineMode.Dispersing;
    }
}
