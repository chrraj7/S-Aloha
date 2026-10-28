# 🧾 Gestion des demandes de service

> Pratique ITIL 4 *Service request management*. Code :
> `backend/Modules/ServiceRequestManagement`, `frontend/src/modules/service-request-management`.
> Préfixe des règles : **REQ**. Socle commun et conventions (✅ / 🔜, lots) :
> [règles métier](../regles-metier.md).

<sub>[← Règles métier](../regles-metier.md) · [Toutes les pratiques](readme.md)</sub>

## 1. Objectif et périmètre

*Objectif ITIL 4 : soutenir la qualité de service convenue en traitant, de manière
efficace et conviviale, toutes les demandes de service prédéfinies initiées par les
utilisateurs.*

Une **demande de service** est une sollicitation **prévue** : accès, matériel, logiciel,
information. Elle suit un **modèle** connu à l'avance (catalogue de demandes), avec ou sans
approbation. Hors périmètre : un dysfonctionnement (→ [incident](gestion-des-incidents.md)),
une modification non standard du SI (→ [changement](habilitation-des-changements.md)).

## 2. Rôles et droits

| Rôle ITIL | Rôle applicatif | Responsabilités |
|---|---|---|
| Demandeur | Tous (créateur) | Soumet, suit, annule, confirme la satisfaction |
| Bénéficiaire | Utilisateur AD | Reçoit le service demandé (peut différer du demandeur) |
| Approbateur | Manager | Approuve ou rejette |
| Agent d'exécution | User (responsable) | Réalise la demande et ses tâches |
| Gestionnaire du catalogue | Manager | Tient les modèles de demande |

| Action | User | Manager | Admin | Statut |
|---|---|---|---|---|
| Soumettre, modifier, traiter une demande approuvée | ✔ | ✔ | ✔ | ✅ |
| Approuver / rejeter | ✘ | ✔ | ✔ | ✅ |
| Annuler sa propre demande avant traitement | Demandeur | ✔ | ✔ | ✅ REQ-09 |
| Tenir le catalogue de demandes | ✘ | ✔ | ✔ | ✅ REQ-20 |
| Modifier l'échéance d'une demande issue d'un modèle | ✘ | ✔ | ✔ | ✅ REQ-22 |
| Supprimer | ✘ | ✘ | ✔ | ✅ |

## 3. Données

Champs communs (référence `REQ-AAAA-NNNN`, titre, description, statut, responsable AD,
créateur = demandeur, dates) : [socle](../regles-metier.md#5-règles-communes-aux-processus).

| Champ | Type | Oblig. | Valeurs / règle | Statut |
|---|---|---|---|---|
| `RequestedItem` | texte 200 | ✔ | Objet demandé ; repris du modèle s'il est vide | ✅ |
| `ServiceId`, `AffectedService` | lien, texte 150 | | Service du catalogue concerné et son nom recopié ([SLM-10](gestion-des-niveaux-de-service.md)) | ✅ |
| `RequestedFor`, `RequestedForDisplayName` | AD | | Bénéficiaire ; par défaut le demandeur (REQ-07) | ✅ |
| `SatisfactionScore`, `SatisfactionComment`, `SatisfactionAt` | entier 1 à 5, texte, date | facultatif | Enquête de satisfaction du demandeur ou du bénéficiaire, une fois la demande close (REQ-32) | ✅ |
| `AutoClosed` | booléen | auto | Close automatiquement 5 jours ouvrés après la satisfaction (REQ-11) | ✅ |
| `ContestReason` | texte | à la contestation | Motif de la contestation d'une demande satisfaite, effacé à la satisfaction suivante (REQ-10) | ✅ |
| `CancelReason` | texte | à la requalification | Motif d'annulation, avec la référence du changement ou de l'incident créé (REQ-12) | ✅ |
| `DueDate` | date | | Échéance : calculée depuis le modèle à l'approbation (REQ-22) ; souhaitée, sans modèle à délai cible | ✅ |
| `ApprovedBy`, `ApprovedAt` | texte, date | auto | Approbation (SOC-08) | ✅ |
| `FulfilledAt`, `ClosedAt` | date UTC | auto | Satisfaction, clôture | ✅ |
| `CatalogItemId` | lien | | Modèle du catalogue de demandes, choisi à la création ; sans modèle, demande libre (objet demandé obligatoire) | ✅ REQ-20 |
| `RejectionReason` | texte | au rejet | Motif communiqué au demandeur | ✅ |
| `CancelledAt` | date UTC | auto | Annulation | ✅ REQ-09 |

**Modèle de demande** (`RequestCatalogItems`, ✅ REQ-20) : nom (objet demandé), description,
service du catalogue, approbation requise (oui/non), délai cible (heures de service), groupe
d'exécution (AD), actif/inactif ; tâches prédéfinies (titre, responsable facultatif, ordre) ✅ REQ-24. Un modèle se désactive plutôt
que de se supprimer (suppression refusée tant qu'une demande en vient).

**Tâche d'exécution** (`RequestTasks`, ✅ REQ-24) : titre, responsable AD (à défaut, celui de la
demande), statut (À faire, En cours, Terminée, Annulée), ordre, venue du modèle ou ajoutée.
Les tâches du modèle sont recopiées une fois, à l'approbation (ou à la création d'une demande
pré-approuvée) ; on en ajoute à une demande approuvée ou en cours ; elles ne changent plus
une fois la demande satisfaite. Une tâche ajoutée se supprime (gestionnaire) ; une tâche du
modèle s'annule.

