# 2026-11-24 — Audit des dépendances : exception échue retirée (GHSA-fc5q-39xc-vjpf)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `scripts/check-audit-npm.py` n'a plus d'exception ; le tableau des exceptions
  de `docs/reference/securite.md` est remplacé par la mention qu'aucune n'est en cours.
- **Pourquoi** : l'avis GHSA-fc5q-39xc-vjpf (picomatch ≤ 4.0.7) a été retiré par GitHub le
  2026-11-18 ; `npm audit` ne le relève plus, et le script signalait l'exception « sans
  objet (avis plus relevé) : la retirer ». Relevé par l'audit du 2026-11-21 (#345, point
  voisin).
- **Preuve** : `gh api /advisories/GHSA-fc5q-39xc-vjpf` donne `withdrawn_at`
  2026-11-18T15:38:56Z ; `python3 scripts/check-audit-npm.py` : 0 avis haut ou critique,
  0 problème, sans avertissement.
- **Fichiers** : `scripts/check-audit-npm.py`, `docs/reference/securite.md`,
  `changelog.d/audit-npm-exception-datee.md` (la note en attente ne cite plus l'exception)
- **Documentation** : le mécanisme des exceptions datées reste décrit tel quel.
- **Commit** : point voisin de #345
