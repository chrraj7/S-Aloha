# 2026-11-19 — Changement créé depuis la fiche d'un problème (CHG-23)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : l'onglet Informations d'un problème offre « Créer un changement » : le
  changement (Normal, risque Moyen) reprend le titre et la description du problème, cite sa
  référence, naît dans le périmètre du problème et lui est relié ; la fiche du changement
  s'ouvre ensuite. Deux tests d'interface : le changement créé et relié, et un refus de l'API
  affiché sans lien créé. Le parcours a été rejoué contre l'API (un User crée le changement
  dans le périmètre du problème, le lien se lit des deux côtés) par un test jetable, hors du
  dépôt.
- **Pourquoi** : CHG-23 prévoit la création depuis un problème ou une action corrective ; seule
  l'action (PRB-27) la proposait, le problème n'avait que le lien manuel.
- **Fichiers** : `frontend/src/modules/problem-management/pages/ProblemDetail.jsx`,
  `frontend/src/modules/problem-management/pages/ProblemChange.test.jsx`
- **Documentation** : habilitation des changements (CHG-23, liens), gestion des problèmes
  (droits, liens), README (Problèmes)
- **Commit** : CHG-23
