# Réglages du mod Commander (admins)

Référence de toutes les variables lues par `Scripts/CommanderConfig.cs` (état du code au 31/10/2026 : cœur, lots A, B et C,
avec les corrections de la relecture). Elle sert aussi de base à la section Commander du panneau web.

## Syntaxe

Une ligne par réglage dans le serverconfig, après `load_mod_server_only <ID>` :

```
mod_variable CMD:<clé>:<valeur>
```

- **Préfixe** `CMD` (insensible à la casse ; « à confirmer » dans `docs/PLAN.md`).
- **Clé** insensible à la casse. Plusieurs clés ont un **alias** (nom anglais et nom français) : les deux font la même chose.
  Préférer les clés sans accent (`carre` plutôt que `carré`).
- **Booléen** : `1`, `true`, `yes`, `on`, `oui` / `0`, `false`, `no`, `off`, `non`.
- **Entier** : nombre sans décimale. **Réel** : point ou virgule décimale (`1.5` ou `1,5`).
- Une valeur **hors bornes ou invalide est ignorée** (le défaut ou la valeur précédente reste) et notée dans le journal `[CMD]`.
- L'**ordre des lignes est libre** : la cohérence est vérifiée une fois tout lu (`fillmin` ≤ `fillmax`, `drummin` ≥ `auxmin`,
  classes de ligne, uniformes `skin`) ; chaque correction est notée en `[CMD-WARN]`.
- Une clé inconnue est ignorée et notée dans le journal.
- Ne jamais mettre `#` dans une valeur : dans le serverconfig, `#` commence un commentaire.
- Les réglages sont lus au chargement du mod (`PassConfigVariables`). Seuls `actif`, `maxbots`, `maxperofficer` et `lignesia`
  peuvent être changés en jeu, par les commandes admin, jusqu'au redémarrage.
- Le réglage complet en vigueur s'affiche avec `!cmd config` et dans le journal au démarrage (`Configuration : …`).

Colonnes des tableaux : **clé** (alias), **type**, **défaut**, **bornes** (incluses), **effet**.

---

## 1. Général

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `enabled` | `actif` | booléen | `true` | | Active le mod (levées, ordres). Peut être changé en jeu par `!cmd on` / `!cmd off`. |
| `maxbots` | `plafond` | entier | `60` | 0 à 250 | Plafond de bots du mod sur tout le serveur, toutes lignes confondues. Plafond de tous les autres plafonds. Changé en jeu par `!cmd max`. |
| `maxperofficer` | `plafondofficier` | entier | `20` | 1 à 100 | Plafond de bots par officier. Changé en jeu par `!cmd maxofficier`. |
| `kickdead` | | booléen | `false` | | Retire (kick) du serveur les bots morts, 5 s après leur mort. Faux par défaut : chaque kick est noté dans le journal admin et part au webhook Discord ; un bot mort reste connecté jusqu'au changement de manche, où le jeu supprime tous les bots. Les bots des **lignes IA** morts sont toujours retirés. Tous les kicks du mod sont limités à **un par seconde**. |
| `tuerdisperses` | | booléen | `false` | | Fin de la dispersion (officier mort, renvoi, déroute ; ne concerne pas les lignes IA, toujours retirées par kick). Faux : les bots restent immobiles sur place, ne sont plus pilotés ni comptés dans le moral, jusqu'à la fin de manche ; ils comptent toujours dans le total de bots et sont **retirés en premier** (kick, un à la fois) si le total dépasse le plafond. Vrai : ils sont tués (`serverAdmin slay` : journal admin, webhook Discord). |
| `pauselevee` | | réel (s) | `30` | 0 à 600 | Quand le jeu refuse l'apparition d'un bot (15 s sans apparaître : places de section ou de classe), ce bot est retiré, le reste de la demande est abandonné, l'officier est prévenu une fois et ses levées et renforts sont en pause pendant ce délai. |
| `spawninterval` | | réel (s) | `0.25` | 0.05 à 5 | Intervalle minimal entre deux créations de bot (étale la charge). |
| `dedupwindow` | | réel (s) | `1` | 0 à 5 | Une phrase vocale équivalente à l'ordre du menu donné juste avant, dans ce délai, est ignorée (pas d'ordre exécuté deux fois). |
| `disperseseconds` | | réel (s) | `8` | 0 à 60 | Durée de la dispersion d'une ligne (officier mort, renvoi, déroute) ; ensuite, voir `tuerdisperses`. |

