# 2026-11-27 — Menu Configuration : sas et CMDB assistée selon le rôle par périmètre

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : les sous-entrées « Sas de staging » et « CMDB assistée » du module Configuration
  suivent le rôle de gestionnaire d'un périmètre (`useManagesPerimeter`), comme la section
  Paramétrage, et non plus le rôle AD.
- **Pourquoi** : l'API sert ces écrans aux gestionnaires d'un périmètre (ADR-0014 D4, PER-03).
  Un compte User AD gestionnaire d'un périmètre ne les voyait pas dans le menu ; un compte
  Manager AD simple utilisateur de ses périmètres les voyait et recevait 403.
- **Preuve** : `Layout.perimeter.test.jsx` (registre réel, `perimeters.mine` simulé) échoue sur
  `main` pour les deux cas du défaut et passe après correction ; témoins Manager partout, Admin
  AD et User partout ; suite Vitest complète verte.
- **Fichiers** : `frontend/src/core/components/Layout.jsx`,
  `frontend/src/core/components/Layout.perimeter.test.jsx`
- **Documentation** : `docs/reference/frontend.md` (§ Registre, `roles`)
- **Commit** : correction de #439
