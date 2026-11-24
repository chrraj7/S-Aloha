# 🧭 Produit

> Pourquoi S-Aloha existe, pour qui, et quels processus ITIL il couvre.
>
> « Ny fahaiza-manao ho amin'ny tolotra tsara kokoa. »

<sub>[← Documentation](../readme.md) · [Produit](produit.md) · [Règles métier](regles-metier.md) · [Architecture](architecture.md) · [Base de données](base-de-donnees.md) · [Frontend](frontend.md) · [Exploitation](exploitation.md) · [Sécurité](securite.md) · [Glossaire](glossaire.md)</sub>

## Pourquoi S-Aloha existe

S-Aloha est une plateforme d'**intégration des processus ITIL** pour une DSI d'entreprise,
qui **aide à trier les tickets**, **à tenir la CMDB à jour** et **s'ouvre aux assistants**
de ses utilisateurs : le **tri assisté des tickets** suggère la qualification d'un incident
dès sa saisie et signale les pannes probables ; la **CMDB assistée** propose de normaliser,
dédoublonner et relier les CI ; le **serveur MCP** laisse l'assistant de chacun interroger
S-Aloha en lecture seule, avec ses droits. Ces trois fonctionnalités sont activées ou
coupées par l'Admin ([fonctionnalités optionnelles](fonctionnalites-optionnelles.md)) ; le
mode démonstration active le tri assisté. Autour d'elles,
un seul outil pour les pratiques du cycle de vie du service IT — incidents, demandes,
problèmes, changements, configuration, niveaux de service, connaissances, amélioration
continue — avec la **conformité NIS 2** et la **gestion de projet** de la DSI sur le même
socle.