## 2. Levée

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `allowedclasses` | `classes` | liste de classes | `ArmyLineInfantry,Guard,Grenadier,LightInfantry,Rifleman` | classes de ligne | Classes qu'un officier peut demander avec `!ligne`. Séparateurs `,` `;` ou `\|`. Noms du jeu, alias (voir plus bas) ou numéros. Seules les **classes de ligne** sont gardées : `ArmyLineInfantry`, `Guard`, `Grenadier`, `LightInfantry`, `Rifleman`, `Sergeant` (infanterie à pied armée d'un fusil ou d'un mousquet) ; les autres sont retirées avec un `[CMD-WARN]` (porte-drapeau et musicien sont des auxiliaires, voir `aux` ; marine, cavalerie, artillerie et soutien n'ont rien à faire en rang). Liste vide ou toute invalide : la liste précédente est gardée. |
| `defaultclass` | `classe` | classe | `ArmyLineInfantry` | une classe connue | Classe d'une levée sans classe, ou dont la classe n'est pas permise. Si elle n'est pas dans `allowedclasses`, elle est remplacée au démarrage par `ArmyLineInfantry` (si permise), sinon par la classe permise de plus petit numéro. |
| `aux` | | booléen | `true` | | Ajoute un porte-drapeau et un tambour aux lignes assez grandes (pris sur le nombre demandé, comptés dans les plafonds, un de chaque par ligne). |
| `auxmin` | | entier | `8` | 1 à 100 | Taille de ligne à partir de laquelle un porte-drapeau est ajouté. |
| `drummin` | | entier | `20` | 1 à 100 | Taille de ligne à partir de laquelle un tambour est ajouté. Ramené à `auxmin` s'il est plus petit. |
| `lignedefaut` | | entier | `12` | 0 à 100 | **Levée sans chat** (joueurs console) : un officier d'infanterie **sans ligne** qui donne l'ordre Renfort du menu ou crie Reinforce lève une ligne de ce nombre d'hommes, classe `defaultclass`, uniformes selon `skin`, dans les plafonds. `0` désactive. Avec une ligne, Renfort reste l'ordre de renforts. |

Le nombre demandé par `!ligne` est aussi borné à 100 dans le code (en plus des plafonds).

Noms de classe acceptés (`allowedclasses`, `defaultclass`, `skin`, et dans le chat) :
- noms du jeu, sans tenir compte de la casse : `ArmyLineInfantry` (1), `NavalMarine` (2), `NavalCaptain` (3), `NavalSailor` (4),
  `NavalSailor2` (5), `ArmyInfantryOfficer` (6), `CoastGuard` (7), `Carpenter` (8), `Surgeon` (9), `Rifleman` (10),
  `LightInfantry` (11), `FlagBearer` (12), `Customs` (13), `Guard` (16), `Grenadier` (18), `Musician` (19), `Sergeant` (20),
  `Cannoneer` (21), `Deprecated_Rocketeer` (22), `Sapper` (23), `Hussar` (24), `Dragoon` (25) ;
- numéros ci-dessus ;
- alias : `ligne`, `line` (1) ; `garde`, `guard` (16) ; `grenadier` (18) ; `legere`, `légère`, `light` (11) ;
  `carabinier`, `rifleman` (10) ; `marine` (2) ; `douanier` (13) ; `garde-cote` (7) ; `drapeau` (12) ;
  `tambour`, `musicien` (19) ; `sergent` (20).

