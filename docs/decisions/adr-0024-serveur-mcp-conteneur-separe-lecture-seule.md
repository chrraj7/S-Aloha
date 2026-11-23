# ADR-0024 — Serveur MCP : conteneur séparé, lecture seule, jetons personnels

<sub>[← Documentation](../readme.md) · [Toutes les décisions](readme.md)</sub>

- **Date** : 2026-11-23
- **Statut** : accepté
- **Décideur** : Leipa

## Contexte

Le *Model Context Protocol* (MCP) permet à un assistant (le client MCP) d'appeler des
outils et de lire des ressources qu'un serveur lui expose. Un serveur MCP de S-Aloha
laisserait un technicien interroger ses tickets, la connaissance ou l'impact d'un CI
depuis l'assistant qu'il emploie déjà, sans copier-coller.

L'[ADR-0022](adr-0022-pgvector-similarite-dans-postgresql.md) le prévoyait après la
v1.0.0 ; décision du 2026-11-23 : il est livré **avant** la version stable, comme
fonctionnalité optionnelle ([ADR-0023](adr-0023-fonctionnalites-optionnelles-et-modele-de-langage.md)).

Trois contraintes :

- **Le texte des tickets est écrit par n'importe quel utilisateur** et lu par
  l'assistant. Une consigne glissée dans une description peut détourner l'assistant d'un
  gestionnaire : avec des outils d'écriture, un simple utilisateur agirait avec les droits
  d'un autre (injection indirecte).
- **L'identité vient de l'AD** et la session est un jeton en cookie HttpOnly
  ([ADR-0018](adr-0018-jeton-en-cookie-httponly-et-csp.md)) : un client MCP ne peut pas
  s'en servir, et S-Aloha n'a pas de serveur d'autorisation OAuth, que la spécification
  MCP prévoit pour le transport HTTP.
- **Les droits par périmètre** ([ADR-0014](adr-0014-perimetres-droits-scopes.md)) sont
  appliqués par l'API, route par route : les refaire ailleurs les ferait diverger.

## Décision

1. **Un conteneur dédié**, `mcp`, comme toute fonctionnalité optionnelle
   ([ADR-0023](adr-0023-fonctionnalites-optionnelles-et-modele-de-langage.md)) : construit
   depuis le dépôt, rangé dans un profil du compose, démarré ou non par l'exploitant.
   nginx lui transmet `/mcp`. À la différence des autres conteneurs, il agit toujours
   **pour un utilisateur** : il n'a ni jeton de conteneur, ni route interne, ni accès à la
   base, ni secret. Il appelle les routes publiques de l'API (`http://api:8080/api/…`)
   avec le jeton personnel de l'utilisateur, si bien que les périmètres, les vues en DTO
   ([ADR-0016](adr-0016-reponses-de-l-api-en-dto.md)) et la recherche sont ceux de l'API.
   Une donnée qui manque à un outil s'ajoute à l'API, jamais au conteneur.
2. **Lecture seule** : aucun outil n'écrit, même par proposition. Une écriture demandera
   une nouvelle décision.
3. **Jetons d'accès personnels** : créés par l'utilisateur, affichés une seule fois,
   conservés sous forme d'empreinte, à durée limitée et révocables par lui et par l'Admin.
   L'API les accepte sur ses seules routes de **lecture** (`GET`) ; le rôle et les
   périmètres sont relus dans l'annuaire à l'usage, pas figés à la création du jeton.
4. **Fonctionnalité optionnelle** `mcp-server` : coupée, l'API refuse les jetons
   personnels et le conteneur ne sert rien.
5. **Contenu signalé comme donnée** : les textes saisis par les utilisateurs sont rendus
   dans des champs marqués comme tels, et le serveur annonce au client qu'ils ne sont pas
   des consignes. La vraie protection reste la lecture seule.
6. **SDK** : le SDK C# officiel du protocole, qualifié à son ajout (version stable,
   licence compatible avec l'[AGPLv3](adr-0006-licence-agplv3.md)) par le contrôle des
   dépendances ajoutées.

Spécification : [serveur MCP](../reference/serveur-mcp.md).

## Conséquences

- Une image et un projet de tests de plus, avec les deux autres conteneurs de
  l'[ADR-0023](adr-0023-fonctionnalites-optionnelles-et-modele-de-langage.md) : la CI
  change (workflows), ce qui demande l'accord du mainteneur (étiquette `outillage`).
- Une table des jetons personnels et un schéma d'authentification de plus dans l'API
  (chemins sensibles) ; le serveur MCP ne porte aucune donnée à sauvegarder.
- Chaque appel d'outil fait au moins un aller-retour vers l'API : quelques millisecondes
  sur le réseau du compose, sans effet pour un assistant.
- La [sécurité](../reference/securite.md) (surface exposée) et l'[exploitation](../reference/exploitation.md)
  décriront le conteneur à sa livraison.

## Alternatives envisagées

- **Serveur MCP dans le processus de l'API** (`/mcp`) — écarté par le mainteneur : toute
  fonctionnalité optionnelle tourne dans son conteneur
  ([ADR-0023](adr-0023-fonctionnalites-optionnelles-et-modele-de-langage.md)) ; un SDK de
  plus dans le processus web serait une surface de plus dans l'API elle-même.
- **Jeton de conteneur et routes internes**, comme les autres fonctionnalités — écarté :
  le conteneur verrait alors tous les périmètres, et devrait refaire le filtrage de chaque
  utilisateur.
- **Conteneur avec accès direct à la base** — écarté : il devrait refaire les périmètres
  et les vues de l'API.
- **OAuth 2.1** — écarté pour cette version : il faudrait un serveur d'autorisation devant
  l'AD. À reconsidérer si des clients MCP distants le demandent.
- **Serveur local par utilisateur** (transport stdio, binaire installé sur le poste) —
  écarté : un binaire à distribuer et signer pour chaque système, comme l'agent nomade de
  la découverte, encore à arbitrer.
- **Outils d'écriture avec confirmation** — écartés pour cette version : l'injection
  indirecte vise justement l'utilisateur qui confirme.
- **Réutiliser le jeton de session** (`returnToken`) — écarté : non révocable un par un,
  il porte tous les droits de l'utilisateur, écriture comprise.
