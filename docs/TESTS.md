# Campagne de tests en jeu (Commander 0.2.0, positionnement 3.20)

Sur une **instance de test sans autres joueurs**. Pour chaque test : faire l'action, regarder le jeu, vérifier la ligne `[CMD]`, puis
noter `OK` ou `KO` (avec message exact, ligne du journal, capture). Les textes entre guillemets sont copiés du code :
`<…>` = valeur variable. Valeurs par défaut : 15 bots par officier et par manche (`quotaofficier`), drapeau dès 8 (`auxmin`),
tambour dès 12 (`drummin`), médecin dès 10 (`medecinmin`), créations par 15 (`spawnlot`), kicks par 10 par seconde.

Journal : `tail -f /home/debian/holdfast/server/logs_output/outputlog_<instance>.txt | grep -a --line-buffered "\[CMD"`.
`[CMD-WARN]` isolé : à noter ; en boucle : KO.

## 1. Préparation

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| P.1 | Config (panneau) : `mods_installed_server_only <ID>`, `load_mod_server_only <ID>` (**jamais `load_mod`**), `mod_variable CMD:actif:true`. Lancer l'instance | Dans l'ordre : `Configuration : actif=True, …`, `Commander 0.2.0 chargé — Positionnement 3.20.`, `Boucle créée.`, `Manche <n> sur <carte> (mode <m>).` **Si « 3.20 » manque, ce n'est pas la bonne version.** | |
| P.2 | Laisser tourner 2 min sans bot | Aucun `[CMD-WARN]` ; `Charge : intervalle normal estimé …` au plus une fois | |
| P.3 | `rc login <mdp>`, puis `!cmd etat` | Bandeau `Commander : actif, 0 ligne(s) + 0 …` ; le mot de passe n'apparaît nulle part dans le journal | |
| P.4 | Jouer **officier d'infanterie** pour la suite | | |