## 3. Formation

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `spacing` | `espacement` | réel (m) | `1.0` | 0.5 à 5 | Distance entre deux hommes d'un même rang (sert aussi au carré et à la charge). |
| `rankdistance` | `distancerangs` | réel (m) | `1.2` | 0.5 à 5 | Distance entre deux rangs. |
| `doublerank` | `rangdouble` | booléen | `true` | | Rang double à la levée (sinon rang simple). L'officier change ensuite avec `!rangs` ou les phrases DoubleRank / SingleRank. |

## 4. Ordres

Pas de variable propre aux ordres : leur répartition (menu officier, phrases vocales, chat) est fixe, voir [GUIDE.md](GUIDE.md).
Réglages qui s'y rapportent : `dedupwindow` (§1), `carre` (§7), `renforts` (§6).
| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `retraitem` | | réel (m) | `20` | 0 à 100 | Recul de la ligne sur la phrase Retreat. |

Valeurs fixes dans le code (non réglables) : charge 40 m droit devant, marche 2,2 m/s, course 4,5 m/s. Une charge ou une retraite
donnée à une ligne qui suit l'officier ou marche en colonne la forme d'abord devant lui.

## 5. Lot A : succession et moral

### Succession (n° 3)

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `succession` | | booléen | `true` | | À la mort ou au départ de l'officier, un sergent bot tient la ligne en attendant qu'un officier la reprenne. Sinon, dispersion directe. |
| `successionmin` | | entier | `6` | 1 à 100 | Nombre minimal de soldats de ligne vivants pour qu'il y ait succession ; en dessous, dispersion directe. |
| `successionseconds` | `successiondelai` | réel (s) | `20` | 3 à 120 | Durée pendant laquelle la ligne attend un nouvel officier avant de se disperser. |
| `successionrecul` | | réel (m) | `15` | 0 à 50 | Recul en ordre au début de la succession (sans repère de position, la ligne fait halte). |
| `reprendrerayon` | | réel (m) | `10` | 2 à 50 | Distance à laquelle un officier sans ligne peut reprendre une ligne sans officier de son camp (`!reprendre`, phrases Follow ou StandGround). |

Les officiers du camp à moins de 100 m de la ligne reçoivent un message privé pour la reprendre (pas les autres : chaque message
privé part aussi au webhook Discord admin).

### Moral (n° 4)

