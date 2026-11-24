# 2026-11-24 — Essai, trace et page du modèle de langage (F14 étape 4c, OPT-20, OPT-22, OPT-26, OPT-27)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : la fin de l'étape 4, qui solde OPT-20 à OPT-28.
  - **Essai** (OPT-26) : `POST /api/admin/settings/language-model/test` (Admin) remet les réglages
    enregistrés, même désactivés, au conteneur (`POST /language-model/test`), la CMDB assistée
    coupée comprise. L'essai suit le chemin d'une tâche : pseudonymisation, appel, schéma ; le
    modèle doit recopier le jeton d'un nom d'hôte, rétabli ensuite. Résultat sans secret,
    comme ADM-63 : modèle joignable et durée, réponse non conforme et motif, délai ou erreur ;
    conteneur injoignable dit tel quel.
  - **Trace** (OPT-27) : table `LanguageModelCalls` (fonctionnalité, tâche, modèle, emplacement,
    version des consignes, empreinte SHA-256 de l'entrée pseudonymisée, durée, issue, essais,
    réponses écartées, motif), sans l'invite ni la réponse. Le conteneur la remet après chaque
    appel d'une fonctionnalité (`POST /internal/language-model/calls`, CMDB assistée activée) ;
    l'API garde celle de l'essai. Purge quotidienne après 90 jours
    (`Features:LanguageModelCallRetentionDays`). Compteurs par fonctionnalité et issue :
    `GET …/language-model/usage?days=`.
  - **Page** : carte « Modèle de langage » sous Administration › Fonctionnalités optionnelles,
    réglable et essayée avant d'activer la CMDB assistée. Passer à « hébergé » demande une case
    de confirmation ; l'avertissement « les données envoyées au modèle quittent le SI » et le
    rappel du registre des traitements restent ensuite affichés (OPT-22). Clé jamais réaffichée,
    gardée sans saisie, effaçable (OPT-23). Compteurs des 30 derniers jours.
  - **Appel d'essai** : `FeaturesClient` gagne un appel qu'un écran attend sans fonctionnalité
    activée, au délai du modèle (deux essais, plus 10 s) ; son client HTTP ne borne plus le
    délai, chaque appel le borne.
- **Pourquoi** : sous-partie 4c de l'étape 4 du chantier F14 (#366), après les réglages (4a,
  #392) et le connecteur (4b, #393). La CMDB assistée (étape 5) s'appuie sur ce modèle.
- **Preuve** : `LanguageModelEssayTests` (2 tests d'API : essai désactivé, échec motivé sans
  secret, conteneur absent, Admin seul ; traces reçues, refusées hors bornes ou coupées,
  compteurs, purge à 90 jours), 2 tests du conteneur (trace remise sans l'invite ni la réponse,
  essai par le chemin d'une tâche), 3 tests de la page (réglages et clé, confirmation de
  l'hébergé et avertissement permanent, essai et compteurs). Ils échouent sur `main`.
- **Fichiers** : `backend/Core/Features/LanguageModel.cs`, `FeaturesClient.cs`,
  `FeatureEvents.cs`, `backend/Core/Data/AppDbContext.cs`, `backend/Program.cs`, migration
  `LanguageModelCalls` ; `features/SAloha.Features/LanguageModel/LanguageModelEssay.cs`,
  `LanguageModelClient.cs`, `Host/ApiClient.cs`, `Host/ContainerHost.cs` ;
  `frontend/src/core/pages/admin/LanguageModelSettings.jsx`, `OptionalFeatures.jsx`,
  `PlatformSettings.jsx`, `frontend/src/api.js`
- **Documentation** : fonctionnalités optionnelles (OPT-20, OPT-22, OPT-26, OPT-27 ✅ ; § 5,
  § 6, § 7), administration, base de données, exploitation, architecture, plan d'action
- **Commit** : #366
