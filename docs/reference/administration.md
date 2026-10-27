# 🛠️ Administration et référentiels

<sub>[← Documentation](../readme.md) · [Règles métier](regles-metier.md) · [Frontend](frontend.md) · [ADR-0019](../decisions/adr-0019-console-d-administration.md)</sub>

> La section **Administration** (Admin seul) porte la plateforme elle-même : utilisateurs et
> rôles, santé, journal d'audit global, nettoyage des doublons ([SOC-26](regles-metier.md#9-traçabilité-commentaires-administration)).
> La section **Paramétrage** (Manager, Admin) porte le fonctionnement des pratiques, dont les
> **catégories** d'incident et de problème ([SOC-27](regles-metier.md#9-traçabilité-commentaires-administration)).
> Règles préfixées **ADM**. Résout le chantier A1 du [plan d'action](../plan-action.md) ; choix
> tranchés dans l'[ADR-0019](../decisions/adr-0019-console-d-administration.md).

## 1. Entrées

| Section | Entrée | Visible par | Règles | Statut |
|---|---|---|---|---|
| Administration | Utilisateurs et rôles | Admin | ADM-10 à ADM-13 | ✅ |
| Administration | Santé | Admin | ADM-20, ADM-21 | ✅ |
| Administration | Journal d'audit | Admin | ADM-30, ADM-31 | ✅ |
| Administration | Nettoyage des doublons | Admin | ADM-40 à ADM-43 | ✅ |
| Administration | Périmètres | Admin | [PER-01 à PER-12](perimetres.md) | ✅ |
| Administration | Index de recherche | Admin | [RAG-08](recherche.md) | ✅ |
| Administration | Messagerie et stockage | Admin | ADM-60 à ADM-63 | ✅ |
| Paramétrage | Catégories (incidents, problèmes) | Manager, Admin | ADM-50 à ADM-53 | ✅ |

## 2. Utilisateurs et rôles

| ID | Règle | Statut |
|---|---|---|
| ADM-10 | **Liste** : les membres directs des trois groupes AD de rôle (`Ldap:Groups`), lus en direct dans l'annuaire par le compte de service ; pour chacun, compte, nom affiché, rôle AD (le plus élevé de ses groupes de rôle), groupes de rôle, affectations de périmètre (directes, et par ses groupes AD), date de dernière connexion. Recherche par nom ou compte, filtre par rôle | ✅ |
| ADM-11 | **Mapping** groupes AD → rôles affiché en lecture (réglé dans la configuration de l'API, pas dans l'interface) ; une affectation de périmètre qui désigne un compte absent des groupes de rôle est signalée « sans accès » (il ne peut pas se connecter) | ✅ |
| ADM-12 | **Dernière connexion** : à chaque connexion réussie, l'API retient le compte et la date, rien d'autre (donnée personnelle minimale) ; une tâche quotidienne efface les comptes sans connexion depuis **13 mois** (`Admin:LastLoginRetentionMonths`) | ✅ |
| ADM-13 | Annuaire injoignable : la page l'indique (503 « Annuaire injoignable »), sans liste partielle ; la dernière connexion n'est jamais affichée seule comme liste des utilisateurs | ✅ |

## 3. Santé

| ID | Règle | Statut |
|---|---|---|
| ADM-20 | **État des composants**, chacun « OK », « Dégradé » ou « Hors service » avec un message et la durée de la vérification : base (connexion, migrations en attente), annuaire (liaison du compte de service), service d'embeddings ([RAG-08](recherche.md)), coffre de la découverte (clé configurée), messagerie et stockage des pièces jointes (activés ou non, ADM-63), tâches de fond (dernier relevé des notifications datées, tâches de découverte en échec) | ✅ |
| ADM-21 | **Version** déployée (numéro et commit), date de démarrage de l'API, environnement ; aucun secret ni chaîne de connexion affichés. Les sondes publiques `/health/live` et `/health/ready` sont distinctes ([#121](https://github.com/chrraj7/S-Aloha/issues/121)) | ✅ |

## 4. Journal d'audit global

| ID | Règle | Statut |
|---|---|---|
| ADM-30 | **Journal global** ([SOC-20](regles-metier.md#9-traçabilité-commentaires-administration)) : toutes pratiques, du plus récent au plus ancien, filtrable par auteur, type d'enregistrement (pratique), action, référence et période (30 derniers jours par défaut) ; pages de 100 lignes ; chaque ligne mène à la fiche | ✅ |
| ADM-31 | **Export CSV** des lignes filtrées (séparateur « ; », BOM UTF-8, comme [REP-08](reporting.md)), 10 000 lignes au plus | ✅ |

## 5. Nettoyage des doublons

| ID | Règle | Statut |
|---|---|---|
| ADM-40 | **Doublons probables** : deux enregistrements d'une même pratique (incidents, demandes, problèmes, changements, articles, améliorations) et d'un même périmètre, au **titre normalisé identique** (casse, accents, ponctuation et espaces ignorés), créés à **7 jours** d'intervalle au plus. Les CI, services, accords, évaluations et projets sont exclus (registres tenus à la main, unicité déjà contrôlée) | ✅ |
| ADM-41 | **Fusion** : l'Admin choisit l'enregistrement conservé ; les liens et les commentaires du doublon lui sont reportés (sans lien vers lui-même ni lien en double), le doublon est supprimé, et l'opération est tracée au journal d'audit des deux références. Refusée (400) si le doublon porte des éléments propres à sa pratique (analyses, actions correctives, décisions du CAB…, comptés comme pour [SOC-14](regles-metier.md#5-règles-communes-aux-processus)) ou s'il n'est pas dans le même périmètre : à traiter à la main. Ses notifications sont effacées. Distincte de la résolution « Doublon » d'un incident (INC-08), qui garde les deux enregistrements reliés | ✅ |
| ADM-42 | **« Pas un doublon »** : la paire n'est plus proposée | ✅ |
| ADM-43 | Confirmation avant la fusion, qui rappelle les deux références et ce qui est reporté ([SOC-14](regles-metier.md#5-règles-communes-aux-processus)) | ✅ |

## 6. Catégories (Paramétrage)

| ID | Règle | Statut |
|---|---|---|
| ADM-50 | **Liste des catégories** par pratique (incidents, problèmes), tenue par les gestionnaires dans Paramétrage (gestionnaire d'au moins un périmètre, [PER-13](perimetres.md)) : nom unique par pratique (casse ignorée), active ou non ; une catégorie utilisée se désactive au lieu de se supprimer (409) | ✅ |
| ADM-51 | Les fiches d'incident et de problème proposent les **catégories actives** de leur pratique ; dès que la pratique a au moins une catégorie active, l'API refuse (400) une catégorie hors liste à la création et quand la catégorie change, et ramène la saisie au nom exact de la liste (casse, espaces). Sans catégorie active (installation neuve), la saisie reste libre. Une catégorie inactive reste lisible sur les enregistrements qui la portent | ✅ |
| ADM-52 | **Reprise** : à la migration, les catégories déjà saisies sur les incidents et les problèmes deviennent des catégories actives de leur pratique (sans tenir compte de la casse) | ✅ |
| ADM-53 | Le service affecté reste tiré du catalogue ([SLM-10](processus/gestion-des-niveaux-de-service.md)) ; SOC-27 est réalisée avec ADM-50 à ADM-52 | ✅ |

## 7. Messagerie et stockage

Décision : [ADR-0020](../decisions/adr-0020-pieces-jointes-s3-et-smtp-configurables.md). Les
deux sont désactivés tant que l'Admin ne les a pas réglés.

| ID | Règle | Statut |
|---|---|---|
| ADM-60 | **SMTP** des courriels ([SOC-22](regles-metier.md#8-notifications)) : envoi activé ou non, serveur, port, chiffrement (STARTTLS, TLS implicite ou aucun pour un relais interne ; certificat toujours vérifié), compte facultatif, adresse et nom d'expéditeur, adresse de l'interface (liens des courriels, http(s)). L'activation exige le serveur et une adresse d'expéditeur valide (400) | ✅ |
| ADM-61 | **Stockage objet compatible S3** des pièces jointes ([SOC-25](regles-metier.md#8-notifications)), Garage en référence : activé ou non, adresse http(s), région (`garage` par défaut), bucket (3 à 63 caractères : minuscules, chiffres, points, tirets), clé d'accès et clé secrète, adressage par chemin (défaut) ou par sous-domaine, taille maximale d'un fichier (1 à 100 Mo, 10 par défaut). L'activation exige l'adresse, le bucket et les deux clés (400) | ✅ |
| ADM-62 | **Secrets** (mot de passe SMTP, clé secrète S3) chiffrés par le coffre (`Discovery:VaultKey`, [CFG-45](processus/gestion-de-la-configuration.md)) ; refusés (400) si le coffre n'a pas de clé ; jamais renvoyés (l'API dit seulement s'ils sont enregistrés) ; laissés vides, ils sont gardés, et « Effacer » les supprime. Chaque champ modifié est tracé au journal d'audit (type `parametre`, références « Messagerie » et « Stockage ») ; un secret n'y figure que comme « modifié » ou « effacé » | ✅ |
| ADM-63 | **Essais** avec les réglages enregistrés, même désactivés : courriel d'essai à une adresse saisie ; dépôt, relecture et suppression d'un objet d'essai dans le bucket. Le résultat (réussite, ou message de l'erreur sans secret) s'affiche sur la page. La page **Santé** indique si chacun est activé, et « Hors service » si un secret est enregistré alors que la clé du coffre manque | ✅ |

## 8. API

| Route | Policy | Rôle |
|---|---|---|
| `GET /api/admin/users` (`q`, `role`) | Admin | Utilisateurs et rôles (ADM-10, ADM-11) |
| `GET /api/admin/health` | Admin | Santé (ADM-20, ADM-21) |
| `GET /api/admin/audit` (`author`, `type`, `action`, `reference`, `from`, `to`, `page`), `GET …/audit/export` | Admin | Journal global, export (ADM-30, ADM-31) |
| `GET /api/admin/duplicates`, `POST /api/admin/duplicates/merge`, `POST /api/admin/duplicates/dismiss` | Admin | Doublons, fusion, « pas un doublon » (ADM-40 à ADM-42) |
| `GET/PUT /api/admin/settings/mail`, `POST …/mail/test` (`to`) | Admin | SMTP, courriel d'essai (ADM-60, ADM-62, ADM-63) |
| `GET/PUT /api/admin/settings/storage`, `POST …/storage/test` | Admin | Stockage S3, essai de dépôt (ADM-61 à ADM-63) |
| `GET /api/categories?practice=`, `POST`, `PUT /{id}`, `DELETE /{id}` | User (lecture), gestionnaire d'un périmètre ([PER-13](perimetres.md)) (écriture) | Catégories (ADM-50 à ADM-52) |
