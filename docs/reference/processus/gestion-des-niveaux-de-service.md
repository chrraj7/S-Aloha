# 📏 Gestion des niveaux de service

> Pratique ITIL 4 *Service level management*. Code :
> `backend/Modules/ServiceLevelManagement`, `frontend/src/modules/service-level-management`.
> Préfixe des règles : **SLM**. Socle commun et conventions (✅ / 🔜, lots) :
> [règles métier](../regles-metier.md).

<sub>[← Règles métier](../regles-metier.md) · [Toutes les pratiques](readme.md)</sub>

## 1. Objectif et périmètre

*Objectif ITIL 4 : fixer des cibles claires, orientées métier, pour les niveaux de
service, et s'assurer que la prestation est correctement évaluée, suivie et gérée au
regard de ces cibles.*

La pratique tient le **catalogue des services** (ce que la DSI fournit) et les **accords de
niveau de service** (SLA : ce qu'elle s'engage à fournir, à qui), puis **mesure** la
prestation réelle. Hors périmètre : les contrats fournisseurs (gestion des fournisseurs),
la facturation des services.

## 2. Rôles et droits

| Rôle ITIL | Rôle applicatif | Responsabilités |
|---|---|---|
| Gestionnaire des niveaux de service | Manager | Négocie et tient les SLA, conduit les revues |
| Propriétaire de service | Responsable AD du service | Garant du service et de ses résultats |
| Client | Texte (direction, entité) | Partie de l'accord |
| Consultation | User | Lit le catalogue et les SLA |

| Action | User | Manager | Admin | Statut |
|---|---|---|---|---|
| Consulter le catalogue et les SLA | ✔ | ✔ | ✔ | ✅ |
| Créer, modifier un service ou un SLA | ✘ | ✔ | ✔ | ✅ |
| Enregistrer une revue de SLA | ✘ | ✔ | ✔ | ✅ SLM-22 |
| Supprimer | ✘ | ✘ | ✔ | ✅ |

## 3. Données

