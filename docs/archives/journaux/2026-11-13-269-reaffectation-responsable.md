# 2026-11-13 — Réaffectation d'un enregistrement engagé : le responsable en place ou un gestionnaire (#269)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `PUT /api/changes/{id}`, `/api/requests/{id}` et `/api/improvements/{id}` refusent
  (400) qu'un tiers change le responsable d'un changement Autorisé, Planifié ou Mis en œuvre,
  d'une demande En cours, ou le porteur d'une amélioration Validée ou En cours. Le responsable
  d'avant l'envoi, un membre du groupe responsable et un gestionnaire du périmètre réaffectent.
  Sans responsable, la première désignation revient à un gestionnaire ; le reste de la fiche se
  modifie comme avant.
- **Pourquoi** : décision du mainteneur du 2026-11-09 (option A du dossier de #269). Les
  transitions réservées au responsable (CHG-14, REQ-25, CSI-11) regardaient le responsable
  d'avant l'envoi, mais un tiers se désignait d'un premier envoi puis transitait au second.
  Les § 2 des trois pratiques disaient encore ces gestes ouverts à tout User, et CHG-07 que le
  responsable « se modifie librement ».
- **Vérifié** : `OwnerReassignmentTests`, un test par pratique (tiers refusé, y compris vers un
  groupe ou en retirant le responsable ; responsable, membre du groupe et gestionnaire
  acceptés ; avant l'engagement, le responsable se modifie librement), rouges sans la garde.
- **Sans responsable** : le cas n'était pas dans le dossier de #269. Décision tacite du
  2026-11-17 (dossier publié sur la pull request le 2026-11-13) : seul un gestionnaire fait la
  première désignation d'un enregistrement engagé, sinon le détour en deux envois restait
  ouvert sur tout enregistrement sans titulaire. Le responsable d'un changement standard, né
  autorisé, se désigne donc à la création ; `ItilModulesTests` le fait désormais, et désigne le
  responsable d'un changement normal avant son autorisation.
- **Fichiers** : `backend/Core/Records/RecordController.cs` (`ReassignedByThirdParty`),
  `ChangesController.cs`, `ServiceRequestsController.cs`, `ImprovementsController.cs`,
  `tests/SAloha.Api.Tests/OwnerReassignmentTests.cs`, `ItilModulesTests.cs`
- **Documentation** : spécifications des changements (§ 2, CHG-07), des demandes (§ 2, REQ-25)
  et de l'amélioration continue (§ 2, CSI-11)
- **Commit** : correction de #269