## 4. Cycle de vie

```mermaid
stateDiagram-v2
    direction LR
    [*] --> Soumise
    [*] --> Approuvee : modèle sans approbation
    Soumise --> Approuvee : Manager
    Soumise --> Rejetee : Manager + motif
    Soumise --> Annulee : demandeur
    Approuvee --> EnCours : prise en charge
    Approuvee --> Annulee : demandeur
    EnCours --> Satisfaite : tâches terminées
    Satisfaite --> EnCours : non conforme
    Satisfaite --> Close : confirmation ou 5 jours ouvrés
    Approuvee : Approuvée
    Rejetee : Rejetée
    Annulee : Annulée
    EnCours : En cours
```

**Aujourd'hui (✅)** : statuts Soumise, Approuvée, Rejetée, En cours, Satisfaite, Close ;
approbation réservée aux gestionnaires ; traitement seulement après approbation.
**Cible (🔜 REQ-08, lot 1)** : seules les transitions ci-dessous (SOC-05) ; statut
*Annulée* ajouté.

| De → Vers | Qui | Conditions (400 sinon) | Effets | Statut |
|---|---|---|---|---|
| — → Soumise | Tous | Titre, objet demandé | Référence | ✅ |
| — → Approuvée | Auto | Modèle sans approbation | `ApprovedBy` = « Modèle pré-approuvé » | ✅ REQ-21 |
| Soumise → Approuvée | Manager | | `ApprovedAt`, `ApprovedBy` | ✅ |
| Soumise → Rejetée | Manager | Motif de rejet | | ✅ (motif) |
| Soumise, Approuvée → Annulée | Demandeur, Manager | Pas encore En cours | `CancelledAt` | ✅ REQ-09 |
| Approuvée → En cours | Tous | Responsable renseigné | | ✅ (approbation exigée) / 🔜 (responsable) |
| En cours → Satisfaite | Responsable, Manager | Toutes les tâches Terminée ou Annulée | `FulfilledAt` | ✅ REQ-25 |
| Satisfaite → En cours | Demandeur, bénéficiaire, Manager | Motif (`ContestReason`) ; dans les 5 jours ouvrés | `FulfilledAt` effacé ; responsable notifié | ✅ REQ-10 |
| Satisfaite → Close | Demandeur, Manager, ou auto après 5 jours ouvrés | | `ClosedAt` ; `AutoClosed` si automatique, demandeur notifié | ✅ REQ-11 |
| Rejetée, Annulée, Close → … | — | Statuts finaux | | ✅ REQ-08 |

