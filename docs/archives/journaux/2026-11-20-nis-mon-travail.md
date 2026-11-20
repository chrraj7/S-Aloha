# 2026-11-20 — Évaluations NIS 2 ouvertes dans Mon travail

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : « Mes dossiers ouverts » (Mon travail) de la console liste aussi les évaluations
  NIS 2 dont l'utilisateur est le responsable, directement ou par un groupe, tant qu'elles ne
  sont pas validées. Test d'API : le pilote voit son évaluation en Brouillon, pas celle qu'il a
  validée, et un autre compte ne voit ni l'une ni l'autre.
- **Pourquoi** : la console de la conformité NIS 2 annonçait ce bloc « à venir » (lot 2), alors
  que toutes les autres pratiques y figurent déjà. Au passage, la mention « à venir » du texte
  du référentiel incomplet (NIS-23) est corrigée : la console le signale à l'Admin depuis
  NIS-23 (`ConsoleController.cs`, `ReferentialWithoutText`).
- **Fichiers** : `backend/Core/Pilotage/ConsoleController.cs`,
  `tests/SAloha.Api.Tests/ComplianceExitMvpTests.cs`
- **Documentation** : conformité NIS 2 (§ 8, console)
- **Commit** : console NIS 2, Mon travail