Ce qu'il apporte, en bref : des tickets triés et une CMDB tenue avec l'aide de
propositions que l'équipe décide, S-Aloha interrogeable depuis les assistants de chacun,
une console qui dit à chacun quoi faire, des pratiques
reliées entre elles, un cycle de vie ITIL tenu avec une trace de tout, une recherche qui
trouve la solution avant le ticket, et un déploiement on-prem en logiciel libre. L'intégration
en entreprise passe par l'**Active Directory** de l'organisation : connexion sans compte à
créer, rôles tirés de ses groupes. La présentation publique est dans le
[README](../../README.md#fonctionnalités).

Le nom vient du malgache *aloha*, « d'abord, avant tout » : le service passe avant tout
(*Service alohan'ny zavatra rehetra*). Voir le [glossaire](glossaire.md#vocabulaire-malgache).

## Pour qui

**Organisations visées** : les DSI — PME, ETI, collectivités, établissements publics —
qui veulent des pratiques ITIL 4 sans outil propriétaire ni licence par utilisateur, et une
aide concrète pour trier les tickets et tenir la CMDB ; les entités soumises à **NIS 2** qui
veulent suivre leur conformité au même endroit que leurs incidents et leurs changements.
L'Active Directory de l'organisation est le prérequis de l'intégration.

**Utilisateurs** :

| Profil | Rôle applicatif | Ce qu'il fait dans S-Aloha |
|---|---|---|
| Collaborateur IT (support, exploitation, études) | **User** | Déclare, analyse, réalise les actions qui lui sont affectées |
| Gestionnaire de processus (*process manager*), chef de projet | **Manager** | Qualifie, autorise, approuve, pilote les statuts et les projets, relance |
| Administrateur de la plateforme | **Admin** | Tous les droits, y compris la suppression |

Les rôles sont dérivés des groupes AD ; le détail des droits est dans
[`regles-metier.md`](regles-metier.md#1-rôles-et-droits).

## Cartographie des processus ITIL et état

Ordre du cycle de vie du service. Source unique : `MODULES` dans
`frontend/src/modules/registry.js` — un processus passe de **Bientôt** à **Actif** le jour
où son interface existe ([ADR-0003](../decisions/adr-0003-plateforme-modulaire-par-processus-itil.md)).

| Processus | Objet | État | Module (API, interface) |
|---|---|---|---|
| **Incidents** | Rétablir le service au plus vite | Actif | `Modules/IncidentManagement`, `modules/incident-management` |
| **Demandes** | Demandes de service utilisateurs | Actif | `Modules/ServiceRequestManagement`, `modules/service-request-management` |
| **Problèmes** | Causes racines et erreurs connues | Actif | `Modules/ProblemManagement`, `modules/problem-management` |
| **Changements** | Changements et mises en production | Actif | `Modules/ChangeEnablement`, `modules/change-enablement` |
| **Configuration** | Actifs et dépendances (CMDB) | Actif | `Modules/ServiceConfigurationManagement`, `modules/service-configuration-management` |
| **Niveaux de service** | Catalogue, SLA et engagements | Actif | `Modules/ServiceLevelManagement`, `modules/service-level-management` |
| **Connaissances** | Capitalisation et formation | Actif | `Modules/KnowledgeManagement`, `modules/knowledge-management` |
| **Amélioration (CSI)** | Amélioration continue des services | Actif | `Modules/ContinualImprovement`, `modules/continual-improvement` |
| **Conformité NIS 2** | Évaluer la conformité au Référentiel Cyber France (ANSSI) | Actif | `Modules/ComplianceAssessment`, `modules/compliance-assessment` |
| **Déclarations réglementaires** | Déclarer les incidents de sécurité dans les délais légaux (NIS 2 : ANSSI ; RGPD : CNIL) | Actif | `Modules/RegulatoryNotifications`, `modules/regulatory-notifications` |
| **Projets** | Conduire les projets de la DSI : Scrum, Kanban, Cycle en V, PRINCE2, hybride | Actif | `Modules/ProjectManagement`, `modules/project-management` |

Référentiel : les **pratiques ITIL 4** publiées par PeopleCert/Axelos
([ADR-0007](../decisions/adr-0007-pratiques-itil4-et-socle-commun-des-processus.md)). Chaque
pratique tient son cycle de vie complet ; toutes ont quitté le stade MVP le 2026-10-16,
selon des critères vérifiables, règle par règle ([plan d'action § Sortie du MVP](../plan-action.md#sortie-du-mvp)).
Les évolutions restantes sont spécifiées dans [`processus/`](processus/readme.md) et
ordonnées dans le [plan d'action](../plan-action.md#7-feuille-de-route-fonctionnelle).

## Socle transverse (Pilotage)

Indépendants des processus, accessibles à tous les rôles :

- **Sur chaque fiche** — commentaires publics ou notes de travail, historique complet
  (journal d'audit), liens vers les autres enregistrements, cas similaires ;
- **Notifications** — cloche avec compteur : affectations, décisions attendues et rendues,
  échéances (SLA en risque ou dépassé, revues, actions en retard) ;

- **Ma console** — page d'accueil orientée action, composée côté serveur selon le rôle
  (règles : [`regles-metier.md`](regles-metier.md#2-consoles-par-rôle)) ;
- **Recherche** — recherche hybride (mots et sens) sur les contenus validés, cas
  similaires sur les fiches ([recherche](recherche.md)) ;
- **Périmètres** — droits et visibilité limités à une direction, un site ou une entité,
  affectations d'utilisateurs et de groupes AD, écran Administration › Périmètres
  ([périmètres](perimetres.md)) ;
- **Administration et Paramétrage** — pour l'Admin : utilisateurs et rôles lus dans
  l'annuaire, santé de la plateforme, journal d'audit global et export, nettoyage des
  doublons ; pour les gestionnaires et l'Admin : catégories d'incident et de problème,
  découverte du réseau ([administration](administration.md)) ;
- **Fonctionnalités optionnelles** — activées ou coupées par
  l'Admin, exécutées hors de l'API, dans un conteneur à part des fonctions de base :
  **tri assisté des tickets** (suggestions et
  panne probable sur les incidents), **CMDB assistée** (propositions d'un modèle de langage,
  dans le SI ou hébergé, au choix de l'organisation), **serveur MCP** (S-Aloha en lecture
  pour les assistants des utilisateurs) ([fonctionnalités optionnelles](fonctionnalites-optionnelles.md)) ;
- **Reporting** — tableau de bord à onglets : score de santé des services, incidents
  (MTTR, MTTD, résolution au premier contact, SLA), problèmes, changements et projets,
  conformité ; filtres période et service, exports CSV et PDF ([reporting](reporting.md)).

## Les processus

Chaque processus reprend l'objectif de la pratique ITIL 4 correspondante. Règles
détaillées, pratique par pratique : [`processus/`](processus/readme.md).

| Processus | Objectif ITIL 4 | Ce que fait le module |
|---|---|---|
| Incidents | Minimiser l'impact négatif des incidents en rétablissant le service normal au plus vite | Déclaration, priorité P1–P4, assignation AD ; incident majeur piloté par un gestionnaire ; échéances de prise en charge et de résolution en heures de service, SLA en risque ; attente motivée, réouverture encadrée ; résolution avec code ; problème lié |
| Demandes | Délivrer la qualité de service convenue en traitant les demandes prédéfinies des utilisateurs, de façon efficace et conviviale | Catalogue de demandes et modèles pré-approuvés, bénéficiaire ; approbation par un gestionnaire ; échéance et groupe d'exécution ; retards en relance ; annulation par le demandeur |
| Changements | Maximiser le nombre de changements réussis : évaluer les risques, autoriser, gérer le calendrier | Types Standard (depuis un modèle pré-autorisé), Normal, Urgent ; autorité selon le risque, CAB, séparation des tâches ; périodes de gel ; plans de mise en œuvre et de retour arrière ; calendrier et conflits ; dates réelles, revue post-implémentation |
| Configuration | Une information exacte et fiable sur les services et les CI, quand et où elle est nécessaire | CI typés par environnement, propriétaire ; relations et impact en amont et en aval ; rattachement aux services ; vérification ; retrait encadré ; portefeuille applicatif et actifs matériels, inventaire et fins de support ; découverte automatique (balayage IP et bannières, SNMP v2c et v3, annuaire, inventaire SSH et WinRM, hyperviseurs Proxmox VE et vCenter) avec sas de staging, coffre d’identifiants, et relations « Héberge » et « Se connecte à » proposées au gestionnaire |
| Niveaux de service | Des cibles claires, orientées métier, et le suivi de la prestation au regard de ces cibles | Catalogue des services ; SLA en heures de service (délais de prise en charge et de résolution P1–P4) ; respect mesuré, alerte sous la cible ; expiration et revues |
| Connaissances | Un usage efficace, efficient et pratique de l'information et des connaissances | Articles (solution, procédure, erreur connue, FAQ) en Markdown ; soumission puis publication validée ; revue périodique et demande d'archivage ; articles d'erreur connue créés depuis les problèmes ; compteur d'utilisation |
| Amélioration continue | Aligner les services sur l'évolution des besoins par l'amélioration continue | Registre priorisé (source, priorité), mesures de départ et cible, modèle ITIL 4 en 7 étapes, validation ; propositions sur problèmes récurrents et SLA sous la cible ; retards en relance |
| Conformité NIS 2 | Protéger l'information en gérant les risques pour sa confidentialité, son intégrité et sa disponibilité | Évaluation au Référentiel Cyber France (ANSSI) : questionnaire noté, scores et maturité, écarts et actions d'amélioration, preuves liées, comparaison de deux évaluations, export CSV, revue annuelle, version du référentiel figée ([conformité NIS 2](processus/conformite-nis2.md)) |
| Déclarations réglementaires | Protéger l'information : déclarer les incidents de sécurité aux autorités dans les délais légaux | Incident marqué « sécurité » par tout rôle, qualifié par un gestionnaire (incident important au sens de NIS 2, violation de données personnelles) ; une déclaration par régime retenu, échéances en heures calendaires (alerte précoce 24 h, notification 72 h, rapport final), contenu complété par tous et figé à chaque envoi, motifs du retard RGPD ; échéances proches dans la console, rappels, respect des délais au reporting ([déclarations réglementaires](processus/declarations-reglementaires.md)) |
| Gestion de projet | Faire aboutir les projets en planifiant, déléguant, suivant et maîtrisant tous leurs aspects | La méthode (Scrum, Kanban, Cycle en V, PRINCE2, hybride) active les composants : backlog, sprints et burndown, tableau, phases ou étapes, jalons, livrables, risques, business case, tolérances, prévision de la date de fin ; validation par un gestionnaire ; console des projets ([ADR-0015](../decisions/adr-0015-gestion-de-projet-methode-active-composants.md)) |

Tous les enregistrements se **relient** entre eux par leur référence (incident → problème,
problème → changement, changement → CI, problème → article d'erreur connue…).

### Gestion des problèmes

1. **Détection et enregistrement** : déclaration avec impact × urgence → priorité P1–P4
   calculée, référence `PRB-AAAA-NNNN`.
2. **Investigation et diagnostic** : analyses de cause racine — **5 Pourquoi**,
   **Ishikawa (6M)**, **arbre des défaillances (FTA)** avec portes ET/OU.
3. **Erreur connue** : statut dédié et contournement documenté.
4. **Résolution** : actions correctives avec échéance et matrice **RACI**, chaque rôle
   affecté à un utilisateur ou groupe AD ; un **gestionnaire du problème** pilote le dossier.
5. **Suivi et clôture** : suivi transverse des actions, retards signalés, MTTR.

Règles détaillées : [gestion des problèmes](processus/gestion-des-problemes.md).

## Ce que S-Aloha refuse de devenir

- **Un outil SaaS multi-locataire** : S-Aloha se déploie on-prem, chez une organisation,
  contre son Active Directory.
- **Un gestionnaire de comptes** : aucun mot de passe ni utilisateur n'est stocké ;
  l'identité et les rôles viennent de l'AD.
- **Une collection d'applications** : les processus partagent un socle (identité,
  annuaire, console, reporting) et une navigation uniques.
- **Un outil qui décide à la place des équipes** : les fonctionnalités optionnelles
  suggèrent et proposent ; une personne décide, et le journal d'audit garde l'origine de
  chaque modification.
