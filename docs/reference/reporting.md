# 📊 Reporting

> Tableau de bord des pratiques (page **Reporting**, `/reports`) : santé des services,
> incidents, problèmes, changements et projets, conformité. Capacité transverse du socle
> (`backend/Core/Pilotage/DashboardController.cs`, `frontend/src/core/pages/Reporting.jsx` et
> `reporting/`), pas une pratique ITIL. Préfixe des règles : **REP**. Conventions (✅ / 🔜,
> lots) : [règles métier](regles-metier.md#3-conventions-de-ce-référentiel). Indicateurs
> propres à chaque pratique : § 8 de son document.

<sub>[← Documentation](../readme.md) · [Produit](produit.md) · [Règles métier](regles-metier.md) · [Architecture](architecture.md) · [Base de données](base-de-donnees.md) · [Frontend](frontend.md) · [Exploitation](exploitation.md) · [Sécurité](securite.md) · [Glossaire](glossaire.md)</sub>

## 1. Objectif

Donner en un écran l'état du service rendu et ce qui demande une action : un score de
santé expliqué, les incidents à suivre, les causes racines en attente, les changements et
projets à risque, la conformité, l'état de la CMDB. Chaque onglet se lit seul et s'exporte.

## 2. Filtres et onglets

Filtres communs, conservés dans l'URL (`?tab=&period=&from=&to=&service=&perimeter=`) :

- **Période** : 7, 30 (défaut), 90 jours, 12 mois ou personnalisée ; bornes **incluses**,
  aujourd'hui compris ([SOC-18](regles-metier.md#7-reporting)).
- **Service** : nom d'un service du catalogue. Il filtre les **incidents**, les
  **demandes** et les **problèmes** (service affecté), pas les changements ni les projets, qui n'ont pas de
  service ; les onglets Changements & projets et Configuration & inventaire l'ignorent.
- **Périmètre** ([PER-09](perimetres.md)) : les indicateurs portent sur les périmètres de
  l'utilisateur (tous pour l'Admin) ; le filtre, affiché dès qu'on en a plusieurs, en retient
  un. Un périmètre qui n'est pas le sien ne renvoie rien.

| Onglet | API | Règles |
|---|---|---|
| Synthèse globale | `GET /api/reports/dashboard/overview` | REP-01 à REP-07 |
| Incidents | `GET /api/reports/dashboard/incidents` | REP-10 à REP-16 |
| Demandes | `GET /api/reports/dashboard/requests` | REP-60 à REP-63 |
| Problèmes | `GET /api/reports/dashboard/problems` | REP-20 à REP-26 |
| Changements & projets | `GET /api/reports/dashboard/changes-projects` | REP-30 à REP-35 |
| Conformité & métriques | `GET /api/reports/dashboard/compliance` | REP-40 à REP-45 |
| Configuration & inventaire | `GET /api/reports/dashboard/configuration` | REP-50 à REP-54 |

Paramètres : `from`, `to` (dates, incluses), `service` (facultatif, sans tenir compte de la
casse). Policy : User. L'ancien point d'entrée `GET /api/reports/summary` reste servi.

### Présentation

Un tableau de bord en **widgets** (`reporting/charts.jsx`) :

- **Tuiles** d'indicateurs : libellé en petites capitales au-dessus du chiffre, unité en plus
  petit, précision dessous ; une tuile en alerte porte un liseré rouge **et** la mention
  écrite de l'alerte. Les rangées se remplissent sans trou (5 ou 6 tuiles : 3 par ligne ;
  7 ou 8 : 4).
- **Intertitres** à losange par thème dans les onglets denses : *Conformité réglementaire*,
  *Niveaux de service*, *Amélioration continue*, *Connaissances* ; *CMDB*, *Découverte et
  cycle de vie*.
- **Score de santé** en jauge circulaire, à la couleur de son niveau.
- **Graphiques** dessinés à la largeur réelle de leur carte (hauteur fixe, graduations à
  11 px) : des **colonnes empilées** pour les comptes par jour (volumétrie, découverte) —
  par **semaine** au-delà de 45 jours —, avec le total de chaque série en légende ; une
  **courbe** pour les cumuls (burn-up). Tout graphique bascule en tableau (« Voir les
  données ») ; une série entièrement nulle affiche « Aucune donnée sur la période ».

