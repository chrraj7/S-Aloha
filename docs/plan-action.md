# 🗺️ Plan d'action

<sub>[← Documentation](readme.md)</sub>

> **Ce qu'il reste à faire.** Alimenté par les constats ([`archives/`](archives/readme.md)),
> vidé au fur et à mesure : un chantier terminé quitte ce plan et rejoint le journal du
> jour ([protocole](protocole-documentation.md)).
>
> Le statut de chaque bug vit dans [GitHub Issues](https://github.com/chrraj7/S-Aloha/issues)
> ([ADR-0001](decisions/adr-0001-github-issues-source-de-verite.md)) ; ce plan porte les
> **chantiers** et l'ordre de traitement.
>
> *Mis à jour le 2026-10-06 : [constat initial](archives/etat-initial-2026-10-06.md),
> [revue fonctionnelle](archives/revue-fonctionnelle-2026-10-06.md).*

**Qui** débloque : *code* (réalisable sans arbitrage), *décision* (à trancher d'abord),
*action* (geste d'exploitation hors du dépôt).

## Prochaine séance

*Mise à jour le 2026-11-04.* État : 352 règles ✅ (compte du 2026-11-16, [RAG-10](reference/recherche.md)
comprise : calibrage fait le 2026-11-09), une en lot 3
(RAG-12, indexation des pièces jointes), aucune en lot 2 ([synthèse](reference/processus/readme.md)) ;
tous les indicateurs des pratiques sont au tableau de bord. Le lot 3 des pratiques, F7
(courriels, pièces jointes) et les indicateurs ont été faits le 2026-10-23
([journal](archives/journal-2026-10-23.md)).

**Versions** : préversions 0.1.0 et 0.2.0 retirées le 2026-10-23 (releases et tags) ; la
première version publiée sera **stable**, à la fin de ce plan d'action.

**Reste du plan** : le chantier **F14** (fonctionnalités optionnelles : tri assisté des
tickets, CMDB assistée, serveur MCP), à livrer **avant la version stable** (décision du
2026-11-23, [§ F14](#fonctionnalités-optionnelles-f14)) ; les chantiers à arbitrer
ci-dessous, les points avant mise en production (section 5 : S1, S2 ; S3 et O1 faits le
2026-10-29) et les tâches de communauté (section 8).

**À arbitrer avant de commencer** :

| Chantier | Décision à prendre |
|---|---|
| ~~F7~~ | **Décidé et fait le 2026-10-23** : pièces jointes dans un stockage objet **compatible S3 (Garage)**, courriels par un **SMTP configurable** ; le paramétrage des deux se fait dans la **console d'administration**. [ADR-0020](decisions/adr-0020-pieces-jointes-s3-et-smtp-configurables.md) ; **réglages et essais faits le 2026-10-23** (ADM-60 à ADM-63). Courriels des notifications et préférences par utilisateur faits le 2026-10-23 (SOC-22). Pièces jointes faites le 2026-10-23 (SOC-25 : types bureautiques, texte et images ; nœud Garage dans `docker-compose.yml`) |
| RAG-12 — indexation des pièces jointes | Extraire le texte des documents déposés pour la recherche : quels formats (PDF, bureautique), bibliothèque d'extraction, taille indexée, contenu soumis aux droits de l'enregistrement |
| F12 — référentiels définis par l'Admin | Spécifier d'abord (format d'import, échelle, calcul des niveaux), sur le modèle de l'[ADR-0012](decisions/adr-0012-referentiel-nis2-structure-versionnee-texte-importe.md) |
| F13 phase 4 — agent nomade | Forme de l'agent (service Windows et Linux, langage, signature du binaire), jeton d'enrôlement par périmètre, fréquence d'envoi ; preuves NIS 2 nourries par la découverte |

**Actions en attente (hors dépôt)** :

- **Recette** : automatisée depuis le 2026-10-26 par le job **Recette** de la CI, sur une pile
  neuve à chaque PR ([architecture § Intégration continue](reference/architecture.md#intégration-continue)).
  L'AMD reste un laboratoire d'essais à la demande (déployé le 2026-10-23 avec main jusqu'à
  #172 ; S3 et SMTP essayés le 2026-10-24), hors du chemin de fusion.
- **WinRM** ([CFG-57](reference/processus/gestion-de-la-configuration.md)) à valider sur un
  Windows réel (aucun au laboratoire).
- **Avant la production** : S1, S2 (section 5).
- **Communauté** : [#126](https://github.com/chrraj7/S-Aloha/issues/126) (captures d'écran de la configuration) reste ouverte ; #120 à #125 sont faites.

## 1. Bloquants fonctionnels — le processus est inutilisable

Aucun bloquant ouvert : B1 à B3 corrigés et couverts par des tests le 2026-10-06
([journal](archives/journal-2026-10-06.md)).

## 2. Défauts majeurs — correctifs locaux

M1, M2, M4, M5 et M6 corrigés et couverts par des tests le 2026-10-06
([journal](archives/journal-2026-10-06.md)) ; M7 soldé le 2026-10-11 (PRB-24), M3 le
2026-10-12 (groupes AD dans le jeton, SOC-10, PRB-26). M8 attend une décision (section 4).

## 3. Règles métier que l'API ne tient pas

[Règles métier](reference/regles-metier.md) décrit le comportement **voulu** : c'est le
code qui doit s'y conformer.

| # | Écart | Qui |
|---|---|---|
| ~~R4~~ | ~~Réouverture d'un problème sans motif~~ — soldé le 2026-10-13 ([PRB-10](reference/processus/gestion-des-problemes.md#4-cycle-de-vie), motif exigé) | code |
| ~~R6~~ | ~~AD injoignable : 500 sur l'annuaire, « identifiants invalides » au login~~ — soldé le 2026-10-16 : 503 « Annuaire injoignable » à la connexion et à la recherche | code |
| ~~R7~~ | ~~Annuaire : aucun délai de connexion explicite~~ — soldé le 2026-10-16 : `Ldap:TimeoutSeconds` (5 s) sur la connexion et chaque opération ([audit du 2026-10-16](archives/audit-code-2026-10-16.md) § 3) | code |

## 4. Chantiers à arbitrer

| # | Chantier | Décision à prendre |
|---|---|---|
| ~~A1~~ | ~~**Console d'administration** (Admin = console Manager)~~ | Soldé le 2026-10-19 ([ADR-0019](decisions/adr-0019-console-d-administration.md)) : utilisateurs lus dans l'annuaire avec leur dernière connexion, santé, journal d'audit global, doublons détectés et fusionnés, catégories (SOC-27) ; règles [ADM](reference/administration.md) |

## 5. Avant une mise en production

Détail : [sécurité § Points de durcissement](reference/securite.md#points-de-durcissement-avant-production).

| # | Chantier | Qui |
|---|---|---|
| S1 | **Secrets hors du dépôt** (`Jwt:Key`, mots de passe PostgreSQL et LDAP) | code + action |
| S2 | ~~**LDAPS** et **HTTPS**~~ — fait le 2026-11-09 : LDAPS avec l'autorité de l'annuaire (`Ldap:CaCertificatePath`, [exploitation § LDAPS](reference/exploitation.md#ldaps)) et recette HTTPS (`docker-compose.https.yml`, Caddy, [exploitation § HTTPS](reference/exploitation.md#https)) ; reste à l'exploitant d'activer l'un et l'autre sur son installation | action |
| S3 | ~~**CORS** restreint~~ — fait le 2026-10-29 (`Cors:AllowedOrigins`, vide par défaut, [#122](https://github.com/chrraj7/S-Aloha/issues/122)) ; API et Swagger non publiés hors de nginx | code |
| ~~S7~~ | ~~Refus de démarrer hors développement avec les valeurs d'exemple~~ — soldé le 2026-10-16 (`SampleSecrets` ; démonstration : `Security:AllowSampleSecrets`) ([audit du 2026-10-16](archives/audit-code-2026-10-16.md) § 1) | code |
| ~~S8~~ | ~~Audience du jeton JWT~~ — soldé le 2026-10-16 (`Jwt:Audience`, émise et vérifiée) ([audit du 2026-10-16](archives/audit-code-2026-10-16.md) § 2) | code |
| ~~O1~~ | ~~Démarrage : sondes, rattrapage SLA en tâche de fond, migrations pilotables~~ — soldé le 2026-10-29 : sondes `/health/live` et `/health/ready` ([#121](https://github.com/chrraj7/S-Aloha/issues/121)) ; rattrapage des échéances SLA en tâche de fond et migrations pilotables (`Database:MigrateOnStartup`, option `--migrate`) faits le 2026-10-21 ([audit du 2026-10-16](archives/audit-code-2026-10-16.md) § 4) | code |

## 6. Qualité

| # | Chantier | Qui |
|---|---|---|
| ~~Q1~~ | ~~**Tests** : socle en place (API : xUnit + Testcontainers ; frontend : Vitest), B1–B3 et M1, M2, M4–M6 couverts ; étendre aux scénarios R1–R6~~ — soldé le 2026-11-21 : les scénarios R1 à R6 de la [revue du 2026-10-06](archives/revue-fonctionnelle-2026-10-06.md) ont leur test — R1 `ValidationRulesTests.PRB_02_valeurs_fermees_du_module_problemes`, R2 et R3 `ValidationRulesTests.PRB_17_18_RACI_a_la_modification_et_date_d_achevement`, R4 `ValidationRulesTests.PRB_11_12_13_14_conditions_du_cycle_de_vie` et `TransitionsTests`, R5 `AuditTests` (réaffectation RACI d'une action comprise, [SOC-20](reference/regles-metier.md#9-traçabilité-commentaires-administration)), R6 `DirectoryUnavailableTests` | code |
| Q2 | **CI** : build backend et frontend, `python3 scripts/check-docs.py` | code |
| Q4 | Créer les issues GitHub des sections 1 à 7 et les lier ici ; premières **good first issue** ouvertes le 2026-10-19 pour la communauté : [#120](https://github.com/chrraj7/S-Aloha/issues/120) à [#126](https://github.com/chrraj7/S-Aloha/issues/126), toutes faites sauf [#126](https://github.com/chrraj7/S-Aloha/issues/126) | action |
| Q5 | ~~Mesure de la couverture de tests (coverlet) publiée en CI, sans seuil bloquant~~ — fait le 2026-11-03 ([#123](https://github.com/chrraj7/S-Aloha/issues/123), [architecture § 9](reference/architecture.md#9-tests)) ; reste `dotnet format` ([audit du 2026-10-16](archives/audit-code-2026-10-16.md) § 8) | code |
| ~~Q6~~ | ~~`.dockerignore` pour `backend` et `frontend`~~ — soldé le 2026-10-26 ([audit du 2026-10-16](archives/audit-code-2026-10-16.md) § 9, [#120](https://github.com/chrraj7/S-Aloha/issues/120)) : contexte de construction limité aux sources ([exploitation](reference/exploitation.md)) | code |

## 7. Feuille de route fonctionnelle

Les dix pratiques tiennent leur cycle de vie complet ; toutes ont quitté le stade MVP le
2026-10-16 ([§ Sortie du MVP](#sortie-du-mvp), [cartographie](reference/produit.md#cartographie-des-processus-itil-et-état),
[ADR-0007](decisions/adr-0007-pratiques-itil4-et-socle-commun-des-processus.md)). Les évolutions
restantes sont spécifiées règle par règle dans [`reference/processus/`](reference/processus/readme.md)
(règles 🔜 du lot 3 ; le lot 2 est terminé depuis le 2026-10-21).

| # | Processus | Enrichissement | Règles |
|---|---|---|---|
| ~~F2~~ | Incidents, Problèmes | ~~Service et catégorie tirés du catalogue et d'un référentiel~~ — soldé le 2026-10-19 | [SOC-27](reference/regles-metier.md#9-traçabilité-commentaires-administration), [SLM-10](reference/processus/gestion-des-niveaux-de-service.md) |
| ~~F3~~ | Demandes | ~~Tâches d'exécution créées depuis le modèle~~ — soldé le 2026-10-23 (REQ-24, REQ-25) | [REQ-24, REQ-25](reference/processus/gestion-des-demandes.md) |
| ~~F5~~ | Configuration | ~~Import en masse, attributs par type de CI, exactitude de la CMDB~~ — soldé le 2026-11-04 : import et attributs faits le 2026-10-23, exactitude de la CMDB le 2026-10-23 ([#124](https://github.com/chrraj7/S-Aloha/issues/124), CFG-32), CI à vérifier dans la console le 2026-11-03 ([#125](https://github.com/chrraj7/S-Aloha/issues/125)) | [CFG-16, CFG-30](reference/processus/gestion-de-la-configuration.md) |
| ~~F7~~ | Tous | ~~Pièces jointes, notifications par courriel~~ — soldé le 2026-10-23 (indexation des pièces jointes à arbitrer : RAG-12) | [SOC-22, SOC-25](reference/regles-metier.md#8-notifications) |
| ~~F8~~ | Connaissances, Incidents | ~~Suggestion d'articles et cas similaires dès la saisie, relier en un clic~~ — soldé le 2026-10-21 | [INC-19](reference/processus/gestion-des-incidents.md), [RAG-09, RAG-11](reference/recherche.md) |
| ~~F11~~ | Incidents, sécurité | **Déclarations réglementaires** des incidents cyber (NIS 2 : alerte précoce 24 h, notification 72 h, rapport final ; RGPD : 72 h) : lot 2 fait le 2026-10-21 (qualification de l'incident, déclarations, échéances, console, notifications, respect des délais)  ; modèle de contenu par régime et indicateurs faits le 2026-10-23 — soldé | [DRG-08, DRG-21, DRG-22](reference/processus/declarations-reglementaires.md) |
| F12 | Conformité | 🔜 **Évaluations sur référentiels définis par l'Admin** (ex. CR-CMM, ISO 27002) : l'Admin importe un référentiel en CSV depuis sa console en choisissant les colonnes (domaine ou pratique, niveau, identifiant, question, preuve attendue) et règle l'échelle (ex. Implémenté = 1, Partiel = 0,5, Non = 0 ; un niveau n'est atteint que si tous ses points de contrôle et ceux des niveaux inférieurs sont implémentés). Le contenu est importé par l'organisation et **jamais versionné** dans le dépôt, même logique que l'[ADR-0012](decisions/adr-0012-referentiel-nis2-structure-versionnee-texte-importe.md) : CR-CMM est sous licence CC BY-NC 4.0, usage interne libre, œuvre dérivée ou revente soumises à l'accord écrit de ses auteurs (vérifié le 2026-10-12). Place : section Conformité & réglementation | À spécifier |
| F13 | Configuration, Sécurité | **Découverte automatique du SI et CMDB augmentée** (phases 1 et 2, console de staging, Shadow IT et obsolescence faites le 2026-10-19, bannières, constructeur par préfixe MAC, SNMP v3, interfaces, inventaire SSH, hyperviseurs, flux réseau et WinRM le 2026-10-19 ; agent et preuves NIS 2 à venir) : approche hybride (agentless & agent léger), balayage IP / ICMP sweep, fingerprinting, synchronisation annuaire (AD / LDAP / Entra ID), cartographie des dépendances, protocoles standard (ICMP/ARP, SNMP v2c/v3, WMI/WinRM, SSH, REST), Credentials Vault, zone de staging et réconciliation, détection Shadow IT et alignement NIS 2 (ReCyF) | [Chantier F13](#découverte-automatique-et-cmdb-augmentée-f13) |
| F14 | Incidents, Configuration, socle | 🔜 **Fonctionnalités optionnelles**, exécutées hors de l'API (un conteneur commun, le serveur MCP à part), activées par l'Admin : tri assisté des tickets, CMDB assistée (modèle de langage dans le SI ou hébergé), serveur MCP en lecture seule. **Avant la version stable** (décision du 2026-11-23) | [Chantier F14](#fonctionnalités-optionnelles-f14) |

Ces chantiers sont décidés : leurs règles sont écrites et classées en lots
([plan d'implémentation du lot 1](reference/processus/readme.md#plan-dimplémentation-conseillé--lot-1)).

### Découverte automatique et CMDB augmentée (F13)

Ce chantier introduit le moteur de découverte automatique hybride pour alimenter la CMDB,
historiquement hors périmètre. Il s'appuie sur la sobriété (non-intrusivité, limitation de
bande passante), une zone de staging (tampon et réconciliation avant injection en production),
un coffre-fort d'identifiants chiffré (Credentials Vault), la cartographie des dépendances
et l'alignement NIS 2 (détection du Shadow IT et de l'obsolescence selon le ReCyF de l'ANSSI).
Modèle à 3 niveaux et architecture de la découverte : [§ 3.1](reference/processus/gestion-de-la-configuration.md#31-modèle-à-trois-couches) et [§ 4.2](reference/processus/gestion-de-la-configuration.md#42-découverte-et-sas-de-staging) de la pratique Configuration.

#### Principes directeurs

- **Non-intrusivité et sobriété** : scans asynchrones légers, limitation de bande passante (`SemaphoreSlim`, throttling configurable) pour préserver les équipements critiques et le réseau.
- **Approche hybride (Agent / Sans agent)** : priorité à l'agentless (ICMP, SNMP, WMI/WinRM, SSH, LDAP) pour un déploiement immédiat sans friction ; agent léger autonome pour les postes nomades ou hors réseau local.
- **Vision « Single Source of Truth » (SSOT)** : réconciliation centralisée rattachant directement les CI découverts aux incidents, problèmes, changements et services ITIL.
- **Traçabilité et conformité NIS 2** : identification du Shadow IT, détection des logiciels/OS obsolètes, et alimentation directe des preuves du Référentiel Cyber France (ReCyF ANSSI).

**Reprise décidée le 2026-10-19** : finir la phase 2 d'abord (bannières SSH et HTTP,
constructeur par préfixe MAC, SNMP v3, `ifTable`) ; SNMP v3 avec la bibliothèque
`Lextm.SharpSnmpLib` (licence MIT, la cryptographie USM n'est pas réécrite) ; base OUI
**importée par l'Admin** depuis le fichier public de l'IEEE, rien de tiers versionné (même
logique que l'[ADR-0012](decisions/adr-0012-referentiel-nis2-structure-versionnee-texte-importe.md)). Phase 2 terminée le 2026-10-19 (CFG-49 à CFG-51).

**Phase 3, décidée le 2026-10-19** : livrée dans l'ordre SSH (inventaire Linux/Unix),
hyperviseurs (Proxmox VE et VMware vCenter, relation « Héberge »), cartographie des flux, puis
WinRM ; API cloud reportées. Clé d'hôte SSH en confiance au premier contact (clé changée
refusée jusqu'à acceptation par l'Admin) ; relations déduites **proposées** au gestionnaire,
jamais créées seules (même logique que le sas, CFG-40). Inventaire SSH fait le 2026-10-19
(CFG-52, CFG-53) ; hyperviseurs et relation « Héberge » proposée le 2026-10-19 (CFG-54, CFG-55) ; flux réseau le 2026-10-19 (CFG-56) ; WinRM le 2026-10-19 (CFG-57), **à valider sur un Windows réel** (aucun Windows au laboratoire) ; phase 3 terminée, API cloud reportées.

#### Plan de mise en œuvre par phases

| Phase | Intitulé | Description technique et livrables | Statut |
|---|---|---|---|
| **Phase 1** | **Socle de données, Credentials Vault & Directory Sync** | • Identification technique (IP, MAC, système), portefeuille applicatif (CFG-43, CFG-48) et actifs matériels (CFG-44) en colonnes dédiées ; CPU, RAM et autres attributs avec CFG-16.<br/>• Coffre d'identifiants chiffré AES-256-GCM, clé hors base (`DiscoveryCredentials` : comptes de service Windows, clés SSH, communautés SNMP).<br/>• Collecteur de l'annuaire, à part de `LdapService`, pour les ordinateurs du domaine (`objectCategory=computer`, recherche paginée).<br/>• Sas de staging (`DiscoveredAssets`) et moteur de réconciliation initiale (règles de matching par n° de série, MAC, FQDN). | ✅ (attributs génériques CFG-16 : lot 3) |
| **Phase 2** | **Découverte réseau & SNMP (Agentless)** | • Ordonnanceur de découverte (`DiscoveryJob` : plages IP / CIDR, fréquence et fenêtre hors heures de bureau).<br/>• Moteur de balayage réseau (Ping ICMP sweep, sondes TCP ciblées) et empreinte passive (OUI MAC, TTL, bannières HTTP/SSH).<br/>• Collecte SNMP v2c/v3 (MIB-II, `sysDescr`, `ifTable` pour commutateurs, routeurs, pare-feu, NAS, imprimantes). | ✅ (bannières, OUI MAC, SNMP v3 et `ifTable` : CFG-49 à CFG-51) |
| **Phase 3** | **Inventaire approfondi & Cartographie des dépendances** | • Connecteurs d'administration authentifiés : SSH (Linux/Unix) et WMI/WinRM (Windows) pour inventaire complet (OS, logiciels installés, services actifs).<br/>• Connecteurs REST pour hyperviseurs et Cloud (VMware, Proxmox, AWS/Azure).<br/>• Cartographie automatique des dépendances (*Dependency Mapping*) : déduction des relations *« Héberge »* (hyperviseur $\rightarrow$ VM) et *« Se connecte à »* / *« Dépend de »* (analyse des flux et ports d'écoute actifs). | ✅ SSH, Proxmox VE, vCenter, « Héberge » et flux proposés, WinRM à valider sur Windows réel (CFG-52 à CFG-57) ; 🔜 cloud |
| **Phase 4** | **Agent nomade, Alignement NIS 2 & Console de Staging** | • Agent léger autonome pour postes distants/nomades (`POST /api/discovery/agent-report` avec jeton d'enrôlement par périmètre).<br/>• Détection automatique du Shadow IT (actifs réseau non répertoriés) et de l'obsolescence (corrélation EOL) alimentant le module `ComplianceAssessment` (ReCyF Objectifs 4, 5, 6).<br/>• Interface utilisateur React : console de staging, tableau de réconciliation (diff avant/après) et validation par le Gestionnaire de configuration. | ✅ console de staging, Shadow IT, obsolescence, tableau de bord ; 🔜 agent, preuves NIS 2 |

**Lot 1** : terminé le 2026-10-13 (étapes 1 à 6, périmètres compris, NIS-18) ; RAG-10
(calibrage de la recherche) le 2026-11-09, plancher 0,47. **Lot 2** : terminé le 2026-10-21 ([déclarations réglementaires](reference/processus/declarations-reglementaires.md)
F11 comprises) ; restent les règles 🔜 du lot 3.

### Fonctionnalités optionnelles (F14)

**Décidé le 2026-11-23** ([ADR-0023](decisions/adr-0023-fonctionnalites-optionnelles-et-modele-de-langage.md),
[ADR-0024](decisions/adr-0024-serveur-mcp-conteneur-separe-lecture-seule.md)) : trois
fonctionnalités optionnelles, **hébergées et exécutées hors de l'API** : le tri assisté, la
CMDB assistée et toute fonctionnalité future dans un **conteneur commun** (`features`), le
serveur MCP dans le sien (`mcp`) ; activées ou coupées par l'Admin ; à livrer **avant la
version stable**, qui attend F14. Le service d'embeddings reste à part : la recherche,
fonction de base, en dépend. Spécification, 60 règles en lot 3 : [fonctionnalités optionnelles](reference/fonctionnalites-optionnelles.md)
(OPT : activation, contrat entre l'API et le conteneur des fonctionnalités, modèle de langage),
[tri assisté des tickets](reference/tri-assiste-des-tickets.md) (TRI),
[CMDB assistée](reference/cmdb-assistee.md) (CMA), [serveur MCP](reference/serveur-mcp.md) (MCP).

#### Organisation du code

L'API garde le socle (activation, propositions, port interne) ; le calcul des
fonctionnalités vit dans deux projets .NET à part, construits chacun en image.

```
backend/
├── Core/
│   ├── Features/                       # registre, activation, garde [RequiresFeature], GET /api/features,
│   │                                   # port interne : jetons de conteneur, fil d'événements (OPT-01 à OPT-13)
│   ├── Proposals/                      # propositions, décision, origine au journal d'audit (OPT-08, OPT-14, OPT-15, OPT-17)
│   ├── Platform/                       # + réglages du modèle de langage, à côté du SMTP (OPT-20, OPT-22, OPT-23)
│   └── Auth/                           # + jetons d'accès personnels (MCP-03, MCP-04)
└── Modules/<Pratique>/Controllers/     # + lectures internes et routes minces de la pratique
                                        #   (relais des suggestions, analyses demandées, convention de nommage)
features/                               # hors de l'API
├── SAloha.Features/                    # conteneur features, commun aux fonctionnalités présentes et futures
│   ├── Host/                           #   client du port interne, curseur d'événements, une file
│   │                                   #   par fonctionnalité, activation lue dans l'API
│   ├── LanguageModel/                  #   connecteur, pseudonymisation, validation de schéma (OPT-20 à OPT-28)
│   ├── TicketTriage/                   #   tri assisté des tickets (TRI)
│   └── AssistedCmdb/                   #   CMDB assistée (CMA)
└── SAloha.Mcp/                         # conteneur mcp (MCP), sans jeton propre ni port interne
frontend/src/
├── core/
│   ├── features.js                     # fonctionnalités activées, lues par le registre (OPT-04)
│   ├── pages/admin/                    # + Fonctionnalités optionnelles, Modèle de langage
│   └── components/                     # + encadré commun des propositions
└── modules/
    ├── incident-management/            # + suggestions, bandeau « Panne probable »
    └── service-configuration-management/  # + CMDB assistée, aide au sas
tests/
├── SAloha.Api.Tests/                   # socle : activée et coupée, port interne, propositions, jetons
└── SAloha.Features.Tests/              # conteneurs, contre une API simulée et un faux serveur de modèle
```

Compose : profils `fonctionnalites` (conteneur `features`) et `mcp`, un jeton pour le
conteneur `features`, `/mcp` relayé par nginx ; les deux images sont publiées en `edge`
comme les autres. Une fonctionnalité future est un dossier de plus dans
`SAloha.Features`, sans image ni profil de plus.

#### Étapes

Suivi : [#362](https://github.com/chrraj7/S-Aloha/issues/362) et ses sous-issues ; chaque issue d'étape
propose un découpage en PR de 10 fichiers au plus. Une issue **assignée** est bloquée par la
dépendance qu'elle cite, et désassignée à sa fusion. Une PR par étape ou par sous-partie, sa documentation dans la même PR (règles 🔜 → ✅, référence, journal,
note du journal des versions), ses tests **activée et coupée**. Toutes touchent des
chemins sensibles (migrations, `Program.cs`, authentification, compose) : fusion par le
mainteneur. Les étapes 2 et 6 ajoutent un projet et une image à la CI : étiquette
`outillage`, posée par le mainteneur.

| Étape | Contenu | Règles | Statut |
|---|---|---|---|
| 1 ([#363](https://github.com/chrraj7/S-Aloha/issues/363)) | Socle : registre, Administration › Fonctionnalités optionnelles, `GET /api/features`, garde des routes, origine au journal d'audit | OPT-01 à OPT-08 | ✅ 2026-11-23 |
| 2 ([#364](https://github.com/chrraj7/S-Aloha/issues/364)) | Contrat : port interne, jeton du conteneur, fil d'événements, lectures, propositions et décision ; conteneur `features` (hôte, sans fonctionnalité) | OPT-10 à OPT-17 | ✅ 2026-11-23 (2a à 2c ; conteneur `features` et profil `fonctionnalites` du compose ; OPT-13, lectures internes, avec les étapes 3 et 5) |
| 3 ([#365](https://github.com/chrraj7/S-Aloha/issues/365)) | Tri assisté des tickets dans le conteneur `features` : vote, impact, corrélation par règles et par le sens, panne probable, interface, jeu d'essai | TRI-01 à TRI-14 | ✅ 2026-11-23 (3a à 3e, #377 à #391 : lectures internes, vote, impact, corrélation, pannes probables, interface, mesure, réglages, conservation, mode démonstration, calibrage de TRI-12) ; 🔜 mise en avant du tri (README, page de connexion, captures, image de partage) |
| 4 ([#366](https://github.com/chrraj7/S-Aloha/issues/366)) | Modèle de langage : réglages, emplacement, pseudonymisation, validation, essai, trace | OPT-20 à OPT-28 | ✅ 2026-11-24 (4a réglages côté API, 4b connecteur dans le conteneur, 4c essai, trace et page) |
| 5 ([#367](https://github.com/chrraj7/S-Aloha/issues/367)) | CMDB assistée dans le conteneur `features` : analyses, propositions, fusion, aide au sas, convention de nommage, jeu d'essai | CMA-01 à CMA-12 | 🔜 |
| 6 ([#368](https://github.com/chrraj7/S-Aloha/issues/368)) | Conteneur `mcp` : jetons personnels, outils de lecture, limites | MCP-01 à MCP-09 | 🔜 |

**Mode démonstration** (décidé le 2026-11-23, [ADR-0009](decisions/adr-0009-mode-demonstration-public.md)) :
il démarre le conteneur `features` avec le **seul tri assisté** activé, qui ne demande pas
de modèle de langage ; la CMDB assistée et le serveur MCP y restent coupés. Fait le 2026-11-23
(#365, 3e) : le chargeur active le tri et charge son jeu d'essai, dont une panne probable.

**Mise en avant** (décision du 2026-11-23) : le tri assisté des tickets, la CMDB assistée
et le serveur MCP sont les trois fonctionnalités que S-Aloha met en avant dans toutes ses
présentations et pages ; la connexion à l'Active Directory n'y est plus qu'un prérequis
d'intégration en entreprise. README, produit et page Soutenir le disent depuis le
2026-11-23, en annonçant les trois fonctionnalités pour la première version stable. À la
livraison de chacune (étapes 3, 5 et 6) : retirer cette annonce, ajouter son bloc à la page de
connexion (qui ne présente que des fonctionnalités existantes), ses captures en tête de
l'aperçu du README, et refaire l'image de partage du dépôt.

### Sortie du MVP

**Toutes les pratiques sont sorties du MVP le 2026-10-16.** Une pratique perdait la mention
**(MVP)** quand les prérequis communs étaient en place — lot 1, commentaires (SOC-24) et
notifications (SOC-21), tous faits le 2026-10-13 — et que toutes ses règles de sortie
(cœur de métier du lot 2) étaient à ✅ :

| Date | Pratiques | Réserves acceptées |
|---|---|---|
| — | Problèmes (pratique de référence, jamais en MVP ; dernières règles PRB-10, PRB-25, PRB-27 le 2026-10-13) | |
| 2026-10-13 | Connaissances, Configuration, Conformité NIS 2 | RAG-10 en attente ; pièces jointes de NIS-08 avec SOC-25 |
| 2026-10-14 | Niveaux de service, Amélioration continue, Incidents, Demandes | SLM-21 et tâches (REQ-24, REQ-25) au lot 3 ; modèle de demande facultatif ; cible de conformité d'un SLA facultative |
| 2026-10-16 | Changements, Projets | CI concernés d'un modèle standard à venir |

Le détail règle par règle est dans les journaux du
[2026-10-13](archives/journal-2026-10-13.md), du [2026-10-14](archives/journal-2026-10-14.md)
et du [2026-10-16](archives/journal-2026-10-16.md).

## 8. Communauté et financement

Accueil des contributeurs et financement du projet ; canaux et contreparties publics :
[Soutenir S-Aloha](soutenir.md).

| # | Chantier | Qui |
|---|---|---|
| ~~C2~~ | ~~Code de conduite, `.github/FUNDING.yml`~~ — faits le 2026-10-23 (Contributor Covenant 2.1, signalement privé par « Report content » de GitHub : activer **Settings › Moderation options › Reported content** du dépôt) | code + action |
| ~~C5~~ | ~~Tableau GitHub Projects public alimenté par ce plan~~ — fait le 2026-10-23 : [S-Aloha — feuille de route](https://github.com/users/chrraj7/projects/3) (issues ouvertes et chantiers restants) | action |
| ~~C6~~ | ~~Activer GitHub Sponsors~~ (profil public, objectif « 5 monthly sponsors ») ; **niveaux** décidés le 2026-10-23 — Soutien 5 $, Bronze 25 $, Argent 100 $, Or 500 $ par mois ([Soutenir](soutenir.md)) — créés sur la page Sponsors le 2026-10-23 — soldé ([ADR-0011](decisions/adr-0011-financement-sponsoring-et-services.md)) | action |
| C7 | Lancement **à la version stable** (décision du 2026-10-23) : LinuxFr, Reddit, AlternativeTo, Framalibre (textes prêts) ; aperçu social du dépôt : image de partage du langage visuel v2 ([protocole § 2](protocole-documentation.md#2-mettre-à-jour-la-référence)) ; awesome-selfhosted **4 mois après la première version stable** (première version de plus de 4 mois exigée ; les préversions retirées ne comptent plus) | action |
| C8 | Démo en ligne réinitialisée chaque nuit | décision |
| C9 | `GOVERNANCE.md` dès le deuxième contributeur régulier | décision |
