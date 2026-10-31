using System.Collections.Generic;
using UnityEngine;

namespace HoldfastCommander
{
    /// <summary>
    /// Noms des bots (« Prénom Nom ») tirés au hasard selon la faction, plausibles pour l'époque. Listes propres au mod.
    /// Pas de doublon dans une même ligne ; longueur bornée (le jeu coupe les noms à 32 caractères, le mod à 20).
    /// CMD:nomsaleatoires faux : « Soldat N » comme avant.
    /// </summary>
    public static class Names
    {
        private sealed class NameSet
        {
            public readonly string[] First;
            public readonly string[] Last;

            public NameSet(string first, string last)
            {
                First = first.Split(' ');
                Last = last.Split(' ');
            }
        }

        private static readonly NameSet French = new NameSet(
            "Jean Pierre Louis Jacques François Antoine Nicolas Joseph Michel Étienne Claude André Charles Henri Guillaume Paul Martin "
            + "Simon Denis Laurent Mathieu Julien Augustin Benoît Bernard Gabriel Hippolyte Honoré Jules Léon Marcel Noël Olivier Philippe "
            + "Raymond Sébastien Thomas Victor Xavier Baptiste Félix Gaspard Hector Isidore Lucien",
            "Martin Bernard Dubois Durand Lefebvre Moreau Laurent Simon Michel Leroy Roux David Bertrand Morel Fournier Girard Bonnet "
            + "Dupont Lambert Fontaine Rousseau Vincent Muller Lefèvre Faure André Mercier Blanc Guérin Boyer Garnier Chevalier François "
            + "Legrand Gauthier Garcia Perrin Robin Clément Morin Nicolas Henry Roussel Mathieu Gautier Masson Marchand Duval Denis "
            + "Dumont Marie Lemaire Noël Meyer Dufour Meunier Brun Blanchard Giraud Joly Rivière Lucas Brunet Gaillard Barbier Arnaud");

        private static readonly NameSet British = new NameSet(
            "John William James George Thomas Henry Charles Joseph Robert Edward Richard Samuel David Benjamin Daniel Francis "
            + "Alexander Andrew Archibald Angus Duncan Hugh Ewan Malcolm Neil Colin Donald Kenneth Arthur Frederick Albert Alfred Walter "
            + "Matthew Michael Peter Stephen Isaac Jonathan Nathaniel Hector Ralph Lachlan",
            "Smith Jones Taylor Brown Williams Wilson Johnson Davies Robinson Wright Thompson Evans Walker White Roberts Green Hall Wood "
            + "Jackson Clarke Hughes Edwards Turner Hill Moore Cooper Ward Harris Martin King Baker Morgan Parker Cook Bell Price Bailey "
            + "Campbell Stewart MacDonald Murray Grant Fraser Cameron Ross Mackenzie Robertson Reid Ferguson Scott Graham Hamilton Burns "
            + "Douglas Kerr Sinclair Munro Gordon Forbes Hunter Mitchell Paterson Wallace");

        private static readonly NameSet German = new NameSet(
            "Johann Friedrich Wilhelm Karl Heinrich Ludwig August Georg Christian Gottfried Ernst Franz Hermann Otto Paul Peter Jakob "
            + "Joseph Konrad Martin Michael Philipp Andreas Albrecht Anton Bernhard Christoph Gustav Hans Julius Klaus Leopold Matthias "
            + "Moritz Rudolf Sebastian Theodor Valentin Wolfgang Adolf Emil Ferdinand",
            "Müller Schmidt Schneider Fischer Weber Meyer Wagner Becker Schulz Hoffmann Schäfer Koch Bauer Richter Klein Wolf Schröder "
            + "Neumann Schwarz Zimmermann Braun Krüger Hofmann Hartmann Lange Schmitt Werner Krause Meier Lehmann Schmid Schulze Maier "
            + "Köhler Herrmann König Walter Mayer Huber Kaiser Fuchs Peters Lang Scholz Möller Weiß Jung Hahn Vogel Friedrich Keller "
            + "Günther Frank Berger Winkler Roth Beck Lorenz Baumann Franke Albrecht");

