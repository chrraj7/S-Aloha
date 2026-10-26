# 🧭 Périmètres

<sub>[← Documentation](../readme.md) · [Règles métier](regles-metier.md) · [Frontend](frontend.md) · [ADR-0014](../decisions/adr-0014-perimetres-droits-scopes.md)</sub>

> Droits **scopés par périmètre** : un périmètre est une partie de l'organisation (direction,
> site, entité) ; un gestionnaire ou un intervenant n'agit que sur les enregistrements de ses
> périmètres, l'administrateur sur tous. Règles préfixées **PER**. Statut : **implémenté**
> (étape 6 du [plan du lot 1](processus/readme.md#plan-dimplémentation-conseillé--lot-1)) ;
> les choix structurants sont tranchés dans l'[ADR-0014](../decisions/adr-0014-perimetres-droits-scopes.md) (accepté).
> **Avancement** : modèle, reprise des données et API d'administration (PER-01, PER-02,
> PER-10 à PER-12) ; rôle effectif, création, visibilité, décisions et déplacement par
> périmètre (PER-03 à PER-07) ; liens, console, reporting et recherche limités au périmètre
> (PER-08, PER-09) ; écran Administration › Périmètres. Étape 6 terminée le 2026-10-13.

## 1. Pourquoi

Aujourd'hui, le rôle d'un utilisateur vient de ses groupes Active Directory et vaut pour
**toute** la plateforme : un Manager de la direction financière peut autoriser un changement
du site de production, un intervenant voit les incidents de toutes les entités. Dès qu'une
DSI sert plusieurs directions ou sites, il faut que les décisions de gestionnaire — et, selon
le choix retenu, la visibilité — se limitent au périmètre de chacun.

## 2. Modèle

```mermaid
erDiagram
    Perimeter ||--o{ PerimeterAssignment : "affecte"
    Perimeter ||--o{ Record : "porte"
    PerimeterAssignment }o--|| Principal : "utilisateur ou groupe AD"
```

| Objet | Champs | Règle |
|---|---|---|
| **Périmètre** (`Perimeter`) | nom (unique), description, suppression douce (`DeletedAt`, `DeletedBy`) | PER-01 |
| **Affectation** (`PerimeterAssignment`) | principal AD (`User` ou `Group`, identifiant, nom affiché), rôle (`Manager` ou `User`), périmètre ; unique (principal, rôle, périmètre) | PER-02 |
| **Enregistrement** de pratique | `PerimeterId` (obligatoire) | PER-04 |

- **Admin** : portée **globale**, jamais affecté à un périmètre (l'affectation d'un Admin à un
  périmètre est refusée).
- **Manager** et **User** : une ou plusieurs affectations ; un même compte peut être Manager
  sur un périmètre et User sur un autre.
- Le **groupe AD de rôle** (`Ldap:Groups`) continue de donner l'**accès** à la plateforme et
  le statut d'Admin ; les **affectations** donnent la **portée** et le rôle par périmètre.

## 3. Règles

| ID | Règle | Statut | Lot |
|---|---|---|---|
| PER-01 | **Périmètre** : nom unique (insensible à la casse) et description ; créé, renommé et supprimé par l'**Admin** seul. Suppression **douce** (restaurable) ; suppression définitive seulement si aucun enregistrement ni affectation n'y est rattaché (409 sinon, avec les compteurs). « Organisation » ne se supprime pas | ✅ | |
| PER-02 | **Affectation** d'un utilisateur ou d'un **groupe AD** à un périmètre avec le rôle Manager ou User, par l'Admin ; plusieurs affectations par compte ; jamais d'Admin scopé | ✅ | |
| PER-03 | **Rôle effectif** sur un enregistrement : le plus élevé des rôles que l'utilisateur tient sur le périmètre de l'enregistrement, directement ou par un de ses groupes (étape 5, SOC-10) ; Admin partout ; le rôle par périmètre prime sur le rôle AD (D4) | ✅ | |
| PER-04 | Tout enregistrement de pratique **porte un périmètre**, obligatoire à la création ; proposé par défaut quand l'utilisateur n'a qu'un périmètre (sélecteur masqué) ; on ne crée que dans un périmètre où l'on est affecté (403), jamais dans un périmètre supprimé (400) | ✅ | |
| PER-05 | **Visibilité** : un utilisateur voit les enregistrements de ses périmètres ; les **référentiels partagés** (catalogue des services, articles publiés) restent visibles de tous (ADR-0014, D2), en lecture seule hors de ses périmètres ; hors de ses périmètres, un enregistrement répond 404 (fiche et routes propres : historique, impact d'un CI, questionnaire NIS 2, éléments et synthèse d'un projet, analyses et actions d'un problème) | ✅ | |
| PER-06 | Les **décisions de gestionnaire** (autoriser, approuver, publier, valider, qualifier un problème, transitions réservées) exigent le rôle **Manager sur le périmètre** de l'enregistrement ; chef de projet ou gestionnaire du périmètre pour un projet (PRJ-04) | ✅ | |
| PER-07 | **Changer le périmètre** d'un enregistrement : Manager des deux périmètres, ou Admin ; tracé au journal d'audit (SOC-20, champ `perimeterId`) | ✅ | |
| PER-08 | **Liens** entre enregistrements de périmètres différents : permis si l'utilisateur voit les deux ; un lien ne donne pas accès à l'enregistrement d'un autre périmètre : il est listé masqué (« Enregistrement d'un autre périmètre », sans référence ni lien), ne se retire pas ; relier vers un enregistrement invisible répond comme une référence inconnue (400) ; impact d'un CI et conflits de changements masquent de même | ✅ | |
| PER-09 | **Console, reporting, recherche, cas similaires** : limités aux périmètres de l'utilisateur ; les blocs de pilotage de la console, aux périmètres dont il est **gestionnaire** (un User AD Manager d'un périmètre y a accès, D4) ; l'Admin voit tout ; filtre « Périmètre » (`?perimeter=`) dans la console et le reporting dès que l'on a plusieurs périmètres | ✅ | |
| PER-10 | **Reprise des données** : à la mise à niveau, un périmètre « Organisation » est créé et reçoit tous les enregistrements ; un compte qu'**aucune affectation** ne désigne (ni directement ni par ses groupes) est réputé affecté à ce périmètre avec son rôle AD (comportement inchangé) (ADR-0014, D3). *Amendé le 2026-10-12 : la règle valait « tant qu'aucune affectation n'existe » ; la première affectation créée par l'Admin aurait alors coupé l'accès de tous les autres comptes* | ✅ (rattachement par la migration `Perimeters`) | |
| PER-11 | Créations, modifications, suppressions de périmètres et d'affectations sont **tracées** au journal d'audit (SOC-20) | ✅ | |
| PER-12 | Fiche d'un périmètre : compteurs des enregistrements rattachés par pratique et liste des affectations | ✅ | |

