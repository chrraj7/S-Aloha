# 2026-11-24 — Essai du modèle et remise des traces, côté conteneur (F14 étape 4c-3, OPT-26, OPT-27)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : la part du conteneur `features` dans l'essai et la trace.
  - **Essai** (OPT-26) : `POST /language-model/test` reçoit les réglages enregistrés, même
    désactivés, sans fonctionnalité activée, et passe par le chemin d'une tâche :
    pseudonymisation, appel, schéma. Le modèle doit répondre « pret » et recopier le jeton d'un
    nom d'hôte ; rétabli, ce jeton redonne le nom d'origine, qui n'est jamais parti. La route rend
    la trace de l'appel, que l'API gardera (4c-2).
  - **Trace** (OPT-27) : après chaque appel d'une fonctionnalité, le connecteur remet sa trace à
    l'API (`POST /internal/language-model/calls`, 4c-1), sans l'invite ni la réponse ; une trace
    non remise est journalisée, la tâche continue. L'essai ne remet rien : l'API garde sa trace.
- **Pourquoi** : 4c, l'essai, la trace et la page, dépasse dix fichiers ; il se découpe en
  quatre PR : 4c-1 table des traces (migration), 4c-2 essai et compteurs côté API, 4c-3 ce
  conteneur, 4c-4 la page.
- **Preuve** : 2 tests du conteneur (`OPT_27_…` : trace remise sans l'invite ni la réponse, et
  pas pour l'essai ; `OPT_26_…` : jeton exigé, essai valide sans fuite du nom d'hôte, jeton non
  recopié refusé). Ils échouent sur `main`.
- **Fichiers** : `features/SAloha.Features/LanguageModel/LanguageModelEssay.cs`,
  `LanguageModelClient.cs`, `Host/ApiClient.cs`, `Host/ContainerHost.cs`
- **Documentation** : fonctionnalités optionnelles (OPT-26, OPT-27 en cours ; § 6, § 7),
  architecture
- **Commit** : #366
