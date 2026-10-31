# Guide du joueur : Commander

Sur ce serveur, un **officier** peut lever sa propre **ligne de soldats** (des bots) et la commander.
Rien à installer : le mod tourne sur le serveur. Il marche sur PC, Xbox et PlayStation.

> Le mod n'a pas encore été testé en jeu : certains détails peuvent changer après les premiers essais.

**Sur console**, tout se fait avec la roue d'ordres et les phrases vocales, y compris **lever une ligne** : un officier sans
ligne qui donne l'ordre **Renfort** (ou crie **Reinforce**) lève une ligne par défaut (voir 1.1). Seuls demandent le chat :
renvoyer la ligne (`!renvoyer`), voir le moral (`!moral`), choisir les uniformes (`!skin`, `!skins`) et quelques bascules (`!pas`).
Si votre console vous laisse écrire dans le chat, les commandes marchent comme sur PC. Sans chat, la ligne disparaît de toute
façon à votre mort (ou après la succession) et en fin de manche.

Les réponses du mod arrivent en **message privé** (bandeau à l'écran et chat), visibles aussi sur console.

---

## 1. Officiers

### 1.1 Lever une ligne

Il faut être **officier d'infanterie** (`ArmyInfantryOfficer`), **en vie**. Tapez dans le chat :

```
!ligne <nombre> [préfixe] [classe] [uniforme]
```

- `!ligne 20` : 20 soldats de la classe par défaut (en général l'infanterie de ligne).
- `!ligne 15 garde` : 15 gardes.
- `!ligne 12 grenadier 2` : 12 grenadiers avec l'uniforme n° 2 (`0` : uniforme par défaut).
- `!ligne 20 GDE` ou `!ligne GDE 20` : 20 soldats dont le nom commence par « GDE » (préfixe de 1 à 10 lettres ou chiffres) ;
  `!ligne 20 GDE 2` : avec l'uniforme 2. L'ordre des mots est libre : le premier nombre est l'effectif, le second l'uniforme.

Les soldats portent des noms de personnes de leur faction (Jean Martin, John Smith…), sans doublon dans la ligne.

**Sans chat** (console) : l'ordre **Renfort** de la roue, ou la phrase **Reinforce**, donné par un officier qui n'a **pas encore de
ligne**, lève une ligne par défaut (12 hommes de la classe par défaut). Quand vous avez déjà une ligne, Renfort demande des renforts.

Classes acceptées (selon ce que l'admin autorise, parmi les classes de ligne `ArmyLineInfantry`, `Guard`, `Grenadier`,
`LightInfantry`, `Rifleman`, `Sergeant`) : le nom du jeu, un nom court (`ligne`, `garde`, `grenadier`, `legere`, `carabinier`,
`sergent`) ou le numéro de la classe.
Si la classe n'est pas permise, le mod prend la classe par défaut et vous le dit.

À savoir :
- Le nombre est limité par un **plafond par officier** et par un **plafond du serveur**. Ce dernier baisse quand le serveur se
  remplit de vrais joueurs ou quand il peine. Si vous demandez trop, vous recevez ce qui reste et le mod vous le dit.
- Taper `!ligne` de nouveau **complète** votre ligne (jusqu'au plafond).
- Les soldats apparaissent un par un, juste derrière vous (c'est le seul moment où le mod les déplace d'un coup ; ensuite ils
  marchent toujours jusqu'à leur place).
- Si le jeu refuse l'apparition d'un soldat (places de classe pleines dans les sections d'apparition), le reste de la levée est
  abandonné, vous êtes prévenu, et vos levées et renforts sont en pause 30 s.
- Une ligne assez grande reçoit un **porte-drapeau** (dès 8 hommes par défaut) et un **tambour** (dès 20), pris sur le nombre demandé.
  En ligne, le drapeau est à une extrémité du premier rang et le tambour à l'autre ; en colonne, ils marchent en tête.
  Le tambour (ou le fifre) joue en marche et aussi ligne formée, à la halte et au carré ; il se tait pendant la charge et le feu
  à volonté (réglage du serveur). Les morceaux sont tirés au hasard et s'enchaînent ; `!musique` fait taire ou rejouer, `!musique <n°>` fixe un morceau,
  `!musique hasard` revient au hasard.
- Pas de ligne pour les factions Privateer et Spanish.
- `!ligne` sans nombre affiche l'aide et la liste des classes permises.

### 1.2 Commander depuis la roue d'ordres (PC et console)

Les ordres d'officier du jeu (la roue d'ordres) commandent votre ligne :

| Ordre de la roue | Ce que fait la ligne |
|---|---|
| **Former la ligne** | Se forme sur le trait de l'ordre, face à sa direction : chacun garde sa position relative (pas de croisements), court vers sa place si elle est à plus de 4 m puis marche. Les absents ne laissent pas de trou. Sort aussi du carré et de la colonne. |
| **Apprêtez-vous** (Make Ready) | Les soldats épaulent et visent le point que vous regardez au moment de l'ordre (gardé pour la salve et le feu à volonté ; redonnez l'ordre pour changer de point). |
| **Feu** | Salve de toute la ligne. |
| **Feu à volonté** | Chacun tire et recharge seul, jusqu'à l'ordre de cesser le feu. |
| **Cessez-le-feu** | Arrêt du tir, armes baissées. |
| **Charge** | Baïonnette au canon, la ligne court vers le point visé (ou droit devant si vous visez près de vous). |
| **Rompre et suivre** | La ligne vous suit **en colonne** (2 de front par défaut), arme à l'épaule, en suivant votre chemin ; elle marche quand vous marchez, court quand vous courez, s'arrête quand vous vous arrêtez. « Former la ligne » la redéploie en ligne. |
| **Émote** (geste, par exemple la touche de geste rapide) | Bascule : la ligne vous suit en colonne, ou fait halte si elle vous suivait déjà (un appui = une action). Marche aussi sur console avec les gestes. L'admin peut limiter les gestes concernés (`gestesuivre`). |
| **Renfort** | Sans ligne : lève une ligne par défaut (voir 1.1). Avec une ligne : demande des renforts (voir 1.7). |

### 1.3 Les phrases vocales

Le menu des **phrases vocales** du jeu donne les autres ordres. Il marche aussi sur console.
Attention : le jeu limite à **5 phrases toutes les 30 secondes**, on ne peut pas les enchaîner trop vite.

| Phrase | Ce que fait la ligne |
|---|---|
| **Follow** | Vous suit en colonne (comme « Rompre et suivre »). |
| **Halt** | Halte. En ligne, elle reste formée. Hors formation, chacun s'arrête où il est. |
| **Forward March** | En avant : la ligne se forme devant vous si besoin, puis avance droit devant. Le tambour joue. |
| **At The Double** | Pas de course (si la ligne marche, elle court ; sinon, elle courra à la prochaine marche). |
| **Form Column** | Colonne derrière vous, qui vous suit (comme « Rompre et suivre »). |
| **Double Rank** / **Single Rank** | Deux rangs / un seul rang. |
| **Insult** | Bascule rang simple / rang double (raccourci que l'on peut assigner dans les contrôles du jeu). |
| **Close Ranks** | Serrer les rangs : les vides laissés par les morts se referment. |
| **Remove Bayonet** | Baïonnette : met la baïonnette si la plupart ne l'ont pas, l'enlève sinon. |
| **Get Down** | Accroupi / debout (bascule). |
| **Take Aim** / **Make Ready** | Viser. |
| **First Rank Fire** / **Second Rank Fire** | Salve du premier / du second rang seulement. |
| **Fire** | Salve de toute la ligne. |
| **Fire At Will** | Feu à volonté. |
| **Cease Fire** | Cessez-le-feu. |
| **Charge** | Charge droit devant. |
| **Stand Ground** | Tenez bon : halte, et **ralliement** si possible (remonte le moral, voir 1.5). |
| **Retreat** | Retraite : la ligne se reforme 20 m en arrière, face à la même direction (formée d'abord devant vous si elle vous suivait). |
| **Reinforce** | Comme l'ordre Renfort de la roue (levée par défaut sans ligne, renforts avec une ligne). |
| **Present Arms** | **Former le carré** (voir 1.8). |

Si vous donnez un ordre avec la roue et criez juste après la phrase qui va avec, l'ordre n'est pas exécuté deux fois.

### 1.4 Les commandes du chat

| Commande | Effet |
|---|---|
| `!ligne <nombre> [préfixe] [classe] [uniforme]` | Lever ou compléter une ligne (`0` comme uniforme : uniforme par défaut). En carré, `!ligne` seul fait reformer la ligne. |
| `!former` | La ligne se forme 3 m devant vous, face à votre direction. Sort du carré. |
| `!suivre` | La ligne vous suit en colonne. |
| `!rangs` | Passe d'un rang à deux, ou de deux à un. `!rangs 1` ou `!rangs 2` pour choisir. |
| `!carre` (ou `!carré`) | Former le carré autour de vous. |
| `!baionnette` (ou `!baïonnette`) | Mettre / enlever la baïonnette (bascule). Les soldats apparaissent baïonnette au canon (réglage du serveur) : le premier `!baionnette` l'enlève. |
| `!accroupi` | Accroupi / debout (bascule). |
| `!pas` | Pas ordinaire / pas de course (bascule). |
| `!renforts` | Demander des renforts. |
| `!musique [n°\|hasard]` | Musiciens : jouer / se taire (bascule) ; avec un numéro, morceau fixe (s'il existe pour l'instrument) ; `hasard` : morceaux tirés au hasard (défaut). |
| `!moral` | Voir le moral de la ligne et quand le prochain ralliement est possible. |
| `!reprendre` | Prendre une ligne restée sans officier, près de vous (voir 1.6). |
| `!skins [classe]` | Uniformes possibles pour votre faction (avec une classe : leurs noms). |
| `!skin <classe> <uniforme>` | Uniforme de vos prochains soldats de cette classe. `0` remet l'uniforme par défaut. |
| `!renvoyer` | Renvoyer la ligne : elle se disperse puis disparaît. |

Une commande par seconde au plus. Une commande inconnue est ignorée.

### 1.5 Le moral

Chaque ligne a un **moral de 0 à 100**. Il commence à 100.

- **Il baisse** quand des soldats tombent (plus la ligne est petite, plus chaque perte compte), un peu à chaque soldat blessé,
  et beaucoup si **vous mourez**.
- **Il remonte** doucement quand la ligne n'est plus touchée depuis quelques secondes, et d'un coup au **ralliement**.
- **Effets** :
  - moral **bas** (sous 40 par défaut) : la ligne tire moins bien (plus lente, moins précise) ;
  - moral **très bas** (sous 20) : la ligne **flanche** et recule (15 m), puis encore toutes les 20 s tant que ça ne va pas mieux ;
    un carré reste en carré et se reforme en arrière. Vous êtes prévenu une fois, jusqu'au prochain ralliement ;
  - moral à **0** : **déroute**, la ligne se disperse.
- **Rallier** : criez **Stand Ground**. Un sergent joueur peut aussi rallier (section 2). Il faut attendre un délai entre deux
  ralliements (60 s par défaut). `!moral` dit où vous en êtes.

### 1.6 Si l'officier tombe : la succession

Si vous mourez ou quittez la partie et que votre ligne compte encore au moins un soldat :
- un **sergent** bot (classe Sergeant, à la place d'un soldat) prend la ligne : elle recule de quelques mètres, en ordre, et tire à volonté ;
- si vous êtes mort et que vous réapparaissez **en officier d'infanterie dans la même faction** avant la fin du délai, vous
  **retrouvez votre ligne automatiquement** (« Vous reprenez votre ligne (N hommes) ») ; elle s'arrête où elle est, donnez
  « Rompre et suivre » pour qu'elle vous rejoigne ;
- si vous réapparaissez dans une autre classe, la ligne reste au sergent : en sergent (ou en redevenant officier), `!reprendre`
  la reprend à n'importe quelle distance ;
- pendant tout le délai, vous êtes prioritaire : personne d'autre ne peut la reprendre tant que vous êtes sur le serveur ;
- si vous avez quitté la partie, les officiers de votre camp qui sont à moins de 100 m reçoivent un message ;
- si vous êtes parti, un officier ou un sergent **sans ligne** qui s'approche (10 m par défaut) peut la **reprendre** : `!reprendre`, ou crier
  **Follow** (la ligne le suit) ou **Stand Ground** (la ligne s'arrête) ;
- si personne ne la reprend à temps (60 s par défaut), elle se disperse.

Si vous la reprenez et qu'elle dépasse votre plafond, les soldats les plus éloignés s'en vont.
Une ligne trop petite, ou en déroute, se disperse tout de suite à la mort de son officier.

Si la ligne a été dispersée pendant votre absence, un message vous le dit à la réapparition : `!ligne` pour en lever une nouvelle. Un sergent qui
a repris une ligne la commande avec sa propre roue d'ordres (former la ligne, feu, feu à volonté, suivre, apprêtez-vous, cessez-le-feu).

### 1.6 bis Soldats bloqués

Les soldats marchent toujours : ils ne sont jamais téléportés pour se mettre en ligne, suivre ou rattraper. Un soldat bloqué
(rocher, pente, autre soldat) fait de courts détours ; s'il n'y arrive toujours pas, il reste où il est jusqu'au prochain ordre
et la ligne se resserre sans lui.

### 1.7 Les renforts

L'ordre **Renfort** de la roue, la phrase **Reinforce** ou `!renforts` font venir quelques soldats (5 par défaut),
de la même classe que votre dernière levée. Ils arrivent **par l'arrière** (30 m derrière vous) et rejoignent la ligne à pied.
Il faut attendre entre deux renforts (90 s par défaut), et les plafonds s'appliquent toujours.

### 1.8 Le carré

Contre la cavalerie : `!carre` ou la phrase **Present Arms**.
- La ligne forme un carré **autour de vous**, une face dans votre direction ; porte-drapeau et tambour au centre.
- Baïonnette au canon. Une grande ligne (16 soldats par défaut) fait **2 rangs** par face, le rang extérieur à genoux.
- Chaque soldat **tire seul** sur l'ennemi qui approche de sa face : un peu plus loin pour la cavalerie (60 m) que pour l'infanterie (40 m).
- **Halt** ne fait pas sortir du carré. Pour en sortir : **Former la ligne**, `!former`, `!ligne`, **Follow**, **Forward March**,
  **Form Column**, **Retreat** ou **Charge**.

### 1.9 Les uniformes

- `!skins` montre les uniformes connus pour votre faction, classe par classe (par exemple `ArmyLineInfantry 1-4`).
- `!skins garde` donne leurs noms.
- `!skin garde 2` : vos **prochains** gardes porteront l'uniforme 2 (les renforts aussi). `!skin garde 0` : retour au défaut.
- Vous pouvez aussi donner l'uniforme à la levée : `!ligne 20 garde 2`.

### 1.10 Fin de la ligne

La ligne se disperse quand vous la renvoyez (`!renvoyer`), en cas de déroute, ou quand vous mourez sans succession. Après quelques
secondes, les soldats dispersés restent immobiles où ils sont (ou sont tués, selon le réglage du serveur) ; ils sont retirés
peu à peu si le serveur a besoin de place. Tous les bots du mod disparaissent à la fin de la manche.

Quand le plafond de bots baisse (serveur qui se remplit ou qui peine), des soldats quittent les lignes peu à peu ; vous êtes
prévenu au premier départ.

### 1.11 Lignes ennemies menées par l'IA

Un admin peut aussi en faire apparaître une tout de suite, même face à des joueurs : `!cmd ia lever` (voir CONFIG.md). Ces lignes
« forcées » ne se retirent pas quand des joueurs rejoignent leur camp ; elles finissent normalement ou sur `!cmd ia retirer`.

Si l'admin les active, un camp **sans aucun vrai joueur** face à un camp qui joue reçoit des lignes ennemies menées par un
officier bot : elles se forment près de leurs points d'apparition, avancent vers le groupe de joueurs le plus proche, s'arrêtent
pour tirer en salves et chargent de près. Annonce « Une ligne ennemie approche » à la première vague. Dès qu'un vrai joueur
rejoint ce camp, elles se retirent (les bots disparaissent un par un). Les lignes des joueurs passent toujours avant : si la place
manque pour votre levée ou vos renforts, des bots ennemis IA sont retirés pour vous la faire.

---

## 2. Sergents

Un **sergent joueur** peut remonter le moral des lignes de son camp : utilisez votre **ordre de ralliement** de sergent
près d'une ligne (25 m par défaut). La ligne se rallie, comme avec Stand Ground, avec le même délai entre deux ralliements.

---

## 3. Joueurs sans ligne

- Les soldats bots portent des noms comme « Soldat 3 », « Porte-drapeau », « Tambour », avec le tag de régiment de leur officier.
- Ils comptent comme des ennemis ou des alliés normaux : on peut les tuer, ils peuvent vous tuer.
- Le nombre de bots baisse quand le serveur se remplit de vrais joueurs : plus il y a de monde, moins il y a de bots.
- Vous voulez commander ? Prenez la classe **officier** et tapez `!ligne 20` (sur console : ordre **Renfort**).
