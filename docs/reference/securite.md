# 🛡️ Sécurité

> Le modèle de sécurité tel qu'il est, et ce qui reste à durcir avant une mise en production. Correctifs suivis dans [GitHub Issues](https://github.com/chrraj7/S-Aloha/issues) (label `security`).

<sub>[← Documentation](../readme.md) · [Produit](produit.md) · [Règles métier](regles-metier.md) · [Architecture](architecture.md) · [Base de données](base-de-donnees.md) · [Frontend](frontend.md) · [Exploitation](exploitation.md) · [Sécurité](securite.md) · [Glossaire](glossaire.md)</sub>

## Identité et authentification

- Aucune identité locale : **aucun mot de passe ni compte n'est stocké** par S-Aloha. Seule la
  **date de dernière connexion** de chaque compte est retenue (ADM-12), effacée après 13 mois
  sans connexion : donnée personnelle minimale, à mentionner au registre des traitements.
  Un utilisateur qui choisit de recevoir ses notifications par courriel (SOC-22) fait retenir
  son adresse, son nom affiché, son rôle et ses groupes AD (relevés à chaque connexion) ;
  revenir à « Aucun » efface ces données.
- Connexion (`POST /api/auth/login`) : recherche de l'utilisateur dans l'AD par le compte
  de service, puis **bind LDAP avec le DN et le mot de passe de l'utilisateur** — c'est
  l'AD qui valide le mot de passe.
- Rôle déterminé par l'appartenance (`memberOf`) aux groupes configurés ; aucun groupe ⇒
  connexion refusée.
- Annuaire injoignable (serveur absent, compte de service refusé) : 503 « Annuaire
  injoignable », distinct d'un refus d'identifiants (401) ; l'erreur est journalisée (R6).
- Jeton **JWT HS256** signé avec `Jwt:Key`, vérifié sur l'émetteur, l'**audience**
  (`Jwt:Audience`, S8) et la signature, durée `Jwt:ExpiryHours` (8 h par défaut). Pas de révocation : un jeton
  reste valide jusqu'à expiration, même si l'utilisateur est retiré du groupe AD.
- **Côté navigateur** (S5, [ADR-0018](../decisions/adr-0018-jeton-en-cookie-httponly-et-csp.md)) : le
  jeton vit dans le cookie `saloha_session`, `HttpOnly` (illisible par un script de la
  page), `SameSite=Strict`, limité à `/api`, `Secure` dès que la requête arrive en HTTPS ;
  déconnexion par `POST /api/auth/logout`. **CSRF** : une modification authentifiée par le
  cookie sans l'en-tête `X-S-Aloha: 1` reçoit 403. Les clients d'API (scripts) obtiennent le
  jeton sur demande (`returnToken`) et l'envoient en `Authorization: Bearer`.
- **CSP** servie par nginx : scripts de la seule origine (le script de thème en ligne autorisé
  par son empreinte), styles et polices de la seule origine (ADR-0021), `frame-ancestors 'none'`, `object-src 'none'` ;
  avec `X-Content-Type-Options: nosniff` et `Referrer-Policy: same-origin`.
- Le jeton porte les **groupes AD directs** de l'utilisateur (SOC-10), lus à la connexion :
  un changement d'appartenance prend effet à la connexion suivante. Les noms de groupes
  sont visibles de qui détient le jeton (son titulaire).

## Autorisation

Trois policies imbriquées dans `backend/Program.cs` :

| Policy | Rôles admis |
|---|---|
| `User` | User, Manager, Admin |
| `Manager` | Manager, Admin |
| `Admin` | Admin |