## 5. Règles de gestion

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REQ-01 | Demande créée au statut **Soumise**, référence `REQ-AAAA-NNNN` ; l'objet demandé est obligatoire | ✅ | |
| REQ-02 | **Approuvée** et **Rejetée** : gestionnaire uniquement ; l'approbation horodate `ApprovedAt` / `ApprovedBy` | ✅ | |
| REQ-03 | **En cours**, **Satisfaite** et **Close** exigent une demande approuvée : une demande **rejetée est terminée** | ✅ | |
| REQ-04 | Revenir à Soumise ou passer à Rejetée **efface l'approbation** | ✅ | |
| REQ-05 | `FulfilledAt` posé à Satisfaite, `ClosedAt` à Close, effacés à la sortie (SOC-08) | ✅ | |
| REQ-06 | Un **rejet** exige un motif, communiqué au demandeur | ✅ | 1 |
| REQ-07 | Le **bénéficiaire** vaut le demandeur par défaut ; une demande pour autrui garde les deux | ✅ | 2 |
| REQ-08 | **Transitions contraintes** selon le § 4 (SOC-05) ; Rejetée, Annulée et Close sont finales | ✅ | 1 |
| REQ-09 | Statut **Annulée** : le demandeur (ou un Manager) annule une demande Soumise ou Approuvée, tant qu'elle n'est pas En cours | ✅ | 1 |
| REQ-10 | **Contestation** : dans les 5 jours ouvrés, le demandeur ou le bénéficiaire renvoie une demande Satisfaite En cours, avec motif | ✅ | 2 |
| REQ-11 | **Clôture** à la confirmation du demandeur, ou automatique 5 jours ouvrés après Satisfaite | ✅ | 3 |
| REQ-12 | Une demande qui révèle un **besoin non standard** se requalifie : créer un changement (ou un incident) relié, puis annuler la demande avec ce motif (`CancelReason`, qui cite la référence créée) ; demandeur ou gestionnaire, demande Soumise ou Approuvée (mêmes droits que REQ-09) | ✅ | 2 |

### Catalogue de demandes

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REQ-20 | **Catalogue de demandes** tenu par les gestionnaires : chaque demande se crée depuis un **modèle actif** ; l'objet demandé et le service en sont repris | ✅ (page « Catalogue de demandes » ; modèle choisi à la création, objet et service repris ; sans modèle, demande libre) | |
| REQ-21 | Un modèle **sans approbation** crée la demande directement **Approuvée** (« Modèle pré-approuvé »), comme un changement standard | ✅ | |
| REQ-22 | **Échéance** = soumission (ou approbation si requise) + délai cible du modèle, en heures de service (SLM-14) ; modifiable par un Manager seulement | ✅ (calculée à l'approbation, ou à la création pré-approuvée, dans le calendrier du service du modèle ; la fiche renvoie le seul jour : le même jour garde l'heure calculée et n'est pas une modification) | |
| REQ-23 | Le **groupe d'exécution** du modèle devient le responsable de la demande approuvée | ✅ (si la demande n'a pas encore de responsable) | |
| REQ-24 | **Tâches d'exécution** : les tâches prédéfinies du modèle sont créées à l'approbation, chacune avec son responsable ; on peut en ajouter | ✅ | 3 |
| REQ-25 | **Satisfaite** exige que toutes les tâches soient Terminée ou Annulée | ✅ | 3 |

## 6. Délais, calculs et alertes

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REQ-30 | Une demande est **en retard** si son échéance est passée et qu'elle n'est ni Satisfaite, ni Close, ni Rejetée, ni Annulée ; même règle de date que PRB-19 (en retard le lendemain) | ✅ (`overdue` sur la fiche et le registre ; console, Relances des gestionnaires ; notification au responsable et aux gestionnaires, une fois) | |
| REQ-31 | Notifications (SOC-21) : soumission → approbateurs ; approbation ou rejet → demandeur ; affectation → responsable ; satisfaction → demandeur et bénéficiaire | ✅ | |
| REQ-32 | **Enquête de satisfaction** à la clôture (note 1 à 5, commentaire), facultative | ✅ | 3 |