        private static readonly NameSet Austrian = new NameSet(
            "Johann Franz Joseph Anton Karl Leopold Ferdinand Ignaz Alois Matthias Michael Georg Jakob Andreas Florian Lorenz Sebastian "
            + "Wenzel Vinzenz Kaspar Balthasar Martin Simon Stefan Thomas Rupert Peter Paul Ludwig Maximilian Engelbert Johannes Felix "
            + "Ernst Rudolf Ladislaus Istvan Janos Ferenc Laszlo Tomas",
            "Gruber Huber Bauer Wagner Müller Pichler Steiner Moser Mayer Hofer Leitner Berger Fuchs Eder Fischer Schmid Winkler Weber "
            + "Schwarz Maier Schneider Reiter Mayr Schmidt Wimmer Egger Brunner Lang Baumgartner Auer Binder Lechner Wolf Wallner Aigner "
            + "Ebner Koller Lehner Haas Schuster Holzer Kern Strasser Novak Horvath Kovacs Szabo Nagy Toth Varga Dvorak Svoboda Prochazka "
            + "Kral Benes Jelinek Ruzicka Hajek Kolar");

        private static readonly NameSet Russian = new NameSet(
            "Ivan Pyotr Aleksei Nikolai Dmitri Sergei Mikhail Vasili Andrei Fyodor Grigori Yakov Pavel Semyon Stepan Timofei Yegor "
            + "Konstantin Matvei Nikita Osip Prokhor Savva Trofim Yuri Boris Danila Filipp Gavriil Ilya Kirill Kuzma Lev Luka Makar "
            + "Maksim Nazar Roman Taras Vladimir",
            "Ivanov Smirnov Kuznetsov Popov Vasiliev Petrov Sokolov Mikhailov Novikov Fyodorov Morozov Volkov Alekseev Lebedev Semyonov "
            + "Yegorov Pavlov Kozlov Stepanov Nikolaev Orlov Andreev Makarov Nikitin Zakharov Zaitsev Soloviev Borisov Yakovlev Grigoriev "
            + "Romanov Vorobiev Sergeev Kuzmin Frolov Aleksandrov Dmitriev Korolev Gusev Kiselev Ilyin Maksimov Polyakov Sorokin "
            + "Vinogradov Kovalev Belov Medvedev Antonov Tarasov Zhukov Baranov Filippov Komarov Davydov Belyaev Gerasimov Bogdanov "
            + "Osipov Sidorov");

        private static readonly NameSet Italian = new NameSet(
            "Giuseppe Giovanni Antonio Francesco Luigi Angelo Pietro Carlo Domenico Salvatore Vincenzo Michele Paolo Andrea Giacomo "
            + "Lorenzo Matteo Nicola Raffaele Stefano Tommaso Alessandro Bartolomeo Cesare Emilio Enrico Federico Filippo Gaetano Ignazio "
            + "Leonardo Marco Mario Orazio Pasquale Raimondo Sebastiano Ugo Vittorio Alberto Biagio",
            "Rossi Russo Ferrari Esposito Bianchi Romano Colombo Ricci Marino Greco Bruno Gallo Conti DeLuca Mancini Costa Giordano "
            + "Rizzo Lombardi Moretti Barbieri Fontana Santoro Mariani Rinaldi Caruso Ferrara Galli Martini Leone Longo Gentile Martinelli "
            + "Vitale Lombardo Serra Coppola DeSantis Marchetti Parisi Villa Conte Ferri Fabbri Bianco Marini Grasso Valentini Messina "
            + "Sala DeAngelis Gatti Pellegrini Palumbo Sanna Farina Rizzi Monti Cattaneo Morelli");