Moral de 0 à 100, à 100 à la levée.

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `moral` | | booléen | `true` | | Active le moral (pertes, effets, ralliement). |
| `moralbas` | | réel | `40` | 0 à 100 | Sous ce seuil, tir moins précis (délai aléatoire jusqu'à 2 s et visée bruitée, de plus en plus fort vers 0). |
| `moralrecul` | | réel | `20` | 0 à 100 | Sous ce seuil, la ligne flanche et recule de `moralreculm` m, de nouveau toutes les 20 s (un carré se reforme en arrière, sans se défaire ; une ligne IA qui charge va au moins 5 s au bout de sa charge). Elle se reprend à `moralrecul` + 10. L'officier n'est prévenu qu'une fois, jusqu'au ralliement. |
| `moralreculm` | | réel (m) | `15` | 0 à 100 | Recul d'une ligne qui flanche. |
| `moralperte` | | réel | `80` | 0 à 200 | Points perdus pour la perte de toute la ligne : chaque mort coûte `moralperte` / taille de la ligne avant la perte. |
| `moralofficier` | | réel | `30` | 0 à 100 | Points perdus à la mort de l'officier (pas à son départ). |
| `moralfeu` | | réel | `1` | 0 à 20 | Points perdus quand un bot de la ligne est touché sans mourir. |
| `moralregen` | | réel | `1` | 0 à 20 | Points regagnés par seconde quand la ligne n'est plus touchée depuis `moralcalme` s. |
| `moralcalme` | | réel (s) | `10` | 0 à 120 | Délai sans être touché avant que le moral remonte. |
| `moralrallie` | | réel | `25` | 0 à 100 | Points regagnés à un ralliement (StandGround de l'officier, ordre de ralliement ou buff SergeantLineRally d'un sergent joueur). |
| `moralrallierecharge` | | réel (s) | `60` | 0 à 600 | Délai minimal entre deux ralliements d'une même ligne. |
| `rallyradius` | `rallierayon` | réel (m) | `25` | 2 à 100 | Rayon du ralliement d'un sergent joueur (ordre SergeantRally) autour de lui. |

À 0, la ligne est en déroute et se disperse.

## 6. Lot A : renforts et remplissage

### Renforts (n° 5)

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `renforts` | | booléen | `true` | | Active les renforts (ordre Renfort du menu, phrase Reinforce, `!renforts`). |
| `renfortsnombre` | | entier | `5` | 1 à 50 | Soldats par renfort (dans la limite des plafonds), de la classe de la dernière levée. |
| `renfortsdelai` | | réel (s) | `90` | 0 à 1800 | Délai entre deux renforts d'un même officier. |
| `renfortsdistance` | | réel (m) | `30` | 2 à 100 | Distance derrière l'officier où apparaissent les renforts, qui rejoignent ensuite la ligne à pied. |

### Plafond selon le remplissage (n° 11)

Plafond global = droite de `fillmax` (0 vrai joueur) à `fillmin` (`fillfull` vrais joueurs ou plus), jamais au-dessus de `maxbots`
(ou du plafond `!cmd max`). Le plafond effectif est le plus petit de ce plafond et du plafond de charge (§8).
Si `fillmin` est plus grand que `fillmax`, les deux valeurs sont échangées.

Retraits quand le total dépasse le plafond, un pas toutes les `fillintervalle` s : d'abord les **dispersés laissés sur place**,
puis les demandes en attente et les bots des **lignes IA** (dès que le total dépasse le plafond, sans marge), enfin, si l'écart
dépasse `fillmarge`, les demandes en attente et les bots des lignes des joueurs (le plus loin de son chef). Retrait par kick (un
par seconde au plus pour tout le mod). Un bot par pas, ou écart / 5 quand c'est la charge du serveur qui limite. L'officier n'est
prévenu qu'au premier retrait d'une série. Les dispersés et les bots IA ne comptent pas pour les levées et renforts des joueurs :
à une levée ou à des renforts, la place est faite tout de suite (demandes IA annulées, puis bots IA retirés). Les demandes
d'apparition des joueurs passent toujours avant celles des lignes IA.

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `fillmax` | | entier | `60` | 0 à 250 | Plafond quand le serveur est vide. |
| `fillmin` | | entier | `10` | 0 à 250 | Plafond à partir de `fillfull` vrais joueurs. |
| `fillfull` | | entier | `40` | 0 à 250 | Nombre de vrais joueurs pour lequel le plafond vaut `fillmin`. `0` désactive la fonction (plafond = `maxbots`). |
| `fillmarge` | `fillmargin` | entier | `5` | 0 à 100 | Écart toléré au-dessus du plafond avant de retirer des bots déjà là (les nouvelles levées et renforts sont refusés dès le plafond). |
| `fillintervalle` | `fillinterval` | réel (s) | `10` | 1 à 600 | Intervalle entre deux pas de retrait (voir ci-dessus). |

## 7. Lot B : uniformes et carré

### Uniformes (n° 15)

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `skin` | | `Faction:Classe:uniforme` | aucun | uniforme connu pour ce couple (≥ 1) | Uniforme par défaut des bots d'une faction et d'une classe. **Répétable** (une ligne par couple). Faction et classe par nom ou numéro. Exemple : `CMD:skin:British:Guard:2`. Un uniforme inconnu est ignoré. |
| `skinlibre` | | booléen | `false` | | Accepte les ids d'uniforme absents de la table du mod (uniformes ajoutés par des mods), pour `skin` comme pour `!skin` et `!ligne`. |

Choix de l'uniforme d'un bot : choix de l'officier (`!skin`, ou `!ligne … <uniforme>` ; `0` revient au défaut), sinon `CMD:skin`,
sinon l'uniforme 1 du jeu. Exception, le **musicien** sans réglage : premier uniforme qui n'est pas de la marine (British 3,
French 3, sinon 1).
Factions : `British` (1), `French` (2), `Prussian` (3), `Russian` (4), `Italian` (5), `Allied` (6), `Central` (7), `Austrian` (8),
`Privateer` (9), `Spanish` (10), `ARBritish` (11), `ARAmerican` (12). Pas de ligne pour `Privateer` et `Spanish`.
Liste des uniformes connus : `!skins` et `!skins <classe>` en jeu, ou la table de `Scripts/Uniforms.cs`.

Les lignes `CMD:skin` sont vérifiées une fois tous les réglages lus : leur place par rapport à `CMD:skinlibre` n'a pas d'importance.

### Carré (n° 7)

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `carre` | `carré` | booléen | `true` | | Autorise le carré (`!carre`, phrase PresentArms). |
| `carredeuxrangs` | | entier | `16` | 4 à 200 | Nombre de soldats à partir duquel le carré a 2 rangs par face (rang extérieur accroupi). |
| `carreportee` | | réel (m) | `40` | 5 à 150 | Portée du tir automatique du carré sur l'infanterie (arc de ±50° devant chaque face). |
| `carreporteecavalerie` | | réel (m) | `60` | 5 à 150 | Portée sur la cavalerie (Hussar, Dragoon). |

## 8. Lot B : plafond selon la charge (n° 16)

Mesure la durée moyenne d'une image du serveur (moyenne lissée sur environ 100 images). Au-dessus du seuil, le plafond de charge baisse de `chargepas` (à partir du nombre
réel de bots, sans descendre sous `chargemin`) ; nettement sous le seuil (80 %), il remonte de 1 par décision, puis disparaît
en atteignant `maxbots`. Pas de décision pendant les 30 s qui suivent le démarrage ou un chargement de carte.

