# 2026-11-18 — CHG-15, CHG-25 : quitter le type Urgent ne contourne plus la revue ni le gel (#323)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `ChangesController.CheckStatusAsync` lit le type **d'avant l'envoi**. Un
  changement Mis en œuvre ou Clos de type Urgent ne change plus de type (400), en un envoi
  comme en deux. Un changement planifié qui quitte Urgent repasse le contrôle du gel, comme
  un déplacement (CHG-25).
- **Pourquoi** : les deux règles lisaient le type envoyé. Le responsable clôturait sans revue un
  changement Urgent en le disant Normal. Un gestionnaire laissait un changement Normal autorisé
  à son nom dans une période de gel.
- **Vérifié** : `ChangeLifecycleTests.CHG_15_un_changement_urgent_mis_en_oeuvre_ne_quitte_pas_Urgent_pour_eviter_la_revue`
  (un envoi, deux envois, un gestionnaire ; témoin : Urgent, la revue est exigée puis la
  clôture passe) et `ChangeGovernanceTests.CHG_25_un_changement_urgent_planifie_dans_le_gel_n_y_reste_pas_en_devenant_Normal`
  (témoin : resté Urgent, le même envoi passe). Les deux sont rouges sans la correction.
- **Fichiers** : `backend/Modules/ChangeEnablement/Controllers/ChangesController.cs`,
  `tests/SAloha.Api.Tests/ChangeLifecycleTests.cs`, `tests/SAloha.Api.Tests/ChangeGovernanceTests.cs`
- **Documentation** : habilitation des changements (CHG-15, CHG-25 et leurs scénarios)
- **Commit** : correction de #323
