# 2026-11-13 — REQ-13 : aucun envoi ne laisse une demande En cours sans responsable (#301)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `PUT /api/requests/{id}` refuse (400) de retirer le responsable d'une demande En
  cours sans en désigner un autre, et de contester une demande Satisfaite (retour En cours,
  REQ-10) sans responsable. La garde de REQ-13 porte sur le statut visé, plus sur la seule
  transition Approuvée → En cours.
- **Pourquoi** : la garde posée avec #227 ne jouait qu'à l'entrée depuis Approuvée ; un
  enregistrement de la fiche En cours, responsable vidé, répondait 200 et laissait l'état que
  REQ-13 interdit (revue des commits du 2026-11-11).
- **Vérifié** : `RequestContestTests.REQ_13_aucun_envoi_ne_laisse_une_demande_en_cours_sans_responsable`
  (retrait par le responsable et par un gestionnaire, remplacement permis, contestation sans
  responsable, demande restée sans responsable avant la règle encore modifiable), rouge sans la
  correction.
- **Fichiers** : `backend/Modules/ServiceRequestManagement/Controllers/ServiceRequestsController.cs`,
  `tests/SAloha.Api.Tests/RequestContestTests.cs`
- **Documentation** : spécification des demandes (REQ-13)
- **Commit** : correction de #301
