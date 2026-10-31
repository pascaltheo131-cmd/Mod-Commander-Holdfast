# Compiler et publier le mod

Le mod se compile **sur un PC Windows** avec Unity et les Holdfast Mod Tools. Ce serveur ne peut pas le compiler :
il vérifie seulement que le code compile contre les DLL du jeu (voir plus bas).

## 1. Ce qu'il faut
- Un PC Windows avec le compte Steam qui possède **Holdfast: Nations At War** (c'est lui qui publiera sur le Workshop).
- **Unity 2022.3.62f2** exactement : c'est la version du serveur installé le 31/10/2026 (lue dans `globalgamemanagers`).
  Unity Hub → *Installs → Install Editor → Archive → download archive* → 2022.3.62f2. Aucun module en plus.
  Avant chaque compilation, vérifier que le jeu n'a pas changé de version :
  `grep -ao '2022\.3\.[0-9]*f[0-9]*' "/home/debian/holdfast/server/Holdfast NaW_Data/globalgamemanagers" | head -1`.
- Les **Holdfast Mod Tools** : bibliothèque Steam, filtre **Outils**, « Holdfast: Nations At War - Mod Tools ».
  Copier leur dossier ailleurs avant de l'ouvrir, pour qu'une mise à jour Steam ne l'écrase pas.

## 2. Créer le mod
1. Unity Hub → *Add project from disk* → le dossier des Mod Tools, ouvert avec 2022.3.62f2.
2. Menu des Mod Tools → « Create new mod », nom **Commander**.
3. Copier **seulement** les fichiers `Scripts/*.cs` dans le dossier du mod. **Aucune scène, aucun prefab, aucune image** :
   une scène rendrait le serveur incompatible avec les consoles.
4. Attendre la recompilation : la console Unity ne doit afficher **aucune erreur rouge**. Sinon, transmettre le message tel quel.

## 3. Compiler
Mod Tools → **Build mod** (cible Linux standalone si on vous la demande). Résultat : `Commander.umod`.

## 4. Publier (non répertorié)
1. Outil de publication des Mod Tools, connecté au compte Steam.
2. Titre « Commander (serveur) », tag **Script**, visibilité **Non répertorié**.
3. L'**ID** est le nombre à la fin de l'URL Workshop. Le transmettre. Pour chaque nouvelle version, mettre à jour le même objet
   (l'ID ne change pas), puis redémarrer l'instance quand elle est vide.

## 5. Charger sur le serveur (via le panneau)
Ne pas modifier `serverconfig_<instance>.txt` à la main : il est régénéré par le panneau.
```
mods_installed_server_only <ID> #Commander
load_mod_server_only <ID> #Commander
mod_variable CMD:actif:true
```
**Jamais `load_mod`** : ce serait un mod client, que les joueurs console ne peuvent pas avoir.

Commencer par une **instance de test sans joueurs**, puis vérifier dans `server/logs_output/outputlog_<instance>.txt` :
- les lignes `[CMD]` de démarrage ;
- l'absence de `[CMD-WARN]` en boucle ;
- que le serveur reste visible depuis une console.

## Vérification sur ce serveur
Sans Unity, le code est compilé contre les DLL de `server/Holdfast NaW_Data/Managed/` (UnityEngine, HoldfastSharedMethods*,
HoldfastGameMethods) dans un projet `netstandard2.1` hors du dépôt. Cela attrape les erreurs de syntaxe et de type,
pas les limites propres à uMod (liste d'interdictions `ModToolsSecurityRestrictions`) : seule la compilation dans Unity les valide.
