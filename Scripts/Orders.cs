using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Ordres de l'officier à sa ligne, selon la répartition retenue (docs/PLAN.md, 31/10/2026) :
    /// - menu d'ordres officier (OnOfficerOrderStart) pour les ordres principaux ;
    /// - phrases vocales (OnPlayerShout) pour le reste, et aussi pour les ordres principaux criés sans le menu.
    /// Un ordre du menu ne déclenche pas OnPlayerShout pour l'officier (chaque client joue la phrase lui-même) ; les bots, eux,
    /// crient des acclamations : leurs phrases sont filtrées dans CommanderMod (docs/CARBONPLAYERS.md §5.1).
    /// Anti-doublon gardé pour les phrases criées à la main : une phrase équivalente à l'ordre du menu donné juste avant
    /// (dans CMD:dedupwindow) est ignorée, pour ne pas exécuter deux fois le même ordre.
    /// Limite du jeu : 5 phrases par 30 s par joueur, les ordres par phrases ne peuvent pas s'enchaîner vite.
    /// </summary>
    public static class Orders
    {
        // Phrases équivalentes à chaque ordre du menu : celles de notre table, plus celle que le jeu associe à l'ordre
        // (FormLine → StandGround, Reinforce → Help, BreakRankAndFollow → Compliment), au cas où elle arriverait quand même [TEST].
        private static readonly Dictionary<int, int[]> PhrasesOfOrder = new Dictionary<int, int[]>
        {
            { GameIds.HighCommandOrderType.OfficerFormLine, new[] { GameIds.CharacterVoicePhrase.StandGround } },
            { GameIds.HighCommandOrderType.OfficerCharge, new[] { GameIds.CharacterVoicePhrase.Charge } },
            { GameIds.HighCommandOrderType.OfficerFire, new[] { GameIds.CharacterVoicePhrase.Fire } },
            { GameIds.HighCommandOrderType.OfficerFireAtWill, new[] { GameIds.CharacterVoicePhrase.FireAtWill } },
            { GameIds.HighCommandOrderType.OfficerCeaseFire, new[] { GameIds.CharacterVoicePhrase.CeaseFire } },
            { GameIds.HighCommandOrderType.OfficerMakeReady, new[] { GameIds.CharacterVoicePhrase.MakeReady, GameIds.CharacterVoicePhrase.TakeAim } },
            { GameIds.HighCommandOrderType.OfficerBreakRankAndFollow, new[] { GameIds.CharacterVoicePhrase.Follow, GameIds.CharacterVoicePhrase.Compliment } },
            { GameIds.HighCommandOrderType.OfficerReinforce, new[] { GameIds.CharacterVoicePhrase.Reinforce, GameIds.CharacterVoicePhrase.Help } },
        };

        // Au-delà de cette distance de l'officier, la position d'un ordre de charge est prise comme cible.
        private const float PointedTargetDistance = 5f;

        /// <summary>Ordre du menu officier, accepté par le jeu (OnOfficerOrderStart).</summary>
        public static void OnOfficerOrder(int officerId, int order, Vector3 position, float yaw, float now)
        {
            var c = LineManager.GetCommander(officerId);
            if (c == null || !c.HasLine) return;
            c.LastOrderPhrases = PhrasesOfOrder.TryGetValue(order, out var phrases) ? phrases : new int[0];
            c.LastOrderAt = now;
            CmdLog.Info("Ordre " + GameIds.OrderName(order) + " de " + c.Name + " (" + ServerConsole.Num(position.x) + ", "
                        + ServerConsole.Num(position.z) + ", lacet " + ServerConsole.Num(yaw) + ").");

            var line = c.Line;
            switch (order)
            {
                // [TEST] : orderPosition est-il la position de l'officier ou le point visé ?
                case GameIds.HighCommandOrderType.OfficerFormLine: line.FormAt(position, yaw, now); break;
                case GameIds.HighCommandOrderType.OfficerMakeReady: line.Aim(now); break;
                case GameIds.HighCommandOrderType.OfficerFire: line.Fire(-1, now); break;
                case GameIds.HighCommandOrderType.OfficerFireAtWill: line.StartFireAtWill(now); break;
                case GameIds.HighCommandOrderType.OfficerCeaseFire: line.CeaseFire(now); break;
                case GameIds.HighCommandOrderType.OfficerCharge: line.Charge(ChargeTarget(officerId, position), now); break;
                case GameIds.HighCommandOrderType.OfficerBreakRankAndFollow: line.Follow(now); break;
                case GameIds.HighCommandOrderType.OfficerReinforce:
                    // TODO lot A (renforts) : quelques bots arrivent depuis l'arrière, avec délai de recharge et plafond.
                    break;
            }
        }

        /// <summary>Phrase vocale d'un joueur (OnPlayerShout).</summary>
        public static void OnShout(int playerId, int phrase, float now)
        {
            var c = LineManager.GetCommander(playerId);
            if (c == null || !c.HasLine) return;
            if (now - c.LastOrderAt <= CommanderConfig.DedupWindow && System.Array.IndexOf(c.LastOrderPhrases, phrase) >= 0)
            {
                CmdLog.Info("Phrase " + GameIds.PhraseName(phrase) + " de " + c.Name + " ignorée (suit l'ordre du menu).");
                return;
            }

            var line = c.Line;
            switch (phrase)
            {
                case GameIds.CharacterVoicePhrase.Follow: line.Follow(now); break;
                case GameIds.CharacterVoicePhrase.Halt: line.Halt(now); break;
                case GameIds.CharacterVoicePhrase.ForwardMarch: line.March(line.Running, now); break;
                case GameIds.CharacterVoicePhrase.AtTheDouble:
                    if (line.Mode == LineMode.Marching) line.March(true, now);
                    else line.SetRunning(true);
                    break;
                case GameIds.CharacterVoicePhrase.FormColumn: line.Column(now); break;
                case GameIds.CharacterVoicePhrase.DoubleRank: line.SetRanks(2, now); break;
                case GameIds.CharacterVoicePhrase.SingleRank: line.SetRanks(1, now); break;
                case GameIds.CharacterVoicePhrase.CloseRanks: line.CloseRanks(now); break;
                // FixBayonet n'est dans aucun menu vocal : RemoveBayonet sert de bascule (et !baionnette dans le chat).
                case GameIds.CharacterVoicePhrase.RemoveBayonet: line.ToggleBayonet(now); break;
                case GameIds.CharacterVoicePhrase.GetDown: line.ToggleCrouch(now); break;
                case GameIds.CharacterVoicePhrase.TakeAim:
                case GameIds.CharacterVoicePhrase.MakeReady: line.Aim(now); break;
                case GameIds.CharacterVoicePhrase.FirstRankFire: line.Fire(0, now); break;
                case GameIds.CharacterVoicePhrase.SecondRankFire: line.Fire(1, now); break;
                case GameIds.CharacterVoicePhrase.Fire: line.Fire(-1, now); break;
                case GameIds.CharacterVoicePhrase.FireAtWill: line.StartFireAtWill(now); break;
                case GameIds.CharacterVoicePhrase.CeaseFire: line.CeaseFire(now); break;
                case GameIds.CharacterVoicePhrase.Charge: line.Charge(null, now); break;
                // TODO lot A (moral) : tenez bon rallie la ligne ; pour l'instant, halte.
                case GameIds.CharacterVoicePhrase.StandGround: line.Halt(now); break;
                case GameIds.CharacterVoicePhrase.Retreat: line.Retreat(now); break;
                case GameIds.CharacterVoicePhrase.Reinforce:
                    // TODO lot A (renforts).
                    break;
                default:
                    return;
            }
            CmdLog.Info("Phrase " + GameIds.PhraseName(phrase) + " de " + c.Name + " → ligne " + line.Mode + ".");
        }

        /// <summary>Point visé par l'ordre de charge s'il est loin de l'officier, sinon null (droit devant la ligne).</summary>
        private static Vector3? ChargeTarget(int officerId, Vector3 orderPosition)
        {
            var officer = Players.Get(officerId);
            if (officer == null || !officer.TryGetPosition(out var pos)) return null;
            return Line.Flat(orderPosition - pos).magnitude > PointedTargetDistance ? orderPosition : (Vector3?)null;
        }
    }
}
