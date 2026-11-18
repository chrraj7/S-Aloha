# 2026-11-18 — Audit npm : exception datée pour picomatch, outillage de build seulement

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : l'étape « Dépendances vulnérables » du job Interface passe par
  `scripts/check-audit-npm.py`, qui lance `npm audit` deux fois (toutes dépendances, puis
  `--omit=dev`). Un avis haut ou critique sur le paquet livré bloque toujours ; sur
  l'outillage de build et de test, il bloque sauf exception datée, inscrite dans le script.
  Première exception : GHSA-fc5q-39xc-vjpf, jusqu'au 2026-12-18.
- **Pourquoi** : l'avis GHSA-fc5q-39xc-vjpf (picomatch, ReDoS, haute), mis à jour le
  2026-11-17, couvre `<= 4.0.7`, la dernière version publiée : aucune mise à jour ne le
  corrige.
  `npm audit --audit-level=high` a mis la CI de `main` au rouge sur cinq fusions de suite
  (#326 à #330) sans que leur code soit en cause, et le contrôle exigé « Interface (React) »
  bloquait toute PR (#331, #332). picomatch n'arrive que par vite et vitest :
  `npm audit --omit=dev` ne relève rien. Une `overrides` n'avait aucune version saine à
  cibler, et la seule correction proposée par npm est une rétrogradation majeure (vite 6.2,
  vitest 3.1). Analyse et preuve en #326.
- **Fichiers** : `scripts/check-audit-npm.py`, `.github/workflows/ci.yml`
- **Documentation** : `docs/reference/securite.md` (règle et table des exceptions),
  `docs/reference/architecture.md` (§ Intégration continue)
- **Commit** : #326 à #330
