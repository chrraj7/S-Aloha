# 📖 Glossaire

> Les termes ITIL, S-Aloha, techniques et malgaches employés dans l'interface et la documentation.

<sub>[← Documentation](../readme.md) · [Produit](produit.md) · [Règles métier](regles-metier.md) · [Architecture](architecture.md) · [Base de données](base-de-donnees.md) · [Frontend](frontend.md) · [Exploitation](exploitation.md) · [Sécurité](securite.md) · [Glossaire](glossaire.md)</sub>

## ITIL et processus

| Terme | Définition |
|---|---|
| **ITIL** | Référentiel de bonnes pratiques de gestion des services IT (PeopleCert/Axelos). Les processus de S-Aloha reprennent les objectifs des **pratiques ITIL** ([ADR-0007](../decisions/adr-0007-pratiques-itil4-et-socle-commun-des-processus.md)). |
| **Pratique** (ITIL) | Ensemble de ressources organisationnelles destiné à accomplir un objectif (gestion des incidents, habilitation des changements…). Dans S-Aloha : un processus, un module. |
| **Processus** | Ensemble d'activités ITIL (incidents, problèmes, changements…). Un processus = un module S-Aloha. |
| **Incident** | Interruption ou dégradation d'un service ; l'objectif est de le rétablir au plus vite. |
| **Incident majeur** | Incident d'impact exceptionnel, suivi en tête de la console des gestionnaires jusqu'à sa résolution. |
| **Demande de service** | Demande prédéfinie d'un utilisateur (accès, matériel, information), approuvée puis satisfaite. |
| **Problème** | Cause, inconnue au départ, d'un ou plusieurs incidents. On en cherche la cause racine. |
| **Erreur connue** (*Known Error*) | Problème dont la cause racine est identifiée et pour lequel un **contournement** est documenté. |
| **Contournement** (*workaround*) | Moyen de réduire ou supprimer l'impact d'un problème en attendant sa résolution définitive. |
| **RCA** (*Root Cause Analysis*) | Analyse de cause racine. Trois méthodes dans S-Aloha : 5 Pourquoi, Ishikawa, FTA. |
| **5 Pourquoi** | Chaîne de questions « pourquoi ? » successives jusqu'à la cause racine. |
| **Ishikawa (6M)** | Diagramme en arête de poisson ; causes classées en six familles : Méthode, Matériel, Main-d'œuvre, Milieu, Matière, Mesure. |
| **FTA** (*Fault Tree Analysis*) | Arbre des défaillances : décomposition d'un événement redouté en causes combinées par des portes **ET** / **OU**. |
| **RACI** | Matrice d'affectation : **R**esponsable (réalise), **A**pprobateur (rend compte, un seul), **C**onsulté, **I**nformé. |
| **Impact × urgence** | Les deux axes qui déterminent la **priorité** P1 (la plus haute) à P4. |
| **MTTR** | *Mean Time To Resolve* : durée moyenne de résolution — en jours pour les problèmes (création → clôture), en heures pour les incidents (création → résolution). |
| **Changement** | Ajout, modification ou retrait de tout ce qui peut affecter un service. Types : **standard** (modèle déjà évalué, pré-autorisé), **normal** (évalué et autorisé), **urgent** (à mettre en œuvre au plus vite). |
| **Habilitation des changements** (*change enablement*) | Pratique ITIL : évaluer les risques, autoriser les changements, gérer leur calendrier. |
| **Autorité de changement** | Personne ou instance qui autorise un changement ; dans S-Aloha, un gestionnaire. |
| **Calendrier des changements** | Planning des changements planifiés (*change schedule*). |
| **Plan de retour arrière** (*backout plan*) | Procédure de retour à l'état antérieur si un changement échoue. |
| **CI** (*Configuration Item*) | Élément de configuration : composant à gérer pour délivrer un service (serveur, application, base…), relié à d'autres CI. |
| **CMDB** | Base de données de configuration : CI et leurs relations (processus Configuration). |
| **Portefeuille applicatif** (*application portfolio*) | Inventaire des applications, de leur gouvernance (propriétaire métier, responsable DSI, criticité) et de leur cycle de vie ; volet applications de la pratique ITIL *Portfolio management*, tenu dans la CMDB ([configuration](processus/gestion-de-la-configuration.md), CFG-43). |
| **Actifs matériels** (*IT asset management*) | Inventaire du matériel physique (constructeur, numéro de série, emplacement, garantie, fin de support) ; volet matériel de la pratique ITIL *IT asset management*, tenu dans la CMDB, sans le volet financier ([configuration](processus/gestion-de-la-configuration.md), CFG-44). |
| **Actif inconnu** (*Shadow IT*) | Équipement vu sur le réseau par la découverte, sans compte d'ordinateur dans l'annuaire ni CI rapproché (CFG-42). |
| **Sas de staging** | Zone d'attente des actifs découverts automatiquement, rapprochés des CI existants avant toute entrée dans la CMDB (CFG-40, CFG-41). |
| **Catalogue des services** | Liste des services délivrés aux métiers, avec leur criticité et leurs heures de service. |
| **SLA** | *Service Level Agreement* : accord de niveau de service entre le fournisseur et un client, avec des cibles (disponibilité, délais de résolution). |
| **Article de connaissance** | Solution, procédure, erreur connue documentée ou FAQ, publié après validation et revu périodiquement. |
| **CSI** | *Continual Service Improvement* : amélioration continue des services (pratique *continual improvement* d'ITIL). |
| **NIS 2** | Directive européenne (UE) 2022/2555 sur la cybersécurité des entités importantes et essentielles ; évaluée dans S-Aloha par la pratique [Conformité NIS 2](processus/conformite-nis2.md). |
| **ReCyF** | Référentiel Cyber France de l'ANSSI : 20 objectifs, 4 piliers, 152 exigences qui déclinent NIS 2 ; seule sa structure est dans le dépôt ([ADR-0012](../decisions/adr-0012-referentiel-nis2-structure-versionnee-texte-importe.md)). |
| **EI / EE** | Entité importante / entité essentielle au sens de NIS 2 ; une exigence « EI/EE » s'applique aux deux, « EE » aux seules entités essentielles. |
| **Déclaration réglementaire** | Déclaration d'un incident cyber à une autorité dans un délai légal (NIS 2 : ANSSI ; RGPD : CNIL), associée à l'incident ; voir [déclarations réglementaires](processus/declarations-reglementaires.md). |
| **Incident important** | Au sens de NIS 2, incident dont l'impact sur la fourniture des services oblige à le déclarer ; la qualification revient au gestionnaire (INC-41). |
| **Prise de connaissance** | Moment où l'organisation apprend l'incident de sécurité : point de départ des délais légaux de déclaration, en heures calendaires ; par défaut, la déclaration de l'incident (DRG-10). |
| **Alerte précoce** | Premier envoi NIS 2 à l'autorité, sous 24 h après la prise de connaissance d'un incident important (DRG-10). |
| **Maturité** | Niveau de 1 (Initial) à 5 (Optimisé) déduit du score d'une évaluation NIS 2, sur une échelle inspirée du CMMI (NIS-12). |
| **Écart** | Pour une exigence évaluée : 3 moins le score obtenu ; base des actions d'amélioration (NIS-13, NIS-15). |
| **Recherche hybride** | Recherche qui combine les **mots** (plein texte PostgreSQL) et le **sens** (similarité entre vecteurs) ; voir [recherche](recherche.md). |
| **Embeddings** | Vecteurs numériques qui représentent le sens d'un texte ; produits par le modèle `bge-m3`, dans le service séparé `embeddings` ([ADR-0013](../decisions/adr-0013-recherche-hybride-service-embeddings-separe.md)). |
| **Cas similaires** | Articles, problèmes et incidents résolus proches d'un incident ou d'un problème, proposés sur sa fiche (RAG-06). |
| **RRF** | *Reciprocal Rank Fusion* : fusion de deux classements (mots, sens) par la somme de 1 / (k + rang), avec k = 60. |
| **CAB** | *Change Advisory Board*, comité consultatif des changements : dans S-Aloha, deux gestionnaires distincts qui autorisent un changement normal à risque élevé ([CHG-20](processus/habilitation-des-changements.md)). |
| **Revue post-implémentation** | Retour d'expérience après un changement urgent ou échoué : ce qui s'est passé, causes, enseignements ([CHG-15](processus/habilitation-des-changements.md)). |
| **Période de gel** | Intervalle pendant lequel aucun changement non urgent ne peut être planifié ([CHG-25](processus/habilitation-des-changements.md)). |
| **Heures de service** | Plages pendant lesquelles un service est assuré ; les délais des SLA se comptent dans ces heures ([SLM-14](processus/gestion-des-niveaux-de-service.md)). |
| **Modèle de demande** | Demande prédéfinie du catalogue de demandes : objet, approbation requise ou non, délai cible, groupe d'exécution ([REQ-20](processus/gestion-des-demandes.md)). |
| **Résolution au premier contact** | Incident résolu sans prise en charge ni changement de responsable ([INC-23](processus/gestion-des-incidents.md)). |
| **Registre d'amélioration continue** (CIR) | Liste des opportunités d'amélioration, avec leur valeur attendue, leur priorité et leur avancement. |
| **Modèle d'amélioration continue** | Démarche ITIL en sept étapes : vision, situation actuelle, cible, plan, action, vérification, maintien de la dynamique. |
| **Code de résolution** | Nature de la résolution d'un incident : correctif appliqué, contournement, résolu sans action, non reproductible, doublon (relié à l'incident conservé). |
| **Code de clôture** | Motif de clôture d'un problème : corrigé, erreur connue acceptée (risque accepté sans correctif), doublon (relié au problème conservé), non retenu. |
| **Référence** | Identifiant d'un enregistrement : préfixe du processus, année, numéro (`INC-2026-0001`). Sert aussi à relier deux enregistrements. |

