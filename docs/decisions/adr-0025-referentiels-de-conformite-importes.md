# ADR-0025 — Référentiels de conformité importés par l'organisation : rien dans le dépôt, colonnes, échelle et niveaux réglés à l'import

<sub>[← Documentation](../readme.md) · [Toutes les décisions](readme.md)</sub>

- **Date** : 2026-11-24
- **Statut** : proposé
- **Décideur** : Leipa

## Contexte

La pratique [Conformité NIS 2](../reference/processus/conformite-nis2.md) évalue un
périmètre sur un seul référentiel, le ReCyF de l'ANSSI, dont la structure est embarquée
dans l'API et le texte importé par l'administrateur
([ADR-0012](adr-0012-referentiel-nis2-structure-versionnee-texte-importe.md)). Les
organisations ont d'autres référentiels à suivre : les mesures d'ISO/IEC 27002, un modèle
de maturité comme CR-CMM, une politique interne, un référentiel sectoriel. Le chantier F12
du [plan d'action](../plan-action.md) prévoit de les évaluer avec le même outillage :
questionnaire, preuves, scores, écarts, actions d'amélioration, comparaison, export.

Trois faits pèsent sur la conception :

- **Les contenus ne sont pas libres.** Les intitulés d'ISO/IEC 27002 sont protégés par le
  droit d'auteur de l'ISO ; CR-CMM est sous licence CC BY-NC 4.0 (usage interne libre,
  œuvre dérivée ou revente soumises à l'accord écrit de ses auteurs, vérifié le
  2026-10-12). Aucun ne peut entrer dans un dépôt sous AGPLv3
  ([ADR-0006](adr-0006-licence-agplv3.md)), pas même leur structure.
- **Les formats diffèrent.** Chaque référentiel arrive en tableur avec ses propres
  colonnes : domaine ou pratique, identifiant, question, niveau, preuve attendue, dans un
  ordre et sous des noms variables.
- **Les échelles et les niveaux diffèrent.** Certains référentiels se notent en
  « implémenté, partiel, non », d'autres sur plus de valeurs ; les modèles de maturité
  rangent leurs points de contrôle par niveau, un niveau n'étant atteint que si tous ses
  points et ceux des niveaux inférieurs sont tenus. Le score à seuils de NIS 2
  ([NIS-12](../reference/processus/conformite-nis2.md#6-délais-calculs-et-alertes)) ne
  rend pas cette règle.

## Décision

1. **Rien du référentiel n'entre dans le dépôt**, ni texte ni structure : l'administrateur
   importe le référentiel entier, sous la responsabilité de l'organisation, et atteste à
   l'import qu'elle peut en faire usage. Le dépôt ne livre que l'outillage.
2. **Le format se règle à l'import, pas dans le code** : un CSV quelconque, dont
   l'administrateur associe les colonnes aux rôles attendus (domaine, identifiant,
   question, et s'il y en a, niveau et preuve attendue), avec un aperçu avant
   l'enregistrement. Un import est **tout ou rien** : une ligne fautive refuse le fichier
   entier et le compte rendu la désigne.
3. **L'échelle de réponse appartient au référentiel** : 2 à 6 valeurs, chacune avec un
   poids de 0 à 1 ; « non applicable », justifié, s'y ajoute toujours. Le score est la
   moyenne des poids, comme celui de NIS 2 est la moyenne des notes.
4. **Le niveau atteint suit la règle cumulative des modèles de maturité** : le plus haut
   niveau dont tous les points applicables, et ceux des niveaux inférieurs, sont notés à
   une valeur de poids 1. Un référentiel sans niveau n'a qu'un score.
5. **Les évaluations partagent le registre et le cycle de vie de NIS 2** (`EVA-AAAA-NNNN`,
   brouillon, en cours, validée, réouverture, preuves, revue périodique, périmètres) : un
   seul registre des évaluations de conformité, le référentiel en plus. Une évaluation
   garde la version importée qu'elle évalue ; une version nouvelle se traite par une
   évaluation nouvelle, comparée à la précédente par les identifiants des points.

La spécification est [Évaluations sur référentiels importés](../reference/processus/evaluations-referentiels-importes.md)
(règles REF).

## Conséquences

- Le dépôt reste entièrement sous AGPLv3 ; une organisation n'y trouve aucun référentiel
  prêt à l'emploi : elle importe le sien, comme le texte du ReCyF.
- L'association des colonnes rend l'import accessible sans retraiter le tableur, au prix
  d'un assistant en trois étapes et d'un compte rendu ligne à ligne.
- Le référentiel NIS 2 garde son fonctionnement (structure embarquée, catégorie d'entité,
  score sur 3, maturité à seuils) : les deux familles cohabitent dans le même registre.
- Les réponses portent le libellé et le poids de la valeur choisie : l'échelle ne change
  plus dès qu'une évaluation vise la version.

## Alternatives envisagées

- **Embarquer des référentiels** comme la structure du ReCyF — écarté : aucun des
  référentiels visés n'est réutilisable sous AGPLv3.
- **Imposer un format CSV** (noms de colonnes fixes) — écarté : chaque organisation
  devrait retraiter son tableur ; l'association des colonnes coûte un écran.
- **Importer aussi les classeurs XLSX** — reporté : un tableur exporte en CSV, et lire
  XLSX ajoute une dépendance ; à revoir si les retours le demandent.
- **Niveau par seuil de score**, comme la maturité de NIS 2 — écarté : un modèle de
  maturité exige toutes les pratiques d'un niveau, une moyenne élevée ne le prouve pas.
- **Un registre séparé** pour ces évaluations — écarté : il dupliquerait cycle de vie,
  preuves, revue périodique, périmètres et console, déjà tenus pour NIS 2.

## Points soumis au mainteneur

Proposés ici, à confirmer avec la spécification : import tout ou rien (point 2) ; règle
stricte des niveaux, poids 1 exigé (point 4) ; registre commun avec NIS 2 et version figée à
la réouverture, contrairement à NIS-18 (point 5) ; CSV seul ; plafonds de la spécification
(5 Mio, 2 000 points, 6 valeurs d'échelle, niveaux 1 à 9).
