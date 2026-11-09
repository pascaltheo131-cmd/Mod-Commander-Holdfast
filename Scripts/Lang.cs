using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HoldfastCommander
{
    /// <summary>
    /// Langue des messages aux joueurs. Le mod écrit ses messages en français ; ils sont traduits à l'envoi
    /// (ServerConsole.Private / Broadcast) pour qui joue en anglais. Langue d'un joueur : celle de sa dernière commande du
    /// chat (!line → anglais, !ligne → français), sinon CMD:langue (fr par défaut, en, ou both pour les annonces).
    /// Un texte sans traduction connue part en français : rien n'est perdu.
    /// </summary>
    public static class Lang
    {
        /// <summary>Langue par défaut : "fr", "en" ; "both" = annonces dans les deux langues, joueurs en français.</summary>
        public static string Default = "fr";

        private static readonly Dictionary<int, bool> English = new Dictionary<int, bool>();

        public static void SetPlayer(int playerId, bool english) => English[playerId] = english;
        public static void Forget(int playerId) => English.Remove(playerId);

        public static bool IsEnglish(int playerId) =>
            English.TryGetValue(playerId, out var en) ? en : Default == "en";

        /// <summary>Texte pour ce joueur : traduit s'il joue en anglais ; « FR : a | EN: b » réduit à sa langue.</summary>
        public static string ForPlayer(int playerId, string french)
        {
            var en = IsEnglish(playerId);
            if (TrySplitBilingual(french, out var fr, out var eng)) return en ? eng : fr;
            return en ? ToEnglish(french) : french;
        }

        /// <summary>Annonce publique : langue par défaut du serveur (both : les deux).</summary>
        public static string ForAll(string french)
        {
            if (Default == "en") return ToEnglish(french);
            if (Default == "both")
            {
                var en = ToEnglish(french);
                return en == french ? french : french + " | " + en;
            }
            return french;
        }

        private static bool TrySplitBilingual(string text, out string fr, out string en)
        {
            fr = en = null;
            if (!text.StartsWith("FR : ")) return false;
            var i = text.IndexOf(" | EN: ", System.StringComparison.Ordinal);
            if (i < 0) return false;
            fr = text.Substring(5, i - 5);
            en = text.Substring(i + 7);
            return true;
        }

        public static string ToEnglish(string french)
        {
            if (string.IsNullOrEmpty(french)) return french;
            foreach (var t in Table)
            {
                var m = t.Key.Match(french);
                if (m.Success) return t.Key.Replace(french, t.Value);
            }
            return french;
        }

        private static KeyValuePair<Regex, string> T(string pattern, string english) =>
            new KeyValuePair<Regex, string>(new Regex("^" + pattern + "$", RegexOptions.Singleline), english);

        // Une entrée par message (expression régulière sur le texte français entier → anglais). L'ordre compte : le premier
        // qui correspond l'emporte.
        private static readonly List<KeyValuePair<Regex, string>> Table = new List<KeyValuePair<Regex, string>>
        {
            // ---- Lignes : ordres du chat
            T(@"Vous n'avez pas de ligne\. (.*)", "You have no line. !line <number> raises one, then !form, !follow, !ranks 1|2, !help."),
            T(@"La ligne vous suit\.", "The line is following you."),
            T(@"La ligne se forme devant vous\.", "The line is forming in front of you."),
            T(@"L'escadron se met en double colonne derrière vous\.", "The squadron forms a double column behind you."),
            T(@"La ligne se reforme devant vous\.", "The line is re-forming in front of you."),
            T(@"Position inconnue, réessayez\.", "Position unknown, try again."),
            T(@"Rang simple\.", "Single rank."),
            T(@"Rang double\.", "Double rank."),
            T(@"Baïonnette : ordre donné\.", "Bayonets: order given."),
            T(@"Accroupi / debout : ordre donné\.", "Crouch / stand: order given."),
            T(@"Pas de course\.", "Running pace."),
            T(@"Pas ordinaire\.", "Walking pace."),
            T(@"Formez le carré ! .*", "Form square! (leave it with !form or the Form Line order)"),
            T(@"Le carré est désactivé sur ce serveur\.", "Square is disabled on this server."),
            T(@"Musique désactivée sur ce serveur\.", "Music is disabled on this server."),
            T(@"Morceaux au hasard\.", "Random tunes."),
            T(@"Morceau (\d+) \(.*\)\.", "Tune $1 (if the game knows it for the musician's instrument)."),
            T(@"!musique .*", "!music [tune number 1 to 99 | hasard]"),
            T(@"Les musiciens jouent\.", "The musicians are playing."),
            T(@"Les musiciens se taisent\.", "The musicians stop playing."),
            T(@"Votre médecin soigne les blessés\.", "Your medic heals the wounded."),
            T(@"Votre médecin reste à sa place\.", "Your medic holds his place."),
            T(@"Renforts désactivés sur ce serveur\.", "Reinforcements are disabled on this server."),
            T(@"(\d+) hommes renvoyés : .*", "$1 men dismissed: the bots leave gradually."),
            T(@"Ordre réservé à la cavalerie\.", "Cavalry only."),
            T(@"Escadron de cavalerie : .*", "Cavalry squadron: !follow, !form, !ranks, charge (game Charge order), !wedge, !rally, !skirmish, !pace, !reinforce, !dismiss."),
            T(@"Escadron à pied : pas de charge en coin\.", "Squadron on foot: no wedge charge."),
            T(@"Charge en coin : .*", "Wedge charge: the squadron follows you in a triangle, sabres drawn. End: !follow or !form."),
            T(@"Ralliement : l'escadron revient derrière vous\.", "Rally: the squadron comes back behind you."),
            T(@"Escadron à pied : pas de tirailleurs à cheval\.", "Squadron on foot: no mounted skirmishers."),
            T(@"Tirailleurs : .*", "Skirmish: the squadron spreads out in front of you and fires at will. End: !form or !follow."),

            // ---- Levée
            T(@"(\d+) hommes arrivent( \(plafond : (\d+) sur (\d+)\))?\. Ligne : (\d+)\.(.*)", "$1 men are coming. Line: $5."),
            T(@"Trop de nombres : .*", "Too many numbers: !line 20 GDE 2 [class]."),
            T(@"Une seule classe par levée\.", "Only one class per raise."),
            T(@"Préfixe : .*", "Prefix: one word of 1 to 10 letters, digits, dashes or underscores."),
            T(@"Caractère interdit dans le préfixe\.", "Forbidden character in the prefix."),
            T(@"Faction inconnue : réapparaissez puis réessayez\.", "Unknown faction: respawn and try again."),
            T(@"Les lignes de bots sont désactivées sur ce serveur\.", "Bot lines are disabled on this server."),
            T(@"Seul un officier d'infanterie .*", "Only a living infantry officer or cavalryman (Hussar, Dragoon) can raise a line."),
            T(@"Levées interdites pour vous .*", "You may not raise bots until the end of the round (sanction)."),
            T(@"Pas de ligne possible pour cette faction\.", "No line possible for this faction."),
            T(@"Levées en pause .* (\d+) s\.", "Raising paused (the game refused spawns): try again in $1 s."),
            T(@"Votre ligne est d'infanterie : .*", "Your line is infantry: !dismiss it first."),
            T(@"Votre escadron est de cavalerie : .*", "Your squadron is cavalry: !dismiss it first."),
            T(@"Plafond atteint : votre ligne compte déjà (\d+) hommes\.", "Cap reached: your line already has $1 men."),
            T(@"Plafond de bots du serveur atteint \((\d+) avec (\d+) joueurs\)\.", "Server bot cap reached ($1 with $2 players)."),
            T(@"Levées dans (\d+) s\.", "Raising opens in $1 s."),
            T(@"Levées fermées pour cette manche\.", "Raising is closed for this round."),
            T(@"Pas de levée avec un ennemi à moins de (\d+) m\.", "No raising with an enemy within $1 m."),
            T(@"Uniforme (\S+) inconnu pour (.+)", "Unknown uniform $1 for $2"),
            T(@"Classe inconnue : (.+)\.", "Unknown class: $1."),
            T(@"!skin .*", "!skin <class> <uniform> (0: default uniform). !skins for the list."),
            T(@"Vous prenez la ligne de (.+) : (\d+) hommes(.*)\.", "You take over the line of $1: $2 men."),
            T(@"Pas de quota de bots sur ce serveur\.", "No bot quota on this server."),
            T(@"Quota : (\d+) bots levés sur (\d+) par manche, reste (.*)", "Quota: $1 bots raised out of $2 per round, $3 left."),

            // ---- Ligne au combat
            T(@"Vous reprenez votre ligne \((\d+) hommes\)\. .*", "You take back your line ($1 men). Use Break Rank and Follow to bring it to you."),
            T(@"Vous reprenez votre ligne \((\d+) hommes\) auprès de votre porte-drapeau\.", "You take back your line ($1 men) next to your flag bearer."),
            T(@"Votre porte-drapeau tient encore : .*", "Your flag bearer still stands: you are revived next to him."),
            T(@"Votre ligne a été dispersée : .*", "Your line was dispersed: !line to raise a new one."),
            T(@"Un sergent tient votre ligne : réapparaissez en officier dans les (\d+) s .*", "A sergeant holds your line: respawn as an officer within $1 s to take it back (as a sergeant: !takeover)."),
            T(@"Ligne de (.+) sans officier \((\d+) hommes\) : approchez à (\d+) m .* (\d+) s\.", "$1's line has no officer ($2 men): come within $3 m and type !takeover (or shout Follow) within $4 s."),
            T(@"Votre ligne est en déroute et se disperse\.", "Your line is routed and disperses."),
            T(@"Votre ligne flanche et recule \(moral (\d+)\)\. .*", "Your line wavers and falls back (morale $1). Shout Stand Ground to rally it."),
            T(@"Charge générale : plus de tir, mêlée seulement\.", "General charge: no more firing, melee only."),
            T(@"Visée du joueur indisponible : .*", "Your aim is not available yet: wait a moment and order Make Ready again."),
            T(@"Les hommes reprennent leur souffle : nouvelle charge dans (\d+) s\.", "The men are catching their breath: next charge in $1 s."),
            T(@"Pas de charge de cavalerie avant (\d+) min.*", "No cavalry charge before minute $1."),
            T(@"Renforts en pause .* (\d+) s\.", "Reinforcements paused: try again in $1 s."),
            T(@"Renforts pas encore prêts : encore (\d+) s\.", "Reinforcements not ready yet: $1 s left."),
            T(@"Pas de renforts : votre ligne est au complet\.", "No reinforcements: your line is at full strength."),
            T(@"Pas de renforts : plafond de bots du serveur atteint\.", "No reinforcements: the server bot cap is reached."),
            T(@"(\d+) hommes en renfort arrivent par l'arrière\.", "$1 reinforcements are coming from the rear."),
            T(@"Plafond de bots abaissé .*", "Bot cap lowered (server busier): some men leave your line."),
            T(@"Le jeu refuse l'apparition de vos hommes.*", "The game refuses to spawn your men (class slots?). Raising paused, try again shortly."),
            T(@"Moral désactivé sur ce serveur\.", "Morale is disabled on this server."),
            T(@"Moral : (\d+)/100 \((.+)\)\. Ralliement possible dans (\d+) s\.", "Morale: $1/100. Rally possible in $3 s."),
            T(@"Moral : (\d+)/100 \((.+)\)\. Ralliement possible \(StandGround\)\.", "Morale: $1/100. Rally possible (Stand Ground)."),
            T(@"Votre camp a moins de joueurs : ligne jusqu'à (\d+) hommes\.", "Your side has fewer players: line up to $1 men."),

            // ---- Cavalerie, artillerie, discipline
            T(@"Cavalerie complète pour votre camp.*", "Cavalry is full for your side: come back as an Officer, a line of bots awaits you."),
            T(@"Servants : prenez une fois un boulet .*", "Gun crew: take a ball from the gun's ammo box once so they can find it."),
            T(@"(\d+) servants arrivent\. .*", "$1 gun crew are coming. Release the gun after each shot: they reload, you aim and fire."),
            T(@"Plafond de bots du serveur atteint : pas de servants\.", "Server bot cap reached: no gun crew."),
            T(@"Avertissement : (.+)\.", "Warning: $1."),
            T(@"Sanction : (.+)\. Ligne renvoyée, .*", "Sanction: $1. Line dismissed, no raising until the end of the round."),
            T(@"Sanction : (.+)\.", "Sanction: $1."),
            T(@"Pas de règlement particulier sur ce serveur\.", "No specific rules on this server."),

            // ---- Annonces
            T(@"L'artillerie peut ouvrir le feu\.", "Artillery may open fire."),
            T(@"Charge générale dans (\d+) s !", "General charge in $1 s!"),
            T(@"CHARGE GÉNÉRALE ! .*", "GENERAL CHARGE! Melee only: dismount, no pistols, no artillery."),
            T(@"Des joueurs rejoignent (.+) : les lignes ennemies se retirent\.", "Players are joining $1: the enemy lines withdraw."),
            T(@"Une ligne ennemie \((.+)\) approche\.", "An enemy line ($1) is approaching."),
            T(@"Les lignes ennemies se retirent\.", "The enemy lines withdraw."),
            T(@"Il reste 5 minutes\.", "5 minutes left."),
            T(@"Victoire : (.+) !", "Victory: $1!"),
            T(@"Levées ouvertes : .*", "Raising is open: !line to raise your men (otherwise automatic raise at LIVE)."),
            T(@"LIVE ! Levées ouvertes : .*", "LIVE! Raising is open: without !line, officers and cavalrymen get their men."),
            T(@"Les levées sont ouvertes : !ligne", "Raising is open: !line"),
            T(@"Les lignes arrivent : .*", "Lines are arriving: officers and cavalrymen get their men."),
            T(@"Ligne enregistrée \((\d+) hommes\) : elle arrivera dans (\d+) s\.", "Line recorded ($1 men): it will arrive in $2 s."),
            T(@"L'officier (.+) est tombé : un sergent tient sa ligne\.", "Officer $1 has fallen: a sergeant holds his line."),
            T(@"L'officier (.+) est (tombé|parti) : sa ligne se disperse\.", "Officer $1 is gone: his line disperses."),
            T(@"La ligne se rallie\.", "The line is rallying."),
            T(@"La ligne fait halte\.", "The line is halting."),
            T(@"Erreur du mod, commande ignorée\.", "Mod error, command ignored."),
            T(@"Bienvenue ! .*", "Welcome! Pick a side, then the Officer class to command. !line <number> [prefix] [uniform] raises your line. To re-form/follow: !form, !follow, !ranks 1|2."),
            T(@"Autres commandes : .*", "More commands: !bayonet, !crouch, !pace, !square, !morale, !reinforce, !dismiss, !takeover. Uniforms: !skins [class], !skin <class> <id>. Help: !help, rules: !rules."),
            T(@"Commande réservée aux admins du serveur\.", "Command reserved for server admins."),
        };
    }
}
