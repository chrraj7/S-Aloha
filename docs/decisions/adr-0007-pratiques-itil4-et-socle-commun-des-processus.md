# ADR-0007 — Pratiques ITIL comme référentiel, socle commun des processus et liens dans le socle

<sub>[← Documentation](../readme.md) · [Toutes les décisions](readme.md)</sub>

- **Date** : 2026-10-06
- **Statut** : accepté
- **Décideur** : Leipa
- **Révision** : 2026-11-27, numéros d'édition d'ITIL retirés à la demande du décideur :
  S-Aloha suit les pratiques publiées, sans se lier à une édition

## Contexte

Sept des huit processus du registre étaient « Bientôt » : incidents, demandes,
changements, configuration, niveaux de service, connaissances, amélioration continue.
L'objectif est de les livrer en **MVP** (enregistrer, suivre un cycle de vie, porter les
décisions des gestionnaires, relier les processus), l'enrichissement venant ensuite.

Trois questions se posaient :

1. **Quel référentiel ?** Le module Problèmes a été construit sur une ancienne édition
   d'ITIL, organisée en processus du cycle de vie des services. Le site officiel
   (PeopleCert, propriétaire d'Axelos) publie les **pratiques ITIL**, avec un guide par
   pratique, et introduit progressivement une nouvelle édition, dont le contenu public est
   encore partiel.
2. **Comment livrer sept modules sans dupliquer sept fois le même code** (référence
   annuelle, contrôle des statuts, responsable AD, recherche, droits) ?
3. **Comment relier les processus** (incident → problème, changement → CI) alors qu'un
   module ne doit pas appeler un autre module ([ADR-0003](adr-0003-plateforme-modulaire-par-processus-itil.md)) ?

## Décision

1. **Référentiel : les pratiques ITIL.** Chaque module reprend l'objectif officiel de la
   pratique correspondante (gestion des incidents, gestion des demandes de service,
   habilitation des changements, gestion de la configuration des services, gestion des
   niveaux de service, gestion des connaissances, amélioration continue) et son
   vocabulaire (changement standard pré-autorisé, calendrier des changements, CI,
   registre et modèle d'amélioration continue en sept étapes). La nouvelle édition conserve
   ces pratiques : l'alignement reste valable, et la documentation ne cite aucun numéro
   d'édition.
2. **Socle commun des processus** dans `backend/Core/Records` : une entité de base `Record`
   (non mappée, une table par processus) et un contrôleur générique
   `RecordController<T, TDto>`. Chaque module ne déclare que son préfixe de référence, ses
   statuts, ses champs et ses règles (statuts réservés aux gestionnaires, pré-conditions,
   horodatages). La génération de référence devient `Core/Data/References`, utilisée aussi
   par les problèmes ; la matrice de priorité devient `Core/Itil/Priority`. Côté interface,
   les composants `RecordList` / `RecordDetail` / `RecordForm` sont pilotés par un
   `config.jsx` par module.
3. **Liens dans le socle** : une table `ItemLinks` (type et identifiant de chaque
   extrémité) et un registre `ItemLinks.Kinds` dans `Core/Links`. Le socle connaît les
   types reliables, comme `Core/Pilotage` connaît les données des modules ; les modules
   ne se connaissent toujours pas.

Le module Problèmes garde son code propre (analyses RCA, actions RACI) : il n'est pas
migré sur `Record`.

## Conséquences

- Un nouveau processus coûte une entité, un DTO, un contrôleur de quelques dizaines de
  lignes et un fichier de configuration côté interface.
- Les nouveaux modules valident leurs valeurs fermées dès le départ (400 avec les valeurs
  admises) et effacent les dates de transition à la réouverture : les défauts R1 et M4 du
  module Problèmes ne s'y reproduisent pas.
- `Core/Links` devient la troisième exception à la règle « le socle ne connaît pas les
  modules », documentée dans [`architecture.md`](../reference/architecture.md#3-organisation-du-code--socle-et-modules).
- Les liens n'ont pas de clé étrangère : leur intégrité est tenue par l'API (suppression
  avec l'enregistrement, lien orphelin ignoré à la lecture).
- Onze tables nouvelles : `EnsureCreated` ne les crée pas sur une base existante. La
  procédure est dans [`exploitation.md`](../reference/exploitation.md#évolutions-de-schéma) ;
  les migrations EF (S4) deviennent prioritaires.
- Le comportement propre à un processus qui ne rentre pas dans les points d'extension du
  contrôleur générique devra être ajouté au module (comme le calendrier des changements
  ou les relations entre CI), ou faire évoluer le socle.

## Alternatives envisagées

- **Rester sur l'ancienne édition d'ITIL** — écarté : ce n'est plus le référentiel publié ;
  les pratiques apportent notamment l'habilitation des changements (changements standard
  pré-autorisés) et le modèle d'amélioration continue.
- **Viser d'emblée la nouvelle édition** — écarté pour le MVP : publication encore
  progressive ; les pratiques visées sont communes aux deux éditions.
- **Un contrôleur et une page écrits à la main par module** — écarté : sept fois les mêmes
  règles (référence, statuts, droits, recherche), avec le risque d'y reproduire les défauts
  relevés sur le module Problèmes.
- **Clés étrangères directes entre modules** (`Incident.ProblemId`…) — écarté : couple les
  modules entre eux et ne couvre qu'un lien prévu à l'avance ; la table de liens couvre
  toute paire de processus.
