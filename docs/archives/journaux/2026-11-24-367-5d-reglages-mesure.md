# 2026-11-24 — Réglages et mesure de la CMDB assistée (F14 étape 5d, CMA-10, CMA-12)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : les réglages de l'Admin et la mesure, sur le modèle du tri assisté (TRI-10, TRI-13).
  - **Réglages** (CMA-12) : `{ tasks, maxItems, pairsPerRequest, stagingHelp }` dans la colonne
    `OptionalFeatures.SettingsJson` (sans migration) ; par défaut toutes les tâches, 500 CI,
    50 paires, aide au sas activée ; bornes : de 20 à 500 CI, de 10 à 50 paires.
    `/api/admin/features/{key}/settings` sert désormais les deux fonctionnalités qui ont des
    réglages (`IFeatureSettings` : bornes et champs tracés), chaque réglage changé tracé au journal.
  - **Effets** : une tâche retirée ne se lance plus (400) et le conteneur ne la traite plus,
    même lancée avant ; `AssistedCmdb:MaxItems` reste le plafond de l'exploitation, la limite
    de l'Admin vaut en dessous ; Fusionner va par requêtes de la taille réglée ; l'aide au sas
    coupée, la route interne du sas ne remet plus d'actif et l'option de la tâche de découverte
    est masquée. Les gestionnaires lisent `GET /api/assisted-cmdb/settings`.
  - **Mesure** (CMA-10) : `GET /api/admin/assisted-cmdb/metrics`, par tâche (les six, et l'aide
    au sas), sur 30 et 90 jours : parts acceptées, corrigées, rejetées, expirées (les ouvertes
    en attente, hors des parts) ; réponses non conformes d'après les traces (OPT-27 : réponses
    écartées, plus une conforme par appel valide), appels en échec, durée moyenne des appels ;
    analyses terminées, durée moyenne et maximale, et en échec.
  - **Interface** : carte de la CMDB assistée dans Administration › Fonctionnalités
    optionnelles (réglages, mesure, tâche surtout rejetée signalée) ; Configuration › CMDB
    assistée n'offre que les tâches proposées et affiche la limite réglée.
- **Pourquoi** : 5d de l'étape 5 du chantier F14 (#367) ; CMA-10 et CMA-12 ✅. Reste CMA-11
  (jeu d'essai mesuré sur un modèle exécutable dans le SI), qui fixera les tâches coupées par
  défaut, puis la mise en avant de la CMDB assistée. Le tableau des comptes de règles
  (pratiques) est mis à jour pour les étapes 4 à 6 (OPT, CMA, MCP).
- **Preuve** : `AssistedCmdbSettingsTests` (défauts, modification tracée, bornes, lecture des
  gestionnaires et du conteneur, tâche retirée refusée, limite réglée, aide au sas coupée),
  `AssistedCmdbMetricsTests` (parts par tâche sur 30 et 90 jours, réponses non conformes,
  durée des analyses, 403, 404 coupée), `FeatureSettingsTests`, `AssistedCmdbSettingsTests` du
  conteneur (tâche retirée sautée, paires par requête), `AssistedCmdbSettings.test.jsx`,
  `AssistedCmdb.test.jsx`, `DiscoverySettings.test.jsx`.
- **Fichiers** : `backend/Core/Features/FeatureSettings.cs`,
  `backend/Modules/ServiceConfigurationManagement/{AssistedCmdb,AssistedCmdbMetrics,AssistedStaging}.cs`,
  `features/SAloha.Features/AssistedCmdb/{AssistedCmdb,Duplicates}.cs`,
  `features/SAloha.Features/Host/ApiClient.cs`, `frontend/src/api.js`,
  `frontend/src/core/pages/admin/{AssistedCmdbSettings,OptionalFeatures}.jsx`,
  `frontend/src/modules/service-configuration-management/pages/{AssistedCmdb,DiscoverySettings}.jsx`
- **Documentation** : CMDB assistée (§ 2 à § 5 ; CMA-10, CMA-12 ✅), fonctionnalités
  optionnelles (§ 5, § 6, OPT-13), exploitation, interface, pratiques (comptes), plan d'action,
  note de version
- **Commit** : #367
