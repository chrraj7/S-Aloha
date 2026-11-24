# 2026-11-18 — PER-08 : les preuves d'une évaluation NIS 2 masquent la cible d'un autre périmètre

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : les preuves d'une réponse (`GET /api/assessments/{id}/questionnaire` et
  `/export`) suivent PER-08. La copie recopiée à l'ajout (type, identifiant, référence, titre)
  n'est servie que si le lecteur voit la cible dans son périmètre **actuel**. Sinon, la preuve
  compte toujours (`hidden`, sans type, identifiant ni référence, titre « Enregistrement d'un
  autre périmètre »), l'export écrit ce libellé, et `DELETE …/evidence/{id}` répond 404, comme
  pour un lien masqué. Le questionnaire l'affiche sans lien ni bouton « Retirer ». Une cible
  supprimée garde sa copie, comme avant.
- **Pourquoi** : l'ajout vérifiait le droit de **l'auteur**, mais la copie n'était jamais
  relue contre le périmètre du lecteur, ni contre celui de la cible quand elle est déplacée
  (PER-07).
- **Vérifié** : `PerimeterViewsTests.PER_08_NIS_08_une_preuve_d_un_autre_perimetre_est_masquee_au_questionnaire_et_a_l_export`.
  Il couvre une cible d'un autre périmètre et une cible déplacée ensuite. Le témoin est la
  preuve du Siège, toujours décrite. L'Admin et un utilisateur des deux périmètres lisent tout,
  et ce dernier retire sa preuve. Le test est rouge sans la correction. Le test d'interface
  `Assessment.test.jsx` vérifie qu'une preuve masquée s'affiche sans lien ni bouton.
- **Fichiers** : `backend/Modules/ComplianceAssessment/Controllers/AssessmentsController.cs`,
  `frontend/src/modules/compliance-assessment/components/Questionnaire.jsx`,
  `frontend/src/modules/compliance-assessment/components/Assessment.test.jsx`,
  `tests/SAloha.Api.Tests/PerimeterViewsTests.cs`
- **Documentation** : conformité NIS 2 (NIS-08, route des preuves), périmètres (PER-08)
- **Commit** : PER-08, preuves d'une évaluation NIS 2
