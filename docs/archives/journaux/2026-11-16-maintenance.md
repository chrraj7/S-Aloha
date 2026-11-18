# 2026-11-16 — Maintenance documentaire hebdomadaire

- **Quoi** : vérification de la zone 3 (frontend et routes contre `frontend/src`, semaine 3 du mois) ; la référence du frontend retrouve les routes et les modules réellement déclarés.
- **Pourquoi** : `docs/reference/frontend.md` omettait quatre routes de `frontend/src/App.jsx` (`/changes/models`, `/regulatory-notifications`, `/regulatory-notifications/:id`, `/admin/search`) et, dans l'arborescence, les modules `compliance-assessment` et `regulatory-notifications` ainsi que plusieurs pages du socle et de la Configuration.
- **Fichiers** : `docs/reference/frontend.md` (§ Structure, § Routes).
- **Documentation** : `docs/reference/frontend.md`. Preuves : `frontend/src/App.jsx:73-128` (routes), `frontend/src/modules/registry.js:63,108,141` (entrées de navigation), arborescence de `frontend/src/modules/` et `frontend/src/core/pages/`.
- **Commit** : maintenance documentaire du 2026-11-16 (zone 3)

## Plan d'action recalé

Issues citées par `docs/plan-action.md` : #2, #4, #8, #9, #31, #42, #172 (PR fusionnées ou fermées en octobre 2026), #120 (fermée le 2026-10-26), #121 (fermée le 2026-11-03), #122 (fermée le 2026-11-03), #123 (fermée le 2026-11-03), #124 (fermée le 2026-10-23), #125 (fermée le 2026-11-03), #126 (ouverte). Aucun passage à corriger : le plan présente déjà #126 comme ouverte et #120 à #125 comme faites. Aucun chantier à solder.

## Écart signalé, non corrigé

- `docs/reference/frontend.md` § Paramétrage (ligne `/changes/models`) réserve les modèles de changement standard à Manager et Admin ; le registre (`registry.js:63`) montre l'entrée à tous et `StandardChanges.jsx` ne teste aucun rôle côté interface (l'API reste juge). À trancher : documenter « gestion réservée » ou masquer l'entrée.