Attention : la clé `charge` désigne ici la **charge du serveur**, pas l'ordre de charge.

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `charge` | | booléen | `true` | | Active le plafond selon la charge. |
| `chargeseuil` | | réel (ms) | `0` | 0 à 1000 | Seuil de durée moyenne d'une image. `0` = automatique : 1,5 × la **médiane** de la durée d'image mesurée pendant 60 s cumulées **sans aucun bot** (après la mise en route), notée dans le journal ; avant ce calibrage, 1,5 × l'intervalle estimé (1000 / targetFrameRate, sinon fixedDeltaTime). Une valeur > 0 est prioritaire. |
| `chargemin` | | entier | `10` | 1 à 250 | Plancher du plafond de charge. |
| `chargepas` | | entier | `5` | 1 à 50 | Baisse du plafond à chaque mesure au-dessus du seuil. |
| `chargeintervalle` | | réel (s) | `5` | 1 à 120 | Intervalle entre deux décisions. |

## 9. Lot C : lignes ennemies IA (n° 12)

Un camp de la manche reçoit des lignes IA **seulement s'il n'a aucun vrai joueur** alors que l'autre camp en a, si le mod est
actif (`actif`) et si le mode de jeu de la manche est dans `iamodes`. Elles se retirent dès qu'un vrai joueur rejoint ce camp, ou
s'il ne reste personne en face : retrait direct par **kick** (un par seconde), sans dispersion. Leurs morts sont aussi retirés par
kick (sinon ils resteraient connectés et occuperaient les places de section et de classe du camp, vague après vague). Pas de
nouvelle vague après la fin de manche ; après `!cmd vider` ou `!cmd off`, il faut de nouveau attendre `iadelai`.
Les lignes IA n'ont ni porte-drapeau ni tambour (places de classe limitées du camp). Une seule annonce publique par camp et par
manche, à la première vague, et une au retrait quand un joueur rejoint le camp.

**Attention** : les morts des bots IA comptent comme des morts du camp IA ; dans un mode à renforts limités, elles **consomment
les renforts de ce camp** (et peuvent lui faire perdre la manche). Le camp d'un joueur est la faction de sa dernière apparition dans la manche. Les lignes IA restent sous le
plafond effectif et passent après les lignes des joueurs (§6).

