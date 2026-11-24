# Journal des versions

Les changements notables de chaque version, pour qui installe et met à jour S-Aloha.
Format inspiré de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/) ; versions selon
le [versionnage sémantique](https://semver.org/lang/fr/).

La première version publiée est la **1.0.0**, stable. Les préversions 0.1.0 et 0.2.0 ont été
retirées le 2026-10-23 ; leurs sections, conservées ci-dessous, sont devenues des étapes datées
du développement. Lire la section **Mise à jour** de chaque version avant de mettre à jour une
installation.

Le détail au jour le jour (sujets traités, raisons, commits) est dans les
[journaux](docs/archives/readme.md) ; les notes complètes de chaque version dans les
[releases](https://github.com/chrraj7/S-Aloha/releases). Les notes des
dernières fusions attendent dans [`changelog.d/`](changelog.d/readme.md) d'être versées
ci-dessous, chaque lundi.

## Non publié

## [1.0.0] — 2026-11-24

Première version stable : tri assisté des tickets, CMDB assistée et serveur MCP (optionnels,
activés par l'Admin, hors de l'API), dix pratiques ITIL 4 reliées, conformité NIS 2,
découverte du SI vers un sas de rapprochement, intégration à l'Active Directory. Images
`ghcr.io/chrraj7/s-aloha-{api,web,features,mcp,annuaire-demo}:v1.0.0` et `latest`.

**Réserves** : calibrages de la recherche (RAG-10) et du tri assisté (TRI-12) mesurés sur des
données fictives, à remesurer sur des données réelles ; inventaire WinRM (CFG-57) à valider sur
un Windows réel ; évaluations sur référentiels importés (F12, spécifiées) et agent
d'inventaire nomade (F13 phase 4) pas encore implémentés ; pas de démonstration en ligne ; `dotnet
format` absent de la CI ; gouvernance formelle à venir avec le deuxième contributeur régulier.

### Ajouté

- **Incidents** : un incident résolu sans réouverture est clos automatiquement après 10 jours
  ouvrés, au terme de la fenêtre de réouverture ; le déclarant et l'utilisateur affecté sont
  prévenus. Un incident cyber avec une déclaration réglementaire en cours n'est pas clos
  (INC-10).

- **Fonctionnalités optionnelles** : Administration › Fonctionnalités optionnelles montre le
  tri assisté des tickets, la CMDB assistée et le serveur MCP, leur conteneur et leurs
  dépendances, et les active ou les coupe ; la page Santé signale un conteneur perdu. Le
  journal d'audit indique l'origine de chaque modification (OPT-01 à OPT-08).

- **Conteneur des fonctionnalités optionnelles** : image `s-aloha-features` et profil
  `fonctionnalites` du compose (`FEATURES_URL`, `FEATURES_TOKEN`). Il accueillera le tri
  assisté des tickets et la CMDB assistée, que l'Admin active ensuite ; il n'en propose
  encore aucune (OPT-01, OPT-10).

- **Tri assisté des tickets, premier état** : le conteneur `features` propose désormais le tri
  assisté, que l'Admin active dans Administration › Fonctionnalités optionnelles. Après chaque
  enregistrement d'un incident Nouveau ou En cours, il propose une catégorie, un service et un
  groupe responsable, d'après les incidents résolus les plus proches du même périmètre, ainsi
  qu'un impact, d'après la criticité du service et des CI reliés. Chaque proposition porte son
  motif. L'affichage des propositions sur la fiche et à la saisie viendra ensuite (TRI-01,
  TRI-03, TRI-04, TRI-08).

- **Tri assisté des tickets, pannes probables** : quand au moins trois incidents ouverts d'un
  même périmètre se ressemblent, créés à moins d'une heure d'intervalle, ils forment une panne
  probable. Ils sont liés par le même service, le même CI relié ou, avec le service
  d'embeddings, une description proche. Les gestionnaires du périmètre en sont notifiés une
  fois, et chaque incident du groupe reçoit l'impact Élevé proposé. Le bandeau de la fiche
  viendra avec l'interface (TRI-05 à TRI-07).

- **Tri assisté des tickets, sur la fiche incident** : un encadré « Suggestions du tri assisté »
  montre, tant que l'incident est Nouveau ou En cours, une ligne par champ avec son motif et ses
  sources ; l'agent l'applique, la corrige ou la rejette. Un groupe responsable suggéré
  s'applique en escalade, au motif prérempli et modifiable. Un bandeau « Panne probable » réunit
  les incidents liés, propose de rattacher l'incident au parent et, à un gestionnaire, de
  déclarer ce parent incident majeur (TRI-07, TRI-09).

- **Tri assisté des tickets, à la saisie** : en déclarant un incident, dès 4 caractères de titre,
  un encadré « Suggestions » propose, sous les cas similaires, une catégorie, un service, un
  groupe responsable ou un impact, chacun avec son motif ; « Appliquer » remplit le champ, rien
  ne s'applique seul. Les incidents ouverts proches s'y affichent aussi, s'il y en a (TRI-02,
  TRI-11).

- **Tri assisté des tickets, réglages et mesure** : dans Administration › Fonctionnalités
  optionnelles, le tri activé montre ses réglages (nombre de voisins, seuils, fenêtre de
  corrélation, chaque suggestion activable à part), tracés au journal d'audit, et sa mesure
  par champ sur 30 et 90 jours : parts acceptées, corrigées, rejetées et laissées ; un champ
  surtout corrigé est signalé. Les suggestions appliquées à la saisie sont comptées et
  tracées avec l'origine « Tri assisté des tickets » (TRI-09, TRI-10, TRI-13).

- **Modèle de langage** (Administration › Fonctionnalités optionnelles) : réglage du modèle dont
  se sert la CMDB assistée, dans le SI ou hébergé hors du SI, au format de complétion de
  conversation (Ollama, vLLM, LocalAI, llama.cpp, offres hébergées). Un modèle hébergé se
  confirme explicitement, et la page rappelle alors en permanence que les données quittent le
  SI. Avant chaque envoi, adresses, noms d'hôte, numéros de série, comptes et courriels sont
  remplacés par des jetons ; la réponse est validée avant toute proposition. Un essai vérifie
  le modèle avec les réglages enregistrés, et chaque appel est tracé 90 jours, sans l'invite ni
  la réponse (OPT-20 à OPT-28).

- **CMDB assistée** (Configuration › CMDB assistée, fonctionnalité optionnelle) : un gestionnaire
  lance une analyse sur son périmètre ; le modèle de langage propose de **renommer** des CI selon
  la convention de nommage de leur type (Paramétrage › Convention de nommage), de **normaliser**
  l'écriture du système, du constructeur et du modèle, et de **reclasser** un CI dans le bon type.
  Chaque proposition se décide une à une ou par sélection, dans la file ou sur la fiche du CI,
  par les règles habituelles des CI ; rien n'est appliqué seul. Adresses, numéros de série et
  comptes ne sont jamais envoyés au modèle (CMA-01 à CMA-04, CMA-07, CMA-09).

- **CMDB assistée, fusion des doublons** : la tâche **Fusionner** repère les CI saisis deux fois
  (« srvpaie1 » et « SRV-PAIE-01 », par exemple). Le conteneur présélectionne lui-même les paires
  proches d'un même environnement, et le modèle ne juge que celles-ci, jamais toute la CMDB. Une
  paire rejetée n'est plus proposée. Dans la file, le gestionnaire garde le CI de son choix : les
  liens, les relations et les actifs du sas de l'autre lui sont reportés, et le doublon passe
  Retiré, motif « Fusionné dans … », tracé au journal des deux CI (CMA-05, CMA-06).

- **CMDB assistée, Relier et Criticité** : la tâche **Relier** propose les relations qui manquent
  (« Dépend de », « Héberge »…) entre des CI dont la description cite un autre CI ; la relation
  s'accepte dans la file, au besoin avec un autre type, et jamais vers soi-même ni en double. La
  tâche **Criticité** propose la criticité des applications qui n'en ont pas. Les six tâches de la
  CMDB assistée sont désormais disponibles au lancement d'une analyse (CMA-02, CMA-09).
- **CMDB assistée, aide au sas** : une tâche de découverte peut porter l'option « Analyser avec la
  CMDB assistée ». À sa fin, chaque actif laissé au sas reçoit un CI candidat ou un type pour sa
  création, avec son motif, sur la fiche de l'actif ; le gestionnaire le suit en un clic, en
  décide autrement ou le rejette. La décision reste celle du rapprochement (CMA-08).

- **CMDB assistée, réglages et mesure** : dans Administration › Fonctionnalités optionnelles,
  la CMDB assistée activée montre ses réglages (tâches proposées aux gestionnaires, nombre de CI
  au plus par analyse, paires au plus par requête, aide au sas), tracés au journal d'audit, et
  sa mesure par tâche sur 30 et 90 jours : parts acceptées, corrigées, rejetées et expirées,
  réponses non conformes du modèle, durée des appels et des analyses ; une tâche surtout
  rejetée est signalée. Une tâche retirée n'est plus proposée au lancement d'une analyse
  (CMA-10, CMA-12).

- **Serveur MCP** : jetons d'accès personnels, créés et révoqués dans Mes préférences
  (nom, durée de 7 à 90 jours, affichés une seule fois), listés et révocables par l'Admin ;
  ils n'ouvrent que la lecture, avec les droits du moment de leur titulaire (MCP-03, MCP-04).

- **Serveur MCP** : un assistant compatible MCP interroge S-Aloha en lecture, avec le jeton
  personnel et les droits de son utilisateur, sur `https://<adresse de S-Aloha>/mcp`. Six
  outils : recherche, fiche par sa référence (liens et commentaires compris), cas similaires,
  « Mon travail », impact d'un CI (dépendances, services, incidents et changements ouverts),
  propositions du tri assisté. Rien ne s'y écrit ; les textes saisis sont rendus comme des
  données ; 60 requêtes par minute et par jeton. Conteneur `mcp` (image `s-aloha-mcp`,
  profil `mcp` du compose), activé par l'Admin (MCP-01 à MCP-09).

- **Changement créé depuis un problème** (CHG-23) : « Créer un changement » sur la fiche d'un
  problème ouvre un changement qui reprend son titre et sa description, dans son périmètre, et
  relié au problème.

- **Conformité NIS 2** : les évaluations ouvertes dont on est le responsable apparaissent
  dans « Mon travail » de la console, jusqu'à leur validation.

- **Recherche** : le texte des pièces jointes d'un article publié, d'un problème établi ou d'un
  incident résolu est trouvé par la recherche et les cas similaires — texte brut, PDF à couche
  texte, Word, Excel et PowerPoint (docx, xlsx, pptx) et OpenDocument (odt, ods, odp),
  100 000 caractères au plus par fichier (RAG-12).

- **Niveaux de service** : la console des gestionnaires liste dans « Relances » les SLA en
  vigueur qui expirent dans les 30 jours, avec leur date d'expiration (SLM-24).

- **Problèmes** : l'historique du problème trace la réaffectation RACI d'une action corrective
  (RACI avant et après, auteur, date) ; une RACI inchangée n'écrit rien (SOC-20).

- **Sondes de santé de l'API** (O1) : `GET /health/live` (le processus répond) et
  `GET /health/ready` (la base aussi, 503 sinon), sans jeton et sans détail, pour Docker et
  Kubernetes ; healthcheck du service `api` dans `docker-compose.yml`.

- **Console : CI à vérifier** (CFG-15) : les CI En service dont la fiche n'a pas été vérifiée
  depuis 12 mois apparaissent dans Mon travail pour leur propriétaire (groupes compris) et
  dans Relances pour les gestionnaires de leur périmètre, avec la date de la dernière
  vérification ; compteurs des tuiles inclus.

- **HTTPS** (S2) : `docker-compose.https.yml` place un reverse proxy TLS (Caddy) devant
  S-Aloha : autorité interne, Let's Encrypt ou certificat fourni ; seuls les ports 80
  (redirection) et 443 sont publiés ; le cookie de session devient `Secure`
  ([exploitation § HTTPS](docs/reference/exploitation.md#https)).
- **LDAPS avec l'autorité de l'annuaire** (S2) : `Ldap:CaCertificatePath` désigne le fichier PEM
  de l'autorité qui a émis le certificat du contrôleur de domaine, pour la connexion et la
  découverte Active Directory. La validation du certificat et du nom n'est jamais désactivée
  ([exploitation § LDAPS](docs/reference/exploitation.md#ldaps)).

- **Indicateurs de conformité et de configuration** : écarts NIS 2 traités et évolution du score
  (NIS-30, NIS-31), disponibilité des services sous SLA (SLM-31), délais et abandons des
  améliorations (CSI-30 à CSI-32), usage et fraîcheur des articles (KB-30 à KB-32), délai de
  qualification et issue des déclarations (DRG-21, DRG-22) ; exactitude de la CMDB, CI les plus
  impliqués, CI orphelins (CFG-32 à CFG-34).

- **Indicateurs au tableau de bord** : délai de prise en charge et taux de réouverture des
  incidents (INC-31, INC-33) ; part des problèmes clos avec cause racine, récurrences après
  correctif et problèmes aux incidents les plus nombreux (PRB-33 à PRB-35) ; changements suivis
  d'un incident, délai d'autorisation et respect des créneaux (CHG-33 à CHG-35).

- **Pièces jointes** (SOC-25) : chaque fiche a un onglet « Pièces jointes » ; les fichiers
  (bureautique, texte, images) vont dans un stockage compatible S3, Garage en tête, réglé
  dans la console ; `docker-compose.yml` fournit un nœud Garage (profil `pieces-jointes`).

- **Notifications par courriel** (SOC-22) : chacun choisit, dans « Mes préférences », de
  recevoir ses notifications par courriel, au fil de l'eau ou en résumé quotidien, à
  l'adresse de son choix ; liens vers les fiches dès que l'adresse de l'interface est réglée
  (Administration › Messagerie et stockage).

- **Disponibilité mesurée** (SLM-21) : chaque mois, la fiche du SLA donne la disponibilité du
  service (heures de service sans incident majeur) face à sa cible ; un mois sous la cible
  alerte les gestionnaires et propose une amélioration.
- **Durabilité des améliorations** (CSI-15) : une amélioration réalisée porte une date de
  contrôle ; à cette date, le porteur confirme que le résultat tient, ou en ouvre une nouvelle.
- **Déclarations réglementaires** (DRG-08) : la fiche ne propose que les rubriques du régime
  (NIS 2 : gravité, indicateurs de compromission… ; RGPD : nature de la violation,
  conséquences probables…) et signale celles encore vides.

- **Configuration** (CFG-16, CFG-30) : attributs propres à chaque type de CI (processeurs et
  mémoire d'un serveur, version et éditeur d'une application…) ; import en masse de CI par
  un fichier CSV (Admin), tout ou rien, avec le rapport des lignes refusées.

- **Connaissances** (KB-13, KB-15, KB-16) : « utile / pas utile » sur un article publié (un
  vote par lecteur ; un article jugé peu utile remonte à son responsable) ; chaque
  publication est conservée en version, relisible sur la fiche ; quatre yeux : un
  gestionnaire ne publie pas un article dont il est le seul rédacteur (sauf Admin).

- **Problèmes sans activité** (PRB-32) : un problème ouvert sans analyse, action, commentaire
  ni modification depuis 30 jours remonte aux gestionnaires (Relances, notification).
- **Modèle standard suspendu** (CHG-27) : l'échec d'un changement Standard suspend son modèle
  jusqu'à la revue d'un gestionnaire (remise en service ou désactivation, motivée).

- **Incidents d'une même panne** (INC-16) : un incident se rattache à un incident parent ;
  résoudre le parent résout ses enfants avec la même résolution (encadré « Même panne »).
- **Attente prolongée** (INC-26) : un incident En attente depuis plus de 5 jours ouvrés
  remonte au responsable (console, notification) et aux gestionnaires (Relances).

- **Messagerie et stockage** (ADM-60 à ADM-63, [ADR-0020](docs/decisions/adr-0020-pieces-jointes-s3-et-smtp-configurables.md)) :
  nouvelle page Administration › Messagerie et stockage pour régler le serveur SMTP des
  courriels et le stockage objet compatible S3 (Garage) des pièces jointes, chacun avec un
  essai ; secrets chiffrés par le coffre, jamais réaffichés, modifications tracées ; état
  affiché dans la page Santé. Préalable aux courriels (SOC-22) et aux pièces jointes (SOC-25).

- **Reporting : onglet Demandes** (REP-60 à REP-63, indicateurs REQ-40 à REQ-43) : délai moyen
  de traitement, respect des délais, taux d'approbation et satisfaction moyenne, globalement
  et par modèle du catalogue ; avis par note ; demandes en retard, aussi en badge de l'onglet.

- **Demandes : tâches d'exécution, clôture, satisfaction** (REQ-24, REQ-25, REQ-11, REQ-32) :
  un modèle du catalogue porte ses tâches prédéfinies, créées sur la demande à l'approbation
  (onglet Tâches) ; une demande n'est satisfaite que ses tâches terminées ou annulées ; le
  demandeur confirme la clôture, sinon elle est automatique 5 jours ouvrés après la
  satisfaction ; enquête de satisfaction facultative (note de 1 à 5) une fois la demande close.

- **Fin du lot 2** (RAG-09, RAG-11, SOC-15, NIS-23, CSI-22, CFG-18) : cas similaires proposés
  dès la saisie du titre d'un incident ou d'une demande, et article ou problème relié en un
  clic depuis les cas similaires ; un enregistrement terminé ne se modifie que par un
  gestionnaire (un User le rouvre) ; exigences NIS 2 sans texte signalées à l'Admin ;
  améliorations proposées depuis plus de 30 jours signalées pour la revue du registre ;
  service « dégradé » quand un CI qui le supporte est hors service.

- **Incidents et problèmes** (INC-04, INC-05, INC-15, INC-17, PRB-07, PRB-20, PRB-22) : canal
  de signalement et utilisateur affecté (notifié de la résolution) ; un incident majeur se
  résout relié à un problème, et un incident majeur déjà résolu sans problème est signalé
  « problème à ouvrir » ; escalade vers un autre groupe motivée ; source du problème
  (réactif, à relier à un incident avant l'analyse, ou proactif) ; conclusion d'une analyse
  validée comme cause racine en un clic.

- **Demandes : bénéficiaire, contestation, requalification** (REQ-07, REQ-10, REQ-12) : le
  bénéficiaire vaut le demandeur par défaut ; une demande satisfaite se conteste dans les 5
  jours ouvrés, avec un motif, par le demandeur, le bénéficiaire ou un gestionnaire ; un
  besoin non standard se requalifie en changement ou en incident relié, la demande étant
  annulée avec ce motif.

- **Changements : conflit commenté et relances** (CHG-22, CHG-30 à CHG-32) : autoriser un
  changement en conflit de calendrier exige un commentaire de l'autorité (au CAB aussi) ; un
  changement planifié dont la fin est passée sans mise en œuvre, ou mis en œuvre depuis plus
  de 2 jours ouvrés sans clôture, est relancé (console du responsable et des gestionnaires,
  notification) ; part des changements urgents par mois au reporting.

- **Déclarations réglementaires des incidents de sécurité** (INC-40 à INC-44, DRG-01 à
  DRG-13, REP-44) : tout rôle marque un incident « sécurité » ; un gestionnaire le qualifie
  (incident important au sens de NIS 2, violation de données personnelles), ce qui crée une
  déclaration par régime retenu, avec ses échéances légales en heures calendaires (NIS 2 :
  alerte précoce 24 h, notification 72 h, rapport final un mois après ; RGPD : 72 h). Contenu
  complété par tous et figé à chaque envoi, motifs du retard RGPD, incident non clos tant
  qu'une déclaration est en cours. Échéances proches ou dépassées dans l'Urgent des
  gestionnaires et en notifications, respect des délais au reporting. Nouvelle entrée
  Conformité & réglementation › Déclarations.

- **Migrations en étape de déploiement** (O1) : option `--migrate` (applique les migrations
  et s'arrête) et réglage `Database:MigrateOnStartup` (vrai par défaut) ; l'API démarrée sans
  migrer refuse une base en retard. Le rattrapage des échéances SLA se fait en tâche de fond,
  visible dans Administration › Santé.

- **Découverte : inventaire WinRM** (CFG-57) : serveurs et postes Windows, en WS-Management
  sur HTTPS avec un compte de domaine ou local (NTLM) : système, constructeur, numéro de
  série, logiciels, services actifs, ports en écoute et flux réseau. **À valider sur un
  Windows réel** : testé contre un serveur WS-Management factice.

- **Découverte : flux réseau** (CFG-56) : l'inventaire SSH relève les connexions établies ;
  une connexion vers ou depuis un autre actif du sas propose « client Se connecte à serveur »
  (avec les ports vus), validée par le gestionnaire comme les relations d'hébergement.

- **Découverte : hyperviseurs et relations proposées** (CFG-54, CFG-55) : scans Proxmox VE
  (jeton d'API) et VMware vCenter (compte en lecture seule) — nœuds ou hôtes ESXi, machines
  virtuelles et conteneurs avec leur adresse MAC et leur système. La relation « hôte Héberge
  machine » est proposée dans le sas (vue Relations proposées) et n'entre dans la CMDB que si
  le gestionnaire l'accepte. Un certificat autosigné est retenu au premier contact, comme les
  clés SSH (Paramétrage › Découverte du réseau › Clés et certificats).

- **Découverte : inventaire SSH** (CFG-52, CFG-53) : nouveau type de scan pour les serveurs
  Linux et Unix, avec un compte SSH du coffre (mot de passe ou clé privée) sans droits
  d'administration : système, constructeur, logiciels installés, services actifs, ports en
  écoute, affichés sur la fiche de l'actif. La clé de chaque serveur est retenue au premier
  contact ; si elle change, l'identifiant n'est pas envoyé et l'hôte attend que l'Admin
  accepte la nouvelle clé (Paramétrage › Découverte du réseau › Clés d'hôte SSH).

- **Découverte : SNMP v3 et interfaces** (CFG-51) : un scan SNMP utilise une communauté v2c ou
  un utilisateur v3 du coffre (authentification SHA-2, chiffrement AES ; MD5 et DES refusés) et
  lit la table des interfaces des équipements (nom, état, débit, adresse MAC), affichée sur la
  fiche de l'actif. Le protocole SNMP passe par la bibliothèque SharpSnmpLib (licence MIT).

- **Découverte : empreinte passive** (CFG-49, CFG-50) : le balayage IP lit les bannières SSH
  et HTTP des ports ouverts (système, constructeur, type plus précis), affichées sur la fiche
  de l'actif ; le constructeur d'un équipement sans SNMP se déduit du préfixe de son adresse
  MAC, d'après le registre OUI de l'IEEE que l'Admin importe dans Paramétrage › Découverte du
  réseau › Constructeurs (OUI).

- **Paramétrage › Catégories** (ADM-50 à ADM-53, SOC-27) : liste des catégories d'incident
  et de problème tenue par les gestionnaires ; dès qu'une pratique en a une active, la
  catégorie se choisit dans la liste (l'API refuse une catégorie hors liste). Une catégorie
  utilisée se désactive au lieu de se supprimer. La console d'administration (A1) est complète.

- **Administration › Nettoyage des doublons** (ADM-40 à ADM-43) : doublons probables (même
  pratique, même périmètre, même titre aux accents et à la casse près, créés à 7 jours au
  plus) ; fusion qui reporte liens et commentaires sur l'enregistrement conservé, ou « pas un
  doublon ».

- **Administration › Utilisateurs et rôles** (ADM-10 à ADM-13) : membres des groupes AD de
  rôle lus dans l'annuaire, rôle effectif par périmètre et sa source, date de dernière
  connexion (seule donnée retenue, 13 mois), affectations de comptes sans accès signalées.

- **Administration › Santé et Journal d'audit** (ADM-20 à ADM-31) : état de la base, de
  l'annuaire, des embeddings, du coffre et des tâches de fond, version déployée ; journal
  d'audit de toutes les pratiques, filtrable par auteur, pratique, action, référence et
  période, exportable en CSV.

- **Observabilité** (O2) : journal JSON hors développement, identifiant de trace sur chaque
  ligne et dans l'en-tête `traceparent` des réponses ; traces et métriques OpenTelemetry
  (HTTP, PostgreSQL, annuaire, découverte, notifications) exportées si
  `OTEL_EXPORTER_OTLP_ENDPOINT` est défini.
- **Suppressions dans l'interface** (SOC-14, M8) : un Admin supprime un enregistrement, un
  problème compris, et un gestionnaire une analyse ou une action corrective, après une
  confirmation qui rappelle la référence et compte ce qui part avec (liens, commentaires,
  analyses, relations, accords…).

### Modifié

- **Image de l'API** : elle écoute aussi sur le port **8081**, port interne réservé au
  conteneur des fonctionnalités optionnelles ; ne pas le publier (OPT-11).

- **Mode démonstration** : le conteneur des fonctionnalités optionnelles démarre d'office, et
  le tri assisté des tickets y est activé avec un jeu d'essai fictif. Des incidents résolus
  lui servent de voisins, et une panne probable de quatre incidents ouverts montre le bandeau
  et la notification. Docker Compose 2.24 ou plus est requis (ADR-0009).

- **CMDB assistée, tâches par défaut** : mesurées sur un jeu d'essai de 55 CI avec un modèle exécutable dans le SI,
  Renommer, Reclasser et Criticité sont proposées par défaut ; Normaliser, Fusionner et Relier, sous 70 % de précision,
  sont coupées par défaut et s'activent dans Administration › Fonctionnalités optionnelles. Une normalisation d'un champ
  vide est désormais écartée (CMA-09, CMA-11).

- **Page de connexion** : ses quatre blocs présentent d'abord le tri assisté des tickets, la
  CMDB assistée et le serveur MCP, puis la console par rôle.

- **Demandes** : une demande approuvée ne passe En cours qu'avec un responsable désigné
  (REQ-13) ; une demande du catalogue le reçoit déjà de son modèle à l'approbation.

- **Recherche et périmètres** (RAG-13) : les résultats invisibles de l'utilisateur sont écartés
  avant le classement ; un utilisateur aux périmètres étroits reçoit toute la liste demandée,
  et non une liste écourtée.

- **Incidents majeurs** : un incident majeur déclaré sans responsable ne sort de
  « Nouveau » qu'avec un gestionnaire pour responsable ; la console le signale
  « Responsable à désigner » (INC-13).

- **Contribuer** : l'entrée de journal et la note du journal des versions d'une PR vont
  chacune dans leur propre fichier (`docs/archives/journaux/`, `changelog.d/`), si bien que
  deux PR ouvertes en même temps n'entrent plus en conflit sur ces fichiers.

- **Recherche** : le plancher de similarité passe de 0,5 à 0,47, calibré sur un jeu de
  requêtes (RAG-07, RAG-10) : davantage de bonnes réponses trouvées par le sens, sans
  réponse proposée aux requêtes qui n'en ont pas.

- **Recherche** : la similarité des vecteurs est calculée dans PostgreSQL par pgvector
  (index HNSW) au lieu de la mémoire de l'API ; la mémoire de l'API ne croît plus avec
  l'index, et tout processus qui lit la base voit le même index (ADR-0022, RAG-05).
- **Exigence** : pgvector 0.8.0 ou plus (parcours itératif de l'index) ; avec une version
  plus ancienne, la requête vectorielle est journalisée et la recherche reste lexicale.

- **Images de développement `edge`** : l'état de `main` est publié sur ghcr.io à chaque
  fusion (CI verte), et c'est désormais l'image par défaut des fichiers compose — `latest`
  datait du 2026-10-19. Les images des préversions 0.1.0 et 0.2.0, retirées, ne sont plus
  proposées. Mise à jour : `docker compose pull && docker compose up -d`.
- **Langage visuel v2** ([ADR-0021](docs/decisions/adr-0021-langage-visuel-v2-sombre-et-nouvel-embleme.md)) :
  **thème sombre par défaut** (le mode clair reste à un clic, et mémorisé), surfaces bleu nuit
  en couches, accent turquoise, angles adoucis, titres en Geologica et texte en IBM Plex Sans,
  contrastes des textes portés à 4,5:1 dans les deux thèmes. **Nouvel emblème** — un « S »
  hexagonal — dans la barre latérale, la page de connexion et le favicon.
- **Navigation** : une icône propre à chaque entrée de Paramétrage et d'Administration ;
  entrées compactes, Paramétrage et Administration au pied de la liste, et un menu
  **réductible** en rail d'icônes (menus volants au survol), mémorisé dans le navigateur.
- **Reporting en widgets** : tuiles d'indicateurs compactes, intertitres par thème, score de
  santé en jauge ; graphiques à la taille de leur carte, volumétrie et découverte en colonnes
  empilées (par semaine au-delà de 45 jours).
- **Réponses de l'API en DTO** (A2) : l'API ne renvoie plus ses entités internes, mais des
  objets de réponse explicites, de mêmes noms de champs ; les navigations internes (problème
  d'une analyse, données brutes de la découverte…) n'en sortent plus. Le contrat est figé par
  un test.

### Corrigé

- **Changements, demandes, amélioration** : le responsable (ou le porteur) d'un enregistrement
  engagé — changement autorisé, planifié ou mis en œuvre, demande en cours, amélioration
  validée ou en cours — n'est changé que par le responsable en place, un membre de son groupe
  ou un gestionnaire du périmètre. Un tiers ne se désigne plus pour mettre en œuvre, satisfaire
  ou engager à l'envoi suivant ; sans responsable, c'est un gestionnaire qui le désigne (CHG-07,
  REQ-25, CSI-11).

- **Incidents** : un incident parent dont un enfant est déjà résolu ou clos peut de nouveau
  changer de périmètre ; ses enfants terminés le suivent, avec leurs déclarations et leurs
  notifications, et seuls les enfants ouverts sont à détacher d'abord (INC-16, PER-07).

- **Incidents** : enregistrer la fiche d'un incident dont le service est hors catalogue
  n'efface plus ce service de la fiche, de la liste ni du reporting (SLM-10, INC-20).

- **Changements** : un changement Urgent mis en œuvre ne peut plus être renvoyé Normal, ce qui
  le clôturait sans revue post-implémentation (CHG-15). Un changement Urgent planifié pendant
  une période de gel qui devient Normal voit son créneau revérifié : le gel le refuse (CHG-25).

- **Changements** : modifier le type, le risque, les plans ou le créneau d'un changement
  Évalué efface les décisions du CAB déjà données ; une autorisation « CAB » porte toujours
  sur le contenu que les deux gestionnaires ont vu (CHG-20).

- **Amélioration continue** : une fois l'amélioration validée, sa valeur, son effort et sa
  priorité ne changent que par un gestionnaire ; la fiche les fige pour un utilisateur
  (CSI-12).

- **Problèmes** : le changement créé depuis une action corrective et l'article d'erreur connue
  créé depuis la fiche naissent dans le périmètre du problème ; un utilisateur de plusieurs
  périmètres n'est plus refusé (PRB-27, PRB-30, PER-04).

- **Résolution d'un incident parent** (INC-15, INC-16) : résoudre un parent résolvait aussi un
  enfant majeur sans problème relié, que sa résolution directe refusait. Cet enfant reste
  désormais ouvert ; la réponse de l'API le liste avec la raison (`unresolvedChildren`).

- **Échéance d'une demande d'un modèle sans délai cible** (REQ-22) : un User ne pouvait plus
  modifier l'échéance souhaitée d'une demande issue d'un modèle du catalogue sans délai cible ;
  seule l'échéance calculée d'un modèle à délai cible reste réservée aux gestionnaires.

- **Champs réservés aux gestionnaires sur une fiche** (INC-13, PER-06) : la case « Incident
  majeur », le commentaire sur un conflit de calendrier d'un changement et les champs de
  gestionnaire d'une déclaration réglementaire suivaient le rôle AD. Ils suivent désormais le
  rôle sur le périmètre de l'enregistrement, comme l'API : le gestionnaire d'un périmètre,
  User dans l'AD, déclare un incident majeur depuis la fiche ; un Manager AD simple User du
  périmètre ne voit plus la case active.

- **Connaissances, quatre yeux** (KB-03, KB-16) : un gestionnaire qui réécrit un article publié
  dont il est le seul rédacteur ne le republie plus seul ; l'article repasse en brouillon et un
  autre gestionnaire le republie ([#215](https://github.com/chrraj7/S-Aloha/issues/215)).

- **Référentiels sans périmètre à l'écran** (PER-13) : les boutons des modèles de changement
  standard, du catalogue de demandes, des périodes de gel et des fins de support, et la section
  **Paramétrage** du menu, suivaient le rôle de l'annuaire ; ils suivent désormais, comme l'API,
  le rôle de gestionnaire d'au moins un périmètre — un User de l'annuaire gestionnaire d'un
  périmètre les voit, un Manager de l'annuaire simple utilisateur partout ne les voit plus. Le
  référentiel NIS 2 reste à l'Admin.

- **Décisions de gestionnaire sur les fiches** (PER-06) : la revue et la demande d'archivage
  d'un article publié, la soumission à publication, le contrôle de durabilité d'une amélioration,
  la vérification d'un CI et le retrait de la pièce jointe d'un autre suivaient le rôle de
  l'annuaire ; ils suivent désormais, comme l'API, le rôle sur le périmètre de l'enregistrement.

- **Retrait d'un service** (SLM-16) : un service ne se retire plus tant qu'il porte un SLA En
  vigueur, un incident ou une demande ouverts ; le refus cite les références en cause. Un SLA
  ne continue plus d'alerter pour un service retiré.

- **CORS** : une origine de `Cors:AllowedOrigins` écrite avec une barre oblique finale est
  admise au lieu d'être refusée sans message (#239).

- **Incidents enfants** : un parent résolu en *Doublon* laisse ses enfants ouverts et les liste
  avec la raison, au lieu de les résoudre en *Doublon* sans lien vers l'incident conservé
  (INC-08, INC-16).

- **Configuration** : un User ne crée plus un CI directement au statut « Retiré », réservé au
  gestionnaire comme le retrait d'un CI existant (CFG-12).

- **Demandes requalifiées** : l'incident créé par la requalification d'une demande reçoit
  ses échéances SLA dès sa création (REQ-12, INC-21).

- **Demandes** : seuls le responsable de la demande (ou un membre du groupe responsable) et
  un gestionnaire la déclarent satisfaite (§ 4, REQ-25).

- **Incidents** (INC-27) : enregistrer la fiche d'un incident depuis l'interface n'efface plus
  son début constaté ; l'incident reste compté dans le temps moyen de détection (MTTD).

- **Demandes** : le bloc « Requalifier » paraît pour le gestionnaire du périmètre de la
  demande, et non plus selon le rôle de l'annuaire (REQ-12, PER-06).

- **Demandes** : changer l'objet, le service ou le bénéficiaire d'une demande approuvée ou en
  cours la renvoie à l'approbation ; une demande satisfaite ou close ne les change plus ; seuls
  le demandeur et un gestionnaire changent le bénéficiaire (REQ-04, REQ-07).

- **Changements** : seuls le responsable du changement et un gestionnaire le mettent en
  œuvre et le closent (§ 4 de la spécification).

- **Amélioration continue** : seuls le porteur et un gestionnaire engagent une amélioration
  et la déclarent réalisée ; l'engager exige un porteur (CSI-11).

- **Projets et registres réservés** : les boutons du chef de projet (business case, étapes,
  livrables, suppressions) et la création d'un service, d'un SLA ou d'une évaluation NIS 2
  suivent le rôle sur le périmètre, comme l'API, et non plus le rôle Active Directory
  (PER-06, PRJ-04).

- **Demandes** : une demande En cours ne perd plus son responsable par un simple
  enregistrement, et une contestation envoyée sans responsable est refusée ; le responsable
  se remplace, il ne se retire pas (REQ-13).

- **Projets** : la validation de fin d'étape est refusée sur un projet clôturé ou abandonné,
  et son bouton n'y est plus proposé (PRJ-07, PRJ-21).

- **Demandes** : enregistrer la fiche d'une demande dont le service est hors catalogue
  n'efface plus ce service ; la demande approuvée ne repart plus à l'approbation, et la
  contestation d'une demande satisfaite est de nouveau possible (REQ-04, REQ-10).

- **Incidents enfants** : la résolution d'un incident parent ne résout que ses enfants du
  même périmètre (INC-16, PER-07).

- **Notifications des déclarations réglementaires** (SOC-23, DRG-01) : quand un incident change
  de périmètre, les notifications de ses déclarations le suivent avec elles.

- **« Ouvrir un problème lié »** (INC-14) : le bouton de la fiche d'un incident échouait pour
  un utilisateur de plusieurs périmètres (« Choisir le périmètre de l'enregistrement. ») et,
  pour un Admin, créait le problème dans le périmètre par défaut ; le problème est désormais
  créé dans le périmètre de l'incident.
- **Qualification réglementaire d'un incident de sécurité** (INC-41, PER-06) : le formulaire
  « Qualifier » suivait le rôle de l'annuaire ; il suit désormais le rôle sur le périmètre de
  l'incident, comme l'API — un gestionnaire du périmètre le voit, un Manager de l'annuaire
  simple utilisateur de ce périmètre ne le voit plus.
- **Échéance d'une demande du catalogue** (REQ-22) : enregistrer la fiche d'une demande issue
  d'un modèle à délai cible était refusé à un User (« Seul un gestionnaire modifie
  l'échéance… »), et ramenait l'échéance à minuit pour un gestionnaire ; le même jour renvoyé
  par le formulaire garde désormais l'échéance calculée, heure comprise.
- **Graphiques du reporting** : un graphique affiché d'abord vide (« Aucune donnée sur la
  période »), ou réaffiché après « Voir les données », gardait une largeur de repli étirée à
  sa carte, avec des graduations agrandies ; il suit de nouveau la largeur de sa carte.
- **Rapport complet (PDF) du reporting** : le PDF ne contenait qu'une page, la hauteur d'un
  écran ; il déroule désormais tous les onglets (un par page), en thème clair et avec ses
  barres même quand le navigateur n'imprime pas les arrière-plans.
- **Mes préférences** : la page restait vide — l'appel des préférences de courriel était
  écrasé par celui de la session (même nom dans le client de l'API) et l'application
  s'interrompait à l'ouverture. Une **garde d'erreur** évite désormais toute page blanche :
  une page qui échoue affiche un message, et le menu reste utilisable.
- **Mode démonstration** : le chargement des données d'exemple s'arrêtait au premier
  problème, refusé par les règles des pratiques ajoutées depuis (problème relié à un incident
  avant l'analyse, CI relié avant l'évaluation d'un changement, autorisation par un autre que
  le demandeur, changement standard tiré d'un modèle, publication à quatre yeux, motif
  d'attente, valeur et effort d'une amélioration). Il charge de nouveau tout le jeu.
- **Import du registre OUI** (CFG-50) refusé en 413 : nginx limitait le corps des requêtes à
  1 Mo, quand `oui.csv` fait environ 4 Mo. La limite de `/api/` passe à 16 Mo (couvre aussi
  le texte du référentiel NIS 2, 5 Mo) ; un refus de taille par un proxy s'affiche clairement.
- **Utilisateurs et rôles** (ADM-10) et **synchronisation de l'annuaire** (découverte) en
  erreur 500 contre un Active Directory : les références que l'AD renvoie vers ses autres
  partitions (Configuration, zones DNS) sont désormais sautées.
- **Cookie de session** sans l'attribut `Secure` derrière un proxy TLS (Traefik, ingress) :
  nginx transmet le schéma reçu du proxy amont.
- Lignes hors JSON au démarrage (« Cannot load library libgssapi_krb5 ») : le chiffrement
  GSS de Npgsql est désactivé, sauf choix explicite dans la chaîne de connexion.

- **Fins de support** : la liste (`GET /api/discovery/end-of-life`) est de nouveau lisible par
  tous les utilisateurs, comme documenté ; elle exigeait le rôle Manager.

### Sécurité

- **Audit des dépendances de l'interface** : un avis haut ou critique bloque toujours le
  paquet livré ; sur l'outillage de build et de test, une exception datée, un avis à la
  fois, peut attendre une version corrigée.

- **Preuves d'une évaluation NIS 2** (PER-08, NIS-08) : une preuve n'est décrite (référence,
  titre, lien) que si le lecteur voit l'enregistrement désigné dans son périmètre actuel ;
  ailleurs, elle compte sans être décrite (« Enregistrement d'un autre périmètre »), dans le
  questionnaire comme dans l'export, et ne se retire pas.

- **Synthèse NIS 2 et service dégradé** (PER-08, NIS-14, NIS-15, CFG-18) : l'action liée à un
  écart et le CI en cause d'un service dégradé ne sont décrits que dans les périmètres de
  l'appelant ; ailleurs, ils comptent sans référence ni lien (« Action d'un autre
  périmètre », « CI d'un autre périmètre »).

- **Mot de passe du compte de service de l'annuaire hors de `docker-compose.yml`** (S1) : il
  se fournit par l'environnement (`LDAP_BIND_PASSWORD`), comme les autres secrets ; hors
  développement, l'API refuse de démarrer s'il est vide, un bind sans mot de passe étant
  anonyme. Une installation lancée avec le seul `docker-compose.yml` doit désormais fournir
  cette variable ; la démonstration ne change pas.

- **CORS limité aux origines configurées** (S3) : l'API n'accepte plus les requêtes de
  n'importe quelle origine. `Cors:AllowedOrigins` liste les origines externes admises ; vide
  par défaut, ce qui suffit au frontend fourni (même origine, par nginx).

- **Clôture des incidents** (INC-10) : seul un gestionnaire du périmètre clôt un incident
  résolu ; un User le résout ou le rouvre, mais sa demande de clôture est refusée (403).

- **Déclarations réglementaires et périmètres** (DRG-01, PER-07) : quand un incident change de
  périmètre, ses déclarations le suivent ; une déclaration ne change plus seule de périmètre.

- **Découverte et périmètres** (PER-05, PER-06, PER-07, CFG-41) : un actif rapproché d'un CI
  déplacé depuis vers un autre périmètre ne met plus ce CI à jour ; il repasse au
  rapprochement dans son seul périmètre, et le sas ne renvoie le CI rapproché (liste, fiche
  de l'actif) que s'il est visible du gestionnaire.

- **Incidents enfants et périmètres** (PER-07, INC-16) : un incident rattaché à un parent ne
  change plus de périmètre ; on le détache d'abord de son parent, puis on le déplace.

- **Exports CSV de l'interface** (REP-08, inventaire, synthèse de conformité) : une cellule
  qui contient un retour chariot est mise entre guillemets, comme dans l'export du journal
  d'audit ; elle reste une seule cellule à l'ouverture dans le tableur.

- **Incidents parents et périmètres** (PER-07, INC-16) : un incident qui a des enfants
  rattachés ne change plus de périmètre ; on détache d'abord ses enfants, puis on le déplace.

- **Relations proposées par la découverte** (CFG-55, PER-05, PER-07) : une proposition ne
  décrit que les CI visibles de l'appelant, et ne s'accepte que si ses deux CI sont encore au
  périmètre de la proposition ; un CI déplacé entre deux scans ne se relie plus depuis
  l'ancien périmètre.

- **Aucun secret d'exemple dans `docker-compose.yml`** (S1) : le mot de passe PostgreSQL, la
  clé JWT et la clé du coffre se fournissent par l'environnement (`POSTGRES_PASSWORD`,
  `JWT_KEY`, `DISCOVERY_VAULT_KEY`, par exemple `docker compose --env-file …`) ; les valeurs
  d'exemple ne restent que dans `docker-compose.demo.yml`. Une installation lancée avec le
  seul `docker-compose.yml` doit désormais fournir ces trois variables.

- **LDAPS avec l'autorité de l'annuaire** (S2) : le certificat du contrôleur de domaine doit
  porter l'usage « authentification serveur », comme avec la validation du système
  ([exploitation § LDAPS](docs/reference/exploitation.md#ldaps)).

- **Contrôles des pull requests** : une PR prouve son changement par un test qui échoue sans
  lui (job Preuve), ne peut ni retirer ni désactiver de test (Garde-fous), et ses dépendances
  ajoutées comme ses workflows sont analysés ; une CI rouge sur `main` ouvre une issue
  `main-rouge`. Deux injections par expression corrigées dans les workflows de publication
  d'images.

- **Changement autorisé modifié par un gestionnaire** (CHG-07, CHG-20, CHG-21, CHG-22) : la
  modification revient à l'évaluation quand elle est faite par le demandeur, quand le
  changement relève du CAB (Normal à risque Élevé) ou quand le nouveau créneau crée un conflit
  sans commentaire de l'autorité ; sinon, elle vaut autorisation au nom du gestionnaire qui
  l'a faite, et non plus de celui qui avait autorisé la version précédente.
- **Exports CSV : cellules neutralisées** (ADM-31, REP-08, inventaire, synthèse de conformité) :
  une valeur texte qui commence par `=`, `+`, `-`, `@`, une tabulation ou un retour chariot
  est précédée d'une apostrophe, pour que le tableur la lise comme du texte ; les nombres
  restent des nombres.
- **Référentiels sans périmètre** (PER-13) : périodes de gel, modèles de changement standard,
  catalogue de demandes, fins de support et catégories se tiennent en Admin ou en gestionnaire
  d'au moins un périmètre, selon le rôle par périmètre et non plus selon le groupe AD (ADR-0014
  D4). Un compte du groupe AD Manager affecté User partout n'y écrit plus ; un User AD
  gestionnaire d'un périmètre y écrit.
- **Notifications d'un enregistrement déplacé** (SOC-23, PER-07) : quand un enregistrement
  change de périmètre, ses notifications le suivent ; elles se lisent selon le nouveau
  périmètre, dans la cloche comme dans le résumé par courriel.
- **Historique d'un enregistrement** (SOC-20) : il suit désormais la visibilité de la fiche
  (PER-05) ; l'historique des périmètres et des paramètres est réservé à l'Admin.
- **Jeton de session en cookie HttpOnly** (S5) : le jeton n'est plus lisible par les scripts
  de la page ; cookie `SameSite=Strict`, `Secure` en HTTPS, protection CSRF par l'en-tête
  `X-S-Aloha`, déconnexion côté serveur ; nginx sert une politique de sécurité du contenu
  (CSP) et `X-Content-Type-Options`.
- **CSP resserrée** : les polices sont servies par l'application ; la politique n'autorise
  plus aucun domaine externe pour les styles et les polices (`style-src 'self'`,
  `font-src 'self'`).

### Mise à jour

- **Images** : les fichiers compose prennent désormais la dernière version publiée (`latest`)
  par défaut, et non plus `edge` ; `S_ALOHA_VERSION=v1.0.0` fige cette version,
  `S_ALOHA_VERSION=edge` suit `main`. Une installation qui suivait `edge` vérifie les
  consignes ci-dessous, celles des secrets d'abord (S1 : `POSTGRES_PASSWORD`, `JWT_KEY`,
  `DISCOVERY_VAULT_KEY`, `LDAP_BIND_PASSWORD`).

- **Image PostgreSQL** : `pgvector/pgvector:0.8.7-pg16` remplace `postgres:16-alpine`. Une
  base créée par l'ancienne image se sauvegarde puis se restaure (ou ses index se
  reconstruisent par `REINDEX DATABASE`) : le tri du texte diffère entre Alpine et Debian
  ([exploitation § Recherche](docs/reference/exploitation.md#recherche)). Un PostgreSQL
  géré doit proposer l'extension `vector`.

- **Dernière connexion (ADM-12)** : la migration `LastLogins` crée une table (compte, date) ;
  à mentionner au registre des traitements de l'organisation.
- **Pièces jointes** : la migration `Attachments` ajoute la table `Attachments`. Activer le
  stockage dans la console (exploitation, § Pièces jointes et courriels). Un proxy placé devant
  `web` doit admettre des requêtes à la taille des pièces jointes.
- **Courriels** : la migration `MailNotifications` ajoute la table `MailPreferences` et la
  colonne `MailSettings.PublicUrl`. Renseigner l'adresse de l'interface pour des liens
  dans les courriels.
- **Améliorations et déclarations** : la migration `Lot3Fin` ajoute neuf colonnes.
- **Dépôt et images** : le dépôt est désormais `github.com/chrraj7/S-Aloha` et les images
  `ghcr.io/chrraj7/s-aloha-*` ; les anciennes adresses redirigent, mais un `docker-compose.yml`
  copié avant ce changement est à mettre à jour.
- **Configuration** : la migration `ConfigurationLot3` ajoute la colonne `ConfigurationItems.Attributes`.
- **Connaissances** : la migration `KnowledgeLot3` ajoute deux tables et trois colonnes. Les
  articles déjà publiés sont en version 0 jusqu'à leur prochaine publication. Un gestionnaire
  qui rédige seul un article le fait désormais publier par un autre gestionnaire ou un Admin.
- **Changements** : la migration `ProblemChangeLot3` ajoute six colonnes aux modèles standard.
- **Incidents** : la migration `IncidentParent` ajoute la colonne `Incidents.ParentId`.
- **Messagerie et stockage** : la migration `PlatformSettings` ajoute deux tables de réglages.
  Rien n'est activé : régler et essayer dans la console. Les secrets dépendent de
  `Discovery:VaultKey`, qui doit donc être fixée.
- **Demandes** : la migration `RequestTasksSatisfaction` ajoute deux tables (tâches du modèle et
  de la demande) et quatre colonnes.
- **Incidents et problèmes** : la migration `IncidentProblemLot2` ajoute sept colonnes ; les
  problèmes existants sont proactifs (aucun n'est bloqué après coup), les nouveaux réactifs
  par défaut.
- **Demandes** : la migration `RequestContestRequalify` ajoute deux colonnes facultatives.
- **Changements** : la migration `ChangeFollowUps` ajoute deux colonnes facultatives.
- **Déclarations réglementaires** : la migration `RegulatoryNotifications` ajoute sept colonnes
  facultatives aux incidents et une table.
- **Proxy amont** : un second nginx devant S-Aloha (ingress-nginx par exemple) garde sa propre
  limite de 1 Mo ; la porter à 16 Mo (`nginx.ingress.kubernetes.io/proxy-body-size: 16m`) pour
  les imports. Traefik n'en impose pas par défaut.
- **WinRM (CFG-57)** : activer WinRM en HTTPS (port 5986) sur les hôtes à inventorier et
  placer le compte de découverte dans « Remote Management Users ». Aucune migration.
- **Hyperviseurs (CFG-54)** : la migration `Hypervisors` ajoute une colonne (clés existantes :
  « ssh ») et une table ; créer un jeton Proxmox (rôle PVEAuditor) ou un compte vCenter en
  lecture seule.
- **Inventaire SSH (CFG-52)** : la migration `SshInventory` ajoute deux colonnes et deux tables ;
  créer un compte SSH dédié, sans sudo, sur les serveurs à inventorier.
- **SNMP v3 (CFG-51)** : la migration `SnmpV3Interfaces` ajoute trois colonnes facultatives ;
  les communautés v2c et les tâches existantes restent valables.
- **Constructeurs (CFG-50)** : la migration `PassiveFingerprint` crée une table vide ; pour
  en profiter, importer `oui.csv` (https://standards-oui.ieee.org/oui/oui.csv) en Admin.
- **Catégories (ADM-52)** : la migration `Categories` reprend les catégories déjà saisies sur
  les incidents et les problèmes ; dès lors, une catégorie se choisit dans cette liste. La
  relire dans Paramétrage › Catégories pour désactiver les variantes indésirables.
- **Session (S5)** : chacun se reconnecte une fois. Un script qui appelait
  `POST /api/auth/login` et lisait `token` dans la réponse ajoute `"returnToken": true` à sa
  requête. Derrière un reverse proxy TLS, transmettre `X-Forwarded-Proto` pour un cookie
  `Secure`.

## Étape du 2026-10-19 (ex-préversion 0.2.0)

Toutes les pratiques sortent du stade MVP ; la configuration gagne le portefeuille
applicatif, les actifs matériels et la découverte automatique du réseau ; l'API se durcit
(secrets d'exemple refusés, audience du jeton, annuaire injoignable signalé). **Lire la
section Mise à jour avant d'installer** : migrations de base, nouveaux secrets à fournir.

### Ajouté

- **CMDB augmentée et découverte automatique** (chantier F13) : la fiche d'un CI porte son
  identification technique (IP, MAC, système), le **portefeuille applicatif** (code,
  direction, propriétaire métier, responsable DSI, criticité, RTO / RPO, RGPD, NIS 2, cycle
  applicatif) et les **actifs matériels** (constructeur, modèle, numéro de série, garantie,
  fin de support). **Découverte** par balayage IP, SNMP v2c et annuaire Active Directory,
  planifiée par tâche (fréquence, fenêtre horaire) ; les équipements trouvés attendent dans
  un **sas de staging** où ils sont rapprochés des CI (numéro de série, MAC, nom d'hôte, IP),
  créés en CI ou ignorés ; actifs inconnus (Shadow IT) signalés. **Coffre d'identifiants**
  chiffré (communautés SNMP, comptes de domaine), **référentiel des fins de support**,
  **rapport d'inventaire** exportable, onglet **Configuration & inventaire** du tableau de
  bord, bloc du sas dans la console des gestionnaires (CFG-39 à CFG-48, REP-50 à REP-53).

- **Projets sort du MVP**, dernière pratique : PRJ-22 (prévision de la date de fin) et PRJ-34
  (console) sont en place. **Toutes les pratiques ont quitté le stade MVP** ; la mention
  disparaît du README et de la cartographie.

- **Projets** : **date de fin prévue** (vélocité des sprints, phases planifiées ou avancement),
  comparée à la tolérance de délai (PRJ-22) ; **console** : projets à valider, projets hors
  tolérance ou à jalons en retard à relancer, mes projets (PRJ-34). La console affiche aussi
  les demandes en retard, que l'API fournissait déjà (REQ-30).

- **Changements sort du MVP** : toutes les règles listées pour sa sortie sont en place
  (CHG-14 à CHG-16, CHG-20, CHG-21, CHG-24 à CHG-26) ; la mention **(MVP)** disparaît du
  README et de la cartographie. Un changement Standard exige un modèle standard ; les CI
  concernés d'un modèle restent à venir.

- **Gouvernance des changements** : un changement normal à risque élevé est autorisé par le
  **CAB** — deux gestionnaires distincts approuvent, un rejet motivé suffit (CHG-20) ; **périodes
  de gel** sur le calendrier, où seul un changement urgent se planifie (CHG-25) ; **modèles de
  changement standard** : un changement Standard se crée depuis un modèle actif, dont il reprend
  les plans et le risque (CHG-26). Migration `ChangeCabFreezeModels`.

- **Changements** : dates réelles exigées pour la mise en œuvre et la clôture, dépassement de
  la fin planifiée signalé (CHG-14) ; revue post-implémentation exigée pour clore un changement
  urgent ou échoué (CHG-15) ; un changement normal ou urgent s'évalue avec au moins un CI
  relié (CHG-16) ; le demandeur n'autorise pas son propre changement, sauf l'Admin (CHG-21).
  Migration `ChangeActualDatesReview`.

- **Demandes sort du MVP** : toutes les règles listées pour sa sortie sont en place (REQ-09,
  REQ-20 à REQ-23, REQ-30, REQ-31) ; la mention **(MVP)** disparaît du README et de la
  cartographie. Le modèle reste facultatif (demande libre possible) ; les tâches (REQ-24,
  REQ-25) restent au lot 3.

- **Catalogue de demandes** (REQ-20) : les gestionnaires tiennent des modèles (objet, service,
  approbation requise, délai cible, groupe d'exécution) ; une demande se crée depuis un modèle,
  qui en donne l'objet et le service. Un modèle **pré-approuvé** crée la demande Approuvée
  (REQ-21) ; l'**échéance** est calculée à l'approbation en heures de service et seul un
  gestionnaire la modifie (REQ-22) ; le **groupe d'exécution** devient responsable (REQ-23).
  **Demandes en retard** marquées, relancées et listées dans la console (REQ-30). Migration
  `RequestCatalog`.

- **Incidents sort du MVP** : toutes les règles listées pour sa sortie sont en place (INC-07,
  INC-11, INC-13, INC-20 à INC-22, INC-24, INC-25) ; la mention **(MVP)** disparaît du README
  et de la cartographie.

- **Incidents** : le responsable d'un incident majeur doit être gestionnaire du périmètre de
  l'incident, vérifié dans l'annuaire (INC-13) ; **état SLA** de chaque incident (en risque,
  dépassé, suspendu, respecté…) sur la fiche et dans le registre, et incidents en risque ou
  hors délai dans le groupe Urgent de la console du responsable et des gestionnaires (INC-24).

- **Niveaux de service et Amélioration continue sortent du MVP** : toutes les règles listées
  pour leur sortie sont en place (SLM-04, SLM-10, SLM-13, SLM-14, SLM-20, SLM-22 à SLM-24 ;
  CSI-10, CSI-12, CSI-20, CSI-21, CSI-23) ; la mention **(MVP)** disparaît du README et de
  la cartographie. Le respect d'un SLA est mesuré ; sans cible de conformité renseignée, il
  ne déclenche pas d'alerte. La disponibilité mesurée (SLM-21) reste au lot 3.

- **Respect des SLA mesuré** (SLM-20) : part mensuelle des incidents du service résolus dans
  le délai, par priorité, sur la fiche du SLA et par `GET /api/agreements/{id}/compliance` ;
  **cible de respect** facultative sur le SLA. Un mois achevé sous la cible **alerte** les
  gestionnaires et **propose une amélioration** reliée au SLA (SLM-23, CSI-20). Migration
  `AgreementComplianceTarget`.

- **Heures de service** : chaque service porte ses jours ouvrés, sa plage horaire, ses jours
  fériés et son fuseau ; les échéances SLA des incidents s'y comptent (SLM-14, INC-22).
  **Attente motivée** : passer un incident En attente exige un motif (utilisateur,
  fournisseur, changement) ; l'horloge SLA s'arrête pendant l'attente et les échéances en
  sont prolongées à la reprise (INC-07, INC-22). Migration `ServiceHoursOnHold`.

- **Service du catalogue** sur les incidents, problèmes et demandes : choisi dans une liste au
  lieu d'un texte libre, son nom recopié pour l'affichage et le reporting ; l'échéance SLA de
  l'incident suit ce service (SLM-10, INC-20) ; un service retiré ne se choisit plus pour un
  nouvel enregistrement ni un nouveau SLA (SLM-15). Migration `ServiceCatalogLinks` : les
  anciens libellés égaux au nom d'un service du catalogue y sont rattachés.

- **Niveaux de service** : un SLA dont la validité est passée **expire** de lui-même, les
  gestionnaires sont prévenus (SLM-04) ; **délais de prise en charge** P1 à P4 dans le SLA et
  échéance de prise en charge sur l'incident (SLM-13) ; **revue de SLA** : constats et
  décisions enregistrés, prochaine revue reportée de 12 mois (SLM-22). Migration
  `AgreementReviewResponse`.

- **Conformité NIS 2 sort du MVP** : toutes les règles listées pour sa sortie sont en place
  (NIS-08, NIS-09, NIS-14, NIS-16, NIS-17, NIS-18) ; la mention **(MVP)** disparaît du README
  et de la cartographie. Les preuves se relient aux enregistrements ; les pièces jointes
  suivront avec SOC-25.

- **Conformité NIS 2 — preuves et comparaison** : chaque réponse porte ses **preuves**,
  enregistrements désignés par leur référence (changement, article, CI…), reprises dans
  l'export (NIS-08 ; pièces jointes à venir avec SOC-25) ; onglet **Comparaison** avec une
  autre évaluation du même périmètre : évolution des scores par pilier, objectif et
  thématique, exigences dont la réponse a changé (NIS-09). Migration `AssessmentEvidence`
  (nouvelle table).

- **Conformité NIS 2** : la synthèse montre pour chaque écart l'action d'amélioration liée et
  son statut (NIS-14) ; date de la prochaine évaluation (12 mois après la validation par
  défaut), relance du pilote à l'échéance et entrée « À renouveler » dans la console des
  gestionnaires (NIS-16) ; **export CSV de la synthèse** pour les auditeurs (NIS-17).
  Migration `AssessmentNextDate` (une colonne nulle).

- **Problèmes** : rouvrir un problème résolu ou clos exige un motif (PRB-10) ; une action
  corrective n'est déclarée terminée que par son approbateur (A) ou un gestionnaire, son
  responsable (R) demande l'approbation, l'A est notifié (PRB-25) ; **créer un changement
  depuis une action** : titre et description repris, changement rattaché à l'action et relié
  au problème (PRB-27, CHG-23). Migration `ProblemReopenActionApproval` (colonnes nulles).

- **Configuration sort du MVP** : toutes les règles listées pour sa sortie sont en place
  (CFG-12 à CFG-15, CFG-20 à CFG-22) ; la mention **(MVP)** disparaît du README et de la
  cartographie.

- **Configuration** : vérification d'un CI par son propriétaire ou un gestionnaire, relance
  au propriétaire d'un CI En service non vérifié depuis 12 mois (CFG-15) ; services touchés
  par un CI et par ses CI en aval (CFG-21) ; onglet **Impact** de la fiche d'un changement
  (CFG-20) ; CI passé Hors service notifié aussi aux responsables des services touchés
  (CFG-22). Migration `CiVerification` (deux colonnes nulles).

- **Amélioration continue** : source de chaque amélioration et enregistrement d'origine
  relié (CSI-10) ; valeur et effort notés à la validation, priorité calculée (CSI-12) ;
  amélioration en retard relancée à son porteur (CSI-21) ; **récurrence après correctif** :
  un nouvel incident relié à un problème résolu ou clos alerte les gestionnaires et propose
  une amélioration reliée (PRB-23, CSI-20).

- **Connaissances sort du MVP** : toutes les règles listées pour sa sortie sont en place
  (KB-10, KB-11, KB-12, KB-17, KB-20, KB-21) ; la mention **(MVP)** disparaît du README et
  de la cartographie. RAG-10 (calibrage de la recherche), en attente d'un jeu de requêtes
  réelles, ne la conditionne pas (décision du 2026-10-13).

- **Notifications datées** (SOC-21) : relevé périodique (15 minutes, `Notifications:IntervalMinutes`) — SLA en risque → responsable ; SLA dépassé et incident P1 → responsable et gestionnaires (escalade, INC-18) ; veille d'une mise en œuvre → responsable du changement ; action corrective en retard → R, A et gestionnaires ; revue d'article échue → responsable ; SLA expirant dans 30 jours → gestionnaires, revue de SLA échue → propriétaire du service. Un même événement n'est notifié qu'une fois.

- **Notifications dans l'application** (SOC-21, SOC-23) : cloche avec compteur des non lues ;
  toute affectation notifie le nouveau responsable (ou son groupe) ; décisions attendues →
  gestionnaires du périmètre (demande soumise, changement évalué, amélioration proposée,
  article soumis, incident majeur) ; décisions rendues → demandeur (approbation, rejet,
  autorisation, publication, validation, problème qualifié ou clos, incident résolu) ;
  action corrective affectée → son R et son A. Jamais pour un enregistrement que le
  destinataire ne voit pas, jamais à l'auteur de l'événement.

- **Commentaires** (SOC-24) : onglet « Commentaires » sur chaque fiche ; un commentaire est
  public (lu par le demandeur) ou note de travail (lue et écrite par ceux qui traitent
  l'enregistrement, jamais par le demandeur qui n'est pas intervenant) ; retrait par l'auteur
  ou un Admin.

- **Conformité NIS 2 — version du référentiel** (NIS-18) : chaque évaluation enregistre la
  version du ReCyF évaluée ; à la validation, ses exigences applicables sont figées, et une
  nouvelle version embarquée ne change ni son questionnaire, ni son score, ni la synthèse du
  reporting ; la rouvrir la réaligne sur la version en vigueur.

- **Administration › Périmètres** (Admin) : liste des périmètres (supprimés grisés et
  restaurables), création, fiche avec renommage, enregistrements rattachés par pratique,
  affectations d'utilisateurs ou de groupes AD choisis dans l'annuaire (Manager ou User),
  suppression douce et suppression définitive quand rien n'y est rattaché (PER-01, PER-02,
  PER-12). L'étape 6 du lot 1 est terminée.
- **Périmètres — vues transverses** (lot 1, étape 6) : un lien vers un enregistrement d'un
  autre périmètre reste visible mais masqué ; la console, le reporting, la recherche et les
  cas similaires se limitent aux périmètres de l'utilisateur, le pilotage de la console aux
  périmètres qu'il gère ; filtre « Périmètre » dans la console et le reporting (PER-08,
  PER-09).

- **Périmètres — droits et visibilité** (lot 1, étape 6, deuxième partie) : chacun voit les
  enregistrements de ses périmètres (plus le catalogue des services et les articles publiés)
  et crée dans l'un d'eux (choix proposé dès qu'il en a plusieurs) ; les décisions de
  gestionnaire exigent d'être Manager du périmètre de l'enregistrement, et le rôle par
  périmètre prime sur le rôle AD ; déplacer un enregistrement exige d'être gestionnaire des
  deux périmètres, et se trace au journal. Un compte sans affectation garde son rôle AD sur
  « Organisation » (PER-03 à PER-07, PER-10 amendé).

- **Connaissances au-delà du MVP** : contenu rendu en **Markdown** sans HTML brut (onglet
  Lecture, KB-10) ; date de revue fixée à un an à la publication (KB-11) ; article
  d'erreur connue créé depuis le problème, puis proposé à l'archivage quand le problème est
  corrigé (PRB-30, KB-12) ; utilisation de l'article (incidents et demandes reliés, KB-14) ;
  revue confirmée par le responsable ou archivage demandé (KB-17) ; archiver et réactiver
  réservés aux gestionnaires (KB-18) ; brouillon soumis à publication, qui remonte dans les
  décisions des gestionnaires (KB-21).

- **Périmètres — modèle et administration** (lot 1, étape 6, première partie) : périmètres
  (direction, site, entité) avec suppression douce, restauration et suppression définitive
  contrôlée ; affectations d'utilisateurs ou de groupes AD avec le rôle Manager ou User ;
  compteurs par pratique ; tout tracé au journal d'audit. À la mise à niveau, chaque
  enregistrement est rattaché au périmètre « Organisation » : les droits sont inchangés
  (PER-01, PER-02, PER-10 à PER-12).

- **Reporting** : tableau de bord à onglets — synthèse globale avec score de santé expliqué,
  incidents, problèmes, changements et projets, conformité et métriques ; filtres période
  (7, 30, 90 jours, 12 mois, personnalisée) et service ; badges d'alerte sur les onglets ;
  export CSV de l'onglet actif (Excel) et PDF de tous les onglets (règles REP).
- **Incidents** : échéance de résolution tirée du SLA en vigueur du service (INC-21,
  partiel), résolution au premier contact (INC-23), début constaté et MTTD (INC-27).

- **Projets** (gestion de projet, ADR-0015) : la méthode — Scrum, Kanban, Cycle en V,
  PRINCE2 ou hybride — active les composants du projet (backlog, sprints et burndown,
  tableau, phases ou étapes, jalons, livrables, risques, incidents projet, business case,
  tolérances). Le chef de projet crée et conduit, un gestionnaire valide le lancement ;
  synthèse d'avancement et alertes de tolérance. Nouvelle section de menu **Projets**
  (repère vert) entre Pratiques et Conformité & réglementation.

- **Groupes AD dans le jeton** (lot 1, étape 5) : un incident, une demande, un changement,
  une amélioration ou une action confiés à un groupe AD apparaissent dans « Mon travail »,
  « Assignés à moi » et « Mes actions » de chacun de ses membres ; un membre du groupe
  désigné R ou A d'une action en a les droits (SOC-10, PRB-26 — résout M3).

- **Navigation** : section **Conformité & réglementation** (repère violet, tous les rôles)
  entre Pratiques et Paramétrage ; « Conformité NIS 2 » y passe sous le libellé « NIS 2 »,
  la section Pratiques ne garde que les 8 pratiques ITIL. Sections repliables (sauf
  Pilotage), choix mémorisé dans le navigateur ; la section de la page ouverte reste dépliée.

- **Conformité NIS 2** : évaluation au Référentiel Cyber France de l'ANSSI (catégorie
  d'entité, questionnaire noté 0 à 3, scores par thématique, objectif et pilier, maturité,
  écarts, actions d'amélioration) ; structure du référentiel embarquée, texte des exigences
  importé par l'administrateur (ADR-0012). Première entrée de la section Paramétrage.
- **Recherche hybride** (mots et sens) : bloc « Chercher d'abord » dans les connaissances,
  « Cas similaires » sur les fiches incident et problème, Administration › Index de
  recherche ; modèle `bge-m3` dans le nouveau service `embeddings` (facultatif, environ
  1,2 Go téléchargés au premier démarrage) ; sans lui, recherche plein texte (ADR-0013).
- Configuration : vue d'impact d'un CI, amont et aval, transitive (#30).
- Changements : détection des conflits au calendrier, deux changements non rejetés sur un
  même CI et des créneaux qui se chevauchent (#29).
- **Historique** sur chaque fiche : journal d'audit des créations, champs modifiés
  (ancienne et nouvelle valeur), transitions, suppressions et liens, avec auteur et date ;
  conservé 3 ans (SOC-20).
- **Transition forcée** par un administrateur, hors du cycle de vie, avec un motif
  obligatoire tracé à l'historique (SOC-05).

- **Droits et statuts — étape 4 du lot 1** :
  - Incidents : réouverture encadrée — fenêtre de 10 jours ouvrés, motif obligatoire (à
    renouveler à chaque réouverture), compteur `ReopenCount` (INC-11) ; caractère majeur
    déclaré ou retiré par un Manager seulement, priorité forcée P1, responsable nominatif
    (pas un groupe) désigné par un Manager (INC-13).
  - Demandes de service : statut **Annulée** (final), annulation réservée au demandeur ou
    à un Manager, date `CancelledAt` tracée (REQ-09).
  - Configuration : retrait d'un CI réservé au Manager (CFG-12), bloqué si un incident ou
    changement ouvert est lié au CI (CFG-13), liaison interdite vers ou depuis un CI Retiré
    (CFG-14).
  - Problèmes : modification d'une action (titre, description, échéance, RACI) réservée
    au R, à l'A ou à un Manager ; la mise à jour du seul statut reste ouverte à tous
    (PRB-24 — résout M7).

### Modifié

- **Passage à .NET 10 (LTS)** : API en `net10.0`, EF Core 10, Npgsql EF 10, JwtBearer 10,
  Swashbuckle 10 (OpenAPI 3), client LDAP 4 (asynchrone) ; images Docker `sdk:10.0` et
  `aspnet:10.0`. Frontend en **React 19**, Vite 8, Vitest 5. .NET 11 n'est encore qu'en
  version candidate : il sera adopté à sa sortie stable.

- **Recherche d'un compte dans l'annuaire** : les renvois (*referrals*) d'Active Directory sont
  ignorés ; un compte inconnu est refusé comme responsable d'un incident majeur, et la
  connexion ne journalise plus de faux échec LDAP.

- **Sous-titres des pages** : le numéro de version d'ITIL est retiré (« (ITIL) ») ; la page
  Problèmes affichait « ITIL v3 » alors que l'application suit les pratiques ITIL 4.
- **Problèmes sur le socle commun** : le module Problèmes repose désormais, comme les autres
  pratiques, sur `Record` et `RecordController` (registre, transitions, transition forcée,
  périmètres, journal) ; il gagne un **gestionnaire du problème** (responsable AD, par défaut
  le gestionnaire qui qualifie, dans son « Mon travail », PRB-21). API inchangée pour les
  clients existants.
- **NIS-15** : créer une action d'amélioration depuis un écart est désormais réservé aux
  gestionnaires (du périmètre de l'évaluation), comme l'indique le tableau des droits ; l'API
  l'acceptait de tout utilisateur.
- **Validations du cycle de vie** (lot 1, étape 3) : prise en charge d'un incident avec un
  responsable (date de première prise en charge) ; code de résolution, un doublon relié à
  l'incident conservé ; motif de rejet des demandes et des changements ; plans exigés à
  l'évaluation d'un changement, fin planifiée ; propriétaire d'un CI ou d'un service en
  service ; nom de CI unique par environnement ; SLA en vigueur cohérent (cibles complètes et
  croissantes, un seul par service et client) ; amélioration mesurable à la validation,
  réalisée à l'étape 6, abandon motivé ; problèmes : valeurs fermées, contournement d'une
  erreur connue, cause racine et actions terminées pour résoudre, code de clôture, règles
  RACI à la modification d'une action, date d'achèvement conservée (#23, #24, #28).
- **Transitions de statut contraintes** (SOC-05) : chaque pratique n'accepte que les
  transitions de son cycle de vie documenté ; une autre est refusée avec les statuts
  accessibles, et la fiche ne propose plus que ceux-là. Statuts finaux : incident clos,
  demande rejetée ou close, changement rejeté ou clos, CI retiré, SLA expiré, amélioration
  réalisée.
- Page de connexion : titre « Piloter le service IT, de l'incident à l'amélioration. » ;
  sous-titre de marque « Plateforme ITIL ».

### Sécurité

- **Secrets d'exemple refusés** (S7) : hors développement, l'API refuse de démarrer avec la
  clé JWT, la clé du coffre de la découverte, le mot de passe du compte de service ou celui de
  la base livrés en exemple, ou avec une clé JWT de moins de 64 caractères ; le mode démonstration l'accepte explicitement
  (`Security:AllowSampleSecrets`) et le journalise.
- **Audience du jeton** (S8) : les jetons portent et doivent porter l'audience `S-Aloha`
  (`Jwt:Audience`).

### Corrigé

- **Annuaire injoignable** (R6) : la connexion répond « Annuaire injoignable » (503) au lieu
  de « identifiants invalides », et la recherche de personnes et de groupes répond 503 au lieu
  d'une erreur 500. Un contrôleur de domaine qui ne répond pas est abandonné après
  `Ldap:TimeoutSeconds` (5 s par défaut) au lieu du délai TCP du système (R7).

### Mise à jour

- **Secrets** : une installation hors démonstration qui garde la clé JWT, la clé du coffre
  (`Discovery:VaultKey`), le mot de passe de base ou celui du compte de service d'exemple ne
  démarre plus (S7) : les remplacer avant de
  mettre à jour. Les jetons émis avant la mise à jour n'ont pas d'audience : chacun se reconnecte
  une fois (S8).
- **.NET 10** : reconstruire les images (`docker compose build`) ; aucune migration de base
  n'accompagne ce passage. En développement : SDK .NET 10 et `dotnet-ef` 10
  (`dotnet tool update -g dotnet-ef --version 10.*`) ; `DOTNET_ROLL_FORWARD` n'est plus utile.
- `docker compose up` démarre désormais le service `embeddings` et télécharge le modèle
  `bge-m3` (environ 1,2 Go) au premier démarrage ; pour s'en passer :
  `docker compose up -d db api web` et `Embeddings__Url` vide
  ([exploitation § Recherche](docs/reference/exploitation.md#recherche)).
- **Découverte (CFG-45)** : définir `Discovery__VaultKey` (32 caractères au moins, hors du
  dépôt) **avant** d'enregistrer un premier identifiant, puis ne plus la changer : les
  secrets déjà chiffrés deviendraient illisibles. La migration `CmdbDiscovery` s'applique au
  démarrage. Le balayage IP a besoin que l'API joigne les réseaux visés (ping et ports TCP ;
  un conteneur sans ICMP s'en tient aux ports).
- **Portefeuille applicatif et actifs matériels (CFG-43, CFG-44)** : une application En
  service ou Hors service doit désormais porter son code, sa direction, son duo de
  gouvernance et sa criticité ; un équipement réseau, de stockage ou un poste, son
  constructeur, son numéro de série et son emplacement. Les CI existants restent lisibles,
  mais leur prochaine modification exigera ces champs : les compléter depuis l'inventaire
  (filtre « Gouvernance incomplète »).

## Étape du 2026-10-06 (ex-préversion 0.1.0)

Première ouverture publique du dépôt.

### Ajouté

- Console orientée action, composée côté serveur selon le rôle (Admin, Gestionnaire,
  Utilisateur).
- Gestion des problèmes : priorité impact × urgence, analyses de cause racine (5 Pourquoi,
  Ishikawa 6M, arbre des défaillances), erreurs connues, actions correctives avec matrice
  RACI affectée à des utilisateurs ou groupes AD.
- Sept pratiques ITIL 4 en MVP : incidents, demandes, changements (avec calendrier),
  configuration (CI et relations), niveaux de service (catalogue et SLA), connaissances,
  amélioration continue ; liens entre enregistrements de tous les processus.
- Reporting : MTTR problèmes et incidents, taux de changements réussis, retards,
  volumétrie par processus.
- Identité Active Directory (LDAP → JWT), sans compte local ; thèmes clair et sombre.
- Mode démonstration : annuaire Samba AD et données d'exemple fictives, sans AD
  d'entreprise.
- Images publiées sur `ghcr.io` : `s-aloha-api`, `s-aloha-web`, `s-aloha-annuaire-demo`.
- Migrations EF, avec reprise des bases créées par les versions de développement.

### Mise à jour

- Première version : pas de mise à jour depuis une version publiée. Une base créée avant
  les migrations EF est reprise automatiquement
  ([ADR-0008](docs/decisions/adr-0008-migrations-ef.md)).
- Configuration par défaut destinée à l'évaluation : suivre la
  [liste de durcissement](docs/reference/securite.md#points-de-durcissement-avant-production)
  avant toute mise en production.
