# 🗂️ Pratiques ITIL — référentiel fonctionnel

> Un document par pratique ITIL 4, sur le même gabarit. Socle commun (rôles, console,
> conventions, règles `SOC`) : [règles métier](../regles-metier.md). Ce référentiel sert
> de **spécification** à qui implémente, contributeur ou agent de code : chaque règle a un
> identifiant, un statut (✅ implémentée / 🔜 à implémenter), un lot et ses scénarios
> d'acceptation.

<sub>[← Règles métier](../regles-metier.md) · [← Documentation](../../readme.md)</sub>

## Les pratiques

| Pratique | Préfixe | ✅ | 🔜 lot 1 | 🔜 lot 2 | 🔜 lot 3 |
|---|---|---|---|---|---|
| [Socle commun](../regles-metier.md) | SOC | 27 | 0 | 0 | 0 |
| [Gestion des problèmes](gestion-des-problemes.md) | PRB | 30 | 0 | 0 | 0 |
| [Gestion des incidents](gestion-des-incidents.md) | INC | 32 | 0 | 0 | 0 |
| [Gestion des demandes de service](gestion-des-demandes.md) | REQ | 22 | 0 | 0 | 0 |
| [Habilitation des changements](habilitation-des-changements.md) | CHG | 27 | 0 | 0 | 0 |
| [Gestion de la configuration](gestion-de-la-configuration.md) | CFG | 39 | 0 | 0 | 0 |
| [Gestion des niveaux de service](gestion-des-niveaux-de-service.md) | SLM | 17 | 0 | 0 | 0 |
| [Gestion des connaissances](gestion-des-connaissances.md) | KB | 16 | 0 | 0 | 0 |
| [Amélioration continue](amelioration-continue.md) | CSI | 16 | 0 | 0 | 0 |
| [Conformité NIS 2](conformite-nis2.md) | NIS | 23 | 0 | 0 | 0 |
| [Déclarations réglementaires](declarations-reglementaires.md) | DRG | 12 | 0 | 0 | 0 |
| [Gestion de projet](gestion-de-projet.md) | PRJ | 23 | 0 | 0 | 0 |
| [Périmètres](../perimetres.md) | PER | 13 | 0 | 0 | 0 |
| [Recherche](../recherche.md) | RAG | 12 | 0 | 0 | 1 |
| [Reporting](../reporting.md) | REP | 43 | 0 | 0 | 0 |
| [Fonctionnalités optionnelles](../fonctionnalites-optionnelles.md) | OPT | 16 | 0 | 0 | 9 |
| [Tri assisté des tickets](../tri-assiste-des-tickets.md) | TRI | 9 | 0 | 0 | 5 |
| [CMDB assistée](../cmdb-assistee.md) | CMA | 0 | 0 | 0 | 12 |
| [Serveur MCP](../serveur-mcp.md) | MCP | 0 | 0 | 0 | 9 |
| **Total** | | **377** | **0** | **0** | **36** |

*Comptes au 2026-11-16 (main `84c4070`), sur les tableaux de règles (hors indicateurs du § 8
des pratiques ; les règles REP du [reporting](../reporting.md) qui les affichent sont comptées).
Une règle faite en partie compte ✅ (7 règles, la part restante est décrite dans son statut).
Dernière règle de lot 1, RAG-10 (calibrage de la recherche), faite le 2026-11-09 ; reste
RAG-12 (pièces jointes, lot 3). Les 60 règles des fonctionnalités optionnelles (OPT, TRI, CMA,
MCP, chantier F14) ont été spécifiées le 2026-11-23, toutes en lot 3, à livrer avant la
version stable ; OPT-01 à OPT-08 (étape 1, #363) OPT-10 à OPT-17 (#364, parties 2a à 2c ; OPT-13 avec les lectures du tri, #365) et TRI-01 à TRI-08, TRI-11 (#365, parties 3a à 3c) sont faites le même jour.*

## Interface et navigation

La navigation de l'application (menus, sous-entrées, pages réservées aux gestionnaires,
onglets des fiches, boutons de transition) dérive de ce référentiel :
[frontend § Navigation cible](../frontend.md#navigation-cible). Une règle implémentée qui
ajoute une page ou un onglet met cette section à jour dans la même PR.

## Mode d'emploi pour implémenter une règle

1. **Lire** le [socle](../regles-metier.md) (§ 3 : conventions) puis le document de la
   pratique visée.
2. **Choisir** des règles 🔜 du lot le plus bas, de préférence une étape entière du plan
   ci-dessous.
3. **Implémenter** chaque règle avec ses **tests** : un test d'API par règle (cas nominal et
   refus), en reprenant les scénarios du § 10 du document ; un test d'interface si la règle
   est visible. Le message d'erreur d'un refus (400) dit pourquoi, en français.
