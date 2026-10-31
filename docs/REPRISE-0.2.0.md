# Commander 0.2.0 — reprise du 31 octobre 2026

Sources préparées à partir du commit Holdfast `91c24ab` et du fichier non suivi `Names.cs` laissé par Claude.
Panneau de référence : `c01a823`. Aucune instance de jeu redémarrée, aucun mod Workshop remplacé.

## Changements

- Suivre / colonne : 2 de front par défaut, 3 réglable. Chaque rang suit la trace de l'officier, hauteur comprise, au lieu de couper directement les virages. Drapeau puis tambour en tête.
- Course : filtrage de la vitesse indépendant de la fréquence d'images, hystérésis marche/course, suppression du seuil qui forçait la marche près de la place. Les retardataires courent au-delà de 6 m et reviennent à l'allure normale sous 2 m.
- Ligne : drapeau à une extrémité, tambour à l'autre ; le carré conserve ses auxiliaires au centre.
- Bot immobile malgré un ordre de déplacement : détour après 3 s sans déplacement significatif, recalage après 6 s pour les tâches de formation si un sol est trouvé par rayon. Pas de téléportation si le rayon ne trouve rien. **Remplacé après 0.2.0, voir « Corrections après l'essai 0.2.0 ».**
- Noms personnels par faction, uniques dans une ligne, préfixe facultatif. Le préfixe reste dans l'objet ligne et est donc conservé pour les renforts et la succession.
- `!ligne 20 GDE`, `!ligne GDE 20`, `!ligne 20 GDE 2` ; la classe reste facultative. Les nombres sont lus dans l'ordre : effectif, puis uniforme. Les mots reconnus comme classes restent des classes ; les autres servent de préfixe (10 caractères maximum, lettres/chiffres/tirets/traits bas).
- `!renvoyer` annule les apparitions en attente et programme la déconnexion des bots suivis encore connectés, morts compris. Retrait limité à un bot par seconde, avec vérification de l'identité ; les anciens bots déjà abandonnés par le mod ne sont pas identifiables avec certitude et ne sont pas repris.
- Succession : un vrai bot de classe Sergeant remplace un soldat si nécessaire, sans augmenter l'effectif de la ligne. L'apparition dépend toujours des classes permises par la section du jeu. Après réapparition en sergent ou officier, `!reprendre` reprend la ligne ; l'ancien chef peut reprendre sa propre ligne à distance, les autres doivent rester dans le rayon configuré. Les ordres natifs du sergent sont reliés aux actions de la ligne.
- Délai de succession par défaut passé de 20 à 60 s pour permettre une réapparition ; un réglage explicite existant reste prioritaire. Les conditions existantes restent applicables : succession activée, au moins `successionmin` soldats vivants (1 par défaut, au lieu de 6), moral supérieur à zéro. Sans reprise avant l'échéance, la dispersion existante reste en place.
- Visée des lignes de joueurs : à « Viser », le mod mémorise le point sous le regard du chef (premier impact du rayon de caméra, hors de son propre personnage ; sinon point à 1 000 m). Les soldats prennent ce point en joue dès l'ordre, corrigent la hauteur depuis leur propre position et le conservent pour la salve / le feu à volonté. Redonner « Viser » actualise le point. Tourner le regard après cet ordre ne change pas le point mémorisé.
- Le serveur demande désormais `sendFullCameraData true` (**après 0.2.0 : seulement tant qu'un joueur commande une ligne**). Les données de caméra sont conservées uniquement pour les chefs suivis ; aucun envoi extérieur. Si la caméra est indisponible ou trop ancienne lors de l'ordre, un message prévient le joueur ; aucune fausse copie de visée n'est annoncée.
- L'IA et le carré gardent leur ciblage autonome. Réglages de portée, arc et hauteur pour l'IA ; dispersion ajoutée configurable pour tous. Pas de compensation balistique de la chute des balles, pas de prédiction des cibles en mouvement.
- Suppression du faux avertissement « Bot : ok ». Un retour explicite indiquant qu'un identifiant n'existe plus retire la fiche du bot pour éviter les tentatives répétées. Normalisation des angles existante conservée.

## Réglages ajoutés

| Variable | Défaut | Bornes / effet |
|---|---|---|
| `CMD:colonnes` | `2` | 2 ou 3 de front |
| `CMD:nomsaleatoires` | `true` | false : Soldat N et noms de rôles |
| `CMD:viseehauteur` | `true` | correction verticale des cibles IA ; les joueurs copient toujours le point du chef |
| `CMD:viseearc` | `15` | 0 à 15°, demi-angle d'acquisition IA ; carré : son arc existant de 50° |
| `CMD:viseeportee` | `100` | 10 à 200 m, acquisition IA ; le déclenchement du carré garde ses portées propres |
| `CMD:precision` | `2` | 0 à 15°, dispersion angulaire maximale ajoutée à celle du jeu ; plus petit = plus précis |

Les mêmes définitions sont ajoutées à `holdfast-panel/public/commander-vars.js`. Leur prise en compte côté serveur du panneau nécessite son prochain redémarrage ; aucune configuration d'instance n'a été enregistrée.

## Vérifications réalisées

- Compilation complète des scripts contre les DLL du serveur, netstandard2.1 : zéro erreur, zéro avertissement.
- Tests exécutant les sources de navigation, noms, formations, pilotage, visée et méthode de lecture de `!ligne` avec un moteur simulé : 2 023 assertions réussies. Ces tests n'exécutent pas Unity et ne valident pas les échanges du jeu.
- Table du panneau : unicité des clés, validation des réglages ajoutés ou ajustés, bornes refusées et aller-retour écriture/lecture sans perte.
- Les ordres de sergent, le cycle de succession, la déconnexion effective et les données de caméra devront être vérifiés en jeu.

## Recompiler et essayer

1. Dans le projet Unity / Holdfast Mod Tools existant, remplacer les fichiers du dossier Scripts par ceux de cette version, y compris `FollowTrail.cs`, `Shooting.cs` et `Names.cs`. Ne pas importer les tests, fichiers du panneau, notes ou scripts de préparation dans le mod.
2. Compiler avec la version Unity compatible avec le jeu (la documentation existante indique 2022.3.62f2 ; revérifier si le jeu a été mis à jour).
3. Mettre à jour l'objet Workshop existant `3830947372`, sans créer un nouvel ID. Aucun fichier `.umod` prêt à publier n'a été produit par le contrôle .NET.
4. Installer et essayer sur une instance de test, sans autres joueurs. Redémarrage du jeu uniquement au moment choisi par l'utilisateur.
5. Si de nouveaux réglages doivent être enregistrés par le panneau, redémarrer son service lors de cette mise en service. Aucun changement du modèle ni du serverconfig du jeu n'est nécessaire pour les nouveaux défauts.

### Essais prioritaires

- `!ligne 20 GDE 2` puis `!suivre` : vérifier noms/préfixe/uniforme, drapeau puis tambour, 2 de front. Marcher, courir pendant 30 s, s'arrêter puis faire deux virages à 90°. Refaire avec `CMD:colonnes:3`.
- Former la ligne sur le trait : vérifier les auxiliaires aux deux extrémités et l'absence de régression du placement.
- Viser une cible à gauche, en hauteur puis en contrebas ; donner « Viser », vérifier que les fusils s'orientent avant « Feu ». Tourner ensuite le regard sans redonner Viser : la salve doit conserver le premier point. Redonner Viser et vérifier le changement. Refaire à genoux et sur console.
- Bloquer un bot contre un rocher : détours courts toutes les 3 s environ (côtés alternés), puis renoncement à sa place après 4 échecs ; aucune téléportation.
- `!renvoyer` pendant l'apparition, après quelques morts, puis après une ligne complète : aucun bot de cette ligne encore suivi ne doit rester connecté après la file de retraits ; un vrai joueur ne doit jamais être retiré.
- Mort de l'officier avec au moins un soldat et un moral suffisant : apparition d'un Sergeant, pas de dépassement d'effectif ; réapparaître en sergent, `!reprendre`, puis tester sa roue d'ordres et les phrases vocales. Vérifier le préfixe et la faction, puis les renforts.
- Laisser le délai de succession expirer et changer de carte pendant une succession : vérifier nettoyage des demandes en attente et absence d'identifiants réutilisés.

## Limites connues

- Pas de prise de possession directe d'un bot : aucune fonction compatible n'a été trouvée dans l'API du mod serveur. La solution retenue par l'utilisateur est la réapparition en sergent puis `!reprendre`.
- Le contournement est local, pas un calcul de chemin complet. La recherche du sol (placement à l'apparition) et les rayons de visée dépendent des colliders de la carte et doivent être essayés sur les cartes utilisées.
- La hauteur du fusil est estimée à 1,4 m debout et 1 m accroupi. Les animations, armes et la précision effective doivent être vérifiées en jeu.
- Aucun test réel nouveau n'a été effectué sur les serveurs en service.


## Corrections après l'essai 0.2.0 (Claude, non commitées)

Relecture de 0.2.0 :
- `sendFullCameraData` n'est plus forcé au démarrage : activé seulement tant qu'un joueur commande une ligne, désactivé sinon (coût réseau et CPU de la caméra de chaque joueur à chaque paquet).
- Le message « Réapparaissez en sergent… » n'est envoyé que si l'officier est mort (un officier parti n'est plus sur le serveur).
- Charge et Retreat depuis la colonne : la ligne se forme d'abord devant l'officier (l'ancre de colonne n'est pas une ligne).

Placement et déplacements (demande de l'utilisateur : « pas de TP pour former la ligne ») :
- **Aucune téléportation de déplacement** : supprimés le recalage à 6 s (`Bot.Steer`) et toute autre téléportation pour former la ligne, suivre, la colonne, le carré, rattraper ou débloquer.
- Bot bloqué (distance à sa place qui ne baisse pas de 0,5 m en 3 s, à plus de 1,5 m) : détour court (pivot de 45 à 90°, côté alterné à chaque essai, un pas en arrière à partir du deuxième, puis 1 à 2 s de marche), puis nouvel essai ; après 4 échecs, il renonce à sa place (reste où il est, la ligne se resserre sans lui) jusqu'au prochain ordre.
- Placements gardés : juste après l'apparition (le jeu fait apparaître le bot sur une section **au hasard** de sa faction, parfois à des centaines de mètres : sans ce placement la ligne mettrait des minutes à se former), et un bot tombé sous la carte (25 m sous son chef, replacé sur son chef). Un bot dans un arbre n'est pas déplacé.
- Recherche du sol (`Ground.cs`) pour ces placements : `RaycastAll`, végétation écartée par le nom de l'objet, plus bas impact à ± 3 m de la hauteur de l'officier ou d'un bot voisin ; sinon le bot est posé sur l'officier (ou sur un bot de la ligne pour le sergent de succession), jamais en l'air.

Mise en ligne :
- Affectation stable : ordre de gauche à droite gardé d'un ordre à l'autre tant qu'il n'a pas d'inversion de plus de 1,5 m ; pas de réaffectation pendant la mise en place (seulement à l'arrivée d'un bot, à un départ ou à un renoncement).
- Seuls les soldats présents ont une place : les absents (en apparition, morts, ayant renoncé) ne laissent pas de trou, la ligne se resserre vers le centre ; auxiliaires à une place d'écart aux extrémités.
- Allure : course tant que le bot est à plus de 4 m de sa place (retour à la marche sous 3 m), ralentissement seulement dans le dernier mètre et demi ; en suivant, allure de l'officier.

Diagnostic : `!cmd debug` donne en plus les compteurs de la manche (détours, renoncements, bots replacés) et, par bot, la hauteur y visée et réelle et le nombre de détours.
