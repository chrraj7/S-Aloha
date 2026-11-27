# ADR-0008 — Migrations EF à la place d'EnsureCreated, avec reprise des bases existantes

<sub>[← Documentation](../readme.md) · [Toutes les décisions](readme.md)</sub>

- **Date** : 2026-10-06
- **Statut** : accepté
- **Décideur** : Leipa

## Contexte

Le schéma était créé au démarrage par `Database.EnsureCreated()`, qui ne fait rien sur une
base existante. Les modules ITIL ([ADR-0007](adr-0007-pratiques-itil4-et-socle-commun-des-processus.md))
ajoutent dix tables : sur toute installation en service, elles manquaient, et les nouveaux
processus — console comprise — répondaient 500. Les bases existantes n'ont pas de table
d'historique de migrations.

## Décision

- Le schéma est tenu par les **migrations EF** (`backend/Core/Data/Migrations`), appliquées
  au démarrage de l'API.
- Première migration, `Initial`, générée depuis le modèle d'avant les modules (`5b14dee`) :
  elle reproduit exactement ce que créait `EnsureCreated`. Seconde, `ItilModules` : les dix
  tables nouvelles, sans modifier les existantes.
- **Reprise** : au démarrage, une base sans `__EFMigrationsHistory` mais avec la table
  `Problems` est une base `EnsureCreated`. `DatabaseSchema.Migrate` y inscrit `Initial`
  comme appliquée — et `ItilModules` si la table `Incidents` existe déjà —, puis applique
  le reste.

## Conséquences

- Mettre à jour une installation se réduit à redéployer (après sauvegarde) ; les données
  sont conservées.
- Toute évolution du modèle s'accompagne d'une migration générée, relue et commitée avec
  le code (procédure dans [`exploitation.md`](../reference/exploitation.md#évolutions-de-schéma)).
- Les migrations et la détection des tables (`to_regclass`) sont propres à PostgreSQL :
  changer de SGBD impose de les régénérer.
- Le paquet `Microsoft.EntityFrameworkCore.Design` est ajouté (outillage seulement, non
  publié). L'outil `dotnet-ef` exécute l'API : sur un poste sans runtime .NET 8,
  `DOTNET_ROLL_FORWARD=Major`.
- La reprise repose sur deux tables témoins (`Problems`, `Incidents`) : une base dans un état
  intermédiaire non prévu (tables créées à la main) n'est pas reconnue.

## Alternatives envisagées

- **Garder EnsureCreated et documenter la réinitialisation** — écarté : perte de données
  sur toute installation à jour des modules.
- **Une seule migration contenant tout le schéma** — écarté : impossible à appliquer sur une
  base d'avant les modules (tables `Problems` déjà présentes) sans y inscrire de faux
  historique ; deux migrations collent aux deux états réels des installations.
- **Script SQL manuel fourni à l'exploitant** — écarté : geste manuel à chaque version,
  source d'écart entre installations.
