# 2026-11-13 — INC-16 : les enfants résolus ou clos suivent le parent déplacé (#293)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : un parent ne change plus de périmètre tant qu'il a des enfants **ouverts** dans
  un autre périmètre que celui visé (400, message corrigé) ; ses enfants résolus ou clos le
  suivent dans le même enregistrement, toujours rattachés, avec leurs déclarations
  réglementaires et leurs notifications. Le journal d'audit trace le changement de périmètre
  de chaque enfant.
- **Pourquoi** : un enfant résolu ne se détache plus (INC-16) et ne se déplace pas seul
  (PER-07) : un parent ouvert dans le mauvais périmètre ne pouvait plus être déplacé, pas même
  par un administrateur. Décision du mainteneur du 2026-11-10 (option A).
- **Vérifié** : `PerimeterRightsTests.PER_07_INC_16_un_enfant_termine_suit_le_parent_deplace_un_enfant_ouvert_le_retient`
  (enfant résolu qui suit avec sa déclaration, ses notifications et sa ligne d'historique ;
  enfant ouvert qui retient, puis reste dans son périmètre une fois détaché) et
  `PER_07_INC_16_un_enfant_termine_d_un_troisieme_perimetre_retient_le_parent`, rouges sans la
  correction pour le premier ; `PER_07_INC_16_un_parent_ne_change_pas_de_perimetre_tant_qu_il_a_des_enfants_rattaches`
  reste le témoin de l'enfant ouvert.
- **Troisième périmètre** : un enfant terminé d'un troisième périmètre (lien hérité d'avant
  PER-07) retient le parent (400), comme avant. Décision du mainteneur du 2026-11-18 sur la PR
  (option 2 : garder le blocage) ; le test attend ce refus, et le parent se déplace vers le
  périmètre de cet enfant.
- **Fichiers** : `backend/Modules/IncidentManagement/Controllers/IncidentsController.cs`,
  `tests/SAloha.Api.Tests/PerimeterRightsTests.cs`
- **Documentation** : spécification de la gestion des incidents (INC-16, scénario), périmètres
  (PER-07)
- **Commit** : correction de #293
