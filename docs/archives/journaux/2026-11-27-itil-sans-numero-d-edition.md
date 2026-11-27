# 2026-11-27 — ITIL cité sans numéro d'édition

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : plus aucun numéro d'édition d'ITIL dans le dépôt : le numéro qui suivait « ITIL »
  est retiré (98 occurrences, 52 fichiers) dans le README et sa partie anglaise, la référence, les
  spécifications des pratiques, le glossaire, le plan d'action, les ADR, les archives, le
  journal des versions (sections publiées comprises), l'accueil du wiki, le modèle d'issue
  « Évolution », les commentaires du code et des tests. Les passages où l'édition portait le
  sens (ADR-0003, ADR-0007, constat initial, journal du 2026-10-13, section 0.1.0 du journal des
  versions) parlent d'une « ancienne » et d'une « nouvelle » édition ; l'ADR-0007 porte sa
  révision datée. Le nom de fichier de l'ADR-0007 reste inchangé, pour ne casser aucun lien
  publié. Image de partage `partage-1280x640.png` refaite : « Plateforme ITIL libre, chez vous ».
- **Pourquoi** : décision du mainteneur, en séance : S-Aloha suit les pratiques ITIL publiées,
  sans se lier à une édition ; une nouvelle édition est en cours de publication. Même logique
  que les sous-titres des pages, sans numéro depuis le 2026-10-13. Exception assumée au
  principe des archives et des ADR figés, sur décision explicite.
- **Preuve** : `git grep -iE "itil[ ®™-]*(v|version|\(version)? ?[0-9]"` ne trouve plus que le
  nom de fichier de l'ADR-0007 ; recherche des retours à la ligne entre « ITIL » et le numéro :
  aucun ; image de partage vérifiée à l'œil ; wiki généré à blanc (54 pages, aucun numéro) ;
  `scripts/check-docs.py` et `scripts/changelog.py` sans problème.
- **Fichiers** : `.github/ISSUE_TEMPLATE/evolution.yml`, `backend/Modules/*/Models/Entities.cs`,
  `frontend/src/modules/*/config.jsx`, `tests/SAloha.Api.Tests/ItilModulesTests.cs`,
  `MigrationTests.cs`, `scripts/sync-wiki.py`, `docs/screenshots/partage-1280x640.png`
- **Documentation** : `README.md`, `CHANGELOG.md`, `docs/reference/` (produit, architecture,
  glossaire, règles métier, base de données, tri assisté, `processus/`), `docs/decisions/`
  (ADR-0003, ADR-0007, ADR-0008, ADR-0015, index), `docs/archives/` (constat initial, journaux
  des 2026-10-06, 07, 13, 19 et 24)
- **Reste au mainteneur** : téléverser l'image de partage comme aperçu social ; retoucher les
  notes de la release v1.0.0, qui citent encore l'édition.
- **Commit** : ITIL sans numéro d'édition
