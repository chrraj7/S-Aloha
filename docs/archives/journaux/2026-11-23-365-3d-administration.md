# 2026-11-23 — Page d'administration du tri et suggestions appliquées envoyées (F14 étape 3d, TRI-09, TRI-10, TRI-13)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** :
  - **Page d'administration** : dans Administration › Fonctionnalités optionnelles, le tri
    activé montre ses réglages (TRI-13). Chaque nombre a ses bornes, chaque suggestion est
    activable ; ils s'enregistrent par `PUT /api/admin/features/{clé}/settings` (#386), le
    refus de l'API affiché. La page montre aussi la mesure (TRI-10, #388) : par champ, sur 30
    ou 90 jours, le nombre de décisions et les parts acceptées, corrigées, rejetées et
    laissées, et les suggestions en attente. Un champ surtout corrigé, encore activé, est
    signalé : le couper se décide à côté. Un appel d'API absent ou en échec n'emporte pas la
    page.
  - **Saisie** (TRI-09) : le formulaire de création retient les suggestions appliquées et les
    envoie avec la création, dans l'en-tête `X-S-Aloha-Suggestions` (`createOptions` de la
    pratique). L'API les compte et les trace (#388).
- **Pourquoi** : dernière sous-partie de 3d, chantier F14 (#365). Avec elle, TRI-09, TRI-10 et
  TRI-13 sont faits ; il ne reste au tri que TRI-12, le calibrage, et l'état de l'index des
  incidents ouverts sur la page (3e).
- **Preuve** : `TriageSettings.test.jsx` (3 tests : lecture, bornes, modification et
  enregistrement, suggestion coupée ; refus de l'API affiché ; mesure sur 30 et 90 jours, champ
  surtout corrigé signalé). Dans `TriageSuggestions.test.jsx`, les suggestions appliquées sont
  retenues et l'en-tête se construit. Ils échouent sur la base (Preuve, interface). Suite de
  l'interface : 213 ✔ ; build ✔.
- **Fichiers** : `frontend/src/core/pages/admin/TriageSettings.jsx`, `OptionalFeatures.jsx`,
  `frontend/src/modules/incident-management/` (`TriageSuggestions.jsx`, `config.jsx`),
  `frontend/src/core/components/RecordList.jsx`, `frontend/src/api.js`
- **Documentation** : tri assisté (TRI-09, TRI-10, TRI-13 ✅, notes de statut à jour, § 4),
  fonctionnalités optionnelles (§ 5), référence de l'interface, synthèse des pratiques
  (381 ✅), note `changelog.d/365-3d.md`
- **Commit** : #365
