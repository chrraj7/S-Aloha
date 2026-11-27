# 2026-11-27 — Application mobile référencée ; documentation d'entrée à jour

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : l'application mobile [S-Aloha Mobile](https://github.com/chrraj7/S-Aloha-mobile)
  (iOS et Android, Expo, dépôt à part en anglais) n'était citée nulle part. Elle l'est dans le
  README (section « Application mobile », navigation, feuille de route, « Sous le capot »,
  contribution, documentation, partie anglaise), le produit (§ Accès : navigateur et mobile),
  l'architecture (vue d'ensemble, § 4), la sécurité (jeton dans SecureStore, téléphone perdu,
  HTTPS), l'exploitation (`EXPO_PUBLIC_API_URL`, adresse de l'interface en production), le
  guide de contribution, la page Soutenir (chantier à sponsoriser), l'accueil de la
  documentation et du wiki, et le plan d'action (feuille de route, action en attente).
  Au passage : feuille de route du README et page Soutenir sans la recherche dans les pièces
  jointes (RAG-12, faite le 2026-11-24) ni le durcissement (S1 à S3 faits côté code) ; vue
  d'ensemble de l'architecture alignée sur le compose (ASP.NET Core 10 et non 8, pgvector,
  conteneurs `garage`, `features` et `mcp` avec leurs profils) ; accueil du wiki aligné sur la
  mise en avant des fonctionnalités assistées (plus de 400 règles, et non 250).
- **Pourquoi** : demande du mainteneur, en séance : actualiser la base documentaire après la
  v1.0.0 et lier la version mobile.
- **Preuve** : ce que la documentation dit du mobile est lu dans son dépôt (README,
  `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `src/api.ts`) et dans l'API : `returnToken` existe
  (`Core/Auth/AuthDtos.cs`), le jeton est accepté en `Authorization: Bearer`, le port 8080 de
  l'API est publié par le compose ; compatibilité du mobile avec la v1.0.0 non éprouvée (action
  au plan). Wiki généré à blanc ; `scripts/check-docs.py` sans problème.
- **Fichiers** : `scripts/sync-wiki.py`
- **Documentation** : `README.md`, `CONTRIBUTING.md`, `docs/readme.md`, `docs/soutenir.md`,
  `docs/plan-action.md`, `docs/reference/` (produit, architecture, sécurité, exploitation)
- **Commit** : application mobile référencée
