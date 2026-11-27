# Constat — état initial au 2026-10-06

<sub>[← Documentation](../readme.md)</sub>

> **Document d'archive, figé.** Relevé de l'état du dépôt au moment du passage à la
> plateforme S-Aloha et de la mise en place de la base documentaire. Il n'est pas mis à
> jour : ce qui reste vrai est replié dans [`reference/`](../reference/), ce qui reste à
> faire dans [`plan-action.md`](../plan-action.md).

## Périmètre

Application de gestion des problèmes selon une ancienne édition d'ITIL (5 commits, du socle initial au design
« fond neutre »), restructurée le 2026-10-06 en plateforme modulaire S-Aloha
([ADR-0003](../decisions/adr-0003-plateforme-modulaire-par-processus-itil.md)) avec un
nouveau langage visuel ([ADR-0005](../decisions/adr-0005-langage-visuel-a-jetons-en-css-natif.md)).

## Hérités de la première version

### Données

- **Pas de migrations EF** : schéma créé par `EnsureCreated()` au démarrage. Toute
  évolution du modèle est ignorée sur une base existante.
- Identités stockées en chaînes AD (`sAMAccountName`, nom d'affichage) sans référentiel :
  un renommage AD n'est pas répercuté.

### Sécurité

- **Secrets par défaut versionnés** : `Jwt:Key`, mot de passe PostgreSQL et mot de passe
  du compte de service LDAP en clair dans `backend/appsettings.json` et
  `docker-compose.yml`.
- **LDAP sans SSL** par défaut (port 389) : mots de passe utilisateurs en clair sur le
  réseau.
- **CORS `AllowAnyOrigin`**.
- **JWT en `sessionStorage`**, exposé à un éventuel XSS ; pas de révocation avant
  expiration (8 h).
- API et **Swagger publiés** directement sur le port 8080, en HTTP.
- Pas de limitation du nombre de tentatives de connexion.

### Qualité

- **Aucun test** automatisé (backend ou frontend), aucune CI.
- Statuts et méthodes stockés en chaînes libres (`"En analyse"`, `"FIVE_WHYS"`), sans
  énumération ni contrainte en base.

### Défauts corrigés pendant la restructuration

Relevés en vérifiant l'interface contre l'API réelle, corrigés le 2026-10-06 :

- **Création de la première analyse d'un problème** : l'analyse était enregistrée mais
  l'API répondait **500** (cycle de sérialisation `Analysis → Problem → Analyses`).
  Corrigé par `ReferenceHandler.IgnoreCycles` dans `backend/Program.cs`.
- **Badges « En analyse » et « Erreur connue » jamais colorés** : la classe générée
  (`Enanalyse`, `Erreurconnue`) ne correspondait pas aux classes CSS (`EnAnalyse`,
  `ErreurConnue`).
- **Reporting** : un libellé long réduisait la barre associée ; barres désormais alignées
  dans une piste commune.

## Documentation

- Trois documents (`README.md`, `docs/rules.md`, `docs/architecture.md`) décrivant encore
  l'arborescence plate `Controllers/`, `Services/`, `pages/` après la restructuration.
- Captures d'écran antérieures au nouveau design.
- Réorganisation par nature de document le 2026-10-06
  ([ADR-0002](../decisions/adr-0002-documentation-dans-le-depot.md)) :
  `rules.md` → `reference/regles-metier.md`, `architecture.md` → `reference/architecture.md`,
  création de `produit`, `base-de-donnees`, `frontend`, `exploitation`, `securite`,
  `glossaire`, des ADR 0001 à 0005 et de `plan-action.md` ; captures régénérées.
