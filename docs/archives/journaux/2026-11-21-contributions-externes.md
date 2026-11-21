# 2026-11-21 — Les pull requests venues d'un fork entrent dans le circuit

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : le workflow `contributions.yml` pose sur une PR de fork, depuis la branche par
  défaut, `contribution-externe`, sa classe de risque et `a-verifier`. Cette dernière est
  reposée à chaque poussée, qui retire `verifiee`, `contre-verifiee` et `a-corriger`. À la
  fusion, il remercie l'auteur et l'invite à mettre une étoile au dépôt. Les commits de la
  PR sont lus comme objets git, jamais exécutés. CONTRIBUTING décrit le circuit d'une PR de
  fork et la façon de soutenir le projet sans coder ; le README ajoute le badge des étoiles.
- **Pourquoi** : #346, première contribution externe, n'avait reçu ni classe de risque ni
  étiquette du circuit, car le jeton d'un fork est en lecture seule. Fusionnée en squash,
  elle aurait aussi fait passer DCO au rouge sur `main` : le commit aurait eu pour auteur
  l'adresse du compte de la personne qui l'a ouverte, et non celle de son `Signed-off-by`.
  Elle a été reprise telle quelle dans #347 et fusionnée par rebase. L'approbation de la CI
  n'est plus demandée qu'à la première contribution (réglage du dépôt, même jour).
- **Fichiers** : `.github/workflows/contributions.yml`, `.github/workflows/garde-fous.yml`
  (commentaire), `CONTRIBUTING.md`, `README.md`
- **Documentation** : architecture § Intégration continue, sécurité § Contrôles du dépôt
- **Commit** : les PR de fork entrent dans le circuit
