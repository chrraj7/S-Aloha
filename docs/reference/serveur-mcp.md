# 🔌 Serveur MCP

> [Fonctionnalité optionnelle](fonctionnalites-optionnelles.md) (clé `mcp-server`,
> conteneur propre `mcp`, à part du conteneur commun des autres fonctionnalités). S-Aloha ouvre en **lecture**, par le *Model Context Protocol*, ses
> enregistrements et sa recherche aux assistants qu'emploient ses utilisateurs, avec les
> droits de chacun. Rien ne s'y écrit. Préfixe des règles : **MCP**. Décision :
> [ADR-0024](../decisions/adr-0024-serveur-mcp-conteneur-separe-lecture-seule.md).

<sub>[← Fonctionnalités optionnelles](fonctionnalites-optionnelles.md) · [Recherche](recherche.md) · [Périmètres](perimetres.md) · [Sécurité](securite.md)</sub>

## 1. Objectif

Un technicien qui travaille avec un assistant compatible MCP, sur son poste ou dans son
éditeur, l'interroge sur ses tickets, la connaissance ou l'impact d'un CI, sans copier-coller
et sans aucun droit de plus que dans l'interface.

```mermaid
sequenceDiagram
    actor U as Utilisateur
    participant C as Assistant (client MCP)
    participant W as nginx
    participant M as mcp
    participant A as API
    U->>C: « quels incidents ouverts sur la messagerie ? »
    C->>W: POST /mcp, jeton personnel
    W->>M: relaie
    M->>A: GET /api/search…, même jeton
    A-->>M: résultats visibles de l'utilisateur
    M-->>C: résultat de l'outil (contenu marqué comme donnée)
```

## 2. Règles

