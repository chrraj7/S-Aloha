# 2026-11-16 — SLM-10 : la fiche d'un incident enregistrée telle quelle garde un service hors catalogue (#310)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `PUT /api/incidents/{id}` garde le libellé de service enregistré quand le corps ne
  porte ni `serviceId` ni `affectedService` et que l'incident n'a pas de service du catalogue.
  Un libellé envoyé (même vide) le remplace toujours ; un service du catalogue retiré
  (`serviceId` absent) emporte toujours son nom recopié.
- **Pourquoi** : la fiche d'un incident n'envoie que `serviceId`. Sur un incident au service
  hors catalogue (SLM-10 : « simple libellé » — client de l'API, données antérieures, demande
  requalifiée par REQ-12), l'API effaçait le libellé à chaque enregistrement : il disparaissait
  de la fiche, de la liste et du reporting (revue du 2026-11-14). Même défaut que #307 pour les
  demandes, même garde.
- **Vérifié** : `ServiceLevelExitMvpTests.SLM_10_INC_20_la_fiche_d_un_incident_enregistree_telle_quelle_garde_un_service_hors_catalogue`
  (corps de la fiche : libellé gardé ; service du catalogue choisi puis retiré : l'ancien
  libellé ne revient pas ; libellé vidé exprès : retiré), rouge sans la correction.
- **Reste** : la fiche n'affiche pas le libellé d'un service hors catalogue ; il ne se lit que
  dans la liste et ne se retire que par l'API.
- **Fichiers** : `backend/Modules/IncidentManagement/Controllers/IncidentsController.cs`,
  `tests/SAloha.Api.Tests/ServiceLevelExitMvpTests.cs`
- **Documentation** : aucune règle ne change (SLM-10, INC-20 tenues comme écrites)
- **Commit** : correction de #310