4. **Mettre à jour la documentation dans la même PR** : marque 🔜 → ✅, colonne « Lot »
   vidée, tableau des transitions (§ 4) et données (§ 3) alignés sur le code ;
   [base de données](../base-de-donnees.md) si le schéma change (migration EF) ;
   [architecture](../architecture.md) si une route est ajoutée.
5. **Citer les identifiants** dans le titre de la PR et les messages de commit
   (« Implémente INC-06 et INC-08 »).
6. **Ne jamais** modifier un comportement ✅ sans avoir d'abord modifié sa règle ; une
   règle qui se révèle mauvaise s'amende dans la PR, avec sa justification.

## Plan d'implémentation conseillé — lot 1

Ordre pensé pour ne construire chaque mécanisme qu'une fois.

### Étape 1 — Mécanisme de transitions (socle)

Un tableau de transitions par pratique dans le code (`RecordController` et module
Problèmes), refus 400 explicite, transition forcée par un Admin avec motif.
**Fait le 2026-10-10**, sauf la transition forcée, reportée à l'étape 2 (elle s'appuie sur
le journal d'audit pour tracer le motif).

| Règle | Objet |
|---|---|
| [SOC-05](../regles-metier.md#5-règles-communes-aux-processus) | Mécanisme commun des transitions contraintes |
| [PRB-10](gestion-des-problemes.md), [INC-09](gestion-des-incidents.md), [REQ-08](gestion-des-demandes.md), [CHG-11](habilitation-des-changements.md), [CFG-10](gestion-de-la-configuration.md), [SLM-12](gestion-des-niveaux-de-service.md), [KB-18](gestion-des-connaissances.md), [CSI-11](amelioration-continue.md) | Tableau des transitions de chaque pratique (§ 4) |

### Étape 2 — Journal d'audit

| Règle | Objet |
|---|---|
| [SOC-20](../regles-metier.md#9-traçabilité-commentaires-administration) | Journal des créations, modifications, transitions, suppressions et liens ; onglet « Historique » |

Prérequis des motifs (réouverture, rejet, abandon) et des transitions forcées.
**Fait le 2026-10-10**, avec la transition forcée par un Admin (SOC-05).

### Étape 3 — Validations et champs obligatoires

**Fait le 2026-10-10.**

| Règle | Objet |
|---|---|
| [SOC-02](../regles-metier.md#5-règles-communes-aux-processus), [PRB-02](gestion-des-problemes.md) | Valeurs fermées dans le module Problèmes ([#28](https://github.com/chrraj7/S-Aloha/issues/28)) |
| [PRB-17](gestion-des-problemes.md), [PRB-18](gestion-des-problemes.md) | RACI à la modification ([#23](https://github.com/chrraj7/S-Aloha/issues/23)), date d'achèvement ([#24](https://github.com/chrraj7/S-Aloha/issues/24)) |
| [PRB-11](gestion-des-problemes.md), [PRB-12](gestion-des-problemes.md), [PRB-13](gestion-des-problemes.md), [PRB-14](gestion-des-problemes.md) | Erreur connue, résolution, code de clôture, `ResolvedAt` |
| [INC-06](gestion-des-incidents.md), [INC-08](gestion-des-incidents.md) | Prise en charge, code de résolution |
| [CHG-12](habilitation-des-changements.md), [CHG-13](habilitation-des-changements.md) | Plans exigés à l'évaluation, fin planifiée |
| [CFG-11](gestion-de-la-configuration.md), [CFG-17](gestion-de-la-configuration.md) | Propriétaire obligatoire, unicité du nom |
| [SLM-03](gestion-des-niveaux-de-service.md), [SLM-11](gestion-des-niveaux-de-service.md) | SLA en vigueur cohérent, propriétaire de service |
| [CSI-14](amelioration-continue.md), [CSI-16](amelioration-continue.md) | Validation mesurable, étape 6 avant réalisation |
| [REQ-06](gestion-des-demandes.md), [CHG-17](habilitation-des-changements.md), [CSI-13](amelioration-continue.md) | Motif obligatoire au rejet et à l'abandon |

### Étape 4 — Droits et statuts

**Fait le 2026-10-11.**

| Règle | Objet |
|---|---|
| [INC-11](gestion-des-incidents.md) | Réouverture encadrée, compteur |
| [INC-13](gestion-des-incidents.md) | Incident majeur déclaré par un Manager |
| [REQ-09](gestion-des-demandes.md) | Statut Annulée, annulation par le demandeur |
| [CFG-12](gestion-de-la-configuration.md), [CFG-13](gestion-de-la-configuration.md), [CFG-14](gestion-de-la-configuration.md) | Retrait d'un CI : Manager, sans enregistrement ouvert ; CI retiré non reliable |
| [PRB-24](gestion-des-problemes.md) | Modifier une action : R, A ou Manager — résout M7 |

### Étape 5 — Groupes AD

**Fait le 2026-10-12.**

| Règle | Objet |
|---|---|
| [SOC-10](../regles-metier.md#5-règles-communes-aux-processus), [PRB-26](gestion-des-problemes.md) | Groupes de l'utilisateur dans le jeton ; « Mon travail » et actions des groupes |

### Étape 6 — Périmètres

Droits scopés par périmètre (direction, site, entité) : affectations d'utilisateurs et de
groupes AD, périmètre porté par chaque enregistrement, décisions de gestionnaire limitées au
périmètre, entrée Administration › Périmètres. Suppose l'étape 5 (groupes dans le jeton).

| Règle | Objet |
|---|---|
| [PER-01 à PER-12](../perimetres.md) | Modèle, administration, droits et visibilité par périmètre, reprise des données |

Décisions D1 à D4 acceptées le 2026-10-10 : [ADR-0014](../../decisions/adr-0014-perimetres-droits-scopes.md).
Livraison en plusieurs PR : **modèle, reprise et API d'administration** (fait le 2026-10-12 :
PER-01, PER-02, PER-10 à PER-12), **droits et visibilité des enregistrements** (PER-03 à
PER-07, 2026-10-12), **liens, console, reporting et recherche** (PER-08, PER-09,
2026-10-13) et **écran d'administration** (2026-10-13). **Étape 6 terminée le 2026-10-13.**

### Et après le lot 1

Étapes 4 à 6 terminées (2026-10-11 à 2026-10-13), NIS-18 aussi (2026-10-13) ; le lot 1 est
terminé ; [RAG-10](../recherche.md) (calibrage de la recherche) l'a été le 2026-11-09, plancher
0,47. Le cœur du lot 2 a suivi :
toutes les pratiques ont quitté le stade MVP le 2026-10-16
([plan d'action](../../plan-action.md#sortie-du-mvp)). Le **lot 2 est terminé** le 2026-10-21,
[déclarations réglementaires](declarations-reglementaires.md) comprises ; restent les règles 🔜
du lot 3 du tableau ci-dessus.

### Hors étapes

| Règle | Objet |
|---|---|
| [KB-10](gestion-des-connaissances.md) | Rendu Markdown des articles ([#25](https://github.com/chrraj7/S-Aloha/issues/25)) |

## Gabarit d'un document de pratique

Un nouveau document (nouvelle pratique) reprend ce plan, dans cet ordre :

```markdown
# <icône> <Nom de la pratique>

> Pratique ITIL 4 *<nom anglais>*. Code : `backend/Modules/<Module>`,
> `frontend/src/modules/<module>`. Préfixe des règles : **XXX**. Socle et conventions :
> [règles métier](../regles-metier.md).

## 1. Objectif et périmètre      objectif ITIL 4, définition, hors périmètre
## 2. Rôles et droits            rôles ITIL ↔ rôles applicatifs ; matrice action × rôle, avec statut
## 3. Données                    champ | type | obligatoire | valeurs / règle | statut
## 4. Cycle de vie               diagramme Mermaid ; tableau De → Vers | Qui | Conditions | Effets | Statut
## 5. Règles de gestion          ID | Règle | Statut | Lot
## 6. Délais, calculs et alertes ID | Règle | Statut | Lot (notifications comprises)
## 7. Liens avec les autres pratiques
## 8. Console et indicateurs     blocs de console ; indicateur | calcul | statut
## 9. API                        route | policy | rôle
## 10. Scénarios d'acceptation   Étant donné / quand / alors, un par règle clé, cite l'ID
```