| Clé | Alias | Type | Défaut | Bornes | Effet |
|---|---|---|---|---|---|
| `lignesia` | | booléen | `false` | | Active les lignes IA (faux tant que ce n'est pas essayé en jeu). Changé en jeu par `!cmd ia on\|off`. |
| `ianombre` | | entier | `1` | 0 à 10 | Nombre de lignes IA par camp vide (si `iaparjoueurs` vaut 0). |
| `iaparjoueurs` | | entier | `0` | 0 à 100 | Une ligne IA pour ce nombre de vrais joueurs en face (arrondi au-dessus). `0` = nombre fixe `ianombre`. |
| `iataille` | | entier | `15` | 4 à 100 | Soldats par ligne IA (moins si le plafond manque de place, en tenant compte des places déjà réservées par les autres lignes IA ; sous 4, pas de ligne), plus un officier bot. |
| `iaclasse` | | classe | `ArmyLineInfantry` | classe de ligne | Classe des soldats IA (mêmes classes de ligne que `allowedclasses`). |
| `iadelai` | | réel (s) | `30` | 0 à 600 | Attente avant la première ligne : après le début de manche ou le départ du dernier joueur du camp. |
| `iaattente` | | réel (s) | `60` | 0 à 1800 | Attente entre deux vagues, quand une ligne IA a été détruite. |
| `iaportee` | | réel (m) | `60` | 10 à 200 | Distance à laquelle la ligne IA s'arrête pour tirer. |
| `iacharge` | | réel (m) | `20` | 0 à 100 | Distance de l'ennemi le plus proche sous laquelle la ligne IA charge. Elle charge aussi avec un net avantage numérique (au moins le double, 6 soldats ou plus), mais seulement à moins de 2 × `iacharge` et après au moins une salve. |
| `iabloque` | | réel (s) | `10` | 3 à 120 | Ligne bloquée si son centre bouge de moins de 2 m pendant ce délai : elle repart avec un cap un peu différent. Après 3 blocages de suite, elle ouvre le feu de là où elle est si la cible est à moins de 1,5 × `iaportee`, sinon elle attend 30 s. En marche (toutes lignes), un homme à plus de 15 m de sa place depuis 8 s y est téléporté. |
| `iamodes` | | liste d'entiers | vide | numéros de GameplayMode | Modes de jeu où les lignes IA sont permises, séparés par `,` (`ArmyAssault` 1, `ArmyBattlefield` 2, `ArmyConquest` 3, `ArmyDeathmatch` 4, `ArmySiege` 5, … `Linebattle` 22). Vide = tous les modes, avec un `[CMD-WARN]` au démarrage si `lignesia` est vrai (« modes non vérifiés »). |

---

## 10. Commandes admin

Il faut d'abord être connecté en admin : `rc login <mot de passe>` dans la console admin du jeu. Puis, au choix :
- dans le chat : `!cmd <sous-commande>` ;
- dans la console admin : `rc cmd <sous-commande>`.

Réponse en message privé. Un joueur non connecté en admin reçoit « Commande réservée aux admins du serveur. »
Les changements durent **jusqu'au redémarrage** du serveur (le serverconfig n'est pas modifié).

| Commande | Effet |
|---|---|
| `!cmd` ou `!cmd etat` (`état`) | État : actif ou non, nombre de lignes et de lignes sans officier, bots / plafond, joueurs, plafond par officier, files d'attente, charge. |
| `!cmd config` | Tous les réglages en vigueur. |
| `!cmd on` | Active les lignes de bots (et renvoie les réglages de base des bots au jeu). |
| `!cmd off` | Désactive les lignes et supprime tous les bots du mod. |
| `!cmd max <0 à 250>` | Plafond global (remplace `maxbots`) ; au-delà, les bots sont retirés progressivement. |
| `!cmd maxofficier <1 à 100>` | Plafond par officier (remplace `maxperofficer`). |
| `!cmd vider` | Supprime tous les bots du mod (le mod reste actif). |
| `!cmd ia on\|off\|etat` | Active ou désactive les lignes IA (off : elles se retirent), ou affiche leur état. |

