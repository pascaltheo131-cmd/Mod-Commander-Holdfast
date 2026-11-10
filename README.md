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

## Commandes du joueur (dans le chat, valables sur son PC seulement)
| Commande | Effet |
| --- | --- |
| `!musket on` | Tirs du mod joués (aussi les tirs proches) et sons de tir **du jeu** coupés ; tous les autres sons du jeu (rechargement, mêlée…) restent intacts. |
| `!musket off` | Plus de tirs du mod, son du jeu normal. C'est l'état au lancement du jeu. |

Seuls les sons de tir du jeu (`au_musket_N`) sont coupés, au moment où ils jouent ; le réglage de volume du jeu n'est pas touché. La fumée reste dans les deux cas. Rien n'est enregistré : au prochain lancement du jeu, retour à `off`.

## Serveur et consoles
- À déclarer dans la config du serveur comme un mod normal (`mods_installed <id Workshop>`), **pas** `mods_installed_server_only` :
  les joueurs doivent le télécharger.
- Pas de scène dans le mod : le serveur reste compatible console. Les joueurs console ne chargent pas de mods et n'entendront
  simplement rien de plus.

## Compiler dans Unity (Holdfast Mod Tools)
1. Créer un nouveau mod « Ambiance » dans le projet Unity des outils de mod (comme Commander).
2. Copier `Scripts/` (les deux fichiers .cs) et `Sounds/` (les 23 `tir_XX.wav`) dans le dossier du mod.
3. Sélectionner les 40 sons dans Unity → inspecteur : **Force To Mono** coché, **Load Type : Decompress On Load**,
   **Compression : Vorbis** (qualité ~70). Les noms doivent rester `tir_01` … `tir_23` : le mod les charge par leur nom.
4. Vérifier qu'ils font partie des ressources du mod (asset du mod, pas d'une scène), puis compiler et publier comme Commander.

## À vérifier au premier essai
- Journal du jeu côté joueur (Player.log) : `[AMB] Ambiance chargée : 40 tirs.` Si `Ressources du mod introuvables`, les sons
  ne sont pas dans les ressources du mod ; si `0 tirs`, leurs noms ne correspondent pas.
- Qu'un tir d'un autre joueur ou d'un bot déclenche bien le son et la fumée chez soi (le jeu prévient les mods à chaque tir ;
  à confirmer pour les tireurs distants).
- `[AMB] Aucun shader de particules : fumée désactivée.` : il faudra alors fournir un matériau de fumée dans le mod.