## S-Aloha

| Terme | Définition |
|---|---|
| **Socle** (*Core*) | Code transverse à tous les processus : authentification, annuaire, données, pilotage. |
| **Module** | Implémentation d'un processus ITIL (`Modules/<Processus>`, `modules/<processus>`). |
| **Registre** | `frontend/src/modules/registry.js`, source unique de la navigation : les processus et leur état. |
| **Bientôt** | État d'un processus déclaré dans le registre mais sans interface (`status: 'soon'`). |
| **Pilotage** | Section transverse du menu : Ma console et Reporting. |
| **Pratiques** | Section du menu qui regroupe les huit pratiques ITIL (registres et vues), visible par tous. |
| **Paramétrage** | Section du menu réservée aux gestionnaires et administrateurs : ce qui règle le **fonctionnement des pratiques** (catalogues, modèles, périodes de gel, référentiels). |
| **Administration** | Section du menu réservée à l'administrateur : la **plateforme** elle-même (utilisateurs et rôles, santé, journal d'audit, nettoyage). |
| **Fonctionnalité optionnelle** | Capacité que l'Admin active ou coupe (tri assisté des tickets, CMDB assistée, serveur MCP), exécutée hors de l'API : dans le conteneur commun `features`, ou, pour le serveur MCP, dans le sien ; coupée, elle est absente ([fonctionnalités optionnelles](fonctionnalites-optionnelles.md)). |
| **Proposition** | Modification suggérée par une fonctionnalité optionnelle ; elle n'est appliquée que si un utilisateur qui en a le droit l'accepte, et le journal d'audit en garde l'origine ([OPT-14, OPT-15](fonctionnalites-optionnelles.md#3-contrat-entre-lapi-et-le-conteneur-des-fonctionnalités)). |
| **Panne probable** | Groupe d'au moins trois incidents ouverts d'un même périmètre, liés par leur service, leur CI ou leur sens, signalé par le tri assisté ([TRI-07](tri-assiste-des-tickets.md)). |
| **Ma console** | Page d'accueil par rôle, orientée action : groupes Urgent, Décisions, Relances, Mon travail, À suivre. |
| **Règle ✅ / 🔜** | Règle fonctionnelle implémentée (✅) ou à implémenter (🔜), identifiée par un code stable (`INC-06`) ; voir les [conventions](regles-metier.md#3-conventions-de-ce-référentiel). |
| **Lot** | Ordre d'implémentation des règles 🔜 : 1 robustesse, 2 pratique ITIL complète, 3 confort ([plan du lot 1](processus/readme.md#plan-dimplémentation-conseillé--lot-1)). |

## Technique

| Terme | Définition |
|---|---|
| **AD** | Active Directory, annuaire de l'organisation : source des identités et des rôles. |
| **LDAP / LDAPS** | Protocole d'accès à l'annuaire ; LDAPS est sa version chiffrée (port 636). |
| **Bind** | Authentification auprès de l'annuaire LDAP (compte de service, ou utilisateur pour valider son mot de passe). |
| **`sAMAccountName`** | Identifiant de connexion Windows d'un utilisateur AD ; sert d'identifiant dans S-Aloha. |
| **JWT** | Jeton signé émis à la connexion, porteur du nom et du rôle, envoyé dans l'en-tête `Authorization: Bearer`. |
| **Policy** | Règle d'autorisation ASP.NET Core : `User`, `Manager`, `Admin`. |
| **Jeton de design** | Variable CSS nommée (`--bg-surface`, `--accent-blue`…) redéfinie par thème. |
| **Relation proposée** | Relation entre deux actifs du sas déduite par la découverte (« hyperviseur Héberge VM », « client Se connecte à serveur ») ; elle n'entre dans la CMDB que si le gestionnaire l'accepte ([CFG-55, CFG-56](processus/gestion-de-la-configuration.md)). |
| **Confiance au premier contact** | *Trust on first use* : la clé SSH ou le certificat autosigné d'un hôte est retenu au premier scan ; s'il change ensuite, aucun identifiant n'est envoyé tant que l'Admin n'a pas accepté la nouvelle clé ([CFG-53](processus/gestion-de-la-configuration.md)). |
| **OUI** | *Organizationally Unique Identifier* : les 24 premiers bits d'une adresse MAC, attribués par l'IEEE à un constructeur ; la découverte en déduit le constructeur d'un équipement ([CFG-50](processus/gestion-de-la-configuration.md)). |
| **Bannière** | Texte qu'un service réseau annonce de lui-même (ligne d'identification SSH, en-tête `Server` HTTP), lu sans authentification pour préciser le système d'un équipement ([CFG-49](processus/gestion-de-la-configuration.md)). |
| **SNMP v3 / USM** | Version de SNMP qui authentifie (SHA-2) et chiffre (AES) les échanges, par utilisateur (*User-based Security Model*) ; la v2c n'a qu'une communauté en clair ([CFG-51](processus/gestion-de-la-configuration.md)). |
| **WinRM / WS-Management** | Accès d'administration à distance de Windows, par messages SOAP sur HTTPS (port 5986) ; la découverte y lit les classes WMI de l'inventaire ([CFG-57](processus/gestion-de-la-configuration.md)). |
| **NTLM** | Protocole d'authentification Windows par défi et réponse, sans envoyer le mot de passe ; utilisé par l'inventaire WinRM. |
| **Modèle de langage** | Modèle qui produit du texte à partir d'une invite ; la CMDB assistée l'interroge par un connecteur unique, au format `…/v1/chat/completions`, et n'en retient que des propositions validées ([OPT-20 à OPT-28](fonctionnalites-optionnelles.md#4-connecteur-de-modèle-de-langage)). |
| **Pseudonymisation** | Remplacement, avant l'envoi au modèle de langage, des adresses, noms d'hôte, numéros de série et comptes par des jetons stables (`IP_1`, `HOTE_3`), rétablis dans la réponse ([OPT-24](fonctionnalites-optionnelles.md#4-connecteur-de-modèle-de-langage)). |
| **MCP** | *Model Context Protocol* : protocole par lequel un assistant (client MCP) appelle les outils et lit les ressources d'un serveur ; le serveur MCP de S-Aloha est en lecture seule ([serveur MCP](serveur-mcp.md)). |
| **Jeton d'accès personnel** | Secret créé par un utilisateur pour un client MCP, affiché une fois, à durée limitée, révocable, valable sur les seules routes de lecture ([MCP-03](serveur-mcp.md)). |
| **Port interne** | Second port de l'API, publié nulle part, par lequel le conteneur `features` des fonctionnalités optionnelles lit les événements et déposent leurs propositions ([OPT-11](fonctionnalites-optionnelles.md#3-contrat-entre-lapi-et-le-conteneur-des-fonctionnalités)). |
| **ADR** | *Architecture Decision Record* : une décision structurante par fichier, dans [`decisions/`](../decisions/). |

## Vocabulaire malgache

| Terme | Sens |
|---|---|
| **Aloha** | « D'abord, avant » — le service passe avant tout. |
| **Service alohan'ny zavatra rehetra** | « Le service avant toute chose » — explique le nom sur la page de connexion : *aloha* signifie « d'abord » en malgache. |
| **Ny fahaiza-manao ho amin'ny tolotra tsara kokoa** | « Le savoir-faire au service d'une meilleure offre » — maxime de la page de connexion, affichée avec cette traduction. |
