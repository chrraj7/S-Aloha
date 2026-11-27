# 2026-11-27 — Preuve lit les étiquettes au moment du job

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : le job **Preuve** de la CI lit les étiquettes de la PR par l'API au moment où il
  tourne (`gh api …/issues/<n>/labels`, jeton en lecture : `issues` et `pull-requests`), et non
  plus dans l'événement `pull_request`. Il les affiche dans son journal.
- **Pourquoi** : la consigne « poser `preuve-non-requise`, puis relancer ce job » ne marchait
  pas : une relance rejoue l'événement d'origine, sans l'étiquette posée depuis. Constaté sur
  #436 : relancé après la pose, Preuve a lu une liste vide et échoué ; il a fallu fermer puis
  rouvrir la PR pour relancer toute la CI. Garde-fous n'est pas concerné : il se relance seul
  sur `labeled`.
- **Preuve** : actionlint 1.7.12 et zizmor 1.31.1 (versions de la CI) sans remarque ; la
  commande de lecture rend `outillage,preuve-non-requise,risque-sensible` pour #436, et
  `scripts/preuve.py` l'accepte ; le contrôle exigé garde son nom, `Preuve`.
- **Fichiers** : `.github/workflows/ci.yml`
- **Documentation** : `CONTRIBUTING.md` (§ Preuve et garde-fous), `docs/reference/architecture.md`
  (§ Intégration continue)
- **Commit** : Preuve lit les étiquettes actuelles
