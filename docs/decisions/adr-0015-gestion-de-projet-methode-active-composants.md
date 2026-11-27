# ADR-0015 — Gestion de projet : la méthode active des composants

<sub>[← Documentation](../readme.md) · [Toutes les décisions](readme.md)</sub>

- **Date** : 2026-10-12
- **Statut** : accepté
- **Décideur** : Leipa

## Contexte

La DSI conduit des projets — migrations, refontes, déploiements de services — dont les
changements, problèmes et améliorations vivent déjà dans S-Aloha, sans objet pour les
porter. ITIL compte la **gestion de projet** parmi ses pratiques de gestion générale.

Les équipes n'appliquent pas toutes la même méthode : Scrum ou Kanban pour le
développement, Cycle en V pour l'intégration et la recette, PRINCE2 pour les projets
encadrés, et souvent un mélange. Un module qui impose une méthode écarte les autres ; un
module qui affiche tout (sprints, phases, business case, tolérances) sur chaque projet
noie l'utilisateur dans des onglets vides.

Spécification : [gestion de projet](../reference/processus/gestion-de-projet.md)
(règles PRJ).

## Décision

1. Un module **Projets**, dans une section de menu **Projets** entre Pratiques et
   Conformité & réglementation ([frontend § Navigation cible](../reference/frontend.md#navigation-cible)).
2. Tout utilisateur **crée** un projet et en devient le **chef de projet** ; un
   **gestionnaire valide** le projet pour le faire passer du cadrage à l'exécution.
3. Le projet porte une **méthode** — Scrum, Kanban, Cycle en V, PRINCE2 ou Hybride —
   choisie à la création et modifiable par le chef de projet ou un gestionnaire.
4. La méthode **active des composants** (backlog, sprints, tableau, burndown, phases,
   étapes de gestion, jalons, livrables, risques, incidents projet, business case,
   tolérances) selon une table fixe ; en **Hybride**, le chef de projet les coche. La fiche
   n'affiche que les onglets des composants actifs.
5. Les composants sont des **objets partagés** entre méthodes (un jalon est le même en
   Cycle en V et en Hybride) : pas de module par méthode.
6. Changer de méthode **masque** les composants désactivés sans supprimer leurs données.
7. Le projet est un enregistrement du socle (`Record`, référence `PRJ-AAAA-NNNN`, liens,
   historique) : il se relie aux changements, problèmes, améliorations, CI et services.

## Conséquences

- Une seule table de correspondance méthode → composants, côté API, que l'interface lit :
  ajouter une méthode revient à ajouter une ligne.
- Les règles d'un composant (un seul sprint actif, validation d'un livrable…) valent quelle
  que soit la méthode qui l'active.
- Un **incident projet** (PRINCE2 : *issue*) est distinct d'un incident ITIL ; le nom est
  repris de la méthode, l'interface le qualifie toujours (« incident projet »).
- PRINCE2 est une marque de PeopleCert ; S-Aloha en reprend le vocabulaire (étapes,
  tolérances, business case), sans contenu du manuel.
- Non couverts : portefeuille de projets, temps passés, charge des ressources, Gantt.

## Alternatives envisagées

- **Une seule méthode (Scrum ou Cycle en V)** : simple, mais écarte une part des équipes
  de la DSI et pousse les autres vers un outil tiers.
- **Un module par méthode** : duplique jalons, risques et livrables, et interdit le
  mélange (Hybride), qui est la pratique la plus courante.
- **Tous les composants sur tous les projets** : aucune règle à écrire, mais des onglets
  vides sur chaque fiche et des notions (sprint, tolérance) hors sujet pour la méthode.
- **Composants choisis librement pour chaque projet** : c'est l'Hybride ; en faire la seule
  option retire les bons réglages par défaut que donne une méthode nommée.