Les réponses longues (`etat`, `config`) arrivent en plusieurs messages privés.

---

## 11. Exemple complet de serverconfig

Toutes les valeurs ci-dessous sont les **défauts** : seules les lignes qui changent quelque chose sont utiles.

```
load_mod_server_only <ID>
# Général
mod_variable CMD:actif:true
mod_variable CMD:maxbots:60
mod_variable CMD:maxperofficer:20
mod_variable CMD:kickdead:false
mod_variable CMD:spawninterval:0.25
mod_variable CMD:dedupwindow:1
mod_variable CMD:disperseseconds:8
mod_variable CMD:tuerdisperses:false
mod_variable CMD:pauselevee:30
# Levée
mod_variable CMD:classes:ArmyLineInfantry,Guard,Grenadier,LightInfantry,Rifleman
mod_variable CMD:classe:ArmyLineInfantry
mod_variable CMD:aux:true
mod_variable CMD:auxmin:8
mod_variable CMD:drummin:20
mod_variable CMD:lignedefaut:12
# Formation
mod_variable CMD:espacement:1.0
mod_variable CMD:distancerangs:1.2
mod_variable CMD:rangdouble:true
# Ordres
mod_variable CMD:retraitem:20
# Lot A : succession
mod_variable CMD:succession:true
mod_variable CMD:successionmin:6
mod_variable CMD:successiondelai:20
mod_variable CMD:successionrecul:15
mod_variable CMD:reprendrerayon:10
# Lot A : moral
mod_variable CMD:moral:true
mod_variable CMD:moralbas:40
mod_variable CMD:moralrecul:20
mod_variable CMD:moralreculm:15
mod_variable CMD:moralperte:80
mod_variable CMD:moralofficier:30
mod_variable CMD:moralfeu:1
mod_variable CMD:moralregen:1
mod_variable CMD:moralcalme:10
mod_variable CMD:moralrallie:25
mod_variable CMD:moralrallierecharge:60
mod_variable CMD:rallierayon:25
# Lot A : renforts
mod_variable CMD:renforts:true
mod_variable CMD:renfortsnombre:5
mod_variable CMD:renfortsdelai:90
mod_variable CMD:renfortsdistance:30
# Lot A : remplissage
mod_variable CMD:fillmax:60
mod_variable CMD:fillmin:10
mod_variable CMD:fillfull:40
mod_variable CMD:fillmarge:5
mod_variable CMD:fillintervalle:10
# Lot B : uniformes (skin sans défaut, exemple)
mod_variable CMD:skinlibre:false
mod_variable CMD:skin:British:Guard:2
# Lot B : carré
mod_variable CMD:carre:true
mod_variable CMD:carredeuxrangs:16
mod_variable CMD:carreportee:40
mod_variable CMD:carreporteecavalerie:60
# Lot B : charge du serveur
mod_variable CMD:charge:true
mod_variable CMD:chargeseuil:0
mod_variable CMD:chargemin:10
mod_variable CMD:chargepas:5
mod_variable CMD:chargeintervalle:5
# Lot C : lignes IA
mod_variable CMD:lignesia:false
mod_variable CMD:ianombre:1
mod_variable CMD:iaparjoueurs:0
mod_variable CMD:iataille:15
mod_variable CMD:iaclasse:ArmyLineInfantry
mod_variable CMD:iadelai:30
mod_variable CMD:iaattente:60
mod_variable CMD:iaportee:60
mod_variable CMD:iacharge:20
mod_variable CMD:iabloque:10
# iamodes : vide par défaut (tous les modes) ; pour limiter, par exemple :
mod_variable CMD:iamodes:1,2,3
```

Les commentaires `#` sont sur leur propre ligne, jamais dans une valeur.
