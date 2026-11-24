# 2026-11-24 — CMDB assistée dans le conteneur : Renommer, Normaliser, Reclasser (F14 étape 5a-3, CMA-02, CMA-07, CMA-09)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : l'exécution d'une analyse dans le conteneur `features`.
  - **Analyse** (CMA-01) : annoncée par le fil (`analysis.requested`), traitée seulement en file ;
    En cours, puis un compte rendu par lot, puis Terminée, ou En échec avec le motif quand le
    modèle est en panne ou trop lent (OPT-28). Rejouée après un redémarrage, une analyse finie
    ne se refait pas.
  - **Lots** : les CI de la sélection, ou du périmètre page après page, par 20 ; une requête au
    modèle par tâche et par lot, consignes versionnées (`1`) et schéma de la tâche (OPT-25).
  - **Tâches** (CMA-02) : Renommer, selon la convention du type (CMA-04), donnée au modèle avec
    les seuls CI de ce type ; Normaliser le système, le constructeur et le modèle ; Reclasser dans
    un type de la liste.
  - **Données envoyées** (CMA-07) : nom, type, environnement, système, constructeur, modèle,
    description bornée à 1 000 caractères, relations existantes. Ni adresse, ni numéro de
    série, ni compte : non envoyés, et masqués s'ils paraissent dans une description. Aucun CI
    hors du périmètre de l'analyse.
  - **Sortie validée** (CMA-09) : une proposition d'un CI hors du lot, vide, ou un renommage d'un
    type sans convention, est écartée et comptée ; une valeur inchangée ou un second avis sur le
    même champ est ignoré. Les retenues sont déposées (OPT-14), un gestionnaire les décide.
  - **Contrôle de santé** : le conteneur déclare désormais la CMDB assistée (OPT-01).
- **Pourquoi** : 5a-3 de l'étape 5 du chantier F14 (#367), après le lancement (5a-1, #399) et la
  remise au conteneur (5a-2, #400). La convention de nommage côté API (5a-4) et la page (5a-5)
  suivent.
- **Preuve** : `CmdbTasksTests` (2 tests : champs envoyés et valeurs masquées ; propositions
  écartées, ignorées, retenues) et `AssistedCmdbTests` (2 tests de bout en bout : analyse des
  trois tâches, propositions et comptes rendus, rien de sensible ni d'un autre périmètre envoyé ;
  modèle en panne, analyse finie non refaite). Ils ne compilent pas sur `main`. Le test d'OPT-01
  attend désormais les deux fonctionnalités déclarées.
- **Fichiers** : `features/SAloha.Features/AssistedCmdb/AssistedCmdb.cs`, `CmdbTasks.cs`,
  `Host/ApiClient.cs`, `Host/ContainerHost.cs`
- **Documentation** : CMDB assistée (CMA-01, CMA-02, CMA-07, CMA-09 en cours ; § 2), architecture
- **Commit** : #367
