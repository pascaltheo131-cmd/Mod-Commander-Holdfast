# Campagne de tests en jeu

À suivre dans l'ordre, sur une **instance de test sans autres joueurs**. Chaque test : faire les étapes, comparer au résultat attendu,
puis remplir la colonne **Résultat** : `OK`, ou `KO` + ce qui s'est passé (message exact, ligne du journal, capture).
Un test KO n'empêche pas de continuer, sauf mention contraire.

Notations : `<id>` = id d'un bot, `<moi>` = votre id (colonne de `rc playerlist`), `<mdp>` = mot de passe admin de l'instance,
`<adverse>` = la faction d'en face sur la carte (ex. `French`). Les commandes `rc …` se tapent dans la console admin du jeu (touche F1
sur PC), les commandes `!…` dans le chat.

---

## 1. Préparation

### 1.1 Instance de test
1. Sur **holdfast.tpsrv.fr**, créer une instance de test (nom court, par ex. `test`), **non listée ou avec mot de passe** pour que personne n'entre.
2. Choisir une carte d'infanterie simple et plate (ligne de bataille / champ ouvert), mode avec deux camps d'infanterie.
3. Régler un mot de passe admin. Si possible, une durée de manche courte (5 à 10 min) pour tester la fin de manche sans attendre.
4. Lancer l'instance depuis le panneau. Ne jamais faire ces tests sur une instance où des joueurs sont connectés.

