# 2026-11-16 — Tests : le simulacre d'embeddings ne dépend plus du processus (#311)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : `FakeEmbeddingClient` hache les mots par FNV-1a au lieu de `string.GetHashCode()`.
  Un mot occupe la même dimension d'une exécution des tests à l'autre.
- **Pourquoi** : la CI de `main` a échoué le 2026-11-14 sur
  `PerimeterViewsTests.RAG_13_le_filtre_des_perimetres_precede_le_classement` (6 résultats au
  lieu de 5), puis a réussi à la relance, sur le même commit. `string.GetHashCode()` est tiré au
  hasard à chaque processus : à certaines exécutions, un mot d'un enregistrement créé par un
  autre test tombait sur la dimension du mot cherché, et cet enregistrement, voisin au sens
  du vectoriel (similarité au-dessus de 0,47 pour un passage de quatre mots au plus), s'ajoutait
  aux résultats.
- **Vérifié** : `SearchTests.Le_simulacre_d_embeddings_est_le_meme_d_une_execution_a_l_autre`,
  rouge sans la correction (dimension 609 au lieu de 447 à l'essai). Le mécanisme a été rejoué
  par un test jetable : un incident résolu dont le titre est un mot choisi pour tomber sur la
  dimension du mot cherché, sans aucun mot commun, sort dans `/api/search`. Suite d'API
  complète deux fois de suite : 381 verts.
- **Reste** : une collision entre deux mots de tests différents reste possible, mais elle est
  désormais la même à chaque exécution : elle se voit dans la PR qui l'introduit, plus au
  hasard sur `main`.
- **Fichiers** : `tests/SAloha.Api.Tests/FakeEmbeddingClient.cs`,
  `tests/SAloha.Api.Tests/SearchTests.cs`
- **Documentation** : aucune règle ne change ; le code de l'API n'est pas touché
- **Commit** : correction de #311
