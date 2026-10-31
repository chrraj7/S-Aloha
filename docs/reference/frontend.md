# 🖥️ Frontend

> React 19 + Vite 8 + react-router 7, servi par nginx. Aucune bibliothèque de composants : CSS natif à jetons ([ADR-0004](../decisions/adr-0004-conserver-le-stack-dotnet-react.md), [ADR-0005](../decisions/adr-0005-langage-visuel-a-jetons-en-css-natif.md)).

<sub>[← Documentation](../readme.md) · [Produit](produit.md) · [Règles métier](regles-metier.md) · [Architecture](architecture.md) · [Base de données](base-de-donnees.md) · [Frontend](frontend.md) · [Exploitation](exploitation.md) · [Sécurité](securite.md) · [Glossaire](glossaire.md)</sub>

## Structure

```
frontend/
├── index.html                    # métadonnées, script de thème avant rendu
├── nginx.conf                    # sert le build, proxifie /api/ vers le service api
├── public/favicon.svg            # emblème sur tuile marine (ADR-0021)
└── src/
    ├── main.jsx
    ├── api.js                    # client fetch (cookie de session HttpOnly, en-tête X-S-Aloha, ADR-0018) ; 401 ⇒ retour /login
    ├── App.jsx                   # déclaration des routes
    ├── styles.css                # jetons, thèmes, composants — voir § Design
    ├── assets/fonts/             # Geologica, IBM Plex Sans et Mono (woff2, OFL)
    ├── core/                     # socle UI, transverse aux processus
    │   ├── about.js              # URL du code source et licence (AGPLv3, VITE_SOURCE_URL)
    │   ├── fields.js             # champs communs (titre, description, responsable AD), formats de date
    │   ├── itemTypes.js          # types reliables → libellé et page (miroir de ItemLinks côté API)
    │   ├── components/
    │   │   ├── Layout.jsx        # sidebar (desktop) / tiroir (mobile), construite depuis le registre
    │   │   ├── ThemeToggle.jsx   # bascule clair / sombre
    │   │   ├── icons.jsx         # icônes SVG en ligne
    │   │   ├── DirectoryPicker.jsx # recherche dans l'annuaire AD (utilisateur ou groupe)
    │   │   ├── RaciEditor.jsx    # éditeur RACI (rôle + DirectoryPicker)
    │   │   ├── RecordList.jsx    # registre d'un processus : filtres, création, tableau
    │   │   ├── RecordDetail.jsx  # fiche : en-tête, éditeur, contenu propre, éléments liés
    │   │   ├── RecordForm.jsx    # formulaire décrit par des champs ; conversions API ↔ formulaire
    │   │   ├── StatusBadge.jsx   # badge de statut selon la tonalité déclarée
    │   │   ├── Comments.jsx      # fil de commentaires d'une fiche, public ou note de travail (SOC-24)
    │   │   ├── PerimeterSelect.jsx # choix du périmètre (création, déplacement), masqué avec un seul périmètre ; core/usePerimeters.js
    │   │   ├── ServiceSelect.jsx # choix du service dans le catalogue (SLM-10) ; core/useServices.js (withServices pour les configurations)
    │   │   ├── Markdown.jsx      # rendu Markdown sûr (éléments React, jamais de HTML brut), KB-10
    │   │   └── LinkedItems.jsx   # éléments liés (tous processus), ajout par référence
    │   └── pages/
    │       ├── Login.jsx         # panneau de marque (fonctionnalités : FEATURES), formulaire AD
    │       ├── Console.jsx       # « Ma console », orientée action
    │       ├── Reporting.jsx     # tableau de bord à onglets, filtres période et service, exports
    │       └── reporting/        # un composant par onglet, charts.jsx (graphiques SVG), exportCsv.js
    └── modules/
        ├── registry.js           # registre des processus ITIL — source unique de la navigation
        ├── problem-management/
        │   ├── components/       # FiveWhys, Ishikawa, FtaTree
        │   └── pages/            # Problems, ProblemDetail (onglets), Actions
        ├── incident-management/          # config.jsx + pages Incidents, IncidentDetail
        ├── service-request-management/   # config.jsx + pages Requests, RequestDetail
        ├── change-enablement/            # config.jsx + pages Changes, ChangeDetail, ChangeSchedule
        ├── service-configuration-management/ # config.jsx + pages ConfigurationItems, ConfigurationItemDetail
        ├── service-level-management/     # config.jsx + pages Services, ServiceDetail, Agreements, AgreementDetail
        ├── knowledge-management/         # config.jsx + pages Articles, ArticleDetail (onglet Lecture, cycle de l'article)
        ├── continual-improvement/        # config.jsx + pages Improvements, ImprovementDetail
        └── project-management/           # config.jsx, rights.js, pages Projects, ProjectDetail ;
                                          # components : Overview, Agile, BurndownChart, Registers, PartTable
```

### Modules décrits par configuration

Hors problèmes, chaque module tient dans un `config.jsx` lu par les composants génériques
du socle (`RecordList`, `RecordDetail`) : client d'API (`api.js` › `records(path)`),
`basePath`, `linkType`, statuts avec tonalité (`tone` : `new`, `progress`, `wait`, `done`,
`bad`, `closed` — classes `.badge.t-*`) et marque `manager` des statuts réservés, champs du
formulaire (`text`, `textarea`, `select`, `date`, `datetime`, `number`, `checkbox`, `person`),
colonnes du registre, badges et mentions de l'en-tête. Un champ peut dépendre des autres
valeurs (`when(form)` : le portefeuille applicatif ne s'affiche que pour une application,
l'identification matérielle que pour un équipement), porter une aide (`hint`, texte ou
fonction des valeurs) ; `type: 'heading'` est un intertitre de bloc, sans valeur. Une page n'ajoute que ce qui est
propre au processus :

