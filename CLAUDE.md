# Mod Commander

Mod serveur Holdfast inspiré de Commander Battles. Commencer par lire `README.md`, puis `docs/PLAN.md` et `docs/API.md`.

## Règles
- **Projet séparé de Holdfast Points** : ne jamais modifier `../holdfast-points`. On peut le lire comme exemple (structure, `GameChat.cs`).
- **Côté serveur seulement**, scripts sans scène ni asset : sinon le serveur devient incompatible console.
- **Aucun envoi réseau, aucune télémétrie, aucune statistique.**
- Ne pas copier de code de Commander Battles : on s'en inspire seulement.
- Enums du jeu en entiers (les noms côté mod sont en retard, voir `docs/API.md` §2).
- Ne jamais journaliser un mot de passe (`OnRCLogin`).
- Aucun essai sur une instance où des joueurs sont connectés. Les essais se font sur une instance de test, avec l'accord de l'utilisateur.
- La compilation se fait sur le PC de l'utilisateur (Unity + Holdfast Mod Tools), pas sur ce serveur.
