# 2026-11-23 — Réglages du tri appliqués par le conteneur (F14 étape 3d, TRI-13)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** : le conteneur `features` lit les réglages du tri assisté
  (`GET /internal/features/assisted-ticket-triage/settings`, #386) avec les référentiels, une
  fois par minute au plus, avant toute recherche de voisins.
  - **Ce qui s'applique** : le nombre de voisins, les votants minimaux et la part minimale du
    vote (TRI-03), la fenêtre de corrélation, le seuil de panne probable et le seuil de
    similarité (TRI-05 à TRI-07).
  - **Suggestions coupées** : la catégorie, le service, le groupe ou l'impact coupés ne sont
    plus proposés, à la saisie comme en tâche de fond. La panne probable coupée : plus aucun
    groupe n'est signalé, ceux déjà signalés se ferment, et l'impact ne vient plus que de la
    criticité.
  - **Sans réglages** servis (ancienne API, 404) : valeurs par défaut.
- **Pourquoi** : sous-partie 3d de l'étape 3 du chantier F14 (#365), après les réglages côté
  API (#386). La page d'administration suit, avec la mesure (TRI-10).
- **Preuve** : `TriageRulesTests` (un test de plus : catégorie ou impact coupés, part minimale
  de 80 %, cinq voisins, panne probable coupée) et `TicketTriageTests` (un test de plus : l'hôte
  réel contre un port interne factice qui sert des réglages, recherche à cinq voisins,
  catégorie absente, aucun signalement de panne, impact par le CI). Ils ne compilent pas sur
  la base (Preuve, conteneur).
- **Fichiers** : `features/SAloha.Features/TicketTriage/` (`TicketTriage`, `TriageRules`),
  `features/SAloha.Features/Host/ApiClient.cs`, `tests/SAloha.Features.Tests/`
- **Documentation** : tri assisté (TRI-13 en cours), fonctionnalités optionnelles (§ 7)
- **Commit** : #365