Toute route sauf `/api/auth/login` et les sondes `/health/live` et `/health/ready` (état seul,
sans détail, O1) exige au minimum `User`. Les routes internes des fonctionnalités optionnelles
(`/internal/…`) n'existent que sur le port interne 8081, et exigent le jeton du conteneur
`features` (`Features:Token`, 32 caractères au moins, comparé en temps constant ; policy
`FeatureContainer`) : un jeton d'utilisateur n'y ouvre rien ([OPT-10, OPT-11](fonctionnalites-optionnelles.md#3-contrat-entre-lapi-et-le-conteneur-des-fonctionnalités)). Dans l'autre sens, le conteneur `features` exige le même jeton à chaque appel de l'API, contrôle de santé compris (401 sans lui) ; il tourne sans privilège, n'est publié sur aucun port, et refuse de démarrer avec le jeton d'exemple hors démonstration (S7). Au-delà des policies, le
**rôle effectif par périmètre** (`Core/Perimeters/PerimeterAccess`, [périmètres](perimetres.md))
décide de la visibilité (404 hors de ses périmètres) et des décisions de gestionnaire (403),
dans le socle des pratiques et le module Problèmes. Le frontend masque des
actions selon le rôle, mais **le contrôle fait foi côté API**. La console est composée
côté serveur : un client ne reçoit jamais un bloc non autorisé pour son rôle. Matrice
complète : [`regles-metier.md`](regles-metier.md#1-rôles-et-droits).

**Coffre d'identifiants de la découverte** ([CFG-45](processus/gestion-de-la-configuration.md)) :
communautés SNMP v2c, phrases des utilisateurs SNMP v3 (MD5 et DES refusés, CFG-51), comptes SSH (mot de passe ou clé privée, CFG-52), jetons Proxmox VE et comptes vCenter (CFG-54) et comptes de domaine chiffrés en AES-256-GCM (chiffrement authentifié : une
valeur altérée en base est refusée), clé dérivée de `Discovery:VaultKey`, tenue hors de la
base ; l'API n'en renvoie jamais la valeur, et un échec de scan n'en cite pas. Écriture
réservée à l'Admin. L'inventaire SSH vérifie la clé de l'hôte avant d'envoyer l'identifiant
(confiance au premier contact ; clé changée refusée jusqu'à acceptation par l'Admin, CFG-53) et
n'exécute que des lectures, sans sudo : un compte sans droits d'administration suffit. Les API
d'hyperviseurs ne sont appelées qu'en HTTPS ; un certificat autosigné est retenu au premier
contact de la même façon (CFG-54) ; des comptes en lecture seule suffisent (PVEAuditor,
Lecture seule de vCenter). L'inventaire WinRM n'emploie que HTTPS et NTLM (implémentation
gérée de .NET, `System.Net.Security.UseManagedNtlm`) : un serveur qui réclamerait Basic
n'obtient pas le mot de passe (CFG-57).

**Pièces jointes** ([SOC-25](regles-metier.md#8-notifications)) : liste fermée de types
(bureautique, texte, images matricielles) ; le type servi vient de l'extension, jamais du
navigateur, et la signature du contenu doit lui correspondre ; HTML, SVG, scripts et
exécutables sont refusés. Le nom de fichier est assaini (ni chemin ni caractère de contrôle),
l'objet rangé sous une clé aléatoire. Le téléchargement est toujours une pièce jointe
(`Content-Disposition: attachment`), avec `X-Content-Type-Options: nosniff` et une CSP
`sandbox` : le navigateur n'interprète pas le fichier. Pas d'antivirus intégré : brancher, au
besoin, l'analyse du stockage de l'organisation.

**Messagerie et stockage** ([ADM-60 à ADM-63](administration.md#7-messagerie-et-stockage)) : le mot
de passe SMTP et la clé secrète S3 sont chiffrés par le même coffre, jamais renvoyés par
l'API ni écrits au journal d'audit ; réglage réservé à l'Admin. Le certificat du serveur SMTP
est toujours vérifié ; « Aucun » chiffrement ne convient qu'à un relais du réseau interne.

## Surface exposée

| Port | Service | Contenu |
|---|---|---|
| 80 | `web` (nginx) | Frontend + proxy `/api/` |
| 8080 | `api` | API **et Swagger** (`/swagger`), exposés directement |
| 8081 | `api` (port interne) | Routes `/internal/…` du conteneur des fonctionnalités optionnelles et sondes, rien d'autre ; **jamais publié** hors du réseau compose ([OPT-11](fonctionnalites-optionnelles.md#3-contrat-entre-lapi-et-le-conteneur-des-fonctionnalités)) |
| — | `db` | Non publié hors du réseau compose |
| sortant | `api` (découverte) | Ping, ports TCP témoins, SNMP (UDP 161) vers les réseaux des tâches de scan, LDAP vers l'annuaire : à ouvrir par le pare-feu vers les seuls réseaux à inventorier ([CFG-46](processus/gestion-de-la-configuration.md)) |
| — | `garage` | Stockage S3 des pièces jointes (profil `pieces-jointes`) : non publié hors du réseau compose ; l'API l'atteint sur `garage:3900` ([SOC-25](regles-metier.md#8-notifications)) |
| — | `embeddings` | Non publié hors du réseau compose ; reçoit le texte des contenus indexés, qui **ne quitte pas le SI** (modèle exécuté localement, [ADR-0013](../decisions/adr-0013-recherche-hybride-service-embeddings-separe.md)) |

## Points de durcissement avant production

État au 2026-10-06 — constat détaillé :
[`archives/etat-initial-2026-10-06.md`](../archives/etat-initial-2026-10-06.md).

| Point | État actuel | Cible |
|---|---|---|
| Secrets | ✅ `docker-compose.yml` lit le mot de passe PostgreSQL, `Jwt:Key` et `Discovery:VaultKey` dans l'environnement, vides à défaut (S1, [exploitation § Secrets](exploitation.md#secrets)) ; valeurs **d'exemple** de ces trois réglages dans `appsettings.json` et `docker-compose.demo.yml` seulement, du jeton du conteneur `features` (`Features:Token`, `FEATURES_TOKEN`) dans `docker-compose.demo.yml` seulement, et de `BindPassword` dans `appsettings.json` et `docker-compose.yml` ; hors développement, l'API **refuse de démarrer** avec elles (le conteneur `features` aussi, avec son jeton d'exemple) ou avec une clé JWT de moins de 64 caractères (S7), sauf en démonstration (`Security:AllowSampleSecrets`, avertissement au journal) | Secrets Docker ou variables d'environnement injectées, clé JWT aléatoire ≥ 64 caractères, clé du coffre aléatoire ≥ 32 caractères fixée avant le premier identifiant |
| LDAP | ✅ LDAPS (636) pris en charge, avec l'autorité de l'annuaire (`Ldap:CaCertificatePath`) ; validation du certificat et du nom jamais désactivée ([exploitation § LDAPS](exploitation.md#ldaps)). Par défaut `UseSsl: false`, port 389 : mots de passe utilisateurs en clair sur le réseau | `UseSsl: true`, port 636 |
| Transport | ✅ `docker-compose.https.yml` : reverse proxy TLS (Caddy), seuls 80 (redirection) et 443 publiés ([exploitation § HTTPS](exploitation.md#https)). Par défaut, HTTP sur le port 80 | Lancer avec ce fichier |
| CORS | ✅ Aucune origine externe par défaut ; `Cors:AllowedOrigins` les liste, sans cookie (S3) | Laisser vide si le frontend passe par nginx (même origine) |
| Exposition | API et Swagger publiés sur 8080 | API joignable seulement via nginx ; Swagger désactivé en production |
| Jeton côté navigateur | ✅ Cookie `HttpOnly`, `SameSite=Strict`, en-tête anti-CSRF, CSP servie par nginx ([ADR-0018](../decisions/adr-0018-jeton-en-cookie-httponly-et-csp.md)) ; non `Secure` en HTTP seul | HTTPS ([S2](exploitation.md#https)) pour un cookie `Secure` |
| Révocation | Aucune avant expiration | Durée courte et/ou contrôle de l'appartenance AD |
| Tentatives de connexion | Aucune limitation de débit | Limitation par IP et par compte |

**Dépendances** — contrôle : `dotnet list package --vulnerable --include-transitive`
(backend) et `npm audit` (frontend). Corrigé le 2026-10-06 (`8f918a1`) :
`Microsoft.Extensions.Caching.Memory` 8.0.0, tirée par EF Core 8.0.8
([GHSA-qj66-m88j-hmgj](https://github.com/advisories/GHSA-qj66-m88j-hmgj), gravité haute) ;
passage à EF Core et JwtBearer 8.0.31, Npgsql EF 8.0.11. Plus aucune vulnérabilité
relevée sur l'API.

Frontend, corrigé le 2026-10-06 : 16 alertes, dont 2 critiques (tinypool, exécution de code
par pollution de prototype). Passage à Vite 6.4, Vitest 4.1 et react-router 7.18 ; les
dépendances transitives (postcss, esbuild, browserslist, source-map-js) suivent.
`npm audit` : 0 vulnérabilité.

Mise à niveau du 2026-10-16 : .NET 10 (LTS) — EF Core et JwtBearer 10.0.13, Npgsql EF 10.0.3,
Swashbuckle 10.3, Novell LDAP 4.0 — et React 19, Vite 8, Vitest 5. `dotnet list package
--vulnerable --include-transitive` et `npm audit` : 0 vulnérabilité.

Depuis le 2026-11-18, l'audit du frontend passe par
[`scripts/check-audit-npm.py`](../../scripts/check-audit-npm.py) : deux `npm audit`, toutes
dépendances et paquet livré seul (`--omit=dev`). Un avis haut ou critique sur le paquet livré
bloque toujours ; sur l'outillage de build et de test, il bloque sauf **exception datée**,
un avis (GHSA) par exception, inscrite dans le script et donc soumise à l'étiquette
`outillage`. Une exception attend une version corrigée ; échue, elle bloque de nouveau.

Aucune exception en cours. La dernière, GHSA-fc5q-39xc-vjpf (picomatch ≤ 4.0.7, tiré par vite
et vitest), est retirée le 2026-11-24 : l'avis a été retiré par GitHub le 2026-11-18, et le
script la signalait sans objet.

## Contrôles du dépôt

Réglages du dépôt GitHub public, en place depuis le 2026-10-06 :

| Contrôle | Effet |
|---|---|
| Signalement privé de vulnérabilités | Les failles arrivent hors des issues publiques ([SECURITY.md](../../SECURITY.md)) |
| Secret scanning + push protection | Un push contenant un secret reconnu est refusé |
| Dependabot (alertes, correctifs, versions) | Alerte et PR de correctif dès qu'un avis touche une dépendance ; mises à jour hebdomadaires groupées (npm, NuGet, Docker, Docker Compose, Actions) ; une mise à jour mineure ou corrective se fusionne d'elle-même, CI au vert, une majeure reste à la main ; « Suivi Dependabot » ([`dependabot-suivi.yml`](../../.github/workflows/dependabot-suivi.yml), toutes les 6 h) supprime ensuite sa branche et lance la CI de `main`, que la fusion par le jeton du workflow ne déclenche pas |
| CodeQL ([`codeql.yml`](../../.github/workflows/codeql.yml)) | Analyse statique des workflows, du C# et du JavaScript sur chaque pull request (fork compris), chaque poussée de `main` et chaque semaine ; une PR sans résultat CodeQL ne peut pas être fusionnée |
| CI ([architecture § Intégration continue](architecture.md#intégration-continue)) | Tests, build, audit des dépendances, santé de la doc |
| Preuve et garde-fous | Une PR prouve son changement par un test qui échoue sans lui ; elle ne peut ni retirer ni désactiver de test, ni modifier l'outillage de vérification sans l'accord du mainteneur ; sa classe de risque est calculée d'après ses chemins |
| Revue des dépendances et des workflows | Dépendance ajoutée vulnérable (haute, critique) ou sous licence incompatible refusée ; workflows analysés par actionlint et zizmor |
| Filet après fusion | CI de `main` rouge → issue `main-rouge` avec la commande d'annulation |
| Règles de `main` | Ni suppression ni push forcé ; toute modification passe par une pull request, CI au vert (DCO et Recette compris) et historique linéaire. Aucune dérogation, administrateur compris |
| Actions | Seules les actions publiées par GitHub ; jeton en lecture seule ; workflows de la première contribution d'un contributeur externe soumis à approbation |

Première analyse CodeQL : une alerte, `cs/log-forging` (gravité moyenne). L'identifiant
saisi à la connexion était journalisé tel quel en cas d'échec LDAP : des sauts de ligne
permettaient d'insérer de fausses lignes dans le journal. Ils sont retirés avant
journalisation (`backend/Core/Auth/LdapService.cs`).

Non disponibles sans GitHub Advanced Security : détection des secrets hors fournisseurs
connus et vérification de validité des secrets détectés.
