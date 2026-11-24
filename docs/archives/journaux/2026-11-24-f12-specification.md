# 2026-11-24 — F12 spécifié : évaluations sur référentiels importés (REF-01 à REF-15, ADR-0025)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : spécification de la pratique « Évaluations sur référentiels importés », au
  gabarit des pratiques, 15 règles en lot 3 : import CSV par l'Admin avec association des
  colonnes et aperçu (REF-01), contrôles tout ou rien (REF-02), fiche et attestation du droit
  d'usage (REF-03), échelle pondérée de 2 à 6 valeurs (REF-04), statuts Brouillon, Publié,
  Retiré (REF-05), versions rapprochées par identifiant (REF-06), évaluations dans le registre
  et le cycle de vie de NIS 2, version figée (REF-07, REF-08), score (REF-09), niveau atteint
  par la règle cumulative des modèles de maturité et progression du niveau suivant (REF-10),
  écarts et actions d'amélioration (REF-11), comparaison (REF-12), export (REF-13),
  consultation (REF-14), reporting (REF-15). ADR-0025, **proposé** : rien du référentiel
  dans le dépôt, format, échelle et niveaux réglés à l'import.
- **Pourquoi** : F12 était « à spécifier d'abord (format d'import, échelle, calcul des
  niveaux) » ; spécification demandée par le mainteneur le 2026-11-24.
- **Preuve** : exemples chiffrés des scénarios recalculés (REF-09 : 50,0 % ; REF-10 :
  niveau 1, progression 50 %, score 87,5 %) ; `scripts/check-docs.py` sans problème.
- **Fichiers** : —
- **Documentation** : `docs/reference/processus/evaluations-referentiels-importes.md` (nouveau),
  `docs/decisions/adr-0025-referentiels-de-conformite-importes.md` (nouveau),
  `docs/decisions/readme.md`, `docs/reference/regles-metier.md` (préfixe REF),
  `docs/reference/processus/readme.md` (synthèse : 15 🔜 en lot 3), `docs/plan-action.md`
  (F12 spécifié, prochaine séance)
- **Soldé du plan d'action** : la spécification de F12 ; restent la confirmation des points de
  l'ADR-0025 par le mainteneur, puis l'implémentation.
- **Commit** : F12