        private static readonly NameSet Spanish = new NameSet(
            "José Juan Antonio Francisco Manuel Pedro Miguel Diego Pablo Luis Andrés Fernando Rafael Joaquín Tomás Vicente Gregorio "
            + "Ramón Agustín Alonso Bartolomé Cristóbal Domingo Esteban Felipe Gaspar Gonzalo Ignacio Jaime Lorenzo Martín Mateo Nicolás "
            + "Rodrigo Santiago Sebastián Simón Alejandro Bernardo Cayetano",
            "García González Rodríguez Fernández López Martínez Sánchez Pérez Gómez Martín Jiménez Ruiz Hernández Díaz Moreno Muñoz "
            + "Álvarez Romero Alonso Gutiérrez Navarro Torres Domínguez Vázquez Ramos Gil Ramírez Serrano Blanco Molina Morales Suárez "
            + "Ortega Delgado Castro Ortiz Rubio Marín Sanz Núñez Iglesias Medina Garrido Cortés Castillo Santos Lozano Guerrero Cano "
            + "Prieto Méndez Cruz Calvo Gallego Herrera Márquez León Peña Flores Cabrera Campos");

        private static readonly NameSet American = new NameSet(
            "John William James Thomas George Samuel Joseph Benjamin Daniel Nathaniel Jonathan Ebenezer Elijah Isaac Josiah Jeremiah "
            + "Caleb Amos Asa Eli Ezra Gideon Hezekiah Ichabod Jedediah Levi Moses Noah Obadiah Reuben Seth Silas Solomon Zebulon "
            + "Abraham Aaron Charles Henry Peter Richard Andrew",
            "Adams Allen Baker Barnes Bradford Brewster Carter Chase Clark Coffin Cushing Dana Dexter Edwards Emerson Fisher Foster "
            + "Fuller Gardner Gates Greene Hale Hancock Hathaway Hawkins Hayes Holmes Howland Hubbard Hunt Lincoln Lowell Mason Morgan "
            + "Morris Nash Otis Paine Parsons Phelps Pierce Prescott Putnam Quincy Reed Revere Sargent Sherman Spencer Stark Sumner "
            + "Thayer Tilden Underwood Warren Webster Wheeler Whitney Winslow Wright");

        private static readonly Dictionary<int, NameSet> ByFaction = new Dictionary<int, NameSet>
        {
            { GameIds.FactionCountry.British, British },
            { GameIds.FactionCountry.ARBritish, British },
            { GameIds.FactionCountry.Allied, British },
            { GameIds.FactionCountry.Privateer, British },
            { GameIds.FactionCountry.French, French },
            { GameIds.FactionCountry.Prussian, German },
            { GameIds.FactionCountry.Central, German },
            { GameIds.FactionCountry.Austrian, Austrian },
            { GameIds.FactionCountry.Russian, Russian },
            { GameIds.FactionCountry.Italian, Italian },
            { GameIds.FactionCountry.Spanish, Spanish },
            { GameIds.FactionCountry.ARAmerican, American },
        };

        /// <summary>
        /// Nom au hasard pour la faction, d'au plus maxLength caractères, absent de used (ajouté à used). Si le prénom rend le nom
        /// trop long, il est réduit à son initiale ; après plusieurs essais, un numéro départage.
        /// </summary>
        public static string Random(int faction, int maxLength, HashSet<string> used)
        {
            var set = ByFaction.TryGetValue(faction, out var s) ? s : British;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var first = set.First[UnityEngine.Random.Range(0, set.First.Length)];
                var last = set.Last[UnityEngine.Random.Range(0, set.Last.Length)];
                var name = Fit(first, last, maxLength);
                if (name == null || used.Contains(name)) continue;
                used.Add(name);
                return name;
            }
            // Le suffixe peut dépasser trois caractères et un nom numéroté peut déjà exister.
            for (var number = 1; ; number++)
            {
                var suffix = " " + number;
                var budget = Mathf.Max(1, maxLength - suffix.Length);
                var stem = Fit(set.First[0], set.Last[0], budget) ?? "S";
                var numbered = stem + suffix;
                if (numbered.Length > maxLength || used.Contains(numbered)) continue;
                used.Add(numbered);
                return numbered;
            }
        }

        private static string Fit(string first, string last, int maxLength)
        {
            var full = first + " " + last;
            if (full.Length <= maxLength) return full;
            var initial = first.Substring(0, 1) + ". " + last;
            if (initial.Length <= maxLength) return initial;
            return last.Length <= maxLength ? last : null;
        }
    }
}
