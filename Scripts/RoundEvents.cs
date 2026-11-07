namespace HoldfastCommander
{
    /// <summary>Annonces publiques de la manche (début, 5 minutes restantes, victoire) et récapitulatif gardé pour la manche seulement.</summary>
    public static class RoundEvents
    {
        private const int Sides = 2; // 0 : attaquant, 1 : défenseur, selon OnRoundDetails
        private static int _attacker = -1, _defender = -1;
        private static readonly int[] Raised = new int[Sides];
        private static readonly int[] Lost = new int[Sides];
        private static readonly int[] Routed = new int[Sides];
        private static bool _fiveMinutesSaid;

        public static void OnRoundStart(string mapName, int attacker, int defender)
        {
            _attacker = attacker;
            _defender = defender;
            for (var i = 0; i < Sides; i++) Raised[i] = Lost[i] = Routed[i] = 0;
            _fiveMinutesSaid = false;
            var text = "Manche : " + mapName + ", " + GameIds.FactionName(attacker) + " contre " + GameIds.FactionName(defender) + ".";
            if (Rules.Reminder) text += " Règlement : !regles";
            ServerConsole.Broadcast(text);
        }

        public static void Tick()
        {
            if (_fiveMinutesSaid || _attacker < 0) return;
            var remaining = RoundTime.Remaining;
            if (remaining < 0f || remaining > 300f) return;
            _fiveMinutesSaid = true;
            // Annonce seulement près du seuil (pas en arrivant sur une manche presque finie).
            if (remaining > 290f) ServerConsole.Broadcast("Il reste 5 minutes.");
        }

        private static int Side(int faction) => faction == _attacker ? 0 : faction == _defender ? 1 : -1;

        public static void OnLineRaised(int faction) { var s = Side(faction); if (s >= 0) Raised[s]++; }
        public static void OnBotLost(int faction) { var s = Side(faction); if (s >= 0) Lost[s]++; }
        public static void OnLineRouted(int faction) { var s = Side(faction); if (s >= 0) Routed[s]++; }

        public static void OnWinner(int faction)
        {
            if (_attacker < 0) return;
            ServerConsole.Broadcast("Victoire : " + GameIds.FactionName(faction) + " !");
            ServerConsole.Broadcast(Recap(0, _attacker) + " | " + Recap(1, _defender));
            _attacker = _defender = -1;
        }

        private static string Recap(int side, int faction) =>
            GameIds.FactionName(faction) + " : " + Raised[side] + " ligne(s) levée(s), " + Lost[side] + " bot(s) perdu(s), "
            + Routed[side] + " ligne(s) en déroute";
    }
}
