# ADR-0023 — Fonctionnalités optionnelles : un conteneur dédié par fonctionnalité, activé par l'Admin ; modèle de langage par un connecteur unique

<sub>[← Documentation](../readme.md) · [Toutes les décisions](readme.md)</sub>

- **Date** : 2026-11-23
- **Statut** : accepté — revient sur le principe de l'[ADR-0013](adr-0013-recherche-hybride-service-embeddings-separe.md) qui écartait l'envoi de données hors du SI (alternative « service hébergé chez un tiers »), pour le seul modèle de langage
- **Décideur** : Leipa

## Contexte

Trois fonctionnalités doivent rejoindre S-Aloha avant la première version stable
(décision du 2026-11-23) : le **tri assisté des tickets**, la **CMDB assistée** et le
**serveur MCP**. Elles ne servent pas toutes les organisations. Certaines demandent des
ressources (modèle de langage, service d'embeddings) ou ouvrent une surface nouvelle :
chacune doit pouvoir être activée ou coupée par l'Admin, dans la console, sans
réinstaller. Le mainteneur a fixé un principe : **une fonctionnalité optionnelle est
hébergée et exécutée dans un conteneur dédié, séparé des fonctions de base**. Une panne,
une charge ou une faille d'une fonctionnalité optionnelle ne doit pas atteindre l'API.

Aujourd'hui, un composant facultatif se règle de deux façons : par la configuration de
l'API (`Embeddings:Url`, [ADR-0013](adr-0013-recherche-hybride-service-embeddings-separe.md)),
ou dans la console (SMTP et stockage, [ADR-0020](adr-0020-pieces-jointes-s3-et-smtp-configurables.md)).
Rien ne dit à l'interface qu'une capacité est absente, et rien n'empêche l'API de servir
une route d'une capacité coupée.

Les premiers brouillons envisageaient des greffons chargés à chaud dans l'API et un
connecteur par fournisseur de modèle. Les pratiques ITIL restent un monolithe modulaire
compilé ([ADR-0003](adr-0003-plateforme-modulaire-par-processus-itil.md)), et cette
décision n'y change rien : les fonctionnalités optionnelles ne sont pas des pratiques.
Ce sont des **satellites** de l'API.

L'[ADR-0013](adr-0013-recherche-hybride-service-embeddings-separe.md) a écarté un service
hébergé chez un tiers, car les données des incidents et des problèmes quitteraient le SI.
Une organisation sans serveur de calcul adapté ne pourrait alors pas utiliser la CMDB
assistée ; d'autres ont déjà retenu un fournisseur de modèle hébergé et l'ont inscrit à
leur registre des traitements. Le mainteneur a tranché le 2026-11-23 : le modèle hébergé
est **permis**, et c'est à l'Admin de choisir, selon la politique de son organisation.

## Décision

1. **Un conteneur par fonctionnalité optionnelle** : `ticket-triage`, `assisted-cmdb` et
   `mcp`, chacun construit depuis le dépôt (projets .NET à part,
   [ADR-0004](adr-0004-conserver-le-stack-dotnet-react.md)) et rangé dans un profil du
   compose. L'exploitant démarre ceux qu'il veut rendre disponibles. Aucun ne touche à la
   base : un conteneur ne parle qu'à l'API, et aux services dont il a besoin (service
   d'embeddings, modèle de langage).
2. **L'API garde le socle, et lui seul** : la liste des fonctionnalités et leur état,
   l'activation par l'Admin, le stockage des propositions et leur décision, le journal
   d'audit. Aucun code propre à une fonctionnalité optionnelle ne s'exécute dans l'API.
3. **Un contrat unique entre l'API et les conteneurs** :
   - des **routes internes** sur un port de l'API que nginx ne relaie pas, chaque appel
     authentifié par le jeton propre au conteneur, qui n'ouvre que ce dont sa
     fonctionnalité a besoin ;
   - un **fil d'événements** (incident créé, CI modifié, tâche de découverte terminée…)
     que les conteneurs lisent à leur rythme avec un curseur : un conteneur arrêté
     rattrape son retard, l'API n'attend jamais ;
   - des **lectures** qui rendent les mêmes vues que l'API publique
     ([ADR-0016](adr-0016-reponses-de-l-api-en-dto.md)), dans les périmètres de
     l'utilisateur quand l'appel est fait pour lui ([ADR-0014](adr-0014-perimetres-droits-scopes.md)) ;
   - des **propositions** : seule écriture permise à un conteneur ;
   - de rares appels de l'API vers un conteneur (suggestions à la saisie), bornés par un
     délai court et jamais bloquants.

   Le serveur MCP fait exception : il agit pour un utilisateur, avec le jeton personnel de
   celui-ci, sur les routes publiques de lecture
   ([ADR-0024](adr-0024-serveur-mcp-conteneur-separe-lecture-seule.md)).
4. **L'Admin active ou coupe**, dans Administration › Fonctionnalités optionnelles. Une
   fonctionnalité ne s'active que si son conteneur répond et reconnaît le jeton. Chaque
   changement est tracé au journal d'audit.
5. **Coupée, une fonctionnalité est absente** : l'API répond 404 sur ses routes, refuse
   le jeton de son conteneur et ne lui sert plus d'événements ; l'interface ne l'affiche
   pas (elle lit la liste des fonctionnalités actives, `GET /api/features`). Les
   propositions déjà produites restent en base et reparaissent à la réactivation.
6. **Une proposition n'est jamais appliquée seule** : elle est décidée par un utilisateur
   qui a le droit de modifier l'enregistrement visé. L'application passe par le chemin
   normal de la pratique : toutes ses règles s'appliquent. Le journal d'audit garde
   l'**origine** de chaque modification (saisie, tri assisté des tickets, CMDB assistée).
7. **Un seul connecteur de modèle de langage**, bibliothèque partagée par les conteneurs
   qui en ont besoin : le format de complétion de conversation `POST …/v1/chat/completions`,
   que parlent les serveurs d'inférence courants (Ollama, vLLM, LocalAI, llama.cpp) et la
   plupart des offres hébergées. Adresse, modèle et clé (chiffrée par le coffre) se règlent
   dans la console, comme le SMTP ([ADR-0020](adr-0020-pieces-jointes-s3-et-smtp-configurables.md)) ;
   le conteneur les reçoit par une route interne. La réponse est validée contre un schéma
   JSON ; une réponse non conforme est écartée.
8. **Modèle dans le SI ou hébergé : au choix de l'Admin**. L'Admin déclare l'emplacement
   du modèle. Une adresse qui se résout hors des plages privées n'est acceptée que si
   l'Admin a déclaré le modèle « hébergé hors du SI » et confirmé ce choix. La console
   l'affiche alors en permanence, et le journal d'audit trace la déclaration. Dans tous
   les cas, seules les données utiles à la tâche partent, **pseudonymisées** (adresses,
   noms d'hôte, numéros de série, comptes remplacés par des jetons stables et rétablis au
   retour) ; aucun secret du coffre n'est jamais envoyé.
9. **Le service d'embeddings ne change pas** : il reste local
   ([ADR-0013](adr-0013-recherche-hybride-service-embeddings-separe.md)), et l'index de la
   recherche ne contient toujours que du contenu validé. Le tri assisté des tickets tient
   son propre index des incidents ouverts, dans son conteneur.

Spécification : [fonctionnalités optionnelles](../reference/fonctionnalites-optionnelles.md),
[tri assisté des tickets](../reference/tri-assiste-des-tickets.md),
[CMDB assistée](../reference/cmdb-assistee.md).

## Conséquences

- Trois images de plus à construire et publier, et leurs projets de tests : la CI change
  (workflows), ce qui demande l'accord du mainteneur (étiquette `outillage`).
- Dans l'API : la liste des fonctionnalités, la table des propositions, le fil
  d'événements, une colonne `Origin` au journal d'audit (migration ; les lignes
  existantes valent « Saisie »), les réglages du modèle de langage, un second port
  d'écoute pour les routes internes et un schéma d'authentification des conteneurs
  (chemins sensibles).
- Un secret par conteneur, fourni par l'environnement à l'API et au conteneur ; une
  valeur d'exemple est refusée hors du mode démonstration, comme les autres secrets.
- Chaque fonctionnalité se teste **activée et coupée** (404 quand elle est coupée).
  Les conteneurs se testent contre une API simulée, et le connecteur contre un faux
  serveur de modèle : aucun modèle réel dans la CI.
- Une organisation qui choisit un modèle hébergé l'inscrit à son registre des
  traitements (RGPD) ; la console le lui rappelle. S-Aloha ne promet pas plus que la
  pseudonymisation et la minimisation qu'il applique.
- La documentation ne nomme aucune offre hébergée ; les exemples utilisent des serveurs
  locaux.
- Le chantier F14 du [plan d'action](../plan-action.md) précède la version stable, qui
  attend sa livraison.

## Alternatives envisagées

- **Fonctionnalités dans le processus de l'API**, activées par un interrupteur — écarté
  par le mainteneur : une panne ou une charge d'une fonctionnalité optionnelle
  atteindrait les fonctions de base.
- **Un seul conteneur pour toutes les fonctionnalités optionnelles** — écarté : on ne
  pourrait pas en démarrer une sans les autres, et la panne de l'une arrêterait toutes
  les autres.
- **Greffons chargés à chaud** (assemblies déposées dans un dossier) — écarté : exécution
  de code tiers dans le processus de l'API, contraire à l'[ADR-0003](adr-0003-plateforme-modulaire-par-processus-itil.md).
- **Conteneurs avec accès direct à la base** — écarté : ils devraient refaire les
  périmètres, les vues et les règles de l'API, qui divergeraient.
- **Événements poussés par l'API** (appel du conteneur à chaque enregistrement) —
  écarté : un conteneur arrêté perdrait des événements, et l'API attendrait ses réponses.
- **Un connecteur par fournisseur** — écarté : autant de clients à maintenir, alors qu'un
  format commun couvre les serveurs locaux et la plupart des offres.
- **Plafond fixé par l'exploitant dans la configuration** (interdire un modèle hébergé
  quel que soit le choix de l'Admin) — écarté par le mainteneur : le choix appartient à
  l'organisation, qui l'exprime par son Admin.
- **Modèle hébergé interdit sans exception** (ADR-0013 tel quel) — écarté : il fermerait
  la CMDB assistée aux organisations sans serveur de calcul.
