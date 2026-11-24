# 2026-11-24 — F13 phase 4 spécifiée : agent d'inventaire nomade et preuves NIS 2 de la découverte (CFG-58 à CFG-64, NIS-25, ADR-0026)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : spécification du reste de la phase 4 de F13, 8 règles en lot 3. Agent nomade
  (configuration) : jeton d'enrôlement par périmètre (CFG-58), échangé une fois contre une
  identité d'agent propre (CFG-59), rapport HTTPS vers le sas, source « Agent », rapproché
  comme les autres (CFG-60), fréquence de 24 heures réglable par périmètre (CFG-61), suivi,
  agents silencieux ou en retard de version, révocation (CFG-62), l'exécutable lui-même —
  .NET autonome, service sans privilège, HTTPS seulement, aucune donnée d'utilisateur
  (CFG-63) —, distribution avec sommes et attestation de provenance (CFG-64). Conformité
  NIS 2 : état de la découverte joint en preuve figée aux exigences de recensement, de
  cartographie et de maintien en condition (NIS-25). ADR-0026, **proposé**.
- **Correction** : CFG-47 et le lien « Découverte → Conformité NIS 2 » citaient les
  objectifs 4, 5 et 6 du ReCyF ; d'après la structure embarquée (`objectifs.csv`), le 4 porte
  sur les ressources humaines et le 6 sur les accès physiques : les preuves de la découverte
  visent les objectifs 1 et 5 (thématiques « Recensement des SI », « Cartographie des systèmes
  d'information », « Maintien en condition opérationnelle et de sécurité »).
- **Pourquoi** : la phase 4 attendait ses décisions (forme de l'agent, langage, signature,
  enrôlement, fréquence) ; traitement des chantiers du plan d'action demandé par le
  mainteneur le 2026-11-24.
- **Preuve** : codes d'exigences et thématiques des scénarios lus dans `exigences.csv` ;
  `scripts/check-docs.py` sans problème.
- **Fichiers** : —
- **Documentation** : `docs/reference/processus/gestion-de-la-configuration.md`,
  `docs/reference/processus/conformite-nis2.md`, `docs/reference/processus/readme.md`
  (23 🔜 en lot 3), `docs/decisions/adr-0026-agent-d-inventaire-nomade.md` (nouveau),
  `docs/decisions/readme.md`, `docs/plan-action.md`
- **Soldé du plan d'action** : la spécification de F13 phase 4 ; restent la confirmation des
  points de l'ADR-0026, puis l'implémentation.
- **Commit** : F13 phase 4