- `tab = { label, render(record, reload) }` : l'**onglet propre à la pratique** — Conflits
  (changement), Relations et impact (CI), Accords (SLA) (service) ; une pratique à
  plusieurs onglets passe une **liste** `[{ label, render }]` (évaluation NIS 2 :
  Questionnaire, Synthèse, Comparaison ; changement : Conflits, Impact) ;
- `children(record, reload)` : complément de l'onglet **Informations** (ex. « Ouvrir un
  problème lié » et l'encadré « Cas similaires » sur un incident ; « Cas similaires » sur un
  problème ; services touchés et vérification sur un CI) ;
- `RecordList` accepte un `intro` affiché sous le titre du registre (bloc « Chercher
  d'abord » des connaissances).

`RecordDetail` affiche les onglets dans l'ordre **Informations → onglet de la pratique →
Liens** (Liens si `config.linkType`). Un onglet vide affiche un message explicite ; une
fiche s'ouvre toujours sur Informations. La fiche Problème, hors socle, suit le même ordre :
Informations · Analyse de cause racine · Actions correctives · Liens. La liste des statuts
ne propose que le statut courant et ses successeurs permis (`core/useTransitions.js`, graphe
servi par l'API, SOC-05) ; les statuts réservés aux gestionnaires y sont désactivés pour
les autres rôles. L'API reste seule juge.

Règle de dépendance : `modules/*` peut importer `core/*` ; `core/*` n'importe aucun module,
sauf le registre `modules/registry.js` (lu par `Layout`).

## Routes

| Route | Page | Section de navigation |
|---|---|---|
| `/login` | `core/pages/Login` | — (publique) |
| `/` | `core/pages/Console` | Pilotage |
| `/reports` | `core/pages/Reporting` | Pilotage |
| `/problems` | `modules/problem-management/pages/Problems` | Pratiques › Problèmes |
| `/problems/:id` | `modules/problem-management/pages/ProblemDetail` | Pratiques › Problèmes |
| `/actions` | `modules/problem-management/pages/Actions` | Pratiques › Problèmes |
| `/incidents`, `/incidents/:id` | `modules/incident-management/pages/*` | Pratiques › Incidents |
| `/requests`, `/requests/:id`, `/requests/catalog` | `modules/service-request-management/pages/*` (registre, fiche, catalogue de demandes) | Pratiques › Demandes |
| `/changes`, `/changes/schedule`, `/changes/:id` | `modules/change-enablement/pages/*` | Pratiques › Changements |
| `/configuration`, `/configuration/:id` | `modules/service-configuration-management/pages/ConfigurationItems`, `ConfigurationItemDetail` | Pratiques › Configuration |
| `/configuration/inventory` | `modules/service-configuration-management/pages/Inventory` | Pratiques › Configuration › Inventaire |
| `/configuration/staging`, `/configuration/staging/:id` | `modules/service-configuration-management/pages/Staging`, `StagingAsset` | Pratiques › Configuration › Sas de staging (Manager, Admin) |
| `/services`, `/services/:id`, `/agreements`, `/agreements/:id` | `modules/service-level-management/pages/*` | Pratiques › Niveaux de service |
| `/knowledge`, `/knowledge/:id` | `modules/knowledge-management/pages/*` | Pratiques › Connaissances |
| `/improvements`, `/improvements/:id` | `modules/continual-improvement/pages/*` | Pratiques › Amélioration (CSI) |
| `/assessments`, `/assessments/:id` | `modules/compliance-assessment/pages/Assessments`, `AssessmentDetail` | Conformité & réglementation › NIS 2 |
| `/projects`, `/projects/:id` | `modules/project-management/pages/Projects`, `ProjectDetail` | Projets › Projets |
| `/admin/perimeters`, `/admin/perimeters/:id` | `core/pages/admin/Perimeters`, `PerimeterDetail` | Administration › Périmètres (Admin) |
| `/preferences` | `core/pages/Preferences` | Mes préférences : notifications par courriel (SOC-22), icône sous l'utilisateur dans la barre latérale |
| `/admin/users`, `/admin/platform`, `/admin/health`, `/admin/audit`, `/admin/duplicates` | `core/pages/admin/Users`, `PlatformSettings`, `Health`, `AuditLog`, `Duplicates` | Administration › Utilisateurs et rôles, Messagerie et stockage, Santé, Journal d'audit, Nettoyage des doublons (Admin) |
| `/settings/categories` | `core/pages/settings/Categories` | Paramétrage › Catégories d'incident et de problème (Manager, Admin) |
| `/settings/discovery` | `modules/service-configuration-management/pages/DiscoverySettings` | Paramétrage › Découverte du réseau (Manager, Admin) ; onglets `?tab=` : tâches, identifiants, fins de support, clés et certificats, constructeurs (OUI) |
| `/settings/nis2` | `modules/compliance-assessment/pages/ReferentialSettings` | Paramétrage › Référentiel NIS 2 (Admin) |

Toutes les routes sauf `/login` passent par `Private` (jeton présent) et sont rendues dans
`Layout`. Les droits réels sont contrôlés par l'API.

## Registre des modules

`frontend/src/modules/registry.js` exporte :

- `GROUPS` — les groupes de modules, dans l'ordre de la barre latérale : **Pratiques**
  (`id: 'practices'`, les pratiques ITIL), **Projets** (`id: 'projects'`, `tone: 'projects'`)
  et **Conformité & réglementation**
  (`id: 'compliance'`, `tone: 'compliance'`, obligations hors ITIL : NIS 2, et demain les
  déclarations réglementaires). Un groupe sans module visible n'est pas affiché ;
- `MODULES` — les modules, dans l'ordre du cycle de vie pour les pratiques : `id`, `label`,
  `description`, `status` (`active` | `soon`), `group` (clé de `GROUPS` ; absent,
  `'practices'`, lu par `groupOf(module)`) et, pour un module actif, `href`
  (point d'entrée), `routes` (préfixes d'URL qui l'activent dans la navigation), `pages`
  (sous-entrées affichées quand le module est ouvert, facultatives ; quand une sous-page en
  prolonge une autre — `/changes/schedule` sous `/changes` — la correspondance exacte l'emporte,
  puis le préfixe le plus long : la fiche `/configuration/staging/3` active « Sas de staging »,
  pas le registre `/configuration`) ;
- `SECTIONS` — les sections de menu après les groupes de modules : **Paramétrage**
  (`id: 'settings'`, `roles: ['Manager','Admin']`) et **Administration**
  (`id: 'administration'`, `roles: ['Admin']`), chacune avec ses `entries`. Une entrée :
  `{ href, label, description, icon, roles? }`, où `icon` est une clé de `SECTION_ICONS`
  **propre à l'entrée** (`'categories'`, `'discovery'`, `'users'`, `'health'`…) : deux
  entrées ne partagent jamais une icône, sans quoi on ne les distingue plus dans le rail
  (vérifié par `Layout.rail.test.jsx`, modules compris). Une section n'est affichée que si au moins une de ses
  entrées est visible pour le rôle (pas de section vide, vérifié par
  `Layout.sections.test.jsx` sur le registre réel). Le titre de la section
  Administration porte un marqueur ambre (`.nav-section.admin`) ;
- `roles` (section, module ou sous-entrée, facultatif) : rôles qui voient l'entrée ; absent, tous.
  `visibleFor(entry, role)` décide ; la barre latérale masque les entrées réservées (l'API
  reste seule juge des droits) ;

**Sous-entrée filtrée** : un `href` peut porter un filtre de registre
(`/problems?status=Erreur+connue`) ; la page lit ce filtre dans l'URL et adapte son titre.
Le filtre s'encode avec `URLSearchParams` **des deux côtés** (registre et page) : l'espace
devient « + », sinon les URL ne se comparent pas.

**Sous-entrée active** : correspondance de l'URL complète (chemin et filtre), puis du
chemin, puis du préfixe. Les sous-entrées sont des `Link` (et non des `NavLink`, qui ne
compare que le chemin) ; l'entrée active porte `aria-current="page"`.

Un module `soon` apparaît grisé, non cliquable, avec le badge « Bientôt ».

### Ajouter un module

1. Créer `src/modules/<processus>/` : un `config.jsx` (voir § Modules décrits par
   configuration) et des pages qui passent cette configuration à `RecordList` et
   `RecordDetail` ; ajouter le client dans `api.js` et le type dans `core/itemTypes.js`.
2. Déclarer ses routes dans `App.jsx`, sous le commentaire du processus.
3. Dans `registry.js`, passer le module à `status: 'active'` et renseigner `href`, `routes`,
   `pages`. Rien d'autre à toucher dans la navigation.
4. Côté backend : [`architecture.md`](architecture.md#3-organisation-du-code--socle-et-modules).
5. Mettre à jour [`produit.md`](produit.md#cartographie-des-processus-itil-et-état).

## Navigation cible

> Consigne pour la conception de l'interface (agent Design compris) : la navigation
> **dérive du [référentiel fonctionnel](processus/readme.md)**. Chaque entrée ci-dessous
> renvoie à la règle qui la justifie ; une entrée 🔜 apparaît dans le menu **quand sa
> règle est implémentée**, jamais avant (pas de page vide). Le registre
> `frontend/src/modules/registry.js` reste la source unique du menu.

### Principes

- **Ordre du cycle de vie du service** pour les pratiques (incidents → demandes →
  problèmes → changements → configuration → niveaux de service → connaissances →
  amélioration), comme aujourd'hui.
- Chaque pratique ouvre sur son **registre** ; les sous-entrées sont les autres **objets**
  de la pratique (§ 3 de son document) et ses **vues** (calendrier, impact).
- **Six sections**, dans cet ordre : **Pilotage** (tous), **Pratiques** (tous, les
  8 pratiques ITIL seulement), **Projets** (tous), **Conformité & réglementation** (tous),
  **Paramétrage** (Manager, Admin) et **Administration** (Admin seul).
- **Projets** = la conduite des projets de la DSI ([gestion de projet](processus/gestion-de-projet.md)) :
  une démarche bornée dans le temps, pas une pratique de service ; la mettre à part garde
  aux Pratiques l'ordre du cycle de vie du service.
- **Conformité & réglementation** = les **obligations réglementaires** qui ne relèvent
  pas d'une pratique ITIL (NIS 2, déclarations réglementaires…). Les mêler aux pratiques
  surchargerait la liste et brouillerait l'ordre du cycle de vie. Un futur module
  réglementaire y déclare `group: 'compliance'` dans le registre.
- **Sections repliables** : Pratiques, Projets, Conformité & réglementation, Paramétrage et
  Administration ont un titre-bouton (`aria-expanded`, chevron) ; Pilotage ne se replie
  pas. Le choix est mémorisé dans le navigateur (`localStorage`, clé
  `s-aloha-nav-collapsed` ; confort seulement, sans échec si le stockage est bloqué) et
  partagé par la barre latérale et le tiroir mobile. La section qui contient la **page
  ouverte reste toujours dépliée** : on ne cache jamais où l'on se trouve.
- Un **titre de section long** passe à la ligne au lieu d'être tronqué, dans la barre
  latérale comme dans le tiroir mobile.
- **Paramétrage** = le **fonctionnement des pratiques** : catalogues, modèles, périodes,
  référentiels qui règlent la façon dont une pratique s'exerce. Délégable aux
  gestionnaires. **Administration** = la **plateforme** elle-même : accès et rôles,
  sécurité, santé, traçabilité. Admin seul.
- Une **file de travail** (brouillons à publier, CI à vérifier) n'est pas du paramétrage :
  elle reste une sous-entrée de sa pratique, éventuellement réservée par `roles`.
- Une section n'apparaît que si elle a **au moins une entrée visible** pour le rôle : pas de
  section vide (registre `SECTIONS`, champ `roles` des sections et des entrées).
- Ce qui est **propre à un enregistrement** (historique, commentaires, liens, impact) est un
  **onglet de sa fiche**, pas une entrée de menu.
- **Pilotage** reste transverse à toutes les pratiques.

### Pilotage

| Entrée | Visible par | Règle | Statut |
|---|---|---|---|
| Ma console (filtre « Périmètre » dès que l'on en a plusieurs, PER-09) | Tous | [socle § 2](regles-metier.md#2-consoles-par-rôle) | ✅ |
| Reporting : tableau de bord à onglets (synthèse, incidents, problèmes, changements et projets, conformité, configuration et inventaire), période, service, export CSV et PDF | Tous | [REP-01 à REP-53](reporting.md), [SOC-18, SOC-19](regles-metier.md#7-reporting) | ✅ |
| Notifications : **cloche** au pied de la barre latérale (barre supérieure en mobile) avec compteur, pas une entrée de menu ; `core/components/NotificationBell.jsx` | Tous | [SOC-21](regles-metier.md#8-notifications) | ✅ |

### Pratiques

| Pratique | Sous-entrée | Visible par | Règle | Statut |
|---|---|---|---|---|
| [Incidents](processus/gestion-des-incidents.md) | Registre des incidents | Tous | INC-01 | ✅ |
| | Filtres prédéfinis du registre : mes incidents, non pris en charge, SLA en risque, majeurs | Tous | INC-06, INC-24, INC-12 | ✅ (filtres de base) / 🔜 |
| [Demandes](processus/gestion-des-demandes.md) | Registre des demandes | Tous | REQ-01 | ✅ |
| | Nouvelle demande **depuis le catalogue** (choix d'un modèle) | Tous | REQ-20 | ✅ |
| | Catalogue de demandes (modèles) | Tous ; écriture Manager | REQ-20 | ✅ |
| [Problèmes](processus/gestion-des-problemes.md) | Registre des problèmes | Tous | PRB-01 | ✅ |
| | Actions correctives (suivi transverse) | Tous | PRB-15 | ✅ |
| | Erreurs connues : sous-entrée `/problems?status=Erreur+connue` | Tous | PRB-11 | ✅ |
| [Changements](processus/habilitation-des-changements.md) | Registre des changements | Tous | CHG-01 | ✅ |
| | Calendrier des changements (conflits, **périodes de gel**) | Tous | CHG-08, CHG-09, CHG-25 | ✅ |
| | Modèles de changement standard | Tous ; écriture Manager | CHG-26 | ✅ |
| [Configuration](processus/gestion-de-la-configuration.md) | Registre des CI (filtre par type) | Tous | CFG-01, CFG-07 | ✅ |
| | Inventaire : portefeuille applicatif et actifs matériels, export CSV, impression | Tous | CFG-39 | ✅ |
| | Sas de staging : tuiles-filtres (à traiter, en conflit, inconnus, rapprochés, ignorés, relations proposées), fiche d'un actif avec la décision (rapprocher du CI proposé, créer le CI pré-rempli, rapprocher d'un autre CI, ignorer, rétablir), la comparaison découvert / CMDB, et ce que la découverte a vu (bannières, interfaces, services, ports en écoute, logiciels filtrables) ; vue Relations proposées (« Héberge », « Se connecte à » : accepter une fois les deux actifs rapprochés, ou écarter) | Manager, Admin | CFG-40 à CFG-42, CFG-49 à CFG-56 | ✅ |
| | CI à vérifier | Tous (propriétaires), Manager | CFG-15 | 🔜 |
| [Niveaux de service](processus/gestion-des-niveaux-de-service.md) | Catalogue des services | Tous | SLM-01 | ✅ |
| | Accords (SLA) | Tous | SLM-02 | ✅ |
| | Respect des SLA (tableau mensuel) | Tous | SLM-20, SLM-21 | 🔜 |
| [Connaissances](processus/gestion-des-connaissances.md) | Base de connaissances, avec le bloc « Chercher d'abord » (recherche hybride) en tête du registre | Tous | KB-04, RAG-05 | ✅ |
| | Brouillons à publier : bloc « Décisions » de la console (KB-21) ; fiche : onglet **Lecture** (Markdown, KB-10) et bloc « Cycle de l'article » (soumission, revue, archivage demandé, utilisation) | Manager, Admin (soumission : tous) | KB-21, KB-10, KB-17 | ✅ (console, fiche) / 🔜 (sous-entrée de menu) |
| [Amélioration](processus/amelioration-continue.md) | Registre d'amélioration | Tous | CSI-01 | ✅ |

### Projets

Section ouverte à **tous les rôles**, repère **vert** ; modules déclarés `group: 'projects'`.

| Module | Sous-entrée | Visible par | Règle | Statut |
|---|---|---|---|---|
| [Projets](processus/gestion-de-projet.md) (« Agile, cycle en V, PRINCE2 ») | Registre des projets | Tous | PRJ-01 | ✅ |

Fiche d'un projet : **Informations** (avec la synthèse PRJ-30 à PRJ-33, et le choix des
composants pour un projet hybride), puis un onglet par **composant actif** — Étapes, Phases,
Jalons, Livrables, Backlog, Sprints (et burndown), Tableau, Business case et tolérances,
Risques, Incidents projet — puis **Liens** et **Historique**. Un composant inactif n'a pas
d'onglet (PRJ-03). Le bouton de validation de fin d'étape n'apparaît que sur l'étape
courante (PRJ-21).

### Conformité & réglementation

Section ouverte à **tous les rôles**, repère **violet** ; elle ne contient que les modules
déclarés `group: 'compliance'` dans le registre.

| Module | Sous-entrée | Visible par | Règle | Statut |
|---|---|---|---|---|
| [NIS 2](processus/conformite-nis2.md) (page « Conformité NIS 2 ») | Registre des évaluations (« Évaluations ReCyF (ANSSI) ») ; fiche : Informations · Questionnaire · Synthèse · Liens | Tous | NIS-02, NIS-04, NIS-10 | ✅ |
| [Déclarations réglementaires](processus/declarations-reglementaires.md) (page « Déclarations réglementaires », `/regulatory-notifications`) | Registre (régime, incident, autorité, prochaine échéance en rouge si dépassée) ; fiche : contenu, envois, échéances et contenu envoyé · Liens · Commentaires · Historique | Tous (décisions : Manager) | DRG-01 à DRG-13 | ✅ |

L'encadré « Déclarations réglementaires » et la qualification restent sur la **fiche d'un
incident cyber** (INC-41) : ce sont des éléments de l'incident, pas des entrées de menu. ✅
La console des gestionnaires les reprend dans « Urgent » (incidents à qualifier, échéances
proches ou dépassées).

### Paramétrage

Section réservée aux **Manager et Admin** ; elle apparaît avec sa première entrée
implémentée. Une entrée par objet de paramétrage, regroupées par pratique dans l'ordre du
cycle de vie.

| Entrée | Visible par | Règle | Statut |
|---|---|---|---|
| Catégories (incidents, problèmes) | Manager, Admin | [ADM-50 à ADM-53](administration.md) | ✅ |
| Catalogue de demandes (modèles) | Manager, Admin | [REQ-20](processus/gestion-des-demandes.md) | 🔜 |
| Modèles de changement standard | Manager, Admin | [CHG-26](processus/habilitation-des-changements.md) | 🔜 |
| Périodes de gel | Manager, Admin | [CHG-25](processus/habilitation-des-changements.md) | 🔜 |
| Import des CI (CSV) | Admin | [CFG-30](processus/gestion-de-la-configuration.md) | 🔜 |
| Découverte du réseau (`/settings/discovery`) : tâches de scan (balayage IP, SNMP, Active Directory, SSH, Proxmox VE, vCenter, WinRM ; cible, port, identifiant, fréquence, fenêtre, lancement, dernier passage), identifiants du coffre (Admin : communautés SNMP v2c, utilisateurs SNMP v3, comptes SSH par mot de passe ou clé, jetons Proxmox, comptes vCenter et Windows ; secret jamais réaffiché), fins de support, clés d'hôte et certificats retenus (acceptation : Admin), constructeurs par préfixe MAC (import OUI : Admin) | Manager, Admin | [CFG-45 à CFG-57](processus/gestion-de-la-configuration.md) | ✅ |
| Heures de service (calendriers) | Manager, Admin | [SLM-14](processus/gestion-des-niveaux-de-service.md) | 🔜 |
| Référentiel NIS 2 : structure embarquée et import du texte des exigences (`/settings/nis2`) | Admin | [NIS-20, NIS-21](processus/conformite-nis2.md) | ✅ |

Le **catalogue des services** reste dans la pratique Niveaux de service : c'est un objet de
la pratique, consulté par tous, pas un réglage.

« Découverte du réseau » est la **première entrée ouverte aux gestionnaires** : la section
Paramétrage apparaît pour eux, avec cette seule entrée (l'onglet du coffre y reste réservé à
l'Admin) ; « Référentiel NIS 2 » reste à l'Admin.

### Administration

Section réservée à l'**Admin** ; elle apparaît avec sa première entrée implémentée.

| Entrée | Contenu | Règle | Statut |
|---|---|---|---|
| Utilisateurs et rôles (`/admin/users`) | Membres des groupes AD de rôle (annuaire), rôle effectif par périmètre et sa source, dernière connexion ; mapping groupes AD → rôles (lecture) ; affectations « sans accès » | [ADM-10 à ADM-13](administration.md) | ✅ |
| Messagerie et stockage (`/admin/platform`) | SMTP (serveur, chiffrement, compte, expéditeur, adresse de l'interface, courriel d'essai) ; stockage S3 (adresse, bucket, clés, taille maximale, essai de dépôt) ; secrets jamais réaffichés | [ADM-60 à ADM-63](administration.md) | ✅ |
| Santé (`/admin/health`) | Base, annuaire, embeddings, coffre, messagerie, stockage, tâches de fond, version déployée | [ADM-20, ADM-21](administration.md) | ✅ |
| Journal d'audit (`/admin/audit`) | Journal global, filtrable par auteur, pratique, action, référence, période ; pages de 100 ; export CSV | [ADM-30, ADM-31](administration.md) | ✅ |
| Nettoyage des doublons (`/admin/duplicates`) | Paires probables côte à côte ; « Conserver … » (fusion confirmée, `DeleteDialog`), « Pas un doublon » | [ADM-40 à ADM-43](administration.md) | ✅ |
| Périmètres | Périmètres (création, renommage, suppression douce, restauration, suppression définitive si rien n'y est rattaché), fiche avec compteurs par pratique et affectations d'utilisateurs ou de groupes AD (Manager, User) | [PER-01 à PER-12](perimetres.md) | ✅ |
| Index de recherche | État du service d'embeddings et de l'index par source, reconstruction ; avis ambre si le service est injoignable | [RAG-08](recherche.md) | ✅ |

« Index de recherche » est la **première entrée réelle** de la section : un Admin voit
les six sections (Pilotage, Pratiques, Projets, Conformité & réglementation, Paramétrage,
Administration), un Manager cinq (sans Administration), un User quatre (sans Paramétrage ni
Administration).

### Onglets communs des fiches

Toutes les fiches d'enregistrement partagent la même structure d'onglets, dans cet ordre ;
un onglet sans contenu reste visible avec un état vide explicite.

| Onglet | Contenu | Règle | Statut |
|---|---|---|---|
| Informations | Champs du § 3, actions de transition (§ 4) en boutons nommés par l'étape (« Prendre en charge », « Autoriser »…) | § 3 et § 4 de chaque pratique | ✅ (champs) / 🔜 (boutons de transition) |
| Suppression (Informations) | Bouton « Supprimer » (Admin ; analyse ou action d'un problème : Manager) ; boîte de dialogue `core/components/DeleteDialog.jsx` qui rappelle la référence et liste ce qui part avec, focus sur « Annuler », Échap pour fermer | SOC-14 | ✅ |
| Propre à la pratique | Analyse de cause racine et Actions correctives (problème), Relations et impact (CI), Accords (SLA) (service), Conflits (changement) | PRB-05, CFG-04, SLM-02, CHG-09 | ✅ |
| Liens | Enregistrements reliés, toutes pratiques ; onglet dédié ; un enregistrement d'un autre périmètre est listé masqué, sans lien ni retrait (PER-08) | SOC-12, PER-08 | ✅ |
| Commentaires | Fil chronologique, public ou note de travail (marquée, invisible du demandeur) ; `core/components/Comments.jsx` | SOC-24 | ✅ |
| Pièces jointes | Liste (nom, taille, auteur, date), téléchargement par lien, dépôt borné en taille et en types, retrait par l'auteur ou un gestionnaire ; avertissement si le stockage n'est pas activé ; `core/components/Attachments.jsx` | SOC-25 | ✅ |
| Historique | Journal d'audit de l'enregistrement (`core/components/History.jsx`) ; un Admin voit aussi, dans la liste des statuts, les transitions hors graphe, marquées « transition forcée », avec motif obligatoire (`StatusField.jsx`) | SOC-20, SOC-05 | ✅ |

Un bouton de transition n'apparaît que si la transition est **permise** depuis le statut
courant (tableau § 4) et **autorisée** pour le rôle (§ 2) ; ses conditions (motif, champ
obligatoire) s'affichent dans une boîte de dialogue avant l'envoi.

### Console

Les blocs à venir de chaque pratique sont listés au § 8 de son document, avec leur groupe
(Urgent, Décisions, Relances, Mon travail). La console garde ses groupes et son ordre
([socle § 2](regles-metier.md#2-consoles-par-rôle)) ; un nouveau bloc rejoint son groupe
sans en créer de nouveau.

## Design

Langage visuel à jetons, en CSS natif sans Tailwind
([ADR-0005](../decisions/adr-0005-langage-visuel-a-jetons-en-css-natif.md)), dans sa
**version 2** : sombre par défaut, surfaces en couches, nouvel emblème
([ADR-0021](../decisions/adr-0021-langage-visuel-v2-sombre-et-nouvel-embleme.md)). Tout est
dans `frontend/src/styles.css`.

### Jetons

Définis sur `:root` (thème clair) et redéfinis sur `.dark` (thème sombre, **par défaut**).
**Toute couleur passe par un jeton** défini dans les deux thèmes.

| Famille | Jetons |
|---|---|
| Fonds (couches 0 à 3) | `--bg-app`, `--bg-surface`, `--bg-surface-raised`, `--bg-surface-hover`, `--bg-overlay` ; `--bg-nav` (barre latérale, sombre dans les deux thèmes) |
| Textes | `--text-primary`, `--text-secondary`, `--text-tertiary`, `--text-disabled`, `--text-on-nav` |
| Bordures et focus | `--border-subtle`, `--border-default`, `--border-strong` (bord des champs, ≥ 3:1), `--focus-ring` |
| Accents | `--accent-{blue,teal,emerald,red,amber,violet}` et leur variante `-soft` ; `--on-accent` (texte sur un aplat d'accent) |
| Graphiques | `--chart-series`, `--chart-reference`, `--chart-1` à `--chart-3` |
| Ombres | `--shadow-sm`, `--shadow-md`, `--shadow-lg`, `--glow-focus` |
| Angles | `--radius-xs` 3 · `--radius-sm` 6 · `--radius-md` 10 · `--radius-lg` 14 px |
| Marque (indépendante du thème) | `--brand-navy`, `--brand-navy-deep`, `--brand-blue`, `--brand-teal`, `--brand-gradient`, `--brand-line`, `--focus-line` ; `--mark-from`, `--mark-to` (trait de l'emblème) |
| Typographie | `--font-display` (Geologica), `--font-sans` (IBM Plex Sans), `--font-mono` (IBM Plex Mono) |

Contrastes vérifiés : tous les textes à **4,5:1** au moins sur toutes les surfaces, dans les
deux thèmes ; chaque accent à 4,5:1 sur sa variante `-soft`.

**Alias historiques** — `--navy`, `--ink`, `--muted`, `--line`, `--blue`, `--blue-soft`,
`--ok`, `--warn`, `--danger` pointent vers les jetons. Ils existent pour les styles en
ligne des pages antérieures ; ne pas les utiliser dans du code nouveau.

### Typographie

- **Polices servies par l'application** (`src/assets/fonts`, licences OFL jointes) :
  Geologica (variable) pour les titres, chiffres clés et le nom de la marque, en graisse 500 ;
  IBM Plex Sans pour le texte ; IBM Plex Mono pour les références (`.row-ref`, `.mono`).
  Aucun appel à un service externe : une installation sans Internet garde sa typographie.
- Échelle en jetons sur `:root` : `--fs-2xs` 11,5 · `--fs-xs` 12 · `--fs-sm` 13 ·
  `--fs-base` 15 · `--fs-md` 16 · `--fs-lg` 20 · `--fs-xl` 28 · `--fs-2xl` 32 px ;
  `html` à 16 px. **Toute nouvelle taille de police passe par ces jetons**, sans valeur en
  px en dur.
- Tailles courantes : cellules, champs et boutons 14 px, libellés 13, badges et en-têtes de
  colonnes 11,5, titre de page 32 (26 en mobile), sous-titre 16 en `--text-secondary`.

### Emblème

- `BrandMark` (`core/components/icons.jsx`) : un « S » hexagonal d'un seul trait dans un
  hexagone en filet, et un losange au départ du trait — le « premier point » d'*Aloha*.
  Trait en dégradé `--mark-from` → `--mark-to` (bleu → turquoise de la marque) ; sur la
  carte de connexion en thème clair, encres foncées (`#1463d6` → `#00745c`).
- Rendu dans la barre latérale (32 px), la barre mobile et la page de connexion, sans tuile
  de fond ; chaque instance a son propre identifiant de dégradé (`useId`).
- Favicon (`public/favicon.svg`) : l'emblème sur une tuile marine arrondie.

### Principes

- **Angles adoucis** par les jetons `--radius-*` : badges et puces 3 px, boutons, champs et
  liens de navigation 6 px, cartes, tableaux et tuiles 10 px, fenêtres modales 14 px.
- **Barre latérale** de 248 px sur `--bg-nav`, toujours sombre, en sections : *Pilotage*
  (Console, Reporting), *Pratiques* (les pratiques ITIL), *Projets*, *Conformité &
  réglementation*,
  puis *Paramétrage* et *Administration* selon le rôle, **ancrées au pied de la liste**, à
  l'écart du travail quotidien (§ Navigation cible). Le lien actif porte un fond bleu à 16 %
  et un liseré au dégradé de la marque.
- **Entrées compactes** (36 px) : icône et libellé seuls ; la description du registre
  passe en infobulle. Les sous-entrées du module ouvert suivent un fil vertical aligné sur
  l'icône. Seule la liste défile : marque et pied restent visibles.
- **Pied** : avatar, nom et rôle, cloche, thème ; puis une rangée d'icônes (*Mes
  préférences*, *Se déconnecter*, *Réduire le menu*) et le lien *Code source* (AGPLv3).
- **Rail** (bureau seulement) : *Réduire le menu* ramène la barre à 64 px d'icônes,
  choix mémorisé dans le navigateur (`localStorage`, clé `s-aloha-nav-rail` ; confort
  seulement). Les libellés restent lus par les lecteurs d'écran ; les titres de section
  deviennent un filet à losange, toutes sections dépliées. Au survol ou au focus, un **menu
  volant** donne le libellé, la description et les sous-entrées ; *Paramétrage* et
  *Administration* y tiennent chacune en une icône dont le menu volant liste les entrées.
  Le tiroir mobile reste toujours déployé.
- **Repères de section** : un **losange** devant le titre (le premier point de l'emblème) —
  bleu pour Pilotage et les pratiques, **turquoise** pour Projets (`.nav-section.projects`),
  **violet** pour Conformité & réglementation (`.nav-section.compliance`), **ambre** pour
  Administration (`.nav-section.admin`). Le même losange, au ton de la liste, précède le
  titre des listes de la console.
- **Le dégradé de la marque** est réservé à l'emblème, au liseré du lien actif et au
  soulignement de l'onglet actif (`--focus-line`) : il n'habille ni boutons ni fonds.
- **Canevas** (`.bg-canvas`) : grille fine de 24 px et lueur bleue en coin, pour éviter un
  fond plat sur les pages peu denses.
- **Badges** de statut, de priorité et de rôle RACI, en casse de phrase ; tableaux cliquables.
- **Accessibilité** : `:focus-visible` en anneau `--focus-ring` de 2 px, décalé de 2 px ;
  `aria-label` sur les boutons-icônes.
- **Impression** (`@media print`, `core/printTheme.js`) : la coque, bornée à la fenêtre à
  l'écran (`.shell` à 100vh, `.main` qui défile), se déroule entièrement — sinon une seule
  hauteur d'écran est imprimée ; impression **toujours en thème clair** (classe `dark` retirée
  à `beforeprint`, rendue à `afterprint`) ; couleurs du contenu conservées
  (`print-color-adjust: exact`) pour que barres, pastilles et badges restent sur le papier.
- **Garde d'erreur** (`core/components/ErrorBoundary.jsx`) : une exception au rendu affiche
  « Cette page n'a pas pu s'afficher » (recharger, retour à la console, détail technique
  replié) au lieu d'une page blanche. Une garde entoure la page ouverte dans `Layout` — la
  navigation reste utilisable, et changer de page la réarme (`key` = chemin) ; une garde
  racine (`main.jsx`) couvre la coque elle-même.
- **Jamais de composant déclaré dans le rendu d'un autre** : React le recrée à chaque
  rendu et la saisie perd le focus (défaut M1 de l'arbre des défaillances). Un sous-composant
  se déclare au niveau du module (ex. `FtaNode` dans `FtaTree.jsx`).

### Console

`core/pages/Console.jsx`, alimentée par `GET /api/console` (contenu par rôle :
[règles métier § 2](regles-metier.md#2-consoles-par-rôle)).

- En-tête : date et rôle, « Bonjour, <prénom> », phrase propre au rôle.
- Tuiles de synthèse (`.c-tile`) : Urgent, Décisions, Relances, Mon travail ; une tuile mène
  à son groupe, une tuile à zéro s'efface.
- Groupes dans l'ordre de traitement : Urgent, Décisions, Relances, Mon travail, À suivre.
- Listes compactes `.c-list` (référence, titre, métadonnées, chevron) plutôt que des
  tableaux ; ton de la liste par classe `tone-red`, `tone-blue`, `tone-amber`,
  `tone-emerald`, `tone-neutral`. Pas de bandeau ni d'émojis ; état vide « Tout est à jour ».

### Page de connexion

- Le panneau de marque (gauche) ne liste pas les modules. De
  haut en bas : marque, « Plateforme ITIL de la DSI », titre « Piloter le service IT, de
  l'incident à l'amélioration. », maxime malgache et sa
  traduction, explication du nom, puis « Ce que vous y faites » : quatre blocs de
  fonctionnalités (constante `FEATURES` de `Login.jsx` : console par rôle, cause racine et
  RACI AD, changements, mesure du service rendu).
- Message et blocs forment un seul ensemble centré verticalement : aucun défilement de
  1024×768 à 1920×1080 ; contraste ≥ 8:1 pour tous les textes du panneau.
- **Un bloc de fonctionnalité correspond à une fonctionnalité réelle** : le mettre à jour
  quand une fonctionnalité change (ex. « Cibles SLA » tant que l'atteinte des SLA n'est pas
  mesurée, chantier F1).

### Thème clair / sombre

- Le thème est une classe `dark` sur `<html>`.
- Il est posé **avant le premier rendu** par un script en tête d'`index.html` : **sombre par
  défaut**, sauf si l'utilisateur a choisi le mode clair (`localStorage`, clé
  `s-aloha-theme`) — [ADR-0021](../decisions/adr-0021-langage-visuel-v2-sombre-et-nouvel-embleme.md).
  Décidé dans React, il ferait flasher l'écran au chargement.
- `ThemeToggle` ne fait que basculer la classe et enregistrer le choix ; si le stockage est
  indisponible (navigation privée), le thème reste en mémoire pour la session.

### Responsive

| Largeur | Comportement |
|---|---|
| ≥ 1024 px | sidebar fixe ; page de connexion avec panneau de marque |
| < 1024 px | sidebar remplacée par une barre supérieure et un **tiroir** ; connexion compacte |
| ≤ 640 px | grilles sur une colonne, tableaux défilants horizontalement |

## Session côté navigateur

- Le **jeton** n'est jamais accessible au frontend : l'API le pose dans un cookie `HttpOnly`
  (S5, [ADR-0018](../decisions/adr-0018-jeton-en-cookie-httponly-et-csp.md)). Le navigateur ne
  garde que le **profil affiché** (`user`, non secret) en `sessionStorage` ; un nouvel onglet
  ou un rechargement retrouve la session par `GET /api/auth/me` (`Private` dans `App.jsx`).
- Chaque requête (`api.js`) porte l'en-tête `X-S-Aloha: 1`, exigé par l'API sur toute
  modification (protection CSRF).
- Déconnexion : `POST /api/auth/logout` (cookie effacé), puis profil oublié.
- Une réponse **401** de l'API (session expirée) oublie le profil et renvoie vers `/login`.
- La CSP de `nginx.conf` autorise le script de thème en ligne d'`index.html` par son empreinte
  SHA-256 : modifier ce script impose de mettre l'empreinte à jour (`src/security/csp.test.js`
  le vérifie).
- Implications de sécurité : [`securite.md`](securite.md).
