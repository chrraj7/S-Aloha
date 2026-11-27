# ADR-0003 — Plateforme modulaire par processus ITIL : socle, modules et registre

<sub>[← Documentation](../readme.md) · [Toutes les décisions](readme.md)</sub>

- **Date** : 2026-10-06
- **Statut** : accepté — amendé le 2026-10-06 (voir l'addendum)
- **Décideur** : Leipa

## Contexte

L'application est née comme un outil de **gestion des problèmes** selon une ancienne édition d'ITIL
(`ProblemManagement.Api`), avec une arborescence plate : `Controllers/`, `Services/`,
`Models/`, `Data/` côté backend, `pages/` et `components/` côté frontend. L'ambition est
désormais plus large : **S-Aloha**, une plateforme couvrant l'ensemble des processus ITIL
(incidents, demandes, changements, configuration, niveaux de service, connaissances,
amélioration continue — voir [`produit.md`](../reference/produit.md)).

Dans l'arborescence plate, rien ne distinguait ce qui est transverse (authentification
AD, annuaire, console, reporting) de ce qui est propre aux problèmes (RCA, actions RACI).
Ajouter un deuxième processus aurait mélangé les deux.

## Décision

Organiser le code en **un socle et un module par processus ITIL** :

- **Backend** (projet `SAloha.Api`) : `Core/` (Auth, Directory, Data, Pilotage) et
  `Modules/<Processus>/` (Controllers, Models). Namespaces `SAloha.Api.Core.*` et
  `SAloha.Api.Modules.<Processus>`.
- **Frontend** : `src/core/` (Layout, Login, Console, Reporting, composants partagés) et
  `src/modules/<processus>/` (pages et composants propres).
- **Registre** `frontend/src/modules/registry.js` : **source unique** de la navigation et
  de la page de connexion. Chaque processus y est déclaré avec son pilier S-A-L-O-H-A ; il
  passe de `soon` (« Bientôt ») à `active` le jour où son interface existe, sans autre
  modification de la navigation.
- Les routes d'API existantes sont conservées à l'identique.
- Renommage de la configuration par défaut : groupes AD `GRP-SALOHA-*`, compte
  `svc-saloha`, base et utilisateur PostgreSQL `saloha`, issuer JWT `S-Aloha`, projet
  compose `s-aloha`.

## Conséquences

- Premier module : `ProblemManagement` / `problem-management`. Les sept autres processus
  sont visibles dans la navigation, marqués « Bientôt ».
- `Core/Data/AppDbContext` reste **unique** et déclare les entités de tous les modules ;
  `Core/Pilotage` agrège les données des modules pour la console et le reporting. Ce sont
  les deux seules dépendances du socle vers les modules.
- Le changement de nom de base (`problemdb` → `saloha`) **casse une installation
  existante** : un déploiement antérieur doit soit conserver l'ancienne chaîne de
  connexion par variables d'environnement, soit migrer ses données (voir
  [`exploitation.md`](../reference/exploitation.md)). De même, les groupes AD
  `GRP-PROBLEM-*` doivent être renommés ou remappés.
- Procédure d'ajout d'un module : [`architecture.md`](../reference/architecture.md) et
  [`frontend.md`](../reference/frontend.md).

## Alternatives envisagées

- **Une application par processus** (dépôts ou déploiements séparés) — écarté : les
  processus ITIL partagent les utilisateurs, l'annuaire, la console et sont liés entre eux
  (un incident ouvre un problème, un problème déclenche un changement). Des applications
  séparées dupliqueraient le socle et rendraient ces liens coûteux.
- **Microservices par processus** — écarté : complexité d'exploitation disproportionnée
  pour un déploiement on-prem en Docker Compose.
- **Garder l'arborescence plate et préfixer les fichiers** — écarté : ne rend pas visible
  la frontière socle / processus, qui est précisément ce qu'on veut protéger.

## Addendum — 2026-10-06

La notion de **piliers S-A-L-O-H-A** est retirée de la plateforme : le registre ne déclare
plus de pilier par processus (`PILLARS`, champ `pillar` et `pillarOf` supprimés), la barre
latérale n'affiche plus la lettre du pilier, et la page de connexion ne les présente plus.
Le registre `frontend/src/modules/registry.js` reste la **source unique de la navigation** ;
le reste de la décision est inchangé.