### 1.2 Lignes de configuration (dans le panneau, jamais à la main dans `serverconfig_<instance>.txt`)
- **Phase 0** : aucune ligne Commander.
- **Phases 1 et suivantes** : ajouter (remplacer `<ID>` par l'ID Workshop du mod) :
  ```
  mods_installed_server_only <ID> #Commander
  load_mod_server_only <ID> #Commander
  mod_variable CMD:actif:true
  ```
  **Jamais `load_mod`** (mod client, les consoles ne pourraient plus entrer).
- Les autres `mod_variable CMD:…` indiquées dans une phase s'ajoutent sous ces lignes ; **redémarrer l'instance** après chaque changement.
  Ne jamais mettre de `#` dans une valeur (le reste de la ligne serait ignoré).

### 1.3 Lire le journal
Fichier : `/home/debian/holdfast/server/logs_output/outputlog_<instance>.txt` (sur le serveur). Lignes du mod seulement :
```
grep -a "\[CMD" /home/debian/holdfast/server/logs_output/outputlog_test.txt | tail -40
tail -f /home/debian/holdfast/server/logs_output/outputlog_test.txt | grep -a --line-buffered "\[CMD"
```
- `[CMD] …` : déroulement normal (commandes reçues, ordres, levées, fins de ligne).
- `[CMD-WARN] …` : erreur absorbée par le mod. Quelques-unes isolées : à noter. **En boucle** (des dizaines par minute,
  ou « N avertissement(s) masqué(s) ») : KO, copier les 20 premières.
- Vous pouvez aussi demander à Claude de lire ce fichier après une série de tests.

### 1.4 Connexion admin
En jeu : `rc login <mdp>` → message de connexion réussie. À refaire après chaque reconnexion. Le mod ne journalise jamais le mot de passe :
vérifier qu'aucune ligne `[CMD]` ne le contient (test P.1).

| N° | But | Étapes | Attendu | Résultat |
|---|---|---|---|---|
| P.1 | Mot de passe absent du journal | Après `rc login <mdp>` (phase 1), chercher le mot de passe dans le journal : `grep -a -c "<mdp>" outputlog_test.txt` | 0 ligne `[CMD]` qui le contient | |
| P.2 | Id personnel | `rc playerlist` | Votre nom et votre `<moi>` apparaissent | |

---

## 2. Phase 0 : sans le mod (`rc carbonPlayers`)

Reprise de [CARBONPLAYERS.md](CARBONPLAYERS.md) §10. Faction britannique dans les exemples : remplacer par celle de la carte.

| N° | But | Étapes | Attendu / à noter | Résultat |
|---|---|---|---|---|
| 0.1 | Création et id | `rc carbonPlayers spawnSpecific British ArmyLineInfantry "CMD Test 1" x 2` puis `rc playerlist` | Bot apparu tout de suite ? Uniforme 92nd Gordon Highlanders ? Nom affiché ? Noter son id | |
| 0.2 | Camp adverse | `rc carbonPlayers spawnSpecific <adverse> ArmyLineInfantry "CMD Ennemi" x 1` | Bot dans le camp adverse | |
| 0.3 | Places | Avec `max_players` bas (ex. 2) dans le panneau, créer 5 bots, puis connecter un 2ᵉ compte | Entrée acceptée ? Liste des serveurs : 1 ou 6 joueurs ? | |
| 0.4 | Pilotage | `rc carbonPlayers forceInputAxis true <id>`, `rc carbonPlayers forceInputRotation true <id>`, `rc carbonPlayers inputRotation 90 <id>`, `rc carbonPlayers inputAxis 0 1 <id>`, `rc carbonPlayers setRunning true <id>`, puis `rc carbonPlayers inputAxis 0 0.5 <id>`, puis `rc carbonPlayers inputAxis 0 0 <id>` | Le bot tourne, avance, court ; 0.5 = marche lente ? ; 0 0 = arrêt | |
| 0.5 | Placement | `rc teleport <id> <moi>` puis `rc teleport <id> 100,20,100` | Bot sur vous, puis au point donné | |
| 0.6 | Tir | `rc carbonPlayers pitch 0 <id>`, `rc carbonPlayers playerAction StartAimingFirearm <id>`, `rc carbonPlayers playerAction FireFirearm <id>`, `rc carbonPlayers playerAction StartReloadFirearm <id>`, puis `rc carbonPlayers pitch 1 <id>` et tirer de nouveau | Vise, tire, recharge ; avec pitch 1 la balle monte-t-elle ? | |
| 0.7 | Baïonnette, accroupi, mêlée | `rc carbonPlayers playerAction StartBayonetAttach <id>`, `rc carbonPlayers playerAction Crouch <id>` (2 fois : bascule ?), `rc carbonPlayers playerAction MeleeStrikeHigh <id>` puis `rc carbonPlayers playerAction None <id>` | Baïonnette mise, accroupi/debout, coup | |
| 0.8 | Phrase | `rc carbonPlayers voicePhrase Charge <id>` | Le bot crie « Charge » | |
| 0.9 | Musicien et drapeau | `rc carbonPlayers spawnSpecific British Musician "CMD Tambour" x 3`, `rc carbonPlayers instrumentPlay <id> MarchingDrum 1`, `rc carbonPlayers instrumentStop <id>` ; `rc carbonPlayers spawnSpecific British FlagBearer "CMD Drapeau" x 1` puis `rc carbonPlayers switchWeapon BearingFlag <id>` | Le tambour joue puis s'arrête ; le drapeau est porté | |
| 0.10 | Ordre donné par un bot | `rc carbonPlayers spawnSpecific British ArmyInfantryOfficer "CMD Officier" x 1`, puis `rc carbonPlayers highCommandOrder <id> OfficerFormLine <x,y,z> 0` | Marqueur de ligne visible ? | |
| 0.11 | Ralliement | `rc carbonPlayers spawnSpecific British Sergeant "CMD Sergent" x 1`, se placer près de lui, `rc carbonPlayers highCommandOrder <id> SergeantRally <x,y,z> 0` | Buff de ralliement reçu ? | |
| 0.12 | Réapparition | `rc carbonPlayers allowAutoRespawn true` puis `rc serverAdmin slay <id>` ; puis `rc carbonPlayers allowAutoRespawn false` et `rc serverAdmin slay <id>` | Avec true : revient (autre classe ?) ; avec false : reste mort | |
| 0.13 | Retrait | `rc serverAdmin kick <id>` | Bot retiré malgré « Unable to kick » ? Message Discord admin envoyé ? | |
| 0.14 | Sous-commande absente | `rc carbonPlayers teleportToPlayer <id> <moi>` | « Unable to parse command » | |
| 0.15 | Ordres d'officier | Jouer officier, 3 bots en ligne placés par `rc teleport`, donner « Feu » puis « Former la ligne » au menu d'ordres | Les bots crient-ils une acclamation ? (en phase 1, le journal ne doit montrer aucun ordre venant d'eux) | |

À noter aussi (PLAN.md étape 0) : coût CPU de 20, 40, 80 bots (charge du processus vue dans le panneau ou `top`) :
20 : ……  40 : ……  80 : ……

---

## 3. Phase 1 : mod chargé, cœur

Config : les 3 lignes de 1.2 seulement (valeurs par défaut : 60 bots au total, 20 par officier). Jouer **officier d'infanterie**.

### 3.1 Démarrage
| N° | But | Étapes | Attendu | Résultat |
|---|---|---|---|---|
| 1.1 | Chargement | Lancer l'instance, lire le journal | Dans l'ordre : `[CMD] Configuration : actif=True, maxbots=60, maxperofficer=20, …`, `[CMD] Commander 0.1.0 chargé.`, `[CMD] Boucle créée.`, puis `[CMD] Manche <n> sur <carte> (mode <m>).` | |
| 1.2 | Pas d'erreur en boucle | Laisser tourner 5 min sans rien faire | Aucun `[CMD-WARN]` ; au plus une ligne `[CMD] Charge : intervalle normal estimé …` | |
| 1.3 | Boucle active | `rc login <mdp>` puis `!cmd etat` dans le chat | Bandeau : `Commander : actif, 0 ligne(s) + 0 sans officier, 0/59 bots (1 joueurs ; 20 par officier), …` (≈59 avec 1 joueur) | |
| 1.4 | Variable inconnue | Ajouter `mod_variable CMD:testfaux:1`, redémarrer | `[CMD] Variable inconnue ignorée : testfaux`, le mod démarre quand même. Retirer la ligne | |

### 3.2 Levée de ligne (`!ligne`)
| N° | But | Étapes | Attendu | Résultat |
|---|---|---|---|---|
| 1.5 | Refus hors officier | Jouer simple soldat : `!ligne 5` | « Seul un officier d'infanterie en vie peut lever une ligne. » | |
| 1.6 | Aide | Officier : `!ligne` | Aide des commandes + liste des classes permises | |
| 1.7 | Levée simple | `!ligne 5` | « 5 hommes arrivent. Ligne : 5. » ; 5 bots apparaissent derrière vous en uniforme de ligne de votre faction ; journal `… lève 5 bots (ArmyLineInfantry, …) ; ligne 5, total 5/59.` | |
| 1.8 | Compléter | `!ligne 3` | « 3 hommes arrivent. Ligne : 8. » ; un porte-drapeau parmi eux (dès 8 hommes) | |
| 1.9 | Plafond par officier | `!ligne 30` | « 12 hommes arrivent (plafond : 12 sur 30). Ligne : 20. » ; un tambour (dès 20) | |
| 1.10 | Plafond atteint | `!ligne 5` | « Plafond atteint : votre ligne compte déjà 20 hommes. » | |
| 1.11 | Classe | `!renvoyer`, puis `!ligne 4 garde` | 4 gardes | |
| 1.12 | Classe inconnue | `!ligne 4 cavalerie` | « Classe inconnue : cavalerie. Classes : … » | |
| 1.13 | Classe non permise | `!ligne 4 Hussar` | Bots levés en ArmyLineInfantry + « Classe Hussar non permise : ArmyLineInfantry. » | |
| 1.14 | Anti-répétition | Taper `!moral` deux fois en moins d'1 s | La 2ᵉ est ignorée sans message | |
| 1.15 | Renvoyer | `!renvoyer` | « N hommes renvoyés : la ligne se disperse. » ; les bots s'enfuient puis disparaissent ~8 s plus tard | |
| 1.16 | Sans ligne | `!former` sans ligne | « Vous n'avez pas de ligne. » + aide | |

### 3.3 Formation (lever `!ligne 12` avant)
| N° | But | Étapes | Attendu | Résultat |
|---|---|---|---|---|
| 1.17 | Former devant soi | `!former` | « La ligne se forme devant vous. » ; ligne face à votre direction, 3 m devant, 2 rangs, ~1 m entre hommes | |
| 1.18 | Rangs | `!rangs 1`, puis `!rangs 2`, puis `!rangs` | « Rang simple. » / « Rang double. » / bascule ; la ligne se réorganise | |
| 1.19 | Suivre | `!suivre` puis marcher 30 m | « La ligne vous suit. » ; les bots suivent en groupe lâche autour de vous | |
| 1.20 | Pas | En marche (phrase `ForwardMarch`), `!pas` deux fois | « Pas de course. » puis « Pas ordinaire. » ; vitesse change | |
| 1.21 | Baïonnette | `!baionnette` deux fois | La plupart mettent puis retirent la baïonnette | |
| 1.22 | Accroupi | `!accroupi` deux fois | Accroupis puis debout | |
| 1.23 | Terrain | Former sur une pente, près d'un obstacle | Noter si des bots restent coincés ou flottent | |

### 3.4 Ordres du menu officier (roue d'ordres)
Pour chacun, vérifier aussi le journal : `[CMD] Ordre <Ordre> de <vous> (x, z, lacet …).`

| N° | Ordre | Étapes | Attendu | Résultat |
|---|---|---|---|---|
| 1.24 | Former la ligne | Ligne en suivi, viser un point à 20 m, ordre « Former la ligne » | La ligne se forme sur le marqueur, dans son orientation. **Noter** : sur le point visé ou sur vous ? | |
| 1.25 | Apprêtez-vous | Ordre « Apprêtez-vous / En joue » | Tous les soldats visent (léger décalage entre eux) | |
| 1.26 | Feu | Ordre « Feu » | Salve de toute la ligne (étalée sur ~0,4 s), puis rechargement | |
| 1.27 | Feu à volonté | Ordre « Feu à volonté », attendre 30 s | Tirs individuels, rechargement automatique | |
| 1.28 | Cessez-le-feu | Ordre « Cessez-le-feu » | Les tirs s'arrêtent, armes baissées | |
| 1.29 | Charge (devant) | Ordre « Charge » sans viser loin | Baïonnette au canon, la ligne court ~40 m droit devant, quelques cris | |
| 1.30 | Charge (point visé) | Re-former, ordre « Charge » en visant un point à plus de 5 m | La ligne charge vers ce point en gardant sa largeur | |
| 1.31 | Rompre et suivre | Ordre « Rompre les rangs et suivre » | La ligne vous suit | |
| 1.32 | Renfort | Ordre « Renfort » | Voir lot A (A.9) ; en phase 1 : message de renforts | |
| 1.33 | Cris des bots ignorés | Après 1.26, chercher dans le journal | Aucune ligne `Phrase … de Soldat …` : seuls vos ordres apparaissent | |

### 3.5 Phrases vocales (menu vocal)
Le jeu limite à **5 phrases par 30 s** : faire les tests par paquets de 5, puis attendre 30 s. Pour chaque phrase, le journal doit montrer
`[CMD] Phrase <Phrase> de <vous> → ligne <mode>.` **Noter aussi si la phrase n'existe pas dans votre menu** (selon la voix de l'uniforme).

| N° | Phrase | Attendu | Dispo au menu PC ? | Résultat |
|---|---|---|---|---|
| 1.34 | `Follow` | La ligne vous suit | | |
| 1.35 | `Halt` | Halte (en ligne : reste formée ; sinon chacun s'arrête) | | |
| 1.36 | `ForwardMarch` | La ligne se forme si besoin et avance droit devant, tambour joue | | |
| 1.37 | `AtTheDouble` | En marche : pas de course ; sinon course pour le prochain mouvement | | |
| 1.38 | `FormColumn` | Colonne derrière vous | | |
| 1.39 | `DoubleRank` | Rang double | | |
| 1.40 | `SingleRank` | Rang simple | | |
| 1.41 | `CloseRanks` | Après avoir tué 3 bots (`rc serverAdmin slay <id>`) : les vides se referment | | |
| 1.42 | `RemoveBayonet` | Bascule baïonnette (mise si la plupart ne l'ont pas) | | |
| 1.43 | `GetDown` | Bascule accroupi / debout | | |
| 1.44 | `TakeAim` | Viser | | |
| 1.45 | `MakeReady` | Viser (phrase de la catégorie canons : noter si absente) | | |
| 1.46 | `FirstRankFire` | Seul le 1ᵉʳ rang tire | | |
| 1.47 | `SecondRankFire` | Seul le 2ᵉ rang tire | | |
| 1.48 | `Fire` | Salve de toute la ligne | | |
| 1.49 | `FireAtWill` | Feu à volonté | | |
| 1.50 | `CeaseFire` | Cessez-le-feu | | |
| 1.51 | `Charge` | Charge droit devant | | |
| 1.52 | `StandGround` | Halte (+ ralliement, voir lot A) | | |
| 1.53 | `Retreat` | La ligne se reforme 20 m en arrière, même orientation | | |
| 1.54 | `Reinforce` | Renforts (voir lot A) | | |
| 1.55 | `PresentArms` | Carré (voir lot B) | | |
| 1.56 | Anti-doublon | Ajouter `mod_variable CMD:dedupwindow:5`, redémarrer ; ordre « Charge » au menu puis crier `Charge` dans les 5 s | Journal : `Phrase Charge de <vous> ignorée (suit l'ordre du menu).` Retirer la ligne ensuite | |

### 3.6 Mort de l'officier, départ, fin de manche
Pour 1.57 et 1.58, mettre `mod_variable CMD:succession:false` (la succession se teste au lot A), redémarrer.

| N° | But | Étapes | Attendu | Résultat |
|---|---|---|---|---|
| 1.57 | Mort de l'officier | `!ligne 10`, puis `rc serverAdmin slay <moi>` | Bandeau pour tous : « L'officier <vous> est tombé : sa ligne se disperse. » ; bots s'enfuient, disparaissent ~8 s après ; journal `Fin de la ligne de <vous> (tombé) : 10 bots.` | |
| 1.58 | Départ de l'officier | `!ligne 10`, puis quitter le serveur ; revenir et lire le journal | `Fin de la ligne de <vous> (parti)`, pas de bandeau ; plus aucun bot du mod après ~10 s (`rc playerlist`) | |
| 1.59 | Bot mort | `rc serverAdmin slay <id>` sur un bot | Le bot reste au sol, la ligne se réorganise sans lui ; pas de kick (rien dans le journal admin / Discord) | |
| 1.60 | Fin de manche | Ligne de 10, laisser la manche finir (ou changer de carte depuis le panneau) | `[CMD] Nettoyage (fin de manche) : …`, puis `[CMD] Manche <n+1> …` ; aucun bot du mod dans la nouvelle manche | |
| 1.61 | Après fin de manche | Nouvelle manche : `!ligne 5` | Fonctionne normalement | |

### 3.7 Commandes admin
Après `rc login <mdp>`. Réponse en bandeau.

| N° | Commande | Attendu | Résultat |
|---|---|---|---|
| 1.62 | `!cmd etat` (ou `!cmd`) | État : lignes, bots/plafond, en attente, charge | |
| 1.63 | `!cmd config` | Tous les réglages (`actif=True, maxbots=60, …`) | |
| 1.64 | `!cmd max 8`, puis `!ligne 12` | « Plafond global : 8 … » ; levée limitée à 8 (ou message de plafond serveur) | |
| 1.65 | `!cmd maxofficier 3`, puis `!ligne 5` | « Plafond par officier : 3. » ; levée limitée à 3 | |
| 1.66 | `!cmd off` | « Lignes de bots désactivées, bots supprimés. » ; `!ligne 5` → « Les lignes de bots sont désactivées sur ce serveur. » | |
| 1.67 | `!cmd on` | « Lignes de bots activées. » ; `!ligne 5` refonctionne | |
| 1.68 | `!cmd vider` | « Tous les bots du mod sont supprimés. » ; plus de bots dans `rc playerlist` | |
| 1.69 | `!cmd truc` | Liste des sous-commandes | |
| 1.70 | `rc cmd etat` dans la console admin | Même réponse que `!cmd etat` | |
| 1.71 | Non admin : se reconnecter sans `rc login`, `!cmd etat` | « Commande réservée aux admins du serveur. » | |
| 1.72 | `!cmd max 300` | « !cmd max <0 à 250> », rien ne change | |

Remettre `!cmd max 60` et `!cmd maxofficier 20` (ou redémarrer) avant la suite.

---

## 4. Lot A : succession, moral, renforts, remplissage

Retirer `CMD:succession:false`. Besoin d'un **2ᵉ compte** (PC ou console) pour A.3, A.4, A.8 et A.11.

| N° | But | Étapes | Attendu | Résultat |
|---|---|---|---|---|
| A.1 | Succession | `!ligne 10`, `!former`, puis `rc serverAdmin slay <moi>` | Bandeau « L'officier <vous> est tombé : un sergent tient sa ligne. » ; la ligne recule de ~15 m et tire à volonté ; journal `le sergent bot #… tient sa ligne` | |
| A.2 | Pas de reprise | Ne rien faire pendant 20 s | Journal `Personne n'a repris la ligne de <vous> : dispersion.` ; la ligne se disperse | |
| A.3 | Reprise par `!reprendre` | A.1, puis le 2ᵉ compte (officier, même camp) approche à moins de 10 m et tape `!reprendre` | Il reçoit l'avis de ligne sans officier, puis « Vous prenez la ligne de <vous> : N hommes. » ; la ligne s'arrête | |
| A.4 | Reprise par phrase | Idem, 2ᵉ compte crie `Follow` (ou `StandGround`) près de la ligne | Reprise ; avec Follow la ligne le suit | |
| A.5 | Reprise refusée | `!reprendre` loin de toute ligne / en ayant déjà une ligne | « Aucune ligne sans officier à moins de 10 m. » / « Vous commandez déjà une ligne. » | |
| A.6 | Trop petite pour succession | `!ligne 4`, `rc serverAdmin slay <moi>` | Dispersion directe (moins de 6 soldats) | |
| A.7 | Moral : pertes | `!ligne 10`, `!moral`, puis tuer des bots un par un (`rc serverAdmin slay <id>`) en tapant `!moral` entre chaque | Départ « Moral : 100/100 (bon) » ; baisse d'environ 8 par mort ; sous 40 « ébranlé, tir moins précis » ; sous 20 message « Votre ligne flanche et recule … » et recul ; à 0 « Votre ligne est en déroute et se disperse. » | |
| A.8 | Ralliement `StandGround` | Moral bas (A.7), crier `StandGround` | « La ligne se rallie. Moral : …(+25) » ; recrier tout de suite → pas de nouveau ralliement (recharge 60 s, visible dans `!moral`) | |
| A.9 | Ralliement par un sergent joueur | 2ᵉ compte en sergent près de la ligne, ordre de ralliement du menu | Journal `Ralliement de la ligne de <vous> (sergent …)` ou `(buff SergeantLineRally)` | |
| A.10 | Remontée | Moral bas, ne plus être touché 10 s, attendre 30 s | `!moral` remonte d'environ 1 par seconde | |
| A.11 | Renforts | `!ligne 10`, ordre « Renfort » (ou `!renforts`, ou phrase `Reinforce`) | « 5 hommes en renfort arrivent par l'arrière. » ; ils apparaissent ~30 m derrière vous et rejoignent la ligne à pied | |
| A.12 | Recharge des renforts | Redemander tout de suite | « Renforts pas encore prêts : encore N s. » (90 s) | |
| A.13 | Renforts au complet | Ligne de 20, renforts après 90 s | « Pas de renforts : votre ligne est au complet. » | |
| A.14 | Remplissage | Config : `mod_variable CMD:fillfull:2`, `mod_variable CMD:fillmin:5`, `mod_variable CMD:fillmax:30`, redémarrer. Seul : `!cmd etat`, `!ligne 20` ; puis connecter le 2ᵉ compte | Seul : plafond ≈18. 2ᵉ compte arrivé : journal `Plafond de bots : 18 → 5 …` ; puis un bot retiré toutes les 10 s (message « Plafond de bots abaissé … ») jusqu'à 10 bots (5 + marge 5) | |
| A.15 | Remontée du plafond | Le 2ᵉ compte quitte | Plafond revient à ≈18 ; `!ligne 5` de nouveau possible. Retirer les 3 lignes `fill…` | |

---

## 5. Lot B : uniformes, carré, plafond selon la charge

| N° | But | Étapes | Attendu | Résultat |
|---|---|---|---|---|
| B.1 | Liste courte | `!skins` | « <Faction> : … » avec les plages d'uniformes par classe | |
| B.2 | Liste d'une classe | `!skins garde` | Noms des uniformes, en plusieurs messages si long | |
| B.3 | Choisir un uniforme | `!skin garde <id valide>`, puis `!ligne 4 garde` | « Guard : vos prochains bots porteront l'uniforme … » ; les 4 gardes le portent | |
| B.4 | Uniforme à la levée | `!renvoyer`, `!ligne 4 ligne <id valide>` | Bots dans cet uniforme ; les renforts suivants aussi | |
| B.5 | Uniforme invalide | `!skin garde 9999` ; `!ligne 4 ligne 9999` | « Uniforme 9999 inconnu pour … » | |
| B.6 | Retour au défaut | `!skin garde 0` | « Guard : uniforme par défaut. » | |
| B.7 | Défaut serveur | Config `mod_variable CMD:skin:British:Guard:<id>` (votre faction), redémarrer, `!ligne 4 garde` | Uniforme imposé par le serveur ; un id faux donne `[CMD] skin : uniforme … inconnu …, ignoré.` | |
| B.8 | Carré par chat | `!ligne 20`, `!carre` | « Formez le carré ! … » ; 4 faces autour de vous, baïonnette au canon ; à 16+ soldats, rang extérieur accroupi | |
| B.9 | Carré par phrase | Crier `PresentArms` | Carré | |
| B.10 | Halte en carré | En carré, crier `Halt` | Le carré reste | |
| B.11 | Tir du carré | En carré, `rc carbonPlayers spawnSpecific <adverse> ArmyLineInfantry "CMD Ennemi" x 1` puis `rc teleport <id> <x,y,z à ~30 m>` ; puis un `Hussar` ennemi à ~50 m | La face concernée tire sur l'ennemi (infanterie ≤ 40 m, cavalerie ≤ 60 m) | |
| B.12 | Sortie du carré | `!former` (ou ordre « Former la ligne », ou `!ligne` sans nombre) | La ligne se reforme, hommes debout | |
| B.13 | Carré désactivé | Config `mod_variable CMD:carre:false`, `!carre` | « Le carré est désactivé sur ce serveur. » ; `PresentArms` sans effet | |
| B.14 | Mesure de charge | Après 1 min, `!cmd etat` | Fin du message : `image X ms (seuil Y), mod Z ms, plafond de charge aucun` ; noter X, Y, Z | |
| B.15 | Plafond de charge forcé | Config `mod_variable CMD:chargeseuil:1`, redémarrer, `!ligne 20` | Journal `Charge : image … → plafond de charge …` en baisse par pas de 5 jusqu'à 10 ; bots retirés un par un. Retirer la ligne ensuite | |
| B.16 | Charge désactivée | Config `mod_variable CMD:charge:false` | `!cmd etat` : « charge non surveillée » | |

---

## 6. Lot C : lignes ennemies IA (prévu, pas encore écrit)

À préciser quand le code sera fini (variables exactes, messages). D'après PLAN.md n° 12 : lignes de bots menées par l'IA dans l'autre camp,
**seulement s'il n'y a aucun joueur en face**, retirées quand de vrais joueurs arrivent. Variable prévue : `CMD:lignesia`.

| N° | But | Étapes | Attendu | Résultat |
|---|---|---|---|---|
| C.0 | Variable réservée (avant le lot C) | Config `mod_variable CMD:lignesia:true` sur la version actuelle | `[CMD] Variable lignesia prévue pour un lot suivant, ignorée.` | |
| C.1 | Désactivé par défaut | Sans la variable, seul en jeu | Aucune ligne IA | |
| C.2 | Apparition | Variable activée, seul en jeu, camp d'en face vide | Une ou des lignes IA apparaissent en face ; journal `[CMD]` qui l'indique | |
| C.3 | Comportement | Observer une ligne IA, s'en approcher avec sa ligne | Elle se forme, avance, tire, éventuellement charge ; ses ordres (bot officier) ne sont pas pris pour les vôtres dans le journal | |
| C.4 | Plafond | `!cmd etat` avec lignes IA + votre ligne | Le total ne dépasse pas le plafond ; vos levées restent possibles (à préciser) | |
| C.5 | Retrait à l'arrivée d'un joueur | 2ᵉ compte rejoint le camp d'en face | Les lignes IA se retirent | |
| C.6 | Retour | Le 2ᵉ compte part | Les lignes IA reviennent (si prévu) | |
| C.7 | Fin de manche | Laisser finir la manche | Nettoyage, rien ne reste | |
| C.8 | Console | Refaire C.2 avec un compte console connecté | Serveur toujours joignable, lignes IA visibles | |

---

## 7. Compatibilité console (Xbox / PlayStation)

Avec le mod chargé (config de la phase 1). Un testeur sur console, vous sur PC pour lire le journal et taper `rc`.

| N° | But | Étapes | Attendu | Résultat (Xbox / PS) |
|---|---|---|---|---|
| X.1 | Serveur visible | Chercher l'instance dans la liste des serveurs sur console | Visible, sans mention de mod requis | |
| X.2 | Entrée | Rejoindre | Entrée sans téléchargement ni erreur | |
| X.3 | Bandeaux | Vous (PC) : `rc broadcast Test bandeau` puis `rc serverAdmin privateMessage <id console> Test prive` | Les deux sont lus sur console | |
| X.4 | Chat console | Testeur officier : `!ligne 5` (si le chat est disponible) | Ligne levée ; journal `Commande de <testeur> : !ligne 5`. Si pas de chat sur console : noter KO (déclencheur sans chat à prévoir) | |
| X.5 | Roue d'ordres | Testeur : « Former la ligne », « Feu », « Charge », « Rompre et suivre » à la roue | Ligne obéit ; journal `Ordre … de <testeur>` | |
| X.6 | Phrases vocales | Testeur : `Halt`, `ForwardMarch`, `FormColumn`, `StandGround`, `Retreat`, `PresentArms` | Ligne obéit ; **noter la liste des phrases présentes dans le menu console** | |
| X.7 | Réponses en bandeau | Testeur : ordre « Renfort » deux fois | Le 2ᵉ message « Renforts pas encore prêts … » est lu sur console | |
| X.8 | Mort de l'officier console | `rc serverAdmin slay <id console>` | Bandeau vu par tous, succession ou dispersion comme au lot A | |
| X.9 | `teleport` / `playSong` | `rc teleport <id console> <moi>` ; `rc playSong <id console> <songId>` | Téléporté ; morceau entendu ? | |

---

## 8. Charge (20 / 40 / 60 bots)

Config : ajouter `mod_variable CMD:fillfull:0` (sinon le plafond descend avec le nombre de joueurs), redémarrer.
Préparer : `rc login <mdp>`, `!cmd maxofficier 60`, `!cmd max 60`. À chaque palier : attendre 2 min avec la ligne en **feu à volonté**,
puis `!cmd etat` et relever `image … ms (seuil …), mod … ms, plafond de charge …`, ainsi que la charge processeur de l'instance (panneau ou `top`).

| N° | Palier | Étapes | Image (ms) | Seuil (ms) | Mod (ms) | Plafond de charge | CPU | `[CMD-WARN]` ? | Ressenti (saccades, bots figés) | Résultat |
|---|---|---|---|---|---|---|---|---|---|---|
| L.0 | 0 bot | `!cmd etat` | | | | | | | | |
| L.1 | 20 | `!ligne 20`, ordre « Former la ligne », « Feu à volonté » | | | | | | | | |
| L.2 | 40 | `!ligne 20` (ligne 40), reformer, feu à volonté | | | | | | | | |
| L.3 | 60 | `!ligne 20` (ligne 60), reformer, feu à volonté | | | | | | | | |
| L.4 | 60 en marche | `ForwardMarch` puis `Charge` | | | | | | | | |
| L.5 | Retour | `!cmd vider`, attendre 1 min | | | | | | | | |

Attendu : pas de `[CMD-WARN]` en boucle ; si l'image dépasse le seuil, le journal montre `Charge : … → plafond de charge …` et le mod
retire des bots jusqu'à ce que ça redescende. Noter le palier à partir duquel le jeu devient gênant : il fixera `CMD:maxbots`.
Retirer `CMD:fillfull:0` et redémarrer à la fin.

---

## 9. Bilan
- Tests KO (numéros) : ……
- Phrases vocales présentes au menu officier, PC : …… / console : ……
- « Former la ligne » : ligne posée sur le point visé ou sur l'officier : ……
- Palier de bots confortable : ……
- Lignes du journal à transmettre (copier les `[CMD-WARN]`) : ……
