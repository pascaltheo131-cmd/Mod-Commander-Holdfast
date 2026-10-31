# Commander (nom provisoire)

Mod **côté serveur seulement** pour Holdfast: Nations At War, inspiré de [Commander Battles](docs/COMMANDER-BATTLES.md) :
un officier lève sa propre ligne de bots et la commande.

- Jouable par tous : PC, Xbox, PlayStation. Les joueurs n'ont rien à installer (`load_mod_server_only`).
- Aucun envoi réseau, aucune statistique : les stats restent le rôle de [Holdfast Points](../holdfast-points/README.md),
  qui est un projet séparé.

État : sources 0.2.0, compilation contre les DLL du serveur et tests simulés réussis. Un premier essai en jeu de la version précédente a eu lieu ; cette nouvelle version reste à compiler dans Unity et à tester en jeu. Voir [la reprise et les essais prioritaires](docs/REPRISE-0.2.0.md).

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