## 2. Levée et quota

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| L.1 | En simple soldat : `!ligne 5` | « Seul un officier d'infanterie en vie peut lever une ligne. » | |
| L.2 | Officier : `!ligne` (seul) | **15 bots d'un coup** derrière vous, avec drapeau, tambour, médecin ; chat « 15 hommes arrivent. Ligne : 15. » ; journal `<vous> lève 15 bots (ArmyLineInfantry, <faction>) ; ligne 15, total 15/<plafond>.` puis `<nom> #<id> apparu, …` et `Levée de <vous> : 15 hommes levés.` | |
| L.3 | Bot mal apparu (classe d'artillerie, ou pas apparu) | `<bot> apparu en <classe> … : retiré, nouvel essai …` / `<bot> créé mais non apparu après 4 s : retiré, nouvel essai …` ; bilan « … n'ont pas pu apparaître (limites de classes des sections d'apparition : vérifiez spawn_override pour toutes les sections) » si échec après 6 essais | |
| L.4 | `!quota` | « Quota : 15 bots levés sur 15 par manche, reste 0… » | |
| L.5 | `!renvoyer`, puis `!ligne 5` | Bots dispersés puis retirés (10 par seconde, `<bot> #<id> retiré du serveur (kick).`) ; puis « Quota atteint : 15 bots par manche (déjà levés : 15). » | |
| L.6 | `!cmd quota reset` (admin) puis `!ligne 4 garde` | `Admin <vous> : quotas remis à zéro` ; 4 gardes | |
| L.7 | `!ligne 4 cavalerie` | « Classe inconnue : cavalerie. Classes : … » | |
| L.8 | Se déconnecter avec une ligne, revenir | `Fin de la ligne de <vous> (parti) : <n> bots.` ; bots dispersés puis retirés. Si un sergent tenait la ligne : `Officier <vous> déconnecté : sa ligne tenue par un sergent se disperse (<n> bots).` | |

## 3. Formations (ligne de 15 levée)

Durée mesurée par le mod : `Ligne de <vous> formée en <t> s (<n> hommes).` **Attendu : ≤ 10 s à 15 hommes sur terrain plat** (noter la valeur).

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| F.1 | `!former` | Ligne 3 m devant vous, 2 rangs, ~0,73 m entre hommes, sans bousculade ; ligne « formée en » | |
| F.2 | `!rangs 1`, `!rangs 2`, puis phrase **Insult** | « Rang simple. » / « Rang double. » ; Insult bascule : « La ligne se forme sur un rang. » / « … sur deux rangs. » ; `Phrase Insult de <vous> → ligne <mode>.` | |
| F.3 | Ordre roue « Former la ligne » sur un point à 20 m | `Ordre <Ordre> de <vous> (<x>, <z>, lacet <l>).` ; déploiement en éventail, chacun à sa place fixe | |
| F.4 | `!carre` (lignes de 15 et de 40 si échauffement) | Carré équilibré (4 côtés de même longueur ; 1 rang jusqu'à 32 hommes), médecin au centre | |
| F.5 | Tuer 3 bots (`rc serverAdmin slay <id>`), phrase **CloseRanks** | « Les bots resserrent les rangs. » ; trous refermés | |
| F.6 | Former sur une pente / près d'arbres | Aucun bot flottant ; sinon `<bot> #<id> tombé sous la carte (y …) : replacé sur son chef.` / `… bloqué à … m de sa place : détour…` (noter) | |

## 4. Colonne et suivre

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| C.1 | **Patriotic Cheer** (touche B) | La ligne vous suit en colonne ; journal `Positionnement 3.20 : suivi de <vous> à <n> de front, intervalle … ; ligne sur <r> rang(s).` | |
| C.2 | Marcher 50 m, tourner | Colonne par blocs, aile en tête, pas d'accordéon ni de bot perdu | |
| C.3 | Patriotic Cheer de nouveau | Halte : la ligne s'arrête (un appui = une action) | |
| C.4 | Phrases **Follow**, **ForwardMarch**, **AtTheDouble**, **Halt** (5 phrases max / 30 s) | « La ligne vous suit. » / « La ligne avance. » / « … au pas de course. » / « La ligne fait halte. » ; `Phrase <Phrase> de <vous> → ligne <mode>.` | |

## 5. Tir et mêlée

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| T.1 | Roue : « Apprêtez-vous » puis « Feu » | Visée puis salve étalée ~0,4 s ; `En joue : <n> bot(s) prêt(s), <k> ignoré(s) (…)` puis `Feu : <n> bot(s) prêt(s), …` | |
| T.2 | « Feu à volonté » 30 s, puis « Cessez-le-feu » | Tirs individuels + rechargement, puis armes baissées | |
| T.3 | « Charge » vers des bots ennemis (`rc carbonPlayers spawnSpecific <adverse> ArmyLineInfantry "CMD Ennemi" x 5`) | Baïonnette sans va-et-vient, course, frappes au contact | |
| T.4 | Plus d'ennemi au contact | **Sortie de mêlée propre** : les bots cessent de frapper, rangent la garde et rejoignent leur place (pas de bot qui frappe dans le vide) | |

## 6. Auxiliaires

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| A.1 | Ligne ≥ 8 | Porte-drapeau **drapeau en main** ; `<bot> : drapeau en main.` (KO : `… n'a pas confirmé le drapeau en main après 10 essais …`) | |
| A.2 | Ligne ≥ 12, ligne en marche | Musicien uniforme au hasard, **mains libres**, joue ; `Musique : <bot> #<id> mains libres confirmées.`, `… instrument sorti : …`, `… morceau <n> « <titre> », instrumentPlay …`, `… buff Musician reçu : le morceau est joué.` (KO : `… pas de buff Musician après 5 s …`) | |
| A.3 | `!musique` deux fois, puis `!musique hasard` | Se tait / reprend | |
| A.4 | Ligne ≥ 10 ; blesser un bot ou vous-même (allié à < 15 m) | Un seul médecin ; va soigner puis revient : `Soins : <médecin> #<id> soigne <nom> #<id> (<pv> PV).`, puis `… a soigné <nom> #<id>.` | |
| A.5 | `!medecin` | Médecin au repos / actif | |

## 7. Fin de manche

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| M.1 | Tuer tous les ennemis (ou attendre la fin de manche) | Bots cessent le feu, dansent, crient ; `Victoire de <faction> : <n> bots fêtent.` | |
| M.2 | Faire réapparaître un ennemi pendant la fête | `Fin de la fête : des ennemis sont revenus, les lignes reprennent leurs ordres.` | |
| M.3 | Changement de manche | `Nettoyage (<raison>) : <n> bots retirés, …` ; nouvelle ligne `Manche <n> sur …` ; quotas remis à zéro | |

## 8. Échauffement

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| E.1 | `mod_variable CMD:echauffement:<carte>` (nom tel que dans `Manche <n> sur <carte>`), redémarrer | Début de manche : « Manche d'échauffement : bots sans quota (jusqu'à 100 par officier). » ; `!cmd etat` affiche « (manche d'échauffement) » | |
| E.2 | `!ligne 40`, puis `!ligne 40` | Pas de « Quota atteint » ; créations par lots, kicks au `!renvoyer` sans gel visible | |
| E.3 | Carte suivante (hors liste) | Quota normal de retour | |

## 9. IA

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| I.1 | `!cmd ia lever` | `Admin <vous> : cmd ia lever → …` ; `Ligne IA forcée pour <faction> : <n> soldats (<classe>).` | |
| I.2 | Approcher la ligne IA | Elle avance, tire, charge : `Ligne IA <nom> charge (<n> contre <m>, ennemi à <d> m).` | |
| I.3 | `!cmd ia retirer` | `Admin <vous> : cmd ia retirer (<n> ligne(s)).`, `Ligne IA <nom> terminée (retrait).` | |

## 10. Performances

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| R.1 | Lever 15, puis 3 lignes (échauffement) | Au plus une ligne / 10 s : `Image lente : <ms> ms, dont <x> ms dans le mod (<c> création(s), <k> kick(s), <n> commande(s) console, <r> recalcul(s) de places, <g> recherche(s) de sol ; <b> bots) → pic venu surtout du mod / du jeu.` Noter qui cause le pic | |
| R.2 | Attendre 60 s | `Charge 60 s : <b> bots, <x> commandes console/s, <p> paquets/s ; mod <m> ms/image en moyenne, max <y> ms ; <i> images, <a> > 50 ms, <c> > 100 ms (max <z> ms) ; mémoire gérée <M> Mo, <g> passage(s) du GC.` Noter pour 15 / 45 / 100 bots | |
| R.3 | Toute la campagne | Pas de milliers de « Updating setting » ; un bot inconnu est oublié : `<nom> #<id> inconnu du jeu après 5 commandes refusées : oublié.` | |

## 11. Console

| N° | Faire | Voir / journal | Rés. |
|---|---|---|---|
| K.1 | Officier sur console : roue « Former la ligne », « Feu », Patriotic Cheer | Mêmes effets et lignes `Ordre …` qu'au PC | |
| K.2 | Console admin : `rc cmd etat`, `rc cmd quota reset` | Réponses identiques au chat ; erreurs éventuelles en `Commande rc cmd : …` | |
| K.3 | Une console rejoint le serveur | Entrée acceptée (mod serveur seulement) | |
