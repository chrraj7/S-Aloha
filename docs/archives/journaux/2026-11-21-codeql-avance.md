# 2026-11-21 — CodeQL en configuration avancée, pour analyser aussi les PR de fork

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : l'analyse CodeQL passe de la configuration par défaut de GitHub au workflow
  `codeql.yml`. Il garde les mêmes langages (actions, C#, JavaScript/TypeScript), la même
  suite de requêtes et les mêmes noms de jobs (« Analyze (…) »). Il analyse chaque pull
  request vers `main`, fork compris, chaque poussée de `main` et `main` chaque semaine.
- **Pourquoi** : la configuration par défaut n'a jamais analysé #346, première PR venue
  d'un fork, ni à l'ouverture, ni après l'approbation des workflows, ni après une fermeture
  suivie d'une réouverture. La règle de `main` qui exige un résultat CodeQL la bloquait donc
  sans fin, alors que les onze contrôles exigés étaient verts. Code scanning accepte les
  résultats d'une exécution déclenchée par `pull_request`, fork compris.
- **Mise en place** : la configuration par défaut est désactivée avant la fusion, car elle
  bloque le dépôt des résultats de tout workflow CodeQL ; l'analyse de cette PR est ensuite
  relancée.
- **Fichiers** : `.github/workflows/codeql.yml`
- **Documentation** : architecture § Intégration continue, sécurité § Contrôles du dépôt
- **Commit** : CodeQL en configuration avancée
