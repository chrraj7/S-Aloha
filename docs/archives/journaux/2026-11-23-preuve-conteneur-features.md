# 2026-11-23 — Le contrôle Preuve couvre le conteneur des fonctionnalités optionnelles

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `scripts/preuve.py` traite un troisième côté, à côté de l'API et de
  l'interface. Le conteneur `features` a son code dans `features/**/*.cs` et ses tests dans
  `tests/SAloha.Features.Tests`. Une PR qui modifie ce code sans test du conteneur est
  refusée ; ses tests du conteneur sont rejoués sur la base, par `dotnet test` de ce projet,
  et doivent y échouer. Un projet de tests absent de la base, quand il arrive avec la PR,
  compte comme une compilation impossible : il prouve.
- **Pourquoi** : le conteneur arrive avec #372 (chantier F14, #364). Sans ce côté, Preuve
  répondait « rien à prouver » à toute PR du conteneur. Demandé par le mainteneur le
  2026-11-23.
- **Preuve** : essais du script sur de vraies paires de commits. #372 (code et tests du
  conteneur) est prouvée, son projet de tests étant absent de `main`. Un commit jetable qui
  modifie le code du conteneur sans test est refusé. Un commit jetable qui ajoute un test
  passant déjà sur la base est refusé (« passent déjà »). #374 (code de l'API, test du
  conteneur sans code du conteneur) : seule l'API est rejouée, et prouvée.
- **Fichiers** : `scripts/preuve.py`
- **Documentation** : CONTRIBUTING (§ Preuve et garde-fous)
- **Commit** : #364
