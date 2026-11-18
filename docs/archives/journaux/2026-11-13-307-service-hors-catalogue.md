# 2026-11-13 — REQ-04 : la fiche enregistrée telle quelle garde un service hors catalogue (#307)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `PUT /api/requests/{id}` garde le libellé de service enregistré quand le corps ne
  porte ni `serviceId` ni `affectedService` et que la demande n'a pas de service du catalogue.
  Un libellé envoyé (même vide) reste une modification ; un service du catalogue retiré
  (`serviceId` absent) emporte toujours son libellé.
- **Pourquoi** : la fiche d'une demande n'envoie que `serviceId`. Sur une demande au service
  hors catalogue (SLM-10 : « simple libellé »), l'API effaçait le libellé à chaque
  enregistrement ; depuis la garde de REQ-04 sur le service, la demande Approuvée repartait à
  Soumise sans que rien ait été touché, et la contestation d'une demande Satisfaite (REQ-10)
  était refusée (revue du 2026-11-13).
- **Vérifié** : `RequestReworkTests.REQ_04_la_fiche_enregistree_sans_rien_toucher_garde_un_service_hors_catalogue`
  (corps de la fiche : approbation gardée, contestation acceptée, libellé vidé exprès → Soumise),
  rouge sans la correction ; `REQ_04_la_fiche_retire_encore_un_service_du_catalogue` pour le
  cas voisin.
- **Reste** : la fiche n'affiche pas le libellé d'un service hors catalogue ; il ne se lit et
  ne se retire que par l'API.
- **Fichiers** : `backend/Modules/ServiceRequestManagement/Controllers/ServiceRequestsController.cs`,
  `tests/SAloha.Api.Tests/RequestReworkTests.cs`
- **Documentation** : aucune règle ne change (REQ-04, REQ-10, SLM-10 tenues comme écrites)
- **Commit** : correction de #307