## 3. Synthèse globale

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REP-01 | **Score de santé** = 100 − pénalités, plancher 0, formule affichée sous le score : incidents majeurs ouverts −25 chacun (50 au plus), P1 ouverts non majeurs −10 (30 au plus), incidents ouverts hors SLA −5 (25 au plus), changements en échec clos sur la période −5 (20 au plus), projets hors tolérance de délai ou de coût −10 (20 au plus). **Bon** ≥ 80, **Dégradé** 50 à 79, **Critique** < 50 | ✅ | |
| REP-02 | Indicateurs : incidents majeurs ouverts (en alerte), **taux de résolution dans le SLA** sur la période (incidents résolus ayant une échéance, [INC-21](processus/gestion-des-incidents.md)), changements planifiés **cette semaine** (lundi à dimanche, hors rejetés), **projets à risque** (tolérance À surveiller ou Hors tolérance, [PRJ-31, PRJ-32](processus/gestion-de-projet.md)) | ✅ | |
| REP-03 | **Volumétrie** par jour de la période (colonnes regroupées par semaine au-delà de 45 jours) : incidents et demandes créés, changements mis en œuvre (début planifié des changements Planifié, Mis en œuvre ou Clos) | ✅ | |
| REP-04 | **Matrice des priorités** des incidents et problèmes ouverts : P1 Critique, P2 Élevée, P3 Moyenne, P4 Basse | ✅ | |
| REP-05 | **État de tolérance** d'un projet (délai, coût) : même calcul que la synthèse du projet ([PRJ-31, PRJ-32](processus/gestion-de-projet.md)) | ✅ | |
| REP-06 | **Projets à risque ou en retard** : liste avec leur état de tolérance et le lien vers la fiche | ✅ | |
| REP-07 | **Badges** des onglets : Incidents = majeurs ouverts + ouverts hors SLA (rouge s'il y a un majeur) ; Demandes = demandes en retard (REQ-30) ; Problèmes = RCA en attente (REP-22) ; Changements & projets = projets hors tolérance + changements en échec (rouge s'il y a un projet hors tolérance) ; Conformité = SLA à revoir ; Configuration = actifs matériels en fin de support dépassée + actifs du sas de staging à rapprocher ou en conflit, pour les gestionnaires (rouge s'il y a une fin de support dépassée) | ✅ | |
| REP-08 | **Exports** : CSV de l'onglet actif (séparateur « ; », BOM UTF-8, décimales à la française : s'ouvre tel quel dans Excel ; une cellule texte qui commence par `=`, `+`, `-`, `@`, une tabulation ou un retour chariot est précédée d'une apostrophe et reste du texte, un nombre reste un nombre) ; **PDF** par l'impression du navigateur, tous les onglets chargés puis imprimés à la suite, chacun sur une nouvelle page, toujours en thème clair, barres et légendes comprises même sans l'option « arrière-plans » du navigateur | ✅ | |

## 4. Incidents

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REP-10 | **MTTR** : moyenne en heures de `ResolvedAt − CreatedAt` des incidents résolus sur la période | ✅ | |
| REP-11 | **MTTD** : moyenne en heures de `CreatedAt − OccurredAt` des incidents déclarés sur la période qui ont un début constaté ([INC-27](processus/gestion-des-incidents.md)), avec le nombre d'incidents mesurés | ✅ | |
| REP-12 | **FCR** : part des incidents résolus au premier contact ([INC-23](processus/gestion-des-incidents.md)) parmi les résolus de la période | ✅ | |
| REP-13 | **Backlog par âge** des incidents ouverts : < 24 h, 1 à 7 jours, > 7 jours | ✅ | |
| REP-14 | **Burn-up** : cumul jour par jour des incidents créés et résolus depuis le début de la période ; répartition des créés par catégorie et par service | ✅ | |
| REP-15 | **Incidents à suivre** : ouverts majeurs, **SLA en risque** (moins de 25 % du délai restant, [INC-24](processus/gestion-des-incidents.md)) ou **SLA dépassé** ; majeurs d'abord, puis par échéance | ✅ | |
| REP-16 | **Prise en charge et réouvertures** : délai moyen en heures de `FirstResponseAt − CreatedAt` des incidents pris en charge sur la période ([INC-31](processus/gestion-des-incidents.md)) ; part des incidents résolus sur la période rouverts au moins une fois (INC-33) | ✅ | 2 |

## 5. Problèmes

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REP-20 | Problèmes ouverts et **erreurs connues** | ✅ | |
| REP-21 | **Incidents par problème** : nombre moyen d'incidents reliés | ✅ | |
| REP-22 | **RCA en attente** : problème En analyse dont une analyse a conclu, sans cause racine validée sur le problème | ✅ | |
| REP-23 | **Top 5 des services** qui génèrent le plus de problèmes, avec leurs incidents reliés | ✅ | |
| REP-24 | **Entonnoir** : Identifié (Nouveau), RCA en cours (En analyse), Contournement trouvé (Erreur connue sans correctif planifié), Correctif planifié (ouvert, avec une action ouverte ou un changement relié), Clôturé (Résolu ou Clos) | ✅ | |
| REP-25 | **Actions correctives** : ouvertes, en retard (avec leurs responsables R), terminées sur la période | ✅ | |
| REP-26 | **Qualité de la résolution** : part des problèmes Résolu ou Clos dont la cause racine est renseignée ([PRB-33](processus/gestion-des-problemes.md)) ; **récurrences** — problèmes résolus ou clos ayant reçu un incident sur la période, d'après l'alerte PRB-23, en alerte s'il y en a (PRB-35) ; **5 problèmes** aux incidents reliés les plus nombreux (PRB-34) | ✅ | 2, 3 |

## 6. Changements et projets

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REP-30 | **Taux de succès** des changements dont le résultat est renseigné, clos sur la période | ✅ | |
| REP-31 | Changements **par type** (Standard, Normal, Urgent) créés sur la période | ✅ | |
| REP-32 | **Avancement** des projets En cours ou Suspendus : points terminés, sinon phases ou étapes terminées, sinon livrables validés ; moyenne des projets mesurables | ✅ | |
| REP-33 | **Calendrier** des changements des 30 prochains jours (hors rejetés et clos), en Gantt | ✅ | |
| REP-34 | **Matrice 5 × 5** des risques ouverts des projets actifs (probabilité × impact) | ✅ | |
| REP-35 | **Effets des changements** : part des changements mis en œuvre sur la période reliés à un incident créé dans les 7 jours qui suivent ([CHG-33](processus/habilitation-des-changements.md)) ; délai moyen d'autorisation des Normal et Urgent autorisés sur la période (CHG-34) ; **respect des créneaux** — dates réelles comprises dans les dates planifiées, parmi les changements mis en œuvre aux dates réelles saisies (CHG-35) | ✅ | 2, 3 |

## 7. Conformité et métriques

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REP-40 | Dernière **évaluation NIS 2 validée** : score, maturité, score par pilier, écarts, actions d'amélioration reliées ([NIS-10 à NIS-12](processus/conformite-nis2.md)), calculés sur ses exigences figées ([NIS-18](processus/conformite-nis2.md)). Seule une évaluation Validée donne un score ; les évaluations non validées sont listées à part (référence, statut, lien vers la fiche), sans score | ✅ | |
| REP-41 | **Respect des SLA par service** (incidents résolus sur la période ayant une échéance) et **SLA à revoir** (En vigueur, date de revue passée) | ✅ | |
| REP-42 | **Amélioration continue** : registre par statut, améliorations réalisées sur la période | ✅ | |
| REP-43 | **Connaissances** : articles publiés, brouillons, revues en retard, publications de la période | ✅ | |
| REP-44 | **Déclarations réglementaires** : par régime, déclarations de la période (prise de connaissance), en cours, non requises, et respect des délais par étape — envois faits avant leur échéance / envois (DRG-20, [DRG](processus/declarations-reglementaires.md)) ; export CSV | ✅ | 2 |
| REP-45 | **Indicateurs des pratiques** : NIS 2 — écarts dotés d'une amélioration reliée (NIS-31) et évolution du score depuis l'évaluation validée précédente du même périmètre (NIS-30) ; **disponibilité** de la période des SLA En vigueur dotés d'une cible (SLM-21, SLM-31) ; amélioration — réalisées sur la période par source, délai moyen de la validation à la réalisation en jours, taux d'abandon (CSI-30 à CSI-32) ; connaissances — articles à jour, incidents résolus de la période reliés à un article, 10 articles les plus reliés à des incidents ou des demandes (KB-30 à KB-32) ; déclarations — délai moyen de la prise de connaissance à la qualification de l'incident, déclarées et non requises par régime (DRG-21, DRG-22) | ✅ | 2, 3 |

## 8. Configuration et inventaire

État de la CMDB **à date** (CI non retirés de mes périmètres) : la période ne règle que la
graphique de la découverte. Le sas de staging n'est montré qu'aux gestionnaires, sur les
périmètres qu'ils gèrent ([CFG-40](processus/gestion-de-la-configuration.md)).

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REP-50 | **Répartitions de la CMDB** : CI par type, par environnement, par statut ; applications par criticité (« Non renseignée » comprise) ([CFG-31](processus/gestion-de-la-configuration.md)) | ✅ | |
| REP-51 | **Couverture du portefeuille applicatif** : part des applications avec code, direction, propriétaire métier, responsable DSI et criticité ([CFG-35, CFG-43](processus/gestion-de-la-configuration.md)) | ✅ | |
| REP-52 | **Obsolescence** : actifs matériels par état de support, actifs en fin de support dépassée ou dans les 6 mois (25 au plus, du plus ancien au plus récent), avec la source de la date ([CFG-38, CFG-47](processus/gestion-de-la-configuration.md)) ; en alerte s'il y en a une dépassée | ✅ | |
| REP-53 | **Découverte** (gestionnaires) : actifs à rapprocher, en conflit, inconnus (Shadow IT, 25 derniers listés), rapprochés ; actifs par source ; nouveaux actifs et rapprochements par jour de la période ([CFG-36, CFG-37, CFG-42](processus/gestion-de-la-configuration.md)) | ✅ | |
| REP-54 | **Qualité de la CMDB** : exactitude — part des CI En service vérifiés depuis moins de 12 mois, avec le nombre de CI En service mesurés ([CFG-32](processus/gestion-de-la-configuration.md)) ; 10 CI reliés au plus d'incidents créés sur la période (CFG-33) ; CI sans propriétaire ni relation, nombre et 25 premiers (CFG-34) | ✅ | 2 |

## 9. Demandes

Demandes de mes périmètres, filtrées par service ; « Hors catalogue » regroupe les demandes
sans modèle ([gestion des demandes](processus/gestion-des-demandes.md), § 8).

| ID | Règle | Statut | Lot |
|---|---|---|---|
| REP-60 | **Indicateurs** : délai moyen de traitement en heures (`FulfilledAt − CreatedAt`, demandes satisfaites sur la période, REQ-40) ; **respect des délais** (satisfaites au plus tard à l'échéance / satisfaites ayant une échéance, REQ-41) ; **taux d'approbation** (approuvées / approuvées + rejetées, décisions de la période ; un modèle pré-approuvé ne compte pas ; une demande rejetée l'est à sa dernière modification, REQ-42) ; **satisfaction moyenne** sur 5 (avis de la période, REQ-43) ; demandes ouvertes et en retard | ✅ | 2 |
| REP-61 | **Par modèle du catalogue** : demandes créées, délai moyen, respect des délais, approbation, satisfaction | ✅ | 2 |
| REP-62 | **Avis de satisfaction** par note (1 à 5) | ✅ | 3 |
| REP-63 | **Demandes en retard** ([REQ-30](processus/gestion-des-demandes.md)) : 25 au plus, de l'échéance la plus ancienne à la plus récente, avec le lien vers la fiche ; export CSV de l'onglet | ✅ | 2 |

## 10. Limites connues

- Les **heures** des échéances SLA sont calendaires ; heures de service et suspension En
  attente viendront avec [INC-22](processus/gestion-des-incidents.md).
- Le service d'un incident est retrouvé **par son nom** dans le catalogue en attendant
  INC-20.
- Les indicateurs se calculent en mémoire sur l'ensemble des incidents et problèmes
  filtrés : adapté aux volumes d'une DSI, à revoir au-delà de quelques dizaines de
  milliers d'enregistrements.
