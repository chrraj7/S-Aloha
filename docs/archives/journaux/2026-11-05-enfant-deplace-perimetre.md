# 2026-11-05 — PER-07 : un incident enfant ne quitte pas le périmètre de son parent

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `PUT /api/incidents/{id}` refuse (400) de changer le périmètre d'un incident
  rattaché à un parent d'un autre périmètre ; le message invite à le détacher d'abord
  (`PUT /api/incidents/{id}/parent`, référence vide). Le contrôle passe par un nouveau
  point d'extension `CheckMoveAsync` de `RecordController`, appelé après la vérification des
  droits de PER-07 et avant toute modification.
- **Pourquoi** : décision du mainteneur du 2026-11-04 (option « refuser le déplacement ») :
  aucun lien parent / enfant entre deux périmètres, et rien ne change en silence.
- **Fichiers** : `backend/Core/Records/RecordController.cs`,
  `backend/Modules/IncidentManagement/Controllers/IncidentsController.cs`,
  `tests/SAloha.Api.Tests/PerimeterRightsTests.cs`
- **Documentation** : référence des périmètres (PER-07), spécification des incidents (INC-16)
- **Commit** : PER-07, déplacement d'un incident enfant