## 7. Liens avec les autres pratiques

| Lien | Sens | Règle | Statut |
|---|---|---|---|
| Demande → Service | Service du catalogue concerné | REQ-20, SLM-10 | ✅ |
| Demande → Changement | Besoin non standard | REQ-12 | ✅ (requalification ou lien manuel) |
| Demande → CI | Matériel ou logiciel fourni | Lien manuel | ✅ |
| Demande → Article | Procédure d'exécution | Lien manuel | ✅ |

## 8. Console et indicateurs

Console ✅ : demandes Soumise dans « Décisions » (gestionnaires) ; demandes en retard
(Relances, gestionnaires, REQ-30) ; mes demandes ouvertes (Mon travail). À venir : demandes approuvées non
prises en charge depuis plus d'un jour ouvré (Relances, lot 2).

| Indicateur | Calcul | Statut |
|---|---|---|
| Volumétrie par statut | | ✅ |
| Délai moyen de traitement | `FulfilledAt − CreatedAt`, par modèle | ✅ REQ-40 ([REP-60](../reporting.md#9-demandes)) |
| Respect des délais | Part des demandes satisfaites au plus tard à l'échéance | ✅ REQ-41 |
| Taux d'approbation | Approuvées / (approuvées + rejetées), par modèle | ✅ REQ-42 |
| Satisfaction moyenne | REQ-32, avec la répartition des notes | ✅ REQ-43 |

## 9. API

| Route | Policy | Rôle |
|---|---|---|
| `GET /api/requests` (filtres `status`, `q`, `owner`), `GET /api/requests/{id}` | User | Registre, fiche |
| `POST /api/requests`, `PUT /api/requests/{id}` | User (Approuvée/Rejetée : Manager) | Création, modification, transitions |
| `DELETE /api/requests/{id}` | Admin | Suppression |
| `GET/POST /api/requests/{id}/tasks`, `PUT …/tasks/{taskId}`, `DELETE …/tasks/{taskId}` (Manager) | User | Tâches d'exécution (REQ-24, REQ-25) |
| `POST /api/requests/{id}/satisfaction` (`score`, `comment`) | Demandeur, bénéficiaire | Enquête de satisfaction d'une demande close (REQ-32) |
| `POST /api/requests/{id}/requalify` (`target` : `change` ou `incident`, `reason`) | Demandeur, Manager | Requalification d'un besoin non standard : changement ou incident créé et relié, demande annulée (REQ-12) |
| `GET /api/request-catalog` (`?all=true` : désactivés compris), `GET /api/request-catalog/{id}` | User | Catalogue de demandes (REQ-20) |
| `POST/PUT /api/request-catalog[/{id}]` | Manager | Tenue du catalogue |
| `DELETE /api/request-catalog/{id}` | Admin | Suppression d'un modèle dont aucune demande ne vient |

## 10. Scénarios d'acceptation

- **REQ-02** — *Étant donné* une demande Soumise, *quand* un User la passe à Approuvée, *alors* 403.
- **REQ-03** — *Étant donné* une demande Rejetée, *quand* on la passe En cours, *alors* 400 « doit d'abord être approuvée ».
- **REQ-06** — *Étant donné* une demande Soumise, *quand* un Manager la rejette sans motif, *alors* 400.
- **REQ-09** — *Étant donné* une demande Approuvée créée par Ursula, *quand* Ursula l'annule, *alors* elle est Annulée ; En cours, l'annulation est refusée (400).
- **REQ-21** — *Étant donné* un modèle « Compte VPN » sans approbation, *quand* on crée une demande depuis ce modèle, *alors* elle est Approuvée, `ApprovedBy` = « Modèle pré-approuvé ».
- **REQ-30** — *Étant donné* une demande En cours due hier, *alors* elle figure dans les demandes en retard ; due aujourd'hui, non.
