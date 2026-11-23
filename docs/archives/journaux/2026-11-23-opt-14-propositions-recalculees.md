# 2026-11-23 — Propositions recalculées et valeurs écartées (OPT-14, OPT-15)

<sub>[← Archives](../readme.md)</sub>

- **Quoi** :
  - Une proposition ouverte devenue périmée, parce que sa cible a été modifiée, et que le
    conteneur redépose à l'identique est désormais **rafraîchie** : valeur avant, motifs,
    sources, confiance et date. Elle redevient acceptable.
  - Une valeur que l'agent a **écartée**, en la rejetant ou en retenant une autre valeur
    (proposition corrigée), n'est plus reproposée sur la même cible : l'API répond 200 avec
    l'identifiant de la proposition écartée, sans rien créer. Une autre valeur du même champ
    reste proposable.
- **Pourquoi** : défaut de la partie 2b (#371). Toute modification d'un incident rend ses
  propositions ouvertes périmées, même l'acceptation d'une autre proposition. Or le
  dédoublonnage renvoyait l'ancienne proposition, toujours périmée : elle ne pouvait plus
  jamais s'accepter (409), alors qu'OPT-15 prévoit son recalcul. Le non-retour d'une valeur
  rejetée a été décidé par le mainteneur le 2026-11-23, avant le vote du tri assisté (#365) ;
  la valeur corrigée suit la même logique, elle aussi écartée par l'agent.
- **Preuve** : `ProposalsTests` (2 tests de plus : proposition périmée recalculée à l'identique
  puis acceptée ; valeur rejetée puis corrigée non reproposée, autre valeur proposable), qui
  échouent sur `main`.
- **Fichiers** : `backend/Core/Features/InternalProposalsController.cs`
- **Documentation** : fonctionnalités optionnelles (OPT-14, OPT-15)
- **Commit** : #365
