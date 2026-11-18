# 2026-11-17 — PER-08 : la synthèse NIS 2 et le service dégradé masquent la cible d'un autre périmètre

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : l'action d'amélioration liée à un écart (`GET /api/assessments/{id}/score` et
  `/export`, refus 409 de `POST /api/assessments/{id}/improvements`) et le CI en cause d'un
  service dégradé (`GET /api/services/{id}/degradation`) suivent PER-08 : une cible dans un
  périmètre que l'appelant ne voit pas compte (écart traité, service dégradé) sans être
  décrite (`hidden`, sans identifiant ni référence). La synthèse affiche « Action d'un autre
  périmètre » à la place du lien et du bouton de création ; le bandeau « Service dégradé »
  cite « CI d'un autre périmètre » sans lien.
- **Pourquoi** : PER-08 demande qu'un lien ne décrive pas l'enregistrement d'un autre
  périmètre ; ces deux lectures passent par les liens et doivent masquer comme la liste des
  liens, l'impact d'un CI et les conflits de changements.
- **Fichiers** : `backend/Modules/ComplianceAssessment/Controllers/AssessmentsController.cs`,
  `backend/Modules/ServiceLevelManagement/Controllers/ServicesController.cs`,
  `frontend/src/modules/compliance-assessment/components/Synthesis.jsx`,
  `frontend/src/modules/service-level-management/pages/ServiceDetail.jsx`,
  `tests/SAloha.Api.Tests/PerimeterViewsTests.cs`
- **Documentation** : périmètres (PER-08), conformité NIS 2 (NIS-15, route de la synthèse)
- **Commit** : PER-08, synthèse NIS 2 et service dégradé