Champs communs (référence, titre, description, statut, responsable AD, créateur, dates) :
[socle](../regles-metier.md#5-règles-communes-aux-processus).

### Service (`Services`, `SVC-AAAA-NNNN`)

| Champ | Type | Oblig. | Valeurs / règle | Statut |
|---|---|---|---|---|
| `Status` | texte 30 | ✔ | En conception, En service, Retiré ; choisi à la création | ✅ |
| `Criticality` | texte 10 | ✔ | Faible, Moyenne, Élevée | ✅ |
| `ServiceHours` | texte 150 | | Libellé des heures de service (« Lun–Ven 8 h–18 h ») | ✅ |
| `Owner…` | AD | ✔ (🔜) | Propriétaire de service | ✅ |
| `WorkingDays`, `OpeningTime`, `ClosingTime`, `Holidays`, `TimeZone` | textes | | Heures de service structurées (SLM-14) : jours « 1,2,3,4,5 » (1 = lundi), plage « HH:mm » (ouverture avant fermeture, les deux ou aucune), jours fériés « AAAA-MM-JJ », fuseau IANA (UTC par défaut) ; tout vide : 24 h/24, 7 j/7 | ✅ |

### Accord de niveau de service (`Agreements`, `SLA-AAAA-NNNN`)

| Champ | Type | Oblig. | Valeurs / règle | Statut |
|---|---|---|---|---|
| `ServiceId` | lien | ✔ | Service couvert | ✅ |
| `Customer` | texte 200 | | Client de l'accord | ✅ |
| `Status` | texte 30 | auto | Brouillon, En vigueur, Expiré | ✅ |
| `AvailabilityTarget` | décimal | | Disponibilité cible en % (ex. 99,5) | ✅ |
| `ComplianceTarget` | décimal | | Cible de respect : part des incidents résolus dans le délai, en % (ex. 95) ; sans cible, le respect est mesuré sans alerte (SLM-20, SLM-23) | ✅ |
| `ResolutionHoursP1` … `P4` | entier | | Délai de résolution par priorité, en heures de service | ✅ |
| `ResponseHoursP1` … `P4` | entier | | Délai de prise en charge par priorité ; facultatifs, complets et croissants s'ils sont donnés, chacun au plus égal au délai de résolution de sa priorité | ✅ SLM-13 |
| Revues (`AgreementReviews`) | liste | | Date, auteur, constats, décisions, prochaine revue (SLM-22) | ✅ |
| `ValidFrom`, `ValidTo`, `ReviewDate` | date | | Validité, prochaine revue | ✅ |

## 4. Cycle de vie

```mermaid
stateDiagram-v2
    direction LR
    state "Service" as S {
        [*] --> EnConception
        EnConception --> EnService
        EnService --> Retire
        EnConception : En conception
        EnService : En service
        Retire : Retiré
    }
    state "SLA" as A {
        [*] --> Brouillon
        Brouillon --> EnVigueur : cibles complètes
        EnVigueur --> Expire : fin de validité
        EnVigueur --> Brouillon : renégociation
        EnVigueur : En vigueur
        Expire : Expiré
    }
```

**Transitions (✅ SLM-12)** : réservées aux gestionnaires ; seules les transitions
ci-dessous sont permises (SOC-05).

| Objet | De → Vers | Qui | Conditions (400 sinon) | Statut |
|---|---|---|---|---|
| Service | En conception → En service | Manager | Propriétaire renseigné, heures de service | ✅ |
| Service | En service → Retiré | Manager | Aucun SLA En vigueur ; aucun incident ou demande ouvert sur le service | ✅ SLM-16 |
| SLA | Brouillon → En vigueur | Manager | Service En service ; délais P1–P4 renseignés et croissants (P1 ≤ P2 ≤ P3 ≤ P4) ; `ValidFrom` ; pas d'autre SLA En vigueur pour le même service et le même client | ✅ |
| SLA | En vigueur → Expiré | Auto, ou Manager | `ValidTo` passé (auto, relevé périodique ; gestionnaires notifiés) | ✅ SLM-04 |
| SLA | En vigueur → Brouillon | Manager | Renégociation ; les mesures en cours restent sur la version précédente | ✅ SLM-12 |
| SLA | Expiré → … | — | Statut final ; un nouvel accord se crée | ✅ SLM-12 |

## 5. Règles de gestion

| ID | Règle | Statut | Lot |
|---|---|---|---|
| SLM-01 | **Catalogue des services** : nom, criticité, heures de service, responsable ; tenu par les gestionnaires | ✅ | |
| SLM-02 | **SLA** rattaché à un service : client, disponibilité cible, délais de résolution P1 à P4, validité, date de revue ; tenus par les gestionnaires ; supprimer un service supprime ses SLA | ✅ | |
| SLM-03 | Passer un SLA **En vigueur** exige des cibles complètes et croissantes, et l'unicité (service, client) parmi les SLA En vigueur | ✅ | 1 |
| SLM-04 | Un SLA passe **Expiré** automatiquement le lendemain de `ValidTo` | ✅ (relevé périodique des événements datés ; notification « SLA expiré » aux gestionnaires ; le journal d'audit trace le passage) | |
| SLM-05 | **Revue échue** : un SLA En vigueur dont la date de revue est passée remonte dans « Relances » des gestionnaires | ✅ | |
| SLM-10 | Les incidents (INC-20), problèmes et demandes désignent leur **service** dans le catalogue (`ServiceId`) au lieu d'un texte libre | ✅ (nom recopié dans `AffectedService` pour l'affichage et le reporting ; l'échéance SLA de l'incident suit le service du catalogue) | |
| SLM-11 | Un service **En service** a obligatoirement un propriétaire | ✅ | 1 |
| SLM-12 | **Transitions contraintes** selon le § 4 (SOC-05) | ✅ | 1 |
| SLM-13 | **Délais de prise en charge** P1 à P4 dans le SLA (`ResponseHoursP1…P4`), mesurés sur `FirstResponseAt` des incidents (INC-06) | ✅ (délais dans le SLA ; échéance `ResponseDueAt` de l'incident, comparée à `FirstResponseAt`) / 🔜 (taux de respect, avec SLM-20) | |
| SLM-14 | **Heures de service structurées** : jours ouvrés, plages horaires, jours fériés, fuseau (défaut : 24 h/24 et 7 j/7) ; tous les délais SLA se comptent dans ces heures | ✅ (`Core/Itil/ServiceCalendar.cs` ; échéances des incidents, INC-22 ; une plage par jour, sans passage de minuit) | |
| SLM-15 | Un service **Retiré** ne peut plus être choisi pour un nouvel incident, demande ou SLA | ✅ (400 ; les enregistrements qui le portaient le gardent et restent modifiables) | |
| SLM-16 | Un service **ne se retire pas** tant qu'il porte un SLA En vigueur, un incident ouvert (ni Résolu ni Clos) ou une demande ouverte (ni Satisfaite, Close, Rejetée ni Annulée) | ✅ (400 citant les références en cause ; contrôlé au retrait seulement) | |

## 6. Délais, calculs et alertes

| ID | Règle | Statut | Lot |
|---|---|---|---|
| SLM-20 | **Mesure du respect** (F1) : pour chaque SLA En vigueur, chaque mois, part des incidents du service (et de sa priorité) résolus dans le délai — échéances calculées par INC-21, hors temps En attente (INC-22) | ✅ (`Core/Itil/SlaCompliance.cs` ; mois UTC ; incidents du service créés pendant la validité du SLA et dotés d'une échéance : comptés au mois de leur résolution, ou, encore ouverts, au mois où l'échéance est passée ; les incidents ne portant pas de client, un SLA mesure tout son service ; encadré « Respect du SLA » de la fiche) | |
| SLM-21 | **Disponibilité mesurée** : 100 % moins la durée cumulée des incidents **majeurs** du service (création → résolution, dans les heures de service), rapportée aux heures de service du mois ; comparée à `AvailabilityTarget` | ✅ (fiche du SLA, colonne « Disponibilité » du respect mensuel : heures de service du calendrier du service, jusqu'à maintenant pour le mois en cours ; incidents majeurs simultanés comptés une fois ; heures d'indisponibilité et nombre d'incidents majeurs) | 3 |
| SLM-22 | **Revue de SLA** : le gestionnaire enregistre la revue (date, constats, décisions) ; la prochaine date de revue est reportée de 12 mois par défaut | ✅ (encadré « Revues du SLA » de la fiche ; refusée sur un SLA expiré) | |
| SLM-23 | Un mois sous la cible (SLM-20 ou SLM-21) crée une **alerte** pour le gestionnaire et propose une **amélioration** reliée (CSI-20) | ✅ (pour SLM-20 : au relevé périodique, mois précédent d'un SLA En vigueur doté d'une `ComplianceTarget` ; alerte « SLA sous la cible » aux gestionnaires et amélioration Proposée, source « SLA », reliée au SLA, une fois par mois ; pour SLM-21 : SLA doté d'une `AvailabilityTarget`, alerte « Disponibilité sous la cible » et amélioration reliée) | |
| SLM-24 | Notifications (SOC-21) : SLA expirant dans 30 jours → gestionnaires ; revue échue → propriétaire du service | ✅ | |

## 7. Liens avec les autres pratiques

| Lien | Sens | Règle | Statut |
|---|---|---|---|
| Service → SLA | Engagements | SLM-02 | ✅ |
| Incident → Service | Mesure du respect | SLM-10, SLM-20 | ✅ |
| CI → Service | Services supportés | CFG-21 | ✅ |
| Demande → Service | Modèles de demande par service | REQ-20 | ✅ |
| SLA → Amélioration | Écart à la cible | SLM-23, CSI-20 | ✅ |

## 8. Console et indicateurs

Console ✅ : SLA à revoir (Relances, gestionnaires) ; SLA en vigueur expirant dans les
30 jours (Relances, gestionnaires des périmètres, « Expire le … », SLM-24, qui le notifie
aussi). Un mois sous la cible est notifié aux gestionnaires (SLM-23). À venir : SLA sous la
cible ce mois (Urgent, gestionnaires).

| Indicateur | Calcul | Statut |
|---|---|---|
| Volumétrie (services, SLA par statut) | | ✅ |
| Respect des SLA | SLM-20, par SLA et par mois | ✅ (fiche du SLA) / 🔜 SLM-30 (reporting, lot 2) |
| Disponibilité mesurée | SLM-21 | ✅ (fiche du SLA ; tableau de bord, SLM-31, [REP-45](../reporting.md#7-conformité-et-métriques)) |
| SLA à revoir, expirant | SLM-05, SLM-24 | ✅ (console, Relances) |

## 9. API

| Route | Policy | Rôle |
|---|---|---|
| `GET /api/services`, `GET /api/services/{id}` (avec SLA) | User | Catalogue |
| `POST/PUT /api/services[/{id}]` | Manager | Tenue du catalogue |
| `GET /api/agreements`, `GET /api/agreements/{id}` | User | SLA |
| `POST/PUT /api/agreements[/{id}]` | Manager | Tenue des SLA |
| `DELETE /api/services/{id}`, `/api/agreements/{id}` | Admin | Suppression |
| `POST /api/agreements/{id}/reviews` (`{ findings, decisions, nextReviewDate }`) | Manager du périmètre | Revue du SLA (SLM-22) ; la fiche (`GET /api/agreements/{id}`) liste les revues, la plus récente d'abord |
| `GET /api/agreements/{id}/compliance?from=&to=` (mois AAAA-MM ; six derniers mois par défaut, 24 au plus) | User | Respect mesuré par mois : total, dans le délai, %, cible, atteinte, détail par priorité (SLM-20) |

## 10. Scénarios d'acceptation

- **SLM-02** — *Quand* un User crée un service, *alors* 403.
- **SLM-03** — *Étant donné* un SLA Brouillon avec P1 = 8 h et P2 = 4 h, *quand* un Manager le passe En vigueur, *alors* 400 « délais non croissants » ; un second SLA En vigueur pour le même service et le même client, 409.
- **SLM-04** — *Étant donné* un SLA En vigueur dont `ValidTo` était hier, *alors* il est Expiré.
- **SLM-14** — *Étant donné* des heures de service Lun–Ven 8 h–18 h, *quand* un incident P3 (24 h) est créé vendredi à 17 h, *alors* son échéance est mercredi à 11 h (1 h vendredi + 10 h lundi + 10 h mardi + 3 h mercredi = 24 h de service).
- **SLM-16** — *Étant donné* un service En service portant un SLA En vigueur et un incident En cours, *quand* un Manager le passe Retiré, *alors* 400 citant le SLA ; une fois le SLA Expiré, 400 citant l'incident ; une fois l'incident résolu (et aucune demande ouverte), le retrait est accepté.
- **SLM-20** — *Étant donné* 10 incidents P2 du service en octobre, dont 9 résolus dans le délai, *alors* le respect d'octobre vaut 90 %.
