# ADR-0026 — Agent d'inventaire nomade : exécutable .NET autonome, rapport HTTPS sortant vers le sas, enrôlement par jeton de périmètre

<sub>[← Documentation](../readme.md) · [Toutes les décisions](readme.md)</sub>

- **Date** : 2026-11-24
- **Statut** : proposé
- **Décideur** : Leipa

## Contexte

La découverte de S-Aloha est **sans agent** : l'API va chercher l'information par
l'annuaire, le balayage IP, SNMP, SSH, WinRM et les hyperviseurs
([gestion de la configuration § 4.2](../reference/processus/gestion-de-la-configuration.md#42-découverte-et-sas-de-staging)).
Elle ne voit donc que ce qui est joignable depuis le réseau où tourne l'API. Les **postes
nomades** — portables en télétravail, en déplacement, sur un site sans lien permanent —
échappent à l'inventaire, alors que ce sont souvent les plus exposés. La phase 4 du chantier
F13 ([plan d'action](../plan-action.md#découverte-automatique-et-cmdb-augmentée-f13)) prévoit
pour eux un **agent léger** qui envoie son inventaire, et laisse à décider sa forme, son
langage, la signature du binaire, l'enrôlement et la fréquence d'envoi.

Contraintes :

- rien ne doit entrer dans la CMDB sans passer par le sas de staging (CFG-40, CFG-41) ;
- un poste nomade n'est pas joignable : c'est lui qui appelle, quand il a du réseau ;
- un secret qui circule sur des centaines de postes finira par fuiter : il ne doit ouvrir
  que le dépôt d'un rapport, dans un seul périmètre ;
- le projet n'a ni budget ni certificat de signature de code ; il a déjà une CI qui publie
  des images.

## Décision

1. **Un exécutable .NET autonome** (`agent/SAloha.Agent`, publié en fichier unique, sans
   runtime à installer) pour Windows x64 et Linux x64, installé comme **service** (service
   Windows, unité systemd) sous un compte sans privilège. Même langage que l'API : la
   lecture de l'inventaire reprend la logique des collecteurs SSH et WinRM, les contributeurs
   n'ont pas d'autre chaîne d'outils à apprendre.
2. **Il collecte l'inventaire du poste et rien d'autre** : celui que donnent déjà SSH et
   WinRM (nom, système, constructeur, modèle, numéro de série, interfaces, logiciels
   installés, services). Aucune donnée d'utilisateur : ni compte connecté, ni fichiers, ni
   navigation.
3. **Il appelle, il n'écoute pas** : un rapport en HTTPS sortant vers l'API, au démarrage du
   service puis toutes les 24 heures par défaut, avec un aléa ; le rapport arrive dans le sas
   comme un actif découvert de source « Agent » et suit la même réconciliation.
4. **Enrôlement en deux temps** : un gestionnaire crée un **jeton d'enrôlement** pour un
   périmètre (durée, nombre d'usages, révocable) ; l'agent l'échange une fois contre une
   **identité d'agent** propre (identifiant et secret), qui ne sert qu'à déposer ses
   rapports et se révoque seule. Le jeton d'enrôlement ne dépose jamais de rapport.
5. **Distribution sans certificat payant** : les binaires sont publiés avec chaque version,
   avec leurs sommes SHA-256 et une **attestation de provenance** de la CI (preuve vérifiable
   qu'ils sortent de ce dépôt et de ce commit). Une organisation qui veut une signature
   Authenticode signe avec son propre certificat avant de déployer par ses outils (GPO,
   Intune, gestionnaire de paquets). Pas de mise à jour automatique : l'organisation déploie
   les versions ; l'API signale les agents en retard.

La spécification est dans [gestion de la configuration](../reference/processus/gestion-de-la-configuration.md#5-règles-de-gestion)
(CFG-58 à CFG-64) ; les preuves NIS 2 nourries par la découverte dans
[conformité NIS 2](../reference/processus/conformite-nis2.md#5-règles-de-gestion) (NIS-25).

## Conséquences

- Les postes nomades entrent au sas comme les autres actifs ; le Shadow IT et
  l'obsolescence (CFG-42, CFG-47) les couvrent.
- Pour recevoir les rapports hors du réseau interne, l'exploitant publie **les seules
  routes de l'agent** (`/api/agent/…` : enrôlement et rapport, distinctes des routes de gestion) sur son point d'entrée exposé, ou impose
  le VPN : la documentation d'exploitation donne les deux montages.
- Un projet .NET, ses tests et la publication de binaires s'ajoutent à la CI : changement
  d'outillage (étiquette `outillage`, posée par le mainteneur).
- Sans signature Authenticode, Windows SmartScreen avertit à l'exécution manuelle du
  binaire ; un déploiement par les outils de l'organisation n'est pas concerné.

## Alternatives envisagées

- **Agent en Go** — binaire plus petit, mais un second langage dans le dépôt pour un seul
  composant ; écarté.
- **Agents existants** (osquery, agent GLPI) dont l'API lirait les rapports — écarté : une
  dépendance de plus à installer et à suivre, et des formats qui ne sont pas ceux du sas.
- **Jeton de périmètre utilisé pour chaque rapport** — écarté : une fuite exposerait tout le
  périmètre jusqu'à la révocation, qui couperait tous les agents à la fois.
- **Certificat de signature de code** — reporté : coût annuel, et les organisations visées
  déploient par leurs propres outils, qui peuvent signer.
- **L'API qui interroge les postes par le VPN** — c'est déjà possible par WinRM et SSH quand
  le poste est connecté ; l'agent couvre précisément le cas où il ne l'est pas.

## Points soumis au mainteneur

Proposés ici, à confirmer avec la spécification : langage .NET (point 1) ; aucune donnée
d'utilisateur (point 2) ; 24 heures par défaut, réglables de 1 à 168 par périmètre
(point 3) ; exposition des seules routes de l'agent ou VPN, au choix de l'exploitant
(conséquences) ; attestation de provenance plutôt qu'un certificat payant, et pas de mise à
jour automatique (point 5) ; plateformes Windows x64 et Linux x64 d'abord (macOS et ARM
plus tard).
