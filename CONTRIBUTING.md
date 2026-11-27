# Contribuer à S-Aloha

Merci de votre intérêt. S-Aloha est distribué sous [AGPLv3](LICENSE) : toute
contribution est publiée sous la même licence. Chacun s'engage à respecter le
[code de conduite](CODE_OF_CONDUCT.md).

> **EN:** Contributions are welcome in English or French. Every commit must be signed
> off (`git commit -s`) under the [Developer Certificate of Origin](https://developercertificate.org/).
> Pull requests from forks follow the same checks and are merged by rebase, so your signed
> commits land in `main` as they are. A ⭐ on the repository helps other teams find S-Aloha.

## Par où commencer

- Le tableau [S-Aloha — feuille de route](https://github.com/users/chrraj7/projects/3) montre ce
  qu'il reste à faire, tiré du [plan d'action](docs/plan-action.md).
- Les issues [`good first issue`](https://github.com/chrraj7/S-Aloha/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
  sont rédigées pour un premier passage : contexte, fichiers concernés, critère de fin.
  Les [`help wanted`](https://github.com/chrraj7/S-Aloha/issues?q=is%3Aissue+is%3Aopen+label%3A%22help+wanted%22)
  demandent un peu plus de contexte.
- Indiquez dans l'issue que vous la prenez, pour éviter un travail en double.
- Lancez l'application **sans Active Directory** avec le mode démonstration :

  ```bash
  docker compose -f docker-compose.yml -f docker-compose.demo.yml up -d --build
  ```

  puis connectez-vous sur http://localhost avec `demo.admin` / `Demo-Admin-2026`
  (autres comptes : [README](README.md#essayer-en-5-minutes)).
- Pour savoir **quoi** implémenter : chaque pratique ITIL a sa spécification dans
  [`docs/reference/processus/`](docs/reference/processus/readme.md), dont le tableau
  d'accueil compte les règles 🔜 « à implémenter » par pratique et par lot.
  Chaque règle porte un identifiant (`INC-15`) à citer dans votre pull request et des
  scénarios d'acceptation qui servent de tests ; le
  [mode d'emploi](docs/reference/processus/readme.md#mode-demploi-pour-implémenter-une-règle)
  dit quoi mettre à jour dans la même pull request.
- Pour comprendre le code : [architecture](docs/reference/architecture.md), et pour
  ajouter un écran ou un module : [frontend](docs/reference/frontend.md).
- Issues, pull requests et commits sont acceptés **en français ou en anglais**.
- L'application mobile (iOS, Android, Expo) a son dépôt, [S-Aloha-mobile](https://github.com/chrraj7/S-Aloha-mobile),
  tenu en anglais, avec les mêmes règles : commits signés (DCO), pull request aux contrôles
  verts. Une évolution de l'API dont le mobile a besoin se propose ici.

## Signaler un bug ou proposer une évolution

Les [issues](https://github.com/chrraj7/S-Aloha/issues) sont la source de vérité
du projet ([ADR-0001](docs/decisions/adr-0001-github-issues-source-de-verite.md)).
Utilisez les modèles proposés. Une faille de sécurité ne passe **jamais** par une
issue publique : voir [SECURITY.md](SECURITY.md).

## Proposer une modification

1. Une pull request par sujet, à partir de `main`.
2. Lancez les contrôles avant de pousser :

   ```bash
   TESTCONTAINERS_RYUK_DISABLED=true dotnet test tests/SAloha.Api.Tests   # API, Docker requis
   dotnet test tests/SAloha.Features.Tests                                # conteneur des fonctionnalités optionnelles
   cd frontend && npm test && npm run build                               # interface
   python3 scripts/check-docs.py                                          # documentation
   ```

3. La documentation est mise à jour dans la même pull request que le code
   ([ADR-0002](docs/decisions/adr-0002-documentation-dans-le-depot.md)), en suivant le
   [protocole documentaire](docs/protocole-documentation.md).
4. La CI doit être au vert pour que la pull request soit fusionnée.

Messages de commit à l'indicatif, décrivant le changement (français de préférence,
anglais accepté).

## Après l'ouverture de la pull request

Une pull request ouverte depuis votre fork suit le même circuit que celles du projet :

1. **Première contribution** : GitHub attend que le mainteneur approuve le lancement de
   la CI. Vos pull requests suivantes la lancent sans attendre.
2. Les contrôles tournent à chaque poussée : CI, DCO, Hygiène, Garde-fous, CodeQL. La
   classe de risque est posée d'après les fichiers touchés (`risque-doc`, `risque-code`,
   `risque-sensible`).
3. La pull request est ensuite relue au regard de son issue et de la spécification. Si
   des changements sont demandés, ils arrivent dans une revue ; poussez les corrections
   sur la même branche, et chaque poussée relance la vérification.
4. **Fusion par rebase** : vos commits arrivent dans `main` tels quels, avec votre nom et
   votre signature. Gardez-en peu : un par sujet, idéalement un seul, chacun signé à
   l'adresse de son auteur (voir ci-dessous).

## Certificat d'origine (DCO)

Chaque commit porte la ligne `Signed-off-by: Prénom Nom <adresse>`, ajoutée par
`git commit -s`. Par cette signature, vous certifiez avoir le droit de soumettre votre
contribution sous la licence du projet, selon le
[Developer Certificate of Origin 1.1](https://developercertificate.org/). Vous restez
titulaire de vos droits ; aucune cession n'est demandée
([ADR-0010](docs/decisions/adr-0010-certificat-d-origine-dco.md)).

Oubli sur le dernier commit : `git commit --amend -s --no-edit`, puis
`git push --force-with-lease` sur votre branche.

Le job **DCO** de la CI le vérifie sur chaque pull request : un commit sans
`Signed-off-by` à l'adresse de son auteur bloque la fusion. Pour signer après coup
tous les commits de la branche : `git rebase --signoff main`.

## Preuve et garde-fous

Une pull request qui modifie le code de l'API, du conteneur des fonctionnalités optionnelles
(`features/`) ou de l'interface ajoute ou modifie un test
qui **échoue sans son changement** : le job **Preuve** rejoue ses tests sur le code de
`main` et le vérifie ([`scripts/preuve.py`](scripts/preuve.py)). Le job **Garde-fous**
refuse qu'une PR supprime, réduise ou désactive des tests, ou modifie l'outillage de
vérification (workflows, scripts de contrôle, configuration des tests) sans l'accord du
mainteneur ([`scripts/garde-fous.py`](scripts/garde-fous.py)). Une refactorisation sans
effet observable le dit dans sa description : le mainteneur pose alors
`preuve-non-requise`.

Le job **Hygiène** refuse (contrôle exigé pour fusionner) tout trailer autre que
`Signed-off-by` — ligne de co-auteur, lien de session d'un outil — et les termes réservés
du projet (noms et adresses internes que le dépôt public ne doit pas contenir) :
[`scripts/check-hygiene.py`](scripts/check-hygiene.py).

## Soutenir sans coder

Une ⭐ sur le [dépôt](https://github.com/chrraj7/S-Aloha) aide d'autres équipes à
découvrir S-Aloha : c'est le soutien le plus simple. Décrire votre usage dans une
[discussion](https://github.com/chrraj7/S-Aloha/discussions), signaler un bug ou parler du
projet autour de vous compte aussi. Pour un soutien financier, voir
[Soutenir S-Aloha](docs/soutenir.md).