| ID | Règle | Statut | Lot |
|---|---|---|---|
| MCP-01 | **Transport** : HTTP « Streamable HTTP » du protocole, sur `/mcp`, que nginx relaie au conteneur `mcp` ; version du protocole : celle du SDK retenu, consignée ici à la livraison. Fonctionnalité coupée ou conteneur arrêté : 404. Une requête dont l'en-tête `Origin` est présent et absent de `Cors:AllowedOrigins` est refusée (403), contre le détournement par résolution DNS | 🔜 | 3 |
| MCP-02 | **Pour un utilisateur, jamais pour soi** : le conteneur n'a ni jeton propre, ni route interne, ni accès à la base, ni secret ; chaque outil appelle les routes **publiques de lecture** de l'API avec le jeton personnel de l'utilisateur (MCP-03). Une donnée qui manque à un outil s'ajoute à l'API | 🔜 | 3 |
| MCP-03 | **Jetons d'accès personnels** : créés par l'utilisateur (Préférences › Jetons d'accès) avec un nom et une durée (1 à 90 jours, 30 par défaut) ; affichés **une seule fois** ; conservés sous forme d'empreinte SHA-256 ; 5 jetons actifs au plus par compte ; révocables par l'utilisateur et par l'Admin. L'API ne les accepte que sur les routes `GET`, et refuse tout jeton personnel (401) quand la fonctionnalité est coupée | ✅ côté API (`PersonalTokenHandler`, préfixe `sat_`, schéma choisi d'après l'en-tête ; routes `/api/me/tokens` et `/api/admin/tokens`, 404 coupées) / 🔜 page Préférences (6a-2) | 3 |
| MCP-04 | **Identité relue à l'usage** : à la première requête d'un jeton puis au moins toutes les 5 minutes, l'API relit dans l'annuaire les groupes de rôle du compte ; un compte absent des groupes de rôle ou désactivé est refusé (401). Rôle et périmètres sont ceux du moment, jamais ceux de la création du jeton. Annuaire injoignable : 503, comme à la connexion | ✅ | 3 |
| MCP-05 | **Outils**, tous en lecture : `search` (recherche libre, [RAG-05](recherche.md), 10 résultats) ; `get_record` (une fiche par sa référence : la vue que l'interface en montre à l'utilisateur, liens et commentaires compris) ; `similar_cases` ([RAG-06](recherche.md)) ; `my_work` (« Mon travail » de la console) ; `ci_impact` (pour un CI : ce qui en dépend par les relations, incidents et changements ouverts reliés) ; `triage_suggestions` (propositions du [tri assisté](tri-assiste-des-tickets.md) sur un incident, s'il est activé). Aucun outil d'écriture n'est déclaré au client | 🔜 | 3 |
| MCP-06 | **Périmètres** : chaque outil passe par la portée de l'API ([PER-09](perimetres.md), [RAG-13](recherche.md)) ; un enregistrement hors de portée est « introuvable », jamais « interdit » | 🔜 | 3 |
| MCP-07 | **Contenu marqué comme donnée** : titres, descriptions et commentaires sont rendus dans des champs marqués « saisi par un utilisateur » ; les consignes du serveur annoncent au client que ces champs sont des données, pas des consignes. La lecture seule (MCP-05) reste la protection | 🔜 | 3 |
| MCP-08 | **Limites** : 60 appels par minute et par jeton (429 au-delà), 20 résultats par appel au plus, champs texte tronqués à 2 000 caractères (`Mcp:*`) | 🔜 | 3 |
| MCP-09 | **Traçabilité** : chaque appel d'outil est écrit au journal technique (JSON, [ADR-0017](../decisions/adr-0017-observabilite-journal-json-et-opentelemetry.md)) : compte, identifiant du jeton, outil, nombre de résultats, durée, issue ; jamais le contenu ni le jeton. La **dernière utilisation** du jeton est tenue à jour (une fois par minute au plus) et affichée à l'utilisateur et à l'Admin | 🔜 (côté API ✅ : dernière utilisation à la minute, journal « compte, identifiant du jeton, méthode, chemin » ; journal des outils : conteneur, 6b ; affichage : 6a-2) | 3 |

## 3. Interface

| Endroit | Contenu | Statut |
|---|---|---|
| Préférences › **Jetons d'accès** (tous les rôles, si la fonctionnalité est activée) | Créer (nom, durée), voir la liste (nom, création, expiration, dernière utilisation), révoquer ; adresse du serveur à copier | 🔜 |
| Administration › Fonctionnalités optionnelles › Serveur MCP | État du conteneur, tous les jetons actifs (compte, nom, expiration, dernière utilisation ; jamais le jeton), révocation | 🔜 |

## 4. API

| Route | Policy | Rôle |
|---|---|---|
| `POST /mcp`, `GET /mcp`, `DELETE /mcp` (conteneur `mcp`) | Jeton personnel | Protocole MCP (MCP-01, MCP-05) |
| `GET /api/me/tokens`, `POST /api/me/tokens`, `DELETE /api/me/tokens/{id}` | User | Jetons personnels (MCP-03) |
| `GET /api/admin/tokens`, `DELETE /api/admin/tokens/{id}` | Admin | Liste et révocation (MCP-03) |
| `GET /health` (conteneur) | — (réseau du compose) | Santé du conteneur ([OPT-01](fonctionnalites-optionnelles.md#2-activation)) |

## 5. Scénarios d'acceptation

- **MCP-03** — *Étant donné* un jeton personnel, *quand* l'assistant appelle `PUT /api/incidents/12` avec lui, *alors* l'API refuse (401 ou 405) ; révoqué, le jeton est refusé à l'appel suivant.
- **MCP-04** — *Étant donné* un utilisateur retiré de son groupe de rôle après la création de son jeton, *quand* l'assistant appelle un outil plus de 5 minutes après, *alors* l'appel est refusé (401).
- **MCP-05** — *Étant donné* le serveur activé, *quand* le client demande la liste des outils, *alors* elle ne contient que les six outils de lecture.
- **MCP-06** — *Étant donné* un utilisateur du seul périmètre Usine, *quand* il demande `get_record` d'un incident du Siège, *alors* l'outil répond « introuvable ».
- **MCP-01** — *Étant donné* la fonctionnalité coupée, *quand* un client appelle `/mcp`, *alors* il reçoit 404, et ses jetons sont refusés par l'API.

## 6. Hors de cette version

- **Écriture**, même proposée et confirmée : nouvelle décision ([ADR-0024](../decisions/adr-0024-serveur-mcp-conteneur-separe-lecture-seule.md)).
- **OAuth 2.1** et clients MCP distants hors du réseau de l'organisation.
- **Serveur local par utilisateur** (transport stdio).