## 4. Administration

Entrée **Administration › Périmètres** (Admin), voir [frontend § Administration](frontend.md#administration) :

- **Liste** des périmètres (dont supprimés, grisés, restaurables), création, renommage ;
- **Fiche** : description, compteurs par pratique (PER-12), affectations (ajout par
  recherche dans l'annuaire, utilisateur ou groupe, rôle Manager ou User ; retrait) ;
- **Suppression** douce, restauration, suppression définitive si rien n'est rattaché (PER-01).

L'entrée **Utilisateurs et rôles** (SOC-26) affiche, pour chaque compte constaté, son rôle AD
et ses affectations.

## 5. API

| Route | Policy | Rôle |
|---|---|---|
| GET `/api/perimeters` | User | Mes périmètres en service, avec mon rôle sur chacun (sélecteurs) ; tous pour l'Admin |
| GET/POST/PUT `/api/admin/perimeters[/{id}]`, DELETE `…/{id}` (douce), POST `…/{id}/restore`, DELETE `…/{id}/purge` | Admin | Gestion des périmètres |
| GET/POST `/api/admin/perimeters/{id}/assignments`, DELETE `/api/admin/assignments/{id}` | Admin | Affectations |
| `perimeterId` sur chaque enregistrement et dans les DTO (création, déplacement) ; `?perimeter=` sur les listes | selon PER-04 à PER-07 | Portée |

## 6. Scénarios d'acceptation

- **PER-06** — *Étant donné* Lova, Manager du périmètre « Siège » et User du périmètre
  « Usine », *quand* elle autorise un changement de l'Usine, *alors* 403 ; un changement du
  Siège, *alors* il est autorisé.
- **PER-04** — *Étant donné* un utilisateur affecté au seul périmètre « Siège », *quand* il
  déclare un incident, *alors* l'incident porte le Siège ; dans un périmètre où il n'est pas
  affecté, 403.
- **PER-01** — *Étant donné* un périmètre qui porte des incidents, *quand* l'Admin le purge,
  *alors* 409 avec les compteurs ; supprimé en douceur, il disparaît des sélecteurs et se
  restaure.
- **PER-10** — *Étant donné* une installation existante sans affectation, *après* la mise à
  niveau, *alors* tout est rattaché à « Organisation » et chacun garde ses droits actuels.
