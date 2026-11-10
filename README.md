# Ambiance — rumeur de bataille pour Holdfast

Mod **côté joueur** (projet séparé de Commander et de Holdfast Points) : il ajoute, chez chaque joueur PC qui l'a, des tirs
de mousquet lointains réalistes et une fumée de tir plus dense. Aucun envoi réseau, aucune statistique.

## Ce qu'il fait
- **Son** : à chaque tir (joueurs et bots), un des tirs de `Sounds/` est joué en 3D à la position du tireur :
  - **retard** du son selon la distance (343 m/s : un tir à 300 m s'entend ~0,9 s après) ;
  - **volume** qui baisse avec la distance (pleine force jusqu'à 8 m, plus rien après 900 m) ;
  - **aigus coupés au loin** (filtre de 16 kHz près à 900 Hz au plus loin) : une salve lointaine gronde ;
  - tirs à moins de 6 m ignorés (le son du jeu suffit), hauteur légèrement variée, 24 voix au plus.
- **Fumée** : une douzaine de petites bouffées par tir (texture de nuage irrégulière), chassées devant le canon, qui grossissent, dérivent avec un léger vent et s'effacent en ~8 s. Pas de fumée ajoutée pour son propre tir.

Réglages en tête de `Scripts/AmbianceLoop.cs` (constantes).

## Musiques des musiciens (!realist on)
Quand un musicien (joueur ou bot) joue un air du jeu, l'air est coupé et une musique du mod le remplace, en 3D sur le
musicien (elle le suit, passe d'une oreille à l'autre, baisse avec la distance). `FR_xx` pour tous les camps, `EN_xx`
pour les Britanniques. Le choix dépend seulement de l'air du jeu : tous les joueurs en `!realist on` entendent la même
musique pour la même ligne. Le réglage « musique » du jeu s'applique. `!musket` reste accepté.

## Commandes du joueur (dans le chat, valables sur son PC seulement)
| Commande | Effet |
| --- | --- |
| `!realist on` | Tirs **et musiques** du mod joués (aussi les tirs proches) et sons de tir **du jeu** coupés ; tous les autres sons du jeu (rechargement, mêlée…) restent intacts. |
| `!realist off` | Plus de tirs ni de musiques du mod, son du jeu normal. C'est l'état au lancement du jeu. |

Seuls les sons de tir du jeu (`au_musket_N`) sont coupés, au moment où ils jouent ; le réglage de volume du jeu n'est pas touché. La fumée reste dans les deux cas. Rien n'est enregistré : au prochain lancement du jeu, retour à `off`.

## Serveur et consoles
- À déclarer dans la config du serveur comme un mod normal (`mods_installed <id Workshop>`), **pas** `mods_installed_server_only` :
  les joueurs doivent le télécharger.
- Pas de scène dans le mod : le serveur reste compatible console. Les joueurs console ne chargent pas de mods et n'entendront
  simplement rien de plus.

## Compiler dans Unity (Holdfast Mod Tools)
1. Copier `Scripts/` (AmbianceMod.cs, AmbianceLoop.cs, AmbianceMusic.cs, SoundData.cs) dans le dossier du mod.
2. Copier `Music/` (FR_01 … FR_09, EN_01 … EN_05) dans le dossier du mod. Ne pas renommer : le préfixe donne le camp.
3. Sélectionner les 14 musiques → inspecteur : **Force To Mono** décoché, **Load Type : Streaming**,
   **Compression Format : Vorbis**, **Quality : 100**, puis Apply.
4. Les musiques doivent faire partie des ressources du mod (comme un asset du mod), puis compiler et publier.

## À vérifier au premier essai (Player.log)
- `[AMB] Ressources du mod : N.` puis `[AMB] Musiques : 9 françaises, 5 anglaises.`
- Si `Ressources du mod introuvables` ou `0 françaises` : les musiques ne sont pas dans les ressources du mod.
- En jeu avec `!realist on` : `[AMB] Air du jeu « … » → « FR_xx »` quand un musicien joue.
