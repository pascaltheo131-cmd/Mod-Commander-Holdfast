using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Fin de manche quand un camp n'a plus de joueur vivant. Le jeu termine une manche Assault quand un camp n'a plus
    /// personne une fois les apparitions fermées, mais nos bots comptent comme des joueurs : la manche ne finissait pas.
    /// Après l'heure de fermeture (CMD:fin_joueurs en secondes de manche, 0 = désactivé ; par défaut celle de la charge
    /// générale), un camp qui avait des joueurs et n'en a plus aucun de vivant perd ses bots (slay) : le jeu clôt la manche.
    /// Sans bots (ou si le jeu ne conclut pas), le mod termine lui-même la manche quelques secondes après
    /// (« set roundEndFactionWin <camp adverse> », la commande du bouton de fin de manche du menu admin).
    /// </summary>
    public static class LastStand
    {
        public static float After = -1f;
        private static bool _registered;
        private static bool _done;
        private static float _endAt = -1f;
        private static int _winner = -1;
        private const float EndDelay = 4f;
        private static float _nextCheck;
        private static readonly List<int> Kill = new List<int>();

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            CommanderConfig.ExtraApply.Add((key, value) =>
            {
                if (key != "fin_joueurs" && key != "last_stand") return false;
                After = CommanderConfig.ParseFloat(key, value, -1f, 36000f, After);
                return true;
            });
        }

        public static void Reset()
        {
            _done = false;
            _endAt = -1f;
        }

        /// <summary>Le jeu a conclu la manche : plus rien à forcer.</summary>
        public static void RoundEnded()
        {
            _done = true;
            _endAt = -1f;
        }

        private static float Threshold => After >= 0f ? After : Automod.ChargeTime;

        public static void Tick(float now)
        {
            if (_endAt >= 0f && now >= _endAt)
            {
                _endAt = -1f;
                CmdLog.Info("Le jeu n'a pas conclu la manche : victoire donnée à " + GameIds.FactionName(_winner) + ".");
                ServerConsole.Execute("set roundEndFactionWin " + GameIds.FactionName(_winner), "Fin de manche");
                return;
            }
            if (_done || Threshold <= 0f || now < _nextCheck || RoundTime.Elapsed < Threshold) return;
            _nextCheck = now + 1f;
            foreach (var faction in Factions())
            {
                var humans = 0;
                var alive = 0;
                foreach (var p in Players.All)
                {
                    if (p.IsBot || p.Faction != faction) continue;
                    humans++;
                    // Mort mais attendu par sa ligne (porte-drapeau debout) : il revient, le camp n'est pas éliminé.
                    if (p.Alive || LineManager.AwaitsLeader(p.Id)) alive++;
                }
                if (humans == 0 || alive > 0) continue;
                _done = true;
                Kill.Clear();
                foreach (var p in Players.All) if (p.IsBot && p.Alive && p.Faction == faction) Kill.Add(p.Id);
                CmdLog.Info("Plus aucun joueur vivant pour " + GameIds.FactionName(faction) + " : " + Kill.Count + " bots retirés, fin de manche.");
                ServerConsole.Broadcast("Plus aucun joueur pour " + GameIds.FactionName(faction) + " : la manche se termine.");
                foreach (var id in Kill) BotCommands.Kill(id);
                _winner = Opponent(faction);
                if (_winner >= 0) _endAt = now + EndDelay;
                return;
            }
        }

        private static int Opponent(int faction)
        {
            int att = AiDirector.Attacker, def = AiDirector.Defender;
            var none = GameIds.FactionCountry.None;
            if (faction == att && def != none) return def;
            if (faction == def && att != none) return att;
            foreach (var p in Players.All) if (p.Faction != faction && p.Faction != GameIds.FactionCountry.None) return p.Faction;
            return -1;
        }

        private static readonly HashSet<int> FactionSet = new HashSet<int>();

        private static HashSet<int> Factions()
        {
            FactionSet.Clear();
            foreach (var p in Players.All) if (!p.IsBot && p.Faction != GameIds.FactionCountry.None) FactionSet.Add(p.Faction);
            return FactionSet;
        }
    }
}
