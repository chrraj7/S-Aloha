# 📚 Documentation S-Aloha

> Tout ce qu'il faut pour comprendre, installer et faire évoluer S-Aloha.
> Première visite ? Commencez par le [README](../README.md), puis le [Produit](reference/produit.md).

## Référence

Ce qui est vrai en permanence, mis à jour avec le code.

| | Document | Pour |
|---|---|---|
| 🧭 | [Produit](reference/produit.md) | Comprendre la vision, les publics visés et les pratiques couvertes |
| 📐 | [Règles métier](reference/regles-metier.md) | Connaître les rôles, la console et les règles communes à toutes les pratiques |
| 🗂️ | [Pratiques ITIL](reference/processus/readme.md) | **Spécifier ou implémenter** une pratique : une règle numérotée, un statut ✅ / 🔜, un lot ; choisir une règle à prendre |
| 🏗️ | [Architecture](reference/architecture.md) | Voir comment tout s'assemble : containers, socle, modules, API |
| 🗄️ | [Base de données](reference/base-de-donnees.md) | Lire le modèle de données et les formats d'analyse |
| 🖥️ | [Frontend](reference/frontend.md) | Ajouter un écran ou un module, respecter le design |
| ⚙️ | [Exploitation](reference/exploitation.md) | Installer, brancher l'Active Directory, mettre à jour |
| 📊 | [Reporting](reference/reporting.md) | Lire le tableau de bord : score de santé, indicateurs par onglet, filtres, exports |
| 🔎 | [Recherche](reference/recherche.md) | Comprendre la recherche hybride : contenu indexé, cas similaires, service d'embeddings |
| 🎛️ | [Fonctionnalités optionnelles](reference/fonctionnalites-optionnelles.md) | Activer ou couper le [tri assisté des tickets](reference/tri-assiste-des-tickets.md), la [CMDB assistée](reference/cmdb-assistee.md) et le [serveur MCP](reference/serveur-mcp.md), chacun dans son conteneur ; régler le modèle de langage |
| 🧭 | [Périmètres](reference/perimetres.md) | Droits scopés par direction, site ou entité (droits, visibilité, écran d'administration) |
| 🛠️ | [Administration et référentiels](reference/administration.md) | Utilisateurs et rôles, santé, journal d'audit global, doublons, catégories (ADM) |
| 🛡️ | [Sécurité](reference/securite.md) | Préparer une mise en production |
| 📖 | [Glossaire](reference/glossaire.md) | Retrouver un terme ITIL, technique ou malgache |

## Décisions, plan et historique

| | | Durée de vie |
|---|---|---|
| ⚖️ [Décisions (ADR)](decisions/readme.md) | Les choix structurants et **pourquoi** | Définitif : remplacé, jamais réécrit |
| 🗺️ [Plan d'action](plan-action.md) | Les chantiers ouverts et la feuille de route | Vivant : se vide en avançant |
| 🗃️ [Archives](archives/readme.md) | Constats datés et journaux des sujets traités | Figé |
| 🔄 [Protocole](protocole-documentation.md) | La routine qui tient tout cela à jour | Vivant |
| 💛 [Soutenir S-Aloha](soutenir.md) | Financer la feuille de route : sponsoring, services | Vivant |

Le cycle : un constat daté part en **archives** ; ce qu'il révèle de durable rejoint la
**référence** ; ce qu'il reste à faire va au **plan** ; ce qui est tranché devient une
**décision**.

## Où va quoi

| J'ai… | Je le mets dans… |
|---|---|
| un bug, une faille | [GitHub Issues](https://github.com/chrraj7/S-Aloha/issues) ([ADR-0001](decisions/adr-0001-github-issues-source-de-verite.md)) |
| un chantier qui demande une intention | [`plan-action.md`](plan-action.md) |
| un audit, un état des lieux | `archives/`, daté dans le nom du fichier |
| un sujet traité aujourd'hui | son entrée de journal, `archives/journaux/AAAA-MM-JJ-identifiant.md` ([protocole](protocole-documentation.md#3-journaliser-les-sujets-traités)) |
| un changement notable pour qui installe | une note dans [`changelog.d/`](../changelog.d/readme.md) |
| une décision actée et structurante | un ADR dans [`decisions/`](decisions/readme.md) |
| un fait durable sur le fonctionnement | le document de référence concerné, **dans le même commit que le code** ([ADR-0002](decisions/adr-0002-documentation-dans-le-depot.md)) |

## Wiki

Le [wiki du projet](https://github.com/chrraj7/S-Aloha/wiki) est généré depuis ce
dossier par `scripts/sync-wiki.py` : on le lit, on ne l'édite pas
([protocole § 6](protocole-documentation.md#6-publier-le-wiki)).

## Licence

Code et documentation sont sous [AGPLv3](../LICENSE)
([ADR-0006](decisions/adr-0006-licence-agplv3.md)).

## Conventions

- Documentation en **français**, identifiants de code en **anglais**.
- Fichiers en **minuscules-avec-tirets** ; liens **relatifs**.
- **Un document, une nature** : constat, plan, décision ou référence, jamais un mélange.
- Diagrammes en **Mermaid**, affichés directement par GitHub.
- Captures d'écran dans [`screenshots/`](screenshots/), à régénérer quand l'interface change.
- Le code cite parfois la doc (`docs/reference/frontend.md § Design`, `ADR-0005`) : un
  renommage impose de mettre à jour ces renvois (`grep -rn "docs/\|ADR-" backend frontend/src`).
- Avant de committer :

  ```bash
  python3 scripts/check-docs.py   # liens, ancres et chemins cités
  ```
