# ⚖️ Décisions d'architecture (ADR)

<sub>[← Documentation](../readme.md)</sub>

Une décision structurante par fichier, numérotée sans trou, jamais réécrite après coup :
si une décision est renversée, on en écrit une nouvelle qui la supersède et on met à jour
le statut de l'ancienne. C'est ce qui permet de retrouver *pourquoi* le projet est dans
son état actuel, et pas seulement *quel* est cet état.

| # | Décision | Date | Statut |
|---|---|---|---|
| [0001](adr-0001-github-issues-source-de-verite.md) | GitHub Issues comme source de vérité pour les bugs, la sécurité et le backlog | 2026-10-06 | accepté |
| [0002](adr-0002-documentation-dans-le-depot.md) | La documentation vit dans le dépôt ; un correctif et sa documentation tiennent dans un seul commit | 2026-10-06 | accepté |
| [0003](adr-0003-plateforme-modulaire-par-processus-itil.md) | Plateforme modulaire par processus ITIL : socle + modules + registre | 2026-10-06 | accepté |
| [0004](adr-0004-conserver-le-stack-dotnet-react.md) | On conserve le stack .NET / React / PostgreSQL ; pas de réécriture Next.js / Tailwind | 2026-10-06 | accepté |
| [0005](adr-0005-langage-visuel-a-jetons-en-css-natif.md) | Nouveau langage visuel à jetons clair/sombre, réalisé en CSS natif | 2026-10-06 | supersédé en partie par [0021](adr-0021-langage-visuel-v2-sombre-et-nouvel-embleme.md) |
| [0006](adr-0006-licence-agplv3.md) | S-Aloha est publié sous licence AGPLv3 | 2026-10-06 | accepté |
| [0007](adr-0007-pratiques-itil4-et-socle-commun-des-processus.md) | Pratiques ITIL 4 comme référentiel, socle commun des processus, liens inter-processus dans le socle | 2026-10-06 | accepté |
| [0008](adr-0008-migrations-ef.md) | Migrations EF à la place d'EnsureCreated, avec reprise des bases existantes | 2026-10-06 | accepté |
| [0009](adr-0009-mode-demonstration-public.md) | Un mode démonstration public, avec son propre annuaire Active Directory | 2026-10-06 | accepté |
| [0010](adr-0010-certificat-d-origine-dco.md) | Les contributions sont signées sous le DCO, sans cession de droits | 2026-10-06 | accepté |
| [0011](adr-0011-financement-sponsoring-et-services.md) | Financement par le sponsoring et les services, en commençant par GitHub Sponsors | 2026-10-06 | accepté |
| [0012](adr-0012-referentiel-nis2-structure-versionnee-texte-importe.md) | Référentiel NIS 2 : structure versionnée dans le dépôt, texte importé par l'administrateur | 2026-10-08 | accepté |
| [0013](adr-0013-recherche-hybride-service-embeddings-separe.md) | Recherche hybride : embeddings dans un service séparé, plein texte PostgreSQL, vecteurs sans pgvector | 2026-10-08 | accepté, point 3 remplacé par [0022](adr-0022-pgvector-similarite-dans-postgresql.md) ; modèle hébergé permis pour le modèle de langage par [0023](adr-0023-fonctionnalites-optionnelles-et-modele-de-langage.md) |
| [0014](adr-0014-perimetres-droits-scopes.md) | Périmètres : droits scopés, affectations d'utilisateurs et de groupes AD | 2026-10-10 | accepté |
| [0015](adr-0015-gestion-de-projet-methode-active-composants.md) | Gestion de projet : la méthode active des composants | 2026-10-12 | accepté |
| [0016](adr-0016-reponses-de-l-api-en-dto.md) | Réponses de l'API : des DTO, plus d'entités EF | 2026-10-19 | accepté |
| [0017](adr-0017-observabilite-journal-json-et-opentelemetry.md) | Observabilité : journal JSON, OpenTelemetry optionnel | 2026-10-19 | accepté |
| [0018](adr-0018-jeton-en-cookie-httponly-et-csp.md) | Jeton de session en cookie HttpOnly, politique CSP | 2026-10-19 | accepté |
| [0019](adr-0019-console-d-administration.md) | Administration : utilisateurs depuis l'annuaire, fusion des doublons, catégories | 2026-10-19 | accepté |
| [0020](adr-0020-pieces-jointes-s3-et-smtp-configurables.md) | Pièces jointes sur un stockage compatible S3 (Garage), courriels par un SMTP configurable, réglés dans la console | 2026-10-23 | accepté |
| [0021](adr-0021-langage-visuel-v2-sombre-et-nouvel-embleme.md) | Langage visuel v2 : sombre par défaut, nouvel emblème, polices servies par l'application | 2026-10-23 | accepté |
| [0022](adr-0022-pgvector-similarite-dans-postgresql.md) | pgvector : la similarité des vecteurs est calculée dans PostgreSQL | 2026-11-09 | accepté |
| [0023](adr-0023-fonctionnalites-optionnelles-et-modele-de-langage.md) | Fonctionnalités optionnelles : un conteneur commun, à part des fonctions de base, activé par l'Admin ; modèle de langage par un connecteur unique, dans le SI ou hébergé | 2026-11-23 | accepté, revient sur l'ADR-0013 pour le modèle de langage |
| [0024](adr-0024-serveur-mcp-conteneur-separe-lecture-seule.md) | Serveur MCP : conteneur séparé, lecture seule, jetons personnels | 2026-11-23 | accepté |
| [0025](adr-0025-referentiels-de-conformite-importes.md) | Référentiels de conformité importés par l'organisation : rien dans le dépôt, colonnes, échelle et niveaux réglés à l'import | 2026-11-24 | proposé |

Les choix fondateurs antérieurs à cette base (authentification AD LDAP → JWT, analyses
RCA stockées en JSON) sont décrits dans [`reference/architecture.md`](../reference/architecture.md)
et non dans des ADR : ils n'ont pas été arbitrés entre alternatives au moment où ils ont
été faits. S'ils venaient à être remis en cause, la décision qui les remplace fera l'objet
d'un ADR.

## Quand écrire un ADR

Quand la décision est **structurante et durable** : elle change l'architecture, une
convention, un fournisseur, ou la façon de livrer — et quelqu'un se demandera dans six
mois pourquoi c'est ainsi.

Ne pas en écrire pour un correctif, un choix d'implémentation local, ou une décision
qu'un agent a prise seul sans validation. Un ADR consigne une décision **actée**, pas une
proposition.

## Forme

Nom de fichier : `adr-000X-titre-en-tirets.md`.

```markdown
# ADR-000X — Titre à l'affirmative

- **Date** : YYYY-MM-DD
- **Statut** : accepté | remplacé par ADR-000Y | abandonné
- **Décideur** : …

## Contexte      ce qui n'allait pas, avec les faits mesurés
## Décision      ce qu'on fait, à l'affirmative
## Conséquences  ce que ça implique, y compris les inconvénients acceptés
## Alternatives envisagées   et pourquoi elles ont été écartées
```

Le point qui a le plus de valeur six mois plus tard est **« Alternatives envisagées »** :
sans lui, on repropose périodiquement ce qui a déjà été écarté.
