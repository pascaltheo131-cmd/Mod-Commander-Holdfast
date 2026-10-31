# Commander (nom provisoire)

Mod **côté serveur seulement** pour Holdfast: Nations At War, inspiré de [Commander Battles](docs/COMMANDER-BATTLES.md) :
un officier lève sa propre ligne de bots et la commande.

- Jouable par tous : PC, Xbox, PlayStation. Les joueurs n'ont rien à installer (`load_mod_server_only`).
- Aucun envoi réseau, aucune statistique : les stats restent le rôle de [Holdfast Points](../holdfast-points/README.md),
  qui est un projet séparé.

État : **cœur, lots A, B et C écrits** (`Scripts/`), vérifié par compilation contre les DLL du serveur, jamais compilé dans Unity ni testé en jeu.

| Document | Contenu |
|---|---|
| [docs/PLAN.md](docs/PLAN.md) | Plan de la v1 et vérifications préalables |
| [docs/API.md](docs/API.md) | Ce qu'un mod serveur Holdfast peut faire (événements, commandes, pièges, console) |
| [docs/COMMANDER-BATTLES.md](docs/COMMANDER-BATTLES.md) | Analyse du mod d'origine |
| [docs/CARBONPLAYERS.md](docs/CARBONPLAYERS.md) | Les bots : commandes `carbonPlayers`, ids, uniformes, mouvement, tests en jeu |
| [docs/BUILD.md](docs/BUILD.md) | Compiler dans Unity (2022.3.62f2), publier, charger sur le serveur |
| [docs/GUIDE.md](docs/GUIDE.md) | Guide des joueurs (officiers, sergents) |
| [docs/CONFIG.md](docs/CONFIG.md) | Tous les réglages `CMD:…` et commandes admin |
| [docs/TESTS.md](docs/TESTS.md) | Campagne de tests en jeu |
| [docs/ref/](docs/ref/) | Signatures brutes des interfaces et enums du jeu (serveur du 31/10/2026) |
